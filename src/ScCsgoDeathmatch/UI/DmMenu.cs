using Engine;
using Engine.Input;
namespace Game;

/// <summary>The deathmatch menu (round 2 redesign): one pixel panel with three pages - 比赛 (what every player does, and
/// the host's match controls), 地图 (the host's arena tools), 设置 (this device's HUD and keys) - laid out so nothing has
/// to be scrolled to. A message bar at the bottom always shows the result of the last action: a refusal is never hidden
/// at the top of a long list again (round 2: "房主点开始没反应" - the start was refused for a missing region and the reason
/// sat above the fold). Host tools exist only on the process that runs the world: a remote client's menu has no host rows.</summary>
public sealed class DmMenuPanel : CanvasWidget {
    public enum Page { Match, Map, Settings }
    readonly ComponentPlayer m_player; readonly SubsystemScDeathmatch m_dm;
    readonly DmPixelPanel m_back = new() { Title = "死亡竞赛" };
    readonly Dictionary<Page, DmPixelButton> m_tabs = [];
    readonly Dictionary<Page, CanvasWidget> m_pages = [];
    readonly List<(DmPixelButton Button, Page Page, Func<bool> Visible, Func<bool> Enabled, Func<string> Text, Action Run)> m_buttons = [];
    readonly LabelWidget m_status = new() { FontScale = .66f, Color = DmPx.Text, WordWrap = true, IsHitTestVisible = false, HorizontalAlignment = WidgetAlignment.Stretch };
    readonly LabelWidget m_message = new() { FontScale = .66f, Color = DmPx.Gold, WordWrap = true, IsHitTestVisible = false, HorizontalAlignment = WidgetAlignment.Stretch };
    readonly CanvasWidget m_statusBox = new() { IsHitTestVisible = false };
    Page m_page; double m_messageAt = double.NegativeInfinity;
    // three columns fill the page exactly (round 3: at 172 the third column stood out of the panel's right edge)
    public const float W = 780, H = 452, Left = 250, Row = 50, Col = (W - Left - 14 - 2 * 8) / 3;
    public Page Current => m_page;

    public DmMenuPanel(ComponentPlayer player, SubsystemScDeathmatch dm) {
        m_player = player; m_dm = dm; Size = new Vector2(W, H);
        Children.Add(m_back);
        foreach (var (page, name, x) in new[] { (Page.Match, "比赛", W - 3 * 98f - 14), (Page.Map, "地图", W - 2 * 98f - 14), (Page.Settings, "设置", W - 98f - 14) }) {
            var tab = new DmPixelButton(name, 92, 40); tab.FontScale = .7f; m_tabs[page] = tab; Children.Add(tab); SetWidgetPosition(tab, new Vector2(x, 2));
            var content = new CanvasWidget { Size = new Vector2(W - Left - 14, H - 110) }; m_pages[page] = content; Children.Add(content); SetWidgetPosition(content, new Vector2(Left, 50));
        }
        m_statusBox.Size = new Vector2(Left - 24, H - 110); m_statusBox.Children.Add(m_status); Children.Add(m_statusBox); SetWidgetPosition(m_statusBox, new Vector2(16, 52));
        var bar = new DmPixelPanel { Fill = new Color(8, 9, 11, 240), Edge = DmPx.GoldDark }; bar.Size = new Vector2(W - 140, 46); bar.HorizontalAlignment = WidgetAlignment.Near; bar.VerticalAlignment = WidgetAlignment.Near;
        Children.Add(bar); SetWidgetPosition(bar, new Vector2(12, H - 56));
        var messageBox = new CanvasWidget { Size = new Vector2(W - 164, 40), IsHitTestVisible = false }; messageBox.Children.Add(m_message); m_message.VerticalAlignment = WidgetAlignment.Center;
        Children.Add(messageBox); SetWidgetPosition(messageBox, new Vector2(24, H - 53));

        bool Host() => dm.Authority && dm.Frozen is null; bool Arena() => Host() && dm.Enabled; bool Governing() => dm.View.Governing;
        bool Phase(params DmPhase[] phases) => Arena() && phases.Contains(dm.Match.Phase);
        var self = dm.View.Of(player);
        void Add(Page page, int col, int row, int span, Func<string> text, Func<bool> visible, Action run, Func<bool> enabled = null) {
            var b = new DmPixelButton("", Col * span + 8 * (span - 1), 44); m_pages[page].Children.Add(b); m_pages[page].SetWidgetPosition(b, new Vector2(col * (Col + 8), row * Row));
            m_buttons.Add((b, page, visible, enabled ?? (() => true), text, run));
        }
        // ---- 比赛
        Add(Page.Match, 0, 0, 1, () => "配装轮盘", Governing, () => { Close(); DmWheel.Open(player, dm); });
        Add(Page.Match, 1, 0, 1, () => self.Entered ? "已准备" : self.Desired.IsEmpty ? "空手准备" : "准备", () => Governing(), () => dm.RequestEnter(player, self.Desired.IsEmpty), () => !self.Entered);
        Add(Page.Match, 2, 0, 1, () => "转为观战", () => Governing() && self.Entered, () => dm.RequestSpectate(player));
        Add(Page.Match, 0, 1, 1, () => "开始比赛", () => Arena() && dm.Match.Phase is DmPhase.Editing or DmPhase.Lobby, () => Say(dm.StartMatch(), $"{DmFixed.CountdownSeconds:0} 秒后开始！", close: true));
        Add(Page.Match, 1, 1, 1, () => "开放大厅", () => Phase(DmPhase.Editing), () => Say(dm.OpenLobby(), "大厅已开放：所有玩家回到准备点，竞技规则生效"));
        Add(Page.Match, 1, 1, 1, () => "回到地图编辑", () => Phase(DmPhase.Lobby), () => Say(dm.EditMap(), "已回到地图编辑：竞技规则暂停"));
        Add(Page.Match, 0, 1, 1, () => dm.Match?.Phase == DmPhase.Countdown ? "取消开始" : "结束本局", () => Phase(DmPhase.Countdown, DmPhase.Running), () => Say(dm.StopMatch(), "已结束", close: true));   // closed: the results show on the HUD (round 3: the open menu hid them)
        Add(Page.Match, 0, 2, 1, () => "局长 −", () => Phase(DmPhase.Editing, DmPhase.Lobby), () => Say(dm.SetRules(dm.Match.Rules with { Minutes = Math.Max(DmRules.MinMinutes, dm.Match.Rules.Minutes - 1) }), $"局长 {dm.Match.Rules.Minutes} 分钟"));
        Add(Page.Match, 1, 2, 1, () => "局长 +", () => Phase(DmPhase.Editing, DmPhase.Lobby), () => Say(dm.SetRules(dm.Match.Rules with { Minutes = Math.Min(DmRules.MaxMinutes, dm.Match.Rules.Minutes + 1) }), $"局长 {dm.Match.Rules.Minutes} 分钟"));
        Add(Page.Match, 2, 2, 1, () => dm.View.Rules.Grenades ? "投掷物：开放" : "投掷物：关闭", () => Phase(DmPhase.Editing, DmPhase.Lobby), () => Say(dm.SetRules(dm.Match.Rules with { Grenades = !dm.Match.Rules.Grenades }), dm.Match.Rules.Grenades ? "投掷物已开放" : "投掷物已关闭"));
        // ---- 地图
        Add(Page.Map, 0, 0, 3, () => "把本世界启用为竞技世界", () => Host() && !dm.Enabled, () => { Say(dm.EnableArena(), "已启用：本世界现在是死亡竞赛专用世界。接下来设两个角点、复活点和准备点"); });
        Add(Page.Map, 0, 0, 1, () => dm.Corner(0) is { } c ? "角点 1（已定）" : "角点 1（脚下）", () => Phase(DmPhase.Editing, DmPhase.Lobby), () => Corner(0));
        Add(Page.Map, 1, 0, 1, () => dm.Corner(1) is { } c ? "角点 2（已定）" : "角点 2（脚下）", () => Phase(DmPhase.Editing, DmPhase.Lobby), () => Corner(1));
        Add(Page.Map, 2, 0, 1, () => "准备/观战点（这里）", () => Phase(DmPhase.Editing, DmPhase.Lobby), () => { var (p, yaw) = SubsystemScDeathmatch.Stand(player); Say(dm.SetLobby(p, yaw), "准备/观战点已设在这里（蓝色光柱）", close: true); });
        Add(Page.Map, 0, 1, 1, () => "添加复活点（这里）", () => Phase(DmPhase.Editing, DmPhase.Lobby), () => { var (p, yaw) = SubsystemScDeathmatch.Stand(player); Say(dm.AddSpawn(p, yaw), $"已添加复活点 #{dm.Arena.NextSpawnId - 1}（绿色光柱，箭头是复活后的朝向）。走到下一个位置再加", close: true); });
        Add(Page.Map, 1, 1, 1, () => "删除最近的复活点", () => Phase(DmPhase.Editing, DmPhase.Lobby), () => {
            if (dm.Arena.Spawns.Count == 0) { Say("还没有复活点", null); return; }
            var feet = player.ComponentBody.Position; var nearest = dm.Arena.Spawns.OrderBy(s => Vector3.Distance(s.Position, feet)).First(); Say(dm.RemoveSpawn(nearest.Id), $"已删除复活点 #{nearest.Id}");
        }, () => dm.Arena.Spawns.Count > 0);
        Add(Page.Map, 2, 1, 1, () => "检查地图", () => Arena(), () => { var issues = dm.ArenaIssues(); Say(issues.Count == 0 ? null : string.Join("；", issues.Select(i => (i.Blocks ? "必须处理：" : "建议：") + i.Message)), issues.Count == 0 ? $"地图检查通过：{DmArenaRules.Usable(dm.Arena, dm.Probe).Count} 个复活点可用" : null); });
        // ---- 设置
        Add(Page.Settings, 0, 0, 2, () => "编辑 HUD 布局", () => true, () => { Close(); DmLayoutScreen.Open(player.ComponentInput.IsControlledByTouch); });
        int keyRow = 1;
        foreach (string id in DmUiSettings.KeyIds) { string key = id; Add(Page.Settings, 0, keyRow++, 2, () => $"{DmUiSettings.KeyLabel(key)}：{Short(DmUiSettings.KeyOf(key))}", () => !player.ComponentInput.IsControlledByTouch, () => ChooseKey(key)); }
        Add(Page.Settings, 2, 1, 1, () => DmUiSettings.BoardHold ? "计分板：按住显示" : "计分板：按一下开关", () => !player.ComponentInput.IsControlledByTouch, () => { DmUiSettings.BoardHold = !DmUiSettings.BoardHold; DmUiSettings.Save(); });
        var close = new DmPixelButton("关闭", 108, 46); Children.Add(close); SetWidgetPosition(close, new Vector2(W - 120, H - 56)); m_buttons.Add((close, (Page)(-1), () => true, () => true, () => "关闭", Close));
        m_page = !dm.Enabled ? Page.Map : Page.Match;
    }
    static string Short(string key) => string.IsNullOrEmpty(key) ? "未设置" : key;
    void Close() => m_player.ComponentGui.ModalPanelWidget = null;
    void Corner(int i) {
        var cell = SubsystemScDeathmatch.Cell(m_player.ComponentBody.Position);
        string refused = m_dm.SetCorner(i, cell);
        Say(refused, m_dm.Corner(1 - i) is null ? $"角点 {i + 1} 已定（金色光柱）。走到对角，再按“角点 {2 - i}”" : $"竞技区域已设置：{m_dm.Arena.MaxX - m_dm.Arena.MinX + 1}×{m_dm.Arena.MaxZ - m_dm.Arena.MinZ + 1} 格（青色框）", close: true);
    }
    /// <summary>The result of an action: the refusal when there is one, the success text otherwise - in the message bar
    /// while the menu stays open, as the game's small message when it closes. A map action the player walks on from
    /// (a corner, a respawn point, the lobby point) closes the menu when it succeeds (round 3: on the phone the open
    /// menu kept the player from walking to the next place; the toast also lay over the message bar).</summary>
    void Say(string refused, string done, bool close = false) {
        string text = refused ?? done; if (string.IsNullOrEmpty(text)) return;
        m_message.Text = text; m_message.Color = refused is null ? DmPx.Green : DmPx.Gold; m_messageAt = m_dm.Now;
        if (close && refused is null) { Close(); m_player.ComponentGui.DisplaySmallMessage(text, Color.White, false, false); }
    }
    /// <summary>What the host has to do next before a match can start, in the order it is done (null: nothing). Unlike
    /// the arena check's own words it knows the corner already set: "还没有设置竞技区域的两个角点" after corner 1 read
    /// as "set corner 1 again" (round 3, the user's report).</summary>
    public static string NextStep(SubsystemScDeathmatch dm, IReadOnlyList<DmArenaIssue> issues) {
        bool one = dm.Corner(0) is not null, two = dm.Corner(1) is not null;
        if (one != two) return one ? "走到对角，按“角点 2”" : "走到对角，按“角点 1”";   // a region being made (the old one, if any, stays until then)
        if (!dm.Arena.HasRegion) return "站在区域一角按“角点 1”，再到对角按“角点 2”";
        return issues.FirstOrDefault(i => i.Blocks)?.Message;
    }
    void ChooseKey(string id) {
        var options = new[] { "" }.Concat(ScGunBindings.SelectableKeys()).ToArray();
        DialogsManager.ShowDialog(m_player.GuiWidget, new ListSelectionDialog(DmUiSettings.KeyLabel(id), options, 52, item => {
            string key = (string)item; if (key == "") return "不使用按键";
            var conflicts = DmUiSettings.Conflicts(id, key);
            return ScGunBindings.KeyLabel(key) + (conflicts.Count > 0 ? "（已用于：" + string.Join("、", conflicts) + "）" : "");
        }, item => {
            string key = (string)item; var conflicts = DmUiSettings.Conflicts(id, key);
            DmUiSettings.Keys[id] = key; DmUiSettings.Save();
            Say(null, conflicts.Count > 0 ? $"已设置为 {ScGunBindings.KeyLabel(key)}；注意此键也用于：{string.Join("、", conflicts)}（两者都会响应）" : "按键已保存");
        }));
    }
    string Status() {
        var view = m_dm.View; var self = view.Of(m_player);
        if (m_dm.Frozen is { } frozen) return frozen;
        if (!m_dm.Enabled) return m_dm.Authority ? "本世界还不是竞技世界。\n\n在“地图”页启用后，它会成为死亡竞赛专用世界（不影响其他世界）。建议在专门搭建的竞技地图里启用。" : "服务器的世界不是竞技世界。";
        string phase = view.Phase switch { DmPhase.Editing => "地图编辑", DmPhase.Lobby => "大厅", DmPhase.Countdown => "即将开始", DmPhase.Running => "比赛中 " + DmTimerElement.Clock(view.PhaseEndsAt - m_dm.Now), _ => "结算" };
        var arena = m_dm.Arena;
        string me = !self.Entered ? "未准备" : self.Fighting ? $"存活 {self.Health} 血 / {self.Armour} 甲" : view.Phase == DmPhase.Running ? "已准备，等待复活" : "已准备";
        return $"状态：{phase}\n局长：{view.Rules.Minutes} 分钟\n{Ready()}\n我：{me}\n\n区域：{(arena.HasRegion ? $"{arena.MaxX - arena.MinX + 1}×{arena.MaxZ - arena.MinZ + 1} 格" : m_dm.Corner(0) is not null || m_dm.Corner(1) is not null ? "只设了一个角点" : "未设置")}\n复活点：{arena.Spawns.Count} 个\n准备点：{(arena.HasLobby ? "已设置" : "未设置")}"
            + (m_dm.Authority && view.Phase is DmPhase.Editing or DmPhase.Lobby && NextStep(m_dm, m_dm.ArenaIssues()) is { } next ? $"\n\n下一步：{next}" : "");
    }
    /// <summary>Who is ready: the host sees the names, ready and not (2026-10-06, the user: "房主能查看准备列表，能看谁没准备"); a
    /// client sees the count. "准备" replaced "入场" the same day.</summary>
    string Ready() {
        if (!m_dm.Authority || m_dm.Match is null) return $"已准备：{m_dm.EnteredCount} 人";
        var players = m_dm.Match.Players.Where(p => p.Connected).ToList();
        static string Names(IEnumerable<string> names) { var list = names.ToList(); return list.Count == 0 ? "无" : string.Join("、", list); }
        string Of(bool entered) => Names(players.Where(p => p.Entered == entered).Select(p => string.IsNullOrEmpty(p.Name) ? p.Key : p.Name));
        return $"已准备 {players.Count(p => p.Entered)}/{players.Count}：{Of(true)}\n未准备：{Of(false)}";
    }
    string Hint() {
        var phase = m_dm.View.Phase;
        return m_page switch {
            Page.Map when !m_dm.Enabled => "先点“把本世界启用为竞技世界”。",
            Page.Map => "站到位置上再按按钮。光柱与线框只有房主在编辑和大厅时看得到，隔着方块也能看见。",
            Page.Match when !m_dm.Enabled => "",
            Page.Match when phase == DmPhase.Countdown => m_dm.Authority ? "即将开始；“取消开始”可回到大厅。" : "比赛即将开始。",
            Page.Match when phase == DmPhase.Running => "比赛进行中：改配装在下一条命生效；“榜单”看比分。" + (m_dm.Authority ? "“结束本局”立即结算。" : ""),
            Page.Match when phase == DmPhase.Results => "本局已结算，稍后回到大厅。",
            Page.Match when m_dm.Authority => "流程：大家配装并点“准备”，然后房主点“开始比赛”。",
            Page.Match when phase == DmPhase.Editing => "房主正在搭建地图；开放大厅后按 B（触屏点“配装”）选装备，再点“准备”。",
            Page.Match when phase == DmPhase.Lobby => "按 B（触屏点“配装”）选装备，选好后点“准备”，等房主开始。",
            Page.Settings when !m_player.ComponentInput.IsControlledByTouch && DmUiSettings.KeyIds.SelectMany(id => DmUiSettings.Conflicts(id, DmUiSettings.KeyOf(id)).Select(c => $"{DmUiSettings.KeyOf(id)}（{DmUiSettings.KeyLabel(id)}）也用于{c}")).FirstOrDefault() is { } conflict
                => "注意：" + conflict + "。可点上面的按键改成别的键。",
            _ => "" };
    }
    Matrix m_fit = Matrix.Identity;
    public override void Arrange(Vector2 position, Vector2 parentActualSize) => DmFit.Arranged(this, m_fit, () => base.Arrange(position, parentActualSize));
    public override void Update() {
        m_fit = DmFit.Apply(this, m_player.ComponentGui, Size);
        if (Input.Back || Input.Cancel) { Close(); return; }
        foreach (var (page, tab) in m_tabs) { tab.Selected = page == m_page; tab.IsVisible = page != Page.Map || m_dm.Authority; if (tab.IsClicked) m_page = page; }
        foreach (var (page, content) in m_pages) content.IsVisible = page == m_page;
        m_status.Text = Status();
        if (m_dm.Now - m_messageAt > 12 && m_message.Text.Length > 0 && m_messageAt > double.NegativeInfinity) m_message.Color = DmPx.Dim;
        // until an action answers, the bar says what to do on this page (and follows the page and the phase)
        if (m_messageAt == double.NegativeInfinity) { m_message.Text = Hint(); m_message.Color = DmPx.Dim; }
        foreach (var (button, page, visible, enabled, text, run) in m_buttons) {
            button.IsVisible = (page == (Page)(-1) || page == m_page) && visible(); if (!button.IsVisible) continue;
            button.Text = text(); button.IsEnabled = enabled();
            if (button.IsClicked) { try { run(); } catch (Exception e) { Say("操作失败：" + e.Message, null); KnifeLog.Warning("[CS_DM] menu action failed: " + e); } return; }
        }
    }
}

public static class DmMenu {
    public static void Open(ComponentPlayer player, SubsystemScDeathmatch dm) {
        try { player.ComponentGui.ModalPanelWidget = new DmMenuPanel(player, dm); }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("dm-menu", "[CS_DM] the menu could not be opened: " + e); }
    }
}

/// <summary>The deathmatch HUD's layout editor (DM-10): every element as an outline at its real size and place on a
/// full-screen preview, dragged directly, with its size, turn, opacity and visibility on a panel that can be folded
/// away. It edits a copy - Cancel leaves the saved layout alone - of one device kind at a time (keyboard and mouse, or
/// touch), and the reset entries are always there, so no element can be lost off screen or switched off for good.
/// Hidden elements are drawn dim so they can be found. Nothing here can buy, fire or switch a weapon.</summary>
public sealed class DmLayoutScreen : Screen {
    public const string ScreenName = "ScCsgoDeathmatchLayout";
    static readonly Color Cyan = new(120, 225, 240);
    sealed class Proxy : CanvasWidget {
        public readonly DmPixelPanel Box = new() { Fill = new Color(20, 26, 32) * .6f, Edge = Cyan };
        public readonly LabelWidget Name = new() { FontScale = .6f, DropShadow = true, IsHitTestVisible = false, HorizontalAlignment = WidgetAlignment.Center, VerticalAlignment = WidgetAlignment.Center, TextAnchor = TextAnchor.HorizontalCenter };
        public Proxy() { IsHitTestVisible = false; HorizontalAlignment = WidgetAlignment.Near; VerticalAlignment = WidgetAlignment.Near; Children.Add(Box); Children.Add(Name); }
    }
    readonly CanvasWidget m_preview = new() { Size = new Vector2(float.PositiveInfinity), ClampToBounds = true };
    readonly Dictionary<string, Proxy> m_proxies = new(StringComparer.Ordinal);
    readonly CanvasWidget m_panel = new() { Size = new Vector2(420, 470), HorizontalAlignment = WidgetAlignment.Near, VerticalAlignment = WidgetAlignment.Near, Margin = new Vector2(10, 10) };
    readonly StackPanelWidget m_controls = new() { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch, Margin = new Vector2(10, 8) };
    readonly LabelWidget m_title = new() { FontScale = .78f, Color = DmPx.Gold, DropShadow = false };
    readonly DmPixelButton m_visible = new("", 396);
    readonly DmPixelSlider m_scale = new(DmElementLayout.MinScale, DmElementLayout.MaxScale, .05f, 1) { Format = v => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "x" },
        m_rotation = new(-180, 180, 5, 0) { Format = v => ((int)MathF.Round(v)).ToString(System.Globalization.CultureInfo.InvariantCulture) },
        m_opacity = new(DmElementLayout.MinOpacity, 1, .05f, 1) { Format = v => ((int)MathF.Round(v * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%" };
    bool m_shown = true;
    readonly DmPixelButton m_next = new("下一项", 120), m_device = new("", 200), m_resetOne = new("恢复此项", 120), m_resetAll = new("全部恢复默认", 160),
        m_save = new("保存", 100), m_cancel = new("取消", 100), m_fold = new("收起", 80), m_unfold = new("展开面板", 130);
    readonly ScGunWorldBackground m_background = new();
    Dictionary<string, DmElementLayout> m_working = new(StringComparer.Ordinal);
    bool m_touch, m_moved; string m_selected = DmHudIds.Buy, m_dragging; Vector2 m_grab, m_pressedAt; Screen m_back;
    /// <summary>How far a press must travel before it moves the element (round 3: a tap that only selected an element
    /// moved it a few units).</summary>
    public const float DragThreshold = 8;

    public DmLayoutScreen() {
        Children.Add(m_background); Children.Add(m_preview);
        foreach (string id in DmHudIds.All) { var proxy = new Proxy(); proxy.Size = DmHudIds.Size(id); proxy.Name.Text = DmHudIds.Label(id); m_proxies[id] = proxy; m_preview.Children.Add(proxy); }
        m_panel.Children.Add(new DmPixelPanel { Title = "HUD 布局" }); m_controls.Margin = new Vector2(12, 40); m_panel.Children.Add(m_controls);
        m_title.WordWrap = true; m_title.HorizontalAlignment = WidgetAlignment.Stretch; m_controls.Children.Add(m_title);
        StackPanelWidget Bar(params Widget[] widgets) { var bar = new StackPanelWidget { Direction = LayoutDirection.Horizontal, Margin = new Vector2(0, 3) }; foreach (var w in widgets) { w.Margin = new Vector2(2, 0); bar.Children.Add(w); } return bar; }
        m_controls.Children.Add(Bar(m_next, m_device, m_fold)); m_visible.Margin = new Vector2(2, 3); m_controls.Children.Add(m_visible);
        foreach (var (slider, name) in new[] { (m_scale, "大小"), (m_rotation, "旋转"), (m_opacity, "不透明度") }) {
            slider.Size = new Vector2(296, 44); var row = new StackPanelWidget { Direction = LayoutDirection.Horizontal, Margin = new Vector2(0, 2) };
            row.Children.Add(new LabelWidget { Text = name, FontScale = .7f, Color = DmPx.Text, Size = new Vector2(96, 44), VerticalAlignment = WidgetAlignment.Center, IsHitTestVisible = false }); row.Children.Add(slider); m_controls.Children.Add(row);
        }
        m_controls.Children.Add(Bar(m_resetOne, m_resetAll)); m_controls.Children.Add(Bar(m_save, m_cancel));
        m_controls.Children.Add(new LabelWidget { Text = "直接拖动方框调整位置；点一下选中。隐藏的项目显示为暗色，用“下一项”也能找到。键鼠和触屏各有一套布局。", FontScale = .58f, Color = DmPx.Dim, WordWrap = true, HorizontalAlignment = WidgetAlignment.Stretch, Margin = new Vector2(0, 4) });
        Children.Add(m_panel);
        m_unfold.HorizontalAlignment = WidgetAlignment.Near; m_unfold.VerticalAlignment = WidgetAlignment.Near; m_unfold.Margin = new Vector2(10, 10); m_unfold.IsVisible = false; Children.Add(m_unfold);
    }
    public static void Open(bool touch) {
        if (!ScreensManager.m_screens.ContainsKey(ScreenName)) ScreensManager.AddScreen(ScreenName, new DmLayoutScreen());
        ScreensManager.SwitchScreen(ScreenName, touch);
    }
    public override void Enter(object[] parameters) {
        m_background.ResetCapture(); m_back = ScreensManager.PreviousScreen;
        Use(parameters is { Length: > 0 } && parameters[0] is true);
        m_panel.IsVisible = true; m_unfold.IsVisible = false; m_dragging = null;
    }
    public override void Leave() { m_background.ReleaseCapture(); base.Leave(); }
    void Use(bool touch) { m_touch = touch; m_working = DmHudIds.All.ToDictionary(id => id, id => DmUiSettings.Layout(id, touch, m_preview.ActualSize).Copy(), StringComparer.Ordinal); Select(m_selected); }
    void Select(string id) {
        m_selected = id; var layout = m_working[id];
        m_shown = layout.Visible; m_scale.Min = DmHudIds.MinScale(id); m_scale.Value = layout.Scale; m_rotation.Value = layout.Rotation; m_opacity.Value = layout.Opacity;
    }
    void Back() => ScreensManager.SwitchScreen(m_back ?? ScreensManager.FindScreen<Screen>("Game"));
    /// <summary>The topmost element under a point of the preview (hidden ones included: they must stay findable).</summary>
    public static string ElementAt(Vector2 point, Vector2 area, IReadOnlyDictionary<string, DmElementLayout> layouts, string preferred) {
        if (preferred is not null && layouts[preferred].Contains(point, area, DmHudIds.Size(preferred), DmElement.Margin_)) return preferred;
        foreach (string id in DmHudIds.All.Reverse()) if (layouts[id].Contains(point, area, DmHudIds.Size(id), DmElement.Margin_)) return id;
        return null;
    }
    public override void Update() {
        Vector2 area = m_preview.ActualSize;
        if (Input.Back || Input.Cancel || m_cancel.IsClicked) { Back(); return; }
        if (m_save.IsClicked) { foreach (var pair in m_working) (m_touch ? DmUiSettings.Touch : DmUiSettings.Desktop)[pair.Key] = pair.Value.Normalize(); DmUiSettings.Save(); Back(); return; }
        if (m_fold.IsClicked) { m_panel.IsVisible = false; m_unfold.IsVisible = true; }
        if (m_unfold.IsClicked) { m_panel.IsVisible = true; m_unfold.IsVisible = false; }
        if (m_device.IsClicked) Use(!m_touch);
        if (m_next.IsClicked) Select(DmHudIds.All[(Array.IndexOf(DmHudIds.All, m_selected) + 1) % DmHudIds.All.Length]);
        if (m_resetOne.IsClicked) { m_working[m_selected] = DmHudIds.Default(m_selected, m_touch, m_preview.ActualSize); Select(m_selected); }
        if (m_resetAll.IsClicked) { foreach (string id in DmHudIds.All) m_working[id] = DmHudIds.Default(id, m_touch, m_preview.ActualSize); Select(m_selected); }
        var layout = m_working[m_selected];
        if (m_visible.IsClicked) m_shown = !m_shown;
        if (m_panel.IsVisible) { layout.Visible = m_shown; layout.Scale = Math.Max(m_scale.Value, DmHudIds.MinScale(m_selected)); layout.Rotation = m_rotation.Value; layout.Opacity = m_opacity.Value; }
        // dragging: read from the screen itself; a press that began on the panel is the panel's
        if (Input.Press is { } press && area.X > 1) {
            Vector2 point = m_preview.ScreenToWidget(press);
            bool onPanel = m_panel.IsVisible && HitTestGlobal(press) is { } hit && (ReferenceEquals(hit, m_panel) || hit.IsChildWidgetOf(m_panel)) || ReferenceEquals(HitTestGlobal(press), m_unfold) || HitTestGlobal(press)?.IsChildWidgetOf(m_unfold) == true;
            if (m_dragging is null && !onPanel && Input.Tap is null && ElementAt(point, area, m_working, m_selected) is { } id) {
                if (id != m_selected) Select(id);
                m_dragging = id; var l = m_working[id]; m_grab = point - new Vector2(l.X * area.X, l.Y * area.Y); m_pressedAt = point; m_moved = false;
            }
            m_moved |= m_dragging is not null && Vector2.Distance(point, m_pressedAt) > DragThreshold;
            if (m_dragging is not null && m_moved) { var l = m_working[m_dragging]; Vector2 centre = point - m_grab; l.X = Math.Clamp(centre.X / area.X, 0, 1); l.Y = Math.Clamp(centre.Y / area.Y, 0, 1); }
        }
        else m_dragging = null;
        foreach (var (id, proxy) in m_proxies) {
            var l = m_working[id]; Vector2 size = DmHudIds.Size(id), corner = l.Corner(area, size, DmElement.Margin_);
            proxy.MarginLeft = corner.X; proxy.MarginTop = corner.Y; proxy.RenderTransform = l.Transform(size);
            proxy.ColorTransform = Color.White * (l.Visible ? l.Opacity : .3f);
            proxy.Box.Edge = id == m_selected ? DmPx.Gold : Cyan;
            proxy.Name.Text = DmHudIds.Label(id) + (l.Visible ? "" : "（已隐藏）");
        }
        m_title.Text = $"正在调整：{DmHudIds.Label(m_selected)}";
        m_device.Text = m_touch ? "当前：触屏布局" : "当前：键鼠布局";
        m_visible.Text = (DmHudIds.IsButton(m_selected) ? "显示此按钮：" : "显示此项：") + (m_shown ? "开" : "关"); m_visible.Selected = m_shown;
    }
}
