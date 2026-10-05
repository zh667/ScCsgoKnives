using Engine;

namespace Game;

/// <summary>
/// The line a CS2 sniper shot leaves hanging in the air (round 9, 2026-10-01, user: "一些枪开枪之后会留下明显的枪线（比如awp）").
/// In CS2 it is a child of the gun's tracer system, separate from the moving streak: weapon_tracers_rifle → _rifle_wisp (AWP),
/// weapon_tracers_rifle_ssg → _rifle_wisp_ssg (SSG 08 and G3SG1), weapon_tracers_rifle_scar → _rifle_wisp_scar (SCAR-20); no
/// other gun has one (all 380 exported .vpcf searched). Numbers below are read from those .vpcf files (CS2 units are inches,
/// converted here); docs/tasks/round9-tp-aim-cs2-tracer-hud-20261001.md lists them with what is approximated.
///
/// The whole line from muzzle to impact appears at the shot (C_INIT_CreateSequentialPathV2, CP0 → CP1), 8..36 points by the
/// shot's length, lives 2 s, glows at first (self-illumination) and turns into grey smoke that drifts and curls
/// (BasicMovement, TurbulenceForce, local velocity/acceleration), held near its two ends (DampenToCP). Two ropes draw it: a
/// thin bright core (beam_energy_01) and a wide soft smoke band (beam_smoke_01).
/// </summary>
public sealed class Cs2WispSpec {
    public string System { get; init; }
    /// <summary>C_INIT_InitFloat radius, inches.</summary>
    public float RadiusInches { get; init; }
    public float AlphaMin { get; init; }
    public float AlphaMax { get; init; }
    /// <summary>Alpha over normalised life, (t, value) pairs.</summary>
    public float[] LifeAlpha { get; init; }
    /// <summary>Point count: from 8 above this distance control value up to <see cref="MaxPoints"/> at 1.</summary>
    public float PointKnee { get; init; }
    public int MaxPoints { get; init; }
    public float CoreAlpha { get; init; } = 1;
    /// <summary>Smoke rope alpha scale from 0.23 (short shots) to 1 (long), else 1.</summary>
    public bool SmokeAlphaByDistance { get; init; }
    /// <summary>TurbulenceForce noise amounts (in/s², two layers) and coordinate scales.</summary>
    public Vector3 Turbulence0 { get; init; } = new(20, 20, 20);
    public Vector3 Turbulence1 { get; init; } = new(150, 150, 50);
    /// <summary>RenderRopes m_flStartFadeDot / m_flEndFadeDot (core rope, smoke rope): a rope seen this nearly end-on fades
    /// out; (1, 1) when the file sets none.</summary>
    public Vector2 CoreFadeDot { get; init; } = Vector2.One;
    public Vector2 SmokeFadeDot { get; init; } = Vector2.One;
    public float NoiseScale0 { get; init; } = 3;
    public float NoiseScale1 { get; init; } = 1;
}

public static class Cs2Wisp {
    public const float Inch = .0254f;
    public const float Lifetime = 2f;
    /// <summary>m_flMaxDrawDistance 1000 in.</summary>
    public const float MaxDrawDistance = 1000 * Inch;
    /// <summary>DampenToCP on CP0 and CP1: within 500 in motion is scaled down to 0.2.</summary>
    public const float DampenRange = 500 * Inch, DampenScale = .2f;

    public static readonly Cs2WispSpec Awp = new() {
        System = "weapon_tracers_rifle_wisp", RadiusInches = 3, AlphaMin = .1f, AlphaMax = .5f, PointKnee = .185f, MaxPoints = 36,
        LifeAlpha = [0, 0, .04f, .86f, .33f, .79f, .54f, .19f, 1, 0], CoreFadeDot = new(.995f, 1),
    };
    public static readonly Cs2WispSpec Ssg = new() {
        System = "weapon_tracers_rifle_wisp_ssg", RadiusInches = 3, AlphaMin = .1f, AlphaMax = .5f, PointKnee = .18f, MaxPoints = 36,
        // The file also fades the smoke rope from 0.9 (≈ 26° from the camera axis); with it the SSG 08 / G3SG1 line stayed hard
        // to see from the usual third-person views while the SCAR-20's (no fade) was clear (user, round 11): the smoke rope
        // draws without that fade, as the AWP's and SCAR-20's do; the core keeps it.
        LifeAlpha = [0, 0, .045f, 1, .28f, .82f, .44f, .2f, 1, 0], SmokeAlphaByDistance = true, CoreFadeDot = new(.9f, 1),
    };
    public static readonly Cs2WispSpec Scar = new() {
        System = "weapon_tracers_rifle_wisp_scar", RadiusInches = 1, AlphaMin = .6f, AlphaMax = 1, PointKnee = .18f, MaxPoints = 30,
        LifeAlpha = [0, 0, .05f, .92f, .27f, .26f, 1, 0], CoreAlpha = .35f, SmokeAlphaByDistance = true, NoiseScale0 = 50, NoiseScale1 = 2,
    };
    /// <summary>The wisp a gun's tracer carries in CS2, or null.</summary>
    public static Cs2WispSpec For(string gun) => gun switch {
        "awp" => Awp,
        "ssg08" or "g3sg1" => Ssg,   // the G3SG1 uses the SSG 08's tracer system (its vdata), not the SCAR-20's
        "scar20" => Scar,
        _ => null,
    };

    /// <summary>C_OP_DistanceBetweenCPsToCP: the shot's length 128..2000 in mapped to 0.15..1.</summary>
    public static float DistanceControl(float metres) => MathUtils.Lerp(.15f, 1f, MathUtils.Saturate((metres / Inch - 128) / (2000 - 128)));
    public static int PointCount(Cs2WispSpec spec, float metres) =>
        (int)MathF.Round(MathUtils.Lerp(8, spec.MaxPoints, MathUtils.Saturate((DistanceControl(metres) - spec.PointKnee) / (1 - spec.PointKnee))));
    /// <summary>Source's Bias(x, b) = x^(ln b / ln 0.5).</summary>
    public static float Bias(float x, float b) => MathF.Pow(MathUtils.Saturate(x), MathF.Log(b) / MathF.Log(.5f));
    /// <summary>InterpolateRadius ×3 → ×0.5 over 0..7.5 % of life, then ×0.5 → ×5 to the end, both biased 0.45.</summary>
    public static float RadiusScale(float life) => life < .075f
        ? MathUtils.Lerp(3f, .5f, Bias(life / .075f, .45f))
        : MathUtils.Lerp(.5f, 5f, Bias((life - .075f) / .925f, .45f));
    /// <summary>Piecewise linear over (t, value) pairs.</summary>
    public static float Curve(float[] pairs, float t) {
        if (t <= pairs[0]) return pairs[1];
        for (int i = 2; i < pairs.Length; i += 2)
            if (t <= pairs[i]) return MathUtils.Lerp(pairs[i - 1], pairs[i + 1], (t - pairs[i - 2]) / MathF.Max(pairs[i] - pairs[i - 2], 1e-6f));
        return pairs[^1];
    }
    /// <summary>Self-illumination over normalised age (particle attribute 26): 2.0 → 1.79 at 0.18 → 0.89 at 0.31 → 0.11 at
    /// 0.41 → 0; the line glows at first, then is lit like smoke.</summary>
    static readonly float[] s_glow = [0, 2f, .18f, 1.79f, .31f, .89f, .41f, .11f, .5f, 0];
    public static float Glow(float life) => Curve(s_glow, life);
    /// <summary>The age curve of the turbulence strength (system age, seconds).</summary>
    static readonly float[] s_turbulence = [0, .35f, .2f, 1.87f, 2f, .59f, 4f, .35f];
    public static float TurbulenceStrength(float age) => Curve(s_turbulence, age);
    /// <summary>Alpha along the rope: 0 at both ends, ≈ 0.95 in the middle (<paramref name="i"/> may fall between points).</summary>
    public static float Taper(float i, int n) {
        if (n < 2) return 0;
        float f = MathUtils.Saturate(i / (n - 1));
        return .95f * MathUtils.Saturate(MathF.Min(f, 1 - f) / .15f);
    }
    /// <summary>The rope as drawn: each span between two points cut into pieces of at most <paramref name="step"/> metres along a
    /// Catmull-Rom curve through the points (the points sit up to 7 m apart on a long shot; drawn straight, a span changed
    /// width and fade at once and showed as an angular blob). Returns positions and their fractional point index.</summary>
    public static int Joints(Vector3[] points, float step, Vector3[] positions, float[] index) {
        int n = points.Length, count = 0;
        for (int i = 0; i < n - 1 && count < positions.Length; i++) {
            Vector3 p0 = points[Math.Max(i - 1, 0)], p1 = points[i], p2 = points[i + 1], p3 = points[Math.Min(i + 2, n - 1)];
            int pieces = Math.Clamp((int)MathF.Ceiling(Vector3.Distance(p1, p2) / step), 1, 12);
            for (int k = 0; k < pieces && count < positions.Length; k++) {
                float t = k / (float)pieces;
                positions[count] = Vector3.CatmullRom(p0, p1, p2, p3, t); index[count] = i + t; count++;
            }
        }
        if (count < positions.Length) { positions[count] = points[n - 1]; index[count] = n - 1; count++; }
        return count;
    }
    /// <summary>Colour: born orange (255,156,85), grey (203..227, 219..224, 224..210) by 15 % of life.</summary>
    public static Vector3 Colour(float life, float pick) {
        var grey = Vector3.Lerp(new Vector3(203, 219, 224), new Vector3(227, 224, 210), pick) / 255f;
        return Vector3.Lerp(new Vector3(255, 156, 85) / 255f, grey, MathUtils.Saturate(life / .15f));
    }
    public static float SmokeAlphaScale(Cs2WispSpec spec, float metres) =>
        spec.SmokeAlphaByDistance ? MathUtils.Lerp(.23f, 1f, MathUtils.Saturate((DistanceControl(metres) - .15f) / .85f)) : 1;
}

/// <summary>One lingering line in flight.</summary>
public sealed class Cs2WispTrail {
    public readonly Cs2WispSpec Spec;
    public readonly Vector3 Start, End;
    public readonly float Length, Alpha, ColourPick, SmokeRepeat;
    public readonly Vector3[] Points, Velocities, Forces, Phases;
    public float Age;
    public bool Dead => Age >= Cs2Wisp.Lifetime;

    public Cs2WispTrail(Cs2WispSpec spec, Vector3 start, Vector3 end, Random random) {
        Spec = spec; Start = start; End = end; Length = Vector3.Distance(start, end);
        int n = Math.Max(2, Cs2Wisp.PointCount(spec, Length));
        Points = new Vector3[n]; Velocities = new Vector3[n]; Forces = new Vector3[n]; Phases = new Vector3[n];
        Alpha = random.Float(spec.AlphaMin, spec.AlphaMax); ColourPick = random.Float(0, 1);
        SmokeRepeat = random.Float(350, 750) * Cs2Wisp.Inch;
        Vector3 forward = Length > 1e-4f ? (end - start) / Length : -Vector3.UnitZ;
        Vector3 left = Vector3.Cross(Vector3.UnitY, forward);
        left = left.LengthSquared() > 1e-6f ? Vector3.Normalize(left) : Vector3.UnitX;
        // Initial local velocity 75 / 50 / 0 in/s in the emitter's frame (X along the shot, Y to its left).
        Vector3 v0 = (forward * 75 + left * 50) * Cs2Wisp.Inch;
        m_localAcceleration = left * (-150 * Cs2Wisp.Inch);
        for (int i = 0; i < n; i++) {
            Points[i] = Vector3.Lerp(start, end, i / (float)(n - 1));
            Velocities[i] = v0;
            // PerParticleForce: a random vector × −10 in/s² for each point.
            Forces[i] = Vector3.Normalize(new Vector3(random.Float(-1, 1), random.Float(-1, 1), random.Float(-1, 1)) + new Vector3(1e-3f)) * (-10 * Cs2Wisp.Inch);
            Phases[i] = new Vector3(random.Float(0, 6.283f), random.Float(0, 6.283f), random.Float(0, 6.283f));
        }
    }
    readonly Vector3 m_localAcceleration;

    /// <summary>Gravity −30 in/s², drag 0.3, turbulence and the per-point force; motion scaled down to 0.2 near the muzzle and
    /// the impact. Turbulence is smooth per-point noise (Source's Perlin field is not reproduced bit for bit).</summary>
    public void Update(float dt) {
        if (dt <= 0) return;
        Age += dt;
        float strength = Cs2Wisp.TurbulenceStrength(Age) * Cs2Wisp.Inch;
        // m_fDrag 0.3 read as the velocity lost per 1/30 s step (the unit is not stated in the file).
        float keep = MathF.Pow(1 - .3f, dt * 30);
        for (int i = 0; i < Points.Length; i++) {
            Vector3 p = Points[i], ph = Phases[i];
            float s0 = Spec.NoiseScale0 * Cs2Wisp.Inch * 4, s1 = Spec.NoiseScale1 * Cs2Wisp.Inch * 4;
            Vector3 noise0 = new(MathF.Sin(p.Y * s0 + Age * 3.1f + ph.X), MathF.Sin(p.Z * s0 + Age * 2.7f + ph.Y), MathF.Sin(p.X * s0 + Age * 3.4f + ph.Z));
            Vector3 noise1 = new(MathF.Sin(p.Z * s1 + Age * 1.3f + ph.Y), MathF.Sin(p.X * s1 + Age * 1.1f + ph.Z), MathF.Sin(p.Y * s1 + Age * 1.7f + ph.X));
            Vector3 turbulence = (Spec.Turbulence0 * noise0 + Spec.Turbulence1 * noise1) * strength;
            Vector3 a = turbulence + Forces[i] + m_localAcceleration - Vector3.UnitY * (30 * Cs2Wisp.Inch);
            Velocities[i] = (Velocities[i] + a * dt) * keep;
            float near = MathF.Min(Vector3.Distance(p, Start), Vector3.Distance(p, End));
            float damp = MathUtils.Lerp(Cs2Wisp.DampenScale, 1f, MathUtils.Saturate(near / Cs2Wisp.DampenRange));
            Points[i] = p + Velocities[i] * (dt * damp);
        }
    }
}
