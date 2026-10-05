using System.Reflection;
using System.Xml.Linq;
using Engine;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

static class ArrivalRecoveryChecks {
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const int Block = 701;
    static int Value(int id) => Terrain.MakeBlockValue(Block, 0, GunSpec.WithId(0, id));
    static void Require(bool ok, string message) => TravelChecks.Require(ok, message);
    static void Set(object o, string name, object value) => o.GetType().GetField(name, Any).SetValue(o, value);
    sealed class Trip {
        public Project Project = new();
        public TravelChecks.Inventory Inventory = new() { Values = [Value(1), Value(2), 0, 0], Counts = [1, 1, 0, 0] };
        public ScGunRegistry Registry;
        public ScTravelArrival.Candidate Candidate;
        public ScTravelArrival Arrival;
        public string[] Rows;
        public int DestinationCount;
        public Trip(int destinationCount = 2, int sourceFirst = 1) {
            DestinationCount = destinationCount;
            ScNet.Attach(null);
            var source = new ScGunRegistry { Travel = new ScTravelLedger() }; ScGunRegistry.Current = source;
            while (source.Next < sourceFirst) source.Allocate(0, 1, false, 100, 1500);
            int first = source.Allocate(0, 7, false, 640, 1500), second = source.Allocate(0, 8, false, 641, 1500);
            Inventory.Values[0] = Value(first); Inventory.Values[1] = Value(second);
            var export = ScItemTravel.Export(Project, Inventory, "player/0"); Require(export.Ok, export.Message);
            var envelope = ScTravelEnvelope.Decode(export.Envelope, out _, out _);
            Candidate = new("world/main", "Inventory", envelope, export.Envelope, null);
            Registry = new ScGunRegistry { Travel = new ScTravelLedger(), RecoveryOwner = i => ReferenceEquals(i, Inventory) ? "player/0" : "container/other" };
            ScGunRegistry.Current = Registry;
            for (int i = 0; i < destinationCount; i++) Registry.Allocate(0, 23 + i, false, 900 + i, 1500);
            Arrival = NewArrival();
            Inventory.BeforeAdd = (_, v, _) => v != Value(destinationCount + 2);
            Arrival.Step(Project, Inventory, Block, 0); Arrival.Step(Project, Inventory, Block, 1.1);
            Rows = Enumerable.Range(1, destinationCount + 2).Select(id => Registry.NetworkRow(id, 0)).ToArray();
        }
        public ScTravelArrival NewArrival() => (ScTravelArrival)Activator.CreateInstance(typeof(ScTravelArrival), Any, null, [new List<ScTravelArrival.Candidate> { Candidate }], null);
        public SubsystemScGunBlockBehavior Behavior() {
            var b = new SubsystemScGunBlockBehavior { m_project = Project };
            Set(b, "m_saveReady", true); Set(b, "m_worldLayout", GunSpec.DataLayout); Set(b, "m_registry", Registry);
            Set(b, "m_travel", Registry.Travel); Set(b, "m_time", new SubsystemTime { m_gameTime = 0 }); Set(b, "m_arrival", Arrival);
            Set(b, "m_travelSource", "world/main/sub"); Set(b, "m_travelWorldIdentity", Registry.Travel.WorldIdentity);
            return b;
        }
        public void Reload() {
            var saved = new ValuesDictionary(); Behavior().Save(saved);
            var xml = new XElement("Values", new XAttribute("Name", "ScGunBlockBehavior")); saved.Save(xml);
            XElement V(string name, object value) => new("Value", new XAttribute("Name", name), new XAttribute("Type", value is int ? "int" : "string"), new XAttribute("Value", value));
            var native = new ComponentInventory();
            for (int s = 0; s < Inventory.SlotsCount; s++) native.m_slots.Add(new() { Value = Inventory.GetSlotValue(s), Count = Inventory.GetSlotCount(s) });
            var inventoryData = new ValuesDictionary(); native.Save(inventoryData, null);
            var inventoryXml = new XElement("Values", new XAttribute("Name", "Inventory")); inventoryData.Save(inventoryXml);
            var projectXml = new XElement("Project", new XElement("Subsystems", new XElement(xml),
                new XElement("Values", new XAttribute("Name", "BlocksManager"), V(Block.ToString(), "ScGunBlock"))),
                new XElement("Entities", new XElement("Entity", new XAttribute("Name", "MalePlayer"),
                    new XElement("Values", new XAttribute("Name", "Player"), V("PlayerIndex", 0)), inventoryXml)));
            Require(ScGunLoadIntegrity.ValidateReferences(projectXml) == 2, "actual load guard rejects a recoverable arrival");
            ScGunTravel.Capture(projectXml, "world/main/sub");
            Require(projectXml.Descendants("Value").Any(v => (string)v.Attribute("Name") == "Error"), "XML export failed to retain unresolved obligation");
            Require(ScGunTravel.Prepare(projectXml, "world/main/sub") is null, "same world load treated as another import");
            var roundtrip = new ValuesDictionary(); roundtrip.ApplyOverrides(XElement.Parse(xml.ToString()));
            var loaded = ScGunRegistry.Load(roundtrip.GetValue<ValuesDictionary>("GunRegistry"), 0);
            loaded.Travel = new ScTravelLedger { WorldIdentity = roundtrip.GetValue<string>(ScGunTravel.WorldIdentity) };
            loaded.Travel.LoadIdentities(roundtrip.GetValue<ValuesDictionary>(ScGunTravel.Identities));
            loaded.Travel.LoadReceipts(roundtrip.GetValue<ValuesDictionary>(ScTravelLedger.ReceiptsKey));
            loaded.RecoveryOwner = Registry.RecoveryOwner; Registry = loaded; ScGunRegistry.Current = loaded;
            var inventoryRoundtrip = new ValuesDictionary(); inventoryRoundtrip.ApplyOverrides(XElement.Parse(inventoryXml.ToString()));
            Array.Clear(Inventory.Values); Array.Clear(Inventory.Counts);
            foreach (var entry in inventoryRoundtrip.GetValue<ValuesDictionary>("Slots")) {
                int s = int.Parse(entry.Key[4..]); var item = (ValuesDictionary)entry.Value;
                Inventory.Values[s] = item.GetValue<int>("Contents"); Inventory.Counts[s] = item.GetValue<int>("Count");
            }
        }
        public void Complete() {
            Inventory.BeforeAdd = null;
            Arrival.Step(Project, Inventory, Block, 3); Arrival.Step(Project, Inventory, Block, 4.1);
            Require(Inventory.Total(Value(DestinationCount + 1)) == 1 && Inventory.Total(Value(DestinationCount + 2)) == 1
                && Inventory.Counts.Sum() == 2 + Inventory.Total(900), "remaining gun not remapped exactly once");
            Require(Registry.Travel.Find(Candidate.Envelope.Transfer).Completed && Registry.Count == DestinationCount + 2 && Registry.Recovery.Count == 0, "receipt/debt/count mismatch");
            Require(Enumerable.Range(1, DestinationCount + 2).All(id => Registry.NetworkRow(id, 0) == Rows[id - 1]), "local/imported record changed");
        }
    }
    public static void Run(Action<string, Action> test) {
        void Case(string name, Action<Trip> run, int destinationCount = 2, int sourceFirst = 1) => test("R1-R2/" + name, () => {
            var previous = ScGunRegistry.Current; bool had = BlocksManager.BlockTypeToIndex.TryGetValue(typeof(ScGunBlock), out int old);
            BlocksManager.BlockTypeToIndex[typeof(ScGunBlock)] = Block;
            try { run(new Trip(destinationCount, sourceFirst)); }
            finally { ScGunRegistry.Current = previous; if (had) BlocksManager.BlockTypeToIndex[typeof(ScGunBlock)] = old; else BlocksManager.BlockTypeToIndex.Remove(typeof(ScGunBlock)); }
        });
        Case("partial-save-reload-twice", t => {
            Require(t.Inventory.GetSlotValue(0) == Value(3) && t.Inventory.GetSlotValue(1) == Value(2), "partial premise missing");
            t.Reload(); t.Arrival = t.NewArrival();
            t.Arrival.Step(t.Project, t.Inventory, Block, 0); t.Arrival.Step(t.Project, t.Inventory, Block, 1.1); t.Arrival.Step(t.Project, t.Inventory, Block, 61);
            Require(t.Arrival.Pending(1, Value(2)) && !t.Arrival.Finished, "reload released unfinished gun");
            t.Reload(); t.Arrival = t.NewArrival(); t.Complete();
        });
        Case("recovery-without-source-world", t => {
            t.Reload();
            t.Arrival = ScTravelArrival.Prepare("world/main/sub", "world/main/sub", false, t.Registry.Travel, _ => false, _ => [], _ => null);
            Require(t.Arrival is not null, "own receipt cannot restore arrival without source world"); t.Complete();
        });
        Case("overlapping-source-and-target-ids", t => { t.Reload(); t.Arrival = t.NewArrival(); t.Complete(); }, destinationCount: 1);
        Case("extended-source-id-missing-in-destination", t => { t.Reload(); t.Arrival = t.NewArrival(); t.Complete(); }, sourceFirst: 1024);
        Case("pending-receipt-not-evicted", t => {
            var commit = typeof(ScTravelLedger).GetMethod("Commit", Any);
            for (int i = 0; i < 70; i++) commit.Invoke(t.Registry.Travel, [new ScTravelLedger.Receipt("later-" + i, "digest", "world", true, [])]);
            t.Reload(); t.Arrival = t.NewArrival(); t.Complete();
        });
        Case("unknown-pending-format-refused-without-writing", t => {
            var data = t.Registry.Travel.SaveReceipts(); var arrivals = data.GetValue<ValuesDictionary>("Arrivals");
            string key = t.Candidate.Envelope.Transfer;
            arrivals.SetValue(key, arrivals.GetValue<string>(key).Replace("\"Version\":1", "\"Version\":99"));
            var xml = new XElement("Values"); data.Save(xml); string before = xml.ToString();
            bool refused = false;
            try { new ScTravelLedger().LoadReceipts(data); } catch (InvalidOperationException e) { refused = e.Message.Contains("arrival receipt"); }
            var after = new XElement("Values"); data.Save(after);
            Require(refused && after.ToString() == before, "unknown receipt silently accepted or rewritten");
        });
        Case("pending-export-refused", t => {
            var export = ScItemTravel.Export(t.Project, t.Inventory, "player/0");
            Require(export.Code == ScTravelCode.PendingObligations && !t.Behavior().ReadyForTravel, "mixed local/source ids permitted to leave world");
        });
        Case("player-moves-within-inventory", t => {
            int value = t.Inventory.GetSlotValue(1); t.Inventory.RemoveSlotItems(1, 1); t.Inventory.AddSlotItems(2, value, 1);
            Require(t.Arrival.Pending(2, value), "moved gun no longer protected"); t.Complete();
        });
        Case("compensation-returns-to-other-slot", t => {
            t.Inventory.BeforeAdd = (s, v, _) => { if (v != Value(4)) return true; t.Inventory.Values[s] = 900; t.Inventory.Counts[s] = 1; return false; };
            t.Arrival.Step(t.Project, t.Inventory, Block, 2.2);
            t.Arrival.Step(t.Project, t.Inventory, Block, 3.3);
            int slot = Enumerable.Range(0, 4).First(s => t.Inventory.GetSlotValue(s) == Value(2));
            Require(slot != 1 && t.Registry.Recovery.Count == 0 && t.Arrival.Pending(slot, Value(2)), "refund relocation lost identity"); t.Complete();
        });
        Case("partial-mapping-and-refund-debt-reload", t => {
            t.Inventory.BeforeAdd = (_, v, _) => v != Value(4) && v != Value(2);
            t.Arrival.Step(t.Project, t.Inventory, Block, 2.2); t.Arrival.Step(t.Project, t.Inventory, Block, 3.3);
            Require(t.Registry.Recovery.Count == 1 && t.Inventory.Total(Value(2)) == 0 && t.Inventory.Total(Value(3)) == 1, "partial mapping with compensation premise missing");
            t.Reload(); t.Arrival = t.NewArrival(); t.Inventory.BeforeAdd = null;
            Require(t.Registry.Recovery.Retry(owner => owner == "player/0" ? t.Inventory : null) == 1, "saved refund not delivered");
            Require(t.Registry.Recovery.Retry(_ => t.Inventory) == 0, "refund delivered twice"); t.Complete();
        });
        Case("ambiguous-new-copy-is-not-guessed", t => {
            t.Inventory.AddSlotItems(2, Value(2), 1); t.Inventory.BeforeAdd = null;
            t.Arrival.Step(t.Project, t.Inventory, Block, 2.2); t.Arrival.Step(t.Project, t.Inventory, Block, 3.3);
            Require(t.Inventory.Total(Value(2)) == 2 && t.Inventory.Total(Value(4)) == 0 && !t.Registry.Travel.Find(t.Candidate.Envelope.Transfer).Completed
                && t.Arrival.Pending(1, Value(2)) && t.Arrival.Pending(2, Value(2)), "ambiguous identity was guessed or released");
        });
        Case("world-switch-during-arrival-retains-original-obligation", t => {
            var otherWorld = new ScGunRegistry { Travel = new ScTravelLedger() };
            t.Inventory.BeforeAdd = null;
            t.Inventory.AfterAdd = (_, v, _) => { if (v == Value(4)) ScGunRegistry.Current = otherWorld; };
            t.Arrival.Step(t.Project, t.Inventory, Block, 2.2); t.Arrival.Step(t.Project, t.Inventory, Block, 3.3);
            Require(otherWorld.Count == 0 && otherWorld.Recovery.Count == 0 && t.Registry.Recovery.Count == 1
                && !t.Registry.Travel.Find(t.Candidate.Envelope.Transfer).Completed, "arrival crossed world or forgot compensation");
            ScGunRegistry.Current = t.Registry; t.Inventory.AfterAdd = null;
            t.Registry.Recovery.Retry(_ => t.Inventory); t.Complete();
        });
        Case("container-transfer-pauses-until-complete", t => {
            var other = new TravelChecks.Inventory { Values = [Value(2), 0, 0, 0], Counts = [1, 0, 0, 0] };
            var loader = new ScCsgoKnivesModLoader(); int count = 1;
            loader.HandleMoveInventoryItem(null, t.Inventory, 1, other, 1, ref count, out bool handled);
            Require(handled || count == 0, "unresolved gun allowed into another owner");
            loader.HandleInventoryDragMove(null, t.Inventory, 1, other, 1, false, out bool skip);
            Require(skip, "drag bypassed arrival custody");
            t.Inventory.Values[3] = 900; t.Inventory.Counts[3] = 1; count = 1;
            loader.HandleMoveInventoryItem(null, t.Inventory, 3, other, 1, ref count, out handled);
            Require(!handled && count == 1, "unrelated material movement blocked");
            Require(ScGunMutation.Prepare(other, 0, "container/other", out var reason) is not null && reason == ScGunResult.Success, "same-number local gun incorrectly blocked");
            Require(ScGunMutation.Prepare(t.Inventory, 1, "player/0", out _) is null, "workbench can mutate foreign id");
            t.Complete(); count = 1; loader.HandleMoveInventoryItem(null, t.Inventory, 1, other, 1, ref count, out handled);
            Require(!handled && count == 1, "normal transfer still blocked after completion");
        });
    }
}
