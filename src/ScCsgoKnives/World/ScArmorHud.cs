using System.Globalization;
using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>What the CS armour HUD shows, as CS2's own readout does (deathmatch round 4, the user: "头盔图标在护甲图标上面，
/// 数值一起显示，有头甲就显示，没头甲就不显示（护甲同理），全部仿照CS2"). CS2's hud-HA-armor panel (hudhealthammocenter.xml /
/// .css, pak01 1.41.8.8) shows one badge - the body-armour shield (hud/armor.svg), or the shield with the helmet on it
/// (hud/armor_helmet.svg) - with the armour value beside it, and nothing at all without armour (.hud-HA-armor stays at
/// opacity 0 until .HUD--has-armor). <see cref="HelmetValue"/> is the helmet's own remaining value where it has one (the
/// CS world's protection: two budgets, 100 and 150, shown together beside the badge, the helmet's above as on the badge);
/// null where it has none (CS2: one armour value; the helmet guards the head while there is armour).</summary>
public readonly record struct ScArmorReadout(bool Armour, int ArmourValue, bool Helmet, int? HelmetValue) {
    public bool Shown => Armour || Helmet;
    /// <summary>The world's CS protection: a piece shows while it still protects (a used-up piece is gone, as armour at 0
    /// is in CS2).</summary>
    public static ScArmorReadout Of(ScArmorState s) => new(s.Vest.Protects, s.Vest.Left, s.Helmet.Protects, s.Helmet.Protects ? s.Helmet.Left : null);
    /// <summary>CS2's rule: one armour value; the helmet counts while there is armour.</summary>
    public static ScArmorReadout Cs2(int armour, bool helmet) => new(armour > 0, Math.Max(0, armour), helmet && armour > 0, null);
    /// <summary>The badge texture: CS2's shield, CS2's shield with the helmet on it, or - for a helmet worn without body
    /// armour, which CS2 does not have - CS2's equipment helmet.</summary>
    public string Badge => Armour ? Helmet ? "hud_cs2_armor_helmet" : "hud_cs2_armor" : "hud_helmet";
    /// <summary>The numbers beside the badge, top to bottom.</summary>
    public string[] Values => !Shown ? [] : Armour && Helmet && HelmetValue is { } h ? [Text(h), Text(ArmourValue)] : Armour ? [Text(ArmourValue)] : [Text(HelmetValue ?? 0)];
    static string Text(int v) => v.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The badge of the armour readout, drawn in the colours of CS2's own icon (white rim, dark shield).</summary>
public sealed class ScArmorBadge : CanvasWidget {
    public string Texture;
    public ScArmorBadge() { Size = new Vector2(36, 36); IsHitTestVisible = false; }
    public override void MeasureOverride(Vector2 available) { base.MeasureOverride(available); IsDrawRequired = Texture is not null; }
    public override void Draw(DrawContext dc) {
        Texture2D texture;
        try { texture = ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/" + Texture); } catch (Exception e) { KnifeDiagnostics.WarnOnce("armor-hud-icon", "armor HUD icon unavailable: " + e.Message); return; }
        var batch = dc.PrimitivesRenderer2D.TexturedBatch(texture, false, 0, null, null, BlendState.NonPremultiplied, SamplerState.LinearClamp);
        int first = batch.TriangleVertices.Count;
        batch.QueueQuad(Vector2.Zero, ActualSize, 0, Vector2.Zero, Vector2.One, Color.White * GlobalColorTransform);
        batch.TransformTriangles(GlobalTransform, first);
    }
}

/// <summary>The local player's CS protection on screen as CS2 shows armour (ScArmorReadout): one badge and its value(s);
/// hidden without protection. Each local player has its own HUD in its own GUI. Hidden, placed, sized and turned in the
/// layout screen with its own saved position (ScUiSettings.ArmorHud; the ammo HUD keeps its own); by default at the lower
/// left, above any touch control there. Drawing reads the values only.</summary>
public sealed class ScArmorHud : IDisposable {
    public const string Name = "ScArmorHud";
    public static readonly Vector2 Size = new(112, 40);
    public readonly CanvasWidget Panel = new() { Name = Name, Size = Size, HorizontalAlignment = WidgetAlignment.Near, VerticalAlignment = WidgetAlignment.Far, IsHitTestVisible = false, IsVisible = false };
    public readonly ScArmorBadge Badge = new() { HorizontalAlignment = WidgetAlignment.Near, VerticalAlignment = WidgetAlignment.Center, MarginLeft = 2 };
    /// <summary>The value(s) beside the badge: one centred, or two (the helmet's above, the body's below).</summary>
    public readonly LabelWidget Single = Value(.78f, WidgetAlignment.Center), Upper = Value(.6f, WidgetAlignment.Near), Lower = Value(.6f, WidgetAlignment.Far);
    static LabelWidget Value(float scale, WidgetAlignment vertical) => new() { FontScale = scale, Color = Color.White, DropShadow = true, IsHitTestVisible = false,
        HorizontalAlignment = WidgetAlignment.Near, VerticalAlignment = vertical, MarginLeft = 44 };
    public ScArmorReadout Shown { get; private set; }
    ContainerWidget host;
    public ScArmorHud() { foreach (var w in new Widget[] { Badge, Single, Upper, Lower }) Panel.Children.Add(w); }
    public static ScHudPosition DefaultPosition() => new() { X = .12f, Y = .9f };
    public bool Attach(ComponentGui gui) { host = gui?.ControlsContainerWidget; if (host is null) return false; host.Children.Add(Panel); return true; }
    /// <summary>Shows a readout; one with nothing to show hides the HUD (CS2: no armour, no armour panel).</summary>
    public void Display(ScArmorReadout readout) {
        Shown = readout;
        if (!readout.Shown) { Hide(); return; }
        string[] values = readout.Values;
        Badge.Texture = readout.Badge;
        Single.IsVisible = values.Length == 1; Upper.IsVisible = Lower.IsVisible = values.Length == 2;
        Single.Text = values.Length == 1 ? values[0] : ""; Upper.Text = values.Length == 2 ? values[0] : ""; Lower.Text = values.Length == 2 ? values[1] : "";
        Panel.IsVisible = true; Position();
    }
    /// <summary>Shows the HUD in any container (offline renders use a plain canvas instead of a player's GUI).</summary>
    public void ShowIn(ContainerWidget container, ScArmorReadout readout) {
        if (!ReferenceEquals(host, container)) { Panel.ParentWidget?.Children.Remove(Panel); host = container; container.Children.Add(Panel); }
        Display(readout);
    }
    public void Hide() => Panel.IsVisible = false;
    public void Dispose() => Panel.ParentWidget?.Children.Remove(Panel);
    /// <summary>The lower-left corner, lifted above the highest overlapping touch control there.</summary>
    public static Vector2 FindCorner(Vector2 area, Vector2 size, BoundingRectangle[] obstacles) {
        float x = Math.Min(12, Math.Max(0, area.X - size.X)), y = Math.Max(0, area.Y - size.Y - 12);
        foreach (var r in obstacles.OrderByDescending(r => r.Max.Y))
            if (x < r.Max.X + 6 && x + size.X > r.Min.X - 6 && y < r.Max.Y + 6 && y + size.Y > r.Min.Y - 6) y = Math.Max(0, r.Min.Y - size.Y - 6);
        return new(x, y);
    }
    void Position() {
        if (host is null || host.ActualSize.X <= 1) return;
        Vector2 corner;
        if (ScUiSettings.ArmorHud.Custom) { corner = ScUiSettings.ArmorHud.Position(host.ActualSize, Size); Panel.RenderTransform = ScUiSettings.ArmorHud.Transform(Size); }
        else {
            Panel.RenderTransform = Matrix.Identity;
            var obstacles = ScAmmoHud.Obstacles(host).Select(w => new BoundingRectangle(host.ScreenToWidget(w.GlobalBounds.Min), host.ScreenToWidget(w.GlobalBounds.Max))).ToArray();
            corner = FindCorner(host.ActualSize, Size, obstacles);
        }
        Panel.MarginLeft = Math.Max(0, corner.X); Panel.MarginRight = 0;
        Panel.MarginBottom = Math.Max(0, host.ActualSize.Y - corner.Y - Size.Y); Panel.MarginTop = 0;
    }
    /// <summary>Keeps one HUD per local player in step with the stored values (SubsystemScArmor.Update).</summary>
    public static void UpdateAll(SubsystemScArmor store, Dictionary<ComponentPlayer, ScArmorHud> huds) {
        var players = store.Project?.FindSubsystem<SubsystemPlayers>(false)?.ComponentPlayers;
        foreach (var gone in huds.Keys.Where(p => players is null || !players.Contains(p)).ToArray()) { huds[gone].Dispose(); huds.Remove(gone); }
        if (players is null) return;
        foreach (var player in players) {
            if (player?.ComponentGui is null || player.PlayerData is null) continue;
            if (!huds.TryGetValue(player, out var hud)) { hud = new ScArmorHud(); if (!hud.Attach(player.ComponentGui)) continue; huds[player] = hud; }
            // a world's mode with protection of its own shows that instead (ScMode.ShownArmour; null everywhere else)
            var shown = ScModes.ShownArmour(player); string key = SubsystemScArmor.PlayerKey(player.PlayerData.PlayerIndex);
            if (!ScUiSettings.ArmorHudEnabled || !(player.ComponentHealth?.Health > 0) || shown is null && !store.Readable(key)) { hud.Hide(); continue; }
            hud.Display(shown ?? ScArmorReadout.Of(store.Get(key)));
        }
    }
}
