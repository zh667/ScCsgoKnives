using Engine;
using Engine.Animation;
using Engine.Graphics;
using NekoMeko.Components;
using Neorxna.NeoModel;

namespace Game;

// Reimplement the interface, preserving NMM's concrete component type and its selection/save APIs.
// In particular, SetResModel calls the nonvirtual NMM SetModel; observe model identity on every
// update/animate instead of relying on interception of that call.
public sealed class ComponentCsPlayerAppearance : ComponentNekoMekoModel, INeoModel, IUpdateable, IScFirstPersonAppearance {
    Model previousModel;
    bool active;
    CsPlayerPose pose;
    public static bool OwnsKey(string key) => key is "zh667.cs.ct" or "zh667.cs.t";
    public bool IsCs => OwnsKey(ModelKey);
    public string FirstPersonRole => ModelKey == "zh667.cs.ct" ? "ct" : ModelKey == "zh667.cs.t" ? "t" : null;
    public CsPlayerPose Pose => pose;

    void IUpdateable.Update(float dt) { base.Update(dt); RefreshModel(); }
    void RefreshModel() {
        if (IsCs) {
            ComponentNeoModel.FirstPersonModel = null;
            ComponentNeoModel.FirstPersonArms2Model = null;
        }
        if (ReferenceEquals(Model, previousModel) && active == IsCs) return;
        previousModel = Model;
        if (IsCs) {
            pose = new CsPlayerPose(Model);
            // Empty hands use the game's ordinary empty-hand path; CS held weapons still own FPP.
            BoneTransforms.Clear();
        } else if (active) {
            pose = null;
            BoneTransforms.Clear();
        }
        active = IsCs;
    }
    bool INeoModel.ShouldOverride(ComponentModel component) => base.ShouldOverride(component);
    bool INeoModel.SetModel(Model model) { bool result = base.SetModel(model); RefreshModel(); return result; }
    bool INeoModel.Animate() {
        RefreshModel();
        if (!IsCs) return base.Animate();
        var human = (ComponentHumanModel)TargetComponent;
        int value = ComponentMiner?.ActiveBlockValue ?? 0;
        bool shield = ScTacticalShieldBlock.IsShield(value);
        string asset = ScThirdPerson.AssetFor(value, out _);
        bool armed = asset != null;
        pose.Sample(Time.FrameIndex, Time.FrameDuration, ComponentBody.Velocity.XZ.Length(), armed, shield,
            ComponentBody.CrouchFactor, Math.Max(human.DeathPhase, human.m_lieDownFactorModel),
            shield||human.DeathPhase>0?default:KnifeAnimationController.ReadAction(Entity.FindComponent<ComponentFirstPersonModel>()),asset);
        Array.Copy(pose.Local, TargetComponent.m_boneTransforms, pose.Local.Length);
        // NMM retains the original human controller. Cancel only its root correction, which the
        // native creature renderer appends next; never replace its controller or event subscriptions.
        if (TargetComponent.AnimationController is {} original) {
            var correction = Matrix.CreateFromQuaternion(original.EffectiveRootRotation) * Matrix.CreateTranslation(original.EffectiveRootTranslation);
            TargetComponent.m_boneTransforms[Model.RootBone.Index] = Matrix.Invert(correction) * pose.Local[Model.RootBone.Index];
        }
        // Native CreatureModel.Animate appends the entity transform after INeoModel returns.
        TargetComponent.TextureOverride = null;
        BoneTransforms.Clear();
        foreach (var bone in Model.Bones) BoneTransforms[bone.Name] = pose.Local[bone.Index] ?? bone.Transform;
        return true;
    }
    bool INeoModel.DrawExtras(Camera camera) {
        if (!IsCs) {
            // The CS core/DLC already draw these items on ordinary NMM skeletons.
            int held = ComponentMiner?.ActiveBlockValue ?? 0;
            var human = (ComponentHumanModel)TargetComponent;
            bool coreOwnsItem = !Model.HasSkin && human.m_bodyBone != null && human.m_hand1Bone != null && human.m_hand2Bone != null && ScThirdPerson.AssetFor(held, out _) != null;
            return coreOwnsItem || ScTacticalShieldBlock.IsShield(held) || base.DrawExtras(camera);
        }
        if (ComponentCreature.ComponentHealth.Health <= 0 || camera.GameWidget.IsEntityFirstPersonTarget(Entity)) return true;
        int value = ComponentMiner?.ActiveBlockValue ?? 0;
        if (value == 0 || ScTacticalShieldBlock.IsShield(value)) return true; // DLC owns the shield mesh.
        CsPlayerItems.Draw((ComponentHumanModel)TargetComponent, camera, value);
        return true;
    }
    bool INeoModel.Render(SubsystemModelsRenderer.ModelData data, ModelShader shader, Camera camera) => !IsCs && base.Render(data, shader, camera);
}

public sealed class CsPlayerPose {
    public Model Model { get; }
    public Matrix?[] Local { get; }
    readonly AnimationController controller;
    public ScAgentActions Actions { get; }
    int frame = -1;
    public CsPlayerPose(Model model) {
        Model = model;
        Local = new Matrix?[model.Bones.Count];
        Actions=new(model);
        var loader = new AnimationConfigLoader();
        controller = loader.CreateController(loader.LoadFromJsonNode(System.Text.Json.Nodes.JsonNode.Parse(
            ContentManager.Get<string>("Animations/ScTactical", ".json"))), model);
    }
    public void Sample(int frameIndex, float dt, float speed, bool armed, bool shield, float crouch, float lie, ScWeaponAction action=default,string heldAsset=null) {
        if (frame == frameIndex) return;
        bool first = frame < 0;
        frame = frameIndex;
        controller.Parameters.SetFloat("SpeedAbs", speed);
        controller.Parameters.SetBool("Armed", armed);
        controller.Parameters.SetBool("Shield", shield);
        // Keep the living pose during the collapse instead of falling back to the bind pose.
        controller.Parameters.SetBool("IsDead", false);
        controller.Update(first ? .2f : Math.Clamp(dt, 0, .1f));
        Array.Clear(Local);
        controller.ComputeBoneTransforms(Local);
        if(lie<=0&&!shield)Actions.ApplyHeld(Local,action,heldAsset??action.Asset);
        Matrix root = Local[Model.RootBone.Index] ?? Model.RootBone.Transform;
        root = Matrix.CreateFromQuaternion(controller.EffectiveRootRotation) * root;
        // First edition: a bounded crouch compression and lay-down adaptation, without physics edits.
        float c = Math.Clamp(crouch, 0, 1), l = Math.Clamp(lie, 0, 1);
        root *= Matrix.CreateScale(1, 1 - .38f * c, 1);
        root *= Matrix.CreateRotationX(MathF.PI * .5f * l) * Matrix.CreateTranslation(0, .28f * l, 0);
        Local[Model.RootBone.Index] = root;
    }
}
