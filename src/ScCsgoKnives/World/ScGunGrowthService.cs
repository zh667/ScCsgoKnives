namespace Game;

/// <summary>Turns confirmed kills into levels, one gun record at a time.
///
/// Two steps, both through the one gun transaction and both idempotent:
/// 1. a queued kill credit is added to the gun's count and, in a growing world, marks the level it has earned;
/// 2. the marked level is applied at a moment the gun is not in the middle of an action, converting durability by
///    ratio, keeping the loaded rounds, and keeping the remaining fraction of a charge in progress.
///
/// Levelling never refills a magazine, never repairs and never completes a charge.</summary>
public static class ScGunGrowthService {
    /// <summary>All acquisition routes use this sweep, not just the workbench dialog. Unset is
    /// not an opt-out: a world already carrying counters defaults to growth. Explicit CountOnly stays.</summary>
    public static int Advance(ScGunRegistry registry, IReadOnlyList<ScGunHolders.Holder> holders, double now,
        Func<ScGunHolders.Holder, bool> busy, Action<int, int, int> report) {
        if (registry is null || registry.Disabled) return 0;
        if (registry.GrowthMode == ScGunGrowthMode.Unset
            && registry.Ids.Any(id => registry.TryGetSnapshot(id, out var s) && s.CounterInstalled)) {
            registry.GrowthMode = ScGunGrowthMode.CountAndGrow;
            KnifeLog.Information("[GUN_GROWTH] world rule Unset -> CountAndGrow; existing kills retained and earned levels will catch up when idle");
        }
        bool grow = registry.GrowthMode == ScGunGrowthMode.CountAndGrow;
        DrainKills(registry, holders, grow, busy);
        return grow ? ApplyPendingLevels(registry, holders, now, busy, report) : 0;
    }
    /// <summary>The one holder that can be written for this record, or null when there is none (a dropped gun, a
    /// projectile) or more than one (a shared record the duplicate sweep has not split yet).</summary>
    public static ScGunHolders.Holder? Writable(IReadOnlyList<ScGunHolders.Holder> holders, int id) {
        ScGunHolders.Holder? found = null;
        foreach (var h in holders) {
            if (h.Id != id) continue;
            if (h.Inventory is null) return null;       // dropped/projectile: wait for a pickup
            if (found is not null) return null;         // shared record: let SplitDuplicates resolve it first
            found = h;
        }
        return found;
    }

    /// <summary>Writes as many queued kill credits as their guns can take right now. A credit stays queued (and
    /// saved with the world) until its record has taken it exactly once.</summary>
    public static int DrainKills(ScGunRegistry registry, IReadOnlyList<ScGunHolders.Holder> holders, bool grow,
                                 Func<ScGunHolders.Holder, bool> busy = null) {
        if (registry is null || registry.Disabled || registry.Kills.Count == 0) return 0;
        int written = 0;
        foreach (var entry in registry.Kills.Pending.ToArray()) {
            if (!registry.TryGetSnapshot(entry.RecordId, out var before) || before.Variant != entry.Variant) {
                KnifeDiagnostics.WarnOnce($"kill-credit-record-{entry.RecordId}", $"gun kill credit {entry.EventId}: record {entry.RecordId} is missing/quarantined or has another model; credit retained, not applied");
                continue;
            }
            if (!before.CounterInstalled) {
                KnifeDiagnostics.WarnOnce($"kill-credit-counter-{entry.RecordId}", $"gun kill credit {entry.EventId}: record {entry.RecordId} has no counter; credential retained for diagnosis, not applied");
                continue;
            }
            var holder = Writable(holders, entry.RecordId);
            if (holder is null) continue;               // retried next update; nothing is lost
            // Writing bumps the holding inventory's revision, which a reload in progress reads as "the gun moved".
            // A credit therefore waits for the action to finish rather than cancelling it; the credential is kept.
            if (busy is not null && busy(holder.Value)) continue;
            var mutation = ScGunMutation.Prepare(holder.Value.Inventory, holder.Value.Slot, holder.Value.Key, out ScGunResult why);
            if (mutation is null || mutation.Before.Id != entry.RecordId) continue;
            mutation.KillToComplete = entry;
            var result = mutation.Commit(r => {
                if (r.KillCount < long.MaxValue) r.KillCount++;
                if (!grow) return;
                int earned = ScGunGrowth.LevelFor(r.KillCount);
                if (earned > r.AppliedGrowthLevel && earned > Math.Max(r.PendingGrowthLevel, r.AppliedGrowthLevel)) r.PendingGrowthLevel = earned;
            });
            if (result != ScGunResult.Success) continue; // busy/changed: the same credential is retried
            written++;
            if (registry.TryGetSnapshot(entry.RecordId, out var after) && after.KillCount % ScGunGrowth.KillsPerLevel == 0)
                KnifeLog.Trace($"gun counter: record {entry.RecordId} ({GunSpec.All[after.Variant].Name}) reached {after.KillCount} kills, level {after.EarnedLevel}, applied {after.AppliedGrowthLevel}");
        }
        return written;
    }

    /// <summary>Applies a marked level to one gun. <paramref name="now"/> is game time, used only to keep the
    /// remaining fraction of a charge in progress.</summary>
    public static ScGunResult ApplyPending(IInventory inventory, int slot, string holder, double now, out int from, out int to) {
        from = to = 0;
        if (inventory is null) return ScGunResult.Invalid;
        var mutation = ScGunMutation.Prepare(inventory, slot, holder, out ScGunResult why);
        if (mutation is null) return why;
        var before = mutation.Before;
        if (!before.CounterInstalled) return ScGunResult.Invalid;
        int target = before.PendingGrowthLevel;
        // Old template-created guns accumulated kills under Unset without ever marking a pending level.
        if (ScGunRegistry.Current?.GrowthMode == ScGunGrowthMode.CountAndGrow) target = Math.Max(target, before.EarnedLevel);
        if (target == ScGunGrowth.NoPending || target <= before.AppliedGrowthLevel) return ScGunResult.Invalid;
        from = before.AppliedGrowthLevel; to = ScGunGrowth.Clamp(target);
        var spec = GunSpec.All[before.Variant];
        int level = to;
        return mutation.Commit(r => {
            int oldMax = r.MaxDurability;
            int newMax = ScGunGrowth.MaxDurability(r.Variant, level);
            // Life keeps its ratio: broken stays broken, full stays full, and nothing in between reaches either end.
            r.Durability = ScGunGrowth.ScaleDurability(r.Durability, oldMax, newMax);
            r.MaxDurability = newMax;
            // A capacity that shrinks keeps its live rounds with this gun rather than deleting them.
            int capacity = ScGunGrowth.Capacity(r.Variant, level);
            if (r.Rounds > capacity) { r.ReserveOverflowRounds += r.Rounds - capacity; r.Rounds = capacity; }
            if (r.RechargeReadyAt >= 0) {
                float oldCycle = r.RechargeCycleSeconds > 0 ? r.RechargeCycleSeconds : ScGunGrowth.RechargeSeconds(spec, r.AppliedGrowthLevel);
                float newCycle = ScGunGrowth.RechargeSeconds(spec, level);
                r.RechargeReadyAt = now + ScGunGrowth.ScaleRemaining(Math.Max(0, r.RechargeReadyAt - now), oldCycle, newCycle);
                r.RechargeCycleSeconds = newCycle;
            }
            r.AppliedGrowthLevel = level;
            r.PendingGrowthLevel = ScGunGrowth.NoPending;
            r.GrowthRulesVersion = ScGunGrowth.RulesVersion;
        });
    }

    /// <summary>Sweeps every record with a marked level and applies the ones whose gun is reachable and idle.
    /// <paramref name="busy"/> answers "is this exact holder in the middle of an action right now".</summary>
    public static int ApplyPendingLevels(ScGunRegistry registry, IReadOnlyList<ScGunHolders.Holder> holders, double now,
                                         Func<ScGunHolders.Holder, bool> busy, Action<int, int, int> report) {
        if (registry is null || registry.Disabled) return 0;
        int applied = 0;
        foreach (int id in registry.Ids.ToArray()) {
            if (!registry.TryGetSnapshot(id, out var s) || !s.CounterInstalled) continue;
            int earned = registry.GrowthMode == ScGunGrowthMode.CountAndGrow ? s.EarnedLevel : 0;
            if (Math.Max(s.PendingGrowthLevel, earned) <= s.AppliedGrowthLevel) continue;
            var holder = Writable(holders, id);
            if (holder is null) continue;
            if (busy is not null && busy(holder.Value)) continue;
            var result = ApplyPending(holder.Value.Inventory, holder.Value.Slot, holder.Value.Key, now, out int from, out int to);
            if (result != ScGunResult.Success) {
                KnifeDiagnostics.WarnOnce($"growth-refused-{id}-{to}-{result}", $"[GUN_GROWTH] record {id} upgrade {from}->{to} deferred: {result}");
                continue;
            }
            applied++;
            report?.Invoke(id, from, to);
        }
        return applied;
    }
}
