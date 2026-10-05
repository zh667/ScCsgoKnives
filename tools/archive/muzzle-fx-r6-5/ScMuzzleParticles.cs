using Engine;
using Engine.Graphics;
using static Game.Cs2MuzzleFx;
namespace Game;

/// <summary>
/// CS2's muzzle particles in the world (round 6 of the deathmatch work, 2026-10-05): every shot plays its gun's CS2 muzzle system
/// (Cs2MuzzleFx) at the muzzle - the one this process draws for the shooter, first person or third - in every world, by running
/// each system's own program: the random choice of children, the burst, CS2's initializers and operators in order on CS2's
/// particle attributes (3 radius, 7 alpha, 18 scratch, 38 manual animation frame, ...) with its float inputs - random ranges,
/// particle number, age, attributes, control points, CS2's Hermite curves - and its sprite and trail renderers. Control points:
/// 0 is the muzzle (x along the shot, z up); 1, 3 and 5 take the values of the root system's own configuration for the view
/// (ControlPoints), with the global scale (5) 1 where a configuration leaves it out. Velocities come only from the velocity initializers (an initializer that only moves a particle
/// gives it no speed; "previous position = position" stops it). Particles locked to the muzzle (C_OP_PositionLock) follow the
/// drawn muzzle. Add is drawn additively with the overbright folded into alpha, lighten as a screen blend, alpha as alpha
/// blending, lighten additively at half strength (the overbright's strength enters as its square root: Encode); diffuse renderers
/// take the block light round the muzzle. CS2's bloom-only passes are not drawn: this game has no bloom. Each system's "Unmodelled" list says what is not drawn
/// (shadows, motion-vector frame blending, ground placement). The muzzle's short light lights the smoke and
/// the other diffusely lit particles (LightStrength), not the blocks, the gun or the hands.
/// </summary>
public sealed class ScMuzzleParticles {
    public const float Metres = .0254f;
    public const int MaxLive = 3000;
    const int Fields = 40;
    /// <summary>Where the muzzle is now (false: unknown this frame - the particles keep their place).</summary>
    public delegate bool Anchor(out Vector3 position, out Vector3 forward);
    sealed class P {
        public readonly float[] F = new float[Fields], F0 = new float[Fields];
        public Vector3 Position, Velocity, Color, Shown, Normal, Scratch;
        public float Age, AlphaMul = 1, RadiusMul = 1;
        public int Index; public uint Seed; public bool Dead;
    }
    sealed class Sys {
        public SystemSpec D; public Vector3[] Cp; public Vector3 O, X, Y, Z;
        public float Delay, Age, EmitAt; public int Emitted, Total = -1;
        public readonly List<P> Live = [];
        public Anchor Anchor; public Camera Camera; public Vector3 Inherit; public uint Seed;
        public float ViewRatio;   // first person: the viewmodel's projection over the camera's (0: an ordinary world system)
        public float Unit = Metres;   // metres per CS2 inch: real size in the world; the drawn viewmodel's own scale in first person
    }
    readonly List<Sys> m_systems = [];
    readonly Random m_random = new();
    readonly Dictionary<string, Texture2D> m_textures = [];
    PrimitivesRenderer3D m_renderer;
    int m_live;
    /// <summary>The light (0..1) at a point, for the renderers CS2 lights diffusely (the smoke).</summary>
    public Func<Vector3, float> LightAt;
    public int Count => m_live;
    /// <summary>Shots played and the most particles alive at once (diagnostics: tools/MpM0/sp_gun_visuals.py reads them).</summary>
    public int Shots, Peak;
    /// <summary>The viewmodel's metres per CS2 inch of the last first-person shot (diagnostics).</summary>
    public float LastUnit;

    /// <summary>One shot of <paramref name="gun"/> at <paramref name="muzzle"/> along <paramref name="forward"/>, drawn for
    /// <paramref name="camera"/> only (null: every camera); <paramref name="anchor"/> moves the particles CS2 locks to the muzzle.
    /// <paramref name="viewRatio"/> &gt; 0: a first-person shot, simulated from the viewmodel's muzzle before its field of view is
    /// applied (CsmcFirstPersonRenderer.TryGetPlayerViewModelMuzzle) and drawn in the viewmodel's projection, as CS2 draws them.</summary>
    /// <paramref name="unit"/>: metres per CS2 inch where the particles are simulated - the drawn viewmodel's own scale in first
    /// person, so the flash keeps its size against the gun (0: real size, Metres).
    public void Shot(string gun, bool silenced, Vector3 muzzle, Vector3 forward, bool firstPerson, Anchor anchor = null, Camera camera = null, Vector3 inherit = default, float viewRatio = 0, float unit = 0) {
        var root = Cs2MuzzleFx.System(RootFor(gun, silenced));
        if (root is null || !(forward.LengthSquared() > 1e-8f)) return;
        Basis(forward, out var x, out var y, out var z);
        Shots++;
        if (unit > 0) LastUnit = unit;
        Play(root, 0, muzzle, x, y, z, ControlPoints(root, gun, firstPerson), anchor, camera, inherit, viewRatio, unit > 0 ? unit : Metres, 0);
    }

    // Control points 1 and 3 are set by CS2's code, not its files. Their values here, read from what the systems do with them
    // and checked against CS2's slow-motion frames (fxc-fxh, 2026-10-05):
    // - 1 is 1 in both views. The pump guns' and machine guns' flashes scale and multiply with it, and CS2 shows them large in
    //   first person; the sniper flashes shrink with it, and CS2 shows the AWP's and the SCAR-20's compact. The roots' named
    //   configurations that set it to 0 (fps_view, ssg08, scar) are the effect editor's previews: taken as 0, they leave the
    //   Nova no first-person flash and wrap the SCAR-20's screen in fire.
    // - 3 is how first-person the effect is: 1 locks the Desert Eagle's flash to the gun and makes the SMGs' flash half as big,
    //   the rifles' smoke a quarter as opaque and half as long-lived (CS2's first person shows next to no muzzle smoke); 0 in
    //   third person. The pistols' and pump guns' own configurations give 0.75 (fps_view) and 0.5 (thirdperson).
    // - 5 (global scale) is 1.
    static Vector3[] ControlPoints(SystemSpec root, string gun, bool firstPerson) {
        var cp = new Vector3[8];
        root.Configs.TryGetValue(firstPerson ? "fps_view" : "thirdperson", out var view);
        cp[1] = Vector3.UnitX;
        cp[3] = view is not null && view.TryGetValue(3, out var v3) && v3.X > 0 && v3.X <= 1 ? v3 : new Vector3(firstPerson ? 1 : 0, 0, 0);
        cp[5] = Vector3.One;
        return cp;
    }

    static void Basis(Vector3 forward, out Vector3 x, out Vector3 y, out Vector3 z) {
        x = Vector3.Normalize(forward);
        Vector3 up = MathF.Abs(x.Y) > .99f ? Vector3.UnitZ : Vector3.UnitY;
        z = Vector3.Normalize(up - x * Vector3.Dot(up, x)); y = Vector3.Cross(z, x);
    }

    void Play(SystemSpec s, float delay, Vector3 o, Vector3 x, Vector3 y, Vector3 z, Vector3[] cp, Anchor anchor, Camera camera, Vector3 inherit, float viewRatio, float unit, int depth) {
        if (s is null || s.Missing is not null || depth > 4) return;
        if (s.Emits) {
            var sys = new Sys { D = s, Cp = cp, O = o, X = x, Y = y, Z = z, Delay = delay, Anchor = anchor, Camera = camera, Inherit = inherit, ViewRatio = viewRatio, Unit = unit, Seed = (uint)m_random.Int() };
            m_systems.Add(sys);
            if (delay <= 0) Advance(sys, 0);   // the burst is drawn the frame the shot is fired
        }
        if (s.Children.Count == 0) return;
        // C_OP_ChooseRandomChildrenInGroup: a group with a choice plays that many of its children, picked at random; every other child plays
        var chosen = new HashSet<Child>();
        foreach (var (group, count) in s.Choose) {
            var pool = s.Children.Where(c => c.Group == group).ToList();
            for (int i = 0; i < count && pool.Count > 0; i++) { int k = m_random.Int(0, pool.Count - 1); chosen.Add(pool[k]); pool.RemoveAt(k); }
        }
        var choosing = s.Choose.Select(c => c.Group).ToHashSet();
        foreach (var c in s.Children)
            if (!choosing.Contains(c.Group) || chosen.Contains(c)) Play(Cs2MuzzleFx.System(c.System), delay + c.Delay, o, x, y, z, cp, anchor, camera, inherit, viewRatio, unit, depth + 1);
    }

    // ---- inputs ----
    static float Hash(uint seed, int salt) {
        uint h = seed ^ (uint)salt * 0x9E3779B1u;
        h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
        return (h >> 8) * (1f / 16777216f);
    }
    static float H(P p, int salt) => Hash(p.Seed, salt);
    static float Lerp(float a, float b, float t) => a + (b - a) * t;
    static float Saturate(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    static float Window(float v, float a, float b) => MathF.Abs(b - a) < 1e-6f ? (v >= b ? 1 : 0) : Saturate((v - a) / (b - a));
    static float Remap(float v, float i0, float i1, float o0, float o1, bool clamp) {
        if (MathF.Abs(i1 - i0) < 1e-6f) return v >= i1 ? o1 : o0;
        float t = (v - i0) / (i1 - i0); if (clamp) t = Saturate(t);
        return o0 + (o1 - o0) * t;
    }
    static float Spline(float x) => x * x * (3 - 2 * x);
    static float SourceBias(float x, float b) => b is > 0 and < 1 && MathF.Abs(b - .5f) > 1e-4f ? MathF.Pow(Saturate(x), MathF.Log(b) / MathF.Log(.5f)) : x;
    static float BiasOf(float t, string type, float parameter) => type switch {
        null => t,
        "PF_BIAS_TYPE_EXPONENTIAL" => MathF.Pow(Saturate(t), MathF.Pow(2, -parameter * 4)),
        _ => SourceBias(t, Math.Clamp(.5f + parameter * .5f, .01f, .99f)),
    };
    static float Curve(FloatIn f, float x) {
        var X = f.CurveX; var Y = f.CurveY; int n = X?.Length ?? 0;
        if (n == 0) return f.Literal;
        if (x <= X[0]) return Y[0];
        if (x >= X[n - 1]) return Y[n - 1];
        int i = 0; while (i < n - 2 && x > X[i + 1]) i++;
        float dx = X[i + 1] - X[i]; if (dx <= 1e-6f) return Y[i + 1];
        float t = (x - X[i]) / dx, t2 = t * t, t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * Y[i] + (t3 - 2 * t2 + t) * dx * f.SlopeOut[i] + (-2 * t3 + 3 * t2) * Y[i + 1] + (t3 - t2) * dx * f.SlopeIn[i + 1];
    }
    static float Component(Vector3 v, int c) => c == 0 ? v.X : c == 1 ? v.Y : v.Z;
    static float Eval(FloatIn f, Sys s, P p, int salt) {
        if (f is null) return 0;
        if (f.IsConstant) return f.Constant;
        if (f.Source == "rand") { float t = Hash(p?.Seed ?? s.Seed, salt); return f.A + (f.B - f.A) * BiasOf(t, f.BiasType, f.BiasParameter); }
        float v = f.Source switch {
            "number" => p?.Index ?? 0,
            "numberNormalized" => p is null ? 0 : p.Index / (float)Math.Max(1, s.D.Max - 1),
            "age" => p?.Age ?? 0,
            "ageNormalized" => p is null ? 0 : p.Age / MathF.Max(1e-4f, p.F[1]),
            "attribute" => p?.F[Math.Clamp(f.Attribute, 0, Fields - 1)] ?? 0,
            "cp" => Component(s.Cp[Math.Clamp(f.Cp, 0, 7)], f.Component),
            "systemAge" => s.Age,
            "detail" => 3,
            _ => f.Literal,
        };
        return f.Map switch {
            "mult" => v * f.Mult,
            "remap" => Remap(v, f.In0, f.In1, f.Out0, f.Out1, f.Clamp),
            "remapBiased" => f.Out0 + (f.Out1 - f.Out0) * BiasOf(Window(v, f.In0, f.In1), f.BiasType, f.BiasParameter),
            "curve" => Curve(f, v),
            _ => v,
        };
    }
    static Vector3 EvalV(VecIn v, Sys s, P p, int salt) {
        if (v is null) return Vector3.Zero;
        switch (v.Source) {
            case "pvec": return v.Attribute switch { 0 => p?.Position ?? s.O, 6 => p?.Color ?? Vector3.One, 17 => p?.Scratch ?? Vector3.Zero, 21 => p?.Normal ?? Vector3.Zero, _ => Vector3.Zero } * v.Scale;
            case "interp": return Vector3.Lerp(v.Out0, v.Out1, Window(Eval(v.F, s, p, salt), v.In0, v.In1));
            case "comps": return new Vector3(Eval(v.X, s, p, salt), Eval(v.Y, s, p, salt + 1), Eval(v.Z, s, p, salt + 2));
            default: return v.Constant;
        }
    }
    static Vector3 Between(Vector3 a, Vector3 b, P p, int salt) => new(Lerp(a.X, b.X, H(p, salt)), Lerp(a.Y, b.Y, H(p, salt + 1)), Lerp(a.Z, b.Z, H(p, salt + 2)));
    static Vector3 UnitSphere(P p, int salt) {
        float z = H(p, salt) * 2 - 1, a = H(p, salt + 1) * MathF.PI * 2, r = MathF.Sqrt(MathF.Max(0, 1 - z * z));
        return new Vector3(r * MathF.Cos(a), r * MathF.Sin(a), z) * MathF.Cbrt(MathF.Max(1e-4f, H(p, salt + 2)));
    }
    static Vector3 Local(Sys s, Vector3 v) => s.X * v.X + s.Y * v.Y + s.Z * v.Z;
    static Vector3 ToLocal(Sys s, Vector3 w) => new(Vector3.Dot(w, s.X), Vector3.Dot(w, s.Y), Vector3.Dot(w, s.Z));
    // Source's AngleMatrix (pitch about y - positive pitches x down -, yaw about z, roll about x) applied to a local vector
    static Vector3 Angles(Vector3 v, float pitch, float yaw, float roll) {
        float sp = MathF.Sin(MathUtils.DegToRad(pitch)), cp = MathF.Cos(MathUtils.DegToRad(pitch)), sy = MathF.Sin(MathUtils.DegToRad(yaw)), cy = MathF.Cos(MathUtils.DegToRad(yaw)),
            sr = MathF.Sin(MathUtils.DegToRad(roll)), cr = MathF.Cos(MathUtils.DegToRad(roll));
        var forward = new Vector3(cp * cy, cp * sy, -sp);
        var left = new Vector3(sr * sp * cy - cr * sy, sr * sp * sy + cr * cy, sr * cp);
        var up = new Vector3(cr * sp * cy + sr * sy, cr * sp * sy - sr * cy, cr * cp);
        return forward * v.X + left * v.Y + up * v.Z;
    }
    static void SetFloat(P p, int field, SetMode set, float value, bool initializing) {
        if (field is < 0 or >= Fields) return;
        ref float f = ref p.F[field];
        f = set switch {
            SetMode.ScaleInitial => (initializing ? f : p.F0[field]) * value,
            SetMode.ScaleCurrent => f * value,
            SetMode.AddInitial => (initializing ? f : p.F0[field]) + value,
            SetMode.AddCurrent => f + value,
            _ => value,
        };
    }
    static void SetVector(P p, int field, Vector3 v, SetMode set) {
        static Vector3 Apply(Vector3 old, Vector3 v, SetMode set) => set switch { SetMode.ScaleInitial or SetMode.ScaleCurrent => old * v, SetMode.AddInitial or SetMode.AddCurrent => old + v, _ => v };
        switch (field) {
            case 2: p.Velocity = Vector3.Zero; break;   // the previous position put on the particle: it stands still
            case 6: p.Color = Apply(p.Color, v, set); break;
            case 17: p.Scratch = Apply(p.Scratch, v, set); break;
            case 21: p.Normal = Apply(p.Normal, v, set); break;
        }
    }

    // ---- particles ----
    void Spawn(Sys s, int index) {
        var d = s.D;
        var p = new P { Index = index, Seed = s.Seed * 747796405u + (uint)index * 2891336453u + 1, Position = s.O, Color = d.Color };
        p.F[1] = d.Life; p.F[3] = d.Radius; p.F[4] = d.Rotation; p.F[5] = d.RotationSpeed; p.F[7] = 1; p.F[9] = d.Sequence; p.F[10] = .1f; p.F[16] = 1;
        for (int i = 0; i < d.Init.Count; i++) Initialize(s, p, d.Init[i], 1000 + i * 16);
        Array.Copy(p.F, p.F0, Fields);
        s.Live.Add(p); m_live++; Peak = Math.Max(Peak, m_live);
        Simulate(s, p, 0);   // what the operators show at age 0
    }
    static void Initialize(Sys s, P p, Op op, int salt) {
        switch (op.Kind) {
            case Kind.Sphere: {
                Vector3 dir = UnitSphere(p, salt) * op.Bias;
                if (op.BiasAbs.X > 0) dir.X = MathF.Abs(dir.X);
                if (op.BiasAbs.Y > 0) dir.Y = MathF.Abs(dir.Y);
                if (op.BiasAbs.Z > 0) dir.Z = MathF.Abs(dir.Z);
                dir = dir.LengthSquared() > 1e-10f ? Vector3.Normalize(dir) : Vector3.UnitX;
                float r = Lerp(Eval(op.RadiusMin, s, p, salt + 3), Eval(op.RadiusMax, s, p, salt + 4), H(p, salt + 5));
                float speed = Lerp(Eval(op.SpeedMin, s, p, salt + 6), Eval(op.SpeedMax, s, p, salt + 7), MathF.Pow(H(p, salt + 8), op.SpeedExp));
                Vector3 local = Between(EvalV(op.LocalMin, s, p, salt + 9), EvalV(op.LocalMax, s, p, salt + 10), p, salt + 11);
                p.Position = s.O + Local(s, dir * r) * s.Unit;
                p.Velocity = Local(s, dir * speed + local) * s.Unit;
                break;
            }
            case Kind.Box: p.Position = s.O + Local(s, Between(EvalV(op.Min, s, p, salt), EvalV(op.Max, s, p, salt + 3), p, salt + 6)) * s.Unit; p.Velocity = Vector3.Zero; break;
            case Kind.Offset: p.Position += Local(s, Between(EvalV(op.Min, s, p, salt), EvalV(op.Max, s, p, salt + 3), p, salt + 6)) * s.Unit; break;
            case Kind.Warp: case Kind.WarpScalar: {
                float t = op.Kind == Kind.Warp ? H(p, salt) : Eval(op.Value, s, p, salt);
                Vector3 w = Vector3.Lerp(EvalV(op.Min, s, p, salt + 1), EvalV(op.Max, s, p, salt + 4), t);
                p.Position = s.O + Local(s, ToLocal(s, p.Position - s.O) * w);
                break;
            }
            case Kind.Velocity:
                p.Velocity += Local(s, UnitSphere(p, salt) * Lerp(Eval(op.SpeedMin, s, p, salt + 3), Eval(op.SpeedMax, s, p, salt + 4), H(p, salt + 5))
                    + Between(EvalV(op.LocalMin, s, p, salt + 6), EvalV(op.LocalMax, s, p, salt + 7), p, salt + 8)) * s.Unit;
                break;
            case Kind.VelocityNoise: p.Velocity += Local(s, Between(EvalV(op.Min, s, p, salt), EvalV(op.Max, s, p, salt + 3), p, salt + 6)) * s.Unit; break;
            case Kind.Ring: {
                float perOrbit = MathF.Round(Eval(op.PerOrbit, s, p, salt));
                float a = op.Even && perOrbit > 0 ? MathF.PI * 2 * (p.Index % perOrbit) / perOrbit : H(p, salt + 1) * MathF.PI * 2;
                float radius = Eval(op.Radius, s, p, salt + 2) + Eval(op.Thickness, s, p, salt + 3) * H(p, salt + 4);
                Vector3 radial = Angles(new Vector3(MathF.Cos(a), MathF.Sin(a), 0), Eval(op.Pitch, s, p, salt + 5), Eval(op.Yaw, s, p, salt + 6), Eval(op.Roll, s, p, salt + 7));
                p.Position = s.O + Local(s, radial * radius) * s.Unit;
                p.Velocity = Local(s, radial * Lerp(Eval(op.SpeedMin, s, p, salt + 8), Eval(op.SpeedMax, s, p, salt + 9), H(p, salt + 10))) * s.Unit;
                break;
            }
            case Kind.VelocityFromNormal: p.Velocity += p.Normal * Lerp(op.SpeedLo, op.SpeedHi, H(p, salt)) * s.Unit; break;
            case Kind.DirectionToVector: {
                Vector3 dir = p.Velocity.LengthSquared() > 1e-12f ? p.Velocity : s.X;
                if (op.Normalize) dir = Vector3.Normalize(dir);
                SetVector(p, op.Field, dir * op.Scale, SetMode.Replace);
                break;
            }
            case Kind.Float: SetFloat(p, op.Field, op.Set, Eval(op.Value, s, p, salt), true); break;
            case Kind.Vec: SetVector(p, op.Field, EvalV(op.Vector, s, p, salt), op.Set); break;
            case Kind.ScalarToVector: {
                float t = Remap(op.InField is >= 0 and < Fields ? p.F[op.InField] : 0, op.InMin, op.InMax, 0, 1, false);
                Vector3 v = Vector3.Lerp(EvalV(op.Min, s, p, salt), EvalV(op.Max, s, p, salt + 3), t);
                if (op.Field == 0) p.Position = s.O + Local(s, v) * s.Unit;
                else SetVector(p, op.Field, v, op.Set);
                break;
            }
            case Kind.Sequence: p.F[op.Field] = Math.Clamp(op.SequenceMin + (int)(H(p, salt) * (op.SequenceMax - op.SequenceMin + 1)), op.SequenceMin, op.SequenceMax); break;
            case Kind.Color: p.Color = Vector3.Lerp(op.Color0, op.Color1, H(p, salt)); break;
            case Kind.AgeNoise: p.Age = Lerp(op.StartTime, op.EndTime, H(p, salt)) * p.F[1]; break;
            case Kind.GlobalScale: {
                float k = op.Scale * (op.Cp is >= 0 and < 8 ? s.Cp[op.Cp].X : 1);
                if (op.ScaleRadius) p.F[3] *= k;
                if (op.ScalePosition) p.Position = s.O + (p.Position - s.O) * k;
                if (op.ScaleVelocity) p.Velocity *= k;
                break;
            }
            case Kind.InheritVelocity: p.Velocity += s.Inherit * op.Scale; break;
        }
    }
    static void Simulate(Sys s, P p, float dt) {
        p.Age += dt; p.AlphaMul = 1; p.RadiusMul = 1; p.Shown = p.Color;
        var ops = s.D.Ops;
        for (int i = 0; i < ops.Count; i++) {
            var op = ops[i]; int salt = 5000 + i * 16;
            float life = MathF.Max(1e-4f, p.F[1]), t = p.Age / life;
            switch (op.Kind) {
                case Kind.Move: {
                    Vector3 g = EvalV(op.Gravity, s, p, salt);
                    p.Velocity += new Vector3(g.X, g.Z, -g.Y) * s.Unit * dt;   // CS2's world is z up
                    float drag = Eval(op.Drag, s, p, salt + 3);
                    if (drag > 0) p.Velocity *= MathF.Pow(MathF.Max(0, 1 - drag), dt * 30);
                    p.Position += p.Velocity * dt;
                    break;
                }
                case Kind.Decay: if (p.Age >= life) p.Dead = true; break;
                case Kind.FadeOut: case Kind.FadeIn: {
                    float time = Lerp(op.StartMin, op.StartMax, MathF.Pow(H(p, salt), op.Exp)) * (op.Proportional ? life : 1);
                    float f = time <= 0 ? 1 : op.Kind == Kind.FadeOut ? Saturate((life - p.Age) / time) : Saturate(p.Age / time);
                    p.AlphaMul *= op.Ease ? Spline(f) : f;
                    break;
                }
                case Kind.Radius: {
                    float u = SourceBias(Window(t, op.StartTime, op.EndTime), op.BiasAmount != 0 ? op.BiasAmount : .5f);
                    if (op.Ease) u = Spline(u);
                    p.RadiusMul *= Lerp(Eval(op.StartScale, s, p, salt), Eval(op.EndScale, s, p, salt + 1), u);
                    break;
                }
                case Kind.ColorFade: { float u = Remap(t, op.StartTime, op.EndTime, 0, 1, true); p.Shown = Vector3.Lerp(p.Color, op.Color1, op.Ease ? Spline(u) : u); break; }
                case Kind.MaxVelocity: {
                    float v = p.Velocity.Length(), max = op.MaxSpeed * s.Unit, min = op.MinSpeed * s.Unit;
                    if (max > 0 && v > max) p.Velocity *= max / v; else if (v > 1e-6f && v < min) p.Velocity *= min / v;
                    break;
                }
                case Kind.SetFloat: SetFloat(p, op.Field, op.Set, Eval(op.Value, s, p, salt), false); break;
                case Kind.SetVec: if (op.Field == 17) p.Scratch = EvalV(op.Vector, s, p, salt); break;
                case Kind.Ramp: if (t >= op.StartTime && t <= op.EndTime && op.Field is >= 0 and < Fields) p.F[op.Field] += op.Rate * dt; break;
                case Kind.Lerp: if (op.Field is >= 0 and < Fields) p.F[op.Field] = Lerp(p.F0[op.Field], Eval(op.Output, s, p, salt), Window(t, op.StartTime, op.EndTime)); break;
                case Kind.SpinUpdate: p.F[4] += p.F[5] * dt; break;
                case Kind.Spin: p.F[4] += op.Rate * dt; break;
                case Kind.Cull: if (H(p, salt) < op.Fraction && t >= Lerp(op.StartTime, op.EndTime, H(p, salt + 1))) p.Dead = true; break;
            }
        }
    }
    // C_OP_PositionLock: a locked particle moves (and turns, with rotation) as the muzzle does, fully until its start time and less
    // and less until its end time (both fractions of its life), by the operator's strength
    static void Follow(Sys s, Vector3 o, Vector3 x, Vector3 y, Vector3 z) {
        for (int i = 0; i < s.D.Ops.Count; i++) {
            var op = s.D.Ops[i];
            if (op.Kind != Kind.Lock) continue;
            int salt = 5000 + i * 16;
            foreach (var p in s.Live) {
                float t = p.Age / MathF.Max(1e-4f, p.F[1]);
                float start = Lerp(op.StartMin, op.StartMax, H(p, salt)), end = Lerp(op.EndMin, op.EndMax, H(p, salt + 1));
                float w = (t <= start ? 1 : t >= end ? 0 : 1 - Window(t, start, end)) * Saturate(Eval(op.Strength, s, p, salt + 2));
                if (w <= 0) continue;
                Vector3 rel = p.Position - s.O, moved = op.Rotation ? o + x * Vector3.Dot(rel, s.X) + y * Vector3.Dot(rel, s.Y) + z * Vector3.Dot(rel, s.Z) : o + rel;
                p.Position = Vector3.Lerp(p.Position, moved, w);
                if (op.Rotation) p.Velocity = Vector3.Lerp(p.Velocity, x * Vector3.Dot(p.Velocity, s.X) + y * Vector3.Dot(p.Velocity, s.Y) + z * Vector3.Dot(p.Velocity, s.Z), w);
            }
            break;
        }
        s.O = o; s.X = x; s.Y = y; s.Z = z;
    }
    void Advance(Sys s, float dt) {
        if (s.Total < 0) { s.Total = Math.Clamp((int)MathF.Round(Eval(s.D.EmitCount, s, null, 1)), 0, s.D.Max); s.EmitAt = Eval(s.D.EmitStart, s, null, 2); }
        for (int j = s.Live.Count - 1; j >= 0; j--) {
            var p = s.Live[j];
            Simulate(s, p, dt);
            if (p.Dead) { s.Live.RemoveAt(j); m_live--; }
        }
        if (s.Emitted < s.Total && s.Age >= s.EmitAt) {
            int n = s.D.PerFrame > 0 ? Math.Min(s.D.PerFrame, s.Total - s.Emitted) : s.Total - s.Emitted;
            for (int k = 0; k < n; k++) {
                if (m_live >= MaxLive) { s.Emitted = s.Total; break; }
                Spawn(s, s.Emitted++);
            }
        }
    }

    public void Update(float dt) {
        if (!(dt > 0)) return;
        dt = MathF.Min(dt, .1f);
        for (int i = m_systems.Count - 1; i >= 0; i--) {
            var s = m_systems[i];
            if (s.Anchor is not null && s.Anchor(out var o, out var forward) && forward.LengthSquared() > 1e-8f) { Basis(forward, out var x, out var y, out var z); Follow(s, o, x, y, z); }
            if (s.Delay > 0) { s.Delay -= dt; if (s.Delay > 0) continue; }
            s.Age += dt;
            Advance(s, dt);
            if (s.Total >= 0 && s.Emitted >= s.Total && s.Live.Count == 0) m_systems.RemoveAt(i);
        }
    }

    Texture2D TextureOf(string asset) {
        if (asset is null) return null;
        if (m_textures.TryGetValue(asset, out var t)) return t;
        try { t = ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/" + asset); }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("cs2-muzzle-fx-texture-" + asset, $"No muzzle texture {asset}: {e.Message}"); t = null; }
        return m_textures[asset] = t;
    }
    // The vertex colour: alpha blending as is. Additive: the hue of colour × overbright, and its strength k (above 1) folded into
    // alpha as √k - CS2 adds k in HDR and tone-maps it; added in full in this LDR blend, a few overlapping flash particles burn
    // to white where CS2 shows orange (fxc, 2026-10-05). Calibrated against CS2's frames, not read from its files.
    static Color Encode(Vector3 rgb, float alpha, bool blend) {
        alpha = Saturate(alpha);
        if (blend) return new Color(Saturate(rgb.X), Saturate(rgb.Y), Saturate(rgb.Z), alpha);
        float k = MathF.Max(1, MathF.Max(rgb.X, MathF.Max(rgb.Y, rgb.Z)));
        return new Color(Saturate(rgb.X / k), Saturate(rgb.Y / k), Saturate(rgb.Z / k), MathF.Min(1, alpha * MathF.Sqrt(k)));
    }

    public void Draw(Camera camera) {
        if (m_live == 0) return;
        m_renderer ??= new PrimitivesRenderer3D();
        // CS2's screen sizes (renderer min/max size and size fades): the projected radius over the viewport's width - the reading
        // under which CS2's first-person AWP flash (about a seventh of the height across, fading from 0.1) is drawn at all;
        // radius = size × 2 × depth / projX
        float projX = camera.ProjectionMatrix.M11;
        if (!float.IsFinite(projX) || projX <= 1e-4f) return;
        var view = new View { Camera = camera, Eye = camera.ViewPosition, Forward = camera.ViewDirection, ProjX = projX, Matrix = camera.ViewMatrix, Inverse = camera.InvertedViewMatrix };
        view.Right = Vector3.Normalize(Vector3.Cross(camera.ViewDirection, camera.ViewUp)); view.Up = Vector3.Normalize(Vector3.Cross(view.Right, camera.ViewDirection));
        // the muzzle lights alive now (C_OP_RenderStandardLight), for the particles CS2 lights diffusely
        m_lights.Clear();
        foreach (var s in m_systems) {
            if (s.Live.Count == 0 || s.Camera is not null && s.Camera != camera) continue;
            foreach (var r in s.D.Renderers) {
                if (!r.Light) continue;
                foreach (var p in s.Live) {
                    float radius = p.F[3] * r.RadiusMultiplier * s.Unit;
                    if (radius > 1e-3f) m_lights.Add((Shown(s, view, p.Position), radius, EvalV(r.ColorScale, s, p, 0) * p.Shown * (r.Intensity * p.F[7] * LightStrength)));
                }
            }
        }
        foreach (var s in m_systems) {
            if (s.Live.Count == 0 || s.Camera is not null && s.Camera != camera) continue;
            float light = LightAt?.Invoke(s.O) ?? 1;
            for (int ri = 0; ri < s.D.Renderers.Count; ri++) {
                var r = s.D.Renderers[ri];
                if (r.Light) continue;
                // the renderer's colour texture, and a second one blended in by its own input (layer 1 at that weight, layer 0 at the rest)
                for (int layer = 0; layer < (r.Texture2 is null ? 1 : 2); layer++) DrawLayer(s, r, ri, layer, light, view);
            }
        }
        m_renderer.Flush(camera.ViewProjectionMatrix);
    }
    readonly List<(Vector3 At, float Radius, Vector3 Colour)> m_lights = [];
    /// <summary>CS2's light intensity in this game's light units: a light of intensity 0.2 at alpha 0.75 lights the smoke beside the
    /// muzzle about as much as full daylight, orange (calibrated against CS2's frames, where the smoke round a shotgun's flash glows
    /// orange; not read from its files).</summary>
    public const float LightStrength = 2f;
    Vector3 LightOn(Vector3 at) {
        Vector3 sum = Vector3.Zero;
        foreach (var (o, radius, colour) in m_lights) {
            float d = Vector3.Distance(at, o) / radius;
            if (d < 1) sum += colour * (1 - d) * (1 - d);
        }
        return sum;
    }
    struct View { public Camera Camera; public Vector3 Eye, Forward, Right, Up; public float ProjX; public Matrix Matrix, Inverse; }
    // first person: the viewmodel's projection - across the view scaled by the ratio (radii with it, in DrawLayer)
    static Vector3 Shown(Sys s, in View v, Vector3 at) {
        if (s.ViewRatio <= 0) return at;
        Vector3 p = Vector3.Transform(at, v.Matrix); p.X *= s.ViewRatio; p.Y *= s.ViewRatio;
        return Vector3.Transform(p, v.Inverse);
    }

    void DrawLayer(Sys s, Renderer r, int ri, int layer, float light, in View v) {
        bool blend = r.Blend != "add" && r.Blend != "lighten";
        var sheet = SheetOf(layer == 0 ? r.Texture : r.Texture2);
        var texture = TextureOf(sheet is null ? null : blend ? sheet.Asset ?? sheet.LinearAsset : sheet.LinearAsset ?? sheet.Asset);
        if (texture is null) return;
        // CS2's lighten (the brighter of the two) has no blend state here: drawn additively at half strength
        var batch = m_renderer.TexturedBatch(texture, useAlphaTest: false, layer: blend ? 0 : 1, DepthStencilState.DepthRead, RasterizerState.CullNoneScissor,
            blend ? BlendState.NonPremultiplied : BlendState.Additive, SamplerState.LinearClamp);
        float strength = r.Blend == "lighten" ? .5f : 1;
        float ratio = s.ViewRatio > 0 ? s.ViewRatio : 1;
        Vector3 scale = EvalV(r.ColorScale, s, null, 0) * r.Overbright;
        bool lit = r.Diffuse > 0 && m_lights.Count > 0;
        int sequences = sheet.Sequences?.Length ?? 0;
        float cu = 1f / sheet.Columns, cv = 1f / sheet.Rows;
        foreach (var p in s.Live) {
            int salt = 9000 + ri * 32;
            // layer 1: the second texture at its blend weight; layer 0: the first at the rest (all of it without a second)
            float weight = r.Texture2 is null ? 1 : layer == 1 ? Saturate(Eval(r.Blend2, s, p, salt + 10)) : 1 - Saturate(Eval(r.Blend2, s, p, salt + 10));
            float radius = p.F[3] * p.RadiusMul * Eval(r.RadiusScale, s, p, salt) * s.Unit, alpha = p.F[7] * p.AlphaMul * Eval(r.AlphaScale, s, p, salt + 1) * weight;
            alpha *= p.F[16] * strength;   // CS2's second alpha (ALPHA_ALTERNATE): the smoke's and flashes' first-person weakening
            if (!(radius > 1e-5f) || !(alpha > .002f)) continue;
            // first person: drawn in the viewmodel's projection (× ratio), its screen size measured as the camera sees it
            // (CS2's AWP flash fills ~14 % of the height with a fade from 10 %: fxc, 2026-10-05)
            Vector3 at = Shown(s, v, p.Position);
            float depth = MathF.Max(.02f, Vector3.Dot(at - v.Eye, v.Forward)), perSize = 2 * depth / v.ProjX, size = radius / perSize;
            if (r.MaxSize > 0 && size > r.MaxSize) { radius = r.MaxSize * perSize; size = r.MaxSize; }
            if (size < r.MinSize) { radius = r.MinSize * perSize; size = r.MinSize; }
            radius *= ratio;
            if (size > r.StartFade) alpha *= 1 - Window(size, r.StartFade, r.EndFade);
            if (alpha <= .002f) continue;
            // the sheet frame: the particle's sequence (the second one for the second texture), at its manual frame, over its
            // life, or at the rate in cycles per second
            int seq = sequences > 0 ? Math.Clamp((int)p.F[layer == 0 ? 9 : 13], 0, sequences - 1) : 0;
            int first = sequences > 0 ? sheet.Sequences[seq][0] : 0, frames = Math.Max(1, sequences > 0 ? sheet.Sequences[seq][1] : sheet.Frames);
            float life = MathF.Max(1e-4f, p.F[1]);
            float cycle = r.Animation == "manual" ? Saturate(p.F[38]) : r.Animation == "fit" || r.FitCycle ? Saturate(p.Age / life) : p.Age * r.AnimationRate % 1f;
            int frame = first + Math.Clamp((int)(cycle * frames), 0, frames - 1);
            float u0 = frame % sheet.Columns * cu, v0 = frame / sheet.Columns * cv;
            Vector3 rgb = p.Shown * scale * (r.SelfIllum + r.Diffuse * light);
            if (lit) rgb += p.Shown * scale * r.Diffuse * LightOn(at);
            if (r.Trail) {
                float speed = p.Velocity.Length();
                if (speed < 1e-5f) continue;
                float length = Math.Clamp(speed / s.Unit * p.F[10] * r.LengthScale, r.MinLength, r.MaxLength) * s.Unit;
                if (r.LengthFadeIn > 0) length *= Saturate(p.Age / r.LengthFadeIn);
                if (length < 1e-5f) continue;
                Vector3 head = at, tail = Shown(s, v, p.Position - p.Velocity / speed * length), dir = head - tail;
                if (!(dir.LengthSquared() > 1e-12f)) continue;
                Vector3 side = Vector3.Cross(Vector3.Normalize(dir), head - v.Eye);
                if (!(side.LengthSquared() > 1e-10f)) continue;
                side = Vector3.Normalize(side);
                float headWidth = radius * Eval(r.HeadTaper, s, p, salt + 2), tailWidth = radius * Eval(r.Taper, s, p, salt + 3);
                if (r.MaxSize > 0) { headWidth = MathF.Min(headWidth, r.MaxSize * perSize * ratio); tailWidth = MathF.Min(tailWidth, r.MaxSize * 2 * MathF.Max(.02f, Vector3.Dot(tail - v.Eye, v.Forward)) / v.ProjX * ratio); }
                Color hc = Encode(rgb * EvalV(r.HeadColor, s, p, salt + 4), alpha, blend), tc = Encode(rgb * EvalV(r.TailColor, s, p, salt + 7), alpha, blend);
                batch.QueueQuad(head + side * headWidth, head - side * headWidth, tail - side * tailWidth, tail + side * tailWidth,
                    new Vector2(u0, v0), new Vector2(u0 + cu, v0), new Vector2(u0 + cu, v0 + cv), new Vector2(u0, v0 + cv), hc, hc, tc, tc);
            }
            else {
                float roll = MathUtils.DegToRad(p.F[4]), cos = MathF.Cos(roll), sin = MathF.Sin(roll);
                Vector3 ax = (v.Right * cos + v.Up * sin) * radius, ay = (-v.Right * sin + v.Up * cos) * radius, c = at;
                batch.QueueQuad(c - ax + ay, c + ax + ay, c + ax - ay, c - ax - ay, new Vector2(u0, v0), new Vector2(u0 + cu, v0), new Vector2(u0 + cu, v0 + cv), new Vector2(u0, v0 + cv), Encode(rgb, alpha, blend));
            }
        }
    }
}
