using Engine;

namespace Game;

/// <summary>
/// The arithmetic behind the CS2 tracer ribbon, kept out of the draw call so the
/// headless tools can measure exactly what the renderer draws rather than a copy of
/// it. Everything here comes from the tracer .vpcf; see Cs2Effects.Tracer for which
/// operator each number is read from.
/// </summary>
public static class Cs2Tracer {
    /// <summary>
    /// The world length that spans one unit of viewport height at a given forward
    /// depth, for a projection whose vertical scale factor is projY.
    ///
    /// A view-space point at depth d and height h projects to ndc = h * projY / d, and
    /// the viewport spans ndc -1..1, so a fraction f of its height is 2 * f * d / projY.
    /// </summary>
    public static float MetresPerScreenHeight(float depth, float projY) =>
        projY > 1e-4f ? 2f * MathF.Max(depth, 0.02f) / projY : 0f;

    /// <summary>
    /// One pass's drawn half-width at a point, and the fade that goes with it.
    ///
    /// m_flRadiusScale times the particle radius gives the half-width in world units;
    /// m_flMinSize and m_flMaxSize then clamp it to a band of the viewport height, so a
    /// trail cannot be thinner than a pixel far away or wider than a few pixels close
    /// up. m_flStartFadeSize / m_flEndFadeSize fade the trail out on the *unclamped*
    /// size, which is how the AWP's trail vanishes instead of filling the screen when
    /// it passes the camera.
    /// </summary>
    public static float HalfWidth(Cs2Effects.Tracer spec, Cs2Effects.TracerPass pass,
                                  float depth, float projY, out float sizeFade) {
        sizeFade = 1f;
        float perFraction = MetresPerScreenHeight(depth, projY);
        if (perFraction <= 0f) return 0f;
        float world = spec.HalfWidthMetres(pass);
        if (pass.EndFadeSize > pass.StartFadeSize) {
            float onScreen = world / perFraction;
            sizeFade = 1f - MathUtils.Saturate((onScreen - pass.StartFadeSize)
                                               / (pass.EndFadeSize - pass.StartFadeSize));
        }
        return MathUtils.Clamp(world, pass.MinSize * perFraction, pass.MaxSize * perFraction);
    }

    /// <summary>The same half-width as a fraction of the viewport height.</summary>
    public static float HalfWidthScreenFraction(Cs2Effects.Tracer spec, Cs2Effects.TracerPass pass,
                                                float depth, float projY, out float sizeFade) {
        float perFraction = MetresPerScreenHeight(depth, projY);
        float half = HalfWidth(spec, pass, depth, projY, out sizeFade);
        return perFraction > 0f ? half / perFraction : 0f;
    }

    /// <summary>
    /// How far behind the head the trail is drawn, at a given age and distance from the
    /// viewer: m_flLengthFadeInTime ramps it up from nothing, C_OP_DistanceToTransform
    /// scales it with distance, and m_flMaxLength caps it.
    /// </summary>
    public static float TrailMetres(Cs2Effects.Tracer spec, Cs2Effects.TracerPass pass,
                                    float age, float metresFromViewer) {
        float trail = spec.MetresPerSecond * spec.TrailSecondsMid * spec.LengthScale(metresFromViewer);
        trail = MathUtils.Min(trail, spec.TrailMetres);
        return trail * MathUtils.Saturate(age / MathUtils.Max(pass.LengthFadeIn ?? 0.08f, 1e-3f));
    }

    /// <summary>
    /// A view-space point rescaled so it lands on the same pixel under a projection of
    /// a different field of view. Both projections take the same view space, so equal
    /// pixels means equal x * projX / -z, and the ratio of the two scale factors is the
    /// whole correction. This is what moves the tracer's origin from the eye onto the
    /// muzzle the player can see.
    /// </summary>
    public static Vector3 ReprojectView(Vector3 view, float fovRatio) =>
        new(view.X * fovRatio, view.Y * fovRatio, view.Z);

    /// <summary>Pixel position of a view-space point, for a viewport of this size.</summary>
    public static Vector2 ToPixels(Vector3 view, float projX, float projY, int width, int height) {
        float w = MathF.Max(-view.Z, 1e-4f);
        float ndcX = view.X * projX / w, ndcY = view.Y * projY / w;
        return new Vector2((ndcX * 0.5f + 0.5f) * width, (0.5f - ndcY * 0.5f) * height);
    }
}
