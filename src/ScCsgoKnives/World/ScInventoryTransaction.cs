using System.Runtime.CompilerServices;

namespace Game;

/// <summary>Single game-thread transaction. All slot capacities and inputs checked before mutation.</summary>
public static class ScInventoryTransaction {
    sealed class Epoch { public long Value; }
    static readonly ConditionalWeakTable<object, Epoch> Epochs = new();
    public static long Revision(IInventory inventory) => inventory is null ? -1 : Epochs.GetOrCreateValue(ScInventoryIdentity.Storage(inventory)).Value;
    /// <summary>A CS transaction on this inventory ended. The epoch is this process's own stale-quote guard, not a network
    /// version; slots the transaction rewrote are published by ScNetSlots (multiplayer host only).</summary>
    public static void Changed(IInventory inventory) {
        if (inventory is null) return;
        Epochs.GetOrCreateValue(ScInventoryIdentity.Storage(inventory)).Value++;
        ScNetSlots.TransactionEnded(inventory);
    }
    public static int Count(IInventory inventory, int value) {
        inventory = ScInventoryIdentity.Inventory(inventory);
        int count = 0;
        if (inventory is not null) for (int i = 0; i < inventory.SlotsCount; i++) if (inventory.GetSlotValue(i) == value) count = (int)Math.Min(int.MaxValue, (long)count + inventory.GetSlotCount(i));
        return count;
    }
    public static bool IsWeaponSlot(IInventory inventory, int slot) {
        inventory = ScInventoryIdentity.Inventory(inventory);
        if (inventory is null || slot < 0 || slot >= inventory.SlotsCount) return false;
        // Creative slots represent an infinite source, not a stack of guns.
        // Only writable hotbar/backpack slots can hold changing weapon state.
        return inventory is ComponentCreativeInventory
            ? slot < Math.Min(10, inventory.SlotsCount) && inventory.GetSlotCount(slot) > 0
            : inventory.GetSlotCount(slot) == 1;
    }
    /// <summary>More than one CS gun (or gun template) in one ordinary slot. CS guns answer a stacking of 1 themselves; a
    /// container that sets its own capacity regardless (Sushi's boxes with its stacking options on) can still hold such a
    /// stack. It is kept as it is and cannot be used until it is spread out.</summary>
    public static bool IsGunStack(IInventory inventory, int slot) {
        inventory = ScInventoryIdentity.Inventory(inventory);
        if (inventory is null or ComponentCreativeInventory || slot < 0 || slot >= inventory.SlotsCount || inventory.GetSlotCount(slot) <= 1) return false;
        int value = inventory.GetSlotValue(slot);
        return BlocksManager.BlockTypeToIndex.TryGetValue(typeof(ScGunBlock), out int gun) && Terrain.ExtractContents(value) == gun
            || ScGunSkinTemplateBlock.IsTemplate(value) || ScGunCounterTemplateBlock.IsTemplate(value);
    }
    public static bool ReplaceWithCost(IInventory inventory, int slot, int expected, int replacement, int ammo, int cost) {
        var source = inventory;
        inventory = ScInventoryIdentity.Inventory(inventory);
        var registry = ScGunRegistry.Current;
        if (ScNet.IsRemoteClient || inventory is null || registry is null || registry.Disabled || !ScInventoryCommit.TryEnter()) return false;
        var journal = new ScGunInventoryJournal(inventory);
        string owner = null;
        string Owner() => registry.RecoveryOwner is { } resolve ? resolve(inventory) : ScGunHolders.RecoveryOwner(inventory.Project, inventory);
        bool SameDestination() => ReferenceEquals(registry, ScGunRegistry.Current) && !registry.Disabled
            && ReferenceEquals(inventory, ScInventoryIdentity.Inventory(source)) && Owner() == owner;
        try {
            // Refuse before taking anything unless failed inverse writes can be retained for this exact owner.
            owner = Owner();
            if (string.IsNullOrWhiteSpace(owner) || registry.Recovery.HasPending(owner) || cost < 0
                || !IsWeaponSlot(inventory, slot) || inventory.GetSlotValue(slot) != expected
                || inventory.GetSlotCapacity(slot, replacement) < 1 || !SameDestination()) return false;
            bool creative = inventory is ComponentCreativeInventory;
            if (creative && cost != 0) return false;
            long available = 0;
            for (int i = 0; i < inventory.SlotsCount; i++)
                if (i != slot && inventory.GetSlotValue(i) == ammo) available += inventory.GetSlotCount(i);
            if (available < cost) return false;
            int remaining = cost;
            for (int i = 0; i < inventory.SlotsCount && remaining > 0; i++) {
                if (i == slot || inventory.GetSlotValue(i) != ammo || inventory.GetSlotCount(i) <= 0) continue;
                int amount = Math.Min(remaining, inventory.GetSlotCount(i));
                journal.RemoveExact(i, ammo, amount); remaining -= amount;
                if (!SameDestination()) throw new InvalidOperationException("Replacement destination changed during deduction");
            }
            if (remaining != 0 || !SameDestination() || !IsWeaponSlot(inventory, slot) || inventory.GetSlotValue(slot) != expected)
                throw new InvalidOperationException("Replacement inputs changed");
            if (replacement != expected) {
                if (creative) journal.ReplaceCreative(slot, expected, replacement);
                else {
                    journal.RemoveExact(slot, expected, 1);
                    if (!SameDestination()) throw new InvalidOperationException("Replacement destination changed during removal");
                    journal.AddExact(slot, replacement, 1);
                }
            }
            if (!SameDestination() || inventory.GetSlotValue(slot) != replacement || !IsWeaponSlot(inventory, slot))
                throw new InvalidOperationException("Replacement was not retained by the original destination");
            ScNetGuns.SlotRewritten(inventory, slot, expected, replacement);
            Changed(inventory);
            return true;
        }
        catch (Exception e) {
            // Refund only to the pinned, still provable owner. A vanished/recreated channel retains a durable claim.
            bool canReturn = false;
            try { canReturn = ReferenceEquals(registry, ScGunRegistry.Current) && Owner() == owner; } catch (Exception) { }
            if (canReturn) journal.Rollback(registry.Recovery, owner);
            else journal.DeferRollback(registry.Recovery, owner);
            KnifeLog.Warning("inventory replacement rejected: " + e.Message);
            return false;
        }
        finally { ScInventoryCommit.Exit(); }
    }
}
