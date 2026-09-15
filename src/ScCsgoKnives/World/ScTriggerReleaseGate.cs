namespace Game;

/// <summary>Selection changes and lost focus require release, not a timeout. Device-independent and per player.</summary>
public sealed class ScTriggerReleaseGate {
    object m_inventory;
    int m_slot = -1, m_value;
    int m_blocked;
    bool m_observed;
    public int AllowedSources { get; private set; }
    public bool Observe(object inventory, int slot, int value, bool down, bool available) =>
        ObserveSources(inventory, slot, value, down ? 1 : 0, available, 1);
    public bool ObserveSources(object inventory, int slot, int value, int down, bool available, int requested) {
        bool changed = !m_observed || !ReferenceEquals(inventory, m_inventory) || slot != m_slot || value != m_value;
        m_observed = true; m_inventory = inventory; m_slot = slot; m_value = value;
        if (!available) m_blocked = int.MaxValue;
        else {
            if (changed) m_blocked |= down;
            m_blocked &= down;
        }
        AllowedSources = available ? down & ~m_blocked : 0;
        return available && (requested == 0 || (requested & ~m_blocked) != 0);
    }
}
