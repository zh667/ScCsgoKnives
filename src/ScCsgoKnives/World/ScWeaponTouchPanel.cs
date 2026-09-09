using Engine;
namespace Game;

/// <summary>The mod's own configurable touch buttons.
///
/// Each function owns a button placed from its saved normalised position, so the settings survive a rotation, a
/// resolution change and a UI-scale change, and the M4's silencer button can no longer drag the grenade button
/// with it. Only the button rectangles consume input - there is no panel behind them to swallow a look or a
/// move - and a button that is switched off or hidden releases its finger instead of leaving it held.
///
/// Vanilla movement, looking and firing are untouched: this places the mod's extra actions only.</summary>
public sealed class ScWeaponTouchPanel : IDisposable {
    static readonly HashSet<ScWeaponTouchPanel> s_instances = [];
    public static void SuppressAll(bool suppressed) { foreach (var panel in s_instances) panel.SetSuppressed(suppressed); }
    public static void DisposeAll() { foreach (var panel in s_instances.ToArray()) panel.Dispose(); }
    public sealed class Entry {
        public string Id;
        public readonly BevelledButtonWidget Button = new();
        public readonly ScWeaponButtonInput Input = new();
        public Color BaseCenter, BaseBevel;
        public bool Captured;
    }
    /// <summary>The original button footprint; a scale of 1 reproduces the 0.28-0.39 layout exactly.</summary>
    public const float BaseWidth = 104f, BaseHeight = 60f;
    /// <summary>No configuration may make a button smaller than a reliable touch target.</summary>
    public const float MinTouch = 48f;

    readonly Dictionary<string, Entry> m_entries = new(StringComparer.Ordinal);
    ContainerWidget m_container;
    public static bool MenuActive => ScreensManager.CurrentScreen is ScGunSettingsScreen or ScGunLayoutScreen or ScGunBindingsScreen;

    public IReadOnlyDictionary<string, Entry> Entries => m_entries;
    public bool Clicked(string id) => m_entries.TryGetValue(id, out var e) && e.Input.Clicked;
    public bool Pressed(string id) => m_entries.TryGetValue(id, out var e) && e.Input.Pressed;
    public bool Cancelled(string id) => !m_entries.TryGetValue(id, out var e) || e.Input.Cancelled;

    public static Vector2 SizeOf(ScButtonLayout layout) {
        float scale = Math.Clamp(layout?.Scale ?? 1f, .5f, 2f);
        return new Vector2(Math.Max(BaseWidth * scale, MinTouch), Math.Max(BaseHeight * scale, MinTouch));
    }
    /// <summary>Top-left corner for a normalised centre inside an area, kept fully on screen.</summary>
    public static Vector2 CornerOf(ScButtonLayout layout, Vector2 area) {
        Vector2 size = SizeOf(layout);
        float x = Math.Clamp(layout.X * area.X - size.X * .5f, 0, Math.Max(0, area.X - size.X));
        float y = Math.Clamp(layout.Y * area.Y - size.Y * .5f, 0, Math.Max(0, area.Y - size.Y));
        return new Vector2(x, y);
    }
    /// <summary>The normalised centre a dragged corner lands on. The inverse of <see cref="CornerOf"/>.</summary>
    public static Vector2 CentreOf(Vector2 corner, ScButtonLayout layout, Vector2 area) {
        Vector2 size = SizeOf(layout);
        float x = area.X > 0 ? (Math.Clamp(corner.X, 0, Math.Max(0, area.X - size.X)) + size.X * .5f) / area.X : .5f;
        float y = area.Y > 0 ? (Math.Clamp(corner.Y, 0, Math.Max(0, area.Y - size.Y)) + size.Y * .5f) / area.Y : .5f;
        return new Vector2(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));
    }
    /// <summary>Applies one layout's size, position, opacities and label to a button.</summary>
    public static void Style(BevelledButtonWidget button, ScButtonLayout layout, Vector2 area, Color baseCenter, Color baseBevel) {
        Vector2 size = SizeOf(layout), corner = CornerOf(layout, area);
        button.Size = size;
        button.HorizontalAlignment = WidgetAlignment.Near;
        button.VerticalAlignment = WidgetAlignment.Near;
        button.MarginLeft = corner.X; button.MarginRight = 0;
        button.MarginTop = corner.Y; button.MarginBottom = 0;
        button.FontScale = Math.Clamp(size.Y / BaseHeight, .6f, 1.4f);
        // Pressing raises the border and centre instead of moving the button, so the touch area never shifts.
        float pressed = button.m_clickableWidget.IsPressed ? 1.35f : 1f;
        byte background = (byte)Math.Clamp(layout.Background * pressed * baseCenter.A, 0f, 255f);
        byte bevel = (byte)Math.Clamp(layout.Background * pressed * baseBevel.A, 0f, 255f);
        button.CenterColor = new Color(baseCenter.R, baseCenter.G, baseCenter.B, background);
        button.BevelColor = new Color(baseBevel.R, baseBevel.G, baseBevel.B, bevel);
        button.Color = new Color((byte)255, (byte)255, (byte)255, (byte)Math.Clamp(layout.Foreground * 255f, 0f, 255f));
    }

    public void Attach(ContainerWidget container) {
        if (ReferenceEquals(m_container, container)) return;
        Dispose();
        m_container = container;
        s_instances.Add(this);
        foreach (string id in ScGunFunctions.All) {
            var entry = new Entry { Id = id };
            entry.BaseCenter = entry.Button.CenterColor;
            entry.BaseBevel = entry.Button.BevelColor;
            entry.Button.Text = ScGunFunctions.Label(id);
            entry.Button.IsVisible = false;
            m_entries[id] = entry;
            container?.Children.Add(entry.Button);
        }
    }

    /// <summary>One frame. <paramref name="wanted"/> says which functions the held item offers right now;
    /// everything else is hidden, and a hidden or disabled button gives its finger back.</summary>
    public void Update(Vector2 area, bool enabled, bool touch, Func<string, bool> wanted) {
        bool master = ScUiSettings.CustomButtons && !MenuActive;
        foreach (var entry in m_entries.Values) {
            var layout = ScUiSettings.Layout(entry.Id);
            bool visible = master && enabled && layout.Enabled && wanted(entry.Id);
            entry.Button.IsVisible = visible;
            if (visible && area.X > 1 && area.Y > 1) Style(entry.Button, layout, area, entry.BaseCenter, entry.BaseBevel);
            entry.Input.Sample(entry.Button, touch, visible);
            entry.Captured = visible && entry.Input.Pressed;
        }
    }
    void SetSuppressed(bool suppressed) {
        if (suppressed) foreach (var entry in m_entries.Values) { entry.Button.IsVisible = false; entry.Input.Cancel(); entry.Captured = false; }
    }
    /// <summary>True while any button owns a finger; a menu or a weapon change must end that capture.</summary>
    public bool AnyCaptured => m_entries.Values.Any(e => e.Captured);

    public void Dispose() {
        s_instances.Remove(this);
        foreach (var entry in m_entries.Values) entry.Button.ParentWidget?.Children.Remove(entry.Button);
        m_entries.Clear();
        m_container = null;
    }
}
