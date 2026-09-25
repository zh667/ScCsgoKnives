namespace Game;

/// <summary>Fifty-level gun growth. The total growth is spread across all fifty levels so that reaching Lv50
/// reproduces exactly what the published public beta reached at Lv30: damage x10, magazine x6.5, maximum life x2.5,
/// with the same charge growth ratio. Lv0 bullet cadence matches the extracted CS2 data.
/// Only level bonuses are reduced: ordinary guns x1.325, auto-snipers x1.2, bolt actions x2.625 at Lv50.
///
/// Lv10 retains damage x2, life x1.5 and capacity x1.5. Ordinary fire rate is x1.065.
///
/// Every number is derived from the gun's *base* value and the applied level, never from an already-grown value,
/// so re-applying a level can never compound.
///
/// The rules version is stored per gun. It exists so a rebalance can convert old guns explicitly instead of
/// silently re-scaling their saved state on every load.</summary>
public static class ScGunGrowth {
    public const int MaxLevel = 50;
    public const int PrecisionLevel = 10;
    public const int KillsPerLevel = 25;
    /// <summary>The parameter set below. Stored per gun as GrowthRulesVersion; unrelated to the mod version.
    /// 7 lowers the Lv31–Lv50 kill thresholds. Runtime-only fire-rate balance does not convert saved fields.</summary>
    public const int RulesVersion = 7;
    /// <summary>PendingGrowthLevel when nothing is waiting. Level 0 is a real level, so the sentinel is -1.</summary>
    public const int NoPending = -1;

    /// <summary>Cumulative kills: Lv10=250, Lv20=750, Lv30=1750, Lv40=3250, Lv50=5250.
    /// Per level: 1–10 ×25, 11–20 ×50, 21–30 ×100, 31–40 ×150, 41–50 ×200.</summary>
    public static long KillsFor(int level) {
        int l = Clamp(level);
        return 25L * Math.Min(l, 10) + 50L * Math.Min(Math.Max(l - 10, 0), 10)
            + 100L * Math.Min(Math.Max(l - 20, 0), 10) + 150L * Math.Min(Math.Max(l - 30, 0), 10)
            + 200L * Math.Max(l - 40, 0);
    }
    /// <summary>The per-weapon kill-requirement multiplier. A scoped sniper needs 40 % fewer kills, the Zeus 70 %
    /// fewer, and a machine gun 50 % more.</summary>
    public static float Difficulty(int variant) {
        if (IsTaser(variant)) return .30f;
        if (IsSniper(variant)) return .60f;
        string n = variant >= 0 && variant < GunSpec.All.Length ? GunSpec.All[variant].Name : "";
        if (n is "m249" or "negev") return 1.50f;
        return 1f;
    }
    public static bool IsSniper(int variant) {
        string n = variant >= 0 && variant < GunSpec.All.Length ? GunSpec.All[variant].Name : "";
        return n is "ssg08" or "awp" or "scar20" or "g3sg1";
    }
    /// <summary>Exact rational scaling (3/10, 3/5, 3/2) so a float rounding never adds or drops a kill.</summary>
    public static long KillsFor(int variant, int level) {
        long kills = KillsFor(level);
        if (IsTaser(variant)) return (kills * 3 + 9) / 10;
        if (IsSniper(variant)) return (kills * 3 + 4) / 5;
        string n = variant >= 0 && variant < GunSpec.All.Length ? GunSpec.All[variant].Name : "";
        return n is "m249" or "negev" ? (kills * 3 + 1) / 2 : kills;
    }
    /// <summary>The level this many valid kills unlocks. Counting continues past the cap; the level stops at 50.</summary>
    public static int LevelFor(long kills) {
        long k = Math.Max(0, kills); int level = 0;
        while (level < MaxLevel && k >= KillsFor(level + 1)) level++;
        return level;
    }
    public static int LevelFor(int variant, long kills) {
        long k = Math.Max(0, kills); int level = 0;
        while (level < MaxLevel && k >= KillsFor(variant, level + 1)) level++;
        return level;
    }
    public static long ProgressKills(long kills, long credit) => kills > long.MaxValue - credit ? long.MaxValue : kills + credit;
    /// <summary>Kills still needed for the next level, or 0 at the cap.</summary>
    public static long ToNextLevel(long kills) => ToNextLevel(-1, kills);
    public static long ToNextLevel(int variant, long kills) {
        int level = variant < 0 ? LevelFor(kills) : LevelFor(variant, kills);
        return level >= MaxLevel ? 0 : KillsFor(variant, level + 1) - Math.Max(0, kills);
    }
    public static int Clamp(int level) => Math.Clamp(level, 0, MaxLevel);
    public static bool IsTaser(int variant) => variant >= 0 && variant < GunSpec.All.Length && GunSpec.All[variant].RechargeSeconds > 0f;

    /// <summary>The ten levels of a growth tier, capped so calls above Lv50 stay flat.</summary>
    static int Tier(int level, int index) => Math.Clamp(Clamp(level) - index * 10, 0, 10);
    /// <summary>All gains are additive against base, not compounded. Lv10 x2 (unchanged), and the remaining
    /// growth spread over Lv11-Lv50 so Lv50 = x10, the public beta's Lv30.</summary>
    public static float DamageMultiplier(int level) =>
        1f + .10f * Tier(level, 0) + .15f * Tier(level, 1) + .20f * Tier(level, 2) + .20f * Tier(level, 3) + .25f * Tier(level, 4);
    /// <summary>Bolt actions retain their tier shape, with 35% less bonus, never a slower Lv0.</summary>
    public static bool IsBoltSniper(int variant) => variant >= 0 && variant < GunSpec.All.Length
        && GunSpec.All[variant].Name is "awp" or "ssg08";
    public static float FireRateMultiplier(int variant, int level) => IsBoltSniper(variant)
        ? 1f + .65f * (.05f * Tier(level, 1) + .05f * Tier(level, 2) + .075f * Tier(level, 3) + .075f * Tier(level, 4))
        : 1f + (IsAutoSniper(variant) ? .004f : .0065f) * Clamp(level);
    public static bool IsAutoSniper(int variant) => variant >= 0 && variant < GunSpec.All.Length
        && GunSpec.All[variant].Name is "scar20" or "g3sg1";
    /// <summary>A base fire-rate multiplier when the model is not known is treated as a normal gun.</summary>
    public static float FireRateMultiplier(int level) => FireRateMultiplier(-1, level);
    public const float BaseFireRateScale = 1f;
    public static float BalanceFireRateScale(int variant) => BaseFireRateScale;
    public static float ShotInterval(int variant, float baseSeconds, int level) =>
        baseSeconds > 0 ? baseSeconds / (BalanceFireRateScale(variant) * FireRateMultiplier(variant, Clamp(level))) : 0;
    public static float SkinDamageMultiplier(int variant, int skinId) => ScGunSkinCatalog.Fits(ScGunSkinCatalog.Find(skinId), variant) ? 1.5f : 1f;

    /// <summary>Magazine: Lv10 is unchanged at +50 %, and the remaining growth is spread to Lv50 at x6.5, the
    /// public beta's Lv30. Integer twentieths avoid floor drift at exact capacity boundaries. The Zeus stays one charge.</summary>
    public static int Capacity(int variant, int level) {
        if (variant < 0 || variant >= GunSpec.All.Length) return 0;
        int baseCapacity = GunSpec.All[variant].Magazine;
        if (IsTaser(variant)) return baseCapacity;
        int L = Clamp(level);
        int twentieths = Tier(L, 0) + Tier(L, 1) + 2 * Tier(L, 2) + 3 * Tier(L, 3) + 4 * Tier(L, 4);
        int grown = baseCapacity + (int)((long)baseCapacity * twentieths / 20);
        if (L >= PrecisionLevel && baseCapacity > 1) grown = Math.Max(grown, baseCapacity + 1);
        return grown;
    }
    public static int Capacity(GunSpec spec, int level) => Capacity(Array.IndexOf(GunSpec.All, spec), level);

    /// <summary>Maximum durability: Lv10 is unchanged at +50 %, then spread to Lv50 at x2.5, the public beta's Lv30.</summary>
    public static int MaxDurability(int variant, int level) {
        int baseLife = ScGunDurability.Full(variant);
        float factor = 1f + .05f * Tier(level, 0) + .02f * Tier(level, 1) + .02f * Tier(level, 2) + .03f * Tier(level, 3) + .03f * Tier(level, 4);
        return RoundHalfUp(baseLife * factor);
    }
    public static int RoundHalfUp(double value) => (int)Math.Floor(value + .5);

    /// <summary>Zeus starts at CS2's 30 seconds. Only 65% of the old frequency bonus is retained:
    /// Lv10 18.1818 s, Lv50 4.37956 s. Saved cycles keep their remaining seconds.</summary>
    public static float RechargeSeconds(GunSpec spec, int level) {
        if (spec is null || spec.RechargeSeconds <= 0) return 0;
        float factor = 1f - .05f * Tier(level, 0) - .02f * Tier(level, 1) - .01f * Tier(level, 2) - .005f * Tier(level, 3) - .005f * Tier(level, 4);
        // Only new cycles use this scale. Saved RechargeReadyAt/RechargeCycleSeconds are untouched.
        return spec.RechargeSeconds / (1f + .65f * (1f / factor - 1f));
    }

    /// <summary>Spread and camera recoil are scaled once, on the final angle: 1 - 0.10L, and exactly zero at Lv10.</summary>
    public static float AngleScale(int level) { int L = Clamp(level); return L >= PrecisionLevel ? 0f : 1f - .10f * L; }

    /// <summary>A normal bullet gun at Lv10 has no weapon range limit of its own (continuously loaded world only).
    /// The Zeus keeps its planned close-range exception and grows 8 % a level instead.</summary>
    public static bool UnlimitedRange(int variant, int level) => Clamp(level) >= PrecisionLevel && !IsTaser(variant);
    public static float RangeScale(int variant, int level) {
        int L = Clamp(level);
        // Lv10 keeps the public beta's +50 %; the rest is spread so Lv50 reaches x5, the public beta's Lv30.
        return IsTaser(variant) ? 1f + .05f * Tier(L, 0) + .0875f * Math.Clamp(L - 10, 0, 40) : 1f + .25f * Math.Min(L, PrecisionLevel);
    }
    /// <summary>How far a shot may actually reach at Lv10: the loaded-world budget, a finite number, never
    /// infinity or NaN, so vectors, serialization and the UI stay well defined.</summary>
    // Metadata sentinel only. LoadedLimit resolves it to a real loaded-world boundary
    // before any ray endpoint is constructed.
    public const float LoadedWorldRange = float.MaxValue;
    public static float Range(int variant, int level, float baseRange) {
        if (!float.IsFinite(baseRange) || baseRange <= 0) return 0;
        if (UnlimitedRange(variant, level)) return LoadedWorldRange;
        return baseRange * RangeScale(variant, level);
    }

    /// <summary>Durability keeps its ratio when the ceiling moves: a broken gun stays broken, a full one stays
    /// full, and anything between lands strictly inside the new range.</summary>
    public static int ScaleDurability(int durability, int oldMax, int newMax) {
        if (newMax < 1) return 0;
        if (durability <= 0) return 0;
        if (oldMax < 1 || durability >= oldMax) return newMax;
        int scaled = (int)((long)durability * newMax / oldMax);
        return Math.Clamp(scaled, 1, newMax - 1);
    }
    /// <summary>A charge in progress keeps its remaining fraction when the cycle length changes.</summary>
    public static double ScaleRemaining(double remaining, float oldCycle, float newCycle) {
        if (!double.IsFinite(remaining) || remaining <= 0) return 0;
        if (!(oldCycle > 0) || !(newCycle > 0)) return remaining;
        return Math.Min(newCycle, remaining * newCycle / oldCycle);
    }

    /// <summary>A duplicate item is a new gun, so it does not carry the survival kills or the level they bought
    /// (plan §4.3): otherwise creative-copying one maximum-level weapon would hand out two.
    ///
    /// Everything that is not growth comes across untouched - model, finish, silencer, ammunition, charge - and the
    /// capacity and life the copy loses go through the same conservation rules a level change uses, so surplus live
    /// rounds move to this gun's own reserve and a broken gun stays broken. The gun's state is never simply cleared.</summary>
    public static void StripGrowth(ScGunRecord copy) {
        if (copy is null || !copy.CounterInstalled) return;
        int level = Clamp(copy.AppliedGrowthLevel);
        // The module is equipment, not earned growth. Copies keep it but start at zero kills/Lv0.
        copy.KillCount = 0;
        copy.GrowthKillCredit = 0;
        copy.AppliedGrowthLevel = 0; copy.PendingGrowthLevel = NoPending; copy.GrowthRulesVersion = RulesVersion;
        if (level <= 0) return;
        int newMax = MaxDurability(copy.Variant, 0);
        copy.Durability = ScaleDurability(copy.Durability, copy.MaxDurability, newMax);
        copy.MaxDurability = newMax;
        int capacity = Capacity(copy.Variant, 0);
        if (copy.Rounds > capacity) { copy.ReserveOverflowRounds += copy.Rounds - capacity; copy.Rounds = capacity; }
        // A charge in progress keeps its own absolute ready time; clearing the remembered cycle makes the next
        // level change measure from this copy's real level instead of the original's.
        copy.RechargeCycleSeconds = 0;
    }

    /// <summary>The install cost of a kill counter, and the crafting level it needs (plan §4.1).</summary>
    public const int InstallLevel = 3;
    /// <summary>Headless seam: a host with no registered blocks supplies the item values itself. Never set in game.</summary>
    internal static Func<Dictionary<int, int>> InstallCostOverride;
    public static Dictionary<int, int> InstallCost() => InstallCostOverride?.Invoke() ?? new() {
        [ScWeaponMaterialBlock.Value(ScWeaponMaterialBlock.Mechanism)] = 4,
        [ScWeaponMaterialBlock.Value(ScWeaponMaterialBlock.Optics)] = 1,
        [Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<GermaniumChunkBlock>(true))] = 8,
        [Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<DiamondChunkBlock>(true))] = 2,
    };
}

/// <summary>What a world does with kill counters. Chosen once, before the first counter is installed, and never
/// switched while playing: switching would either take levels back or hand them out retroactively.</summary>
public enum ScGunGrowthMode { Unset, CountOnly, CountAndGrow }
