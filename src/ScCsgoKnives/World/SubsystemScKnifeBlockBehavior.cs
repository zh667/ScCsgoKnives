using Engine;
using TemplatesDatabase;
namespace Game;

public sealed class SubsystemScKnifeBlockBehavior : SubsystemBlockBehavior, IUpdateable {
    public override int[] HandledBlocks => [BlocksManager.GetBlockIndex<ScKnifeBlock>()];

    public UpdateOrder UpdateOrder => UpdateOrder.Default;

    readonly Dictionary<ComponentPlayer, ScKnifeStrike> m_strikes = [];
    readonly Dictionary<ComponentPlayer, BevelledButtonWidget> m_buttons = [];
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
            if (!m_buttons.TryGetValue(player, out var button)) {
                button = new BevelledButtonWidget { Text = "重刀", Size = new Vector2(96, 56), HorizontalAlignment = WidgetAlignment.Far, VerticalAlignment = WidgetAlignment.Far, MarginRight = 160, MarginBottom = 150 };
                player.GuiWidget.Children.Add(button); m_buttons[player] = button;
            }
            bool knife = HoldingKnife(player);
            bool gun = Terrain.ExtractContents(player.ComponentMiner.ActiveBlockValue) == BlocksManager.GetBlockIndex<ScGunBlock>(true);
            button.IsVisible = (knife || gun) && CanOperate(player);
            button.Text = knife ? "重刀" : "换弹";
            if (button.IsVisible && button.IsClicked) {
                if (knife) RequestAttack(player, true);
                else Project.FindSubsystem<SubsystemScGunBlockBehavior>(true).RequestReload(player);
            }
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
                player.ComponentMiner.DamageActiveTool(1);
            }
        }
    }
    public override void Dispose() {
        foreach (var pair in m_buttons) pair.Key.GuiWidget.Children.Remove(pair.Value);
        m_buttons.Clear(); m_strikes.Clear(); base.Dispose();
    }

    public override bool OnEditInventoryItem(IInventory inventory, int slotIndex, ComponentPlayer componentPlayer) {
        int value = inventory.GetSlotValue(slotIndex);
        if (Terrain.ExtractContents(value) != BlocksManager.GetBlockIndex<ScKnifeBlock>(true)) return false;

        State(componentPlayer).Cancel();

        if (!KnifeAnimationController.TriggerInspect(componentPlayer)) return true;

        string name = BlocksManager.Blocks[Terrain.ExtractContents(value)].GetDisplayName(
            componentPlayer.Project.FindSubsystem<SubsystemTerrain>(true),
            value
        );
        componentPlayer.ComponentGui.DisplaySmallMessage(
            string.Format(LanguageControl.Get("ScCsgoKnives", "Message", "Inspect"), name),
            Engine.Color.White,
            true,
            false
        );
        return true;
    }
}
