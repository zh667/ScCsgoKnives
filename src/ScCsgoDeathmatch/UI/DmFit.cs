using Engine;
namespace Game;

/// <summary>Keeps a deathmatch panel inside the part of the screen the game's own controls leave free (round 3, the user's
/// phone, 2400x1080 px, about 1183x532 GUI units: the 850x500 buy wheel reached under the hotbar - the game draws its
/// bars and hotbar above the modal panels, so they took the taps on 入场 and 确认 - and under the multiplayer join code).
/// The panel is scaled down to the free area, never up, centred in it, and kept beside or below the join code.</summary>
/// <remarks>The panel keeps its design size for its own layout and is drawn (and hit-tested: the engine uses one transform
/// for both) scaled about its top-left corner, on top of whatever the game's opening animation does - the engine gives a
/// widget with a layout transform its parent's size in its own units, which would shrink the content's room instead.
/// The game centres its modal container in a vertical stack above the move pad; a container taller than that space
/// starts at the top. A bottom margin of the whole height keeps the container that tall, so the panel's top is exactly
/// its top margin; the left or the right margin moves it across the centred container.</remarks>
public static class DmFit {
    public const float Gap = 6, MinScale = .5f;
    public readonly record struct Box(Vector2 Min, Vector2 Max) {
        public float Width => Max.X - Min.X;
        public float Height => Max.Y - Min.Y;
        public bool Overlaps(Box o) => Min.X < o.Max.X && o.Min.X < Max.X && Min.Y < o.Max.Y && o.Min.Y < Max.Y;
    }
    /// <summary>The scale and the top-left corner of a panel of this size in a controls area of this size, beside the
    /// game's side columns (their widths), above its bottom bars (their top) and clear of the join code when there is one
    /// (pure: checked offline).</summary>
    public static (float Scale, Vector2 Corner) Fit(Vector2 area, Vector2 size, float left, float right, float bottom, Box? code) {
        var band = new Box(new Vector2(left + Gap, Gap), new Vector2(area.X - right - Gap, Math.Min(bottom, area.Y) - Gap));
        var candidates = new List<Box> { band };
        if (code is { } c && c.Overlaps(band)) {
            candidates[0] = band with { Max = new Vector2(Math.Min(band.Max.X, c.Min.X - Gap), band.Max.Y) };   // beside it
            candidates.Add(band with { Min = new Vector2(band.Min.X, Math.Max(band.Min.Y, c.Max.Y + Gap)) });   // below it
        }
        float ScaleIn(Box b) => Math.Clamp(Math.Min(b.Width / size.X, b.Height / size.Y), MinScale, 1);
        var best = candidates.OrderByDescending(ScaleIn).First();
        float s = ScaleIn(best); Vector2 scaled = size * s;
        float x = Math.Clamp((area.X - scaled.X) / 2, best.Min.X, Math.Max(best.Min.X, best.Max.X - scaled.X));
        float y = best.Min.Y + Math.Max(0, (best.Height - scaled.Y) / 2);
        return (s, new Vector2(x, y));
    }
    /// <summary>Places a modal panel of this design size for the player's screen as it is now (from the panel's update):
    /// sets its margins and returns the scale to draw it with (see <see cref="Arranged"/>).</summary>
    public static Matrix Apply(Widget panel, ComponentGui gui, Vector2 size) {
        var controls = gui.ControlsContainerWidget; Vector2 area = controls?.ActualSize ?? Vector2.Zero;
        if (area.X < 1 || area.Y < 1) return Matrix.Identity;
        Vector2 At(Widget w, Vector2 p) => controls.ScreenToWidget(w.WidgetToScreen(p));
        float Top(Widget w) => w is { IsVisible: true } && w.ActualSize.Y > 0 ? At(w, Vector2.Zero).Y : area.Y;
        float bottom = Math.Min(Top(gui.ShortInventoryWidget), Top(gui.HealthBarWidget?.ParentWidget));
        float Width(Widget w) => w is { IsVisible: true } ? w.ActualSize.X : 0;
        Box? code = null;
        // the multiplayer platform's "联机码" label (its own widget at the top right; absent without the platform)
        foreach (var w in gui.m_componentPlayer.GuiWidget.Children)
            if (w.GetType().Name == "NetworkStatusWidget" && w.IsVisible && w.ActualSize.X > 0) code = new Box(At(w, Vector2.Zero), At(w, w.ActualSize));
        var (s, corner) = Fit(area, size, Width(gui.m_leftControlsContainerWidget), Width(gui.m_rightControlsContainerWidget), bottom, code);
        float shift = corner.X - (area.X - size.X) / 2;   // the layout box keeps the design size; the drawing starts at its corner
        panel.MarginLeft = Math.Max(0, 2 * shift); panel.MarginRight = Math.Max(0, -2 * shift);
        panel.MarginTop = corner.Y; panel.MarginBottom = area.Y;
        return Matrix.CreateScale(s, s, 1);
    }
    /// <summary>The panel's arrangement with the fit applied after the game's own render transform (its opening
    /// animation), which is left as the game set it.</summary>
    public static void Arranged(Widget panel, Matrix fit, Action arrange) {
        var animated = panel.RenderTransform;
        panel.RenderTransform = animated * fit;
        try { arrange(); } finally { panel.RenderTransform = animated; }
    }
}
