using System.Reflection;
using System.Xml.Linq;
using Game;
using TemplatesDatabase;

/// <summary>world-tree-gun-registry-20261107 (1.5.2). One registry per world tree: the tree file, the load-time
/// decision on detached XML (inject, found, merge), one shared instance for every loaded project of a tree, the newest
/// snapshot preferred over a project's own copy, and no carried-packet import inside a tree. Runs the packaged DLL's
/// own code; the real round trips through Ghoul, Ancient World and Portal are tools/MpM0/sp_tree.py.</summary>
static class GunTreeRegression {
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod) {
        var results = new List<Result>();
        void Check(string name, bool ok, string detail = "") => results.Add(new("gun-tree/" + name, ok, detail));
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        var tree = mod.GetType("Game.ScGunTree", false);
        // A core from before 1.5.2 (the pipeline's baseline run of the delivered packages) has no world tree: one named
        // failure instead of a crash of the whole suite.
        if (tree is null) { Check("present", false, "Game.ScGunTree is not in this core (before 1.5.2)"); return results; }
        var snapshotType = mod.GetType("Game.ScGunTree+Snapshot", true);
        var treeType = mod.GetType("Game.ScGunTree+Tree", true);
        var subsystemType = mod.GetType("Game.SubsystemScGunBlockBehavior", true);
        var registryType = mod.GetType("Game.ScGunRegistry", true);
        var current = registryType.GetField("Current"); var previous = current.GetValue(null);
        var worldsField = typeof(WorldsManager).GetField("WorldsDirectoryName"); string previousWorlds = (string)worldsField.GetValue(null);
        object previousGunBlock = tree.GetField("GunBlock", any).GetValue(null);
        object Call(string name, params object[] args) {
            try { return tree.GetMethod(name, any).Invoke(null, args); }
            catch (TargetInvocationException e) { throw e.InnerException!; }
        }
        object Snapshot(string id, int generation, XElement gun) => Activator.CreateInstance(snapshotType, id, generation, gun, "test", null);
        XElement Value(string n, object v) => new("Value", new XAttribute("Name", n), new XAttribute("Type", v is int ? "int" : "string"), new XAttribute("Value", v));
        XElement Group(string n, params object[] children) => new("Values", new XAttribute("Name", n), children);
        XElement G(XElement p, string n) => p?.Elements("Values").SingleOrDefault(e => (string)e.Attribute("Name") == n);
        string Text(XElement p, string n) => (string)p?.Elements("Value").SingleOrDefault(e => (string)e.Attribute("Name") == n)?.Attribute("Value");
        XElement Gun(XElement p) => G(p.Element("Subsystems"), "ScGunBlockBehavior");
        int layout = (int)mod.GetType("Game.GunSpec").GetField("DataLayout").GetRawConstantValue();
        int schema = (int)registryType.GetField("Schema").GetRawConstantValue();
        string errorKey = (string)mod.GetType("Game.ScGunSaveGuard").GetField("ErrorKey").GetRawConstantValue();
        string directory = Path.Combine(Path.GetTempPath(), "sc-gun-tree-" + Guid.NewGuid().ToString("N"));
        const string root = "data:/Worlds/W";
        try {
            Directory.CreateDirectory(directory);
            current.SetValue(null, null);
            worldsField.SetValue(null, "data:/Worlds");
            Call("ResetForTests");
            var registry = Activator.CreateInstance(registryType); registryType.GetMethod("Allocate").Invoke(registry, [2, 4, false, 400, 1500, 0]);
            string row = ((ValuesDictionary)registryType.GetMethod("Save").Invoke(registry, [0d])).GetValue<ValuesDictionary>("Records").GetValue<string>("1");
            string row3 = row.Replace("r=4,", "r=3,");
            // A gun group as a world saves it: the registry (record #1 or none) plus world-specific fields.
            XElement GunGroup(string record, string stamp = null, int generation = 0, int next = 2) {
                var records = Group("Records"); if (record is not null) records.Add(Value("1", record));
                var g = Group("ScGunBlockBehavior", Value("GunDataLayout", layout), Value("GunTravelSource", "data:/Worlds/W/Here"),
                    Group("GunRegistry", Value("Schema", schema), Value("Next", next), Value("GrowthMode", "CountAndGrow"), records));
                if (stamp is not null) g.Add(Value("GunTreeId", stamp), Value("GunTreeGeneration", generation));
                return g;
            }
            XElement Project(XElement gun) => new("Project", new XElement("Subsystems", Group("GameInfo", Value("WorldSeed", 1)), Group("BlocksManager", Value("302", "ScGunBlock")), gun),
                new XElement("Entities", new XElement("Entity", new XAttribute("Id", "1"), new XAttribute("Name", "MalePlayer"))));
            string id = Guid.NewGuid().ToString("N"), other = Guid.NewGuid().ToString("N");
            // The snapshot has exactly the shape the tree writes: built by the tree's own Group from a parsed registry.
            ValuesDictionary ValuesFromXml(XElement g) { var v = new ValuesDictionary(); v.ApplyOverrides(new XElement(g)); return v; }
            object Registry(XElement g) => registryType.GetMethod("Load").Invoke(null, [ValuesFromXml(g).GetValue<ValuesDictionary>("GunRegistry", null), 0d]);
            var treeGun = (XElement)Call("Group", Registry(GunGroup(row)), null, 0d);
            var snapshot = Snapshot(id, 5, treeGun);
            int Rounds(object reg) { var args = new object[] { 1, null }; bool ok = (bool)registryType.GetMethod("TryGetSnapshot").Invoke(reg, args); return ok ? (int)args[1].GetType().GetProperty("Rounds").GetValue(args[1]) : -1; }
            string Row(XElement p) => Text(G(G(Gun(p), "GunRegistry"), "Records"), "1");
            // Phase B: "others" are the other saves of the tree; a name alone stands for a sibling with one record of its own.
            IReadOnlyList<(string Path, XElement Project)> Saves(IReadOnlyList<string> names) => names.Select(n => (n, Project(GunGroup(row3)))).ToList();
            void Apply(XElement p, object tree, IReadOnlyList<string> others, Func<bool> reassign = null, Action restore = null) =>
                Call("Apply", p, root, "", tree, (Func<IReadOnlyList<(string Path, XElement Project)>>)(() => Saves(others)), reassign ?? (() => false), restore ?? (() => { }));

            // ---- the root of a tree ----
            string Root(string dir) => (string)Call("RootIn", dir, "data:/Worlds");
            Check("root-of-sub-worlds", Root("data:/Worlds/W/Space_0") == root && Root("data:/Worlds/W/Tartareosity") == root
                && Root("data:/Worlds/W/Dimensions/Ancient") == root && Root("data:/Worlds/W") == root && Root("data:\\Worlds\\W\\Space_0\\") == root,
                Root("data:/Worlds/W/Dimensions/Ancient"));
            Check("root-of-unrelated-folder-is-itself", Root("data:/Elsewhere/X") == "data:/Elsewhere/X" && Root("data:/Worlds/W2") != root);

            // ---- the tree file ----
            string file = Path.Combine(directory, "ScCsgoGuns.xml");
            Call("Write", file, (XElement)Call("Document", id, 1, new XElement(treeGun), null));
            Call("Write", file, (XElement)Call("Document", id, 2, new XElement(treeGun), null));
            var problems = new List<string>();
            object Read() { problems.Clear(); return Call("Read", file, problems); }
            int Gen(object s) => (int)snapshotType.GetProperty("Generation").GetValue(s);
            string Source(object s) => (string)snapshotType.GetProperty("Source").GetValue(s);
            var read = Read();
            Check("tree-file-newest-generation", read is not null && Gen(read) == 2 && Source(read) == "ScCsgoGuns.xml" && problems.Count == 0);
            File.WriteAllText(file, "<not xml");
            read = Read();
            Check("damaged-tree-file-falls-back-to-bak", read is not null && Gen(read) == 1 && Source(read) == "ScCsgoGuns.xml.bak" && problems.Count == 1, string.Join("; ", problems));
            File.WriteAllText(file + ".bak", "<ScGunTree Format=\"1\" Id=\"x\" Generation=\"1\" />");
            read = Read();
            Check("unusable-tree-file-and-bak-read-as-none", read is null && problems.Count == 2, string.Join("; ", problems));

            // ---- loading a world: the decision on detached XML ----
            Call("ResetForTests");
            var lost = Project(null); bool reassigned = false;
            Apply(lost, snapshot, [], () => reassigned = true);
            Check("lost-group-gets-the-tree-registry", reassigned && Row(lost) == row && Text(Gun(lost), "GunTreeId") == id && Text(Gun(lost), "GunTreeGeneration") == "5", lost.ToString());

            // A new sub-world (Ancient World's first dimension) has no gun block number: the tree's records are not its items.
            var unmapped = Project(null); unmapped.Element("Subsystems").Elements("Values").Single(e => (string)e.Attribute("Name") == "BlocksManager").Remove();
            Apply(unmapped, snapshot, []);
            string refusal0 = null; try { mod.GetType("Game.ScGunLoadIntegrity", true).GetMethod("Inspect").Invoke(null, [unmapped]); } catch (TargetInvocationException e) { refusal0 = e.InnerException?.Message; }
            Check("new-sub-world-without-gun-block-number-loads", Row(unmapped) == row && refusal0 is null, refusal0 ?? "");
            var stamped = Project(GunGroup(row3, id, 4));
            Apply(stamped, snapshot, []);
            Check("stamped-world-takes-the-newer-tree-registry", Row(stamped) == row && Text(Gun(stamped), "GunTravelSource") == "data:/Worlds/W/Here" && Text(Gun(stamped), "GunTreeGeneration") == "5");
            Call("ResetForTests");
            var newer = Project(GunGroup(row3, id, 7));
            string beforeNewer = newer.ToString(); Apply(newer, snapshot, []);
            var fromNewer = Acquire(Activator.CreateInstance(subsystemType), root, GunGroup(row3, id, 7));
            Check("world-copy-newer-than-the-tree-file-wins", newer.ToString() == beforeNewer && Rounds(Prop(fromNewer, "Registry")) == 3 && (int)Prop(fromNewer, "Generation") == 7, newer.ToString());
            Call("ResetForTests");
            // 1.5.2 (user decision 2026-11-08): a world carrying another tree's stamp (a sub-world copied from another save) is
            // imported by identity: its record joins the tree on a new number, the tree's stamp replaces the other one.
            var foreign = Project(GunGroup(row3, other, 3));
            Apply(foreign, snapshot, []);
            var foreignRecords = G(G(Gun(foreign), "GunRegistry"), "Records").Elements().ToArray();
            Check("world-of-another-tree-imported-by-identity", Text(Gun(foreign), "GunTreeId") == id && foreignRecords.Length == 2 && Row(foreign) == row
                && Text(G(G(Gun(foreign), "GunRegistry"), "Records"), "2") == row3, foreign.ToString());
            // Phase B: a world with a registry of its own joins the tree by identity instead of being refused.
            var ownRecords = Project(GunGroup(row3));
            Apply(ownRecords, snapshot, []);
            Check("unstamped-world-with-records-joins-the-tree", Text(Gun(ownRecords), "GunTreeId") == id && G(G(Gun(ownRecords), "GunRegistry"), "Records").Elements().Count() == 2, ownRecords.ToString());
            var empty = Project(GunGroup(null));
            Apply(empty, snapshot, []);
            Check("unstamped-world-without-records-joins-the-tree", Row(empty) == row && Text(Gun(empty), "GunTreeId") == id);
            var founder = Project(GunGroup(row3));
            Call("ResetForTests"); Apply(founder, null, ["Tartareosity"]);
            Check("unstamped-world-with-siblings-merges-them", Text(Gun(founder), "GunTreeId") is not null && G(G(Gun(founder), "GunRegistry"), "Records").Elements().Count() == 2, founder.ToString());
            founder = Project(GunGroup(row3));
            string before = founder.ToString(); Apply(founder, null, []);
            Check("unstamped-world-alone-founds-the-tree-unchanged", founder.ToString() == before);
            var refusal = Project(Group("ScGunBlockBehavior", Value(errorKey, "refused")));
            before = refusal.ToString(); Apply(refusal, snapshot, []);
            Check("refusal-marker-left-alone", refusal.ToString() == before);
            var restoredLost = Project(null); bool restoreAsked = false;
            Apply(restoredLost, null, ["Space_0"], null, () => { restoreAsked = true; restoredLost.Element("Subsystems").Add(GunGroup(row3)); });
            Check("lost-world-without-tree-restored-then-merged", restoreAsked && Text(Gun(restoredLost), "GunTreeId") is not null, restoredLost.ToString());

            // ---- an old carried packet (with an Error) never refuses a world ----
            // WorldInfo's constructor builds engine palettes that a headless host does not have; only the folder is read.
            static WorldInfo Info(string dir) { var info = (WorldInfo)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(WorldInfo)); info.DirectoryName = dir; return info; }
            bool enabled = (bool)tree.GetProperty("Enabled").GetValue(null);
            var packetWorld = Project(GunGroup(row, id, 5));
            packetWorld.Element("Entities").Element("Entity").Add(Group("ScGunTravel", Value("Version", "3"), Value("Source", "data:/Worlds/W/Tartareosity"), Value("Error", "携带枪械 #3 的记录缺失")));
            before = packetWorld.ToString(); bool threw = false;
            try { mod.GetType("Game.ScGunTravel", true).GetMethod("BeforeLoad").Invoke(null, [packetWorld, Info(root)]); }
            catch (TargetInvocationException) { threw = true; }
            Check("old-carried-packet-ignored", enabled && !threw && packetWorld.ToString() == before, $"enabled={enabled}, threw={threw}");

            // ---- one shared instance per loaded tree ----
            Call("ResetForTests");
            object Acquire(object member, string dir, XElement g) => Call("AcquireFor", member, dir, ValuesFromXml(g), 0d, Guid.NewGuid().ToString("N"), false);
            object Prop(object o, string name) => o.GetType().GetProperty(name)?.GetValue(o) ?? o.GetType().GetField(name)?.GetValue(o);
            var rootMember = Activator.CreateInstance(subsystemType); var spaceMember = Activator.CreateInstance(subsystemType);
            var t1 = Acquire(rootMember, root, GunGroup(row));
            var t2 = Acquire(spaceMember, root + "/Space_0", GunGroup(null, (string)Prop(t1, "Id")));
            Check("projects-of-one-tree-share-one-registry", ReferenceEquals(t1, t2) && ReferenceEquals(Prop(t1, "Registry"), Prop(t2, "Registry"))
                && ReferenceEquals(Prop(t1, "Primary"), rootMember) && Rounds(Prop(t1, "Registry")) == 4);
            var other1 = Acquire(Activator.CreateInstance(subsystemType), "data:/Worlds/X", GunGroup(row3));
            Check("another-tree-has-its-own-registry", !ReferenceEquals(Prop(other1, "Registry"), Prop(t1, "Registry")) && Rounds(Prop(other1, "Registry")) == 3);
            // 1.5.2: a Portal space with records of its own that the tree's merge never read (it appeared later) is merged
            // when it loads, into the live shared registry, with its pending kill credit; its items move on its first update.
            tree.GetField("GunBlock", any).SetValue(null, (Func<int>)(() => 302));
            var legacySpace = Activator.CreateInstance(subsystemType);
            var own = Activator.CreateInstance(registryType); registryType.GetMethod("Allocate").Invoke(own, [2, 3, false, 400, 1500, 0]);
            var ownKills = registryType.GetField("Kills").GetValue(own); ownKills.GetType().GetMethod("Enqueue").Invoke(ownKills, [1, 2]);
            var lateGroup = Group("ScGunBlockBehavior", Value("GunDataLayout", layout)); var lateRegistry = Group("GunRegistry");
            ((ValuesDictionary)registryType.GetMethod("Save").Invoke(own, [0d])).Save(lateRegistry); lateGroup.Add(lateRegistry);
            string lateError = null; try { Acquire(legacySpace, root + "/Space_1", lateGroup); } catch (InvalidOperationException e) { lateError = e.Message; }
            var sharedKills = (System.Collections.IEnumerable)registryType.GetField("Kills").GetValue(Prop(t1, "Registry")).GetType().GetProperty("Pending").GetValue(registryType.GetField("Kills").GetValue(Prop(t1, "Registry")));
            var credit = sharedKills.Cast<object>().SingleOrDefault();
            var remap = subsystemType.GetField("m_treeRemap", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(legacySpace);
            string remapText = remap is null ? null : (string)remap.GetType().GetMethod("Save").Invoke(remap, null);
            Check("late-space-with-its-own-records-merged", lateError is null && (int)Prop(t1, "Loaded") == 3 && (int)registryType.GetProperty("Count").GetValue(Prop(t1, "Registry")) == 2
                && credit is not null && (int)credit.GetType().GetProperty("RecordId").GetValue(credit) == 2 && remapText == "1/2>2",
                $"{lateError} records={registryType.GetProperty("Count").GetValue(Prop(t1, "Registry"))} credit={(credit is null ? "none" : credit.GetType().GetProperty("RecordId").GetValue(credit))} remap={remapText}");
            // A Portal space carrying another tree's stamp is imported by identity the same way, even where the tree remembers
            // having merged a space at that path once (that was another space's registry, not this one).
            ((XElement)Prop(t1, "Worlds")).Add(new XElement("World", new XAttribute("Path", "Space_2"), new XAttribute("Merged", "1")));
            var foreignSpace = Activator.CreateInstance(subsystemType);
            string foreignError = null; try { Acquire(foreignSpace, root + "/Space_2", GunGroup(row3, other, 9)); } catch (InvalidOperationException e) { foreignError = e.Message; }
            var foreignRemap = subsystemType.GetField("m_treeRemap", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(foreignSpace);
            Check("space-of-another-tree-imported", foreignError is null && (int)registryType.GetProperty("Count").GetValue(Prop(t1, "Registry")) == 3
                && foreignRemap is not null && (string)foreignRemap.GetType().GetMethod("Save").Invoke(foreignRemap, null) == "1/2>3", foreignError ?? "");
            var reserve = Activator.CreateInstance(subsystemType);
            Acquire(reserve, root + "/Tartareosity", GunGroup(null, (string)Prop(t1, "Id"), 0, 40));
            Check("joining-world-raises-the-watermark", (int)registryType.GetProperty("Next").GetValue(Prop(t1, "Registry")) >= 40);
            Call("Release", t1, reserve);
            Call("Release", t1, rootMember);
            Check("primary-passes-on-release", ReferenceEquals(Prop(t1, "Primary"), spaceMember) && (bool)Call("IsLoaded", t1));
            Call("Release", t1, legacySpace); Call("Release", t1, foreignSpace);
            Call("Release", t1, spaceMember);
            var t3 = Acquire(Activator.CreateInstance(subsystemType), root, GunGroup(row3));
            Check("last-release-unloads-the-tree", !(bool)Call("IsLoaded", t1) && !ReferenceEquals(t3, t1) && Rounds(Prop(t3, "Registry")) == 3);

            // ---- the first project to load opens the tree from the newest snapshot, not its own copy ----
            Call("ResetForTests");
            var rootXml = Project(GunGroup(row3, id, 4));
            Apply(rootXml, snapshot, []); // ProjectXmlLoad of the root: prepares generation 5 (rounds 4)
            var first = Acquire(Activator.CreateInstance(subsystemType), root + "/Space_0", GunGroup(row3, id, 4)); // a Portal space built first
            Check("first-project-opens-from-the-newest-snapshot", Rounds(Prop(first, "Registry")) == 4 && (int)Prop(first, "Generation") == 5);

            // ---- every loaded project's holders, once ----
            var holderType = mod.GetType("Game.ScGunHolders+Holder", true);
            Array Holders(params (int Id, string Key)[] items) { var a = Array.CreateInstance(holderType, items.Length); for (int i = 0; i < items.Length; i++) a.SetValue(Activator.CreateInstance(holderType, items[i].Id, items[i].Key, null, -1), i); return a; }
            var outer = Array.CreateInstance(typeof(IEnumerable<>).MakeGenericType(holderType), 2);
            outer.SetValue(Holders((1, "root:0")), 0); outer.SetValue(Holders((1, "space:0"), (2, "space:1")), 1);
            var all = (System.Collections.ICollection)Call("Holders", outer, Prop(first, "Registry"), 302);
            Check("holders-of-every-loaded-project", all.Count == 3);
        }
        catch (Exception e) { Check("setup", false, e.ToString()); }
        finally {
            current.SetValue(null, previous); worldsField.SetValue(null, previousWorlds);
            try { tree.GetMethod("ResetForTests", any).Invoke(null, null); } catch (Exception) { }
            try { tree.GetField("GunBlock", any).SetValue(null, previousGunBlock); } catch (Exception) { }
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
        return results;
    }
}
