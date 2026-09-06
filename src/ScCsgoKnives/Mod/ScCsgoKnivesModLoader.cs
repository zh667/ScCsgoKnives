using Engine;
using Engine.Graphics;

namespace Game;

public class ScCsgoKnivesModLoader : ModLoader {
    /// <summary>
    /// The version modinfo.json declares, so the log line cannot drift from the
    /// package the way a hardcoded string did -- 0.8.1 shipped announcing itself
    /// as 0.7.0, which made it look like the wrong build was installed.
    /// </summary>
    string ModVersion => Entity?.modInfo?.Version ?? "unknown";

    public override void __ModInitialize() {
        ModsManager.RegisterHook("OnLoadingFinished", this);
        ModsManager.RegisterHook("OnProjectDisposed", this);
        ModsManager.RegisterHook("OnPlayerSpawned", this);
        ModsManager.RegisterHook("BeforeWidgetUpdate", this);
        ModsManager.RegisterHook("AfterWidgetUpdate", this);
        ModsManager.RegisterHook("ChaseBehaviorScoreTarget", this);
        ModsManager.RegisterHook("UpdateChaseBehaviorChasing", this);
        ModsManager.RegisterHook("HandleMoveInventoryItem", this);
        ModsManager.RegisterHook("HandleInventoryDragMove", this);
        ModsManager.RegisterHook("UpdatePlayerInputDrop", this);
        ModsManager.RegisterHook("OnPlayerInputHit", this);
        ModsManager.RegisterHook("UpdatePlayerInputDig", this);
        ModsManager.RegisterHook("UpdatePlayerInputAim", this);
        ModsManager.RegisterHook("OnFirstPersonModelDrawing", this);
        ModsManager.RegisterHook("IsCrosshairVisible", this);   // hooks only fire for loaders that registered them (0.15.9 forgot this)
    }

    RecipaediaScreen m_assemblyClickScreen;
    int m_assemblyClickValue;
    public override void BeforeWidgetUpdate(Widget widget) {
        if (widget is RecipaediaScreen screen) {
            m_assemblyClickScreen = null;
            if (screen.m_recipesButton.IsClicked && screen.m_blocksList.SelectedItem is int value
                && ScWeaponCrafting.Find(value) is not null) {
                m_assemblyClickScreen = screen; m_assemblyClickValue = value;
            }
        }
    }
    public override void AfterWidgetUpdate(Widget widget) {
        if (widget is not RecipaediaScreen screen) return;
        if (screen.m_blocksList.SelectedItem is int value && ScWeaponCrafting.Find(value) is not null) {
            screen.m_recipesButton.Text = "装配配方";
            screen.m_recipesButton.IsEnabled = true;
        }
        // Vanilla temporarily disables an empty nine-grid recipe button and
        // UpdateCeases clears its click. Capture before that happens; navigate
        // after the vanilla update, without replacing the shared help screen.
        if (m_assemblyClickScreen == screen) {
            m_assemblyClickScreen = null;
            if (ScreensManager.CurrentScreen == screen) {
                ScreensManager.m_screens["RecipaediaRecipes"] = new ScAssemblyRecipesScreen();
                ScreensManager.SwitchScreen("RecipaediaRecipes", m_assemblyClickValue);
            }
        }
    }

    public override bool OnPlayerSpawned(PlayerData.SpawnMode spawnMode, ComponentPlayer player, Vector3 position) {
        if (player is null) return false;
        var project = player.Project;
        project.FindSubsystem<SubsystemScStarterEquipment>(true).TryGrant(
            project.FindSubsystem<SubsystemGameInfo>(true).WorldSettings.GameMode, spawnMode,
            player.PlayerData.PlayerIndex, player.PlayerData.SpawnsCount, player.ComponentMiner.Inventory,
            (value, count) => project.FindSubsystem<SubsystemPickables>(true).AddPickable(value, count, position + Vector3.UnitY * .5f, null, null));
        return false;
    }

    public override void ChaseBehaviorScoreTarget(ComponentChaseBehavior chase, ComponentCreature target, ref float score) =>
        chase.Project.FindSubsystem<SubsystemScGrenades>()?.ScoreTarget(chase, target, ref score);
    public override void UpdateChaseBehaviorChasing(ComponentChaseBehavior chase) =>
        chase.Project.FindSubsystem<SubsystemScGrenades>()?.ApplyChaseOcclusion(chase);

    public override void OnPlayerInputHit(ComponentPlayer player, ref bool operated, ref double interval, ref float range, bool skipped, out bool skipVanilla) {
        bool knife = SubsystemScKnifeBlockBehavior.HoldingKnife(player);
        skipVanilla = knife || SubsystemScGrenades.Holding(player) || Terrain.ExtractContents(player.ComponentMiner.ActiveBlockValue) == BlocksManager.GetBlockIndex<ScGunBlock>(true);
        if (SubsystemScGrenades.Holding(player) && !operated && !skipped) {
            player.Project.FindSubsystem<SubsystemScGrenades>(true).RequestThrow(player, !ScMobileControls.UsesTouchInput(player) && player.ComponentInput.PlayerInput.Aim.HasValue); operated = true;
        }
        if (knife && !operated && !skipped) {
            player.Project.FindSubsystem<SubsystemScKnifeBlockBehavior>(true).RequestAttack(player, !ScMobileControls.UsesTouchInput(player) && player.ComponentInput.PlayerInput.Aim.HasValue);
            operated = true;
        }
    }
    public override void UpdatePlayerInputDig(ComponentPlayer player, bool digging, ref bool operated, ref double interval, bool skipped, out bool skipVanilla) {
        bool knife = SubsystemScKnifeBlockBehavior.HoldingKnife(player);
        skipVanilla = knife || SubsystemScGrenades.Holding(player) || Terrain.ExtractContents(player.ComponentMiner.ActiveBlockValue) == BlocksManager.GetBlockIndex<ScGunBlock>(true);
        if (SubsystemScGrenades.Holding(player) && digging && !operated && !skipped) {
            player.Project.FindSubsystem<SubsystemScGrenades>(true).RequestThrow(player, !ScMobileControls.UsesTouchInput(player) && player.ComponentInput.PlayerInput.Aim.HasValue); operated = true;
        }
        if (knife && digging && !operated && !skipped) {
            player.Project.FindSubsystem<SubsystemScKnifeBlockBehavior>(true).RequestAttack(player, !ScMobileControls.UsesTouchInput(player) && player.ComponentInput.PlayerInput.Aim.HasValue);
            operated = true;
        }
    }
    public override void UpdatePlayerInputAim(ComponentPlayer player, bool aiming, ref bool operated, ref float interval, bool skipped, out bool skipVanilla) {
        skipVanilla = SubsystemScKnifeBlockBehavior.HoldingKnife(player) || SubsystemScGrenades.Holding(player);
        if (ScMobileControls.UsesTouchInput(player)) {
            // Touch Hold emits Dig and Aim together. Only explicit buttons select
            // secondary actions; leave Dig available for the primary action.
            skipVanilla |= Terrain.ExtractContents(player.ComponentMiner.ActiveBlockValue) == BlocksManager.GetBlockIndex<ScGunBlock>(true);
            if (skipVanilla) { player.m_aim = null; player.m_aimStartTime = null; }
            return;
        }
        if (SubsystemScGrenades.Holding(player) && aiming && !operated && !skipped) {
            player.Project.FindSubsystem<SubsystemScGrenades>(true).RequestThrow(player, true); operated = true;
        }
        if (skipVanilla && aiming && !operated && !skipped) {
            player.Project.FindSubsystem<SubsystemScKnifeBlockBehavior>(true).RequestAttack(player, true); operated = true;
        }
    }

    public override void HandleMoveInventoryItem(InventorySlotWidget widget, IInventory source, int sourceSlot, IInventory target, int targetSlot, ref int count, out bool moved) {
        ScInventoryTransaction.Changed(source); ScInventoryTransaction.Changed(target); moved = false;
    }
    public override void HandleInventoryDragMove(InventorySlotWidget widget, IInventory source, int sourceSlot, IInventory target, int targetSlot, bool skipped, out bool skip) {
        ScInventoryTransaction.Changed(source); ScInventoryTransaction.Changed(target); skip = false;
    }
    public override void OnPlayerInputDrop(ComponentPlayer player, bool skipped, out bool skipVanilla) {
        ScInventoryTransaction.Changed(player.ComponentMiner.Inventory); skipVanilla = false;
    }

    public override void OnProjectDisposed() => ScResourceCaches.ClearAll();

    public override void OnLoadingFinished(List<Action> actions) {
        ScResourcePolicy.LoadEdition();
        int index = BlocksManager.GetBlockIndex<ScKnifeBlock>(true);
        int[] values = BlocksManager.Blocks[index].GetCreativeValues().ToArray();
        int gunIndex = BlocksManager.GetBlockIndex<ScGunBlock>(true);
        Log.Information($"[ScCsgoKnives] {ModVersion} initialized. block={index}, knives={CsmcKnifeRig.KnifeCount}, creativeValues={values.Length}, gunBlock={gunIndex}, guns={GunSpec.All.Length}.");

        // Every creative item must survive the round trip through the block
        // value and land on its own asset. A stale variant clamp left over from
        // the three-knife build silently mapped everything past the butterfly
        // onto the butterfly's model and animations, and only the inventory
        // icon gave it away.
        for (int variant = 0; variant < CsmcKnifeRig.KnifeCount; variant++) {
            int value = Terrain.MakeBlockValue(index, 0, variant);
            int roundTrip = ScKnifeBlock.GetVariant(value);
            string expected = CsmcKnifeRig.GetAssetName(variant);
            string actual = CsmcKnifeRig.GetAssetName(roundTrip);
            if (roundTrip != variant || actual != expected) {
                Log.Error($"[ScCsgoKnives] variant {variant} ({expected}) round-trips to {roundTrip} ({actual}); knives will share the wrong model.");
            }
        }

    }

    static int s_lastLoggedValue = int.MinValue;

    /// <summary>The vanilla crosshair is a fixed-size quad 50 units ahead, so it grows with the scope's FOV; the scope draws its own.</summary>
    public override void IsCrosshairVisible(ComponentAimingSights componentAimingSights, ref bool isVisible) {
        // Zoomed on the AUG / SG 553 the reticle is the scope's own dot, so the
        // vanilla crosshair goes too (it grew with the FOV in 0.20.0's video).
        if (CsmcFirstPersonRenderer.ScopeOverlayActive) isVisible = false;
    }

    public override void OnFirstPersonModelDrawing(ComponentFirstPersonModel componentFirstPersonModel, Camera camera, int itemValue, ref Matrix matrix, out bool skip) {
        skip = false;
        itemValue = componentFirstPersonModel.Project.FindSubsystem<SubsystemScGrenades>()?.ViewmodelValue(componentFirstPersonModel.m_componentPlayer,itemValue) ?? itemValue;
        int variant = KnifeAnimationController.ResolveVariant(itemValue);
        if (variant < 0) {
            KnifeAnimationController.Update(componentFirstPersonModel, itemValue);
            return;
        }

        int raw = Terrain.ExtractData(itemValue);
        KnifeRigPose pose = KnifeAnimationController.Update(componentFirstPersonModel, itemValue);
        // Logged whenever the held value changes, so the hook's view of the item
        // can be compared against what ScKnifeBlock.DrawBlock sees.
        if (itemValue != s_lastLoggedValue) {
            s_lastLoggedValue = itemValue;
            Log.Information(
                $"[ScCsgoKnives] hook: value={itemValue} (0x{itemValue:X}), data={Terrain.ExtractData(itemValue)}, "
                + $"rawVariant={raw}, assetCount={CsmcKnifeRig.KnifeCount}, clamped={variant}, "
                + $"asset={CsmcKnifeRig.GetAssetName(variant)}, poseNull={pose is null}, "
                + $"activeBlockValue={componentFirstPersonModel.m_componentMiner.ActiveBlockValue}, m_value={componentFirstPersonModel.m_value}."
            );
        }
        if (pose is null) return;

        // The complete CSMC renderer owns weapon and arms. Returning skip=true
        // prevents SC from applying block offsets, generic poke/swap animation,
        // or drawing the old approximate model on top.
        //
        // The render states are put back the way the engine had them: our draws set
        // their own (0.20.1 made the PBR pass set Opaque, after the AUG lens batch
        // left Additive behind and the arms came out translucent), and whatever the
        // engine draws after this hook expects the state it set, not ours.
        BlendState blend = Display.BlendState;
        DepthStencilState depth = Display.DepthStencilState;
        RasterizerState rasterizer = Display.RasterizerState;
        try {
            skip = CsmcFirstPersonRenderer.Draw(componentFirstPersonModel, camera, variant, pose);
        }
        finally {
            Display.BlendState = blend;
            Display.DepthStencilState = depth;
            Display.RasterizerState = rasterizer;
        }
    }
}
