using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>The mod's own settings page, reached from the game's Settings screen, which the pause menu already
/// opens on every platform. Everything here is a control a thumb can hit; no part of it asks anyone to edit a
/// JSON file, and none of it is stored in a world, so it never travels between devices or resets on a migration.
///
/// Edits work on a copy: Save writes, Cancel puts back exactly what was there on entry.</summary>
public sealed class ScGunSettingsScreen : Screen {
    public const string ScreenName = "ScCsgoGunSettings";

    sealed record Snapshot(bool Buttons, bool KillFeed, bool KillSound, bool Crosshair, string Style, Color Color);
    Snapshot m_entry;
    Screen m_back;
    bool m_narrow;
    bool m_built;

    readonly StackPanelWidget m_content = new() { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch };
    readonly ScrollPanelWidget m_scroll = new() { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch, VerticalAlignment = WidgetAlignment.Stretch };
    CheckboxWidget m_buttons, m_killFeed, m_killSound, m_crosshair;
    ButtonWidget m_edit, m_style, m_save, m_cancel, m_defaults;
    readonly List<(ButtonWidget Button, Color Color)> m_colors = [];
    LabelWidget m_preview, m_status;

    public ScGunSettingsScreen() {
        var frame = ScGunUi.Frame();
        var root = new StackPanelWidget { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch, VerticalAlignment = WidgetAlignment.Stretch, Margin = new Vector2(18, 14) };
        root.Children.Add(new LabelWidget { Text = "CS 枪械 · 模组设置", FontScale = 1.25f, Color = ScGunUi.Text, DropShadow = true, HorizontalAlignment = WidgetAlignment.Center, Margin = new Vector2(0, 6) });
        m_scroll.Children.Add(m_content);
        root.Children.Add(m_scroll);
        var bar = new StackPanelWidget { Direction = LayoutDirection.Horizontal, HorizontalAlignment = WidgetAlignment.Center, Margin = new Vector2(0, 8) };
        m_defaults = ScGunUi.Button("恢复默认", 150); m_cancel = ScGunUi.Button("取消", 130); m_save = ScGunUi.Button("保存", 130);
        foreach (var b in new[] { m_defaults, m_cancel, m_save }) { b.Margin = new Vector2(6, 0); bar.Children.Add(b); }
        root.Children.Add(bar);
        m_status = ScGunUi.Note("");
        m_status.HorizontalAlignment = WidgetAlignment.Center;
        root.Children.Add(m_status);
        Children.Add(frame);
        Children.Add(root);
    }

    public override void Enter(object[] parameters) {
        m_back = ScreensManager.PreviousScreen;
        m_entry = new Snapshot(ScUiSettings.CustomButtons, ScUiSettings.KillFeed, ScUiSettings.KillSound,
                               ScUiSettings.GunCrosshair, ScUiSettings.CrosshairStyle, ScUiSettings.CrosshairColor);
        m_built = false;
        m_status.Text = ScUiSettings.Writable ? "" : "设置文件无法读取，已保留原文件；本次修改不会写入。";
    }

    void Build(bool narrow) {
        m_narrow = narrow; m_built = true;
        m_content.Children.Clear(); m_colors.Clear();
        m_content.Children.Add(ScGunUi.Heading("手机按键"));
        m_buttons = ScGunUi.Toggle("启用模组自定义按键", ScUiSettings.CustomButtons);
        m_content.Children.Add(m_buttons);
        m_content.Children.Add(ScGunUi.Note("关闭后模组新增的战斗按钮全部隐藏并释放触摸，布局数据保留；原版移动、视角、开火和本设置入口不受影响。"));
        m_edit = ScGunUi.Button("编辑按键布局", 190);
        m_content.Children.Add(ScGunUi.Row(ScGunUi.Label("位置、大小、透明度、逐键开关"), m_edit, narrow));
        m_content.Children.Add(ScGunUi.Note($"当前编辑{(SettingsManager.LeftHandedLayout ? "左手" : "右手")}布局；左右手各自保存，切换不会覆盖另一套。"));

        m_content.Children.Add(ScGunUi.Heading("击杀反馈"));
        m_killFeed = ScGunUi.Toggle("击杀播报文字", ScUiSettings.KillFeed);
        m_killSound = ScGunUi.Toggle("击杀提示音效", ScUiSettings.KillSound);
        m_content.Children.Add(m_killFeed);
        m_content.Children.Add(m_killSound);
        m_content.Children.Add(ScGunUi.Note("两项都只控制表现。真正的命中判定、击杀计数、等级成长和伤害结算不受影响；中心命中标记也保持原行为。"));

        m_content.Children.Add(ScGunUi.Heading("枪械准星"));
        m_crosshair = ScGunUi.Toggle("持枪时显示准星", ScUiSettings.GunCrosshair);
        m_content.Children.Add(m_crosshair);
        m_style = ScGunUi.Button(ScUiSettings.StyleLabel(ScUiSettings.CrosshairStyle), 170);
        m_content.Children.Add(ScGunUi.Row(ScGunUi.Label("样式"), m_style, narrow));
        var colors = new StackPanelWidget { Direction = LayoutDirection.Horizontal, HorizontalAlignment = narrow ? WidgetAlignment.Near : WidgetAlignment.Far };
        foreach (var (name, color) in ScUiSettings.Presets) {
            var button = ScGunUi.Button(name, 62);
            button.Color = color; button.Margin = new Vector2(3, 0);
            colors.Children.Add(button);
            m_colors.Add((button, color));
        }
        m_content.Children.Add(ScGunUi.Row(ScGunUi.Label("颜色"), colors, narrow));
        m_preview = new LabelWidget { Text = "预览：＋", FontScale = 1.4f, Color = ScUiSettings.CrosshairColor, DropShadow = true, HorizontalAlignment = WidgetAlignment.Center, Margin = new Vector2(0, 4) };
        m_content.Children.Add(m_preview);
        m_content.Children.Add(ScGunUi.Note("只在手持可用枪械且未开镜时显示。空手、刀具、手雷和原版工具不显示；开镜时使用镜内准星，不叠加两层。颜色只影响这一层，不改变镜内十字线、命中反馈或弹道。"));
    }

    public override void Update() {
        bool narrow = ActualSize.X > 1 && ActualSize.X < ScGunUi.NarrowWidth;
        if (!m_built || narrow != m_narrow) Build(narrow);
        ScUiSettings.CustomButtons = m_buttons.IsChecked;
        ScUiSettings.KillFeed = m_killFeed.IsChecked;
        ScUiSettings.KillSound = m_killSound.IsChecked;
        ScUiSettings.GunCrosshair = m_crosshair.IsChecked;
        if (m_style.IsClicked) {
            int index = Array.IndexOf(ScUiSettings.Styles, ScUiSettings.CrosshairStyle);
            ScUiSettings.CrosshairStyle = ScUiSettings.Styles[(Math.Max(0, index) + 1) % ScUiSettings.Styles.Length];
            m_style.Text = ScUiSettings.StyleLabel(ScUiSettings.CrosshairStyle);
        }
        foreach (var (button, color) in m_colors) if (button.IsClicked) ScUiSettings.CrosshairColor = color;
        m_preview.Color = ScUiSettings.CrosshairColor;
        if (m_edit.IsClicked) { ScreensManager.SwitchScreen(ScGunLayoutScreen.ScreenName); return; }
        if (m_defaults.IsClicked) { ScUiSettings.ResetAll(); m_built = false; return; }
        if (m_cancel.IsClicked || Input.Back || Input.Cancel) { Restore(); Leave(m_back); return; }
        if (m_save.IsClicked) {
            m_status.Text = ScUiSettings.Save() ? "" : "设置未能写入，磁盘上的上一份配置保持不变。";
            if (m_status.Text.Length == 0) Leave(m_back);
        }
    }

    void Restore() {
        ScUiSettings.CustomButtons = m_entry.Buttons; ScUiSettings.KillFeed = m_entry.KillFeed;
        ScUiSettings.KillSound = m_entry.KillSound; ScUiSettings.GunCrosshair = m_entry.Crosshair;
        ScUiSettings.CrosshairStyle = m_entry.Style; ScUiSettings.CrosshairColor = m_entry.Color;
    }
    static void Leave(Screen back) => ScreensManager.SwitchScreen(back ?? ScreensManager.FindScreen<Screen>("Settings"));
}
