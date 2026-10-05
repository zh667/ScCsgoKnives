using System.Text.Json;
using Engine;
using GameEntitySystem;
namespace Game;

/// <summary>What one process shows of the deathmatch (DM-07, DM-09, DM-10): the host's own copy is written by the
/// authority directly, a remote client's by the messages below. Display and prediction only: nothing here decides a
/// life, a score or a loadout.</summary>
public sealed class DmView {
    public sealed record Row(int PlayerIndex, string Key, string Name, DmPlayerPhase Phase, int Kills, int Deaths, int Assists, bool Connected, bool Playing);
    public bool Enabled;
    public DmPhase Phase = DmPhase.Editing;
    public int MatchId;
    /// <summary>When the present phase ends on this process's clock (the match, the countdown, the results).</summary>
    public double PhaseEndsAt;
    public DmRules Rules = new();
    public DmArenaDefinition Arena = new();
    public string Reason = "";
    public List<Row> Rows = [];
    /// <summary>What one local player is shown about itself (a split screen has several; a remote client one).</summary>
    public sealed class Self {
        public DmPlayerPhase Phase = DmPlayerPhase.Preparing;
        public int LifeId, Health, Armour; public bool Helmet, Entered;
        public double ProtectedUntil;
        public DmLoadout Desired = DmLoadout.Empty, Active = DmLoadout.Empty;
        public int DesiredRevision, ActiveRevision;
        public int[] GrenadesLeft = new int[DmCatalogue.GrenadeKinds];
        /// <summary>Gun record → whether its counter shows and the number on it (this player's own guns).</summary>
        public Dictionary<int, (bool Show, long Kills)> Counters = [];
        public DmKill OwnDeath; public double OwnDeathAt; public Vector3 DeathEye; public Vector3? KillerEye;
        public (DmLoadoutOutcome Outcome, DmLoadoutError Error, double At)? Answer;
        public (string Text, double At)? Notice;
        public bool Fighting => Phase is DmPlayerPhase.Alive or DmPlayerPhase.SpawnProtected;
    }
    readonly Dictionary<int, Self> m_selves = [];
    public Self Of(int playerIndex) { if (!m_selves.TryGetValue(playerIndex, out var self)) m_selves[playerIndex] = self = new Self(); return self; }
    public Self Of(ComponentPlayer player) => Of(player?.PlayerData?.PlayerIndex ?? 0);
    public IEnumerable<Self> Selves => m_selves.Values;
    /// <summary>The newest kills, oldest first, each with the local time it arrived.</summary>
    public readonly List<(DmKill Kill, double At)> Feed = [];
    public int LastSequence, FeedMatch;
    public DmResult Result; public double ResultAt;
    public const int FeedLines = 6; public const double FeedSeconds = 6;
    public bool Governing => Enabled && Phase != DmPhase.Editing;
    public Row RowOf(int playerIndex) => Rows.FirstOrDefault(r => r.PlayerIndex == playerIndex);
    /// <summary>A kill event, once: a number already seen (a resend, a late joiner's snapshot) changes nothing.</summary>
    public bool AddKill(DmKill kill, double now) {
        if (kill is null) return false;
        if (kill.MatchId != FeedMatch) { FeedMatch = kill.MatchId; LastSequence = 0; Feed.Clear(); }
        if (kill.Sequence <= LastSequence) return false;
        LastSequence = kill.Sequence; Feed.Add((kill, now));
        if (Feed.Count > FeedLines) Feed.RemoveAt(0);
        return true;
    }
}

/// <summary>The deathmatch package's messages on the CS adapter's packet (design §13). Numbers 100-113: above every
/// number the core and the other packages use (adapter 1-6, core 32-72, agents 80-85, appearance 90-91), and claimed
/// through ScNet, which refuses a number that already has another handler and lists the refusal. Server → client carries
/// state the server decided; client → server carries a wish the server validates against the sender's own connection
/// (the player a message is about is the connection's player, never a key from the payload).</summary>
public static class DmNet {
    public const ushort OpState = 100, OpPlayers = 101, OpSelf = 102, OpPrepare = 103, OpCommit = 104, OpDeath = 105, OpPlace = 106, OpNotice = 107, OpResult = 108, OpAnswer = 109;
    public const ushort OpLoadout = 110, OpEnter = 111, OpSpectate = 112, OpReady = 113;
    /// <summary>Longest text a client may send (a loadout is a few hundred bytes).</summary>
    public const int MaxRequest = 2048;
    static readonly JsonSerializerOptions s_json = new() { WriteIndented = false };
    static bool s_registered;
    static SubsystemScDeathmatch Subsystem => GameManager.Project?.FindSubsystem<SubsystemScDeathmatch>(false);

    public static void Register() {
        if (s_registered) return;
        s_registered = true;
        ScNet.OnClient(OpState, ReceiveState);
        ScNet.OnClient(OpPlayers, ReceivePlayers);
        ScNet.OnClient(OpSelf, ReceiveSelf);
        ScNet.OnClient(OpPrepare, ReceivePrepare);
        ScNet.OnClient(OpCommit, ReceiveCommit);
        ScNet.OnClient(OpDeath, ReceiveDeath);
        ScNet.OnClient(OpPlace, ReceivePlace);
        ScNet.OnClient(OpNotice, ReceiveNotice);
        ScNet.OnClient(OpResult, ReceiveResult);
        ScNet.OnClient(OpAnswer, ReceiveAnswer);
        ScNet.OnServer(OpLoadout, ReceiveLoadout);
        ScNet.OnServer(OpEnter, ReceiveEnter);
        ScNet.OnServer(OpSpectate, ReceiveSpectate);
        ScNet.OnServer(OpReady, ReceiveReady);
        ScNet.PeerAccepted += peer => Subsystem?.PeerAccepted(peer);
        ScNet.PeerLeft += peer => Subsystem?.PeerLeft(peer);
    }

    // ---------------------------------------------------------------- server → client: writers
    public const string ArenaNetworkProblem = "地图描述过长，暂不能联机比赛；原地图已保留，房主可删除过长标签的复活点后重新设置";
    public static int ArenaBytes(DmArenaDefinition arena) => System.Text.Encoding.UTF8.GetByteCount(arena.Encode());
    public static bool ArenaFitsNetwork(DmArenaDefinition arena) => arena is not null && arena.Spawns.Count <= DmArenaRules.MaxSpawns
        && ArenaBytes(arena) <= DmArenaRules.MaxNetworkBytes;
    public static void WriteState(ScNetWriter w, bool enabled, DmPhase phase, int matchId, double remaining, DmRules rules, DmArenaDefinition arena, string reason) {
        string encoded = arena.Encode();
        if (arena.Spawns.Count > DmArenaRules.MaxSpawns || System.Text.Encoding.UTF8.GetByteCount(encoded) > DmArenaRules.MaxNetworkBytes)
            throw new InvalidDataException("arena too long for network state");
        w.Bool(enabled).Byte((byte)phase).Int(matchId).Double(remaining).String(rules.Encode()).String(encoded).String(reason ?? "").String(DmWeapons.Fingerprint);
        if (w.Length > 64 * 1024) throw new InvalidDataException("state exceeds packet budget");
    }
    public static void WritePlayers(ScNetWriter w, IReadOnlyList<DmView.Row> rows) {
        w.Int(rows.Count);
        foreach (var r in rows) w.Int(r.PlayerIndex).String(r.Key).String(r.Name).Byte((byte)r.Phase).Int(r.Kills).Int(r.Deaths).Int(r.Assists).Byte((byte)((r.Connected ? 1 : 0) | (r.Playing ? 2 : 0)));
    }
    public static void WriteSelf(ScNetWriter w, DmPlayer p, double now, IReadOnlyDictionary<int, (bool Show, long Kills)> counters) {
        w.Byte((byte)p.Phase).Int(p.LifeId).Int(p.Health).Int(p.Armour).Bool(p.Helmet).Bool(p.Entered).Double(p.Phase == DmPlayerPhase.SpawnProtected ? Math.Max(0, p.ProtectedUntil - now) : 0)
            .String(p.Desired.Encode()).Int(p.DesiredRevision).String(p.Active.Encode()).Int(p.ActiveRevision);
        for (int kind = 0; kind < DmCatalogue.GrenadeKinds; kind++) w.Byte((byte)Math.Min(255, p.GrenadesLeft(kind)));
        w.Int(counters.Count);
        foreach (var c in counters) w.Int(c.Key).Bool(c.Value.Show).Long(c.Value.Kills);
    }
    public static void WriteDeath(ScNetWriter w, DmKill kill, int victimIndex, Vector3 victimEye, Vector3? killerEye) =>
        w.String(JsonSerializer.Serialize(kill, s_json)).Int(victimIndex).Vector3(victimEye).Bool(killerEye.HasValue).Vector3(killerEye ?? Vector3.Zero);

    // ---------------------------------------------------------------- server → client: readers
    static void ReceiveState(ScNetReader r) {
        bool enabled = r.Bool(); var phase = (DmPhase)r.Byte(); int matchId = r.Int(); double remaining = r.Double();
        string rulesText = r.String(), arenaText = r.String(DmArenaRules.MaxNetworkBytes), reason = r.String(), fingerprint = r.String();
        if (System.Text.Encoding.UTF8.GetByteCount(arenaText) > DmArenaRules.MaxNetworkBytes) throw new InvalidDataException("arena too long for network state");
        if (!Enum.IsDefined(phase) || !DmRules.TryDecode(rulesText, out var rules) || !DmArenaDefinition.TryDecode(arenaText, out var arena)) throw new InvalidDataException("state");
        Subsystem?.ApplyState(enabled, phase, matchId, remaining, rules, arena, reason, fingerprint);
    }
    static void ReceivePlayers(ScNetReader r) {
        int count = r.Count(64); var rows = new List<DmView.Row>(count);
        for (int i = 0; i < count; i++) {
            int index = r.Int(); string key = r.String(64), name = r.String(64); var phase = (DmPlayerPhase)r.Byte(); int kills = r.Int(), deaths = r.Int(), assists = r.Int(); byte flags = r.Byte();
            if (!Enum.IsDefined(phase)) throw new InvalidDataException("phase");
            rows.Add(new(index, key, name, phase, kills, deaths, assists, (flags & 1) != 0, (flags & 2) != 0));
        }
        if (Subsystem is { } s) s.View.Rows = rows;
    }
    static void ReceiveSelf(ScNetReader r) {
        var phase = (DmPlayerPhase)r.Byte(); int life = r.Int(), health = r.Int(), armour = r.Int(); bool helmet = r.Bool(), entered = r.Bool(); double protection = r.Double();
        string desiredText = r.String(), activeText; int desiredRevision = r.Int(); activeText = r.String(); int activeRevision = r.Int();
        var grenades = new int[DmCatalogue.GrenadeKinds];
        for (int kind = 0; kind < grenades.Length; kind++) grenades[kind] = r.Byte();
        int n = r.Count(64); var counters = new Dictionary<int, (bool, long)>(n);
        for (int i = 0; i < n; i++) { int id = r.Int(); bool show = r.Bool(); long kills = r.Long(); counters[id] = (show, kills); }
        if (!Enum.IsDefined(phase) || !DmLoadout.TryDecode(desiredText, out var desired) || !DmLoadout.TryDecode(activeText, out var active)) throw new InvalidDataException("self");
        if (Subsystem is not { } s || s.LocalSelf is not { } v) return;
        v.Phase = phase; v.LifeId = life; v.Health = health; v.Armour = armour; v.Helmet = helmet; v.Entered = entered; v.ProtectedUntil = s.Now + protection;
        v.Desired = desired; v.DesiredRevision = desiredRevision; v.Active = active; v.ActiveRevision = activeRevision; v.GrenadesLeft = grenades; v.Counters = counters;
    }
    static void ReceivePrepare(ScNetReader r) { int life = r.Int(); Vector3 position = r.Vector3(); float yaw = r.Float(); Subsystem?.ClientPrepare(life, position, yaw); }
    static void ReceiveCommit(ScNetReader r) { int life = r.Int(); Vector3 position = r.Vector3(); float yaw = r.Float(); Subsystem?.ClientCommit(life, position, yaw); }
    static void ReceivePlace(ScNetReader r) { Vector3 position = r.Vector3(); float yaw = r.Float(); Subsystem?.ClientPlace(position, yaw); }
    static void ReceiveDeath(ScNetReader r) {
        var kill = JsonSerializer.Deserialize<DmKill>(r.String(), s_json); int victim = r.Int(); Vector3 eye = r.Vector3(); bool has = r.Bool(); Vector3 killer = r.Vector3();
        Subsystem?.ShowDeath(kill, victim, eye, has ? killer : null);
    }
    static void ReceiveNotice(ScNetReader r) { string text = r.String(512); Subsystem?.ShowNotice(text); }
    static void ReceiveResult(ScNetReader r) { var result = JsonSerializer.Deserialize<DmResult>(r.String(16384), s_json); Subsystem?.ShowResult(result); }
    static void ReceiveAnswer(ScNetReader r) {
        var outcome = (DmLoadoutOutcome)r.Byte(); var error = (DmLoadoutError)r.Byte();
        if (Subsystem is { LocalSelf: { } self } s) self.Answer = (outcome, error, s.Now);
    }
    public static string Encode(DmResult result) => JsonSerializer.Serialize(result, s_json);

    // ---------------------------------------------------------------- client → server
    public static bool SendLoadout(DmLoadout loadout) => ScNet.Send(OpLoadout, w => w.String(loadout.Encode()));
    public static bool SendEnter(bool confirmEmpty) => ScNet.Send(OpEnter, w => w.Bool(confirmEmpty));
    public static bool SendSpectate() => ScNet.Send(OpSpectate, null);
    public static bool SendReady(int lifeId) => ScNet.Send(OpReady, w => w.Int(lifeId));
    static void ReceiveLoadout(ScNetPeer from, ComponentPlayer player, ScNetReader r) {
        string text = r.String(MaxRequest);
        if (!DmLoadout.TryDecode(text, out var loadout)) throw new InvalidDataException("loadout");
        Subsystem?.RequestLoadout(player, loadout);
    }
    static void ReceiveEnter(ScNetPeer from, ComponentPlayer player, ScNetReader r) { bool confirm = r.Bool(); Subsystem?.RequestEnter(player, confirm); }
    static void ReceiveSpectate(ScNetPeer from, ComponentPlayer player, ScNetReader r) => Subsystem?.RequestSpectate(player);
    static void ReceiveReady(ScNetPeer from, ComponentPlayer player, ScNetReader r) { int life = r.Int(); Subsystem?.RemoteReady(player, life); }
}
