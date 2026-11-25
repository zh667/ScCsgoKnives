namespace Game;

/// <summary>1.6.0 (feedback-1124-170 D17): the CS weapons' rewards in the survivor case, by CS-style tier. The case files keep
/// their vanilla rewards; the knives (gold tier) are added here too (1.6.0: other mods' integration files are not read).
/// <list type="table">
/// <item>common / 普通: metal parts 2 or 3, mechanical parts 1, magazines 2, shells 12, one throwable</item>
/// <item>uncommon / 稀有: grip, scope, paint supplies 1 or 2, defuse kit (with the agents package)</item>
/// <item>rare / 史诗 (7 %): a factory gun (no Zeus); 2026-11-25: guns weigh 6 against the vanilla 7.75</item>
/// <item>epic / 传说 (2.5 %): a painted gun or a gun with a kill counter (catalogue templates; user decision 2026-11-24: the
/// rarer tier holds the more valuable guns); 2026-11-25: painted 6, counter 3, plus the vanilla items moved down from gold</item>
/// <item>legendary / 金色: only knives, each with its CS finish where it has one (user decision 2026-11-25: gold means a
/// skinned knife; the vanilla gold rewards moved to the epic tier in survivor.json)</item>
/// </list>
/// Every gun reward (including the templates, which are guns) carries its crafting level: the opener only draws guns their
/// level could craft (D17). The relative weights inside a tier are first-pass values (see the task's P-values).</summary>
public static class CsCaseRewards {
    public const string TargetCase = "survivor";
    public const double ComponentWeight = 2, AmmoWeight = 2, ThrowableWeight = 1.5, PartWeight = 1.5, PaintWeight = 1, KitWeight = .75;
    public const double PaintedGunsWeight = 6, CounterGunsWeight = 3, GunsWeight = 6, KnivesWeight = 1;

    /// <summary>Adds the CS rewards to the survivor case. Returns how many were added.</summary>
    public static int Append(IEnumerable<CaseDefinition> cases) {
        var target = cases.FirstOrDefault(c => string.Equals(c.Id, TargetCase, StringComparison.OrdinalIgnoreCase));
        if (target is null) return 0;
        target.Rewards.RemoveAll(r => r.Generated);
        var rewards = Build().ToList();
        target.Rewards.AddRange(rewards);
        return rewards.Count;
    }

    static RewardDefinition R(CaseRarity rarity, int value, int count, double weight, int level = 0) => new() {
        Rarity = rarity, Block = BlocksManager.Blocks[Terrain.ExtractContents(value)].GetType().Name, Data = Terrain.ExtractData(value),
        Value = value, Count = count, Weight = weight, MinPlayerLevel = level, Generated = true
    };

    /// <summary>The crafting level of the gun an item value is (a gun, a painted or a counter template); 0 for anything else.</summary>
    public static int GunLevel(int value) => EffectiveGunStats.TrySnapshotValue(value, out var s) && s.Variant >= 0 && s.Variant < GunSpec.All.Length
        ? ScWeaponCrafting.RequiredPlayerLevel(GunSpec.All[s.Variant].Name) : 0;

    public static IEnumerable<RewardDefinition> Build() {
        int Material(int kind) => ScWeaponMaterialBlock.Value(kind);
        yield return R(CaseRarity.Common, Material(ScWeaponMaterialBlock.Blank), 2, ComponentWeight / 2);
        yield return R(CaseRarity.Common, Material(ScWeaponMaterialBlock.Blank), 3, ComponentWeight / 2);
        yield return R(CaseRarity.Common, Material(ScWeaponMaterialBlock.Mechanism), 1, ComponentWeight);
        yield return R(CaseRarity.Common, ScAmmoBlock.Value(ScAmmoBlock.Magazine), 2, AmmoWeight);
        yield return R(CaseRarity.Common, ScAmmoBlock.Value(ScAmmoBlock.Shell), 12, AmmoWeight);
        int[] throwables = Enumerable.Range(0, ScGrenadeBlock.Names.Length).Where(ScGrenadeBlock.Enabled).ToArray();
        foreach (int kind in throwables) yield return R(CaseRarity.Common, ScGrenadeBlock.Value(kind), 1, ThrowableWeight / throwables.Length);

        yield return R(CaseRarity.Uncommon, Material(ScWeaponMaterialBlock.Grip), 1, PartWeight);
        yield return R(CaseRarity.Uncommon, Material(ScWeaponMaterialBlock.Optics), 1, PartWeight);
        yield return R(CaseRarity.Uncommon, Material(ScWeaponMaterialBlock.Paint), 1, PaintWeight / 2);
        yield return R(CaseRarity.Uncommon, Material(ScWeaponMaterialBlock.Paint), 2, PaintWeight / 2);
        // The defuse kit exists only with the agents package (its block offers no creative value without it).
        int kitIndex = BlocksManager.GetBlockIndex("ScTacticalDefuserBlock", false);
        if (kitIndex > 0 && BlocksManager.Blocks[kitIndex].GetCreativeValues().FirstOrDefault() is int kit && kit != 0)
            yield return R(CaseRarity.Uncommon, kit, 1, KitWeight);

        int painted = BlocksManager.GetBlockIndex<ScGunSkinTemplateBlock>(true), counter = BlocksManager.GetBlockIndex<ScGunCounterTemplateBlock>(true);
        int[] paintedGuns = BlocksManager.Blocks[painted].GetCreativeValues().Where(v => GunLevel(v) > 0).ToArray();
        foreach (int v in paintedGuns) yield return R(CaseRarity.Epic, v, 1, PaintedGunsWeight / paintedGuns.Length, GunLevel(v));
        int[] counterGuns = BlocksManager.Blocks[counter].GetCreativeValues().Where(v => GunLevel(v) > 0).ToArray();
        foreach (int v in counterGuns) yield return R(CaseRarity.Epic, v, 1, CounterGunsWeight / counterGuns.Length, GunLevel(v));

        int gunBlock = BlocksManager.GetBlockIndex<ScGunBlock>(true);
        int[] guns = Enumerable.Range(0, GunSpec.All.Length).Where(v => GunSpec.All[v].Name != "taser").ToArray();
        foreach (int v in guns) yield return R(CaseRarity.Rare, Terrain.MakeBlockValue(gunBlock, 0, GunSpec.WithId(v, GunSpec.FreshFull)), 1,
            GunsWeight / guns.Length, ScWeaponCrafting.RequiredPlayerLevel(GunSpec.All[v].Name));
        foreach (var knife in Knives()) yield return knife;
    }

    /// <summary>The gold tier: every CS knife this edition has, in its CS finish (factory when the knife has none).</summary>
    static IEnumerable<RewardDefinition> Knives() {
        int knifeBlock = BlocksManager.GetBlockIndex<ScKnifeBlock>(true);
        int[] knives = Enumerable.Range(0, CsmcKnifeRig.KnifeCount).Where(ScMinimalEdition.KnifeAvailable).ToArray();
        foreach (int k in knives) yield return R(CaseRarity.Legendary, Terrain.MakeBlockValue(knifeBlock, 0, ScKnifeSkinCatalog.With(k, ScKnifeSkinCatalog.ForVariant(k))), 1, KnivesWeight / knives.Length);
    }

    /// <summary>What a player of this level may draw.</summary>
    public static Func<RewardDefinition, bool> EligibleFor(int playerLevel) => r => r.MinPlayerLevel <= playerLevel;
}
