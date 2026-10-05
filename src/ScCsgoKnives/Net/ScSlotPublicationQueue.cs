namespace Game;

/// <summary>One frame's slot publication bookkeeping. Tokens are resolved backing stores, compared by reference.
/// Transport capability, world/role checks and provider resolution belong to ScNetSlots, not this queue.</summary>
internal sealed class ScSlotPublicationQueue {
    readonly HashSet<object> m_touched = new(ReferenceEqualityComparer.Instance);
    readonly List<object> m_due = [];
    public bool HasTouched => m_touched.Count != 0;
    public bool HasDue => m_due.Count != 0;
    public void Touch(object storage) => m_touched.Add(storage);
    public bool Untouch(object storage) => m_touched.Remove(storage);
    public void ClearTouched() => m_touched.Clear();
    public void Enqueue(object storage) {
        for (int i = 0; i < m_due.Count; i++) if (ReferenceEquals(m_due[i], storage)) return;
        m_due.Add(storage);
    }
    public object[] TakeDue() { var due = m_due.ToArray(); m_due.Clear(); return due; }
    public void Clear() { m_touched.Clear(); m_due.Clear(); }
}
