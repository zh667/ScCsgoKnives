using System.Runtime.CompilerServices;
using Engine;
using GameEntitySystem;
namespace Game;

/// <summary>CS2 hit feedback (headshot-armor-balance-20260929 H4, current-direction-20260929 §4): one sound per shot per
/// target, chosen from what the accepted injury actually did, never from the target's appearance. A shot whose pellets
/// reached the head plays CS2's helmet "dink" (headshot_armor_e1, the sample CS2's DamageHeadShotArmor events reference)
/// with a short cyan-white spark at the first head pellet when the target's head protection really absorbed part of it,
/// even with no health lost; otherwise the bare-head sound when it hurt (a used-up helmet absorbs nothing: bare head, no
/// spark). A body shot plays the kevlar sound when its body protection absorbed part of it, otherwise the native body
/// impact stays. Walls, disabled friendly fire, cancelled injuries, invulnerability and misses never commit protection or
/// hurt, so they never sound. A shotgun is one shot (pellets are merged per target), and repeated hits on one target keep
/// a short spacing. Levels follow CS2's own events per listener role: the local player hit (victim), the local player's
/// own shot (attacker feedback, heard at the shooter) or anyone else's hit (onlooker, positional at the hit). Other
/// projectiles use the same feedback only when their hit geometry named the region; melee never does.</summary>
public static class ScHitSounds {
    public const string HeadshotNoArmor = "Audio/ScCsgoKnives/Hits/headshot_noarmor", HelmetDink = "Audio/ScCsgoKnives/Hits/helmet_dink", Kevlar = "Audio/ScCsgoKnives/Hits/kevlar";
    public const float MinDistance = 8, Spacing = .06f;
    public enum Role { Onlooker, Attacker, Victim }
    /// <summary>CS2's event volumes (game_sounds_player.vsndevts): DamageHeadShotArmor Victim .75 / AttackerFeedback .5 /
    /// Onlooker .5; DamageHeadShot Victim .49 / .5 / .5; DamageKevlar .5, DamageBodyArmor.AttackerFeedback 1.2 (capped at 1).
    /// Relative levels, not yet confirmed by listening against gunfire.</summary>
    public static float Volume(string sound, Role role) => sound switch {
        HelmetDink => role == Role.Victim ? .75f : .5f,
        HeadshotNoArmor => role == Role.Victim ? .49f : .5f,
        Kevlar => role == Role.Attacker ? 1f : .5f,
        _ => .5f
    };
    sealed class Last { public double At = double.NegativeInfinity; }
    static readonly ConditionalWeakTable<ComponentBody, Last> s_last = new();
    /// <summary>What the target's CS protection absorbed on the head and on the body (0 without protection).</summary>
    public readonly record struct Absorbed(float Helmet, float Vest);
    /// <summary>The sound for a shot, or null for the native sounds only.</summary>
    public static string Choose(ScShotHits hits, bool hurt, Absorbed armor) =>
        hits is null ? null : hits.AnyHead ? (armor.Helmet > 0 ? HelmetDink : hurt ? HeadshotNoArmor : null) : armor.Vest > 0 ? Kevlar : null;
    /// <summary>Before delivery: a shot that reached the head brings its own feedback, so the native body impact of the same
    /// attack is muted (never two full sounds for one hit). Returns the shot's regions (null: native sounds only).</summary>
    public static ScShotHits Before(Attackment attack) {
        if (attack is not ScSurvivalBalance.BulletAttack { Hits: { } hits } bullet) return null;
        if (hits.AnyHead) bullet.AttackSoundVolume = 0;
        return hits;
    }
    /// <summary>After delivery: plays the chosen sound once (and the helmet spark); returns the sound (null when none).</summary>
    public static string After(Attackment attack, ScShotHits hits, ComponentHealth health, float before) {
        if (hits is null || health is null) return null;
        var bullet = attack as ScSurvivalBalance.BulletAttack;
        string sound = Choose(hits, health.Health < before - 1e-6f, bullet?.Absorbed ?? default);
        return sound is null ? null : Feedback(attack, sound, sound == HelmetDink ? hits.HeadPoint : hits.Point, sound == HelmetDink ? hits.HeadDirection : hits.Direction);
    }
    /// <summary>Other projectiles: their committed protection, when their hit geometry named the region (current-direction-20260929 §4).</summary>
    static void Settled(Attackment attack, ScArmorSettlement settlement) {
        if (settlement.Channel != ScArmorChannel.Projectile || attack is ScSurvivalBalance.BulletAttack) return;
        Feedback(attack, settlement.Helmet > 0 ? HelmetDink : Kevlar, attack.HitPoint, attack.HitDirection);
    }
    static ScHitSounds() => SubsystemScArmor.Settled += Settled;
    public static void Initialize() { } // runs the static constructor (the settlement subscription) at mod load
    static string Feedback(Attackment attack, string sound, Vector3 point, Vector3 direction) {
        var body = attack.Target?.FindComponent<ComponentBody>(); var project = attack.Target?.Project;
        if (body is null || project is null) return null;
        var time = project.FindSubsystem<SubsystemTime>(false); double now = time?.GameTime ?? 0; var last = s_last.GetOrCreateValue(body);
        if (now - last.At < Spacing && now >= last.At) return null;
        last.At = now;
        if (!Play(project, sound, point, direction, attack.Attacker, attack.Target)) return null;
        // Multiplayer: every client hears the same hit in its own role.
        ScNetFeedback.HitSound(sound, point, direction, attack.Attacker, attack.Target);
        return sound;
    }
    /// <summary>Plays a settled hit for this process's listeners (the server's own result, or one it reported).</summary>
    public static bool Play(Project project, string sound, Vector3 point, Vector3 direction, Entity attacker, Entity target) {
        var role = RoleFor(attacker, target, out var listener);
        try { project.FindSubsystem<SubsystemAudio>(false)?.PlaySound(sound, Volume(sound, role), 0, role == Role.Attacker && listener is { } ear ? ear : point, MinDistance, false); }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("hit-sound-" + sound, "hit sound unavailable: " + e.Message); return false; }
        if (sound == HelmetDink) ScHelmetSpark.Emit(project, point, direction, role == Role.Victim);
        return true;
    }
    public static Role RoleOf(Attackment attack, out Vector3? shooterEye) => RoleFor(attack.Attacker, attack.Target, out shooterEye);
    /// <summary>The listener role of this hit for the players this process shows: hit (victim) wins over shooter (attacker
    /// feedback, heard at the shooter's eye), otherwise onlooker. In multiplayer another client's player is a bystander here.</summary>
    public static Role RoleFor(Entity attacker, Entity target, out Vector3? shooterEye) {
        shooterEye = null;
        if (target?.FindComponent<ComponentPlayer>() is { } victim && ScNet.IsLocal(victim)) return Role.Victim;
        if (attacker?.FindComponent<ComponentPlayer>() is { } shooter && ScNet.IsLocal(shooter)) {
            shooterEye = shooter.ComponentCreatureModel?.EyePosition ?? shooter.ComponentBody?.Position; return Role.Attacker;
        }
        return Role.Onlooker;
    }
}
