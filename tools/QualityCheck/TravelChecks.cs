using System.Reflection;
using Engine;
using Game;
using GameEntitySystem;

static class TravelChecks {
    internal static void Require(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    internal sealed class Inventory : IInventory {
        public Project Project => null;
        public int SlotsCount => 4;
        public int VisibleSlotsCount { get; set; } = 4;
        public int ActiveSlotIndex { get; set; }
        public int[] Values = [100, 900, 0, 0], Counts = [1, 5, 0, 0];
        public Func<int, int, int, bool> BeforeAdd;
        public Action<int, int, int> AfterAdd, AfterRemove;
        public int GetSlotValue(int s) => Counts[s] > 0 ? Values[s] : 0;
        public int GetSlotCount(int s) => Counts[s];
        public int GetSlotCapacity(int s, int v) => s == 0 ? 1 : 40;
        public int GetSlotProcessCapacity(int s, int v) => 0;
        public void AddSlotItems(int s, int v, int n) {
            if (BeforeAdd?.Invoke(s, v, n) == false) return;
            if (Counts[s] > 0 && Values[s] != v) throw new Exception("occupied");
            Values[s] = v; Counts[s] += n; AfterAdd?.Invoke(s, v, n);
        }
        public int RemoveSlotItems(int s, int n) { int removed = Math.Min(Counts[s], n); Counts[s] -= removed; AfterRemove?.Invoke(s, Values[s], removed); return removed; }
        public void ProcessSlotItems(int s, int v, int n, int p, out int rv, out int rn) { rv = rn = 0; }
        public void DropAllItems(Vector3 p) { }
        public int Total(int v) => Enumerable.Range(0, SlotsCount).Where(s => Values[s] == v).Sum(s => Counts[s]);
    }
    internal static void Run(Action<string, Action> test) {
        var saved = ScGunRegistry.Current;
        void Case(string name, Action<ScGunRegistry, Inventory> action) => test("F1/" + name, () => {
            ScNet.Attach(null);
            var inv = new Inventory(); var registry = new ScGunRegistry { RecoveryOwner = i => ReferenceEquals(i, inv) ? "player/0" : null };
            ScGunRegistry.Current = registry;
            try { action(registry, inv); } finally { ScGunRegistry.Current = saved; }
        });
        bool Replace(Inventory inv) => ScInventoryTransaction.ReplaceWithCost(inv, 0, 100, 200, 900, 2);
        void Original(Inventory i) => Require(i.Total(100) == 1 && i.Total(200) == 0 && i.Total(900) == 5, "original gun/material totals not restored");
        Case("normal-cost", (r, i) => { Require(Replace(i), "normal replacement refused"); Require(i.Total(100) == 0 && i.Total(200) == 1 && i.Total(900) == 3 && r.Recovery.Count == 0, "wrong committed totals"); });
        Case("silent-rejection", (r, i) => { i.BeforeAdd = (_, v, _) => v != 200; Require(!Replace(i), "refused add returned success"); Original(i); });
        Case("remove-then-throw", (r, i) => { i.AfterRemove = (s, _, _) => { if (s == 0) throw new Exception("injected after remove"); }; Require(!Replace(i), "throw returned success"); Original(i); });
        Case("add-then-throw", (r, i) => { i.AfterAdd = (_, v, _) => { if (v == 200) throw new Exception("injected after add"); }; Require(!Replace(i), "throw returned success"); Original(i); });
        Case("target-changed-during-cost", (r, i) => { i.AfterRemove = (s, _, _) => { if (s == 1) i.Values[0] = 300; }; Require(!Replace(i), "changed target accepted"); Require(i.Total(300) == 1 && i.Total(100) == 0 && i.Total(200) == 0 && i.Total(900) == 5, "changed slot overwritten/materials lost"); });
        Case("rollback-failure-save-reload-retry", (r, i) => {
            i.BeforeAdd = (_, _, _) => false; Require(!Replace(i), "rejected add returned success");
            Require(r.Recovery.Count == 1 && i.Total(100) == 0 && i.Total(900) == 3, "missing durable compensation");
            var recovery = ScGunRecovery.Load(r.Recovery.Save());
            i.BeforeAdd = null; i.AfterAdd = (_, _, _) => throw new Exception("return changed then threw");
            Require(recovery.Retry(_ => i) == 1, "measured return not completed"); Original(i);
            Require(recovery.Retry(_ => i) == 0, "duplicate recovery"); Original(i);
        });
        Case("owner-disappears-no-refund-to-new-owner", (r, i) => {
            string owner = "player/0"; r.RecoveryOwner = _ => owner;
            i.AfterAdd = (_, v, _) => { if (v == 200) owner = "player/1"; };
            Require(!Replace(i), "changed owner accepted"); Require(r.Recovery.HasPending("player/0"), "original owner's claim lost");
            Require(r.Recovery.Retry(_ => null) == 0 && i.Total(200) == 1 && i.Total(100) == 0, "ambiguous refund executed");
            owner = "player/0"; i.AfterAdd = null; Require(r.Recovery.Retry(_ => i) == 1, "original owner cannot recover"); Original(i);
        });
        Case("save-and-reentrant-mutation-guard", (r, i) => {
            bool guarded = false, nested = true, saveRefused = false;
            i.AfterRemove = (s, _, _) => { if (s == 1) {
                guarded = ScGunMutation.IsCommitting; nested = ScInventoryTransaction.ReplaceWithCost(i, 0, 100, 300, 0, 0);
                try { r.Save(0); } catch (InvalidOperationException e) { saveRefused = e.Message.Contains("transaction/recovery"); }
            } };
            Require(Replace(i) && guarded && saveRefused && !nested && !ScGunMutation.IsCommitting, "mutation/save guard missing or leaked");
        });
        Case("material-partial-remove-then-throw", (r, i) => {
            i.AfterRemove = (s, _, _) => { if (s == 1) throw new Exception("deduction changed then threw"); };
            Require(!Replace(i), "partial cost exception accepted"); Original(i); Require(r.Count == 0 && r.Recovery.Count == 0, "unexpected record/debt");
        });
        Case("pending-debt-refuses-new-mutation", (r, i) => {
            r.Recovery.Grant("player/0", [(950, 2)], "fixture earned grant");
            Require(!Replace(i), "pending debt ignored"); Original(i); Require(r.Recovery.Count == 1, "existing obligation lost");
        });
        Case("creative-replacement", (r, i) => {
            var creative = new ComponentCreativeInventory { OpenSlotsCount = 1 }; creative.m_slots.Add(100); r.RecoveryOwner = _ => "player/0";
            Require(ScInventoryTransaction.ReplaceWithCost(creative, 0, 100, 200, 0, 0) && creative.GetSlotValue(0) == 200, "creative slot not replaced");
            Require(!ScInventoryTransaction.ReplaceWithCost(creative, 0, 200, 300, 900, 1) && creative.GetSlotValue(0) == 200, "creative cost restriction changed");
        });
        Case("world-changed-keeps-old-world-debt", (r, i) => {
            var newer = new ScGunRegistry(); i.AfterAdd = (_, v, _) => { if (v == 200) ScGunRegistry.Current = newer; };
            Require(!Replace(i) && r.Recovery.Count == 1 && newer.Count == 0 && newer.Recovery.Count == 0, "world boundary crossed");
            ScGunRegistry.Current = r; i.AfterAdd = null; r.Recovery.Retry(_ => i); Original(i);
        });
        foreach (bool refuse in new[] { false, true }) test("F1/arrival-" + (refuse ? "refused-and-repeat" : "normal-and-late-restore"), () => Arrival(refuse));
        test("F1/arrival-delayed-after-empty-settle", () => Arrival(true, delayed: true));
        test("F1/arrival-completed-gun-can-leave-inventory", () => Arrival(false, move: true));
    }
    static void Arrival(bool refuse, bool delayed = false, bool move = false) {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var previous = ScGunRegistry.Current; bool had = BlocksManager.BlockTypeToIndex.TryGetValue(typeof(ScGunBlock), out int previousBlock);
        const int block = 701;
        BlocksManager.BlockTypeToIndex[typeof(ScGunBlock)] = block;
        try {
            var inv = new Inventory(); var project = new Project();
            var source = new ScGunRegistry { Travel = new ScTravelLedger() }; ScGunRegistry.Current = source;
            int id = source.Allocate(0, 7, false, 640, 1500);
            int original = Terrain.MakeBlockValue(block, 0, GunSpec.WithId(0, id)); inv.Values[0] = original;
            var exported = ScItemTravel.Export(project, inv, "player/0"); Require(exported.Ok, exported.Message);
            var envelope = ScTravelEnvelope.Decode(exported.Envelope, out _, out _); string row = source.NetworkRow(id, 0);
            var dest = new ScGunRegistry { Travel = new ScTravelLedger(), RecoveryOwner = _ => "player/0" }; ScGunRegistry.Current = dest;
            dest.Allocate(0, 23, false, 900, 1500); string existing = dest.NetworkRow(1, 0);
            var candidate = new ScTravelArrival.Candidate("world/main", "Inventory", envelope, exported.Envelope, null);
            var arrival = (ScTravelArrival)Activator.CreateInstance(typeof(ScTravelArrival), Any, null, [new List<ScTravelArrival.Candidate> { candidate }], null);
            double offset = delayed ? 2 : 0;
            if (delayed) { inv.Counts[0] = 0; arrival.Step(project, inv, block, 0); arrival.Step(project, inv, block, 1.1); inv.Counts[0] = 1; }
            if (refuse) inv.BeforeAdd = (_, v, _) => v == original || v == 900;
            arrival.Step(project, inv, block, offset); string message = arrival.Step(project, inv, block, offset + 1.1);
            if (refuse) {
                Require(message is null || !message.Contains("已随行迁移"), "false migration success message");
                Require(inv.Total(original) == 1 && !dest.Travel.Find(envelope.Transfer).Completed && arrival.Pending(0, original), "rejected arrival lost custody/pending protection");
                inv.BeforeAdd = null; arrival.Step(project, inv, block, offset + 2.2);
            }
            int target = Terrain.MakeBlockValue(block, 0, GunSpec.WithId(0, 2));
            Require(inv.Total(target) == 1 && inv.Total(original) == 0 && dest.Travel.Find(envelope.Transfer).Completed, "arrival not actually completed");
            Require(dest.NetworkRow(1, 0) == existing && dest.NetworkRow(2, 0) == row && dest.Count == 2 && inv.Total(900) == 5, "records/materials changed");
            if (refuse) inv.BeforeAdd = (_, v, _) => v == original || v == 900;
            inv.Values[0] = original; arrival.Step(project, inv, block, offset + 3); arrival.Step(project, inv, block, offset + 4.1);
            if (refuse) {
                Require(!dest.Travel.Find(envelope.Transfer).Completed && inv.Total(original) == 1 && arrival.Pending(0, original), "failed late restoration kept a completed receipt");
                inv.BeforeAdd = null; arrival.Step(project, inv, block, offset + 4.2);
            }
            Require(inv.Total(target) == 1 && dest.Count == 2 && dest.NetworkRow(2, 0) == row, "late restore duplicated/reset gun");
            if (move) {
                inv.Counts[0] = 0; arrival.Step(project, inv, block, 5); arrival.Step(project, inv, block, 6.1); arrival.Step(project, inv, block, 70);
                Require(arrival.Finished && dest.Travel.Find(envelope.Transfer).Completed && dest.NetworkRow(2, 0) == row, "ordinary transfer out revoked a completed arrival");
            }
        } finally { ScGunRegistry.Current = previous; if (had) BlocksManager.BlockTypeToIndex[typeof(ScGunBlock)] = previousBlock; else BlocksManager.BlockTypeToIndex.Remove(typeof(ScGunBlock)); }
    }
}
