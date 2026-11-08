using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using TemplatesDatabase;
namespace Game;

/// <summary>world-tree-gun-registry-20261107 phase B (design §4, §5, decision 2A). Pure: reads detached saves, returns the
/// merged tree registry and, per world, which item (number, model) moves to which tree number. Never guesses: a record it
/// cannot place is kept on a number nobody holds; an item it cannot place keeps its number and meets the integrity check.
/// - One-time merge of a tree whose worlds kept registries of their own (builds before 1.5.2): the root's numbers stay;
///   every other world's records join by identity (the token a record carries from the world it was made in). The same
///   join takes a world that carries another tree's stamp (a sub-world copied from another save) and a Portal space with
///   records of its own that appears after the tree's merge.
/// - Obligations travel with their records: a world's pending kill credits and refunds move into the tree under the
///   tree numbers its records got. One that cannot be placed uniquely (its record did not map, or the tree may already
///   hold the same credential) refuses the merge: an obligation is never dropped and never paid twice.
/// - Decision 2A: an item whose model differs from its own world's record but equals the same-number record of exactly
///   one sibling world, whose identity resolves to a tree record of that model no correctly held item holds, moves there.
/// - A world an older build played after the tree existed (unstamped again): its changes are taken by the receipt (what
///   this build last wrote into that world), independent allocations that collide move to new numbers, and a record
///   changed on both sides keeps the tree's state with the world's copy kept aside.</summary>
public static class ScGunTreeMerge {
    const string GunGroup = "ScGunBlockBehavior", RegistryKey = "GunRegistry";
    static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
    static XElement G(XElement p, string n) => p?.Elements("Values").SingleOrDefault(e => (string)e.Attribute("Name") == n);
    static string Text(XElement p, string n) => (string)p?.Elements("Value").SingleOrDefault(e => (string)e.Attribute("Name") == n)?.Attribute("Value");
    static XElement Field(string n, object v) => new("Value", new XAttribute("Name", n), new XAttribute("Type", v is int ? "int" : "string"), new XAttribute("Value", Convert.ToString(v, Ci)));
    public static string Hash(string row) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(row ?? "")))[..16];

    public readonly record struct Item(int Id, int Variant, bool Extended);
    /// <summary>(number, model) of an item in one world -> its tree number.</summary>
    public sealed class Remap : Dictionary<(int Id, int Variant), int> {
        public string Save() => string.Join(";", this.OrderBy(p => p.Key.Id).Select(p => string.Create(Ci, $"{p.Key.Id}/{p.Key.Variant}>{p.Value}")));
        public static Remap Load(string text) {
            var map = new Remap();
            foreach (string part in (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)) {
                var a = part.Split('>'); var k = a[0].Split('/');
                map[(int.Parse(k[0], Ci), int.Parse(k[1], Ci))] = int.Parse(a[1], Ci);
            }
            return map;
        }
        public bool Moves => this.Any(p => p.Key.Id != p.Value);
    }

    /// <summary>One world's save as the merge reads it.</summary>
    public sealed class World {
        public string Path; public XElement Project, Gun; public ScGunRegistry Registry;
        public readonly Dictionary<string, string> Rows = new(StringComparer.Ordinal);
        public readonly Dictionary<int, string> Identities = new();
        public string Seed; public int Block = -1; public bool Stamped;
        public readonly List<Item> Items = new();
        public bool HasRecords => Rows.Count > 0;
        public string IdentityOf(int id) => Identities.TryGetValue(id, out var t) ? t : string.IsNullOrEmpty(Seed) ? null : ScTravelLedger.Token(Seed, id);
        public int VariantOf(int id) => Registry.TryGetSnapshot(id, out var s) ? s.Variant : -1;
    }
    /// <summary>A world identity as identity tokens are made from it (a GUID in its "N" form).</summary>
    public static string Seed(string uuid) => Guid.TryParse(uuid, out var g) ? g.ToString("N") : uuid;
    public static World Read(string path, XElement project) {
        var w = new World { Path = path ?? "", Project = project };
        var subs = project?.Element("Subsystems");
        w.Gun = G(subs, GunGroup);
        w.Stamped = Text(w.Gun, ScGunTree.TreeIdKey) is not null;
        var values = new ValuesDictionary(); if (w.Gun is not null) values.ApplyOverrides(new XElement(w.Gun));
        w.Registry = ScGunRegistry.Load(values.GetValue<ValuesDictionary>(RegistryKey, null), 0);
        foreach (var r in G(G(w.Gun, RegistryKey), "Records")?.Elements("Value") ?? []) w.Rows[(string)r.Attribute("Name")] = (string)r.Attribute("Value");
        foreach (var r in G(w.Gun, ScGunTravel.Identities)?.Elements("Value") ?? [])
            if (int.TryParse((string)r.Attribute("Name"), NumberStyles.Integer, Ci, out int id)) w.Identities[id] = (string)r.Attribute("Value");
        w.Seed = Seed(Text(w.Gun, ScGunTravel.WorldIdentity));
        var maps = G(subs, "BlocksManager")?.Elements("Value").Where(e => (string)e.Attribute("Value") == "ScGunBlock").ToArray();
        if (maps?.Length == 1 && int.TryParse((string)maps[0].Attribute("Name"), NumberStyles.Integer, Ci, out int b)) w.Block = b;
        if (w.Block >= 0 && project is not null)
            foreach (var (field, _) in ScGunRecordMirror.Holders(project))
                if (int.TryParse((string)field.Attribute("Value"), NumberStyles.Integer, Ci, out int v) && Terrain.ExtractContents(v) == w.Block) {
                    int data = Terrain.ExtractData(v); int id = ScGunEncoding.Id(data);
                    if (!ScGunEncoding.ValidBits(data) || !ScGunEncoding.IsRecordId(id)) continue; // fresh templates hold no record
                    bool ext = ScGunEncoding.Extended(data);
                    w.Items.Add(new(id, ext ? w.VariantOf(id) : data & 63, ext));
                }
        return w;
    }

    /// <summary>The tree registry being built, as the gun group the tree saves (registry, identities, layout).</summary>
    public sealed class Table {
        public XElement Gun;
        public readonly Dictionary<string, int> ByIdentity = new(StringComparer.Ordinal);
        public readonly List<string> Notes = new();
        public int Next, KeptAside, Joined, Corrected, KillsMoved, RefundsMoved;
        XElement Registry => G(Gun, RegistryKey);
        XElement Records => G(Registry, "Records");
        XElement IdentityGroup { get { var g = G(Gun, ScGunTravel.Identities); if (g is null) { g = new XElement("Values", new XAttribute("Name", ScGunTravel.Identities)); Gun.Add(g); } return g; } }
        ScGunGrowthMode Mode => Enum.TryParse<ScGunGrowthMode>(Text(Registry, "GrowthMode"), out var m) ? m : ScGunGrowthMode.Unset;
        public string Row(int id) => Text(Records, id.ToString(Ci));
        public bool Has(int id) => Row(id) is not null;
        public int VariantOf(int id) => Row(id) is { } row && ScGunRegistry.TryReadRow(row, 0, Mode, out var r) ? r.Variant : -1;
        public IEnumerable<int> Ids => Records.Elements("Value").Select(e => int.TryParse((string)e.Attribute("Name"), NumberStyles.Integer, Ci, out int id) ? id : -1).Where(i => i > 0);
        public void Set(int id, string row) {
            Records.Elements("Value").Where(e => (string)e.Attribute("Name") == id.ToString(Ci)).Remove();
            Records.Add(Field(id.ToString(Ci), row));
            if (id >= Next) Next = ScGunEncoding.NextId(id + 1);
            Registry.Elements("Value").Where(e => (string)e.Attribute("Name") == "Next").Remove(); Registry.Add(Field("Next", Next));
        }
        public void Identify(int id, string token) {
            if (string.IsNullOrEmpty(token)) return;
            ByIdentity.TryAdd(token, id);
            var g = IdentityGroup; g.Elements("Value").Where(e => (string)e.Attribute("Name") == id.ToString(Ci)).Remove(); g.Add(Field(id.ToString(Ci), token));
        }
        public int Allocate(string row, string token) {
            int id = Next;
            if (id > GunSpec.LastId) throw new InvalidOperationException("共用记录本已满，无法合并");
            Set(id, row); Identify(id, token); return id;
        }
        public int Aside(string row) { KeptAside++; return Allocate(row, null); }
        // The tree's pending obligations, read from and written back into its registry group in the save format.
        ValuesDictionary Sub(string name) { if (G(Registry, name) is not { } e) return null; var d = new ValuesDictionary(); d.ApplyOverrides(new XElement(e)); return d; }
        void Put(string name, ValuesDictionary d) {
            G(Registry, name)?.Remove();
            var e = new XElement("Values", new XAttribute("Name", name)); d.Save(e); Registry.Add(e);
        }
        public ScGunKillQueue Kills() { var q = new ScGunKillQueue(); q.LoadInto(Sub("PendingKills")); return q; }
        public ScGunRecovery Recovery() => ScGunRecovery.Load(Sub("Recovery"));
        public void Save(ScGunKillQueue kills) => Put("PendingKills", kills.Save());
        public void Save(ScGunRecovery recovery) => Put("Recovery", recovery.Save());

        /// <summary>A table opened on a tree's (or the root's) gun group: its rows keep their numbers. A record without an
        /// explicit identity has the one its world gives it: <paramref name="root"/>'s, or <paramref name="rootSeed"/> (the
        /// root world's identity, kept in the tree file) when the table is the tree's own.</summary>
        public static Table Open(XElement gun, World root, IEnumerable<World> all, string rootSeed = null) {
            var t = new Table { Gun = gun is null ? EmptyGun() : StripWorldFields(new XElement(gun)) };
            if (G(t.Gun, RegistryKey) is null) t.Gun.Add(EmptyGun().Elements());
            int next = int.TryParse(Text(t.Registry, "Next"), NumberStyles.Integer, Ci, out int n) ? n : GunSpec.FirstId;
            // Never hand out a number an item anywhere in the tree already carries.
            int highest = Math.Max(t.Ids.DefaultIfEmpty(0).Max(), all.SelectMany(w => w.Items).Select(i => i.Id).DefaultIfEmpty(0).Max());
            t.Next = ScGunEncoding.NextId(Math.Max(next, highest + 1));
            foreach (var e in G(t.Gun, ScGunTravel.Identities)?.Elements("Value") ?? [])
                if (int.TryParse((string)e.Attribute("Name"), NumberStyles.Integer, Ci, out int id)) t.ByIdentity.TryAdd((string)e.Attribute("Value"), id);
            if (root is not null) foreach (int id in t.Ids.ToArray()) if (root.IdentityOf(id) is { } token && !t.ByIdentity.ContainsKey(token)) t.Identify(id, token);
            if (root is null && !string.IsNullOrEmpty(rootSeed)) {
                var explicitIds = (G(t.Gun, ScGunTravel.Identities)?.Elements("Value") ?? []).Select(e => (string)e.Attribute("Name")).ToHashSet();
                foreach (int id in t.Ids.ToArray()) if (!explicitIds.Contains(id.ToString(Ci)) && !t.ByIdentity.ContainsKey(ScTravelLedger.Token(rootSeed, id))) t.Identify(id, ScTravelLedger.Token(rootSeed, id));
            }
            return t;
        }
        static XElement EmptyGun() => new("Values", new XAttribute("Name", GunGroup), Field("GunDataLayout", GunSpec.DataLayout),
            new XElement("Values", new XAttribute("Name", RegistryKey), Field("Schema", ScGunRegistry.Schema), Field("Next", GunSpec.FirstId),
                Field("GrowthMode", ScGunGrowthMode.Unset.ToString()), new XElement("Values", new XAttribute("Name", "Records"))));
        /// <summary>Only the tree's part of a world's group: registry, identities, layout.</summary>
        static XElement StripWorldFields(XElement gun) {
            var keep = new XElement("Values", new XAttribute("Name", GunGroup));
            foreach (var e in gun.Elements()) if ((string)e.Attribute("Name") is RegistryKey or ScGunTravel.Identities or "GunDataLayout") keep.Add(new XElement(e));
            return keep;
        }
    }

    static void RequireReadable(World w) {
        if (w.Registry.UnknownSchema) throw new InvalidOperationException($"{Label(w)} 的枪械记录格式无法安全读取");
    }
    static string Label(World w) => w.Path.Length == 0 ? "主世界" : w.Path;

    // ---- obligations (kill credits not yet written into their record; refunds not yet delivered) ----
    /// <summary>A refund's destination saved by a world other than the root is resolved only in that world: its entity
    /// and subsystem ids mean nothing in another project of the tree. A player is the same player in every world.</summary>
    public const string WorldOwnerPrefix = "tree-world:";
    public static string OwnerIn(string path, string owner) =>
        string.IsNullOrEmpty(path) || owner is null || owner.StartsWith("player/", StringComparison.Ordinal) || owner.StartsWith(WorldOwnerPrefix, StringComparison.Ordinal)
            ? owner : WorldOwnerPrefix + path + "|" + owner;
    public static bool SplitOwner(string owner, out string path, out string local) {
        path = local = null;
        if (owner is null || !owner.StartsWith(WorldOwnerPrefix, StringComparison.Ordinal)) return false;
        int bar = owner.IndexOf('|', WorldOwnerPrefix.Length); if (bar < 0) return false;
        path = owner[WorldOwnerPrefix.Length..bar]; local = owner[(bar + 1)..]; return true;
    }
    /// <summary>A gun value in a refund at its tree number: true with the value unchanged for anything that is not one of
    /// this world's numbered guns, false when it is one the map does not place.</summary>
    static bool Place(World w, Remap map, int value, out int placed) {
        placed = value;
        if (value == 0 || w.Block < 0 || Terrain.ExtractContents(value) != w.Block) return true;
        int data = Terrain.ExtractData(value);
        if (!ScGunEncoding.ValidBits(data) || !ScGunEncoding.IsRecordId(ScGunEncoding.Id(data))) return true; // a fresh gun
        int id = ScGunEncoding.Id(data), variant = ScGunEncoding.Extended(data) ? w.VariantOf(id) : data & 63;
        if (variant < 0 || !map.ContainsKey((id, variant))) return false;
        placed = Moved(value, map, w.VariantOf) ?? value;
        return true;
    }
    static string Steps(IEnumerable<ScGunUndo> steps) => string.Join(",", steps.Select(x => string.Create(Ci, $"{x.Slot}/{x.Value}/{x.Count}/{(x.Creative ? 1 : 0)}/{x.ReplacedValue}")));
    /// <summary>A joining world's obligations into the table, at the numbers its records got. All or nothing.</summary>
    static void TakeObligations(Table t, World w, Remap map) {
        if (w.Registry.Kills.Count == 0 && w.Registry.Recovery.Count == 0) return;
        var kills = t.Kills(); var recovery = t.Recovery();
        var problems = new List<string>();
        var credits = new List<(int Id, int Variant)>();
        foreach (var e in w.Registry.Kills.Pending) {
            if (!map.TryGetValue((e.RecordId, e.Variant), out int to) || t.VariantOf(to) != e.Variant) { problems.Add($"击杀 #{e.RecordId} 的记录没有唯一对应"); continue; }
            if (kills.Pending.Any(k => k.RecordId == to && k.Variant == e.Variant && k.EventId == e.EventId)) { problems.Add($"击杀 #{e.RecordId} 可能已在共用记录本里"); continue; }
            credits.Add((to, e.Variant));
        }
        var refunds = new List<(string Owner, List<ScGunUndo> Steps)>();
        foreach (var batch in w.Registry.Recovery.Batches) {
            if (batch.Owner.StartsWith(WorldOwnerPrefix, StringComparison.Ordinal)) { problems.Add($"补偿 {batch.Id} 属于另一棵世界树的子世界"); continue; }
            var steps = batch.Steps.Select(x => x.Copy()).ToList(); bool ok = true;
            foreach (var x in steps) {
                ok &= Place(w, map, x.Value, out int value); x.Value = value;
                if (x.Creative) { ok &= Place(w, map, x.ReplacedValue, out int replaced); x.ReplacedValue = replaced; }
            }
            if (!ok) { problems.Add($"补偿 {batch.Id} 里的枪没有唯一对应的记录"); continue; }
            string owner = OwnerIn(w.Path, batch.Owner);
            if (recovery.Batches.Any(b => b.Owner == owner && Steps(b.Steps) == Steps(steps))) { problems.Add($"补偿 {batch.Id} 可能已在共用记录本里"); continue; }
            refunds.Add((owner, steps));
        }
        if (problems.Count > 0)
            throw new InvalidOperationException($"{Label(w)} 的待结算项无法唯一对应到共用记录本（{string.Join("；", problems)}）；请先用当时的版本进入该世界结算后再换版本");
        foreach (var (id, variant) in credits) kills.Enqueue(id, variant);
        foreach (var (owner, steps) in refunds) recovery.Adopt(owner, steps);
        t.Save(kills); t.Save(recovery);
        t.KillsMoved += credits.Count; t.RefundsMoved += refunds.Count;
    }
    /// <summary>A world an older build played after it joined starts from the tree's obligations (they were saved into it).
    /// Unless both sides still hold exactly the same ones, which of them the older build delivered cannot be told.</summary>
    static void RequireSameObligations(Table t, World w) {
        static string K(ScGunKillQueue q) => string.Join(";", q.Pending.Select(e => string.Create(Ci, $"{e.EventId}:{e.RecordId},{e.Variant}")));
        static string R(ScGunRecovery r) => string.Join(";", r.Batches.Select(b => string.Create(Ci, $"{b.Id}|{b.Owner}|") + Steps(b.Steps)));
        var kills = t.Kills(); var recovery = t.Recovery();
        if (w.Registry.Kills.Count + w.Registry.Recovery.Count + kills.Count + recovery.Count == 0) return;
        if (K(w.Registry.Kills) == K(kills) && R(w.Registry.Recovery) == R(recovery)) return;
        throw new InvalidOperationException($"{Label(w)} 被旧版本玩过，而它或共用记录本里有待结算的击杀或补偿，无法确定哪些已经结算；请先用旧版本进入该世界结算后再换版本");
    }

    /// <summary>One world's own registry into the table by identity. Returns (number, model) -> tree number for its items.</summary>
    public static Remap Join(Table t, World w, HashSet<int> held) {
        RequireReadable(w);
        var map = new Remap();
        foreach (var (key, row) in w.Rows.OrderBy(p => int.TryParse(p.Key, out int k) ? k : int.MaxValue)) {
            if (!int.TryParse(key, NumberStyles.Integer, Ci, out int id) || !w.Registry.TryGetSnapshot(id, out var s)) {
                t.Aside(row); t.Notes.Add($"{Label(w)} 的异常记录 {key} 原样保留在新编号上");
                continue;
            }
            string token = w.IdentityOf(id);
            bool holds = w.Items.Any(i => i.Id == id && i.Variant == s.Variant);
            if (token is not null && t.ByIdentity.TryGetValue(token, out int tid) && t.VariantOf(tid) == s.Variant) {
                if (t.Row(tid) != row) {
                    if (holds && !held.Contains(tid)) t.Set(tid, row); // the copy someone holds is the current one
                    else t.Aside(row);
                }
                map[(id, s.Variant)] = tid;
                if (holds) held.Add(tid);
                continue;
            }
            if (token is not null && t.ByIdentity.ContainsKey(token)) t.Notes.Add($"{Label(w)} #{id}：身份相同但型号不同，按新枪处理");
            int n = t.Allocate(row, token); t.Joined++;
            map[(id, s.Variant)] = n;
            if (holds) held.Add(n);
        }
        TakeObligations(t, w, map);
        return map;
    }

    /// <summary>The one-time merge of a tree whose worlds kept their own registries.</summary>
    public sealed record Plan(XElement Gun, Dictionary<string, Remap> Maps, Table Table);
    public static Plan Merge(World root, IReadOnlyList<World> others) {
        var all = new List<World> { root }; all.AddRange(others.OrderBy(w => w.Path, StringComparer.Ordinal));
        RequireReadable(root); // the root's own obligations stay in its table under their numbers
        var t = Table.Open(root.Gun, root, all);
        var maps = all.ToDictionary(w => w.Path, _ => new Remap(), StringComparer.Ordinal);
        // The root keeps its numbers; its correctly held records are taken.
        var held = new HashSet<int>(root.Items.Where(i => t.VariantOf(i.Id) == i.Variant).Select(i => i.Id));
        foreach (var w in all.Skip(1).Where(w => w.HasRecords && !w.Stamped)) {
            var joined = Join(t, w, held);
            foreach (var p in joined) maps[w.Path][p.Key] = p.Value;
        }
        Correct(t, root, all, maps);
        return new(t.Gun, maps, t);
    }
    /// <summary>Worlds of a tree that never joined it, merged into the tree's table when the root loads: by identity, with
    /// decision 2A for the root's items and theirs. Returns each world's remap (the root's under its path).</summary>
    public static Dictionary<string, Remap> MergeInto(Table t, World root, IReadOnlyList<World> joining) {
        var all = new List<World> { root }; all.AddRange(joining.OrderBy(w => w.Path, StringComparer.Ordinal));
        var maps = all.ToDictionary(w => w.Path, _ => new Remap(), StringComparer.Ordinal);
        var held = new HashSet<int>(root.Items.Where(i => t.VariantOf(i.Id) == i.Variant).Select(i => i.Id));
        foreach (var w in all.Skip(1)) foreach (var p in Join(t, w, held)) maps[w.Path][p.Key] = p.Value;
        Correct(t, root, all, maps);
        return maps;
    }
    /// <summary>The tree number an item of a world means now: its world's map, else the number itself.</summary>
    static int Final(World w, Item i, Dictionary<string, Remap> maps) => maps[w.Path].TryGetValue((i.Id, i.Variant), out int n) ? n : i.Id;

    /// <summary>Decision 2A.</summary>
    static void Correct(Table t, World root, List<World> all, Dictionary<string, Remap> maps) {
        var sources = all.Where(w => w == root || w.HasRecords && !w.Stamped).ToList();
        var proposals = new List<(World W, Item I, int Target)>();
        foreach (var w in all) foreach (var i in w.Items.Where(i => !i.Extended).Distinct()) {
            if (t.VariantOf(Final(w, i, maps)) == i.Variant) continue;
            var targets = sources.Where(s => s != w && s.VariantOf(i.Id) == i.Variant)
                .Select(s => s == root ? i.Id : maps[s.Path].TryGetValue((i.Id, i.Variant), out int n) ? n : -1)
                .Where(n => n > 0 && t.VariantOf(n) == i.Variant).Distinct().ToList();
            if (targets.Count == 1) proposals.Add((w, i, targets[0]));
            else t.Notes.Add($"{Label(w)} 的 #{i.Id}（{ScGunNames.Variant(i.Variant)}）对不上记录，" + (targets.Count == 0 ? "没有可证明的原记录" : "有多条可能的原记录") + "，保持原样");
        }
        var moving = proposals.Select(p => (p.W, p.I)).ToHashSet();
        foreach (var group in proposals.GroupBy(p => p.Target)) {
            int target = group.Key;
            bool heldElsewhere = all.Any(w => w.Items.Any(i => !moving.Contains((w, i)) && Final(w, i, maps) == target && t.VariantOf(target) == i.Variant));
            bool oneItem = group.Select(p => p.I).Distinct().Count() == 1; // copies of one item in several worlds
            if (heldElsewhere || !oneItem) { t.Notes.Add($"#{target} 有多个持有者，相关错位物品保持原样"); continue; }
            foreach (var p in group) { maps[p.W.Path][(p.I.Id, p.I.Variant)] = target; }
            t.Corrected++;
        }
    }

    /// <summary>A world an older build played after the tree existed, against what this build last wrote into it.</summary>
    public static Remap Reconcile(Table t, World w, IReadOnlyDictionary<int, string> receipt) {
        RequireReadable(w); RequireSameObligations(t, w);
        var map = new Remap();
        foreach (var (key, row) in w.Rows.OrderBy(p => int.TryParse(p.Key, out int k) ? k : int.MaxValue)) {
            if (!int.TryParse(key, NumberStyles.Integer, Ci, out int id) || !w.Registry.TryGetSnapshot(id, out var s)) {
                if (!int.TryParse(key, NumberStyles.Integer, Ci, out int bad) || t.Row(bad) != row) t.Aside(row);
                continue;
            }
            string tree = t.Row(id);
            if (tree is null) { t.Set(id, row); t.Identify(id, w.IdentityOf(id)); map[(id, s.Variant)] = id; continue; } // made by the older build, free here
            if (tree == row) { map[(id, s.Variant)] = id; continue; }
            if (receipt.TryGetValue(id, out string before)) {
                if (Hash(row) == before) { map[(id, s.Variant)] = id; continue; }                 // only the tree changed it
                if (Hash(tree) == before && t.VariantOf(id) == s.Variant) { t.Set(id, row); map[(id, s.Variant)] = id; continue; } // only this world did
                t.Aside(row); t.Notes.Add($"{Label(w)} #{id} 两边都改过，保留共用记录本里的，另一份另存"); map[(id, s.Variant)] = id; continue;
            }
            // Not written here before: the older build in this world and this tree each allocated this number.
            map[(id, s.Variant)] = t.Allocate(row, w.IdentityOf(id)); t.Notes.Add($"{Label(w)} #{id} 与共用记录本里的同号记录是两把枪，改到 #{map[(id, s.Variant)]}");
        }
        return map;
    }

    /// <summary>A world's items moved to their tree numbers, on its detached XML. Returns how many moved.</summary>
    public static int Apply(XElement project, Remap map, Func<int, int> extendedVariant = null) {
        if (map is null || map.Count == 0) return 0;
        int block = -1;
        var entries = G(project.Element("Subsystems"), "BlocksManager")?.Elements("Value").Where(e => (string)e.Attribute("Value") == "ScGunBlock").ToArray();
        if (entries?.Length == 1) int.TryParse((string)entries[0].Attribute("Name"), NumberStyles.Integer, Ci, out block);
        if (block < 0) return 0;
        int moved = 0;
        foreach (var (field, _) in ScGunRecordMirror.Holders(project)) {
            if (!int.TryParse((string)field.Attribute("Value"), NumberStyles.Integer, Ci, out int v) || Terrain.ExtractContents(v) != block) continue;
            if (Moved(v, map, extendedVariant) is int next && next != v) { field.SetAttributeValue("Value", next.ToString(Ci)); moved++; }
        }
        return moved;
    }
    /// <summary>The item value at its tree number, or null when the map says nothing about it.</summary>
    public static int? Moved(int value, Remap map, Func<int, int> extendedVariant = null) {
        int data = Terrain.ExtractData(value); if (!ScGunEncoding.ValidBits(data)) return null;
        int id = ScGunEncoding.Id(data); if (!ScGunEncoding.IsRecordId(id)) return null;
        int variant = ScGunEncoding.Extended(data) ? extendedVariant?.Invoke(id) ?? -1 : data & 63;
        if (variant < 0 || !map.TryGetValue((id, variant), out int to) || to == id) return null;
        return Terrain.ReplaceData(value, ScGunEncoding.Encode(variant, to));
    }
}
