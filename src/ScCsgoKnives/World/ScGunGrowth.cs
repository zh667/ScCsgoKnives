namespace Game;

/// <summary>Thirty-level gun growth: 100 valid kills a level, 3000 for the
/// last one, no separate experience system. Every number is derived from the gun's *base* value and the applied
/// level, never from an already-grown value, so re-applying a level can never compound.
///
/// The rules version is stored per gun. It exists so a future rebalance can convert old guns explicitly
/// instead of silently re-scaling their saved state on every load.</summary>
public static class ScGunGrowth {
    public const int MaxLevel = 30;
    public const int PrecisionLevel = 10;
    public const int KillsPerLevel = 100;
    /// <summary>The parameter set below. Stored per gun as GrowthRulesVersion; unrelated to the mod version.</summary>
    public const int RulesVersion = 2;
    /// <summary>PendingGrowthLevel when nothing is waiting. Level 0 is a real level, so the sentinel is -1.</summary>
    public const int NoPending = -1;

    /// <summary>Cumulative kills needed for a level: 100, 200 … 3000.</summary>
    public static long KillsFor(int level) => (long)Math.Clamp(level, 0, MaxLevel) * KillsPerLevel;
    /// <summary>The level this many valid kills unlocks. Counting continues past 3000; the level stops at 30.</summary>
    public static int LevelFor(long kills) => kills <= 0 ? 0 : (int)Math.Min(MaxLevel, kills / KillsPerLevel);
    /// <summary>Kills still needed for the next level, or 0 at the cap.</summary>
    public static long ToNextLevel(long kills) {
        int level = LevelFor(kills);
        return level >= MaxLevel ? 0 : KillsFor(level + 1) - Math.Max(0, kills);
    }
    public static int Clamp(int level) => Math.Clamp(level, 0, MaxLevel);
    public static bool IsTaser(int variant) => variant >= 0 && variant < GunSpec.All.Length && GunSpec.All[variant].RechargeSeconds > 0f;

    static int First(int level) => Math.Min(Clamp(level),10);
    static int Second(int level) => Math.Clamp(Clamp(level)-10,0,10);
    static int Third(int level) => Math.Clamp(Clamp(level)-20,0,10);
    /// <summary>All gains are additive against base, not compounded: Lv10=2x, Lv20=5x, Lv30=10x.</summary>
    public static float DamageMultiplier(int level) => 1f + .10f * First(level) + .30f * Second(level) + .50f * Third(level);
    /// <summary>Lv0-10 unchanged; each following level adds 10% / 15% of base shots per second.</summary>
    public static float FireRateMultiplier(int level) => 1f + .10f * Second(level) + .15f * Third(level);
    public static float ShotInterval(float baseSeconds,int level) => baseSeconds > 0 ? baseSeconds / FireRateMultiplier(level) : 0;
    public static float SkinDamageMultiplier(int variant, int skinId) => ScGunSkinCatalog.Fits(ScGunSkinCatalog.Find(skinId), variant) ? 1.5f : 1f;

    /// <summary>Magazine: C = C0 + floor(C0 × L / 20); the Zeus stays at one charge. Lv10 always gains at
    /// least one round when the base holds more than one.</summary>
    public static int Capacity(int variant, int level) {
        if (variant < 0 || variant >= GunSpec.All.Length) return 0;
        int baseCapacity = GunSpec.All[variant].Magazine;
        if (IsTaser(variant)) return baseCapacity;
        int L = Clamp(level);
        // Integer twentieths avoid floor drift at exact capacity boundaries: 1.5x / 3.5x / 6.5x.
        int twentieths = First(L) + 4 * Second(L) + 6 * Third(L);
        int grown = baseCapacity + (int)((long)baseCapacity * twentieths / 20);
        if (L >= PrecisionLevel && baseCapacity > 1) grown = Math.Max(grown, baseCapacity + 1);
        return grown;
    }
    public static int Capacity(GunSpec spec, int level) => Capacity(Array.IndexOf(GunSpec.All, spec), level);

    /// <summary>Maximum durability: M = roundHalfUp(M0 × (1 + 0.05L)), M0 being the model's class life.</summary>
    public static int MaxDurability(int variant, int level) {
        int baseLife = ScGunDurability.Full(variant);
        return RoundHalfUp(baseLife * (1.0 + .05 * Clamp(level)));
    }
    public static int RoundHalfUp(double value) => (int)Math.Floor(value + .5);

    /// <summary>Charge cycle: 10 -> 5 -> 2.5 -> 1 seconds, preserves the existing first ten levels.</summary>
    public static float RechargeSeconds(GunSpec spec, int level) =>
        spec is null || spec.RechargeSeconds <= 0 ? 0 : spec.RechargeSeconds * (float)(1.0 - .05 * First(level) - .025 * Second(level) - .015 * Third(level));

    /// <summary>Spread and camera recoil are scaled once, on the final angle: 1 − 0.10L, and exactly zero at Lv10.</summary>
    public static float AngleScale(int level) { int L = Clamp(level); return L >= PrecisionLevel ? 0f : 1f - .10f * L; }

    /// <summary>A normal bullet gun at Lv10 has no weapon range limit of its own (continuously loaded world only).
    /// The Zeus keeps its planned close-range exception and grows 5 % a level instead.</summary>
    public static bool UnlimitedRange(int variant, int level) => Clamp(level) >= PrecisionLevel && !IsTaser(variant);
    public static float RangeScale(int variant, int level) {
        int L = Clamp(level);
        return IsTaser(variant) ? 1f + .05f * First(L) + .15f * Second(L) + .20f * Third(L) : 1f + .25f * Math.Min(L,PrecisionLevel);
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
        [ScWeaponMaterialBlock.Value(ScWeaponMaterialBlock.Blank)] = 1,
        [ScWeaponMaterialBlock.Value(ScWeaponMaterialBlock.Mechanism)] = 1,
        [Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<GlassBlock>(true))] = 1,
        [Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<GermaniumChunkBlock>(true))] = 1,
    };
}

/// <summary>What a world does with kill counters. Chosen once, before the first counter is installed, and never
/// switched while playing: switching would either take levels back or hand them out retroactively.</summary>
public enum ScGunGrowthMode { Unset, CountOnly, CountAndGrow }
