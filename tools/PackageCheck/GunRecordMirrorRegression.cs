using System.Reflection;
using System.Xml.Linq;
using Game;
using TemplatesDatabase;

/// <summary>gun-record-mirror-20261106 (1.5.1). A world saved without CS loses the ScGunBlockBehavior group (the engine
/// saves only subsystems it has templates for); the player log of 2026-11-02 then showed "旧枪仍在，但整个枪械记录表或布局来源
/// 缺失" on reinstall. These cases run the packaged DLL's own mirror code on detached XML: the old refusal without a copy,
/// the verbatim restore with one, every copy that must not be used, and CS block numbers taken by another mod
/// while CS was absent (the engine gives a new block the lowest number no loaded block holds).</summary>
static class GunRecordMirrorRegression {
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod) {
        var results = new List<Result>();
        void Check(string name, bool ok, string detail = "") => results.Add(new("gun-mirror/" + name, ok, detail));
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        // A core from before 1.5.1 (the pipeline's baseline run of the delivered packages) has no mirror: one named
        // failure, not a crash of the whole suite.
        var mirror = mod.GetType("Game.ScGunRecordMirror");
        if (mirror is null) { Check("present", false, "Game.ScGunRecordMirror is not in this core (before 1.5.1)"); return results; }
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
            const int knife = 303;
            XElement World(int seed = 12345, string blockName = "ScGunBlock", params (int Variant, int Id)[] guns) {
                if (guns.Length == 0) guns = [(2, 1)];
                var slots = Group("Slots"); int slot = 0;
                foreach (var g in guns) slots.Add(Group((slot++).ToString(), Value("Contents", Terrain.MakeBlockValue(block, 0, Data(g.Variant, g.Id))), Value("Count", 1)));
                slots.Add(Group((slot++).ToString(), Value("Contents", Terrain.MakeBlockValue(knife, 0, 5)), Value("Count", 1)));
                var table = Group("GunRegistry", Value("Schema", schema), Value("Next", 2), Value("GrowthMode", "CountAndGrow"), Group("Records", Value("1", row)));
                return new XElement("Project", new XElement("Subsystems",
                        Group("GameInfo", Value("WorldSeed", seed)),
                        Group("BlocksManager", Value(block.ToString(), blockName), Value(knife.ToString(), "ScKnifeBlock")),
                        Group("ScGunBlockBehavior", Value("GunDataLayout", layout), Value("GunTravelSource", "data:/Worlds/Test"), table)),
                    new XElement("Entities", new XElement("Entity", new XAttribute("Id", "1"), new XAttribute("Name", "MalePlayer"), Group("Inventory", slots))));
            }
            // What the engine leaves after a save without CS: the group is gone, items and the block name stay.
            XElement Dropped(XElement world) { var copy = new XElement(world); Gun(copy).Remove(); return copy; }
            string Copy(string name, XElement world) {
                string path = Path.Combine(directory, name); Directory.CreateDirectory(path); path = Path.Combine(path, "ScCsgoGunRecords.xml");
                Call(mirror, "Write", path, Document(world)); return path;
            }
            string[] family = ["ScGunBlock", "ScKnifeBlock"];
            XElement Document(XElement world) => (XElement)Call(mirror, "Document", world, family);
            bool Restore(XElement project, string path, Func<int, bool> reserved = null) => (bool)Call(mirror, "RestoreFrom", project, path, reserved ?? (_ => false));
            // Refused: nothing restored and the project byte-for-byte as before.
            bool Refused(XElement project, string path) { string before = project.ToString(); return !Restore(project, path) && project.ToString() == before; }

            var world = World();
            var document = Document(world);
            Check("copy-holds-the-saved-group-verbatim", document is not null && XNode.DeepEquals(document.Elements("Values").Single(), Gun(world))
                && (string)document.Attribute("Seed") == "12345" && (int?)document.Attribute("Block") == block, document?.ToString() ?? "null");
            Check("no-copy-of-a-save-without-records", Document(Dropped(world)) is null);
            var marked = new XElement(world); Gun(marked).Add(Value(errorKey, "refused")); Check("no-copy-of-a-refused-group", Document(marked) is null);

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
            // The gun block still has a name in this world but under another number than the copy says: not a number
            // taken while CS was absent, nothing to infer from.
            var renumbered = Dropped(world);
            G(renumbered.Element("Subsystems"), "BlocksManager").Elements("Value").Single(e => (string)e.Attribute("Name") == block.ToString()).SetAttributeValue("Name", "310");
            Check("gun-block-number-differs-refused", Refused(renumbered, path));
            Check("copy-older-than-items-refused", Refused(Dropped(World(guns: [(2, 1), (2, 2)])), path));
            Check("model-mismatch-refused", Refused(Dropped(World(guns: [(3, 1)])), path));

            // Another block mod added while CS was absent took the gun number 302 (the lowest number without a loaded
            // block): the save keeps only its name there. Its own item sits in slot 9; the knife number 303 was not taken.
            string Contents(XElement p, string slot) => (string)p.Descendants("Values").Single(e => (string)e.Attribute("Name") == "Slots")
                .Elements("Values").Single(e => (string)e.Attribute("Name") == slot).Elements("Value").Single(e => (string)e.Attribute("Name") == "Contents").Attribute("Value");
            Dictionary<string, int> Numbers(XElement p) => G(p.Element("Subsystems"), "BlocksManager").Elements("Value")
                .GroupBy(e => (string)e.Attribute("Value")).ToDictionary(g => g.Key, g => int.Parse((string)g.Single().Attribute("Name")));
            XElement Taken(Action<XElement> during = null) {
                var p = Dropped(world);
                G(p.Element("Subsystems"), "BlocksManager").Elements("Value").Single(e => (string)e.Attribute("Name") == block.ToString()).SetAttributeValue("Value", "OtherModBlock");
                p.Descendants("Values").Single(e => (string)e.Attribute("Name") == "Slots").Add(Group("9", Value("Contents", Terrain.MakeBlockValue(block, 0, 7)), Value("Count", 3)));
                during?.Invoke(p);
                return p;
            }
            string gunValue = Terrain.MakeBlockValue(block, 0, Data(2, 1)).ToString(), knifeValue = Terrain.MakeBlockValue(knife, 0, 5).ToString();
            string other = Terrain.MakeBlockValue(block, 0, 7).ToString();
            string takenPath = Copy("taken", world);
            var taken = Taken();
            bool back = Restore(taken, takenPath);
            var numbers = Numbers(taken); int fresh = numbers.GetValueOrDefault("ScGunBlock");
            Check("taken-number-reassigned-and-items-moved", back && fresh == 300 && numbers["OtherModBlock"] == block && numbers["ScKnifeBlock"] == knife
                && Contents(taken, "0") == Terrain.MakeBlockValue(fresh, 0, Data(2, 1)).ToString() && Contents(taken, "1") == knifeValue && Contents(taken, "9") == other
                && XNode.DeepEquals(Gun(taken), Gun(world)), taken.ToString());
            var takenInspection = Call(integrity, "Inspect", taken);
            Check("reassigned-world-loads-cleanly", (bool)Call(guard, "BeforeLoad", taken)
                && ((Array)takenInspection.GetType().GetProperty("Issues").GetValue(takenInspection)).Length == 0);
            Check("reassignment-repeated-is-a-no-op", Refused(taken, takenPath));
            // The player moved the gun to slot 5 while CS was absent and put the other mod's item into its old slot:
            // neither is touched (the other mod's item may not become a gun), and the copy still restores the records.
            var shuffled = Taken(p => {
                var slots = p.Descendants("Values").Single(e => (string)e.Attribute("Name") == "Slots");
                slots.Elements("Values").Single(e => (string)e.Attribute("Name") == "0").Elements("Value").Single(e => (string)e.Attribute("Name") == "Contents").SetAttributeValue("Value", other);
                slots.Add(Group("5", Value("Contents", gunValue), Value("Count", 1)));
            });
            back = Restore(shuffled, takenPath);
            Check("moved-item-left-alone", back && Contents(shuffled, "0") == other && Contents(shuffled, "5") == gunValue && Numbers(shuffled)["ScGunBlock"] == 300, shuffled.ToString());
            var reservedWorld = Taken();
            back = Restore(reservedWorld, takenPath, n => n == 300);
            Check("static-number-not-reused", back && Numbers(reservedWorld)["ScGunBlock"] == 301 && Contents(reservedWorld, "0") == Terrain.MakeBlockValue(301, 0, Data(2, 1)).ToString());
            var freed = Dropped(world);
            G(freed.Element("Subsystems"), "BlocksManager").Elements("Value").Single(e => (string)e.Attribute("Name") == block.ToString()).Remove();
            back = Restore(freed, takenPath);
            Check("free-number-named-again", back && Numbers(freed)["ScGunBlock"] == block && Contents(freed, "0") == gunValue);
            var notTaken = Dropped(world); back = Restore(notTaken, takenPath);
            Check("untaken-numbers-unchanged", back && Numbers(notTaken).Count == 2 && Contents(notTaken, "0") == gunValue);

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
