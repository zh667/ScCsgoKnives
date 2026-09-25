using System.Globalization;
using System.Xml.Linq;
using TemplatesDatabase;
namespace Game;

/// <summary>Check persisted item references before any live registry can allocate IDs.
/// Uses the world's saved block map, never the current mod load-order index.</summary>
public static class ScGunLoadIntegrity {
    static XElement Group(XElement p, string name) => p?.Elements("Values").SingleOrDefault(e => (string)e.Attribute("Name") == name);
    static string Text(XElement p, string name) => (string)p?.Elements("Value").SingleOrDefault(e => (string)e.Attribute("Name") == name)?.Attribute("Value");
    static int Number(string text) => int.Parse(text ?? throw new InvalidOperationException("枪械物品字段缺失"), CultureInfo.InvariantCulture);
    public static int ValidateReferences(XElement project) {
        var subs = project.Element("Subsystems");
        var gun = Group(subs, "ScGunBlockBehavior");
        // Legacy layouts have their own explicit migration/refusal policy; never reinterpret them as v5.
        if (Text(gun, "GunDataLayout") == ScGunRegistry.LegacyStamp.ToString()) return 0;
        var values = new ValuesDictionary();
        if (gun is not null) values.ApplyOverrides(new XElement(gun));
        ScGunSaveGuard.Validate(values);
        var table = values.GetValue<ValuesDictionary>("GunRegistry", null);
        if (table is null && values.ContainsKey("GunDataLayout"))
            throw new InvalidOperationException("枪械布局仍在，但 GunRegistry 记录表缺失；请恢复同一世界的完整备份，禁止初始化为空表");
        var registry = ScGunRegistry.Load(table, 0);
        if (registry.Disabled) throw new InvalidOperationException("枪械记录表无法安全读取");
        var maps = Group(subs, "BlocksManager")?.Elements("Value").Where(e => (string)e.Attribute("Value") == "ScGunBlock").ToArray() ?? [];
        if (maps.Length == 0 && registry.Count == 0 && registry.QuarantinedCount == 0) return 0; // new world
        if (maps.Length != 1) throw new InvalidOperationException("保存的枪械方块映射缺失或重复，不能验证旧物品");
        int block = Number((string)maps[0].Attribute("Name"));
        if (block is < 1 or > 1023) throw new InvalidOperationException("保存的枪械方块编号无效");
        int checkedItems = 0;
        void Item(int value, int count, string holder) {
            if (count <= 0 || Terrain.ExtractContents(value) != block) return;
            int data = Terrain.ExtractData(value), id = GunSpec.GetId(data), variant = GunSpec.GetVariant(data);
            if (GunSpec.IsForeign(data) || variant < 0 || variant >= GunSpec.All.Length)
                throw new InvalidOperationException($"{holder} 的枪械编码无法确认，原物品已保留");
            if (GunSpec.IsFresh(data)) return;
            if (count != 1 || !registry.TryGetSnapshot(id, out var state) || state.Variant != variant)
                throw new InvalidOperationException($"{holder} 引用枪械记录 #{id}（{ScGunNames.Variant(variant)}），但记录缺失、已隔离、型号不符或实例堆叠。已阻止进入和自动保存；请恢复该世界的升级前完整备份，不能用其他世界同编号的枪替代");
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
        return checkedItems;
    }
}
