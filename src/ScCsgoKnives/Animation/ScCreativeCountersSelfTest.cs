using Engine;
using TemplatesDatabase;
using System.Xml.Linq;
namespace Game;

public static class ScCreativeCountersSelfTest {
    public static void Run(Action<string, bool, string> check) {
        var original = ScGunRegistry.Current;
        var locator = ScGunMutation.HolderLocator;
        var oldTypes = BlocksManager.BlockTypeToIndex.ToArray();
        var oldGun = BlocksManager.Blocks[510]; var oldTemplate = BlocksManager.Blocks[511];
        var gun = new ScGunBlock { BlockIndex = 510, MaxStacking = 1 };
        var template = new ScGunCounterTemplateBlock { BlockIndex = 511 };
        void T(string name, Func<bool> test) {
            ScGunRegistry.Current = new(); ScGunMutation.HolderLocator = null;
            try { check("creative-counters/" + name, test(), name); }
            catch (Exception e) { check("creative-counters/" + name, false, e.ToString()); }
        }
        ComponentCreativeInventory Creative(int value) {
            var inv = new ComponentCreativeInventory { OpenSlotsCount = 10 };
            for (int i = 0; i < 10; i++) inv.m_slots.Add(0);
            inv.AddSlotItems(0, value, 1); inv.m_slots.AddRange(template.GetCreativeValues()); return inv;
        }
        bool Ready(IInventory inv, ScGunSnapshot expected) {
            int value = inv.GetSlotValue(0);
            return Terrain.ExtractContents(value) == 510 && ScGunBlock.IsKnown(value)
                && KnifeAnimationController.ResolveVariant(value) == ScGunBlock.AssetIndex(expected.Variant)
                && GunSpec.TryGetSnapshot(Terrain.ExtractData(value), out var s) && !s.Fresh && s.CounterInstalled
                && s.SkinId == expected.SkinId && s.Variant == expected.Variant && s.KillCount == 0 && s.Level == 0
                && s.Rounds == GunSpec.All[s.Variant].Magazine && s.Durability == s.MaxDurability;
        }
        try {
            BlocksManager.BlockTypeToIndex[typeof(ScKnifeBlock)] = 509;
            BlocksManager.BlockTypeToIndex[typeof(ScGunBlock)] = 510; BlocksManager.Blocks[510] = gun;
            BlocksManager.BlockTypeToIndex[typeof(ScGunCounterTemplateBlock)] = 511; BlocksManager.Blocks[511] = template;
            var values = template.GetCreativeValues().ToArray();
            T("catalogue-stable-unique-all-guns-and-finishes", () => values.Length == GunSpec.All.Length + ScGunSkinCatalog.All.Count()
                && values.Distinct().Count() == values.Length && values.SequenceEqual(template.GetCreativeValues())
                && values.All(v => ScGunCounterTemplateBlock.TrySnapshot(v, out var s) && s.CounterInstalled && s.Fresh)
                && ScGunRegistry.Current.Count == 0);
            foreach (int value in values) {
                ScGunCounterTemplateBlock.TrySnapshot(value, out var source);
                string key = GunSpec.All[source.Variant].Name + "/" + source.SkinId;
                T("creative-take-and-animation/" + key, () => {
                    var inv = Creative(value); var catalogue = inv.m_slots.Skip(10).ToArray();
                    return ScGunCounterTemplateBlock.Materialize(inv, 0, "player") == ScGunResult.Success
                        && Ready(inv, source) && catalogue.SequenceEqual(inv.m_slots.Skip(10)) && ScGunRegistry.Current.Count == 1;
                });
                T("survival-take-mutate-and-two-xml-roundtrips/" + key, () => {
                    var inv = new ComponentInventory(); inv.m_slots.Add(new()); inv.AddSlotItems(0, value, 1);
                    if (ScGunCounterTemplateBlock.Materialize(inv, 0, "player") != ScGunResult.Success || !Ready(inv, source)) return false;
                    int actual = inv.GetSlotValue(0), id = GunSpec.GetId(Terrain.ExtractData(actual));
                    var tx = ScGunMutation.Prepare(inv, 0, "player", out _);
                    if (tx is null || tx.Commit(r => { r.Rounds--; r.Durability--; r.KillCount = 7; }) != ScGunResult.Success) return false;
                    if (ScGunCounterTemplateBlock.IsTemplate(inv.GetSlotValue(0))) return false; // next Update cannot reset this gun
                    for (int n = 0; n < 2; n++) {
                        var saved = new ValuesDictionary(); inv.Save(saved, null); saved.SetValue("SlotsCount", 1);
                        saved.SetValue("GunRegistry", ScGunRegistry.Current.Save(0));
                        var xml = new XElement("Values"); saved.Save(xml);
                        var loaded = new ValuesDictionary(); loaded.ApplyOverrides(XElement.Parse(xml.ToString()));
                        ScGunRegistry.Current = ScGunRegistry.Load(loaded.GetValue<ValuesDictionary>("GunRegistry"), 0);
                        inv = new ComponentInventory(); inv.Load(loaded, null);
                    }
                    return inv.GetSlotValue(0) == actual && inv.GetSlotCount(0) == 1
                        && ScGunRegistry.Current.TryGetSnapshot(id, out var s) && s.CounterInstalled && s.KillCount == 7
                        && s.SkinId == source.SkinId && s.Variant == source.Variant && s.Rounds == source.Rounds - 1
                        && s.Durability == source.Durability - 1 && ScGunBlock.IsKnown(actual);
                });
            }
            T("two-takes-have-independent-records", () => {
                var inv = Creative(values[0]); inv.AddSlotItems(1, values[0], 1);
                return ScGunCounterTemplateBlock.Materialize(inv, 0, "A") == ScGunResult.Success
                    && ScGunCounterTemplateBlock.Materialize(inv, 1, "B") == ScGunResult.Success
                    && inv.GetSlotValue(0) != inv.GetSlotValue(1) && ScGunRegistry.Current.Count == 2;
            });
            T("readonly-catalogue-is-never-converted", () => {
                var inv = Creative(values[0]); int before = inv.GetSlotValue(10);
                return ScGunCounterTemplateBlock.Materialize(inv, 10, "catalogue") == ScGunResult.Invalid
                    && inv.GetSlotValue(10) == before && ScGunRegistry.Current.Count == 0;
            });
            T("full-table-preserves-item", () => {
                while (!ScGunRegistry.Current.IsFull) ScGunRegistry.Current.Allocate(0, 1, false, 1);
                var inv = Creative(values[0]);
                return ScGunCounterTemplateBlock.Materialize(inv, 0, "player") == ScGunResult.RegistryFull && inv.GetSlotValue(0) == values[0];
            });
            T("invalid-template-preserved", () => {
                int value = Terrain.MakeBlockValue(511, 0, 9999); var inv = Creative(value);
                return ScGunCounterTemplateBlock.Materialize(inv, 0, "player") == ScGunResult.Invalid && inv.GetSlotValue(0) == value && ScGunRegistry.Current.Count == 0;
            });
            T("failed-template-does-not-draw-over-camera", () => {
                var matrix = Matrix.Identity;
                template.DrawBlock(null, values[0], Color.White, 1, ref matrix, new DrawBlockEnvironmentData { DrawBlockMode = DrawBlockMode.FirstPerson });
                return true; // no renderer/assets touched before the guard
            });
        }
        finally {
            ScGunRegistry.Current = original; ScGunMutation.HolderLocator = locator;
            BlocksManager.BlockTypeToIndex.Clear(); foreach (var p in oldTypes) BlocksManager.BlockTypeToIndex[p.Key] = p.Value;
            BlocksManager.Blocks[510] = oldGun; BlocksManager.Blocks[511] = oldTemplate;
        }
    }
}
