namespace Game;

/// <summary>Receipts are based on observed inventory deltas in finally, even when a container mutates then
/// throws. All material, ammo and gun operations use this one journal; no nested opaque inventory helper.</summary>
internal sealed class ScGunInventoryJournal(IInventory inventory, Action<IInventory> beforeWrite) {
    readonly List<ScGunUndo> m_undo = [];
    static int Quantity(IInventory inv, int slot, int value) => inv.GetSlotValue(slot) == value ? inv.GetSlotCount(slot) : 0;
    // Every journaled slot write is noted for the multiplayer slot publication (ScNetSlots): it goes out when the
    // transaction ends, with its final state, whether it committed or was rolled back.
    public void RemoveExact(int slot, int value, int amount) {
        int before = Quantity(inventory, slot, value), reported = 0;
        ScInventoryWriteReceipt receipt = default;
        if (amount <= 0 || before < amount) throw new InvalidOperationException("Inventory deduction no longer available");
        beforeWrite(inventory);
        try { reported = inventory.RemoveSlotItems(slot, amount); }
        finally {
            receipt = ScInventoryWriteReceipt.Removal(amount, reported, before, Quantity(inventory, slot, value));
            if (receipt.Observed > 0) m_undo.Add(new ScGunUndo { Slot = slot, Value = value, Count = receipt.Observed });
        }
        if (!receipt.Exact) throw new InvalidOperationException("Inventory deducted an unexpected amount");
    }
    public void AddExact(int slot, int value, int amount) {
        int before = Quantity(inventory, slot, value);
        ScInventoryWriteReceipt receipt = default;
        if (inventory.GetSlotCount(slot) > 0 && inventory.GetSlotValue(slot) != value) throw new InvalidOperationException("Replacement slot occupied");
        beforeWrite(inventory);
        try { inventory.AddSlotItems(slot, value, amount); }
        finally {
            receipt = ScInventoryWriteReceipt.Addition(amount, before, Quantity(inventory, slot, value));
            if (receipt.Observed > 0) m_undo.Add(new ScGunUndo { Slot = slot, Value = value, Count = -receipt.Observed });
        }
        if (!receipt.Exact) throw new InvalidOperationException("Inventory accepted an unexpected amount");
    }
    public void ReplaceCreative(int slot, int expected, int replacement) {
        beforeWrite(inventory);
        try { inventory.AddSlotItems(slot, replacement, 1); }
        finally {
            if (inventory.GetSlotValue(slot) == replacement && replacement != expected)
                m_undo.Add(new ScGunUndo { Slot = slot, Value = expected, Count = 1, Creative = true, ReplacedValue = replacement });
        }
        if (inventory.GetSlotValue(slot) != replacement) throw new InvalidOperationException("Creative slot rejected replacement");
    }
    public void Rollback(ScGunRecovery recovery, string owner) {
        m_undo.Reverse();
        if (!ScGunRecovery.Apply(inventory, m_undo)) recovery.Enqueue(owner, m_undo);
        m_undo.Clear();
    }
    public void DeferRollback(ScGunRecovery recovery,string owner) {
        m_undo.Reverse();recovery.Enqueue(owner,m_undo);m_undo.Clear();
    }
}
