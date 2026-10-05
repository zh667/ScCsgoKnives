using System.Runtime.CompilerServices;
using Engine;
using GameEntitySystem;
namespace Game;

/// <summary>The weapon actions a game mode is asked about before the authority accepts one.</summary>
public enum ScAttackKind { Shot, Zeus, Knife, Throw, Explosion, Fire, Bomb }

/// <summary>What is known about one attack when it reaches its target (deathmatch-addon, design §11/§13): frozen by the
/// authority when the attack is made, so a result decided later - who killed whom, with what, how - never reads the
/// attacker's present hand, scope or eyes. A fact that could not be established stays at its default; nothing here is
/// taken from a client's word.</summary>
public sealed class ScAttackFacts {
    public ScAttackKind Kind;
    /// <summary>The item value of the weapon that made the attack (0: unknown).</summary>
    public int WeaponValue;
    /// <summary>The weapon's asset name ("ak47", "knife_karambit", "grenade_hegrenade"); null when unknown.</summary>
    public string Weapon;
    /// <summary>The attacker's player index (-1: not a player, or unknown).</summary>
    public int AttackerPlayer = -1;
    /// <summary>The authority's number of the shot / strike / detonation this attack belongs to (pellets share one).</summary>
    public long AttackId;
    /// <summary>The weapon has a scope at all; and whether it was up when the shot left.</summary>
    public bool HasScope, Scoped;
    /// <summary>The path from the muzzle to the hit crossed an active smoke.</summary>
    public bool ThroughSmoke;
    /// <summary>The attacker was flash-blinded when the attack was made.</summary>
    public bool AttackerBlind;
    /// <summary>How many solid cells the round went through before the hit.</summary>
    public int Penetrations;
    /// <summary>A knife's heavy strike.</summary>
    public bool Heavy;
    /// <summary>Eye to hit point, in blocks (0: not a ranged attack).</summary>
    public float Distance;
    /// <summary>Written by the mode that settled the injury (ScMode.OwnsInjuries): this attack ended the target's life
    /// under the mode's rules, although the engine's health did not reach zero. The shooter's feedback reads it.</summary>
    public bool Lethal;
}

/// <summary>An engine attack of this mod that carries its facts.</summary>
public interface IScAttackFacts { ScAttackFacts Facts { get; } }

/// <summary>A world mode's own recoil (deathmatch round 5: CS2's fixed spray patterns). Angles in degrees, X up and Y left
/// of the shooter's own aim; the mode keeps the state, the core applies it: the view part moves the local player's look
/// every frame, the rest bends each round as it leaves.</summary>
public interface IScRecoil {
    /// <summary>At <paramref name="now"/>, before any shot at that moment: where a round would leave relative to the
    /// shooter's own aim (Bullet), and how much of that the view shows (View, part of Bullet).</summary>
    (Vector2 Bullet, Vector2 View) At(ComponentPlayer player, double now);
    /// <summary>A round left: the pattern advances by one shot of <paramref name="spec"/> in that handling state
    /// (<paramref name="alternate"/>: scoped, silenced, burst or the alternate trigger, as the gun's numbers are chosen).</summary>
    void Fired(ComponentPlayer player, GunSpec spec, bool alternate, double now);
}

/// <summary>A game mode an optional package runs in ONE world (deathmatch-addon, design §13). The core asks it at the few
/// places where a world's rules can differ from survival; with no mode registered for the world - every ordinary world -
/// each question has its survival answer and nothing here is consulted. A mode is registered for a Project and gone
/// with it: nothing global is switched.</summary>
public abstract class ScMode {
    /// <summary>Stable id, also the key of the mode's marker in the world (ScWorldModes).</summary>
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    /// <summary>Changes whenever the rules both ends must agree on change (compared in the handshake).</summary>
    public virtual string RulesFingerprint => "";
    /// <summary>This player's weapons follow the mode's rules right now.</summary>
    public virtual bool Governs(ComponentPlayer player) => false;
    /// <summary>No ammunition items, no wear, no growth credit for this player's weapons.</summary>
    public virtual bool FreeUse(ComponentPlayer player) => Governs(player);
    /// <summary>The weapon numbers in force for this shooter; false keeps the survival ones.</summary>
    public virtual bool TryGunStats(ComponentPlayer shooter, GunSpec spec, int value, bool alternate, out EffectiveGunStats stats) { stats = default; return false; }
    /// <summary>May <paramref name="attacker"/> hurt <paramref name="target"/>? null: the world's own rules (its
    /// friendly-fire setting, the factions). <paramref name="sourcePlayer"/> names the player an area effect belongs to
    /// when no attacker entity is at hand.</summary>
    public virtual bool? MayHurt(Entity attacker, Entity target, int sourcePlayer = -1) => null;
    /// <summary>The mode settles this target's injuries itself (in the engine's injury hook): CS protection, clothing and
    /// resilience stay out of the attack's own amount.</summary>
    public virtual bool OwnsInjuries(Entity target) => false;
    /// <summary>Authority, right before a weapon action of this player is committed: the mode may end a protection first,
    /// or refuse the action (false: nothing is fired, struck or thrown, nothing is consumed).</summary>
    public virtual bool AcceptAttack(ComponentPlayer attacker, ScAttackKind kind) => true;
    /// <summary>Authority, after that action was committed (the round left, the strike landed or missed, the throwable
    /// flew): the mode's own bookkeeping, for example a per-life allowance the world's inventory does not count.</summary>
    public virtual void AttackCommitted(ComponentPlayer attacker, ScAttackKind kind, int weaponValue) { }
    /// <summary>Throwables are taken from this player's stack when thrown even in a creative world (the mode counts them).</summary>
    public virtual bool CountsThrowables(ComponentPlayer player) => false;
    /// <summary>What the CS armour HUD shows for this local player in place of the world's CS protection, for a mode that
    /// settles protection of its own (deathmatch round 3, the user: "护甲现在就用核心CS自己的护甲HUD吧"; round 4: CS2's
    /// readout, ScArmorReadout.Cs2); null: the CS protection, as everywhere else. Display only: nothing is stored, sent or
    /// settled from it.</summary>
    public virtual ScArmorReadout? ShownArmour(ComponentPlayer player) => null;
    /// <summary>This player's recoil under the mode's rules (deathmatch round 5: CS2's spray patterns); null: the survival
    /// kick. Asked for a governed player only.</summary>
    public virtual IScRecoil Recoil(ComponentPlayer player) => null;
    /// <summary>The hit volumes of this body under the mode's rules, in world space (deathmatch round 5: CS2's hitboxes); a
    /// shot is tested against exactly these and a ray between them misses. Null: the core's own regions.</summary>
    public virtual ScHitCapsule[] HitCapsules(ComponentBody body) => null;
    /// <summary>How this shooter's rounds cross blocks and bodies under the mode's rules (deathmatch round 6: CS2's
    /// penetration); null: a round stops at the first thing it meets. Asked for a governed player only.</summary>
    public virtual IScPenetration Penetration(ComponentPlayer shooter) => null;
}

/// <summary>The modes of the loaded worlds, one per Project at most.</summary>
public static class ScModes {
    static readonly ConditionalWeakTable<Project, ScMode> s_modes = new();
    /// <summary>Registers the mode this world runs. A second mode for one world is refused (false) and logged; the same
    /// mode registering again replaces itself.</summary>
    public static bool Register(Project project, ScMode mode) {
        if (project is null || mode is null) return false;
        if (s_modes.TryGetValue(project, out var present)) {
            if (present.Id != mode.Id) { KnifeLog.Warning($"[CS_MODE] {mode.Id} refused: this world already runs {present.Id}"); return false; }
            s_modes.Remove(project);
        }
        s_modes.Add(project, mode);
        return true;
    }
    public static void Unregister(Project project, ScMode mode) {
        if (project is not null && s_modes.TryGetValue(project, out var present) && ReferenceEquals(present, mode)) s_modes.Remove(project);
    }
    public static ScMode Of(Project project) => project is not null && s_modes.TryGetValue(project, out var mode) ? mode : null;
    /// <summary>The mode governing this player's weapons, or null (survival rules).</summary>
    public static ScMode For(ComponentPlayer player) => Of(player?.Project) is { } mode && mode.Governs(player) ? mode : null;
    public static bool FreeUse(ComponentPlayer player) => Of(player?.Project)?.FreeUse(player) == true;
    public static bool OwnsInjuries(Entity target) => Of(target?.Project)?.OwnsInjuries(target) == true;
    public static bool? MayHurt(Entity attacker, Entity target, int sourcePlayer = -1) => Of(target?.Project)?.MayHurt(attacker, target, sourcePlayer);
    public static bool AcceptAttack(ComponentPlayer attacker, ScAttackKind kind) => Of(attacker?.Project)?.AcceptAttack(attacker, kind) ?? true;
    public static void AttackCommitted(ComponentPlayer attacker, ScAttackKind kind, int weaponValue) => Of(attacker?.Project)?.AttackCommitted(attacker, kind, weaponValue);
    public static ScArmorReadout? ShownArmour(ComponentPlayer player) => Of(player?.Project)?.ShownArmour(player);
    public static IScRecoil Recoil(ComponentPlayer player) => For(player)?.Recoil(player);
    /// <summary>The world's mode's hit volumes for <paramref name="body"/>; null without a mode (a body of no entity included).</summary>
    public static ScHitCapsule[] HitCapsules(ComponentBody body) => Of(body?.Entity?.Project)?.HitCapsules(body);
    public static IScPenetration Penetration(ComponentPlayer shooter) => For(shooter)?.Penetration(shooter);
    /// <summary>The engine's friendly-fire answer for one of this mod's attacks under the world's mode: true = stopped.</summary>
    public static bool? Stops(Attackment attack) => MayHurt(attack?.Attacker, attack?.Target) switch { true => false, false => true, _ => null };
}
