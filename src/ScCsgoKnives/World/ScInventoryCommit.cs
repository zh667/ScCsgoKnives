using System.Threading;
namespace Game;

/// <summary>Shared exclusion for inventory writes, compensation and save snapshots. No gun rules live here.</summary>
internal static class ScInventoryCommit {
    static int s_active;
    public static bool Active => Volatile.Read(ref s_active) != 0;
    public static bool TryEnter() => Interlocked.CompareExchange(ref s_active, 1, 0) == 0;
    public static void Exit() => Volatile.Write(ref s_active, 0);
}
