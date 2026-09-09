namespace Game;

/// <summary>Applying a CS2 finish at the workbench. The transaction writes the record's
/// paint ID and nothing else, so rounds, silencer, durability, max durability and the Zeus charge come out
/// of it byte for byte. Like a repair it is quoted first - the record id, its revision, the finish it wears
/// and the exact materials are frozen - and the commit re-checks all of it, so a gun that changed under the
/// dialog is re-quoted instead of being charged at the old price. EffectiveGunStats derives the skin damage
/// bonus from that ID; changing finish never resets installed counters, kills, levels or pending growth.</summary>
public static class ScWeaponSkinning {
    public sealed record Candidate(int Slot, int Value) {
        public int Variant => ScGunBlock.GetVariant(Value);
        public int SkinId => ScGunBlock.SkinOf(Value);
    }
    /// <summary>A priced finish change, valid only for this record revision. <c>Skin</c> null means stripping
    /// back to the factory look.</summary>
    public sealed record Quote(int Slot, int Value, int Id, int Revision, int FromSkinId, ScGunSkin Skin,
                               IReadOnlyDictionary<int, int> Cost, bool Fresh);

    /// <summary>Every gun in the inventory this build can read, damaged or not.</summary>
    public static IEnumerable<Candidate> Candidates(IInventory inventory, int gunBlockIndex = -1) {
        int gun = gunBlockIndex >= 0 ? gunBlockIndex : BlocksManager.GetBlockIndex<ScGunBlock>(true);
        int slots = inventory is ComponentCreativeInventory creative ? creative.OpenSlotsCount : inventory.SlotsCount;
        for (int i = 0; i < slots; i++) {
            int value = inventory.GetSlotValue(i);
            if (inventory.GetSlotCount(i) > 0 && Terrain.ExtractContents(value) == gun && ScGunBlock.IsKnown(value)
                && ScGunSkinCatalog.For(ScGunBlock.GetVariant(value)).Any())
                yield return new Candidate(i, value);
        }
    }

    /// <summary>Prices a change. Null when the slot cannot be read, the finish does not belong to this gun,
    /// or the gun already wears it - re-picking the current finish is refused before anything is charged.</summary>
    public static Quote Prepare(IInventory inventory, int slot, ScGunSkin skin, bool free, Func<int, int> materialValue) {
        if (inventory is null || slot < 0 || slot >= inventory.SlotsCount) return null;
        if (!ScInventoryTransaction.IsWeaponSlot(inventory, slot)) return null;
        int value = inventory.GetSlotValue(slot);
        if (inventory.GetSlotCount(slot) <= 0 || !GunSpec.TryGetSnapshot(Terrain.ExtractData(value), out var s)) return null;
        if (skin is not null && !ScGunSkinCatalog.Fits(skin, s.Variant)) return null;
        int target = skin?.PaintId ?? ScGunSkinCatalog.None;
        if (target == s.SkinId) return null;
        var cost = free ? new Dictionary<int, int>() : ScGunSkinCatalog.CostOf(skin, materialValue);
        return new Quote(slot, value, s.Id, s.Revision, s.SkinId, skin, cost, s.Fresh);
    }

    /// <summary>Commits a quote through the one gun transaction: materials out, paint ID in, everything else
    /// untouched. A gun that was still a stackable factory template gets its instance record here, which is
    /// what stops two finished guns from ever sharing one item value.</summary>
    public static ScGunResult Apply(IInventory inventory, Quote quote, string holder) {
        if (inventory is null || quote is null || quote.Slot < 0 || quote.Slot >= inventory.SlotsCount) return ScGunResult.Invalid;
        if (inventory.GetSlotValue(quote.Slot) != quote.Value) return ScGunResult.StateChanged;
        var mutation = ScGunMutation.Prepare(inventory, quote.Slot, holder, out ScGunResult why);
        if (mutation is null) return why;
        if (mutation.Before.Id != quote.Id || mutation.Before.Revision != quote.Revision || mutation.Before.SkinId != quote.FromSkinId)
            return ScGunResult.StateChanged;
        int target = quote.Skin?.PaintId ?? ScGunSkinCatalog.None;
        if (target == mutation.Before.SkinId) return ScGunResult.Invalid;
        return mutation.Commit(r => r.SkinId = target, materials: quote.Cost);
    }
}
