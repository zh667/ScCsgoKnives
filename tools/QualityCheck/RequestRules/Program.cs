using Game;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 1) return 2;
var results = new List<object>(); int failed = 0;
void Check(string name, Action action) {
    try { action(); results.Add(new { name, passed = true, detail = "" }); }
    catch (Exception e) { failed++; results.Add(new { name, passed = false, detail = e.ToString() }); }
}
void Need(bool condition, string why) { if (!condition) throw new Exception(why); }
Check("capacity/once/id-retention", () => {
    var f = new Fixture(2); int calls = 0;
    int one = f.Add((_, _) => calls++), two = f.Add((_, _) => calls++);
    Need(!f.Requests.TryRegister(f.Context, (_, _) => { }, out _) && f.Requests.Count == 2, "capacity ignored");
    f.Requests.Complete(one, 7, "reply"); f.Requests.Complete(one, 8, "duplicate");
    f.Requests.WorldClosed(f.World, -2, "unknown"); f.Requests.SessionClosed(f.Session, -2, "unknown");
    int three = f.Add((_, _) => calls++);
    Need(calls == 2 && one < two && two < three && f.Requests.Count == 1, "completion/reset mismatch");
});
Check("timeout-inclusive/clock-reversal", () => {
    var f = new Fixture(); int result = 0;
    f.Add((code, _) => result = code); f.Now = 129.999999; f.Tick(); Need(result == 0, "too early");
    f.Now = 130; f.Tick(); Need(result == -2 && f.Requests.Count == 0, "deadline not inclusive");
    result = 0; f.Add((code, _) => result = code); f.Now = 129; f.Tick(); Need(result == -2, "clock reversal ignored");
});
Check("ownership-is-object-identity", () => {
    var f = new Fixture(); int calls = 0;
    f.World = new EqualToken(); f.Session = new EqualToken(); f.Add((_, _) => calls++);
    f.Requests.WorldClosed(new EqualToken(), -2, "old"); f.Requests.SessionClosed(new EqualToken(), -2, "old");
    Need(calls == 0, "value equality substituted for owner identity");
    f.World = new EqualToken(); f.Tick(); Need(calls == 1, "changed identity retained request");
});
Check("reentrant-completion-before-callback-and-snapshot", () => {
    var f = new Fixture(); int id = 0, nested = 0, calls = 0;
    id = f.Add((_, _) => { calls++; Need(f.Requests.Count == 0, "not removed before callback"); f.Requests.Complete(id, 1, "again"); nested = f.Add((_, _) => calls++); });
    f.Requests.WorldClosed(f.World, -2, "unknown");
    Need(calls == 1 && nested > id && f.Requests.Contains(nested), "cleanup included newly registered request");
});
Check("environment-read-order-and-reentry", () => {
    var f = new Fixture(); int calls = 0;
    f.Add((_, _) => { calls++; f.Now = 100; }); f.Add((_, _) => { calls++; f.Now = 100; });
    f.Reads.Clear(); f.Now = 130; f.Tick();
    Need(calls == 1 && f.Requests.Count == 1 && string.Join(",", f.Reads) == "world,session,remote,blocked,now,now,world,session,remote,blocked,now,now", "short circuit or per-entry reads changed");
    f.Reads.Clear(); f.World = new object(); f.Tick(); Need(string.Join(",", f.Reads) == "world", "world mismatch queried downstream environment");
});
Check("callback-exceptions-do-not-stop-cleanup", () => {
    var f = new Fixture(); int calls = 0;
    f.Add((_, _) => throw new Exception("first")); f.Add((_, _) => calls++); f.Tick(); Need(f.Requests.Count == 2, "unexpired removed");
    f.Blocked = true; f.Tick(); Need(f.Errors == 1 && calls == 1 && f.Requests.Count == 0, "exception interrupted cleanup");
});
Check("orphan-clear-observes-callback-world-change", () => {
    var f = new Fixture(); object world = f.World; int calls = 0;
    f.Add((_, _) => { calls++; f.World = world; }); f.Add((_, _) => { calls++; f.World = world; });
    f.World = null; f.Requests.ClearOrphaned(f.Context.World, -2, "unknown");
    Need(calls == 1 && f.Requests.Count == 1, "current world frozen for batch");
});
Check("process-exhaustion-never-wraps", () => {
    var f = new Fixture(); typeof(ScWorkbenchRequests).GetField("m_next", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(f.Requests, int.MaxValue - 1);
    int id = f.Add((_, _) => { }); f.Requests.Complete(id, 1, "ok");
    Need(id == int.MaxValue && !f.Requests.TryRegister(f.Context, (_, _) => { }, out _), "id wrapped after completion");
});
Check("completed-context-collectible-while-manager-lives", () => {
    var (requests, weak) = GarbageCase.Make(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    Need(requests.Count == 0 && weak.All(w => !w.IsAlive), "table retains context"); GC.KeepAlive(requests);
});
BoundaryRuleChecks.Run(Check);
var references = Assembly.GetExecutingAssembly().GetReferencedAssemblies().Select(a => a.Name).Order().ToArray();
Check("no-engine-or-core-assembly-required", () => Need(references.All(n => n.StartsWith("System") || n == "Microsoft.CSharp"), "engine dependency entered rules"));
var report = new { count = results.Count, failed, results, references, artifact = Assembly.GetExecutingAssembly().Location,
    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location))), scope = "exact production lifecycle source; .NET base library only, no engine setup" };
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));
File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"RequestRules: {results.Count - failed}/{results.Count}");
return failed == 0 ? 0 : 1;

sealed class EqualToken { public override bool Equals(object other) => other is EqualToken; public override int GetHashCode() => 1; }
sealed class Fixture {
    public object World = new(), Session = new(); public double Now = 100; public bool Remote = true, Blocked; public int Errors;
    public readonly ScWorkbenchRequests Requests; public readonly ScWorkbenchRequests.Context Context; public readonly List<string> Reads = [];
    public Fixture(int capacity = 64) {
        Requests = new(capacity, 30, _ => Errors++);
        Context = new(() => { Reads.Add("world"); return World; }, () => { Reads.Add("session"); return Session; },
            () => { Reads.Add("remote"); return Remote; }, () => { Reads.Add("blocked"); return Blocked; }, () => { Reads.Add("now"); return Now; });
    }
    public int Add(Action<int, string> done) { if (!Requests.TryRegister(Context, done, out int id)) throw new Exception("registration failed"); return id; }
    public void Tick() => Requests.Tick(Context, -2, "unknown");
}
static class GarbageCase {
    sealed class Callback { public void Done(int code, string detail) { } }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static (ScWorkbenchRequests, WeakReference[]) Make() {
        var requests = new ScWorkbenchRequests(64, 30, _ => { }); object world = new(), session = new(); var callback = new Callback();
        var context = new ScWorkbenchRequests.Context(() => world, () => session, () => true, () => false, () => 0);
        requests.TryRegister(context, callback.Done, out int id); requests.Complete(id, 1, "ok");
        return (requests, [new(world), new(session), new(callback)]);
    }
}
