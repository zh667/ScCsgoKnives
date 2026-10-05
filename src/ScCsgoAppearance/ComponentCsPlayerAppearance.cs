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
    TacticalWorldGloves gloves;
    string gloveKey;
    int[] originalMeshOrders;
    public static bool OwnsKey(string key) => key is "zh667.cs.ct" or "zh667.cs.t";
    public bool IsCs => OwnsKey(ModelKey);
    public string FirstPersonRole => ModelKey == "zh667.cs.ct" ? "ct" : ModelKey == "zh667.cs.t" ? "t" : null;
    public CsPlayerPose Pose => pose;

    void IUpdateable.Update(float dt) { base.Update(dt); RefreshModel(); ScNetAppearance.Tick(this); }

    /// <summary>NEO's item-anchor buffer, kept valid for NMM's own held-item drawing on a model that is not ours
    /// (mp-user-logs-20261002: IndexOutOfRange in ComponentNeoModel.ItemAnchorPositions on both clients). NEO fills
    /// ComponentNeoModel.AbsoluteBoneTransforms only in its own Update, and NMM indexes it with the current model's bones
    /// when drawing. On a multiplayer client the platform's CompatNet mod skips every neorxna.dll component update of
    /// players other than the local one (ScopedUpdateGuardCompatNetAdapter 13), so for those players the array stays empty
    /// for ever; an entity that arrived by packet is also drawn once before any component updated. NEO allocates a new
    /// array on each of its updates, so "long enough and not ours" means NEO is live and nothing is touched; otherwise
    /// the same two steps NEO's update does are done here, with the pose of this draw. No NEO or NMM code is changed.</summary>
    Matrix[] m_neoItemBones;
    bool EnsureNeoItemBones(ComponentHumanModel human) {
        var model = human.Model; var neo = ComponentNeoModel;
        if (model is null || neo is null || human.m_boneTransforms is null || human.m_boneTransforms.Length < model.Bones.Count) return false;
        var current = neo.AbsoluteBoneTransforms;
        if (current is not null && current.Length >= model.Bones.Count && !ReferenceEquals(current, m_neoItemBones)) return true;
        if (m_neoItemBones is null || m_neoItemBones.Length != model.Bones.Count) m_neoItemBones = new Matrix[model.Bones.Count];
        human.ProcessBoneHierarchy(model.RootBone, Matrix.Identity, m_neoItemBones);
        neo.AbsoluteBoneTransforms = m_neoItemBones;
        return true;
    }
    void RefreshModel() {
        CsNeoBoneBuffer.Ensure(TargetComponent);
        if (IsCs) {
            ComponentNeoModel.FirstPersonModel = null;
            ComponentNeoModel.FirstPersonArms2Model = null;
        }
        if (ReferenceEquals(Model, previousModel) && active == IsCs) return;
        previousModel = Model;
        gloves=null;gloveKey=null;originalMeshOrders=null;
        if (IsCs) {
            pose = new CsPlayerPose(Model);
            // Tactical owns role-aware empty hands and CS held weapons own their FPP path.
            BoneTransforms.Clear();
        } else if (active) {
            pose = null;
            BoneTransforms.Clear();
        }
        active = IsCs;
    }
    bool INeoModel.ShouldOverride(ComponentModel component) => base.ShouldOverride(component);
    bool INeoModel.SetModel(Model model) { bool result = base.SetModel(model); RefreshModel(); return result; }
    readonly TacticalAirState m_air=new();
    bool INeoModel.Animate() {
        RefreshModel();
        if (!IsCs) return base.Animate();
        RefreshGloves();
        var human = (ComponentHumanModel)TargetComponent;
        // The grenade just thrown stays the presented item until its action ends (last of a stack, creative stacks).
        int value = ScThirdPerson.PresentedValue(human, out var throwing);
        bool shield = ScTacticalShieldBlock.IsShield(value);
        string asset = ScThirdPerson.AssetFor(value, out var stance);
        bool armed = asset != null;
        pose.Throw = throwing;
        pose.Plant = asset == "c4" ? ScThirdPerson.PlantPhaseOf(human) : default;
        // Same air rule as the NPC actors; creative flight is never a jump.
        var locomotion=Entity.FindComponent<ComponentLocomotion>();var health=Entity.FindComponent<ComponentHealth>();
        bool grounded=ComponentBody.StandingOnValue.HasValue||ComponentBody.StandingOnBody is not null||ComponentBody.ImmersionFactor>.3f
            ||locomotion?.LadderValue.HasValue==true||locomotion?.IsCreativeFlyEnabled==true||health?.Health<=0;
        // Once per game frame, however many cameras animate this player.
        m_air.Advance(Time.FrameIndex,Time.FrameDuration,grounded,ComponentBody.Velocity);
        pose.Motion=(m_air.Airborne,ComponentBody.CrouchFactor>.5f,m_air.Rising,m_air.Moving);
        // A held gun follows the look pitch (ScAgentActions.ApplyAimPitch); remote players' look angles are replicated.
        pose.Pitch = stance?.PitchFollows == true && locomotion is not null ? Math.Clamp(ScThirdPerson.PitchSign * locomotion.LookAngles.Y, -1.2f, 1.2f) : 0;
        pose.Sample(Time.FrameIndex, Time.FrameDuration, ComponentBody.Velocity.XZ.Length(), armed, shield,
            ComponentBody.CrouchFactor, Math.Max(human.DeathPhase, human.m_lieDownFactorModel),
            shield||human.DeathPhase>0?default:ScNetPresentation.ActionOf(Entity),asset);
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
            // NMM's own drawing needs NEO's bone buffer; without a usable one the engine's vanilla item drawing runs instead.
            return coreOwnsItem || ScTacticalShieldBlock.IsShield(held) || EnsureNeoItemBones(human) && base.DrawExtras(camera);
        }
        gloves?.Draw((ComponentHumanModel)TargetComponent,camera);
        if (ComponentCreature.ComponentHealth.Health <= 0 || camera.GameWidget.IsEntityFirstPersonTarget(Entity)) return true;
        int value = ScThirdPerson.PresentedValue((ComponentHumanModel)TargetComponent, out var throwing);
        if (value == 0 || ScTacticalShieldBlock.IsShield(value)) return true; // DLC owns the shield mesh.
        if (throwing.Active && throwing.Released) return true; // thrown: the hand is empty until the action ends
        if (ScThirdPerson.PlantPhaseOf((ComponentHumanModel)TargetComponent) is { Active: true, Placed: true }) return true; // planted: the charge is on the ground
        CsPlayerItems.Draw((ComponentHumanModel)TargetComponent, camera, value);
        return true;
    }
    bool INeoModel.Render(SubsystemModelsRenderer.ModelData data, ModelShader shader, Camera camera) => !IsCs && base.Render(data, shader, camera);
    void RefreshGloves(){
        string selected=ComponentPlayer==null?"":Project.FindSubsystem<SubsystemScTactical>(false)?.GloveFor(ComponentPlayer.PlayerData.PlayerIndex)??"";
        // NMM may reapply the same shared model and reset draw orders without changing its identity.
        if(gloveKey==selected){if(gloves!=null)TargetComponent.MeshDrawOrders=gloves.BodyMeshOrders;return;}
        originalMeshOrders??=TargetComponent.MeshDrawOrders.ToArray();
        gloves=null;TargetComponent.MeshDrawOrders=originalMeshOrders;
        var glove=TacticalArms.Gloves.FirstOrDefault(g=>g.Key==selected);
        if(glove!=null)try{gloves=new(Model,glove);TargetComponent.MeshDrawOrders=gloves.BodyMeshOrders;}
        catch(Exception e){KnifeDiagnostics.WarnOnce("world-glove-"+selected,e.Message);}
        gloveKey=selected;
    }
}

public sealed class CsPlayerPose {
    public Model Model { get; }
    public Matrix?[] Local { get; }
    readonly AnimationController controller;
    /// <summary>Air state for the jump/in-air clips, set before <see cref="Sample"/> each frame.</summary>
    public (bool Airborne, bool Crouch, bool Rising, bool Moving) Motion;
    /// <summary>The throw in progress, set before <see cref="Sample"/> each frame (default: none).</summary>
    public ScThrowPhase Throw;
    /// <summary>The C4 being planted, set before <see cref="Sample"/> each frame (default: none).</summary>
    public ScPlantPhase Plant;
    /// <summary>The look pitch a held gun follows, set before <see cref="Sample"/> each frame (radians, up positive).</summary>
    public float Pitch;
    /// <summary>The actor has real crouch clips (r2-c4-completion-20260929); older caches keep the squash.</summary>
    readonly bool crouchClips;
    public ScAgentActions Actions { get; }
    int frame = -1;
    public CsPlayerPose(Model model) {
        Model = model;
        Local = new Matrix?[model.Bones.Count];
        Actions=new(model);
        crouchClips=model.Animations.Any(a=>a.Name=="crouch");
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
        controller.Parameters.SetBool("Airborne", Motion.Airborne); controller.Parameters.SetBool("Crouch", Motion.Crouch);
        controller.Parameters.SetBool("AirRising", Motion.Rising); controller.Parameters.SetBool("AirMoving", Motion.Moving);
        // Keep the living pose during the collapse instead of falling back to the bind pose.
        controller.Parameters.SetBool("IsDead", false);
        controller.Update(first ? .2f : Math.Clamp(dt, 0, .1f));
        Array.Clear(Local);
        controller.ComputeBoneTransforms(Local);
        if(lie<=0&&!shield&&!Actions.ApplyThrowPosed(Local,heldAsset??action.Asset,Throw,crouch)&&!(Plant.Active&&Actions.ApplyPlant(Local,Plant,crouch))){
            Actions.ApplyHeld(Local,action,heldAsset??action.Asset);
            Actions.ApplyAimPitch(Local,Pitch,heldAsset??action.Asset);
        }
        Matrix root = Local[Model.RootBone.Index] ?? Model.RootBone.Transform;
        root = Matrix.CreateFromQuaternion(controller.EffectiveRootRotation) * root;
        // First edition: a bounded crouch compression and lay-down adaptation, without physics edits.
        float c = Math.Clamp(crouch, 0, 1), l = Math.Clamp(lie, 0, 1);
        if (!crouchClips) root *= Matrix.CreateScale(1, 1 - .38f * c, 1);
        root *= Matrix.CreateRotationX(MathF.PI * .5f * l) * Matrix.CreateTranslation(0, .28f * l, 0);
        Local[Model.RootBone.Index] = root;
    }
}
