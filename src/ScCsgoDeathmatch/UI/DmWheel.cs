using Engine;
using Engine.Graphics;
using Engine.Input;
namespace Game;

/// <summary>The radial part of the buy menu: up to eight sectors around a centre. A sector is chosen by a click or a tap
/// that begins and ends in it (a finger that slides off, a cancelled touch or the system's back gesture chooses nothing),
/// or by its number key. It draws and reports; what a choice means is the panel's.</summary>
public sealed class DmWheelWidget : CanvasWidget {
    public const float Radius = 200, Inner = 64; public const int MaxSectors = 8, Centre = -2, None = -1;
    public readonly List<(string Label, int Icon, bool Marked)> Sectors = [];
    /// <summary>The pixel silhouette of each sector (null: the label alone).</summary>
    public readonly List<Texture2D> Sprites = [];
    public string CentreText = "关闭";
    /// <summary>What was chosen this frame: a sector index, <see cref="Centre"/>, or <see cref="None"/>.</summary>
    public int Picked { get; private set; } = None;
    int m_hover = None;
    readonly LabelWidget[] m_labels = new LabelWidget[MaxSectors]; readonly DmPixelIcon[] m_icons = new DmPixelIcon[MaxSectors];
    readonly LabelWidget m_centre = new() { FontScale = .8f, DropShadow = true, IsHitTestVisible = false, HorizontalAlignment = WidgetAlignment.Center, VerticalAlignment = WidgetAlignment.Center, TextAnchor = TextAnchor.HorizontalCenter };
    static Vector2 Middle => new(Radius + 10);
    public DmWheelWidget() {
        Size = new Vector2(2 * Radius + 20); IsHitTestVisible = true;
        for (int i = 0; i < MaxSectors; i++) {
            m_icons[i] = new DmPixelIcon { Scale = 2, HorizontalAlignment = WidgetAlignment.Near, VerticalAlignment = WidgetAlignment.Near, IsVisible = false };
            m_labels[i] = new LabelWidget { FontScale = .62f, DropShadow = true, IsHitTestVisible = false, HorizontalAlignment = WidgetAlignment.Near, VerticalAlignment = WidgetAlignment.Near, TextAnchor = TextAnchor.HorizontalCenter, Size = new Vector2(120, 22), IsVisible = false };
            Children.Add(m_icons[i]); Children.Add(m_labels[i]);
        }
        Children.Add(m_centre);
    }
    /// <summary>The sector a point (relative to the wheel's middle) lies in: sector 0 is centred at the top, the rest follow
    /// clockwise. The centre disc is <see cref="Centre"/>, outside the ring is <see cref="None"/>.</summary>
    public static int SectorAt(Vector2 fromMiddle, int count) {
        float r = fromMiddle.Length();
        if (r < Inner) return Centre;
        if (r > Radius || count <= 0) return None;
        float step = MathF.PI * 2 / count, angle = MathF.Atan2(fromMiddle.X, -fromMiddle.Y);
        if (angle < 0) angle += MathF.PI * 2;
        return (int)MathF.Floor((angle + step / 2) / step) % count;
    }
    /// <summary>Where a sector's content is centred, relative to the wheel's middle.</summary>
    public static Vector2 SectorCentre(int index, int count) {
        float angle = MathF.PI * 2 * index / Math.Max(1, count), r = (Inner + Radius) / 2;
        return new Vector2(MathF.Sin(angle), -MathF.Cos(angle)) * r;
    }
    public override void Update() {
        Picked = None;
        int count = Sectors.Count;
        m_hover = Input.MousePosition is { } mouse ? SectorAt(ScreenToWidget(mouse) - Middle, count) : None;
        if (Input.Click is { } click) {
            int from = SectorAt(ScreenToWidget(click.Start) - Middle, count), to = SectorAt(ScreenToWidget(click.End) - Middle, count);
            if (from == to && from != None && ReferenceEquals(HitTestGlobal(click.Start), this) && ReferenceEquals(HitTestGlobal(click.End), this)) Picked = from;
        }
        for (int i = 0; i < Math.Min(count, MaxSectors); i++) if (Input.IsKeyDownOnce(Key.Number1 + i)) Picked = i;
        for (int i = 0; i < MaxSectors; i++) {
            var sprite = i < Sprites.Count ? Sprites[i] : null;
            bool on = i < count; m_labels[i].IsVisible = on; m_icons[i].IsVisible = on && sprite is not null;
            if (!on) continue;
            Vector2 at = Middle + SectorCentre(i, count);
            m_labels[i].Text = $"{i + 1} {Sectors[i].Label}"; m_labels[i].Color = i == m_hover ? new Color(16, 16, 16) : Sectors[i].Marked ? DmPx.Gold : DmPx.Text;
            m_labels[i].MarginLeft = at.X - 60; m_labels[i].MarginTop = at.Y + (sprite is not null ? 12 : -11);
            if (sprite is not null) {
                float scale = sprite.Width > 46 ? 1 : 2; m_icons[i].Texture = sprite; m_icons[i].Scale = scale; m_icons[i].Color = i == m_hover ? new Color(16, 16, 16) : Sectors[i].Marked ? DmPx.Gold : DmPx.Text;
                m_icons[i].MarginLeft = MathF.Round(at.X - sprite.Width * scale / 2); m_icons[i].MarginTop = MathF.Round(at.Y - sprite.Height * scale - 2);
            }
        }
        m_centre.Text = CentreText;
    }
    public override void MeasureOverride(Vector2 parentAvailableSize) { base.MeasureOverride(parentAvailableSize); IsDrawRequired = true; }
    public override void Draw(DrawContext dc) {
        var batch = dc.PrimitivesRenderer2D.FlatBatch(0, DepthStencilState.None, null, BlendState.AlphaBlend);
        int first = batch.TriangleVertices.Count, count = Sectors.Count; Vector2 m = Middle;
        const int Arc = 48;
        for (int s = 0; s < Arc; s++) {                                 // the centre disc
            float a0 = MathF.PI * 2 * s / Arc, a1 = MathF.PI * 2 * (s + 1) / Arc;
            batch.QueueTriangle(m, m + new Vector2(MathF.Sin(a0), -MathF.Cos(a0)) * (Inner - 4), m + new Vector2(MathF.Sin(a1), -MathF.Cos(a1)) * (Inner - 4), 0, (m_hover == Centre ? new Color(64, 70, 78, 240) : new Color(18, 20, 24, 236)) * GlobalColorTransform);
        }
        for (int i = 0; i < count; i++) {
            float step = MathF.PI * 2 / count, from = step * i - step / 2 + .012f, to = step * i + step / 2 - .012f;
            Color color = (i == m_hover ? DmPx.Gold : Sectors[i].Marked ? new Color(70, 54, 18, 230) : new Color(20, 22, 26, 228)) * GlobalColorTransform;
            int pieces = Math.Max(2, Arc / count);
            for (int s = 0; s < pieces; s++) {
                float a0 = from + (to - from) * s / pieces, a1 = from + (to - from) * (s + 1) / pieces;
                Vector2 d0 = new(MathF.Sin(a0), -MathF.Cos(a0)), d1 = new(MathF.Sin(a1), -MathF.Cos(a1));
                batch.QueueQuad(m + d0 * Inner, m + d0 * Radius, m + d1 * Radius, m + d1 * Inner, 0, color);
            }
        }
        batch.TransformTriangles(GlobalTransform, first);
    }
}

/// <summary>The buy menu (DM-02, DM-03): a Counter-Strike style wheel - six categories, then the models of one, then how
/// the chosen model looks - beside the loadout being put together. Everything is free and every look this build has is
/// open; a look changes nothing but the look. Nothing here gives the player anything: confirming sends the whole
/// loadout as a wish, and the server says whether it is worn now, at the next life, or kept for the first one.</summary>
public sealed class DmWheelPanel : CanvasWidget {
    readonly ComponentPlayer m_player; readonly SubsystemScDeathmatch m_dm;
    readonly DmWheelWidget m_wheel = new() { HorizontalAlignment = WidgetAlignment.Near, VerticalAlignment = WidgetAlignment.Near, Margin = new Vector2(14, 40) };
    readonly LabelWidget m_title = new() { FontScale = .8f, Color = DmPx.Gold, DropShadow = false }, m_heading = new() { FontScale = .74f, Color = DmPx.Text, DropShadow = false }, m_summary = new() { FontScale = .62f, Color = DmPx.Text, DropShadow = false }, m_hint = new() { FontScale = .56f, Color = DmPx.Dim, DropShadow = false, WordWrap = true, Size = new Vector2(380, -1) };
    readonly DmPixelButton m_previous = new("上一页", 96), m_next = new("下一页", 96), m_confirm = new("确认配装", 200), m_enter = new("入场", 110), m_clear = new("清空", 80), m_close = new("关闭", 80);
    readonly DmPixelButton m_drawerPrevious = new("<", 48), m_drawerNext = new(">", 48);
    readonly DmPixelButton m_counter = new("计数器：关", 230);
    sealed class Option { public DmPixelButton Button; public BlockIconWidget Icon; public LabelWidget Name; public Action Choose; }
    readonly Option[] m_options = new Option[6];
    readonly List<(string Name, int Icon, bool Marked, Action Choose)> m_drawer = [];
    DmLoadout m_working; int m_group = -1, m_page, m_drawerPage, m_gun = -1, m_knife = -1;
    public const int PerPage = DmWheelWidget.MaxSectors;
    static int GunIcon(int variant) => Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScGunBlock>(true), 0, GunSpec.WithId(variant, GunSpec.FreshFull));
    static int SkinIcon(int variant, int paint) => paint == ScGunSkinCatalog.None ? GunIcon(variant) : Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScGunSkinTemplateBlock>(true), 0, paint);
    static int KnifeIcon(int variant, int finish) => Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScKnifeBlock>(true), 0, ScKnifeSkinCatalog.With(variant, finish));

    public DmWheelPanel(ComponentPlayer player, SubsystemScDeathmatch dm) {
        m_player = player; m_dm = dm; m_working = dm.View.Of(player).Desired;
        Size = new Vector2(850, 500);
        Children.Add(new DmPixelPanel());
        void Put(Widget w, float x, float y) { w.HorizontalAlignment = WidgetAlignment.Near; w.VerticalAlignment = WidgetAlignment.Near; w.MarginLeft = x; w.MarginTop = y; Children.Add(w); }
        Put(m_title, 18, 8); Children.Add(m_wheel);
        Put(m_previous, 14, 446); Put(m_next, 338, 446);
        Put(m_heading, 452, 14);
        for (int i = 0; i < m_options.Length; i++) {
            var option = new Option { Button = new DmPixelButton("", 122, 92) };
            option.Icon = new BlockIconWidget { Size = new Vector2(96, 54), IsHitTestVisible = false, HorizontalAlignment = WidgetAlignment.Center, VerticalAlignment = WidgetAlignment.Near, Margin = new Vector2(0, 6) };
            option.Name = new LabelWidget { FontScale = .5f, DropShadow = true, IsHitTestVisible = false, HorizontalAlignment = WidgetAlignment.Center, VerticalAlignment = WidgetAlignment.Far, Margin = new Vector2(0, 6), TextAnchor = TextAnchor.HorizontalCenter };
            option.Button.Children.Add(option.Icon); option.Button.Children.Add(option.Name);
            m_options[i] = option; Put(option.Button, 452 + i % 3 * 128, 46 + i / 3 * 98);
        }
        Put(m_drawerPrevious, 452, 244); Put(m_drawerNext, 788, 244); Put(m_counter, 512, 244);
        Put(m_summary, 452, 300); Put(m_hint, 452, 410);
        Put(m_confirm, 452, 440); Put(m_enter, 658, 440); Put(m_clear, 14 + 110, 446); Put(m_close, 772, 440);
        m_clear.MarginLeft = 228;
        Top();
    }
    DmView.Self Self => m_dm.View.Of(m_player);
    DmRules Rules => m_dm.View.Rules;

    void Top() {
        m_group = -1; m_page = 0; m_gun = m_knife = -1; m_drawer.Clear(); m_drawerPage = 0;
        m_wheel.Sectors.Clear(); m_wheel.Sprites.Clear();
        foreach (var (group, name) in DmCatalogue.Groups) {
            m_wheel.Sectors.Add((name, 0, false));
            m_wheel.Sprites.Add(group switch { DmCatalogue.Group.Pistols => DmPx.Sprite("glock"), DmCatalogue.Group.Smgs => DmPx.Sprite("mp9"), DmCatalogue.Group.Rifles => DmPx.Sprite("ak47"),
                DmCatalogue.Group.Heavy => DmPx.Sprite("nova"), DmCatalogue.Group.Knives => DmPx.Sprite("knife_karambit") ?? DmPx.Sprite("knife"), _ => DmPx.Sprite("taser") });
        }
        m_wheel.CentreText = "关闭";
    }
    /// <summary>The entries of a category, in catalogue order: (name, icon, marked as in the loadout, what choosing it does).</summary>
    List<(string Name, int Icon, bool Marked, Action Choose)> Entries(DmCatalogue.Group group) {
        var list = new List<(string, int, bool, Action)>();
        if (group == DmCatalogue.Group.Knives) {
            for (int v = 0; v < DmCatalogue.KnifeCount; v++) { int variant = v; list.Add((DmNames.Knife(v), KnifeIcon(v, ScKnifeSkinCatalog.None), m_working.Knife?.Variant == v, () => OpenKnife(variant))); }
            return list;
        }
        if (group == DmCatalogue.Group.Gear) {
            foreach (int v in DmCatalogue.GunsOf(group)) { int variant = v; list.Add((DmNames.Gun(v), GunIcon(v), m_working.Zeus?.Variant == v, () => m_working = m_working with { Zeus = m_working.Zeus is null ? new DmGunChoice(variant) : null })); }
            if (Rules.Grenades) for (int kind = 0; kind < DmCatalogue.GrenadeKinds; kind++) {
                if (!ScGrenadeBlock.Enabled(kind)) continue;
                int k = kind, have = m_working.Grenades.Count(g => g == k);
                list.Add((ScGrenadeBlock.Names[k] + (have > 0 ? $" ×{have}" : ""), ScGrenadeBlock.Value(k), have > 0, () => ToggleGrenade(k)));
            }
            return list;
        }
        foreach (int v in DmCatalogue.GunsOf(group)) { int variant = v; list.Add((DmNames.Gun(v), GunIcon(v), m_working.Primary?.Variant == v || m_working.Secondary?.Variant == v, () => OpenGun(variant))); }
        return list;
    }
    /// <summary>One more of a throwable while the per-life limits allow it; at the limit, none of that kind.</summary>
    void ToggleGrenade(int kind) {
        var more = m_working with { Grenades = [.. m_working.Grenades, kind] };
        m_working = DmCatalogue.Validate(more, Rules) == DmLoadoutError.None ? more : m_working with { Grenades = m_working.Grenades.Where(g => g != kind).ToArray() };
    }
    void OpenGun(int variant) {
        m_gun = variant; m_knife = -1; m_drawerPage = 0; m_drawer.Clear();
        bool secondary = DmCatalogue.IsSecondary(variant); var present = secondary ? m_working.Secondary : m_working.Primary;
        m_counter.Selected = present?.Variant == variant && present.Counter;
        void Wear(int paint) { var choice = new DmGunChoice(variant, paint, m_counter.Selected); m_working = secondary ? m_working with { Secondary = choice } : m_working with { Primary = choice }; }
        m_drawer.Add(("原厂外观", GunIcon(variant), present?.Variant == variant && present.SkinId == ScGunSkinCatalog.None, () => Wear(ScGunSkinCatalog.None)));
        foreach (var skin in ScGunSkinCatalog.Available.Where(s => ScGunSkinCatalog.Fits(s, variant))) { int paint = skin.PaintId; m_drawer.Add((skin.Name, SkinIcon(variant, paint), present?.Variant == variant && present.SkinId == paint, () => Wear(paint))); }
        m_drawer.Add(("不携带" + (secondary ? "副武器" : "主武器"), 0, false, () => m_working = secondary ? m_working with { Secondary = null } : m_working with { Primary = null }));
    }
    void OpenKnife(int variant) {
        m_knife = variant; m_gun = -1; m_drawerPage = 0; m_drawer.Clear();
        foreach (int finish in DmCatalogue.KnifeFinishes(variant)) { int f = finish; m_drawer.Add((ScKnifeSkinCatalog.Name(f, variant), KnifeIcon(variant, f), m_working.Knife is { } k && k.Variant == variant && k.Finish == f, () => m_working = m_working with { Knife = new DmKnifeChoice(variant, f) })); }
        m_drawer.Add(("不携带刀", 0, false, () => m_working = m_working with { Knife = null }));
    }
    void Refresh() {
        if (m_group >= 0) {
            var entries = Entries(DmCatalogue.Groups[m_group].Group); int pages = Math.Max(1, (entries.Count + PerPage - 1) / PerPage); m_page = Math.Clamp(m_page, 0, pages - 1);
            m_wheel.Sectors.Clear(); m_wheel.Sprites.Clear();
            foreach (var e in entries.Skip(m_page * PerPage).Take(PerPage)) { m_wheel.Sectors.Add((e.Name, e.Icon, e.Marked)); m_wheel.Sprites.Add(e.Icon == 0 ? null : DmPx.ItemSprite(e.Icon)); }
            m_wheel.CentreText = "返回";
            m_previous.IsVisible = m_next.IsVisible = pages > 1; m_previous.IsEnabled = m_page > 0; m_next.IsEnabled = m_page < pages - 1;
            m_title.Text = $"{DmCatalogue.Groups[m_group].Name}{(pages > 1 ? $"（{m_page + 1}/{pages}）" : "")} · 全部免费";
        }
        else { m_previous.IsVisible = m_next.IsVisible = false; m_title.Text = "配装轮盘 · 全部免费"; }
        if (m_gun >= 0) OpenGunKeep(); else if (m_knife >= 0) OpenKnifeKeep();
        int drawerPages = Math.Max(1, (m_drawer.Count + m_options.Length - 1) / m_options.Length); m_drawerPage = Math.Clamp(m_drawerPage, 0, drawerPages - 1);
        for (int i = 0; i < m_options.Length; i++) {
            int index = m_drawerPage * m_options.Length + i; bool on = index < m_drawer.Count; var option = m_options[i]; option.Button.IsVisible = on;
            if (!on) { option.Choose = null; continue; }
            var entry = m_drawer[index]; option.Choose = entry.Choose; option.Name.Text = entry.Name; option.Name.Color = entry.Marked ? DmPx.Gold : DmPx.Text; option.Button.Selected = entry.Marked;
            option.Icon.IsVisible = entry.Icon != 0; if (entry.Icon != 0 && option.Icon.Value != entry.Icon) option.Icon.Value = entry.Icon;
        }
        m_drawerPrevious.IsVisible = m_drawerNext.IsVisible = drawerPages > 1; m_drawerPrevious.IsEnabled = m_drawerPage > 0; m_drawerNext.IsEnabled = m_drawerPage < drawerPages - 1;
        m_counter.IsVisible = m_gun >= 0;
        m_heading.Text = m_gun >= 0 ? DmNames.Gun(m_gun) + " · 选择外观" : m_knife >= 0 ? DmNames.Knife(m_knife) + " · 选择涂装" : m_group >= 0 ? "选择型号" : "选择类别";
        m_summary.Text = Summary(m_working, Rules);
        var self = Self; var error = DmCatalogue.Validate(m_working, Rules);
        bool protectedNow = self.Phase == DmPlayerPhase.SpawnProtected && self.ProtectedUntil > m_dm.Now;
        m_confirm.Text = protectedNow ? "确认 · 立即装备" : self.Phase == DmPlayerPhase.Alive ? "确认 · 下次复活装备" : "确认 · 保存配装";
        m_confirm.IsEnabled = error == DmLoadoutError.None;
        m_enter.IsVisible = !self.Entered; m_enter.Text = m_working.IsEmpty ? "空手入场" : "入场";
        m_hint.Text = error != DmLoadoutError.None ? DmCatalogue.Describe(error) : self.Entered ? "外观不影响强度；每条命满弹，备弹无限，仍需换弹。" : "首次入场不会自动发放任何装备：选好后确认，再入场。";
    }
    void OpenGunKeep() { int page = m_drawerPage; bool counter = m_counter.Selected; OpenGun(m_gun); m_counter.Selected = counter; m_drawerPage = page; }
    void OpenKnifeKeep() { int page = m_drawerPage; OpenKnife(m_knife); m_drawerPage = page; }
    /// <summary>The loadout in words (also what the confirmation shows).</summary>
    public static string Summary(DmLoadout loadout, DmRules rules) {
        string Gun(DmGunChoice g) => g is null ? "—" : DmNames.Gun(g.Variant) + (g.SkinId != ScGunSkinCatalog.None ? "｜" + ScGunSkinCatalog.NameOf(g.SkinId) : "") + (g.Counter ? "｜计数器" : "");
        string grenades = loadout.Grenades.Count == 0 ? (rules.Grenades ? "—" : "房主未开放") : string.Join("、", loadout.Grenades.GroupBy(g => g).Select(g => ScGrenadeBlock.Names[g.Key] + (g.Count() > 1 ? "×" + g.Count() : "")));
        return $"主武器：{Gun(loadout.Primary)}\n副武器：{Gun(loadout.Secondary)}\n刀：{(loadout.Knife is { } k ? DmNames.Knife(k.Variant) + "｜" + ScKnifeSkinCatalog.Name(k.Finish, k.Variant) : "—")}\n宙斯：{(loadout.Zeus is null ? "—" : "携带")}\n投掷物：{grenades}";
    }
    Matrix m_fit = Matrix.Identity;
    public override void Arrange(Vector2 position, Vector2 parentActualSize) => DmFit.Arranged(this, m_fit, () => base.Arrange(position, parentActualSize));
    public override void Update() {
        var gui = m_player.ComponentGui;
        m_fit = DmFit.Apply(this, gui, Size);
        if (Input.Back || Input.Cancel || m_close.IsClicked || !m_dm.View.Governing) { gui.ModalPanelWidget = null; return; }
        int picked = m_wheel.Picked;
        if (picked == DmWheelWidget.Centre) { if (m_group >= 0) Top(); else { gui.ModalPanelWidget = null; return; } }
        else if (picked >= 0) {
            if (m_group < 0) { if (picked < DmCatalogue.Groups.Length) { m_group = picked; m_page = 0; } }
            else { var entries = Entries(DmCatalogue.Groups[m_group].Group); int index = m_page * PerPage + picked; if (index < entries.Count) entries[index].Choose(); }
        }
        if (m_previous.IsClicked) m_page--; if (m_next.IsClicked) m_page++;
        if (m_drawerPrevious.IsClicked) m_drawerPage--; if (m_drawerNext.IsClicked) m_drawerPage++;
        foreach (var option in m_options) if (option.Button.IsVisible && option.Button.IsClicked) option.Choose?.Invoke();
        if (m_counter.IsClicked) m_counter.Selected = !m_counter.Selected;
        m_counter.Text = m_counter.Selected ? "计数器：开（本局击杀数）" : "计数器：关";
        if (m_counter.IsVisible && m_gun >= 0) {
            bool secondary = DmCatalogue.IsSecondary(m_gun); var present = secondary ? m_working.Secondary : m_working.Primary;
            if (present?.Variant == m_gun && present.Counter != m_counter.Selected) { var changed = present with { Counter = m_counter.Selected }; m_working = secondary ? m_working with { Secondary = changed } : m_working with { Primary = changed }; }
        }
        if (m_clear.IsClicked) m_working = DmLoadout.Empty;
        if (m_confirm.IsClicked && DmCatalogue.Validate(m_working, Rules) == DmLoadoutError.None) m_dm.RequestLoadout(m_player, m_working);
        if (m_enter.IsClicked && DmCatalogue.Validate(m_working, Rules) == DmLoadoutError.None) {
            m_dm.RequestLoadout(m_player, m_working); m_dm.RequestEnter(m_player, m_working.IsEmpty);   // "空手入场" is the explicit confirmation
            gui.ModalPanelWidget = null; return;
        }
        Refresh();
    }
}

public static class DmWheel {
    public static void Open(ComponentPlayer player, SubsystemScDeathmatch dm) {
        if (!dm.View.Governing) return;
        try { player.ComponentGui.ModalPanelWidget = new DmWheelPanel(player, dm); }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("dm-wheel", "[CS_DM] the buy wheel could not be opened: " + e); }
    }
}
