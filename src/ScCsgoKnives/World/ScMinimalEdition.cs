namespace Game;

/// <summary>Compile-time presentation profile. Saved model/paint IDs remain the full catalogue.</summary>
public static class ScMinimalEdition {
#if SC_MINIMAL
    public const bool Enabled = true;
#else
    public const bool Enabled = false;
#endif
#if SC_MINIMAL_INSPECT
    public const bool InspectEnabled = true;
#else
    public const bool InspectEnabled = !Enabled;
#endif
    public static bool SkinAvailable(int paintId) => !Enabled || paintId == 0 || ScGunSkinCatalog.Find(paintId)?.Key is
        "cu_ak47_rubber" or "cu_anime_aug" or "am_lightning_awp" or "gs_famas_mecha" or "so_green" or
        "cu_galil_candychaos" or "gs_m249_nebula_crusader" or "cu_m4a1s_printstream" or "cu_m4a1_howling" or
        "gs_negev_thor" or "cu_scar20_intervention" or "cu_sg553_caution" or "cu_ssg08_dragonfire_scope";
    public static bool KnifeAvailable(int variant) => !Enabled || variant is 8 or 9;
}

#if SC_MINIMAL
// Stable native block type names retain old inventory values while tactical gameplay is absent.
public sealed class ScChickenEggBlock : ScCompatibilityItemBlock { public ScChickenEggBlock(){DefaultDisplayName="CS 小鸡生成蛋";MaxStacking=40;} }
public sealed class ScTacticalShieldBlock : ScCompatibilityItemBlock { public ScTacticalShieldBlock(){DefaultDisplayName="防爆盾";} }
public sealed class ScTacticalBeaconBlock : ScCompatibilityItemBlock { public ScTacticalBeaconBlock(){DefaultDisplayName="招募信标/战术维修包";MaxStacking=10;} }
public sealed class ScTacticalDefuserBlock : ScCompatibilityItemBlock { public ScTacticalDefuserBlock(){DefaultDisplayName="拆弹钳";} }
public sealed class ScTacticalSquadBlock : ScCompatibilityItemBlock { public ScTacticalSquadBlock(){DefaultDisplayName="敌队挑战信标";} }
#endif
