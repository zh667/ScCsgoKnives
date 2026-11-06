using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Engine;
using TemplatesDatabase;
namespace Game;

/// <summary>gun-record-mirror-20261106 (1.5.1): a world opened and saved without the CS mod keeps its gun records and,
/// where another mod took them meanwhile, the CS block numbers of its items.
/// The engine saves only the subsystems it has templates for, so such a save drops the ScGunBlockBehavior group from
/// Project.xml while the gun items and the BlocksManager names of their blocks stay; reinstalling CS then found guns
/// without records and refused the world. And the engine hands a new mod's block the lowest number with no loaded block,
/// without checking that an absent mod's saved name still holds it, so a block mod added while CS was absent can take
/// a CS number; the save then keeps only the newcomer's name for it.
/// Every save therefore also writes, to <see cref="FileName"/> in the world folder (the engine never touches unknown
/// files there; exporting, snapshotting and deleting a world take the whole folder): the gun group verbatim, the
/// number of every CS block, and where each CS item lies (inventory slot, pickable, projectile) with its exact value.
/// A load whose Project.xml has lost the group uses that file only when it belongs to this world (seed). A CS block
/// whose number was taken gets a free number, and only the recorded places that still hold exactly the recorded
/// value and count are moved to it; anything else at the old number is left to the mod that owns it now. The group
/// comes back only if the file holds a same-model record for every numbered gun the world holds; otherwise nothing is
/// restored and the existing refusal stands. Project.xml stays the only authority: the file is never read while the
/// group is present and never merged into it. Blocks placed in the terrain (weapon workbench, airdrop crate) are not
/// covered: they live in the terrain files.
/// User decision 2026-11-06, an explicit exception to "no automatic backups": one file per world, replaced at each
/// save (the previous one kept as .bak).</summary>
public static class ScGunRecordMirror {
    public const string FileName = "ScCsgoGunRecords.xml";
    public const int Format = 1;
    const string RootName = "ScGunRecordMirror", GunGroup = "ScGunBlockBehavior", RegistryKey = "GunRegistry";
    const int FirstDynamicBlock = 300, BlockCount = 1024;
    static readonly object s_write = new();
    static string s_notice;

    static XElement Group(XElement parent, string name) => parent?.Elements("Values").SingleOrDefault(e => (string)e.Attribute("Name") == name);
    static XElement Field(XElement parent, string name) => parent?.Elements("Value").SingleOrDefault(e => (string)e.Attribute("Name") == name);
    static string Text(XElement parent, string name) => (string)Field(parent, name)?.Attribute("Value");
    /// <summary>The world's saved block numbers: number -> block type name.</summary>
    static Dictionary<int, string> Numbers(XElement project) {
        var numbers = new Dictionary<int, string>();
        foreach (var entry in Group(project.Element("Subsystems"), "BlocksManager")?.Elements("Value") ?? [])
            if (int.TryParse((string)entry.Attribute("Name"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                numbers[index] = (string)entry.Attribute("Value");
        return numbers;
    }
    static int? GunBlock(XElement project) {
        var numbers = Numbers(project).Where(p => p.Value == "ScGunBlock").Select(p => p.Key).ToArray();
        return numbers.Length == 1 ? numbers[0] : null;
    }
    static string Seed(XElement project) => Text(Group(project.Element("Subsystems"), "GameInfo"), "WorldSeed");
    static int Records(XElement gun) => Group(Group(gun, RegistryKey), "Records")?.Elements().Count() ?? 0;

    /// <summary>Every place in Project.xml that holds an item value, with its count field when it has one: the same
    /// holders the integrity check reads.</summary>
    static IEnumerable<(XElement Value, XElement Count)> Holders(XElement project) {
        foreach (var slots in project.Descendants("Values").Where(e => (string)e.Attribute("Name") == "Slots"))
            foreach (var slot in slots.Elements("Values"))
                if (Field(slot, "Contents") is { } contents) yield return (contents, Field(slot, "Count"));
        var subsystems = project.Element("Subsystems");
        foreach (string name in new[] { "Pickables", "Projectiles" })
            foreach (var item in Group(Group(subsystems, name), name)?.Elements("Values") ?? [])
                if (Field(item, "Value") is { } value) yield return (value, Field(item, "Count"));
    }
    /// <summary>The path from the project root to a field: element name plus its Name and Id (entity ids are kept by
    /// the engine across saves; the template name is checked too).</summary>
    static IEnumerable<XElement> Steps(XElement field) {
        foreach (var element in field.AncestorsAndSelf().Reverse().Skip(1)) {
            var step = new XElement("Step", new XAttribute("E", element.Name.LocalName));
            foreach (string key in new[] { "Name", "Id" }) if (element.Attribute(key) is { } a) step.Add(new XAttribute(key, a.Value));
            yield return step;
        }
    }
    static XElement Resolve(XElement project, XElement item) {
        XElement at = project;
        foreach (var step in item.Elements("Step")) {
            string element = (string)step.Attribute("E"), name = (string)step.Attribute("Name"), id = (string)step.Attribute("Id");
            var matches = at.Elements(element).Where(e => (string)e.Attribute("Name") == name && (string)e.Attribute("Id") == id).Take(2).ToArray();
            if (matches.Length != 1) return null;
            at = matches[0];
        }
        return at;
    }

    /// <summary>A save by CS always holds the registry, so a group without it (or no group at all) was lost by a save
    /// without CS. A group carrying a refusal marker is a deliberate refusal and is never replaced.</summary>
    internal static bool Lost(XElement gun) => gun is null
        || Group(gun, RegistryKey) is null && Text(gun, "GunDataLayout") is null && Text(gun, ScGunSaveGuard.ErrorKey) is null;

    /// <summary>The block types of the CS family loaded now (core, agents, deathmatch and their stand-ins).</summary>
    static string[] FamilyBlocks() => BlocksManager.Blocks
        .Where(b => b is not null && b.GetType().Assembly.GetName().Name?.StartsWith("ScCsgo", StringComparison.Ordinal) == true)
        .Select(b => b.GetType().Name).Distinct().ToArray();

    /// <summary>The copy of a finished save, or null when that save holds no complete gun group.</summary>
    internal static XElement Document(XElement project, IEnumerable<string> familyBlocks) {
        var gun = Group(project.Element("Subsystems"), GunGroup);
        if (gun is null || Group(gun, RegistryKey) is null || Text(gun, ScGunSaveGuard.ErrorKey) is not null) return null;
        int? block = GunBlock(project); string seed = Seed(project);
        if (block is null || seed is null) return null;
        var family = familyBlocks.ToHashSet(StringComparer.Ordinal);
        var blocks = new XElement("Blocks"); var numbers = new HashSet<int>();
        foreach (var (index, name) in Numbers(project).OrderBy(p => p.Key))
            if (family.Contains(name)) { blocks.Add(new XElement("Block", new XAttribute("Name", name), new XAttribute("Index", index))); numbers.Add(index); }
        var items = new XElement("Items");
        foreach (var (value, count) in Holders(project)) {
            if (!int.TryParse((string)value.Attribute("Value"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) || !numbers.Contains(Terrain.ExtractContents(v))) continue;
            var item = new XElement("Item", new XAttribute("Value", v));
            if (count is not null) item.Add(new XAttribute("Count", (string)count.Attribute("Value")));
            item.Add(Steps(value));
            items.Add(item);
        }
        return new XElement(RootName, new XAttribute("Format", Format), new XAttribute("Block", block.Value),
            new XAttribute("Seed", seed), new XAttribute("Build", ScCompatibility.ActiveBuild), new XElement(gun), blocks, items);
    }

    /// <summary>OnProjectXmlSaved: the engine's saving thread, the complete XML, before Project.xml is written.
    /// Never fails the save.</summary>
    public static void CaptureSaved(XElement project) {
        try {
            var document = Document(project, FamilyBlocks()); if (document is null) return;
            string world = Text(Group(project.Element("Subsystems"), GunGroup), ScGunTravel.SourcePath);
            if (string.IsNullOrWhiteSpace(world)) return;
            Write(Storage.GetSystemPath(Storage.CombinePaths(world, FileName)), document);
        }
        catch (Exception e) { KnifeLog.Warning("[GUN_MIRROR] gun record copy not written; the world save itself is unaffected: " + e.Message); }
    }

    /// <summary>Verified temporary file, the previous copy kept as .bak, then a same-directory replace.</summary>
    internal static void Write(string path, XElement document) {
        byte[] bytes = Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));
        lock (s_write) ScUiSettings.WriteAtomic(path, bytes, () => { if (System.IO.File.Exists(path)) System.IO.File.Copy(path, path + ".bak", true); });
    }

    /// <summary>The player notice of the last restore, handed once to the world that loads next.</summary>
    public static string TakeNotice() { string notice = s_notice; s_notice = null; return notice; }

    /// <summary>ProjectXmlLoad, before any other gun step reads the group or the engine assigns block numbers.
    /// True when the lost group was put back.</summary>
    public static bool BeforeLoad(XElement project, WorldInfo world) {
        try {
            if (string.IsNullOrWhiteSpace(world?.DirectoryName)) return false;
            if (!Lost(Group(project.Element("Subsystems"), GunGroup))) return false; // the usual case: no file access
            s_notice = null;
            return RestoreFrom(project, Storage.GetSystemPath(Storage.CombinePaths(world.DirectoryName, FileName)), StaticNumbers());
        }
        catch (Exception e) { KnifeLog.Warning("[GUN_MIRROR] gun record restore check failed: " + e.Message); return false; }
    }

    /// <summary>Numbers that loaded block types claim through a public static Index field (the engine places those
    /// before any saved name): a moved CS block must not land on one.</summary>
    static Func<int, bool> StaticNumbers() {
        var numbers = new HashSet<int>();
        foreach (var mod in ModsManager.ModList)
            foreach (var type in mod.BlockTypes)
                if (type.GetField("Index", BindingFlags.Public | BindingFlags.Static) is { } field && field.FieldType == typeof(int))
                    try { numbers.Add((int)field.GetValue(null)); } catch (Exception) { }
        return numbers.Contains;
    }

    internal sealed record Reassignment(Dictionary<int, int> Moved, int Restored, int Unconfirmed);

    /// <summary>Gives each CS block whose number another block took while CS was absent a free number, and moves the
    /// recorded items that are still exactly where and what they were. Nothing changes when no number was taken.</summary>
    internal static Reassignment Reassign(XElement project, XElement copy, Func<int, bool> reserved) {
        var moved = new Dictionary<int, int>();
        var manager = Group(project.Element("Subsystems"), "BlocksManager");
        var blocks = copy.Element("Blocks")?.Elements("Block").ToArray() ?? [];
        if (manager is null || blocks.Length == 0) return new(moved, 0, 0);
        var numbers = Numbers(project);
        var named = numbers.Values.ToHashSet(StringComparer.Ordinal);
        var used = numbers.Keys.ToHashSet();
        var entries = new List<(int Index, string Name)>();
        foreach (var block in blocks) {
            string name = (string)block.Attribute("Name");
            if (string.IsNullOrEmpty(name) || !int.TryParse((string)block.Attribute("Index"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int old)) continue;
            if (named.Contains(name)) continue; // still holds its number
            if (!numbers.ContainsKey(old)) { entries.Add((old, name)); used.Add(old); continue; } // nobody took it: name it again
            int fresh = Enumerable.Range(FirstDynamicBlock, BlockCount - FirstDynamicBlock).FirstOrDefault(i => !used.Contains(i) && !reserved(i));
            if (fresh == 0) throw new InvalidOperationException("没有空余的方块编号可以分给 CS 方块");
            entries.Add((fresh, name)); used.Add(fresh); moved[old] = fresh;
        }
        foreach (var (index, name) in entries)
            manager.Add(new XElement("Value", new XAttribute("Name", index.ToString(CultureInfo.InvariantCulture)), new XAttribute("Type", "string"), new XAttribute("Value", name)));
        int restored = 0, unconfirmed = 0;
        if (moved.Count == 0) return new(moved, 0, 0);
        foreach (var item in copy.Element("Items")?.Elements("Item") ?? []) {
            if (!int.TryParse((string)item.Attribute("Value"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                || !moved.TryGetValue(Terrain.ExtractContents(value), out int fresh)) continue;
            var field = Resolve(project, item);
            string count = (string)item.Attribute("Count");
            if (field is null || (string)field.Attribute("Value") != value.ToString(CultureInfo.InvariantCulture) || Text(field.Parent, "Count") != count) { unconfirmed++; continue; }
            field.SetAttributeValue("Value", Terrain.ReplaceContents(value, fresh).ToString(CultureInfo.InvariantCulture));
            restored++;
        }
        return new(moved, restored, unconfirmed);
    }

    /// <summary>Puts a lost group back from the copy at <paramref name="path"/> (or its .bak), after moving CS blocks
    /// whose numbers were taken. Returns false and leaves the project untouched when the group is present or no copy
    /// of this world is readable; returns false with only the block numbers and recorded items moved when no copy can
    /// restore the group (knives, ammo and new guns then load; numbered guns meet the existing refusal).</summary>
    internal static bool RestoreFrom(XElement project, string path, Func<int, bool> reserved) {
        var subsystems = project.Element("Subsystems");
        if (subsystems is null || !Lost(Group(subsystems, GunGroup))) return false;
        var refused = new List<string>(); var copies = new List<(string Name, XElement Copy)>();
        foreach (string candidate in new[] { path, path + ".bak" }) {
            if (!System.IO.File.Exists(candidate)) continue;
            string name = System.IO.Path.GetFileName(candidate);
            try {
                var copy = XElement.Load(candidate);
                if (copy.Name != RootName || (int?)copy.Attribute("Format") != Format) throw new InvalidOperationException("记录副本格式不受支持");
                string seed = Seed(project);
                if (seed is null || seed != (string)copy.Attribute("Seed")) throw new InvalidOperationException("记录副本属于另一个世界（种子不同）");
                copies.Add((name, copy));
            }
            catch (Exception e) { refused.Add(name + ": " + e.Message); }
        }
        if (copies.Count == 0) {
            if (refused.Count > 0) KnifeLog.Warning("[GUN_MIRROR] gun records were lost and the copy cannot be used: " + string.Join("; ", refused));
            return false;
        }
        // The newest copy of this world says which numbers CS held and where its items were.
        Reassignment reassignment;
        try { reassignment = Reassign(project, copies[0].Copy, reserved); }
        catch (Exception e) { KnifeLog.Warning("[GUN_MIRROR] CS block numbers taken while CS was absent cannot be given back: " + e.Message); return false; }
        if (reassignment.Moved.Count > 0)
            KnifeLog.Warning($"[GUN_MIRROR] block numbers taken while CS was absent: {string.Join(", ", reassignment.Moved.Select(p => p.Key + "->" + p.Value))}; " +
                $"{reassignment.Restored} recorded items moved, {reassignment.Unconfirmed} no longer where they were (left unchanged)");
        foreach (var (name, copy) in copies) {
            XElement group;
            try { group = Plan(project, copy, reassignment.Moved); }
            catch (Exception e) { refused.Add(name + ": " + e.Message); continue; }
            Group(subsystems, GunGroup)?.Remove();
            subsystems.Add(group);
            KnifeLog.Warning($"[GUN_MIRROR] this world was saved without the CS mod and had lost its gun records; restored {Records(group)} records from {name}");
            s_notice = reassignment.Moved.Count == 0
                ? "CS：这个世界曾在未启用 CS 时存档，枪械记录已从世界文件夹里的副本恢复。"
                : $"CS：未启用 CS 期间，其他模组占用了 CS 物品的方块编号；已把 {reassignment.Restored} 件 CS 物品改回，" +
                  (reassignment.Unconfirmed > 0 ? $"{reassignment.Unconfirmed} 件在那段时间被移动过、无法确认，未改动。" : "全部找回。") +
                  "放在地上的武器装配台和空投箱不在此列。";
            return true;
        }
        KnifeLog.Warning("[GUN_MIRROR] gun records were lost and the copy cannot be used: " + string.Join("; ", refused));
        return false;
    }

    /// <summary>The group a copy may restore into <paramref name="project"/>, or an exception naming why not.
    /// <paramref name="moved"/> maps CS block numbers taken while CS was absent to the numbers given back.
    /// Reads nothing but its arguments and changes neither.</summary>
    internal static XElement Plan(XElement project, XElement copy, IReadOnlyDictionary<int, int> moved) {
        if (copy.Name != RootName || (int?)copy.Attribute("Format") != Format) throw new InvalidOperationException("记录副本格式不受支持");
        var groups = copy.Elements("Values").Where(e => (string)e.Attribute("Name") == GunGroup).ToArray();
        if (groups.Length != 1) throw new InvalidOperationException("记录副本里没有唯一的枪械记录");
        var group = new XElement(groups[0]);
        if (Group(group, RegistryKey) is null || Text(group, ScGunSaveGuard.ErrorKey) is not null) throw new InvalidOperationException("记录副本不完整");
        string seed = Seed(project);
        if (seed is null || seed != (string)copy.Attribute("Seed")) throw new InvalidOperationException("记录副本属于另一个世界（种子不同）");
        int? block = GunBlock(project), saved = (int?)copy.Attribute("Block");
        int? expected = saved is int s && moved.TryGetValue(s, out int fresh) ? fresh : saved;
        if (block is null || block != expected)
            throw new InvalidOperationException("世界里的枪械方块编号与记录副本不一致，且无法确认被谁占用");
        var values = new ValuesDictionary(); values.ApplyOverrides(new XElement(group));
        ScGunSaveGuard.Validate(values);
        // Every numbered gun the world holds must be in the copy as the same model: a copy older than the items is
        // refused, never patched.
        var probe = new XElement(project);
        Group(probe.Element("Subsystems"), GunGroup)?.Remove();
        probe.Element("Subsystems").Add(new XElement(group));
        var inspection = ScGunLoadIntegrity.Inspect(probe);
        if (inspection.Issues.Length > 0) throw new InvalidOperationException("记录副本与世界里的枪对不上：" + inspection.Issues[0].Reason);
        int next = ScGunRegistry.Load(values.GetValue<ValuesDictionary>(RegistryKey, null), 0).Next;
        if (inspection.Next > next) throw new InvalidOperationException($"世界里有比记录副本更新的枪（编号到 {inspection.Next - 1}）");
        return group;
    }
}
