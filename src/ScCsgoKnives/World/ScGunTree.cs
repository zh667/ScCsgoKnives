using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Engine;
using TemplatesDatabase;
using File = System.IO.File;
using Path = System.IO.Path;
using Directory = System.IO.Directory;
using InvalidDataException = System.IO.InvalidDataException;
using EnumerationOptions = System.IO.EnumerationOptions;
namespace Game;

/// <summary>world-tree-gun-registry-20261107 (1.5.2; design docs/tasks/world-tree-gun-registry-20261107.md).
/// One gun registry for a whole world tree: the top-level world folder under the Worlds folder and every sub-world in
/// it (Ghoul <c>World1/Tartareosity</c>, Ancient World <c>World1/Dimensions/Ancient</c>, Portal <c>World1/Space_N</c>).
/// A record number then names the same gun in every world of the tree, however a sub-world mod moves the item: copying
/// the player XML, restoring item values after a load, or moving the player entity between two projects running at
/// once. No carried packets, no arrival renumbering inside a tree.
/// The authority is <see cref="FileName"/> in the tree's root folder (verified temporary file, previous one kept as
/// .bak, generation counter, tree GUID). In memory there is one shared instance per root, held by every loaded project
/// of the tree (reference counted). Every world still saves the full registry into its own Project.xml group, stamped
/// with the tree id and generation, for older builds and as the last fallback.
/// A world whose own registry is not yet part of the tree (saved by an older build, carrying another tree's stamp, or a
/// Portal space that appears after the merge) is merged by record identity (ScGunTreeMerge); what cannot be placed
/// uniquely is refused, never guessed.</summary>
public static class ScGunTree {
    public const string FileName = "ScCsgoGuns.xml";
    public const int Format = 1;
    public const string TreeIdKey = "GunTreeId", GenerationKey = "GunTreeGeneration";
    const string RootName = "ScGunTree", GunGroup = "ScGunBlockBehavior", RegistryKey = "GunRegistry", LayoutKey = "GunDataLayout";

    public sealed class Tree {
        public string Root, Id;
        public int Generation;
        public ScGunRegistry Registry;
        public ScTravelLedger Ledger;
        internal readonly List<SubsystemScGunBlockBehavior> Members = [];
        /// <summary>The project that owns the tree-wide duties (file write, duplicate audit, growth, refunds): the
        /// first one loaded that is still loaded.</summary>
        public SubsystemScGunBlockBehavior Primary => Members.Count > 0 ? Members[0] : null;
        public int Loaded => Members.Count;
        /// <summary>Per world of the tree (path relative to the root, "" for the root): Receipt (what this build last
        /// wrote into that world, record -> row hash), Pending (its items' remap from a merge, by number and model) and
        /// Merged (its own registry is already in the tree; it has not been saved by this build since).</summary>
        public XElement Worlds = new("Worlds");
        internal readonly Dictionary<SubsystemScGunBlockBehavior, string> Paths = [];
    }
    internal sealed record Snapshot(string Id, int Generation, XElement Gun, string Source, XElement Worlds = null);

    static readonly Dictionary<string, Tree> s_trees = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>What the last ProjectXmlLoad of a root decided, for the first project of that tree to load: the newest
    /// snapshot (file or memory), or only the id of a tree being founded. Whichever project of the tree loads first - a
    /// Portal space may be built before its root's gun subsystem loads - opens the tree from it.</summary>
    static readonly Dictionary<string, Snapshot> s_prepared = new(StringComparer.OrdinalIgnoreCase);
    static readonly ConditionalWeakTable<XElement, object> s_done = new();
    static readonly object s_queue = new(), s_file = new();
    static Task s_writes = Task.CompletedTask;

    /// <summary>The process that owns the world: single player, or the multiplayer host/server. A remote client shows
    /// the server's records and keeps no tree.</summary>
    public static bool Enabled => ScNet.IsAuthority;

    static string Canonical(string path) => (path ?? "").Replace('\\', '/').TrimEnd('/');
    static XElement G(XElement parent, string name) => parent?.Elements("Values").SingleOrDefault(e => (string)e.Attribute("Name") == name);
    static string Text(XElement parent, string name) => (string)parent?.Elements("Value").SingleOrDefault(e => (string)e.Attribute("Name") == name)?.Attribute("Value");
    static XElement Field(string name, object value) => new("Value", new XAttribute("Name", name),
        new XAttribute("Type", value is int ? "int" : "string"), new XAttribute("Value", Convert.ToString(value, CultureInfo.InvariantCulture)));

    /// <summary>The root of a world's tree: the first folder under the Worlds folder. A folder outside it is its own tree.</summary>
    public static string RootOf(string directory) => RootIn(directory, WorldsManager.WorldsDirectoryName);
    internal static string RootIn(string directory, string worlds) {
        string dir = Canonical(directory), top = Canonical(worlds);
        if (top.Length == 0 || !dir.StartsWith(top + "/", StringComparison.OrdinalIgnoreCase)) return dir;
        string rest = dir[(top.Length + 1)..];
        int slash = rest.IndexOf('/');
        return slash < 0 ? dir : top + "/" + rest[..slash];
    }
    static string SystemFile(string root) => Storage.GetSystemPath(Storage.CombinePaths(root, FileName));

    // ---- the tree file ----

    internal static XElement Document(string id, int generation, XElement gun, XElement worlds = null) =>
        new(RootName, new XAttribute("Format", Format), new XAttribute("Id", id), new XAttribute("Generation", generation),
            new XAttribute("Build", ScCompatibility.ActiveBuild), gun, worlds is null ? null : new XElement(worlds));

    // ---- per-world state in the tree file ----
    static string Label(string path) => string.IsNullOrEmpty(path) ? "主世界" : path;
    internal static string RelativeTo(string root, string dir) {
        string r = Canonical(root), d = Canonical(dir);
        return string.Equals(r, d, StringComparison.OrdinalIgnoreCase) ? "" : d.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase) ? d[(r.Length + 1)..] : d;
    }
    static XElement Entry(XElement worlds, string path, bool create) {
        var e = worlds?.Elements("World").FirstOrDefault(x => string.Equals((string)x.Attribute("Path") ?? "", path ?? "", StringComparison.OrdinalIgnoreCase));
        if (e is null && create && worlds is not null) { e = new XElement("World", new XAttribute("Path", path ?? "")); worlds.Add(e); }
        return e;
    }
    internal static ScGunTreeMerge.Remap PendingOf(XElement worlds, string path) =>
        Entry(worlds, path, false)?.Attribute("Pending") is { } a ? ScGunTreeMerge.Remap.Load(a.Value) : null;
    internal static bool MergedOf(XElement worlds, string path) => (string)Entry(worlds, path, false)?.Attribute("Merged") == "1";
    /// <summary>The root world's identity: records made in the root carry identities made from it, so a world joining later
    /// (a Ghoul world, a Portal space, a world an older build played) finds them by identity.</summary>
    internal static string RootSeed(XElement worlds) => (string)worlds?.Attribute("RootSeed");
    static void KeepRootSeed(XElement worlds, string seed) {
        if (worlds is not null && RootSeed(worlds) is null && !string.IsNullOrEmpty(seed)) worlds.SetAttributeValue("RootSeed", ScGunTreeMerge.Seed(seed));
    }
    internal static Dictionary<int, string> ReceiptOf(XElement worlds, string path) {
        if (Entry(worlds, path, false)?.Attribute("Receipt") is not { } a) return null;
        var receipt = new Dictionary<int, string>();
        foreach (string part in a.Value.Split(';', StringSplitOptions.RemoveEmptyEntries)) {
            int colon = part.IndexOf(':');
            if (colon > 0 && int.TryParse(part[..colon], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) receipt[id] = part[(colon + 1)..];
        }
        return receipt;
    }
    /// <summary>The world has taken its remap and its registry is part of the tree: nothing pending for it any more.</summary>
    static XElement Settled(XElement worlds, string path) {
        var copy = worlds is null ? new XElement("Worlds") : new XElement(worlds);
        var e = Entry(copy, path, false);
        e?.Attribute("Pending")?.Remove(); e?.Attribute("Merged")?.Remove();
        return copy;
    }

    /// <summary>The registry part of a world's gun group, as this build saves it.</summary>
    internal static XElement Group(ScGunRegistry registry, ScTravelLedger ledger, double now) {
        var gun = new XElement("Values", new XAttribute("Name", GunGroup));
        var table = new XElement("Values", new XAttribute("Name", RegistryKey)); registry.Save(now).Save(table); gun.Add(table);
        if (ledger?.SaveIdentities() is { } identities) {
            var ids = new XElement("Values", new XAttribute("Name", ScGunTravel.Identities)); identities.Save(ids); gun.Add(ids);
        }
        gun.Add(Field(LayoutKey, ScGunRegistry.StampFor(registry.LegacyWorld)));
        return gun;
    }

    /// <summary>The newest readable tree file: the file, else its .bak. Problems are reported, never thrown.</summary>
    internal static Snapshot Read(string path, List<string> problems) {
        foreach (string candidate in new[] { path, path + ".bak" }) {
            if (!File.Exists(candidate)) continue;
            try {
                var doc = XElement.Load(candidate);
                if (doc.Name != RootName || (int?)doc.Attribute("Format") != Format) throw new InvalidDataException("unsupported tree file format");
                string id = (string)doc.Attribute("Id");
                if (!Guid.TryParse(id, out _)) throw new InvalidDataException("tree id missing");
                int generation = (int?)doc.Attribute("Generation") ?? throw new InvalidDataException("generation missing");
                var groups = doc.Elements("Values").Where(e => (string)e.Attribute("Name") == GunGroup).ToArray();
                if (groups.Length != 1 || G(groups[0], RegistryKey) is null) throw new InvalidDataException("gun registry missing");
                var values = new ValuesDictionary(); values.ApplyOverrides(new XElement(groups[0]));
                ScGunSaveGuard.Validate(values);
                return new(id, generation, new XElement(groups[0]), Path.GetFileName(candidate), doc.Element("Worlds") is { } worlds ? new XElement(worlds) : new XElement("Worlds"));
            }
            catch (Exception e) { problems.Add(Path.GetFileName(candidate) + ": " + e.Message); }
        }
        return null;
    }

    /// <summary>Verified temporary file, the previous file kept as .bak, then a same-directory replace.</summary>
    internal static void Write(string path, XElement document) {
        byte[] bytes = Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
        lock (s_file) ScUiSettings.WriteAtomic(path, bytes, () => { if (File.Exists(path)) File.Copy(path, path + ".bak", true); });
    }
    /// <summary>File writes leave the game thread but stay in order; a load waits for them (<see cref="Flush"/>).</summary>
    static void Queue(string path, XElement document) {
        lock (s_queue) s_writes = s_writes.ContinueWith(_ => {
            try { Write(path, document); }
            catch (Exception e) { KnifeLog.Warning("[GUN_TREE] tree registry file not written (the world's own copy stands): " + e.Message); }
        }, TaskScheduler.Default);
    }
    public static void Flush() {
        Task pending; lock (s_queue) pending = s_writes;
        try { pending.Wait(); } catch (Exception) { }
    }

    // ---- loading a world (ProjectXmlLoad, both CS hooks; the second call is a no-op) ----

    public static void BeforeLoad(XElement project, WorldInfo world) {
        if (!Enabled || string.IsNullOrWhiteSpace(world?.DirectoryName) || project.Element("Subsystems") is null) return;
        if (s_done.TryGetValue(project, out _)) return;
        string dir = Canonical(world.DirectoryName), root = RootOf(dir);
        Flush();
        var problems = new List<string>();
        Snapshot tree = s_trees.TryGetValue(root, out var live) && live.Primary is { } primary
            ? new(live.Id, live.Generation, Group(live.Registry, live.Ledger, primary.Now), "memory", new XElement(live.Worlds))
            : Read(SystemFile(root), problems);
        if (problems.Count > 0) KnifeLog.Warning("[GUN_TREE] " + string.Join("; ", problems));
        // With a tree, only the saves of worlds it has never heard of are read (a late merge at the root's load).
        var known = tree?.Worlds;
        Apply(project, root, RelativeTo(root, dir), tree, () => Saves(root, dir, known is null ? null : rel => Entry(known, rel, false) is null),
            () => ScGunRecordMirror.ReassignFromCopy(project, dir), () => ScGunRecordMirror.RestoreLost(project, dir));
        s_done.Add(project, root);
    }

    /// <summary>The decision, on detached XML: puts the tree's registry into this world's gun group, after merging this
    /// world's own registry into it when it has one (phase B), founds the tree, or refuses what cannot be merged safely.
    /// <paramref name="others"/> reads the other saves of the tree (asked only when no tree exists yet).
    /// <paramref name="reassignFromCopy"/> gives back CS block numbers from this world's own copy (true when that copy
    /// exists); <paramref name="restoreLost"/> is the 1.5.1 restore from it.</summary>
    internal static void Apply(XElement project, string root, string path, Snapshot tree, Func<IReadOnlyList<(string Path, XElement Project)>> others,
        Func<bool> reassignFromCopy, Action restoreLost) {
        var subs = project.Element("Subsystems");
        var gun = G(subs, GunGroup);
        if (Text(gun, ScGunSaveGuard.ErrorKey) is not null) return; // a refusal stands
        bool alreadyMerged = false;
        if (tree is not null) {
            // A merge left this world a remap (by number and model): applied to whatever its save holds now.
            if (PendingOf(tree.Worlds, path) is { } pending) {
                var own = ScGunTreeMerge.Read(path, project);
                int moved = ScGunTreeMerge.Apply(project, pending, own.VariantOf);
                KnifeLog.Warning($"[GUN_TREE] {Label(path)}: {moved} item(s) moved to their numbers in the shared registry");
                if (moved > 0) ScGunRecordMirror.Notice("CS：这个世界的枪已改到主世界共用记录本里的编号。");
            }
            alreadyMerged = MergedOf(tree.Worlds, path);
            tree = tree with { Worlds = Settled(tree.Worlds, path) };
        }
        if (ScGunRecordMirror.Lost(gun)) {
            // A save without CS dropped the group (or the world is new). Block numbers taken meanwhile are given back
            // from this world's own copy; the records come from the tree.
            if (tree is not null) {
                bool hadRecords = reassignFromCopy();
                Inject(subs, tree);
                if (hadRecords) {
                    KnifeLog.Warning($"[GUN_MIRROR] this world was saved without the CS mod and had lost its gun records; restored {G(G(G(subs, GunGroup), RegistryKey), "Records")?.Elements().Count() ?? 0} records from the tree registry ({tree.Source})");
                    ScGunRecordMirror.Notice("CS：这个世界曾在未启用 CS 时存档，枪械记录已从主世界的共用记录本恢复。");
                }
                s_prepared[root] = tree;
                return;
            }
            // No tree yet: the 1.5.1 path (this world's own copy) may still bring the group back.
            restoreLost();
            gun = G(subs, GunGroup);
            if (ScGunRecordMirror.Lost(gun)) { Found(project, root, path, others); return; }
        }
        string stamp = Text(gun, TreeIdKey);
        if (stamp is not null) {
            if (tree is null) { s_prepared[root] = new(stamp, int.TryParse(Text(gun, GenerationKey), out int g) ? g : 0, null, "world copy"); return; } // the file is gone: this copy re-founds it
            if (stamp != tree.Id) { Finish(project, root, path, JoinWorld(project, path, tree, null, foreign: true), others); return; } // e.g. a sub-world copied from another save
            // A save whose tree-file write never finished (the game closed at once) leaves the world's own copy newer
            // than the file: that copy wins, never the older file.
            int own = int.TryParse(Text(gun, GenerationKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out int og) ? og : 0;
            if (own > tree.Generation) {
                KnifeLog.Warning($"[GUN_TREE] {root}: this world's copy (generation {own}) is newer than the tree registry ({tree.Generation}, {tree.Source}); the world's copy is used");
                var copy = new XElement("Values", new XAttribute("Name", GunGroup), gun.Elements().Where(e => (string)e.Attribute("Name") is RegistryKey or ScGunTravel.Identities or LayoutKey).Select(e => new XElement(e)));
                s_prepared[root] = new(stamp, own, copy, "world copy", tree.Worlds);
                return;
            }
            Finish(project, root, path, tree, others);
            return;
        }
        // Unstamped: last saved by a build before 1.5.2 (never joined, or an older build played it after it joined).
        if (tree is not null) {
            if (!HasRecords(gun) || alreadyMerged) { Finish(project, root, path, tree, others); return; }
            Finish(project, root, path, JoinWorld(project, path, tree, ReceiptOf(tree.Worlds, path), foreign: false), others);
            return;
        }
        Found(project, root, path, others);
    }
    /// <summary>One world's own registry into the tree's, on its detached XML: by receipt (an older build played it since
    /// this build last saved it) or by identity (never merged yet, merged late, or carrying another tree's stamp).</summary>
    static Snapshot JoinWorld(XElement project, string path, Snapshot tree, Dictionary<int, string> receipt, bool foreign) {
        var w = ScGunTreeMerge.Read(path, project);
        var table = ScGunTreeMerge.Table.Open(tree.Gun, null, [w], RootSeed(tree.Worlds));
        var map = receipt is not null ? ScGunTreeMerge.Reconcile(table, w, receipt) : ScGunTreeMerge.Join(table, w, []);
        int moved = ScGunTreeMerge.Apply(project, map, w.VariantOf);
        tree = new Snapshot(tree.Id, tree.Generation, table.Gun, receipt is not null ? "reconciled" : foreign ? "imported" : "joined", tree.Worlds);
        KnifeLog.Warning($"[GUN_TREE] {Label(path)}: " + (receipt is not null ? "played by an older build since its last save here; its changes"
                : foreign ? "carried the registry of another world tree; its records were imported by identity and" : "its own registry") +
            $" merged into the shared registry; {moved} item(s) renumbered, {table.Joined} new record(s), {table.KeptAside} record(s) kept aside" + Obligations(table) +
            (table.Notes.Count > 0 ? "; " + string.Join("; ", table.Notes) : ""));
        ScGunRecordMirror.Notice(receipt is not null ? "CS：这个世界被旧版本玩过，枪械记录已和主世界的共用记录本对好。"
            : foreign ? "CS：这个世界的枪械记录来自另一个存档，已按记录身份并入主世界的共用记录本。" : "CS：这个世界的枪械记录已合并进主世界的共用记录本。");
        return tree;
    }
    /// <summary>The tree's registry into this world's group. When the root loads, every other world of the tree that never
    /// joined it - no merge, remap or receipt for it in the tree file (a Portal space whose own registry an older build made
    /// after the merge, its player's items saved in the root with that space's numbers) - is merged first, by identity and
    /// with decision 2A, so the root's items and that world's are placed before anything is played.</summary>
    static void Finish(XElement project, string root, string path, Snapshot tree, Func<IReadOnlyList<(string Path, XElement Project)>> others) {
        if (path.Length == 0 && tree.Gun is not null && others is not null) tree = MergeLateWorlds(project, path, tree, others());
        Inject(project.Element("Subsystems"), tree); s_prepared[root] = tree;
    }
    static Snapshot MergeLateWorlds(XElement project, string path, Snapshot tree, IReadOnlyList<(string Path, XElement Project)> saves) {
        var late = saves.Select(x => ScGunTreeMerge.Read(x.Path, x.Project)).Where(w => w.HasRecords && !w.Stamped && Entry(tree.Worlds, w.Path, false) is null).ToList();
        if (late.Count == 0) return tree;
        var self = ScGunTreeMerge.Read(path, project);
        var table = ScGunTreeMerge.Table.Open(tree.Gun, null, late.Append(self), RootSeed(tree.Worlds));
        var maps = ScGunTreeMerge.MergeInto(table, self, late);
        int moved = ScGunTreeMerge.Apply(project, maps[path], self.VariantOf);
        var worlds = new XElement(tree.Worlds ?? new XElement("Worlds"));
        foreach (var w in late) {
            var e = Entry(worlds, w.Path, true); e.SetAttributeValue("Merged", "1");
            if (maps[w.Path].Moves) e.SetAttributeValue("Pending", maps[w.Path].Save());
        }
        KnifeLog.Warning($"[GUN_TREE] {Label(path)}: {late.Count} world(s) of this tree had registries of their own it never took in ({string.Join(", ", late.Select(w => w.Path))}); merged now: " +
            $"{table.Joined} new record(s), {table.KeptAside} kept aside, {table.Corrected} misnumbered gun(s) corrected{Obligations(table)}; {moved} item(s) renumbered here" +
            (table.Notes.Count > 0 ? "; " + string.Join("; ", table.Notes) : ""));
        ScGunRecordMirror.Notice("CS：这个主世界下其他世界的枪械记录已并入共用记录本" + (table.Corrected > 0 ? $"，并按记录身份改回了 {table.Corrected} 把编号错位的枪。" : "。"));
        return new Snapshot(tree.Id, tree.Generation, table.Gun, "late-merged", worlds);
    }
    static string Obligations(ScGunTreeMerge.Table t) => t.KillsMoved + t.RefundsMoved == 0 ? "" : $", {t.KillsMoved} pending kill credit(s) and {t.RefundsMoved} refund(s) moved with their records";
    /// <summary>No tree yet. Alone in its tree (or the only one with records), this world founds it; otherwise every world
    /// of the tree is merged once, the root's numbers kept (design §4), with decision 2A.</summary>
    static void Found(XElement project, string root, string path, Func<IReadOnlyList<(string Path, XElement Project)>> others) {
        var self = ScGunTreeMerge.Read(path, project);
        var saves = others().Select(s => ScGunTreeMerge.Read(s.Path, s.Project)).ToList();
        if (!saves.Any(w => w.HasRecords && !w.Stamped)) { s_prepared[root] = new(Guid.NewGuid().ToString("N"), 0, null, "new"); return; }
        var rootWorld = path.Length == 0 ? self : saves.FirstOrDefault(w => w.Path.Length == 0) ?? self;
        var rest = saves.Where(w => w != rootWorld).ToList(); if (self != rootWorld) rest.Add(self);
        var plan = ScGunTreeMerge.Merge(rootWorld, rest);
        int moved = ScGunTreeMerge.Apply(project, plan.Maps[path], self.VariantOf);
        var worlds = new XElement("Worlds");
        KeepRootSeed(worlds, rootWorld.Seed);
        foreach (var w in rest.Append(rootWorld).Where(w => w != self)) {
            var map = plan.Maps[w.Path];
            if (map.Moves) Entry(worlds, w.Path, true).SetAttributeValue("Pending", map.Save());
            if (w.HasRecords && !w.Stamped) Entry(worlds, w.Path, true).SetAttributeValue("Merged", "1");
        }
        var snapshot = new Snapshot(Guid.NewGuid().ToString("N"), 0, plan.Gun, "merged", worlds);
        int merged = rest.Count(w => w.HasRecords && !w.Stamped);
        KnifeLog.Warning($"[GUN_TREE] {root}: merged the registries of {merged + 1} worlds into one shared registry ({plan.Table.Joined} new record(s), " +
            $"{plan.Table.KeptAside} kept aside, {plan.Table.Corrected} misnumbered gun(s) corrected{Obligations(plan.Table)}); {Label(path)}: {moved} item(s) renumbered now" +
            (plan.Table.Notes.Count > 0 ? "; " + string.Join("; ", plan.Table.Notes) : ""));
        ScGunRecordMirror.Notice("CS：已把这个主世界下各个世界的枪械记录合并成一本共用记录本" + (plan.Table.Corrected > 0 ? $"，并按记录身份改回了 {plan.Table.Corrected} 把编号错位的枪。" : "。"));
        Inject(project.Element("Subsystems"), snapshot);
        s_prepared[root] = snapshot;
    }
    static bool HasRecords(XElement gun) => G(G(gun, RegistryKey), "Records")?.HasElements == true;
    /// <summary>The tree's registry in place of whatever registry the group holds; the world's own fields stay.</summary>
    internal static void Inject(XElement subs, Snapshot tree) {
        var gun = G(subs, GunGroup);
        if (gun is null) { gun = new XElement("Values", new XAttribute("Name", GunGroup)); subs.Add(gun); }
        foreach (var name in new[] { RegistryKey, ScGunTravel.Identities }) G(gun, name)?.Remove();
        gun.Elements("Value").Where(e => (string)e.Attribute("Name") is LayoutKey or TreeIdKey or GenerationKey).Remove();
        foreach (var element in tree.Gun.Elements()) gun.Add(new XElement(element));
        gun.Add(Field(TreeIdKey, tree.Id), Field(GenerationKey, tree.Generation));
    }
    /// <summary>The other saves of the tree (Project.xml of the root and of Ghoul/Ancient worlds, SpaceProject.xml of
    /// Portal spaces), as detached XML with their path relative to the root. Read only when no tree exists yet.</summary>
    static IReadOnlyList<(string Path, XElement Project)> Saves(string root, string dir, Func<string, bool> wanted = null) {
        var found = new List<(string, XElement)>();
        string top = Storage.GetSystemPath(root), own = Path.GetFullPath(Storage.GetSystemPath(dir));
        if (!Directory.Exists(top)) return found;
        foreach (string file in Directory.EnumerateFiles(top, "*.xml", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4 })) {
            string name = Path.GetFileName(file);
            if (name is not ("Project.xml" or "SpaceProject.xml")) continue;
            string folder = Path.GetFullPath(Path.GetDirectoryName(file)!);
            if (string.Equals(folder, own, StringComparison.OrdinalIgnoreCase)) continue;
            try {
                string rel = Path.GetRelativePath(Path.GetFullPath(top), folder).Replace('\\', '/'); rel = rel == "." ? "" : rel;
                if (wanted is not null && !wanted(rel)) continue;
                found.Add((rel, XElement.Load(file)));
            }
            catch (Exception e) { KnifeLog.Warning($"[GUN_TREE] could not read {file}: {e.Message}"); }
        }
        return found;
    }

    // ---- the loaded tree (subsystem Load / Save / Dispose) ----

    /// <summary>The shared tree of a world being loaded. The first project of a tree brings its parsed registry and
    /// ledger; every later one (a Portal space project, a sub-world loaded while the root is) gets the same instances.</summary>
    internal static Tree Acquire(SubsystemScGunBlockBehavior member, string directory, ValuesDictionary values, ScGunRegistry registry, ScTravelLedger ledger,
        Func<ValuesDictionary, (ScGunRegistry Registry, ScTravelLedger Ledger)> build) {
        string root = RootOf(directory), path = RelativeTo(root, directory);
        string stamp = values.GetValue<string>(TreeIdKey, null);
        int staged = values.GetValue<ValuesDictionary>(RegistryKey, null)?.GetValue<int>("Next", 0) ?? 0;
        var ownRegistry = registry; string ownSeed = ledger?.WorldIdentity;
        // A project built without the ProjectXmlLoad hook (a Portal space) may still hold its own registry. When the tree's
        // merge already took it in, its items move by that merge's remap on its first update; otherwise (it appeared after
        // the merge, or it carries another tree's stamp) it is merged now, by identity, into the live shared registry.
        // ownIsTree: this project's own registry opened the tree (it was founded from it), so there is nothing to merge.
        void Join(Tree t, bool ownIsTree = false) {
            bool own = !ownIsTree && stamp != t.Id && ownRegistry.Count + ownRegistry.QuarantinedCount + ownRegistry.Kills.Count + ownRegistry.Recovery.Count > 0;
            bool foreign = stamp is not null && stamp != t.Id;
            var pending = foreign ? null : PendingOf(t.Worlds, path);
            Func<int, int> ownVariant = id => ownRegistry.TryGetSnapshot(id, out var s) ? s.Variant : -1;
            if (own && (foreign || pending is null && !MergedOf(t.Worlds, path))) {
                var map = LateMerge(t, path, values, member.Now, foreign);
                t.Worlds = Settled(t.Worlds, path);
                if (map.Moves) member.ScheduleRemap(map, ownVariant);
            }
            else if (pending is not null) member.ScheduleRemap(pending, ownVariant);
            else if (own) t.Worlds = Settled(t.Worlds, path);
            if (path.Length == 0) KeepRootSeed(t.Worlds, ownSeed);
            t.Paths[member] = path;
        }
        if (s_trees.TryGetValue(root, out var tree)) {
            Join(tree);
            tree.Members.Add(member);
            tree.Registry.ReserveThrough(staged); // the integrity check's watermark for this world's orphaned numbers
            return tree;
        }
        s_prepared.Remove(root, out var prepared);
        int own = stamp is not null ? values.GetValue<int>(GenerationKey, 0) : -1;
        // Another tree's stamp (a copied sub-world) opens from the snapshot too; its own registry is merged in by Join.
        bool fromSnapshot = prepared?.Gun is not null && (stamp is null || stamp != prepared.Id || prepared.Generation >= own);
        if (fromSnapshot) {
            // The newest snapshot of the tree, not this project's own (possibly older) copy.
            var snapshot = new ValuesDictionary(); snapshot.ApplyOverrides(new XElement(prepared.Gun));
            (registry, ledger) = build(snapshot);
            if (registry.UnknownSchema) throw new InvalidOperationException("主世界共用枪械记录本的格式无法安全读取，已拒绝进入世界。");
        }
        string id = fromSnapshot ? prepared.Id : stamp ?? prepared?.Id ?? Guid.NewGuid().ToString("N");
        int generation = fromSnapshot ? prepared.Generation : stamp is not null ? Math.Max(own, prepared?.Generation ?? 0) : prepared?.Generation ?? 0;
        tree = new Tree { Root = root, Id = id, Generation = generation, Registry = registry, Ledger = ledger, Worlds = prepared?.Worlds is { } w ? new XElement(w) : new XElement("Worlds") };
        Join(tree, ownIsTree: !fromSnapshot);
        tree.Members.Add(member);
        s_trees[root] = tree;
        KnifeLog.Information($"[GUN_TREE] {root}: tree {id} generation {generation}, {registry.Count} records, opened by {Canonical(directory)}");
        return tree;
    }
    /// <summary>The gun block's number in the running game (a late merge reads the refunds of a project already built).
    /// Headless tests set it.</summary>
    internal static Func<int> GunBlock = () => BlocksManager.GetBlockIndex<ScGunBlock>(true);
    /// <summary>A project with records of its own that the tree's merge did not take in (a Portal space that appeared
    /// later, or one carrying another tree's stamp): its registry joins the live shared one by identity, with its
    /// obligations; returns how its items move (applied on its first update).</summary>
    static ScGunTreeMerge.Remap LateMerge(Tree t, string path, ValuesDictionary values, double now, bool foreign) {
        var group = new XElement("Values", new XAttribute("Name", GunGroup)); values.Save(group);
        var w = ScGunTreeMerge.Read(path, new XElement("Project", new XElement("Subsystems", group)));
        w.Block = GunBlock();
        var table = ScGunTreeMerge.Table.Open(Group(t.Registry, t.Ledger, now), null, [w], RootSeed(t.Worlds));
        // Numbers this project used without a record of them (its items may still carry them) are never handed out.
        table.Next = Math.Max(table.Next, ScGunEncoding.NextId(Math.Min(w.Registry.Next, GunSpec.LastId + 1)));
        var map = ScGunTreeMerge.Join(table, w, []);
        var merged = new ValuesDictionary(); merged.ApplyOverrides(new XElement(table.Gun));
        var registry = ScGunRegistry.Load(merged.GetValue<ValuesDictionary>(RegistryKey, null), now);
        if (registry.UnknownSchema) throw new InvalidOperationException("合并后的枪械记录本无法安全读取，已拒绝进入世界。");
        var added = t.Registry.Absorb(registry);
        foreach (var (token, id) in table.ByIdentity) t.Ledger?.Identities.TryAdd(id, token);
        KnifeLog.Warning($"[GUN_TREE] {Label(path)}: " + (foreign ? "carried the registry of another world tree; imported by identity" : "has a registry of its own the tree's merge did not take in; merged now")
            + $" into the live shared registry: {added.Records} record(s) added ({table.Joined} new, {table.KeptAside} kept aside), {added.Kills} pending kill credit(s) and {added.Refunds} refund(s) moved with their records; "
            + $"{map.Count(p => p.Key.Id != p.Value)} number(s) change for its items" + (table.Notes.Count > 0 ? "; " + string.Join("; ", table.Notes) : ""));
        ScGunRecordMirror.Notice(foreign ? "CS：这个世界的枪械记录来自另一个存档，已按记录身份并入主世界的共用记录本。" : "CS：这个世界的枪械记录已合并进主世界的共用记录本。");
        return map;
    }
    /// <summary>A refund's owner as the world <paramref name="project"/> saves it: qualified with that world's path in the
    /// tree unless it is the root's own or a player's.</summary>
    internal static string QualifyOwner(Tree tree, GameEntitySystem.Project project, string owner) {
        foreach (var (member, path) in tree.Paths) if (ReferenceEquals(member.Project, project)) return ScGunTreeMerge.OwnerIn(path, owner);
        return owner;
    }
    /// <summary>A world's gun subsystem joins its tree: its own saved values parsed the way the subsystem always did
    /// (registry, identities, receipts); the newest prepared snapshot instead when it opens the tree.</summary>
    internal static Tree AcquireFor(SubsystemScGunBlockBehavior member, string directory, ValuesDictionary values, double now, string worldIdentity, bool legacy) {
        var receipts = values.GetValue<ValuesDictionary>(ScTravelLedger.ReceiptsKey, null);
        (ScGunRegistry Registry, ScTravelLedger Ledger) Build(ValuesDictionary saved) {
            var registry = ScGunRegistry.Load(saved.GetValue<ValuesDictionary>(RegistryKey, null), now);
            registry.LegacyWorld = legacy;
            var ledger = new ScTravelLedger { WorldIdentity = worldIdentity };
            ledger.LoadIdentities(saved.GetValue<ValuesDictionary>(ScGunTravel.Identities, null));
            ledger.LoadReceipts(receipts);
            return (registry, ledger);
        }
        var own = Build(values);
        return Acquire(member, directory, values, own.Registry, own.Ledger, Build);
    }
    /// <summary>A member's save: the primary writes the tree file. Returns the generation this save is stamped with.</summary>
    internal static int Saved(Tree tree, SubsystemScGunBlockBehavior member, double now) {
        if (tree.Primary != member) return tree.Generation;
        tree.Generation++;
        var gun = Group(tree.Registry, tree.Ledger, now);
        // Receipts: what every loaded world of the tree is saved with now (the same registry), for the reconcile of a
        // world an older build plays later.
        string receipt = string.Join(";", G(G(gun, RegistryKey), "Records")?.Elements("Value")
            .Select(e => (string)e.Attribute("Name") + ":" + ScGunTreeMerge.Hash((string)e.Attribute("Value"))) ?? []);
        foreach (var (m, path) in tree.Paths) if (tree.Members.Contains(m)) Entry(tree.Worlds, path, true).SetAttributeValue("Receipt", receipt);
        Queue(SystemFile(tree.Root), Document(tree.Id, tree.Generation, gun, tree.Worlds));
        return tree.Generation;
    }
    /// <summary>A Portal space applied its remap on its first update: nothing pending for it any more.</summary>
    internal static void Applied(Tree tree, SubsystemScGunBlockBehavior member) {
        if (tree.Paths.TryGetValue(member, out var path)) tree.Worlds = Settled(tree.Worlds, path);
    }
    internal static void Release(Tree tree, SubsystemScGunBlockBehavior member) {
        tree.Members.Remove(member); tree.Paths.Remove(member);
        if (tree.Members.Count > 0) return;
        if (s_trees.TryGetValue(tree.Root, out var current) && current == tree) s_trees.Remove(tree.Root);
        Flush(); // the last world of the tree is going: its file is on disk before the next world (or the menu) comes
    }
    static ScGunTree() { AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush(); }
    internal static bool IsLoaded(Tree tree) => tree.Members.Count > 0;
    /// <summary>Every holder of the tree: each loaded project's inventories, pickables and projectiles, and the pending
    /// refunds of the shared registry once.</summary>
    internal static List<ScGunHolders.Holder> Holders(IEnumerable<IEnumerable<ScGunHolders.Holder>> projects, ScGunRegistry registry, int gunIndex) {
        var holders = projects.SelectMany(p => p).ToList();
        foreach (var batch in registry.Recovery.Batches) foreach (var step in batch.Steps) {
            if (step.Count <= 0 || Terrain.ExtractContents(step.Value) != gunIndex || !ScGunHolders.MatchesRecord(step.Value)) continue;
            int id = GunSpec.GetId(Terrain.ExtractData(step.Value));
            if (id >= GunSpec.FirstId && id <= GunSpec.LastId) holders.Add(new ScGunHolders.Holder(id, $"recovery:{batch.Id}", null, -1));
        }
        return holders;
    }
    /// <summary>A refund's destination: in the world its owner names (a qualified owner), in the root for the root's own
    /// owners, in any loaded world for a player. Nothing (kept for later) when that world is not loaded.</summary>
    internal static IInventory ResolveRecoveryOwner(Tree tree, string owner) {
        bool qualified = ScGunTreeMerge.SplitOwner(owner, out string path, out string local);
        bool anywhere = !qualified && owner is not null && owner.StartsWith("player/", StringComparison.Ordinal);
        foreach (var member in tree.Members) {
            if (member.Project is not { } project) continue;
            string at = tree.Paths.TryGetValue(member, out var p) ? p : "";
            if (!anywhere && !string.Equals(at, qualified ? path : "", StringComparison.OrdinalIgnoreCase)) continue;
            if (ScGunHolders.ResolveRecoveryOwner(project, qualified ? local : owner) is { } inventory) return inventory;
        }
        return null;
    }
    /// <summary>Test support: forget every loaded tree.</summary>
    internal static void ResetForTests() { s_trees.Clear(); s_prepared.Clear(); }
}
