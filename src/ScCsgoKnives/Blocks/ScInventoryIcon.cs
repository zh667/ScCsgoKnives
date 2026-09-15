using Engine;
using Engine.Graphics;

namespace Game;

/// <summary>Shared inventory / Recipaedia icon draw. CS2 econ renders are 4:3; a square
/// DrawFlatBlock quad would stretch them. DrawFlatBlock's quad is 0.85 of <see cref="DrawSize"/>
/// inside a 3.6 ortho cube. 1.45 filled that cube the way the old square-padded 128 icons
/// needed; the 256x192 sheets already use almost the full width, so 1.45 put AK/AWP barrels
/// past the slot bevel. 1.28 keeps the widest silhouette inside the recess.</summary>
public static class ScInventoryIcon {
    public const float DrawSize = 1.28f;

    public static void Draw(
        PrimitivesRenderer3D renderer,
        int value,
        float size,
        ref Matrix matrix,
        Texture2D texture,
        Color color,
        DrawBlockEnvironmentData env
    ) {
        if (texture is null) return;
        int longest = Math.Max(texture.Width, texture.Height);
        Matrix iconMatrix = Matrix.CreateScale(texture.Width / (float)longest, texture.Height / (float)longest, 1f) * matrix;
        BlocksManager.DrawFlatBlock(renderer, value, DrawSize * size, ref iconMatrix, texture, color, false, env);
    }
}
