using System.Reflection;
using System.Xml.Linq;
using Game;
using TemplatesDatabase;

/// <summary>gun-record-mirror-20261106 (1.5.1). A world saved without CS loses the ScGunBlockBehavior group (the engine
/// saves only subsystems it has templates for); the player log of 2026-11-02 then showed "旧枪仍在，但整个枪械记录表或布局来源
/// 缺失" on reinstall. These cases run the packaged DLL's own mirror code on detached XML: the old refusal without a copy,
/// the verbatim restore with one, and every copy that must not be used.</summary>
static class GunRecordMirrorRegression {
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod) {
        var results = new List<Result>();
        void Check(string name, bool ok, string detail = "") => results.Add(new("gun-mirror/" + name, ok, detail));
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        var mirror = mod.GetType("Game.ScGunRecordMirror", true);
        var integrity = mod.GetType("Game.ScGunLoadIntegrity", true);
        var guard = mod.GetType("Game.ScGunSaveGuard", true);
        var registryType = mod.GetType("Game.ScGunRegistry", true); var current = registryType.GetField("Current"); var previous = current.GetValue(null);
        var spec = mod.GetType("Game.GunSpec", true);
        object Call(Type type, string name, params object[] args) {
            try { return type.GetMethod(name, any).Invoke(null, args); }
            catch (TargetInvocationException e) { throw e.InnerException!; }
        }
        XElement Value(string n, object v) => new("Value", new XAttribute("Name", n), new XAttribute("Type", v is int ? "int" : "string"), new XAttribute("Value", v));
        XElement Group(string n, params object[] children) => new("Values", new XAttribute("Name", n), children);
        XElement G(XElement p, string n) => p?.Elements("Values").SingleOrDefault(e => (string)e.Attribute("Name") == n);
        XElement Gun(XElement p) => G(p.Element("Subsystems"), "ScGunBlockBehavior");
        const int block = 302;
        int layout = (int)spec.GetField("DataLayout").GetRawConstantValue();
        int schema = (int)registryType.GetField("Schema").GetRawConstantValue();
        string errorKey = (string)guard.GetField("ErrorKey").GetRawConstantValue();
        int Data(int variant, int id) => (int)spec.GetMethod("WithId").Invoke(null, [variant, id]);
        string directory = Path.Combine(Path.GetTempPath(), "sc-gun-mirror-" + Guid.NewGuid().ToString("N"));
        try {
            Directory.CreateDirectory(directory);
            current.SetValue(null, null);
            // A real record row from this build's registry, as TravelRegression does.
            var registry = Activator.CreateInstance(registryType); registryType.GetMethod("Allocate").Invoke(registry, [2, 4, false, 400, 1500, 0]);
            string row = ((ValuesDictionary)registryType.GetMethod("Save").Invoke(registry, [0d])).GetValue<ValuesDictionary>("Records").GetValue<string>("1");
            XElement World(int seed = 12345, string blockName = "ScGunBlock", params (int Variant, int Id)[] guns) {
                if (guns.Length == 0) guns = [(2, 1)];
                var slots = Group("Slots"); int slot = 0;
                foreach (var g in guns) slots.Add(Group((slot++).ToString(), Value("Contents", Terrain.MakeBlockValue(block, 0, Data(g.Variant, g.Id))), Value("Count", 1)));
                var table = Group("GunRegistry", Value("Schema", schema), Value("Next", 2), Value("GrowthMode", "CountAndGrow"), Group("Records", Value("1", row)));
                return new XElement("Project", new XElement("Subsystems",
                        Group("GameInfo", Value("WorldSeed", seed)),
                        Group("BlocksManager", Value(block.ToString(), blockName)),
                        Group("ScGunBlockBehavior", Value("GunDataLayout", layout), Value("GunTravelSource", "data:/Worlds/Test"), table)),
                    new XElement("Entities", new XElement("Entity", new XAttribute("Name", "MalePlayer"), Group("Inventory", slots))));
            }
            // What the engine leaves after a save without CS: the group is gone, items and the block name stay.
            XElement Dropped(XElement world) { var copy = new XElement(world); Gun(copy).Remove(); return copy; }
            string Copy(string name, XElement world) {
                string path = Path.Combine(directory, name); Directory.CreateDirectory(path); path = Path.Combine(path, "ScCsgoGunRecords.xml");
                Call(mirror, "Write", path, (XElement)Call(mirror, "Document", world)); return path;
            }
            bool Restore(XElement project, string path) => (bool)Call(mirror, "RestoreFrom", project, path);
            // Refused: nothing restored and the project byte-for-byte as before.
            bool Refused(XElement project, string path) { string before = project.ToString(); return !Restore(project, path) && project.ToString() == before; }

            var world = World();
            var document = (XElement)Call(mirror, "Document", world);
            Check("copy-holds-the-saved-group-verbatim", document is not null && XNode.DeepEquals(document.Elements("Values").Single(), Gun(world))
                && (string)document.Attribute("Seed") == "12345" && (int?)document.Attribute("Block") == block, document?.ToString() ?? "null");
            Check("no-copy-of-a-save-without-records", Call(mirror, "Document", Dropped(world)) is null);
            var marked = new XElement(world); Gun(marked).Add(Value(errorKey, "refused")); Check("no-copy-of-a-refused-group", Call(mirror, "Document", marked) is null);

            string path = Copy("main", world);
            var first = File.ReadAllText(path);
            Call(mirror, "Write", path, document);
            Check("second-save-keeps-previous-as-bak", File.Exists(path + ".bak") && File.ReadAllText(path + ".bak") == first);

            // The reported failure, reproduced: no copy -> nothing restored -> the integrity check refuses as in the log.
            var lost = Dropped(world);
            bool untouched = Refused(lost, Path.Combine(directory, "absent", "ScCsgoGunRecords.xml"));
            string error = null; try { Call(integrity, "Inspect", lost); } catch (InvalidOperationException e) { error = e.Message; }
            Check("without-copy-old-refusal-reproduced", untouched && error?.Contains("旧枪仍在") == true, error ?? "Inspect did not refuse");

            lost = Dropped(world);
            bool restored = Restore(lost, path);
            var inspection = Call(integrity, "Inspect", lost);
            var issues = (Array)inspection.GetType().GetProperty("Issues").GetValue(inspection);
            Check("lost-group-restored-verbatim", restored && XNode.DeepEquals(Gun(lost), Gun(world)) && (bool)Call(guard, "BeforeLoad", lost) && issues.Length == 0,
                lost.ToString());
            Check("restore-repeated-is-a-no-op", Refused(lost, path));

            var present = new XElement(world);
            G(G(Gun(present), "GunRegistry"), "Records").Element("Value").SetAttributeValue("Value", row.Replace("r=4,", "r=3,"));
            Check("present-group-is-authority", Refused(present, path));
            var refusal = Dropped(world); refusal.Element("Subsystems").Add(Group("ScGunBlockBehavior", Value(errorKey, "refused")));
            Check("refusal-marker-left-alone", Refused(refusal, path));
            Check("other-world-seed-refused", Refused(Dropped(World(seed: 999)), path));
            Check("gun-block-number-taken-refused", Refused(Dropped(World(blockName: "OtherModBlock")), path));
            Check("copy-older-than-items-refused", Refused(Dropped(World(guns: [(2, 1), (2, 2)])), path));
            Check("model-mismatch-refused", Refused(Dropped(World(guns: [(3, 1)])), path));

            File.WriteAllText(path, "<not xml");
            lost = Dropped(world);
            Check("damaged-copy-falls-back-to-bak", Restore(lost, path) && XNode.DeepEquals(Gun(lost), Gun(world)));
            string lone = Copy("lone", world); File.WriteAllText(lone, "<not xml");
            bool threw = false, refused = false;
            try { refused = Refused(Dropped(world), lone); } catch (Exception) { threw = true; }
            Check("damaged-copy-without-bak-refused-quietly", refused && !threw);
        }
        catch (Exception e) { Check("setup", false, e.ToString()); }
        finally {
            current.SetValue(null, previous);
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
        return results;
    }
}
