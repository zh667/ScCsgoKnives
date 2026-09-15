namespace Game;

public sealed record ScCrosshairShape(float Width = 2, float Length = 8, float Gap = 3, float Scale = 1, float Dot = 3.2f) {
    public ScCrosshairShape Normalize() => new(Limit(Width, .5f, 8, 2), Limit(Length, 1, 32, 8),
        Limit(Gap, 0, 24, 3), Limit(Scale, .5f, 3, 1), Limit(Dot, 1, 16, 3.2f));
    static float Limit(float value, float min, float max, float fallback) => float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
