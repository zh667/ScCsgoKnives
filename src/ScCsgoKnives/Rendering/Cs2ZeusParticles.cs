using System;
using System.Collections.Generic;
using Engine;
using Engine.Graphics;

namespace Game;

/// <summary>
/// The sprite and spark systems of the Zeus's shot (Cs2TaserEffect), drawn by two
/// passes: the muzzle glow, flare and sparks ride the first-person weapon (view
/// space, after the gun, so the gun cannot hide them), the impact glow and sparks
/// sit in the world at the trace end. Both build the same particles from the same
/// file; only the frame they are drawn in differs.
/// </summary>
public static class Cs2ZeusParticles {
    public sealed class Sprite { public Vector3 Offset; public float Life, Radius, Roll; public Color Tint; }
    public sealed class Spark { public Vector3 Origin, Velocity; public float Life, Born; }

    /// <summary>A spark trail is its velocity over this slice of time; C_OP_RenderTrails' length scale is not read.</summary>
    public const float SparkTrailSeconds = 0.06f;                     // assumed
    /// <summary>The flare's MOD2X blend (overbright 10) has no engine equivalent: drawn additive at this alpha with its radius capped.</summary>
    public const float FlareAlpha = 0.6f;                             // assumed
    public const float FlareRadiusCapInches = 40f;                    // assumed

    static readonly Random s_random = new();
    static readonly Dictionary<string, Texture2D> s_textures = new(StringComparer.Ordinal);

    public static Texture2D Texture(string cs2Name) {
        string baked = Cs2TaserEffect.BakedTexture(cs2Name);
        if (baked is null) {
            KnifeDiagnostics.WarnOnce($"cs2-zeus-texture-{cs2Name ?? "none"}",
                $"No baked texture for the Zeus's {cs2Name ?? "unnamed texture"}; that part is not drawn.");
            return null;
        }
        if (s_textures.TryGetValue(baked, out Texture2D t)) return t;
        try { t = ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/" + baked); }
        catch (Exception e) {
            KnifeDiagnostics.WarnOnce($"cs2-zeus-texture-{baked}", $"Could not load the Zeus texture {baked}: {e.Message}");
            t = null;
        }
        s_textures[baked] = t;
        return t;
    }

    public static Color LerpColor(int[] a, int[] b, float t) =>
        a is { Length: >= 3 } && b is { Length: >= 3 }
            ? new Color((byte)MathUtils.Lerp(a[0], b[0], t), (byte)MathUtils.Lerp(a[1], b[1], t), (byte)MathUtils.Lerp(a[2], b[2], t), (byte)255)
            : Color.White;

    static Vector3 RandomInSphere(float radius) {
        Vector3 d = new(s_random.Float(-1f, 1f), s_random.Float(-1f, 1f), s_random.Float(-1f, 1f));
        float l = d.Length();
        if (l < 1e-4f) return Vector3.Zero;
        return d / l * radius * MathF.Cbrt(s_random.Float(0f, 1f));
    }

    /// <summary>C_OP_InstantaneousEmitter / ContinuousEmitter count of sprites, each within the spawn sphere, with its own life, radius and tint.</summary>
    public static List<Sprite> Sprites(Cs2TaserEffect.Sprites s) {
        var list = new List<Sprite>();
        if (s?.LifeSeconds is not { Length: >= 2 } || s.RadiusInches is not { Length: >= 2 }) return list;
        int n = (int)MathF.Round(s.Count);
        for (int i = 0; i < n; i++) {
            list.Add(new Sprite {
                Offset = RandomInSphere(s.SphereInches * Cs2Placement.InchesToEngine),
                Life = s_random.Float(s.LifeSeconds[0], s.LifeSeconds[1]),
                Radius = s_random.Float(s.RadiusInches[0], s.RadiusInches[1]) * Cs2Placement.InchesToEngine,
                Roll = s_random.Float(0f, MathF.PI * 2f),
                Tint = LerpColor(s.ColorMin, s.ColorMax, s_random.Float(0f, 1f)),
            });
        }
        return list;
    }

    /// <summary>
    /// C_INIT_CreateWithinSphereTransform's local speeds: X along `forward`, Y along
    /// `side`, Z along `up` (CS2's up), in inches/s; spread over the emission window.
    /// </summary>
    public static List<Spark> Sparks(Cs2TaserEffect.Sparks s, Vector3 origin, Vector3 forward, Vector3 side, Vector3 up) {
        var list = new List<Spark>();
        if (s?.SpeedMin is not { Length: >= 3 } || s.SpeedMax is not { Length: >= 3 } || s.LifeSeconds is not { Length: >= 2 }) return list;
        int n = s.Count;
        for (int i = 0; i < n; i++) {
            Vector3 v = forward * s_random.Float(s.SpeedMin[0], s.SpeedMax[0])
                      + side * s_random.Float(s.SpeedMin[1], s.SpeedMax[1])
                      + up * s_random.Float(s.SpeedMin[2], s.SpeedMax[2]);
            list.Add(new Spark {
                Origin = origin, Velocity = v * Cs2Placement.InchesToEngine,
                Life = s_random.Float(s.LifeSeconds[0], s.LifeSeconds[1]),
                Born = n > 1 ? s.EmissionSeconds * i / (n - 1) : 0f,
            });
        }
        return list;
    }

    /// <summary>Camera-facing sprites (C_OP_RenderSprites): radius by the ramp over life, alpha off over the last FadeOut seconds.</summary>
    public static void DrawSprites(PrimitivesRenderer3D renderer, List<Sprite> list, Cs2TaserEffect.Sprites spec, float age,
                                   Vector3 anchor, Vector3 right, Vector3 up, DepthStencilState depth) {
        if (spec is null || list is null || list.Count == 0) return;
        Texture2D texture = Texture(spec.Texture);
        if (texture is null) return;
        TexturedBatch3D batch = renderer.TexturedBatch(texture, useAlphaTest: false, layer: 1,
            depth, RasterizerState.CullNoneScissor, BlendState.Additive, SamplerState.LinearClamp);
        float fadeSeconds = spec.FadeOut?.Seconds ?? 0f;
        foreach (Sprite s in list) {
            if (age >= s.Life) continue;
            float f = age / s.Life;
            float radius = s.Radius * (spec.Radius?.At(f) ?? 1f);
            float alpha = fadeSeconds > 0f && s.Life - age < fadeSeconds ? (s.Life - age) / fadeSeconds : 1f;
            if (spec.Mod2x) {
                alpha *= FlareAlpha;
                radius = MathF.Min(radius, FlareRadiusCapInches * Cs2Placement.InchesToEngine);
            }
            Vector3 c = anchor + s.Offset;
            (float sin, float cos) = MathF.SinCos(s.Roll);
            Vector3 r = (right * cos + up * sin) * radius, u = (up * cos - right * sin) * radius;
            Color col = new(s.Tint.R, s.Tint.G, s.Tint.B, (byte)MathUtils.Clamp(255f * alpha, 0f, 255f));
            batch.QueueTriangle(c - r - u, c - r + u, c + r + u, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f), col);
            batch.QueueTriangle(c - r - u, c + r + u, c + r - u, new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(1f, 1f), col);
        }
    }

    /// <summary>
    /// Spark trails (C_OP_RenderTrails): each flies its velocity under CS2's gravity
    /// (along `gravityAxis`, the frame's up), fading in and out as the file says.
    /// </summary>
    public static void DrawSparks(PrimitivesRenderer3D renderer, List<Spark> list, Cs2TaserEffect.Sparks spec, float age,
                                  Vector3 eye, Vector3 gravityAxis, DepthStencilState depth) {
        if (spec is null || list is null || list.Count == 0) return;
        Texture2D texture = Texture(spec.Texture);
        if (texture is null) return;
        TexturedBatch3D batch = renderer.TexturedBatch(texture, useAlphaTest: false, layer: 0,
            depth, RasterizerState.CullNoneScissor, BlendState.Additive, SamplerState.LinearClamp);
        float g = spec.Movement?.GravityMetres ?? 0f;
        float half = (spec.RadiusInches is { Length: >= 1 } ? spec.RadiusInches[0] : 1f) * (spec.RadiusScale ?? 1f) * Cs2Placement.InchesToEngine;
        float fadeIn = spec.FadeIn?.Seconds ?? 0f, fadeOut = spec.FadeOut?.Seconds ?? 0f;
        Color tint = spec.Color is { Length: >= 3 } ? new Color((byte)spec.Color[0], (byte)spec.Color[1], (byte)spec.Color[2], (byte)255) : Color.White;
        float maxLength = (spec.MaxLengthInches ?? 1e6f) * Cs2Placement.InchesToEngine;
        foreach (Spark s in list) {
            float t = age - s.Born;
            if (t < 0f || t >= s.Life) continue;
            Vector3 p = s.Origin + s.Velocity * t + gravityAxis * (0.5f * g * t * t);
            Vector3 v = s.Velocity + gravityAxis * (g * t);
            float speed = v.Length();
            if (speed < 1e-5f) continue;
            float trail = MathF.Min(speed * SparkTrailSeconds, maxLength);
            Vector3 dir = v / speed;
            Vector3 tail = p - dir * trail;
            float alpha = 1f;
            if (fadeIn > 0f && t < fadeIn) alpha = t / fadeIn;
            if (fadeOut > 0f && s.Life - t < fadeOut) alpha = MathF.Min(alpha, (s.Life - t) / fadeOut);
            Vector3 side = Vector3.Cross(dir, p - eye);
            float sl = side.Length();
            if (!float.IsFinite(sl) || sl < 1e-6f) continue;
            side *= half / sl;
            Color col = new(tint.R, tint.G, tint.B, (byte)MathUtils.Clamp(255f * alpha, 0f, 255f));
            batch.QueueTriangle(tail - side, tail + side, p + side, new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f), col);
            batch.QueueTriangle(tail - side, p + side, p - side, new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(1f, 1f), col);
        }
    }
}
