using Engine;
using Engine.Graphics;
using Engine.Media;
namespace Game;

public enum ScHitPart { Unknown, Body, Head }

/// <summary>One drawn mesh of a creature as a hit volume: the mesh's bone-local bounding box and
/// that bone's absolute (world) matrix, exactly the pair the renderer draws the mesh with.</summary>
public readonly record struct ScPartBox(BoundingBox Local, Matrix World, bool Head);

/// <summary>Per-model headshot rule. HeadBones names the bones whose meshes count as the head;
/// Shrink scales those boxes about their centre (1 = the drawn mesh box).</summary>
public sealed record ScHeadRule(string[] HeadBones, Vector3 Shrink) {
    public static readonly ScHeadRule Default = new(["Head"], Vector3.One);
    public bool IsHead(string bone) => Array.IndexOf(HeadBones, bone) >= 0;
    public BoundingBox Apply(BoundingBox box) {
        if (Shrink == Vector3.One) return box;
        Vector3 c = box.Center(), h = box.Size() * .5f * Shrink;
        return new BoundingBox(c - h, c + h);
    }
}

/// <summary>Registry of head rules keyed by ComponentModel.ModelRoute (community plan M1b, 0.30.0).
/// Vanilla creature models were enumerated from Content.zip 1.9.2.1 with Engine.Media.Collada on
/// 2026-09-07: every one below carries a mesh on a bone named "Head"; the fish models have none.</summary>
public static class ScHeadRules {
    public static readonly string[] VanillaHeadModels = [
        "Models/Bear", "Models/PolarBear", "Models/Bison", "Models/Bull", "Models/Camel", "Models/Camel_Saddled", "Models/Cow",
        "Models/Donkey", "Models/Giraffe", "Models/Gnu", "Models/Horse", "Models/Hyena", "Models/Jaguar", "Models/Leopard",
        "Models/Lion", "Models/Moose", "Models/Reindeer", "Models/Rhino", "Models/Tiger", "Models/Wildboar", "Models/Wolf", "Models/Zebra",
        "Models/HumanMale", "Models/HumanFemale", "Models/Werewolf",
        "Models/Duck", "Models/Pigeon", "Models/Raven", "Models/Seagull", "Models/Sparrow",
        "Models/Cassowary", "Models/Ostrich",
    ];
    static readonly Dictionary<string, ScHeadRule> s_rules = new(StringComparer.OrdinalIgnoreCase);
    static ScHeadRules() { foreach (string route in VanillaHeadModels) s_rules[route] = ScHeadRule.Default; }
    /// <summary>Other mods register their creature models here; a null rule disables headshots for that model.</summary>
    public static void Register(string modelRoute, ScHeadRule rule) { lock (s_rules) s_rules[modelRoute] = rule; }
    public static bool IsRegistered(string modelRoute) { lock (s_rules) return modelRoute is not null && s_rules.ContainsKey(modelRoute); }
    /// <summary>The rule for a model: an explicit registration wins (even a disabling null); otherwise an
    /// unregistered model gets the default rule only when it uses a vanilla creature model class and
    /// actually has a mesh on a "Head" bone. Anything else is a plain body hit.</summary>
    public static ScHeadRule For(string modelRoute, bool vanillaHeadClass, bool hasHeadMesh) {
        lock (s_rules) if (modelRoute is not null && s_rules.TryGetValue(modelRoute, out var rule)) return rule;
        return vanillaHeadClass && hasHeadMesh ? ScHeadRule.Default : null;
    }
}

/// <summary>Pure headshot geometry (no engine objects beyond maths) so the same code runs in the game,
/// in the runtime self-test and in PackageCheck against the vanilla models.</summary>
public static class ScHeadshot {
    /// <summary>估计: the plan's first test point (2.0). Applied to the pellet's post-F13 power once; never to the Zeus.</summary>
    public const float Multiplier = 2f;
    public static float MultiplierFor(GunSpec gun) => gun is null || gun.RechargeSeconds > 0 ? 1 : Multiplier;
    static float Axis(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
    /// <summary>Ray parameter where the ray enters the box, or null. The ray is moved into the box's bone space,
    /// so the parameter is measured along the caller's (unnormalised) direction in world units.</summary>
    public static float? Intersect(in ScPartBox part, Vector3 origin, Vector3 direction) {
        Matrix inverse = Matrix.Invert(part.World);
        Vector3 o = Vector3.Transform(origin, inverse), d = Vector3.TransformNormal(direction, inverse);
        float near = 0, far = float.MaxValue;
        for (int axis = 0; axis < 3; axis++) {
            float oa = Axis(o, axis), da = Axis(d, axis), min = Axis(part.Local.Min, axis), max = Axis(part.Local.Max, axis);
            if (Math.Abs(da) < 1e-9f) { if (oa < min || oa > max) return null; continue; }
            float t1 = (min - oa) / da, t2 = (max - oa) / da;
            if (t1 > t2) (t1, t2) = (t2, t1);
            near = Math.Max(near, t1); far = Math.Min(far, t2);
            if (near > far) return null;
        }
        return float.IsFinite(near) ? near : null;
    }
    /// <summary>Nearest mesh box along the ray decides the part: a body mesh in front of the head is a body hit,
    /// a ray that crosses no mesh box at all (it only grazed the tolerant body AABB) is Unknown.</summary>
    public static (ScHitPart Part, float Distance) Resolve(IEnumerable<ScPartBox> parts, Vector3 origin, Vector3 direction, float maxDistance) {
        ScHitPart part = ScHitPart.Unknown; float best = float.MaxValue;
        foreach (var box in parts) {
            float? t = Intersect(box, origin, direction);
            if (t.HasValue && t.Value <= maxDistance && t.Value < best) { best = t.Value; part = box.Head ? ScHitPart.Head : ScHitPart.Body; }
        }
        return (part, part == ScHitPart.Unknown ? -1 : best);
    }
    /// <summary>Bind-pose absolute bone matrices composed the way ComponentModel.ProcessBoneHierarchy does when no
    /// animation transform is set: child × parent, with ModelScale applied at the root.</summary>
    public static Matrix[] ComposeBindPose(IReadOnlyList<(Matrix Transform, int Parent)> bones, float modelScale = 1) {
        var result = new Matrix[bones.Count]; var done = new bool[bones.Count];
        Matrix Absolute(int i) {
            if (done[i]) return result[i];
            Matrix m = bones[i].Transform;
            if (bones[i].Parent < 0 && modelScale != 1) m = Matrix.CreateScale(modelScale) * m;
            result[i] = bones[i].Parent < 0 ? m : m * Absolute(bones[i].Parent);
            done[i] = true; return result[i];
        }
        for (int i = 0; i < bones.Count; i++) Absolute(i);
        return result;
    }
    /// <summary>Hit volumes of a model file in its bind pose (tools and tests; the game uses the live pose).</summary>
    public static List<ScPartBox> PartsFromModelData(ModelData data, ScHeadRule rule, float modelScale = 1) {
        var absolute = ComposeBindPose(data.Bones.Select(b => (b.Transform, b.ParentBoneIndex)).ToList(), modelScale);
        var parts = new List<ScPartBox>();
        foreach (var mesh in data.Meshes) {
            if (!mesh.IsVisible) continue;
            bool head = rule.IsHead(data.Bones[mesh.ParentBoneIndex].Name);
            parts.Add(new(head ? rule.Apply(mesh.BoundingBox) : mesh.BoundingBox, absolute[mesh.ParentBoneIndex], head));
        }
        return parts;
    }
    public static bool HasHeadMesh(ModelData data, ScHeadRule rule) => data.Meshes.Any(m => m.IsVisible && rule.IsHead(data.Bones[m.ParentBoneIndex].Name));
}
