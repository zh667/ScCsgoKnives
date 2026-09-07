using System.Xml.Linq;
using System.Globalization;
using Engine;
using TemplatesDatabase;
namespace Game;

/// <summary>Published-format migration tested through XML and the actual registry readers. Does not
/// authorize migration based on a bit pattern, or alter any real user world.</summary>
public static class ScGun0282MigrationSelfTest {
    static XElement V(string name, string type, object value) => new("Value", new XAttribute("Name", name), new XAttribute("Type", type), new XAttribute("Value", Convert.ToString(value, CultureInfo.InvariantCulture)));
    static XElement G(string name, params object[] children) => new("Values", new XAttribute("Name", name), children);
    static XElement Group(XElement parent, string name) => parent.Elements("Values").Single(e => (string)e.Attribute("Name") == name);
    static int Contents(XElement slot) => int.Parse((string)slot.Element("Value").Attribute("Value"), CultureInfo.InvariantCulture);
    static XElement Slot(int index, int value, bool creative = false, int count = 1) => G("Slot" + index, V("Contents", "int", value), creative ? null : V("Count", "int", count));
    static int Old(int v, int rounds, bool off = false) => Terrain.MakeBlockValue(512, 7, 65536 | v | (rounds << 6) | (off ? 16384 : 0));
    public static XElement Fixture() => new("Project", new XAttribute("Name", "GameProject"),
        new XElement("Subsystems", G("UsedMods", G("Mods", G("0", V("PackageName", "string", ScGun0282Migration.Package), V("Version", "string", "0.28.2")))),
            G("BlocksManager", V("512", "string", "ScGunBlock")), G("ScGunBlockBehavior", G("ZeusRechargeAt")),
            G("Pickables", G("Pickables")), G("Projectiles", G("Projectiles")), G("MovingBlocks", G("MovingBlockSets"))),
        new XElement("Entities", new XElement("Entity", new XAttribute("Name", "MalePlayer"),
            G("Inventory", G("Slots")), G("CreativeInventory", G("Slots")))));
    static XElement Slots(XElement world, string inventory = "Inventory") => Group(world.Element("Entities").Element("Entity"), inventory).Element("Values");
    static ScGunRegistry Registry(XElement world) {
        var dict = new ValuesDictionary(); dict.ApplyOverrides(Group(Group(world.Element("Subsystems"), "ScGunBlockBehavior"), "GunRegistry"));
        return ScGunRegistry.Load(dict, 0);
    }
    public static void Run(Action<string, bool, string> check) {
        var current = ScGunRegistry.Current;
        void Test(string name, Func<bool> run) {
            ScGunRegistry.Current = new();
            try { check("migration-0282/" + name, run(), name); }
            catch (Exception e) { check("migration-0282/" + name, false, e.ToString()); }
        }
        try {
            Test("35-guns-state-and-durability", () => {
                var source = Fixture(); var slots = Slots(source); var expected = new List<(int Value, int Variant, int Rounds, bool Off)>();
                for (int v = 0; v < GunSpec.All.Length; v++) foreach (int rounds in new[] { 0, 1, GunSpec.All[v].Magazine }) foreach (bool off in new[] { false, true }) {
                    int value = Old(v, rounds, off); slots.Add(Slot(expected.Count, value)); expected.Add((value, v, rounds, off));
                }
                var original = new XElement(source); var plan = ScGun0282Migration.Prepare(source);
                if (!XNode.DeepEquals(original, source) || plan.Guns != expected.Count) return false;
                ScGunRegistry.Current = Registry(plan.Document);
                int i = 0;
                foreach (var slot in Slots(plan.Document).Elements("Values")) {
                    int value = Contents(slot), data = Terrain.ExtractData(value); var e = expected[i++];
                    if (Terrain.ExtractContents(value) != 512 || Terrain.ExtractLight(value) != Terrain.ExtractLight(e.Value) || !ScGunBlock.IsKnown(value)
                        || GunSpec.GetVariant(data) != e.Variant || GunSpec.GetRounds(data) != e.Rounds || GunSpec.GetSilencerOff(data) != e.Off
                        || GunSpec.GetDurability(data) != ScGunDurability.Full(e.Variant)) return false;
                }
                return i == expected.Count;
            });
            Test("container-creative-pickable-projectile-moving", () => {
                var source = Fixture(); int awp = Old(2, 3), usp = Old(5, 2, true);
                Slots(source).Add(Slot(0, awp), Slot(1, 42, count: 8));
                Slots(source, "CreativeInventory").Add(Slot(0, awp, true));
                source.Element("Entities").Add(new XElement("Entity", new XAttribute("Name", "StashChestDiamond"), G("Chest", G("Slots", Slot(0, usp)))));
                var subs = source.Element("Subsystems");
                Group(Group(subs, "Pickables"), "Pickables").Add(G("0", V("Value", "int", awp), V("Count", "int", 1), V("Position", "Vector3", "1,2,3")));
                Group(Group(subs, "Projectiles"), "Projectiles").Add(G("0", V("Value", "int", awp)));
                Group(Group(subs, "MovingBlocks"), "MovingBlockSets").Add(G("0", V("Blocks", "string", awp + ",1,2,3;42,0,0,0;")));
                var plan = ScGun0282Migration.Prepare(source); ScGunRegistry.Current = Registry(plan.Document);
                if (plan.Guns != 6 || plan.Records != 6) return false; // equal-valued old guns receive separate records
                var a = Slots(plan.Document).Element("Values"); var b = Slots(plan.Document, "CreativeInventory").Element("Values");
                if (Contents(a) == Contents(b)) return false;
                var untouched = Slots(plan.Document).Elements("Values").Skip(1).Single();
                if (!XNode.DeepEquals(untouched, Slots(source).Elements("Values").Skip(1).Single())) return false;
                var chest = plan.Document.Element("Entities").Elements("Entity").Last().Descendants("Value").Single(e => (string)e.Attribute("Name") == "Contents");
                return GunSpec.GetSilencerOff(Terrain.ExtractData(int.Parse((string)chest.Attribute("Value"))))
                    && Group(Group(plan.Document.Element("Subsystems"), "MovingBlocks"), "MovingBlockSets").Descendants("Value").Single().Attribute("Value").Value.EndsWith(",1,2,3;42,0,0,0;");
            });
            Test("backup-before-apply-failure-untouched", () => {
                var source = Fixture(); Slots(source).Add(Slot(0, Old(2, 3))); var original = new XElement(source); bool called = false;
                try { ScGun0282Migration.Execute(source, () => { called = XNode.DeepEquals(source, original); throw new InvalidOperationException("disk full"); }); return false; }
                catch (InvalidOperationException) { return called && XNode.DeepEquals(source, original); }
            });
            Test("migration-once-preserves-used-durability", () => {
                var source = Fixture(); Slots(source).Add(Slot(0, Old(0, 3))); int backups = 0;
                ScGun0282Migration.Execute(source, () => { backups++; return "test.snapshot"; });
                var reg = Registry(source); ScGunRegistry.Current = reg;
                int id = GunSpec.GetId(Terrain.ExtractData(Contents(Slots(source).Element("Values"))));
                // A later normal save carries a worn record and the marker. Do not reset durability on reload.
                reg.Get(id).Durability = 1234;
                var node = Group(Group(source.Element("Subsystems"), "ScGunBlockBehavior"), "GunRegistry"); node.RemoveNodes(); reg.Save(0).Save(node);
                var serialized = XElement.Parse(source.ToString());
                var second = ScGun0282Migration.Execute(serialized, () => { backups++; return "bad.snapshot"; });
                return second is null && backups == 1 && Registry(serialized).TryGetSnapshot(id, out var s) && s.Durability == 1234;
            });
            Test("internal-and-unknown-versions-not-guessed", () => {
                foreach (var version in new[] { "0.32.0", "0.34.0", "0.35.0", "0.35.2", "unknown" }) {
                    var source = Fixture(); var mod = Group(Group(source.Element("Subsystems"), "UsedMods"), "Mods").Element("Values");
                    mod.Elements("Value").Single(v => (string)v.Attribute("Name") == "Version").SetAttributeValue("Value", version);
                    Slots(source).Add(Slot(0, Terrain.MakeBlockValue(512, 0, 116173))); var original = new XElement(source);
                    if (ScGun0282Migration.Execute(source, () => throw new Exception("must not back up")) is not null || !XNode.DeepEquals(source, original)) return false;
                }
                return true;
            });
            Test("mixed-markers-and-invalid-ammo-refused", () => {
                foreach (string kind in new[] { "layout", "registry", "ammo", "stack" }) {
                    var source = Fixture(); var sub = Group(source.Element("Subsystems"), "ScGunBlockBehavior");
                    if (kind == "layout") sub.Add(V("GunDataLayout", "int", 4));
                    if (kind == "registry") sub.Add(G("GunRegistry"));
                    Slots(source).Add(Slot(0, Old(2, kind == "ammo" ? 6 : 3), count: kind == "stack" ? 2 : 1));
                    var original = new XElement(source);
                    try { ScGun0282Migration.Execute(source, () => throw new Exception("backup must not run for invalid input")); return false; }
                    catch (InvalidOperationException) { if (!XNode.DeepEquals(source, original)) return false; }
                }
                return true;
            });
            Test("id-limit-preflight-and-stateless-guns", () => {
                var source = Fixture(); for (int i = 0; i < 1023; i++) Slots(source).Add(Slot(i, Old(0, 3)));
                var original = new XElement(source);
                try { ScGun0282Migration.Prepare(source); return false; }
                catch (InvalidOperationException) { if (!XNode.DeepEquals(source, original)) return false; }
                foreach (var slot in Slots(source).Elements("Values")) slot.Element("Value").SetAttributeValue("Value", Old(0, 30));
                var plan = ScGun0282Migration.Prepare(source); return plan.Guns == 1023 && plan.Records == 0;
            });
            Test("legacy-readers-only-in-authorized-source", () => {
                int v1 = 2 | (3 << 2), v2 = 16384 | 5 | (7 << 6) | 8192;
                return ScGun0282Migration.TryDecode(v1, out var a) && a == new ScGun0282Migration.OldGun(2, 3, false)
                    && ScGun0282Migration.TryDecode(v2, out var b) && b == new ScGun0282Migration.OldGun(5, 7, true)
                    && !ScGun0282Migration.TryDecode(-1, out _) && !ScGun0282Migration.TryDecode(65536 | 63, out _);
            });
            Test("failed-hook-sets-load-guard", () => {
                var source = Fixture(); Slots(source).Add(Slot(0, Old(2, 6))); var originalSlots = new XElement(Slots(source));
                ScGun0282Migration.BeforeLoad(source, null); // validation fails before accessing world/backup
                var sub = Group(source.Element("Subsystems"), "ScGunBlockBehavior");
                return XNode.DeepEquals(originalSlots, Slots(source)) && sub.Elements("Value").Any(v => (string)v.Attribute("Name") == ScGun0282Migration.ErrorKey)
                    && !sub.Elements("Values").Any(v => (string)v.Attribute("Name") == "GunRegistry");
            });
            Test("physical-backup-and-xml-reload", () => {
                var source = Fixture(); Slots(source).Add(Slot(0, Old(2, 3)));
                string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "scgun-0282-tests", Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(directory, "Regions"));
                string projectPath = System.IO.Path.Combine(directory, "Project.xml"); source.Save(projectPath);
                byte[] before = System.IO.File.ReadAllBytes(projectPath), region = [1, 3, 5, 7];
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(directory, "Regions", "Region0.dat"), region);
                System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "SurvivalcraftStash.json"), "{\"keep\":true}");
                string backup = null;
                // Match GameManager: the source Project.xml stream remains open during the load hook.
                using (var input = System.IO.File.OpenRead(projectPath)) {
                    var node = XElement.Load(input);
                    ScGun0282Migration.Execute(node, () => backup = ScGun0282Migration.BackupWorld("system:" + directory.Replace('\\', '/')));
                    node.Save(System.IO.Path.Combine(directory, "Migrated.xml"));
                }
                using var zipStream = Storage.OpenFile(backup, OpenFileMode.Read);
                using var zip = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Read);
                byte[] Read(string name) { using var s = zip.GetEntry(name).Open(); using var bytes = new System.IO.MemoryStream(); s.CopyTo(bytes); return bytes.ToArray(); }
                if (!Read("Project.xml").SequenceEqual(before) || !Read("Regions/Region0.dat").SequenceEqual(region)
                    || System.Text.Encoding.UTF8.GetString(Read("SurvivalcraftStash.json")) != "{\"keep\":true}" || !System.IO.File.ReadAllBytes(projectPath).SequenceEqual(before)) return false;
                var reloaded = XElement.Load(System.IO.Path.Combine(directory, "Migrated.xml"));
                ScGunRegistry.Current = Registry(reloaded);
                int data = Terrain.ExtractData(Contents(Slots(reloaded).Element("Values")));
                return ScGun0282Migration.Prepare(reloaded) is null && GunSpec.GetVariant(data) == 2 && GunSpec.GetRounds(data) == 3 && GunSpec.GetDurability(data) == 200;
            });
        }
        finally { ScGunRegistry.Current = current; }
    }
}
