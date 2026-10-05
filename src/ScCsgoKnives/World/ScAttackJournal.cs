using System.Globalization;
namespace Game;

/// <summary>Test diagnostics for third-party targets that do not take CS damage (post-mp-bugs-20260930 §3): every CS attack
/// delivered through <see cref="ScDamageIndicator.Deliver"/> (player guns and knife, companions, enemy squads) with the
/// target's template, the attack type and its Projectile context, the power submitted and the power after the engine's and
/// other mods' ProcessAttackment hooks, the injury the attack itself computed and the health actually lost. An injury that was
/// never computed means ProcessAttackment stopped early; the engine catches such exceptions and logs "Attack execute error"
/// (ComponentMiner.AttackBody). Off unless a test switches it on; changes nothing about the attack.</summary>
public static class ScAttackJournal {
    public static bool Record;
    public const int Limit = 400;
    static readonly Queue<string> s_entries = new();
    public static string Text => string.Join("\n", s_entries);
    public static void Clear() => s_entries.Clear();

    internal readonly record struct Before(string Target, float Health, float Power);
    internal static Before Begin(Attackment attack) => new(
        attack?.Target?.ValuesDictionary?.DatabaseObject?.Name ?? "?", Health(attack), attack?.AttackPower ?? 0);

    internal static void End(Attackment attack, Before before) {
        if (attack is null) return;
        float? injury = attack is ScSurvivalBalance.BulletAttack bullet ? bullet.ComputedInjury : null;
        float after = Health(attack);
        string entry = string.Create(CultureInfo.InvariantCulture,
            $"{Engine.Time.RealTime:0.000} {attack.GetType().Name} by {attack.Attacker?.ValuesDictionary?.DatabaseObject?.Name ?? "?"} on {before.Target} "
            + $"projectile {Describe((attack as ProjectileAttackment)?.Projectile)} power {before.Power:0.###} -> {attack.AttackPower:0.###} "
            + $"injury {(injury is float i ? i.ToString("0.###", CultureInfo.InvariantCulture) : attack is ScSurvivalBalance.BulletAttack ? "not computed" : "n/a")} "
            + $"health {before.Health:0.####} -> {after:0.####}");
        if (s_entries.Count >= Limit) s_entries.Dequeue();
        s_entries.Enqueue(entry);
    }

    static string Describe(Projectile p) => p is null ? "null"
        : string.Create(CultureInfo.InvariantCulture, $"{p.GetType().Name}(value {p.Value} speed {p.Velocity.Length():0.#} owner {p.OwnerEntity?.Id ?? -1} project {(p.Project is null ? "null" : "set")}{(p is ScBulletProjectile { Electric: true } ? " electric" : "")})");
    static float Health(Attackment attack) =>
        attack?.Target?.FindComponent<ComponentHealth>()?.Health ?? attack?.Target?.FindComponent<ComponentDamage>()?.Hitpoints ?? float.NaN;
}
