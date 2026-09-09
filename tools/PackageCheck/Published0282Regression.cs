using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Game;
using TemplatesDatabase;

static class Published0282Regression {
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod, string package, string snapshot) {
        List<Result> results = [];
        void Check(string name, bool ok, string detail) => results.Add(new("published-0282/" + name, ok, detail));
        var registry = mod.GetType("Game.ScGunRegistry"); var current = registry.GetField("Current"); var saved = current.GetValue(null);
        var migration = mod.GetType("Game.ScGun0282Migration"); var spec = mod.GetType("Game.GunSpec");
        try {
            using var archive = ZipFile.OpenRead(package);
            using (var stream = archive.GetEntry("modinfo.json").Open()) {
                if (JsonDocument.Parse(stream).RootElement.GetProperty("Version").GetString() != "0.28.2") throw new Exception("Source package is not 0.28.2");
            }
            using var dllStream = archive.Entries.Single(e => e.FullName.EndsWith("ScCsgoKnives.dll")).Open();
            using var dllBytes = new MemoryStream(); dllStream.CopyTo(dllBytes); dllBytes.Position = 0;
            var source = new PackageContext("published-0282").LoadFromStream(dllBytes);
            var oldSpec = source.GetType("Game.GunSpec"); var oldGuns = (Array)oldSpec.GetField("All").GetValue(null);
            string hash; using (var file = File.OpenRead(package)) hash = Convert.ToHexString(SHA256.HashData(file));
            Check("source-package", oldGuns.Length == 35, "Actual 0.28.2 DLL; source package SHA256=" + hash);
            XElement Group(XElement e, string name) => e.Elements("Values").Single(x => (string)x.Attribute("Name") == name);
            object Prepare(XElement xml) => migration.GetMethod("Prepare").Invoke(null, [xml]);
            XElement Doc(object plan) => (XElement)plan.GetType().GetProperty("Document").GetValue(plan);
            object ReadRegistry(XElement xml) {
                var data = new ValuesDictionary(); data.ApplyOverrides(Group(Group(xml.Element("Subsystems"), "ScGunBlockBehavior"), "GunRegistry"));
                return registry.GetMethod("Load").Invoke(null, [data, 0d]);
            }
            int Read(string name, int data) => Convert.ToInt32(spec.GetMethod(name).Invoke(null, [data]));
            for (int v = 0; v < oldGuns.Length; v++) {
                var oldGun = oldGuns.GetValue(v); string name = (string)oldSpec.GetField("Name").GetValue(oldGun);
                int capacity = (int)oldSpec.GetField("Magazine").GetValue(oldGun);
                var xml = (XElement)mod.GetType("Game.ScGun0282MigrationSelfTest").GetMethod("Fixture").Invoke(null, null);
                var slots = Group(Group(xml.Element("Entities").Element("Entity"), "Inventory"), "Slots");
                var expected = new List<(int Rounds, bool Off)>();
                for (int rounds = 0; rounds <= capacity; rounds++) foreach (bool off in new[] { false, true }) {
                    int data = (int)oldSpec.GetMethod("MakeData").Invoke(null, [v, rounds, off]);
                    slots.Add(new XElement("Values", new XAttribute("Name", "Slot" + expected.Count),
                        new XElement("Value", new XAttribute("Name", "Contents"), new XAttribute("Type", "int"), new XAttribute("Value", Terrain.MakeBlockValue(512, 7, data))),
                        new XElement("Value", new XAttribute("Name", "Count"), new XAttribute("Type", "int"), new XAttribute("Value", "1"))));
                    expected.Add((rounds, off));
                }
                string before = xml.ToString(); var plan = Prepare(xml); var migrated = Doc(plan);
                bool ok = before == xml.ToString();
                for (int pass = 0; pass < 2; pass++) {
                    migrated = XElement.Parse(migrated.ToString()); var table = ReadRegistry(migrated); current.SetValue(null, table);
                    int i = 0;
                    foreach (var slot in Group(Group(migrated.Element("Entities").Element("Entity"), "Inventory"), "Slots").Elements("Values")) {
                        int value = (int)slot.Elements("Value").Single(x => (string)x.Attribute("Name") == "Contents").Attribute("Value");
                        int data = Terrain.ExtractData(value); var e = expected[i++];
                        ok &= Read("GetVariant", data) == v && Read("GetRounds", data) == e.Rounds && Read("GetSilencerOff", data) == (e.Off ? 1 : 0)
                            && Read("GetDurability", data) == (int)mod.GetType("Game.ScGunDurability").GetMethod("Full", [typeof(int)]).Invoke(null, [v]);
                    }
                    var node = Group(Group(migrated.Element("Subsystems"), "ScGunBlockBehavior"), "GunRegistry"); node.RemoveNodes();
                    ((ValuesDictionary)registry.GetMethod("Save").Invoke(table, [0d])).Save(node);
                    ok &= Prepare(migrated) is null;
                }
                Check(name + "/all-rounds-both-silencer-two-reloads", ok, $"{expected.Count} item states generated by actual published MakeData; identity/ammo/silencer/full durability preserved; no repeat migration");
            }
            if (snapshot is not null) {
                using var zip = ZipFile.OpenRead(snapshot); using var stream = zip.GetEntry("Project.xml").Open();
                var original = XElement.Load(stream); string before = original.ToString(); var plan = Prepare(original);
                var doc = Doc(plan); bool ok = before == original.ToString();
                for (int n = 0; n < 2; n++) {
                    doc = XElement.Parse(doc.ToString()); var table = ReadRegistry(doc);
                    ok &= !(bool)registry.GetProperty("Disabled").GetValue(table);
                    var node = Group(Group(doc.Element("Subsystems"), "ScGunBlockBehavior"), "GunRegistry"); node.RemoveNodes();
                    ((ValuesDictionary)registry.GetMethod("Save").Invoke(table, [0d])).Save(node);
                    ok &= Prepare(doc) is null;
                }
                Check("untouched-source-world-snapshot", ok, $"Read-only archive Project.xml; guns={plan.GetType().GetProperty("Guns").GetValue(plan)}; two XML/registry roundtrips; original archive untouched");
            }
        } catch (Exception e) { Check("failure", false, e.ToString()); }
        finally { current.SetValue(null, saved); }
        return results;
    }
}
