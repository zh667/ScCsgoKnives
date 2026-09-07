namespace Game;

/// <summary>Magazine replacement is atomic at animation completion; tube shells commit individually. Every write goes
/// through ScGunMutation, so a refused reload (no ammo, full table, changed record) pays nothing and fills nothing.</summary>
public sealed class ScReloadTransaction {
    public static string CostMessage(bool creative, bool shells, int count) => creative
        ? "创造模式：无限弹药，无需消耗"
        : $"装填消耗：{(shells ? "霰弹" : "通用弹匣")} ×{count}";

    public readonly IInventory Inventory;
    public readonly int Slot, Ammo, Cost, Capacity;
    public readonly string Holder;
    public int Expected { get; private set; }
    public long Revision { get; private set; }
    public bool Discarded { get; private set; }
    public bool Inserted { get; private set; }
    public bool Cancelled { get; private set; }
    /// <summary>Why the last write was refused, for the caller's message.</summary>
    public ScGunResult LastResult { get; private set; } = ScGunResult.Success;
    public ScReloadTransaction(IInventory inventory, int slot, int value, int ammo, int cost, int capacity) : this(inventory, slot, value, ammo, cost, capacity, null) { }
    public ScReloadTransaction(IInventory inventory, int slot, int value, int ammo, int cost, int capacity, string holder) {
        Inventory = inventory; Slot = slot; Expected = value; Ammo = ammo; Cost = cost; Capacity = capacity; Holder = holder ?? $"inventory:{slot}";
        Revision = ScInventoryTransaction.Revision(inventory);
    }
    public bool Valid => !Cancelled && Inventory is not null && Inventory.ActiveSlotIndex == Slot
        && ScInventoryTransaction.Revision(Inventory) == Revision && ScInventoryTransaction.IsWeaponSlot(Inventory, Slot) && Inventory.GetSlotValue(Slot) == Expected;
    public void Cancel() => Cancelled = true;
    public bool ModeMatches(bool creative) => creative == (Cost == 0);
    bool Write(int rounds, int cost) {
        if (!Valid) { Cancel(); return false; }
        var mutation = ScGunMutation.Prepare(Inventory, Slot, Holder, out ScGunResult why);
        LastResult = mutation is null ? why : mutation.Commit(r => r.Rounds = Math.Clamp(rounds, 0, Capacity), Ammo, cost);
        if (LastResult != ScGunResult.Success) { Cancel(); return false; }
        Expected = mutation.Expected; Revision = ScInventoryTransaction.Revision(Inventory); return true;
    }
    public bool Discard() {
        if (Discarded || Inserted) return false;
        // The drop cue is visual only; cancellation preserves rounds and reserves.
        if (!Valid) { Cancel(); return false; }
        Discarded = true; return true;
    }
    public bool InsertMagazine() {
        if (!Discarded || Inserted) return false;
        if (!Write(Capacity, Cost)) return false;
        Inserted = true; return true;
    }
    public bool FinishMagazine(double now, double completeAt) => double.IsFinite(now)
        && double.IsFinite(completeAt) && now >= completeAt && InsertMagazine();
    public bool InsertShell() {
        int rounds = GunSpec.GetRounds(Terrain.ExtractData(Expected));
        return rounds < Capacity && Write(rounds + 1, Cost == 0 ? 0 : 1);
    }
    public static bool IsTube(string name) => name is "nova" or "xm1014" or "sawedoff";
    public static int AmmoKind(GunSpec gun) => gun.Pellets > 1 ? ScAmmoBlock.Shell : ScAmmoBlock.Magazine;
    public static int Required(GunSpec gun) => gun.Name switch { "taser" => 0, "mag7" => 5, "p90" or "bizon" => 2, "m249" => 3, "negev" => 5, _ => 1 };
}
