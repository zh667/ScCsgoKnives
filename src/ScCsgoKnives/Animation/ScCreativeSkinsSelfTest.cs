using TemplatesDatabase;
using System.Xml.Linq;
namespace Game;

public static class ScCreativeSkinsSelfTest {
    public static void Run(Action<string, bool, string> check) {
        var original = ScGunRegistry.Current; var locator = ScGunMutation.HolderLocator;
        bool hadGun = BlocksManager.BlockTypeToIndex.TryGetValue(typeof(ScGunBlock), out int oldGun);
        bool hadTemplate = BlocksManager.BlockTypeToIndex.TryGetValue(typeof(ScGunSkinTemplateBlock), out int oldTemplate);
        var old510 = BlocksManager.Blocks[510]; var old511 = BlocksManager.Blocks[511];
        var gun = new ScGunBlock { BlockIndex = 510, MaxStacking = 1 };
        var template = new ScGunSkinTemplateBlock { BlockIndex = 511 };
        void T(string name, Func<bool> f) {
            ScGunRegistry.Current = new(); ScGunMutation.HolderLocator = null;
            try { check("creative-skins/" + name, f(), name); } catch (Exception e) { check("creative-skins/" + name, false, e.ToString()); }
        }
        ComponentCreativeInventory Inventory(int value) {
            var i = new ComponentCreativeInventory { OpenSlotsCount = 10 };
            for (int n = 0; n < 10; n++) i.m_slots.Add(0);
            i.AddSlotItems(0, value, 1); i.m_slots.AddRange(template.GetCreativeValues()); return i;
        }
        int Source(int paint) => Terrain.MakeBlockValue(511, 0, paint);
        try {
            BlocksManager.BlockTypeToIndex[typeof(ScGunBlock)] = 510; BlocksManager.Blocks[510] = gun;
            BlocksManager.BlockTypeToIndex[typeof(ScGunSkinTemplateBlock)] = 511; BlocksManager.Blocks[511] = template;
            T("catalogue-44-stable-no-records", () => {
                int[] a = template.GetCreativeValues().ToArray(), b = template.GetCreativeValues().ToArray();
                return a.Length == 44 && a.SequenceEqual(b) && a.Distinct().Count() == 44 && ScGunRegistry.Current.Count == 0
                    && a.All(v => ScGunSkinTemplateBlock.TrySnapshot(v, out var s) && s.SkinId == Terrain.ExtractData(v) && s.Fresh)
                    && template.GetCategory(a[0]) == "Weapons" && template.GetDisplayOrder(a[0]) > 221 && template.MaxStacking == 1;
            });
            foreach (var skin in ScGunSkinCatalog.All) T("take-" + skin.Key, () => {
                int value = Source(skin.PaintId); var inv = Inventory(value);
                var catalog = inv.m_slots.Skip(10).ToArray();
                if (ScGunSkinTemplateBlock.Materialize(inv, 0, "player") != ScGunResult.Success) return false;
                int actual = inv.GetSlotValue(0);
                return Terrain.ExtractContents(actual) == 510 && GunSpec.TryGetSnapshot(Terrain.ExtractData(actual), out var s)
                    && !s.Fresh && s.SkinId == skin.PaintId && GunSpec.All[s.Variant].Name == skin.Gun
                    && s.Rounds == GunSpec.All[s.Variant].Magazine && s.Durability == s.MaxDurability
                    && catalog.SequenceEqual(inv.m_slots.Skip(10)) && ScGunRegistry.Current.Count == 1;
            });
            T("two-takes-independent-and-persist", () => {
                var inv = Inventory(Source(180));
                if (ScGunSkinTemplateBlock.Materialize(inv, 0, "A") != ScGunResult.Success) return false;
                inv.AddSlotItems(1, Source(180), 1);
                if (ScGunSkinTemplateBlock.Materialize(inv, 1, "B") != ScGunResult.Success) return false;
                int first = Terrain.ExtractData(inv.GetSlotValue(0)), second = Terrain.ExtractData(inv.GetSlotValue(1));
                var tx = ScGunMutation.Prepare(inv, 0, "A", out _);
                if (tx.Commit(r => { r.Rounds--; r.Durability--; }) != ScGunResult.Success) return false;
                var registry = ScGunRegistry.Current;
                for (int n = 0; n < 2; n++) {
                    var xml = new XElement("Values"); registry.Save(0).Save(xml);
                    var values = new ValuesDictionary(); values.ApplyOverrides(XElement.Parse(xml.ToString())); registry = ScGunRegistry.Load(values, 0);
                }
                return GunSpec.GetId(first) != GunSpec.GetId(second) && registry.TryGetSnapshot(GunSpec.GetId(first), out var a)
                    && registry.TryGetSnapshot(GunSpec.GetId(second), out var b) && a.SkinId == 180 && b.SkinId == 180
                    && a.Rounds == b.Rounds - 1 && a.Durability == b.Durability - 1;
            });
            T("readonly-catalogue-refused-and-not-listed", () => {
                var inv = Inventory(Terrain.MakeBlockValue(510, 0, GunSpec.MakeData(0, 30)));
                inv.m_slots.Add(inv.GetSlotValue(0));
                return ScGunMutation.Prepare(inv, 10, "catalogue", out _) is null && ScGunRegistry.Current.Count == 0
                    && ScWeaponSkinning.Candidates(inv, 510).Select(c => c.Slot).SequenceEqual(new[] { 0 })
                    && ScWeaponSkinning.Prepare(inv, inv.SlotsCount - 1, ScGunSkinCatalog.Find(180), true, ScWeaponMaterialBlock.Value) is null;
            });
            T("registry-full-keeps-template", () => {
                while (!ScGunRegistry.Current.IsFull) ScGunRegistry.Current.Allocate(0, 1, false, 1);
                int value = Source(180); var inv = Inventory(value);
                return ScGunSkinTemplateBlock.Materialize(inv, 0, "player") == ScGunResult.RegistryFull && inv.GetSlotValue(0) == value;
            });
            T("post-publication-rollback-keeps-template", () => {
                var inv = Inventory(Source(180)); var tx = ScGunMutation.Prepare(inv, 0, "A", out _);
                tx.AfterRecordWrite = () => throw new InvalidOperationException("injected");
                return tx.Commit(_ => { }) == ScGunResult.InventoryRejected && inv.GetSlotValue(0) == Source(180)
                    && ScGunRegistry.Current.Count == 0 && ScGunRegistry.Current.QuarantinedCount == 1 && ScGunRegistry.Current.Next == 2;
            });
            T("container-template-save-before-use", () => {
                var inv = new ComponentInventory(); inv.m_slots.Add(new()); inv.AddSlotItems(0, Source(456), 1);
                var data = new ValuesDictionary(); inv.Save(data, null); data.SetValue("SlotsCount", 1);
                var restored = new ComponentInventory(); restored.Load(data, null);
                return restored.GetSlotValue(0) == Source(456) && ScGunRegistry.Current.Count == 0
                    && ScGunSkinTemplateBlock.Materialize(restored, 0, "player") == ScGunResult.Success
                    && GunSpec.GetSkinId(Terrain.ExtractData(restored.GetSlotValue(0))) == 456;
            });
            T("material-colour-and-pbr-same-stem", () => {
                foreach (var skin in ScGunSkinCatalog.All) {
                    string selected = ScGunVisualMaterial.Resolve(skin.Gun, skin.PaintId, key => key, out string material);
                    if (selected != skin.Material || material != skin.Material) return false;
                    selected = ScGunVisualMaterial.Resolve(skin.Gun, skin.PaintId, key => key == skin.Material ? null : key, out material);
                    if (selected != skin.Gun + "_hd" || selected != material) return false;
                }
                return true;
            });
        }
        finally {
            ScGunRegistry.Current = original; ScGunMutation.HolderLocator = locator;
            BlocksManager.Blocks[510] = old510; BlocksManager.Blocks[511] = old511;
            if (hadGun) BlocksManager.BlockTypeToIndex[typeof(ScGunBlock)] = oldGun; else BlocksManager.BlockTypeToIndex.Remove(typeof(ScGunBlock));
            if (hadTemplate) BlocksManager.BlockTypeToIndex[typeof(ScGunSkinTemplateBlock)] = oldTemplate; else BlocksManager.BlockTypeToIndex.Remove(typeof(ScGunSkinTemplateBlock));
        }
    }
}
