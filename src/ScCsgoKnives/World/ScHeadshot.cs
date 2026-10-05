using Engine;
using Engine.Graphics;
using Engine.Media;
namespace Game;

/// <summary>Where a shot landed. Body is the torso or an unspecified part (every fallback hit); Arm and Leg are only
/// reported by models whose limbs are known (headshot-armor-balance-20260929 H1; appended, values 0-2 unchanged). Neck
/// and Stomach are CS2's own hit groups, reported only by a world's mode that brings CS2's hitboxes (deathmatch round 5,
/// ScMode.HitCapsules; appended, values 0-4 unchanged); everything else that reads a part treats them as the torso.</summary>
public enum ScHitPart { Unknown, Body, Head, Arm, Leg, Neck, Stomach }

/// <summary>One CS2-style hit volume in world space: every point within <paramref name="Radius"/> of the segment from
/// <paramref name="A"/> to <paramref name="B"/> (a capsule; A == B is a sphere). Nothing outside the capsules is hit:
/// the gaps between them are misses, as in CS2 (deathmatch round 5).</summary>
public readonly record struct ScHitCapsule(Vector3 A, Vector3 B, float Radius, ScHitPart Part) {
    /// <summary>Distance along the ray (unit <paramref name="direction"/>) where it enters the capsule, or null. A ray that
    /// starts inside enters at 0.</summary>
    public float? Intersect(Vector3 origin, Vector3 direction) {
        float r2 = Radius * Radius, best = float.MaxValue;
        Vector3 ab = B - A, ao = origin - A;
        float ll = Vector3.Dot(ab, ab);
        if (ll > 1e-12f) {
            // the infinite cylinder around AB, kept where the entry lies between the two caps
            float abd = Vector3.Dot(ab, direction), abao = Vector3.Dot(ab, ao);
            float a = ll - abd * abd, b = ll * Vector3.Dot(ao, direction) - abao * abd, c = ll * Vector3.Dot(ao, ao) - abao * abao - r2 * ll;
            if (a > 1e-12f) {
                float disc = b * b - a * c;
                if (disc >= 0) {
                    float t = (-b - MathF.Sqrt(disc)) / a, along = abao + t * abd;
                    if (along >= 0 && along <= ll) { if (t >= 0) best = t; else if (c <= 0) best = 0; }
                }
            }
        }
        // the two end spheres
        float Sphere(Vector3 centre) {
            Vector3 m = origin - centre; float mb = Vector3.Dot(m, direction), mc = Vector3.Dot(m, m) - r2;
            if (mc > 0 && mb > 0) return float.MaxValue;
            float disc = mb * mb - mc;
            return disc < 0 ? float.MaxValue : Math.Max(0, -mb - MathF.Sqrt(disc));
        }
        best = Math.Min(best, Math.Min(Sphere(A), Sphere(B)));
        return best < float.MaxValue ? best : null;
    }
    /// <summary>The nearest capsule along the ray within <paramref name="maxDistance"/>: its part and distance, or Unknown.</summary>
    public static (ScHitPart Part, float Distance) Resolve(IReadOnlyList<ScHitCapsule> capsules, Vector3 origin, Vector3 direction, float maxDistance) {
        if (!(direction.LengthSquared() > 1e-12f)) return (ScHitPart.Unknown, -1);
        Vector3 d = Vector3.Normalize(direction); float scale = direction.Length();
        ScHitPart part = ScHitPart.Unknown; float best = float.MaxValue;
        foreach (var capsule in capsules)
            if (capsule.Intersect(origin, d) is float t && t / scale <= maxDistance && t < best) { best = t; part = capsule.Part; }
        return part == ScHitPart.Unknown ? (part, -1) : (part, best / scale);
    }
}

/// <summary>One drawn mesh of a creature as a hit volume: the mesh's bone-local bounding box and
/// that bone's absolute (world) matrix, exactly the pair the renderer draws the mesh with. A non-head box reports
/// <paramref name="Region"/> (Body unless the model's limbs are known).</summary>
public readonly record struct ScPartBox(BoundingBox Local, Matrix World, bool Head, ScHitPart Region) {
    /// <summary>The original three-value form (reflection callers construct it with exactly three arguments).</summary>
    public ScPartBox(BoundingBox Local, Matrix World, bool Head) : this(Local, World, Head, ScHitPart.Body) { }
}

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
            if (t.HasValue && t.Value <= maxDistance && t.Value < best) { best = t.Value; part = box.Head ? ScHitPart.Head : box.Region is ScHitPart.Arm or ScHitPart.Leg ? box.Region : ScHitPart.Body; }
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
            string bone = data.Bones[mesh.ParentBoneIndex].Name; bool head = rule.IsHead(bone);
            parts.Add(new(head ? rule.Apply(mesh.BoundingBox) : mesh.BoundingBox, absolute[mesh.ParentBoneIndex], head, RigidRegion(bone)));
        }
        return parts;
    }
    /// <summary>Region of a rigid mesh by its bone: the vanilla human's Hand1/Hand2 are arms, Leg1..4 legs, the rest body.</summary>
    public static ScHitPart RigidRegion(string bone) => bone is null ? ScHitPart.Body
        : bone.StartsWith("Leg", StringComparison.Ordinal) ? ScHitPart.Leg
        : bone.StartsWith("Hand", StringComparison.Ordinal) || bone.StartsWith("Arm", StringComparison.Ordinal) ? ScHitPart.Arm : ScHitPart.Body;
    public static bool HasHeadMesh(ModelData data, ScHeadRule rule) => data.Meshes.Any(m => m.IsVisible && rule.IsHead(data.Bones[m.ParentBoneIndex].Name));
}
