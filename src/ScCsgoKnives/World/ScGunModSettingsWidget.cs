namespace Game;

/// <summary>API 1.9.3 Mod Settings entry. The existing editor keeps its Save/Cancel transaction.</summary>
public sealed class ScGunModSettingsWidget : UniformSpacingPanelSettingWidget {
    ButtonWidget m_open;
    public override bool Supports(Type type) => type == typeof(bool);
    public override void Initialize(ModSettingItem descriptor, object currentValue, string name, string description) {
        base.Initialize(descriptor, currentValue, name, description);
        m_open = ScGunUi.Button("打开设置", 150);
        Assemble(m_open);
    }
    public override void Update() {
        base.Update();
        if (m_open?.IsClicked == true) ScCsgoKnivesModLoader.OpenSettings();
    }
}
