using Engine;
namespace Game;

/// <summary>1.7.0 survival shotgun rules (feedback-1124-170 D1–D4, P2/P3). The player's four shotguns reach 1.5 times as
/// far (the whole distance curve stretches: maximum range, full-damage distance and middle node), keep at least 30 % of
/// their damage at the end of that range, and push creatures back at close range. Total damage per trigger pull and the
/// pellet count are unchanged. Enemy T keep the 1.6.0 numbers (Q2): they read <see cref="ScGunHandling.For"/> and
/// <see cref="ScSurvivalBalance.FalloffBase"/> through <see cref="EffectiveGunStats.NpcFalloff"/>. The deathmatch builds
/// its own numbers and is not affected.</summary>
public static class ScShotgunTuning {
    public const float RangeScale = 1.5f, Floor = .3f;
    public const float KnockbackRange = 6f, KnockbackImpulse = 1.5f, KnockbackMassCap = 300f, BulletImpulse = .6f, MeleeImpulse = 1.5f;
    public static bool Applies(GunSpec spec) => spec is not null && spec.Pellets > 1;
    static readonly Dictionary<string, ScGunHandling.Gun> s_tuned = new(StringComparer.Ordinal);
    /// <summary>The handling table row the player's gun uses: the table itself, or the stretched row of a shotgun.</summary>
    public static ScGunHandling.Gun Survival(GunSpec spec) {
        var g = spec is null ? null : ScGunHandling.For(spec.Name);
        if (g is null || !Applies(spec)) return g;
        lock (s_tuned) {
            if (!s_tuned.TryGetValue(spec.Name, out var t)) s_tuned[spec.Name] = t = Stretch(g);
            return t;
        }
    }
    /// <summary>A stretched copy; the source row is never modified. Range rounds to half a block.</summary>
    public static ScGunHandling.Gun Stretch(ScGunHandling.Gun g) => new() {
        Range = MathF.Round(g.Range * RangeScale * 2) / 2, FalloffStart = g.FalloffStart * RangeScale,
        MidDistance = g.MidDistance * RangeScale, MidMultiplier = g.MidMultiplier, FalloffFloor = Math.Max(g.FalloffFloor, Floor),
        Alternate = g.Alternate, Modes = g.Modes
    };
    /// <summary>The weapon's own range before growth when gunplay handling is off (GunSpec.RangeBlocks), stretched for shotguns.</summary>
    public static float BaseRange(GunSpec spec) => Applies(spec) ? spec.RangeBlocks * RangeScale : spec.RangeBlocks;
    /// <summary>The control impulse of one attack (D4/P3): knives 1.5, bullets 0.6, a shotgun within 6 blocks of the shooter's
    /// eyes against a body lighter than 300 also 1.5. Audited bosses lose it later in ScProjectileDefense, as before.</summary>
    public static float Impulse(GunSpec spec, bool melee, bool eligible, float distance, float mass) =>
        !eligible ? 0 : melee ? MeleeImpulse
        : Applies(spec) && float.IsFinite(distance) && distance <= KnockbackRange && float.IsFinite(mass) && mass < KnockbackMassCap ? KnockbackImpulse
        : BulletImpulse;
}
