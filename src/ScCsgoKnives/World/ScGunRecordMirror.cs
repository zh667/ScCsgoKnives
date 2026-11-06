using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Engine;
using TemplatesDatabase;
namespace Game;

/// <summary>gun-record-mirror-20261106 (1.5.1): a world opened and saved without the CS mod keeps its gun records.
/// The engine saves only the subsystems it has templates for, so such a save drops the ScGunBlockBehavior group from
/// Project.xml while the gun items and the BlocksManager name of their block stay; reinstalling CS then found guns
/// without records and refused the world. Every save therefore also writes that group verbatim to
/// <see cref="FileName"/> in the world folder (the engine never touches unknown files there; exporting, snapshotting
/// and deleting a world take the whole folder). A load whose Project.xml has lost the group takes it back from that
/// file, but only when the file belongs to this world (seed, gun block number) and accounts for every numbered gun
/// the world holds; otherwise nothing is restored and the existing refusal stands. Project.xml stays the only
/// authority: the file is never read while the group is present and never merged into it.
/// User decision 2026-11-06, an explicit exception to "no automatic backups": one file per world, replaced at each
/// save (the previous one kept as .bak), holding only this group.</summary>
public static class ScGunRecordMirror {
    public const string FileName = "ScCsgoGunRecords.xml";
    public const int Format = 1;
    const string RootName = "ScGunRecordMirror", GunGroup = "ScGunBlockBehavior", RegistryKey = "GunRegistry";
    static readonly object s_write = new();

    static XElement Group(XElement parent, string name) => parent?.Elements("Values").SingleOrDefault(e => (string)e.Attribute("Name") == name);
    static string Text(XElement parent, string name) => (string)parent?.Elements("Value").SingleOrDefault(e => (string)e.Attribute("Name") == name)?.Attribute("Value");
    static int? GunBlock(XElement project) {
        var entries = Group(project.Element("Subsystems"), "BlocksManager")?.Elements("Value").Where(e => (string)e.Attribute("Value") == "ScGunBlock").ToArray();
        return entries?.Length == 1 && int.TryParse((string)entries[0].Attribute("Name"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int block) ? block : null;
    }
    static string Seed(XElement project) => Text(Group(project.Element("Subsystems"), "GameInfo"), "WorldSeed");
    static int Records(XElement gun) => Group(Group(gun, RegistryKey), "Records")?.Elements().Count() ?? 0;

    /// <summary>A save by CS always holds the registry, so a group without it (or no group at all) was lost by a save
    /// without CS. A group carrying a refusal marker is a deliberate refusal and is never replaced.</summary>
    internal static bool Lost(XElement gun) => gun is null
        || Group(gun, RegistryKey) is null && Text(gun, "GunDataLayout") is null && Text(gun, ScGunSaveGuard.ErrorKey) is null;

    /// <summary>The copy of a finished save, or null when that save holds no complete gun group.</summary>
    internal static XElement Document(XElement project) {
        var gun = Group(project.Element("Subsystems"), GunGroup);
        if (gun is null || Group(gun, RegistryKey) is null || Text(gun, ScGunSaveGuard.ErrorKey) is not null) return null;
        int? block = GunBlock(project); string seed = Seed(project);
        if (block is null || seed is null) return null;
        return new XElement(RootName, new XAttribute("Format", Format), new XAttribute("Block", block.Value),
            new XAttribute("Seed", seed), new XAttribute("Build", ScCompatibility.ActiveBuild), new XElement(gun));
    }

    /// <summary>OnProjectXmlSaved: the engine's saving thread, the complete XML, before Project.xml is written.
    /// Never fails the save.</summary>
    public static void CaptureSaved(XElement project) {
        try {
            var document = Document(project); if (document is null) return;
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

    /// <summary>ProjectXmlLoad, before any other gun step reads the group. True when the lost group was put back.</summary>
    public static bool BeforeLoad(XElement project, WorldInfo world) {
        try {
            if (string.IsNullOrWhiteSpace(world?.DirectoryName)) return false;
            if (!Lost(Group(project.Element("Subsystems"), GunGroup))) return false; // the usual case: no file access
            return RestoreFrom(project, Storage.GetSystemPath(Storage.CombinePaths(world.DirectoryName, FileName)));
        }
        catch (Exception e) { KnifeLog.Warning("[GUN_MIRROR] gun record restore check failed: " + e.Message); return false; }
    }

    /// <summary>Puts a lost group back from the copy at <paramref name="path"/> (or its .bak). Leaves the project
    /// untouched and returns false when the group is present or no copy passes <see cref="Plan"/>.</summary>
    internal static bool RestoreFrom(XElement project, string path) {
        var subsystems = project.Element("Subsystems");
        if (subsystems is null || !Lost(Group(subsystems, GunGroup))) return false;
        var refused = new List<string>();
        foreach (string candidate in new[] { path, path + ".bak" }) {
            if (!System.IO.File.Exists(candidate)) continue;
            XElement group;
            try { group = Plan(project, XElement.Load(candidate)); }
            catch (Exception e) { refused.Add(System.IO.Path.GetFileName(candidate) + ": " + e.Message); continue; }
            Group(subsystems, GunGroup)?.Remove();
            subsystems.Add(group);
            KnifeLog.Warning($"[GUN_MIRROR] this world was saved without the CS mod and had lost its gun records; restored {Records(group)} records from {System.IO.Path.GetFileName(candidate)}");
            return true;
        }
        if (refused.Count > 0) KnifeLog.Warning("[GUN_MIRROR] gun records were lost and the copy cannot be used: " + string.Join("; ", refused));
        return false;
    }

    /// <summary>The group a copy may restore into <paramref name="project"/>, or an exception naming why not.
    /// Reads nothing but its arguments and changes neither.</summary>
    internal static XElement Plan(XElement project, XElement copy) {
        if (copy.Name != RootName || (int?)copy.Attribute("Format") != Format) throw new InvalidOperationException("记录副本格式不受支持");
        var groups = copy.Elements("Values").Where(e => (string)e.Attribute("Name") == GunGroup).ToArray();
        if (groups.Length != 1) throw new InvalidOperationException("记录副本里没有唯一的枪械记录");
        var group = new XElement(groups[0]);
        if (Group(group, RegistryKey) is null || Text(group, ScGunSaveGuard.ErrorKey) is not null) throw new InvalidOperationException("记录副本不完整");
        string seed = Seed(project);
        if (seed is null || seed != (string)copy.Attribute("Seed")) throw new InvalidOperationException("记录副本属于另一个世界（种子不同）");
        int? block = GunBlock(project);
        if (block is null || block.Value != (int?)copy.Attribute("Block"))
            throw new InvalidOperationException("世界里的枪械方块编号与记录副本不一致（可能在卸载期间被其他模组占用）");
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
