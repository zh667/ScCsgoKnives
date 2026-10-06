using System.Runtime.CompilerServices;
using Engine;
using Engine.Graphics;
using Engine.Input;
namespace Game;

/// <summary>One placed HUD element: a canvas of its base size, put where this device's layout says, scaled and turned
/// about its own centre (the engine draws and hit-tests through the same transform, so a turned button is pressed
/// where it is seen), faded by its opacity.</summary>
public class DmElement : CanvasWidget {
    public readonly string Id;
    public readonly Vector2 BaseSize;
    public DmElement(string id) { Id = id; BaseSize = DmHudIds.Size(id); Size = BaseSize; HorizontalAlignment = WidgetAlignment.Near; VerticalAlignment = WidgetAlignment.Near; IsVisible = false; }
    /// <summary>Safe margin kept between an element and the screen edge.</summary>
    public const float Margin_ = 4;
    public void Place(DmElementLayout layout, Vector2 area, bool show) {
        IsVisible = show && layout.Visible && area.X > 1 && area.Y > 1;
        if (!IsVisible) return;
        Vector2 corner = layout.Corner(area, BaseSize, Margin_);
        MarginLeft = corner.X; MarginTop = corner.Y; MarginRight = 0; MarginBottom = 0;
        RenderTransform = layout.Transform(BaseSize);
        ColorTransform = Color.White * layout.Opacity;
        Inert(this);
    }
    /// <summary>Takes the element's display parts out of hit testing (the engine gives a touch to the topmost
    /// hit-testable widget under it: round 3, the equipment's empty box lay over the user's CS reload button and took its
    /// touches). Buttons keep theirs; a scroll panel keeps its own (its rows do not).</summary>
    static void Inert(Widget widget) {
        if (widget is ButtonWidget) return;
        if (widget is not ScrollPanelWidget) widget.IsHitTestVisible = false;
        if (widget is ContainerWidget container) foreach (var child in container.Children) Inert(child);
    }
    protected static LabelWidget Text(float scale, TextAnchor anchor = TextAnchor.Left, Color? color = null) => new() {
        FontScale = scale, Color = color ?? DmPx.Text, DropShadow = true, TextAnchor = anchor, IsHitTestVisible = false,
        HorizontalAlignment = anchor.HasFlag(TextAnchor.HorizontalCenter) ? WidgetAlignment.Center : anchor.HasFlag(TextAnchor.Right) ? WidgetAlignment.Far : WidgetAlignment.Near };
}

sealed class DmButtonElement : DmElement {
    public readonly DmPixelButton Button;
    readonly ScWeaponButtonInput m_input = new();
    public bool Clicked { get; private set; }
    public DmButtonElement(string id, string text) : base(id) { Button = new DmPixelButton(text, BaseSize.X, BaseSize.Y) { HorizontalAlignment = WidgetAlignment.Stretch, VerticalAlignment = WidgetAlignment.Stretch }; Children.Add(Button); }
    /// <summary>Reads the button with its own finger (a touch that began on it is this button's until it is lifted, and
    /// never becomes a look, a move or a shot).</summary>
    public void Sample(bool touch) { m_input.Sample(Button, touch, IsVisible); Clicked = IsVisible && m_input.Clicked; }
    public void Release() { m_input.Cancel(); Clicked = false; }
}

sealed class DmTimerElement : DmElement {
    readonly DmPixelPanel m_back = new() { Fill = DmPx.PanelSoft, Edge = new Color(0, 0, 0, 0) };
    readonly DmPixelText m_time = new() { Scale = 4, Size = new Vector2(180, 32), Anchor = TextAnchor.HorizontalCenter, HorizontalAlignment = WidgetAlignment.Center, VerticalAlignment = WidgetAlignment.Near, Margin = new Vector2(0, 2) };
    readonly LabelWidget m_phase = Text(.54f, TextAnchor.HorizontalCenter, DmPx.Dim);
    public DmTimerElement() : base(DmHudIds.Timer) { Children.Add(m_back); Children.Add(m_time); m_phase.VerticalAlignment = WidgetAlignment.Far; m_phase.Margin = new Vector2(0, 2); Children.Add(m_phase); }
    public static string Clock(double seconds) { int s = (int)Math.Ceiling(Math.Max(0, seconds)); return $"{s / 60}:{s % 60:00}"; }
    public void Show(DmPhase phase, double remaining, bool practice, string lobby = "大厅 · 等待房主开始") {
        m_time.Text = phase switch { DmPhase.Running => Clock(remaining), DmPhase.Countdown => Math.Ceiling(Math.Max(0, remaining)).ToString("0"), DmPhase.Results => "END", DmPhase.Lobby => "LOBBY", _ => "EDIT" };
        m_time.Color = phase == DmPhase.Running && remaining <= 30 || phase == DmPhase.Countdown ? DmPx.Red : DmPx.Text;
        m_phase.Text = phase switch { DmPhase.Running => practice ? "个人死亡竞赛 · 练习" : "个人死亡竞赛", DmPhase.Countdown => "比赛即将开始", DmPhase.Results => $"{Math.Ceiling(Math.Max(0, remaining)):0} 秒后回到大厅", DmPhase.Lobby => lobby, _ => "地图编辑中（规则未生效）" };
    }
}

sealed class DmProtectElement : DmElement {
    readonly DmPixelPanel m_back = new() { Fill = new Color(240, 240, 240) * .2f, Edge = new Color(240, 240, 240) * .6f };
    readonly LabelWidget m_text = Text(.66f, TextAnchor.HorizontalCenter, Color.White);
    public DmProtectElement() : base(DmHudIds.Protect) { Children.Add(m_back); m_text.VerticalAlignment = WidgetAlignment.Center; Children.Add(m_text); }
    public void Show(double remaining) => m_text.Text = $"复活保护 {Math.Max(0, remaining):0.0} 秒 · 攻击即解除";
}

/// <summary>CS2's kill feed: killer, the weapon's silhouette, the marks the server established (CS2's own icons, drawn smooth
/// since 2026-10-06 like the wheel's), victim. The local player's kills are framed in red, as CS2 does.</summary>
public sealed class DmFeedElement : DmElement {
    sealed class Row : CanvasWidget {
        public readonly DmPixelPanel Back = new() { Fill = new Color(10, 11, 13, 200), Edge = new Color(0, 0, 0, 0) };
        public readonly StackPanelWidget Line = new() { Direction = LayoutDirection.Horizontal, HorizontalAlignment = WidgetAlignment.Far, VerticalAlignment = WidgetAlignment.Center, Margin = new Vector2(8, 3), IsHitTestVisible = false };
        public readonly LabelWidget Killer = Text(.6f), Victim = Text(.6f), Words = Text(.52f, TextAnchor.Left, DmPx.Gold);
        public readonly DmHudIcon Weapon = new() { Height = 24, VerticalAlignment = WidgetAlignment.Center, Margin = new Vector2(8, 0) };
        public readonly DmHudIcon[] Marks = Enumerable.Range(0, 5).Select(_ => new DmHudIcon { Height = 20, VerticalAlignment = WidgetAlignment.Center, Margin = new Vector2(2, 0) }).ToArray();
        public Row() {
            HorizontalAlignment = WidgetAlignment.Far; IsHitTestVisible = false; Margin = new Vector2(0, 2);
            Killer.VerticalAlignment = Victim.VerticalAlignment = Words.VerticalAlignment = WidgetAlignment.Center; Victim.Margin = new Vector2(8, 0);
            Children.Add(Back); Line.Children.Add(Killer); Line.Children.Add(Weapon); foreach (var m in Marks) Line.Children.Add(m); Line.Children.Add(Words); Line.Children.Add(Victim); Children.Add(Line);
        }
    }
    readonly Row[] m_rows = new Row[DmView.FeedLines];
    public DmFeedElement() : base(DmHudIds.Feed) {
        var stack = new StackPanelWidget { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Far, VerticalAlignment = WidgetAlignment.Near, IsHitTestVisible = false };
        for (int i = 0; i < m_rows.Length; i++) { m_rows[i] = new Row(); stack.Children.Add(m_rows[i]); }
        Children.Add(stack);
    }
    /// <summary>One line of the feed in words. Every mark is a fact the server put on the event; a mark it did not put is not shown.</summary>
    public static string Line(DmKill kill) {
        string weapon = DmNames.Weapon(kill.Weapon);
        if (!kill.Scored) return kill.Cause switch { DmDeathCause.OutOfBounds => $"{kill.VictimName} 离开了竞技区域", DmDeathCause.Suicide => $"{kill.VictimName} 自己结束了这条命" + (weapon.Length > 0 ? $"（{weapon}）" : ""), _ => $"{kill.VictimName} 死于环境" };
        var marks = DmArt.Marks(kill).Select(m => m.Word).Reverse().ToList();
        return $"{kill.KillerName}  [{weapon}{(marks.Count > 0 ? " · " + string.Join(" · ", marks) : "")}]  {kill.VictimName}";
    }
    public void Show(IReadOnlyList<(DmKill Kill, double At)> feed, double now, string selfKey) {
        var recent = feed.Where(f => now - f.At <= DmView.FeedSeconds).ToList();
        for (int i = 0; i < m_rows.Length; i++) {
            var row = m_rows[i]; bool on = i < recent.Count; row.IsVisible = on;
            if (!on) continue;
            var kill = recent[i].Kill; bool mine = kill.KillerKey == selfKey, me = kill.VictimKey == selfKey;
            row.Back.Edge = mine ? DmPx.Red : new Color(0, 0, 0, 0); row.Back.Fill = mine ? new Color(40, 10, 10, 210) : new Color(10, 11, 13, 200);
            foreach (var m in row.Marks) m.IsVisible = false;
            if (!kill.Scored) {
                row.Killer.Text = ""; row.Weapon.Texture = DmPx.HudIcon("kill_suicide"); row.Words.Text = ""; row.Victim.Text = Line(kill); row.Victim.Color = me ? DmPx.Red : DmPx.Text; continue;
            }
            row.Killer.Text = kill.KillerName; row.Killer.Color = mine ? DmPx.Gold : DmPx.Text;
            row.Weapon.Texture = DmPx.WeaponHud(kill.Weapon);
            var words = new List<string>(); int used = 0;
            foreach (var (icon, word) in DmArt.Marks(kill)) {
                if (used < row.Marks.Length && DmPx.HudIcon("kill_" + icon) is { } t) { row.Marks[used].Texture = t; row.Marks[used].IsVisible = true; used++; } else words.Add(word);
            }
            row.Words.Text = (row.Weapon.Texture is null ? DmNames.Weapon(kill.Weapon) + " " : "") + string.Join(" ", words);
            row.Victim.Text = kill.VictimName; row.Victim.Color = me ? DmPx.Red : DmPx.Text;
        }
    }
}

sealed class DmDeathElement : DmElement {
    readonly DmPixelPanel m_back = new() { Fill = new Color(14, 8, 8, 225), Edge = DmPx.Red };
    readonly LabelWidget m_title = Text(.84f, TextAnchor.HorizontalCenter, DmPx.Red), m_detail = Text(.62f, TextAnchor.HorizontalCenter), m_next = Text(.58f, TextAnchor.HorizontalCenter, DmPx.Dim);
    readonly DmHudIcon m_weapon = new() { Height = 42, HorizontalAlignment = WidgetAlignment.Center, VerticalAlignment = WidgetAlignment.Center };
    public DmDeathElement() : base(DmHudIds.Death) {
        Children.Add(m_back);
        m_title.VerticalAlignment = WidgetAlignment.Near; m_title.Margin = new Vector2(0, 10); m_weapon.Margin = new Vector2(0, -4); m_detail.VerticalAlignment = WidgetAlignment.Far; m_detail.Margin = new Vector2(0, 30); m_next.VerticalAlignment = WidgetAlignment.Far; m_next.Margin = new Vector2(0, 8);
        Children.Add(m_title); Children.Add(m_weapon); Children.Add(m_detail); Children.Add(m_next);
    }
    /// <param name="buy">How this device opens the buy wheel ("点“配装”", "按 B").</param>
    public void Show(DmKill death, DmPlayerPhase phase, bool entered, bool running, string buy) {
        m_title.Text = death is null ? "等待复活" : death.Scored ? $"你被 {death.KillerName} 击杀" : death.Cause == DmDeathCause.OutOfBounds ? "你离开了竞技区域" : death.Cause == DmDeathCause.Suicide ? "你结束了自己的这条命" : "你死于环境";
        m_weapon.Texture = death?.Scored == true ? DmPx.WeaponHud(death.Weapon) : null;
        m_detail.Text = death is null || !death.Scored ? "" : DmNames.Weapon(death.Weapon) + string.Concat(DmArt.Marks(death).Select(m => " · " + m.Word));
        m_next.Text = !running ? "本局已结束" : phase == DmPlayerPhase.DeathView ? $"即将复活 · 复活保护期间{buy}换装备立即生效" : phase == DmPlayerPhase.SpawnPending ? "正在寻找安全的复活点…" : entered ? "" : "已转为观战，可在竞技菜单重新准备";
    }
}

sealed class DmScoresElement : DmElement {
    readonly DmPixelPanel m_back = new() { Title = "计分板" };
    readonly StackPanelWidget m_rows = new() { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch, IsHitTestVisible = false };
    readonly ScrollPanelWidget m_scroll = new() { Direction = LayoutDirection.Vertical, HorizontalAlignment = WidgetAlignment.Stretch, VerticalAlignment = WidgetAlignment.Stretch, Margin = new Vector2(10, 42) };
    string m_shown;
    public DmScoresElement() : base(DmHudIds.Scores) { Children.Add(m_back); m_scroll.Children.Add(m_rows); Children.Add(m_scroll); }
    static Widget Row(string place, string name, string kills, string deaths, string assists, Color color, bool header, bool self) {
        var row = new CanvasWidget { Size = new Vector2(-1, 30), HorizontalAlignment = WidgetAlignment.Stretch, IsHitTestVisible = false };
        if (self) row.Children.Add(new DmPixelPanel { Fill = DmPx.Gold * .2f, Edge = DmPx.Gold * .7f });
        var nameLabel = Text(.62f, TextAnchor.Left, color); nameLabel.Text = name; nameLabel.Margin = new Vector2(54, 0); nameLabel.VerticalAlignment = WidgetAlignment.Center; row.Children.Add(nameLabel);
        foreach (var (text, x, w) in new[] { (place, 6f, 40f), (kills, 330f, 60f), (deaths, 400f, 60f), (assists, 470f, 60f) }) {
            var t = new DmPixelText { Text = text, Scale = header ? 2 : 3, Color = color, Size = new Vector2(w, 30), Anchor = TextAnchor.Left, HorizontalAlignment = WidgetAlignment.Near, VerticalAlignment = WidgetAlignment.Center, Margin = new Vector2(x, 0) };
            row.Children.Add(t);
        }
        return row;
    }
    /// <summary>The board from the server's rows. Equal kills share a place: no order is invented between them.</summary>
    public void Show(string title, IReadOnlyList<DmScore> scores, string selfKey) {
        string signature = title + "|" + string.Join(";", scores.Select(s => $"{s.Key},{s.Name},{s.Kills},{s.Deaths},{s.Assists},{s.Place},{s.Connected},{s.Playing}"));
        if (signature == m_shown) return;
        m_shown = signature; m_back.Title = title;
        m_rows.Children.Clear();
        m_rows.Children.Add(Row("#", "玩家", "K", "D", "A", DmPx.Dim, true, false));
        foreach (var s in scores)
            m_rows.Children.Add(Row(s.Place.ToString(), s.Name + (s.Connected ? s.Playing ? "" : "（观战）" : "（离线）"), s.Kills.ToString(), s.Deaths.ToString(), s.Assists.ToString(), s.Key == selfKey ? DmPx.Gold : s.Connected ? DmPx.Text : DmPx.Dim, false, s.Key == selfKey));
    }
}

/// <summary>The host's map-editing panel (round 2, R2-4): while the map is built and while the lobby waits, what the arena
/// has and what is still missing, always on screen; on touch, the editing actions as buttons (a phone has no F6 and
/// should not have to reopen the menu for every corner and point).</summary>
sealed class DmEditElement : DmElement {
    readonly DmPixelPanel m_back = new() { Title = "地图编辑", Fill = DmPx.PanelSoft, TitleHeight = 28 };
    readonly LabelWidget m_lines = Text(.56f); readonly DmPixelButton[] m_buttons;
    readonly ScWeaponButtonInput[] m_inputs;
    static readonly string[] s_labels = ["角点1", "角点2", "+复活点", "准备点", "开始"];
    public DmEditElement() : base(DmHudIds.Edit) {
        Children.Add(m_back); m_lines.Margin = new Vector2(10, 34); m_lines.VerticalAlignment = WidgetAlignment.Near; m_lines.WordWrap = true; m_lines.Size = new Vector2(BaseSize.X - 20, -1); Children.Add(m_lines);
        // (Margin = Vector2 sets left AND right, top AND bottom: the fourth and fifth button were squeezed to no width -
        // invisible and not pressable on the phone, round 3. Only the left and bottom margins are set.)
        m_buttons = s_labels.Select((t, i) => new DmPixelButton(t, (BaseSize.X - 12 - 4 * 4) / 5, 40) { HorizontalAlignment = WidgetAlignment.Near, VerticalAlignment = WidgetAlignment.Far, MarginLeft = 6 + i * ((BaseSize.X - 12 - 4 * 4) / 5 + 4), MarginBottom = 6 }).ToArray();
        foreach (var b in m_buttons) { b.FontScale = .56f; Children.Add(b); }
        m_inputs = m_buttons.Select(_ => new ScWeaponButtonInput()).ToArray();
    }
    /// <summary>Returns the action pressed this frame (0-4), or -1.</summary>
    public int Show(SubsystemScDeathmatch dm, bool touch) {
        var a = dm.Arena; var issues = dm.ArenaIssues();
        string region = a.HasRegion ? $"区域 {a.MaxX - a.MinX + 1}×{a.MaxZ - a.MinZ + 1}" : dm.Corner(0) is not null || dm.Corner(1) is not null ? "区域：还差一个角点" : "区域：未设置";
        string next = DmMenuPanel.NextStep(dm, issues);
        // the menu key as this device has it set (2026-10-06, the user: "我之前改了打开菜单为U键，但是那里还是显示F6打开菜单")
        string menuKey = DmUiSettings.KeyOf(DmUiSettings.KeyMenu);
        m_lines.Text = $"{region} · 复活点 {a.Spawns.Count} · 准备点 {(a.HasLobby ? "已设" : "未设")} · 已准备 {dm.EnteredCount}\n" + (next is not null ? "下一步：" + next : "可以开始了")
            + (touch ? "" : string.IsNullOrEmpty(menuKey) ? "\n竞技菜单键未设置" : $"\n{menuKey} 打开菜单操作");
        int pressed = -1;
        for (int i = 0; i < m_buttons.Length; i++) {
            m_buttons[i].IsVisible = touch;
            m_inputs[i].Sample(m_buttons[i], touch, IsVisible && touch);
            if (touch && IsVisible && m_inputs[i].Clicked) pressed = i;
        }
        m_buttons[0].Selected = dm.Corner(0) is not null; m_buttons[1].Selected = dm.Corner(1) is not null;
        return pressed;
    }
}

/// <summary>Names shown to players (never an asset name).</summary>
public static class DmNames {
    public static string Gun(int variant) => variant >= 0 && variant < GunSpec.All.Length ? Weapon(GunSpec.All[variant].Name) : "";
    public static string Weapon(string asset) {
        if (string.IsNullOrEmpty(asset)) return "";
        if (asset.StartsWith("knife", StringComparison.Ordinal)) return "刀";
        int grenade = Array.IndexOf(ScGrenadeBlock.Assets, asset);
        if (grenade >= 0) return ScGrenadeBlock.Names[grenade];
        return asset switch {
            "ak47" => "AK-47", "m4a4" => "M4A4", "m4a1s" => "M4A1-S", "awp" => "AWP", "deagle" => "沙漠之鹰", "glock18" => "格洛克 18", "hkp2000" => "P2000", "usp_silencer" => "USP-S", "p250" => "P250",
            "fiveseven" => "FN57", "tec9" => "Tec-9", "cz75a" => "CZ75", "elite" => "双持贝瑞塔", "revolver" => "R8 左轮", "mac10" => "MAC-10", "mp9" => "MP9", "mp7" => "MP7", "mp5sd" => "MP5-SD", "ump45" => "UMP-45",
            "p90" => "P90", "bizon" => "PP-野牛", "galilar" => "加利尔 AR", "famas" => "法玛斯", "aug" => "AUG", "sg556" => "SG 553", "ssg08" => "SSG 08", "scar20" => "SCAR-20", "g3sg1" => "G3SG1",
            "nova" => "新星", "xm1014" => "XM1014", "sawedoff" => "截短霰弹枪", "mag7" => "MAG-7", "m249" => "M249", "negev" => "内格夫", "taser" => "宙斯 x27", _ => asset };
    }
    /// <summary>A knife's own name (the item's display name), not "knife N".</summary>
    public static string Knife(int variant) {
        try { return BlocksManager.Blocks[BlocksManager.GetBlockIndex<ScKnifeBlock>(true)].GetDisplayName(null, Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScKnifeBlock>(true), 0, variant)); }
        catch (Exception) { return $"刀 {variant + 1}"; }
    }
}

/// <summary>The deathmatch HUD of one local player: built in that player's own GUI when the world is an arena world, gone
/// when it is not. Everything it shows is the view's (the server's word); what it sends is a wish. While a panel or a
/// dialog is open the HUD steps aside (round 2: the timer was drawn over the buy wheel).</summary>
sealed class DmHudRoot : IDisposable {
    readonly CanvasWidget m_host = new() { IsHitTestVisible = false, HorizontalAlignment = WidgetAlignment.Stretch, VerticalAlignment = WidgetAlignment.Stretch, Size = new Vector2(float.PositiveInfinity) };
    readonly DmButtonElement m_buy = new(DmHudIds.Buy, "配装"), m_board = new(DmHudIds.Board, "榜单"), m_menu = new(DmHudIds.Menu, "竞技");
    readonly DmTimerElement m_timer = new(); readonly DmProtectElement m_protect = new();
    readonly DmFeedElement m_feed = new(); readonly DmDeathElement m_death = new(); readonly DmScoresElement m_scores = new(); readonly DmEditElement m_edit = new();
    bool m_boardOn; double m_noticeShown = double.NegativeInfinity; double m_answerShown = double.NegativeInfinity;
    public DmHudRoot() { foreach (var e in Elements) m_host.Children.Add(e); }
    IEnumerable<DmElement> Elements => [m_feed, m_timer, m_protect, m_death, m_edit, m_scores, m_buy, m_board, m_menu];
    /// <summary>Puts the HUD into the player's GUI below the game's controls container (round 3, the user's phone: above
    /// it, the HUD covered the CS weapon buttons and the game's own): the game's buttons, the CS weapon buttons and the
    /// panels stay on top and keep their touches. The controls container is hidden under the death camera; the HUD,
    /// beside it, is not.</summary>
    public void Attach(ContainerWidget root, Widget controls) {
        bool below = controls is not null && ReferenceEquals(controls.ParentWidget, root);
        if (ReferenceEquals(m_host.ParentWidget, root) && (!below || root.Children.IndexOf(m_host) < root.Children.IndexOf(controls))) return;
        m_host.ParentWidget?.Children.Remove(m_host);
        if (below) root.Children.InsertBefore(controls, m_host); else root.Children.Add(m_host);
    }
    public void Dispose() { m_buy.Release(); m_board.Release(); m_menu.Release(); m_host.ParentWidget?.Children.Remove(m_host); }

    public void Step(ComponentPlayer player, SubsystemScDeathmatch dm) {
        var gui = player.ComponentGui; var view = dm.View; var self = view.Of(player); double now = dm.Now;
        bool touch = player.ComponentInput.IsControlledByTouch;
        Vector2 area = m_host.ActualSize;
        bool modal = gui.ModalPanelWidget is not null || DialogsManager.HasDialogs(player.GuiWidget);
        bool controls = gui.ControlsContainerWidget?.IsVisible != false;   // the game hides its own HUD under cameras that do not control the player
        bool game = ScreensManager.CurrentScreen is null || ReferenceEquals(ScreensManager.CurrentScreen, ScreensManager.FindScreen<Screen>("Game"));
        bool governing = view.Governing, shown = !modal && controls; DmElementLayout L(string id) => DmUiSettings.Layout(id, touch, area);

        // ---- buttons and keys: the same things either way
        m_buy.Place(L(DmHudIds.Buy), area, governing && shown); m_board.Place(L(DmHudIds.Board), area, governing && shown); m_menu.Place(L(DmHudIds.Menu), area, view.Enabled && shown);
        m_buy.Sample(touch); m_board.Sample(touch); m_menu.Sample(touch);
        var input = player.GameWidget.Input; bool keys = game && !modal && Window.IsActive && !ScreensManager.IsAnimating;
        bool Key(string id, bool once) => keys && Enum.TryParse<Key>(DmUiSettings.KeyOf(id), out var key) && key != Engine.Input.Key.Null && (once ? input.IsKeyDownOnce(key) : input.IsKeyDown(key));
        if (governing && (m_buy.Clicked || Key(DmUiSettings.KeyBuy, true))) { DmWheel.Open(player, dm); return; }
        if (m_menu.Clicked || Key(DmUiSettings.KeyMenu, true) || DmHud.TakeMenuRequest(player)) { DmMenu.Open(player, dm); return; }
        if (m_board.Clicked) m_boardOn = !m_boardOn;
        bool boardKey = DmUiSettings.BoardHold ? Key(DmUiSettings.KeyBoard, false) : false;
        if (!DmUiSettings.BoardHold && Key(DmUiSettings.KeyBoard, true)) m_boardOn = !m_boardOn;
        if (!governing) m_boardOn = false;

        // ---- the host's map editing
        m_edit.Place(L(DmHudIds.Edit), area, dm.ShowsEditing && shown && ScNet.IsLocal(player));
        if (m_edit.IsVisible) {
            int action = m_edit.Show(dm, touch);
            string Do(int a) {
                var cell = SubsystemScDeathmatch.Cell(player.ComponentBody.Position); var (p, yaw) = SubsystemScDeathmatch.Stand(player);
                return a switch { 0 => dm.SetCorner(0, cell), 1 => dm.SetCorner(1, cell), 2 => dm.AddSpawn(p, yaw), 3 => dm.SetLobby(p, yaw), _ => dm.StartMatch() };
            }
            if (action >= 0) {
                string refused = Do(action), done = action switch {
                    0 or 1 when dm.Corner(1 - action) is null => $"角点 {action + 1} 已定：走到对角，按“角点{2 - action}”",
                    0 or 1 => $"竞技区域已设置：{dm.Arena.MaxX - dm.Arena.MinX + 1}×{dm.Arena.MaxZ - dm.Arena.MinZ + 1} 格",
                    2 => $"已添加复活点 #{dm.Arena.NextSpawnId - 1}：走到下一个位置再加",
                    3 => "准备/观战点已设在这里",
                    _ => "比赛即将开始！" };
                gui.DisplaySmallMessage(refused ?? done, refused is null ? Color.White : new Color(255, 200, 90), false, false);
            }
        }

        // ---- what the server says
        bool running = view.Phase == DmPhase.Running;
        m_timer.Place(L(DmHudIds.Timer), area, governing && shown);
        // in the lobby the timer's line says what this player still has to do (MP r2: a joining player saw only "LOBBY")
        string lobby = self.Entered ? "已准备 · 等待房主开始" : touch ? "点“配装”选装备后点“准备”" : $"按 {DmUiSettings.KeyOf(DmUiSettings.KeyBuy)} 配装后点“准备”";
        if (m_timer.IsVisible) m_timer.Show(view.Phase, view.PhaseEndsAt - now, view.Rows.Count(r => r.Playing && r.Connected) < 2, lobby);
        // the health on the game's own bar (round 3, the user: "血量显示用原版的就好了"; shown in a creative world too while
        // this player fights) and the armour on the CS armour HUD (DmMode.ShownArmour): the deathmatch draws neither
        m_health = governing && self.Fighting ? Math.Clamp(self.Health, 0, 100) / 100f : null;
        var bar = gui.HealthBarWidget;
        if (bar is not null && m_health is { } h) { bar.IsVisible = true; bar.Value = h; m_barShown = true; }
        else if (bar is not null && m_barShown) {   // back to the game's own rule (ComponentGui.Load: hidden in a creative world)
            bar.IsVisible = player.Project.FindSubsystem<SubsystemGameInfo>(true).WorldSettings.GameMode != GameMode.Creative; m_barShown = false;
        }
        m_protect.Place(L(DmHudIds.Protect), area, governing && self.Phase == DmPlayerPhase.SpawnProtected && self.ProtectedUntil > now && shown);
        if (m_protect.IsVisible) m_protect.Show(self.ProtectedUntil - now);
        string selfKey = view.RowOf(player.PlayerData.PlayerIndex)?.Key;
        m_feed.Place(L(DmHudIds.Feed), area, governing && !modal && view.Feed.Count > 0 && now - view.Feed[^1].At <= DmView.FeedSeconds);
        if (m_feed.IsVisible) m_feed.Show(view.Feed, now, selfKey);
        bool dead = governing && running && self.Entered && self.Phase is DmPlayerPhase.DeathView or DmPlayerPhase.SpawnPending && self.OwnDeath is not null;
        m_death.Place(L(DmHudIds.Death), area, dead && !modal);
        // (the game hides its controls and panels while the player is dead: the wheel opens again after the respawn)
        if (m_death.IsVisible) m_death.Show(self.OwnDeath, self.Phase, self.Entered, running, touch ? "点“配装”" : $"按 {DmUiSettings.KeyOf(DmUiSettings.KeyBuy)} ");
        bool results = view.Phase == DmPhase.Results && view.Result is not null;
        m_scores.Place(L(DmHudIds.Scores), area, governing && !modal && (m_boardOn || boardKey || results));
        if (m_scores.IsVisible) {
            if (results) m_scores.Show(ResultTitle(view.Result), view.Result.Scores, selfKey);
            else m_scores.Show(running ? "计分板 · 剩余 " + DmTimerElement.Clock(view.PhaseEndsAt - now) : "计分板", DmScores.Rank(view.Rows.Select(r => new DmScore(r.Key, r.Name, r.Kills, r.Deaths, r.Assists, r.Connected, r.Playing))), selfKey);
        }

        // (the weapons: the game's hotbar and the CS core's own gun HUD - round 3, the user: "用核心CS的枪械HUD就可以了，不用新加")

        // ---- words for this player
        if (self.Notice is { } notice && notice.At > m_noticeShown) { m_noticeShown = notice.At; gui.DisplaySmallMessage(notice.Text, Color.White, false, false); }
        if (self.Answer is { } answer && answer.At > m_answerShown) {
            m_answerShown = answer.At;
            gui.DisplaySmallMessage(answer.Outcome switch { DmLoadoutOutcome.Now => "已立即装备", DmLoadoutOutcome.NextLife => "下次复活时装备", DmLoadoutOutcome.Saved => "配装已保存", _ => "配装未被接受：" + DmCatalogue.Describe(answer.Error) }, Color.White, false, false);
        }
    }
    float? m_health; bool m_barShown;
    /// <summary>At drawing time - after every update of the frame, the game's own health component included, which writes
    /// the bar too - the bar shows the deathmatch health (display only: the engine's health is not touched).</summary>
    public void DrawHealth(ComponentGui gui) { if (m_health is { } h && gui.HealthBarWidget is { } bar) bar.Value = h; }
    static string ResultTitle(DmResult result) => (result.Formal ? "比赛结果" : "练习结果（少于两名参赛者，不计正式成绩）") + result.Reason switch { "stopped" => " · 房主结束", "empty" => " · 参赛者全部离开", "interrupted" => " · 上次中断", _ => "" };
}

/// <summary>The HUD's entry points from the mod loader.</summary>
public static class DmHud {
    static readonly ConditionalWeakTable<ComponentGui, DmHudRoot> s_roots = new();
    static bool s_menuRequested;
    /// <summary>The game's settings page asked for the deathmatch menu: it opens for the first local player once the game
    /// screen is back (the route that needs no key and no HUD button, on every platform).</summary>
    public static void RequestMenu() => s_menuRequested = true;
    internal static bool TakeMenuRequest(ComponentPlayer player) { if (!s_menuRequested || !ScNet.IsLocal(player)) return false; s_menuRequested = false; return true; }
    public static void Update(ComponentGui gui) {
        if (gui?.m_componentPlayer is not { } player || !ScNet.IsLocal(player) || player.Project?.FindSubsystem<SubsystemScDeathmatch>(false) is not { } dm || player.GuiWidget is null || player.GameWidget is null) return;
        if (!s_roots.TryGetValue(gui, out var root)) s_roots.Add(gui, root = new DmHudRoot());
        try { root.Attach(player.GuiWidget, gui.ControlsContainerWidget); root.Step(player, dm); }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("dm-hud", "[CS_DM] HUD update failed: " + e); }
    }
    public static void Draw(ComponentGui gui, Camera camera, int drawOrder) { if (gui is not null && s_roots.TryGetValue(gui, out var root)) root.DrawHealth(gui); }
}
