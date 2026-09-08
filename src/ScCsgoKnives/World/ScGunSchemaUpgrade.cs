using System.Globalization;
using System.Xml.Linq;
using System.Security.Cryptography;
using Engine;
namespace Game;

/// <summary>A verified world backup before this build first rewrites an older record schema.
///
/// Loading a schema 1 or 2 table converts it in memory and the next normal save writes schema 3, which older
/// builds refuse to open. That is a real format change, so the compatibility policy's rule applies: back the
/// whole world up, close and reopen the archive to prove it, and only then let the converted world be played.
/// A backup that cannot be completed refuses the load instead of quietly upgrading without one.
///
/// Nothing here rewrites an item, a record or a stamp. The conversion itself lives in ScGunRegistry.Load; this
/// only decides whether the player has a way back.
///
/// The 0.28.2 migration keeps its own snapshot routine and its own file name, which have already shipped; this
/// is deliberately a separate copy so a change here cannot disturb that verified path.</summary>
public static class ScGunSchemaUpgrade {
    public const string Marker = "GunSchemaUpgrade";
    static readonly CultureInfo CI = CultureInfo.InvariantCulture;

    static XElement Subsystem(XElement project) => project.Element("Subsystems")?.Elements("Values")
        .FirstOrDefault(e => (string)e.Attribute("Name") == "ScGunBlockBehavior");
    static XElement Group(XElement parent, string name) => parent?.Elements("Values").FirstOrDefault(e => (string)e.Attribute("Name") == name);
    static XElement Value(string name, string type, object value) =>
        new("Value", new XAttribute("Name", name), new XAttribute("Type", type), new XAttribute("Value", Convert.ToString(value, CI)));

    /// <summary>The saved record schema, or 0 when this world has no gun table at all.</summary>
    public static int SavedSchema(XElement project) {
        var registry = Group(Subsystem(project), "GunRegistry");
        var field = registry?.Elements("Value").FirstOrDefault(v => (string)v.Attribute("Name") == "Schema");
        return field is not null && int.TryParse((string)field.Attribute("Value"), NumberStyles.Integer, CI, out int schema) ? schema : 0;
    }
    /// <summary>Whether loading this world would convert it to a schema older builds cannot read.</summary>
    public static bool NeedsBackup(XElement project) {
        int schema = SavedSchema(project);
        // A marker from a prior conversion does not prove this older on-disk schema was
        // backed up. Restored/unsaved old data always gets its own verified source snapshot.
        return schema != 0 && schema != ScGunRegistry.Schema && ScGunRegistry.IsKnownSchema(schema);
    }

    public static string FileName(int from, int to) =>
        $"ScCsgoKnives-gunschema-{from}-to-{to}-{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CI)}-{Guid.NewGuid().ToString("N")[..8]}";

    /// <summary>Writes the world to a snapshot under a temporary name, reopens it, checks every file arrived and
    /// that Project.xml is readable, and only then gives it its final name. Existing snapshots are excluded so a
    /// backup can never contain a backup, and no existing snapshot is overwritten.</summary>
    public static string Snapshot(string directory, string name, int expectedSchema = 0) {
        string path = WorldsManager.MakeSnapshotFilename(directory, name);
        string pending = WorldsManager.MakeSnapshotFilename(directory, name + ".incomplete");
        var files = new List<(string Path, string Relative)>();
        void Collect(string folder, string relative) {
            foreach (string file in Storage.ListFileNames(folder).OrderBy(f => f, StringComparer.Ordinal)) {
                if (file.EndsWith(".snapshot", StringComparison.OrdinalIgnoreCase)) continue;
                files.Add((Storage.CombinePaths(folder, file), relative + file));
            }
            foreach (string child in Storage.ListDirectoryNames(folder).OrderBy(f => f, StringComparer.Ordinal))
                Collect(Storage.CombinePaths(folder, child), relative + child + "/");
        }
        Collect(directory, "");
        if (!files.Any(f => f.Relative == "Project.xml")) throw new InvalidOperationException("原世界缺少 Project.xml");
        using (var output = Storage.OpenFile(pending, OpenFileMode.Create))
        using (var archive = new System.IO.Compression.ZipArchive(output, System.IO.Compression.ZipArchiveMode.Create)) {
            foreach (var file in files) {
                var entry = archive.CreateEntry(file.Relative, System.IO.Compression.CompressionLevel.Fastest);
                using var input = Storage.OpenFile(file.Path, OpenFileMode.Read);
                using var target = entry.Open(); input.CopyTo(target);
            }
        }
        using (var input = Storage.OpenFile(pending, OpenFileMode.Read))
        using (var archive = new System.IO.Compression.ZipArchive(input, System.IO.Compression.ZipArchiveMode.Read)) {
            if (archive.Entries.Count != files.Count) throw new InvalidOperationException("备份文件数量不符");
            foreach (var file in files) {
                var saved = archive.GetEntry(file.Relative) ?? throw new InvalidOperationException("备份缺少 " + file.Relative);
                using var original = Storage.OpenFile(file.Path, OpenFileMode.Read);
                using var copy = saved.Open();
                if (!SHA256.HashData(original).AsSpan().SequenceEqual(SHA256.HashData(copy)))
                    throw new InvalidOperationException("备份校验失败或源文件发生变化：" + file.Relative);
            }
            using var stream = archive.GetEntry("Project.xml").Open();
            var project = XElement.Load(stream);
            if (expectedSchema != 0 && SavedSchema(project) != expectedSchema)
                throw new InvalidOperationException("备份中的枪械 schema 与待迁移来源不一致");
        }
        Storage.MoveFile(pending, path);
        return path;
    }

    /// <summary>Records that this world has been backed up for the schema change, so it happens once. The marker
    /// travels with the world through the subsystem's own save.</summary>
    public static void Mark(XElement project, int from, string backup) {
        var target = Subsystem(project);
        if (target is null) return;
        Group(target, Marker)?.Remove();
        target.Add(new XElement("Values", new XAttribute("Name", Marker),
            Value("Version", "int", 1), Value("From", "int", from), Value("To", "int", ScGunRegistry.Schema),
            Value("Backup", "string", backup), Value("Utc", "string", DateTime.UtcNow.ToString("O", CI))));
    }

    /// <summary>Called from the XML load hook after the format guard and the 0.28.2 migration. Returns the backup
    /// path when one was made, null when none was needed. Throws when a needed backup could not be completed.</summary>
    public static string BeforeLoad(XElement project, WorldInfo world) {
        if (world is null || !NeedsBackup(project)) return null;
        int from = SavedSchema(project);
        string backup = Snapshot(world.DirectoryName, FileName(from, ScGunRegistry.Schema), from);
        if (string.IsNullOrWhiteSpace(backup)) throw new InvalidOperationException("世界备份没有成功完成，记录格式升级取消");
        Mark(project, from, backup);
        KnifeLog.Information($"gun record schema {from} -> {ScGunRegistry.Schema}: world backed up to {backup} before the first save in the new format");
        return backup;
    }
}
