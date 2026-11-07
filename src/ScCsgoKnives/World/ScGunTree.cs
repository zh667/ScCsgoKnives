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

/// <summary>world-tree-gun-registry-20261107 (1.6.0, phase A; design docs/tasks/world-tree-gun-registry-20261107.md).
/// One gun registry for a whole world tree: the top-level world folder under the Worlds folder and every sub-world in
/// it (Ghoul <c>World1/Tartareosity</c>, Ancient World <c>World1/Dimensions/Ancient</c>, Portal <c>World1/Space_N</c>).
/// A record number then names the same gun in every world of the tree, however a sub-world mod moves the item: copying
/// the player XML, restoring item values after a load, or moving the player entity between two projects running at
/// once. No carried packets, no arrival renumbering inside a tree.
/// The authority is <see cref="FileName"/> in the tree's root folder (verified temporary file, previous one kept as
/// .bak, generation counter, tree GUID). In memory there is one shared instance per root, held by every loaded project
/// of the tree (reference counted). Every world still saves the full registry into its own Project.xml group, stamped
/// with the tree id and generation, for older builds and as the last fallback.
/// Phase A refuses, rather than guesses, a world whose own unstamped registry would have to be merged with another
/// one of the same tree; phase B adds that merge.</summary>
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
    }
    internal sealed record Snapshot(string Id, int Generation, XElement Gun, string Source);

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

    internal static XElement Document(string id, int generation, XElement gun) =>
        new(RootName, new XAttribute("Format", Format), new XAttribute("Id", id), new XAttribute("Generation", generation),
            new XAttribute("Build", ScCompatibility.ActiveBuild), gun);

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
                return new(id, generation, new XElement(groups[0]), Path.GetFileName(candidate));
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

    static InvalidOperationException Pending(string detail) => new(
        "这个世界和同一主世界下的其他世界各有一本枪械记录本，需要合并后才能安全加载（" + detail + "）。本开发版不做合并，已拒绝加载，原世界文件未改动。");

    public static void BeforeLoad(XElement project, WorldInfo world) {
        if (!Enabled || string.IsNullOrWhiteSpace(world?.DirectoryName) || project.Element("Subsystems") is null) return;
        if (s_done.TryGetValue(project, out _)) return;
        string dir = Canonical(world.DirectoryName), root = RootOf(dir);
        Flush();
        var problems = new List<string>();
        Snapshot tree = s_trees.TryGetValue(root, out var live) && live.Primary is { } primary
            ? new(live.Id, live.Generation, Group(live.Registry, live.Ledger, primary.Now), "memory")
            : Read(SystemFile(root), problems);
        if (problems.Count > 0) KnifeLog.Warning("[GUN_TREE] " + string.Join("; ", problems));
        Apply(project, root, tree, () => OtherRegistries(root, dir),
            () => ScGunRecordMirror.ReassignFromCopy(project, dir), () => ScGunRecordMirror.RestoreLost(project, dir));
        s_done.Add(project, root);
    }

    /// <summary>The decision, on detached XML: puts the tree's registry into this world's gun group, founds the tree,
    /// or refuses. <paramref name="others"/> lists the other worlds of the tree that hold an unstamped registry of their
    /// own (asked only when no tree exists yet). <paramref name="reassignFromCopy"/> gives back CS block numbers from
    /// this world's own copy (true when that copy exists); <paramref name="restoreLost"/> is the 1.5.1 restore from it.</summary>
    internal static void Apply(XElement project, string root, Snapshot tree, Func<IReadOnlyList<string>> others, Func<bool> reassignFromCopy, Action restoreLost) {
        var subs = project.Element("Subsystems");
        var gun = G(subs, GunGroup);
        if (Text(gun, ScGunSaveGuard.ErrorKey) is not null) return; // a refusal stands
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
            if (ScGunRecordMirror.Lost(gun)) { Found(root, others); return; }
        }
        string stamp = Text(gun, TreeIdKey);
        if (stamp is not null) {
            if (tree is null) { s_prepared[root] = new(stamp, int.TryParse(Text(gun, GenerationKey), out int g) ? g : 0, null, "world copy"); return; } // the file is gone: this copy re-founds it
            if (stamp != tree.Id) throw Pending("这个世界的记录本属于另一棵世界树");
            // A save whose tree-file write never finished (the game closed at once) leaves the world's own copy newer
            // than the file: that copy wins, never the older file.
            int own = int.TryParse(Text(gun, GenerationKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out int og) ? og : 0;
            if (own > tree.Generation) {
                KnifeLog.Warning($"[GUN_TREE] {root}: this world's copy (generation {own}) is newer than the tree registry ({tree.Generation}, {tree.Source}); the world's copy is used");
                var copy = new XElement("Values", new XAttribute("Name", GunGroup), gun.Elements().Where(e => (string)e.Attribute("Name") is RegistryKey or ScGunTravel.Identities or LayoutKey).Select(e => new XElement(e)));
                s_prepared[root] = new(stamp, own, copy, "world copy");
                return;
            }
            Inject(subs, tree); s_prepared[root] = tree;
            return;
        }
        // Unstamped: last saved by a build before 1.6.0.
        if (tree is not null) {
            if (HasRecords(gun)) throw Pending("所在主世界已有共用记录本");
            Inject(subs, tree); s_prepared[root] = tree;
            return;
        }
        Found(root, others);
    }
    static void Found(string root, Func<IReadOnlyList<string>> others) {
        var holders = others();
        if (holders.Count > 0) throw Pending("还有：" + string.Join("、", holders));
        s_prepared[root] = new(Guid.NewGuid().ToString("N"), 0, null, "new");
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
    /// <summary>Other worlds of the tree whose saves hold an unstamped registry with records (Project.xml of a
    /// Ghoul/Ancient world, SpaceProject.xml of a Portal space). Read only when no tree exists yet.</summary>
    static IReadOnlyList<string> OtherRegistries(string root, string dir) {
        var found = new List<string>();
        string top = Storage.GetSystemPath(root), own = Path.GetFullPath(Storage.GetSystemPath(dir));
        if (!Directory.Exists(top)) return found;
        foreach (string file in Directory.EnumerateFiles(top, "*.xml", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4 })) {
            string name = Path.GetFileName(file);
            if (name is not ("Project.xml" or "SpaceProject.xml")) continue;
            if (string.Equals(Path.GetFullPath(Path.GetDirectoryName(file)!), own, StringComparison.OrdinalIgnoreCase)) continue;
            try {
                var gun = G(XElement.Load(file).Element("Subsystems"), GunGroup);
                if (gun is not null && Text(gun, TreeIdKey) is null && HasRecords(gun)) found.Add(Path.GetRelativePath(top, Path.GetDirectoryName(file)!).Replace('\\', '/'));
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
        string root = RootOf(directory);
        string stamp = values.GetValue<string>(TreeIdKey, null);
        int staged = values.GetValue<ValuesDictionary>(RegistryKey, null)?.GetValue<int>("Next", 0) ?? 0;
        if (s_trees.TryGetValue(root, out var tree)) {
            // A project created without the ProjectXmlLoad hook (a Portal space) may still hold its own registry.
            if (stamp != tree.Id && registry.Count + registry.QuarantinedCount > 0) throw Pending(Canonical(directory) + " 有自己的记录本");
            tree.Members.Add(member);
            tree.Registry.ReserveThrough(staged); // the integrity check's watermark for this world's orphaned numbers
            return tree;
        }
        s_prepared.Remove(root, out var prepared);
        int own = stamp is not null ? values.GetValue<int>(GenerationKey, 0) : -1;
        if (prepared?.Gun is not null && (stamp is null || stamp == prepared.Id) && prepared.Generation >= own) {
            // The newest snapshot of the tree, not this project's own (possibly older) copy.
            var snapshot = new ValuesDictionary(); snapshot.ApplyOverrides(new XElement(prepared.Gun));
            (registry, ledger) = build(snapshot);
            if (registry.UnknownSchema) throw new InvalidOperationException("主世界共用枪械记录本的格式无法安全读取，已拒绝进入世界。");
        }
        else if (stamp is not null && prepared is not null && prepared.Id != stamp && prepared.Gun is not null) throw Pending(Canonical(directory) + " 的记录本属于另一棵世界树");
        string id = stamp ?? prepared?.Id ?? Guid.NewGuid().ToString("N");
        int generation = stamp is not null ? Math.Max(own, prepared?.Generation ?? 0) : prepared?.Generation ?? 0;
        tree = new Tree { Root = root, Id = id, Generation = generation, Registry = registry, Ledger = ledger };
        tree.Members.Add(member);
        s_trees[root] = tree;
        KnifeLog.Information($"[GUN_TREE] {root}: tree {id} generation {generation}, {registry.Count} records, opened by {Canonical(directory)}");
        return tree;
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
        Queue(SystemFile(tree.Root), Document(tree.Id, tree.Generation, Group(tree.Registry, tree.Ledger, now)));
        return tree.Generation;
    }
    internal static void Release(Tree tree, SubsystemScGunBlockBehavior member) {
        tree.Members.Remove(member);
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
    internal static IInventory ResolveRecoveryOwner(Tree tree, string owner) {
        foreach (var member in tree.Members)
            if (member.Project is { } project && ScGunHolders.ResolveRecoveryOwner(project, owner) is { } inventory) return inventory;
        return null;
    }
    /// <summary>Test support: forget every loaded tree.</summary>
    internal static void ResetForTests() { s_trees.Clear(); s_prepared.Clear(); }
}
