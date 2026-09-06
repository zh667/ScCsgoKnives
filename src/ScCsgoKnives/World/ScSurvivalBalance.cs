using Engine;
using System.Runtime.CompilerServices;
namespace Game;

public static class ScSurvivalBalance {
    sealed class Control { public double Next; }
    static readonly ConditionalWeakTable<ComponentBody, Control> Controls = new();
    public static float Power(string gun) => gun switch {
        "deagle" => 14, "revolver" => 18,
        "mac10" or "mp9" or "mp7" or "ump45" or "mp5sd" or "p90" or "bizon" => 6,
        "galilar" or "famas" => 9,
        "ak47" or "m4a4" or "m4a1s" or "aug" or "sg556" => 10,
        "m249" or "negev" => 8,
        "ssg08" => 26, "awp" => 38, "scar20" or "g3sg1" or "taser" => 18,
        "nova" => 22, "xm1014" => 16, "sawedoff" or "mag7" => 24,
        "glock18" or "hkp2000" or "p250" or "usp_silencer" or "fiveseven" or "tec9" or "cz75a" or "elite" => 7,
        _ => 0
    };
    public static float Falloff(GunSpec gun, float distance) {
        if (gun.Pellets > 1) return distance <= 5 ? 1 : distance <= 15 ? MathUtils.Lerp(1, .4f, (distance - 5) / 10) : MathUtils.Lerp(.4f, .15f, Math.Clamp((distance - 15) / 10, 0, 1));
        bool sniper = gun.Name is "ssg08" or "awp" or "scar20" or "g3sg1";
        bool rifle = gun.Name is "ak47" or "m4a4" or "m4a1s" or "aug" or "sg556" or "galilar" or "famas" or "m249" or "negev";
        float start = sniper ? 40 : rifle ? 20 : 10, end = sniper || rifle ? 64 : 30, floor = sniper ? .9f : rifle ? .75f : .6f;
        return MathUtils.Lerp(1, floor, Math.Clamp((distance - start) / (end - start), 0, 1));
    }
    public static float PelletPower(GunSpec gun, float distance) => Power(gun.Name) * Falloff(gun, distance) / Math.Max(1, gun.Pellets);
    public static void Attack(ComponentBody body, ComponentPlayer player, Vector3 point, Vector3 direction, float power, double now, bool melee = false, bool zeus = false) {
        ComponentHealth health = body.Entity.FindComponent<ComponentHealth>();
        float before = health?.Health ?? 0;
        int weapon = player.ComponentMiner.ActiveBlockValue;
        Attackment attack = melee ? new MeleeAttackment(body, player.Entity, point, direction, power)
            : new ProjectileAttackment(body, player.Entity, point, direction, power, null);
        Control control = Controls.GetOrCreateValue(body);
        bool eligible = now >= control.Next;
        attack.ImpulseFactor = eligible ? (melee ? 1.5f : .6f) : 0;
        attack.StunTimeSet = eligible ? (zeus ? (body.Mass >= 200 ? .3f : 1f) : .1f) : 0;
        attack.StunTimeAdd = 0;
        attack.AllowImpulseAndStunWhenDamageIsZero = false;
        if (eligible) control.Next = now + (zeus ? 5 : .8);
        ComponentMiner.AttackBody(attack);
        int outcome = ScCombatFeedback.Outcome(before, health?.Health ?? before);
        if (outcome > 0) player.Project.FindSubsystem<SubsystemScGunBlockBehavior>(false)?.ReportHit(player, body, weapon, point, outcome, now);
    }
}
