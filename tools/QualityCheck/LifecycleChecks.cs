using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Game;
using GameEntitySystem;

static class LifecycleChecks {
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static T Blank<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    static void Need(bool condition, string why) => TravelChecks.Require(condition, why);
    sealed class Transport : IScNetTransport {
        public ScNetRole Role { get; set; } = ScNetRole.Client;
        public ScNetHandshake Handshake { get; set; } = ScNetHandshake.Accepted;
        public string HandshakeDetail => "lifecycle fixture";
        public IReadOnlyList<ScNetPeer> Peers => [];
        public bool IsLocal(ComponentPlayer p) => true;
        public int Sent, Request; public bool Reject;
        public bool SendToServer(ushort op, byte[] payload) { Sent++; if (op == 50) Request = new ScNetReader(payload).Int(); return !Reject; }
        public bool SendTo(ScNetPeer p, ushort op, byte[] payload) => true;
        public void Broadcast(ushort op, byte[] payload, ScNetPeer except) { }
    }
    internal static void Run(Action<string, Action> test) {
        ScNetWorkbench.Register();
        void Tick() { var tick = typeof(ScNetWorkbench).GetMethod("Tick"); Need(tick is not null, "no timeout/lifecycle tick"); tick.Invoke(null, null); }
        int Pending() => WorkbenchChecks.Pending();
        void Request(Action<ScWorkbenchResult> done) => ScNetWorkbench.Run(new(ScWorkbenchOpKind.Craft, new Point3()), () => throw new Exception("client ran local effect"), done);
        void Response(int id) => ScNet.ReceiveOnClient(ScNetWorkbench.OpResult, new ScNetWriter().Int(id).Int(1).String("").ToArray());
        void Case(string name, Action<Transport, Project> body) => test(name, () => {
            var previousProject = GameManager.m_project; var previousClock = ScNet.Clock;
            var t = new Transport(); var p = new Project(); GameManager.m_project = p; ScNet.Attach(t);
            try { body(t, p); }
            finally { GameManager.m_project = null; new ScCsgoKnivesModLoader().OnProjectDisposed(); ScNet.Attach(null); ScNet.Clock = previousClock; GameManager.m_project = previousProject; }
        });
        Case("F4/normal-answer-once", (t, p) => { int count = 0; Request(r => { Need(r.Code == 1, "not server success"); count++; }); Response(t.Request); Response(t.Request); Need(count == 1 && Pending() == 0 && t.Sent == 1, "duplicate callback/request"); });
        Case("F4/world-exit-unknown", (t, p) => {
            int count = 0; ScWorkbenchResult result = default; Request(r => { result = r; count++; });
            GameManager.m_project = null; new ScCsgoKnivesModLoader().OnProjectDisposed(); new ScCsgoKnivesModLoader().OnProjectDisposed();
            Need(Pending() == 0 && count == 1 && result.Code == -2 && t.Sent == 1, "pending closure retained or outcome misclassified");
        });
        Case("F4/timeout-late-result-no-retry", (t, p) => {
            double now = 10; ScNet.Clock = () => now; int callbacks = 0; ScWorkbenchResult result = default;
            Request(r => { callbacks++; result = r; }); int old = t.Request; now += 31; Tick(); Response(old);
            Need(Pending() == 0 && callbacks == 1 && result.Code == -2 && t.Sent == 1, "timeout not unknown/exactly once");
            int fresh = 0; Request(_ => fresh++); Response(old); Need(fresh == 0 && Pending() == 1, "late result completed new request"); Response(t.Request); Need(fresh == 1, "new session request stuck");
        });
        Case("F4/disconnect-and-rejoin", (t, p) => {
            int old = 0; Request(r => { Need(r.Code == -2, "disconnect guessed no execution"); old++; });
            t.Handshake = ScNetHandshake.NotApplicable; Tick(); Need(old == 1 && Pending() == 0, "disconnect retained request");
            t.Handshake = ScNetHandshake.Accepted; int fresh = 0; Request(_ => fresh++); Response(t.Request); Need(fresh == 1, "rejoin cannot request");
        });
        Case("F4/definite-send-rejection", (t, p) => { t.Reject = true; ScWorkbenchResult result = default; Request(r => result = r); Need(result.Code == -1 && Pending() == 0, "definite rejection misclassified"); });
        Case("F4/old-world-cleanup-keeps-new-world", (t, p) => {
            int old = 0, fresh = 0; Request(_ => old++); var next = new Project(); GameManager.m_project = next; Request(_ => fresh++);
            var close = typeof(ScNetWorkbench).GetMethod("WorldClosed"); Need(close is not null, "no owner-specific cleanup"); close.Invoke(null, [p]); close.Invoke(null, [p]);
            Need(old == 1 && fresh == 0 && Pending() == 1, "old cleanup affected fresh request"); Response(t.Request); Need(fresh == 1, "fresh result lost");
        });
        Case("F4/bounded-pending-and-throwing-callback", (t, p) => {
            int completed = 0; Request(_ => throw new Exception("disposed UI"));
            for (int i = 0; i < 100; i++) Request(_ => completed++);
            Need(Pending() <= 64 && t.Sent <= 64, "pending requests unbounded");
            GameManager.m_project = null; new ScCsgoKnivesModLoader().OnProjectDisposed();
            Need(Pending() == 0 && completed == 100, "one callback blocked other cleanup");
        });
        test("F5/departed-action-cleanup-idempotent", () => {
            ScNet.Attach(null); BlocksManager.BlockTypeToIndex[typeof(ScGunBlock)] = 701; BlocksManager.BlockNameToIndex[nameof(ScGunBlock)] = 701;
            var behavior = new SubsystemScGunBlockBehavior(); var registry = new ScGunRegistry();
            void Set(string name, object value) => typeof(SubsystemScGunBlockBehavior).GetField(name, Any).SetValue(behavior, value);
            Set("m_saveReady", true); Set("m_registry", registry); Set("m_players", new SubsystemPlayers()); Set("m_time", new SubsystemTime { m_gameTime = 1000 }); Set("m_recoveryAt", double.PositiveInfinity); Set("m_duplicateScanAt", double.PositiveInfinity);
            var player = Blank<ComponentPlayer>(); player.PlayerData = Blank<PlayerData>();
            var states = (IDictionary)typeof(SubsystemScGunBlockBehavior).GetField("m_states", Any).GetValue(behavior);
            var stateType = typeof(SubsystemScGunBlockBehavior).GetNestedType("GunState", Any); var state = Activator.CreateInstance(stateType, true);
            var inventory = new TravelChecks.Inventory(); var reload = new ScReloadTransaction(inventory, 0, 100, 900, 2, 30);
            stateType.GetField("Reload").SetValue(state, reload); stateType.GetField("BusyUntil").SetValue(state, 1d); states.Add(player, state); behavior.SetFireButton(player, true);
            behavior.Update(.016f); behavior.Update(.016f);
            Need(behavior.ReadyForTravel && states.Count == 0 && !behavior.FireButtonDown(player) && reload.Cancelled, "departed action retained");
            Need(inventory.Total(100) == 1 && inventory.Total(900) == 5 && registry.Count == 0 && registry.Recovery.Count == 0, "uncommitted reload granted/lost resources");
            var previous = ScGunRegistry.Current; ScGunRegistry.Current = registry; registry.RecoveryOwner = _ => "player/0";
            try {
                int id = registry.Allocate(0, 7, false, 640, 1500);
                inventory.Values[0] = Terrain.MakeBlockValue(701, 0, GunSpec.WithId(0, id));
                var inserted = new ScReloadTransaction(inventory, 0, inventory.Values[0], 900, 2, 30, ScGunHolders.Key(inventory, 0));
                Need(inserted.Discard() && inserted.InsertMagazine(), "fixture reload did not commit");
                string row = registry.NetworkRow(id, 0); int material = inventory.Total(900);
                stateType.GetField("Reload").SetValue(state, inserted); states.Add(player, state); behavior.Update(.016f);
                Need(inserted.Cancelled && inserted.Inserted && registry.NetworkRow(id, 0) == row && inventory.Total(900) == material, "departed committed reload was undone/refunded");
            } finally { ScGunRegistry.Current = previous; }
            registry.Recovery.Grant("player/0", [(900, 2)], "existing debt"); Need(!behavior.ReadyForTravel && registry.Recovery.Count == 1, "real obligation incorrectly cleared");
            var rejoined = Blank<ComponentPlayer>(); rejoined.PlayerData = Blank<PlayerData>(); rejoined.PlayerData.PlayerIndex = player.PlayerData.PlayerIndex;
            var freshState = Activator.CreateInstance(stateType, true); stateType.GetField("BusyUntil").SetValue(freshState, 10d); states.Add(rejoined, freshState); behavior.SetFireButton(rejoined, true);
            typeof(SubsystemScGunBlockBehavior).GetMethod("PlayerLeft", Any).Invoke(behavior, [player]);
            Need(states.Count == 1 && states.Contains(rejoined) && behavior.FireButtonDown(rejoined), "old player cleanup cleared new component of same index");
        });
        Case("F9/dispose-releases-world-owners", (t, p) => {
            t.Role = ScNetRole.Host; var registry = new ScGunRegistry(); ScGunRegistry.Current = registry;
            var armor = new SubsystemScArmor { m_project = p }; p.m_subsystems.Add(armor);
            ScNetMirror.RecordsTick(registry, 0); ScNetMirror.ArmorTick(armor); p.Dispose(); GameManager.m_project = null;
            new ScCsgoKnivesModLoader().OnProjectDisposed(); new ScCsgoKnivesModLoader().OnProjectDisposed();
            foreach (string field in new[] { "s_registryOwner", "s_armorOwner", "s_wantsOwner" }) Need(typeof(ScNetMirror).GetField(field, Any).GetValue(null) is null, "retained " + field);
        });
        Case("F9/old-dispose-keeps-new-host-and-dormant-records", (t, p) => {
            t.Role = ScNetRole.Host; var old = new SubsystemScArmor { m_project = p }; p.m_subsystems.Add(old);
            var previous = new ScGunRegistry(); ScGunRegistry.Current = previous; ScNetMirror.RecordsTick(previous, 0); ScNetMirror.ArmorTick(old);
            var nextWorld = new Project(); var next = new ScGunRegistry(); next.Allocate(0, 7, false, 640, 1500);
            var armor = new SubsystemScArmor { m_project = nextWorld }; nextWorld.m_subsystems.Add(armor);
            GameManager.m_project = nextWorld; ScGunRegistry.Current = next; ScNetMirror.RecordsTick(next, 0); ScNetMirror.ArmorTick(armor);
            p.Dispose(); typeof(ScNetMirror).GetMethod("ReleaseRegistry").Invoke(null, [previous]); new ScCsgoKnivesModLoader().OnProjectDisposed();
            Need(ReferenceEquals(typeof(ScNetMirror).GetField("s_registryOwner", Any).GetValue(null), next)
                && ReferenceEquals(typeof(ScNetMirror).GetField("s_armorOwner", Any).GetValue(null), armor) && next.Count == 1, "new host owner/records cleared");
            nextWorld.Dispose(); GameManager.m_project = null; ScNet.Attach(null); new ScCsgoKnivesModLoader().OnProjectDisposed();
            Need(typeof(ScNetMirror).GetField("s_registryOwner", Any).GetValue(null) is null && next.Count == 1, "host to standalone retains world or resets data");
        });
    }
}
