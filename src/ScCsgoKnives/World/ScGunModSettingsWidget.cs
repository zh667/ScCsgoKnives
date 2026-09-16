namespace Game;

/// <summary>API 1.9.3 Mod Settings entry. The existing editor keeps its Save/Cancel transaction.</summary>
public sealed class ScGunModSettingsWidget : UniformSpacingPanelSettingWidget {
    bool m_opened;
    public override bool Supports(Type type) => type == typeof(bool);
    public override void Initialize(ModSettingItem descriptor, object currentValue, string name, string description) {
        base.Initialize(descriptor, currentValue, name, description);
        m_opened=false;
        Assemble(ScGunUi.Note("正在打开枪械设置…"));
    }
    public override void Update() {
        base.Update();
        // Navigate only after the native page has been built. Returning re-enters its root,
        // whose items do not contain this widget, so Back cannot bounce into the editor.
        if (!m_opened && ScreensManager.CurrentScreen is ModSettingsScreen && IsVisible && ParentWidget is not null) {
            m_opened=true;ScCsgoKnivesModLoader.OpenSettings();
        }
    }
}
