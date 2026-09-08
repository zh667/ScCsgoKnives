using System.Reflection;
using GameEntitySystem;
using System.Xml.Linq;
using TemplatesDatabase;
namespace Game;

/// <summary>Format guard tests use the same ValuesDictionary/XML boundary as the engine hooks.</summary>
public static class ScGunSaveGuardSelfTest {
    internal sealed class InventoryForGuard : IInventory {
        public int[] Values = new int[4], Counts = new int[4]; public Project Project => null; public int SlotsCount => 4; public int VisibleSlotsCount { get; set; } = 4; public int ActiveSlotIndex { get; set; }
        public Action OnAdd;
        public int GetSlotValue(int i) => Values[i]; public int GetSlotCount(int i) => Counts[i]; public int GetSlotCapacity(int i, int v) => i == 0 ? 1 : 40; public int GetSlotProcessCapacity(int i, int v) => 0;
        public void Add(int value) { Values[0] = value; Counts[0] = 1; }
        public void AddSlotItems(int i, int v, int c) { if (Counts[i] > 0 && Values[i] != v) throw new InvalidOperationException(); Values[i] = v; Counts[i] += c; OnAdd?.Invoke(); }
        public int RemoveSlotItems(int i, int c) { int n = Math.Min(c, Counts[i]); Counts[i] -= n; return n; }
        public void ProcessSlotItems(int i, int v, int c, int p, out int result, out int resultCount) { result = v; resultCount = 0; }
        public void DropAllItems(Engine.Vector3 p) { Array.Clear(Counts); }
    }
    static XElement V(string name, string type, object value) => new("Value", new XAttribute("Name", name), new XAttribute("Type", type), new XAttribute("Value", Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)));
    static XElement World(int stamp, int schema) => new("Project", new XElement("Subsystems", new XElement("Values", new XAttribute("Name", "ScGunBlockBehavior"), V("GunDataLayout", "int", stamp), new XElement("Values", new XAttribute("Name", "GunRegistry"), V("Schema", "int", schema)))));
    static ValuesDictionary Dict(int stamp, int schema) {
        var d = new ValuesDictionary(); d.SetValue("GunDataLayout", stamp);
        var r = new ValuesDictionary(); r.SetValue("Schema", schema); d.SetValue("GunRegistry", r); return d;
    }
    public static void Run(Action<string, bool, string> check) {
        void T(string n, Func<bool> f) {
            var saved = ScGunRegistry.Current; var locator = ScGunMutation.HolderLocator;
            try { ScGunMutation.HolderLocator = null; check("gun-format/" + n, f(), n); }
            catch (Exception e) { check("gun-format/" + n, false, e.ToString()); }
            finally { ScGunRegistry.Current = saved; ScGunMutation.HolderLocator = locator; }
        }
        T("unknown-layout-refused-and-source-unchanged", () => {
            var w = World(6, ScGunRegistry.Schema); var before = w.ToString();
            bool ok = !ScGunSaveGuard.BeforeLoad(w) && w.ToString() != before; // only an explicit error marker is added
            var group = w.Descendants("Values").Single(e => (string)e.Attribute("Name") == "ScGunBlockBehavior");
            var d = new ValuesDictionary(); d.ApplyOverrides(new XElement(group));
            try { ScGunSaveGuard.Validate(d); return false; }
            catch (InvalidOperationException) {
                group.Elements("Value").Where(v => (string)v.Attribute("Name") == ScGunSaveGuard.ErrorKey).Remove();
                return ok && w.ToString() == before;
            }
        });
        T("unknown-schema-refused", () => {
            var w = World(GunSpec.DataLayout, 99); return !ScGunSaveGuard.BeforeLoad(w);
        });
        T("supported-layouts-accepted", () => {
            ScGunSaveGuard.Validate(Dict(GunSpec.DataLayout, ScGunRegistry.Schema));
            ScGunSaveGuard.Validate(Dict(ScGunRegistry.LegacyStamp, ScGunRegistry.SchemaWithoutSkins));
            ScGunSaveGuard.Validate(new ValuesDictionary());
            if (!ScGunSaveGuard.BeforeLoad(ScGun0282MigrationSelfTest.Fixture())) return false;
            return true;
        });
        foreach (var format in new[] { (6, 2), (5, 99), (4, 99), (0, 2), (3, 2) }) {
            int layout = format.Item1, schema = format.Item2;
            T($"subsystem-rejects-load-save-{layout}-{schema}", () => {
                var input = Dict(layout, schema); var output = Dict(layout, schema);
                string original = Xml(output);
                var subsystem = new SubsystemScGunBlockBehavior();
                try { subsystem.Load(input); return false; } catch (InvalidOperationException) { }
                // Exercise the actual override, with no Project: guard must precede base.Save/output writes.
                try { subsystem.Save(output); return false; } catch (InvalidOperationException) { }
                return Xml(input) == original && Xml(output) == original;
            });
        }
        T("save-defense-after-unsupported-registry", () => {
            var subsystem = new SubsystemScGunBlockBehavior();
            Set(subsystem, "m_saveReady", true); Set(subsystem, "m_worldLayout", 5);
            Set(subsystem, "m_registry", ScGunRegistry.Load(Dict(5, 99).GetValue<ValuesDictionary>("GunRegistry"), 0));
            var output = Dict(5, 99); string before = Xml(output);
            try { subsystem.Save(output); return false; } catch (InvalidOperationException) { return Xml(output) == before; }
        });
        T("save-defense-unknown-layout-even-if-ready", () => {
            var subsystem = new SubsystemScGunBlockBehavior();
            Set(subsystem, "m_saveReady", true); Set(subsystem, "m_worldLayout", 6); Set(subsystem, "m_registry", new ScGunRegistry());
            var output = Dict(6, 2); string before = Xml(output);
            try { subsystem.Save(output); return false; } catch (InvalidOperationException) { return Xml(output) == before; }
        });
        T("legacy-two-real-subsystem-saves", () => {
            var values = Dict(4, 1);
            for (int i = 0; i < 2; i++) {
                ScGunSaveGuard.Validate(values);
                var registry = ScGunRegistry.Load(values.GetValue<ValuesDictionary>("GunRegistry"), 0);
                registry.LegacyWorld = ScGunRegistry.Classify(values.GetValue<int>("GunDataLayout"), true, true) == ScGunRegistry.WorldStatus.Legacy;
                var subsystem = new SubsystemScGunBlockBehavior();
                Set(subsystem, "m_saveReady", true); Set(subsystem, "m_worldLayout", 4);
                Set(subsystem, "m_registry", registry); Set(subsystem, "m_time", new SubsystemTime());
                var saved = new ValuesDictionary(); subsystem.Save(saved);
                values = new ValuesDictionary(); values.ApplyOverrides(XElement.Parse(Xml(saved)));
                if (values.GetValue<int>("GunDataLayout") != 4 || !registry.LegacyWorld) return false;
            }
            return true;
        });
        T("schema1-two-xml-subsystem-saves-preserve-state", () => {
            var values = Dict(5, 1);
            var records = new ValuesDictionary(); records.SetValue("1", "0,4,1,400,1500,7,8.5");
            var table = values.GetValue<ValuesDictionary>("GunRegistry"); table.SetValue("Records", records); table.SetValue("Next", 20);
            for (int i = 0; i < 2; i++) {
                ScGunSaveGuard.Validate(values);
                var registry = ScGunRegistry.Load(values.GetValue<ValuesDictionary>("GunRegistry"), 0);
                if (!registry.TryGetSnapshot(1, out var s) || s != new ScGunSnapshot(1, 0, 4, true, 400, 1500, 7, 8.5, 0) || registry.Next != 20) return false;
                var subsystem = new SubsystemScGunBlockBehavior();
                Set(subsystem, "m_saveReady", true); Set(subsystem, "m_worldLayout", 5);
                Set(subsystem, "m_registry", registry); Set(subsystem, "m_time", new SubsystemTime());
                var saved = new ValuesDictionary(); subsystem.Save(saved);
                values = new ValuesDictionary(); values.ApplyOverrides(XElement.Parse(Xml(saved)));
                if (values.GetValue<int>("GunDataLayout") != 5 || values.GetValue<ValuesDictionary>("GunRegistry").GetValue<int>("Schema") != ScGunRegistry.Schema) return false;
            }
            return true;
        });
        // Two real XML save/reload rounds through the subsystem, carrying the counter and growth state a 0.40.0
        // world holds. Decoding a string is not proof of persistence; this goes out through Save and back in
        // through the same ValuesDictionary/XML boundary the engine uses, twice.
        T("schema2-to-3-two-rounds-preserve-counter-and-growth", () => {
            var start = Dict(5, ScGunRegistry.SchemaWithoutGrowth);
            var records = new ValuesDictionary();
            records.SetValue("1", "0,17,1,900,1500,4,12.5,180");   // schema 2: AK, 17 rounds, silencer off, 900/1500, charge 12.5, Fire Serpent
            var table = start.GetValue<ValuesDictionary>("GunRegistry");
            table.SetValue("Records", records); table.SetValue("Next", 20);
            ScGunSaveGuard.Validate(start);
            var registry = ScGunRegistry.Load(table, 0);
            if (registry.LoadedSchema != ScGunRegistry.SchemaWithoutGrowth || !registry.TryGetSnapshot(1, out var converted)) return false;
            if (converted.CounterInstalled || converted.KillCount != 0 || converted.Rounds != 17 || !converted.SilencerOff
                || converted.Durability != 900 || converted.MaxDurability != 1500 || converted.SkinId != 180
                || Math.Abs(converted.RechargeReadyAt - 12.5) > .01) return false;
            // Fit a counter, earn a level, and mark one still waiting: the state a live world would be carrying.
            registry.GrowthMode = ScGunGrowthMode.CountAndGrow;
            var record = registry.Get(1);
            record.CounterInstalled = true; record.KillCount = 250; record.AppliedGrowthLevel = 2;
            record.PendingGrowthLevel = 2; record.GrowthRulesVersion = ScGunGrowth.RulesVersion;
            record.MaxDurability = ScGunGrowth.MaxDurability(0, 2); record.Durability = 900; record.ReserveOverflowRounds = 3;
            registry.Kills.Enqueue(1, 0);
            var values = start;
            for (int round = 0; round < 2; round++) {
                var subsystem = new SubsystemScGunBlockBehavior();
                Set(subsystem, "m_saveReady", true); Set(subsystem, "m_worldLayout", 5);
                Set(subsystem, "m_registry", registry); Set(subsystem, "m_time", new SubsystemTime());
                var saved = new ValuesDictionary(); subsystem.Save(saved);
                values = new ValuesDictionary(); values.ApplyOverrides(XElement.Parse(Xml(saved)));
                ScGunSaveGuard.Validate(values);
                if (values.GetValue<int>("GunDataLayout") != 5) return false;
                var reloadedTable = values.GetValue<ValuesDictionary>("GunRegistry");
                if (reloadedTable.GetValue<int>("Schema") != ScGunRegistry.Schema) return false;
                registry = ScGunRegistry.Load(reloadedTable, 0);
                if (!registry.TryGetSnapshot(1, out var s)) return false;
                if (s.Variant != 0 || s.Rounds != 17 || !s.SilencerOff || s.Durability != 900
                    || s.MaxDurability != ScGunGrowth.MaxDurability(0, 2) || s.SkinId != 180
                    || !s.CounterInstalled || s.KillCount != 250 || s.AppliedGrowthLevel != 2
                    || s.PendingGrowthLevel != 2 || s.GrowthRulesVersion != ScGunGrowth.RulesVersion
                    || s.ReserveOverflowRounds != 3) return false;
                if (registry.GrowthMode != ScGunGrowthMode.CountAndGrow || registry.Kills.Count != 1
                    || registry.Kills.Pending[0].RecordId != 1 || registry.Next != 20) return false;
            }
            return true;
        });
        // A world about to be converted to a schema older builds cannot read is backed up first, once.
        T("schema-upgrade-asks-for-a-backup-once", () => {
            var older = World(GunSpec.DataLayout, ScGunRegistry.SchemaWithoutGrowth);
            var current = World(GunSpec.DataLayout, ScGunRegistry.Schema);
            var brandNew = new XElement("Project", new XElement("Subsystems"));
            if (ScGunSchemaUpgrade.SavedSchema(older) != ScGunRegistry.SchemaWithoutGrowth) return false;
            if (ScGunSchemaUpgrade.SavedSchema(brandNew) != 0) return false;
            if (!ScGunSchemaUpgrade.NeedsBackup(older) || ScGunSchemaUpgrade.NeedsBackup(current) || ScGunSchemaUpgrade.NeedsBackup(brandNew)) return false;
            // Once marked it never asks again, and the marker records where the backup went.
            ScGunSchemaUpgrade.Mark(older, ScGunRegistry.SchemaWithoutGrowth, "world/Backup.snapshot");
            if (ScGunSchemaUpgrade.NeedsBackup(older)) return false;
            var values = new ValuesDictionary(); values.ApplyOverrides(new XElement(older.Descendants("Values").First(e => (string)e.Attribute("Name") == "ScGunBlockBehavior")));
            var marker = values.GetValue<ValuesDictionary>(ScGunSchemaUpgrade.Marker, null);
            if (marker is null || marker.GetValue<int>("From", 0) != ScGunRegistry.SchemaWithoutGrowth
                || marker.GetValue<int>("To", 0) != ScGunRegistry.Schema || marker.GetValue<string>("Backup", "") != "world/Backup.snapshot") return false;
            // A backup failure refuses the load instead of upgrading without one.
            var refused = World(GunSpec.DataLayout, ScGunRegistry.SchemaWithoutSkins);
            ScGunSaveGuard.Refuse(refused, "backup failed");
            var refusedValues = new ValuesDictionary();
            refusedValues.ApplyOverrides(new XElement(refused.Descendants("Values").First(e => (string)e.Attribute("Name") == "ScGunBlockBehavior")));
            try { ScGunSaveGuard.Validate(refusedValues); return false; } catch (InvalidOperationException) { }
            // Names carry the source and target format, a UTC stamp and a unique suffix.
            string name = ScGunSchemaUpgrade.FileName(1, ScGunRegistry.Schema);
            return name.Contains("-1-to-3-") && name != ScGunSchemaUpgrade.FileName(1, ScGunRegistry.Schema);
        });
        T("record-restored-before-refund", () => {
            var registry = new ScGunRegistry(); ScGunRegistry.Current = registry;
            var inv = new InventoryForGuard();
            int id = registry.Allocate(0, 4, false, 1234); int value = Terrain.MakeBlockValue(512, 0, GunSpec.WithId(0, id)); inv.Add(value);
            registry.Get(id).Holder = "previous"; registry.Get(id).RechargeReadyAt = 25;
            var before = registry.Get(id).Copy(); inv.AddSlotItems(1, 900, 3);
            bool observed = false;
            inv.OnAdd = () => { observed = registry.Get(id).Snapshot(id) == before.Snapshot(id) && registry.Get(id).Holder == before.Holder; };
            var tx = ScGunMutation.Prepare(inv, 0, "guard", out _); if (tx is null) return false;
            tx.AfterRecordWrite = () => throw new InvalidOperationException("after record");
            var result = tx.Commit(r => { r.SkinId = 180; r.Durability = 1; r.Rounds = 2; r.MaxDurability = 1400; r.SilencerOff = true; r.RechargeReadyAt = 90; }, ammo: 900, cost: 1);
            return result == ScGunResult.InventoryRejected && registry.TryGetSnapshot(id, out var s)
                && s == before.Snapshot(id) && observed && inv.GetSlotCount(1) == 3
                && inv.GetSlotValue(0) == value && tx.Id == id && tx.Expected == value && !ScGunMutation.IsCommitting;
        });
        foreach (bool clone in new[] { false, true }) T(clone ? "clone-post-publish-fault" : "fresh-post-publish-fault", () => {
            var registry = new ScGunRegistry(); ScGunRegistry.Current = registry;
            int id = clone ? registry.Allocate(0, 4, true, 400) : 0;
            if (clone) ScGunMutation.HolderLocator = (_, _) => ["another-holder"];
            int original = Terrain.MakeBlockValue(512, 0, GunSpec.WithId(0, id));
            var inv = new InventoryForGuard(); inv.Add(original); inv.AddSlotItems(1, 900, 3);
            var before = clone ? registry.Get(id).Snapshot(id) : default;
            int next = registry.Next;
            var tx = ScGunMutation.Prepare(inv, 0, "guard", out _);
            bool reached = false;
            tx.AfterRecordWrite = () => { reached = registry.TryGetSnapshot(next, out var s) && s.SkinId == 180; throw new InvalidOperationException("after publish"); };
            var result = tx.Commit(r => r.SkinId = 180, ammo: 900, cost: 1);
            return reached && result == ScGunResult.InventoryRejected && registry.Next == next + 1 && registry.QuarantinedCount == 1
                && !registry.TryGetSnapshot(next, out _) && (!clone || registry.Get(id).Snapshot(id) == before)
                && inv.Values[0] == original && inv.Counts[0] == 1 && inv.Counts[1] == 3
                && tx.Expected == original && tx.Id == id && !ScGunMutation.IsCommitting;
        });
    }
    static string Xml(ValuesDictionary values) { var root = new XElement("Values"); values.Save(root); return root.ToString(SaveOptions.DisableFormatting); }
    static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
}
