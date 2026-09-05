namespace Game;

/// <summary>Reload events commit independently. Cancellation never refunds a discarded magazine.</summary>
public sealed class ScReloadTransaction {
    public readonly IInventory Inventory;
    public readonly int Slot, Ammo, Cost, Capacity;
    public int Expected { get; private set; }
    public long Revision { get; private set; }
    public bool Discarded { get; private set; }
    public bool Inserted { get; private set; }
    public bool Cancelled { get; private set; }
    public ScReloadTransaction(IInventory inventory, int slot, int value, int ammo, int cost, int capacity) {
        Inventory = inventory; Slot = slot; Expected = value; Ammo = ammo; Cost = cost; Capacity = capacity;
        Revision = ScInventoryTransaction.Revision(inventory);
    }
    public bool Valid => !Cancelled && Inventory is not null && Inventory.ActiveSlotIndex == Slot
        && ScInventoryTransaction.Revision(Inventory) == Revision && Inventory.GetSlotCount(Slot) == 1 && Inventory.GetSlotValue(Slot) == Expected;
    public void Cancel() => Cancelled = true;
    public bool ModeMatches(bool creative) => creative == (Cost == 0);
    bool Write(int rounds, int cost) {
        if (!Valid) { Cancel(); return false; }
        int replacement = Terrain.ReplaceData(Expected, GunSpec.SetRounds(Terrain.ExtractData(Expected), rounds));
        if (!ScInventoryTransaction.ReplaceWithCost(Inventory, Slot, Expected, replacement, Ammo, cost)) { Cancel(); return false; }
        Expected = replacement; Revision = ScInventoryTransaction.Revision(Inventory); return true;
    }
    public bool Discard() {
        if (Discarded || Inserted) return false;
        if (!Write(0, 0)) return false;
        Discarded = true; return true;
    }
    public bool InsertMagazine() {
        if (!Discarded || Inserted) return false;
        if (!Write(Capacity, Cost)) return false;
        Inserted = true; return true;
    }
    public bool InsertShell() {
        int rounds = GunSpec.GetRounds(Terrain.ExtractData(Expected));
        return rounds < Capacity && Write(rounds + 1, Cost == 0 ? 0 : 1);
    }
    public static bool IsTube(string name) => name is "nova" or "xm1014" or "sawedoff";
    public static int AmmoKind(GunSpec gun) => gun.Pellets > 1 ? ScAmmoBlock.Shell : ScAmmoBlock.Magazine;
    public static int Required(GunSpec gun) => gun.Name switch { "taser" => 0, "mag7" => 5, "p90" or "bizon" => 2, "m249" => 3, "negev" => 5, _ => 1 };
}
