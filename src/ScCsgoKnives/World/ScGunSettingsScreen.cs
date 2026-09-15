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

    sealed record Snapshot(bool Buttons, bool KillFeed, bool KillSound, bool Crosshair, string Style, Color Color, bool ButtonOnly, ScCrosshairShape Shape, bool SimpleMaterials);
    Snapshot m_working;
    bool m_returningFromLayout;
    Screen m_back;
    bool m_narrow;
    bool m_built;

    readonly StackPanelWidget m_content = new() { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch };
    readonly ScrollPanelWidget m_scroll = new() { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch, VerticalAlignment = WidgetAlignment.Stretch };
    CheckboxWidget m_buttons, m_killFeed, m_killSound, m_crosshair;
    CheckboxWidget m_buttonOnly, m_simpleMaterials;
    SliderWidget m_width, m_length, m_gap, m_scale, m_dot;
    ButtonWidget m_edit, m_style, m_save, m_cancel, m_defaults;
    ButtonWidget m_copyGroup;
    readonly ButtonWidget m_bindings = ScGunUi.Button("武器按键绑定", 230);
    readonly ButtonWidget m_recoverView = ScGunUi.Button("恢复正常视角", 230);
    readonly ScGunWorldBackground m_background = new();
    readonly List<(ButtonWidget Button, Color Color)> m_colors = [];
    LabelWidget m_preview, m_status;
    ScCrosshairPreview m_shapePreview;
    ButtonWidget m_parameterGroup;
    bool m_shapeControls;
    SliderWidget m_red, m_green, m_blue;
    readonly CanvasWidget m_root = new() { Size = new Vector2(float.PositiveInfinity) };
    readonly LabelWidget m_title = new() { Text = "CS 枪械 · 模组设置", FontScale = 1.1f, TextAnchor = TextAnchor.HorizontalCenter, DropShadow = true };
    readonly StackPanelWidget m_bar = new() { Direction = LayoutDirection.Horizontal };
    static Snapshot Capture() => new(ScUiSettings.CustomButtons, ScUiSettings.KillFeed, ScUiSettings.KillSound,
        ScUiSettings.GunCrosshair, ScUiSettings.CrosshairStyle, ScUiSettings.CrosshairColor, ScUiSettings.ButtonOnlyFire, ScUiSettings.CrosshairShape, ScUiSettings.SimpleMaterials);
    static void Apply(Snapshot s) {
        ScUiSettings.CustomButtons = s.Buttons; ScUiSettings.KillFeed = s.KillFeed; ScUiSettings.KillSound = s.KillSound;
        ScUiSettings.GunCrosshair = s.Crosshair; ScUiSettings.CrosshairStyle = s.Style; ScUiSettings.CrosshairColor = s.Color;
        ScUiSettings.SimpleMaterials = s.SimpleMaterials;
        ScUiSettings.ButtonOnlyFire = s.ButtonOnly; ScUiSettings.CrosshairShape = s.Shape.Normalize();
    }

    public ScGunSettingsScreen() {
        Children.Add(m_background);
        var frame = ScGunUi.Frame();
        var root = m_root;
        root.Children.Add(m_title);
        m_scroll.Children.Add(m_content);
        root.Children.Add(m_scroll);
        var bar = m_bar;
        m_defaults = ScGunUi.Button("恢复默认", 150); m_cancel = ScGunUi.Button("取消", 130); m_save = ScGunUi.Button("保存", 130);
        foreach (var b in new[] { m_defaults, m_cancel, m_save }) { b.Margin = new Vector2(6, 0); bar.Children.Add(b); }
        root.Children.Add(bar);
        m_status = ScGunUi.Note("");
        m_status.HorizontalAlignment = WidgetAlignment.Center;
        root.Children.Add(m_status);
        Children.Add(frame);
        Children.Add(root);
    }

    public override void MeasureOverride(Vector2 available) {
        float w = Math.Max(280, available.X), h = Math.Max(220, available.Y);
        bool narrow = w < 650;
        if (m_working is not null && (!m_built || narrow != m_narrow)) Build(narrow);
        m_root.SetWidgetPosition(m_title,new Vector2(12,8)); m_title.Size = new Vector2(w-24,40);
        m_root.SetWidgetPosition(m_scroll,new Vector2(12,56)); m_scroll.DesiredSize = new Vector2(w-24,Math.Max(60,h-156));
        float bw = Math.Min(150,(w-48)/3);
        foreach(var b in new[]{m_defaults,m_cancel,m_save}) ((CanvasWidget)b).Size = new Vector2(bw,48);
        m_root.SetWidgetPosition(m_bar,new Vector2((w-3*(bw+12))/2,h-92)); m_bar.DesiredSize = new Vector2(3*(bw+12),48);
        m_root.SetWidgetPosition(m_status,new Vector2(12,h-38)); m_status.Size = new Vector2(w-24,32);
        base.MeasureOverride(available);
    }

    public override void Enter(object[] parameters) {
        m_background.ResetCapture();
        ScWeaponTouchPanel.SuppressAll(true);
        KnifeLog.Trace("[CS_UI_0413] settings enter: isolated background, path=" + ScUiSettings.Path);
        KnifeLog.Trace($"[CS_SCOPE_0416] settings enter baseView={SettingsManager.ViewAngle} sensitivity={SettingsManager.LookSensitivity}");
        if (!m_returningFromLayout) { m_back = ScreensManager.PreviousScreen; m_working = Capture(); }
        m_returningFromLayout = false;
        m_built = false;
        m_status.Text = ScUiSettings.Writable ? "" : "设置文件无法读取，已保留原文件；本次修改不会写入。";
    }

    void Build(bool narrow) {
        m_narrow = narrow; m_built = true;
        m_content.Children.Clear(); m_colors.Clear();
        m_content.Children.Add(ScGunUi.Heading("玩家交流"));
        m_copyGroup = ScGunUi.Button("复制群号", 150);
        m_content.Children.Add(ScGunUi.Row(ScGunUi.Label("交流群：1087216872"), m_copyGroup, narrow));
        m_content.Children.Add(ScGunUi.Note("点击按钮将群号复制到系统剪贴板，手机和电脑均可使用。"));
        m_content.Children.Add(ScGunUi.Heading("视角恢复"));
        m_content.Children.Add(m_recoverView);
        m_content.Children.Add(ScGunUi.Note($"当前基础视野 {SettingsManager.ViewAngle*100:0.##}%、灵敏度 {SettingsManager.LookSensitivity*100:0.##}%。若拿刀或空手仍像开镜，可恢复原版默认值。确认后立即生效并单独保存，不受本页取消影响。"));
        m_content.Children.Add(ScGunUi.Heading("武器画质"));
        m_simpleMaterials = ScGunUi.Toggle("简化材质（适合手机）", m_working.SimpleMaterials);
        m_content.Children.Add(m_simpleMaterials);
        m_content.Children.Add(ScGunUi.Note("降低金属反光和表面凹凸效果，保留皮肤颜色、武器动画与场景明暗。保存后生效；关闭可恢复完整材质。"));
        m_content.Children.Add(ScGunUi.Heading("武器操作绑定"));
        m_content.Children.Add(m_bindings);
        m_content.Children.Add(ScGunUi.Note("设置开火／轻刀、换弹、开镜、消音器、连发、速射、检视、重刀、强投和轻投，共 10 项操作。与下面的触屏布局独立。"));
        m_content.Children.Add(ScGunUi.Heading("手机按键"));
        m_buttons = ScGunUi.Toggle("启用模组自定义按键", m_working.Buttons);
        m_content.Children.Add(m_buttons);
        m_buttonOnly = ScGunUi.Toggle("触屏仅开火按钮射击", m_working.ButtonOnly);
        m_content.Children.Add(m_buttonOnly);
        m_content.Children.Add(ScGunUi.Note("仅按钮模式：空白触屏只转动视角，鼠标、键盘和手柄不受影响。请先在布局启用开火键；若关闭开火键或按钮总开关，会回退触屏攻击，避免无法开枪。"));
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
        m_parameterGroup = ScGunUi.Button(m_shapeControls ? "调整：形状 ▸" : "调整：颜色 ▸", 190);
        m_content.Children.Add(m_parameterGroup);
        m_preview = ScGunUi.Note("");
        m_width = ScGunUi.Slider(.5f, 8, .5f, m_working.Shape.Width, "十字粗细");
        m_length = ScGunUi.Slider(1, 32, 1, m_working.Shape.Length, "十字线长");
        m_gap = ScGunUi.Slider(0, 24, 1, m_working.Shape.Gap, "中心间距");
        m_scale = ScGunUi.Slider(.5f, 3, .1f, m_working.Shape.Scale, "整体大小");
        m_dot = ScGunUi.Slider(1, 16, .2f, m_working.Shape.Dot, "圆点直径");
        var editor = new ScCrosshairEditor();
        m_shapePreview = editor.Preview;
        m_shapePreview.Shape = m_working.Shape; m_shapePreview.Tint = m_working.Color; m_shapePreview.CrossStyle = m_working.Style;
        foreach (var slider in new[] { m_red, m_green, m_blue, m_width, m_length, m_gap, m_scale, m_dot }) {
            slider.IsVisible = m_shapeControls == (slider != m_red && slider != m_green && slider != m_blue);
            editor.Controls.Children.Add(slider);
        }
        m_content.Children.Add(editor);
        m_content.Children.Add(m_preview);
        m_content.Children.Add(ScGunUi.Note("十字参数仅用于十字样式，圆点直径仅用于圆点；原版图案只调整体大小。"));
        m_content.Children.Add(ScGunUi.Note("只在手持可用枪械且未开镜时显示。空手、刀具、手雷和原版工具不显示；开镜时使用镜内准星，不叠加两层。颜色只影响这一层，不改变镜内十字线、命中反馈或弹道。"));

    }

    public override void Update() {
        bool narrow = ActualSize.X > 1 && ActualSize.X < 650;
        if (!m_built || narrow != m_narrow) Build(narrow);
        if(m_recoverView.IsClicked) {
            DialogsManager.ShowDialog(this,new MessageDialog("恢复正常视角？",
                "将退出 CS 开镜，恢复原版基础视野 100%（80°）和灵敏度 50%，并立即保存。不会重置其他设置、武器或存档；本页取消不会撤销此次恢复。",
                "恢复并保存","取消",answer=> {
                    if(answer!=MessageDialogButton.Button1)return;
                    m_status.Text=ScViewRecovery.RestoreAndSave();m_built=false;m_background.ResetCapture();
                }));return;
        }
        m_working = m_working with { Buttons = m_buttons.IsChecked, KillFeed = m_killFeed.IsChecked,
            KillSound = m_killSound.IsChecked, Crosshair = m_crosshair.IsChecked,
            SimpleMaterials = m_simpleMaterials.IsChecked, ButtonOnly = m_buttonOnly.IsChecked, Shape = new ScCrosshairShape(m_width.Value, m_length.Value, m_gap.Value, m_scale.Value, m_dot.Value).Normalize(),
            Color = new Color((byte)m_red.Value, (byte)m_green.Value, (byte)m_blue.Value) };
        if (m_style.IsClicked) {
            int index = Array.IndexOf(ScUiSettings.Styles, m_working.Style);
            m_working = m_working with { Style = ScUiSettings.Styles[(Math.Max(0, index) + 1) % ScUiSettings.Styles.Length] };
            m_style.Text = ScUiSettings.StyleLabel(m_working.Style);
        }
        foreach (var (button, color) in m_colors) if (button.IsClicked) {
            m_working = m_working with { Color = color }; m_red.Value = color.R; m_green.Value = color.G; m_blue.Value = color.B;
        }
        if (m_parameterGroup.IsClicked) {
            m_shapeControls = !m_shapeControls;
            m_parameterGroup.Text = m_shapeControls ? "调整：形状 ▸" : "调整：颜色 ▸";
            foreach (var slider in new[] { m_red, m_green, m_blue, m_width, m_length, m_gap, m_scale, m_dot })
                slider.IsVisible = m_shapeControls == (slider != m_red && slider != m_green && slider != m_blue);
        }
        m_preview.Color = m_working.Color;
        m_shapePreview.Shape=m_working.Shape;m_shapePreview.Tint=m_working.Color;m_shapePreview.CrossStyle=m_working.Style;
        m_preview.Text = $"右侧为等比例缩小预览 · RGB {m_working.Color.R}, {m_working.Color.G}, {m_working.Color.B}";
        if (m_copyGroup?.IsClicked == true) {
            try {
                ClipboardManager.ClipboardString = "1087216872";
                m_status.Text = "群号已复制：1087216872";
            }
            catch (Exception e) {
                m_status.Text = "无法访问系统剪贴板，请手动输入群号 1087216872";
                KnifeLog.Warning("[CS_UI] copy group number failed: " + e.Message);
            }
        }
        if (m_edit.IsClicked) { m_returningFromLayout = true; ScreensManager.SwitchScreen(ScGunLayoutScreen.ScreenName); return; }
        if (m_bindings.IsClicked) { m_returningFromLayout = true; ScreensManager.SwitchScreen(ScGunBindingsScreen.ScreenName); return; }
        if (m_defaults.IsClicked) { m_working = new(true, true, true, true, ScUiSettings.StyleVanilla, Color.White, false, new(), ScResourcePolicy.Edition == "Optimized512"); m_built = false; return; }
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
    public override void Leave() {
        m_background.ReleaseCapture();
        KnifeLog.Trace($"[CS_SCOPE_0416] settings leave baseView={SettingsManager.ViewAngle} sensitivity={SettingsManager.LookSensitivity}");
        base.Leave();
    }
}
