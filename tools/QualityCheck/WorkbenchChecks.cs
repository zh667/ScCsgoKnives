using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Game;
using GameEntitySystem;

// Runs unchanged against the captured old DLL and candidate in separate processes. Traces include wire bytes/ids,
// callback order/results, clock reads and live count; no effects are sent to a real transport or server.
static class WorkbenchChecks {
    const BindingFlags Any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal static readonly List<string> Trace = [];
    internal static int Pending() {
        if (typeof(ScNetWorkbench).GetField("s_pending", Any) is { } old) return ((IDictionary)old.GetValue(null)).Count;
        object requests = typeof(ScNetWorkbench).GetField("s_requests", Any).GetValue(null);
        return (int)requests.GetType().GetProperty("Count", Any).GetValue(requests);
    }
    static void Need(bool ok, string why) { if (!ok) throw new Exception(why); }
    static readonly ScWorkbenchOp Op = new(ScWorkbenchOpKind.Craft, new Point3(2, -3, 4), 12345, 3, 5, 6789, 7, 8, "中文目标", "quote-state");
    sealed class Transport : IScNetTransport {
        public ScNetRole Role { get; set; } = ScNetRole.Client;
        public ScNetHandshake Handshake { get; set; } = ScNetHandshake.Accepted;
        public string HandshakeDetail => "controlled fixture";
        public IReadOnlyList<ScNetPeer> Peers => [];
        public string Name;
        public int Sends, Id;
        public bool Reject, Throw;
        public Action DuringSend;
        public bool IsLocal(ComponentPlayer p) => true;
        public bool SendToServer(ushort op, byte[] payload) {
            Sends++; Id = new ScNetReader(payload).Int();
            Trace.Add($"send:{Name}:{op}:{Convert.ToBase64String(payload)}");
            DuringSend?.Invoke();
            if (Throw) throw new IOException("send fixture");
            return !Reject;
        }
        public bool SendTo(ScNetPeer p, ushort op, byte[] payload) => throw new Exception("unexpected server send");
        public void Broadcast(ushort op, byte[] payload, ScNetPeer except) => throw new Exception("unexpected broadcast");
    }
    sealed class Fixture {
        public double Now = 100;
        public int ClockReads, LocalCalls;
        public Project World = new();
        public Transport Net = new() { Name = "a" };
        public List<(string Name, int Code, string Detail)> Results = [];
        public Fixture() {
            GameManager.m_project = World; ScNet.Attach(Net); ScNetWorkbench.LastResult = null;
            ScNet.Clock = () => { ClockReads++; return Now; };
        }
        public void Request(string name, Action<ScWorkbenchResult> then = null) => ScNetWorkbench.Run(Op, () => { LocalCalls++; return new(17, "local"); }, r => {
            Results.Add((name, r.Code, r.Detail)); Trace.Add($"done:{name}:{r.Code}:{r.Detail}:pending={Pending()}"); then?.Invoke(r);
        });
        public void State(string at) => Trace.Add($"state:{at}:pending={Pending()}:sends={Net.Sends}:local={LocalCalls}:clock={ClockReads}:last={ScNetWorkbench.LastResult?.Code}/{ScNetWorkbench.LastResult?.Detail}");
    }
    static void Answer(int id, int code = 1, string text = "server") => ScNet.ReceiveOnClient(ScNetWorkbench.OpResult, new ScNetWriter().Int(id).Int(code).String(text).ToArray());
    internal static void Run(Action<string, Action> test) {
        ScNetWorkbench.Register();
        void Case(string name, Action<Fixture> act) => test("workbench/" + name, () => {
            Trace.Add("case:" + name);
            var previous = GameManager.m_project; var clock = ScNet.Clock;
            var f = new Fixture();
            try { act(f); f.State("end"); }
            finally { GameManager.m_project = null; ScNetWorkbench.ClearOrphaned(); ScNet.Attach(null); ScNet.Clock = clock; GameManager.m_project = previous; }
            Need(Pending() == 0, "case leaked pending request");
        });
        Case("normal-duplicate-late", f => {
            f.Request("first"); int first = f.Net.Id; f.State("registered"); Answer(first); Answer(first, 2, "duplicate");
            f.Request("next"); int next = f.Net.Id; Answer(first, 3, "late");
            Need(next > first && f.Results.Count == 1 && Pending() == 1 && ScNetWorkbench.LastResult?.Detail == "server", "late response reached new request/LastResult");
            Answer(next); Need(f.Results.Count == 2 && f.Net.Sends == 2 && f.LocalCalls == 0, "normal lifecycle changed");
        });
        Case("timeout-exact-boundary-and-clock-rollback", f => {
            f.Request("deadline"); int id = f.Net.Id; f.Now = 129.999999; ScNetWorkbench.Tick(); f.State("before"); Need(Pending() == 1, "early expiry");
            f.Now = 130; Answer(id); Need(f.Results.Single().Code == -2 && ScNetWorkbench.LastResult is null, "exact deadline accepted response");
            f.Request("rollback"); f.Now = 129; ScNetWorkbench.Tick(); Need(f.Results.Last().Code == -2 && f.Net.Sends == 2, "clock rollback/retry changed");
        });
        Case("send-false-and-send-exception", f => {
            f.Net.Reject = true; f.Request("false"); Need(f.Results.Last().Code == -1 && Pending() == 0, "false is not confirmed unsent");
            f.Net.Reject = false; f.Net.Throw = true; f.Request("throw"); Need(f.Results.Last().Code == -2 && Pending() == 0 && f.Net.Sends == 2, "exception guessed unsent/retried");
        });
        Case("response-during-send-then-exception", f => {
            f.Net.DuringSend = () => Answer(f.Net.Id); f.Net.Throw = true; f.Request("synchronous");
            Need(f.Results.Count == 1 && f.Results[0].Code == 1 && Pending() == 0, "send failure completed twice");
        });
        Case("capacity-and-callback-exception", f => {
            f.Request("throwing", _ => throw new InvalidOperationException("callback fixture"));
            for (int i = 1; i < 65; i++) f.Request("r" + i);
            Need(Pending() == 64 && f.Net.Sends == 64 && f.Results.Single().Code == -1, "64 request limit changed");
            ScNetWorkbench.WorldClosed(f.World);
            Need(f.Results.Count == 65 && f.Results.Skip(1).All(r => r.Code == -2) && Pending() == 0, "callback exception stopped cleanup");
        });
        Case("attach-reentry-and-old-session-clear", f => {
            var old = f.Net; var next = new Transport { Name = "b" };
            f.Request("old", _ => { Need(ReferenceEquals(ScNet.Transport, next), "Attach cancelled before replacing transport"); f.Net = next; f.Request("new"); });
            int oldId = old.Id; ScNet.Attach(next); int newId = next.Id;
            ScNetWorkbench.SessionClosed(old); ScNetWorkbench.SessionClosed(old); Answer(oldId);
            Need(Pending() == 1 && f.Results.Count == 1 && newId > oldId, "stale session cleared new request/reused id");
            ScNet.Attach(next); Need(Pending() == 1, "reattach same session cancelled request"); Answer(newId);
        });
        Case("disconnect-handshake-and-reconnect", f => {
            f.Request("disconnect"); int old = f.Net.Id; f.Net.Handshake = ScNetHandshake.NotApplicable; ScNetWorkbench.Tick();
            f.Request("blocked"); Need(f.Net.Sends == 1 && f.Results.Select(r => r.Code).SequenceEqual(new[] { -2, -1 }), "blocked request sent or old outcome changed");
            f.Net.Handshake = ScNetHandshake.Accepted; f.Request("rejoined"); Answer(old); Need(Pending() == 1, "late disconnect answer matched new request"); Answer(f.Net.Id);
        });
        Case("old-world-clear-and-orphans", f => {
            var old = f.World; f.Request("old"); f.World = new Project(); GameManager.m_project = f.World; f.Request("new");
            ScNetWorkbench.WorldClosed(old); ScNetWorkbench.WorldClosed(old); ScNetWorkbench.ClearOrphaned();
            Need(Pending() == 1 && f.Results.Count == 1 && f.Results[0].Code == -2, "old world cleanup reached new world"); Answer(f.Net.Id);
        });
        Case("result-callback-reenters", f => {
            f.Request("outer", _ => { Need(Pending() == 0, "callback ran before removal"); f.Request("inner"); });
            int outer = f.Net.Id; Answer(outer); int inner = f.Net.Id; Answer(outer); Need(Pending() == 1, "outer response removed inner request"); Answer(inner);
        });
        Case("snapshot-callback-finishes-another-and-registers", f => {
            int first = 0, second = 0; bool nested = false;
            void Reenter(int other) { if (nested) return; nested = true; Answer(other, 9, "nested"); f.Request("fresh"); }
            f.Request("first", _ => Reenter(second)); first = f.Net.Id;
            f.Request("second", _ => Reenter(first)); second = f.Net.Id;
            ScNetWorkbench.WorldClosed(f.World);
            Need(f.Results.Select(r => r.Code).SequenceEqual(new[] { -2, 9 }) && Pending() == 1, "snapshot revisited completed item or included new one");
            Answer(f.Net.Id);
        });
        Case("tick-rechecks-clock-after-callback", f => {
            f.Request("first", _ => f.Now = 100); f.Request("second", _ => f.Now = 100); f.Now = 130; ScNetWorkbench.Tick();
            Need(f.Results.Count == 1 && Pending() == 1, "Tick froze context before reentrant callback"); Answer(f.Net.Id);
        });
        Case("tick-rechecks-world-after-callback", f => {
            var world = f.World; f.Request("first", _ => GameManager.m_project = world); f.Request("second", _ => GameManager.m_project = world);
            GameManager.m_project = new Project(); ScNetWorkbench.Tick(); Need(f.Results.Count == 1 && Pending() == 1, "Tick used one world snapshot"); Answer(f.Net.Id);
        });
        Case("orphan-clear-rechecks-world-after-callback", f => {
            var world = f.World; f.Request("first", _ => GameManager.m_project = world); f.Request("second", _ => GameManager.m_project = world);
            GameManager.m_project = null; ScNetWorkbench.ClearOrphaned(); Need(f.Results.Count == 1 && Pending() == 1, "orphan clear froze world snapshot"); Answer(f.Net.Id);
        });
        foreach (var role in new[] { ScNetRole.Standalone, ScNetRole.Host }) Case("local-" + role, f => {
            f.Net.Role = role; f.Request("local");
            Need(f.LocalCalls == 1 && f.Net.Sends == 0 && f.Results.Single().Code == 17, "local path became remote");
            bool localThrow = false, doneThrow = false;
            try { ScNetWorkbench.Run(Op, () => throw new InvalidOperationException("local"), _ => { }); } catch (InvalidOperationException) { localThrow = true; }
            try { ScNetWorkbench.Run(Op, () => new(1), _ => throw new InvalidOperationException("done")); } catch (InvalidOperationException) { doneThrow = true; }
            Need(localThrow && doneThrow, "direct local exceptions were swallowed");
        });
        Case("malformed-result-does-not-complete", f => {
            f.Request("pending"); int dropped = ScNet.Dropped;
            ScNet.ReceiveOnClient(ScNetWorkbench.OpResult, new ScNetWriter().Int(f.Net.Id).Int(1).String("bad").Byte(99).ToArray());
            Need(ScNet.Dropped == dropped + 1 && Pending() == 1 && f.Results.Count == 0, "decode boundary moved after completion"); Answer(f.Net.Id);
        });
        foreach (string ending in new[] { "response", "timeout", "world", "session", "false", "throw" }) test("workbench/releases-context-" + ending, () => {
            var weak = ReleasedContext(ending); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Need(weak.All(w => !w.IsAlive), "completed request retains callback/world/transport"); Trace.Add("released:" + ending);
        });
        // Last in this fresh process. Exhaustion must not wrap or reset after any cleanup.
        Case("process-id-exhaustion", f => {
            object owner = null; var field = typeof(ScNetWorkbench).GetField("s_next", Any);
            if (field is null) { owner = typeof(ScNetWorkbench).GetField("s_requests", Any).GetValue(null); field = owner.GetType().GetField("m_next", Any); }
            field.SetValue(owner, int.MaxValue - 1); f.Request("last"); Need(f.Net.Id == int.MaxValue, "last id changed"); Answer(f.Net.Id);
            ScNetWorkbench.WorldClosed(f.World); ScNet.Attach(new Transport { Name = "after-exhaustion" }); f.Request("exhausted");
            Need(f.Results.Last().Code == -1 && Pending() == 0 && f.Net.Sends == 1, "id wrapped/reset after reconnect");
        });
    }
    sealed class CallbackContext { public void Done(ScWorkbenchResult result) { } }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference[] ReleasedContext(string ending) {
        ScNet.Attach(null); ScNetWorkbench.ClearOrphaned();
        var world = new Project(); var transport = new Transport { Name = "gc-" + ending, Reject = ending == "false", Throw = ending == "throw" }; var callback = new CallbackContext();
        GameManager.m_project = world; ScNet.Attach(transport); ScNet.Clock = () => 100;
        ScNetWorkbench.Run(Op, () => new(1), callback.Done);
        if (ending == "response") Answer(transport.Id);
        else if (ending == "timeout") { ScNet.Clock = () => 130; ScNetWorkbench.Tick(); }
        else if (ending == "world") ScNetWorkbench.WorldClosed(world);
        else if (ending == "session") ScNetWorkbench.SessionClosed(transport);
        Need(Pending() == 0, "context test request was not completed");
        GameManager.m_project = null; ScNet.Attach(null); ScNet.Clock = () => 0;
        return [new(world), new(transport), new(callback)];
    }
}
