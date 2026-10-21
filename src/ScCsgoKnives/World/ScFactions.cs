using GameEntitySystem;
namespace Game;

/// <summary>Faction contract for optional packages; the core never references agent types. Appearance (CT/T) is not
/// a faction: a recruited T is an ally and an enemy-squad T is an enemy. Area sources are saved as player indices or
/// the enemy marker, so a fire keeps its side after its thrower leaves or goes offline.</summary>
public static class ScFactions {
    public enum Side { Neutral, Player, Ally, Enemy }
    public const int EnemySource = -2;
    /// <summary>Keyed so a package re-registering on load replaces its resolver. Return null for foreign entities.</summary>
    public static readonly Dictionary<string, Func<Entity, Side?>> Resolvers = new(StringComparer.Ordinal);
    public static Side Of(Entity entity) {
        if (entity is null) return Side.Neutral;
        if (entity.FindComponent<ComponentPlayer>() is not null) return Side.Player;
        foreach (var resolver in Resolvers.Values.ToArray()) if (resolver(entity) is Side side) return side;
        return Side.Neutral;
    }
    /// <summary>Whether an area effect (HE, fire, flash) from <paramref name="owner"/> may reach a target of
    /// <paramref name="side"/>. Players keep self-damage and the world friendly-fire setting; allies are never reached by
    /// a player's area (<see cref="Spares"/>), whatever the setting; enemy areas never reach enemies. Other creatures
    /// are unchanged.</summary>
    public static bool AreaAllowed(int owner, Side side, int targetPlayer, bool friendlyFire) => side switch {
        Side.Player => owner == EnemySource || targetPlayer == owner || friendlyFire,
        Side.Ally => owner < 0,
        Side.Enemy => owner != EnemySource,
        _ => true,
    };
    /// <summary>A recruited ally is never hurt by a player (2026-10-21 player request: "希望增加队友免伤，老是误伤队友"): any
    /// player's attack, whatever the weapon - this mod's, the game's or another mod's - leaves an ally untouched, also
    /// with the world's friendly fire allowed. The enemy side still hurts allies; players among themselves keep the
    /// world's friendly-fire setting. The one rule for allies: this mod's attacks ask it in DisableFriendlyFire, every
    /// other attack is emptied by <see cref="Spare"/> in the ProcessAttackment hooks.</summary>
    public static bool Spares(Entity attacker, Entity target) =>
        attacker?.FindComponent<ComponentPlayer>() is not null && target is not null && Of(target) == Side.Ally;
    /// <summary>For a ProcessAttackment hook: an attack <see cref="Spares"/> covers is emptied the way a shield stops one -
    /// no injury, impulse or stun follows. True when it was.</summary>
    public static bool Spare(Attackment attack) {
        if (attack is null || !(attack.AttackPower > 0) || !Spares(attack.Attacker, attack.Target)) return false;
        attack.AttackPower = 0; attack.m_injuryAmount = null;
        attack.ImpulseFactor = 0; attack.StunTimeSet = 0; attack.StunTimeAdd = 0; attack.AllowImpulseAndStunWhenDamageIsZero = false;
        return true;
    }
}
