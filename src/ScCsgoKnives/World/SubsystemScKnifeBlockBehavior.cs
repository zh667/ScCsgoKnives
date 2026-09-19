using Engine;
using TemplatesDatabase;
namespace Game;

public sealed class SubsystemScKnifeBlockBehavior : SubsystemBlockBehavior, IUpdateable {
    public override int[] HandledBlocks => [BlocksManager.GetBlockIndex<ScKnifeBlock>()];

    public UpdateOrder UpdateOrder => UpdateOrder.Default;

    readonly Dictionary<ComponentPlayer, ScKnifeStrike> m_strikes = [];
    readonly Dictionary<ComponentPlayer, ScWeaponTouchPanel> m_buttons = [];
    readonly Dictionary<int, double> m_savedRecovery = [];
    readonly Dictionary<(ComponentPlayer Player, string Id), bool> m_touchThrows = [];
    // Inspect is an edge-triggered action. Keep a per-player held latch so a
    // platform that reports a held key/button as "clicked" cannot restart the
    // inspect clip every update and leave the weapon inspecting forever.
    readonly Dictionary<ComponentPlayer, bool> m_inspectHeld = [];
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
    static bool CanOperate(ComponentPlayer player) => ScGunBindings.Available(player);
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
                ScControllerFeedback.KnifeHit(player,state.Heavy);
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
        if (player?.ComponentGui is null) return;
        var container = player.ComponentGui.ControlsContainerWidget;
        ScWeaponTouchPanel panel = null;
        // Keyboard bindings work on every platform. Only mobile devices create touch widgets.
        if (ScMobileControls.IsMobileDevice) {
            if (!m_buttons.TryGetValue(player, out panel)) m_buttons[player] = panel = new ScWeaponTouchPanel();
            panel.Attach(container);
        }
        bool knife = HoldingKnife(player);
        bool gun = Terrain.ExtractContents(player.ComponentMiner.ActiveBlockValue) == BlocksManager.GetBlockIndex<ScGunBlock>(true);
        bool grenade = SubsystemScGrenades.Holding(player);
        bool c4 = ScC4Block.IsValue(player.ComponentMiner.ActiveBlockValue);
        bool touch = player.ComponentInput.IsControlledByTouch || panel?.AnyCaptured == true;
        if (touch) player.ComponentInput.IsControlledByTouch = true;
        bool enabled = Window.IsActive && CanOperate(player) && container.IsVisible;
        var spec = gun ? ScGunBlock.SpecOf(player.ComponentMiner.ActiveBlockValue) : null;
        string secondary = gun ? SecondaryOf(spec) : null;
        panel?.Update(container.ActualSize, enabled, touch, id => id switch {
            ScGunFunctions.Reload => gun,
            ScGunFunctions.Fire => gun,
            ScGunFunctions.KnifeHeavy => knife,
            ScGunFunctions.ThrowWeak or ScGunFunctions.ThrowStrong => grenade,
            ScGunFunctions.Plant => c4,
            ScGunFunctions.C4Timer => c4,
            ScGunFunctions.Inspect => knife || gun || grenade || c4,
            _ => secondary == id,
        });
        bool Pressed(string id) => enabled && (panel?.Pressed(id) == true || ScGunBindings.Down(player, id));
        bool Clicked(string id) => enabled && (panel?.Clicked(id) == true || ScGunBindings.Down(player, id, true));
        int ThrowSources(string id) => (panel?.Pressed(id) == true || panel?.Clicked(id) == true ? 2 : 0)
            | (ScGunBindings.KeyboardDown(player, id) || ScGunBindings.KeyboardDown(player, id, true) ? 4 : 0)
            | (ScGamepadBindings.Down(player, id, false) || ScGamepadBindings.Down(player, id, true) ? 8 : 0);
        bool Cancelled(string id) {
            bool wasTouch = m_touchThrows.GetValueOrDefault((player,id));
            m_touchThrows[(player,id)] = panel?.Pressed(id) == true;
            // A disabled touch widget reports cancellation every frame. It must not cancel
            // an independent keyboard-mapped grenade when the keyboard key is released.
            return !enabled || wasTouch && panel?.Cancelled(id) == true && !ScGunBindings.Down(player,id);
        }
        Project.FindSubsystem<SubsystemScGunBlockBehavior>(true).SetFireButton(player, gun && enabled && panel?.Pressed(ScGunFunctions.Fire) == true);
        Project.FindSubsystem<SubsystemScC4>()?.SetPlantButton(player, c4 && enabled && panel?.Pressed(ScGunFunctions.Plant) == true);
        if(c4 && Clicked(ScGunFunctions.C4Timer)) {Project.FindSubsystem<SubsystemScC4>()?.ConfigureTimer(player);return;}
        var grenades = Project.FindSubsystem<SubsystemScGrenades>(true);
        grenades.SetThrowButton(player, true, grenade && Pressed(ScGunFunctions.ThrowWeak),
            grenade && Clicked(ScGunFunctions.ThrowWeak), !grenade || Cancelled(ScGunFunctions.ThrowWeak), ThrowSources(ScGunFunctions.ThrowWeak));
        grenades.SetThrowButton(player, false, grenade && Pressed(ScGunFunctions.ThrowStrong),
            grenade && Clicked(ScGunFunctions.ThrowStrong), !grenade || Cancelled(ScGunFunctions.ThrowStrong), ThrowSources(ScGunFunctions.ThrowStrong));
        if (knife && Clicked(ScGunFunctions.KnifeHeavy)) RequestAttack(player, true);
        if (knife && Clicked(ScGunFunctions.Fire)) RequestAttack(player, false);
        // Keyboard R is read by UpdateGun, where it also interrupts safely at the reload boundary.
        if (gun && enabled && panel?.Clicked(ScGunFunctions.Reload) == true) Project.FindSubsystem<SubsystemScGunBlockBehavior>(true).RequestReload(player);
        // Native AimPressed already consumes this trigger (including world interaction priority).
        // Calling RequestSecondary too would cycle zoom twice or start/cancel a silencer action.
        bool secondaryClick=secondary is not null && enabled && (panel?.Clicked(secondary)==true
            || ScGunBindings.KeyboardDown(player,secondary,true)
            || (ScMobileControls.UsesTouchInput(player) || !ScGamepadBindings.NativeAimBinding(secondary)) && ScGamepadBindings.Down(player,secondary,true));
        if (gun && secondaryClick) Project.FindSubsystem<SubsystemScGunBlockBehavior>(true).RequestSecondary(player);
        // Inspect must be a real edge from the mapped action. Do not derive it
        // from a held state: API 1.9.3.1 can leave an input snapshot held while
        // the player is merely looking/using the weapon, which caused phantom
        // inspect loops without any inspect press.
        bool touchInspect = enabled && panel?.Clicked(ScGunFunctions.Inspect) == true;
        bool keyInspect = enabled && ScGunBindings.Down(player, ScGunFunctions.Inspect, true);
        bool inspectPressed = touchInspect || keyInspect;
        m_inspectHeld[player] = false;
        if ((knife || gun || grenade || c4) && inspectPressed && !(c4 && Project.FindSubsystem<SubsystemScC4>()?.IsPlanting(player) == true)) {
            if (knife) State(player).Cancel();
            KnifeLog.Information($"[GUN_INPUT] inspect edge player={player.PlayerData.PlayerIndex} kind={(knife ? "knife" : gun ? "gun" : "grenade")} source={(touchInspect ? "touch" : "mapped")}");
            KnifeAnimationController.TriggerInspect(player);
        }
    }
    public override void Dispose() {
        foreach (var panel in m_buttons.Values) panel.Dispose();
        m_buttons.Clear(); m_strikes.Clear(); m_touchThrows.Clear(); m_inspectHeld.Clear(); base.Dispose();
    }

    public override bool OnEditInventoryItem(IInventory inventory, int slotIndex, ComponentPlayer componentPlayer) => false;
}
