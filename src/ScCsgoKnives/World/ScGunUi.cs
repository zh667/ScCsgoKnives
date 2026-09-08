using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>Small shared builders for the mod's own screens.
///
/// Everything is built in code rather than from a content XML, so the panels cannot break when the game's own
/// screen layouts change, and the same builders serve the phone and the desktop: sizes are logical units, touch
/// targets never fall below 48, and rows stack into one column when the width runs out.</summary>
public static class ScGunUi {
    /// <summary>Minimum reliable touch target, in logical units.</summary>
    public const float Touch = 48f;
    /// <summary>Below this width the two-column layouts collapse into one.</summary>
    public const float NarrowWidth = 720f;
    public static readonly Color Text = new(235, 238, 240);
    public static readonly Color Dim = new(160, 168, 174);
    public static readonly Color Accent = new(90, 210, 225);
    public static readonly Color Panel = new(18, 22, 26, 225);
    public static readonly Color PanelEdge = new(64, 74, 82, 235);

    public static LabelWidget Label(string text, float scale = 1f, Color? color = null, TextAnchor anchor = TextAnchor.Left) => new() {
        Text = text, FontScale = scale, Color = color ?? Text, TextAnchor = anchor, DropShadow = true,
        VerticalAlignment = WidgetAlignment.Center,
    };
    public static BevelledButtonWidget Button(string text, float width = 140f, float height = Touch) => new() {
        Text = text, Size = new Vector2(width, Math.Max(height, Touch)), FontScale = .82f,
        VerticalAlignment = WidgetAlignment.Center, HorizontalAlignment = WidgetAlignment.Center,
    };
    public static CheckboxWidget Toggle(string text, bool value) {
        var box = new CheckboxWidget { Text = text, IsChecked = value, IsAutoCheckingEnabled = true, VerticalAlignment = WidgetAlignment.Center };
        box.Size = new Vector2(-1, Touch);
        return box;
    }
    public static SliderWidget Slider(float min, float max, float granularity, float value, string text) => new() {
        MinValue = min, MaxValue = max, Granularity = granularity, Value = Math.Clamp(value, min, max), Text = text,
        Size = new Vector2(230, Touch), VerticalAlignment = WidgetAlignment.Center,
    };
    /// <summary>A framed panel; the mod never paints over the whole screen with an opaque sheet.</summary>
    public static BevelledRectangleWidget Frame() => new() {
        CenterColor = Panel, BevelColor = PanelEdge, BevelSize = 2f, HorizontalAlignment = WidgetAlignment.Stretch,
        VerticalAlignment = WidgetAlignment.Stretch, DirectionalLight = 0f, AmbientLight = 1f,
    };
    /// <summary>One settings row: a label on the left, one control on the right, stacked instead when narrow.</summary>
    public static StackPanelWidget Row(Widget left, Widget right, bool narrow) {
        var row = new StackPanelWidget {
            Direction = narrow ? LayoutDirection.Vertical : LayoutDirection.Horizontal,
            HorizontalAlignment = WidgetAlignment.Stretch, Margin = new Vector2(0, 4),
        };
        left.HorizontalAlignment = narrow ? WidgetAlignment.Near : WidgetAlignment.Stretch;
        left.VerticalAlignment = WidgetAlignment.Center;
        right.HorizontalAlignment = narrow ? WidgetAlignment.Near : WidgetAlignment.Far;
        right.VerticalAlignment = WidgetAlignment.Center;
        row.Children.Add(left);
        row.Children.Add(right);
        return row;
    }
    public static LabelWidget Heading(string text) => new() {
        Text = text, FontScale = 1.05f, Color = Accent, DropShadow = true, Margin = new Vector2(0, 10),
        HorizontalAlignment = WidgetAlignment.Near,
    };
    /// <summary>Wrapping explanatory text under a control. Never used to carry a number the reader needs.</summary>
    public static LabelWidget Note(string text) => new() {
        Text = text, FontScale = .7f, Color = Dim, WordWrap = true, HorizontalAlignment = WidgetAlignment.Stretch,
        Margin = new Vector2(0, 2),
    };
}
