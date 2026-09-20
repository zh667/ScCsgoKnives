using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Engine.Animation;
using Engine.Graphics;
using Engine.Media;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

static class ActionChecks {
    sealed class Inventory:IInventory {
        public int[] Values=new int[10];public Project Project=>null;public int SlotsCount=>10;public int VisibleSlotsCount{get;set;}=10;public int ActiveSlotIndex{get;set;}
        public int GetSlotValue(int i)=>Values[i];public int GetSlotCount(int i)=>Values[i]!=0?1:0;public int GetSlotCapacity(int i,int v)=>1;public int GetSlotProcessCapacity(int i,int v)=>0;
        public void AddSlotItems(int i,int v,int n)=>Values[i]=v;public int RemoveSlotItems(int i,int n){Values[i]=0;return 1;}
        public void ProcessSlotItems(int i,int v,int n,int p,out int rv,out int rn){rv=rn=0;}public void DropAllItems(Vector3 p){}
    }
    static T Blank<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    public static void Run(string root,string output,Dictionary<string,Model> models,ComponentHumanModel human,ComponentBody body,
        ModelShader shader,RenderTarget2D target,Action<string,bool> check,IDictionary<string,List<object>> caches) {
        string assets=Path.Combine(root,"src/ScCsgoKnives/Assets");
        var a=new ScWeaponActionTimeline();var b=new ScWeaponActionTimeline();a.Start("ak47",ScWeaponActionKind.Reload,"reload",10,2.8f);b.Start("mp9",ScWeaponActionKind.Inspect,"inspect",10,3.5f);
        check("actions entity isolation and read idempotence",a.Read(11)==a.Read(11)&&a.Read(11).Kind==ScWeaponActionKind.Reload&&b.Read(11).Kind==ScWeaponActionKind.Inspect);
        a.Clear();check("actions cancellation independent",!a.Read(11).Active&&b.Read(11).Active);
        check("actions expiration",!b.Read(14).Active);
        var fpp=Blank<ComponentFirstPersonModel>();var p=Blank<ComponentPlayer>();var miner=Blank<ComponentMiner>();var inv=new Inventory();miner.Inventory=inv;p.ComponentMiner=miner;p.ComponentHealth=new ComponentHealth{Health=1};fpp.m_componentPlayer=p;
        var e=Blank<Entity>();e.m_components=[p,miner,fpp];foreach(var c in e.m_components)c.m_entity=e;
        inv.Values[0]=Terrain.MakeBlockValue(701,0,GunSpec.MakeData(0,30,false));inv.Values[1]=Terrain.MakeBlockValue(701,0,GunSpec.MakeData(8,30,false));inv.Values[2]=inv.Values[0];
        float volume=SettingsManager.SoundsVolume;SettingsManager.SoundsVolume=0;KnifeClock.Reset(1/60f);
        try {
            KnifeAnimationController.Update(fpp,inv.Values[0]);var first=KnifeAnimationController.ReadAction(fpp);
            check("third-person selection starts draw without drawing",first.Kind==ScWeaponActionKind.Draw&&first.Asset=="ak47");
            KnifeClock.VirtualNow=10;KnifeAnimationController.Update(fpp,inv.Values[0]);check("third-person update completes draw without drawing",KnifeAnimationController.ReadAction(fpp).Kind==ScWeaponActionKind.Idle);
            inv.ActiveSlotIndex=1;KnifeAnimationController.Update(fpp,inv.Values[1]);var next=KnifeAnimationController.ReadAction(fpp);
            KnifeAnimationController.Update(fpp,inv.Values[0]);check("departing first-person item cannot revert selected action",KnifeAnimationController.ReadAction(fpp)==next&&next.Asset=="mp9");
            inv.ActiveSlotIndex=2;KnifeAnimationController.Update(fpp,inv.Values[2]);long token=KnifeAnimationController.ReadAction(fpp).Sequence;
            inv.ActiveSlotIndex=0;KnifeAnimationController.Update(fpp,inv.Values[0]);check("same-model different slot redraw",KnifeAnimationController.ReadAction(fpp).Sequence>token);
            var vanilla=new ComponentHumanModel();var original=(Model)caches["Fixture/Default"][0];
            _=Weapon("ak47");p.ComponentBody=body;p.ComponentSpawn=new ComponentSpawn{SpawnDuration=0};p.ComponentLocomotion=new ComponentLocomotion();
            e.m_project=human.Project;e.m_components.Add(vanilla);vanilla.m_entity=e;
            var creature=new ComponentCreature{ComponentBody=body,ComponentHealth=new ComponentHealth{Health=1},ComponentSpawn=new ComponentSpawn{SpawnDuration=0},ComponentLocomotion=new ComponentLocomotion()};
            e.m_components.Add(creature);creature.m_entity=e;e.m_components.Add(body);
            vanilla.Load(new ValuesDictionary{{"ModelName","Fixture/Default"},{"CastsShadow",true},{"PrepareOrder",0},{"BoundingSphereRadius",2f},{"WalkAnimationSpeed",1f},{"WalkBobHeight",0f},{"WalkLegsAngle",1f}},null);
            vanilla.m_componentMiner=miner;
            Matrix? lastHand=null;
            foreach(var method in new[]{"QaDraw","QaInspect"}){
                typeof(KnifeAnimationController).GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,[fpp,ScGunBlock.AssetIndex(0)]);
                KnifeClock.VirtualNow+=.35;vanilla.m_boneTransforms[vanilla.m_bodyBone.Index]=Matrix.CreateTranslation(body.Position);
                check("vanilla third-person "+method+" hook",ScThirdPerson.Pose(vanilla,.1f));
                var nowHand=vanilla.m_boneTransforms[vanilla.m_hand2Bone.Index];
                check("vanilla action changes the right arm",nowHand.HasValue&&(!lastHand.HasValue||lastHand!=nowHand));lastHand=nowHand;
                var saved=vanilla.m_boneTransforms.ToArray();ScThirdPerson.Pose(vanilla,.1f);check("vanilla repeated camera pose",saved.SequenceEqual(vanilla.m_boneTransforms));
            }
            inv.Values[0]=Terrain.MakeBlockValue(701,0,GunSpec.MakeData(26,0,false));KnifeAnimationController.Update(fpp,inv.Values[0]);
            KnifeAnimationController.TriggerReload(p,false,3);double began=KnifeClock.Now;var sections=Cs2Rig.GetReloadSections("nova");
            KnifeClock.VirtualNow=began+sections.LoopStart+sections.LoopLength*.5;var shell1=KnifeAnimationController.ReadAction(fpp);
            KnifeClock.VirtualNow+=sections.LoopLength;var shell2=KnifeAnimationController.ReadAction(fpp);
            check("shotgun snapshot follows per-shell loop",shell1.LoopedReload&&shell2.Elapsed>shell1.Elapsed&&Math.Abs(shell1.ClipTime-shell2.ClipTime)<.001f);
            KnifeAnimationController.CancelReloadAction(p,shell2.Sequence);check("cancelled reload clears presentation",KnifeAnimationController.ReadAction(fpp).Kind==ScWeaponActionKind.Idle);
            inv.Values[0]=0;KnifeAnimationController.Update(fpp,0);check("empty hand clears third-person action",KnifeAnimationController.ReadAction(fpp).Asset==null);
        } finally {KnifeClock.Release();SettingsManager.SoundsVolume=volume;KnifeAnimationController.ClearSession();}
        Texture2D Texture(string key){string route="Textures/ScCsgoKnives/"+key;if(caches.TryGetValue(route,out var c))return (Texture2D)c[0];
            string file=Path.Combine(assets,route+".png");if(!File.Exists(file))file=Path.Combine(assets,route+".webp");using var s=File.OpenRead(file);var tex=Texture2D.Load(Image.Load(s));caches[route]=[tex];return tex;}
        ScThirdPersonWeapon Weapon(string asset){
            foreach(string file in Directory.GetFiles(Path.Combine(assets,"Models/ScCsgoKnives"),asset+"*cs2*.obj")){using var s=File.OpenRead(file);caches["Models/ScCsgoKnives/"+Path.GetFileNameWithoutExtension(file)]=[ObjModelReader.Load(s)];}
            Texture(ScGunSkinCatalog.Material(asset,0));foreach(var part in ScGunNativeMesh.Parts(asset))if(part.Material!=null)Texture(part.Material);
            bool native=ScGunNativeMesh.Resolve(asset,0,out _,out _)!=null;var w=ScThirdPersonWeapon.For(asset,native);if(w==null)throw new Exception("missing weapon "+asset);return w;
        }
        int frame=1000;
        foreach(var pair in models){
            var model=pair.Value;human.SetModel(model);var sampler=new ScAgentActions(model);
            var npc=new ComponentTacticalModel();var enemy=new ComponentTacticalEnemy{State=TacticalEnemyState.Create(TacticalRole.Rifle,"fixture",new Engine.Random(1))};
            var npcBody=new ComponentBody{Position=body.Position,BoxSize=body.BoxSize};
            var npcCreature=new ComponentCreature{ComponentBody=npcBody,ComponentHealth=new ComponentHealth{Health=1},ComponentSpawn=new ComponentSpawn{SpawnDuration=0},ComponentLocomotion=new ComponentLocomotion()};
            var npcEntity=Blank<Entity>();npcEntity.m_project=human.Project;npcEntity.m_components=[npc,enemy,npcCreature,npcBody,npcCreature.ComponentHealth,npcCreature.ComponentSpawn,npcCreature.ComponentLocomotion];foreach(var c in npcEntity.m_components)c.m_entity=npcEntity;
            enemy.Creature=npcCreature;typeof(ComponentTacticalEnemy).GetField("time",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(enemy,human.Project.FindSubsystem<SubsystemTime>());
            enemy.State.ReloadLeft=1.4f;
            npc.Load(new ValuesDictionary{{"ModelName","Models/ScCsgoTactical/"+pair.Key},{"CastsShadow",true},{"PrepareOrder",0},{"BoundingSphereRadius",2f},{"AnimationConfigPath","Animations/ScTactical"}},null);
            npc.AnimationController.Update(.3f);npc.Animate();var npcPose=npc.m_boneTransforms.ToArray();npc.Animate();
            check(pair.Key+" actual NPC reload and repeat cameras",npc.VisualAction.Kind==ScWeaponActionKind.Reload&&npcPose.SequenceEqual(npc.m_boneTransforms));
            enemy.State.ReloadLeft=0;npc.Animate();check(pair.Key+" actual NPC reload completion",npc.VisualAction.Kind!=ScWeaponActionKind.Reload&&!npcPose.SequenceEqual(npc.m_boneTransforms));
            foreach(var spec in GunSpec.All){
                foreach(var kind in new[]{ScWeaponActionKind.Draw,ScWeaponActionKind.Reload}){
                    if(kind==ScWeaponActionKind.Reload&&spec.Name=="taser")continue;
                    var act=new ScWeaponAction(spec.Name,kind,kind==ScWeaponActionKind.Draw?"deploy":"reload",1,.6f,2, .6f);
                    check(pair.Key+" "+spec.Name+" "+kind+" native world clip",sampler.ClipFor(act)!=null);
                }
            }
            foreach(string asset in new[]{"ak47","mp9","deagle","nova"}){
                var weapon=Weapon(asset);
                foreach(var kind in new[]{ScWeaponActionKind.Idle,ScWeaponActionKind.Draw,ScWeaponActionKind.Reload,ScWeaponActionKind.Inspect})foreach(float progress in new[]{.25f,.55f,.8f}){
                    if(kind==ScWeaponActionKind.Idle&&progress!=.25f)continue;
                    string clip=kind==ScWeaponActionKind.Draw?"deploy":kind==ScWeaponActionKind.Reload?"reload":"inspect";
                    var act=new ScWeaponAction(asset,kind,clip,7,progress*3,3,progress*Cs2Rig.Duration(asset,clip));
                    var pose=new CsPlayerPose(model);pose.Sample(frame++,.1f,1.2f,true,false,0,0,act);
                    var baseline=new CsPlayerPose(model);baseline.Sample(frame++,.1f,1.2f,true,false,0,0);
                    check(pair.Key+asset+kind+progress+" legs remain locomotion",model.Bones.Where(b=>b.Name.StartsWith("leg_")||b.Name.StartsWith("ankle_")).All(b=>pose.Local[b.Index]==baseline.Local[b.Index]));
                    var before=(Matrix?[])pose.Local.Clone();pose.Sample(frame-2,.1f,1.2f,true,false,0,0,act);check("repeat camera keeps exact pose",before.SequenceEqual(pose.Local));
                    Array.Copy(pose.Local,human.m_boneTransforms,pose.Local.Length);human.m_boneTransforms[model.RootBone.Index]*=body.Matrix;
                    human.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,human.AbsoluteBoneTransformsForCamera);
                    Matrix hand=human.AbsoluteBoneTransformsForCamera[model.FindBone("hand_R").Index];var world=pose.Actions.WeaponWorld(weapon.GripRight,hand);
                    check("animated grip remains at hand",Vector3.Distance(Vector3.Transform(weapon.GripRight,world),hand.Translation)<.001f);
                    var joints=new Matrix[48];SubsystemModelsRenderer.CalculateJointMatrices(human,model,Matrix.Identity,joints);
                    var view=Matrix.CreateLookAt(body.Position+new Vector3(2.8f,1.35f,3),body.Position+new Vector3(0,1,0),Vector3.UnitY);
                    Display.RenderTarget=target;Display.Viewport=new Viewport(0,0,480,640);Display.Clear(new Color(22,27,34),1,0);
                    Display.BlendState=BlendState.Opaque;Display.DepthStencilState=DepthStencilState.Default;Display.RasterizerState=RasterizerState.CullCounterClockwiseScissor;Display.ScissorRectangle=new Rectangle(0,0,480,640);
                    shader.Transforms.World[0]=view;shader.Transforms.View=Matrix.Identity;var projection=Matrix.CreatePerspectiveFieldOfView(.75f,.75f,.1f,100);shader.Transforms.Projection=projection;
                    shader.InstancesCount=1;shader.JointMatrices=joints;
                    foreach(var mesh in model.Meshes)foreach(var part in mesh.MeshParts){shader.Texture=model.GetTexture(model.GetMaterial(part.MaterialIndex).BaseColorTexture.TextureIndex);Display.DrawIndexed(PrimitiveType.TriangleList,shader,part.VertexBuffer,part.IndexBuffer,part.StartIndex,part.IndicesCount);}
                    var renderer=new PrimitivesRenderer3D();var weaponPose=weapon.ActionPose(act);Vector3 lo=new(float.MaxValue),hi=new(float.MinValue);
                    foreach(var group in weapon.Groups){if(!weapon.ShowPart(group,weaponPose,act))continue;var transform=weapon.PartTransform(group,weaponPose)*world;foreach(var v in group.Mesh.Vertices){var q=Vector3.Transform(v.Position,transform);lo=Vector3.Min(lo,q);hi=Vector3.Max(hi,q);}var partView=transform*view;ScGunNativeMesh.DrawWorld(renderer,group.Mesh,Texture(group.Texture),Color.White,1,ref partView,new DrawBlockEnvironmentData{Light=15,DrawBlockMode=DrawBlockMode.ThirdPerson});}
                    check("animated weapon bounded "+asset+kind+progress,float.IsFinite(lo.X)&&(hi-lo).Length()<3&&Vector3.Distance((lo+hi)/2,hand.Translation)<2);
                    renderer.Flush(projection);using var file=File.Create(Path.Combine(output,$"action-{pair.Key}-{asset}-{kind}-{progress:0.00}.png"));RenderTarget2D.Save(target,file,ImageFileFormat.Png,false);
                }
            }
        }
    }
}
