using System.Globalization;
using System.Xml.Linq;
using TemplatesDatabase;
using Engine;
namespace Game;

/// <summary>Check persisted item references before any live registry can allocate IDs.
/// Uses the world's saved block map, never the current mod load-order index.</summary>
public static class ScGunLoadIntegrity {
    public const string ProtectionKey = "GunIntegrityProtection";
    public sealed record Issue(int Id, int Variant, string Holder, string Reason);
    public sealed record Inspection(int CheckedItems, int Next, Issue[] Issues);
    static XElement Group(XElement p, string name) => p?.Elements("Values").SingleOrDefault(e => (string)e.Attribute("Name") == name);
    static string Text(XElement p, string name) => (string)p?.Elements("Value").SingleOrDefault(e => (string)e.Attribute("Name") == name)?.Attribute("Value");
    static int Number(string text) => int.Parse(text ?? throw new InvalidOperationException("枪械物品字段缺失"), CultureInfo.InvariantCulture);
    /// <summary>Strict diagnostic retained for audits. Gameplay uses the backed-up local-damage path below.</summary>
    public static int ValidateReferences(XElement project) {
        var result = Inspect(project);
        if (result.Issues.Length > 0) throw new InvalidOperationException(result.Issues[0].Reason);
        return result.CheckedItems;
    }
    public static Inspection Inspect(XElement project) {
        var subs = project.Element("Subsystems");
        var gun = Group(subs, "ScGunBlockBehavior");
        // ValuesDictionary merges duplicate keys. Such XML cannot be preserved verbatim by normal saves.
        if (gun?.DescendantsAndSelf("Values").Any(group => group.Elements()
            .Where(e => e.Name == "Value" || e.Name == "Values")
            .GroupBy(e => (string)e.Attribute("Name")).Any(g => g.Count() > 1)) == true)
            throw new InvalidOperationException("枪械存档存在重复字段或记录编号，无法原样保存，拒绝加载");
        // Legacy layouts have their own explicit migration/refusal policy; never reinterpret them as v5.
        if (Text(gun, "GunDataLayout") == ScGunRegistry.LegacyStamp.ToString()) return new(0, 1, []);
        var values = new ValuesDictionary();
        if (gun is not null) values.ApplyOverrides(new XElement(gun));
        ScGunSaveGuard.Validate(values);
        var table = values.GetValue<ValuesDictionary>("GunRegistry", null);
        var rawRows = table?.GetValue<ValuesDictionary>("Records", null);
        if (rawRows is not null && rawRows.Any(row => row.Value is not string))
            throw new InvalidOperationException("枪械记录含非文本字段，无法证明原样保存，拒绝加载");
        if (table is null && values.ContainsKey("GunDataLayout"))
            throw new InvalidOperationException("枪械布局仍在，但 GunRegistry 记录表缺失；请恢复同一世界的完整备份，禁止初始化为空表");
        var registry = ScGunRegistry.Load(table, 0);
        if (registry.Disabled) throw new InvalidOperationException("枪械记录表无法安全读取");
        var maps = Group(subs, "BlocksManager")?.Elements("Value").Where(e => (string)e.Attribute("Value") == "ScGunBlock").ToArray() ?? [];
        if (maps.Length == 0 && registry.Count == 0 && registry.QuarantinedCount == 0) return new(0, registry.Next, []); // new world
        if (maps.Length != 1) throw new InvalidOperationException("保存的枪械方块映射缺失或重复，不能验证旧物品");
        int block = Number((string)maps[0].Attribute("Name"));
        if (block is < 1 or > 1023) throw new InvalidOperationException("保存的枪械方块编号无效");
        int checkedItems = 0;
        int next = registry.Next;
        var issues = new List<Issue>();
        void Item(int value, int count, string holder) {
            if (count <= 0 || Terrain.ExtractContents(value) != block) return;
            // Detached XML must not depend on a previous world's static Current registry.
            int data = Terrain.ExtractData(value), id = (data >> 6) & 1023, variant = data & GunSpec.VariantMask;
            if ((data & (1 << 16)) != 0 || variant < 0 || variant >= GunSpec.All.Length)
                throw new InvalidOperationException($"{holder} 的枪械编码无法确认，原物品已保留");
            if (id is GunSpec.FreshFull or GunSpec.FreshEmpty) return;
            if (count != 1) throw new InvalidOperationException($"{holder} 存在堆叠的枪械实例，无法安全分离，原物品已保留");
            if (table is null || Text(gun, "GunDataLayout") != "5" && !values.ContainsKey("GunRegistry"))
                throw new InvalidOperationException("旧枪仍在，但整个枪械记录表或布局来源缺失；不能作为新世界继续分配编号，请恢复完整备份");
            next = Math.Max(next, id + 1); // includes every orphan, including inactive inventories and drops
            if (!registry.TryGetSnapshot(id, out var state) || state.Variant != variant) {
                string reason = registry.TryGetSnapshot(id, out state)
                    ? $"型号冲突：物品为{ScGunNames.Variant(variant)}，记录为{ScGunNames.Variant(state.Variant)}"
                    : "记录缺失或已隔离";
                issues.Add(new(id, variant, holder, $"{holder} / #{id}：{reason}"));
            }
            checkedItems++;
        }
        foreach (var slots in project.Descendants("Values").Where(e => (string)e.Attribute("Name") == "Slots")) {
            bool creative = (string)slots.Parent?.Attribute("Name") == "CreativeInventory";
            foreach (var slot in slots.Elements("Values")) {
                string raw = Text(slot, "Contents");
                if (raw is null) continue;
                int value = Number(raw);
                if (Terrain.ExtractContents(value) != block) continue;
                string count = Text(slot, "Count");
                Item(value, count is null && creative ? 1 : Number(count), $"{(string)slots.Parent?.Attribute("Name")}/{(string)slot.Attribute("Name")}");
            }
        }
        foreach (string name in new[] { "Pickables", "Projectiles" })
            foreach (var item in Group(Group(subs, name), name)?.Elements("Values") ?? []) {
                string raw = Text(item, "Value");
                if (raw is null) continue;
                int value = Number(raw);
                if (Terrain.ExtractContents(value) == block) Item(value, name == "Pickables" ? Number(Text(item, "Count")) : 1, name);
            }
        foreach (var set in Group(Group(subs, "MovingBlocks"), "MovingBlockSets")?.Elements("Values") ?? [])
            foreach (string piece in (Text(set, "Blocks") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)) {
                string[] parts = piece.Split(',');
                if (parts.Length != 4) throw new InvalidOperationException("移动方块存档无法安全读取");
                Item(Number(parts[0]), 1, "MovingBlocks");
            }
        // Recovery receipts and pending kill credits also retain ownership of old IDs.
        foreach (var batch in registry.Recovery.Batches) foreach (var step in batch.Steps)
            Item(step.Value, step.Count, $"Recovery/{batch.Id}");
        foreach (var entry in registry.Kills.Pending)
            if (entry.RecordId >= GunSpec.FirstId && entry.RecordId <= GunSpec.LastId) next = Math.Max(next, entry.RecordId + 1);
        return new(checkedItems, next, issues.ToArray());
    }

    /// <summary>Stage only the allocation watermark and an audit notice. Never patch an item or a record.</summary>
    public static XElement Prepare(XElement source) {
        var result = Inspect(source);
        var oldNotice = Group(Group(source.Element("Subsystems"), "ScGunBlockBehavior"), ProtectionKey);
        if (result.Issues.Length == 0 && result.Next <= ScGunRegistry.Load(ReadTable(source), 0).Next
            && (oldNotice is null || Text(oldNotice, "Count") == "0")) return null;
        var doc = new XElement(source);
        var gun = Group(doc.Element("Subsystems"), "ScGunBlockBehavior");
        var table = Group(gun, "GunRegistry") ?? throw new InvalidOperationException("记录表缺失，禁止初始化");
        var next = table.Elements("Value").SingleOrDefault(e => (string)e.Attribute("Name") == "Next");
        if (next is null) table.Add(Field("Next", result.Next)); else next.SetAttributeValue("Value", result.Next);
        // Keep the verified first backup reference across repeated loads; refresh only the diagnosis.
        var previous = Group(gun, ProtectionKey);
        string details = string.Join("\n", result.Issues.Select(i => i.Reason));
        string backup = Text(previous, "Details") == details || result.Issues.Length == 0 ? Text(previous, "Backup") : null;
        var notice = new XElement("Values", new XAttribute("Name", ProtectionKey), Field("Count", result.Issues.Length), Field("Next", result.Next));
        if (backup is not null) notice.Add(Field("Backup", backup));
        notice.Add(Field("Details", details));
        previous?.Remove();gun.Add(notice);
        return doc;
    }
    static XElement Field(string name, object value) => new("Value", new XAttribute("Name", name),
        new XAttribute("Type", value is int ? "int" : "string"), new XAttribute("Value", value));
    static ValuesDictionary ReadTable(XElement doc) {
        var table = Group(Group(doc.Element("Subsystems"), "ScGunBlockBehavior"), "GunRegistry");
        if (table is null) return null;
        var values = new ValuesDictionary();values.ApplyOverrides(new XElement(table));return values;
    }
    /// <summary>No staged changes activate until a full backup exists. Exceptions reach the native load guard.</summary>
    public static string ApplyProtected(XElement source, Func<string> backup, Func<string, bool> backupExists) {
        var staged = Prepare(source);
        if (staged is null) return null;
        var notice = Group(Group(staged.Element("Subsystems"), "ScGunBlockBehavior"), ProtectionKey);
        string path = Text(notice, "Backup");
        if (string.IsNullOrWhiteSpace(path) || !backupExists(path)) path = backup();
        if (string.IsNullOrWhiteSpace(path) || !backupExists(path)) throw new InvalidOperationException("异常枪械保护备份未完成，未修改物品和编号");
        notice.Elements("Value").Where(e => (string)e.Attribute("Name") == "Backup").Remove();notice.Add(Field("Backup", path));
        source.ReplaceNodes(staged.Nodes());
        KnifeLog.Warning($"[GUN_INTEGRITY] 保留 {Text(notice, "Count")} 处异常枪械，正常枪可继续使用；next={Text(notice, "Next")}，原数据备份={path}；{Text(notice, "Details")}");
        return path;
    }
    public static string BeforeLoad(XElement project, WorldInfo world, string verifiedBackup) => ApplyProtected(project,
        () => verifiedBackup ?? ScGunSchemaUpgrade.Snapshot(world.DirectoryName, "ScCsgoKnives-before-integrity-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]), Storage.FileExists);
}
