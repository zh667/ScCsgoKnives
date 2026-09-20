using Engine;
using Engine.Graphics;
namespace Game;

public sealed class ComponentTacticalModel : ComponentCreatureModel {
    public override void AnimateCreature(){}
    public override void Animate(){
        base.Animate();
        if(m_componentCreature.ComponentHealth.Health<=0&&m_boneTransforms[Model.RootBone.Index] is Matrix root){
            var body=m_componentCreature.ComponentBody;var pos=body.Position;
            m_boneTransforms[Model.RootBone.Index]=root*Matrix.CreateTranslation(-pos)*Matrix.CreateFromAxisAngle(body.Matrix.Right,-MathF.PI*.5f*DeathPhase)*Matrix.CreateTranslation(pos+Vector3.UnitY*.15f);
        }
    }
    public override void SyncAnimationParameters(){base.SyncAnimationParameters();var inv=Entity.FindComponent<ComponentTacticalInventory>();AnimationController?.Parameters.SetBool("Armed",Entity.FindComponent<ComponentTacticalEnemy>()?.State!=null||inv?.GetSlotCount(0)>0);AnimationController?.Parameters.SetBool("Shield",inv?.GetSlotCount(0)>0&&ScTacticalShieldBlock.IsShield(inv.GetSlotValue(0)));}
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
        var fist=(AbsoluteBoneTransformsForCamera[hand.Index]*camera.InvertedViewMatrix).Translation;
        var world=ScThirdPersonMath.WeaponWorld(weapon.GripRight,fist,m_componentCreature.ComponentBody.Matrix.Forward,Vector3.UnitY);
        var terrain=Project.FindSubsystem<SubsystemTerrain>(true);var p=world.Translation;
        var env=new DrawBlockEnvironmentData{DrawBlockMode=DrawBlockMode.ThirdPerson,InWorldMatrix=world,Owner=Entity,SubsystemTerrain=terrain,Light=terrain.Terrain.GetCellLight(Terrain.ToCell(p.X),Terrain.ToCell(p.Y),Terrain.ToCell(p.Z))};
        var view=world*camera.ViewMatrix;var renderer=Project.FindSubsystem<SubsystemModelsRenderer>(true).PrimitivesRenderer;
        foreach(var group in weapon.Groups){if(group.Silencer&&state.SilencerOff)continue;var tex=group.Texture==asset+"_hd"?texture:ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/"+group.Texture);
            if(native is not null)ScGunNativeMesh.DrawWorld(renderer,group.Mesh,tex,Color.White,1,ref view,env);else BlocksManager.DrawMeshBlock(renderer,group.Mesh,tex,Color.White,1,ref view,env);}
        ScStatTrakRenderer.DrawThirdPerson(value,asset,native is not null,view,renderer,LightingManager.LightIntensityByLightValue[Math.Clamp(env.Light,0,15)]);
    }
}
