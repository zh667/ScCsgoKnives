using Engine;
using GameEntitySystem;
using TemplatesDatabase;
namespace Game;

/// <summary>Offline checks for the kill counter, the ten-level growth and the schema 3 record format.
///
/// These are decode/transaction assertions on real serialisation, not a claim of device acceptance: they say the
/// rules compute and persist correctly, not that the guns feel right in a hand.</summary>
public static class ScGunGrowthSelfTest {
    sealed class Inventory : IInventory {
        public Project Project => null;
        public int SlotsCount => 8;
        public int VisibleSlotsCount { get; set; } = 8;
        public int ActiveSlotIndex { get; set; }
        public readonly int[] Values = new int[8], Counts = new int[8];
        public int RefuseSlot = -1, ThrowOnRemoveSlot = -1;
        public int GetSlotValue(int i) => Values[i];
        public int GetSlotCount(int i) => Counts[i];
        public int GetSlotCapacity(int i, int v) => i == 0 ? 1 : 40;
        public int GetSlotProcessCapacity(int i, int v) => 0;
        public void AddSlotItems(int i, int v, int n) { if (n == 0) return; if (Counts[i] > 0 && Values[i] != v) throw new InvalidOperationException("mixed slot"); Values[i] = v; Counts[i] += n; }
        public int RemoveSlotItems(int i, int n) { if (i == ThrowOnRemoveSlot) throw new InvalidOperationException("injected removal failure"); if (i == RefuseSlot) return 0; n = Math.Min(n, Counts[i]); Counts[i] -= n; return n; }
        public void ProcessSlotItems(int i, int v, int count, int process, out int result, out int resultCount) { result = v; resultCount = 0; }
        public void DropAllItems(Vector3 position) => Array.Clear(Counts);
    }
    const int GunBlock = 512;
    /// <summary>Stand-in item values for the material and ammunition blocks, which are not registered headlessly.
    /// The transaction only ever compares item values, so distinct constants exercise the same paths.</summary>
    const int Ammo = 900, Blank = 950, Mechanism = 951, Glass = 952, Germanium = 953;
    static Dictionary<int, int> TestInstallCost() => new() { [Blank] = 1, [Mechanism] = 1, [Glass] = 1, [Germanium] = 1 };
    static int Variant(string name) => Array.FindIndex(GunSpec.All, g => g.Name == name);
    static ScGunRegistry Fresh() => ScGunRegistry.Current = new ScGunRegistry { GrowthMode = ScGunGrowthMode.CountAndGrow };
    static (Inventory Inventory, int Id) Gun(ScGunRegistry registry, string name, int rounds = -1, int durability = -1, bool counter = true, int level = 0, long kills = 0) {
        int variant = Variant(name);
        int max = ScGunGrowth.MaxDurability(variant, level);
        int id = registry.Allocate(variant, rounds < 0 ? ScGunGrowth.Capacity(variant, level) : rounds, false, durability < 0 ? max : durability, max);
        registry.TryGetSnapshot(id, out _);
        if (counter) Apply(registry, id, r => { r.CounterInstalled = true; r.KillCount = kills; r.AppliedGrowthLevel = level; r.GrowthRulesVersion = ScGunGrowth.RulesVersion; });
        var inventory = new Inventory();
        inventory.AddSlotItems(0, Terrain.MakeBlockValue(GunBlock, 0, GunSpec.WithId(variant, id)), 1);
        return (inventory, id);
    }
    /// <summary>Direct record edit for test setup only; gameplay never has this path.</summary>
    static void Apply(ScGunRegistry registry, int id, Action<ScGunRecord> change) {
        var field = typeof(ScGunRegistry).GetField("m_records", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var records = (Dictionary<int, ScGunRecord>)field.GetValue(registry);
        change(records[id]);
    }
    static ScGunSnapshot Snap(ScGunRegistry registry, int id) { registry.TryGetSnapshot(id, out var s); return s; }
    /// <summary>A fresh gun's item value without asking BlocksManager for the block index.</summary>
    static int Template(int variant) => Terrain.MakeBlockValue(GunBlock, 0, GunSpec.WithId(variant, GunSpec.FreshFull));

    public static void Run(Action<string, bool, string> check) {
        void Test(string name, Func<bool> test) { try { check("growth/" + name, test(), name); } catch (Exception e) { check("growth/" + name, false, e.ToString()); } }
        var saved = ScGunRegistry.Current;
        ScGunGrowth.InstallCostOverride = TestInstallCost;
        try { Checks(Test); } finally { ScGunRegistry.Current = saved; ScGunGrowth.InstallCostOverride = null; }
    }

    static void Checks(Action<string, Func<bool>> Test) {
        // --- thresholds -------------------------------------------------------------------------------------
        Test("levels-are-100-each-to-1000", () =>
            ScGunGrowth.LevelFor(0) == 0 && ScGunGrowth.LevelFor(99) == 0 && ScGunGrowth.LevelFor(100) == 1
            && ScGunGrowth.LevelFor(199) == 1 && ScGunGrowth.LevelFor(200) == 2 && ScGunGrowth.LevelFor(999) == 9
            && ScGunGrowth.LevelFor(1000) == 10 && ScGunGrowth.LevelFor(50000) == 10
            && ScGunGrowth.KillsFor(10) == 1000 && ScGunGrowth.ToNextLevel(999) == 1 && ScGunGrowth.ToNextLevel(1000) == 0);
        // --- capacity ---------------------------------------------------------------------------------------
        Test("capacity-table", () => {
            int ak = Variant("ak47"), m4 = Variant("m4a1s"), awp = Variant("awp"), nova = Variant("nova"), negev = Variant("negev"), taser = Variant("taser");
            int[] akWant = [30, 33, 36, 39, 42, 45], m4Want = [20, 22, 24, 26, 28, 30], awpWant = [5, 5, 6, 6, 7, 7], novaWant = [8, 8, 9, 10, 11, 12], negevWant = [150, 165, 180, 195, 210, 225];
            for (int i = 0; i < 6; i++) {
                int level = i * 2;
                if (ScGunGrowth.Capacity(ak, level) != akWant[i] || ScGunGrowth.Capacity(m4, level) != m4Want[i]
                    || ScGunGrowth.Capacity(awp, level) != awpWant[i] || ScGunGrowth.Capacity(nova, level) != novaWant[i]
                    || ScGunGrowth.Capacity(negev, level) != negevWant[i] || ScGunGrowth.Capacity(taser, level) != 1) return false;
            }
            return true;
        });
        Test("capacity-never-shrinks-with-level", () => {
            for (int v = 0; v < GunSpec.All.Length; v++) for (int l = 1; l <= ScGunGrowth.MaxLevel; l++)
                if (ScGunGrowth.Capacity(v, l) < ScGunGrowth.Capacity(v, l - 1)) return false;
            return true;
        });
        // --- durability, damage, charge ---------------------------------------------------------------------
        Test("max-durability-plus-50-percent", () =>
            ScGunGrowth.MaxDurability(Variant("ak47"), 10) == 2250 && ScGunGrowth.MaxDurability(Variant("ak47"), 0) == 1500
            && ScGunGrowth.MaxDurability(Variant("awp"), 10) == 300 && ScGunGrowth.MaxDurability(Variant("taser"), 10) == 150
            && ScGunGrowth.MaxDurability(Variant("negev"), 10) == 6000);
        Test("durability-scales-by-ratio", () =>
            ScGunGrowth.ScaleDurability(750, 1500, 2250) == 1125 && ScGunGrowth.ScaleDurability(0, 1500, 2250) == 0
            && ScGunGrowth.ScaleDurability(1500, 1500, 2250) == 2250 && ScGunGrowth.ScaleDurability(1, 1500, 2250) == 1
            && ScGunGrowth.ScaleDurability(1499, 1500, 2250) == 2248);
        Test("zeus-damage-150-to-225", () => {
            float lv0 = ScSurvivalBalance.Power("taser"), lv10 = lv0 * ScGunGrowth.DamageMultiplier(10);
            return Math.Abs(lv0 - 150f) < .001f && Math.Abs(lv10 - 225f) < .001f && ScHeadshot.MultiplierFor(GunSpec.ForAsset("taser")) == 1f;
        });
        Test("damage-plus-50-percent", () => {
            float ak = ScSurvivalBalance.Power("ak47");
            return Math.Abs(ak - 15f) < .001f && Math.Abs(ak * ScGunGrowth.DamageMultiplier(10) - 22.5f) < .001f
                && Math.Abs(ScGunGrowth.DamageMultiplier(5) - 1.25f) < .0001f;
        });
        Test("charge-10-to-5", () => {
            var zeus = GunSpec.ForAsset("taser");
            return Math.Abs(ScGunGrowth.RechargeSeconds(zeus, 0) - 10f) < .001f
                && Math.Abs(ScGunGrowth.RechargeSeconds(zeus, 1) - 9.5f) < .001f
                && Math.Abs(ScGunGrowth.RechargeSeconds(zeus, 10) - 5f) < .001f
                && ScGunGrowth.RechargeSeconds(GunSpec.ForAsset("ak47"), 10) == 0;
        });
        Test("charge-keeps-remaining-ratio", () =>
            Math.Abs(ScGunGrowth.ScaleRemaining(5, 10, 5) - 2.5) < 1e-9
            && Math.Abs(ScGunGrowth.ScaleRemaining(2.75, 5.5f, 5) - 2.5) < 1e-9
            && ScGunGrowth.ScaleRemaining(0, 10, 5) == 0);
        // --- angles and range -------------------------------------------------------------------------------
        Test("angles-zero-only-at-max", () => {
            for (int l = 0; l < ScGunGrowth.MaxLevel; l++) if (Math.Abs(ScGunGrowth.AngleScale(l) - (1f - .1f * l)) > 1e-5f) return false;
            return ScGunGrowth.AngleScale(ScGunGrowth.MaxLevel) == 0f;
        });
        Test("range-unlimited-only-for-bullet-guns", () => {
            int awp = Variant("awp"), taser = Variant("taser");
            return ScGunGrowth.UnlimitedRange(awp, 10) && !ScGunGrowth.UnlimitedRange(awp, 9) && !ScGunGrowth.UnlimitedRange(taser, 10)
                && Math.Abs(ScGunGrowth.Range(awp, 9, 64) - 208f) < .01f
                && Math.Abs(ScGunGrowth.Range(taser, 10, 3.05f) - 4.575f) < .001f
                && float.IsFinite(ScGunGrowth.Range(awp, 10, 64));
        });
        Test("effective-stats-follow-level", () => {
            var registry = Fresh();
            var (inventory, id) = Gun(registry, "ak47", level: 10, kills: 1000);
            var spec = GunSpec.ForAsset("ak47");
            var s = EffectiveGunStats.Resolve(spec, inventory.GetSlotValue(0), false);
            return s.Level == 10 && s.Capacity == 45 && s.MaxDurability == 2250 && Math.Abs(s.Power - 22.5f) < .001f
                && s.AngleScale == 0f && s.UnlimitedRange && Math.Abs(s.Falloff(spec, 500) - 1f) < 1e-6f
                && id > 0;
        });
        // --- schema 3 round trip ----------------------------------------------------------------------------
        Test("schema-3-round-trip", () => {
            var registry = Fresh();
            var (inventory, id) = Gun(registry, "m4a1s", rounds: 7, durability: 900, level: 3, kills: 357);
            Apply(registry, id, r => { r.SilencerOff = true; r.RechargeReadyAt = 100; r.RechargeCycleSeconds = 8.5f; r.ReserveOverflowRounds = 4; r.PendingGrowthLevel = 4; });
            var before = Snap(registry, id);
            var saved = registry.Save(90);
            var reloaded = ScGunRegistry.Load(saved, 90);
            var after = Snap(reloaded, id);
            return !reloaded.UnknownSchema && reloaded.LoadedSchema == ScGunRegistry.Schema
                && after.Variant == before.Variant && after.Rounds == 7 && after.SilencerOff && after.Durability == 900
                && after.MaxDurability == before.MaxDurability && after.CounterInstalled && after.KillCount == 357
                && after.AppliedGrowthLevel == 3 && after.PendingGrowthLevel == 4 && after.GrowthRulesVersion == ScGunGrowth.RulesVersion
                && Math.Abs(after.RechargeCycleSeconds - 8.5f) < .001f && Math.Abs(after.RechargeReadyAt - 100) < .01
                && after.ReserveOverflowRounds == 4 && reloaded.QuarantinedCount == 0;
        });
        Test("schema-1-and-2-convert", () => {
            var d = new ValuesDictionary();
            d.SetValue("Schema", ScGunRegistry.SchemaWithoutSkins); d.SetValue("Next", 3);
            var records = new ValuesDictionary();
            records.SetValue("1", "0,17,1,900,1500,4,-1");
            d.SetValue("Records", records);
            var one = ScGunRegistry.Load(d, 0);
            var s1 = Snap(one, 1);
            var d2 = new ValuesDictionary();
            d2.SetValue("Schema", ScGunRegistry.SchemaWithoutGrowth); d2.SetValue("Next", 3);
            var records2 = new ValuesDictionary();
            records2.SetValue("1", "0,17,1,900,1500,4,12.5,180");
            d2.SetValue("Records", records2);
            var two = ScGunRegistry.Load(d2, 100);
            var s2 = Snap(two, 1);
            return one.QuarantinedCount == 0 && s1.Rounds == 17 && s1.SilencerOff && s1.Durability == 900 && s1.MaxDurability == 1500
                && s1.Revision == 4 && s1.SkinId == ScGunSkinCatalog.None && !s1.CounterInstalled && s1.KillCount == 0
                && s1.AppliedGrowthLevel == 0 && s1.PendingGrowthLevel == ScGunGrowth.NoPending && s1.GrowthRulesVersion == 0
                && two.QuarantinedCount == 0 && s2.SkinId == 180 && Math.Abs(s2.RechargeReadyAt - 112.5) < .01 && !s2.CounterInstalled;
        });
        Test("unknown-schema-kept-verbatim", () => {
            var d = new ValuesDictionary();
            d.SetValue("Schema", ScGunRegistry.Schema + 1); d.SetValue("Next", 5);
            var records = new ValuesDictionary(); records.SetValue("1", "anything at all"); d.SetValue("Records", records);
            var registry = ScGunRegistry.Load(d, 0);
            var written = registry.Save(0);
            return registry.UnknownSchema && registry.Disabled && ReferenceEquals(written, d)
                && written.GetValue<int>("Schema", 0) == ScGunRegistry.Schema + 1;
        });
        Test("bad-rows-quarantined-verbatim", () => {
            var d = new ValuesDictionary();
            d.SetValue("Schema", ScGunRegistry.Schema); d.SetValue("Next", 9);
            var records = new ValuesDictionary();
            string good = "v=0,r=5,s=0,d=10,m=1500,n=0,c=-1,p=0,ct=0,k=0,gl=0,gp=-1,gv=0,rc=0,ov=0";
            records.SetValue("1", good);
            records.SetValue("2", "v=0,r=5,s=0,d=10,m=1500,n=0,c=-1,p=0,ct=0,k=0,gl=0,gp=-1,gv=0,rc=0");        // a field short
            records.SetValue("3", good + ",zz=1");                                                                // an unknown field
            records.SetValue("4", "v=0,r=99999,s=0,d=10,m=1500,n=0,c=-1,p=0,ct=0,k=0,gl=0,gp=-1,gv=0,rc=0,ov=0"); // over capacity
            records.SetValue("5", "v=0,r=5,s=0,d=10,m=1500,n=0,c=-1,p=0,ct=0,k=7,gl=0,gp=-1,gv=0,rc=0,ov=0");     // kills without a counter
            records.SetValue("6", "v=0,r=5,s=0,d=10,m=1500,n=0,c=-1,p=0,ct=1,k=0,gl=11,gp=-1,gv=1,rc=0,ov=0");    // level past the cap
            d.SetValue("Records", records);
            var registry = ScGunRegistry.Load(d, 0);
            var written = registry.Save(0).GetValue<ValuesDictionary>("Records", null);
            return registry.Count == 1 && registry.QuarantinedCount == 5
                && written.GetValue<string>("2", "") == "v=0,r=5,s=0,d=10,m=1500,n=0,c=-1,p=0,ct=0,k=0,gl=0,gp=-1,gv=0,rc=0"
                && written.GetValue<string>("3", "") == good + ",zz=1"
                && written.GetValue<string>("4", "").Contains("99999") && registry.Next == 9;
        });
        Test("grown-negev-capacity-accepted", () => {
            var d = new ValuesDictionary();
            d.SetValue("Schema", ScGunRegistry.Schema); d.SetValue("Next", 2);
            var records = new ValuesDictionary();
            int negev = Variant("negev");
            records.SetValue("1", $"v={negev},r=225,s=0,d=10,m=6000,n=0,c=-1,p=0,ct=1,k=1000,gl=10,gp=-1,gv=1,rc=0,ov=0");
            d.SetValue("Records", records);
            var registry = ScGunRegistry.Load(d, 0);
            return registry.Count == 1 && registry.QuarantinedCount == 0 && Snap(registry, 1).Rounds == 225;
        });
        Test("unknown-growth-mode-refused", () => {
            var d = new ValuesDictionary();
            d.SetValue("Schema", ScGunRegistry.Schema); d.SetValue("GrowthMode", "SomethingElse");
            var registry = ScGunRegistry.Load(d, 0);
            return registry.UnknownSchema && registry.Disabled;
        });
        Test("growth-mode-survives-two-rounds", () => {
            var registry = Fresh(); registry.GrowthMode = ScGunGrowthMode.CountOnly;
            var once = ScGunRegistry.Load(registry.Save(0), 0);
            var twice = ScGunRegistry.Load(once.Save(0), 0);
            return once.GrowthMode == ScGunGrowthMode.CountOnly && twice.GrowthMode == ScGunGrowthMode.CountOnly;
        });
        // --- kill queue -------------------------------------------------------------------------------------
        Test("kill-queue-round-trip", () => {
            var registry = Fresh();
            var (_, id) = Gun(registry, "ak47");
            long a = registry.Kills.Enqueue(id, Variant("ak47")), b = registry.Kills.Enqueue(id, Variant("ak47"));
            var reloaded = ScGunRegistry.Load(registry.Save(0), 0);
            return a == 1 && b == 2 && registry.Kills.Count == 2 && reloaded.Kills.Count == 2
                && reloaded.Kills.Pending[0].EventId == 1 && reloaded.Kills.Pending[1].RecordId == id;
        });
        Test("kill-queue-drops-unknown-records", () => {
            var registry = Fresh();
            var (_, id) = Gun(registry, "ak47");
            registry.Kills.Enqueue(id, Variant("ak47"));
            var saved = registry.Save(0);
            saved.GetValue<ValuesDictionary>("Records", null).SetValue(id.ToString(), "corrupt");
            var reloaded = ScGunRegistry.Load(saved, 0);
            return reloaded.Kills.Count == 0 && reloaded.QuarantinedCount == 1;
        });
        Test("kill-queue-rejects-corrupt", () => {
            var d = new ValuesDictionary();
            d.SetValue("Schema", ScGunRegistry.Schema);
            var kills = new ValuesDictionary(); kills.SetValue("Next", "3");
            var entries = new ValuesDictionary(); entries.SetValue("1", "not,a,credential"); kills.SetValue("Entries", entries);
            d.SetValue("PendingKills", kills);
            var registry = ScGunRegistry.Load(d, 0);
            return registry.UnknownSchema && registry.Disabled;
        });
        // --- counting and levelling -------------------------------------------------------------------------
        Test("counted-kill-marks-pending-level", () => {
            var registry = Fresh();
            var (inventory, id) = Gun(registry, "ak47", kills: 99);
            registry.Kills.Enqueue(id, Variant("ak47"));
            var holders = new List<ScGunHolders.Holder> { new(id, "test", inventory, 0) };
            int written = ScGunGrowthService.DrainKills(registry, holders, grow: true);
            var s = Snap(registry, id);
            return written == 1 && registry.Kills.Count == 0 && s.KillCount == 100 && s.PendingGrowthLevel == 1 && s.AppliedGrowthLevel == 0;
        });
        Test("count-only-world-never-marks-a-level", () => {
            var registry = Fresh(); registry.GrowthMode = ScGunGrowthMode.CountOnly;
            var (inventory, id) = Gun(registry, "ak47", kills: 99);
            registry.Kills.Enqueue(id, Variant("ak47"));
            ScGunGrowthService.DrainKills(registry, [new(id, "test", inventory, 0)], grow: false);
            var s = Snap(registry, id);
            return s.KillCount == 100 && s.PendingGrowthLevel == ScGunGrowth.NoPending && s.AppliedGrowthLevel == 0;
        });
        Test("credit-waits-for-an-action-to-finish", () => {
            var registry = Fresh();
            var (inventory, id) = Gun(registry, "ak47");
            registry.Kills.Enqueue(id, Variant("ak47"));
            var holders = new List<ScGunHolders.Holder> { new(id, "test", inventory, 0) };
            // A write bumps the inventory revision, which an in-progress reload reads as "the gun moved".
            int held = ScGunGrowthService.DrainKills(registry, holders, grow: true, busy: _ => true);
            int later = ScGunGrowthService.DrainKills(registry, holders, grow: true, busy: _ => false);
            return held == 0 && registry.Kills.Count == 0 && later == 1 && Snap(registry, id).KillCount == 1;
        });
        Test("a-reload-in-progress-survives-a-kill-credit", () => {
            var registry = Fresh();
            var (inventory, id) = Gun(registry, "ak47", rounds: 5);
            inventory.AddSlotItems(1, Ammo, 2);
            var reload = new ScReloadTransaction(inventory, 0, inventory.GetSlotValue(0), Ammo, 1, 30, "test");
            if (!reload.Discard()) return false;
            registry.Kills.Enqueue(id, Variant("ak47"));
            var holders = new List<ScGunHolders.Holder> { new(id, "test", inventory, 0) };
            ScGunGrowthService.DrainKills(registry, holders, grow: true, busy: _ => true);
            bool inserted = reload.InsertMagazine();
            ScGunGrowthService.DrainKills(registry, holders, grow: true, busy: _ => false);
            var s = Snap(registry, id);
            return inserted && s.Rounds == 30 && s.KillCount == 1 && inventory.Counts[1] == 1;
        });
        Test("credit-waits-for-a-reachable-gun", () => {
            var registry = Fresh();
            var (_, id) = Gun(registry, "ak47");
            registry.Kills.Enqueue(id, Variant("ak47"));
            // A dropped gun has no inventory; a shared record has two holders. Neither may be written yet.
            int none = ScGunGrowthService.DrainKills(registry, [new(id, "pickable", null, -1)], grow: true);
            int shared = ScGunGrowthService.DrainKills(registry, [new(id, "a", new Inventory(), 0), new(id, "b", new Inventory(), 0)], grow: true);
            return none == 0 && shared == 0 && registry.Kills.Count == 1 && Snap(registry, id).KillCount == 0;
        });
        Test("apply-level-keeps-ammo-scales-life", () => {
            var registry = Fresh();
            var (inventory, id) = Gun(registry, "ak47", rounds: 7, durability: 750, kills: 1000);
            Apply(registry, id, r => r.PendingGrowthLevel = 10);
            var result = ScGunGrowthService.ApplyPending(inventory, 0, "test", 0, out int from, out int to);
            var s = Snap(registry, id);
            return result == ScGunResult.Success && from == 0 && to == 10
                && s.Rounds == 7 && s.Durability == 1125 && s.MaxDurability == 2250
                && s.AppliedGrowthLevel == 10 && s.PendingGrowthLevel == ScGunGrowth.NoPending
                && s.GrowthRulesVersion == ScGunGrowth.RulesVersion && s.ReserveOverflowRounds == 0;
        });
        Test("apply-level-is-once-only", () => {
            var registry = Fresh();
            var (inventory, id) = Gun(registry, "ak47", durability: 750, kills: 1000);
            Apply(registry, id, r => r.PendingGrowthLevel = 10);
            ScGunGrowthService.ApplyPending(inventory, 0, "test", 0, out _, out _);
            int life = Snap(registry, id).Durability;
            var again = ScGunGrowthService.ApplyPending(inventory, 0, "test", 0, out _, out _);
            return again != ScGunResult.Success && Snap(registry, id).Durability == life && Snap(registry, id).MaxDurability == 2250;
        });
        Test("broken-gun-never-revives-on-levelling", () => {
            var registry = Fresh();
            var (inventory, id) = Gun(registry, "awp", rounds: 0, durability: 0, kills: 1000);
            Apply(registry, id, r => r.PendingGrowthLevel = 10);
            ScGunGrowthService.ApplyPending(inventory, 0, "test", 0, out _, out _);
            var s = Snap(registry, id);
            return s.Durability == 0 && s.MaxDurability == 300;
        });
        Test("charge-in-progress-keeps-its-fraction", () => {
            var registry = Fresh();
            var (inventory, id) = Gun(registry, "taser", rounds: 0, kills: 1000);
            Apply(registry, id, r => { r.RechargeReadyAt = 105; r.RechargeCycleSeconds = 10; r.PendingGrowthLevel = 10; });
            ScGunGrowthService.ApplyPending(inventory, 0, "test", 100, out _, out _);
            var s = Snap(registry, id);
            return Math.Abs(s.RechargeReadyAt - 102.5) < .001 && Math.Abs(s.RechargeCycleSeconds - 5f) < .001f && s.Rounds == 0;
        });
        Test("capacity-shrink-keeps-live-rounds", () => {
            var registry = Fresh();
            var (inventory, id) = Gun(registry, "ak47", rounds: 45, level: 10, kills: 1000);
            // A rule change that lowers the level must move the surplus, never delete it.
            Apply(registry, id, r => { r.AppliedGrowthLevel = 10; r.PendingGrowthLevel = ScGunGrowth.NoPending; });
            var mutation = ScGunMutation.Prepare(inventory, 0, "test", out _);
            var result = mutation.Commit(r => {
                int capacity = ScGunGrowth.Capacity(r.Variant, 0);
                r.ReserveOverflowRounds += Math.Max(0, r.Rounds - capacity);
                r.Rounds = Math.Min(r.Rounds, capacity);
                r.AppliedGrowthLevel = 0; r.MaxDurability = ScGunGrowth.MaxDurability(r.Variant, 0);
                r.Durability = Math.Min(r.Durability, r.MaxDurability);
            });
            var s = Snap(registry, id);
            return result == ScGunResult.Success && s.Rounds == 30 && s.ReserveOverflowRounds == 15 && s.Rounds + s.ReserveOverflowRounds == 45;
        });
        Test("reserve-fills-a-reload-without-charging", () => {
            var registry = Fresh();
            var (inventory, id) = Gun(registry, "ak47", rounds: 25);
            Apply(registry, id, r => r.ReserveOverflowRounds = 8);
            inventory.AddSlotItems(1, Ammo, 2);
            var reload = new ScReloadTransaction(inventory, 0, inventory.GetSlotValue(0), Ammo, 1, 30, "test");
            bool done = reload.Discard() && reload.InsertMagazine();
            var s = Snap(registry, id);
            return done && s.Rounds == 30 && s.ReserveOverflowRounds == 3 && inventory.Counts[1] == 2;
        });
        Test("reserve-absorbed-when-a-magazine-is-still-needed", () => {
            var registry = Fresh();
            var (inventory, id) = Gun(registry, "ak47", rounds: 5);
            Apply(registry, id, r => r.ReserveOverflowRounds = 3);
            inventory.AddSlotItems(1, Ammo, 2);
            var reload = new ScReloadTransaction(inventory, 0, inventory.GetSlotValue(0), Ammo, 1, 30, "test");
            bool done = reload.Discard() && reload.InsertMagazine();
            var s = Snap(registry, id);
            return done && s.Rounds == 30 && s.ReserveOverflowRounds == 0 && inventory.Counts[1] == 1;
        });
        Test("mag7-magazine-cost-follows-capacity", () => {
            var mag7 = GunSpec.ForAsset("mag7");
            return ScReloadTransaction.RequiredFor(mag7, 5) == 5 && ScReloadTransaction.RequiredFor(mag7, 7) == 7
                && ScReloadTransaction.RequiredFor(GunSpec.ForAsset("negev"), 225) == 5
                && ScReloadTransaction.RequiredFor(GunSpec.ForAsset("ak47"), 45) == 1;
        });
        // --- installing the counter -------------------------------------------------------------------------
        Test("install-preserves-everything", () => {
            var registry = Fresh();
            var (inventory, id) = Gun(registry, "m4a1s", rounds: 6, durability: 800, counter: false);
            Apply(registry, id, r => { r.SilencerOff = true; r.SkinId = 984; });
            inventory.AddSlotItems(1, Blank, 3); inventory.AddSlotItems(2, Mechanism, 3);
            var quote = ScGunCounter.Prepare(inventory, 0, free: true);
            var result = ScGunCounter.Apply(inventory, quote, "test");
            var s = Snap(registry, id);
            return result == ScGunResult.Success && s.Id == id && s.Rounds == 6 && s.Durability == 800 && s.SilencerOff
                && s.SkinId == 984 && s.CounterInstalled && s.KillCount == 0 && s.AppliedGrowthLevel == 0
                && s.GrowthRulesVersion == ScGunGrowth.RulesVersion;
        });
        Test("install-refuses-a-repeat", () => {
            var registry = Fresh();
            var (inventory, _) = Gun(registry, "ak47");
            return ScGunCounter.Prepare(inventory, 0, free: true) is null;
        });
        Test("install-costs-are-charged-once", () => {
            var registry = Fresh();
            var (inventory, _) = Gun(registry, "ak47", counter: false);
            int slot = 1;
            foreach (var (item, count) in ScGunGrowth.InstallCost()) inventory.AddSlotItems(slot++, item, count + 2);
            var quote = ScGunCounter.Prepare(inventory, 0, free: false);
            var result = ScGunCounter.Apply(inventory, quote, "test");
            for (int i = 1; i < slot; i++) if (inventory.Counts[i] != 2) return false;
            return result == ScGunResult.Success && quote.Cost.Count == 4;
        });
        Test("install-shortfall-charges-nothing", () => {
            var registry = Fresh();
            var (inventory, id) = Gun(registry, "ak47", counter: false);
            var quote = ScGunCounter.Prepare(inventory, 0, free: false);
            var result = ScGunCounter.Apply(inventory, quote, "test");
            return result == ScGunResult.InsufficientMaterials && !Snap(registry, id).CounterInstalled;
        });
        Test("a-duplicate-does-not-inherit-kills-or-level", () => {
            var registry = Fresh();
            var (inventory, id) = Gun(registry, "ak47", rounds: 45, durability: 2250, level: 10, kills: 1000);
            Apply(registry, id, r => { r.SkinId = 180; r.SilencerOff = false; });
            int copyId = registry.Clone(id);
            var original = Snap(registry, id);
            var copy = Snap(registry, copyId);
            return copyId > 0 && copyId != id
                // the original keeps everything
                && original.CounterInstalled && original.KillCount == 1000 && original.AppliedGrowthLevel == 10
                && original.Rounds == 45 && original.MaxDurability == 2250
                // the copy keeps identity and appearance but not the growth, and loses nothing it was carrying
                && !copy.CounterInstalled && copy.KillCount == 0 && copy.AppliedGrowthLevel == 0
                && copy.Variant == original.Variant && copy.SkinId == 180
                && copy.MaxDurability == 1500 && copy.Durability == 1500
                && copy.Rounds == 30 && copy.ReserveOverflowRounds == 15
                && copy.Rounds + copy.ReserveOverflowRounds == original.Rounds;
        });
        Test("a-split-duplicate-loses-only-the-growth", () => {
            var registry = Fresh();
            var (first, id) = Gun(registry, "awp", rounds: 7, durability: 300, level: 10, kills: 1200);
            var second = new Inventory();
            second.AddSlotItems(0, first.GetSlotValue(0), 1);           // the same record in two places: a duplicated item
            var saved = ScGunMutation.HolderLocator;
            try {
                ScGunMutation.HolderLocator = (record, except) => record == id && except != "second" ? ["second"] : [];
                var mutation = ScGunMutation.Prepare(first, 0, "first", out _);
                if (mutation is null || mutation.Commit(_ => { }) != ScGunResult.Success) return false;
                var acting = Snap(registry, mutation.Id);
                var kept = Snap(registry, id);
                return mutation.Id != id && !acting.CounterInstalled && acting.KillCount == 0
                    && acting.Rounds == 5 && acting.ReserveOverflowRounds == 2 && acting.MaxDurability == 200
                    && kept.CounterInstalled && kept.KillCount == 1200 && kept.Rounds == 7;
            } finally { ScGunMutation.HolderLocator = saved; }
        });
        // --- kill rules -------------------------------------------------------------------------------------
        Test("creative-and-missing-shooter-carry-no-credential", () =>
            ScGunKillCredit.For(0, creative: true, 1) is null && ScGunKillCredit.For(GunSpec.WithId(0, GunSpec.FreshFull), false, 1) is null);
        Test("credential-freezes-the-record", () => {
            var registry = Fresh();
            var (inventory, id) = Gun(registry, "ak47");
            var credit = ScGunKillCredit.For(Terrain.ExtractData(inventory.GetSlotValue(0)), false, 42);
            return credit is not null && credit.RecordId == id && credit.Variant == Variant("ak47") && credit.ShotId == 42;
        });
        // --- Zeus -------------------------------------------------------------------------------------------
        Test("zeus-recipe-expands-as-specified", () => {
            var zeus = ScWeaponCrafting.All.First(e => !e.Knife && e.Name == "taser");
            // Blanks are 4 iron + 1 coal; a mechanism is 1 blank + 2 copper + 1 germanium; a grip is 2 leather + 1 plank.
            int blanks = zeus.B + zeus.M;
            int iron = blanks * 4, coal = blanks, copper = zeus.M * 2, germanium = zeus.M + zeus.Germanium;
            return zeus.Level == 6 && zeus.B == 6 && zeus.M == 6 && zeus.H == 1 && zeus.O == 0 && zeus.Diamond == 2 && zeus.Germanium == 4
                && iron == 48 && coal == 12 && copper == 12 && germanium == 10 && zeus.H * 2 == 2 && zeus.H == 1;
        });
        Test("zeus-full-repair-stays-one-blank-one-mechanism", () => {
            var zeus = ScWeaponCrafting.All.First(e => !e.Knife && e.Name == "taser");
            var cost = ScWeaponRepair.FullCost(zeus);
            var ak = ScWeaponRepair.FullCost(ScWeaponCrafting.All.First(e => !e.Knife && e.Name == "ak47"));
            return cost.Count == 2 && cost[ScWeaponRepair.Blank] == 1 && cost[ScWeaponRepair.Mechanism] == 1
                && ak[ScWeaponRepair.Blank] == 1;
        });
        Test("zeus-repair-price-ignores-the-new-recipe", () => {
            var zeus = ScWeaponCrafting.All.First(e => !e.Knife && e.Name == "taser");
            var half = ScWeaponRepair.Cost(zeus, 50, 100);
            return half[ScWeaponRepair.Blank] == 1 && half[ScWeaponRepair.Mechanism] == 1;
        });
        // --- StatTrak placement -----------------------------------------------------------------------------
        Test("official-attachment-for-every-gun", () => {
            foreach (var spec in GunSpec.All) {
                var hd = ScGunStatTrak.For(spec.Name, false);
                if (hd is null || string.IsNullOrEmpty(hd.Bone)) return false;
                var m = hd.Matrix;
                if (!float.IsFinite(m.M41 + m.M42 + m.M43) || !float.IsFinite(m.M11)) return false;
            }
            // The Zeus has no legacy body in CS2; asking for one falls back to its current attachment, never to another gun's.
            return ScGunStatTrak.For("taser", true) == ScGunStatTrak.For("taser", false)
                && ScGunStatTrak.For("ak47", true) != ScGunStatTrak.For("ak47", false)
                && ScGunStatTrak.For("nothing", false) is null;
        });
        Test("counter-panel-saturates-without-truncating-the-record", () =>
            ScGunStatTrak.PanelText(0) == "000000" && ScGunStatTrak.PanelText(1234) == "001234"
            && ScGunStatTrak.PanelText(1_000_000) == "999999" && ScGunGrowth.LevelFor(long.MaxValue) == 10);
        // --- attribute page ---------------------------------------------------------------------------------
        Test("attribute-card-has-no-durability-or-materials", () => {
            foreach (var spec in GunSpec.All) {
                var rows = ScGunAttributes.Rows(spec, Template(Array.IndexOf(GunSpec.All, spec)), 0);
                if (rows.Count != 8) return false;
                foreach (var row in rows) {
                    string text = row.Label + row.Text + row.Unit + (row.Detail ?? "");
                    if (text.Contains("耐久") || text.Contains("坯件") || text.Contains("机构") || text.Contains("材料")
                        || text.Contains("稀有") || text.Contains("重量") || text.Contains("穿甲") || text.Contains("配件")) return false;
                }
            }
            return true;
        });
        Test("attribute-rows-match-combat", () => {
            var registry = Fresh();
            var (inventory, _) = Gun(registry, "ak47", level: 10, kills: 1000);
            int value = inventory.GetSlotValue(0);
            var spec = GunSpec.ForAsset("ak47");
            var rows = ScGunAttributes.Rows(spec, value, 10);
            var stats = EffectiveGunStats.Resolve(spec, value, false);
            var damage = rows.First(r => r.Kind == ScGunAttributes.Kind.Damage);
            var range = rows.First(r => r.Kind == ScGunAttributes.Kind.Range);
            var capacity = rows.First(r => r.Kind == ScGunAttributes.Kind.Capacity);
            var spread = rows.First(r => r.Kind == ScGunAttributes.Kind.Spread);
            return damage.Text == "22.5" && capacity.Text == stats.Capacity.ToString() && capacity.Text == "45"
                && range.Unlimited && range.Text == "无限*" && range.Fraction == 1f && spread.Text == "0";
        });
        Test("zeus-card-shows-charge-not-reload", () => {
            var zeus = GunSpec.ForAsset("taser");
            var rows = ScGunAttributes.Rows(zeus, Template(Variant("taser")), 10);
            var charge = rows.First(r => r.Kind == ScGunAttributes.Kind.ReloadOrCharge);
            var damage = rows.First(r => r.Kind == ScGunAttributes.Kind.Damage);
            var capacity = rows.First(r => r.Kind == ScGunAttributes.Kind.Capacity);
            return charge.Label.Contains("充能") && charge.LowerIsBetter && charge.Text == "5"
                && damage.Text == "225" && capacity.Text == "1";
        });
        Test("bar-scales-are-fixed-not-per-selection", () => {
            var r = ScGunAttributes.Ranges;
            foreach (var spec in GunSpec.All) for (int level = 0; level <= ScGunGrowth.MaxLevel; level++)
                foreach (var row in ScGunAttributes.Rows(spec, Template(Array.IndexOf(GunSpec.All, spec)), level))
                    if (!float.IsFinite(row.Fraction) || row.Fraction < 0 || row.Fraction > 1) return false;
            return r.Damage > 0 && r.Range > 0 && r.Capacity >= 225;
        });
        // --- interface settings -----------------------------------------------------------------------------
        Test("default-layout-matches-the-original-positions", () => {
            var area = new Vector2(850, 850 * 9f / 16f);
            var reload = ScGunFunctions.Default(ScGunFunctions.Reload, false);
            var corner = ScWeaponTouchPanel.CornerOf(reload, area);
            var size = ScWeaponTouchPanel.SizeOf(reload);
            // The original was 104x60, 160 in from the right edge, 150 up from the bottom.
            return Math.Abs(size.X - 104) < .01f && Math.Abs(size.Y - 60) < .01f
                && Math.Abs(area.X - (corner.X + size.X) - 160) < .5f && Math.Abs(area.Y - (corner.Y + size.Y) - 150) < .5f;
        });
        Test("button-geometry-is-invertible-and-on-screen", () => {
            var area = new Vector2(1280, 720);
            foreach (string id in ScGunFunctions.All) foreach (float scale in new[] { .5f, 1f, 2f }) {
                var layout = ScGunFunctions.Default(id, false); layout.Scale = scale; layout.Normalize();
                var size = ScWeaponTouchPanel.SizeOf(layout);
                if (size.X < ScWeaponTouchPanel.MinTouch || size.Y < ScWeaponTouchPanel.MinTouch) return false;
                var corner = ScWeaponTouchPanel.CornerOf(layout, area);
                if (corner.X < 0 || corner.Y < 0 || corner.X + size.X > area.X + .01f || corner.Y + size.Y > area.Y + .01f) return false;
                var centre = ScWeaponTouchPanel.CentreOf(corner, layout, area);
                if (Math.Abs(centre.X - layout.X) > .002f || Math.Abs(centre.Y - layout.Y) > .002f) return false;
            }
            return true;
        });
        Test("dragging-off-screen-is-clamped-back", () => {
            var area = new Vector2(1280, 720);
            var layout = ScGunFunctions.Default(ScGunFunctions.Inspect, false);
            var centre = ScWeaponTouchPanel.CentreOf(new Vector2(-9000, 9000), layout, area);
            var corner = ScWeaponTouchPanel.CornerOf(new ScButtonLayout { X = centre.X, Y = centre.Y }, area);
            return centre.X >= 0 && centre.X <= 1 && centre.Y >= 0 && centre.Y <= 1 && corner.X >= 0 && corner.Y >= 0;
        });
        Test("hands-keep-separate-layouts", () => {
            ScUiSettings.ResetAll();
            var right = ScUiSettings.CopyHand(false);
            right[ScGunFunctions.Reload].X = .1f;
            ScUiSettings.ReplaceHand(false, right);
            return Math.Abs(ScUiSettings.Hand(false)[ScGunFunctions.Reload].X - .1f) < .001f
                && Math.Abs(ScUiSettings.Hand(true)[ScGunFunctions.Reload].X - ScGunFunctions.Default(ScGunFunctions.Reload, true).X) < .001f;
        });
        Test("crosshair-colour-round-trips", () => {
            var colour = new Color(90, 220, 255);
            return ScUiSettings.TryParseColor(ScUiSettings.ColorText(colour), out var back) && back == colour
                && !ScUiSettings.TryParseColor("1,2", out _) && !ScUiSettings.TryParseColor("a,b,c", out _);
        });
        Test("secondary-button-only-for-real-modes", () =>
            SubsystemScKnifeBlockBehavior.SecondaryOf(GunSpec.ForAsset("awp")) == ScGunFunctions.Scope
            && SubsystemScKnifeBlockBehavior.SecondaryOf(GunSpec.ForAsset("m4a1s")) == ScGunFunctions.Silencer
            && SubsystemScKnifeBlockBehavior.SecondaryOf(GunSpec.ForAsset("glock18")) == ScGunFunctions.Burst
            && SubsystemScKnifeBlockBehavior.SecondaryOf(GunSpec.ForAsset("revolver")) == ScGunFunctions.RevolverAlt
            && SubsystemScKnifeBlockBehavior.SecondaryOf(GunSpec.ForAsset("mp5sd")) is null
            && SubsystemScKnifeBlockBehavior.SecondaryOf(GunSpec.ForAsset("nova")) is null);
        // --- loaded-world range -----------------------------------------------------------------------------
        Test("loaded-limit-is-finite-and-bounded", () => {
            float open = ScGunRange.LoadedLimit(null, Vector3.Zero, Vector3.UnitX, 512);
            float zero = ScGunRange.LoadedLimit(null, Vector3.Zero, Vector3.Zero, 512);
            return Math.Abs(open - 512) < .001f && zero == 0
                && ScGunGrowth.LoadedWorldRange > 0 && float.IsFinite(ScGunGrowth.LoadedWorldRange);
        });
    }
}
