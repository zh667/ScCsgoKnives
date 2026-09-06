using System.Runtime.CompilerServices;

namespace Game;

/// <summary>Single game-thread transaction. All slot capacities and inputs checked before mutation.</summary>
public static class ScInventoryTransaction {
    sealed class Epoch { public long Value; }
    static readonly ConditionalWeakTable<IInventory, Epoch> Epochs = new();
    public static long Revision(IInventory inventory) => inventory is null ? -1 : Epochs.GetOrCreateValue(inventory).Value;
    public static void Changed(IInventory inventory) { if (inventory is not null) Epochs.GetOrCreateValue(inventory).Value++; }
    public static int Count(IInventory inventory, int value) {
        int count = 0;
        if (inventory is not null) for (int i = 0; i < inventory.SlotsCount; i++) if (inventory.GetSlotValue(i) == value) count = (int)Math.Min(int.MaxValue, (long)count + inventory.GetSlotCount(i));
        return count;
    }
    public static bool IsWeaponSlot(IInventory inventory, int slot) {
        if (inventory is null || slot < 0 || slot >= inventory.SlotsCount) return false;
        // Creative slots represent an infinite source, not a stack of guns.
        // Only writable hotbar/backpack slots can hold changing weapon state.
        return inventory is ComponentCreativeInventory creative
            ? slot < creative.OpenSlotsCount && inventory.GetSlotCount(slot) > 0
            : inventory.GetSlotCount(slot) == 1;
    }
    public static bool ReplaceWithCost(IInventory inventory, int slot, int expected, int replacement, int ammo, int cost) {
        if (!IsWeaponSlot(inventory, slot) || cost < 0
            || inventory.GetSlotValue(slot) != expected || inventory.GetSlotCapacity(slot, replacement) < 1) return false;
        if (inventory is ComponentCreativeInventory creative) {
            if (cost != 0) return false;
            // AddSlotItems replaces a writable creative slot directly. Removing
            // one item does not empty an infinite creative source.
            creative.AddSlotItems(slot, replacement, 1);
            if (creative.GetSlotValue(slot) != replacement) return false;
            Changed(inventory);
            return true;
        }
        if (Count(inventory, ammo) < cost) return false;
        var removed = new List<(int Slot, int Value, int Count)>();
        int remaining = cost;
        for (int i = 0; i < inventory.SlotsCount && remaining > 0; i++) {
            if (i == slot || inventory.GetSlotValue(i) != ammo) continue;
            int amount = Math.Min(remaining, inventory.GetSlotCount(i));
            int actual = inventory.RemoveSlotItems(i, amount);
            removed.Add((i, ammo, actual)); remaining -= actual;
            if (actual != amount) break;
        }
        if (remaining > 0 || inventory.GetSlotValue(slot) != expected || inventory.RemoveSlotItems(slot, 1) != 1) {
            foreach (var item in removed) inventory.AddSlotItems(item.Slot, item.Value, item.Count);
            return false;
        }
        inventory.AddSlotItems(slot, replacement, 1);
        Changed(inventory);
        return true;
    }
}
