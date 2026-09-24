namespace Game;

/// <summary>Workbench repair. Independent Lv0 costs scale by 20% per ten applied levels;
/// missing durability is applied before the single final ceiling. The quote freezes the
/// record id and revision, the durability it priced and the materials; the commit re-checks all of it through
/// ScGunMutation, so a gun that changed (or was swapped for a same-looking one) is re-quoted, never repaired at the old price.</summary>
public static class ScWeaponRepair {
    public sealed record Candidate(int Slot, int Value) {
        public int Durability => GunSpec.GetDurability(Terrain.ExtractData(Value));
        public int Full => GunSpec.GetMaxDurability(Terrain.ExtractData(Value));
    }
    /// <summary>A priced repair, valid only for this record revision.</summary>
    public sealed record Quote(int Slot, int Value, int Id, int Revision, int Durability, int Full, IReadOnlyDictionary<int, int> Cost) {
        public int Level {get;init;}
    }
    public const int Blank = 0, Mechanism = 1; // ScWeaponMaterialBlock kinds: 金属坯件, 精密机构
    /// <summary>Compatibility hook for explicit base-cost overrides; empty in the default economy.</summary>
    public static readonly Dictionary<string, (int Blank, int Mechanism)> FixedFullCost = new(StringComparer.Ordinal) {
    };
    /// <summary>Lv0 whole-repair basis, independent of assembly costs. Never use a rounded level quote as the basis.</summary>
    public static Dictionary<int, int> FullCost(ScWeaponCrafting.Entry e) {
        var cost = new Dictionary<int, int>();
        if (e is null || e.Knife) return cost;
        if (FixedFullCost.TryGetValue(e.Name, out var fixedCost)) {
            if (fixedCost.Blank > 0) cost[Blank] = fixedCost.Blank;
            if (fixedCost.Mechanism > 0) cost[Mechanism] = fixedCost.Mechanism;
            return cost;
        }
        var basis=ScGunDurability.ClassOf(e.Name) switch {
            ScGunDurability.Class.Pistol or ScGunDurability.Class.Shotgun => (2,1),
            ScGunDurability.Class.AutoSniper => (4,3), ScGunDurability.Class.MachineGun => (5,3),
            ScGunDurability.Class.Taser => (5,5), _ => (3,2)
        };
        cost[Blank]=basis.Item1;cost[Mechanism]=basis.Item2;
        return cost;
    }
    /// <summary>This repair's cost by material kind: ceil(missing/full × full cost), never zero for a worn gun.</summary>
    public static Dictionary<int, int> Cost(ScWeaponCrafting.Entry e, int durability, int full) => CostAtLevel(e,durability,full,0);
    public static float LevelMultiplier(int level) => (5+ScGunGrowth.Clamp(level)/10)/5f;
    public static Dictionary<int,int> FullCostAtLevel(ScWeaponCrafting.Entry e,int level) => CostAtLevel(e,0,1,level);
    public static Dictionary<int, int> CostAtLevel(ScWeaponCrafting.Entry e, int durability, int full,int level) {
        var cost = new Dictionary<int, int>();
        if(full<=0)return cost;
        int missing = full - Math.Clamp(durability, 0, full);
        if (missing <= 0) return cost;
        foreach (var (kind, amount) in FullCost(e)) {
            long numerator=(long)amount*(5+ScGunGrowth.Clamp(level)/10)*missing,denominator=5L*full;
            int need = checked((int)((numerator+denominator-1)/denominator));
            if (need > 0) cost[kind] = need;
        }
        return cost;
    }
    public static IEnumerable<Candidate> Candidates(IInventory inventory, int gunBlockIndex = -1) {
        inventory = ScInventoryIdentity.Inventory(inventory);
        if (inventory is null) yield break;
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
        if (c is null || entry is null || entry.Knife || !GunSpec.TryGetSnapshot(Terrain.ExtractData(c.Value), out var s) || entry.Variant!=s.Variant) return null;
        var cost = free ? new Dictionary<int, int>() : CostAtLevel(entry, s.Durability, s.MaxDurability,s.Level).ToDictionary(p => materialValue(p.Key), p => p.Value);
        return new Quote(c.Slot, c.Value, s.Id, s.Revision, s.Durability, s.MaxDurability, cost) {Level=s.Level};
    }
    /// <summary>Commits a quote: the slot must still hold that value, the record must still be at the quoted revision and
    /// durability, the materials must all be there; then durability goes to full in one transaction.</summary>
    public static ScGunResult TryRepair(IInventory inventory, Quote quote, string holder) {
        if (inventory is null || quote is null || quote.Slot < 0 || quote.Slot >= inventory.SlotsCount) return ScGunResult.Invalid;
        if (inventory.GetSlotValue(quote.Slot) != quote.Value) return ScGunResult.StateChanged;
        var mutation = ScGunMutation.Prepare(inventory, quote.Slot, holder, out ScGunResult why);
        if (mutation is null) return why;
        if (mutation.Before.Id != quote.Id || mutation.Before.Revision != quote.Revision || mutation.Before.Durability != quote.Durability
            || mutation.Before.MaxDurability != quote.Full || mutation.Before.Level != quote.Level) return ScGunResult.StateChanged;
        if (quote.Durability >= quote.Full) return ScGunResult.Invalid;
        return mutation.Commit(r => r.Durability = r.MaxDurability, materials: quote.Cost);
    }
}
