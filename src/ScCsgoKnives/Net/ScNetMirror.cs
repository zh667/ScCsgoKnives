using Engine;
using GameEntitySystem;
namespace Game;

/// <summary>Server-owned world state that remote clients only display: gun records (rounds, wear, silencer, charge, finish,
/// counter, level — everything the ammo HUD, skins and StatTrak read) and CS protection values. A client joins with the
/// server's saved subsystems; afterwards the server sends every change, in the save format, every 0.1 s, and a full copy
/// to each client whose handshake was just accepted. Nothing here runs outside a multiplayer host or client.</summary>
public static class ScNetMirror {
    public const ushort OpRecords = 32, OpArmor = 33, OpRegistry = 34, OpWant = 35;
    const int RowsPerMessage = 256;
    const double Interval = .1;
    /// <summary>Client: a gun in hand whose slot names a record the mirror does not hold is first waited for (the row is
    /// normally on its way), then asked for: after <see cref="WantFirst"/> s, and again after twice the time each, at most
    /// <see cref="WantAttempts"/> times.</summary>
    public const double WantFirst = .5;
    public const int WantAttempts = 5, WantIdsPerMessage = 16;

    static readonly Dictionary<int, int> s_sentRevisions = [];
    static readonly Dictionary<string, string> s_sentArmor = new(StringComparer.Ordinal);
    static string s_sentGrowthMode;
    static double s_recordsAt, s_armorAt;
    static object s_registryOwner, s_armorOwner;
    /// <summary>Counters read by the offline checks: row messages that could not be queued for every client (the rows stay
    /// due), record requests a client sent and the server answered.</summary>
    public static int RowSendFailures, WantsSent, WantsAnswered;

    /// <summary>Release only the matching world-owned objects. Disposing an older project cannot erase a new host's cache.</summary>
    public static void ReleaseRegistry(ScGunRegistry registry) {
        if (ReferenceEquals(s_registryOwner, registry)) { s_registryOwner = null; s_sentRevisions.Clear(); s_sentGrowthMode = null; s_recordsAt = 0; }
        if (ReferenceEquals(s_wantsOwner, registry)) { s_wantsOwner = null; s_wants.Clear(); }
    }
    public static void ReleaseArmor(SubsystemScArmor armor) {
        if (!ReferenceEquals(s_armorOwner, armor)) return;
        s_armorOwner = null; s_sentArmor.Clear(); s_armorAt = 0;
    }
    public static void ClearOrphaned() {
        if (GameManager.Project is null || !ReferenceEquals(s_registryOwner, ScGunRegistry.Current)) ReleaseRegistry(s_registryOwner as ScGunRegistry);
        if (GameManager.Project is null || !ReferenceEquals(s_wantsOwner, ScGunRegistry.Current)) ReleaseRegistry(s_wantsOwner as ScGunRegistry);
        if (s_armorOwner is SubsystemScArmor armor && (GameManager.Project is null || !ReferenceEquals(armor.Project, GameManager.Project))) ReleaseArmor(armor);
    }

    public static void Register() {
        ScNet.OnClient(OpRecords, ApplyRecords);
        ScNet.OnClient(OpArmor, ApplyArmor);
        ScNet.OnClient(OpRegistry, ApplyRegistry);
        ScNet.OnServer(OpWant, AnswerWant);
        ScNet.PeerAccepted += SendAll;
        ScNet.ClientAccepted += s_wants.Clear;
    }

    // ---------------------------------------------------------------- server
    /// <summary>Server: sends what changed right now (before an answer that the client will read its mirror for).</summary>
    public static void Flush() {
        s_armorAt = 0;
        FlushRecords();
        ArmorTick(GameManager.Project?.FindSubsystem<SubsystemScArmor>(false));
    }
    /// <summary>Server: sends the gun records that changed, and the shot confirmations, right now.</summary>
    public static void FlushRecords() {
        if (!ScNet.IsHost) return;
        s_recordsAt = 0;
        RecordsTick(ScGunRegistry.Current, GameManager.Project?.FindSubsystem<SubsystemTime>(false)?.GameTime ?? 0);
    }
    /// <summary>Server: sends only the gun records that changed, now (ScNetSlots: before the slots that name them).</summary>
    internal static void FlushRows() {
        if (ScNet.IsHost && ScGunRegistry.Current is { } registry) SendChangedRows(registry, GameManager.Project?.FindSubsystem<SubsystemTime>(false)?.GameTime ?? 0);
    }
    /// <summary>Called every frame by the gun subsystem; a no-op outside a multiplayer host.</summary>
    public static void RecordsTick(ScGunRegistry registry, double now) {
        if (!ScNet.IsHost || registry is null) return;
        if (ScNet.Now < s_recordsAt && ReferenceEquals(s_registryOwner, registry)) return;
        s_recordsAt = ScNet.Now + Interval;
        SendChangedRows(registry, now);
        string mode = registry.GrowthMode.ToString();
        if (mode != s_sentGrowthMode) {
            s_sentGrowthMode = mode;
            ScNet.Broadcast(OpRegistry, w => w.String(mode));
        }
    }
    static void SendChangedRows(ScGunRegistry registry, double now) {
        if (!ReferenceEquals(s_registryOwner, registry)) { s_registryOwner = registry; s_sentRevisions.Clear(); s_sentGrowthMode = null; }
        var changed = new List<(int Id, int Revision)>();
        var present = new HashSet<int>();
        foreach (var (id, revision) in registry.NetworkRevisions) {
            present.Add(id);
            if (!s_sentRevisions.TryGetValue(id, out int sent) || sent != revision) changed.Add((id, revision));
        }
        var removed = s_sentRevisions.Keys.Where(id => !present.Contains(id)).ToList();
        // A row counts as sent only once it was queued for every accepted client; otherwise it stays due and goes out
        // with the next tick (the game's own state is never undone for it, and nothing is evaluated again). With no row
        // to send, a client whose shot count changed still gets its confirmation (a message with no rows).
        if (SendRows(null, registry, changed.Select(c => c.Id).ToList(), removed, now)) {
            foreach (var (id, revision) in changed) s_sentRevisions[id] = revision;
            foreach (int id in removed) s_sentRevisions.Remove(id);
        }
        else if (changed.Count > 0 || removed.Count > 0) RowSendFailures++;
    }

    public static void ArmorTick(SubsystemScArmor store) {
        if (!ScNet.IsHost || store is null) return;
        if (!ReferenceEquals(s_armorOwner, store)) { s_armorOwner = store; s_sentArmor.Clear(); }
        if (ScNet.Now < s_armorAt) return;
        s_armorAt = ScNet.Now + Interval;
        var changes = new List<KeyValuePair<string, string>>();
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, encoded) in store.NetworkEntries()) {
            present.Add(key);
            if (!s_sentArmor.TryGetValue(key, out var sent) || sent != encoded) { changes.Add(new(key, encoded)); s_sentArmor[key] = encoded; }
        }
        foreach (string key in s_sentArmor.Keys.Where(k => !present.Contains(k)).ToList()) { s_sentArmor.Remove(key); changes.Add(new(key, "")); }
        if (changes.Count > 0) ScNet.Broadcast(OpArmor, w => { w.Int(changes.Count); foreach (var (k, v) in changes) w.String(k).String(v); });
    }

    /// <summary>Rows (and "gone" for removed records) to one client or to every accepted one. Each client's message ends
    /// with that client's own shot confirmation when one is due (ScNetGuns.Ack): the rounds a shot took and the word that
    /// it was this client's shot are one message, read in one go.</summary>
    static bool SendRows(ScNetPeer to, ScGunRegistry registry, List<int> ids, List<int> removed, double now) {
        var rows = ids.Select(id => (id, registry.NetworkRow(id, now))).Concat(removed.Select(id => (id, (string)null))).ToList();
        int batches = Math.Max(1, (rows.Count + RowsPerMessage - 1) / RowsPerMessage);
        bool all = true;
        foreach (var peer in to is null ? ScNet.Peers.ToArray() : [to]) {
            bool confirm = ScNetGuns.TryAck(peer, out var ack);
            if (rows.Count == 0 && !confirm) continue;
            bool sent = true;
            for (int b = 0; b < batches; b++) {
                var batch = rows.Skip(b * RowsPerMessage).Take(RowsPerMessage).ToList();
                bool withAck = confirm && b == batches - 1;
                sent &= ScNet.TrySendTo(peer, OpRecords, w => {
                    w.Int(batch.Count);
                    foreach (var (id, row) in batch) { w.Int(id).Bool(row is not null); if (row is not null) w.String(row); }
                    w.Bool(withAck);
                    if (withAck) w.Int(ack.Selection).Int(ack.Value).Int(ack.Fired).Int(ack.Skipped);
                });
            }
            if (sent && confirm) ScNetGuns.AckSent(peer);
            all &= sent;
        }
        return all;
    }

    /// <summary>Server: a client asks for records its slots name and its mirror lacks. Answered with the present rows (or
    /// "gone" for a record that does not exist); nothing is evaluated or changed.</summary>
    static void AnswerWant(ScNetPeer from, ComponentPlayer player, ScNetReader r) {
        int n = r.Count(WantIdsPerMessage);
        var ids = new List<int>(n);
        for (int i = 0; i < n; i++) { int id = r.Int(); if (ScGunEncoding.IsRecordId(id) && !ids.Contains(id)) ids.Add(id); }
        if (ScGunRegistry.Current is not { } registry || ids.Count == 0) return;
        double now = GameManager.Project?.FindSubsystem<SubsystemTime>(false)?.GameTime ?? 0;
        var have = ids.Where(id => registry.NetworkRow(id, now) is not null).ToList();
        WantsAnswered++;
        SendRows(from, registry, have, ids.Except(have).ToList(), now);
    }

    /// <summary>A client whose handshake was just accepted gets everything, whatever it joined with.</summary>
    static void SendAll(ScNetPeer peer) {
        var project = GameManager.Project;
        double now = project?.FindSubsystem<SubsystemTime>(false)?.GameTime ?? 0;
        if (ScGunRegistry.Current is { } registry) {
            SendRows(peer, registry, registry.NetworkRevisions.Select(p => p.Key).ToList(), [], now);
            ScNet.SendTo(peer, OpRegistry, w => w.String(registry.GrowthMode.ToString()));
        }
        if (project?.FindSubsystem<SubsystemScArmor>(false) is { } store) {
            var all = store.NetworkEntries().ToList();
            ScNet.SendTo(peer, OpArmor, w => { w.Int(all.Count); foreach (var (k, v) in all) w.String(k).String(v); });
        }
        Log.Information($"[ScCsgoNet] server: full state sent to {peer} ({ScGunRegistry.Current?.Count ?? 0} gun records)");
    }

    // ---------------------------------------------------------------- client
    sealed class Want { public double NextAt; public int Asked; }
    static readonly Dictionary<int, Want> s_wants = [];
    static object s_wantsOwner;
    /// <summary>Client, each frame a gun in hand waits for its record (ScGunBlock.AwaitsRecord): asks the server for it,
    /// bounded (see <see cref="WantFirst"/>). The gun stays unusable until the row is here; nothing is guessed meanwhile.</summary>
    public static void ClientWants(int id) {
        if (!ScNet.IsRemoteClient || ScNet.ClientBlocked || !ScGunEncoding.IsRecordId(id) || ScGunRegistry.Current is not { } registry) return;
        if (!ReferenceEquals(s_wantsOwner, registry)) { s_wantsOwner = registry; s_wants.Clear(); }
        double now = ScNet.Now;
        if (!s_wants.TryGetValue(id, out var want)) { s_wants[id] = new Want { NextAt = now + WantFirst }; return; }
        if (want.Asked >= WantAttempts || now < want.NextAt) return;
        if (!ScNet.Send(OpWant, w => w.Int(1).Int(id))) return;
        WantsSent++; want.Asked++; want.NextAt = now + WantFirst * (1 << want.Asked);
        if (want.Asked == WantAttempts) KnifeLog.Warning($"[ScCsgoNet] client: gun record {id} asked for {WantAttempts} times without an answer; the gun stays waiting for the server");
    }

    static void ApplyRecords(ScNetReader r) {
        var registry = ScGunRegistry.Current;
        double now = GameManager.Project?.FindSubsystem<SubsystemTime>(false)?.GameTime ?? 0;
        // The whole message is read before anything of it is applied.
        int n = r.Count(RowsPerMessage), refused = 0;
        var rows = new List<(int Id, string Row)>(n);
        for (int i = 0; i < n; i++) { int id = r.Int(); rows.Add((id, r.Bool() ? r.String(512) : null)); }
        bool confirmed = r.Bool();
        int selection = 0, value = 0, fired = 0, skipped = 0;
        if (confirmed) { selection = r.Int(); value = r.Int(); fired = r.Int(); skipped = r.Int(); }
        r.Finish();
        foreach (var (id, row) in rows) {
            if (registry is null) continue;
            if (!registry.ApplyNetworkRow(id, row, now)) { if (row is not null) refused++; continue; }
            if (row is not null) s_wants.Remove(id);
        }
        // This client's own shots among those rounds: applied with the rows, never apart from them.
        if (confirmed) ScNetGuns.ApplyAck(selection, value, fired, skipped);
        if (refused > 0) KnifeDiagnostics.WarnOnce("scnet-row-refused", $"[ScCsgoNet] client: {refused} gun record row(s) from the server did not parse and were ignored");
    }

    static void ApplyRegistry(ScNetReader r) {
        string mode = r.String(64);
        if (ScGunRegistry.Current is { } registry && Enum.TryParse(mode, out ScGunGrowthMode parsed) && Enum.IsDefined(parsed)) registry.GrowthMode = parsed;
    }

    static void ApplyArmor(ScNetReader r) {
        var store = GameManager.Project?.FindSubsystem<SubsystemScArmor>(false);
        int n = r.Count(100000);
        for (int i = 0; i < n; i++) {
            string key = r.String(128), encoded = r.String(128);
            store?.ApplyNetworkEntry(key, encoded);
        }
    }
}
