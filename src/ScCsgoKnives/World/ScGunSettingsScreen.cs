using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>The mod's own settings page, reached from the game's Settings screen, which the pause menu already
/// opens on every platform. Everything here is a control a thumb can hit; no part of it asks anyone to edit a
/// JSON file. Device settings never travel between devices; the one exception is the natural enemy-squad rules,
/// which inside a world belong to that world (saved with it) and outside a world are the defaults for new worlds.
///
/// Edits work on a copy: Save writes, Cancel puts back exactly what was there on entry.</summary>
public sealed class ScGunSettingsScreen : Screen {
    public const string ScreenName = "ScCsgoGunSettings";

    // Round 12 (2026-10-01 user request): the creative hit preview follows the damage-direction switch, and the armor HUD's
    // visibility moved to the layout editor (where its position already was), so neither is part of this page's copy.
    sealed record Snapshot(bool Buttons, bool KillFeed, bool KillSound, bool Crosshair, string Style, Color Color, bool ButtonOnly, ScCrosshairShape Shape, bool SimpleMaterials, bool DamageIndicator, bool GrenadePreview);
    Snapshot m_working;
    bool m_returningFromLayout;
    Screen m_back;
    bool m_narrow;
    bool m_built;

    readonly StackPanelWidget m_content = new() { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch };
    readonly ScrollPanelWidget m_scroll = new() { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch, VerticalAlignment = WidgetAlignment.Stretch };
    CheckboxWidget m_buttons, m_killFeed, m_killSound, m_crosshair, m_damageIndicator, m_grenadePreview;
    CheckboxWidget m_buttonOnly, m_simpleMaterials;
    SliderWidget m_width, m_length, m_gap, m_scale, m_dot;
    ButtonWidget m_edit, m_style, m_save, m_cancel, m_defaults;
    ButtonWidget m_copyGroup;
    readonly ButtonWidget m_bindings = ScGunUi.Button("武器按键绑定", 230);
    readonly ButtonWidget m_recoverView = ScGunUi.Button("恢复正常视角", 230);
    readonly ButtonWidget m_agentVoice = ScGunUi.Button("探员语音设置",260);
    // Natural enemy squads: world rules inside a world (agents installed), otherwise defaults for new worlds.
    ScEnemyRules m_enemy;
    bool m_enemyWorld;
    CheckboxWidget m_enemyNatural;
    ButtonWidget m_enemyLess, m_enemyMore, m_enemyInput, m_enemyDensity;
    LabelWidget m_enemyDays, m_enemyProgress;
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
    static Snapshot Capture() => new Snapshot(ScUiSettings.CustomButtons, ScUiSettings.KillFeed, ScUiSettings.KillSound,
        ScUiSettings.GunCrosshair, ScUiSettings.CrosshairStyle, ScUiSettings.CrosshairColor, ScUiSettings.ButtonOnlyFire, ScUiSettings.CrosshairShape, ScUiSettings.SimpleMaterials, ScUiSettings.DamageIndicator, ScUiSettings.GrenadePreview);
    static void Apply(Snapshot s) {
        ScUiSettings.CustomButtons = s.Buttons; ScUiSettings.KillFeed = s.KillFeed; ScUiSettings.KillSound = s.KillSound;
        ScUiSettings.GunCrosshair = s.Crosshair; ScUiSettings.CrosshairStyle = s.Style; ScUiSettings.CrosshairColor = s.Color;
        ScUiSettings.SimpleMaterials = s.SimpleMaterials; ScUiSettings.DamageIndicator = s.DamageIndicator; ScUiSettings.GrenadePreview = s.GrenadePreview;
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
        // The group number is the first row of the scrolling list, not a fixed header (2026-10-01 user request: a pinned
        // block took scroll space away), so the list starts right under the title.
        float scrollTop=54;
        m_root.SetWidgetPosition(m_scroll,new Vector2(12,scrollTop)); m_scroll.DesiredSize = new Vector2(w-24,Math.Max(60,h-scrollTop-100));
        float bw = Math.Min(150,(w-48)/3);
        foreach(var b in new[]{m_defaults,m_cancel,m_save}) ((CanvasWidget)b).Size = new Vector2(bw,48);
        m_root.SetWidgetPosition(m_bar,new Vector2((w-3*(bw+12))/2,h-92)); m_bar.DesiredSize = new Vector2(3*(bw+12),48);
        m_root.SetWidgetPosition(m_status,new Vector2(12,h-38)); m_status.Size = new Vector2(w-24,32);
        base.MeasureOverride(available);
    }

    public override void Enter(object[] parameters) {
        m_background.ResetCapture();
        ScWeaponTouchPanel.SuppressAll(true);
        if (!m_returningFromLayout) {
            m_back = ScreensManager.PreviousScreen; m_working = Capture();
            var project=GameManager.Project;var world=project is null?null:ScEnemyRulesBridge.Read?.Invoke(project);
            m_enemyWorld=world.HasValue;m_enemy=(world??ScUiSettings.EnemyDefaults).Normalize();
        }
        m_returningFromLayout = false;
        m_built = false;
        m_status.Text = ScUiSettings.Writable ? "" : "设置文件无法读取，已保留原文件；本次修改不会写入。";
    }

    void Build(bool narrow) {
        m_narrow = narrow; m_built = true;
        m_content.Children.Clear(); m_colors.Clear();
        // One horizontal row at every width (fits 336 px): the narrow vertical row put the label's top above the list's clip.
        m_copyGroup = ScGunUi.Button("复制群号", 130);
        m_content.Children.Add(ScGunUi.Row(ScGunUi.Label("交流群：1087216872"), m_copyGroup, false));
        // Round 12 (2026-10-01 user request: "一些设置下面不需要那么多的文字，精简一些，只保留必要提示"): a short hint only where a
        // control does not explain itself; the details stay in the confirmation dialogs.
        m_content.Children.Add(ScGunUi.Heading("视角恢复"));
        m_content.Children.Add(m_recoverView);
        m_content.Children.Add(ScGunUi.Note($"当前视野 {SettingsManager.ViewAngle*100:0.##}%、灵敏度 {SettingsManager.LookSensitivity*100:0.##}%；不开镜也像开镜时使用。"));
        m_content.Children.Add(ScGunUi.Heading("投掷物"));
        m_grenadePreview = ScGunUi.Toggle("准备投掷时显示预测轨迹", m_working.GrenadePreview);
        m_content.Children.Add(m_grenadePreview);
        m_content.Children.Add(ScGunUi.Note("红圈：起爆／起火点；蓝圈：落定点。"));
        BuildEnemy(narrow);
        m_content.Children.Add(ScGunUi.Heading("探员语音"));
        m_content.Children.Add(m_agentVoice);
        m_content.Children.Add(ScGunUi.Heading("武器画质"));
        m_simpleMaterials = ScGunUi.Toggle("简化材质（适合手机）", m_working.SimpleMaterials);
        m_content.Children.Add(m_simpleMaterials);
        m_content.Children.Add(ScGunUi.Note("降低金属反光和凹凸细节，保存后生效。"));
        m_content.Children.Add(ScGunUi.Heading("武器操作绑定"));
        m_content.Children.Add(m_bindings);
        m_content.Children.Add(ScGunUi.Heading("手机按键"));
        m_buttons = ScGunUi.Toggle("启用模组自定义按键", m_working.Buttons);
        m_content.Children.Add(m_buttons);
        m_buttonOnly = ScGunUi.Toggle("仅开火按钮（关闭为全屏操作）", m_working.ButtonOnly);
        m_content.Children.Add(m_buttonOnly);
        m_content.Children.Add(ScGunUi.Note("仅开火按钮：点空白处只转视角，不开火。"));
        m_edit = ScGunUi.Button("编辑按键布局", 190);
        m_content.Children.Add(ScGunUi.Row(ScGunUi.Label("按键位置布局"), m_edit, narrow));
        m_content.Children.Add(ScGunUi.Note($"当前为{(SettingsManager.LeftHandedLayout ? "左手" : "右手")}布局，左右手分别保存。"));

        m_content.Children.Add(ScGunUi.Heading("击杀反馈"));
        m_killFeed = ScGunUi.Toggle("击杀播报文字", m_working.KillFeed);
        m_killSound = ScGunUi.Toggle("击杀提示音效", m_working.KillSound);
        m_content.Children.Add(m_killFeed);
        m_content.Children.Add(m_killSound);
        m_content.Children.Add(ScGunUi.Heading("受击方向"));
        m_damageIndicator = ScGunUi.Toggle("受到CS武器伤害时显示方向", m_working.DamageIndicator);
        m_content.Children.Add(m_damageIndicator);
        m_content.Children.Add(ScGunUi.Note("创造模式下被击中也会显示（较淡）。"));

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
        m_content.Children.Add(ScGunUi.Note("十字参数只用于十字，圆点直径只用于圆点，原版样式只调整体大小。"));

    }

    void BuildEnemy(bool narrow) {
        m_content.Children.Add(ScGunUi.Heading("敌对小队（自然刷新）"));
        m_content.Children.Add(ScGunUi.Note(m_enemyWorld?"当前世界的规则，随世界保存。"
            :GameManager.Project is not null?"当前世界未启用探员包：以下为新世界默认值。"
            :"新世界默认值；已有世界请在世界内修改。"));
        m_enemyNatural=ScGunUi.Toggle("自然刷新敌对小队",m_enemy.Natural);m_content.Children.Add(m_enemyNatural);
        m_enemyLess=ScGunUi.Button("−",64);m_enemyMore=ScGunUi.Button("+",64);m_enemyInput=ScGunUi.Button("输入",96);
        var days=new StackPanelWidget{Direction=LayoutDirection.Horizontal,HorizontalAlignment=narrow?WidgetAlignment.Near:WidgetAlignment.Far};
        foreach(var b in new[]{m_enemyLess,m_enemyMore,m_enemyInput}){b.Margin=new Vector2(3,0);days.Children.Add(b);}
        m_enemyDays=ScGunUi.Label("");m_content.Children.Add(ScGunUi.Row(m_enemyDays,days,narrow));
        m_enemyDensity=ScGunUi.Button("",170);m_content.Children.Add(ScGunUi.Row(ScGunUi.Label("刷新密度"),m_enemyDensity,narrow));
        m_enemyProgress=ScGunUi.Note("");m_content.Children.Add(m_enemyProgress);
        m_content.Children.Add(ScGunUi.Note("关闭只停止新的自然刷新，已有敌队保留。"));
        RefreshEnemy();
    }
    void RefreshEnemy() {
        m_enemyDays.Text=m_enemy.GraceDays==0?"开档后立即允许":$"开档满 {m_enemy.GraceDays} 个游戏日后开始";
        m_enemyDensity.Text=ScEnemySpawnPolicy.Label(m_enemy.Density);
        var project=GameManager.Project;
        m_enemyProgress.Text=m_enemyWorld&&project is not null&&ScEnemyRulesBridge.Progress is {} progress?progress(project,m_enemy):"";
    }

    public override void Update() {
        if(m_agentVoice.IsClicked){if(ScAgentVoice.OpenSettings is {} settings)settings(this);else DialogsManager.ShowDialog(this,new MessageDialog("探员语音","安装独立的CS探员语音附属包后可选择中文／英文。","知道了",null,null));}
        bool narrow = ActualSize.X > 1 && ActualSize.X < 650;
        if (!m_built || narrow != m_narrow) Build(narrow);
        UpdateEnemy();
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
            SimpleMaterials = m_simpleMaterials.IsChecked, DamageIndicator = m_damageIndicator.IsChecked, GrenadePreview = m_grenadePreview.IsChecked, ButtonOnly = m_buttonOnly.IsChecked, Shape = new ScCrosshairShape(m_width.Value, m_length.Value, m_gap.Value, m_scale.Value, m_dot.Value).Normalize(),
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
        m_preview.Text = $"预览 · RGB {m_working.Color.R}, {m_working.Color.G}, {m_working.Color.B}";
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
        if (m_defaults.IsClicked) { m_working = new(true, true, true, true, ScUiSettings.StyleVanilla, Color.White, false, new(), ScResourcePolicy.Edition == "Optimized512", true, true); m_enemy=ScEnemyRules.Default; m_built = false; return; }
        if (m_cancel.IsClicked || Input.Back || Input.Cancel) { Leave(m_back); return; }
        if (m_save.IsClicked) {
            var previous = Capture();var previousEnemy=ScUiSettings.EnemyDefaults;
            Apply(m_working);
            // Outside a world (or without the agents package) the enemy rules are the new-world defaults in this file.
            if(!m_enemyWorld){ScUiSettings.NaturalEnemies=m_enemy.Natural;ScUiSettings.EnemyGraceDays=m_enemy.GraceDays;ScUiSettings.EnemyDensity=m_enemy.Density;}
            m_status.Text = ScUiSettings.Save() ? "" : "设置未能写入，磁盘上的上一份配置保持不变。";
            if (m_status.Text.Length == 0 && m_enemyWorld && !(GameManager.Project is {} project && ScEnemyRulesBridge.Write?.Invoke(project,m_enemy)==true))
                m_status.Text = "当前世界已关闭或探员包不可用，敌对小队规则未写入；其他设置已保存。";
            if (m_status.Text.Length == 0) Leave(m_back);
            else if (!ScUiSettings.Writable || m_status.Text.StartsWith("设置未能")) {
                Apply(previous);ScUiSettings.NaturalEnemies=previousEnemy.Natural;ScUiSettings.EnemyGraceDays=previousEnemy.GraceDays;ScUiSettings.EnemyDensity=previousEnemy.Density;
            }
        }
    }
    void UpdateEnemy() {
        if(m_enemyNatural is null)return;
        m_enemy=m_enemy with {Natural=m_enemyNatural.IsChecked};
        if(m_enemyLess.IsClicked)m_enemy=(m_enemy with {GraceDays=m_enemy.GraceDays-1}).Normalize();
        if(m_enemyMore.IsClicked)m_enemy=(m_enemy with {GraceDays=m_enemy.GraceDays+1}).Normalize();
        if(m_enemyDensity.IsClicked)m_enemy=m_enemy with {Density=(ScEnemyDensity)(((int)m_enemy.Density+1)%3)};
        if(m_enemyInput.IsClicked)DialogsManager.ShowDialog(this,new TextBoxDialog($"等待游戏天数（0～{ScEnemyRules.MaxGraceDays}，0为立即）",m_enemy.GraceDays.ToString(),3,text=>{
            if(text is null)return;
            if(!int.TryParse(text,out int days)||days<0||days>ScEnemyRules.MaxGraceDays){m_status.Text=$"请输入 0～{ScEnemyRules.MaxGraceDays} 的整数。";return;}
            m_enemy=m_enemy with {GraceDays=days};m_status.Text="";}));
        RefreshEnemy();
    }

    static void Leave(Screen back) => ScreensManager.SwitchScreen(back ?? ScreensManager.FindScreen<Screen>("Settings"));
    public override void Leave() {
        m_background.ReleaseCapture();
        base.Leave();
    }
}
