using Engine;
using TemplatesDatabase;
namespace Game;

public sealed class SubsystemScKnifeBlockBehavior : SubsystemBlockBehavior, IUpdateable {
    public override int[] HandledBlocks => [BlocksManager.GetBlockIndex<ScKnifeBlock>()];

    public UpdateOrder UpdateOrder => UpdateOrder.Default;

    readonly Dictionary<ComponentPlayer, ScKnifeStrike> m_strikes = [];
    readonly Dictionary<ComponentPlayer, ScWeaponTouchPanel> m_buttons = [];
    readonly Dictionary<int, double> m_savedRecovery = [];
    SubsystemTime m_time;
    SubsystemPlayers m_players;
    public override void Load(ValuesDictionary values) {
        base.Load(values);
        m_time = Project.FindSubsystem<SubsystemTime>(true);
        m_players = Project.FindSubsystem<SubsystemPlayers>(true);
        var saved = values.GetValue<ValuesDictionary>("KnifeRecovery", null);
        if (saved is not null) foreach (var item in saved)
            if (int.TryParse(item.Key, out int index) && item.Value is double remaining && double.IsFinite(remaining)) m_savedRecovery[index] = Math.Clamp(remaining, 0, 1);
    }
    public override void Save(ValuesDictionary values) {
        base.Save(values);
        var saved = new ValuesDictionary();
        foreach (var pair in m_savedRecovery) saved.SetValue(pair.Key.ToString(), pair.Value);
        foreach (var pair in m_strikes) saved.SetValue(pair.Key.PlayerData.PlayerIndex.ToString(), Math.Max(0, pair.Value.Next - m_time.GameTime));
        values.SetValue("KnifeRecovery", saved);
    }
    ScKnifeStrike State(ComponentPlayer player) {
        if (!m_strikes.TryGetValue(player, out var state)) {
            m_strikes[player] = state = new ScKnifeStrike();
            if (m_savedRecovery.Remove(player.PlayerData.PlayerIndex, out double remaining)) state.Next = m_time.GameTime + remaining;
        }
        return state;
    }
    public static bool HoldingKnife(ComponentPlayer player) => Terrain.ExtractContents(player.ComponentMiner.ActiveBlockValue) == BlocksManager.GetBlockIndex<ScKnifeBlock>(true);
    static bool CanOperate(ComponentPlayer player) => player.ComponentHealth.Health > 0 && player.ComponentGui.ModalPanelWidget is null && !DialogsManager.HasDialogs(player.GuiWidget);
    public void RequestAttack(ComponentPlayer player, bool heavy) {
        if (!HoldingKnife(player) || !CanOperate(player)) return;
        var state = State(player);
        if (state.HitAt >= 0 || m_time.GameTime < state.Next) return;
        if (!KnifeAnimationController.TriggerKnifeAttack(player, heavy)) return;
        if (!state.Start(m_time.GameTime, heavy)) return;
        state.Inventory = player.ComponentMiner.Inventory;
        state.Slot = state.Inventory.ActiveSlotIndex;
        state.Value = player.ComponentMiner.ActiveBlockValue;
        state.Revision = ScInventoryTransaction.Revision(state.Inventory);
    }
    public void Update(float dt) {
        KnifeQa.Step();
        foreach (var player in m_players.ComponentPlayers) {
            if(!ScMobileControls.IsMobileDevice && Window.IsActive && CanOperate(player) && player.GameWidget.Input.IsKeyDownOnce(Engine.Input.Key.G)
                && (HoldingKnife(player) || ScGunBlock.IsKnown(player.ComponentMiner.ActiveBlockValue) && Terrain.ExtractContents(player.ComponentMiner.ActiveBlockValue)==BlocksManager.GetBlockIndex<ScGunBlock>(true)))
                KnifeAnimationController.TriggerInspect(player);
            UpdateButtons(player);
            bool knife = HoldingKnife(player);
            var state = State(player);
            if (!knife || !CanOperate(player) || state.Inventory != player.ComponentMiner.Inventory
                || state.Slot != state.Inventory?.ActiveSlotIndex || state.Value != player.ComponentMiner.ActiveBlockValue
                || state.Revision != ScInventoryTransaction.Revision(state.Inventory)) { state.Cancel(); continue; }
            if (!state.TakeHit(m_time.GameTime)) continue;
            Camera camera = player.GameWidget.ActiveCamera;
            Ray3 ray = new(camera.ViewPosition, camera.ViewDirection);
            var hit = player.ComponentMiner.Raycast<BodyRaycastResult>(ray, RaycastMode.Interaction, true, true, true, ScKnifeStrike.Range(state.Heavy));
            if (hit.HasValue) {
                float power = ScKnifeStrike.Power(state.Heavy) * player.ComponentMiner.StrengthFactor;
                ScSurvivalBalance.Attack(hit.Value.ComponentBody, player, hit.Value.HitPoint(), ray.Direction, power, m_time.GameTime, melee: true);
                KnifeAnimationController.KnifeHitPose(player, state.Heavy);
            }
        }
    }
    /// <summary>Which of the mod's secondary actions this gun actually has. A gun with no alternate mode offers
    /// none: the button is not shown just because the id exists.</summary>
    public static string SecondaryOf(GunSpec spec) =>
        spec is null ? null
        : spec.ZoomLevels.Length > 0 ? ScGunFunctions.Scope
        : spec.HasBurstMode ? ScGunFunctions.Burst
        : spec.HasSilencer ? ScGunFunctions.Silencer
        : spec.CycleSecondsAlternate > 0 ? ScGunFunctions.RevolverAlt
        : null;

    void UpdateButtons(ComponentPlayer player) {
        // Desktop creates no mobile widgets, even on a touchscreen laptop.
        if (!ScMobileControls.IsMobileDevice) return;
        var container = player.ComponentGui.ControlsContainerWidget;
        if (!m_buttons.TryGetValue(player, out var panel)) m_buttons[player] = panel = new ScWeaponTouchPanel();
        panel.Attach(container);
        bool knife = HoldingKnife(player);
        bool gun = Terrain.ExtractContents(player.ComponentMiner.ActiveBlockValue) == BlocksManager.GetBlockIndex<ScGunBlock>(true);
        bool grenade = SubsystemScGrenades.Holding(player);
        bool touch = player.ComponentInput.IsControlledByTouch || panel.AnyCaptured;
        if (touch) player.ComponentInput.IsControlledByTouch = true;
        bool enabled = Window.IsActive && CanOperate(player) && container.IsVisible;
        var spec = gun ? ScGunBlock.SpecOf(player.ComponentMiner.ActiveBlockValue) : null;
        string secondary = gun ? SecondaryOf(spec) : null;
        panel.Update(container.ActualSize, enabled, touch, id => id switch {
            ScGunFunctions.Reload => gun,
            ScGunFunctions.Fire => gun,
            ScGunFunctions.KnifeHeavy => knife,
            ScGunFunctions.ThrowWeak or ScGunFunctions.ThrowStrong => grenade,
            ScGunFunctions.Inspect => knife || gun,
            _ => secondary == id,
        });
        Project.FindSubsystem<SubsystemScGunBlockBehavior>(true).SetFireButton(player, gun && panel.Pressed(ScGunFunctions.Fire));
        var grenades = Project.FindSubsystem<SubsystemScGrenades>(true);
        grenades.SetThrowButton(player, true, grenade && panel.Pressed(ScGunFunctions.ThrowWeak),
            grenade && panel.Clicked(ScGunFunctions.ThrowWeak), !grenade || panel.Cancelled(ScGunFunctions.ThrowWeak));
        grenades.SetThrowButton(player, false, grenade && panel.Pressed(ScGunFunctions.ThrowStrong),
            grenade && panel.Clicked(ScGunFunctions.ThrowStrong), !grenade || panel.Cancelled(ScGunFunctions.ThrowStrong));
        if (knife && panel.Clicked(ScGunFunctions.KnifeHeavy)) RequestAttack(player, true);
        if (gun && panel.Clicked(ScGunFunctions.Reload)) Project.FindSubsystem<SubsystemScGunBlockBehavior>(true).RequestReload(player);
        if (gun && secondary is not null && panel.Clicked(secondary)) Project.FindSubsystem<SubsystemScGunBlockBehavior>(true).RequestSecondary(player);
        if ((knife || gun) && enabled && (panel.Clicked(ScGunFunctions.Inspect) || player.GameWidget.Input.IsKeyDownOnce(Engine.Input.Key.G))) {
            if (knife) State(player).Cancel();
            KnifeAnimationController.TriggerInspect(player);
        }
    }
    public override void Dispose() {
        foreach (var panel in m_buttons.Values) panel.Dispose();
        m_buttons.Clear(); m_strikes.Clear(); base.Dispose();
    }

    public override bool OnEditInventoryItem(IInventory inventory, int slotIndex, ComponentPlayer componentPlayer) => false;
}
