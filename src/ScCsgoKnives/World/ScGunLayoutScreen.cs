using Engine;
using Engine.Graphics;
using Engine.Input;
namespace Game;

/// <summary>The touch-button layout editor.
///
/// The preview fills the whole screen at exactly the proportions the controls area has in play, so a button
/// dragged here lands where it is dragged. It works on a copy of the current hand's layout: Cancel leaves the
/// saved one untouched, Save writes it, and the reset entries are always reachable, so a button dragged off
/// screen or switched off can never strand the player in a file editor.
///
/// Nothing here can fire, throw, reload or fit a silencer. The previewed buttons are drawings with no hit
/// testing at all, and the drag is read from the screen itself, so no combat action exists to trigger.</summary>
public sealed class ScGunLayoutScreen : Screen {
    public const string ScreenName = "ScCsgoGunLayout";
    public const string HudId="ammo_hud";
    static readonly string[] Entries=[..ScGunFunctions.All,HudId];
    ScHudPosition m_hud=new();
    readonly LabelWidget m_hudProxy=new(){Text="弹药 HUD\n30 / 30",FontScale=.7f,DropShadow=true,TextAnchor=TextAnchor.HorizontalCenter|TextAnchor.VerticalCenter,
        Size=new Vector2(116,76),IsHitTestVisible=false,IsVisible=false};
    CheckboxWidget m_hudCustom;
    readonly ScHudGesture m_hudGesture=new();

    Screen m_back;
    bool m_leftHanded;
    bool m_built, m_narrow, m_collapsed;
    Dictionary<string, ScButtonLayout> m_working = [];
    string m_selected = ScGunFunctions.Reload;
    string m_dragging;
    Vector2 m_dragOffset;

    /// <summary>Fills the screen: the normalised positions map one to one onto the real controls area.</summary>
    readonly CanvasWidget m_preview = new() { Size = new Vector2(float.PositiveInfinity), ClampToBounds = true };
    readonly Dictionary<string, BevelledButtonWidget> m_proxies = new(StringComparer.Ordinal);
    readonly Dictionary<string, (Color Center, Color Bevel)> m_base = new(StringComparer.Ordinal);
    readonly StackPanelWidget m_controls = new() { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch, Margin = new Vector2(8, 6) };
    readonly CanvasWidget m_panel = new();
    readonly CanvasWidget m_panelHost = new() { Size = new Vector2(float.PositiveInfinity), IsHitTestVisible = false };
    readonly StackPanelWidget m_panelBody = new() { Direction = LayoutDirection.Vertical };
    readonly StackPanelWidget m_footer = new() { Direction = LayoutDirection.Horizontal, HorizontalAlignment = WidgetAlignment.Center };
    readonly ButtonWidget m_expand = ScGunUi.Button("展开面板", 130);
    readonly ScrollPanelWidget m_panelScroll = new() { Direction = LayoutDirection.Vertical,
        HorizontalAlignment = WidgetAlignment.Stretch, VerticalAlignment = WidgetAlignment.Stretch };
    LabelWidget m_title, m_warning;
    CheckboxWidget m_enabled;
    SliderWidget m_size, m_background, m_foreground;
    ButtonWidget m_next, m_side, m_collapse, m_resetOne, m_resetAll, m_cancel, m_save;
    readonly ButtonWidget m_choose=ScGunUi.Button("选择按键或弹药 HUD",250);
    readonly ScGunWorldBackground m_worldBackground = new();

    public ScGunLayoutScreen() {
        Children.Add(m_worldBackground);
        Children.Add(m_preview);
        foreach (string id in ScGunFunctions.All) {
            var proxy = new BevelledButtonWidget { Text = ScGunFunctions.Label(id), IsHitTestVisible = false, IsUpdateEnabled = false };
            foreach (var child in proxy.AllChildren) child.IsHitTestVisible = false;
            m_base[id] = (proxy.CenterColor, proxy.BevelColor);
            m_proxies[id] = proxy;
            m_preview.Children.Add(proxy);
        }
        m_preview.Children.Add(m_hudProxy);
        m_panel.Children.Add(ScGunUi.Frame());
        m_panelScroll.Children.Add(m_controls);
        m_panelBody.Children.Add(m_panelScroll);
        m_panelBody.Children.Add(m_footer);
        m_panel.Children.Add(m_panelBody);
        m_panelHost.Children.Add(m_panel);
        Children.Add(m_panelHost);
        m_expand.HorizontalAlignment = WidgetAlignment.Near;
        m_expand.VerticalAlignment = WidgetAlignment.Near;
        m_expand.Margin = new Vector2(12, 12);
        m_expand.IsVisible = false;
        Children.Add(m_expand);
    }

    public override void Enter(object[] parameters) {
        m_worldBackground.ResetCapture();
        ScWeaponTouchPanel.SuppressAll(true);
        KnifeLog.Trace("[CS_UI_0413] layout enter: isolated background, live touch suppressed");
        m_back = ScreensManager.PreviousScreen;
        m_leftHanded = SettingsManager.LeftHandedLayout;
        m_working = ScUiSettings.CopyHand(m_leftHanded);
        m_hud=ScUiSettings.AmmoHud.Copy();
        m_hudGesture.Reset();
        m_selected = ScGunFunctions.Reload;
        m_dragging = null;
        m_collapsed = false;
        m_panel.IsVisible = true; m_expand.IsVisible = false;
        m_built = false;
    }

    public override void Leave() { m_worldBackground.ReleaseCapture(); base.Leave(); }

    void Build(bool narrow) {
        m_narrow = narrow; m_built = true;
        m_controls.Children.Clear(); m_footer.Children.Clear();
        m_title = ScGunUi.Label("", 1f, ScGunUi.Accent);
        m_title.WordWrap = true;
        m_controls.Children.Add(m_title);
        var top = new StackPanelWidget { Direction = LayoutDirection.Horizontal, HorizontalAlignment = WidgetAlignment.Stretch };
        m_next = ScGunUi.Button("下一个按键", 150); m_collapse = ScGunUi.Button("收起面板", 120);
        m_next.Margin = new Vector2(2, 0); m_collapse.Margin = new Vector2(2, 0);
        top.Children.Add(m_next); top.Children.Add(m_collapse);
        m_controls.Children.Add(top);
        m_choose.ParentWidget?.Children.Remove(m_choose);m_controls.Children.Add(m_choose);
        m_enabled = ScGunUi.Toggle("显示此按键", true);
        m_controls.Children.Add(m_enabled);
        m_hudCustom=ScGunUi.Toggle("自定义位置（关闭为自动）",false);
        m_controls.Children.Add(m_hudCustom);
        m_size = ScGunUi.Slider(.5f, 2f, .05f, 1f, "大小");
        m_background = ScGunUi.Slider(.1f, 1f, .05f, .35f, "背景透明度");
        m_foreground = ScGunUi.Slider(.4f, 1f, .05f, .9f, "文字透明度");
        foreach (var slider in new[] { m_size, m_background, m_foreground }) { slider.HorizontalAlignment = WidgetAlignment.Stretch; m_controls.Children.Add(slider); }
        m_warning = ScGunUi.Note("");
        m_controls.Children.Add(m_warning);
        m_controls.Children.Add(ScGunUi.Note("直接拖动按键或弹药 HUD 调整位置；编辑期间不会触发战斗。隐藏项用「下一个按键」或选择列表找回。面板挡住时可先收起。"));
        m_controls.Children.Add(ScGunUi.Note("仅显示与当前选中按键能同时使用的一组按钮，其他操作用「下一个按键」切换。"));
        if (GameManager.Project is null) m_controls.Children.Add(ScGunUi.Note("当前未进入世界；进入游戏后再打开本页即可看到真实游戏背景。"));
        var bar = new StackPanelWidget { Direction = narrow ? LayoutDirection.Vertical : LayoutDirection.Horizontal, HorizontalAlignment = WidgetAlignment.Center, Margin = new Vector2(0, 4) };
        m_side = ScGunUi.Button("面板换边", 120); m_resetOne = ScGunUi.Button("恢复此键", 120); m_resetAll = ScGunUi.Button("全部默认", 120);
        foreach (var b in new[] { m_side, m_resetOne, m_resetAll }) { b.Margin = new Vector2(3, 0); bar.Children.Add(b); }
        m_controls.Children.Add(bar);
        var bar2 = new StackPanelWidget { Direction = LayoutDirection.Horizontal, HorizontalAlignment = WidgetAlignment.Center, Margin = new Vector2(0, 4) };
        m_cancel = ScGunUi.Button("取消", 120); m_save = ScGunUi.Button("保存", 120);
        foreach (var b in new[] { m_cancel, m_save }) { b.Margin = new Vector2(3, 0); bar2.Children.Add(b); }
        m_footer.Children.Add(bar2);
        // The panel sits opposite the hand the buttons default to, so it starts out covering none of them.
        m_panelHost.HorizontalAlignment = WidgetAlignment.Stretch;
        m_panelHost.VerticalAlignment = WidgetAlignment.Stretch;
        m_panel.HorizontalAlignment = m_leftHanded ? WidgetAlignment.Far : WidgetAlignment.Near;
        m_panel.VerticalAlignment = narrow ? WidgetAlignment.Far : WidgetAlignment.Center;
        // Three 120-unit buttons need 378 units, not the former 320. Scroll vertically
        // on short phone screens so Save/Cancel/Reset remain reachable.
        LoadSelected();
    }

    public override void MeasureOverride(Vector2 availableSize) {
        bool narrow = availableSize.X < 650;
        if (!m_built || narrow != m_narrow) Build(narrow);
        m_panel.Size = new Vector2(Math.Min(410, Math.Max(280, availableSize.X - 24)), Math.Max(160, availableSize.Y - 24));
        m_panelHost.Size = m_preview.Size = new Vector2(float.PositiveInfinity);
        base.MeasureOverride(availableSize);
    }

    public bool PreviewVisible(string id) => id==HudId?m_selected==HudId&&m_hud.Custom:
        Concurrent.FirstOrDefault(g=>g.Contains(m_selected))?.Contains(id)==true;

    ScButtonLayout Selected => ScUiSettings.Layout(m_working, m_selected, m_leftHanded);
    void LoadSelected() {
        bool hud=m_selected==HudId;
        m_enabled.IsVisible=m_size.IsVisible=m_background.IsVisible=m_foreground.IsVisible=!hud;
        m_hudGesture.Reset();m_dragging=null;m_hudCustom.IsVisible=hud;
        if(hud){
            m_title.Text="弹药 HUD · 屏幕位置";
            m_hudCustom.IsChecked=m_hud.Custom;
            return;
        }
        var layout = Selected;
        m_title.Text = $"{ScGunFunctions.Label(m_selected)}（{m_selected}）· {(m_leftHanded ? "左手" : "右手")}布局";
        m_enabled.IsChecked = layout.Enabled;
        m_size.Value = layout.Scale; m_background.Value = layout.Background; m_foreground.Value = layout.Foreground;
    }

    /// <summary>Buttons that can be on screen at the same time; only these can genuinely overlap.</summary>
    static readonly string[][] Concurrent = [
        [ScGunFunctions.Reload, ScGunFunctions.Scope, ScGunFunctions.Inspect, ScGunFunctions.Fire],
        [ScGunFunctions.Reload, ScGunFunctions.Silencer, ScGunFunctions.Inspect, ScGunFunctions.Fire],
        [ScGunFunctions.Reload, ScGunFunctions.Burst, ScGunFunctions.Inspect, ScGunFunctions.Fire],
        [ScGunFunctions.Reload, ScGunFunctions.RevolverAlt, ScGunFunctions.Inspect, ScGunFunctions.Fire],
        [ScGunFunctions.KnifeHeavy, ScGunFunctions.Inspect],
        [ScGunFunctions.ThrowWeak, ScGunFunctions.ThrowStrong],
    ];
    public static bool Overlap(ScButtonLayout a, ScButtonLayout b, Vector2 area) {
        Vector2 pa = ScWeaponTouchPanel.CornerOf(a, area), sa = ScWeaponTouchPanel.SizeOf(a);
        Vector2 pb = ScWeaponTouchPanel.CornerOf(b, area), sb = ScWeaponTouchPanel.SizeOf(b);
        return pa.X < pb.X + sb.X && pb.X < pa.X + sa.X && pa.Y < pb.Y + sb.Y && pb.Y < pa.Y + sa.Y;
    }
    string FindOverlap(Vector2 area) {
        foreach (string[] group in Concurrent)
            for (int i = 0; i < group.Length; i++) for (int j = i + 1; j < group.Length; j++) {
                var a = ScUiSettings.Layout(m_working, group[i], m_leftHanded);
                var b = ScUiSettings.Layout(m_working, group[j], m_leftHanded);
                if (a.Enabled && b.Enabled && Overlap(a, b, area))
                    return $"{ScGunFunctions.Label(group[i])} 与 {ScGunFunctions.Label(group[j])} 重叠，同时出现时会互相遮挡。";
            }
        return "";
    }

    public override void Update() {
        if (!m_built) return;
        m_panel.IsVisible = !m_collapsed;
        m_expand.IsVisible = m_collapsed;
        if(m_selected==HudId){
            m_hud.Custom=m_hudCustom.IsChecked;m_hud.Normalize();
        }else{
            var layout=Selected;layout.Enabled=m_enabled.IsChecked;
            layout.Scale=m_size.Value;layout.Background=m_background.Value;layout.Foreground=m_foreground.Value;layout.Normalize();
        }
        if(m_choose.IsClicked) {
            m_dragging=null;
            DialogsManager.ShowDialog(this,new ListSelectionDialog("选择按键或 HUD",Entries,56,
                item=>(string)item==HudId?"弹药 HUD（位置）":ScGunFunctions.Label((string)item)+(ScUiSettings.Layout(m_working,(string)item,m_leftHanded).Enabled?"":"（已隐藏）"),
                item=>{m_selected=(string)item;LoadSelected();}));return;
        }
        if(DialogsManager.HasDialogs(this)){m_dragging=null;return;}

        Vector2 area = m_preview.ActualSize;
        if (area.X > 1 && area.Y > 1) {
            Drag(area);
            foreach (string id in ScGunFunctions.All) {
                var proxy = m_proxies[id];
                var l = ScUiSettings.Layout(m_working, id, m_leftHanded);
                proxy.IsVisible = PreviewVisible(id);
                var (center, bevel) = m_base[id];
                ScWeaponTouchPanel.Style(proxy, l, area, center, bevel);
                // Keep the original dim editor preview for switched-off buttons.
                if (!l.Enabled) proxy.Color = new Color((byte)150, (byte)150, (byte)150, (byte)90);
                if (id == m_selected) proxy.BevelColor = ScGunUi.Accent;
            }
            m_hudProxy.IsVisible=PreviewVisible(HudId);
            m_preview.SetWidgetPosition(m_hudProxy,m_hud.Position(area,m_hudProxy.Size));
            m_hudProxy.RenderTransform=m_hud.Transform(m_hudProxy.Size);
            m_warning.Text=m_selected==HudId?(m_hud.Custom?"手机：双指移动、捏合缩放、转动旋转。电脑：拖动移动，滚轮缩放，Shift＋滚轮旋转。面板挡住时先收起；保存后应用。":"当前为自动右下角位置。勾选后可直接调整 HUD。"):FindOverlap(area);
        }

        if (m_collapse.IsClicked) { m_collapsed = !m_collapsed; m_collapse.Text = m_collapsed ? "展开面板" : "收起面板"; }
        if (m_expand.IsClicked) { m_collapsed = false; m_collapse.Text = "收起面板"; }
        if (m_side.IsClicked) m_panel.HorizontalAlignment = m_panel.HorizontalAlignment == WidgetAlignment.Near ? WidgetAlignment.Far : WidgetAlignment.Near;
        if (m_next.IsClicked) {
            m_dragging=null;
            int index = Array.IndexOf(Entries, m_selected);
            m_selected = Entries[(Math.Max(0, index) + 1) % Entries.Length];
            LoadSelected();
        }
        if (m_resetOne.IsClicked) { if(m_selected==HudId)m_hud=new();else m_working[m_selected] = ScGunFunctions.Default(m_selected, m_leftHanded); LoadSelected(); }
        if (m_resetAll.IsClicked) {
            foreach (string id in ScGunFunctions.All) m_working[id] = ScGunFunctions.Default(id, m_leftHanded);
            m_hud=new();
            LoadSelected();
        }
        if (m_cancel.IsClicked || Input.Back || Input.Cancel) { Back(); return; }
        if (m_save.IsClicked) {
            var original = ScUiSettings.CopyHand(m_leftHanded);
            var originalHud=ScUiSettings.AmmoHud.Copy();
            ScUiSettings.ReplaceHand(m_leftHanded, m_working);
            ScUiSettings.AmmoHud=m_hud.Copy();
            if (!ScUiSettings.Save()) {
                ScUiSettings.ReplaceHand(m_leftHanded, original);
                ScUiSettings.AmmoHud=originalHud;
                m_warning.Text = "布局未能写入，上一份配置保持不变。";
            }
            else Back();
        }
    }

    /// <summary>True while the point is over the open control panel, which must keep its own presses.</summary>
    bool OverPanel(Vector2 screenPoint) {
        if (m_collapsed || m_panel.ActualSize.X < 1) return false;
        Vector2 local = m_panel.ScreenToWidget(screenPoint);
        return local.X >= 0 && local.Y >= 0 && local.X <= m_panel.ActualSize.X && local.Y <= m_panel.ActualSize.Y;
    }

    void Drag(Vector2 area) {
        if(m_selected==HudId&&m_hud.Custom){
            if(m_hudGesture.Step(Input.TouchLocations,m_hud,area,m_hudProxy.Size,m_preview.ScreenToWidget,p=>!OverPanel(p)&&!OverExpand(p))){m_dragging=null;return;}
            if(Input.Scroll is Vector3 wheel){
                var pos=new Vector2(wheel.X,wheel.Y);
                if(!OverPanel(pos)&&HudHit(m_preview.ScreenToWidget(pos),area)){
                    if(Input.IsKeyDown(Key.Shift))m_hud.Rotation+=wheel.Z*MathF.PI/36;
                    else m_hud.Scale*=MathF.Pow(1.1f,wheel.Z);
                    m_hud.Normalize();
                }
            }
        }else m_hudGesture.Reset();
        Vector2? press = Input.Press;
        if (press is null) { m_dragging = null; return; }
        if (m_collapsed) {
            Vector2 onExpand = m_expand.ScreenToWidget(press.Value);
            if (onExpand.X >= 0 && onExpand.Y >= 0 && onExpand.X <= m_expand.ActualSize.X && onExpand.Y <= m_expand.ActualSize.Y) return;
        }
        if (m_dragging is null && OverPanel(press.Value)) return;
        Vector2 point = m_preview.ScreenToWidget(press.Value);
        if(m_selected==HudId){
            if(!m_hud.Custom)return;
            var corner=m_hud.Position(area,m_hudProxy.Size);
            if(m_dragging==null){
                if(!HudHit(point,area))return;
                m_dragging=HudId;m_dragOffset=point-corner;return;
            }
            m_hud.X=Math.Clamp((point.X-m_dragOffset.X+m_hudProxy.Size.X/2)/area.X,0,1);
            m_hud.Y=Math.Clamp((point.Y-m_dragOffset.Y+m_hudProxy.Size.Y/2)/area.Y,0,1);
            return;
        }
        if (m_dragging is null) {
            if (point.X < 0 || point.Y < 0 || point.X > area.X || point.Y > area.Y) return;
            foreach (string id in ScGunFunctions.All.Reverse()) {
                if (!PreviewVisible(id)) continue;
                var l = ScUiSettings.Layout(m_working, id, m_leftHanded);
                Vector2 corner = ScWeaponTouchPanel.CornerOf(l, area), size = ScWeaponTouchPanel.SizeOf(l);
                if (point.X < corner.X || point.Y < corner.Y || point.X > corner.X + size.X || point.Y > corner.Y + size.Y) continue;
                m_dragging = id; m_dragOffset = point - corner;
                if (m_selected != id) { m_selected = id; LoadSelected(); }
                return;
            }
            return;
        }
        var dragged = ScUiSettings.Layout(m_working, m_dragging, m_leftHanded);
        Vector2 centre = ScWeaponTouchPanel.CentreOf(point - m_dragOffset, dragged, area);
        dragged.X = centre.X; dragged.Y = centre.Y;
    }

    bool OverExpand(Vector2 p){var local=m_expand.ScreenToWidget(p);return m_collapsed&&local.X>=0&&local.Y>=0&&local.X<=m_expand.ActualSize.X&&local.Y<=m_expand.ActualSize.Y;}
    bool HudHit(Vector2 point,Vector2 area){
        var center=m_hud.Position(area,m_hudProxy.Size)+m_hudProxy.Size/2;
        var local=Vector2.Transform(point-center,Matrix.CreateRotationZ(-m_hud.Rotation))/m_hud.Scale;
        return MathF.Abs(local.X)<=m_hudProxy.Size.X/2&&MathF.Abs(local.Y)<=m_hudProxy.Size.Y/2;
    }

    void Back() => ScreensManager.SwitchScreen(m_back ?? ScreensManager.FindScreen<Screen>(ScGunSettingsScreen.ScreenName) ?? ScreensManager.FindScreen<Screen>("Settings"));
}
