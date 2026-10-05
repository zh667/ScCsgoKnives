namespace Game;

/// <summary>One settled hit: what the life had, what it has now.</summary>
public readonly record struct DmSettlement(int Health, int Armour, int HealthLost, int ArmourLost, bool Lethal);

/// <summary>The competitive health and armour rule (DM-08, C2): 100 health, 100 armour and a helmet, settled once here and
/// nowhere else. The armour arithmetic is Counter-Strike's as published in the CS:GO SDK lineage
/// (CCSPlayer::OnTakeDamage: half of the weapon's armour ratio reaches health, half of the rest wears the armour, a
/// worn-out armour lets the remainder through, health loses whole points); the per-weapon numbers come from this
/// machine's CS2 weapons.vdata through the core's Cs2Weapons table. That CS2's code still does exactly this was NOT
/// measured in CS2 here: the evidence table records it as such.</summary>
public static class DmCombat {
    public const float ArmourBonus = .5f, ArmourRatioBase = .5f;
    public const float StomachMultiplier = 1.25f, LegMultiplier = .75f;
    /// <summary>CS2's hit groups (round 5: the capsules report them, DmHitboxes): head by the weapon's own multiplier, neck,
    /// chest and arms 1, stomach 1.25, legs 0.75. Measured in CS2 1.41.8.8 (2026-10-04, AK-47, no armour, 174 units: chest
    /// and neck 35, stomach 44; at 1012 units head 138, leg 25 - each the vdata damage after its range modifier).</summary>
    public static float RegionMultiplier(ScHitPart part, float headMultiplier) => part switch {
        ScHitPart.Head => headMultiplier, ScHitPart.Stomach => StomachMultiplier, ScHitPart.Leg => LegMultiplier, _ => 1f };
    /// <summary>Whether armour covers the region: the helmet the head, the vest chest, stomach and arms (measured on the
    /// chest: 26 health and 3 armour from an AK-47 at 1012 units), nothing the legs (measured: 25, no armour lost). The
    /// neck is counted with the vest - NOT measured.</summary>
    public static bool Covered(ScHitPart part, bool helmet) => part switch { ScHitPart.Head => helmet, ScHitPart.Leg => false, _ => true };

    /// <summary>One hit of <paramref name="damage"/> (after distance and region) on a life with this health and armour.</summary>
    public static DmSettlement Settle(int health, int armour, float damage, float weaponArmourRatio, bool covered) {
        if (!(damage > 0) || health <= 0) return new(health, armour, 0, 0, false);
        float toHealth = damage; int armourLost = 0;
        if (covered && armour > 0) {
            float ratio = ArmourRatioBase * weaponArmourRatio;
            float passed = damage * ratio;
            float worn = (damage - passed) * ArmourBonus;
            if (worn > armour) { worn = armour * (1 / ArmourBonus); passed = damage - worn; armourLost = armour; }
            else { if (worn < 0) worn = 1; armourLost = Math.Min(armour, (int)worn); }
            toHealth = passed;
        }
        int lost = Math.Min(health, Math.Max(0, (int)toHealth));
        return new(health - lost, armour - armourLost, lost, armourLost, health - lost <= 0);
    }
    /// <summary>A shot whose pellets landed on several regions (a shotgun): each region's share is settled in turn on what the
    /// one before left, head first, so the armour is worn once and the order never depends on pellet numbering.</summary>
    public static DmSettlement SettleRegions(int health, int armour, bool helmet, IEnumerable<(ScHitPart Part, float Damage)> regions, float headMultiplier, float weaponArmourRatio, out ScHitPart lethalPart) {
        int h = health, a = armour, healthLost = 0, armourLost = 0; lethalPart = ScHitPart.Unknown;
        foreach (var (part, damage) in regions.OrderBy(r => r.Part switch { ScHitPart.Head => 0, ScHitPart.Neck => 1, ScHitPart.Body => 2, ScHitPart.Stomach => 3, ScHitPart.Arm => 4, _ => 5 })) {
            var s = Settle(h, a, damage * RegionMultiplier(part, headMultiplier), weaponArmourRatio, Covered(part, helmet));
            healthLost += s.HealthLost; armourLost += s.ArmourLost; h = s.Health; a = s.Armour;
            if (s.Lethal) { lethalPart = part; break; }
        }
        return new(h, a, healthLost, armourLost, h <= 0);
    }
    /// <summary>Damage left at a distance in blocks: damage × modifier^(units / 500), 1 block = 1 m (the core's table carries
    /// the units per metre it was made with).</summary>
    public static float AtDistance(float damage, float rangeModifier, float blocks, float unitsPerMetre, float falloffUnits) =>
        rangeModifier is > 0 and < 1 && falloffUnits > 0 ? damage * MathF.Pow(rangeModifier, Math.Max(0, blocks) * unitsPerMetre / falloffUnits) : damage;
}
