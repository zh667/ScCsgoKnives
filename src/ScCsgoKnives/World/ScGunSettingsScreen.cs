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
    Snapshot m_working;
    bool m_returningFromLayout;
    Screen m_back;
    bool m_narrow;
    bool m_built;

    readonly StackPanelWidget m_content = new() { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch };
    readonly ScrollPanelWidget m_scroll = new() { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch, VerticalAlignment = WidgetAlignment.Stretch };
    CheckboxWidget m_buttons, m_killFeed, m_killSound, m_crosshair;
    ButtonWidget m_edit, m_style, m_save, m_cancel, m_defaults;
    readonly List<(ButtonWidget Button, Color Color)> m_colors = [];
    LabelWidget m_preview, m_status;
    SliderWidget m_red, m_green, m_blue;
    static Snapshot Capture() => new(ScUiSettings.CustomButtons, ScUiSettings.KillFeed, ScUiSettings.KillSound,
        ScUiSettings.GunCrosshair, ScUiSettings.CrosshairStyle, ScUiSettings.CrosshairColor);
    static void Apply(Snapshot s) {
        ScUiSettings.CustomButtons = s.Buttons; ScUiSettings.KillFeed = s.KillFeed; ScUiSettings.KillSound = s.KillSound;
        ScUiSettings.GunCrosshair = s.Crosshair; ScUiSettings.CrosshairStyle = s.Style; ScUiSettings.CrosshairColor = s.Color;
    }

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
        if (!m_returningFromLayout) { m_back = ScreensManager.PreviousScreen; m_working = Capture(); }
        m_returningFromLayout = false;
        m_built = false;
        m_status.Text = ScUiSettings.Writable ? "" : "设置文件无法读取，已保留原文件；本次修改不会写入。";
    }

    void Build(bool narrow) {
        m_narrow = narrow; m_built = true;
        m_content.Children.Clear(); m_colors.Clear();
        m_content.Children.Add(ScGunUi.Heading("手机按键"));
        m_buttons = ScGunUi.Toggle("启用模组自定义按键", m_working.Buttons);
        m_content.Children.Add(m_buttons);
        m_content.Children.Add(ScGunUi.Note("关闭后模组新增的战斗按钮全部隐藏并释放触摸，布局数据保留；原版移动、视角、开火和本设置入口不受影响。"));
        m_edit = ScGunUi.Button("编辑按键布局", 190);
        m_content.Children.Add(ScGunUi.Row(ScGunUi.Label("位置、大小、透明度、逐键开关"), m_edit, narrow));
        m_content.Children.Add(ScGunUi.Note($"当前编辑{(SettingsManager.LeftHandedLayout ? "左手" : "右手")}布局；左右手各自保存，切换不会覆盖另一套。"));

        m_content.Children.Add(ScGunUi.Heading("击杀反馈"));
        m_killFeed = ScGunUi.Toggle("击杀播报文字", m_working.KillFeed);
        m_killSound = ScGunUi.Toggle("击杀提示音效", m_working.KillSound);
        m_content.Children.Add(m_killFeed);
        m_content.Children.Add(m_killSound);
        m_content.Children.Add(ScGunUi.Note("两项都只控制表现。真正的命中判定、击杀计数、等级成长和伤害结算不受影响；中心命中标记也保持原行为。"));

        m_content.Children.Add(ScGunUi.Heading("枪械准星"));
        m_crosshair = ScGunUi.Toggle("持枪时显示准星", m_working.Crosshair);
        m_content.Children.Add(m_crosshair);
        m_style = ScGunUi.Button(ScUiSettings.StyleLabel(m_working.Style), 170);
        m_content.Children.Add(ScGunUi.Row(ScGunUi.Label("样式"), m_style, narrow));
        var colors = new StackPanelWidget { Direction = LayoutDirection.Horizontal, HorizontalAlignment = narrow ? WidgetAlignment.Near : WidgetAlignment.Far };
        foreach (var (name, color) in ScUiSettings.Presets) {
            var button = ScGunUi.Button(name, 62);
            button.Color = color; button.Margin = new Vector2(3, 0);
            colors.Children.Add(button);
            m_colors.Add((button, color));
        }
        m_content.Children.Add(ScGunUi.Row(ScGunUi.Label("颜色"), colors, narrow));
        m_red = ScGunUi.Slider(0, 255, 1, m_working.Color.R, "红 R");
        m_green = ScGunUi.Slider(0, 255, 1, m_working.Color.G, "绿 G");
        m_blue = ScGunUi.Slider(0, 255, 1, m_working.Color.B, "蓝 B");
        foreach (var slider in new[] { m_red, m_green, m_blue }) {
            slider.HorizontalAlignment = WidgetAlignment.Stretch; m_content.Children.Add(slider);
        }
        m_preview = new LabelWidget { Text = "预览：＋", FontScale = 1.4f, Color = m_working.Color, DropShadow = true, HorizontalAlignment = WidgetAlignment.Center, Margin = new Vector2(0, 4) };
        m_content.Children.Add(m_preview);
        m_content.Children.Add(ScGunUi.Note("只在手持可用枪械且未开镜时显示。空手、刀具、手雷和原版工具不显示；开镜时使用镜内准星，不叠加两层。颜色只影响这一层，不改变镜内十字线、命中反馈或弹道。"));
    }

    public override void Update() {
        bool narrow = ActualSize.X > 1 && ActualSize.X < ScGunUi.NarrowWidth;
        if (!m_built || narrow != m_narrow) Build(narrow);
        m_working = m_working with { Buttons = m_buttons.IsChecked, KillFeed = m_killFeed.IsChecked,
            KillSound = m_killSound.IsChecked, Crosshair = m_crosshair.IsChecked,
            Color = new Color((byte)m_red.Value, (byte)m_green.Value, (byte)m_blue.Value) };
        if (m_style.IsClicked) {
            int index = Array.IndexOf(ScUiSettings.Styles, m_working.Style);
            m_working = m_working with { Style = ScUiSettings.Styles[(Math.Max(0, index) + 1) % ScUiSettings.Styles.Length] };
            m_style.Text = ScUiSettings.StyleLabel(m_working.Style);
        }
        foreach (var (button, color) in m_colors) if (button.IsClicked) {
            m_working = m_working with { Color = color }; m_red.Value = color.R; m_green.Value = color.G; m_blue.Value = color.B;
        }
        m_preview.Color = m_working.Color;
        m_preview.Text = $"预览：＋  RGB {m_working.Color.R}, {m_working.Color.G}, {m_working.Color.B}";
        if (m_edit.IsClicked) { m_returningFromLayout = true; ScreensManager.SwitchScreen(ScGunLayoutScreen.ScreenName); return; }
        if (m_defaults.IsClicked) { m_working = new(true, true, true, true, ScUiSettings.StyleVanilla, Color.White); m_built = false; return; }
        if (m_cancel.IsClicked || Input.Back || Input.Cancel) { Leave(m_back); return; }
        if (m_save.IsClicked) {
            var previous = Capture();
            Apply(m_working);
            m_status.Text = ScUiSettings.Save() ? "" : "设置未能写入，磁盘上的上一份配置保持不变。";
            if (m_status.Text.Length == 0) Leave(m_back);
            else Apply(previous);
        }
    }

    static void Leave(Screen back) => ScreensManager.SwitchScreen(back ?? ScreensManager.FindScreen<Screen>("Settings"));
}
