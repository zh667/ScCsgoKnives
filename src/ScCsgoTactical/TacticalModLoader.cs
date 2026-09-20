using Engine;
using Engine.Input;
using Engine.Graphics;
namespace Game;

public sealed class TacticalModLoader : ModLoader {
    public override void __ModInitialize(){TacticalAppearanceIntegration.Initialize(Entity);foreach(string hook in new[]{"ProcessAttackment","OnLoadingFinished","UpdateInput","OnPlayerInputInteract","OnPlayerInputHit","UpdatePlayerInputDig","OnCreatureDied","OnFirstPersonModelDrawing","OnModelDrawExtra","OnModelCalculateBones","OnProjectLoaded","OnSaveSpawnData","OnReadSpawnData","DeadBeforeDrops"})ModsManager.RegisterHook(hook,this);}
    public override void OnProjectLoaded(GameEntitySystem.Project project)=>project.FindSubsystem<SubsystemTacticalEnemies>(false)?.Register();
    public override void OnSaveSpawnData(ComponentSpawn spawn,SpawnEntityData data)=>SubsystemTacticalEnemies.SaveSpawn(spawn,data);
    public override void OnReadSpawnData(GameEntitySystem.Entity entity,SpawnEntityData data)=>SubsystemTacticalEnemies.ReadSpawn(entity,data);
    public override void DeadBeforeDrops(ComponentHealth health,ref KillParticleSystem particles,ref bool dropAll){if(health.Entity.FindComponent<ComponentTacticalEnemy>() is {} enemy){dropAll=false;enemy.Died();}}
    public override void OnModelDrawExtra(ComponentModel model,Camera camera,out bool skip){
        skip=false;if(model is not ComponentHumanModel human||!ScShieldProtection.Holding(human.m_componentCreature.ComponentBody,out var inv))return;
        int value=inv.GetSlotValue(inv.ActiveSlotIndex);var world=ScShieldProtection.Pose(human.m_componentCreature.ComponentBody);var view=world*camera.ViewMatrix;var pos=world.Translation;
        var env=new DrawBlockEnvironmentData{DrawBlockMode=DrawBlockMode.ThirdPerson,Owner=human.Entity,InWorldMatrix=world,SubsystemTerrain=human.m_subsystemTerrain,Light=human.m_subsystemTerrain.Terrain.GetCellLight(Terrain.ToCell(pos.X),Terrain.ToCell(pos.Y),Terrain.ToCell(pos.Z))};
        BlocksManager.Blocks[Terrain.ExtractContents(value)].DrawBlock(human.m_subsystemModelsRenderer.PrimitivesRenderer,value,Color.White,1,ref view,env);skip=true;
    }
    public override void OnModelCalculateBones(ComponentModel model,Camera camera,out bool skip){
        skip=false;if(model is not ComponentHumanModel h||h.m_bodyBone is null||h.m_hand1Bone is null||h.m_hand2Bone is null||!ScShieldProtection.Holding(h.m_componentCreature.ComponentBody,out _))return;
        var transforms=new Matrix[h.Model.Bones.Count];h.ProcessBoneHierarchy(h.Model.RootBone,Matrix.Identity,transforms);var body=transforms[h.m_bodyBone.Index];var pose=ScShieldProtection.Pose(h.m_componentCreature.ComponentBody);
        foreach(var pair in new[]{(h.m_hand1Bone,-1f),(h.m_hand2Bone,1f)}){
            var shoulder=Vector3.Transform(pair.Item1.Transform.Translation,body);var target=pose.Translation+pose.Right*(pair.Item2*.125f)-pose.Forward*.08f;
            h.SetBoneTransform(pair.Item1.Index,ScThirdPersonMath.HandLocal(ScThirdPersonMath.AnglesToward(ScThirdPersonMath.BodyDirection(shoulder,target,body))));
        }
    }
    public override void OnFirstPersonModelDrawing(ComponentFirstPersonModel first,Camera camera,int value,ref Matrix unused,out bool skip){
        skip=false;if(!ScTacticalShieldBlock.IsShield(value))return;
        var blend=Display.BlendState;var depth=Display.DepthStencilState;var raster=Display.RasterizerState;var scissor=Display.ScissorRectangle;
        try{
            Display.ScissorRectangle=ScCameraViewport.Clip(camera.ViewportSize,camera.ViewportMatrix,scissor);
            // Adapt the existing two-hand C4 hold. The shield geometry and offsets are authored for this port.
            var userOffset=new Vector3(KnifeTuning.Cs2ViewmodelOffsetX-2.5f,KnifeTuning.Cs2ViewmodelOffsetZ+1.5f,-KnifeTuning.Cs2ViewmodelOffsetY)*Cs2Placement.InchesToEngine;
            var matrix=Matrix.CreateTranslation(new Vector3(0,-.48f,-.8f)+userOffset);
            var block=BlocksManager.Blocks[Terrain.ExtractContents(value)];
            var env=new DrawBlockEnvironmentData{DrawBlockMode=DrawBlockMode.FirstPerson,Owner=first.Entity,SubsystemTerrain=first.m_subsystemTerrain,Light=first.m_itemLight};
            block.DrawBlock(first.m_primitivesRenderer,value,Color.White,1,ref matrix,env);
            first.m_primitivesRenderer.Flush(Cs2Placement.Projection(camera));
            CsmcFirstPersonRenderer.DrawExtensionArms(first,camera,"c4","idle",Matrix.CreateTranslation(-.10f,-.22f,-.12f));
            skip=true;
        }finally{Display.BlendState=blend;Display.DepthStencilState=depth;Display.RasterizerState=raster;Display.ScissorRectangle=scissor;}
    }
    public override void OnLoadingFinished(List<Action> actions)=>actions.Add(SubsystemScTactical.RegisterRecipes);
    public override void ProcessAttackment(Attackment attack){
        if(attack?.Target?.Project is {} project&&attack.AttackPower>0){var attacker=attack.Attacker?.FindComponent<ComponentBody>();
            var attacked=attack.Target.FindComponent<ComponentTacticalCompanion>();attacked?.Alert(attacker);
            attack.Target.FindComponent<ComponentTacticalEnemy>()?.Alert(attacker);
            if(attack.Target.FindComponent<ComponentPlayer>() is {} player&&project.FindSubsystem<SubsystemScTactical>(false) is {} tactical)foreach(var c in tactical.Companions)if(c.OwnedBy(player))c.Alert(attacker);
            if(attack.Attacker?.FindComponent<ComponentPlayer>() is {} owner&&project.FindSubsystem<SubsystemScTactical>(false) is {} squad)foreach(var c in squad.Companions)if(c.OwnedBy(owner))c.Alert(attack.Target.FindComponent<ComponentBody>());
        }
        ScShieldProtection.Filter(attack);
    }
    public override void UpdateInput(ComponentInput input,WidgetInput widget){var bombs=input.Project.FindSubsystem<SubsystemTacticalBombs>(false);bombs?.Input(input);if(ScWeaponActionGate.Blocks(input.m_componentPlayer))return;if(widget.IsKeyDownOnce(Key.E)&&SubsystemScTactical.Open(input.m_componentPlayer)){input.m_playerInput.ToggleInventory=false;input.m_playerInput.EditItem=false;input.m_playerInput.Interact=null;}}
    public override void OnPlayerInputInteract(ComponentPlayer p,ref bool operated,ref double interval,ref int use,ref int interact,ref int place){if(ScWeaponActionGate.Blocks(p)||SubsystemScTactical.Open(p)){operated=true;use=interact=place=0;}}
    public override void OnPlayerInputHit(ComponentPlayer p,ref bool operated,ref double interval,ref float range,bool skipped,out bool skipVanilla){skipVanilla=ScWeaponActionGate.Blocks(p)||ScTacticalShieldBlock.IsShield(p.ComponentMiner.ActiveBlockValue);if(skipVanilla){range=0;operated=true;}}
    public override void UpdatePlayerInputDig(ComponentPlayer p,bool digging,ref bool operated,ref double interval,bool skipped,out bool skipVanilla){skipVanilla=ScWeaponActionGate.Blocks(p)||ScTacticalShieldBlock.IsShield(p.ComponentMiner.ActiveBlockValue);if(skipVanilla)operated=true;}
    public override void OnCreatureDied(ComponentHealth health,Injury injury,ref int experience,ref bool kills){health.Entity.FindComponent<ComponentTacticalCompanion>()?.Died();health.Entity.FindComponent<ComponentTacticalEnemy>()?.Died();}
}
