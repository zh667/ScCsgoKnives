namespace Game;

public interface IScResourceCache { string Name { get; } int Count { get; } void Clear(); }

/// <summary>Managed resources only. Eviction drops references, never disposes shared GPU objects.</summary>
public sealed class ScResourceCache<TKey, TValue> : IScResourceCache where TKey : notnull {
    sealed record Entry(TKey Key, TValue Value) { public long Used; }
    readonly Dictionary<TKey, LinkedListNode<Entry>> m_entries = new();
    readonly LinkedList<Entry> m_recent = new();
    readonly Func<long> m_clock;
    readonly long m_idle;
    public string Name { get; }
    public int Capacity { get; }
    public int Count => m_entries.Count;
    public ScResourceCache(string name, int capacity, long idleMilliseconds = 0, Func<long> clock = null) {
        if (capacity < 1 || idleMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        Name = name; Capacity = capacity; m_idle = idleMilliseconds; m_clock = clock ?? (() => Environment.TickCount64);
        ScResourceCaches.Register(this);
    }
    public bool TryGetValue(TKey key, out TValue value) {
        if (m_entries.TryGetValue(key, out var node)) {
            node.Value.Used = m_clock(); m_recent.Remove(node); m_recent.AddLast(node);
            value = node.Value.Value; Trim(); return true;
        }
        value = default; return false;
    }
    public TValue this[TKey key] {
        get => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException();
        set {
            if (m_entries.Remove(key, out var old)) m_recent.Remove(old);
            m_entries[key] = m_recent.AddLast(new Entry(key, value) { Used = m_clock() }); Trim();
        }
    }
    public void Trim() {
        long now = m_clock();
        // A visible pile of dropped weapons may exceed the soft model limit.
        // Keep resources used in the last two seconds rather than rebuilding them every frame.
        while (Count > Capacity && m_recent.First is { } first && now - first.Value.Used >= m_idle) {
            m_entries.Remove(first.Value.Key); m_recent.RemoveFirst();
        }
    }
    public void Clear() { m_entries.Clear(); m_recent.Clear(); }
}

public static class ScResourceCaches {
    static readonly List<WeakReference<IScResourceCache>> Caches = [];
    internal static void Register(IScResourceCache cache) => Caches.Add(new(cache));
    public static Dictionary<string, int> Counts() {
        var result = new Dictionary<string, int>();
        for (int i = Caches.Count - 1; i >= 0; i--) {
            if (Caches[i].TryGetTarget(out var cache)) result[cache.Name] = cache.Count;
            else Caches.RemoveAt(i);
        }
        return result;
    }
    public static void ClearAll() {
        for (int i = Caches.Count - 1; i >= 0; i--) {
            if (Caches[i].TryGetTarget(out var cache)) cache.Clear();
            else Caches.RemoveAt(i);
        }
    }
}
