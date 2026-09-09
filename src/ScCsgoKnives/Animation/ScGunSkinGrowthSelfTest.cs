using TemplatesDatabase;
using System.Xml.Linq;
namespace Game;

/// <summary>Skin/counter operation order and derived damage. Fixtures never touch player worlds.</summary>
public static class ScGunSkinGrowthSelfTest {
    public static void Run(Action<string, bool, string> check) {
        var saved = ScGunRegistry.Current; var locator = ScGunMutation.HolderLocator;
        bool hadGun = BlocksManager.BlockTypeToIndex.TryGetValue(typeof(ScGunBlock), out int oldGun);
        var oldBlock = BlocksManager.Blocks[510];
        void T(string name, Func<bool> test) {
            ScGunRegistry.Current = new() { GrowthMode = ScGunGrowthMode.CountAndGrow }; ScGunMutation.HolderLocator = null;
            try { check("skin-growth/" + name, test(), name); } catch (Exception e) { check("skin-growth/" + name, false, e.ToString()); }
        }
        IInventory InventoryFor(int variant, bool creative) {
            int id = ScGunRegistry.Current.Allocate(variant, Math.Max(0, GunSpec.All[variant].Magazine - 1), true, ScGunDurability.Full(variant) / 2);
            IInventory inv;
            if (creative) { var c = new ComponentCreativeInventory { OpenSlotsCount = 10 }; for (int i = 0; i < 10; i++) c.m_slots.Add(0); inv = c; }
            else { var s = new ComponentInventory(); s.m_slots.Add(new()); inv = s; }
            inv.AddSlotItems(0, Terrain.MakeBlockValue(510, 0, GunSpec.WithId(variant, id)), 1); return inv;
        }
        ScGunSnapshot Snap(IInventory inv) { GunSpec.TryGetSnapshot(Terrain.ExtractData(inv.GetSlotValue(0)), out var s); return s; }
        bool Skin(IInventory inv, ScGunSkin skin) => ScWeaponSkinning.Apply(inv, ScWeaponSkinning.Prepare(inv, 0, skin, true, i => 950 + i), "player") == ScGunResult.Success;
        bool Install(IInventory inv) => ScGunCounter.Apply(inv, ScGunCounter.Prepare(inv, 0, true), "player") == ScGunResult.Success;
        bool SameState(ScGunSnapshot a, ScGunSnapshot b) => a with { SkinId = b.SkinId, Revision = b.Revision } == b;
        try {
            BlocksManager.BlockTypeToIndex[typeof(ScGunBlock)] = 510; BlocksManager.Blocks[510] = new ScGunBlock { BlockIndex = 510, MaxStacking = 1 };
            foreach (bool creative in new[] { false, true }) foreach (var skin in ScGunSkinCatalog.All) {
                int variant = Array.FindIndex(GunSpec.All, g => g.Name == skin.Gun); var spec = GunSpec.All[variant];
                string key = $"{creative}/{skin.Key}";
                T("all-levels-derived-damage/" + key, () => {
                    var inv = InventoryFor(variant, creative); int value = inv.GetSlotValue(0); var before = Snap(inv);
                    if (!Skin(inv, skin)) return false;
                    var painted = Snap(inv);
                    if (!SameState(before, painted) || painted.CounterInstalled || ScGunKillCredit.For(Terrain.ExtractData(value), creative, 1) is not null) return false;
                    if (Math.Abs(EffectiveGunStats.Resolve(spec, value, false).Power - ScSurvivalBalance.Power(spec.Name) * 1.5f) > .001f) return false;
                    for (int level = 0; level <= 30; level++) {
                        var stats = EffectiveGunStats.ResolveLevel(spec, value, false, level);
                        float expected = ScSurvivalBalance.Power(spec.Name) * 1.5f * (1 + Math.Min(level,10)*.1f + Math.Clamp(level-10,0,10)*.3f + Math.Clamp(level-20,0,10)*.5f);
                        if (Math.Abs(stats.Power - expected) > .001f || Math.Abs(stats.PelletPower(spec, 0) * spec.Pellets - expected) > .001f) return false;
                        var damage = ScGunAttributes.Rows(spec, value, level).First(r => r.Kind == ScGunAttributes.Kind.Damage);
                        if (Math.Abs(float.Parse(damage.Text, System.Globalization.CultureInfo.InvariantCulture) - expected) > .051f) return false;
                    }
                    return SameState(painted, Snap(inv)); // previews never install counter or mutate levels
                });
                foreach (bool skinFirst in new[] { false, true }) T($"operation-order/{key}/skinFirst={skinFirst}", () => {
                    var inv = InventoryFor(variant, creative); int original = inv.GetSlotValue(0); var before = Snap(inv);
                    // Before installation, no past kill can acquire a credit, including a skin-only gun.
                    if (skinFirst && !Skin(inv, skin)) return false;
                    if (ScGunKillCredit.For(Terrain.ExtractData(original), creative, 99) is not null || !Install(inv)) return false;
                    var installed = Snap(inv);
                    if (!installed.CounterInstalled || installed.KillCount != 0 || installed.Level != 0 || installed.Id != before.Id || installed.Rounds != before.Rounds) return false;
                    var tx = ScGunMutation.Prepare(inv, 0, "player", out _);
                    if (tx.Commit(r => { r.KillCount = 237; r.AppliedGrowthLevel = 2; r.MaxDurability = ScGunGrowth.MaxDurability(variant, 2); r.PendingGrowthLevel = 3; }) != ScGunResult.Success) return false;
                    var earned = Snap(inv);
                    if (!skinFirst && (!Skin(inv, skin) || !SameState(earned, Snap(inv)))) return false;
                    var painted = Snap(inv); var other = ScGunSkinCatalog.For(variant).First(s => s.PaintId != skin.PaintId);
                    if (!Skin(inv, other) || !SameState(painted, Snap(inv))) return false;
                    var recolored = Snap(inv);
                    if (!Skin(inv, null) || !SameState(recolored, Snap(inv))) return false;
                    if (!Skin(inv, skin) || !SameState(recolored, Snap(inv)) || inv.GetSlotValue(0) != original) return false;
                    if (ScGunCounter.Prepare(inv, 0, true) is not null) return false; // no reinstall/reset
                    var stale = ScWeaponSkinning.Prepare(inv, 0, other, true, i => 950 + i);
                    var registry = ScGunRegistry.Current; var credit = ScGunKillCredit.For(Terrain.ExtractData(original), creative, 100);
                    if (credit is null) return false;
                    registry.Kills.Enqueue(credit.RecordId, credit.Variant);
                    if (ScGunGrowthService.DrainKills(registry, [new(credit.RecordId, "player", inv, 0)], false) != 1) return false;
                    if (ScWeaponSkinning.Apply(inv, stale, "player") != ScGunResult.StateChanged || Snap(inv).KillCount != 238) return false;
                    // Actual inventory -> chest -> inventory, with the full registry saved/read twice.
                    var chest = new ComponentChest(); chest.m_slots.Add(new()); chest.AddSlotItems(0, original, 1);
                    if (inv is ComponentCreativeInventory ci) ci.m_slots[0] = 0; else inv.RemoveSlotItems(0, 1);
                    for (int round = 0; round < 2; round++) {
                        var data = new ValuesDictionary(); chest.Save(data, null); data.SetValue("SlotsCount", 1); data.SetValue("GunRegistry", registry.Save(0));
                        var xml = new XElement("Values"); data.Save(xml); var loaded = new ValuesDictionary(); loaded.ApplyOverrides(XElement.Parse(xml.ToString()));
                        registry = ScGunRegistry.Load(loaded.GetValue<ValuesDictionary>("GunRegistry"), 0); ScGunRegistry.Current = registry;
                        chest = new ComponentChest();
                        var entity = (GameEntitySystem.Entity)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GameEntitySystem.Entity));
                        entity.m_components = [chest]; chest.m_entity = entity;
                        chest.Load(loaded, null);
                    }
                    chest.RemoveSlotItems(0, 1); inv.AddSlotItems(0, original, 1);
                    var restored = Snap(inv);
                    bool kept = restored.CounterInstalled && restored.Id == before.Id && restored.KillCount == 238 && restored.Level == 2
                        && restored.PendingGrowthLevel == 3 && restored.SkinId == skin.PaintId && restored.Rounds == before.Rounds
                        && restored.Durability == before.Durability && restored.SilencerOff == before.SilencerOff;
                    if (!kept) return false;
                    var repair = ScWeaponRepair.Prepare(new(0, original), ScWeaponCrafting.All.First(e => !e.Knife && e.Variant == variant), true, i => 950 + i);
                    if (ScWeaponRepair.TryRepair(inv, repair, "player") != ScGunResult.Success) return false;
                    var repaired = Snap(inv);
                    return repaired.KillCount == restored.KillCount && repaired.Level == restored.Level && repaired.PendingGrowthLevel == 3
                        && repaired.CounterInstalled && repaired.Durability == repaired.MaxDurability && repaired.SkinId == skin.PaintId;
                });
            }
            foreach (var spec in GunSpec.All) T("factory-all-levels/" + spec.Name, () => {
                int v = Array.IndexOf(GunSpec.All, spec); var inv = InventoryFor(v, false); int value = inv.GetSlotValue(0);
                for (int level = 0; level <= 30; level++) {
                    var s = EffectiveGunStats.ResolveLevel(spec, value, false, level);
                    if (Math.Abs(s.Power - ScSurvivalBalance.Power(spec.Name) * (1 + Math.Min(level,10)*.1f + Math.Clamp(level-10,0,10)*.3f + Math.Clamp(level-20,0,10)*.5f)) > .001f
                        || s.Capacity != ScGunGrowth.Capacity(v, level) || s.RechargeSeconds != ScGunGrowth.RechargeSeconds(spec, level)) return false;
                }
                return EffectiveGunStats.LevelOf(value) == 0 && !Snap(inv).CounterInstalled;
            });
        }
        finally {
            ScGunRegistry.Current = saved; ScGunMutation.HolderLocator = locator; BlocksManager.Blocks[510] = oldBlock;
            if (hadGun) BlocksManager.BlockTypeToIndex[typeof(ScGunBlock)] = oldGun; else BlocksManager.BlockTypeToIndex.Remove(typeof(ScGunBlock));
        }
    }
}
