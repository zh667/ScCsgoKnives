namespace Game;

/// <summary>Process-owned workbench request lifetimes. World/session tokens are compared by identity; this class
/// neither queries the engine nor knows the wire protocol or player-facing results. One instance lives for the process.</summary>
internal sealed class ScWorkbenchRequests(int capacity, double timeout, Action<Exception> callbackError) {
    // Readers preserve the original short-circuit query order. A completion callback may reenter or change the active
    // world/session/clock between snapshot entries; taking a single environment snapshot would change that behavior.
    internal readonly record struct Context(Func<object> World, Func<object> Session, Func<bool> Remote, Func<bool> Blocked, Func<double> Now);
    sealed record Pending(object World, object Session, double At, Action<int, string> Done);
    readonly Dictionary<int, Pending> m_pending = [];
    int m_next;
    public int Count => m_pending.Count;
    public bool Contains(int id) => m_pending.ContainsKey(id);

    public bool TryRegister(Context context, Action<int, string> done, out int id) {
        id = 0;
        if (m_pending.Count >= capacity || m_next == int.MaxValue) return false;
        id = ++m_next;
        m_pending[id] = new(context.World(), context.Session(), context.Now(), done);
        return true;
    }

    public void Complete(int id, int code, string detail) {
        // Removal precedes arbitrary user code: duplicate/reentrant completions cannot invoke it twice.
        if (!m_pending.Remove(id, out var pending)) return;
        try { pending.Done(code, detail); }
        catch (Exception e) { callbackError(e); }
    }
    public void Tick(Context context, int code, string detail) {
        foreach (var (id, p) in m_pending.ToArray())
            if (!ReferenceEquals(p.World, context.World()) || !ReferenceEquals(p.Session, context.Session())
                || !context.Remote() || context.Blocked() || context.Now() < p.At || context.Now() - p.At >= timeout)
                Complete(id, code, detail);
    }
    public void WorldClosed(object world, int code, string detail) {
        foreach (var (id, p) in m_pending.ToArray()) if (ReferenceEquals(p.World, world)) Complete(id, code, detail);
    }
    public void SessionClosed(object session, int code, string detail) {
        foreach (var (id, p) in m_pending.ToArray()) if (ReferenceEquals(p.Session, session)) Complete(id, code, detail);
    }
    public void ClearOrphaned(Func<object> world, int code, string detail) {
        foreach (var (id, p) in m_pending.ToArray())
            if (world() is null || !ReferenceEquals(p.World, world())) Complete(id, code, detail);
    }
}
