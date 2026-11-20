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
        var recipe = ScAssemblyRecipeCard.Find(m_value);
        if (recipe is null) { m_panel.Children.Add(ScGunUi.Note("无法识别装配配方，请返回后从 CS 武器入口重试。")); return; }
        ScAssemblyRecipeCard.FillPage(m_panel, recipe);
        if (recipe.CreativeOnly) return;
        var entry = recipe.Weapon;
        // The attribute card is the other half of this entry, and the one place that never lists materials.
        if (entry is { Knife:false }) {
            m_attributes = ScGunUi.Button("武器属性", 160);
            m_panel.Children.Add(m_attributes);
        }
    }
    public override void Update() {
        if (BackRequested) { GoBack(); return; }
        if (m_attributes is not null && m_attributes.IsClicked) {
            ScWeaponHelpScreen.Open(true, m_value);
        }
    }
}
