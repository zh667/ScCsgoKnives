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
            foreach (bool creative in new[] { false, true }) foreach (int value in values.Take(GunSpec.All.Length)) {
                ScGunCounterTemplateBlock.TrySnapshot(value, out var source);
                string key = (creative ? "creative/" : "survival/") + GunSpec.All[source.Variant].Name;
                IInventory InventoryFor() {
                    if (creative) return Creative(value);
                    var inv = new ComponentInventory(); inv.m_slots.Add(new()); inv.AddSlotItems(0, value, 1); return inv;
                }
                T("runtime-growth-through-3000/" + key, () => {
                    var inv = InventoryFor();
                    if (ScGunCounterTemplateBlock.Materialize(inv, 0, "player") != ScGunResult.Success) return false;
                    var registry = ScGunRegistry.Current; int actual = inv.GetSlotValue(0); int id = GunSpec.GetId(Terrain.ExtractData(actual));
                    ScGunHolders.Holder[] holders = [new(id, "player", inv, 0)];
                    var reports = new List<(int Id, int From, int To)>();
                    int Advance(bool busy = false) => ScGunGrowthService.Advance(registry, holders, 30, _ => busy, (i, f, t) => reports.Add((i, f, t)));
                    bool SetKills(long kills) => ScGunMutation.Prepare(inv, 0, "player", out _)?.Commit(r => r.KillCount = kills) == ScGunResult.Success;
                    if (registry.GrowthMode != ScGunGrowthMode.Unset || !SetKills(99)) return false;
                    if (Advance() != 0 || registry.GrowthMode != ScGunGrowthMode.CountAndGrow || reports.Count != 0) return false;
                    var credit = ScGunKillCredit.For(Terrain.ExtractData(actual), creative, 1);
                    if (credit is null) return false;
                    registry.Kills.Enqueue(credit.RecordId, credit.Variant);
                    if (Advance(true) != 0 || registry.Get(id).KillCount != 99) return false; // reload/animation defers safely
                    if (Advance() != 1 || registry.Get(id).AppliedGrowthLevel != 1 || registry.Get(id).KillCount != 100 || reports.Single() != (id, 0, 1)) return false;
                    if (Advance() != 0 || reports.Count != 1 || inv.GetSlotValue(0) != actual) return false;
                    if (!SetKills(999) || Advance() != 1 || registry.Get(id).AppliedGrowthLevel != 9) return false;
                    registry.Kills.Enqueue(id, source.Variant);
                    if (Advance() != 1 || registry.Get(id).AppliedGrowthLevel != 10 || registry.Get(id).KillCount != 1000) return false;
                    if (reports.Count != 3 || reports.Last() != (id, 9, 10) || Advance() != 0) return false;
                    foreach(int boundary in new[]{1099,1999,2999}) {
                        if(!SetKills(boundary))return false;
                        Advance();
                        if(registry.Get(id).AppliedGrowthLevel!=boundary/100)return false;
                        registry.Kills.Enqueue(id,source.Variant);
                        if(Advance()!=1||registry.Get(id).KillCount!=boundary+1||registry.Get(id).AppliedGrowthLevel!=(boundary+1)/100)return false;
                    }
                    // Leveling never tops up ammo or creates a second gun. Persist applied levels and counts twice.
                    if (registry.Get(id).Rounds != source.Rounds || registry.Count != 1) return false;
                    for (int n = 0; n < 2; n++) {
                        var xml = new XElement("Values"); registry.Save(30).Save(xml);
                        var loaded = new ValuesDictionary(); loaded.ApplyOverrides(XElement.Parse(xml.ToString()));
                        registry = ScGunRegistry.Load(loaded, 30); ScGunRegistry.Current = registry;
                        if (Advance() != 0 || registry.Get(id).AppliedGrowthLevel != 30 || registry.Get(id).KillCount != 3000) return false;
                    }
                    return reports.Count == 8;
                });
                T("old-unset-104-catches-up-without-another-kill/" + key, () => {
                    var inv = InventoryFor();
                    if (ScGunCounterTemplateBlock.Materialize(inv, 0, "player") != ScGunResult.Success) return false;
                    var tx = ScGunMutation.Prepare(inv, 0, "player", out _);
                    if (tx.Commit(r => { r.KillCount = 104; r.Durability /= 2; r.Rounds = 0; }) != ScGunResult.Success) return false;
                    int id = tx.Id; var registry = ScGunRegistry.Current; int notices = 0;
                    int n = ScGunGrowthService.Advance(registry, [new(id, "player", inv, 0)], 30, _ => false, (_, f, t) => { if (f == 0 && t == 1) notices++; });
                    var s = registry.Get(id);
                    return n == 1 && notices == 1 && s.KillCount == 104 && s.AppliedGrowthLevel == 1 && s.Rounds == 0
                        && s.Durability > 0 && s.Durability < s.MaxDurability && registry.GrowthMode == ScGunGrowthMode.CountAndGrow;
                });
                T("explicit-count-only-stays-count-only/" + key, () => {
                    var inv = InventoryFor();
                    if (ScGunCounterTemplateBlock.Materialize(inv, 0, "player") != ScGunResult.Success) return false;
                    var registry = ScGunRegistry.Current; registry.GrowthMode = ScGunGrowthMode.CountOnly;
                    var tx = ScGunMutation.Prepare(inv, 0, "player", out _); if (tx.Commit(r => r.KillCount = 104) != ScGunResult.Success) return false;
                    registry.Kills.Enqueue(tx.Id, source.Variant); int notices = 0;
                    int n = ScGunGrowthService.Advance(registry, [new(tx.Id, "player", inv, 0)], 30, _ => false, (_, _, _) => notices++);
                    return n == 0 && notices == 0 && registry.Get(tx.Id).KillCount == 105 && registry.Get(tx.Id).AppliedGrowthLevel == 0
                        && registry.GrowthMode == ScGunGrowthMode.CountOnly;
                });
            }
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
