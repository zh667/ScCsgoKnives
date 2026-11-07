using System.Reflection;
using System.Xml.Linq;
using Game;
using TemplatesDatabase;

/// <summary>world-tree-gun-registry-20261107 phase B: the one-time merge of a tree whose worlds kept their own registries
/// (identity join, root numbers kept), decision 2A (the Portal swap: an item's model differs from its own world's record
/// and equals one sibling's same-number record), the older-build round trip by receipt, and remaps keyed by number and
/// model. Detached XML through the packaged DLL's own ScGunTree.Apply / ScGunTreeMerge.</summary>
static class GunTreeMergeRegression {
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod) {
        var results = new List<Result>();
        void Check(string name, bool ok, string detail = "") => results.Add(new("gun-tree-merge/" + name, ok, detail));
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        var tree = mod.GetType("Game.ScGunTree", true); var merge = mod.GetType("Game.ScGunTreeMerge", true);
        var snapshotType = mod.GetType("Game.ScGunTree+Snapshot", true); var tableType = mod.GetType("Game.ScGunTreeMerge+Table", true);
        var registryType = mod.GetType("Game.ScGunRegistry", true); var spec = mod.GetType("Game.GunSpec", true);
        var current = registryType.GetField("Current"); var previous = current.GetValue(null);
        object Call(Type t, string name, params object[] args) {
            try { return t.GetMethods(any).Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(null, args); }
            catch (TargetInvocationException e) { throw e.InnerException!; }
        }
        XElement Value(string n, object v) => new("Value", new XAttribute("Name", n), new XAttribute("Type", v is int ? "int" : "string"), new XAttribute("Value", v));
        XElement Group(string n, params object[] children) => new("Values", new XAttribute("Name", n), children);
        XElement G(XElement p, string n) => p?.Elements("Values").SingleOrDefault(e => (string)e.Attribute("Name") == n);
        string Text(XElement p, string n) => (string)p?.Elements("Value").SingleOrDefault(e => (string)e.Attribute("Name") == n)?.Attribute("Value");
        const int block = 302;
        int layout = (int)spec.GetField("DataLayout").GetRawConstantValue();
        int schema = (int)registryType.GetField("Schema").GetRawConstantValue();
        int V(string name) {
            var all = (Array)spec.GetField("All").GetValue(null);
            for (int i = 0; i < all.Length; i++) { var g = all.GetValue(i); if ((g.GetType().GetField("Name")?.GetValue(g) ?? g.GetType().GetProperty("Name")?.GetValue(g)) as string == name) return i; }
            throw new InvalidOperationException("no gun " + name);
        }
        int awp = V("awp"), m4 = V("m4a1s"), ak = V("ak47");
        string Token(string seed, int id) => (string)mod.GetType("Game.ScTravelLedger", true).GetMethod("Token").Invoke(null, [seed, id]);
        string Row(int variant, int rounds) {
            var r = Activator.CreateInstance(registryType); registryType.GetMethod("Allocate").Invoke(r, [variant, rounds, false, 400, 1500, 0]);
            return ((ValuesDictionary)registryType.GetMethod("Save").Invoke(r, [0d])).GetValue<ValuesDictionary>("Records").GetValue<string>("1");
        }
        int Item(int variant, int id) => Terrain.MakeBlockValue(block, 0, (int)spec.GetMethod("WithId").Invoke(null, [variant, id]));
        // A save: rows (number -> row), explicit identities, its world uuid, the gun items it holds (slot order), optional
        // pending kills; null rows = no gun group at all (a Ghoul world that never saved with CS).
        XElement Save(string uuid, (int Id, string Row)[] rows, (int Id, string Token)[] ids, int[] items, XElement registry = null) {
            var slots = Group("Slots"); int s = 0; foreach (int v in items) slots.Add(Group((s++).ToString(), Value("Contents", v), Value("Count", 1)));
            var subs = new XElement("Subsystems", Group("GameInfo", Value("WorldSeed", 1)), Group("BlocksManager", Value(block.ToString(), "ScGunBlock")));
            if (rows is not null) {
                var records = Group("Records"); foreach (var (i, r) in rows) records.Add(Value(i.ToString(), r));
                var reg = registry ?? Group("GunRegistry", Value("Schema", schema), Value("Next", rows.Select(x => x.Id).DefaultIfEmpty(0).Max() + 1), Value("GrowthMode", "CountAndGrow"), records);
                var gun = Group("ScGunBlockBehavior", Value("GunDataLayout", layout), Value("GunTravelWorldIdentity", uuid), reg);
                if (ids is not null) { var idg = Group("GunTravelIdentities"); foreach (var (i, t) in ids) idg.Add(Value(i.ToString(), t)); gun.Add(idg); }
                subs.Add(gun);
            }
            return new XElement("Project", subs, new XElement("Entities", new XElement("Entity", new XAttribute("Id", "1"), new XAttribute("Name", "MalePlayer"), Group("Inventory", slots))));
        }
        int[] Slots(XElement p) => p.Descendants("Values").Single(e => (string)e.Attribute("Name") == "Slots").Elements("Values")
            .Select(sl => int.Parse((string)sl.Elements("Value").Single(e => (string)e.Attribute("Name") == "Contents").Attribute("Value"))).ToArray();
        XElement Gun(XElement p) => G(p.Element("Subsystems"), "ScGunBlockBehavior");
        string RowOf(XElement p, int id) => Text(G(G(Gun(p), "GunRegistry"), "Records"), id.ToString());
        int Records(XElement p) => G(G(Gun(p), "GunRegistry"), "Records")?.Elements().Count() ?? 0;
        object Snapshot(string id, XElement gun, XElement worlds) => Activator.CreateInstance(snapshotType, id, 3, gun, "test", worlds);
        void Apply(XElement p, string path, object snap, params (string Path, XElement Project)[] others) =>
            Call(tree, "Apply", p, "data:/Worlds/W", path, snap, (Func<IReadOnlyList<(string Path, XElement Project)>>)(() => others), (Func<bool>)(() => false), (Action)(() => { }));
        bool Refused(Action a) { try { a(); return false; } catch (InvalidOperationException) { return true; } }
        string w1 = Guid.NewGuid().ToString("N"), sp = Guid.NewGuid().ToString("N"), tw = Guid.NewGuid().ToString("N");
        try {
            current.SetValue(null, null);
            Call(tree, "ResetForTests");
            string AWP = Row(awp, 4), M4 = Row(m4, 18), AK = Row(ak, 25);

            // ---- the reproduced Portal swap (fixture portal-swap-20261108-1951, in miniature)
            var world1 = Save(w1, [(1, AWP), (2, M4), (3, AK)], [(1, Token(w1, 1)), (2, Token(w1, 2)), (3, Token(w1, 3))], [Item(ak, 1), Item(m4, 2), Item(awp, 3)]);
            var space0 = Save(sp, [(1, AK), (2, M4), (3, AWP)], [(1, Token(w1, 3)), (2, Token(w1, 2)), (3, Token(w1, 1))], []);
            var tartar = Save(null, null, null, [Item(ak, 1), Item(m4, 2), Item(awp, 3)]); // Ghoul's copy of the player, no registry
            Apply(world1, "", null, ("Space_0", space0), ("Tartareosity", tartar));
            var after = Slots(world1);
            Check("swap-corrected-in-the-root", after[0] == Item(ak, 3) && after[1] == Item(m4, 2) && after[2] == Item(awp, 1), string.Join(",", after));
            Check("swap-records-keep-their-state", RowOf(world1, 1) == AWP && RowOf(world1, 3) == AK && RowOf(world1, 2) == M4 && Records(world1) == 3, Gun(world1).ToString());
            var inspection = Call(mod.GetType("Game.ScGunLoadIntegrity", true), "Inspect", world1);
            Check("swap-root-loads-without-conflicts", ((Array)inspection.GetType().GetProperty("Issues").GetValue(inspection)).Length == 0);
            // The prepared tree carries the Ghoul world's remap and the space's merged mark for their own loads.
            var prepared = ((System.Collections.IDictionary)tree.GetField("s_prepared", any).GetValue(null))["data:/Worlds/W"];
            var worlds = (XElement)snapshotType.GetProperty("Worlds").GetValue(prepared);
            Check("swap-remap-pending-for-the-ghoul-copy", worlds.Elements("World").Any(e => (string)e.Attribute("Path") == "Tartareosity" && ((string)e.Attribute("Pending")).Contains($"1/{ak}>3") && ((string)e.Attribute("Pending")).Contains($"3/{awp}>1"))
                && worlds.Elements("World").Any(e => (string)e.Attribute("Path") == "Space_0" && (string)e.Attribute("Merged") == "1"), worlds.ToString());
            // The Ghoul world loads later: its copy of the player gets the remap; Ghoul's fresh copy of corrected items is not touched.
            var later = Save(null, null, null, [Item(ak, 1), Item(awp, 1)]);
            Apply(later, "Tartareosity", prepared);
            var laterSlots = Slots(later);
            Check("remap-keyed-by-number-and-model", laterSlots[0] == Item(ak, 3) && laterSlots[1] == Item(awp, 1), string.Join(",", laterSlots));
            // The space loads with its own (already merged) registry: no second join, nothing kept aside.
            Call(tree, "ResetForTests");
            // (its M4 row differs from the tree's: a second join would keep it aside on a new number)
            var space0Again = Save(sp, [(1, AK), (2, Row(m4, 17)), (3, AWP)], [(1, Token(w1, 3)), (2, Token(w1, 2)), (3, Token(w1, 1))], []); int rowsBefore = Records(world1);
            Apply(space0Again, "Space_0", Activator.CreateInstance(snapshotType, (string)snapshotType.GetProperty("Id").GetValue(prepared), 0, new XElement((XElement)snapshotType.GetProperty("Gun").GetValue(prepared)), "test", worlds));
            Check("merged-world-is-not-joined-twice", Records(space0Again) == rowsBefore, Gun(space0Again).ToString());

            // ---- 2A only on unique evidence
            Call(tree, "ResetForTests");
            var r2 = Save(w1, [(1, AWP), (2, AK), (3, AK)], [(1, Token(w1, 1)), (2, Token(w1, 2)), (3, Token(w1, 3))], [Item(ak, 1)]);
            var s2a = Save(sp, [(1, AK)], [(1, Token(w1, 2))], []); var s2b = Save(tw, [(1, AK)], [(1, Token(w1, 3))], []);
            Apply(r2, "", null, ("A", s2a), ("B", s2b));
            Check("two-possible-originals-left-alone", Slots(r2)[0] == Item(ak, 1), string.Join(",", Slots(r2)));
            Call(tree, "ResetForTests");
            var r3 = Save(w1, [(1, AWP), (3, AK)], [(1, Token(w1, 1)), (3, Token(w1, 3))], [Item(ak, 1), Item(ak, 3)]);
            Apply(r3, "", null, ("Space_0", Save(sp, [(1, AK)], [(1, Token(w1, 3))], [])));
            Check("original-held-by-a-correct-item-left-alone", Slots(r3)[0] == Item(ak, 1) && Slots(r3)[1] == Item(ak, 3), string.Join(",", Slots(r3)));

            // ---- identity join
            Call(tree, "ResetForTests");
            var r4 = Save(w1, [(1, AWP)], [(1, Token(w1, 1))], [Item(awp, 1)]);
            var s4 = Save(sp, [(1, AWP), (2, M4), (5, "not a row")], [(1, Token(w1, 1)), (2, Token(sp, 2))], [Item(m4, 2)]);
            Apply(r4, "", null, ("Space_0", s4));
            var w4 = (XElement)snapshotType.GetProperty("Worlds").GetValue(((System.Collections.IDictionary)tree.GetField("s_prepared", any).GetValue(null))["data:/Worlds/W"]);
            string pending4 = (string)w4.Elements("World").Single(e => (string)e.Attribute("Path") == "Space_0").Attribute("Pending");
            Check("join-same-identity-same-number-new-identity-new-number", Records(r4) == 3 && RowOf(r4, 1) == AWP && RowOf(r4, 3) == M4 && pending4.Contains($"2/{m4}>3") && pending4.Contains($"1/{awp}>1"), $"{pending4} | {Gun(r4)}");

            // ---- an older build played a world after it joined: by receipt
            Call(tree, "ResetForTests");
            string Hash(string row) => (string)merge.GetMethod("Hash").Invoke(null, [row]);
            string AK24 = Row(ak, 24), AK20 = Row(ak, 20), AWP3 = Row(awp, 3);
            var treeGun = Gun(Save(w1, [(1, AWP), (2, M4), (3, AK24), (4, AK24)], [(1, Token(w1, 1)), (2, Token(w1, 2)), (3, Token(w1, 3)), (4, Token(w1, 4))], []));
            treeGun.Elements("Value").Where(e => (string)e.Attribute("Name") == "GunTravelWorldIdentity").Remove();
            var receipts = new XElement("Worlds", new XElement("World", new XAttribute("Path", "Tartareosity"),
                new XAttribute("Receipt", $"1:{Hash(AWP)};2:{Hash(M4)};3:{Hash(AK)};4:{Hash(AK)}")));
            // world: #1 changed only here, #2 unchanged (tree changed it? no: equal), #3 changed only in the tree, #4 changed on both
            // sides, #5 allocated here while the tree also has no #5 (adopt), #4..: and #6 collides with nothing.
            var oldPlayed = Save(tw, [(1, AWP3), (2, M4), (3, AK), (4, AK20), (5, AK)], [(5, Token(tw, 5))], [Item(awp, 1), Item(ak, 3), Item(ak, 4), Item(ak, 5)]);
            Apply(oldPlayed, "Tartareosity", Snapshot(w1, treeGun, receipts));
            Check("receipt-takes-a-change-made-only-there", RowOf(oldPlayed, 1) == AWP3);
            Check("receipt-keeps-a-change-made-only-in-the-tree", RowOf(oldPlayed, 3) == AK24);
            Check("receipt-conflict-keeps-the-tree-and-the-copy", RowOf(oldPlayed, 4) == AK24 && G(G(Gun(oldPlayed), "GunRegistry"), "Records").Elements().Any(e => (string)e.Attribute("Value") == AK20));
            Check("older-build-allocation-adopted-on-a-free-number", RowOf(oldPlayed, 5) == AK && Slots(oldPlayed)[3] == Item(ak, 5));
            // a number the older build allocated that the tree also allocated since: the world's gun moves
            var treeGun2 = new XElement(treeGun); G(G(treeGun2, "GunRegistry"), "Records").Add(Value("5", M4));
            var collided = Save(tw, [(1, AWP), (2, M4), (3, AK24), (4, AK24), (5, AK)], [], [Item(ak, 5)]);
            Apply(collided, "Tartareosity", Snapshot(w1, treeGun2, receipts));
            int movedTo = (Terrain.ExtractData(Slots(collided)[0]) >> 6) & 1023;
            Check("independent-allocations-renumbered", RowOf(collided, 5) == M4 && movedTo > 5 && RowOf(collided, movedTo) == AK, $"#{movedTo} {Gun(collided)}");

            // ---- unsafe merges are refused, unchanged
            Call(tree, "ResetForTests");
            // A sub-world saved with a kill still to be written into its gun's record (a real registry's own save).
            var killed = Activator.CreateInstance(registryType); registryType.GetMethod("Allocate").Invoke(killed, [ak, 25, false, 400, 1500, 0]);
            var queue = registryType.GetField("Kills").GetValue(killed); queue.GetType().GetMethod("Enqueue").Invoke(queue, [1, ak]);
            var killedXml = Group("GunRegistry"); ((ValuesDictionary)registryType.GetMethod("Save").Invoke(killed, [0d])).Save(killedXml);
            var withKills = Save(sp, [(1, AK)], [], [], killedXml);
            var r6 = Save(w1, [(1, AWP)], [], []); string r6Before = r6.ToString(); string reason = null;
            try { Apply(r6, "", null, ("Space_0", withKills)); } catch (InvalidOperationException e) { reason = e.Message; }
            Check("sub-world-with-pending-kills-refused", reason?.Contains("待结算") == true && r6.ToString() == r6Before, reason ?? "not refused");
        }
        catch (Exception e) { Check("setup", false, e.ToString()); }
        finally { current.SetValue(null, previous); try { Call(tree, "ResetForTests"); } catch (Exception) { } }
        return results;
    }
}
