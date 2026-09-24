namespace Game;

/// <summary>Keeps a gun's earned power and its displayed kills when the growth rules change.
///
/// The published public beta used thirty levels of one hundred kills (RulesVersion 2): its Lv30 was the strongest
/// a gun could be. RulesVersion 6 spread that same total growth across fifty levels and lowered the normal-gun
/// fire rate. RulesVersion 7 keeps that combat curve and only lowers the Lv31–Lv50 kill thresholds. A record
/// written by an older rule set therefore keeps its *damage multiplier* - the headline power - by being placed
/// at the first new level that reaches it, never below, and may rise further if the saved kills already unlock
/// a higher level under the new table. The displayed <see cref="ScGunRecord.KillCount"/> is left exactly as
/// saved; a separate <see cref="ScGunRecord.GrowthKillCredit"/> covers any gap so the level stays reachable
/// without pretending kills happened.
///
/// The conversion is applied once, at load, and marks the record with the current RulesVersion so it never runs
/// twice. Ammunition, durability, silencer, finish and an in-progress charge are conserved; a version upgrade preserves charge seconds while an ordinary level
/// change preserves the remaining fraction.</summary>
public static class ScGunGrowthMigration {
    public static void Convert(ScGunRecord r, double now, bool grows, int sourceSchema) {
        if (r is null) return;
        if (!r.CounterInstalled) { r.GrowthRulesVersion = ScGunGrowth.RulesVersion; return; }
        // Counting-only worlds must not gain levels or a different durability maximum from kills.
        if (!grows) {r.GrowthRulesVersion=ScGunGrowth.RulesVersion;return;}
        int oldRules = r.GrowthRulesVersion;
        // A record with no stored rules came from a build that had no growth rules at all.
        int oldLevel = Math.Max(r.AppliedGrowthLevel, r.PendingGrowthLevel);
        // Under the new thresholds the saved kills alone may already unlock a level; take the higher of that and
        // the level that reproduces the old damage multiplier.
        int fromKills = ScGunGrowth.LevelFor(r.Variant, r.KillCount);
        int fromPower = NewLevelForOldPower(r.Variant, oldLevel, oldRules, sourceSchema);
        int target = ScGunGrowth.Clamp(Math.Max(fromKills, fromPower));
        ApplyLevel(r, target, now, oldRules);
        long needed = ScGunGrowth.KillsFor(r.Variant, target);
        r.GrowthKillCredit = Math.Max(0, needed - r.KillCount);
        r.GrowthRulesVersion = ScGunGrowth.RulesVersion;
    }

    /// <summary>Legacy damage multiplier for a level, by the rule set that produced it.</summary>
    static double LegacyDamageMultiplier(int level, int rules, int schema) {
        int L = Math.Max(0, level);
        if (rules >= 6)  // fifty-level x10 curve (RulesVersion 6 and 7 share combat numbers)
            return ScGunGrowth.DamageMultiplier(Math.Min(L, ScGunGrowth.MaxLevel));
        if (rules == 5)  // the intermediate fifty-level preview (growth capped at Lv30): x6.5
            return 1 + .10 * Math.Min(L, 10) + .25 * Math.Clamp(L - 10, 0, 10) + .20 * Math.Clamp(L - 20, 0, 10);
        if (rules >= 2 || schema == ScGunRegistry.SchemaThirtyLevels || schema == ScGunRegistry.SchemaStagedKills)
            // the published public beta: thirty levels, x2 / x5 / x10
            return 1 + .10 * Math.Min(L, 10) + .30 * Math.Clamp(L - 10, 0, 10) + .50 * Math.Clamp(L - 20, 0, 10);
        // the ten-level preview: x2 at Lv10
        return 1 + .10 * Math.Min(L, 10);
    }
    /// <summary>The first new level whose damage multiplier reaches the old one (never a downgrade).</summary>
    static int NewLevelForOldPower(int variant, int oldLevel, int oldRules, int schema) {
        if (oldLevel <= 0) return 0;
        // Rules 6 and 7 use the same combat curve; only kill thresholds changed, so the saved level is already correct.
        if (oldRules >= 6) return ScGunGrowth.Clamp(oldLevel);
        double target = LegacyDamageMultiplier(oldLevel, oldRules, schema);
        int level = 0;
        while (level < ScGunGrowth.MaxLevel && ScGunGrowth.DamageMultiplier(level) + 1e-6 < target) level++;
        return level;
    }

    /// <summary>Writes the target level and conserves everything that depends on it. This mirrors
    /// <see cref="ScGunGrowthService.ApplyPending"/> so a migration cannot leak rounds, revoke wear or lose the
    /// remaining fraction of a charge.</summary>
    static void ApplyLevel(ScGunRecord r, int level, double now, int oldRules) {
        var spec = GunSpec.All[r.Variant];
        int oldMax = r.MaxDurability;
        int newMax = ScGunGrowth.MaxDurability(r.Variant, level);
        r.Durability = ScGunGrowth.ScaleDurability(r.Durability, oldMax, newMax);
        r.MaxDurability = newMax;
        int capacity = ScGunGrowth.Capacity(r.Variant, level);
        if (r.Rounds > capacity) { r.ReserveOverflowRounds += r.Rounds - capacity; r.Rounds = capacity; }
        // Upgrading preserves the seconds still owed by an already-running charge, including
        // its saved cycle. Only a subsequent shot starts the newly balanced cycle. This also
        // applies when a thirty-level gun receives its equivalent fifty-level growth level.
        if (r.RechargeReadyAt >= 0 && r.RechargeCycleSeconds <= 0)
            r.RechargeCycleSeconds = LegacyCycle(spec, r.AppliedGrowthLevel, oldRules);
        r.AppliedGrowthLevel = level;
        r.PendingGrowthLevel = ScGunGrowth.NoPending;
    }

    /// <summary>The charge cycle an older rule set used, for a gun whose stored cycle is absent.</summary>
    public static float LegacyCycle(GunSpec spec, int level, int rules) {
        if (spec is null || spec.RechargeSeconds <= 0) return 0;
        if (rules >= 6) {
            int l = ScGunGrowth.Clamp(level);
            return spec.RechargeSeconds * (1f - .05f * Math.Min(l,10) - .02f * Math.Clamp(l-10,0,10)
                - .01f * Math.Clamp(l-20,0,10) - .005f * Math.Clamp(l-30,0,10) - .005f * Math.Clamp(l-40,0,10));
        }
        double factor = rules == 5
            ? 1 - .05 * Math.Min(level, 10) - .02 * Math.Clamp(level - 10, 0, 10) - .01 * Math.Clamp(level - 20, 0, 10)
            : 1 - .05 * Math.Min(level, 10) - .025 * Math.Clamp(level - 10, 0, 10) - .015 * Math.Clamp(level - 20, 0, 10);
        return (float)(spec.RechargeSeconds * factor);
    }
}
