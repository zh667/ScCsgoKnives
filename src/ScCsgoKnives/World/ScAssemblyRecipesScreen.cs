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
        var component = ScComponentCrafting.Find(recipeValue);
        var extension = ScWorkbenchExtension.Find(recipeValue);
        if (entry is null && component is null && extension is null) { m_panel.Children.Add(ScGunUi.Note("无法识别装配配方，请返回后从 CS 武器入口重试。")); return; }
        void Label(string text, float scale = 1) => m_panel.Children.Add(new LabelWidget {
            Text = text, FontScale = scale, WordWrap = true, HorizontalAlignment = WidgetAlignment.Center, Margin = new Vector2(4, 5) });
        int output=entry?.Value??component?.Value??extension.Value;
        Label(BlocksManager.Blocks[Terrain.ExtractContents(output)].GetDisplayName(null, output), 1.25f);
        Label(extension is {CreativeOnly:true}?"仅创造模式领取 · 无生存制作配方":component is not null?"武器装配台 · 配件制作 · 每批产出 1 件":$"武器装配台 · 制作等级 {entry?.Level??extension?.Level??1} · 每批产出 {extension?.ResultCount??1} 件");
        if(extension is {CreativeOnly:true}) {
            Label(BlocksManager.Blocks[Terrain.ExtractContents(output)].GetDescription(output), .8f);
            return;
        }
        foreach (var material in entry?.Materials()??component?.Materials()??extension.Materials()) {
            var row = new StackPanelWidget { Direction = LayoutDirection.Horizontal, HorizontalAlignment = WidgetAlignment.Center };
            row.Children.Add(new BlockIconWidget { Value = material.Key, Size = new Vector2(44), Margin = new Vector2(6, 2) });
            row.Children.Add(new LabelWidget { Text = $"{BlocksManager.Blocks[Terrain.ExtractContents(material.Key)].GetDisplayName(null, material.Key)} ×{material.Value}", VerticalAlignment = WidgetAlignment.Center });
            m_panel.Children.Add(row);
        }
        Label("将材料放入背包，交互装配台并选择此型号。", .8f);
        if(extension is not null) Label(BlocksManager.Blocks[Terrain.ExtractContents(output)].GetDescription(output), .8f);
        Label(entry is null?"在装配台选择数量后制作，材料可自动堆叠。":entry.Knife ? "左键轻刀，右键重刀。" : "交付空枪；弹药另行制作。", .8f);
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
