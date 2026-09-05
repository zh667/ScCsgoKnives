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
        if (inventory is not null) for (int i = 0; i < inventory.SlotsCount; i++) if (inventory.GetSlotValue(i) == value) count += inventory.GetSlotCount(i);
        return count;
    }
    public static bool ReplaceWithCost(IInventory inventory, int slot, int expected, int replacement, int ammo, int cost) {
        if (inventory is null || slot < 0 || slot >= inventory.SlotsCount || inventory.GetSlotCount(slot) != 1
            || inventory.GetSlotValue(slot) != expected || inventory.GetSlotCapacity(slot, replacement) < 1 || Count(inventory, ammo) < cost) return false;
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
