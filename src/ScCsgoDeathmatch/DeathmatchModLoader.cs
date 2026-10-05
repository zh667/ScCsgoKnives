using Engine;
using GameEntitySystem;
namespace Game;

/// <summary>The deathmatch package's entry (deathmatch-addon). What it does outside an arena world: registers its save
/// group with the core's compatibility capsule, its message numbers with the CS adapter, and the counter source that
/// answers "not mine" for every gun - nothing else. Every hook below returns at once unless the world's own
/// SubsystemScDeathmatch says the mode governs the player or the cell in question.</summary>
public sealed class DeathmatchModLoader : ModLoader {
    public const string SubsystemGuid = "38b04019-cbff-504d-8cd3-ae3c73af9cfa";
    static SubsystemScDeathmatch Of(Project project) => project?.FindSubsystem<SubsystemScDeathmatch>(false);
    static SubsystemScDeathmatch Current => Of(GameManager.Project);

    public override void __ModInitialize() {
        ScCompatibility.RegisterOwned("Subsystem", DmIds.Subsystem, SubsystemGuid);
        DmNet.Register();
        ScStatTrakRenderer.CounterSource = value => Current?.Counter(value);
        ScGunHolders.DormantSources[SubsystemScDeathmatch.HolderSource] = project => Of(project)?.ParkedItems() ?? [];
        foreach (string hook in new[] { "CalculateCreatureInjuryAmount", "TerrainChangeCell", "UpdatePlayerInputDrop", "HandleInventoryDragMove", "HandleMoveInventoryItem", "GuiUpdate", "GuiDraw", "OnPlayerSpawned", "OnSettingsScreenCreated" })
            ModsManager.RegisterHook(hook, this);
    }
    public override void CalculateCreatureInjuryAmount(Injury injury) => Of(injury?.ComponentHealth?.Project)?.OnInjury(injury);

    /// <summary>The arena's terrain does not change under a match (design §4.2): not by a player, an explosion or fire.</summary>
    public override void TerrainChangeCell(SubsystemTerrain subsystemTerrain, int x, int y, int z, int value, out bool skip) {
        skip = Of(subsystemTerrain?.Project) is { } dm && dm.ProtectsCell(x, y, z);
    }
    /// <summary>A governed player's equipment stays where the mode put it: it is not dropped... (the engine runs this override
    /// under the hook name "UpdatePlayerInputDrop"; dma2 registered "OnPlayerInputDrop" and so never prevented a drop)</summary>
    public override void OnPlayerInputDrop(ComponentPlayer componentPlayer, bool skippedByOtherMods, out bool skipVanilla) {
        skipVanilla = Of(componentPlayer?.Project) is { } dm && dm.Governs(componentPlayer);
    }
    /// <summary>...and not dragged or moved between inventories.</summary>
    public override void HandleInventoryDragMove(InventorySlotWidget inventorySlotWidget, IInventory sourceInventory, int sourceSlotIndex, IInventory targetInventory, int targetSlotIndex, bool skippedByOtherMods, out bool skip) {
        skip = Held(sourceInventory) || Held(targetInventory);
    }
    public override void HandleMoveInventoryItem(InventorySlotWidget inventorySlotWidget, IInventory sourceInventory, int sourceSlotIndex, IInventory targetInventory, int targetSlotIndex, ref int count, out bool moved) {
        moved = Held(sourceInventory) || Held(targetInventory);         // "moved" tells the engine the move was dealt with: nothing moves
    }
    static bool Held(IInventory inventory) => inventory is Component component && component.Entity?.FindComponent<ComponentPlayer>() is { } player && Of(player.Project) is { } dm && dm.Governs(player);

    public override bool OnPlayerSpawned(PlayerData.SpawnMode spawnMode, ComponentPlayer componentPlayer, Vector3 position) {
        if (Of(componentPlayer?.Project) is { Frozen: { } frozen } && ScNet.IsLocal(componentPlayer)) componentPlayer.ComponentGui?.DisplaySmallMessage(frozen, Color.White, false, false);
        return false;
    }
    /// <summary>The route to the deathmatch menu that needs neither a key nor a HUD button (a phone in a world that is not
    /// an arena world yet has neither): an entry on the game's own settings page, which the pause menu opens everywhere.</summary>
    public override void OnSettingsScreenCreated(SettingsScreen settingsScreen, out Dictionary<ButtonWidget, Action> buttonsToAdd) {
        var button = new BevelledButtonWidget { Text = "死亡竞赛", Size = new Vector2(310, 60) };
        buttonsToAdd = new Dictionary<ButtonWidget, Action> { [button] = () => {
            if (GameManager.Project is null) { DialogsManager.ShowDialog(null, new MessageDialog("死亡竞赛", "进入一个世界后再打开：竞技地图的启用和设置都在世界里进行。", "确定", null, null)); return; }
            DmHud.RequestMenu(); ScreensManager.SwitchScreen("Game");
        } };
    }
    public override void GuiUpdate(ComponentGui componentGui) => DmHud.Update(componentGui);
    public override void GuiDraw(ComponentGui componentGui, Camera camera, int drawOrder) => DmHud.Draw(componentGui, camera, drawOrder);
}
