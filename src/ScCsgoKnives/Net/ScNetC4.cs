using System.Runtime.CompilerServices;
using Engine;
namespace Game;

/// <summary>A remote client's plant input on the server: the plant key held, and that client's own fuse choice.</summary>
public sealed class ScRemotePlant {
    public bool Held; public int Fuse = 20;
    /// <summary>When the plant key's state was last received (ScNet.Now).</summary>
    public double ReceivedAt;
    /// <summary>Diagnostics: held states that expired.</summary>
    public int Expired;
}

/// <summary>C4 over the network (current-direction-20260929 §6, M3). The planter's client shows its own plant (crouch,
/// animation, progress, key presses) and sends the plant key's state with its fuse choice; the server runs the same
/// 3.2 s plant, takes the item and arms the charge. Every armed charge is the server's; clients get the charges 4 times a
/// second (their beeps and countdown run locally from that) and each blast as an event. Damage is the server's.</summary>
public static class ScNetC4 {
    public const ushort OpInput = 70, OpCharges = 71, OpBlast = 72;
    static readonly ConditionalWeakTable<ComponentPlayer, ScRemotePlant> s_remote = new();
    static double s_sentAt; static int s_lastCount = -1;
    static bool s_lastHeld; static int s_lastFuse; static double s_heldAt;
    /// <summary>Blasts this client was shown (read by the two-process tests).</summary>
    public static int BlastsShown;

    public static void Register() {
        ScNet.OnServer(OpInput, (from, player, r) => {
            var plant = s_remote.GetOrCreateValue(player);
            plant.Held = r.Bool(); plant.Fuse = Math.Clamp(r.Int(), 5, 300); plant.ReceivedAt = ScNet.Now;
        });
        ScNet.OnClient(OpCharges, r => C4?.ApplyNetworkCharges(r));
        ScNet.OnClient(OpBlast, r => { Vector3 at = r.Vector3(); float radius = r.Float(); BlastsShown++; C4?.ShowBlast(at, radius); });
        ScNet.PeerLeft += peer => { if (peer.Player(GameManager.Project) is { } p && s_remote.TryGetValue(p, out var plant)) plant.Held = false; };
    }
    static SubsystemScC4 C4 => GameManager.Project?.FindSubsystem<SubsystemScC4>(false);

    /// <summary>Server: a remote client's plant key. A key the client stopped renewing (it repeats the state every
    /// <see cref="ScNetGuns.InputRefresh"/> s) counts as released after <see cref="ScNetGuns.InputLease"/>: the plant in
    /// progress is cancelled as for any release, never completed for a client that went silent.</summary>
    public static ScRemotePlant RemotePlant(ComponentPlayer player) {
        if (!ScNet.IsRemoteDriven(player)) return null;
        var plant = s_remote.GetOrCreateValue(player);
        if (plant.Held && ScNet.Now - plant.ReceivedAt > ScNetGuns.InputLease) {
            plant.Held = false; plant.Expired++;
            ScNet.Trace($"c4 P{player.PlayerData?.PlayerIndex} plant key expired: no message for {ScNet.Now - plant.ReceivedAt:0.00} s");
        }
        return plant;
    }

    /// <summary>Client: the plant key's state (when it changes, and repeated while nothing changes) with this player's fuse choice.</summary>
    public static void SendInput(bool held, int fuse) {
        if (!ScNet.IsRemoteClient || held == s_lastHeld && fuse == s_lastFuse && ScNet.Now - s_heldAt < (held ? ScNetGuns.InputRefresh : 2)) return;
        s_lastHeld = held; s_lastFuse = fuse; s_heldAt = ScNet.Now;
        ScNet.Send(OpInput, w => w.Bool(held).Int(fuse));
    }

    /// <summary>Server, each update: the armed charges (while any exist, and once when the last is gone).</summary>
    public static void ServerTick(IReadOnlyList<ScC4Charge> charges) {
        if (!ScNet.IsHost || ScNet.Peers.Count == 0) { s_lastCount = -1; return; }
        if (Time.RealTime < s_sentAt || charges.Count == 0 && s_lastCount == 0) return;
        s_sentAt = Time.RealTime + .25; s_lastCount = charges.Count;
        ScNet.Broadcast(OpCharges, w => {
            w.Int(charges.Count);
            foreach (var c in charges) w.Int(c.Owner).Vector3(c.Position).Float(c.Yaw).Float(c.Remaining).Float(c.Fuse).Float(c.Power).Float(c.Radius);
        });
    }
    public static void Blast(Vector3 position, float radius) => ScNet.Broadcast(OpBlast, w => w.Vector3(position).Float(radius));
}
