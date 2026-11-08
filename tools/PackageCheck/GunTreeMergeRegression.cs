using System.Reflection;
using System.Xml.Linq;
using Game;
using TemplatesDatabase;

/// <summary>world-tree-gun-registry-20261107 phase B: the one-time merge of a tree whose worlds kept their own registries
/// (identity join, root numbers kept), decision 2A (the Portal swap: an item's model differs from its own world's record
/// and equals one sibling's same-number record), the older-build round trip by receipt, remaps keyed by number and
/// model, obligations (kill credits, refunds) moved with their records or refused when they cannot be placed uniquely,
/// and a world carrying another tree's stamp imported by identity. Detached XML through the packaged DLL's own
/// ScGunTree.Apply / ScGunTreeMerge.</summary>
static class GunTreeMergeRegression {
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod) {
        var results = new List<Result>();
        void Check(string name, bool ok, string detail = "") => results.Add(new("gun-tree-merge/" + name, ok, detail));
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        var tree = mod.GetType("Game.ScGunTree", false); var merge = mod.GetType("Game.ScGunTreeMerge", false);
        if (tree is null || merge is null) { Check("present", false, "Game.ScGunTree / ScGunTreeMerge are not in this core (before 1.5.2)"); return results; }
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

            // ---- obligations travel with their records (1.5.2, user decision 2026-11-08) ----
            var undoType = mod.GetType("Game.ScGunUndo", true);
            object Registry((int Variant, int Rounds)[] records, Action<object> fill = null) {
                var r = Activator.CreateInstance(registryType);
                foreach (var (v, n) in records) registryType.GetMethod("Allocate").Invoke(r, [v, n, false, 400, 1500, 0]);
                fill?.Invoke(r); return r;
            }
            XElement Xml(object r) { var x = Group("GunRegistry"); ((ValuesDictionary)registryType.GetMethod("Save").Invoke(r, [0d])).Save(x); return x; }
            void Kill(object r, int id, int variant) { var q = registryType.GetField("Kills").GetValue(r); q.GetType().GetMethod("Enqueue").Invoke(q, [id, variant]); }
            void Refund(object r, string owner, int value) {
                var undo = Activator.CreateInstance(undoType); undoType.GetField("Slot").SetValue(undo, 0); undoType.GetField("Value").SetValue(undo, value); undoType.GetField("Count").SetValue(undo, 1);
                var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(undoType)); list.Add(undo);
                var recovery = registryType.GetProperty("Recovery").GetValue(r); recovery.GetType().GetMethod("Enqueue").Invoke(recovery, [owner, list]);
            }
            string[] KillsOf(XElement p) => G(G(G(Gun(p), "GunRegistry"), "PendingKills"), "Entries")?.Elements("Value").Select(e => (string)e.Attribute("Name") + ":" + (string)e.Attribute("Value")).ToArray() ?? [];
            string[] RefundsOf(XElement p) => G(G(G(Gun(p), "GunRegistry"), "Recovery"), "Batches")?.Elements("Values")
                .Select(b => Text(b, "Owner") + "=" + string.Join(";", G(b, "Steps").Elements("Value").Select(v => (string)v.Attribute("Value")))).ToArray() ?? [];
            string Refusal(Action a) { try { a(); return null; } catch (InvalidOperationException e) { return e.Message; } }
            const string chest = "entity/7/0/Game.ComponentChest";

            // The root keeps its own credit; the space's credit and refund move to the number its record got (#2), the refund's
            // destination bound to the space it was saved in.
            Call(tree, "ResetForTests");
            var rootOwn = Registry([(awp, 4)], r => Kill(r, 1, awp));
            var r6 = Save(w1, [(1, AWP)], [(1, Token(w1, 1))], [Item(awp, 1)], Xml(rootOwn));
            var spaceOwn = Registry([(ak, 25)], r => { Kill(r, 1, ak); Refund(r, chest, Item(ak, 1)); });
            Apply(r6, "", null, ("Space_0", Save(sp, [(1, AK)], [], [], Xml(spaceOwn))));
            var kills6 = KillsOf(r6); var refunds6 = RefundsOf(r6);
            Check("pending-kills-move-with-their-record", RowOf(r6, 2) == AK && kills6.Contains($"1:1,{awp}") && kills6.Contains($"2:2,{ak}") && kills6.Length == 2, string.Join(" ", kills6));
            Check("refunds-move-with-their-record-and-world", refunds6.Length == 1 && refunds6[0] == $"tree-world:Space_0|{chest}=0,{Item(ak, 2)},1,0,0", string.Join(" ", refunds6));

            // Refused, files unchanged: a credit whose record is not there, a credit the tree may already hold (same gun, same
            // event), a refund holding a gun the map does not place, an older build's world whose obligations differ.
            Call(tree, "ResetForTests");
            var r7 = Save(w1, [(1, AWP)], [(1, Token(w1, 1))], []); string r7Before = r7.ToString();
            string noRecord = Refusal(() => Apply(r7, "", null, ("Space_0", Save(sp, [(1, AK)], [], [], Xml(Registry([(ak, 25)], r => Kill(r, 5, ak)))))));
            Check("kill-without-a-placeable-record-refused", noRecord?.Contains("待结算") == true && r7.ToString() == r7Before, noRecord ?? "not refused");
            Call(tree, "ResetForTests");
            var r8 = Save(w1, [(1, AWP)], [(1, Token(w1, 1))], [], Xml(Registry([(awp, 4)], r => Kill(r, 1, awp)))); string r8Before = r8.ToString();
            string copied = Refusal(() => Apply(r8, "", null, ("Space_0", Save(sp, [(1, AWP)], [(1, Token(w1, 1))], [], Xml(Registry([(awp, 4)], r => Kill(r, 1, awp)))))));
            Check("kill-the-tree-may-already-hold-refused", copied?.Contains("待结算") == true && r8.ToString() == r8Before, copied ?? "not refused");
            Call(tree, "ResetForTests");
            var r9 = Save(w1, [(1, AWP)], [(1, Token(w1, 1))], []); string r9Before = r9.ToString();
            string unplaced = Refusal(() => Apply(r9, "", null, ("Space_0", Save(sp, [(1, AK)], [], [], Xml(Registry([(ak, 25)], r => Refund(r, chest, Item(ak, 9))))))));
            Check("refund-holding-an-unplaceable-gun-refused", unplaced?.Contains("待结算") == true && r9.ToString() == r9Before, unplaced ?? "not refused");
            Call(tree, "ResetForTests");
            var oldWithKill = Save(tw, [(1, AWP), (2, M4), (3, AK24), (4, AK24)], [], [], Xml(Registry([(awp, 4), (m4, 18), (ak, 24), (ak, 24)], r => Kill(r, 3, ak))));
            string oldBefore = oldWithKill.ToString();
            string older = Refusal(() => Apply(oldWithKill, "Tartareosity", Snapshot(w1, treeGun, receipts)));
            Check("older-build-world-with-different-obligations-refused", older?.Contains("旧版本") == true && oldWithKill.ToString() == oldBefore, older ?? "not refused");

            // ---- a world carrying another tree's stamp (a sub-world copied from another save): imported by identity ----
            Call(tree, "ResetForTests");
            var copiedIn = Save(tw, [(1, AK)], [(1, Token(tw, 1))], [Item(ak, 1)]);
            Gun(copiedIn).Add(Value("GunTreeId", Guid.NewGuid().ToString("N")), Value("GunTreeGeneration", 9));
            var tree10 = Gun(Save(w1, [(1, AWP)], [(1, Token(w1, 1))], [])); tree10.Elements("Value").Where(e => (string)e.Attribute("Name") == "GunTravelWorldIdentity").Remove();
            string foreignError = Refusal(() => Apply(copiedIn, "Tartareosity", Snapshot(w1, tree10, new XElement("Worlds"))));
            Check("world-of-another-tree-imported-and-renumbered", foreignError is null && Text(Gun(copiedIn), "GunTreeId") == w1 && RowOf(copiedIn, 1) == AWP && RowOf(copiedIn, 2) == AK
                && Slots(copiedIn)[0] == Item(ak, 2), foreignError ?? Gun(copiedIn).ToString());

            // ---- a later join finds the root's records by identity (their implicit identity, from the root seed kept in the tree file)
            Call(tree, "ResetForTests");
            var plainTree = Gun(Save(w1, [(1, AWP), (2, AK)], null, [])); plainTree.Elements("Value").Where(e => (string)e.Attribute("Name") == "GunTravelWorldIdentity").Remove();
            var carried = Save(tw, [(1, AK)], [(1, Token(w1, 2))], [Item(ak, 1)]); // an older build's import of the root's AK-47 (#2 there)
            Apply(carried, "Tartareosity", Snapshot(w1, plainTree, new XElement("Worlds", new XAttribute("RootSeed", w1))));
            Check("later-join-finds-root-records-by-identity", Records(carried) == 2 && Slots(carried)[0] == Item(ak, 2), Gun(carried).ToString());

            // ---- a Portal space an older build gave its own registry after the tree existed, merged when the root loads ----
            // (1.5.1 carried the root's AWP #1, AK-47 #2, M4A1-S #3 into Space_0 as M4 1, AWP 2, AK 3; the player saved there,
            // and the root's save holds the player's AWP under the space's #2, which the tree knows as the AK-47.)
            Call(tree, "ResetForTests");
            var rootGun = Gun(Save(w1, [(1, AWP), (2, AK), (3, M4)], null, [])); rootGun.Elements("Value").Where(e => (string)e.Attribute("Name") == "GunTravelWorldIdentity").Remove();
            var stampedRoot = Save(w1, [(1, AWP), (2, AK), (3, M4)], null, [Item(awp, 2)]);
            Gun(stampedRoot).Add(Value("GunTreeId", w1), Value("GunTreeGeneration", 3));
            var lateSpace = Save(sp, [(1, M4), (2, AWP), (3, AK)], [(1, Token(w1, 3)), (2, Token(w1, 1)), (3, Token(w1, 2))], [Item(ak, 3)]);
            Apply(stampedRoot, "", Snapshot(w1, rootGun, new XElement("Worlds", new XAttribute("RootSeed", w1), new XElement("World", new XAttribute("Path", ""), new XAttribute("Receipt", "")))), ("Space_0", lateSpace));
            var lateWorlds = (XElement)snapshotType.GetProperty("Worlds").GetValue(((System.Collections.IDictionary)tree.GetField("s_prepared", any).GetValue(null))["data:/Worlds/W"]);
            var spaceEntry = lateWorlds.Elements("World").SingleOrDefault(e => (string)e.Attribute("Path") == "Space_0");
            string latePending = (string)spaceEntry?.Attribute("Pending") ?? "";
            Check("late-space-merged-at-root-load", Slots(stampedRoot)[0] == Item(awp, 1) && Records(stampedRoot) == 3 && (string)spaceEntry?.Attribute("Merged") == "1"
                && latePending.Contains($"3/{ak}>2") && latePending.Contains($"2/{awp}>1") && latePending.Contains($"1/{m4}>3"), $"{string.Join(",", Slots(stampedRoot))} | {lateWorlds}");
        }
        catch (Exception e) { Check("setup", false, e.ToString()); }
        finally { current.SetValue(null, previous); try { Call(tree, "ResetForTests"); } catch (Exception) { } }
        return results;
    }
}
