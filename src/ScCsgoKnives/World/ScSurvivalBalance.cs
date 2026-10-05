using Engine;
using System.Runtime.CompilerServices;
namespace Game;

public static class ScSurvivalBalance {
    // Suppress only bullet control on players at the native effect stage, after mod hooks.
    // Damage/armor still use the ordinary projectile pipeline; existing stun is not cleared.
    // The bullet itself (ScBulletProjectile) rides along for mods that read the shot from ProjectileAttackment.Projectile.
    // Both constructor shapes stay: fixtures find the five-parameter one by reflection.
    public class BulletAttack(ComponentBody body,GameEntitySystem.Entity owner,Vector3 point,Vector3 direction,float power,Projectile bullet)
        : ProjectileAttackment(body,owner,point,direction,power,bullet), IScAttackFacts {
        public BulletAttack(ComponentBody body,GameEntitySystem.Entity owner,Vector3 point,Vector3 direction,float power):this(body,owner,point,direction,power,null){}
        /// <summary>The shot's regions (H1), when the shooter resolved them; null for older callers (whole body).</summary>
        public ScShotHits Hits { get; init; }
        /// <summary>What the authority froze about the shot (deathmatch-addon); null for older callers.</summary>
        public ScAttackFacts Facts { get; init; }
        /// <summary>A world's mode decides who may hurt whom there; without one the world's friendly-fire setting does.</summary>
        public override bool DisableFriendlyFire() => ScModes.Stops(this) ?? base.DisableFriendlyFire();
        /// <summary>What CS body protection absorbed from this attack (H4 sounds); set when the engine applies the injury.</summary>
        public ScHitSounds.Absorbed Absorbed { get; set; }
        /// <summary>The injury this attack computed, or null while nothing asked for it (ScAttackJournal).</summary>
        public float? ComputedInjury => m_injuryAmount;
        /// <summary>current-direction-20260929: a shot with known regions meets the target's CS protection region by region
        /// (head protection on the head, body protection on the torso and arms, legs none; a shot without regions counts as
        /// a body hit); then the native pass runs once on what is left, exactly as for any other attack: vanilla or another
        /// mod's clothing, the resilience factor and attack resilience. The protection is only planned here and committed
        /// when the engine really applies the injury (SubsystemScArmor), so invulnerability or a cancelled injury wears
        /// nothing. Computed once and kept: the native getter does not cache for creatures.</summary>
        public override float CalculateInjuryAmount() {
            if (m_injuryAmount.HasValue) return m_injuryAmount.Value;
            // A mode that settles this target's injuries itself gets the attack's own power: no CS protection is planned or
            // worn, no clothing or resilience is applied here (it would be applied twice, or to the wrong rules).
            if (ScModes.OwnsInjuries(Target)) { m_injuryAmount = AttackPower; return AttackPower; }
            if (!(AttackPower > 0)) { float plain = base.CalculateInjuryAmount(); m_injuryAmount = plain; return plain; }
            float total = Hits?.Total ?? 0, scale = total > 0 ? AttackPower / total : 0;
            var regions = Hits is null ? [(ScHitPart.Body, AttackPower)] : Hits.Regions().Select(r => (r.Part, r.Power * scale)).ToArray();
            string key = EnableArmorProtection ? SubsystemScArmor.KeyOf(Target) : null;
            var armor = key is null ? null : Target.Project?.FindSubsystem<SubsystemScArmor>(false);
            float power = armor?.Plan(this, key, ScArmorChannel.Bullet, regions) ?? regions.Sum(r => r.Item2);
            float saved = AttackPower, result;
            AttackPower = power;
            try { result = base.CalculateInjuryAmount(); } finally { AttackPower = saved; }
            m_injuryAmount = result;
            return result;
        }
        protected virtual bool SuppressPlayerControl=>Target.FindComponent<ComponentPlayer>() is not null;
        public override void ImpulseTarget(){if(!SuppressPlayerControl)base.ImpulseTarget();}
        public override void StunTarget(){if(!SuppressPlayerControl)base.StunTarget();}
    }
    // An explicit shot origin prevents arrows or unrelated mod explosions from
    // triggering the chicken's gun-only blast.
    public class GunAttack(ComponentBody body,GameEntitySystem.Entity owner,Vector3 point,Vector3 direction,float power,Projectile bullet)
        : BulletAttack(body,owner,point,direction,power,bullet) {
        public GunAttack(ComponentBody body,GameEntitySystem.Entity owner,Vector3 point,Vector3 direction,float power):this(body,owner,point,direction,power,null){}
    }
    // Defer electric control until injury is confirmed; preserve the public stun parameter
    // so another mod can explicitly deny control without forcing us to enable knockback.
    sealed class ElectricAttack(ComponentBody body,GameEntitySystem.Entity owner,Vector3 point,Vector3 direction,float power,Projectile bullet)
        : GunAttack(body,owner,point,direction,power,bullet) {
        protected override bool SuppressPlayerControl=>false;
        public override void StunTarget() { }
    }
    /// <summary>The knife's strike: the engine's melee attack, with what the authority froze about it and the world's mode
    /// asked who may be hurt (deathmatch-addon). Without a mode it is the melee attack it always was.</summary>
    public sealed class KnifeAttack(ComponentBody body,GameEntitySystem.Entity owner,Vector3 point,Vector3 direction,float power)
        : MeleeAttackment(body,owner,point,direction,power), IScAttackFacts {
        public ScAttackFacts Facts { get; init; }
        public override bool DisableFriendlyFire() => ScModes.Stops(this) ?? base.DisableFriendlyFire();
        public override float CalculateInjuryAmount() => ScModes.OwnsInjuries(Target) ? AttackPower : base.CalculateInjuryAmount();
    }
    sealed class Control { public double Next; }
    static readonly ConditionalWeakTable<ComponentBody, Control> Controls = new();
    /// <summary>F13 (community plan 2026-09-07): every gun's survival power is 1.5 × the 0.28.x table.
    /// Applied exactly once, here; distance falloff, pellet split and any later headshot work build on the result.</summary>
    public const float GunPowerMultiplier = 1.5f;
    public static float Power(string gun) => BasePower(gun) * GunPowerMultiplier;
    public static float PowerAtLevel(string gun,int level) {
        int l=ScGunGrowth.Clamp(level);
        float bonus=gun switch { "nova"=>21, "xm1014"=>12, "sawedoff"=>24, "mag7"=>18, _=>0 };
        return (Power(gun)+bonus*Math.Max(0,1-l/20f))*ScGunGrowth.DamageMultiplier(l);
    }
    /// <summary>0.28.x per-gun table, kept unscaled so the multiplier is the only place the 1.5 lives.</summary>
    public static float BasePower(string gun) => gun switch {
        "deagle" => 14, "revolver" => 18,
        "mac10" or "mp9" or "mp7" or "ump45" or "mp5sd" or "p90" or "bizon" => 6,
        "galilar" or "famas" => 9,
        "ak47" or "m4a4" or "m4a1s" or "aug" or "sg556" => 10,
        "m249" or "negev" => 8,
        "ssg08" => 26, "awp" => 38, "scar20" or "g3sg1" => 16,
        // Zeus x27 (user-directed 2026-09-08): a high-cost, single-charge, close-range burst weapon. 100 × 1.5 = 150
        // at Lv0, 300 at Lv10 under the current +100% damage rule. Headshot multiplier remains 1.
        "taser" => 100,
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
    public static void Attack(ComponentBody body, ComponentPlayer player, Vector3 point, Vector3 direction, float power, double now, bool melee = false, bool zeus = false, bool headshot = false, ScGunKillCredit credit = null, ScShotHits hits = null)
        => AttackWith(body, player, point, direction, power, now, melee, zeus, headshot, credit, hits, null);
    /// <summary><see cref="Attack"/> with the attack's frozen facts (a distinct name: regressions find Attack by name).</summary>
    public static void AttackWith(ComponentBody body, ComponentPlayer player, Vector3 point, Vector3 direction, float power, double now, bool melee, bool zeus, bool headshot, ScGunKillCredit credit, ScShotHits hits, ScAttackFacts facts) {
        ComponentHealth health = body.Entity.FindComponent<ComponentHealth>();
        float before = health?.Health ?? 0;
        // The kill panel names the gun that fired, taken from the shot's credential; the item in the hand now can
        // already be a different one. With no credential (a knife, creative, a gun with no counter) it is the held item.
        int weapon = credit is not null
            ? Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScGunBlock>(true), 0, GunSpec.WithId(credit.Variant, GunSpec.FreshFull))
            : player.ComponentMiner.ActiveBlockValue;
        // The native attack carries the bullet (post-mp-bugs-20260930 item 3): a mod reading the shot from there sees its
        // owner, its round, its speed and where it struck. The knife stays a melee attack.
        Projectile bullet = melee ? null : ScBulletProjectile.For(player.Entity, ScBulletProjectile.RoundOf(weapon), point, direction, power, zeus, now);
        Attackment attack = melee ? new KnifeAttack(body, player.Entity, point, direction, power){Facts=facts}
            : zeus ? new ElectricAttack(body,player.Entity,point,direction,power,bullet){Hits=hits,Facts=facts}
            : new GunAttack(body, player.Entity, point, direction, power,bullet){Hits=hits,Facts=facts};
        Control control = Controls.GetOrCreateValue(body);
        bool eligible = zeus || now >= control.Next;
        attack.ImpulseFactor = eligible ? (melee ? 1.5f : .6f) : 0;
        attack.StunTimeSet = zeus?ScElectricStun.Duration:eligible ? .1f : 0;
        attack.StunTimeAdd = 0;
        attack.AllowImpulseAndStunWhenDamageIsZero = false;
        if (eligible && !zeus) control.Next = now + .8;
        if (!melee) ScProjectileDefense.Apply(body, attack);
        ScDamageIndicator.AttackBody(attack);
        if(zeus)ScElectricStun.ApplyAttack(body,before,health?.Health??before,now,attack.StunTimeSet??0);
        int outcome = ScCombatFeedback.Outcome(before, health?.Health ?? before);
        // A mode that settles injuries itself ends a life without the engine's health reaching zero: it says so on the facts.
        if (facts?.Lethal == true) outcome = 2;
        // A head pellet that confirmed damage without a kill reports 3 (yellow); a kill stays 2 whatever was hit.
        if (outcome == 1 && headshot) outcome = ScCombatFeedback.HeadshotOutcome;
        // The kill belongs to the credential captured when the shot was fired, never to whatever is in the hand now.
        if (outcome > 0) player.Project.FindSubsystem<SubsystemScGunBlockBehavior>(false)?.ReportHit(player, body, weapon, point, outcome, now, credit);
    }
}
