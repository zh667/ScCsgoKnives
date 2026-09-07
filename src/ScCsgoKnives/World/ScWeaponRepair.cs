namespace Game;

/// <summary>Workbench repair (plan C4). A full repair costs roughly a quarter of the gun's assembly blanks and
/// mechanisms (at least one blank); this repair charges ceil(missing/full × full cost) per material, so any wear
/// costs at least one item and the quote shown is exactly what is deducted.</summary>
public static class ScWeaponRepair {
    public sealed record Candidate(int Slot, int Value) {
        public int Durability => GunSpec.GetDurability(Terrain.ExtractData(Value));
        public int Full => ScGunDurability.FullOf(Terrain.ExtractData(Value));
    }
    public const int Blank = 0, Mechanism = 1; // ScWeaponMaterialBlock kinds: 金属坯件, 精密机构
    /// <summary>Full-repair cost by material kind (0 blank, 1 mechanism): about a quarter of the assembly recipe.</summary>
    public static Dictionary<int, int> FullCost(ScWeaponCrafting.Entry e) {
        var cost = new Dictionary<int, int>();
        if (e is null || e.Knife) return cost;
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
    /// <summary>The same cost keyed by item value, for the inventory transaction.</summary>
    public static Dictionary<int, int> CostValues(ScWeaponCrafting.Entry e, int durability, int full) => Cost(e, durability, full).ToDictionary(p => ScWeaponMaterialBlock.Value(p.Key), p => p.Value);
    public static IEnumerable<Candidate> Candidates(IInventory inventory, int gunBlockIndex = -1) {
        int gun = gunBlockIndex >= 0 ? gunBlockIndex : BlocksManager.GetBlockIndex<ScGunBlock>(true);
        for (int i = 0; i < inventory.SlotsCount; i++) {
            int value = inventory.GetSlotValue(i);
            if (inventory.GetSlotCount(i) > 0 && Terrain.ExtractContents(value) == gun && ScGunBlock.IsKnown(value)) {
                var c = new Candidate(i, value);
                if (c.Durability < c.Full) yield return c;
            }
        }
    }
    public static int Repaired(int value) => Terrain.ReplaceData(value, GunSpec.SetDurability(Terrain.ExtractData(value), ScGunDurability.FullOf(Terrain.ExtractData(value))));
    /// <summary>Re-checks the target and the materials, deducts, then rewrites the gun; any shortfall rolls the deduction back.</summary>
    public static bool TryRepair(IInventory inventory, Candidate target, IReadOnlyDictionary<int, int> cost) {
        if (inventory is null || target is null || target.Slot < 0 || target.Slot >= inventory.SlotsCount) return false;
        int count = inventory.GetSlotCount(target.Slot);
        if (count <= 0 || inventory.GetSlotValue(target.Slot) != target.Value || target.Durability >= target.Full) return false;
        if (cost.Any(p => p.Value <= 0 || ScInventoryTransaction.Count(inventory, p.Key) < p.Value)) return false;
        var taken = new List<(int Slot, int Value, int Count)>();
        foreach (var material in cost) {
            int needed = material.Value;
            for (int i = 0; i < inventory.SlotsCount && needed > 0; i++) {
                if (i == target.Slot || inventory.GetSlotValue(i) != material.Key || inventory.GetSlotCount(i) == 0) continue;
                int want = Math.Min(needed, inventory.GetSlotCount(i)), got = inventory.RemoveSlotItems(i, want);
                taken.Add((i, material.Key, got)); needed -= got;
                if (got != want) break;
            }
            if (needed > 0) {
                foreach (var item in taken) inventory.AddSlotItems(item.Slot, item.Value, item.Count);
                return false;
            }
        }
        int removed = inventory.RemoveSlotItems(target.Slot, count);
        if (removed != count) {
            if (removed > 0) inventory.AddSlotItems(target.Slot, target.Value, removed);
            foreach (var item in taken) inventory.AddSlotItems(item.Slot, item.Value, item.Count);
            return false;
        }
        inventory.AddSlotItems(target.Slot, Repaired(target.Value), count);
        ScInventoryTransaction.Changed(inventory);
        return true;
    }
}
