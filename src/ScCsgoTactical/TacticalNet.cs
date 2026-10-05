using System.Runtime.CompilerServices;
using Engine;
using GameEntitySystem;
namespace Game;

/// <summary>The Tactical addon over the network (current-direction-20260929 §6, M3), on the core's ScNet layer.
/// Server: runs every companion and enemy (AI, shots, grenades, bombs, loot, dormancy), beacon uses (the engine repeats a
/// client's use there), companion orders and bomb defusing, validating each client request against its own state (owner,
/// distance, emptiness).
/// Clients: run none of it; the engine replicates the NPC bodies, health and inventories, and this layer adds what the
/// engine does not: each NPC's visible action state (timeline, reload, throw, plant, crouch) and a companion's orders
/// for its owner's panel, the enemy bombs with their defuse progress, and (through the core) the NPC sounds and voices.
/// Outside multiplayer nothing here runs and every path is the single-player one.</summary>
public static class TacticalNet {
    public const ushort OpNpc = 80, OpCommand = 81, OpBombs = 83, OpDefuse = 84, OpBlast = 85;
    public enum Command : byte { Follow, Guard, Cover, ToggleCeaseFire, Dismiss, RemoveOwnerless, PanelOpen, PanelClosed }
    /// <summary>Counters read by the two-process tests.</summary>
    public static int NpcRecordsSent, NpcRecordsApplied, CommandsApplied, CommandsRefused, BombsSent, BombsApplied, BlastsShown;
    public static string LastRefusal = "";

    public static void Register() {
        ScNet.OnClient(OpNpc, ReceiveNpc);
        ScNet.OnServer(OpCommand, ReceiveCommand);
        ScNet.OnClient(OpBombs, r => Project?.FindSubsystem<SubsystemTacticalBombs>(false)?.ApplyNetworkBombs(r));
        ScNet.OnServer(OpDefuse, ReceiveDefuse);
        ScNet.OnClient(OpBlast, r => { Vector3 at = r.Vector3(); float radius = Math.Clamp(r.Float(), 0, 64); BlastsShown++; Project?.FindSubsystem<SubsystemTacticalBombs>(false)?.ShowBlast(at, radius); });
        ScNet.PeerAccepted += _ => s_sent = new(); // a joining client gets every NPC's state again
        ScNet.PeerLeft += peer => { if (peer.Player(Project) is { } p && s_defuse.TryGetValue(p, out var d)) d.Held = false; };
    }
    static Project Project => GameManager.Project;
    /// <summary>A world sound on every client (the server plays its own copy).</summary>
    public static void Sound(string path, float volume, Vector3 position, float range) { if (ScNet.IsHost) ScNetGrenades.Sound(path, volume, position, range); }
    /// <summary>An enemy bomb's blast for every client (damage and sound are the server's and a world sound).</summary>
    public static void Blast(Vector3 position, float radius) { if (ScNet.IsHost) ScNet.Broadcast(OpBlast, w => w.Vector3(position).Float(radius)); }
    /// <summary>A notice for one player: shown here as single player shows it, or sent to the client that plays it.</summary>
    public static void Tell(ComponentPlayer player, string text) {
        if (player is null) return;
        if (ScNet.IsLocal(player)) player.ComponentGui?.DisplaySmallMessage(text, Color.White, false, false);
        else ScNetFeedback.Tell(player, text, Color.White);
    }
    static Ray3 FromEye(ComponentPlayer player, Ray3 ray) {
        Vector3 eye = player.ComponentCreatureModel?.EyePosition ?? player.ComponentBody.Position;
        var direction = ray.Direction.LengthSquared() > 1e-6f ? Vector3.Normalize(ray.Direction) : player.ComponentBody.Matrix.Forward;
        return new Ray3(Vector3.DistanceSquared(ray.Position, eye) <= 2.5f * 2.5f ? ray.Position : eye, direction);
    }
    /// <summary>Where a player looks: its camera here, or (server) the view its client last sent.</summary>
    public static Ray3? View(ComponentPlayer player) {
        if (player is null) return null;
        if (s_defuse.TryGetValue(player, out var d) && d.HasView && ScNet.IsRemoteDriven(player)) return d.View;
        if (ScNetGuns.RemoteInput(player) is { HasAim: true } remote) return remote.Aim;
        return player.GameWidget?.ActiveCamera is { } camera ? new Ray3(camera.ViewPosition, camera.ViewDirection) : null;
    }

    // ------------------------------------------------------------------ NPC state (server → clients)
    sealed class Sent { public long Signature; public double At; }
    static ConditionalWeakTable<Entity, Sent> s_sent = new();
    /// <summary>Server, from each NPC's update: its visible state when it changed, and once a second regardless.</summary>
    public static void Publish(Entity entity, long signature, Action<ScNetWriter> write) {
        if (!ScNet.IsHost || ScNet.Peers.Count == 0 || entity is null) return;
        var sent = s_sent.GetOrCreateValue(entity);
        if (sent.Signature == signature && Time.RealTime - sent.At < 1) return;
        sent.Signature = signature; sent.At = Time.RealTime; NpcRecordsSent++;
        ScNet.Broadcast(OpNpc, w => { w.Int(entity.Id); write(w); });
    }
    public static void WriteAction(ScNetWriter w, ScWeaponAction a) =>
        w.String(a.Asset ?? "").Byte((byte)a.Kind).String(a.Clip ?? "").Long(a.Sequence).Float(a.Elapsed).Float(a.Duration);
    public static ScWeaponAction ReadAction(ScNetReader r) {
        string asset = r.String(64); var kind = (ScWeaponActionKind)Math.Min(r.Byte(), (byte)ScWeaponActionKind.Grenade); string clip = r.String(64);
        long sequence = r.Long(); float elapsed = Math.Clamp(r.Float(), 0, 600), duration = Math.Clamp(r.Float(), 0, 60);
        return new(asset.Length == 0 ? null : asset, kind, clip, sequence, elapsed, duration, elapsed);
    }
    static void ReceiveNpc(ScNetReader r) {
        int id = r.Int(); byte kind = r.Byte();
        var project = Project; if (project is null) return;
        if (kind == 1) project.FindSubsystem<SubsystemTacticalEnemies>(false)?.Enemies.FirstOrDefault(e => e.Entity?.Id == id)?.ApplyNetwork(r);
        else if (kind == 2) project.FindSubsystem<SubsystemScTactical>(false)?.Companions.FirstOrDefault(c => c.Entity?.Id == id)?.ApplyNetwork(r);
        else return;
        NpcRecordsApplied++;
    }

    // ------------------------------------------------------------------ companion orders (client → server)
    /// <summary>An order from a companion's panel: applied here when this process runs the companion, sent otherwise.</summary>
    public static void Order(ComponentPlayer player, ComponentTacticalCompanion companion, Command command) {
        if (companion?.Entity is null) return;
        if (ScNet.IsRemoteClient) { ScNet.Send(OpCommand, w => w.Int(companion.Entity.Id).Byte((byte)command)); return; }
        Apply(player, companion, command);
    }
    static void ReceiveCommand(ScNetPeer from, ComponentPlayer player, ScNetReader r) {
        int id = r.Int(); var command = (Command)r.Byte();
        var companion = Project?.FindSubsystem<SubsystemScTactical>(false)?.Companions.FirstOrDefault(c => c.Entity?.Id == id);
        string refusal = companion is null ? "同伴已不在。" : Refusal(player, companion, command);
        if (refusal is not null) { CommandsRefused++; LastRefusal = refusal; if (command is not (Command.PanelOpen or Command.PanelClosed)) Tell(player, refusal); return; }
        Apply(player, companion, command);
    }
    /// <summary>The panel's own conditions, checked again by the server for a remote owner.</summary>
    static string Refusal(ComponentPlayer player, ComponentTacticalCompanion c, Command command) {
        if (player?.ComponentHealth.Health is not > 0) return "无法操作。";
        if (!c.IsAddedToProject || c.DeathHandled || c.Creature.ComponentHealth.Health <= 0) return "同伴已不在。";
        bool empty = Enumerable.Range(0, c.Inventory.SlotsCount).All(i => c.Inventory.GetSlotCount(i) == 0);
        float d2 = Vector3.DistanceSquared(player.ComponentBody.Position, c.Creature.ComponentBody.Position);
        if (command == Command.RemoveOwnerless) return !c.OwnerMissing ? "这名同伴有主人记录。" : !empty ? "这名同伴缺少主人记录，无法认领或管理。" : d2 > 4.5f * 4.5f ? "距离太远。" : null;
        if (!c.OwnedBy(player)) return "这是其他玩家的同伴。";
        if (d2 > 36 && command != Command.PanelClosed) return "距离太远。";
        if (command == Command.Dismiss && !empty) return "请先取回同伴的全部装备。";
        return null;
    }
    static void Apply(ComponentPlayer player, ComponentTacticalCompanion c, Command command) {
        CommandsApplied++;
        switch (command) {
            case Command.Follow: c.Command(TacticalOrder.Follow); break;
            case Command.Guard: c.Command(TacticalOrder.Guard); break;
            case Command.Cover: c.Command(TacticalOrder.Cover); break;
            case Command.ToggleCeaseFire: c.CeaseFire = !c.CeaseFire; break;
            case Command.Dismiss: c.EndArmor(); c.Project.RemoveEntity(c.Entity, true); break;
            case Command.RemoveOwnerless: c.EndArmor(); c.Project.RemoveEntity(c.Entity, true); Tell(player, "已移除无主同伴。"); break;
            case Command.PanelOpen: c.RemotePanelUntil = Time.RealTime + 2.5; break;
            case Command.PanelClosed: c.RemotePanelUntil = 0; break;
        }
    }
    static double s_panelSentAt;
    /// <summary>Client, while its owner's panel is open: the companion stays put on the server as it does in single player.</summary>
    public static void PanelOpen(ComponentPlayer player, ComponentTacticalCompanion c) {
        if (!ScNet.IsRemoteClient || Time.RealTime - s_panelSentAt < 1) return;
        s_panelSentAt = Time.RealTime; Order(player, c, Command.PanelOpen);
    }
    public static void PanelClosed(ComponentPlayer player, ComponentTacticalCompanion c) { if (ScNet.IsRemoteClient) { s_panelSentAt = 0; Order(player, c, Command.PanelClosed); } }

    // ------------------------------------------------------------------ defusing (client → server)
    public sealed class RemoteDefuse { public bool Held, HasView; public Ray3 View; }
    static readonly ConditionalWeakTable<ComponentPlayer, RemoteDefuse> s_defuse = new();
    /// <summary>Server: a remote client's defuse key (touch button, keyboard or gamepad) and view; null for players read here.</summary>
    public static RemoteDefuse Defuse(ComponentPlayer player) => ScNet.IsRemoteDriven(player) ? s_defuse.GetOrCreateValue(player) : null;
    static void ReceiveDefuse(ScNetPeer from, ComponentPlayer player, ScNetReader r) {
        var d = s_defuse.GetOrCreateValue(player);
        d.Held = r.Bool(); d.View = FromEye(player, r.Ray()); d.HasView = true;
    }
    static bool s_lastHeld; static Vector3 s_lastDirection; static double s_defuseAt;
    /// <summary>Client: its defuse key when it changes, and its view while it is held (a few times a second).</summary>
    public static void SendDefuse(bool held, Ray3 view) {
        if (!ScNet.IsRemoteClient) return;
        double since = Time.RealTime - s_defuseAt;
        bool turned = Vector3.Dot(view.Direction, s_lastDirection) < .9995f;
        if (held == s_lastHeld && !(held && turned && since > .1) && since < (held ? .25 : 1)) return;
        s_lastHeld = held; s_lastDirection = view.Direction; s_defuseAt = Time.RealTime;
        ScNet.Send(OpDefuse, w => w.Bool(held).Ray(view));
    }
}
