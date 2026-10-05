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
    /// <paramref name="side"/>. Players keep self-damage and the world friendly-fire setting; allies are protected
    /// from player areas under the same setting; enemy areas never reach enemies. Other creatures are unchanged.</summary>
    public static bool AreaAllowed(int owner, Side side, int targetPlayer, bool friendlyFire) => side switch {
        Side.Player => owner == EnemySource || targetPlayer == owner || friendlyFire,
        Side.Ally => owner == EnemySource || friendlyFire,
        Side.Enemy => owner != EnemySource,
        _ => true,
    };
}
