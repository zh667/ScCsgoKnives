using Engine;
using Engine.Graphics;
namespace Game;

public sealed class ComponentTacticalModel : ComponentCreatureModel {
    Matrix?[] lastLivingPose;
    ScAgentActions actions;
    Model actionModel;
    Matrix?[] framePose;
    int animatedFrame=-1;
    float animatedDeath=-1;
    bool animatedAlive;
    Model animatedModel;
    ScWeaponAction animatedAction;
    int animatedValue;
    int HeldValue => Entity.FindComponent<ComponentTacticalEnemy>()?.State?.DisplayValue??Entity.FindComponent<ComponentTacticalInventory>()?.GetSlotValue(0)??0;
    ScAgentActions Actions {get {if(actionModel!=Model){actionModel=Model;actions=new(Model);}return actions;}}
    public ScWeaponAction VisualAction=>Entity.FindComponent<ComponentTacticalEnemy>()?.VisualAction??Entity.FindComponent<ComponentTacticalCompanion>()?.VisualAction??default;
    public override void AnimateCreature(){}
    public override void Animate(){
        var action=VisualAction;
        bool alive=m_componentCreature.ComponentHealth.Health>0;
        if(animatedFrame==Time.FrameIndex&&animatedDeath==DeathPhase&&animatedAlive==alive&&animatedModel==Model&&animatedAction==action&&animatedValue==HeldValue&&framePose!=null){
            Array.Copy(framePose,m_boneTransforms,framePose.Length);return;
        }
        base.Animate();
        if(m_componentCreature.ComponentHealth.Health<=0&&m_boneTransforms[Model.RootBone.Index] is Matrix root){
            // Freeze the last living pose so the dead state cannot snap to the bind pose.
            if(lastLivingPose!=null)for(int i=0;i<m_boneTransforms.Length;i++)if(i!=Model.RootBone.Index)m_boneTransforms[i]=lastLivingPose[i];
            float t=Math.Clamp(DeathPhase,0,1),ease=t*t*(3-2*t);
            Bend("spine_1",.08f*ease);Bend("neck_0",.16f*ease);
            var body=m_componentCreature.ComponentBody;var pos=body.Position;
            foreach(var side in new[]{("L",-1f),("R",1f)}){
                RelaxLimb("arm_upper_"+side.Item1,"arm_lower_"+side.Item1,pos+body.Matrix.Right*(side.Item2*.48f)+Vector3.UnitY*1.05f,ease);
                RelaxLimb("arm_lower_"+side.Item1,"hand_"+side.Item1,pos+body.Matrix.Right*(side.Item2*.40f)+Vector3.UnitY*.75f,ease);
            }
            var away=new Vector3(-DeathCauseOffset.X,0,-DeathCauseOffset.Z);
            if(away.LengthSquared()<.001f)away=-body.Matrix.Forward;else away=Vector3.Normalize(away);
            var axis=Vector3.Normalize(Vector3.Cross(Vector3.UnitY,away));
            m_boneTransforms[Model.RootBone.Index]=root*Matrix.CreateTranslation(-pos)*Matrix.CreateFromAxisAngle(axis,MathF.PI*.5f*ease)*Matrix.CreateTranslation(pos+Vector3.UnitY*(.43f*ease));
        }else{
            if(HeldValue!=0&&!ScTacticalShieldBlock.IsShield(HeldValue))Actions.ApplyHeld(m_boneTransforms,action,ScThirdPerson.AssetFor(HeldValue,out _));
            lastLivingPose??=new Matrix?[m_boneTransforms.Length];Array.Copy(m_boneTransforms,lastLivingPose,m_boneTransforms.Length);
        }
        if(framePose?.Length!=m_boneTransforms.Length)framePose=new Matrix?[m_boneTransforms.Length];
        Array.Copy(m_boneTransforms,framePose,framePose.Length);animatedFrame=Time.FrameIndex;animatedDeath=DeathPhase;animatedAlive=alive;animatedModel=Model;animatedAction=action;animatedValue=HeldValue;
    }
    void Bend(string name,float radians){var bone=Model.FindBone(name,false);if(bone==null)return;var local=m_boneTransforms[bone.Index]??bone.Transform;var position=local.Translation;local.Translation=Vector3.Zero;m_boneTransforms[bone.Index]=Matrix.CreateRotationZ(radians)*local*Matrix.CreateTranslation(position);}
    void RelaxLimb(string name,string childName,Vector3 goal,float amount){
        var bone=Model.FindBone(name,false);var child=Model.FindBone(childName,false);if(bone?.ParentBone==null||child==null)return;
        ProcessBoneHierarchy(Model.RootBone,Matrix.Identity,AbsoluteBoneTransformsForCamera);
        var world=AbsoluteBoneTransformsForCamera[bone.Index];var parent=AbsoluteBoneTransformsForCamera[bone.ParentBone.Index];
        var from=Vector3.Normalize(AbsoluteBoneTransformsForCamera[child.Index].Translation-world.Translation);var to=Vector3.Normalize(goal-world.Translation);
        var axis=Vector3.Cross(from,to);if(axis.LengthSquared()<1e-8f)return;
        var rotation=Matrix.CreateFromAxisAngle(Vector3.Normalize(axis),MathF.Acos(Math.Clamp(Vector3.Dot(from,to),-1,1))*amount);
        var local=m_boneTransforms[bone.Index]??bone.Transform;var translation=local.Translation;world.Translation=parent.Translation=Vector3.Zero;
        local=world*rotation*Matrix.Invert(parent);local.Translation=translation;m_boneTransforms[bone.Index]=local;
    }
    public override void SyncAnimationParameters(){base.SyncAnimationParameters();AnimationController?.Parameters.SetFloat("DeathSpeed",1.2f);var inv=Entity.FindComponent<ComponentTacticalInventory>();AnimationController?.Parameters.SetBool("Armed",Entity.FindComponent<ComponentTacticalEnemy>()?.State!=null||inv?.GetSlotCount(0)>0);AnimationController?.Parameters.SetBool("Shield",inv?.GetSlotCount(0)>0&&ScTacticalShieldBlock.IsShield(inv.GetSlotValue(0)));}
    public override void DrawExtras(Camera camera){
        base.DrawExtras(camera);if(m_componentCreature.ComponentHealth.Health<=0)return;
        if(Entity.FindComponent<ComponentTacticalEnemy>()?.State is {} enemy){DrawGun(camera,enemy.DisplayValue);return;}
        var inv=Entity.FindComponent<ComponentTacticalInventory>();if(inv is null||inv.GetSlotCount(0)<=0)return;
        int value=inv.GetSlotValue(0);var block=BlocksManager.Blocks[Terrain.ExtractContents(value)];Matrix world;
        bool isShield=ScTacticalShieldBlock.IsShield(value);
        if(isShield)world=ScShieldProtection.Pose(m_componentCreature.ComponentBody);
        else{DrawGun(camera,value);return;}
        var terrain=Project.FindSubsystem<SubsystemTerrain>(true);var pos=world.Translation;
        var env=new DrawBlockEnvironmentData{DrawBlockMode=DrawBlockMode.ThirdPerson,InWorldMatrix=world,Owner=Entity,SubsystemTerrain=terrain,Light=terrain.Terrain.GetCellLight(Terrain.ToCell(pos.X),Terrain.ToCell(pos.Y),Terrain.ToCell(pos.Z))};
        var matrix=world*camera.ViewMatrix;block.DrawBlock(Project.FindSubsystem<SubsystemModelsRenderer>(true).PrimitivesRenderer,value,Color.White,ScTacticalShieldBlock.IsShield(value)?1:block.GetInHandScale(value),ref matrix,env);
    }
    void DrawGun(Camera camera,int value){
        if(!EffectiveGunStats.TrySnapshotValue(value,out var state))return;
        string asset=GunSpec.All[state.Variant].Name;var native=ScGunNativeMesh.Resolve(asset,state.SkinId,out var texture,out _);
        var weapon=ScThirdPersonWeapon.For(asset,native is not null);var hand=Model.FindBone("hand_R",false);
        if(weapon is null||!weapon.HasRightGrip||hand is null)return;
        var world=Actions.RootWorld(weapon,AbsoluteBoneTransformsForCamera)*camera.InvertedViewMatrix;
        var terrain=Project.FindSubsystem<SubsystemTerrain>(true);var p=world.Translation;
        var env=new DrawBlockEnvironmentData{DrawBlockMode=DrawBlockMode.ThirdPerson,InWorldMatrix=world,Owner=Entity,SubsystemTerrain=terrain,Light=terrain.Terrain.GetCellLight(Terrain.ToCell(p.X),Terrain.ToCell(p.Y),Terrain.ToCell(p.Z))};
        var view=world*camera.ViewMatrix;var renderer=Project.FindSubsystem<SubsystemModelsRenderer>(true).PrimitivesRenderer;
        foreach(var group in weapon.Groups){if(group.Silencer&&state.SilencerOff||!Actions.ShowWorldPart(weapon,group,VisualAction))continue;var tex=group.Texture==asset+"_hd"?texture:ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/"+group.Texture);
            var part=Actions.WorldPart(weapon,group,AbsoluteBoneTransformsForCamera);var partView=part.Transform;
            if(native is not null)ScGunNativeMesh.DrawWorld(renderer,part.Mesh,tex,Color.White,1,ref partView,env);else BlocksManager.DrawMeshBlock(renderer,part.Mesh,tex,Color.White,1,ref partView,env);}
        ScStatTrakRenderer.DrawThirdPerson(value,asset,native is not null,view,renderer,LightingManager.LightIntensityByLightValue[Math.Clamp(env.Light,0,15)]);
    }
}
