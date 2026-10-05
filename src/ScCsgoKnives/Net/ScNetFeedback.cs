using Engine;
using GameEntitySystem;
namespace Game;

/// <summary>What the server tells players about results only it knows (current-direction-20260929 §6, M2):
/// the shooter's hit/kill confirmation (hit marker, kill panel, kill sound), the victim's damage direction, the CS hit
/// sounds with the helmet spark (every client works out its own listener role: hit, shooter or bystander), and refusals.
/// The server shows the same things itself for its own local player; a remote client never computes them.</summary>
public static class ScNetFeedback {
    public const ushort OpHit = 42, OpDamage = 43, OpHitSound = 44, OpNotice = 45, OpVoice = 47;
    /// <summary>Counters read by the two-process tests.</summary>
    public static int HitsReceived, DamageMarksReceived, HitSoundsReceived, NoticesReceived, VoicesReceived;
    public static string LastHitSound = "", LastNotice = "";

    public static void Register() {
        ScNet.OnClient(OpHit, ReceiveHit);
        ScNet.OnClient(OpDamage, ReceiveDamage);
        ScNet.OnClient(OpHitSound, ReceiveHitSound);
        ScNet.OnClient(OpNotice, ReceiveNotice);
        ScNet.OnClient(OpVoice, ReceiveVoice);
    }

    static ComponentPlayer MainPlayer => GameManager.Project?.FindSubsystem<SubsystemPlayers>(false)?.ComponentPlayers.FirstOrDefault(ScNet.IsLocal);

    // ---- the shooter's confirmation
    /// <summary>Server: a remote shooter's confirmed hit (1), kill (2) or head hit (3).</summary>
    public static void Hit(ComponentPlayer shooter, int outcome, string target, string weapon, float distance) =>
        ScNet.SendTo(ScNet.PeerOf(shooter), OpHit, w => w.Byte((byte)outcome).String(target).String(weapon).Float(distance));
    static void ReceiveHit(ScNetReader r) {
        // Everything is read and checked before anything is shown: only a hit (1), a kill (2) or a head hit (3) with a
        // sane distance and nothing after it is a hit confirmation. mpc3 showed a marker for any positive first byte, so a
        // misrouted shot confirmation drew white hit markers (and could have drawn a kill) for shots at nothing.
        int outcome = r.Byte(); string target = r.String(128), weapon = r.String(128); float distance = r.Float();
        r.Finish();
        if (outcome is < 1 or > 3) throw new System.IO.InvalidDataException($"hit outcome {outcome}");
        if (distance < 0 || distance > 4096) throw new System.IO.InvalidDataException($"hit distance {distance}");
        HitsReceived++;
        if (MainPlayer is { } player) GameManager.Project.FindSubsystem<SubsystemScGunBlockBehavior>(false)?.ShowHit(player, outcome, target, weapon, distance);
    }

    // ---- the victim's damage direction
    public static void Damage(ComponentPlayer victim, Vector3 towardSource, float strength) =>
        ScNet.SendTo(ScNet.PeerOf(victim), OpDamage, w => w.Vector3(towardSource).Float(strength));
    static void ReceiveDamage(ScNetReader r) {
        Vector3 toward = r.Vector3(); float strength = Math.Clamp(r.Float(), 0, 1);
        r.Finish();
        DamageMarksReceived++;
        if (MainPlayer is not { } player) return;
        double now = player.Project.FindSubsystem<SubsystemTime>(false)?.GameTime ?? 0;
        ScDamageIndicator.ReportWith(player, toward, now, strength);
    }

    // ---- CS hit sounds and the helmet spark
    public static void HitSound(string sound, Vector3 point, Vector3 direction, Entity attacker, Entity target) =>
        ScNet.Broadcast(OpHitSound, w => w.String(sound).Vector3(point).Vector3(direction).Int(attacker?.Id ?? -1).Int(target?.Id ?? -1));
    static void ReceiveHitSound(ScNetReader r) {
        string sound = r.String(128); Vector3 point = r.Vector3(), direction = r.Vector3(); int attacker = r.Int(), target = r.Int();
        r.Finish();
        if (sound is not (ScHitSounds.HelmetDink or ScHitSounds.HeadshotNoArmor or ScHitSounds.Kevlar)) return;
        HitSoundsReceived++; LastHitSound = sound;
        var project = GameManager.Project;
        if (project is null) return;
        Entity Find(int id) => id < 0 ? null : project.Entities.FirstOrDefault(e => e.Id == id);
        ScHitSounds.Play(project, sound, point, direction, Find(attacker), Find(target));
    }

    // ---- voice callouts
    /// <summary>Server: a voice event (an NPC's or a player's) for every client's voice package; clients never raise the
    /// events the server's gameplay raises (their NPCs run no AI and their throws commit nothing).</summary>
    public static void Voice(Entity entity, string role, string action, bool player) {
        if (entity is null || !ScNet.IsHost || ScNet.Peers.Count == 0) return;
        ScNet.Broadcast(OpVoice, w => w.Int(entity.Id).String(role ?? "").String(action ?? "").Bool(player));
    }
    static void ReceiveVoice(ScNetReader r) {
        int id = r.Int(); string role = r.String(32), action = r.String(64); bool player = r.Bool();
        r.Finish();
        VoicesReceived++;
        if (GameManager.Project?.Entities.FirstOrDefault(e => e.Id == id) is { } entity) ScAgentVoice.Deliver(entity, role.Length == 0 ? null : role, action, player);
    }

    // ---- refusals and other notices for a remote player
    /// <summary>A message for <paramref name="player"/>: shown here for a player this process shows, sent to its client for a
    /// remote client's player (on the server), dropped otherwise.</summary>
    public static void Tell(ComponentPlayer player, string text, Color color) {
        if (player is null) return;
        if (ScNet.IsLocal(player)) player.ComponentGui?.DisplaySmallMessage(text, color, true, false);
        else if (ScNet.PeerOf(player) is { } peer) ScNet.SendTo(peer, OpNotice, w => w.String(text).Int((int)color.PackedValue));
    }
    static void ReceiveNotice(ScNetReader r) {
        string text = r.String(512); var color = new Color((uint)r.Int());
        r.Finish();
        NoticesReceived++; LastNotice = text;
        MainPlayer?.ComponentGui?.DisplaySmallMessage(text, color, true, false);
    }
}
