using System.Globalization;
using System.IO;
using System.Xml.Linq;
using Engine;
using TemplatesDatabase;
namespace Game;

/// <summary>One supported upgrade: an untouched published 0.28.2 world to v5. Transform a detached XML
/// document before entities load, back up the whole original world, then replace the in-memory document.
/// No item-bit heuristic is allowed to authorize migration of an unknown or internal-test world.</summary>
public static class ScGun0282Migration {
    public const string Package = "zh667.ScCsgoKnives", SourceVersion = "0.28.2";
    public const string Marker = "Official0282Migration", ErrorKey = "Official0282MigrationError", NoticeKey = "Official0282JustMigrated";
    static readonly CultureInfo CI = CultureInfo.InvariantCulture;
    public sealed record Plan(XElement Document, int Guns, int Records);
    public readonly record struct OldGun(int Variant, int Rounds, bool SilencerOff);
    static XElement Group(XElement parent, string name) => parent?.Elements("Values").SingleOrDefault(e => (string)e.Attribute("Name") == name);
    static XElement Field(XElement parent, string name) => parent?.Elements("Value").SingleOrDefault(e => (string)e.Attribute("Name") == name);
    static string Text(XElement parent, string name) => (string)Field(parent, name)?.Attribute("Value");
    static int Integer(XElement element) => int.Parse((string)element?.Attribute("Value") ?? throw new InvalidOperationException("缺少物品字段"), CI);
    static XElement Value(string name, string type, object value) => new("Value", new XAttribute("Name", name), new XAttribute("Type", type), new XAttribute("Value", Convert.ToString(value, CI)));
    static XElement GunSubsystem(XElement project, bool create = false) {
        var subsystems = project.Element("Subsystems") ?? throw new InvalidOperationException("世界缺少 Subsystems");
        var gun = Group(subsystems, "ScGunBlockBehavior");
        if (gun is null && create) { gun = new XElement("Values", new XAttribute("Name", "ScGunBlockBehavior")); subsystems.Add(gun); }
        return gun;
    }
    public static string SavedVersion(XElement project) {
        var mods = Group(Group(project.Element("Subsystems"), "UsedMods"), "Mods");
        var matches = mods?.Elements("Values").Where(m => Text(m, "PackageName") == Package).ToArray() ?? [];
        if (matches.Length > 1) throw new InvalidOperationException("世界中存在重复的枪械模组版本记录");
        return matches.Length == 1 ? Text(matches[0], "Version") : null;
    }
    public static bool TryDecode(int data, out OldGun gun) {
        gun = default;
        if (data < 0) return false;
        int variant, rounds; bool off;
        // Exactly the three reader layouts in the actual 0.28.2 DLL. Only used after source-version authorization.
        if ((data & 65536) != 0) {
            if ((data & ~0x17fff) != 0) return false;
            variant = data & 63; rounds = (data >> 6) & 255; off = (data & 16384) != 0;
        }
        else if ((data & 16384) != 0) {
            if ((data & ~0x7fff) != 0) return false;
            variant = data & 63; rounds = (data >> 6) & 127; off = (data & 8192) != 0;
        }
        else {
            if ((data & ~511) != 0) return false;
            variant = data & 3; rounds = (data >> 2) & 63; off = (data & 256) != 0;
        }
        if (variant >= GunSpec.All.Length || rounds > GunSpec.All[variant].Magazine) return false;
        gun = new OldGun(variant, rounds, off); return true;
    }

    /// <summary>Pure preparation. Does not touch disk, the supplied XML or the live registry. Null means
    /// this is not the supported source world / has already migrated. Invalid eligible worlds abort entirely.</summary>
    public static Plan Prepare(XElement source) {
        if (source is null) throw new ArgumentNullException(nameof(source));
        var oldSubsystem = GunSubsystem(source);
        if (Group(oldSubsystem, Marker) is not null) return null;
        if (SavedVersion(source) != SourceVersion) return null;
        if (Field(oldSubsystem, "GunDataLayout") is not null || Group(oldSubsystem, "GunRegistry") is not null || Group(oldSubsystem, "GunWear") is not null)
            throw new InvalidOperationException("版本记录为 0.28.2，但发现测试版布局或记录表；不猜测混合数据，请使用未被测试版改写的备份");
        var document = new XElement(source);
        var subsystems = document.Element("Subsystems");
        var maps = Group(subsystems, "BlocksManager")?.Elements("Value").Where(v => (string)v.Attribute("Value") == "ScGunBlock").ToArray() ?? [];
        if (maps.Length != 1 || !int.TryParse((string)maps[0].Attribute("Name"), out int gunIndex) || gunIndex < 1 || gunIndex > 1023)
            throw new InvalidOperationException("无法唯一确认本世界保存的枪械方块编号");
        var registry = new ScGunRegistry();
        int guns = 0;
        int ConvertItem(int value, int count, bool creative = false) {
            if (Terrain.ExtractContents(value) != gunIndex || count <= 0) return value;
            if (!creative && count != 1) throw new InvalidOperationException("发现堆叠的旧枪，无法安全分配独立身份；迁移未执行");
            if (!TryDecode(Terrain.ExtractData(value), out var old)) throw new InvalidOperationException($"旧枪数据 {Terrain.ExtractData(value)} 不符合 0.28.2 的型号/弹量范围");
            int id;
            if (!old.SilencerOff && old.Rounds == GunSpec.All[old.Variant].Magazine) id = GunSpec.FreshFull;
            else if (!old.SilencerOff && old.Rounds == 0) id = GunSpec.FreshEmpty;
            else {
                id = registry.Allocate(old.Variant, old.Rounds, old.SilencerOff, ScGunDurability.Full(old.Variant));
                if (id < GunSpec.FirstId) throw new InvalidOperationException($"旧枪所需实例记录超过 {GunSpec.LastId} 条，迁移未执行；原物品未改动");
            }
            guns++;
            return Terrain.ReplaceData(value, GunSpec.WithId(old.Variant, id));
        }
        // All serialized IInventory slots, including unloaded block entities and Stash ComponentInventoryBase.
        // CreativeInventory.Save writes only real open slots, without Count; the generated catalogue is not serialized.
        foreach (var slots in document.Descendants("Values").Where(e => (string)e.Attribute("Name") == "Slots")) {
            bool creative = (string)slots.Parent?.Attribute("Name") == "CreativeInventory";
            foreach (var slot in slots.Elements("Values")) {
                string name = (string)slot.Attribute("Name") ?? "";
                if (!name.StartsWith("Slot", StringComparison.Ordinal) || !int.TryParse(name[4..], out int index) || index < 0) continue;
                var field = Field(slot, "Contents");
                if (field is null) continue;
                int value = Integer(field);
                if (Terrain.ExtractContents(value) != gunIndex) continue;
                int count = Field(slot, "Count") is { } countField ? Integer(countField) : creative ? 1 : throw new InvalidOperationException("旧枪库存格缺少数量");
                field.SetAttributeValue("Value", ConvertItem(value, count, creative));
            }
        }
        foreach (string name in new[] { "Pickables", "Projectiles" }) {
            var items = Group(Group(subsystems, name), name);
            if (items is null) continue;
            foreach (var item in items.Elements("Values")) {
                var field = Field(item, "Value"); if (field is null) continue;
                int value = Integer(field);
                if (Terrain.ExtractContents(value) != gunIndex) continue;
                int count = name == "Pickables" ? Integer(Field(item, "Count")) : 1;
                field.SetAttributeValue("Value", ConvertItem(value, count));
            }
        }
        // Verified SubsystemMovingBlocks.Save layout: value,x,y,z;value,x,y,z;...
        var moving = Group(Group(subsystems, "MovingBlocks"), "MovingBlockSets");
        if (moving is not null) foreach (var set in moving.Elements("Values")) {
            var field = Field(set, "Blocks"); if (field is null) continue;
            string[] pieces = ((string)field.Attribute("Value") ?? "").Split(';'); bool changed = false;
            for (int i = 0; i < pieces.Length; i++) {
                if (string.IsNullOrWhiteSpace(pieces[i])) continue;
                string[] cells = pieces[i].Split(',');
                if (cells.Length != 4 || !int.TryParse(cells[0], NumberStyles.Integer, CI, out int value)) throw new InvalidOperationException("无法读取移动方块保存数据");
                if (Terrain.ExtractContents(value) != gunIndex) continue;
                cells[0] = ConvertItem(value, 1).ToString(CI); pieces[i] = string.Join(",", cells); changed = true;
            }
            if (changed) field.SetAttributeValue("Value", string.Join(";", pieces));
        }
        var target = GunSubsystem(document, true);
        // Keeping unrelated state and the old recharge table is harmless: the new gun registry takes precedence.
        target.Add(Value("GunDataLayout", "int", GunSpec.DataLayout));
        var registryNode = new XElement("Values", new XAttribute("Name", "GunRegistry"));
        registry.Save(0).Save(registryNode); target.Add(registryNode);
        target.Add(new XElement("Values", new XAttribute("Name", Marker), Value("Version", "int", 1), Value("FromVersion", "string", SourceVersion),
            Value("Guns", "int", guns), Value("Records", "int", registry.Count)));
        target.Add(Value(NoticeKey, "bool", true));
        return new Plan(document, guns, registry.Count);
    }

    /// <summary>Back up first; only a fully prepared plan replaces the XML. Callback seams allow failure
    /// injection in tests without touching a user's world. Returning null does nothing.</summary>
    public static Plan Execute(XElement project, Func<string> backup) {
        var plan = Prepare(project);
        if (plan is null) return null;
        string path = backup();
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("世界备份没有成功完成，迁移取消");
        Group(GunSubsystem(plan.Document), Marker).Add(Value("Backup", "string", path), Value("Utc", "string", DateTime.UtcNow.ToString("O", CI)));
        project.ReplaceNodes(plan.Document.Nodes().Select(n => n is XElement element ? new XElement(element) : n));
        return plan;
    }
    public static string BackupWorld(string directory) {
        string name = "ScCsgoKnives-before-0282-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CI) + "-" + Guid.NewGuid().ToString("N")[..8];
        string path = WorldsManager.MakeSnapshotFilename(directory, name);
        string pending = WorldsManager.MakeSnapshotFilename(directory, name + ".incomplete");
        var files = new List<(string Path, string Relative)>();
        void Collect(string folder, string relative) {
            foreach (var file in Storage.ListFileNames(folder).OrderBy(f => f, StringComparer.Ordinal)) {
                if (file.EndsWith(".snapshot", StringComparison.OrdinalIgnoreCase)) continue;
                files.Add((Storage.CombinePaths(folder, file), relative + file));
            }
            foreach (var child in Storage.ListDirectoryNames(folder).OrderBy(f => f, StringComparer.Ordinal))
                Collect(Storage.CombinePaths(folder, child), relative + child + "/");
        }
        Collect(directory, "");
        if (!files.Any(f => f.Relative == "Project.xml")) throw new InvalidOperationException("原世界缺少 Project.xml");
        // Standard world snapshot ZIP, using Storage for Windows/Android paths. No WorldInfo/WorldPalette
        // initialization is needed. Keep every sidecar/region; exclude other snapshots to avoid recursion.
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
            var entry = archive.GetEntry("Project.xml") ?? throw new InvalidOperationException("备份缺少 Project.xml");
            using var projectStream = entry.Open();
            if (SavedVersion(XElement.Load(projectStream)) != SourceVersion) throw new InvalidOperationException("备份中的世界版本与迁移来源不符");
        }
        Storage.MoveFile(pending, path); // only a completed, reopened archive receives the final backup name
        return path;
    }
    public static void BeforeLoad(XElement project, WorldInfo world) {
        try {
            var plan = Execute(project, () => BackupWorld(world.DirectoryName));
            if (plan is not null) KnifeLog.Information($"0.28.2 gun migration prepared: {plan.Guns} guns, {plan.Records} records, full durability; original world snapshot kept");
        }
        catch (Exception e) {
            // Hook exceptions are swallowed by ModsManager.TryInvoke. Set an ephemeral error so the
            // subsystem's actual Load fails before gameplay/autosave; do not let the source version be overwritten.
            var target = GunSubsystem(project, true);
            Field(target, ErrorKey)?.Remove(); target.Add(Value(ErrorKey, "string", e.Message));
            KnifeLog.Error("0.28.2 gun migration refused, original items unchanged: " + e);
        }
    }
}
