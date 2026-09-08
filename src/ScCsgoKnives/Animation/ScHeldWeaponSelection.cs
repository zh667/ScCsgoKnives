namespace Game;

/// <summary>Observe selection without observing mutable ammo, wear, finish or silencer fields.
/// Fresh gun allocation in the same slot is materialization, not a second equip.</summary>
public sealed class ScHeldWeaponSelection {
    object m_inventory;
    int m_slot, m_contents, m_data;
    bool m_observed;
    public void Reset() => m_observed = false;
    public bool Observe(object inventory, int slot, int value, bool gun) {
        int contents = Terrain.ExtractContents(value), data = Terrain.ExtractData(value);
        bool same = ReferenceEquals(inventory, m_inventory) && slot == m_slot && contents == m_contents;
        bool allocated = gun && GunSpec.IsFresh(m_data) && !GunSpec.IsFresh(data) && GunSpec.GetVariant(m_data) == GunSpec.GetVariant(data);
        bool changed = m_observed && (!same || (m_data != data && !allocated));
        m_inventory = inventory; m_slot = slot; m_contents = contents; m_data = data; m_observed = true;
        return changed;
    }
}
