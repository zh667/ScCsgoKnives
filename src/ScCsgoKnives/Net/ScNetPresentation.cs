using Engine;
using GameEntitySystem;
namespace Game;

/// <summary>What other players see a player's weapon doing (mp-user-logs-20261002: "远端玩家探员持枪及动作"). Third-person
/// poses read a weapon action (draw, inspect, slash, shoot, reload, silencer, grenade), a throw phase and a C4 plant
/// phase. Before this, each process only had them for players it simulates: its own, and - on the server - what it runs
/// for a remote client (shots, reloads, throws, plants). A client saw nothing of the host's or of another client's
/// actions, and the host saw nothing of a client's own-view actions (inspect, the knife swing).
///
/// Now the process that reads a player's devices is the source of that player's presentation: it sends the action when it
/// changes and the throw/plant phase while one runs (client → server; the server relays to the other accepted clients;
/// the host sends its own player's directly). Observers keep the last state per player and advance it on their own
/// clock. This is presentation only: nothing here strikes, fires, spawns or consumes anything - the server's own state
/// machines stay the authority, and where an observer has its own state for a player (the server for a remote client's
/// throw, plant, shots and reloads) that state wins.</summary>
public static class ScNetPresentation {
    /// <summary>Client → server: the sender's own presentation. Server → clients: the same, for one player.</summary>
    public const ushort OpPresent = 48, OpPlayerPresent = 49;
    const byte HasAction = 1, HasThrow = 2, HasPlant = 4;
    /// <summary>Phases are sent this often while one runs; an observer drops a phase not refreshed for <see cref="PhaseTimeout"/> s.</summary>
    public const double PhaseInterval = 1 / 20.0, PhaseTimeout = .5;
    /// <summary>At most this many presentation messages a second are taken from one client (the rest are dropped).</summary>
    public const int MaxPerSecond = 60;
    /// <summary>Remote action sequences live above the local controller's, so a cached pose keyed by sequence never
    /// mistakes one for the other.</summary>
    const long RemoteSequenceBase = 1L << 40;

    public sealed class Remote {
        public readonly ScWeaponActionTimeline Timeline = new();
        public double ActionStartedAt = double.NegativeInfinity;
        public bool LoopedReload;
        public ScThrowPhase Throw; public double ThrowAt = double.NegativeInfinity;
        public ScPlantPhase Plant; public double PlantAt = double.NegativeInfinity;
        public long Messages; public double RateSecond; public int RateCount;
    }
    static readonly Dictionary<int, Remote> s_remote = [];
    /// <summary>Counters read by tests.</summary>
    public static int Sent, Relayed, Applied, Dropped;

    public static void Register() {
        ScNet.OnServer(OpPresent, ReceiveFromClient);
        ScNet.OnClient(OpPlayerPresent, r => { int index = r.Int(); Apply(index, r); });
        ScNet.PeerLeft += peer => s_remote.Remove(peer.PlayerIndex);
    }
    public static void Clear() { s_remote.Clear(); s_last = default; s_lastThrowActive = s_lastPlantActive = false; s_phaseAt = 0; }
    public static Remote RemoteOf(int playerIndex) => s_remote.GetValueOrDefault(playerIndex);

    // ---------------------------------------------------------------- owner: this process's own player
    readonly record struct Signature(string Asset, ScWeaponActionKind Kind, string Clip, long Sequence, int DurationMs, bool Looped);
    static Signature s_last; static bool s_lastThrowActive, s_lastPlantActive; static double s_phaseAt;

    /// <summary>Every frame, for the player this process reads the devices of, while a session exists.</summary>
    public static void Tick(ComponentPlayer player, ScWeaponAction action, ScThrowPhase thrown, ScPlantPhase plant) {
        bool client = ScNet.IsRemoteClient && !ScNet.ClientBlocked, host = ScNet.IsHost && ScNet.Peers.Count > 0;
        if (!client && !host || player?.PlayerData is null) return;
        var signature = new Signature(action.Asset, action.Kind, action.Clip, action.Sequence, (int)(action.Duration * 1000), action.LoopedReload);
        bool actionChanged = signature != s_last;
        bool phases = thrown.Active || plant.Active, phasesEnded = (s_lastThrowActive && !thrown.Active) || (s_lastPlantActive && !plant.Active);
        bool phaseDue = phases && Time.RealTime - s_phaseAt >= PhaseInterval;
        if (!actionChanged && !phaseDue && !phasesEnded) return;
        byte flags = (byte)((actionChanged ? HasAction : 0) | (phaseDue || phasesEnded ? HasThrow | HasPlant : 0));
        void Write(ScNetWriter w) {
            w.Byte(flags);
            if ((flags & HasAction) != 0) WriteAction(w, action);
            if ((flags & HasThrow) != 0) WriteThrow(w, thrown);
            if ((flags & HasPlant) != 0) WritePlant(w, plant);
        }
        bool sent;
        if (client) sent = ScNet.Send(OpPresent, Write);
        else { ScNet.Broadcast(OpPlayerPresent, w => { w.Int(player.PlayerData.PlayerIndex); Write(w); }); sent = true; }
        if (!sent) return;
        Sent++;
        if (actionChanged) s_last = signature;
        if ((flags & HasThrow) != 0) { s_phaseAt = Time.RealTime; s_lastThrowActive = thrown.Active; s_lastPlantActive = plant.Active; }
    }

    // ---------------------------------------------------------------- server: relay
    static void ReceiveFromClient(ScNetPeer from, ComponentPlayer player, ScNetReader r) {
        if (!s_remote.TryGetValue(from.PlayerIndex, out var remote)) s_remote[from.PlayerIndex] = remote = new Remote();
        byte[] rest = r.Rest();                                        // (taken whole, also when it is dropped for the rate)
        double second = Math.Floor(Time.RealTime);
        if (remote.RateSecond != second) { remote.RateSecond = second; remote.RateCount = 0; }
        if (++remote.RateCount > MaxPerSecond) { Dropped++; return; }
        // Parsed (and so validated) here before anything is relayed; a malformed message throws and is dropped whole.
        Apply(from.PlayerIndex, new ScNetReader(rest));
        ScNet.Broadcast(OpPlayerPresent, w => w.Int(from.PlayerIndex).Raw(rest), from);
        Relayed++;
    }

    // ---------------------------------------------------------------- observers
    static void Apply(int playerIndex, ScNetReader r) {
        byte flags = r.Byte();
        ScWeaponAction action = default; ScThrowPhase thrown = default; ScPlantPhase plant = default;
        if ((flags & HasAction) != 0) action = ReadAction(r);
        if ((flags & HasThrow) != 0) thrown = ReadThrow(r);
        if ((flags & HasPlant) != 0) plant = ReadPlant(r);
        if (!s_remote.TryGetValue(playerIndex, out var remote)) s_remote[playerIndex] = remote = new Remote();
        double now = KnifeClock.Now;
        if ((flags & HasAction) != 0) {
            remote.ActionStartedAt = now - action.Elapsed; remote.LoopedReload = action.LoopedReload;
            if (action.Asset is null) remote.Timeline.Clear();
            else remote.Timeline.Mirror(action.Asset, action.Kind, action.Clip, remote.ActionStartedAt, action.Duration, RemoteSequenceBase + action.Sequence);
        }
        if ((flags & HasThrow) != 0) { remote.Throw = thrown; remote.ThrowAt = Time.RealTime; }
        if ((flags & HasPlant) != 0) { remote.Plant = plant; remote.PlantAt = Time.RealTime; }
        remote.Messages++; Applied++;
    }

    static bool Observed(ComponentPlayer player, out Remote remote) {
        remote = null;
        return player?.PlayerData is not null && ScNet.Role is ScNetRole.Host or ScNetRole.Client && !ScNet.IsLocal(player)
            && s_remote.TryGetValue(player.PlayerData.PlayerIndex, out remote);
    }

    /// <summary>The weapon action third-person poses show for this entity: the local controller's for a player this
    /// process simulates, otherwise whichever of it and the owner's replicated action started later and still runs.</summary>
    public static ScWeaponAction ActionOf(Entity entity) {
        var local = KnifeAnimationController.ReadAction(entity?.FindComponent<ComponentFirstPersonModel>());
        if (entity is null || ScNet.Role is not (ScNetRole.Host or ScNetRole.Client) || !Observed(entity.FindComponent<ComponentPlayer>(), out var remote)) return local;
        var mirrored = remote.Timeline.Read(KnifeClock.Now);
        if (mirrored.Asset is null || !mirrored.Active) return local;
        mirrored = mirrored with { LoopedReload = remote.LoopedReload };
        if (!local.Active || local.Kind is ScWeaponActionKind.Idle) return mirrored;
        double localStartedAt = KnifeClock.Now - local.Elapsed;
        return localStartedAt > remote.ActionStartedAt ? local : mirrored;
    }
    /// <summary>The throw phase observers show for a player they do not simulate (default when none, or stale).</summary>
    public static ScThrowPhase ThrowOf(ComponentPlayer player) =>
        Observed(player, out var remote) && Time.RealTime - remote.ThrowAt <= PhaseTimeout ? remote.Throw : default;
    /// <summary>The plant phase observers show for a player they do not simulate; its clock keeps running between updates.</summary>
    public static ScPlantPhase PlantOf(ComponentPlayer player) {
        if (!Observed(player, out var remote) || !remote.Plant.Active) return default;
        double age = Time.RealTime - remote.PlantAt;
        return age <= PhaseTimeout ? remote.Plant with { Seconds = remote.Plant.Seconds + (float)age } : default;
    }

    // ---------------------------------------------------------------- wire (bounded on read)
    public static void WriteAction(ScNetWriter w, ScWeaponAction a) =>
        w.String(a.Asset ?? "").Byte((byte)a.Kind).String(a.Clip ?? "").Long(a.Sequence).Float(a.Elapsed).Float(a.Duration).Bool(a.LoopedReload);
    public static ScWeaponAction ReadAction(ScNetReader r) {
        string asset = r.String(64); var kind = (ScWeaponActionKind)Math.Min(r.Byte(), (byte)ScWeaponActionKind.Grenade); string clip = r.String(64);
        long sequence = Math.Clamp(r.Long(), 0, RemoteSequenceBase - 1); float elapsed = Finite(r.Float(), 0, 600), duration = Finite(r.Float(), 0, 60); bool looped = r.Bool();
        return new(asset.Length == 0 ? null : asset, kind, clip, sequence, elapsed, duration, elapsed, looped);
    }
    public static void WriteThrow(ScNetWriter w, ScThrowPhase t) {
        w.Bool(t.Active);
        if (t.Active) w.Int(t.Value).String(t.Asset).Byte((byte)Math.Clamp(t.Stage, 0, 3)).Bool(t.Low).Bool(t.Released).Float(t.Pull).Float(t.Hold).Float(t.Wind).Float(t.Follow);
    }
    public static ScThrowPhase ReadThrow(ScNetReader r) {
        if (!r.Bool()) return default;
        int value = r.Int(); string asset = r.String(64); int stage = Math.Min(r.Byte(), (byte)3); bool low = r.Bool(), released = r.Bool();
        float pull = Finite(r.Float(), 0, 1), hold = Finite(r.Float(), 0, 600), wind = Finite(r.Float(), 0, 1), follow = Finite(r.Float(), 0, 1);
        // Only a throwable this version knows is shown; the value is rebuilt from the asset, never taken from the wire.
        int kind = Array.IndexOf(ScGrenadeBlock.Assets, asset);
        return kind < 0 ? default : new(ScGrenadeBlock.Value(kind), asset, stage, low, released, pull, hold, wind, follow);
    }
    public static void WritePlant(ScNetWriter w, ScPlantPhase p) {
        w.Bool(p.Active);
        if (p.Active) w.Bool(p.Placed).Float(p.Seconds).Long(p.Sequence).Vector3(p.Position);
    }
    public static ScPlantPhase ReadPlant(ScNetReader r) {
        if (!r.Bool()) return default;
        bool placed = r.Bool(); float seconds = Finite(r.Float(), 0, ScPlantPhase.EndSeconds + 1); long sequence = r.Long(); Vector3 position = r.Vector3();
        if (!float.IsFinite(position.X + position.Y + position.Z)) position = Vector3.Zero;
        return new(ScC4Block.Value, placed, seconds, RemoteSequenceBase + Math.Clamp(sequence, 0, RemoteSequenceBase - 1), position);
    }
    static float Finite(float value, float min, float max) => float.IsFinite(value) ? Math.Clamp(value, min, max) : min;
}
