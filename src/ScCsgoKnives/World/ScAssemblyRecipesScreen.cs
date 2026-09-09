using Engine;
namespace Game;

public sealed class ScAssemblyRecipesScreen : ScWeaponHelpScreen {
    readonly StackPanelWidget m_panel;
    ButtonWidget m_attributes;
    int m_value;
    public ScAssemblyRecipesScreen() : base("装配配方") {
        m_panel = new StackPanelWidget { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Center,
            VerticalAlignment = WidgetAlignment.Center, Margin = new Vector2(20, 50) };
        var scroll = new ScrollPanelWidget { Direction = LayoutDirection.Vertical };
        scroll.Children.Add(m_panel);
        Body.Children.Add(scroll);
    }
    public override void Enter(object[] parameters) {
        base.Enter(parameters);
        m_panel.Children.Clear();
        m_attributes = null;
        m_value = parameters is { Length: > 0 } && parameters[0] is int v ? v : 0;
        bool skinTemplate = ScGunSkinTemplateBlock.IsTemplate(m_value) || ScGunCounterTemplateBlock.IsTemplate(m_value);
        int recipeValue = skinTemplate && EffectiveGunStats.TrySnapshotValue(m_value, out var snapshot)
            ? ScGunAttributes.TemplateValue(snapshot.Variant) : m_value;
        var entry = ScWeaponCrafting.Find(recipeValue);
        if (entry is null) return;
        void Label(string text, float scale = 1) => m_panel.Children.Add(new LabelWidget {
            Text = text, FontScale = scale, WordWrap = true, HorizontalAlignment = WidgetAlignment.Center, Margin = new Vector2(4, 5) });
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
        // The attribute card is the other half of this entry, and the one place that never lists materials.
        if (!entry.Knife) {
            m_attributes = ScGunUi.Button("武器属性", 160);
            m_panel.Children.Add(m_attributes);
        }
    }
    public override void Update() {
        if (BackRequested) { GoBack(); return; }
        if (m_attributes is not null && m_attributes.IsClicked) {
            ScreensManager.m_screens["RecipaediaRecipes"] = new ScGunAttributesScreen();
            ScreensManager.SwitchScreen("RecipaediaRecipes", m_value);
        }
    }
}
