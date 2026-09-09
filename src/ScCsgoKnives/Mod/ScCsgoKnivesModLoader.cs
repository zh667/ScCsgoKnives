using Engine;
using Engine.Graphics;
using System.Xml.Linq;

namespace Game;

public class ScCsgoKnivesModLoader : ModLoader {
    /// <summary>
    /// The version modinfo.json declares, so the log line cannot drift from the
    /// package the way a hardcoded string did -- 0.8.1 shipped announcing itself
    /// as 0.7.0, which made it look like the wrong build was installed.
    /// </summary>
    string ModVersion => Entity?.modInfo?.Version ?? "unknown";

    public override void __ModInitialize() {
        ModsManager.RegisterHook("ProjectXmlLoad", this);
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
        ModsManager.RegisterHook("OnModelCalculateBones", this); // third person: pose the human's arms around the mod weapon
        ModsManager.RegisterHook("OnModelDrawExtra", this);     // third person: draw the real-scale weapon instead of vanilla's block
        ModsManager.RegisterHook("OnSettingsScreenCreated", this); // a mod settings entry a phone can actually reach
    }

    /// <summary>Adds the mod's settings entry to the game's own Settings screen, which the pause menu opens on
    /// every platform. That is the whole mobile settings route: no file to edit, and the entry cannot be hidden
    /// by switching the combat buttons off.</summary>
    public override void OnSettingsScreenCreated(SettingsScreen settingsScreen, out Dictionary<ButtonWidget, Action> buttonsToAdd) {
        // Match the game's own Settings button style/container (310x60). Do not
        // give this one a bespoke width or it becomes visibly shorter/longer.
        var button = new BevelledButtonWidget {
            Text = "CS 枪械", Style = ContentManager.Get<XElement>("Styles/ButtonStyle_310x60"),
            HorizontalAlignment = WidgetAlignment.Center, VerticalAlignment = WidgetAlignment.Center,
            Margin = new Vector2(0f, 5f)
        };
        buttonsToAdd = new Dictionary<ButtonWidget, Action> {
            [button] = () => { EnsureScreens(); ScreensManager.SwitchScreen(ScGunSettingsScreen.ScreenName); },
        };
    }

    /// <summary>Registers the mod's screens once. ScreensManager.AddScreen throws on a duplicate name.</summary>
    static void EnsureScreens() {
        if (!ScreensManager.m_screens.ContainsKey(ScGunSettingsScreen.ScreenName)) ScreensManager.AddScreen(ScGunSettingsScreen.ScreenName, new ScGunSettingsScreen());
        if (!ScreensManager.m_screens.ContainsKey(ScGunLayoutScreen.ScreenName)) ScreensManager.AddScreen(ScGunLayoutScreen.ScreenName, new ScGunLayoutScreen());
    }

    public override void ProjectXmlLoad(XElement project, WorldInfo world, ContainerWidget widget) {
        if (!ScGunSaveGuard.BeforeLoad(project)) return;
        ScGun0282Migration.BeforeLoad(project, world);
        // A world saved with an older record schema is about to be converted to one older builds cannot read.
        // Back it up first, and refuse the load rather than upgrade without a way back.
        try { ScGunSchemaUpgrade.BeforeLoad(project, world); }
        catch (Exception e) { ScGunSaveGuard.Refuse(project, "记录格式升级前的世界备份失败：" + e.Message); }
    }

    /// <summary>Third person (M3): after vanilla animates a human holding a mod weapon, both hands are re-posed
    /// around the weapon's CS2 grip points; vanilla's in-hand block draw is replaced by the baked weapon.</summary>
    public override void OnModelCalculateBones(ComponentModel componentModel, Camera camera, out bool skip) {
        skip = false; // vanilla still composes the absolute matrices; only the two hand bones were rewritten
        if (componentModel is not ComponentHumanModel human) return;
        try { ScThirdPerson.Pose(human, Time.FrameDuration); }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("third-person-pose", "third person pose: " + e); }
    }
    public override void OnModelDrawExtra(ComponentModel componentModel, Camera camera, out bool skip) {
        skip = false;
        if (componentModel is not ComponentHumanModel human) return;
        try { skip = ScThirdPerson.Draw(human, camera); }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("third-person-draw", "third person draw: " + e); }
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
                // A gun opens on its attribute card, which links to the recipe; a knife goes straight to the recipe.
                bool gun = Terrain.ExtractContents(m_assemblyClickValue) == BlocksManager.GetBlockIndex<ScGunBlock>(true);
                ScreensManager.m_screens["RecipaediaRecipes"] = gun ? new ScGunAttributesScreen(m_assemblyClickValue) : new ScAssemblyRecipesScreen();
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
        bool holdingGun = Terrain.ExtractContents(player.ComponentMiner.ActiveBlockValue) == BlocksManager.GetBlockIndex<ScGunBlock>(true);
        if (!ScMobileControls.UsesTouchInput(player)) {
            // PC guns: the scope / burst / silencer key acts on the press edge, once per press, with no vanilla aim
            // cooldown (1.4 s survival, 0.1 s creative) and no wait for the release. The button is tracked whatever
            // the item, so a button already held when a gun is drawn does not fire the action on the draw.
            SubsystemScGunBlockBehavior guns = null;
            try { guns = player.Project?.FindSubsystem<SubsystemScGunBlockBehavior>(false); } catch (NullReferenceException) { } // a bare test player has no entity
            bool acted = guns is not null && guns.AimPressed(player, aiming, holdingGun && !skipped);
            if (holdingGun) { skipVanilla = true; player.m_aim = null; player.m_aimStartTime = null; if (acted) operated = true; return; }
        }
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

    public override void OnProjectDisposed() { ScResourceCaches.ClearAll(); ScGunVisualMaterial.Clear(); }

    public override void OnLoadingFinished(List<Action> actions) {
        ScEnchantmentCompatibility.Initialize();
        ScResourcePolicy.LoadEdition();
        // Local interface settings: touch buttons, kill feedback and the gun crosshair. Never world data.
        ScUiSettings.Load();
        EnsureScreens();
        int index = BlocksManager.GetBlockIndex<ScKnifeBlock>(true);
        int[] values = BlocksManager.Blocks[index].GetCreativeValues().ToArray();
        int gunIndex = BlocksManager.GetBlockIndex<ScGunBlock>(true);
        int counterIndex = BlocksManager.GetBlockIndex<ScGunCounterTemplateBlock>(true);
        Log.Information($"[ScCsgoKnives] {ModVersion} initialized. block={index}, knives={CsmcKnifeRig.KnifeCount}, creativeValues={values.Length}, gunBlock={gunIndex}, counterTemplateBlock={counterIndex}, guns={GunSpec.All.Length}.");
        Log.Information("[GUN_FOLIAGE] bullet pass-through registry: " + string.Join(", ", BlocksManager.Blocks
            .Where(b => b is not null && b is not AirBlock && b is not FluidBlock && !ScGunRange.StopsBullet(b))
            .Select(b => $"{b.BlockIndex}:{b.GetType().FullName}").Distinct()));

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
        if (CsmcFirstPersonRenderer.ScopeOverlayActive) { isVisible = false; return; }
        // While the mod draws its own gun crosshair, vanilla's is suppressed so there is exactly one layer.
        // With an empty hand, a knife, a grenade or any vanilla tool this hook changes nothing at all.
        var player = componentAimingSights?.m_componentPlayer;
        if (player is null || !ScUiSettings.GunCrosshair || !ScGunCrosshair.HoldingGun(player)) return;
        SubsystemScGunBlockBehavior guns = null;
        try { guns = player.Project?.FindSubsystem<SubsystemScGunBlockBehavior>(false); } catch (NullReferenceException) { }
        if (guns is null) return;
        isVisible = false;
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
            // The finish is read from the held item's record here and cleared straight after, so nothing
            // else in the frame can draw a weapon under another weapon's skin.
            CsmcFirstPersonRenderer.SkinId = Terrain.ExtractContents(itemValue) == BlocksManager.GetBlockIndex<ScGunBlock>(true)
                ? ScGunBlock.SkinOf(itemValue) : ScGunSkinCatalog.None;
            skip = CsmcFirstPersonRenderer.Draw(componentFirstPersonModel, camera, variant, pose);
        }
        finally {
            CsmcFirstPersonRenderer.SkinId = ScGunSkinCatalog.None;
            Display.BlendState = blend;
            Display.DepthStencilState = depth;
            Display.RasterizerState = rasterizer;
        }
    }
}
