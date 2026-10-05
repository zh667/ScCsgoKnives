using System.Runtime.CompilerServices;
using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>A creature component that poses its model for this frame's hit test without advancing its animation and
/// without any camera (headshot-armor-balance-20260929 H1). Fills local bone transforms the way
/// ComponentModel.m_boneTransforms holds them after Animate: the root carries the entity's world transform.</summary>
public interface IScLogicalPose {
    Model LogicalModel { get; }
    bool TryLogicalPose(Matrix?[] local);
}

/// <summary>Hit volumes of a skinned character whose skeleton is known (the CS2 CT/T actors: pelvis, spine_*, neck_0,
/// head_0, arm_*, hand_*, finger_*, leg_*, ankle_*, ball_*). Each joint gets one box around the vertices it dominates,
/// measured once in that joint's own space from the model's skin (vertex positions through the inverse bind matrices),
/// so the box follows the joint exactly as the renderer's skinning moves those vertices. Weapon props (cswp_*) are not
/// the body. The head box is the trimmed core of the head joint's vertices (3rd-97th percentile, shrunk to 92%): the
/// CT's helmet shell and straps are not all skull. Models whose skeleton is not recognised get nothing here and keep
/// the physics-body fallback. Every other joint's vertices are split along their longest axis while that tightens the
/// volume (at most 8 boxes a joint): one box around a CT's spine_2, whose vertices include the SAS pack, reached past the
/// head from behind and the sides and hid it from every side when crouched (c05).</summary>
public static class ScSkinnedHitRegions {
    sealed class Regions { public int[] Bones = []; public BoundingBox[] Boxes = []; public ScHitPart[] Parts = []; public bool Known; }
    static readonly ConditionalWeakTable<Model, Regions> s_regions = new();
    public const float HeadTrim = .03f, HeadShrink = .92f, BodyTrim = .01f, SplitGain = .7f;
    public const int MinVertices = 12, SplitDepth = 3;
    /// <summary>Region of a joint: the first recognised name walking up from it (a finger is an arm, a jaw the head).</summary>
    public static ScHitPart PartOf(ModelBone bone) {
        for (var b = bone; b != null; b = b.ParentBone) {
            string n = b.Name ?? "";
            if (n.StartsWith("cswp_", StringComparison.Ordinal) || n == "cs_weapon_mount") return ScHitPart.Unknown;
            if (n.StartsWith("head_", StringComparison.Ordinal)) return ScHitPart.Head;
            if (n.StartsWith("neck_", StringComparison.Ordinal) || n.StartsWith("spine_", StringComparison.Ordinal) || n == "pelvis" || n.StartsWith("clavicle_", StringComparison.Ordinal)) return ScHitPart.Body;
            if (n.StartsWith("arm_", StringComparison.Ordinal) || n.StartsWith("hand_", StringComparison.Ordinal) || n.StartsWith("finger_", StringComparison.Ordinal)) return ScHitPart.Arm;
            if (n.StartsWith("leg_", StringComparison.Ordinal) || n.StartsWith("ankle_", StringComparison.Ordinal) || n.StartsWith("ball_", StringComparison.Ordinal)) return ScHitPart.Leg;
        }
        return ScHitPart.Unknown;
    }
    public static bool Known(Model model) => model?.Skin is not null && s_regions.GetValue(model, Build).Known;
    /// <summary>Joint boxes (bone index, joint-space box, region) for tools and tests.</summary>
    public static IEnumerable<(int Bone, BoundingBox Box, ScHitPart Part)> Boxes(Model model) {
        var r = s_regions.GetValue(model, Build);
        for (int i = 0; i < r.Bones.Length; i++) yield return (r.Bones[i], r.Boxes[i], r.Parts[i]);
    }
    /// <summary>World hit boxes for absolute (world) bone matrices of this model.</summary>
    public static IEnumerable<ScPartBox> Parts(Model model, Matrix[] absolute) {
        var r = s_regions.GetValue(model, Build);
        if (!r.Known) yield break;
        for (int i = 0; i < r.Bones.Length; i++)
            yield return new(r.Boxes[i], absolute[r.Bones[i]], r.Parts[i] == ScHitPart.Head, r.Parts[i]);
    }
    static Regions Build(Model model) {
        var result = new Regions();
        try {
            var skin = model.Skin; var data = model.ModelData;
            if (skin is null || data is null || skin.Joints is not { Count: > 0 } || skin.InverseBindMatrices is null) return result;
            bool Has(string prefix) => model.Bones.Any(b => b.Name?.StartsWith(prefix, StringComparison.Ordinal) == true);
            if (!Has("head_") || !Has("spine_") || !Has("pelvis") || !Has("arm_") || !Has("leg_")) return result;
            var points = new List<Vector3>[skin.Joints.Count]; var bind = new List<Vector3>[skin.Joints.Count];
            string positionName = new VertexElement(0, VertexElementFormat.Vector3, VertexElementSemantic.Position).Semantic;
            string jointsName = new VertexElement(0, VertexElementFormat.Vector4, VertexElementSemantic.BlendIndices).Semantic;
            string weightsName = new VertexElement(0, VertexElementFormat.Vector4, VertexElementSemantic.BlendWeights).Semantic;
            foreach (var buffer in data.Buffers) {
                var elements = buffer.VertexDeclaration.VertexElements;
                var position = elements.FirstOrDefault(e => e.Semantic == positionName);
                var joints = elements.FirstOrDefault(e => e.Semantic == jointsName);
                var weights = elements.FirstOrDefault(e => e.Semantic == weightsName);
                if (position is null || joints is null || weights is null || joints.Format != VertexElementFormat.Vector4 || weights.Format != VertexElementFormat.Vector4) continue;
                int stride = buffer.VertexDeclaration.VertexStride, count = buffer.Vertices.Length / stride;
                var bytes = buffer.Vertices;
                float F(int offset) => BitConverter.ToSingle(bytes, offset);
                for (int v = 0; v < count; v++) {
                    int o = v * stride; int best = -1; float weight = 0;
                    for (int k = 0; k < 4; k++) { float w = F(o + weights.Offset + 4 * k); if (w > weight) { weight = w; best = (int)MathF.Round(F(o + joints.Offset + 4 * k)); } }
                    if (best < 0 || best >= points.Length || weight < .35f) continue;
                    var p = new Vector3(F(o + position.Offset), F(o + position.Offset + 4), F(o + position.Offset + 8));
                    (points[best] ??= []).Add(Vector3.Transform(p, skin.InverseBindMatrices[best])); (bind[best] ??= []).Add(p);
                }
            }
            var bones = new List<int>(); var boxes = new List<BoundingBox>(); var parts = new List<ScHitPart>();
            // Gear is not body (CS2's hitboxes follow the bones): a torso or limb joint keeps no vertex above the head's chin
            // or inside the head, measured in the bind pose, so pose-independent. The CT's pack and radio antenna are skinned
            // to spine_2 and reached above its head from behind (c07 overlay); a T's hood rim likewise.
            int headJoint = Enumerable.Range(0, points.Length).FirstOrDefault(j => skin.Joints[j]?.Name?.StartsWith("head_", StringComparison.Ordinal) == true && bind[j] is { Count: >= MinVertices }, -1);
            if (headJoint >= 0) {
                var chin = Trimmed(bind[headJoint], HeadTrim); var near = new BoundingBox(chin.Min - new Vector3(.03f), chin.Max + new Vector3(.03f));
                for (int j = 0; j < points.Length; j++) {
                    if (j == headJoint || bind[j] is null || skin.Joints[j] is null || PartOf(skin.Joints[j]) == ScHitPart.Head) continue;
                    var keep = new List<Vector3>(); var keepBind = new List<Vector3>();
                    for (int i = 0; i < bind[j].Count; i++) {
                        var q = bind[j][i];
                        if (q.Y > chin.Min.Y || q.X >= near.Min.X && q.X <= near.Max.X && q.Y >= near.Min.Y && q.Y <= near.Max.Y && q.Z >= near.Min.Z && q.Z <= near.Max.Z) continue;
                        keep.Add(points[j][i]); keepBind.Add(q);
                    }
                    points[j] = keep; bind[j] = keepBind;
                }
            }
            for (int j = 0; j < points.Length; j++) {
                var bone = skin.Joints[j]; var list = points[j];
                if (bone is null || list is null || list.Count < MinVertices) continue;
                var part = PartOf(bone); if (part == ScHitPart.Unknown) continue;
                bool head = part == ScHitPart.Head && bone.Name?.StartsWith("head_", StringComparison.Ordinal) == true;
                if (head) {
                    var box = Trimmed(list, HeadTrim); Vector3 c = box.Center(), h = box.Size() * .5f * HeadShrink;
                    bones.Add(bone.Index); boxes.Add(new(c - h, c + h)); parts.Add(part);
                } else foreach (var box in Split(list, 0)) { bones.Add(bone.Index); boxes.Add(box); parts.Add(part); }
            }
            result.Bones = bones.ToArray(); result.Boxes = boxes.ToArray(); result.Parts = parts.ToArray();
            result.Known = parts.Contains(ScHitPart.Head) && parts.Contains(ScHitPart.Body) && parts.Contains(ScHitPart.Arm) && parts.Contains(ScHitPart.Leg);
        } catch (Exception e) { KnifeDiagnostics.WarnOnce("skinned-hit-regions", "skinned hit regions unavailable; physics-body fallback: " + e.Message); result = new(); }
        return result;
    }
    static float Volume(BoundingBox b) { var d = b.Size(); return Math.Max(d.X, 1e-4f) * Math.Max(d.Y, 1e-4f) * Math.Max(d.Z, 1e-4f); }
    /// <summary>A joint's vertices as one box, or split at the median of the longest axis while the two halves' boxes
    /// together take less than SplitGain of the whole (the joint's geometry is not box-shaped there).</summary>
    static IEnumerable<BoundingBox> Split(List<Vector3> points, int depth) {
        var box = Trimmed(points, BodyTrim);
        if (depth >= SplitDepth || points.Count < 2 * MinVertices) { yield return box; yield break; }
        var size = box.Size(); Func<Vector3, float> key = size.X >= size.Y && size.X >= size.Z ? p => p.X : size.Y >= size.Z ? p => p.Y : p => p.Z;
        var sorted = points.OrderBy(key).ToList(); var low = sorted.GetRange(0, sorted.Count / 2); var high = sorted.GetRange(sorted.Count / 2, sorted.Count - sorted.Count / 2);
        if (Volume(Trimmed(low, BodyTrim)) + Volume(Trimmed(high, BodyTrim)) > SplitGain * Volume(box)) { yield return box; yield break; }
        foreach (var b in Split(low, depth + 1)) yield return b;
        foreach (var b in Split(high, depth + 1)) yield return b;
    }
    static BoundingBox Trimmed(List<Vector3> points, float share) {
        float[] Axis(Func<Vector3, float> f) { var a = points.Select(f).ToArray(); Array.Sort(a); return a; }
        float Lo(float[] a) => a[Math.Clamp((int)(share * (a.Length - 1)), 0, a.Length - 1)];
        float Hi(float[] a) => a[Math.Clamp((int)MathF.Ceiling((1 - share) * (a.Length - 1)), 0, a.Length - 1)];
        var x = Axis(p => p.X); var y = Axis(p => p.Y); var z = Axis(p => p.Z);
        return new(new Vector3(Lo(x), Lo(y), Lo(z)), new Vector3(Hi(x), Hi(y), Hi(z)));
    }
}

/// <summary>Hit volumes of a player, from logical game state only (headshot-armor-balance-20260929 H1). A player's own
/// body model is not animated at all while its owner looks through its eyes (SubsystemModelsRenderer animates only
/// models a camera draws), so a skeleton pose would be stale exactly when enemies shoot the local player. The head is
/// placed on the eye the first-person camera uses (ComponentHumanModel.CalculateEyePosition: crouch and lying down
/// included), the legs up to the hip, the torso between; oriented by the body's yaw. The same for every appearance
/// (vanilla male/female, CS CT/T, NMM and other player models), so it never depends on which model is worn.
/// Proportions of the standing box height H (估计, from the vanilla human model): head from 0.065 H below to 0.125 H
/// above the eye, 0.15 H wide; torso 0.27 H wide, 0.15 H deep; legs 0.2 H wide.</summary>
public static class ScPlayerHitRegions {
    public const float HeadBelowEye = .065f, HeadAboveEye = .125f, HeadWidth = .15f, TorsoWidth = .27f, TorsoDepth = .15f, LegWidth = .2f, StandingHip = .47f, CrouchedHip = .24f;
    /// <summary>Boxes in the body's yaw frame; null while lying down (the fallback body box is used then).</summary>
    public static ScPartBox[] Parts(ComponentBody body, Vector3 eye) {
        float h = body.BoxSize.Y; if (!(h > .2f) || !float.IsFinite(eye.X + eye.Y + eye.Z)) return null;
        var feet = body.Position; float eyeHeight = eye.Y - feet.Y;
        if (eyeHeight < .3f * h) return null; // lying down or sliding: no upright body to divide
        float crouch = Math.Clamp((.875f * h - eyeHeight) / (.875f * h * .55f), 0, 1);
        float hip = MathUtils.Lerp(StandingHip, CrouchedHip, crouch) * h;
        Matrix yaw = Matrix.CreateFromQuaternion(body.Rotation); yaw.Translation = Vector3.Zero;
        Vector3 eyeLocal = Vector3.TransformNormal(eye - feet, Matrix.Transpose(yaw));
        Matrix world = yaw * Matrix.CreateTranslation(feet);
        float headLow = eyeLocal.Y - HeadBelowEye * h, headHigh = eyeLocal.Y + HeadAboveEye * h, headHalf = HeadWidth * h * .5f;
        var head = new BoundingBox(new Vector3(eyeLocal.X - headHalf, headLow, eyeLocal.Z - headHalf * .6f), new Vector3(eyeLocal.X + headHalf, headHigh, eyeLocal.Z + headHalf * 1.4f));
        float torsoHalf = TorsoWidth * h * .5f, depthHalf = TorsoDepth * h * .5f;
        var torso = new BoundingBox(new Vector3(-torsoHalf, hip, eyeLocal.Z - depthHalf), new Vector3(torsoHalf, Math.Max(hip + .05f, headLow), eyeLocal.Z + depthHalf));
        float legHalf = LegWidth * h * .5f;
        var legs = new BoundingBox(new Vector3(-legHalf, 0, -depthHalf), new Vector3(legHalf, hip, depthHalf));
        return [new(head, world, true, ScHitPart.Head), new(torso, world, false, ScHitPart.Body), new(legs, world, false, ScHitPart.Leg)];
    }
}
