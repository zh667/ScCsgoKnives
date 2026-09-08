namespace Game;

/// <summary>Workbench repair (plan C4/C7). A full repair costs roughly a quarter of the gun's assembly blanks and
/// mechanisms (at least one blank); a repair charges ceil(missing/full × full cost) per material. The quote freezes the
/// record id and revision, the durability it priced and the materials; the commit re-checks all of it through
/// ScGunMutation, so a gun that changed (or was swapped for a same-looking one) is re-quoted, never repaired at the old price.</summary>
public static class ScWeaponRepair {
    public sealed record Candidate(int Slot, int Value) {
        public int Durability => GunSpec.GetDurability(Terrain.ExtractData(Value));
        public int Full => GunSpec.GetMaxDurability(Terrain.ExtractData(Value));
    }
    /// <summary>A priced repair, valid only for this record revision.</summary>
    public sealed record Quote(int Slot, int Value, int Id, int Revision, int Durability, int Full, IReadOnlyDictionary<int, int> Cost);
    public const int Blank = 0, Mechanism = 1; // ScWeaponMaterialBlock kinds: 金属坯件, 精密机构
    /// <summary>Guns whose full repair is fixed instead of derived from their assembly recipe. The Zeus's
    /// 2026-09-08 recipe was made deliberately expensive; deriving repair from it would have quietly raised the
    /// price of repairing guns players already own, which the same instruction rules out.</summary>
    public static readonly Dictionary<string, (int Blank, int Mechanism)> FixedFullCost = new(StringComparer.Ordinal) {
        ["taser"] = (1, 1),
    };
    /// <summary>Full-repair cost by material kind (0 blank, 1 mechanism): about a quarter of the assembly recipe,
    /// except where <see cref="FixedFullCost"/> pins it.</summary>
    public static Dictionary<int, int> FullCost(ScWeaponCrafting.Entry e) {
        var cost = new Dictionary<int, int>();
        if (e is null || e.Knife) return cost;
        if (FixedFullCost.TryGetValue(e.Name, out var fixedCost)) {
            if (fixedCost.Blank > 0) cost[Blank] = fixedCost.Blank;
            if (fixedCost.Mechanism > 0) cost[Mechanism] = fixedCost.Mechanism;
            return cost;
        }
        cost[Blank] = Math.Max(1, (int)Math.Round(e.B / 4.0, MidpointRounding.AwayFromZero));
        int mechanisms = (int)Math.Round(e.M / 4.0, MidpointRounding.ToZero);
        if (e.M >= 3) mechanisms = Math.Max(1, mechanisms);
        if (mechanisms > 0) cost[Mechanism] = mechanisms;
        return cost;
    }
    /// <summary>This repair's cost by material kind: ceil(missing/full × full cost), never zero for a worn gun.</summary>
    public static Dictionary<int, int> Cost(ScWeaponCrafting.Entry e, int durability, int full) {
        var cost = new Dictionary<int, int>();
        int missing = full - Math.Clamp(durability, 0, full);
        if (missing <= 0 || full <= 0) return cost;
        foreach (var (kind, amount) in FullCost(e)) {
            int need = (int)Math.Ceiling(missing / (double)full * amount);
            if (need > 0) cost[kind] = need;
        }
        return cost;
    }
    public static IEnumerable<Candidate> Candidates(IInventory inventory, int gunBlockIndex = -1) {
        int gun = gunBlockIndex >= 0 ? gunBlockIndex : BlocksManager.GetBlockIndex<ScGunBlock>(true);
        int slots = inventory is ComponentCreativeInventory creative ? creative.OpenSlotsCount : inventory.SlotsCount;
        for (int i = 0; i < slots; i++) {
            int value = inventory.GetSlotValue(i);
            if (inventory.GetSlotCount(i) > 0 && Terrain.ExtractContents(value) == gun && ScGunBlock.IsKnown(value)) {
                var c = new Candidate(i, value);
                if (c.Durability < c.Full) yield return c;
            }
        }
    }
    /// <summary>Prices a candidate from its current snapshot. <paramref name="materialValue"/> maps a material kind to its item value
    /// (the block registry in the game, anything in tests); null cost = free (creative).</summary>
    public static Quote Prepare(Candidate c, ScWeaponCrafting.Entry entry, bool free, Func<int, int> materialValue) {
        if (c is null || !GunSpec.TryGetSnapshot(Terrain.ExtractData(c.Value), out var s)) return null;
        var cost = free || entry is null ? new Dictionary<int, int>() : Cost(entry, s.Durability, s.MaxDurability).ToDictionary(p => materialValue(p.Key), p => p.Value);
        return new Quote(c.Slot, c.Value, s.Id, s.Revision, s.Durability, s.MaxDurability, cost);
    }
    /// <summary>Commits a quote: the slot must still hold that value, the record must still be at the quoted revision and
    /// durability, the materials must all be there; then durability goes to full in one transaction.</summary>
    public static ScGunResult TryRepair(IInventory inventory, Quote quote, string holder) {
        if (inventory is null || quote is null || quote.Slot < 0 || quote.Slot >= inventory.SlotsCount) return ScGunResult.Invalid;
        if (inventory.GetSlotValue(quote.Slot) != quote.Value) return ScGunResult.StateChanged;
        var mutation = ScGunMutation.Prepare(inventory, quote.Slot, holder, out ScGunResult why);
        if (mutation is null) return why;
        if (mutation.Before.Id != quote.Id || mutation.Before.Revision != quote.Revision || mutation.Before.Durability != quote.Durability) return ScGunResult.StateChanged;
        if (quote.Durability >= quote.Full) return ScGunResult.Invalid;
        return mutation.Commit(r => r.Durability = r.MaxDurability, materials: quote.Cost);
    }
}
