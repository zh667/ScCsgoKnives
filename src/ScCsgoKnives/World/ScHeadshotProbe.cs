using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>Resolves which part of a live creature a pellet ray crosses, from the creature's current logical
/// pose. The vanilla renderer animates a model only on frames it is drawn (SubsystemModelsRenderer.PrepareModel
/// runs Animate once per frame for visible models), so a creature not drawn this frame has a stale pose and is
/// reported Unknown rather than judged against old bone matrices. The world bone matrices are recomposed here
/// from the model's own bone transforms; AbsoluteBoneTransformsForCamera is camera-relative and not used.</summary>
public static class ScHeadshotProbe {
    public static bool VanillaHeadClass(ComponentCreatureModel model) => model is ComponentHumanModel or ComponentFourLeggedModel or ComponentBirdModel or ComponentFlightlessBirdModel;
    public static bool HasHeadMesh(Model model, ScHeadRule rule) => model.Meshes.Any(m => m.IsVisible && rule.IsHead(m.ParentBone.Name));
    public static IEnumerable<ScPartBox> Parts(Model model, ScHeadRule rule, Matrix[] absolute) {
        foreach (var mesh in model.Meshes) {
            if (!mesh.IsVisible) continue;
            bool head = rule.IsHead(mesh.ParentBone.Name);
            yield return new(head ? rule.Apply(mesh.BoundingBox) : mesh.BoundingBox, absolute[mesh.ParentBone.Index], head);
        }
    }
    public static ScHitPart Resolve(ComponentBody body, Vector3 origin, Vector3 direction, float maxDistance, out float distance, out string reason) {
        distance = -1;
        var model = body?.Entity?.FindComponent<ComponentCreatureModel>();
        if (model?.Model is null || model.m_boneTransforms is null || model.m_boneTransforms.Length != model.Model.Bones.Count) { reason = "no creature model"; return ScHitPart.Unknown; }
        var rule = ScHeadRules.For(model.ModelRoute, VanillaHeadClass(model), HasHeadMesh(model.Model, ScHeadRule.Default));
        if (rule is null) { reason = "no head rule for " + (model.ModelRoute ?? model.GetType().Name); return ScHitPart.Unknown; }
        if (!model.IsVisibleForCamera) { reason = "pose not refreshed: not drawn this frame"; return ScHitPart.Unknown; }
        var absolute = new Matrix[model.Model.Bones.Count];
        model.ProcessBoneHierarchy(model.Model.RootBone, Matrix.Identity, absolute);
        var (part, t) = ScHeadshot.Resolve(Parts(model.Model, rule, absolute), origin, direction, maxDistance);
        distance = t;
        reason = part == ScHitPart.Unknown ? "outside every mesh box of " + model.ModelRoute : model.ModelRoute;
        return part;
    }
}
