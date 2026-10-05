// agent-followup-140 F3: offline native evidence for jump states and the jointed corpse. Drives the real
// ComponentTacticalModel through the game's OnAnimateModel sampler on real terrain cells and renders timestamped frames.
// Not a game recording: Windows real-game video at 30/60/120 fps remains the acceptance evidence.
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Engine.Animation;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

if(args.Length!=3)throw new ArgumentException("ActorMotionCheck <Assets folder> <Content.zip> <output folder>");
string assets=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[2]);Directory.CreateDirectory(output);
using var content=ZipFile.OpenRead(args[1]);
string Read(string suffix){using var r=new StreamReader(content.Entries.Single(e=>e.FullName.EndsWith(suffix)).Open());return r.ReadToEnd();}
AnimationTemplateManager.LoadFromJsonNode(JsonNode.Parse(Read("Simple.template.json")));
var caches=(IDictionary<string,List<object>>)typeof(ContentManager).GetField("Caches",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
foreach(string role in new[]{"ct","t"}){
    string name="Animations/ScCsgoTactical/"+role+".scanim";var info=new ContentInfo(name);info.SetContentStream(new MemoryStream(File.ReadAllBytes(Path.Combine(assets,name))));ContentManager.Add(info);
}
foreach(var e in content.Entries.Where(e=>e.FullName.Contains("Shaders/")&&!e.FullName.EndsWith('/'))){using var reader=new StreamReader(e.Open());caches[e.FullName.Replace("Assets/","")]=[reader.ReadToEnd()];}
var frameDuration=typeof(Time).GetProperty("FrameDuration");var frameIndex=typeof(Time).GetProperty("FrameIndex");
// Each step is a new engine frame: Animate caches its pose per Time.FrameIndex, as in the game.
void Frame(float dt){frameDuration.SetValue(null,dt);frameIndex.SetValue(null,Time.FrameIndex+1);}
BlocksManager.Blocks[0]=new AirBlock{BlockIndex=0,IsCollidable=false};BlocksManager.Blocks[2]=new DirtBlock{BlockIndex=2,IsCollidable=true};
var report=new List<object>();var failures=new List<string>();
bool done=false;int exit=0;
Window.Frame+=()=>{if(done)return;done=true;try{
    LightingManager.Initialize();
    // The game samples CS actors through the OnAnimateModel hook (TacticalModLoader -> ScActorSampler); the native
    // controller path is only the fallback. Frames use the hook; a twin without it checks the two agree.
    var animateHook=new ModsManager.ModHook("OnAnimateModel");animateHook.Add(new TacticalModLoader());
    using var target=new RenderTarget2D(480,640,1,ColorFormat.Rgba8888,DepthFormat.Depth24Stencil8);
    using var shader=new ModelShader(Read("Shaders/Model.vsh"),Read("Shaders/Model.psh"),false,1,48);
    var ground=new PrimitivesRenderer3D();
    foreach(string name in new[]{"ct","t"}){
        using var stream=File.OpenRead(Path.Combine(assets,"Models/ScCsgoTactical/"+name+".glb"));
        using var model=Model.Load(GltfLoader.Load(stream),true);
        ScActorAnimations.Ensure(model);
        var clips=model.Animations.Select(a=>a.Name).ToHashSet();
        foreach(string clip in new[]{"jump","jumprun","air","airrun","crouchjump","crouchair","aimjump","aimjumprun","aimair","aimairrun","aimcrouchjump","aimcrouchair"})
            if(!clips.Contains(clip))failures.Add($"{name}: packaged cache lacks {clip}");
        foreach(string scenario in new[]{"hit-regions","jump-stand","jump-run","crouch-idle","crouch-walk","crouch-toggle","fall-2s","fall-5s","fall-10s","fall-10s-moving","fall-speed-jitter","fall-fly-toggle","pose-sheet","death-flat","death-step","death-wall"}){
            if(scenario=="pose-sheet"&&name!="ct")continue; // CT and T share the skeleton and the clips
            var project=new Project();var time=new SubsystemTime();var terrain=new SubsystemTerrain{Terrain=new Terrain()};
            foreach(var sub in new Subsystem[]{new SubsystemSky(),time,new SubsystemGameInfo(),terrain}){sub.m_project=project;project.m_subsystems.Add(sub);}
            var origin=new Vector3(105.5f,70,-212.5f);
            for(int cx=-1;cx<=1;cx++)for(int cz=-1;cz<=1;cz++)terrain.Terrain.AllocateChunk((105>>4)+cx,(-213>>4)+cz).State=TerrainChunkState.Valid;
            var solid=new List<Point3>();void Put(int x,int y,int z){terrain.Terrain.SetCellValueFast(x,y,z,2);solid.Add(new(x,y,z));}
            bool hanging=scenario.StartsWith("fall")||scenario=="pose-sheet"; // pose evidence only: the actor is drawn in place, without ground
            if(!hanging)for(int x=100;x<=111;x++)for(int z=-218;z<=-207;z++)Put(x,69,z);
            if(scenario=="death-step")for(int x=100;x<=111;x++)for(int z=-218;z<=-214;z++)Put(x,70,z);      // one block up, just behind
            if(scenario=="death-wall")for(int x=100;x<=111;x++)for(int y=70;y<=72;y++)Put(x,y,-214);     // wall 1 m behind
            var body=new ComponentBody{Position=origin,BoxSize=new Vector3(.65f,1.8f,.65f),Rotation=Quaternion.Identity,StandingOnValue=2};
            var locomotion=new ComponentLocomotion();
            var creature=new ComponentCreature{ComponentBody=body,ComponentHealth=new ComponentHealth{Health=1},ComponentSpawn=new ComponentSpawn{SpawnDuration=0},ComponentLocomotion=locomotion};
            var component=new ComponentTacticalModel();var entity=(Entity)RuntimeHelpers.GetUninitializedObject(typeof(Entity));entity.m_project=project;
            entity.m_components=[component,body,creature,creature.ComponentHealth,creature.ComponentSpawn,locomotion];foreach(var c in entity.m_components)c.m_entity=entity;
            creature.ComponentHealth.m_componentCreature=creature;
            caches["Fixture/Model"]=[model];caches["Fixture/Config.json"]=[File.ReadAllText(Path.Combine(assets,"Animations/ScTactical.json"))];
            component.Load(new ValuesDictionary{{"ModelName","Fixture/Model"},{"CastsShadow",true},{"PrepareOrder",0},{"BoundingSphereRadius",2f},{"AnimationConfigPath","Fixture/Config"}},null);
            var twin=new ComponentTacticalModel();var twinEntity=(Entity)RuntimeHelpers.GetUninitializedObject(typeof(Entity));twinEntity.m_project=project;
            twinEntity.m_components=[twin,body,creature,creature.ComponentHealth,creature.ComponentSpawn,locomotion];twin.m_entity=twinEntity;
            twin.Load(new ValuesDictionary{{"ModelName","Fixture/Model"},{"CastsShadow",true},{"PrepareOrder",0},{"BoundingSphereRadius",2f},{"AnimationConfigPath","Fixture/Config"}},null);
            int hookedFrames=0,frames=0;float parity=0;
            // One game frame: Animate syncs the parameters and advances the controller itself (never twice per frame).
            void Step(float dt,bool withTwin=false){Frame(dt);time.m_gameTime+=dt;
                ModsManager.ModHooks["OnAnimateModel"]=animateHook;component.Animate();frames++;if(component.Animated)hookedFrames++;
                if(!withTwin)return;
                ModsManager.ModHooks.Remove("OnAnimateModel");twin.Animate();ModsManager.ModHooks["OnAnimateModel"]=animateHook;
                component.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,component.AbsoluteBoneTransformsForCamera);twin.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,twin.AbsoluteBoneTransformsForCamera);
                for(int b=0;b<model.Bones.Count;b++)parity=Math.Max(parity,Vector3.Distance(component.AbsoluteBoneTransformsForCamera[b].Translation,twin.AbsoluteBoneTransformsForCamera[b].Translation));}
            void Render(string label,double t){
                component.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,component.AbsoluteBoneTransformsForCamera);
                var joints=new Matrix[48];SubsystemModelsRenderer.CalculateJointMatrices(component,model,Matrix.Identity,joints);
                foreach(var side in new[]{("side",new Vector3(3.6f,1.2f,0)),("front",new Vector3(0,1.2f,3.6f))}){
                    var eye=origin+side.Item2;var view=Matrix.CreateLookAt(eye,origin+new Vector3(0,.7f,0),Vector3.UnitY);var projection=Matrix.CreatePerspectiveFieldOfView(.8f,.75f,.1f,100);
                    Display.RenderTarget=target;Display.Viewport=new Viewport(0,0,480,640);Display.Clear(new Color(22,27,34),1,0);Display.ScissorRectangle=new Rectangle(0,0,480,640);
                    // Terrain blocks as flat-shaded boxes (top faces lighter), depth tested, so contact is visible.
                    var batch=ground.FlatBatch(0,DepthStencilState.Default,RasterizerState.CullNone,BlendState.Opaque);
                    foreach(var c in solid){var a=new Vector3(c.X,c.Y,c.Z);var b=a+Vector3.One;var top=new Color(96,104,112);var wall=new Color(64,70,78);
                        batch.QueueQuad(new(a.X,b.Y,a.Z),new(b.X,b.Y,a.Z),new(b.X,b.Y,b.Z),new(a.X,b.Y,b.Z),top);
                        batch.QueueQuad(new(a.X,a.Y,b.Z),new(b.X,a.Y,b.Z),new(b.X,b.Y,b.Z),new(a.X,b.Y,b.Z),wall);batch.QueueQuad(new(b.X,a.Y,a.Z),new(b.X,a.Y,b.Z),new(b.X,b.Y,b.Z),new(b.X,b.Y,a.Z),wall);
                        batch.QueueQuad(new(a.X,a.Y,a.Z),new(b.X,a.Y,a.Z),new(b.X,b.Y,a.Z),new(a.X,b.Y,a.Z),wall);batch.QueueQuad(new(a.X,a.Y,a.Z),new(a.X,a.Y,b.Z),new(a.X,b.Y,b.Z),new(a.X,b.Y,a.Z),wall);}
                    ground.Flush(view*projection);
                    Display.BlendState=BlendState.Opaque;Display.DepthStencilState=DepthStencilState.Default;Display.RasterizerState=RasterizerState.CullCounterClockwiseScissor;
                    shader.Transforms.World[0]=view;shader.Transforms.View=Matrix.Identity;shader.Transforms.Projection=projection;
                    shader.InstancesCount=1;shader.JointMatrices=joints;shader.MaterialColor=Vector4.One;shader.EmissionColor=Vector4.Zero;
                    shader.AmbientLightColor=new Vector3(.8f);shader.DiffuseLightColor1=shader.DiffuseLightColor2=new Vector3(.2f);shader.LightDirection1=Vector3.UnitY;shader.LightDirection2=Vector3.UnitZ;
                    shader.FogColor=Vector3.Zero;shader.FogBottomTopDensity=Vector3.Zero;shader.HazeStartDensity=new Vector2(100,0);shader.FogYMultiplier=1;shader.WorldUp=Vector3.UnitY;shader.SamplerState=SamplerState.LinearWrap;
                    foreach(var mesh in model.Meshes)foreach(var part in mesh.MeshParts){shader.Texture=model.GetTexture(model.GetMaterial(part.MaterialIndex).BaseColorTexture.TextureIndex);Display.DrawIndexed(PrimitiveType.TriangleList,shader,part.VertexBuffer,part.IndexBuffer,part.StartIndex,part.IndicesCount);}
                    using var file=File.Create(Path.Combine(output,$"{name}-{scenario}-{label}-{side.Item1}-t{t:0.00}.png"));RenderTarget2D.Save(target,file,ImageFileFormat.Png,false);
                }
            }
            // Hit-region evidence: the model as drawn with every hit box's edges on top (depth test off). Head red, body
            // yellow, arms cyan, legs green. Offline render, not a game recording.
            void RenderBoxes(string label,ScPartBox[] parts,(int Bone,BoundingBox Box,ScHitPart Part)[] boxes){
                component.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,component.AbsoluteBoneTransformsForCamera);
                var joints=new Matrix[Math.Max(48,model.Skin.JointCount)];SubsystemModelsRenderer.CalculateJointMatrices(component,model,Matrix.Identity,joints);
                foreach(var side in new[]{("side",new Vector3(3.2f,1.2f,0)),("front",new Vector3(0,1.2f,-3.2f)),("back",new Vector3(0,1.2f,3.2f))}){
                    var eye=origin+side.Item2;var view=Matrix.CreateLookAt(eye,origin+new Vector3(0,1f,0),Vector3.UnitY);var projection=Matrix.CreatePerspectiveFieldOfView(.8f,.75f,.1f,100);
                    Display.RenderTarget=target;Display.Viewport=new Viewport(0,0,480,640);Display.Clear(new Color(22,27,34),1,0);Display.ScissorRectangle=new Rectangle(0,0,480,640);
                    Display.BlendState=BlendState.Opaque;Display.DepthStencilState=DepthStencilState.Default;Display.RasterizerState=RasterizerState.CullCounterClockwiseScissor;
                    shader.Transforms.World[0]=view;shader.Transforms.View=Matrix.Identity;shader.Transforms.Projection=projection;
                    shader.InstancesCount=1;shader.JointMatrices=joints;shader.MaterialColor=Vector4.One;shader.EmissionColor=Vector4.Zero;
                    shader.AmbientLightColor=new Vector3(.8f);shader.DiffuseLightColor1=shader.DiffuseLightColor2=new Vector3(.2f);shader.LightDirection1=Vector3.UnitY;shader.LightDirection2=Vector3.UnitZ;
                    shader.FogColor=Vector3.Zero;shader.FogBottomTopDensity=Vector3.Zero;shader.HazeStartDensity=new Vector2(100,0);shader.FogYMultiplier=1;shader.WorldUp=Vector3.UnitY;shader.SamplerState=SamplerState.LinearWrap;
                    foreach(var mesh in model.Meshes)foreach(var part in mesh.MeshParts){shader.Texture=model.GetTexture(model.GetMaterial(part.MaterialIndex).BaseColorTexture.TextureIndex);Display.DrawIndexed(PrimitiveType.TriangleList,shader,part.VertexBuffer,part.IndexBuffer,part.StartIndex,part.IndicesCount);}
                    var lines=ground.FlatBatch(1,DepthStencilState.None,RasterizerState.CullNone,BlendState.Opaque);
                    for(int i=0;i<parts.Length;i++){
                        var b=parts[i].Local;var c=boxes[i].Part switch{ScHitPart.Head=>new Color(255,60,60),ScHitPart.Arm=>new Color(60,220,255),ScHitPart.Leg=>new Color(80,255,120),_=>new Color(255,220,60)};
                        Vector3 P(int k)=>Vector3.Transform(new Vector3((k&1)==0?b.Min.X:b.Max.X,(k&2)==0?b.Min.Y:b.Max.Y,(k&4)==0?b.Min.Z:b.Max.Z),parts[i].World);
                        foreach(var (x,y) in new[]{(0,1),(2,3),(4,5),(6,7),(0,2),(1,3),(4,6),(5,7),(0,4),(1,5),(2,6),(3,7)})lines.QueueLine(P(x),P(y),c);
                    }
                    ground.Flush(view*projection);
                    using var file=File.Create(Path.Combine(output,$"{name}-hit-regions-{label}-{side.Item1}.png"));RenderTarget2D.Save(target,file,ImageFileFormat.Png,false);
                }
            }
            var legBones=new[]{"leg_upper_L","leg_lower_L","ankle_L","leg_upper_R","leg_lower_R","ankle_R"}.Select(n=>model.FindBone(n,true).Index).ToArray();
            Quaternion[] Legs()=>legBones.Select(i=>{(component.m_boneTransforms[i]??model.Bones[i].Transform).Decompose(out _,out var q,out _);return q;}).ToArray();
            float Degrees(Quaternion a,Quaternion b)=>MathUtils.RadToDeg(2*MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(a,b)),0,1)));
            if(scenario=="pose-sheet"){
                // The source clips themselves, frame by frame: which pose a long fall would hold.
                for(int i=0;i<=5;i++)Step(1/30f);
                var gait=(Matrix?[])component.m_boneTransforms.Clone();var sheet=new List<object>();
                foreach(string clip in new[]{"jump","air","airrun","crouchair"}){
                    var animation=model.Animations.First(a=>a.Name==clip);var player=new AnimationPlayer();player.SetAnimation(model,animation);player.Play(false);
                    Quaternion[] first=null,before=null;
                    foreach(float phase in new[]{0f,.3f,.6f,.7f,.8f,.9f,1f}){
                        var pose=new Matrix?[model.Bones.Count];player.SampleAtTime(animation.Duration*phase,pose);
                        for(int b=0;b<pose.Length;b++)component.m_boneTransforms[b]=b==model.RootBone.Index?gait[b]:pose[b];
                        var legs=Legs();first??=legs;
                        sheet.Add(new{clip,phase,seconds=animation.Duration*phase,degreesFromFirst=legs.Zip(first,Degrees).Max(),degreesFromPrevious=before==null?0:legs.Zip(before,Degrees).Max()});before=legs;
                        Render(clip+"-p"+((int)(phase*100)).ToString("000"),animation.Duration*phase);
                    }
                }
                report.Add(new{name,scenario,sheet});
            }else if(scenario.StartsWith("fall")){
                // Long airborne periods (video-feedback-20260929 R1). The body stays in place for the camera; velocity and
                // ground contact drive the animation exactly as in the game.
                float seconds=scenario switch{"fall-2s"=>2,"fall-5s"=>5,"fall-speed-jitter"=>5,"fall-fly-toggle"=>6,_=>10};
                bool moving=scenario is "fall-10s-moving" or "fall-speed-jitter";
                for(int i=0;i<=5;i++){body.Velocity=new Vector3(moving?3:0,0,0);Step(1/30f,true);}
                const float dt=1/60f;double t=0;var v=new Vector3(moving?3:0,0,0);Quaternion[] previous=Legs();
                float worstHeld=0,worstEntry=0,worstLanding=0;int restarts=0;var states=new List<string>();var curve=new List<object>();
                var shots=new HashSet<int>();foreach(double at in new[]{.1,.2,.3,.4,.5,.6,.8,1,1.5,2,3,4,5,7.5,10})if(at<=seconds)shots.Add((int)Math.Round(at/dt));
                int total=(int)Math.Round(seconds/dt),landed=total+30;double airborneSince=-1;
                for(int i=1;i<=landed;i++){
                    bool air=i<=total;
                    // Creative-style flight for two seconds in the middle: the game treats it as not airborne.
                    bool supported=!air||scenario=="fall-fly-toggle"&&t>=2&&t<4;
                    if(air&&!supported){v.Y=Math.Max(-50,v.Y-10*dt);if(scenario=="fall-speed-jitter")v.X=2+.6f*MathF.Sin((float)t*40);}else v.Y=0;
                    body.Velocity=v;body.StandingOnValue=supported?2:null;
                    Step(dt,true);t+=dt;
                    bool airborne=component.Air.Airborne;if(airborne&&airborneSince<0)airborneSince=t;if(!airborne)airborneSince=-1;
                    var legs=Legs();float change=legs.Zip(previous,Degrees).Max();previous=legs;
                    double held=airborneSince<0?-1:t-airborneSince;
                    if(held>.6){worstHeld=Math.Max(worstHeld,change);if(change>5)restarts++;}
                    else if(airborne)worstEntry=Math.Max(worstEntry,change);
                    else worstLanding=Math.Max(worstLanding,change);
                    if(i%6==0||change>5)curve.Add(new{t=Math.Round(t,3),airborne,rising=component.Air.Rising,moving=component.Air.Moving,clip=component.AnimationController.m_layerAnimationRef.GetValueOrDefault("Base")?.Source,legDegreesPerFrame=MathF.Round(change,2)});
                    if(shots.Contains(i)||i==total+6||i==total+24)Render("f"+i.ToString("0000"),t);
                }
                if(worstHeld>.5f)failures.Add($"{name} {scenario}: legs move {worstHeld:0.0} degrees per frame while falling (after the entry transition)");
                if(restarts>0)failures.Add($"{name} {scenario}: {restarts} leg snaps above 5 degrees per frame while falling");
                if(component.Air.Airborne)failures.Add($"{name} {scenario}: still airborne after landing");
                if(hookedFrames!=frames)failures.Add($"{name} {scenario}: game sampler used for {hookedFrames}/{frames} frames");
                if(parity>.01f)failures.Add($"{name} {scenario}: game sampler and native controller differ by {parity:0.000} m");
                report.Add(new{name,scenario,seconds,framesPerSecond=60,maxLegDegreesPerFrame=new{entryTransition=worstEntry,whileFalling=worstHeld,landing=worstLanding},snapsWhileFalling=restarts,hookedFrames,frames,samplerParityMaxMeters=parity,curve});
            }else if(scenario=="hit-regions"){
                // headshot-armor-balance-20260929 H1: the skin-derived joint boxes of the logical pose (sampled without
                // advancing the controller) against the vertices as the renderer skins the drawn pose, per posture.
                var logical=(IScLogicalPose)component;var skin=model.Skin;var boxes=ScSkinnedHitRegions.Boxes(model).ToArray();
                if(!ScSkinnedHitRegions.Known(model)||boxes.Length<10)failures.Add($"{name} {scenario}: skeleton not recognised ({boxes.Length} joint boxes)");
                var boxOfBone=boxes.Select((b,i)=>(b.Bone,i)).GroupBy(p=>p.Bone).ToDictionary(g=>g.Key,g=>g.Select(p=>p.i).ToArray());
                string Semantic(VertexElementSemantic x,VertexElementFormat f)=>new VertexElement(0,f,x).Semantic;
                var vertices=new List<(Vector3 P,int[] J,float[] W,int Dominant)>();
                foreach(var buffer in model.ModelData.Buffers){
                    var el=buffer.VertexDeclaration.VertexElements;var pos=el.FirstOrDefault(e=>e.Semantic==Semantic(VertexElementSemantic.Position,VertexElementFormat.Vector3));
                    var ji=el.FirstOrDefault(e=>e.Semantic==Semantic(VertexElementSemantic.BlendIndices,VertexElementFormat.Vector4));var wi=el.FirstOrDefault(e=>e.Semantic==Semantic(VertexElementSemantic.BlendWeights,VertexElementFormat.Vector4));
                    if(pos is null||ji is null||wi is null)continue;int stride=buffer.VertexDeclaration.VertexStride;
                    for(int v=0;v<buffer.Vertices.Length/stride;v+=3){int o=v*stride;float F(int k)=>BitConverter.ToSingle(buffer.Vertices,o+k);
                        var J=new int[4];var W=new float[4];int dom=-1;float best=0;for(int k=0;k<4;k++){J[k]=(int)MathF.Round(F(ji.Offset+4*k));W[k]=F(wi.Offset+4*k);if(W[k]>best){best=W[k];dom=J[k];}}
                        if(best>=.6f)vertices.Add((new Vector3(F(pos.Offset),F(pos.Offset+4),F(pos.Offset+8)),J,W,dom));}
                }
                var postures=new List<object>();Vector3? standingHead=null;
                foreach(string posture in new[]{"stand","crouch","run","jump"}){
                    void Set(){body.StandingOnValue=posture=="jump"?null:2;body.m_crouchFactor=posture=="crouch"?1:0;body.Velocity=posture=="run"?new Vector3(0,0,-4):posture=="jump"?new Vector3(0,1.5f,0):Vector3.Zero;}
                    for(int i=0;i<(posture=="jump"?12:30);i++){Set();Step(1/30f);}
                    var drawn=(Matrix?[])component.m_boneTransforms.Clone();
                    var local=new Matrix?[model.Bones.Count];var again=new Matrix?[model.Bones.Count];bool ok=logical.TryLogicalPose(local)&logical.TryLogicalPose(again);
                    bool untouched=drawn.Zip(component.m_boneTransforms).All(p=>Nullable.Equals(p.First,p.Second)),repeatable=local.Zip(again).All(p=>Nullable.Equals(p.First,p.Second));
                    var abs=new Matrix[local.Length];var saved=component.m_boneTransforms;component.m_boneTransforms=local;component.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,abs);component.m_boneTransforms=saved;
                    var parts=ScSkinnedHitRegions.Parts(model,abs).ToArray();
                    component.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,component.AbsoluteBoneTransformsForCamera);
                    var joints=new Matrix[Math.Max(48,skin.JointCount)];SubsystemModelsRenderer.CalculateJointMatrices(component,model,Matrix.Identity,joints);
                    var inside=new Dictionary<string,(int In,int All)>();
                    foreach(var v in vertices){
                        if(v.Dominant<0||v.Dominant>=skin.Joints.Count||!boxOfBone.TryGetValue(skin.Joints[v.Dominant].Index,out var own))continue;
                        var world=Vector3.Zero;for(int k=0;k<4;k++)if(v.W[k]>0)world+=Vector3.Transform(v.P,joints[v.J[k]])*v.W[k];
                        var q=Vector3.Transform(world,Matrix.Invert(parts[own[0]].World));const float e=.01f;
                        bool hit=own.Any(bi=>{var b=parts[bi].Local;return q.X>=b.Min.X-e&&q.X<=b.Max.X+e&&q.Y>=b.Min.Y-e&&q.Y<=b.Max.Y+e&&q.Z>=b.Min.Z-e&&q.Z<=b.Max.Z+e;});
                        string key=boxes[own[0]].Part.ToString();var c=inside.GetValueOrDefault(key);inside[key]=(c.In+(hit?1:0),c.All+1);
                    }
                    var share=inside.ToDictionary(p=>p.Key,p=>p.Value.All==0?0:(float)p.Value.In/p.Value.All);
                    foreach(var (part,min) in new[]{("Head",.8f),("Body",.85f),("Arm",.85f),("Leg",.85f)})
                        if(!(share.GetValueOrDefault(part)>=min))failures.Add($"{name} {scenario} {posture}: only {share.GetValueOrDefault(part):P0} of the {part} vertices skinned by the renderer lie in their joint's hit box (need {min:P0})");
                    int headBox=Array.FindIndex(boxes,b=>b.Part==ScHitPart.Head&&model.Bones[b.Bone].Name.StartsWith("head_"));
                    Vector3 head=Vector3.Transform(boxes[headBox].Box.Center(),parts[headBox].World);if(posture=="stand")standingHead=head;
                    Vector3 At(string bone)=>abs[model.FindBone(bone,true).Index].Translation;
                    // Rays at a joint from 3 m away in four horizontal directions: the first box crossed, by joint name.
                    string First(Vector3 target,Vector3 from){var d=Vector3.Normalize(target-from);float best=float.MaxValue;int at=-1;
                        for(int i=0;i<parts.Length;i++)if(ScHeadshot.Intersect(parts[i],from,d) is float t&&t<best){best=t;at=i;}
                        return at<0?"miss":boxes[at].Part+":"+model.Bones[boxes[at].Bone].Name;}
                    var sides=new[]{("front",new Vector3(0,0,-3)),("back",new Vector3(0,0,3)),("left",new Vector3(-3,0,0)),("right",new Vector3(3,0,0))};
                    var thigh=(At("leg_upper_L")+At("leg_lower_L"))*.5f;
                    var rays=new Dictionary<string,string[]>{["head"]=sides.Select(x=>First(head,head+x.Item2)).ToArray(),["chest"]=sides.Select(x=>First(At("spine_2"),At("spine_2")+x.Item2)).ToArray(),
                        ["thigh"]=sides.Select(x=>First(thigh,thigh+x.Item2)).ToArray(),["hand"]=sides.Select(x=>First(At("hand_R"),At("hand_R")+x.Item2)).ToArray()};
                    int Reached(string key,string part)=>rays[key].Count(r=>r.StartsWith(part+":"));
                    if(!ok||!untouched||!repeatable)failures.Add($"{name} {scenario} {posture}: logical pose ok={ok} left the drawn pose untouched={untouched} repeatable={repeatable}");
                    // Every part is reachable from most sides; a limb in front of it may occlude it from one side (a real occluder).
                    if(Reached("head","Head")<3||Reached("chest","Body")<2||Reached("thigh","Leg")<3||Reached("hand","Arm")<3)
                        failures.Add($"{name} {scenario} {posture}: parts not reachable from most sides: "+string.Join("; ",rays.Select(r=>r.Key+" "+string.Join(",",r.Value))));
                    if(posture=="crouch"&&standingHead is {} s0&&!(s0.Y-head.Y>.2f))failures.Add($"{name} {scenario}: crouching lowers the head box only {s0.Y-head.Y:0.00} m");
                    // Joint vertices at head height that are not the head's (what occludes it), in the drawn pose.
                    var headBottom=Vector3.Transform(boxes[headBox].Box.Min,parts[headBox].World).Y;var atHead=new Dictionary<string,int>();
                    foreach(var v in vertices){if(v.Dominant<0||v.Dominant>=skin.Joints.Count)continue;var bone=skin.Joints[v.Dominant];if(bone.Name.StartsWith("head_"))continue;
                        var w=Vector3.Zero;for(int k=0;k<4;k++)if(v.W[k]>0)w+=Vector3.Transform(v.P,joints[v.J[k]])*v.W[k];if(w.Y>headBottom+.03f)atHead[bone.Name]=atHead.GetValueOrDefault(bone.Name)+1;}
                    if(posture is "stand" or "crouch")RenderBoxes(posture,parts,boxes);
                    postures.Add(new{posture,headCentreAboveFeet=head.Y-origin.Y,share,rays=rays.ToDictionary(r=>r.Key,r=>string.Join(",",r.Value)),verticesAboveHeadBottomByJoint=atHead,ok,untouched,repeatable});
                }
                var hb=boxes.First(b=>b.Part==ScHitPart.Head&&model.Bones[b.Bone].Name.StartsWith("head_")).Box.Size();
                report.Add(new{name,scenario,jointBoxes=boxes.Length,joints=boxOfBone.Count,parts=boxes.GroupBy(b=>b.Part.ToString()).ToDictionary(g=>g.Key,g=>g.Count()),headBoxJointSpace=new[]{hb.X,hb.Y,hb.Z},sampledVertices=vertices.Count,postures});
                if(!(hb.Y is > .12f and < .4f))failures.Add($"{name} {scenario}: head box height {hb.Y:0.00} m outside 0.12-0.4");
            }else if(scenario.StartsWith("crouch")){
                // r2-c4-completion-20260929: the ground crouch the actors lacked. The legs carry it (the pelvis drops, the
                // feet stay on the ground), walking crouched moves the feet, and going down and back up blends without snaps.
                for(int i=0;i<=10;i++){body.Velocity=Vector3.Zero;body.m_crouchFactor=0;Step(1/30f,true);}
                component.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,component.AbsoluteBoneTransformsForCamera);
                int pelvisBone=model.FindBone("pelvis",true).Index,ankleL=model.FindBone("ankle_L",true).Index,ankleR=model.FindBone("ankle_R",true).Index;
                float standPelvis=component.AbsoluteBoneTransformsForCamera[pelvisBone].Translation.Y-origin.Y;
                const float dt=1/60f;double t=0;Quaternion[] previous=Legs();float worst=0,lowestAnkle=float.MaxValue,highestAnkle=float.MinValue,crouchedPelvis=float.MaxValue;var states=new List<string>();var ankles=new List<float>();
                int count=scenario=="crouch-toggle"?150:90;
                for(int i=1;i<=count;i++){
                    bool down=scenario!="crouch-toggle"||t<1.2;float speed=scenario=="crouch-walk"?1.4f:0;
                    body.m_crouchFactor=Math.Clamp(body.m_crouchFactor+(down?1:-1)*dt/.25f,0,1);body.Velocity=new Vector3(speed,0,0);
                    Step(dt,true);t+=dt;
                    component.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,component.AbsoluteBoneTransformsForCamera);
                    var abs=component.AbsoluteBoneTransformsForCamera;float pelvis=abs[pelvisBone].Translation.Y-origin.Y;
                    float ankle=Math.Min(abs[ankleL].Translation.Y,abs[ankleR].Translation.Y)-origin.Y;lowestAnkle=Math.Min(lowestAnkle,ankle);highestAnkle=Math.Max(highestAnkle,ankle);ankles.Add(abs[ankleR].Translation.Z-origin.Z); // along the body's forward axis (-Z at identity rotation)
                    if(body.m_crouchFactor>=1&&t>.6)crouchedPelvis=Math.Min(crouchedPelvis,pelvis);
                    var legs=Legs();float change=legs.Zip(previous,Degrees).Max();previous=legs;worst=Math.Max(worst,change);
                    if(i%10==0)Render("f"+i.ToString("000"),t);
                    states.Add($"{t:0.000}:crouch={body.m_crouchFactor:0.00}:pelvis={pelvis:0.000}:ankle={ankle:0.000}:{component.AnimationController.m_layerAnimationRef.GetValueOrDefault("Base")?.Source}:{change:0.0}");
                }
                if(scenario!="crouch-toggle"&&!(crouchedPelvis<standPelvis-.2f))failures.Add($"{name} {scenario}: crouching lowers the pelvis only from {standPelvis:0.00} to {crouchedPelvis:0.00} m");
                if(scenario=="crouch-toggle"&&!states[^1].Contains(":idle:")&&!states[^1].Contains(":aim:"))failures.Add($"{name} {scenario}: not back on the standing gait: {states[^1]}");
                if(lowestAnkle<-.05f||highestAnkle>.3f)failures.Add($"{name} {scenario}: the feet leave the ground ({lowestAnkle:0.00}..{highestAnkle:0.00} m)");
                if(worst>12)failures.Add($"{name} {scenario}: a leg snaps {worst:0.0} degrees in one frame");
                if(scenario=="crouch-walk"&&ankles.Max()-ankles.Min()<.15f)failures.Add($"{name} {scenario}: the feet do not step while walking crouched");
                if(hookedFrames!=frames)failures.Add($"{name} {scenario}: game sampler used for {hookedFrames}/{frames} frames");
                if(parity>.01f)failures.Add($"{name} {scenario}: game sampler and native controller differ by {parity:0.000} m");
                report.Add(new{name,scenario,standPelvis,crouchedPelvis,ankleRange=new[]{lowestAnkle,highestAnkle},maxLegDegreesPerFrame=worst,samplerParityMaxMeters=parity,states=states.Where((_,i)=>i%5==0).ToArray()});
            }else if(scenario.StartsWith("jump")){
                float run=scenario=="jump-run"?4:0;var states=new List<string>();
                for(int i=0;i<=5;i++)Step(1/30f,true); // settle on the ground gait
                double t=0;const float dt=1/60f;var v=Vector3.Zero;var p=origin;
                bool air=false;
                for(int i=0;i<=84;i++){ // take off at frame 1 (v0=5, g=10: ~1.0 s flight), then ~0.4 s landed
                    if(i==1){air=true;v=new Vector3(run,5,0);}
                    if(air){v.Y-=10*dt;p+=v*dt;if(p.Y<=origin.Y){p.Y=origin.Y;v=new Vector3(run,0,0);air=false;}}
                    body.Position=new Vector3(origin.X,p.Y,origin.Z);body.Velocity=air?v:new Vector3(run,0,0);body.StandingOnValue=air?null:2;
                    Step(dt,true);t+=dt;
                    if(i%6==0)Render("f"+i.ToString("000"),t);
                    states.Add($"{t:0.000}:{(air?"air":"ground")}:{component.Air.Airborne}:{(component.Air.Rising?"rising":"falling")}:{component.AnimationController.m_layerAnimationRef.GetValueOrDefault("Base")?.Source}");
                }
                bool sawAir=states.Any(s=>s.EndsWith(":True",StringComparison.Ordinal)||s.Contains(":True:"));
                if(!sawAir)failures.Add($"{name} {scenario}: Airborne never raised");
                if(states[^1].Contains(":True:"))failures.Add($"{name} {scenario}: still airborne after landing");
                if(hookedFrames!=frames)failures.Add($"{name} {scenario}: game sampler used for {hookedFrames}/{frames} frames");
                if(parity>.01f)failures.Add($"{name} {scenario}: game sampler and native controller differ by {parity:0.000} m");
                report.Add(new{name,scenario,hookedFrames,frames,samplerParityMaxMeters=parity,states});
            }else{
                for(int i=0;i<=5;i++)Step(1/30f);
                component.DeathCauseOffset=new Vector3(0,0,1); // hit from the front: falls backward (toward -Z: the step/wall side)
                creature.ComponentHealth.Health=0;var samples=new List<object>();double t=0;float lowest=float.MaxValue;string lowestAt="";bool finite=true;
                foreach(double at in new[]{0,.2,.4,.7,1,1.5,2.5,4,7}){
                    while(t<at-1e-9){component.DeathPhase=Math.Min(1,(float)(t/1.2));Step(1/60f);t+=1/60f;}
                    component.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,component.AbsoluteBoneTransformsForCamera);
                    var positions=model.Bones.Select(b=>component.AbsoluteBoneTransformsForCamera[b.Index].Translation).ToArray();
                    finite&=positions.All(q=>float.IsFinite(q.X+q.Y+q.Z));
                    // Floor under each joint: the step blocks occupy z in [-218,-213), so their top (71) applies below z=-213.
                    var depths=new Dictionary<string,float>();
                    foreach(var joint in new[]{"pelvis","spine_2","head_0","hand_L","hand_R","arm_lower_L","arm_lower_R","leg_lower_L","leg_lower_R","ankle_L","ankle_R","ball_L","ball_R"}){var b=model.FindBone(joint,false);if(b!=null){var q=positions[b.Index];
                        float floor=scenario=="death-step"&&q.Z<-213f?71:70;float above=q.Y-floor;depths[joint]=MathF.Round(above,3);if(above<lowest){lowest=above;lowestAt=$"{joint}@t{at:0.00}";}}}
                    samples.Add(new{t=at,pelvis=positions[model.FindBone("pelvis",false).Index].ToString(),head=positions[model.FindBone("head_0",false).Index].ToString(),aboveFloor=depths});
                    Render("s",at);
                }
                if(!finite)failures.Add($"{name} {scenario}: non-finite bone transform");
                if(lowest<-.12f)failures.Add($"{name} {scenario}: {lowestAt} sank {-lowest:0.00} m into the ground");
                report.Add(new{name,scenario,lowestJointAboveFloor=lowest,lowestAt,samples,activeRagdolls=TacticalRagdoll.ActiveCount(project)});
            }
            component.OnEntityRemoved();twin.OnEntityRemoved();
        }
    }
}catch(Exception e){Console.Error.WriteLine(e);failures.Add(e.GetType().Name+": "+e.Message);}finally{Display.RenderTarget=null;Window.Close();}};
Window.Run(480,640,WindowMode.Fixed,"Actor motion diagnostic");
File.WriteAllText(Path.Combine(output,"motion.json"),JsonSerializer.Serialize(new{failures,report,scope="Native offline render of the real model component; not a game recording"},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"actor motion failures={failures.Count}");foreach(var f in failures)Console.WriteLine("FAIL "+f);
return failures.Count==0?0:1;
