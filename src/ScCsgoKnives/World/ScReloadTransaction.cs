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
    bool Write(int rounds, int cost, int reserveSpent = 0) {
        if (!Valid) { Cancel(); return false; }
        var mutation = ScGunMutation.Prepare(Inventory, Slot, Holder, out ScGunResult why);
        LastResult = mutation is null ? why : mutation.Commit(r => {
            r.Rounds = Math.Clamp(rounds, 0, Capacity);
            // Rounds that came out of this gun's own overflow reserve are moved, never created.
            if (reserveSpent > 0) r.ReserveOverflowRounds = Math.Max(0, r.ReserveOverflowRounds - reserveSpent);
            else if (reserveSpent < 0) r.ReserveOverflowRounds = 0;
        }, Ammo, cost);
        if (LastResult != ScGunResult.Success) { Cancel(); return false; }
        Expected = mutation.Expected; Revision = ScInventoryTransaction.Revision(Inventory); return true;
    }
    /// <summary>Live rounds this gun is carrying above its capacity, from a capacity that shrank. Zero in normal play.</summary>
    public int Reserve => GunSpec.TryGetSnapshot(Terrain.ExtractData(Expected), out var s) ? s.ReserveOverflowRounds : 0;
    public bool Discard() {
        if (Discarded || Inserted) return false;
        // The drop cue is visual only; cancellation preserves rounds and reserves.
        if (!Valid) { Cancel(); return false; }
        Discarded = true; return true;
    }
    public bool InsertMagazine() {
        if (!Discarded || Inserted) return false;
        // The gun's own reserve fills the gap first. When it covers the gap on its own no external magazine is
        // charged; when it does not, the magazine is charged and the reserve is absorbed into the loaded rounds.
        int rounds = GunSpec.GetRounds(Terrain.ExtractData(Expected));
        int gap = Math.Max(0, Capacity - rounds), reserve = Reserve;
        bool fromReserve = reserve > 0 && reserve >= gap;
        if (!Write(Capacity, fromReserve ? 0 : Cost, fromReserve ? gap : reserve > 0 ? -1 : 0)) return false;
        Inserted = true; return true;
    }
    public bool FinishMagazine(double now, double completeAt) => double.IsFinite(now)
        && double.IsFinite(completeAt) && now >= completeAt && InsertMagazine();
    public bool InsertShell() {
        int rounds = GunSpec.GetRounds(Terrain.ExtractData(Expected));
        if (rounds >= Capacity) return false;
        // One shell at a time: this gun's own reserve is spent before the player's shells.
        bool fromReserve = Reserve > 0;
        return Write(rounds + 1, fromReserve || Cost == 0 ? 0 : 1, fromReserve ? 1 : 0);
    }
    public static bool IsTube(string name) => name is "nova" or "xm1014" or "sawedoff";
    public static int AmmoKind(GunSpec gun) => gun.Pellets > 1 ? ScAmmoBlock.Shell : ScAmmoBlock.Magazine;
    /// <summary>The base-capacity cost. Kept as the single-argument name because tools and regressions bind to it
    /// by name, and an overload set makes that binding ambiguous.</summary>
    public static int Required(GunSpec gun) => RequiredFor(gun, gun.Magazine);
    /// <summary>What one reload costs at this gun's current capacity. The MAG-7 loads loose shells into a whole
    /// magazine, so its cost is the capacity itself and follows growth; every other gun keeps its fixed
    /// per-model magazine cost, which is what makes a grown capacity a real gain.</summary>
    public static int RequiredFor(GunSpec gun, int capacity) => gun.Name switch {
        "taser" => 0, "mag7" => Math.Max(1, capacity), "p90" or "bizon" => 2, "m249" => 3, "negev" => 5, _ => 1 };
}
