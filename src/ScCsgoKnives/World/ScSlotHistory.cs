namespace Game;

/// <summary>Remembers which hotbar slot a player held before the current one (F02, 0.29.1).
/// A slot only counts as "held" once it stayed active for <see cref="Dwell"/> seconds, so scrolling
/// the wheel through intermediate slots on the way to a grenade, or blinking through another slot and
/// back, does not overwrite the item the player really had in hand before the grenade.</summary>
public sealed class ScSlotHistory {
    public const double Dwell = .3; // 估计: a wheel step passes a slot well under this, a deliberate selection stays well over
    readonly List<int> m_held = []; // slots held for at least Dwell, oldest first, each at most once
    public int Current { get; private set; } = -1;
    public double Since { get; private set; }
    /// <summary>Most recently held slot other than the current one, or -1.</summary>
    public int Previous { get { for (int i = m_held.Count - 1; i >= 0; i--) if (m_held[i] != Current) return m_held[i]; return -1; } }
    public void Observe(int slot, double now) {
        if (slot == Current) return;
        if (Current >= 0 && now - Since >= Dwell) { m_held.Remove(Current); m_held.Add(Current); }
        Current = slot; Since = now;
    }
}
