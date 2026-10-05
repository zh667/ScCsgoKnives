// video-feedback-20260929 R2 and r2-c4-completion-20260929: offline native evidence for the held throwable, the throw and
// the C4 plant in third person, in every posture.
// CS actors (CT, T): the real ScAgentActions with CS2's world pull-pin / throw / plant clips and the world prop bones,
//   on the live gait of the actor's own controller (standing, crouched, running, in the air).
// Rigid-arm humans: ScThirdPerson.ActionPreview, the functions the game's pose hook uses, on a body posed the way the
//   game poses it - the vanilla male/female by the engine's own HumanWalkDriver, NekoMeko Model 1.1's bone sets
//   (Minecraft Classic/Slim, ScMale/ScFemale; a real third-party package, read-only) by NMM's own vanilla replica.
// Six throwables x strong/weak x postures, the C4 plant, contact distances, visibility, frame-to-frame continuity,
// timestamped renders. Contact is measured on the item's surface, not its centre (a bottle gripped near one end is
// still held; a grenade swallowed by a box arm is not seen). Not a game recording: real-game video remains acceptance.
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Engine;
using Engine.Animation;
using Engine.Graphics;
using Engine.Media;
using Game;
using Game.Animation.Drivers;
using GameEntitySystem;
using TemplatesDatabase;

if(args.Length<4)throw new ArgumentException("ThrowPoseCheck <Assets folder> <Content.zip> <output folder> <Full package>");
string assets=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[2]);Directory.CreateDirectory(output);
string nmmPath=Environment.GetEnvironmentVariable("SC_NMM_MODEL_PACKAGE");
using var content=ZipFile.OpenRead(args[1]);using var package=ZipFile.OpenRead(args[3]);
string Read(string suffix){using var r=new StreamReader(content.Entries.Single(e=>e.FullName.EndsWith(suffix)).Open());return r.ReadToEnd();}
AnimationTemplateManager.LoadFromJsonNode(JsonNode.Parse(Read("Simple.template.json")));
var caches=(IDictionary<string,List<object>>)typeof(ContentManager).GetField("Caches",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
foreach(string role in new[]{"ct","t"}){string name="Animations/ScCsgoTactical/"+role+".scanim";var info=new ContentInfo(name);info.SetContentStream(new MemoryStream(File.ReadAllBytes(Path.Combine(assets,name))));ContentManager.Add(info);}
var frameDuration=typeof(Time).GetProperty("FrameDuration");var frameIndex=typeof(Time).GetProperty("FrameIndex");
void Frame(float dt){frameDuration.SetValue(null,dt);frameIndex.SetValue(null,Time.FrameIndex+1);}
var report=new List<object>();var failures=new List<string>();var coverage=new List<object>();
string[] grenades=["grenade_hegrenade","grenade_flashbang","grenade_smokegrenade","grenade_molotov","grenade_incendiary","grenade_decoy"];
string[] postures=["stand","crouch","run","jump"];
// One throw on the gameplay clock: 1 s pull, 0.5 s held, then the throw with its release.
const float PullSeconds=1f,HoldSeconds=.5f,WindSeconds=.3f,FollowSeconds=.7f;
ScThrowPhase At(string asset,float t,bool low){
    int value=ScGrenadeBlock.Value(Array.IndexOf(ScGrenadeBlock.Assets,asset));
    if(t<PullSeconds)return new(value,asset,0,low,false,t/PullSeconds,0,0,0);
    if(t<PullSeconds+HoldSeconds)return new(value,asset,1,low,false,1,t-PullSeconds,0,0);
    float throwing=t-PullSeconds-HoldSeconds;
    return throwing<WindSeconds?new(value,asset,2,low,false,1,0,throwing/WindSeconds,0):new(value,asset,2,low,true,1,0,1,Math.Clamp((throwing-WindSeconds)/FollowSeconds,0,1));
}
ScPlantPhase Planting(float t)=>t>=ScPlantPhase.EndSeconds?default:new(1,t>=ScPlantPhase.PlantSeconds,t,1,Vector3.Zero);
(string Label,float T)[] shots=[("hold",-1),("pull-mid",.5f),("pull-end",.97f),("held",1.3f),("wind-mid",1.65f),("release",1.79f),("follow",2f),("end",2.45f)];
(string Label,float T)[] plantShots=[("reach",.3f),("typing",1.5f),("setting",3f),("commit-1",3.19f),("placed",3.21f),("recover",3.6f)];
bool done=false;
Window.Frame+=()=>{if(done)return;done=true;try{
    LightingManager.Initialize();
    foreach(var pair in new[]{(typeof(AirBlock),0),(typeof(ScKnifeBlock),700),(typeof(ScGunBlock),701),(typeof(ScGrenadeBlock),702),(typeof(ScC4Block),705)}){var block=(Block)Activator.CreateInstance(pair.Item1);block.BlockIndex=pair.Item2;BlocksManager.Blocks[pair.Item2]=block;BlocksManager.BlockTypeToIndex[pair.Item1]=pair.Item2;BlocksManager.BlockNameToIndex[pair.Item1.Name]=pair.Item2;}
    var textures=new Dictionary<string,Texture2D>();
    Texture2D Texture(string key){
        if(textures.TryGetValue(key,out var hit))return hit;
        var entry=package.Entries.FirstOrDefault(e=>e.FullName.StartsWith("Assets/Textures/ScCsgoKnives/"+key+".",StringComparison.OrdinalIgnoreCase));
        Texture2D texture=null;
        if(entry!=null){using var s=entry.Open();using var m=new MemoryStream();s.CopyTo(m);m.Position=0;texture=Texture2D.Load(m);}
        return textures[key]=texture;
    }
    using var target=new RenderTarget2D(480,640,1,ColorFormat.Rgba8888,DepthFormat.Depth24Stencil8);
    using var shader=new ModelShader(Read("Shaders/Model.vsh"),Read("Shaders/Model.psh"),false,1,48);
    var flat=new PrimitivesRenderer3D();
    void Begin(){Display.RenderTarget=target;Display.Viewport=new Viewport(0,0,480,640);Display.Clear(new Color(58,64,72),1,0);Display.ScissorRectangle=new Rectangle(0,0,480,640);}
    void Save(string name){using var file=File.Create(Path.Combine(output,name+".png"));RenderTarget2D.Save(target,file,ImageFileFormat.Png,false);Display.RenderTarget=null;}
    void DrawHeld(ScThirdPersonWeapon weapon,Func<ScThirdPersonWeapon.Group,(BlockMesh Mesh,Matrix Transform)> part,Matrix viewProjection){
        foreach(var group in weapon.Groups){
            if(group.Texture=="weapon_molotov_flame")continue;
            var (mesh,transform)=part(group);var texture=Texture(group.Texture);
            var batch=texture==null?null:flat.TexturedBatch(texture,true,0,DepthStencilState.Default,RasterizerState.CullNone,BlendState.AlphaBlend,SamplerState.PointClamp);
            var plain=flat.FlatBatch(0,DepthStencilState.Default,RasterizerState.CullNone,BlendState.Opaque);
            for(int i=0;i+2<mesh.Indices.Count;i+=3){
                var a=mesh.Vertices[mesh.Indices[i]];var b=mesh.Vertices[mesh.Indices[i+1]];var c=mesh.Vertices[mesh.Indices[i+2]];
                Vector3 pa=Vector3.Transform(a.Position,transform),pb=Vector3.Transform(b.Position,transform),pc=Vector3.Transform(c.Position,transform);
                if(batch!=null)batch.QueueTriangle(pa,pb,pc,a.TextureCoordinates,b.TextureCoordinates,c.TextureCoordinates,Color.White);
                else plain.QueueTriangle(pa,pb,pc,new Color(230,120,40));
            }
        }
        flat.Flush(viewProjection);
    }
    bool Solid(ScThirdPersonWeapon.Group g)=>g.Texture is not ("weapon_molotov_flame" or "weapon_molotov_liquid");
    (Vector3 Centre,float Size) Bounds(ScThirdPersonWeapon weapon,Func<ScThirdPersonWeapon.Group,(BlockMesh Mesh,Matrix Transform)> part){
        Vector3 min=new(float.MaxValue),max=new(float.MinValue);
        foreach(var group in weapon.Groups.Where(Solid)){var (mesh,transform)=part(group);foreach(var v in mesh.Vertices){var p=Vector3.Transform(v.Position,transform);min=Vector3.Min(min,p);max=Vector3.Max(max,p);}}
        return((min+max)*.5f,Vector3.Distance(min,max));
    }
    List<Vector3> Surface(ScThirdPersonWeapon weapon,Func<ScThirdPersonWeapon.Group,(BlockMesh Mesh,Matrix Transform)> part){
        var points=new List<Vector3>();
        foreach(var group in weapon.Groups.Where(Solid)){var (mesh,transform)=part(group);foreach(var v in mesh.Vertices)points.Add(Vector3.Transform(v.Position,transform));}
        return points;
    }
    // A finger is about 8 cm long and 2 cm thick: a held body has surface within a finger of the palm's centre and
    // within two finger widths of a joint of the hand. Letting go is the last tenth of the swing (0.03 s).
    const float PalmReach=.08f,JointReach=.04f,FingerReach=.08f,LetGo=.9f;
    (Matrix View,Matrix Projection) Camera(string side,Vector3 focus,float distance)=>(
        Matrix.CreateLookAt(focus+(side=="side"?new Vector3(distance,.25f,0):side=="front"?new Vector3(0,.25f,-distance):new Vector3(distance*.7f,.6f,-distance*.7f)),focus,Vector3.UnitY),
        Matrix.CreatePerspectiveFieldOfView(.8f,.75f,.1f,100));
    var c4Weapon=ScThirdPersonWeapon.For("c4");
    if(c4Weapon is null||!c4Weapon.HasRightGrip)failures.Add("the C4 has no third-person mesh or grip: NPCs and CT/T players would plant an invisible bomb");
    report.Add(new{c4ThirdPerson=c4Weapon is null?null:new{groups=c4Weapon.Groups.Length,vertices=c4Weapon.Vertices,grip=c4Weapon.GripRight.ToString(),size=Bounds(c4Weapon,g=>(g.Mesh,Matrix.Identity)).Size}});

    // ---------------- CS actors ----------------
    foreach(string name in new[]{"ct","t"}){
        using var stream=File.OpenRead(Path.Combine(assets,"Models/ScCsgoTactical/"+name+".glb"));
        using var model=Model.Load(GltfLoader.Load(stream),true);
        ScActorAnimations.Ensure(model);
        var actions=new ScAgentActions(model);
        foreach(string clip in new[]{"pullpin_grenade","throwhigh_grenade","throwlow_grenade","pullpin_molotov","throwhigh_molotov","throwlow_molotov","hold_grenade","hold_molotov","draw_grenade","draw_molotov",
            "pullpin_crouch_grenade","throwhigh_crouch_grenade","throwlow_crouch_grenade","pullpin_crouch_molotov","throwhigh_crouch_molotov","throwlow_crouch_molotov","plant_c4","plant_crouch_c4","hold_c4",
            "crouch","crouchwalk","aimcrouch","aimcrouchwalk"})
            if(!model.Animations.Any(a=>a.Name==clip))failures.Add($"{name}: packaged cache lacks {clip}");
        var project=new Project();var time=new SubsystemTime();
        foreach(var sub in new Subsystem[]{new SubsystemSky(),time,new SubsystemGameInfo(),new SubsystemTerrain{Terrain=new Terrain()}}){sub.m_project=project;project.m_subsystems.Add(sub);}
        var origin=new Vector3(105.5f,70,-212.5f);
        var body=new ComponentBody{Position=origin,BoxSize=new Vector3(.65f,1.8f,.65f),Rotation=Quaternion.Identity,StandingOnValue=2};var locomotion=new ComponentLocomotion();
        var creature=new ComponentCreature{ComponentBody=body,ComponentHealth=new ComponentHealth{Health=1},ComponentSpawn=new ComponentSpawn{SpawnDuration=0},ComponentLocomotion=locomotion};
        var component=new ComponentTacticalModel();var entity=(Entity)RuntimeHelpers.GetUninitializedObject(typeof(Entity));entity.m_project=project;
        entity.m_components=[component,body,creature,creature.ComponentHealth,creature.ComponentSpawn,locomotion];foreach(var c in entity.m_components)c.m_entity=entity;
        creature.ComponentHealth.m_componentCreature=creature;
        caches["Fixture/Model"]=[model];caches["Fixture/Config.json"]=[File.ReadAllText(Path.Combine(assets,"Animations/ScTactical.json"))];
        component.Load(new ValuesDictionary{{"ModelName","Fixture/Model"},{"CastsShadow",true},{"PrepareOrder",0},{"BoundingSphereRadius",2f},{"AnimationConfigPath","Fixture/Config"}},null);
        var hook=new ModsManager.ModHook("OnAnimateModel");hook.Add(new TacticalModLoader());ModsManager.ModHooks["OnAnimateModel"]=hook;
        int Bone(string bone)=>model.FindBone(bone,true).Index;
        int[] rightHand=model.Bones.Where(b=>b.Name=="hand_R"||b.Name.StartsWith("finger_")&&b.Name.EndsWith("_R")).Select(b=>b.Index).ToArray();
        int[] leftHand=model.Bones.Where(b=>b.Name=="hand_L"||b.Name.StartsWith("finger_")&&b.Name.EndsWith("_L")).Select(b=>b.Index).ToArray();
        if(rightHand.Length<10||leftHand.Length<10)failures.Add($"{name}: hand joints not found ({rightHand.Length} right, {leftHand.Length} left)");
        (float Palm,float Joint,float Left) Contact(List<Vector3> surface,Matrix[] absolute){
            Vector3 palm=(absolute[Bone("hand_R")].Translation+absolute[Bone("finger_middle_0_R")].Translation)*.5f;float toPalm=float.MaxValue,toJoint=float.MaxValue,toLeft=float.MaxValue;
            foreach(var p in surface){
                toPalm=Math.Min(toPalm,Vector3.Distance(p,palm));
                foreach(int i in rightHand)toJoint=Math.Min(toJoint,Vector3.Distance(p,absolute[i].Translation));
                foreach(int i in leftHand)toLeft=Math.Min(toLeft,Vector3.Distance(p,absolute[i].Translation));
            }
            return(toPalm,toJoint,toLeft);
        }
        // The body the way the game moves it for each posture; the controller runs one frame per call, as in the game.
        float crouchFactor=0;
        void Posture(string posture,float t){
            crouchFactor=posture=="crouch"?1:0;body.m_crouchFactor=crouchFactor;body.m_targetCrouchFactor=crouchFactor;
            body.StandingOnValue=posture=="jump"?null:2;body.Velocity=posture switch{"run"=>new Vector3(0,0,-5),"jump"=>new Vector3(0,-2,-1),_=>Vector3.Zero};
            body.Position=origin+(posture=="jump"?new Vector3(0,.8f,0):Vector3.Zero);
        }
        void Step(){Frame(1/60f);time.m_gameTime+=1/60.0;ModsManager.ModHooks["OnAnimateModel"]=hook;component.Animate();}
        Matrix[] Pose(string asset,ScThrowPhase? phase,ScPlantPhase plant=default,bool advance=true){
            if(advance)Step();
            var local=(Matrix?[])component.m_boneTransforms.Clone();
            if(plant.Active){if(!actions.ApplyPlant(local,plant,crouchFactor))failures.Add($"{name}: the actor has no plant clips");}
            else if(phase is {} p){if(!actions.ApplyThrowPosed(local,asset,p,crouchFactor))failures.Add($"{name} {asset}: the actor has no throw clips");}
            else actions.ApplyHeld(local,default,asset);
            Array.Copy(local,component.m_boneTransforms,local.Length);
            component.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,component.AbsoluteBoneTransformsForCamera);
            return component.AbsoluteBoneTransformsForCamera;
        }
        void Render(string file,string asset,ScThirdPersonWeapon weapon,Matrix[] absolute,bool shown){
            var joints=new Matrix[48];SubsystemModelsRenderer.CalculateJointMatrices(component,model,Matrix.Identity,joints);
            var hand=absolute[Bone("hand_R")].Translation;
            foreach(string side in new[]{"side","front","close"}){
                var (view,projection)=side=="close"?Camera(side,hand,.75f):Camera(side,body.Position+new Vector3(0,1f,0),3.2f);
                Begin();
                Display.BlendState=BlendState.Opaque;Display.DepthStencilState=DepthStencilState.Default;Display.RasterizerState=RasterizerState.CullCounterClockwiseScissor;
                shader.Transforms.World[0]=view;shader.Transforms.View=Matrix.Identity;shader.Transforms.Projection=projection;
                shader.InstancesCount=1;shader.JointMatrices=joints;shader.MaterialColor=Vector4.One;shader.EmissionColor=Vector4.Zero;
                shader.AmbientLightColor=new Vector3(.8f);shader.DiffuseLightColor1=shader.DiffuseLightColor2=new Vector3(.2f);shader.LightDirection1=Vector3.UnitY;shader.LightDirection2=Vector3.UnitZ;
                shader.FogColor=Vector3.Zero;shader.FogBottomTopDensity=Vector3.Zero;shader.HazeStartDensity=new Vector2(100,0);shader.FogYMultiplier=1;shader.WorldUp=Vector3.UnitY;shader.SamplerState=SamplerState.LinearWrap;
                foreach(var mesh in model.Meshes)foreach(var part in mesh.MeshParts){shader.Texture=model.GetTexture(model.GetMaterial(part.MaterialIndex).BaseColorTexture.TextureIndex);Display.DrawIndexed(PrimitiveType.TriangleList,shader,part.VertexBuffer,part.IndexBuffer,part.StartIndex,part.IndicesCount);}
                // A floor patch at the ground the body stands on in this posture.
                var floor=flat.FlatBatch(0,DepthStencilState.Default,RasterizerState.CullNone,BlendState.Opaque);var f0=origin;
                floor.QueueQuad(f0+new Vector3(-.8f,-.005f,-.8f),f0+new Vector3(.8f,-.005f,-.8f),f0+new Vector3(.8f,-.005f,.8f),f0+new Vector3(-.8f,-.005f,.8f),new Color(90,96,104));flat.Flush(view*projection);
                if(shown&&weapon!=null)DrawHeld(weapon,g=>actions.WorldPartFor(asset,g,absolute),view*projection);
                Save($"{name}-{file}-{side}");
            }
        }
        // The posture itself, measured on the actor: the crouch is carried by the legs (not a squash), the run moves
        // the feet under the throw, the jump lifts them.
        var legs=new Dictionary<string,(float Pelvis,float AnkleRange,float Stride,string Clip)>();
        foreach(string posture in postures){
            Posture(posture,0);for(int i=0;i<40;i++)Step();
            float pelvis=0,ankleLow=float.MaxValue,ankleHigh=float.MinValue;var ankles=new List<Vector3>();
            for(int i=0;i<40;i++){var a=Pose("grenade_hegrenade",null);pelvis=a[Bone("pelvis")].Translation.Y-body.Position.Y;var ank=a[Bone("ankle_R")].Translation-body.Position;ankles.Add(ank);ankleLow=Math.Min(ankleLow,ank.Y);ankleHigh=Math.Max(ankleHigh,ank.Y);}
            float stride=ankles.Max(p=>p.Z)-ankles.Min(p=>p.Z);
            string clip=component.AnimationController?.m_layerAnimationRef.TryGetValue("Base",out var r)==true?r?.Source:null;
            legs[posture]=(pelvis,ankleHigh-ankleLow,stride,clip);
        }
        if(!(legs["crouch"].Pelvis<legs["stand"].Pelvis-.2f))failures.Add($"{name}: crouching does not lower the pelvis by 0.2 m (stand {legs["stand"].Pelvis:0.00}, crouch {legs["crouch"].Pelvis:0.00})");
        if(!(legs["run"].Stride>.2f))failures.Add($"{name}: the running gait does not move the feet under the throw (stride {legs["run"].Stride:0.00} m)");
        report.Add(new{model=name,postureLegs=legs.ToDictionary(p=>p.Key,p=>new{pelvisAboveFeet=p.Value.Pelvis,ankleRange=p.Value.AnkleRange,strideZ=p.Value.Stride,clip=p.Value.Clip})});
        foreach(string posture in postures)foreach(string asset in grenades){
            var weapon=ScThirdPersonWeapon.For(asset)??throw new Exception("no third-person mesh for "+asset);
            foreach(bool low in new[]{false,true}){
                Posture(posture,0);for(int i=0;i<20;i++)Step();
                var rows=new List<object>();
                foreach(var (label,t) in shots){
                    if(low&&label is "hold" or "pull-mid" or "pull-end")continue;
                    ScThrowPhase? phase=t<0?null:At(asset,t,low);var absolute=Pose(asset,phase);bool shown=phase is not {Released:true};
                    var (centre,size)=Bounds(weapon,g=>actions.WorldPartFor(asset,g,absolute));
                    var contact=Contact(Surface(weapon,g=>actions.WorldPartFor(asset,g,absolute)),absolute);bool lettingGo=phase is {Stage:2,Wind:>=LetGo};
                    rows.Add(new{stage=label,t,shown,meshSize=size,surfaceToPalm=contact.Palm,surfaceToNearestRightHandJoint=contact.Joint,surfaceToNearestLeftHandJoint=contact.Left});
                    if(shown&&(lettingGo?contact.Joint>FingerReach:contact.Palm>PalmReach||contact.Joint>JointReach))failures.Add($"{name} {posture} {asset} {(low?"weak":"strong")} {label}: the throwable is not in the hand (surface {contact.Palm:0.000} m from the palm, {contact.Joint:0.000} m from the nearest joint)");
                    if(label=="pull-mid"&&contact.Left>.1f)failures.Add($"{name} {posture} {asset} {label}: the left hand is {contact.Left:0.000} m from the throwable while pulling the pin");
                    bool render=posture=="stand"?asset is "grenade_hegrenade" or "grenade_molotov" or "grenade_flashbang"||label=="hold":asset is "grenade_hegrenade" or "grenade_molotov"&&!low&&label is "held" or "wind-mid" or "release";
                    if(render)Render($"{asset}-{(low?"weak":"strong")}-{posture}-{label}",asset,weapon,absolute,shown);
                }
                // Continuity at 60 frames a second through the whole throw, on the live gait.
                Vector3? previousHand=null;float handJump=0,palmApart=0,jointApart=0,fingerApart=0;string where="",apartAt="";
                for(float t=0;t<=PullSeconds+HoldSeconds+WindSeconds+FollowSeconds;t+=1/60f){
                    var phase=At(asset,t,low);var absolute=Pose(asset,phase);var hand=absolute[Bone("hand_R")].Translation-body.Position;
                    if(previousHand is {} h&&Vector3.Distance(h,hand)>handJump){handJump=Vector3.Distance(h,hand);where=$"t={t:0.00}";}
                    if(!phase.Released){
                        var contact=Contact(Surface(weapon,g=>actions.WorldPartFor(asset,g,absolute)),absolute);
                        if(phase is {Stage:2,Wind:>=LetGo})fingerApart=Math.Max(fingerApart,contact.Joint);
                        else{if(contact.Palm>palmApart){palmApart=contact.Palm;apartAt=$"t={t:0.00}";}jointApart=Math.Max(jointApart,contact.Joint);}
                    }
                    previousHand=hand;
                }
                if(handJump>.4f)failures.Add($"{name} {posture} {asset} {(low?"weak":"strong")}: the right hand jumps {handJump:0.00} m in one frame at {where}");
                if(palmApart>PalmReach||jointApart>JointReach)failures.Add($"{name} {posture} {asset} {(low?"weak":"strong")}: the throwable leaves the hand before it is let go (surface {palmApart:0.000} m from the palm at {apartAt}, {jointApart:0.000} m from the nearest joint)");
                if(fingerApart>FingerReach)failures.Add($"{name} {posture} {asset} {(low?"weak":"strong")}: while letting go the throwable is {fingerApart:0.000} m from the nearest joint, beyond the fingertips");
                string pull=actions.ThrowClipFor(asset,0,low,posture=="crouch"),throwing=actions.ThrowClipFor(asset,2,low,posture=="crouch");
                report.Add(new{model=name,posture,asset,strength=low?"weak":"strong",worldClips=new{pull,throwing,releaseSeconds=actions.ReleaseTime(throwing)},
                    largestHandStepPerFrame=handJump,largestSurfaceToPalmBeforeLettingGo=palmApart,largestSurfaceToPalmAt=apartAt,largestSurfaceToJointBeforeLettingGo=jointApart,largestSurfaceToJointWhileLettingGo=fingerApart,stages=rows});
                coverage.Add(new{model=name,posture,asset,strength=low?"weak":"strong"});
            }
        }
        // Planting the C4: the game crouches the player (the factor rises over about a quarter second) and the enemy.
        if(c4Weapon is not null)foreach(string start in new[]{"stand","crouch"}){
            Posture(start,0);for(int i=0;i<20;i++)Step();
            var rows=new List<object>();float handJump=0,held=0,atCommit=float.NaN,lowest=float.MaxValue;Vector3 commitCentre=default;Vector3? previousHand=null;
            for(float t=0;t<ScPlantPhase.EndSeconds;t+=1/60f){
                crouchFactor=start=="crouch"?1:Math.Min(1,t/.25f);body.m_crouchFactor=crouchFactor;
                var plant=Planting(t);var absolute=Pose("c4",null,plant);var hand=absolute[Bone("hand_R")].Translation-body.Position;
                if(previousHand is {} h)handJump=Math.Max(handJump,Vector3.Distance(h,hand));previousHand=hand;
                if(!plant.Placed){
                    var surface=Surface(c4Weapon,g=>actions.WorldPartFor("c4",g,absolute));var contact=Contact(surface,absolute);
                    string clip=actions.PlantClip(crouchFactor>=.5f);float clipTime=actions.PlantClipTime(clip,plant),place=actions.PlaceTime(clip);
                    if(clipTime<place-.35f)held=Math.Max(held,Math.Min(contact.Joint,contact.Left));
                    float bottom=surface.Min(p=>p.Y)-origin.Y;lowest=Math.Min(lowest,bottom);
                    if(t>=ScPlantPhase.PlantSeconds-1/60f){atCommit=bottom;commitCentre=surface.Aggregate(Vector3.Zero,(a,p)=>a+p)/surface.Count-origin;}
                }
            }
            if(held>JointReach+.02f)failures.Add($"{name} plant from {start}: the C4 is {held:0.000} m from the nearest joint of either hand while carried");
            // CS2's planting clip never rests the bomb on the ground: its socket bottoms out at ankle height and the brick
            // stays about 0.17 m up (c04); the charge appears on the ground at the commit. The commit must be the clip's own
            // set-down moment: the bomb at its lowest point of the whole plant, and not held high.
            // Where the clip leaves the bomb: late in the recovery, while the plant clip still fully drives the body.
            crouchFactor=1;body.m_crouchFactor=1;float rest;{var late=Planting(ScPlantPhase.EndSeconds-.25f);var lateAbs=Pose("c4",null,late);rest=Surface(c4Weapon,g=>actions.WorldPartFor("c4",g,lateAbs)).Min(p=>p.Y)-origin.Y;}
            if(!(Math.Abs(atCommit-rest)<=.03f&&atCommit<=.2f))failures.Add($"{name} plant from {start}: at the commit the C4 is {atCommit:0.000} m above the ground, where the clip leaves it {rest:0.000} m (lowest dip {lowest:0.000} m); the commit must be the set-down (within 0.03 m of the rest, at most 0.2 m)");
            if(handJump>.3f)failures.Add($"{name} plant from {start}: a hand jumps {handJump:0.00} m in one frame");
            foreach(var (label,t) in plantShots){
                crouchFactor=start=="crouch"?1:Math.Min(1,t/.25f);body.m_crouchFactor=crouchFactor;var plant=Planting(t);var absolute=Pose("c4",null,plant);string clip=actions.PlantClip(crouchFactor>=.5f);
                rows.Add(new{stage=label,t,placed=plant.Placed,clip,clipTime=actions.PlantClipTime(clip,plant)});
                if(start=="stand")Render($"c4-plant-{label}","c4",c4Weapon,absolute,!plant.Placed);
            }
            report.Add(new{model=name,plant=start,placeTime=actions.PlaceTime(actions.PlantClip(true)),largestCarriedGap=held,c4AboveGroundAtCommit=atCommit,c4LowestInPlant=lowest,c4Rest=rest,c4CentreAtCommit=new[]{commitCentre.X,commitCentre.Y,commitCentre.Z},largestHandStepPerFrame=handJump,rows});
            coverage.Add(new{model=name,posture="plant-from-"+start,asset="c4"});
        }
        // Round 12 (2026-10-01): the CS player appearance's aim pitch, as CsPlayerPose applies it (hold pose, then ApplyAimPitch
        // with the look pitch, up positive). r11b turned it the wrong way (user: "第一人称视角抬头看天，切换第三人称却是人物头埋地").
        // Up must lift the barrel, the muzzle and the face, down lower them; the barrel follows the whole pitch without roll
        // or yaw, the head about 80 % of it (spine 30 % + neck 50 %). Rifles and a pistol from the user's log; the AK-47 / M4A1-S /
        // AWP ship as OBJ pieces this harness has no reader for (their weapon build throws here, not in the game).
        foreach(string gun in new[]{"m4a4","famas","deagle"}){
            var weapon=ScThirdPersonWeapon.For(gun,ScGunNativeMesh.Resolve(gun,0,out _,out _)!=null);
            if(weapon is null||!weapon.HasRightGrip){failures.Add($"{name} {gun}: no third-person weapon for the aim-pitch check");continue;}
            Posture("stand",0);for(int i=0;i<20;i++)Step();
            var baseLocal=(Matrix?[])component.m_boneTransforms.Clone();
            (Vector3 Barrel,Vector3 Side,Matrix Head,Vector3 Muzzle) Aim(float pitch){
                var local=(Matrix?[])baseLocal.Clone();actions.ApplyHeld(local,default,gun);actions.ApplyAimPitch(local,pitch,gun);
                Array.Copy(local,component.m_boneTransforms,local.Length);
                component.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,component.AbsoluteBoneTransformsForCamera);
                var abs=component.AbsoluteBoneTransformsForCamera;var w=actions.RootWorld(weapon,abs);
                return(Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ,w)),Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX,w)),abs[Bone("head_0")],Vector3.Transform(weapon.Muzzle,w));
            }
            float Elevation(Vector3 v)=>MathF.Asin(Math.Clamp(Vector3.Normalize(v).Y,-1,1));
            // The gun's cant about its own barrel: its side axis against the horizontal square to the barrel.
            float Cant(Vector3 barrel,Vector3 side){var h=Vector3.Normalize(Vector3.Cross(Vector3.UnitY,barrel));var v=Vector3.Cross(barrel,h);return MathF.Atan2(Vector3.Dot(side,v),Vector3.Dot(side,h));}
            var level=Aim(0);
            Vector3 front=Vector3.Normalize(new Vector3(level.Barrel.X,0,level.Barrel.Z));
            // The head's own axis that faces forward in the level pose.
            Vector3 face=new[]{Vector3.UnitX,-Vector3.UnitX,Vector3.UnitY,-Vector3.UnitY,Vector3.UnitZ,-Vector3.UnitZ}
                .MaxBy(a=>Vector3.Dot(Vector3.Normalize(Vector3.TransformNormal(a,level.Head)),front));
            float Face(Matrix head)=>Elevation(Vector3.TransformNormal(face,head));
            var rows=new List<object>();
            foreach(float pitch in new[]{.6f,-.6f,1.2f}){
                var aimed=Aim(pitch);
                float barrel=Elevation(aimed.Barrel)-Elevation(level.Barrel),head=Face(aimed.Head)-Face(level.Head),roll=Cant(aimed.Barrel,aimed.Side)-Cant(level.Barrel,level.Side),muzzle=aimed.Muzzle.Y-level.Muzzle.Y;
                float yaw=Vector3.Dot(Vector3.Normalize(new Vector3(aimed.Barrel.X,0,aimed.Barrel.Z)),front);
                rows.Add(new{pitch,barrelFollows=barrel,headFollows=head,cantChange=roll,muzzleRise=muzzle,yawDot=yaw});
                string where=$"{name} {gun} pitch {pitch:+0.0;-0.0}";
                if(MathF.Sign(barrel)!=MathF.Sign(pitch)||Math.Abs(barrel-pitch)>.2f*Math.Abs(pitch))failures.Add($"{where}: the barrel turns {barrel:+0.00;-0.00} rad instead of following the look");
                if(MathF.Sign(muzzle)!=MathF.Sign(pitch))failures.Add($"{where}: the muzzle moves {muzzle:+0.00;-0.00} m against the look");
                if(MathF.Sign(head)!=MathF.Sign(pitch)||Math.Abs(head)<.5f*Math.Abs(pitch)||Math.Abs(head)>1.05f*Math.Abs(pitch))failures.Add($"{where}: the face turns {head:+0.00;-0.00} rad (expected about 80 % of the look, same direction)");
                if(Math.Abs(roll)>.1f||yaw<.98f)failures.Add($"{where}: the gun rolls ({roll:+0.00;-0.00} rad about its barrel) or yaws (front dot {yaw:0.000})");
            }
            report.Add(new{model=name,aimPitch=gun,levelBarrelElevation=Elevation(level.Barrel),faceAxis=face.ToString(),rows});
            coverage.Add(new{model=name,posture="aim-pitch",asset=gun});
        }
        component.OnEntityRemoved();
    }

    // ---------------- rigid-arm humans: vanilla and NekoMeko Model ----------------
    var models=new List<(string Name,Func<Stream> Open,string Right,string Left,bool Nmm,string Key)>{
        ("HumanMale",()=>content.GetEntry("Assets/Models/HumanMale.dae").Open(),"Hand2","Hand1",false,"vanilla HumanMale"),
        ("HumanFemale",()=>content.GetEntry("Assets/Models/HumanFemale.dae").Open(),"Hand2","Hand1",false,"vanilla HumanFemale")};
    System.IO.Compression.ZipArchive nmm=null;string nmmSha=null,nmmVersion=null;
    if(nmmPath is {Length:>0}&&File.Exists(nmmPath)){
        byte[] bytes=File.ReadAllBytes(nmmPath);nmmSha=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();nmm=new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        using(var info=new StreamReader(nmm.GetEntry("modinfo.json").Open())){var mi=JsonNode.Parse(info.ReadToEnd());nmmVersion=$"{mi["Name"]} {mi["Version"]} ({mi["PackageName"]})";}
        // NMM assigns the upper arms to the human model's hand bones (ComponentNekoMekoModel: m_hand2Bone = Arm2).
        foreach(var (key,entry) in new[]{("Model_McClassic","Assets/Models/BoneSet_Minecraft/Classic.dae"),("Model_McSlim","Assets/Models/BoneSet_Minecraft/Slim.dae"),("Model_ScMale","Assets/Models/BoneSet_ScMale/HumanMale.dae"),("Model_ScFemale","Assets/Models/BoneSet_ScFemale/HumanFemale.dae")}){
            var e=nmm.GetEntry(entry);if(e==null){failures.Add("NMM package lacks "+entry);continue;}
            models.Add(("NMM-"+key[6..],()=>e.Open(),"Arm2","Arm1",true,key));
        }
    }else failures.Add("third-party model package not found ("+nmmPath+"): NMM skeletons not checked");
    foreach(var (name,open,rightName,leftName,isNmm,key) in models){
        using var raw=open();using var memory=new MemoryStream();raw.CopyTo(memory);memory.Position=0;
        var data=Collada.Load(memory);using var model=Model.Load(data,true);
        ModelBone hand2=model.FindBone(rightName,true),hand1=model.FindBone(leftName,true),bodyBone=model.FindBone("Body",true);
        Vector3 fistRight=ScThirdPerson.FistLocal(model,hand2,true),fistLeft=ScThirdPerson.FistLocal(model,hand1,false);
        BoundingBox? box=ScThirdPerson.ArmBox(model,hand2);
        if(box is null)failures.Add(name+": the right arm has no usable box (the male-constant fallback would be used)");
        if(name=="HumanMale"&&Vector3.Distance(fistRight,ScThirdPersonMath.HandEndLocal(true))>.35f)failures.Add($"male fist from the arm box {fistRight} differs from the measured constant {ScThirdPersonMath.HandEndLocal(true)}");
        // The fist must be at the lower end of the whole arm (the forearm mesh on NMM's Hand bone), not at the elbow.
        if(box is {} armBox&&fistRight.Z>armBox.Min.Z+.5f*(armBox.Max.X-armBox.Min.X))failures.Add($"{name}: the fist {fistRight} is not at the lower end of the arm box {armBox}");
        Matrix[] Compose(Matrix?[] local){var result=new Matrix[model.Bones.Count];
            Matrix Absolute(ModelBone bone){Matrix m=bone.Transform;if(local[bone.Index] is {} a){var t=m.Translation;m.Translation=Vector3.Zero;m*=a;m.Translation+=t;}return bone.ParentBone is null?m:m*Absolute(bone.ParentBone);}
            foreach(var bone in model.Bones)result[bone.Index]=Absolute(bone);return result;}
        // The posed body. Vanilla: the engine's HumanWalkDriver with the parameters the game feeds it. NMM: its own
        // vanilla replica (ComponentNekoMekoModel.AnimateCreatureVanilla): Body, Head, Arm1/2 and Leg1/2; other bones at bind.
        var driver=new HumanWalkDriver();var parameters=new AnimationParameters();
        Matrix?[] Posed(string posture,float crouch,float t){
            float walk=posture=="run"?1:0,phase=posture=="run"?t*1.6f%1:0;
            var local=new Matrix?[model.Bones.Count];float lift=posture=="jump"?.8f:0;
            if(!isNmm){
                parameters.SetFloat("MovementPhase",phase);parameters.SetFloat("Bob",0);parameters.SetFloat("RotationY",0);parameters.SetVector3("Position",new Vector3(0,lift,0));
                parameters.SetFloat("LookAngleX",0);parameters.SetFloat("LookAngleY",0);parameters.SetFloat("WalkLegsAngle",.55f);parameters.SetFloat("WalkBobHeight",.1f);parameters.SetFloat("HeadingOffset",0);
                parameters.SetFloat("CrouchFactor",crouch);parameters.SetBool("IsCreativeFly",false);parameters.SetFloat("LastTurnOrderX",0);parameters.SetFloat("EntityHash",0);parameters.SetFloat("VelocityXZ",5*walk);
                parameters.SetFloat("TotalElapsedGameTime",t);parameters.SetFloat("GameTimeDelta",1/60f);parameters.SetFloat("LieDownFactor",0);
                driver.Update(1/60f,parameters);driver.SampleTransforms(local,model);
            }else{
                float f=MathUtils.Sigmoid(crouch,4),swing=.55f*MathF.Sin(MathF.PI*2*phase)*(crouch>=1?.5f:1);
                local[bodyBone.Index]=Matrix.CreateTranslation(0,lift-MathUtils.Lerp(0,.7f,f),0);
                foreach(var (leg,sign) in new[]{("Leg1",1f),("Leg2",-1f)})if(model.FindBone(leg,false) is {} b)local[b.Index]=Matrix.CreateRotationX(sign*swing)*Matrix.CreateTranslation(0,MathUtils.Lerp(0,.16f,f),MathUtils.Lerp(0,.68f,f))*Matrix.CreateScale(1,1,MathUtils.Lerp(1,.5f,f));
            }
            return local;
        }
        void Render(string file,ScThirdPersonWeapon weapon,Matrix?[] local,Matrix world,bool shown,Vector3 fist){
            var absolute=Compose(local);
            foreach(string side in new[]{"side","front","close"}){
                var (view,projection)=side=="close"?Camera(side,fist,.75f):Camera(side,new Vector3(0,1f,0),3.2f);
                Begin();var batch=flat.FlatBatch(0,DepthStencilState.Default,RasterizerState.CullNone,BlendState.Opaque);
                foreach(var mesh in data.Meshes){
                    Matrix m=absolute[mesh.ParentBoneIndex];
                    foreach(var part in mesh.MeshParts){
                        var buffer=data.Buffers[part.BuffersDataIndex];int stride=buffer.VertexDeclaration.VertexStride;
                        var position=buffer.VertexDeclaration.VertexElements.First(e=>e.Semantic.StartsWith("POSITION"));
                        int total=data.Meshes.SelectMany(x=>x.MeshParts).Where(x=>x.BuffersDataIndex==part.BuffersDataIndex).Max(x=>x.StartIndex+x.IndicesCount);
                        int bytes=buffer.Indices.Length>=total*4?4:2;
                        Vector3 Vertex(int k){int index=bytes==4?BitConverter.ToInt32(buffer.Indices,(part.StartIndex+k)*4):BitConverter.ToUInt16(buffer.Indices,(part.StartIndex+k)*2);
                            return Vector3.Transform(new Vector3(BitConverter.ToSingle(buffer.Vertices,index*stride+position.Offset),BitConverter.ToSingle(buffer.Vertices,index*stride+position.Offset+4),BitConverter.ToSingle(buffer.Vertices,index*stride+position.Offset+8)),m);}
                        for(int k=0;k+2<part.IndicesCount;k+=3){Vector3 a=Vertex(k),b=Vertex(k+1),c=Vertex(k+2);var normal=Vector3.Cross(b-a,c-a);normal=normal.LengthSquared()>1e-12f?Vector3.Normalize(normal):Vector3.UnitY;
                            float light=.55f+.45f*MathF.Abs(Vector3.Dot(normal,Vector3.Normalize(new Vector3(.4f,.8f,-.45f))));bool arm=mesh.Name.Contains("Hand")||mesh.Name.Contains("Arm");
                            batch.QueueTriangle(a,b,c,arm?new Color((byte)(205*light),(byte)(160*light),(byte)(130*light)):new Color((byte)(96*light),(byte)(128*light),(byte)(176*light)));}
                    }
                }
                var floor=flat.FlatBatch(0,DepthStencilState.Default,RasterizerState.CullNone,BlendState.Opaque);
                floor.QueueQuad(new Vector3(-.8f,-.005f,-.8f),new Vector3(.8f,-.005f,-.8f),new Vector3(.8f,-.005f,.8f),new Vector3(-.8f,-.005f,.8f),new Color(90,96,104));
                flat.Flush(view*projection);
                if(shown)DrawHeld(weapon,g=>(g.Mesh,world),view*projection);
                Save($"{name}-{file}-{side}");
            }
        }
        // Seen and held, for an arm that is a box: the share of the body's surface outside the box (by triangle area),
        // whether it touches the box, how deep it sinks into the arm's lower end, and for a body lying across the fist,
        // how far it shows behind and ahead of it. All in the frame of the arm bone the game rotates.
        (float Visible,float Gap,bool Across,float Behind,float Ahead,float Sunk,float Below) Seen(ScThirdPersonWeapon weapon,Matrix world,Matrix handWorld){
            if(box is not {} arm)return(0,float.MaxValue,false,0,0,0,0);
            Matrix toHand=world*Matrix.Invert(handWorld);float metres=Vector3.TransformNormal(Vector3.UnitX,handWorld).Length();
            bool Inside(Vector3 p)=>p.X>arm.Min.X&&p.X<arm.Max.X&&p.Y>arm.Min.Y&&p.Y<arm.Max.Y&&p.Z>arm.Min.Z&&p.Z<arm.Max.Z;
            float total=0,outside=0,gap=float.MaxValue;Vector3 min=new(float.MaxValue),max=new(float.MinValue);
            foreach(var group in weapon.Groups.Where(Solid)){
                var points=group.Mesh.Vertices.Select(v=>Vector3.Transform(v.Position,toHand)).ToArray();
                foreach(var p in points){gap=Math.Min(gap,Vector3.Max(Vector3.Max(arm.Min-p,p-arm.Max),Vector3.Zero).Length()*metres);min=Vector3.Min(min,p);max=Vector3.Max(max,p);}
                for(int i=0;i+2<group.Mesh.Indices.Count;i+=3){
                    Vector3 a=points[group.Mesh.Indices[i]],b=points[group.Mesh.Indices[i+1]],c=points[group.Mesh.Indices[i+2]];
                    float area=Vector3.Cross(b-a,c-a).Length();total+=area;if(!Inside((a+b+c)/3))outside+=area;
                }
            }
            Vector3 size=max-min;bool across=Math.Max(size.X,Math.Max(size.Y,size.Z))>=ScThirdPersonMath.ThroughFist*(arm.Max.X-arm.Min.X);
            return(total>0?outside/total:0,gap,across,(arm.Min.Y-min.Y)*metres,(max.Y-arm.Max.Y)*metres,(max.Z-arm.Min.Z)*metres,(arm.Min.Z-min.Z)*metres);
        }
        var rows=new List<object>();
        void Measure(string label,string asset,ScThirdPersonWeapon weapon,(Matrix?[] Local,Matrix World,Vector3 Fist,Vector3 LeftFist,Vector2 Right,Vector2 Left,bool Shown,Matrix Hold) solved,string posture,bool low,float t){
            var (centre,size)=Bounds(weapon,g=>(g.Mesh,solved.World));
            var seen=Seen(weapon,solved.World,Compose(solved.Local)[hand2.Index]);
            float toLeft=Surface(weapon,g=>(g.Mesh,solved.World)).Min(p=>Vector3.Distance(p,solved.LeftFist));
            rows.Add(new{asset,posture,strength=low?"weak":"strong",stage=label,t,shown=solved.Shown,meshSize=size,visibleSurfaceShare=seen.Visible,gapToArm=seen.Gap,sunkIntoArmEnd=seen.Sunk,showsBelowArmEnd=seen.Below,
                liesAcrossFist=seen.Across,showsBehindFist=seen.Behind,showsAheadOfFist=seen.Ahead,centreToFist=Vector3.Distance(centre,solved.Fist),surfaceToLeftFist=toLeft,right=solved.Right.ToString(),left=solved.Left.ToString()});
            if(solved.Shown){
                if(seen.Gap>.005f)failures.Add($"{name} {posture} {asset} {label}: the item floats {seen.Gap:0.000} m off the arm");
                if(seen.Sunk<.005f||seen.Sunk>.05f)failures.Add($"{name} {posture} {asset} {label}: the item sinks {seen.Sunk:0.000} m into the arm's end; held in the hand it is between 0.005 and 0.05 m");
                if(seen.Visible<.6f)failures.Add($"{name} {posture} {asset} {label}: only {seen.Visible:P0} of the item's surface is outside the arm; it cannot be seen in the hand");
                if(seen.Across&&(seen.Behind<.01f||seen.Ahead<.03f))failures.Add($"{name} {posture} {asset} {label}: lying across the fist, the body shows {seen.Behind:0.000} m behind and {seen.Ahead:0.000} m ahead of it; both ends must show");
            }
            if(label=="pull-mid"&&toLeft>.12f)failures.Add($"{name} {posture} {asset} {label}: the left hand is {toLeft:0.000} m from the throwable while pulling the pin");
        }
        foreach(string posture in postures)foreach(string asset in grenades){
            var weapon=ScThirdPersonWeapon.For(asset);
            foreach(bool low in new[]{false,true}){
                float crouch=posture=="crouch"?1:0;
                foreach(var (label,t) in shots){
                    if(low&&label is "hold" or "pull-mid" or "pull-end")continue;
                    var phase=t<0?default:At(asset,t,low);var solved=ScThirdPerson.ActionPreview(model,rightName,leftName,asset,phase,default,Posed(posture,crouch,Math.Max(0,t)));
                    Measure(label,asset,weapon,solved,posture,low,t);
                    bool render=posture=="stand"?(isNmm?asset is "grenade_hegrenade" or "grenade_molotov"&&!low&&label is "hold" or "held" or "release":asset is "grenade_hegrenade" or "grenade_molotov" or "grenade_flashbang"||label=="hold")
                        :(!isNmm||name=="NMM-McClassic")&&asset is "grenade_hegrenade" or "grenade_molotov"&&!low&&label is "held" or "release";
                    if(render)Render($"{asset}-{(low?"weak":"strong")}-{posture}-{label}",weapon,solved.Local,solved.World,solved.Shown,solved.Fist);
                }
                Vector3? previous=null;float jump=0;string where="";
                for(float t=0;t<=PullSeconds+HoldSeconds+WindSeconds+FollowSeconds;t+=1/60f){var solved=ScThirdPerson.ActionPreview(model,rightName,leftName,asset,At(asset,t,low),default,Posed(posture,crouch,t));
                    if(previous is {} p&&Vector3.Distance(p,solved.Fist)>jump){jump=Vector3.Distance(p,solved.Fist);where=$"t={t:0.00}";}previous=solved.Fist;}
                if(jump>.4f)failures.Add($"{name} {posture} {asset} {(low?"weak":"strong")}: the fist jumps {jump:0.00} m in one frame at {where}");
                rows.Add(new{asset,posture,strength=low?"weak":"strong",stage="continuity",largestFistStepPerFrame=jump,at=where});
                coverage.Add(new{model=name,posture,asset,strength=low?"weak":"strong"});
            }
        }
        // The C4 plant: crouched by the game, the bomb carried by the right hand until the commit.
        if(c4Weapon is not null){
            float lowestAtCommit=float.NaN,jump=0;Vector3? previous=null;
            for(float t=0;t<ScPlantPhase.EndSeconds;t+=1/60f){
                var plant=Planting(t);var solved=ScThirdPerson.ActionPreview(model,rightName,leftName,"c4",default,plant,Posed("crouch",Math.Min(1,t/.25f),t));
                if(previous is {} p)jump=Math.Max(jump,Vector3.Distance(p,solved.Fist));previous=solved.Fist;
                if(!plant.Placed&&t>=ScPlantPhase.PlantSeconds-1/60f)lowestAtCommit=Surface(c4Weapon,g=>(g.Mesh,solved.World)).Min(q=>q.Y);
                if(plant.Placed&&solved.Shown)failures.Add($"{name}: the C4 is still drawn in the hand after the commit");
            }
            foreach(var (label,t) in plantShots){var plant=Planting(t);var solved=ScThirdPerson.ActionPreview(model,rightName,leftName,"c4",default,plant,Posed("crouch",Math.Min(1,t/.25f),t));
                Measure(label,"c4",c4Weapon,solved,"plant",false,t);
                if(!isNmm||name=="NMM-McClassic")Render($"c4-plant-{label}",c4Weapon,solved.Local,solved.World,solved.Shown,solved.Fist);}
            if(jump>.3f)failures.Add($"{name}: the fist jumps {jump:0.00} m in one frame while planting");
            rows.Add(new{asset="c4",posture="plant",stage="summary",c4LowestAboveFeetAtCommit=lowestAtCommit,largestFistStepPerFrame=jump});
            coverage.Add(new{model=name,posture="plant",asset="c4"});
        }
        report.Add(new{model=name,key,source=isNmm?$"{nmmVersion} sha256 {nmmSha}":"Content.zip",handBones=new[]{rightName,leftName},armParent=hand2.ParentBone?.Name,armBox=box?.ToString(),fistRight=fistRight.ToString(),fistLeft=fistLeft.ToString(),
            skeleton=model.Bones.Select(b=>b.Name+(b.ParentBone is null?"":"<"+b.ParentBone.Name)).ToArray(),maleConstant=ScThirdPersonMath.HandEndLocal(true).ToString(),rows});
    }
    nmm?.Dispose();
}catch(Exception e){Console.Error.WriteLine(e);failures.Add(e.GetType().Name+": "+e.Message);}finally{Display.RenderTarget=null;Window.Close();}};
Window.Run(480,640,WindowMode.Fixed,"Throw pose diagnostic");
File.WriteAllText(Path.Combine(output,"throw.json"),JsonSerializer.Serialize(new{failures,coverage,report,scope="Native offline poses and renders; not a game recording"},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"throw pose failures={failures.Count} cases={coverage.Count}");foreach(var f in failures.Take(40))Console.WriteLine("FAIL "+f);
return failures.Count==0?0:1;
