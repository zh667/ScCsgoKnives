using System.IO;
using System.Reflection;
using System.Text.Json;
using Engine;
namespace Game;

/// <summary>
/// CS2's muzzle particle systems (round 6 of the deathmatch work, 2026-10-05; the user: "你还可以看所有枪的枪线，曳光弹，以及
/// 枪烟，我感觉这些还是不够还原", then "把所有枪（有实际数据的），都做了"), read by tools/cs2_muzzle_fx.py out of CS2's own files into
/// AnimationData/cs2_muzzle_fx.json: for every gun the system its model plays at the muzzle (and the suppressed one), and for every
/// system under it its program as CS2 runs it - children and the random choice among them, the control point values of its
/// configurations (fps_view, thirdperson, ...), the burst, the initializers and operators in order with their float and vector
/// inputs (literals, random ranges, particle number / age / attribute, control point components, system age; direct, multiplied,
/// remapped or through CS2's Hermite curves) and the sprite or trail renderers. ScMuzzleParticles runs it. Units are CS2's
/// (inches); vectors local to the muzzle are x forward, y left, z up. What is not run is listed per system ("Unmodelled").
/// </summary>
public static class Cs2MuzzleFx {
    const string Resource = "AnimationData.cs2_muzzle_fx.json", ExpectedFormat = "ScCsgoKnives.Cs2MuzzleFx/2";

    /// <summary>A float input: a constant (Source null) or a random range or a source mapped as CS2 maps it.</summary>
    public sealed class FloatIn {
        public string Source; public float Constant;
        public float A, B; public string BiasType; public float BiasParameter;   // "rand", and the bias of a biased range or remap
        public string Map = "direct"; public float Literal, Mult = 1, In0, In1 = 1, Out0, Out1 = 1; public bool Clamp = true;
        public int Cp, Component, Attribute;
        public float[] CurveX, CurveY, SlopeIn, SlopeOut;
        public bool IsConstant => Source is null;
        public static FloatIn Of(float v) => new() { Constant = v };
    }
    /// <summary>A vector input: a constant, a particle vector, a float interpolated between two vectors, or three floats.</summary>
    public sealed class VecIn {
        public string Source; public Vector3 Constant;
        public int Attribute; public Vector3 Scale = Vector3.One;
        public FloatIn F, X, Y, Z; public float In0, In1 = 1; public Vector3 Out0, Out1;
        public static VecIn Of(Vector3 v) => new() { Constant = v };
    }
    public enum Kind {
        Sphere, Box, Offset, Warp, WarpScalar, Velocity, VelocityNoise, Ring, VelocityFromNormal, DirectionToVector, Float, Vec, ScalarToVector, Sequence, Color, AgeNoise,
        GlobalScale, InheritVelocity, Move, Decay, FadeOut, FadeIn, Radius, ColorFade, MaxVelocity, Lock, SetFloat, SetVec, Ramp, Lerp, SpinUpdate, Spin, Cull,
    }
    public enum SetMode { Replace, ScaleInitial, ScaleCurrent, AddInitial, AddCurrent }
    /// <summary>One initializer or operator (the fields its kind uses; tools/cs2_muzzle_fx.py names them).</summary>
    public sealed class Op {
        public Kind Kind; public int Field, InField, Cp, SequenceMin, SequenceMax; public SetMode Set;
        public FloatIn Value, RadiusMin, RadiusMax, SpeedMin, SpeedMax, PerOrbit, Radius, Thickness, Roll, Pitch, Yaw, Drag, StartScale, EndScale, Strength, Output;
        public VecIn Vector, LocalMin, LocalMax, Min, Max, Gravity;
        public Vector3 Bias, BiasAbs, Color0, Color1;
        public float SpeedExp, InMin, InMax, Exp, Rate, Fraction, BiasAmount, Scale, MinRate, StopTime, StartTime, EndTime, StartMin, StartMax, EndMin, EndMax, MaxSpeed, MinSpeed, SpeedLo, SpeedHi;
        public bool Local, Even, XYOnly, Normalize, Proportional, Ease, Rotation, ScaleRadius, ScalePosition, ScaleVelocity;
    }
    public sealed class Renderer {
        public bool Trail, Light; public string Texture, Texture2, Blend, Animation;
        public float Intensity, RadiusMultiplier;   // a light (C_OP_RenderStandardLight)
        public FloatIn RadiusScale, AlphaScale, Taper, HeadTaper, Blend2; public VecIn ColorScale, HeadColor, TailColor;
        public float Overbright, SelfIllum, Diffuse, AnimationRate, StartFade, EndFade, MaxSize, MinSize, LengthScale, LengthFadeIn, MaxLength, MinLength;
        public bool FitCycle;
    }
    public sealed class Child { public string System; public int Group; public float Delay; }
    public sealed class SystemSpec {
        public string Name, Source, Missing;
        public List<Child> Children = []; public List<(int Group, int Count)> Choose = [];
        public Dictionary<string, Dictionary<int, Vector3>> Configs = [];
        public FloatIn EmitCount, EmitStart; public int PerFrame = -1, Max = 1000;
        public float Radius = 5, Life = 1, Rotation, RotationSpeed; public Vector3 Color = Vector3.One; public int Sequence;
        public List<Op> Init = [], Ops = []; public List<Renderer> Renderers = []; public List<string> Unmodelled = [];
        public bool Emits => EmitCount is not null && Renderers.Count > 0;
    }
    /// <summary>A sprite sheet: Asset as CS2 stores it (alpha-blended renderers), LinearAsset with linear RGB (additive ones).</summary>
    public sealed class Sheet { public string Asset, LinearAsset; public int Columns = 1, Rows = 1, Frames = 1; public int[][] Sequences; }

    public static string LoadError { get; private set; } = "not loaded";
    static Dictionary<string, Dictionary<string, string>> s_guns;
    static Dictionary<string, SystemSpec> s_systems;
    static Dictionary<string, Sheet> s_sheets;
    static readonly bool s_loaded = Load();

    static bool Load() {
        try {
            Assembly assembly = typeof(Cs2MuzzleFx).Assembly;
            string name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(Resource, StringComparison.OrdinalIgnoreCase));
            if (name is null) { LoadError = $"no embedded {Resource}"; KnifeDiagnostics.WarnOnce("cs2-muzzle-fx-load", $"No embedded {Resource}: CS2's muzzle systems are not drawn"); return false; }
            using Stream stream = assembly.GetManifestResourceStream(name);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (!root.TryGetProperty("Format", out var format) || format.GetString() != ExpectedFormat) {
                LoadError = $"{Resource} is not {ExpectedFormat}"; KnifeDiagnostics.WarnOnce("cs2-muzzle-fx-load", LoadError); return false;
            }
            var guns = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var g in root.GetProperty("Guns").EnumerateObject())
                guns[g.Name] = g.Value.EnumerateObject().ToDictionary(m => m.Name, m => m.Value.GetString(), StringComparer.Ordinal);
            var systems = new Dictionary<string, SystemSpec>(StringComparer.Ordinal);
            foreach (var s in root.GetProperty("Systems").EnumerateObject()) systems[s.Name] = ReadSystem(s.Name, s.Value);
            var sheets = new Dictionary<string, Sheet>(StringComparer.Ordinal);
            foreach (var t in root.GetProperty("Textures").EnumerateObject()) {
                var v = t.Value;
                if (v.TryGetProperty("Missing", out var missing) && missing.GetBoolean()) continue;
                sheets[t.Name] = new Sheet { Asset = Str(v, "Asset"), LinearAsset = Str(v, "LinearAsset"), Columns = v.GetProperty("Columns").GetInt32(), Rows = v.GetProperty("Rows").GetInt32(),
                    Frames = v.GetProperty("Frames").GetInt32(), Sequences = v.GetProperty("Sequences").EnumerateArray().Select(q => q.EnumerateArray().Select(x => x.GetInt32()).ToArray()).ToArray() };
            }
            (s_guns, s_systems, s_sheets) = (guns, systems, sheets);
            LoadError = null;
            return true;
        }
        catch (Exception e) {
            LoadError = $"{e.GetType().Name}: {e.Message}";
            KnifeDiagnostics.WarnOnce("cs2-muzzle-fx-load", $"Could not read {Resource}: {e.Message}");
            return false;
        }
    }

    static float Num(JsonElement e, string name, float fallback) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : fallback;
    static int Int(JsonElement e, string name, int fallback) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (int)MathF.Round(v.GetSingle()) : fallback;
    static bool Bool(JsonElement e, string name, bool fallback) => e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;
    static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static Vector3 V3(JsonElement v) => new(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle());

    static FloatIn F(JsonElement e, string name, float fallback) => e.TryGetProperty(name, out var v) ? F(v) : FloatIn.Of(fallback);
    static FloatIn F(JsonElement v) {
        if (v.ValueKind == JsonValueKind.Number) return FloatIn.Of(v.GetSingle());
        var f = new FloatIn { Source = Str(v, "T"), A = Num(v, "A", 0), B = Num(v, "B", 1), Map = Str(v, "Map") ?? "direct", Literal = Num(v, "Lit", 0), Mult = Num(v, "Mult", 1),
            Clamp = Bool(v, "Clamp", true), Cp = Int(v, "Cp", 0), Component = Int(v, "Comp", 0), Attribute = Int(v, "Attr", 3) };
        if (v.TryGetProperty("Bias", out var bias)) { f.BiasType = bias[0].GetString(); f.BiasParameter = bias[1].GetSingle(); }
        if (v.TryGetProperty("In", out var i)) { f.In0 = i[0].GetSingle(); f.In1 = i[1].GetSingle(); }
        if (v.TryGetProperty("Out", out var o)) { f.Out0 = o[0].GetSingle(); f.Out1 = o[1].GetSingle(); }
        if (v.TryGetProperty("Curve", out var c)) {
            var keys = c.EnumerateArray().Select(k => k.EnumerateArray().Select(x => x.GetSingle()).ToArray()).ToArray();
            f.CurveX = keys.Select(k => k[0]).ToArray(); f.CurveY = keys.Select(k => k[1]).ToArray(); f.SlopeIn = keys.Select(k => k[2]).ToArray(); f.SlopeOut = keys.Select(k => k[3]).ToArray();
        }
        return f;
    }
    static VecIn Vv(JsonElement e, string name, Vector3 fallback) => e.TryGetProperty(name, out var v) ? Vv(v) : VecIn.Of(fallback);
    static VecIn Vv(JsonElement v) {
        if (v.ValueKind == JsonValueKind.Array) return VecIn.Of(V3(v));
        var r = new VecIn { Source = Str(v, "T"), Attribute = Int(v, "Attr", 0) };
        if (v.TryGetProperty("Scale", out var s)) r.Scale = V3(s);
        if (r.Source == "interp") {
            r.F = F(v.GetProperty("F")); var i = v.GetProperty("In"); r.In0 = i[0].GetSingle(); r.In1 = i[1].GetSingle();
            r.Out0 = V3(v.GetProperty("O0")); r.Out1 = V3(v.GetProperty("O1"));
        }
        if (r.Source == "comps") { r.X = F(v.GetProperty("X")); r.Y = F(v.GetProperty("Y")); r.Z = F(v.GetProperty("Z")); }
        return r;
    }
    static Vector3 V3(JsonElement e, string name, Vector3 fallback) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? V3(v) : fallback;
    static SetMode Set(JsonElement e) => Str(e, "Set") switch {
        "scaleInitial" => SetMode.ScaleInitial, "scaleCurrent" => SetMode.ScaleCurrent, "addInitial" => SetMode.AddInitial, "addCurrent" => SetMode.AddCurrent, _ => SetMode.Replace };

    static SystemSpec ReadSystem(string name, JsonElement e) {
        var s = new SystemSpec { Name = name, Source = Str(e, "Source"), Missing = Str(e, "Missing") };
        if (e.TryGetProperty("Children", out var kids))
            foreach (var c in kids.EnumerateArray()) s.Children.Add(new Child { System = Str(c, "System"), Group = Int(c, "Group", 0), Delay = Num(c, "Delay", 0) });
        if (e.TryGetProperty("Choose", out var choose))
            foreach (var c in choose.EnumerateArray()) s.Choose.Add((Int(c, "Group", 0), Int(c, "Count", 1)));
        if (e.TryGetProperty("Configs", out var configs))
            foreach (var c in configs.EnumerateObject()) s.Configs[c.Name] = c.Value.EnumerateObject().ToDictionary(p => int.Parse(p.Name, global::System.Globalization.CultureInfo.InvariantCulture), p => V3(p.Value));
        if (e.TryGetProperty("Unmodelled", out var un)) s.Unmodelled = un.EnumerateArray().Select(x => x.GetString()).ToList();
        if (!e.TryGetProperty("Emit", out var emit)) return s;
        s.EmitCount = F(emit, "Count", 1); s.EmitStart = F(emit, "Start", 0); s.PerFrame = Int(emit, "PerFrame", -1); s.Max = Int(e, "Max", 1000);
        var k = e.GetProperty("Const");
        s.Radius = Num(k, "Radius", 5); s.Life = Num(k, "Life", 1); s.Color = V3(k, "Color", Vector3.One); s.Rotation = Num(k, "Rotation", 0); s.RotationSpeed = Num(k, "RotationSpeed", 0); s.Sequence = Int(k, "Sequence", 0);
        foreach (var o in e.GetProperty("Init").EnumerateArray()) { var op = ReadOp(o); if (op is not null) s.Init.Add(op); else s.Unmodelled.Add("runtime: " + Str(o, "Op")); }
        foreach (var o in e.GetProperty("Ops").EnumerateArray()) { var op = ReadOp(o); if (op is not null) s.Ops.Add(op); else s.Unmodelled.Add("runtime: " + Str(o, "Op")); }
        foreach (var r in e.GetProperty("Renderers").EnumerateArray()) {
            bool trail = Str(r, "Kind") == "trail";
            if (Str(r, "Kind") == "light") {
                s.Renderers.Add(new Renderer { Light = true, ColorScale = Vv(r, "ColorScale", Vector3.One), Intensity = Num(r, "Intensity", 1), RadiusMultiplier = Num(r, "RadiusMultiplier", 1) });
                continue;
            }
            s.Renderers.Add(new Renderer {
                Trail = trail, Texture = Str(r, "Texture"), Texture2 = Str(r, "Texture2"), Blend2 = F(r, "Blend2", 0), Blend = Str(r, "Blend") ?? "blend", Animation = Str(r, "Anim") ?? "fixed",
                RadiusScale = F(r, "RadiusScale", 1), AlphaScale = F(r, "AlphaScale", 1), ColorScale = Vv(r, "ColorScale", Vector3.One),
                Overbright = Num(r, "Overbright", 1), SelfIllum = Num(r, "SelfIllum", 0), Diffuse = Num(r, "Diffuse", 1), AnimationRate = Num(r, "AnimRate", .1f), FitCycle = Bool(r, "FitCycle", false),
                StartFade = Num(r, "StartFade", 1e5f), EndFade = Num(r, "EndFade", 2e5f), MaxSize = Num(r, "MaxSize", 5000), MinSize = Num(r, "MinSize", 0),
                LengthScale = Num(r, "LengthScale", 1), LengthFadeIn = Num(r, "LengthFadeIn", 0), MaxLength = Num(r, "MaxLength", 2000), MinLength = Num(r, "MinLength", 0),
                Taper = F(r, "Taper", 1), HeadTaper = F(r, "HeadTaper", 1), HeadColor = Vv(r, "HeadColor", Vector3.One), TailColor = Vv(r, "TailColor", Vector3.One),
            });
        }
        return s;
    }

    static Op ReadOp(JsonElement o) {
        string kind = Str(o, "Op");
        var op = new Op { Field = Int(o, "Field", 3), Set = Set(o) };
        switch (kind) {
            case "sphere": op.Kind = Kind.Sphere; op.RadiusMin = F(o, "RMin", 0); op.RadiusMax = F(o, "RMax", 0); op.SpeedMin = F(o, "SMin", 0); op.SpeedMax = F(o, "SMax", 0);
                op.SpeedExp = Num(o, "SpeedExp", 1); op.LocalMin = Vv(o, "LMin", Vector3.Zero); op.LocalMax = Vv(o, "LMax", Vector3.Zero);
                op.Bias = V3(o, "Bias", Vector3.One); op.BiasAbs = V3(o, "BiasAbs", Vector3.Zero); break;
            case "box": op.Kind = Kind.Box; op.Min = Vv(o, "Min", Vector3.Zero); op.Max = Vv(o, "Max", Vector3.Zero); op.Local = Bool(o, "Local", false); break;
            case "offset": op.Kind = Kind.Offset; op.Min = Vv(o, "Min", Vector3.Zero); op.Max = Vv(o, "Max", Vector3.Zero); op.Local = Bool(o, "Local", false); break;
            case "warp": op.Kind = Kind.Warp; op.Min = Vv(o, "Min", Vector3.One); op.Max = Vv(o, "Max", Vector3.One); break;
            case "warpScalar": op.Kind = Kind.WarpScalar; op.Min = Vv(o, "Min", Vector3.One); op.Max = Vv(o, "Max", Vector3.One); op.Value = F(o, "F", 0); break;
            case "velocity": op.Kind = Kind.Velocity; op.SpeedMin = F(o, "SMin", 0); op.SpeedMax = F(o, "SMax", 0); op.LocalMin = Vv(o, "LMin", Vector3.Zero); op.LocalMax = Vv(o, "LMax", Vector3.Zero); break;
            case "velocityNoise": op.Kind = Kind.VelocityNoise; op.Min = Vv(o, "Min", Vector3.Zero); op.Max = Vv(o, "Max", Vector3.One); break;
            case "ring": op.Kind = Kind.Ring; op.PerOrbit = F(o, "PerOrbit", -1); op.Radius = F(o, "Radius", 0); op.Thickness = F(o, "Thickness", 0); op.SpeedMin = F(o, "SMin", 0); op.SpeedMax = F(o, "SMax", 0);
                op.Roll = F(o, "Roll", 0); op.Pitch = F(o, "Pitch", 0); op.Yaw = F(o, "Yaw", 0); op.Even = Bool(o, "Even", false); op.XYOnly = Bool(o, "XYOnly", true); break;
            case "velocityFromNormal": op.Kind = Kind.VelocityFromNormal; op.SpeedLo = Num(o, "SMin", 0); op.SpeedHi = Num(o, "SMax", 0); break;
            case "directionToVector": op.Kind = Kind.DirectionToVector; op.Field = Int(o, "Field", 0); op.Normalize = Bool(o, "Normalize", false); op.Scale = Num(o, "Scale", 1); break;
            case "float": op.Kind = Kind.Float; op.Value = F(o, "V", 0); break;
            case "vec": op.Kind = Kind.Vec; op.Field = Int(o, "Field", 0); op.Vector = Vv(o, "V", Vector3.Zero); break;
            case "scalarToVector": op.Kind = Kind.ScalarToVector; op.InField = Int(o, "In", 8); op.Field = Int(o, "Field", 0); op.InMin = Num(o, "InMin", 0); op.InMax = Num(o, "InMax", 1);
                op.Min = Vv(o, "Min", Vector3.Zero); op.Max = Vv(o, "Max", Vector3.One); op.Local = Bool(o, "Local", true); break;
            case "sequence": case "sequence2": op.Kind = Kind.Sequence; op.Field = kind == "sequence" ? 9 : 13; op.SequenceMin = Int(o, "Min", 0); op.SequenceMax = Int(o, "Max", 0); break;
            case "color": op.Kind = Kind.Color; op.Color0 = V3(o, "Min", Vector3.One); op.Color1 = V3(o, "Max", Vector3.One); break;
            case "ageNoise": op.Kind = Kind.AgeNoise; op.StartTime = Num(o, "Min", 0); op.EndTime = Num(o, "Max", 1); break;
            case "globalScale": op.Kind = Kind.GlobalScale; op.Cp = Int(o, "Cp", -1); op.Scale = Num(o, "Scale", 1);
                op.ScaleRadius = Bool(o, "Radius", true); op.ScalePosition = Bool(o, "Position", true); op.ScaleVelocity = Bool(o, "Velocity", false); break;
            case "inheritVelocity": op.Kind = Kind.InheritVelocity; op.Scale = Num(o, "Scale", 1); break;
            case "move": op.Kind = Kind.Move; op.Gravity = Vv(o, "Gravity", Vector3.Zero); op.Drag = F(o, "Drag", 0); break;
            case "decay": op.Kind = Kind.Decay; break;
            case "fadeOut": case "fadeIn":
                op.Kind = kind == "fadeOut" ? Kind.FadeOut : Kind.FadeIn; op.StartMin = Num(o, "Min", .25f); op.StartMax = Num(o, "Max", .25f); op.Exp = Num(o, "Exp", 1);
                op.Proportional = Bool(o, "Proportional", true); op.Ease = Bool(o, "Ease", true); break;
            case "radius": op.Kind = Kind.Radius; op.StartTime = Num(o, "StartTime", 0); op.EndTime = Num(o, "EndTime", 1); op.StartScale = F(o, "Start", 1); op.EndScale = F(o, "End", 1);
                op.BiasAmount = Num(o, "Bias", 0); op.Ease = Bool(o, "Ease", false); break;
            case "colorFade": op.Kind = Kind.ColorFade; op.Color1 = V3(o, "Color", Vector3.One); op.StartTime = Num(o, "Start", 0); op.EndTime = Num(o, "End", 1); op.Ease = Bool(o, "Ease", true); break;
            case "maxVelocity": op.Kind = Kind.MaxVelocity; op.MaxSpeed = Num(o, "Max", 0); op.MinSpeed = Num(o, "Min", 0); break;
            case "lock": op.Kind = Kind.Lock; op.StartMin = Num(o, "StartMin", 1); op.StartMax = Num(o, "StartMax", 1); op.EndMin = Num(o, "EndMin", 1); op.EndMax = Num(o, "EndMax", 1);
                op.Rotation = Bool(o, "Rot", false); op.Strength = F(o, "Strength", 1); break;
            case "setFloat": op.Kind = Kind.SetFloat; op.Value = F(o, "V", 0); break;
            case "setVec": op.Kind = Kind.SetVec; op.Field = Int(o, "Field", 0); op.Vector = Vv(o, "V", Vector3.Zero); break;
            case "ramp": op.Kind = Kind.Ramp; op.Rate = Num(o, "Rate", 0); op.StartTime = Num(o, "Start", 0); op.EndTime = Num(o, "End", 1); break;
            case "lerp": op.Kind = Kind.Lerp; op.Output = F(o, "Output", 1); op.StartTime = Num(o, "Start", 0); op.EndTime = Num(o, "End", 1); break;
            case "spinUpdate": op.Kind = Kind.SpinUpdate; break;
            case "spin": op.Kind = Kind.Spin; op.Rate = Num(o, "Rate", 0); op.MinRate = Num(o, "MinRate", 0); op.StopTime = Num(o, "StopTime", 0); break;
            case "cull": op.Kind = Kind.Cull; op.Fraction = Num(o, "Fraction", .5f); op.StartTime = Num(o, "Start", 0); op.EndTime = Num(o, "End", 1); break;
            default: return null;
        }
        return op;
    }

    /// <summary>The system a gun's model plays at the muzzle (null: none, or the data is not loaded).</summary>
    public static string RootFor(string gun, bool silenced) {
        if (s_guns is null || gun is null || !s_guns.TryGetValue(gun, out var modes)) return null;
        return silenced && modes.TryGetValue("silenced", out var s) ? s : modes.TryGetValue("default", out var d) ? d : null;
    }
    public static SystemSpec System(string name) => name is not null && s_systems is not null && s_systems.TryGetValue(name, out var s) ? s : null;
    public static Sheet SheetOf(string texture) => texture is not null && s_sheets is not null && s_sheets.TryGetValue(texture, out var t) ? t : null;
    public static IEnumerable<string> Guns => s_guns?.Keys ?? Enumerable.Empty<string>();
    public static IEnumerable<string> Systems => s_systems?.Keys ?? Enumerable.Empty<string>();
}
