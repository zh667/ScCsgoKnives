using Engine;
using Engine.Graphics;
using Engine.Input;
namespace Game;

/// <summary>The deathmatch's pixel-art look (round 2, R2-6: "像素风的CS2风格，类似于CSMC那种"): CS2's dark HUD panels and
/// gold accent drawn as crisp pixel frames (whole "pixels" of <see cref="Unit"/> GUI units, notched corners, no bevel,
/// no gradient), CS2's own silhouettes as pixel sprites (tools/build_dm_pixel_icons.py) drawn with point sampling, and a
/// small pixel font of the package's own for numbers and Latin text. Chinese text keeps the game's font: there is no
/// pixel CJK font in the package.</summary>
public static class DmPx {
    public static readonly Color Panel = new(13, 15, 18, 236), PanelSoft = new(13, 15, 18, 176), Edge = new(66, 72, 80), EdgeHi = new(110, 118, 128),
        Text = new(238, 238, 236), Dim = new(146, 152, 160), Gold = new(234, 178, 52), GoldDark = new(120, 88, 22), Red = new(232, 76, 64), Green = new(110, 214, 110),
        Blue = new(104, 150, 222), Shade = new(0, 0, 0, 150), Button = new(30, 34, 40, 240), ButtonHover = new(48, 54, 62, 245), Disabled = new(24, 26, 30, 200);
    /// <summary>One pixel of the pixel look, in GUI units.</summary>
    public const float Unit = 2f;

    /// <summary>A pixel frame: a filled box whose one-pixel border has its corner pixels cut away (the CSMC tile shape).</summary>
    public static void Frame(FlatBatch2D b, Vector2 min, Vector2 max, Color fill, Color edge, float u = Unit, float depth = 0) {
        if (max.X - min.X < 3 * u || max.Y - min.Y < 3 * u) { b.QueueQuad(min, max, depth, fill); return; }
        b.QueueQuad(new Vector2(min.X + u, min.Y + u), new Vector2(max.X - u, max.Y - u), depth, fill);
        if (edge.A == 0) return;
        b.QueueQuad(new Vector2(min.X + u, min.Y), new Vector2(max.X - u, min.Y + u), depth, edge);
        b.QueueQuad(new Vector2(min.X + u, max.Y - u), new Vector2(max.X - u, max.Y), depth, edge);
        b.QueueQuad(new Vector2(min.X, min.Y + u), new Vector2(min.X + u, max.Y - u), depth, edge);
        b.QueueQuad(new Vector2(max.X - u, min.Y + u), new Vector2(max.X, max.Y - u), depth, edge);
    }
    /// <summary>A gold tab under a heading or a selected row (CS2's accent bar).</summary>
    public static void Bar(FlatBatch2D b, Vector2 min, Vector2 max, Color color, float depth = 0) => b.QueueQuad(min, max, depth, color);

    // ---------------------------------------------------------------- sprites
    static readonly Dictionary<string, Texture2D> s_sprites = new(StringComparer.Ordinal);
    static readonly HashSet<string> s_missing = new(StringComparer.Ordinal);
    public static Texture2D Sprite(string name) {
        if (string.IsNullOrEmpty(name)) return null;
        if (s_sprites.TryGetValue(name, out var t)) return t;
        if (s_missing.Contains(name)) return null;
        try { t = ContentManager.Get<Texture2D>("Textures/ScCsgoDeathmatch/px/" + name); s_sprites[name] = t; return t; }
        catch (Exception) { s_missing.Add(name); return null; }
    }
    static readonly Dictionary<string, string> s_gunSprite = new(StringComparer.Ordinal) { ["m4a1s"] = "m4a1_silencer", ["m4a4"] = "m4a1", ["glock18"] = "glock", ["hkp2000"] = "p2000" };
    /// <summary>The core's knife asset names (CsmcKnifeRig.FrozenKnifeOrder) as CS2 names their icons. Until 2026-10-06 the
    /// names were looked up as they are, and only the bayonet found its icon - the one knife CS2 writes without the knife_
    /// prefix (the user: "刀具现在只有刺刀有HUD"). tools/build_dm_hud_icons.py carries the same table.</summary>
    static readonly Dictionary<string, string> s_knifeSprite = new(StringComparer.Ordinal) {
        ["karambit"] = "knife_karambit", ["m9"] = "knife_m9_bayonet", ["butterfly"] = "knife_butterfly", ["bayonet"] = "bayonet", ["bowie"] = "knife_survival_bowie",
        ["canis"] = "knife_canis", ["cord"] = "knife_cord", ["css"] = "knife_css", ["default_ct"] = "knife", ["default_t"] = "knife_t", ["falchion"] = "knife_falchion",
        ["flip"] = "knife_flip", ["gut"] = "knife_gut", ["kukri"] = "knife_kukri", ["navaja"] = "knife_gypsy_jackknife", ["outdoor"] = "knife_outdoor", ["push"] = "knife_push",
        ["skeleton"] = "knife_skeleton", ["stiletto"] = "knife_stiletto", ["tactical"] = "knife_tactical", ["talon"] = "knife_widowmaker", ["ursus"] = "knife_ursus" };
    /// <summary>CS2's icon name for a weapon asset name ("ak47", "karambit", "knife_karambit", "grenade_hegrenade", "taser"), or null.</summary>
    public static string IconName(string asset) {
        if (string.IsNullOrEmpty(asset)) return null;
        int grenade = Array.IndexOf(ScGrenadeBlock.Assets, asset);
        if (grenade >= 0) return grenade switch { 0 => "hegrenade", 1 => "flashbang", 2 => "smokegrenade", 3 => "molotov", 4 => "incgrenade", _ => "decoy" };
        if (s_knifeSprite.TryGetValue(asset, out var knife)) return knife;
        if (asset.StartsWith("knife_", StringComparison.Ordinal) && s_knifeSprite.TryGetValue(asset["knife_".Length..], out knife)) return knife;
        if (asset.StartsWith("knife", StringComparison.Ordinal)) return asset;
        return s_gunSprite.GetValueOrDefault(asset, asset);
    }
    static bool IsKnifeIcon(string name) => name == "bayonet" || name.StartsWith("knife", StringComparison.Ordinal);
    /// <summary>The pixel sprite of a weapon by its asset name ("ak47", "karambit", "knife_karambit", "grenade_hegrenade", "taser").</summary>
    public static Texture2D WeaponSprite(string asset) => IconName(asset) is { } name ? Sprite(name) ?? (IsKnifeIcon(name) ? Sprite("knife") : null) : null;
    public static Texture2D GunSprite(int variant) => variant >= 0 && variant < GunSpec.All.Length ? WeaponSprite(GunSpec.All[variant].Name) : null;
    public static Texture2D KnifeSprite(int variant) => WeaponSprite(ScKnifeBlock.GetAssetName(variant));
    /// <summary>The weapon asset name an item value stands for: a gun (its model, also under a skin), a knife, a throwable; null for anything else.</summary>
    public static string ItemAsset(int value) {
        var block = BlocksManager.Blocks[Terrain.ExtractContents(value)];
        if (block is ScGunSkinTemplateBlock) return ScGunSkinCatalog.Find(Terrain.ExtractData(value))?.Gun;
        return block switch {
            ScGunBlock => GunSpec.GetVariant(Terrain.ExtractData(value)) is var v && v >= 0 && v < GunSpec.All.Length ? GunSpec.All[v].Name : null,
            ScKnifeBlock => ScKnifeBlock.GetAssetName(Terrain.ExtractData(value) & 31),
            ScGrenadeBlock => ScGrenadeBlock.Assets[Math.Clamp(ScGrenadeBlock.Kind(value), 0, 5)],
            _ => null };
    }
    /// <summary>The pixel sprite of an item value, or null.</summary>
    public static Texture2D ItemSprite(int value) => WeaponSprite(ItemAsset(value));

    // ---------------------------------------------------------------- CS2's HUD icons (smooth)
    static readonly Dictionary<string, Texture2D> s_hud = new(StringComparer.Ordinal);
    static readonly HashSet<string> s_hudMissing = new(StringComparer.Ordinal);
    /// <summary>CS2's own equipment icon as CS2 draws it - the silhouette SVG rendered smooth, 64 px high, white with its alpha
    /// (tools/build_dm_hud_icons.py) - or null. The buy wheel shows these since 2026-10-06 (the user: the pixel sprites looked
    /// CSMC-like, "可以用cs2自己的"); the kill feed and the equipment rows keep the pixel sprites.</summary>
    public static Texture2D HudIcon(string name) {
        if (string.IsNullOrEmpty(name)) return null;
        if (s_hud.TryGetValue(name, out var t)) return t;
        if (s_hudMissing.Contains(name)) return null;
        try { t = ContentManager.Get<Texture2D>("Textures/ScCsgoDeathmatch/hud/" + name); s_hud[name] = t; return t; }
        catch (Exception e) { s_hudMissing.Add(name); KnifeDiagnostics.WarnOnce("dm-hud-icon-" + name, $"[CS_DM] CS2 icon hud/{name} unavailable ({e.GetType().Name}); the wheel shows the name alone"); return null; }
    }
    public static Texture2D WeaponHud(string asset) => IconName(asset) is { } name ? HudIcon(name) ?? (IsKnifeIcon(name) ? HudIcon("knife") : null) : null;
    public static Texture2D ItemHud(int value) => WeaponHud(ItemAsset(value));
    /// <summary>Draws a smooth icon (linear sampling) with its top-left at <paramref name="at"/>, <paramref name="height"/> high, its aspect kept.</summary>
    public static void Image(PrimitivesRenderer2D r, Texture2D t, Vector2 at, float height, Color color, Matrix transform) {
        if (t is null || t.Height <= 0) return;
        var batch = r.TexturedBatch(t, false, 1, null, null, BlendState.NonPremultiplied, SamplerState.LinearClamp);
        int first = batch.TriangleVertices.Count; Vector2 size = new(t.Width * height / t.Height, height);
        batch.QueueQuad(at, at + size, 0, Vector2.Zero, Vector2.One, color);
        batch.TransformTriangles(transform, first);
    }
    /// <summary>Draws a sprite with its top-left at <paramref name="at"/>, scaled by whole pixels (point sampled). Batch layer
    /// 1: the engine sorts the batches of one flush by layer with an unstable sort, so a sprite on layer 0 beside the
    /// frames' flat batch came out under its own tile in some processes and over it in others (round 2: the equipment
    /// rows showed their silhouettes on the MP client, not in single player or on the host).</summary>
    public static void Sprite(PrimitivesRenderer2D r, Texture2D t, Vector2 at, float scale, Color color, Matrix transform, bool flip = false) {
        if (t is null) return;
        var batch = r.TexturedBatch(t, false, 1, null, null, BlendState.NonPremultiplied, SamplerState.PointClamp);
        int first = batch.TriangleVertices.Count; Vector2 size = new Vector2(t.Width, t.Height) * scale;
        batch.QueueQuad(at, at + size, 0, flip ? new Vector2(1, 0) : Vector2.Zero, flip ? new Vector2(0, 1) : Vector2.One, color);
        batch.TransformTriangles(transform, first);
    }

    // ---------------------------------------------------------------- the pixel font (5x7, the package's own)
    static readonly Dictionary<char, string[]> s_glyphs = new() {
        ['0'] = ["01110", "10001", "10011", "10101", "11001", "10001", "01110"], ['1'] = ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
        ['2'] = ["01110", "10001", "00001", "00110", "01000", "10000", "11111"], ['3'] = ["11110", "00001", "00001", "01110", "00001", "00001", "11110"],
        ['4'] = ["00010", "00110", "01010", "10010", "11111", "00010", "00010"], ['5'] = ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
        ['6'] = ["00110", "01000", "10000", "11110", "10001", "10001", "01110"], ['7'] = ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
        ['8'] = ["01110", "10001", "10001", "01110", "10001", "10001", "01110"], ['9'] = ["01110", "10001", "10001", "01111", "00001", "00010", "01100"],
        ['A'] = ["01110", "10001", "10001", "11111", "10001", "10001", "10001"], ['B'] = ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
        ['C'] = ["01110", "10001", "10000", "10000", "10000", "10001", "01110"], ['D'] = ["11100", "10010", "10001", "10001", "10001", "10010", "11100"],
        ['E'] = ["11111", "10000", "10000", "11110", "10000", "10000", "11111"], ['F'] = ["11111", "10000", "10000", "11110", "10000", "10000", "10000"],
        ['G'] = ["01110", "10001", "10000", "10111", "10001", "10001", "01111"], ['H'] = ["10001", "10001", "10001", "11111", "10001", "10001", "10001"],
        ['I'] = ["01110", "00100", "00100", "00100", "00100", "00100", "01110"], ['J'] = ["00111", "00010", "00010", "00010", "00010", "10010", "01100"],
        ['K'] = ["10001", "10010", "10100", "11000", "10100", "10010", "10001"], ['L'] = ["10000", "10000", "10000", "10000", "10000", "10000", "11111"],
        ['M'] = ["10001", "11011", "10101", "10101", "10001", "10001", "10001"], ['N'] = ["10001", "10001", "11001", "10101", "10011", "10001", "10001"],
        ['O'] = ["01110", "10001", "10001", "10001", "10001", "10001", "01110"], ['P'] = ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
        ['Q'] = ["01110", "10001", "10001", "10001", "10101", "10010", "01101"], ['R'] = ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
        ['S'] = ["01111", "10000", "10000", "01110", "00001", "00001", "11110"], ['T'] = ["11111", "00100", "00100", "00100", "00100", "00100", "00100"],
        ['U'] = ["10001", "10001", "10001", "10001", "10001", "10001", "01110"], ['V'] = ["10001", "10001", "10001", "10001", "10001", "01010", "00100"],
        ['W'] = ["10001", "10001", "10001", "10101", "10101", "10101", "01010"], ['X'] = ["10001", "10001", "01010", "00100", "01010", "10001", "10001"],
        ['Y'] = ["10001", "10001", "01010", "00100", "00100", "00100", "00100"], ['Z'] = ["11111", "00001", "00010", "00100", "01000", "10000", "11111"],
        [':'] = ["00000", "00100", "00100", "00000", "00100", "00100", "00000"], ['.'] = ["00000", "00000", "00000", "00000", "00000", "01100", "01100"],
        ['-'] = ["00000", "00000", "00000", "11111", "00000", "00000", "00000"], ['+'] = ["00000", "00100", "00100", "11111", "00100", "00100", "00000"],
        ['/'] = ["00001", "00010", "00010", "00100", "01000", "01000", "10000"], ['%'] = ["11001", "11010", "00010", "00100", "01000", "01011", "10011"],
        ['#'] = ["01010", "11111", "01010", "01010", "01010", "11111", "01010"], ['('] = ["00010", "00100", "01000", "01000", "01000", "00100", "00010"],
        [')'] = ["01000", "00100", "00010", "00010", "00010", "00100", "01000"], ['!'] = ["00100", "00100", "00100", "00100", "00100", "00000", "00100"],
        ['?'] = ["01110", "10001", "00001", "00110", "00100", "00000", "00100"], ['x'] = ["00000", "00000", "10001", "01010", "00100", "01010", "10001"],
        [','] = ["00000", "00000", "00000", "00000", "00110", "00100", "01000"], ['\''] = ["00100", "00100", "01000", "00000", "00000", "00000", "00000"],
        ['|'] = ["00100", "00100", "00100", "00100", "00100", "00100", "00100"], [' '] = ["00000", "00000", "00000", "00000", "00000", "00000", "00000"],
    };
    /// <summary>Whether every character of the text has a pixel glyph (otherwise the game's font is used for it).</summary>
    public static bool PixelText(string text) => !string.IsNullOrEmpty(text) && text.All(c => s_glyphs.ContainsKey(char.ToUpperInvariant(c)) || s_glyphs.ContainsKey(c));
    public static Vector2 Measure(string text, float scale) => text.Length == 0 ? Vector2.Zero : new Vector2((text.Length * 6 - 1) * scale, 7 * scale);
    /// <summary>Pixel text with its top-left at <paramref name="at"/>; a one-pixel dark shadow under it.</summary>
    public static void Print(FlatBatch2D b, string text, Vector2 at, float scale, Color color, bool shadow = true, float depth = 0) {
        for (int pass = shadow ? 0 : 1; pass < 2; pass++) {
            Vector2 offset = pass == 0 ? new Vector2(scale, scale) : Vector2.Zero; Color c = pass == 0 ? new Color(0, 0, 0, color.A * 3 / 5) : color; float x = at.X;
            foreach (char raw in text) {
                char ch = s_glyphs.ContainsKey(raw) ? raw : char.ToUpperInvariant(raw);
                if (s_glyphs.TryGetValue(ch, out var rows))
                    for (int row = 0; row < 7; row++) for (int col = 0; col < 5; col++)
                        if (rows[row][col] == '1') { Vector2 p = new Vector2(x + col * scale, at.Y + row * scale) + offset; b.QueueQuad(p, p + new Vector2(scale), depth, c); }
                x += 6 * scale;
            }
        }
    }
}

/// <summary>A widget that draws itself with pixel primitives (in its own space, through its global transform, so a turned
/// or scaled HUD element draws - and hit-tests - as placed).</summary>
public abstract class DmPixelWidget : CanvasWidget {
    protected DmPixelWidget() { IsHitTestVisible = false; }
    public override void MeasureOverride(Vector2 parentAvailableSize) { base.MeasureOverride(parentAvailableSize); IsDrawRequired = true; }
    public sealed override void Draw(DrawContext dc) {
        var flat = dc.PrimitivesRenderer2D.FlatBatch(0, DepthStencilState.None, null, BlendState.AlphaBlend);
        int first = flat.TriangleVertices.Count;
        Paint(dc, flat);
        if (GlobalColorTransform != Color.White) for (int i = first; i < flat.TriangleVertices.Count; i++) { var v = flat.TriangleVertices.Array[i]; v.Color *= GlobalColorTransform; flat.TriangleVertices.Array[i] = v; }
        flat.TransformTriangles(GlobalTransform, first);
        PaintSprites(dc);
    }
    /// <summary>Flat pixel shapes, in the widget's own units (0..ActualSize).</summary>
    protected abstract void Paint(DrawContext dc, FlatBatch2D flat);
    /// <summary>Sprites (their own batches), drawn after the shapes.</summary>
    protected virtual void PaintSprites(DrawContext dc) { }
}

/// <summary>A pixel panel: dark fill, one-pixel edge with cut corners, an optional title strip with a gold accent.</summary>
public sealed class DmPixelPanel : DmPixelWidget {
    public Color Fill = DmPx.Panel, Edge = DmPx.Edge;
    public string Title; public float TitleHeight = 34;
    readonly LabelWidget m_title = new() { FontScale = .78f, Color = DmPx.Text, DropShadow = false, HorizontalAlignment = WidgetAlignment.Near, VerticalAlignment = WidgetAlignment.Near, Margin = new Vector2(14, 7), IsHitTestVisible = false };
    public DmPixelPanel() { HorizontalAlignment = WidgetAlignment.Stretch; VerticalAlignment = WidgetAlignment.Stretch; Children.Add(m_title); }
    public override void Update() { m_title.Text = Title ?? ""; m_title.IsVisible = !string.IsNullOrEmpty(Title); }
    protected override void Paint(DrawContext dc, FlatBatch2D b) {
        DmPx.Frame(b, Vector2.Zero, ActualSize, Fill, Edge);
        if (!string.IsNullOrEmpty(Title)) {
            b.QueueQuad(new Vector2(DmPx.Unit, DmPx.Unit), new Vector2(ActualSize.X - DmPx.Unit, TitleHeight), 0, new Color(24, 27, 32, 240));
            b.QueueQuad(new Vector2(DmPx.Unit, TitleHeight), new Vector2(ActualSize.X - DmPx.Unit, TitleHeight + DmPx.Unit), 0, DmPx.Gold);
        }
    }
}

/// <summary>A pixel button: the engine's own button (click, press and keyboard behaviour, hit testing) with its bevelled
/// face replaced by a flat pixel tile: dark, lighter under the pointer, gold when pressed or selected, dim when disabled.
/// Optional sprite left of the text.</summary>
public sealed class DmPixelButton : BevelledButtonWidget {
    public bool Selected; public Texture2D Icon; public float IconScale = 2; public Color Accent = DmPx.Gold;
    public DmPixelButton(string text, float width = 160, float height = 44) {
        m_rectangleWidget.IsVisible = false; Text = text; Size = new Vector2(width, Math.Max(height, 40)); FontScale = .72f;
        m_labelWidget.DropShadow = false;
    }
    public override void MeasureOverride(Vector2 parentAvailableSize) {
        base.MeasureOverride(parentAvailableSize); IsDrawRequired = true;
        bool on = IsEnabledGlobal;
        m_labelWidget.Color = !on ? new Color(96, 100, 106) : m_clickableWidget.IsPressed || Selected ? new Color(16, 16, 16) : DmPx.Text;
        m_labelWidget.MarginLeft = Icon is null ? 0 : Icon.Width * IconScale + 8;
    }
    bool Hovered() {
        if (Input.MousePosition is not { } mouse || !IsEnabledGlobal) return false;
        var hit = HitTestGlobal(mouse); return hit is not null && (ReferenceEquals(hit, this) || hit.IsChildWidgetOf(this));
    }
    public override void Draw(DrawContext dc) {
        var b = dc.PrimitivesRenderer2D.FlatBatch(0, DepthStencilState.None, null, BlendState.AlphaBlend); int first = b.TriangleVertices.Count;
        bool on = IsEnabledGlobal, down = m_clickableWidget.IsPressed;
        Color fill = !on ? DmPx.Disabled : down || Selected ? Accent : Hovered() ? DmPx.ButtonHover : DmPx.Button;
        Color edge = !on ? new Color(44, 46, 50) : down || Selected ? new Color(255, 214, 120) : Hovered() ? DmPx.Gold : DmPx.EdgeHi;
        DmPx.Frame(b, Vector2.Zero, ActualSize, fill * GlobalColorTransform, edge * GlobalColorTransform);
        b.TransformTriangles(GlobalTransform, first);
        if (Icon is not null) DmPx.Sprite(dc.PrimitivesRenderer2D, Icon, new Vector2(10, (ActualSize.Y - Icon.Height * IconScale) / 2), IconScale, (down || Selected ? new Color(16, 16, 16) : DmPx.Text) * GlobalColorTransform, GlobalTransform);
    }
}

/// <summary>Pixel-font text as a widget (numbers, Latin); falls back to the game's font for anything else.</summary>
public sealed class DmPixelText : DmPixelWidget {
    public string Text = ""; public float Scale = 3; public Color Color = DmPx.Text; public bool Shadow = true;
    public TextAnchor Anchor = TextAnchor.Left;
    readonly LabelWidget m_fallback = new() { DropShadow = true, IsHitTestVisible = false };
    public DmPixelText() { Children.Add(m_fallback); }
    public override void MeasureOverride(Vector2 parentAvailableSize) {
        bool pixel = DmPx.PixelText(Text); m_fallback.IsVisible = !pixel;
        if (!pixel) { m_fallback.Text = Text; m_fallback.Color = Color; m_fallback.FontScale = Scale / 3.6f; m_fallback.HorizontalAlignment = Anchor.HasFlag(TextAnchor.Right) ? WidgetAlignment.Far : Anchor.HasFlag(TextAnchor.HorizontalCenter) ? WidgetAlignment.Center : WidgetAlignment.Near; m_fallback.VerticalAlignment = WidgetAlignment.Center; }
        else if (Size.X < 0 || Size.Y < 0) Size = DmPx.Measure(Text, Scale) + new Vector2(Scale);
        base.MeasureOverride(parentAvailableSize);
    }
    protected override void Paint(DrawContext dc, FlatBatch2D b) {
        if (!DmPx.PixelText(Text)) return;
        Vector2 size = DmPx.Measure(Text, Scale);
        float x = Anchor.HasFlag(TextAnchor.Right) ? ActualSize.X - size.X - Scale : Anchor.HasFlag(TextAnchor.HorizontalCenter) ? (ActualSize.X - size.X) / 2 : 0;
        DmPx.Print(b, Text, new Vector2(MathF.Round(x), MathF.Round((ActualSize.Y - size.Y) / 2)), Scale, Color, Shadow);
    }
}

/// <summary>A sprite as a widget, sized to the sprite by whole pixels.</summary>
public sealed class DmPixelIcon : Widget {
    public Texture2D Texture; public float Scale = 2; public Color Color = DmPx.Text;
    public DmPixelIcon() { IsHitTestVisible = false; }
    public override void MeasureOverride(Vector2 parentAvailableSize) {
        DesiredSize = Texture is null ? Vector2.Zero : new Vector2(Texture.Width, Texture.Height) * Scale; IsDrawRequired = Texture is not null;
    }
    public override void Draw(DrawContext dc) { if (Texture is not null) DmPx.Sprite(dc.PrimitivesRenderer2D, Texture, Vector2.Zero, Scale, Color * GlobalColorTransform, GlobalTransform); }
}

/// <summary>A smooth icon as a widget (CS2's HUD icons), drawn <see cref="Height"/> high with its aspect kept.</summary>
public sealed class DmHudIcon : Widget {
    public Texture2D Texture; public float Height = 32; public Color Color = DmPx.Text;
    public DmHudIcon() { IsHitTestVisible = false; }
    public float Width => Texture is null || Texture.Height <= 0 ? 0 : Texture.Width * Height / Texture.Height;
    public override void MeasureOverride(Vector2 parentAvailableSize) { DesiredSize = Texture is null ? Vector2.Zero : new Vector2(Width, Height); IsDrawRequired = Texture is not null; }
    public override void Draw(DrawContext dc) { if (Texture is not null) DmPx.Image(dc.PrimitivesRenderer2D, Texture, Vector2.Zero, Height, Color * GlobalColorTransform, GlobalTransform); }
}

/// <summary>A pixel slider: a flat track, a gold fill up to the value, a square knob and the value in pixel digits at the
/// right. Drag anywhere on it (mouse or touch); the value snaps to <see cref="Step"/>.</summary>
public sealed class DmPixelSlider : DmPixelWidget {
    public float Min, Max = 1, Step = .05f, Value;
    /// <summary>How the value is shown ("x" for a scale, "%" for an opacity, "" for degrees).</summary>
    public Func<float, string> Format = v => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    public const float ReadoutWidth = 64;
    bool m_dragging;
    public DmPixelSlider(float min, float max, float step, float value, float width = 280) { Min = min; Max = max; Step = step; Value = value; Size = new Vector2(width, 40); IsHitTestVisible = true; }
    float TrackLeft => 8; float TrackRight => ActualSize.X - ReadoutWidth - 8;
    public override void Update() {
        if (Input.Press is { } press) {
            if (!m_dragging && Input.Tap is { } tap && HitTestGlobal(tap) is { } hit && ReferenceEquals(hit, this)) m_dragging = true;
            if (m_dragging) {
                float x = ScreenToWidget(press).X, t = Math.Clamp((x - TrackLeft) / Math.Max(1, TrackRight - TrackLeft), 0, 1);
                float v = Min + t * (Max - Min); if (Step > 0) v = MathF.Round(v / Step) * Step;
                Value = Math.Clamp(v, Min, Max);
            }
        }
        else m_dragging = false;
    }
    protected override void Paint(DrawContext dc, FlatBatch2D b) {
        float mid = MathF.Round(ActualSize.Y / 2), left = TrackLeft, right = TrackRight, t = Max > Min ? Math.Clamp((Value - Min) / (Max - Min), 0, 1) : 0, knob = MathF.Round(left + t * (right - left));
        b.QueueQuad(new Vector2(left, mid - 3), new Vector2(right, mid + 3), 0, new Color(40, 44, 50));
        b.QueueQuad(new Vector2(left, mid - 3), new Vector2(knob, mid + 3), 0, m_dragging ? DmPx.Gold : DmPx.GoldDark);
        DmPx.Frame(b, new Vector2(knob - 8, mid - 12), new Vector2(knob + 8, mid + 12), m_dragging ? DmPx.Gold : DmPx.Text, DmPx.Edge);
        string text = Format(Value);
        if (DmPx.PixelText(text)) { Vector2 size = DmPx.Measure(text, 2); DmPx.Print(b, text, new Vector2(MathF.Round(ActualSize.X - size.X - 4), MathF.Round(mid - size.Y / 2)), 2, DmPx.Text); }
    }
}
