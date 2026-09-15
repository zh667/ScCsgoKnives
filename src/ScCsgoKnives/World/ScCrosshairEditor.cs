using Engine;
namespace Game;

/// <summary>Fixed neighbouring preview, including when the slider group changes.</summary>
public sealed class ScCrosshairEditor : CanvasWidget {
    public readonly StackPanelWidget Controls = new() { Direction = LayoutDirection.Vertical };
    public readonly ScCrosshairPreview Preview = new();
    public ScCrosshairEditor() {
        HorizontalAlignment = WidgetAlignment.Stretch;
        Children.Add(Controls); Children.Add(Preview);
    }
    public override void MeasureOverride(Vector2 available) {
        float w = Math.Max(256, available.X);
        float preview = Math.Min(240, w * .42f), left = w - preview - 12;
        Size = new Vector2(w, 244);
        Controls.DesiredSize = new Vector2(left, 240);
        foreach (var slider in Controls.Children.OfType<SliderWidget>()) slider.Size = new Vector2(left, 48);
        SetWidgetPosition(Controls, Vector2.Zero);
        Preview.Size = new Vector2(preview, 240);
        SetWidgetPosition(Preview, new Vector2(left + 12, 0));
        base.MeasureOverride(available);
    }
}
