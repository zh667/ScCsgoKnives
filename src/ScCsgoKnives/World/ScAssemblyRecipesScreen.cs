using Engine;
namespace Game;

public sealed class ScAssemblyRecipesScreen : RecipaediaRecipesScreen {
    readonly StackPanelWidget m_panel;
    public ScAssemblyRecipesScreen() {
        m_panel = new StackPanelWidget { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Center,
            VerticalAlignment = WidgetAlignment.Center, Margin = new Vector2(20, 50) };
        Children.Add(m_panel);
    }
    public override void Enter(object[] parameters) {
        base.Enter(parameters);
        m_panel.Children.Clear();
        var entry = ScWeaponCrafting.Find((int)parameters[0]);
        if (entry is null) return;
        void Label(string text, float scale = 1) => m_panel.Children.Add(new LabelWidget {
            Text = text, FontScale = scale, HorizontalAlignment = WidgetAlignment.Center, Margin = new Vector2(4, 5) });
        Label(BlocksManager.Blocks[Terrain.ExtractContents(entry.Value)].GetDisplayName(null, entry.Value), 1.25f);
        Label($"武器装配台  ·  等级 {entry.Level}  ·  产出 1 件");
        foreach (var material in entry.Materials()) {
            var row = new StackPanelWidget { Direction = LayoutDirection.Horizontal, HorizontalAlignment = WidgetAlignment.Center };
            row.Children.Add(new BlockIconWidget { Value = material.Key, Size = new Vector2(44), Margin = new Vector2(6, 2) });
            row.Children.Add(new LabelWidget { Text = $"{BlocksManager.Blocks[Terrain.ExtractContents(material.Key)].GetDisplayName(null, material.Key)} ×{material.Value}", VerticalAlignment = WidgetAlignment.Center });
            m_panel.Children.Add(row);
        }
        Label("将材料放入背包，交互装配台并选择此型号。", .8f);
        Label(entry.Knife ? "确认后扣料；左键轻刀，右键重刀。" : "确认后扣料，交付空枪；弹药另行制作。", .8f);
    }
    public override void Update() {
        base.Update();
        m_craftingRecipeWidget.IsVisible = false; m_smeltingRecipeWidget.IsVisible = false;
        m_prevRecipeButton.IsVisible = false; m_nextRecipeButton.IsVisible = false;
    }
}
