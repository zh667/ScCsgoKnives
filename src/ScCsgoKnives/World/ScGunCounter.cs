namespace Game;

/// <summary>Installing a kill counter on a gun that already exists.
///
/// The counter goes onto the original instance: same record id, same ammunition, same finish, same durability,
/// same silencer, same charge. Nothing is rebuilt, nothing is topped up and no new gun model is introduced, so
/// the item encoding and the frozen model order are untouched. Counting starts at zero from the install, because
/// no earlier version kept a per-gun history that could honestly be back-filled.</summary>
public static class ScGunCounter {
    public sealed record Candidate(int Slot, int Value) {
        public int Variant => ScGunBlock.GetVariant(Value);
        public bool Installed => GunSpec.TryGetSnapshot(Terrain.ExtractData(Value), out var s) && s.CounterInstalled;
        public long Kills => GunSpec.TryGetSnapshot(Terrain.ExtractData(Value), out var s) ? s.KillCount : 0;
        public int Level => GunSpec.TryGetSnapshot(Terrain.ExtractData(Value), out var s) ? s.Level : 0;
    }
    /// <summary>A priced install, valid only for this record revision.</summary>
    public sealed record Quote(int Slot, int Value, int Id, int Revision, IReadOnlyDictionary<int, int> Cost, bool Fresh);

    /// <summary>Guns in this inventory that this build can read and that have no counter yet.</summary>
    public static IEnumerable<Candidate> Candidates(IInventory inventory, int gunBlockIndex = -1) {
        int gun = gunBlockIndex >= 0 ? gunBlockIndex : BlocksManager.GetBlockIndex<ScGunBlock>(true);
        int slots = inventory is ComponentCreativeInventory creative ? creative.OpenSlotsCount : inventory.SlotsCount;
        for (int i = 0; i < slots; i++) {
            int value = inventory.GetSlotValue(i);
            if (inventory.GetSlotCount(i) > 0 && Terrain.ExtractContents(value) == gun && ScGunBlock.IsKnown(value))
                yield return new Candidate(i, value);
        }
    }

    /// <summary>Prices an install. Null when the slot cannot be read or the gun already carries a counter -
    /// a repeat install is refused before anything is charged.</summary>
    public static Quote Prepare(IInventory inventory, int slot, bool free) {
        if (inventory is null || slot < 0 || slot >= inventory.SlotsCount) return null;
        if (!ScInventoryTransaction.IsWeaponSlot(inventory, slot)) return null;
        int value = inventory.GetSlotValue(slot);
        if (inventory.GetSlotCount(slot) <= 0 || !GunSpec.TryGetSnapshot(Terrain.ExtractData(value), out var s)) return null;
        if (s.CounterInstalled) return null;
        // A model with no official CS2 attachment has nowhere reliable to carry a counter; it is refused rather
        // than fitted with a guessed placement.
        if (!ScGunStatTrak.Has(GunSpec.All[s.Variant].Name, false)) return null;
        return new Quote(slot, value, s.Id, s.Revision, free ? new Dictionary<int, int>() : ScGunGrowth.InstallCost(), s.Fresh);
    }

    /// <summary>Commits an install through the one gun transaction: materials out, counter state in, everything
    /// else byte for byte. A gun that was still a stackable factory template gets its instance record here.</summary>
    public static ScGunResult Apply(IInventory inventory, Quote quote, string holder) {
        if (inventory is null || quote is null || quote.Slot < 0 || quote.Slot >= inventory.SlotsCount) return ScGunResult.Invalid;
        if (inventory.GetSlotValue(quote.Slot) != quote.Value) return ScGunResult.StateChanged;
        var mutation = ScGunMutation.Prepare(inventory, quote.Slot, holder, out ScGunResult why);
        if (mutation is null) return why;
        if (mutation.Before.Id != quote.Id || mutation.Before.Revision != quote.Revision) return ScGunResult.StateChanged;
        if (mutation.Before.CounterInstalled) return ScGunResult.Invalid;
        return mutation.Commit(r => {
            r.CounterInstalled = true;
            r.KillCount = 0;
            r.AppliedGrowthLevel = 0;
            r.PendingGrowthLevel = ScGunGrowth.NoPending;
            r.GrowthRulesVersion = ScGunGrowth.RulesVersion;
        }, materials: quote.Cost);
    }
}
