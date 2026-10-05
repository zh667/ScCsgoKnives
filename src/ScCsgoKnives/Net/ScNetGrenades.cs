using System.Runtime.CompilerServices;
using Engine;
namespace Game;

/// <summary>A remote client's throw on the server: whether that client still holds it (its throw has not begun there)
/// and the view it aims with.</summary>
public sealed class ScRemoteThrow {
    public bool Pressed, HasView;
    /// <summary>When the held state was last received (ScNet.Now).</summary>
    public double ReceivedAt;
    /// <summary>Diagnostics: held states that expired.</summary>
    public int Expired;
    public Vector3 Origin, ViewPosition, ViewDirection = -Vector3.UnitZ;
}

/// <summary>Grenades over the network (current-direction-20260929 §6, M3).
/// Client: its own throw is shown as before (pin, hold, throw, preview) and sent — start, held state and view, cancel; it
/// never takes the item or creates the grenade. Server: runs the same throw timeline for that player from what it sent,
/// releases from its view, and owns every grenade (flight, fuse, detonation, damage, smoke, fire, decoy, flash). Clients get
/// the grenades as a compact snapshot 10 times a second while any exist (they move them locally in between), and the
/// world sounds, fire bursts and their own flash blindness as events.</summary>
public static class ScNetGrenades {
    public const ushort OpStart = 60, OpHeld = 61, OpCancel = 62, OpSnapshot = 63, OpSound = 64, OpBurst = 65, OpBlind = 66;
    const double Interval = .1;
    /// <summary>Server: how long before the end of a remote client's previous throw (already released, its follow-through
    /// still shown here) that client's next start is taken (quick-throw-20261002). The two ends run the same throw on their
    /// own clocks, the server's later by the link's delay; without this a start arriving a frame early was refused and that
    /// throw was shown on the client and never made.</summary>
    public const double TailLead = .25;
    static readonly ConditionalWeakTable<ComponentPlayer, ScRemoteThrow> s_remote = new();
    static double s_sentAt; static int s_lastCount = -1;

    public static void Register() {
        ScNet.OnServer(OpStart, ReceiveStart);
        ScNet.OnServer(OpHeld, ReceiveHeld);
        ScNet.OnServer(OpCancel, (from, player, r) => Grenades?.CancelRemoteThrow(player));
        ScNet.OnClient(OpSnapshot, r => Grenades?.ApplyNetworkSnapshot(r));
        ScNet.OnClient(OpSound, ReceiveSound);
        ScNet.OnClient(OpBurst, r => Grenades?.AddFireBurst(r.Vector3()));
        ScNet.OnClient(OpBlind, ReceiveBlind);
    }
    static SubsystemScGrenades Grenades => GameManager.Project?.FindSubsystem<SubsystemScGrenades>(false);

    // ---------------------------------------------------------------- server
    public static ScRemoteThrow RemoteThrow(ComponentPlayer player) => ScNet.IsRemoteDriven(player) ? s_remote.GetOrCreateValue(player) : null;
    /// <summary>Server, each update of a remote client's throw that is still in hand: a client renews its held throw every
    /// <see cref="ScNetGuns.InputRefresh"/> s; one that went silent for <see cref="ScNetGuns.InputLease"/> no longer holds
    /// it, and the throw is cancelled (nothing is thrown or consumed for it). True when it expired now.</summary>
    public static bool HeldExpired(ComponentPlayer player) {
        if (!ScNet.IsRemoteDriven(player) || !s_remote.TryGetValue(player, out var t) || !t.Pressed || ScNet.Now - t.ReceivedAt <= ScNetGuns.InputLease) return false;
        t.Pressed = false; t.Expired++;
        ScNet.Trace($"grenade P{player.PlayerData?.PlayerIndex} held throw expired: no message for {ScNet.Now - t.ReceivedAt:0.00} s");
        return true;
    }
    static void ReadView(ComponentPlayer player, ScNetReader r) {
        var t = s_remote.GetOrCreateValue(player);
        Vector3 origin = r.Vector3(), view = r.Vector3(); Vector3 direction = r.Vector3();
        Vector3 eye = player.ComponentCreatureModel?.EyePosition ?? player.ComponentBody.Position;
        // Throws leave from the thrower's own eye; a view farther away is taken as a direction only.
        if (Vector3.DistanceSquared(origin, eye) > 2.5f * 2.5f) origin = eye;
        if (Vector3.DistanceSquared(view, eye) > 6 * 6) view = eye;
        float length = direction.Length();
        if (length < 1e-4f) return;
        t.Origin = origin; t.ViewPosition = view; t.ViewDirection = direction / length; t.HasView = true;
    }
    static void ReceiveStart(ScNetPeer from, ComponentPlayer player, ScNetReader r) {
        bool low = r.Bool(); ReadView(player, r);
        var t = s_remote.GetOrCreateValue(player); t.Pressed = true; t.ReceivedAt = ScNet.Now;
        Grenades?.StartRemoteThrow(player, low);
    }
    static void ReceiveHeld(ScNetPeer from, ComponentPlayer player, ScNetReader r) {
        bool pressed = r.Bool(); ReadView(player, r);
        var t = s_remote.GetOrCreateValue(player); t.Pressed = pressed; t.ReceivedAt = ScNet.Now;
    }

    /// <summary>Server, each update: the grenades as they are now, to every client (while any exist, and once when the last ends).</summary>
    public static void ServerTick(IReadOnlyList<ScGrenadeState> active, IReadOnlyList<ScSmokeDisturbance> openings) {
        if (!ScNet.IsHost || ScNet.Peers.Count == 0) { s_lastCount = -1; return; }
        if (Time.RealTime < s_sentAt || active.Count == 0 && s_lastCount == 0) return;
        s_sentAt = Time.RealTime + Interval; s_lastCount = active.Count;
        ScNet.Broadcast(OpSnapshot, w => {
            int n = Math.Min(active.Count, 256);
            w.Int(n);
            for (int i = 0; i < n; i++) {
                var s = active[i];
                w.Int(s.Id).Byte((byte)s.Kind).Int(s.Owner).Vector3(s.Position).Vector3(s.Velocity).Float(s.Remaining).Float(s.Age).Bool(s.Effect).Bool(s.Grounded);
            }
            int m = Math.Min(openings.Count, 32);
            w.Int(m);
            for (int i = 0; i < m; i++) {
                var o = openings[i];
                w.Vector3(o.Center).Float(o.Remaining).Int(Math.Min(o.SmokeIds.Count, 16));
                foreach (int id in o.SmokeIds.Take(16)) w.Int(id);
            }
        });
    }
    /// <summary>Server: a world sound for every client (the server plays it itself): grenades, and the C4, Tactical NPC
    /// shots and enemy bombs, which use the same message.</summary>
    public static void Sound(string path, float volume, Vector3 position, float range, ComponentPlayer except = null) =>
        ScNet.Broadcast(OpSound, w => w.String(path).Float(volume).Vector3(position).Float(range), except is null ? null : ScNet.PeerOf(except));
    public static void Burst(Vector3 position) => ScNet.Broadcast(OpBurst, w => w.Vector3(position));
    /// <summary>Server: a remote client's player was flashed.</summary>
    public static void Blind(ComponentPlayer victim, float duration) => ScNet.SendTo(ScNet.PeerOf(victim), OpBlind, w => w.Float(duration));

    /// <summary>Counters read by the two-process tests.</summary>
    public static int SoundsReceived; public static string LastSound = "";
    static void ReceiveSound(ScNetReader r) {
        string path = r.String(256); float volume = Math.Clamp(r.Float(), 0, 2); Vector3 position = r.Vector3(); float range = Math.Clamp(r.Float(), 0, 64);
        if (!path.StartsWith("Audio/ScCsgoKnives/", StringComparison.Ordinal)) return;
        SoundsReceived++; LastSound = path;
        try { GameManager.Project?.FindSubsystem<SubsystemAudio>(false)?.PlaySound(path, volume, 0, position, range, true); }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("scnet-grenade-sound-" + path, $"[ScCsgoNet] sound {path}: {e.Message}"); }
    }
    static void ReceiveBlind(ScNetReader r) {
        float duration = Math.Clamp(r.Float(), 0, ScGrenadeState.FlashMaximum);
        var player = GameManager.Project?.FindSubsystem<SubsystemPlayers>(false)?.ComponentPlayers.FirstOrDefault(ScNet.IsLocal);
        if (player is not null) Grenades?.BlindLocal(player, duration);
    }

    // ---------------------------------------------------------------- client
    static bool s_lastPressed; static Vector3 s_lastDirection; static double s_heldAt;
    static void WriteView(ScNetWriter w, (Vector3 Origin, Vector3 ViewPosition, Vector3 ViewDirection) view) => w.Vector3(view.Origin).Vector3(view.ViewPosition).Vector3(view.ViewDirection);
    public static bool SendStart(bool low, (Vector3, Vector3, Vector3) view) {
        if (!ScNet.IsRemoteClient) return false;
        s_lastPressed = true; s_heldAt = ScNet.Now;
        bool sent = ScNet.Send(OpStart, w => WriteView(w.Bool(low), view));
        ScNet.Trace($"grenade start sent low={low}: {sent}");
        return sent;
    }
    /// <summary>Client, every frame of its own throw: whether it is still held - false from the frame this client's own
    /// timeline began the throw (button up, and the draw gate passed) - when that changes, and the view while it moves.
    /// The server throws on that word; it does not time the press itself.</summary>
    public static void SendHeld(bool pressed, (Vector3, Vector3, Vector3 ViewDirection) view) {
        if (!ScNet.IsRemoteClient) return;
        bool moved = Vector3.Dot(view.ViewDirection, s_lastDirection) < .99998f;
        if (pressed == s_lastPressed && !(moved && ScNet.Now - s_heldAt > 1 / 30.0) && ScNet.Now - s_heldAt < ScNetGuns.InputRefresh) return;
        s_lastPressed = pressed; s_lastDirection = view.ViewDirection; s_heldAt = ScNet.Now;
        ScNet.Send(OpHeld, w => WriteView(w.Bool(pressed), view));
    }
    public static void SendCancel() { if (ScNet.IsRemoteClient) ScNet.Send(OpCancel, null); }
}
