namespace Game;

/// <summary>Single game-thread release transaction; cancelled preparations have no cost.</summary>
public sealed class ScThrowTransaction {
    readonly IInventory m_inventory;
    readonly int m_slot, m_value;
    readonly long m_revision;
    bool m_finished;
    public ScThrowTransaction(IInventory inventory) {
        m_inventory = inventory; m_slot = inventory.ActiveSlotIndex; m_value = inventory.GetSlotValue(m_slot);
        m_revision = ScInventoryTransaction.Revision(inventory);
    }
    public bool Valid => !m_finished && m_inventory.ActiveSlotIndex == m_slot && m_inventory.GetSlotCount(m_slot) > 0
        && m_inventory.GetSlotValue(m_slot) == m_value && ScInventoryTransaction.Revision(m_inventory) == m_revision;
    public void Cancel() => m_finished = true;
    public bool Commit(bool creative, Func<bool> capacity, Func<bool> spawn) {
        if (!Valid || !capacity()) { Cancel(); return false; }
        m_finished = true;
        if (!creative && m_inventory.RemoveSlotItems(m_slot, 1) != 1) return false;
        bool spawned = false;
        try { spawned = spawn(); return spawned; }
        finally {
            if (!creative && !spawned) m_inventory.AddSlotItems(m_slot, m_value, 1);
            ScInventoryTransaction.Changed(m_inventory);
        }
    }
}
