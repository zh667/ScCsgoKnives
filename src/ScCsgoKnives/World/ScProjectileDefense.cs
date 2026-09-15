using System.Reflection;
namespace Game;

/// <summary>Opt-in compatible component contract of the audited Subnautica 1.2.2.
/// Reuses the target's clock so real projectiles and CS hits share one damage interval.
/// Does not manufacture a Projectile or broadcast collision hooks (which may cause secondary damage).</summary>
public static class ScProjectileDefense {
    public static bool Enabled = true;
    public static void Apply(ComponentBody body, Attackment attack) {
        if (!Enabled || body?.Entity is null) return;
        string name = body.Entity.ValuesDictionary?.DatabaseObject?.Name;
        if (name is not ("BlueWhale" or "GiantTurtle" or "Kraken")) return;
        var health = body.Entity.Components.FirstOrDefault(c => c.GetType().FullName == "Subnautica.ComponentHealthExt");
        if (health is null) return; // same template name in another mod is not a compatibility contract
        var canHit = health.GetType().GetMethod("CanProjectileAttack", Type.EmptyTypes);
        if (canHit?.ReturnType != typeof(bool)) {
            KnifeDiagnostics.WarnOnce("sea-defense-contract", "[GUN_DEFENSE] Subnautica component contract differs; adapter not applied.");
            return;
        }
        try {
            if (name == "GiantTurtle") {
                var immunity = body.Entity.Components.FirstOrDefault(c => c.GetType().FullName == "Subnautica.ComponentImmunity");
                var block = immunity?.GetType().GetMethod("ShouldBlockProjectile", new[] { typeof(bool).MakeByRefType() });
                if (block is not null && block.Invoke(immunity, new object[] { false }) is true) {
                    attack.AttackPower = 0; attack.ImpulseFactor = 0; return;
                }
            }
            bool allowed = (bool)canHit.Invoke(health, null);
            attack.AttackPower = Scale(name, attack.AttackPower, allowed);
            attack.ImpulseFactor = 0;
            KnifeDiagnostics.WarnOnce("sea-defense-active-" + name,
                "[GUN_DEFENSE] " + name + ": shared projectile interval + reduction active; generic Injury caps left to original mod. Hitscan treated as instantaneous (BlueWhale speed gate >=40).");
        } catch (Exception e) {
            // A failed interval callback must not accidentally hand a boss an unrestricted damage path.
            attack.AttackPower = 0;
            KnifeDiagnostics.WarnOnce("sea-defense-failed-" + name, "[GUN_DEFENSE] hit deferred after adapter exception: " + e.GetBaseException().Message);
        }
    }
    public static float Scale(string name, float power, bool allowed) => !allowed ? 0 : name switch {
        "GiantTurtle" => power * .5f, "Kraken" => power < 8 ? 1 : power * .8f, "BlueWhale" => power * .8f, _ => power
    };
}
