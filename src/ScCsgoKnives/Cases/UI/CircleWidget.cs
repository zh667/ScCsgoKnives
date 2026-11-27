using Engine;
using Engine.Graphics;

namespace Game;

public sealed class CircleWidget : Widget {
    const int Segments = 96;

    public Vector2 Size { get; set; }
    public Color FillColor { get; set; } = Color.White;
    public float Depth { get; set; }

    public override void Draw(DrawContext dc) {
        Color color = FillColor * GlobalColorTransform;
        if (color.A == 0) return;

        Matrix transform = GlobalTransform;
        Vector2 localCenter = ActualSize / 2f;
        Vector2.Transform(ref localCenter, ref transform, out Vector2 center);
        FlatBatch2D batch = dc.PrimitivesRenderer2D.FlatBatch(1, DepthStencilState.None);
        float radius = MathUtils.Min(ActualSize.X, ActualSize.Y) / 2f;

        for (int i = 0; i < Segments; i++) {
            float angle1 = 2f * MathF.PI * i / Segments;
            float angle2 = 2f * MathF.PI * (i + 1) / Segments;
            Vector2 point1 = localCenter + radius * new Vector2(MathF.Cos(angle1), MathF.Sin(angle1));
            Vector2 point2 = localCenter + radius * new Vector2(MathF.Cos(angle2), MathF.Sin(angle2));
            Vector2.Transform(ref point1, ref transform, out point1);
            Vector2.Transform(ref point2, ref transform, out point2);
            batch.QueueTriangle(center, point1, point2, Depth, color);
        }
    }

    public override void MeasureOverride(Vector2 parentAvailableSize) {
        IsDrawRequired = true;
        DesiredSize = Size;
    }
}
