using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Xml.Linq;
using Engine;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

// universal-item-travel-analysis-20261002 / OpenSpec subworld-travel T1-T4: guns carried between worlds by a provider
// that restores the traveller's inventory itself (ScItemTravel, ScGunTravelApi), on the packaged core.
//
// Two worlds live in this process, each with its own gun table, transfer ledger, gun block index, project and
// inventories; the process-wide "current" table, block index and project are switched to the world that is acting, as
// loading a world does. A provider is played by the test: export in the world left, carry the text, import in the world
// entered, restore the mapped values, complete.
//
// With SC_ANCIENT_PACKAGE (the user's AncientWorld_v0.41.16.zip, read in memory, never unpacked or written) the same is
// done through that package's own InventorySnapshot Capture / Save / Load / Apply: first as the package ships (the
// reported fault, asserted as present), then with the three bridge calls placed where the minimal patch puts them.
static class ItemTravelRegression {
    internal record Result(string Name, bool Ok, string Detail);
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static T Blank<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    /// <summary>A plain inventory that is an entity component, so the holder scan finds it.</summary>
    sealed class Box : Component, IInventory {
        public int[] Values = new int[12], Counts = new int[12];
        Project IInventory.Project => Entity?.Project; public int SlotsCount => 12; public int VisibleSlotsCount { get; set; } = 12; public int ActiveSlotIndex { get; set; }
        public int GetSlotValue(int s) => Counts[s] > 0 ? Values[s] : 0; public int GetSlotCount(int s) => Counts[s]; public int GetSlotCapacity(int s, int v) => 40; public int GetSlotProcessCapacity(int s, int v) => 0;
        public void AddSlotItems(int s, int v, int n) { Values[s] = v; Counts[s] += n; } public int RemoveSlotItems(int s, int n) { n = Math.Min(n, Counts[s]); Counts[s] -= n; return n; }
        public void ProcessSlotItems(int s, int v, int n, int p, out int rv, out int rn) { rv = rn = 0; } public void DropAllItems(Vector3 p) { }
        public void Put(int slot, int value, int count = 1) { Values[slot] = value; Counts[slot] = count; }
        public void Clear() { Array.Clear(Values); Array.Clear(Counts); }
    }
    sealed class World { public string Name; public object Registry, Ledger; public Project Project; public Box Player, Chest; public int Block; }

    internal static List<Result> Run(Assembly mod) {
        var results = new List<Result>();
        void Test(string name, Func<string> body) { try { results.Add(new("item-travel/" + name, true, body() ?? "")); } catch (Exception e) { results.Add(new("item-travel/" + name, false, (e is TargetInvocationException { InnerException: { } inner } ? inner : e).ToString())); } }
        void Require(bool ok, string why) { if (!ok) throw new Exception(why); }
        Type T(string name) => mod.GetType("Game." + name, true);
        // (A delivered build from before the travel core, run as the baseline: one named result, not a crash.)
        if (mod.GetType("Game.ScItemTravel") is null) { results.Add(new("item-travel/core-entry-points", false, "this build has no Game.ScItemTravel / ScGunTravelApi")); return results; }
        var registryType = T("ScGunRegistry"); var current = registryType.GetField("Current"); var ledgerType = T("ScTravelLedger"); var travel = T("ScItemTravel"); var api = T("ScGunTravelApi");
        var gunBlockType = T("ScGunBlock"); var spec = T("GunSpec");
        object savedRegistry = current.GetValue(null); var savedTypes = new Dictionary<Type, int>(BlocksManager.BlockTypeToIndex); var savedProject = GameManager.m_project;
        var savedBlocks = (Block[])BlocksManager.Blocks.Clone();
        var gunBlock = (Block)Activator.CreateInstance(gunBlockType);
        var specs = ((Array)spec.GetField("All").GetValue(null)).Cast<object>().ToArray();
        int Variant(string name) => Array.FindIndex(specs, s => (string)s.GetType().GetField("Name").GetValue(s) == name);
        int ak = Variant("ak47"), glock = Variant("glock18"), zeus = Array.FindIndex(specs, s => Convert.ToSingle(s.GetType().GetField("RechargeSeconds")?.GetValue(s) ?? s.GetType().GetProperty("RechargeSeconds").GetValue(s)) > 0);
        int schema = (int)registryType.GetField("Schema").GetRawConstantValue(), lastId = (int)spec.GetField("LastId").GetRawConstantValue();

        World Make(string name, int block, string identity = null) {
            var w = new World { Name = name, Block = block, Registry = Activator.CreateInstance(registryType), Ledger = Activator.CreateInstance(ledgerType), Project = new Project(), Player = new Box(), Chest = new Box() };
            if (identity is not null) ledgerType.GetField("WorldIdentity").SetValue(w.Ledger, identity);
            registryType.GetField("Travel").SetValue(w.Registry, w.Ledger);
            foreach (var box in new[] { w.Player, w.Chest }) { var e = Blank<Entity>(); e.Id = 100 + w.Project.m_entities.Count; e.m_project = w.Project; e.m_isAddedToProject = true; e.m_components = [box]; box.m_entity = e; w.Project.m_entities[e] = true; }
            // Production Subsystem.Load installs this resolver before arrival checks. The fixture must provide the
            // same durable entity/slot ownership; a null Project and duplicate entity ids are not live inventories.
            registryType.GetField("RecoveryOwner")?.SetValue(w.Registry, (Func<IInventory, string>)(inventory =>
                (string)T("ScGunHolders").GetMethod("RecoveryOwner").Invoke(null, [w.Project, inventory])));
            return w;
        }
        void Enter(World w) {
            current.SetValue(null, w.Registry); GameManager.m_project = w.Project;
            for (int i = 0; i < BlocksManager.Blocks.Length; i++) if (ReferenceEquals(BlocksManager.Blocks[i], gunBlock)) BlocksManager.Blocks[i] = savedBlocks[i];
            BlocksManager.Blocks[w.Block] = gunBlock; gunBlock.BlockIndex = w.Block; BlocksManager.BlockTypeToIndex[gunBlockType] = w.Block;
        }
        int Gun(World w, int variant, int rounds, int durability = 900, int skin = 0) { Enter(w); return (int)registryType.GetMethod("Allocate").Invoke(w.Registry, [variant, rounds, false, durability, 1500, skin]); }
        int Item(World w, int variant, int id) => Terrain.MakeBlockValue(w.Block, 0, (int)spec.GetMethod("WithId").Invoke(null, [variant, id]));
        int IdOf(int value) => (int)spec.GetMethod("GetId").Invoke(null, [Terrain.ExtractData(value)]);
        string Row(World w, int id) { Enter(w); return (string)registryType.GetMethod("NetworkRow").Invoke(w.Registry, [id, 0d]); }
        int Rounds(World w, int value) { Enter(w); return (int)spec.GetMethod("GetRounds").Invoke(null, [Terrain.ExtractData(value)]); }
        bool Known(World w, int value) { Enter(w); return Terrain.ExtractContents(value) == w.Block && (bool)gunBlockType.GetMethod("IsKnown").Invoke(null, [value]); }
        object Record(World w, int id) { Enter(w); return registryType.GetMethod("Get", Any).Invoke(w.Registry, [id]); }
        void Change(World w, int id, Action<dynamic> change) { dynamic r = Record(w, id); change(r); r.Revision++; }
        int Next(World w) => (int)registryType.GetProperty("Next").GetValue(w.Registry);
        int Count(World w) => (int)registryType.GetProperty("Count").GetValue(w.Registry);
        string State(World w) { Enter(w); var d = (ValuesDictionary)registryType.GetMethod("Save").Invoke(w.Registry, [0d]); var x = new XElement("R"); d.Save(x); var ids = (ValuesDictionary)ledgerType.GetMethod("SaveIdentities").Invoke(w.Ledger, null); var r = (ValuesDictionary)ledgerType.GetMethod("SaveReceipts").Invoke(w.Ledger, null);
            var xi = new XElement("I"); ids?.Save(xi); var xr = new XElement("C"); r?.Save(xr); return x.ToString() + xi + xr; }
        dynamic Export(World w, string transfer = null) { Enter(w); return travel.GetMethod("Export").Invoke(null, [w.Project, w.Player, "player-1", transfer]); }
        dynamic Import(World w, string envelope, bool commit = true, Box into = null) { Enter(w); return travel.GetMethod("Import").Invoke(null, [w.Project, into ?? w.Player, envelope, commit]); }
        dynamic Complete(World w, string envelope) { Enter(w); return travel.GetMethod("Complete").Invoke(null, [w.Project, w.Player, envelope]); }
        string Code(dynamic result) => result.Code.ToString();
        /// What a provider does on arrival: the traveller's inventory there is replaced by what was carried, each value
        /// translated through the mapping the import returned (an item that is not a CS gun keeps its value).
        void Restore(World to, (int Slot, int Value, int Count)[] carried, IReadOnlyDictionary<int, int> values) {
            to.Player.Clear();
            foreach (var (slot, value, count) in carried) to.Player.Put(slot, values is not null && values.TryGetValue(value, out int mapped) ? mapped : value, count);
        }
        (int Slot, int Value, int Count)[] Carried(World w) => Enumerable.Range(0, 12).Where(s => w.Player.Counts[s] > 0).Select(s => (s, w.Player.Values[s], w.Player.Counts[s])).ToArray();
        /// One whole trip as a provider with the bridge makes it; returns the import result.
        dynamic Trip(World from, World to, out string envelope) {
            var export = Export(from); Require((bool)export.Ok && export.Envelope is not null, $"export from {from.Name} refused: {export.Code} {export.Message}");
            envelope = (string)export.Envelope; var carried = Carried(from);
            var import = Import(to, envelope); if (!(bool)import.Ok) return import;
            Restore(to, carried, (IReadOnlyDictionary<int, int>)import.Values);
            var done = Complete(to, envelope); Require((bool)done.Ok, $"completion in {to.Name} refused: {done.Code} {done.Message}");
            return import;
        }
        try {
            // ---------------------------------------------------------------- the reported collisions
            Test("same-number-other-model-in-the-destination", () => {
                var main = Make("main", 304); var sub = Make("sub", 320);
                int id = Gun(main, ak, 7, 640, 0); main.Player.Put(0, Item(main, ak, id));
                int theirs = Gun(sub, glock, 18); sub.Chest.Put(0, Item(sub, glock, theirs));
                Require(id == 1 && theirs == 1, "fixture: both guns should be record 1 of their world");
                string before = Row(sub, 1);
                var import = Trip(main, sub, out _);
                int value = sub.Player.Values[0], local = IdOf(value);
                Require(Code(import) == "Ok" && local == 2 && Terrain.ExtractContents(value) == 320, $"imported as {local} in block {Terrain.ExtractContents(value)}: {import.Code}");
                Require(Known(sub, value) && Rounds(sub, value) == 7 && Row(sub, 2) == Row(main, 1), $"the carried AK is not usable with its own state: known {Known(sub, value)}, rounds {Rounds(sub, value)}");
                Require(Row(sub, 1) == before && Known(sub, sub.Chest.Values[0]) && Rounds(sub, sub.Chest.Values[0]) == 18, "the destination's own record 1 (a Glock) was touched");
                return $"main #1 AK 7 rounds -> sub #2 (block 304 -> 320); sub's own #1 Glock keeps 18";
            });
            Test("same-number-same-model-in-the-destination-keeps-the-carried-state", () => {
                var main = Make("main", 304); var sub = Make("sub", 320);
                int id = Gun(main, ak, 7, 640); main.Player.Put(3, Item(main, ak, id));
                int theirs = Gun(sub, ak, 23, 1500); sub.Chest.Put(0, Item(sub, ak, theirs));
                // What happens without the bridge: the value keeps its number and reads the destination's record.
                int naive = Terrain.ReplaceContents(main.Player.Values[3], 320);
                Require(Known(sub, naive) && Rounds(sub, naive) == 23, "fixture: the unbridged value should read the destination's 23 rounds");
                var import = Trip(main, sub, out _);
                int value = sub.Player.Values[3];
                Require(IdOf(value) == 2 && Rounds(sub, value) == 7 && Row(sub, 2) == Row(main, 1), $"the carried AK reads {Rounds(sub, value)} rounds under #{IdOf(value)}");
                Require(Rounds(sub, sub.Chest.Values[0]) == 23, "the destination's own AK changed");
                return "carried AK: 7 rounds under a new number; the destination's own AK #1 still 23 (the unbridged value read 23)";
            });
            Test("whole-state-carried", () => {
                var main = Make("main", 304); var sub = Make("sub", 320);
                int a = Gun(main, ak, 11, 123, 0), z = Gun(main, zeus, 0, 700);
                Change(main, a, r => { r.SilencerOff = true; r.CounterInstalled = true; r.KillCount = 345L; r.AppliedGrowthLevel = 3; r.GrowthRulesVersion = (int)T("ScGunGrowth").GetField("RulesVersion").GetRawConstantValue(); r.ReserveOverflowRounds = 2; r.GrowthKillCredit = 7L; });
                Change(main, z, r => { r.RechargeReadyAt = 12.5; r.RechargeCycleSeconds = 30f; });
                registryType.GetField("GrowthMode").SetValue(main.Registry, Enum.Parse(T("ScGunGrowthMode"), "CountAndGrow"));
                main.Player.Put(0, Item(main, ak, a)); main.Player.Put(1, Item(main, zeus, z));
                main.Player.Put(2, Terrain.MakeBlockValue(304, 0, (int)spec.GetMethod("WithId").Invoke(null, [glock, 0])), 3);   // three fresh pistols: no record, the block index still changes
                main.Player.Put(5, Terrain.MakeBlockValue(2, 0, 0), 30);                                                                  // dirt: not ours
                var import = Trip(main, sub, out string envelope);
                Require(Row(sub, IdOf(sub.Player.Values[0])) == Row(main, a) && Row(sub, IdOf(sub.Player.Values[1])) == Row(main, z), "a carried record differs from its source");
                Require(Terrain.ExtractContents(sub.Player.Values[2]) == 320 && Terrain.ExtractData(sub.Player.Values[2]) == Terrain.ExtractData(main.Player.Values[2]) && sub.Player.Counts[2] == 3, "fresh guns not carried with the destination's block index");
                Require(sub.Player.Values[5] == main.Player.Values[5] && sub.Player.Counts[5] == 30, "an item that is not a CS gun was changed");
                Require(registryType.GetField("GrowthMode").GetValue(sub.Registry).ToString() == "CountAndGrow", "an unset destination did not adopt the carried guns' growth rule");
                Require(envelope.Length < 4096 && !envelope.Contains("Worlds/"), "the envelope carries more than the carried guns (or a folder name)");
                return $"rows equal for an AK with counter/level/kills/reserve and a charging Zeus; envelope {envelope.Length} chars";
            });
            // ---------------------------------------------------------------- round trips
            Test("round-trip-gun-born-in-the-main-world", () => {
                var main = Make("main", 304); var sub = Make("sub", 320);
                int id = Gun(main, ak, 30, 1500); main.Player.Put(0, Item(main, ak, id));
                Gun(sub, glock, 20); Gun(sub, glock, 20);                                   // the sub world's own records 1 and 2
                Trip(main, sub, out _);
                int there = IdOf(sub.Player.Values[0]);
                Change(sub, there, r => { r.Rounds = 12; r.Durability = 1480; });           // used in the sub world
                int mainNext = Next(main), mainCount = Count(main);
                // The world left keeps the traveller's saved inventory with the gun as it was (30 rounds): the provider replaces it on return.
                var back = Trip(sub, main, out _);
                int home = main.Player.Values[0];
                Require(Code(back) == "Ok" && IdOf(home) == id && Next(main) == mainNext && Count(main) == mainCount, $"returned as #{IdOf(home)} (was #{id}); next {Next(main)}/{mainNext}");
                Require(Rounds(main, home) == 12 && ((dynamic)Record(main, id)).Durability == 1480, $"the returned gun reads {Rounds(main, home)} rounds");
                // and out again: the sub world's number for it is the one it had there
                Change(main, id, r => r.Rounds = 30);
                int subNext = Next(sub);
                Trip(main, sub, out _);
                Require(IdOf(sub.Player.Values[0]) == there && Next(sub) == subNext && Rounds(sub, sub.Player.Values[0]) == 30, "the second trip out did not return to the sub world's number for this gun");
                return $"main #{id} -> sub #{there} -> main #{id} -> sub #{there}; no number spent on either return, state follows the gun (30 -> 12 -> 30)";
            });
            Test("round-trip-gun-born-in-the-sub-world", () => {
                var main = Make("main", 304); var sub = Make("sub", 320);
                Gun(main, ak, 30); Gun(main, ak, 30); Gun(main, ak, 30);                    // main's own 1-3
                int id = Gun(sub, glock, 9, 800); sub.Player.Put(4, Item(sub, glock, id));
                Trip(sub, main, out _); int there = IdOf(main.Player.Values[4]);
                Require(there == 4 && Rounds(main, main.Player.Values[4]) == 9, $"in main as #{there}");
                Change(main, there, r => r.Rounds = 2);
                int subNext = Next(sub);
                Trip(main, sub, out _);
                Require(IdOf(sub.Player.Values[4]) == id && Next(sub) == subNext && Rounds(sub, sub.Player.Values[4]) == 2, "the gun did not come home to its own number with its state");
                return $"sub #{id} -> main #{there} -> sub #{id}, 9 -> 2 rounds";
            });
            Test("return-to-a-world-whose-identity-table-was-dropped", () => {
                // The world left was saved meanwhile by a build that keeps no identity table (it keeps the world's own identity):
                // a gun made there still comes home to its own number, because its identity is the one that world gives that record.
                var main = Make("main", 304, Guid.NewGuid().ToString("N")); var sub = Make("sub", 320);
                Gun(main, glock, 20); int id = Gun(main, ak, 30); main.Player.Put(0, Item(main, ak, id));
                Trip(main, sub, out _); Change(sub, IdOf(sub.Player.Values[0]), r => r.Rounds = 4);
                ((IDictionary)ledgerType.GetField("Identities").GetValue(main.Ledger)).Clear();
                int next = Next(main); Trip(sub, main, out _);
                Require(IdOf(main.Player.Values[0]) == id && Next(main) == next && Count(main) == 2 && Rounds(main, main.Player.Values[0]) == 4, $"returned as #{IdOf(main.Player.Values[0])}, next {Next(main)}/{next}, {Count(main)} records");
                return $"home to #{id} with 4 rounds, no number spent";
            });
            Test("an-ordinary-move-leaves-one-usable-gun", () => {
                var main = Make("main", 304); var sub = Make("sub", 320);
                int id = Gun(main, ak, 30); main.Player.Put(0, Item(main, ak, id));
                Trip(main, sub, out _); Trip(sub, main, out _);
                Enter(main); int holders = ((IEnumerable)T("ScGunHolders").GetMethod("Scan").Invoke(null, [main.Project, 304])).Cast<object>().Count();
                Require(holders == 1 && Count(main) == 1, $"main has {holders} holder(s), {Count(main)} record(s)");
                // In the sub world the traveller left with the gun: the provider took its inventory with it, nothing there holds the record.
                sub.Player.Clear(); Enter(sub); int left = ((IEnumerable)T("ScGunHolders").GetMethod("Scan").Invoke(null, [sub.Project, 320])).Cast<object>().Count();
                Require(left == 0 && Count(sub) == 1, "the world left still has a holder of the carried gun");
                return "after there and back: one record and one holder in main; the sub world keeps an unreferenced record, no item";
            });
            // ---------------------------------------------------------------- refusals at the destination: nothing written
            Test("destination-holder-of-the-same-identity-refuses", () => {
                var main = Make("main", 304); var sub = Make("sub", 320);
                int id = Gun(main, ak, 30); main.Player.Put(0, Item(main, ak, id));
                Trip(main, sub, out _); Change(sub, IdOf(sub.Player.Values[0]), r => r.Rounds = 5);
                // Meanwhile the copy the traveller's saved inventory kept in main ended up in a chest there (another player, a provider that left it accessible).
                main.Chest.Put(0, main.Player.Values[0]); main.Player.Clear();
                var export = Export(sub); string before = State(main);
                var import = Import(main, (string)export.Envelope);
                Require(Code(import) == "HeldElsewhere" && !(bool)import.Ok && import.Values == null && State(main) == before, $"{import.Code}: {import.Message}");
                Require(Rounds(main, main.Chest.Values[0]) == 30, "the record another holder uses was overwritten");
                return $"refused ({import.Message}); the record in the chest keeps 30 rounds";
            });
            Test("returning-identity-of-another-model-refuses", () => {
                var main = Make("main", 304); var sub = Make("sub", 320);
                int id = Gun(main, ak, 30); main.Player.Put(0, Item(main, ak, id));
                var export = Export(main);
                // The destination's identity table names a record of another model for this identity (a damaged or foreign table).
                int other = Gun(sub, glock, 20); string identity = (string)ledgerType.GetMethod("IdentityOf").Invoke(main.Ledger, [id]);
                ((IDictionary)ledgerType.GetField("Identities").GetValue(sub.Ledger))[other] = identity;
                string before = State(sub); var import = Import(sub, (string)export.Envelope);
                Require(Code(import) == "ModelConflict" && State(sub) == before, $"{import.Code}");
                return "refused: never written over a record of another model";
            });
            Test("full-table-growth-conflict-and-busy-table-refuse-unchanged", () => {
                var main = Make("main", 304); int id = Gun(main, ak, 30); main.Player.Put(0, Item(main, ak, id));
                Change(main, id, r => { r.CounterInstalled = true; r.GrowthRulesVersion = (int)T("ScGunGrowth").GetField("RulesVersion").GetRawConstantValue(); });
                registryType.GetField("GrowthMode").SetValue(main.Registry, Enum.Parse(T("ScGunGrowthMode"), "CountAndGrow"));
                string envelope = (string)Export(main).Envelope; var codes = new List<string>();
                var full = Make("full", 320); registryType.GetProperty("Next").SetValue(full.Registry, lastId + 1);
                string before = State(full); var r1 = Import(full, envelope); Require(Code(r1) == "RegistryFull" && State(full) == before, $"full table: {r1.Code}"); codes.Add(Code(r1));
                var other = Make("count-only", 320); registryType.GetField("GrowthMode").SetValue(other.Registry, Enum.Parse(T("ScGunGrowthMode"), "CountOnly"));
                before = State(other); var r2 = Import(other, envelope); Require(Code(r2) == "GrowthConflict" && State(other) == before, $"growth: {r2.Code}"); codes.Add(Code(r2));
                var disabled = Make("legacy", 320); registryType.GetField("LegacyWorld").SetValue(disabled.Registry, true);
                var r3 = Import(disabled, envelope); Require(Code(r3) == "RegistryUnavailable", $"disabled table: {r3.Code}"); codes.Add(Code(r3));
                var none = Make("no-ledger", 320); registryType.GetField("Travel").SetValue(none.Registry, null);
                var r4 = Import(none, envelope); Require(Code(r4) == "RegistryUnavailable", $"no ledger: {r4.Code}");
                return string.Join(", ", codes);
            });
            Test("unknown-version-namespace-schema-and-damaged-text-refuse-unchanged", () => {
                var main = Make("main", 304); var sub = Make("sub", 320);
                int id = Gun(main, ak, 30); main.Player.Put(0, Item(main, ak, id)); string envelope = (string)Export(main).Envelope; string before = State(sub);
                var cases = new (string Name, string Text, string Code)[] {
                    ("a later version", envelope.Replace("\"v\":1", "\"v\":2"), "UnsupportedVersion"),
                    ("another namespace", envelope.Replace("zh667.ScCsgoKnives/guns", "someone/else"), "BadEnvelope"),
                    ("no text", "", "BadEnvelope"), ("not JSON", "<xml/>", "BadEnvelope"),
                    ("one round changed", envelope.Replace("r=30", "r=29"), "Corrupt"),
                    ("cut short", envelope[..(envelope.Length / 2)], "BadEnvelope"),
                    ("a later record schema", Reseal(envelope.Replace($"\"schema\":{schema}", $"\"schema\":{schema + 1}")), "UnsupportedData"),
                    ("a row that does not parse", Reseal(envelope.Replace("r=30", "r=3000000")), "Corrupt"),
                };
                foreach (var (name, text, code) in cases) { var r = Import(sub, text); Require(Code(r) == code && !(bool)r.Ok && State(sub) == before, $"{name}: {r.Code} (expected {code})"); }
                Require(Code(Import(sub, envelope, commit: false)) == "Ok" && State(sub) == before, "the preflight wrote something");
                return string.Join("; ", cases.Select(c => $"{c.Name}: {c.Code}"));
                string Reseal(string text) { var doc = System.Text.Json.Nodes.JsonNode.Parse(text).AsObject(); doc.Remove("digest");
                    var changed = System.Text.Json.JsonSerializer.Deserialize(doc.ToJsonString(), T("ScTravelEnvelope"), new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                    return (string)T("ScTravelEnvelope").GetMethod("Encode").Invoke(changed, null); }
            });
            // ---------------------------------------------------------------- refusals in the world left: nothing produced
            Test("export-refuses-what-cannot-be-carried-as-it-is", () => {
                var main = Make("main", 304); int id = Gun(main, ak, 30); int gun = Item(main, ak, id); var codes = new List<string>();
                main.Player.Put(0, gun, 2); var stacked = Export(main); codes.Add(Code(stacked)); Require(Code(stacked) == "Stacked" && stacked.Envelope == null, "stacked");
                main.Player.Clear(); main.Player.Put(0, gun); main.Player.Put(1, gun); var twice = Export(main); codes.Add(Code(twice)); Require(Code(twice) == "DuplicateCarried", "duplicate");
                main.Player.Clear(); main.Player.Put(0, Item(main, ak, 77)); var missing = Export(main); codes.Add(Code(missing)); Require(Code(missing) is "MissingRecord" or "UnsupportedData", "missing record: " + Code(missing));
                main.Player.Clear(); main.Player.Put(0, gun); dynamic kills = registryType.GetField("Kills").GetValue(main.Registry); kills.Enqueue(id, ak);
                var pending = Export(main); codes.Add(Code(pending)); Require(Code(pending) == "PendingObligations", "pending kill: " + Code(pending));
                main.Player.Clear(); var nothing = Export(main); codes.Add(Code(nothing)); Require(Code(nothing) == "NothingCarried" && (bool)nothing.Ok && nothing.Envelope == null, "nothing carried");
                return string.Join(", ", codes);
            });
            // ---------------------------------------------------------------- the same transfer again, restarts, two travellers
            Test("the-same-transfer-again-changes-nothing", () => {
                var main = Make("main", 304); var sub = Make("sub", 320);
                int id = Gun(main, ak, 7); main.Player.Put(0, Item(main, ak, id));
                var export = Export(main); string envelope = (string)export.Envelope; var carried = Carried(main);
                var first = Import(sub, envelope); var map1 = (IReadOnlyDictionary<int, int>)first.Values; Restore(sub, carried, map1);
                int local = IdOf(sub.Player.Values[0]); Change(sub, local, r => r.Rounds = 6);                    // the gun fires once in the destination
                string before = State(sub);
                var again = Import(sub, envelope); var map2 = (IReadOnlyDictionary<int, int>)again.Values;
                Require(Code(again) == "Repeat" && (bool)again.Ok && map1.OrderBy(p => p.Key).SequenceEqual(map2.OrderBy(p => p.Key)) && State(sub) == before, $"{again.Code}; mapping equal {map1.SequenceEqual(map2)}; state unchanged {State(sub) == before}");
                Require(Rounds(sub, sub.Player.Values[0]) == 6 && Count(sub) == 1, "the repeat put the carried state back or spent another number");
                // the same transfer id with other contents is not that transfer
                Change(main, id, r => r.Rounds = 3);
                Enter(main); var decoded = T("ScTravelEnvelope").GetMethod("Decode").Invoke(null, [envelope, null, null]); string transfer = (string)decoded.GetType().GetProperty("Transfer").GetValue(decoded);
                var reused = Import(sub, (string)Export(main, transfer).Envelope);
                Require(Code(reused) == "TransferReused" && State(sub) == before, $"{reused.Code}");
                Require((bool)Complete(sub, envelope).Ok && Code(Import(sub, envelope)) == "Repeat", "a completed transfer is not repeatable");
                return "repeat: same mapping, nothing written (the gun keeps the 6 rounds it has there); a reused transfer id with other contents refused";
            });
            Test("restart-after-commit-and-crash-before-save", () => {
                var main = Make("main", 304); var sub = Make("sub", 320, Guid.NewGuid().ToString("N"));
                Gun(sub, glock, 20);
                int id = Gun(main, ak, 7); main.Player.Put(0, Item(main, ak, id));
                string envelope = (string)Export(main).Envelope; var carried = Carried(main);
                // (the destination as it is on disk before the traveller arrives)
                World Reload(World w, ValuesDictionary table, ValuesDictionary identities, ValuesDictionary receipts) {
                    var x = new XElement("R"); table.Save(x); var copy = new ValuesDictionary(); copy.ApplyOverrides(x);
                    var loaded = Make(w.Name + " (reloaded)", w.Block, (string)ledgerType.GetField("WorldIdentity").GetValue(w.Ledger));
                    loaded.Registry = registryType.GetMethod("Load").Invoke(null, [copy, 0d]); registryType.GetField("Travel").SetValue(loaded.Registry, loaded.Ledger);
                    ledgerType.GetMethod("LoadIdentities").Invoke(loaded.Ledger, [identities]); ledgerType.GetMethod("LoadReceipts").Invoke(loaded.Ledger, [receipts]);
                    return loaded;
                }
                (ValuesDictionary, ValuesDictionary, ValuesDictionary) Saved(World w) { Enter(w); return ((ValuesDictionary)registryType.GetMethod("Save").Invoke(w.Registry, [0d]), (ValuesDictionary)ledgerType.GetMethod("SaveIdentities").Invoke(w.Ledger, null), (ValuesDictionary)ledgerType.GetMethod("SaveReceipts").Invoke(w.Ledger, null)); }
                var disk = Saved(sub);
                Require(disk.Item3 is null, "a world that took no transfer writes a receipts key");
                var first = Import(sub, envelope); var map = (IReadOnlyDictionary<int, int>)first.Values;
                // A: the world was saved after the commit, the game stopped before the provider restored the inventory.
                var after = Saved(sub); var restarted = Reload(sub, after.Item1, after.Item2, after.Item3);
                string before = State(restarted); var again = Import(restarted, envelope);
                Require(Code(again) == "Repeat" && ((IReadOnlyDictionary<int, int>)again.Values).OrderBy(p => p.Key).SequenceEqual(map.OrderBy(p => p.Key)) && State(restarted) == before && Count(restarted) == 2, $"after a restart: {again.Code}");
                Restore(restarted, carried, (IReadOnlyDictionary<int, int>)again.Values); Require((bool)Complete(restarted, envelope).Ok && Rounds(restarted, restarted.Player.Values[0]) == 7, "not completed after the restart");
                var completed = Saved(restarted); var third = Reload(restarted, completed.Item1, completed.Item2, completed.Item3);
                Require(Code(Import(third, envelope)) == "Repeat", "the completed transfer was forgotten by a save and load");
                // B: the game stopped before the destination was saved at all: the import is simply made again.
                var crashed = Reload(sub, disk.Item1, disk.Item2, disk.Item3);
                var redo = Import(crashed, envelope);
                Require(Code(redo) == "Ok" && Count(crashed) == 2 && ((IReadOnlyDictionary<int, int>)redo.Values).OrderBy(p => p.Key).SequenceEqual(map.OrderBy(p => p.Key)), $"after a crash before the save: {redo.Code}");
                // C: the inventory restored with the old values (a provider without the bridge): completion says so.
                var wrong = Reload(sub, disk.Item1, disk.Item2, disk.Item3); Import(wrong, envelope);
                wrong.Player.Put(0, Terrain.ReplaceContents(carried[0].Value, wrong.Block));
                var problem = Complete(wrong, envelope);
                Require(Code(problem) == "SlotMismatch" && !(bool)problem.Ok, $"completion of a wrong restore: {problem.Code}");
                return "saved-then-restarted: repeat with the same mapping, then completed and remembered; crash before the save: imported afresh with the same result; old values restored: completion refused (" + problem.Message + ")";
            });
            Test("a-build-that-does-not-know-the-receipts-key-loads-the-world-and-loses-only-the-repeat-protection", () => {
                var main = Make("main", 304); var sub = Make("sub", 320, Guid.NewGuid().ToString("N"));
                Gun(sub, glock, 20); int id = Gun(main, ak, 7); main.Player.Put(0, Item(main, ak, id));
                string envelope = (string)Export(main).Envelope; var carried = Carried(main);
                var first = Import(sub, envelope); var map = (IReadOnlyDictionary<int, int>)first.Values; Restore(sub, carried, map);
                Enter(sub); var table = (ValuesDictionary)registryType.GetMethod("Save").Invoke(sub.Registry, [0d]); var ids = (ValuesDictionary)ledgerType.GetMethod("SaveIdentities").Invoke(sub.Ledger, null); var receipts = (ValuesDictionary)ledgerType.GetMethod("SaveReceipts").Invoke(sub.Ledger, null);
                // The gun subsystem's saved values as this build writes them, checked by the guard every family build runs before loading.
                var values = new ValuesDictionary(); values.SetValue("GunDataLayout", (int)spec.GetField("DataLayout").GetRawConstantValue()); values.SetValue("GunRegistry", table);
                values.SetValue("GunTravelWorldIdentity", (string)ledgerType.GetField("WorldIdentity").GetValue(sub.Ledger)); values.SetValue("GunTravelIdentities", ids); values.SetValue((string)ledgerType.GetField("ReceiptsKey").GetRawConstantValue(), receipts);
                var x = new XElement("V"); values.Save(x); var copy = new ValuesDictionary(); copy.ApplyOverrides(x);
                T("ScGunSaveGuard").GetMethod("Validate").Invoke(null, [copy]);
                Require(receipts is not null && copy.GetValue<ValuesDictionary>("GunTravelReceipts", null) is { Count: 1 } && copy.GetValue<ValuesDictionary>("GunRegistry").GetValue<int>("Schema") == schema, "the receipts are not one more key beside an unchanged table");
                // Such a build saves the world without the key; this build then loads it: the transfer is not remembered,
                // the gun's identity is: no second number, the record simply takes the carried state again.
                var later = Make("sub (saved by a build without the key)", 320, (string)ledgerType.GetField("WorldIdentity").GetValue(sub.Ledger));
                later.Registry = registryType.GetMethod("Load").Invoke(null, [copy.GetValue<ValuesDictionary>("GunRegistry"), 0d]); registryType.GetField("Travel").SetValue(later.Registry, later.Ledger);
                ledgerType.GetMethod("LoadIdentities").Invoke(later.Ledger, [copy.GetValue<ValuesDictionary>("GunTravelIdentities")]);
                var again = Import(later, envelope);
                Require(Code(again) == "Ok" && Count(later) == 2 && ((IReadOnlyDictionary<int, int>)again.Values).OrderBy(q => q.Key).SequenceEqual(map.OrderBy(q => q.Key)), $"{again.Code}, {Count(later)} records");
                return "the table's schema and rows are untouched by the new key; without it a repeat is imported over the same number (no duplicate)";
            });
            Test("two-travellers-are-separate-transfers", () => {
                var main = Make("main", 304); var sub = Make("sub", 320);
                int a = Gun(main, ak, 7), b = Gun(main, ak, 19);
                var second = new Box(); { var e = Blank<Entity>(); e.m_project = sub.Project; e.m_isAddedToProject = true; e.m_components = [second]; second.m_entity = e; sub.Project.m_entities[e] = true; }
                main.Player.Put(0, Item(main, ak, a)); string first = (string)Export(main).Envelope; int valueA = main.Player.Values[0];
                main.Player.Clear(); main.Player.Put(0, Item(main, ak, b)); string other = (string)Export(main).Envelope; int valueB = main.Player.Values[0];
                var ia = Import(sub, first); sub.Player.Put(0, ((IReadOnlyDictionary<int, int>)ia.Values)[valueA]);
                var ib = Import(sub, other, into: second); second.Put(0, ((IReadOnlyDictionary<int, int>)ib.Values)[valueB]);
                Require(IdOf(sub.Player.Values[0]) != IdOf(second.Values[0]) && Rounds(sub, sub.Player.Values[0]) == 7 && Rounds(sub, second.Values[0]) == 19, "the two travellers' guns share a record");
                Require(Code(Import(sub, first)) == "Repeat" && Code(Import(sub, other, into: second)) == "Repeat" && Count(sub) == 2, "a repeat of one disturbed the other");
                return "two envelopes, two numbers, two receipts";
            });
            Test("same-world-is-not-a-transfer", () => {
                var main = Make("main", 304); int id = Gun(main, ak, 7); main.Player.Put(0, Item(main, ak, id));
                string envelope = (string)Export(main).Envelope; string before = State(main); int next = Next(main);
                var import = Import(main, envelope);
                Require(Code(import) == "SameWorld" && (bool)import.Ok && ((IReadOnlyDictionary<int, int>)import.Values)[main.Player.Values[0]] == main.Player.Values[0] && Next(main) == next, $"{import.Code}");
                Require((bool)Complete(main, envelope).Ok, "completion within one world refused");
                return "a move inside one world maps every value to itself and spends no number";
            });
            // ---------------------------------------------------------------- one mapping rule for the saved-XML path and the live path
            Test("saved-xml-importer-and-live-importer-share-identities-and-the-rule", () => {
                string world = Guid.NewGuid().ToString("N"); var main = Make("main", 302, world); var sub = Make("sub", 302);
                int id = Gun(main, ak, 4, 400); main.Player.Put(0, Item(main, ak, id)); string row = Row(main, id);
                // the saved-XML path's identity for record 1 of a world with that identity
                XElement Value(string n, object v) => new("Value", new XAttribute("Name", n), new XAttribute("Type", v is int ? "int" : "string"), new XAttribute("Value", v));
                XElement Group(string n, params object[] children) => new("Values", new XAttribute("Name", n), children);
                XElement G(XElement p, string n) => p?.Elements("Values").SingleOrDefault(e => (string)e.Attribute("Name") == n);
                XElement Saved(string identity, bool player, params (int Id, string Row)[] records) => new("Project", new XElement("Subsystems", Group("BlocksManager", Value("302", "ScGunBlock")),
                    Group("ScGunBlockBehavior", Value("GunDataLayout", (int)spec.GetField("DataLayout").GetRawConstantValue()), Value("GunTravelWorldIdentity", identity),
                        Group("GunRegistry", Value("Schema", schema), Value("Next", records.Length + 1), Value("GrowthMode", "Unset"), Group("Records", records.Select(r => (object)Value(r.Id.ToString(), r.Row)).ToArray())))),
                    new XElement("Entities", player ? new XElement("Entity", new XAttribute("Name", "MalePlayer"), Group("Inventory", Group("Slots", Group("0", Value("Contents", Item(main, ak, 1)), Value("Count", 1))))) : null));
                var xml = Saved(world, true, (1, row)); T("ScGunTravel").GetMethod("Capture").Invoke(null, [xml, "data:/Worlds/Main"]);
                string xmlIdentity = (string)G(G(xml.Element("Entities").Element("Entity"), "ScGunTravel"), "Identities").Elements("Value").Single().Attribute("Value");
                string liveIdentity = (string)ledgerType.GetMethod("IdentityOf").Invoke(main.Ledger, [id]);
                Require(xmlIdentity == liveIdentity, "the two paths name the same gun differently");
                // the live path takes the gun into the sub world; the sub world is saved; later the saved-XML path brings the same gun: it returns to the same number
                Gun(sub, glock, 20); Trip(main, sub, out _); int there = IdOf(sub.Player.Values[0]);
                Enter(sub); var ids = (ValuesDictionary)ledgerType.GetMethod("SaveIdentities").Invoke(sub.Ledger, null);
                var target = Saved((string)ledgerType.GetField("WorldIdentity").GetValue(sub.Ledger), false, (1, Row(sub, 1)), (there, Row(sub, there)));
                var identities = Group("GunTravelIdentities"); foreach (var pair in ids) identities.Add(Value(pair.Key, pair.Value)); G(target.Element("Subsystems"), "ScGunBlockBehavior").Add(identities);
                target.Element("Entities").Add(new XElement(xml.Element("Entities").Element("Entity")));
                var plan = T("ScGunTravel").GetMethod("Prepare").Invoke(null, [target, "data:/Worlds/Sub"]);
                var document = (XElement)plan.GetType().GetProperty("Document").GetValue(plan);
                var records = G(G(G(document.Element("Subsystems"), "ScGunBlockBehavior"), "GunRegistry"), "Records").Elements("Value").ToDictionary(e => (string)e.Attribute("Name"), e => (string)e.Attribute("Value"));
                string contents = (string)document.Descendants("Value").Single(e => (string)e.Attribute("Name") == "Contents").Attribute("Value");
                Require(records.Count == 2 && IdOf(int.Parse(contents)) == there && records[there.ToString()] == row, $"the saved-XML importer gave the same gun #{IdOf(int.Parse(contents))} where the live importer gave #{there} ({records.Count} records)");
                return $"identity {liveIdentity[..12]}... equal on both paths; live import -> #{there}, saved-XML import of the same gun -> #{there}, no further number";
            });
            Test("provider-entry-points-use-only-engine-and-base-types", () => {
                var signatures = api.GetMethods(BindingFlags.Public | BindingFlags.Static).Select(m => $"{m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})").OrderBy(s => s).ToArray();
                foreach (var m in api.GetMethods(BindingFlags.Public | BindingFlags.Static)) foreach (var type in m.GetParameters().Select(p => p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType).Append(m.ReturnType))
                    Require(type.Assembly != mod && (!type.IsGenericType || type.GetGenericArguments().All(a => a.Assembly != mod)), $"{m.Name} exposes {type.FullName}: a provider would need a reference to this assembly");
                Require(signatures.ToHashSet().SetEquals(new[] { "Boolean CanExport(IInventory, String&)", "Boolean CanImport(IInventory, String, String&)", "Boolean Complete(IInventory, String, String&)", "Boolean Import(IInventory, String, Dictionary`2&, String&)", "String Export(IInventory, String, String&)" }),
                    "the entry points changed: " + string.Join(" | ", signatures));
                // through the facade, as a provider calls it by reflection
                var main = Make("main", 304); var sub = Make("sub", 320); int id = Gun(main, ak, 7); main.Player.Put(0, Item(main, ak, id));
                Enter(main); object[] ex = [main.Player, "player-1", null]; string envelope = (string)api.GetMethod("Export").Invoke(null, ex);
                Enter(sub); object[] im = [sub.Player, envelope, null, null]; bool ok = (bool)api.GetMethod("Import").Invoke(null, im); var values = (Dictionary<int, int>)im[2];
                Require(ex[2] is null && envelope is not null && ok && im[3] is null && values.Count == 1, $"facade: export refusal {ex[2]}, import {ok} {im[3]}");
                sub.Player.Put(0, values[main.Player.Values[0]]); object[] done = [sub.Player, envelope, null];
                Require((bool)api.GetMethod("Complete").Invoke(null, done) && Rounds(sub, sub.Player.Values[0]) == 7, $"facade completion: {done[2]}");
                Require((int)api.GetField("Version").GetRawConstantValue() == 1 && (string)api.GetField("Namespace").GetRawConstantValue() == "zh667.ScCsgoKnives/guns", "version or namespace changed");
                return string.Join(" | ", signatures);
            });
            // ---------------------------------------------------------------- subworld-travel-generic-20261003: no bridge at all
            // The world left was saved before the switch (every sub-world mod found does that), so its Project.xml holds the
            // packet ScGunTravel.Capture writes on every save. A mod that carries only the item values (Ancient World) puts
            // them back after the next world has loaded; ScTravelArrival reads the packet of the world left and maps them.
            // (a delivered build from before the arrival check, run as the baseline: its generic tests fail by name, nothing crashes)
            var arrivalType = mod.GetType("Game.ScTravelArrival"); var gunTravel = T("ScGunTravel");
            int scar = Variant("scar20"), sawedoff = Variant("sawedoff"), layout = (int)spec.GetField("DataLayout").GetRawConstantValue();
            XElement V(string name, object value) => new("Value", new XAttribute("Name", name), new XAttribute("Type", value is int ? "int" : value is double ? "double" : "string"), new XAttribute("Value", Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)));
            /// The world as its last save left it (gun subsystem, block map, game time, the player with one inventory), with the
            /// packet the real save hook writes.
            XElement Saved(World w, string path, double gameTime, Box carried, string inventory = "Inventory") {
                Enter(w);
                var gun = new XElement("Values", new XAttribute("Name", "ScGunBlockBehavior"));
                var reg = new XElement("Values", new XAttribute("Name", "GunRegistry")); ((ValuesDictionary)registryType.GetMethod("Save").Invoke(w.Registry, [0d])).Save(reg); gun.Add(reg);
                if ((ValuesDictionary)ledgerType.GetMethod("SaveIdentities").Invoke(w.Ledger, null) is { } ids) { var x = new XElement("Values", new XAttribute("Name", "GunTravelIdentities")); ids.Save(x); gun.Add(x); }
                gun.Add(V("GunTravelWorldIdentity", (string)ledgerType.GetField("WorldIdentity").GetValue(w.Ledger)), V("GunDataLayout", layout), V("GunTravelSource", path));
                var slots = new XElement("Values", new XAttribute("Name", "Slots"));
                for (int i = 0; i < 12; i++) if (carried.Counts[i] > 0) slots.Add(new XElement("Values", new XAttribute("Name", "Slot" + i), V("Contents", carried.Values[i]), V("Count", carried.Counts[i])));
                var project = new XElement("Project", new XElement("Subsystems", gun,
                        new XElement("Values", new XAttribute("Name", "BlocksManager"), V(w.Block.ToString(), "ScGunBlock")),
                        new XElement("Values", new XAttribute("Name", "GameInfo"), V("TotalElapsedGameTime", gameTime))),
                    new XElement("Entities", new XElement("Entity", new XAttribute("Name", "MalePlayer"),
                        new XElement("Values", new XAttribute("Name", "Player"), V("PlayerIndex", 1)),
                        new XElement("Values", new XAttribute("Name", inventory), slots))));
                gunTravel.GetMethod("Capture").Invoke(null, [project, path]);
                return XElement.Parse(project.ToString());
            }
            const string MainPath = "w/World2", SubPath = "w/World2/Dimensions/Ancient", OtherPath = "w/World5";
            var disk = new Dictionary<string, XElement>();
            Func<string, bool> isWorld = p => disk.ContainsKey(p);
            Func<string, IEnumerable<string>> children = p => disk.Keys.Where(k => k.StartsWith(p + "/", StringComparison.Ordinal)).Select(k => p + "/" + k[(p.Length + 1)..].Split('/')[0]).Distinct().ToList();
            Func<string, XElement> read = p => disk.TryGetValue(p, out var x) ? new XElement(x) : null;
            object Prepare(World w, string here, string left) { Enter(w); Require(arrivalType is not null, "this build has no arrival check (ScTravelArrival)"); return arrivalType.GetMethod("Prepare").Invoke(null, [here, left, false, w.Ledger, isWorld, children, read]); }
            string Step(object arrival, World w, IInventory inventory, double now) { Enter(w); return (string)arrivalType.GetMethod("Step").Invoke(arrival, [w.Project, inventory, w.Block, now]); }
            bool Finished(object arrival) => (bool)arrivalType.GetProperty("Finished").GetValue(arrival);
            /// The mod's part: the traveller's inventory here becomes the carried values, block index translated, nothing else.
            void Carry(Box from, Box to, int toBlock, int fromBlock) { to.Clear(); for (int i = 0; i < 12; i++) if (from.Counts[i] > 0) to.Put(i, Terrain.ExtractContents(from.Values[i]) == fromBlock ? Terrain.ReplaceContents(from.Values[i], toBlock) : from.Values[i], from.Counts[i]); }
            string Settle(object arrival, World w, IInventory inventory, double from = 0) { string said = null; for (double t = from; t <= from + 3 && !Finished(arrival); t += 0.25) said = Step(arrival, w, inventory, t) ?? said; return said; }
            Test("generic-arrival-the-reported-trip", () => {
                // Game(2) (1).log, 2026-10-03: main world without records takes a fresh SCAR-20 into the sub-world, fires it there
                // (its record is made there, #1), comes back; the main world meanwhile has its own #1 (a sawed-off). Then out again.
                disk.Clear(); var main = Make("main", 304, Guid.NewGuid().ToString("N")); var sub = Make("sub", 320, Guid.NewGuid().ToString("N"));
                int theirs = Gun(main, sawedoff, 5, 250); main.Chest.Put(0, Item(main, sawedoff, theirs));
                main.Player.Put(0, Terrain.MakeBlockValue(304, 0, (int)spec.GetMethod("WithId").Invoke(null, [scar, 0])));    // a fresh SCAR-20: no record
                disk[MainPath] = Saved(main, MainPath, 10, main.Player);
                Carry(main.Player, sub.Player, 320, 304); disk[SubPath] = Saved(sub, SubPath, 1, new Box());
                Require(Prepare(sub, SubPath, MainPath) is null, "a trip with only fresh guns needs no arrival check");
                int fired = Gun(sub, scar, 19, 480); sub.Player.Put(0, Item(sub, scar, fired));                                   // the first shot makes the record there
                Require(fired == 1 && theirs == 1, "fixture: both worlds' #1");
                disk[SubPath] = Saved(sub, SubPath, 20, sub.Player);
                Carry(sub.Player, main.Player, 304, 320);
                int before = main.Player.Values[0];
                Require(!Known(main, before) || Rounds(main, before) != 19, "fixture: the bare value should not read the carried state");
                var arrival = Prepare(main, MainPath, SubPath); Require(arrival is not null, "no arrival check for a trip between a world and its sub-world");
                bool Pending(object a, int slot, int value) => (bool)arrivalType.GetMethod("Pending").Invoke(a, [slot, value]);
                Require(Pending(arrival, 0, before) && !Pending(arrival, 1, before), "the arriving gun is not held back before it has its local number (or another slot is)");
                string said = Settle(arrival, main, main.Player);
                Require(!Pending(arrival, 0, main.Player.Values[0]), "the gun is still held back after it was mapped");
                int home = main.Player.Values[0];
                Require(IdOf(home) == 2 && Known(main, home) && Rounds(main, home) == 19 && Row(main, 2) == Row(sub, 1), $"arrived as #{IdOf(home)}, usable {Known(main, home)}, rounds {Rounds(main, home)}: {said}");
                Require(Rounds(main, main.Chest.Values[0]) == 5, "the main world's own #1 (sawed-off) changed");
                // used at home, then out again: back to its number in the sub-world, with the new state
                Change(main, 2, r => r.Rounds = 15); disk[MainPath] = Saved(main, MainPath, 30, main.Player);
                Carry(main.Player, sub.Player, 320, 304); int subNext = Next(sub);
                var out2 = Prepare(sub, SubPath, MainPath); Settle(out2, sub, sub.Player);
                Require(IdOf(sub.Player.Values[0]) == 1 && Rounds(sub, sub.Player.Values[0]) == 15 && Next(sub) == subNext, $"out again as #{IdOf(sub.Player.Values[0])} reading {Rounds(sub, sub.Player.Values[0])}");
                // reopened after a restart (no world left in this session): the departure is taken already, nothing to do
                Require(Prepare(sub, SubPath, null) is null, "the same departure would be taken in twice");
                return $"fresh SCAR-20 -> fired in the sub-world (#1 there, 19 rounds) -> home as #2 (main's own #1 sawed-off untouched) -> 15 rounds -> sub #1 again; {said}";
            });
            Test("generic-arrival-not-for-an-unrelated-world", () => {
                disk.Clear(); var main = Make("main", 304, Guid.NewGuid().ToString("N")); var other = Make("other", 304, Guid.NewGuid().ToString("N"));
                int id = Gun(other, ak, 9); other.Player.Put(0, Item(other, ak, id)); disk[OtherPath] = Saved(other, OtherPath, 5, other.Player);
                disk[MainPath] = Saved(main, MainPath, 5, main.Player); main.Player.Put(0, other.Player.Values[0]);   // even the same value in the same slot
                Require(Prepare(main, MainPath, OtherPath) is null, "an arrival check between two different top-level worlds (the Command Block mod's \"world\", the world list)");
                Require(Prepare(main, MainPath, MainPath) is null, "an arrival check for a world reopened");
                return "World5 -> World2 and World2 -> World2: no check";
            });
            Test("generic-arrival-after-a-restart-takes-only-a-lone-match", () => {
                disk.Clear(); var main = Make("main", 304, Guid.NewGuid().ToString("N")); var a = Make("a", 320, Guid.NewGuid().ToString("N")); var b = Make("b", 330, Guid.NewGuid().ToString("N"));
                int ia = Gun(a, ak, 3); a.Player.Put(1, Item(a, ak, ia)); int ib = Gun(b, ak, 4); b.Player.Put(1, Item(b, ak, ib));
                disk[MainPath] = Saved(main, MainPath, 1, main.Player); disk[MainPath + "/A"] = Saved(a, MainPath + "/A", 2, a.Player); disk[MainPath + "/B"] = Saved(b, MainPath + "/B", 3, b.Player);
                Carry(a.Player, main.Player, 304, 320);
                int count = Count(main);
                var both = Prepare(main, MainPath, null); string said = Settle(both, main, main.Player);
                Require(Finished(both) && Count(main) == count && IdOf(main.Player.Values[1]) == ia && said is not null, $"two sub-worlds left the same carried gun data: one was taken ({said})");
                b.Player.Clear(); disk[MainPath + "/B"] = Saved(b, MainPath + "/B", 4, b.Player);
                var one = Prepare(main, MainPath, null); Settle(one, main, main.Player);
                Require(Count(main) == count + 1 && Rounds(main, main.Player.Values[1]) == 3, "the lone match was not taken");
                return $"ambiguous: refused ({said}); lone: taken";
            });
            Test("generic-arrival-a-late-second-restore-is-mapped-again", () => {
                disk.Clear(); var main = Make("main", 304, Guid.NewGuid().ToString("N")); var sub = Make("sub", 320, Guid.NewGuid().ToString("N"));
                Gun(main, ak, 30); int id = Gun(sub, ak, 8); sub.Player.Put(2, Item(sub, ak, id));                              // main's own #1 is an AK too
                disk[SubPath] = Saved(sub, SubPath, 7, sub.Player); disk[MainPath] = Saved(main, MainPath, 7, main.Player);
                Carry(sub.Player, main.Player, 304, 320);
                var arrival = Prepare(main, MainPath, SubPath); Settle(arrival, main, main.Player);
                int mapped = main.Player.Values[2]; Require(IdOf(mapped) == 2 && Rounds(main, mapped) == 8, "first restore not mapped");
                Carry(sub.Player, main.Player, 304, 320);                                                                        // the mod puts the carried values back once more
                for (double t = 4; t <= 6; t += 0.25) Step(arrival, main, main.Player, t);
                Require(main.Player.Values[2] == mapped && Rounds(main, main.Player.Values[2]) == 8 && Count(main) == 2, "the second restore was not mapped the same way");
                return "same-model collision: #2 with 8 rounds, also after the mod restored the values a second time";
            });
            // ---------------------------------------------------------------- the real provider package
            string package = Environment.GetEnvironmentVariable("SC_ANCIENT_PACKAGE");
            if (string.IsNullOrEmpty(package) || !File.Exists(package)) results.Add(new("item-travel/ancient-world-0.41.16-real-assembly", true, "NOT RUN: SC_ANCIENT_PACKAGE not supplied (the provider's package is read on the Windows side only)"));
            else Test("ancient-world-0.41.16-real-assembly", () => {
                byte[] dll; string packageSha, version;
                using (var outer = ZipFile.OpenRead(package)) {
                    packageSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package))).ToLowerInvariant();
                    var scmod = outer.Entries.Single(e => e.FullName.EndsWith(".scmod", StringComparison.OrdinalIgnoreCase));
                    using var inner = new MemoryStream(); using (var s = scmod.Open()) s.CopyTo(inner); inner.Position = 0;
                    using var mod0 = new System.IO.Compression.ZipArchive(inner, System.IO.Compression.ZipArchiveMode.Read);
                    using var d = new MemoryStream(); using (var s = mod0.Entries.Single(e => e.FullName.EndsWith("Kelly.Survivalcraft.AncientWorld.dll", StringComparison.OrdinalIgnoreCase)).Open()) s.CopyTo(d); dll = d.ToArray();
                    using var info = new StreamReader(mod0.Entries.Single(e => e.FullName.EndsWith("modinfo.json", StringComparison.OrdinalIgnoreCase)).Open()); version = (string)System.Text.Json.Nodes.JsonNode.Parse(info.ReadToEnd())["Version"];
                }
                string dllSha = Convert.ToHexString(SHA256.HashData(dll)).ToLowerInvariant();
                Require(version == "0.41.16" && dllSha == "f9fe4ccfde6550ec01c76adb3dce2b9820c736e2cb3d3ad8f53ad7abb01df5b9", $"not the analysed package: version {version}, dll {dllSha}");
                var ancient = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(mod).LoadFromStream(new MemoryStream(dll));
                var snapshotType = ancient.GetType("Game.AncientWorldRuntime", true).GetNestedType("InventorySnapshot", Any);
                Require(dll.AsSpan().IndexOf("ScGunTravelApi"u8) < 0 && dll.AsSpan().IndexOf(System.Text.Encoding.Unicode.GetBytes("ScGunTravelApi")) < 0, "this package already names the CS travel entry points: the baseline below is out of date");
                object Capture(IInventory inventory) => snapshotType.GetMethod("Capture").Invoke(null, [inventory]);
                object Roundtrip(object snapshot) => snapshotType.GetMethod("Load").Invoke(null, [XElement.Parse(((XElement)snapshotType.GetMethod("Save").Invoke(snapshot, ["Inventory"])).ToString())]);
                void Apply(object snapshot, IInventory inventory) => snapshotType.GetMethod("Apply").Invoke(snapshot, [inventory]);
                ComponentCreativeInventory Creative(World w, int value) { var inv = new ComponentCreativeInventory { OpenSlotsCount = 10 }; for (int i = 0; i < 10; i++) inv.m_slots.Add(i == 0 ? value : 0); var e = Blank<Entity>(); e.m_project = w.Project; e.m_isAddedToProject = true; e.m_components = [inv]; inv.m_entity = e; w.Project.m_entities[e] = true; return inv; }
                var lines = new List<string>();
                foreach (var (label, variant, rounds) in new[] { ("another model under the same number", glock, 18), ("the same model under the same number", ak, 23) }) {
                    // ---- as the package ships
                    var main = Make("main", 304); var sub = Make("sub", 320);
                    int id = Gun(main, ak, 7, 640); Gun(sub, variant, rounds);
                    Enter(main); var source = Creative(main, Item(main, ak, id)); var carried = Roundtrip(Capture(source));
                    Enter(sub); var arrival = Creative(sub, 0); Apply(carried, arrival); int plain = arrival.GetSlotValue(0);
                    bool known = Known(sub, plain); int read = known ? Rounds(sub, plain) : 0;
                    Require(Terrain.ExtractContents(plain) == 320 && IdOf(plain) == 1 && (variant == ak ? known && read == 23 : !known), $"baseline changed for {label}: block {Terrain.ExtractContents(plain)}, id {IdOf(plain)}, usable {known}, rounds {read}");
                    // ---- with the bridge: export where the snapshot is captured, the text carried beside the provider's own XML,
                    // import before Apply, the snapshot's values translated, completion after Apply
                    main = Make("main", 304); sub = Make("sub", 320); id = Gun(main, ak, 7, 640); Gun(sub, variant, rounds);
                    Enter(main); source = Creative(main, Item(main, ak, id));
                    object[] ex = [source, "player-1", null]; string envelope = (string)api.GetMethod("Export").Invoke(null, ex); Require(envelope is not null && ex[2] is null, "export refused: " + ex[2]);
                    var journal = new XElement("AncientWorldTravel", (XElement)snapshotType.GetMethod("Save").Invoke(Capture(source), ["Inventory"]), new XElement("Extensions", new XElement("Item", new XAttribute("Namespace", "zh667.ScCsgoKnives/guns"), envelope)));
                    journal = XElement.Parse(journal.ToString());
                    Enter(sub); arrival = Creative(sub, 0); var loaded = snapshotType.GetMethod("Load").Invoke(null, [journal.Element("Inventory")]);
                    string text = journal.Element("Extensions").Elements("Item").Single(i => (string)i.Attribute("Namespace") == "zh667.ScCsgoKnives/guns").Value;
                    object[] im = [arrival, text, null, null]; Require((bool)api.GetMethod("Import").Invoke(null, im), "import refused: " + im[3]); var values = (Dictionary<int, int>)im[2];
                    foreach (object stack in (IEnumerable)snapshotType.GetField("Slots").GetValue(loaded)) {
                        MemberInfo member = (MemberInfo)stack.GetType().GetField("Value", Any) ?? stack.GetType().GetProperty("Value", Any);
                        int held = member is FieldInfo f ? (int)f.GetValue(stack) : (int)((PropertyInfo)member).GetValue(stack);
                        if (!values.TryGetValue(held, out int mapped)) continue;
                        if (member is FieldInfo g) g.SetValue(stack, mapped); else ((PropertyInfo)member).SetValue(stack, mapped);
                    }
                    Apply(loaded, arrival); int bridged = arrival.GetSlotValue(0);
                    object[] done = [arrival, text, null]; Require((bool)api.GetMethod("Complete").Invoke(null, done), "completion refused: " + done[2]);
                    Require(Terrain.ExtractContents(bridged) == 320 && IdOf(bridged) == 2 && Known(sub, bridged) && Rounds(sub, bridged) == 7 && Row(sub, 2) == Row(main, id), $"bridged {label}: id {IdOf(bridged)}, usable {Known(sub, bridged)}, rounds {Rounds(sub, bridged)}");
                    lines.Add($"{label} ({rounds} rounds there): as shipped -> #{IdOf(plain)}, usable {known}, reads {read}; with the three calls -> #{IdOf(bridged)}, usable, reads 7");
                }
                // the provider's own XML keeps nothing it does not know: an extension must be saved and loaded by the provider itself
                var probe = Make("main", 304); Enter(probe); var xml = (XElement)snapshotType.GetMethod("Save").Invoke(Capture(Creative(probe, Item(probe, ak, Gun(probe, ak, 7)))), ["Inventory"]);
                xml.Add(new XElement("Extensions", new XElement("Item", "x"))); var resaved = (XElement)snapshotType.GetMethod("Save").Invoke(snapshotType.GetMethod("Load").Invoke(null, [xml]), ["Inventory"]);
                Require(resaved.Element("Extensions") is null, "the provider now keeps unknown extension elements: the patch note is out of date");
                // ---- subworld-travel-generic-20261003: the package exactly as it ships, no bridge. The sub-world was saved before
                // the switch (its packet), the package restores the bare values after loading, the arrival check maps them.
                {
                    disk.Clear(); var gm = Make("main", 304, Guid.NewGuid().ToString("N")); var gs = Make("sub", 320, Guid.NewGuid().ToString("N"));
                    int own = Gun(gm, sawedoff, 5); var chest = gm.Chest; chest.Put(0, Item(gm, sawedoff, own));
                    int fired = Gun(gs, scar, 19, 480); var source = Creative(gs, Item(gs, scar, fired));
                    var saved = new Box(); saved.Put(0, source.GetSlotValue(0));
                    disk[SubPath] = Saved(gs, SubPath, 20, saved, "CreativeInventory"); disk[MainPath] = Saved(gm, MainPath, 5, new Box(), "CreativeInventory");
                    Enter(gs); var carried = Roundtrip(Capture(source));
                    Enter(gm); var arrival = Creative(gm, 0); Apply(carried, arrival); int bare = arrival.GetSlotValue(0);
                    var check = Prepare(gm, MainPath, SubPath); Require(check is not null, "no arrival check after the package's own trip");
                    string said = Settle(check, gm, arrival); int mapped = arrival.GetSlotValue(0);
                    Require(IdOf(bare) == 1 && Terrain.ExtractContents(bare) == 304 && IdOf(mapped) == 2 && Known(gm, mapped) && Rounds(gm, mapped) == 19 && Row(gm, 2) == Row(gs, fired) && Rounds(gm, chest.Values[0]) == 5,
                        $"generic arrival with the real package: bare #{IdOf(bare)}, mapped #{IdOf(mapped)}, usable {Known(gm, mapped)}, rounds {Rounds(gm, mapped)} ({said})");
                    lines.Add($"no bridge, generic arrival check: the package restored #{IdOf(bare)} (the main world's own #1 is a sawed-off); mapped to #{IdOf(mapped)}, usable, reads 19");
                }
                return $"package {packageSha[..16]}, dll {dllSha[..16]} (0.41.16): " + string.Join(" || ", lines) + "; its own inventory XML drops an unknown Extensions element";
            });
        }
        finally {
            current.SetValue(null, savedRegistry); GameManager.m_project = savedProject;
            Array.Copy(savedBlocks, BlocksManager.Blocks, savedBlocks.Length);
            BlocksManager.BlockTypeToIndex.Clear(); foreach (var p in savedTypes) BlocksManager.BlockTypeToIndex[p.Key] = p.Value;
        }
        return results;
    }
}
