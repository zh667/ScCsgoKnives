using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Engine;
using Engine.Animation;
using Engine.Graphics;
using Engine.Media;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

if(args.Length!=3)throw new ArgumentException("ActorSamplingCheck <repo> <Content.zip> <output>");
string root=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[2]);Directory.CreateDirectory(output);
var loadedModels=new List<Model>();var sink=new ErrorSink();Log.AddLogSink(sink);
var checks=new List<string>();var rows=new List<object>();int exit=0;
void Check(string name,bool pass){if(!pass)throw new Exception(name);checks.Add(name);}
void Equal(string name,Matrix?[] a,Matrix?[] b){for(int i=0;i<a.Length;i++)if(a[i]!=b[i])throw new Exception(name+" bone "+i);}
using var content=ZipFile.OpenRead(args[1]);
string Read(string suffix){using var r=new StreamReader(content.Entries.Single(e=>e.FullName.EndsWith(suffix)).Open());return r.ReadToEnd();}
AnimationTemplateManager.LoadFromJsonNode(JsonNode.Parse(Read("Simple.template.json")));
var caches=(IDictionary<string,List<object>>)typeof(ContentManager).GetField("Caches",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
foreach(string role in new[]{"ct","t"}){string name="Animations/ScCsgoTactical/"+role+".scanim";var info=new ContentInfo(name);info.SetContentStream(new MemoryStream(File.ReadAllBytes(Path.Combine(root,"src/ScCsgoTactical/Assets",name))));ContentManager.Add(info);}
var frame=typeof(ComponentTacticalModel).GetField("animatedFrame",BindingFlags.NonPublic|BindingFlags.Instance);
var hook=new ModsManager.ModHook("OnAnimateModel");hook.Add(new TacticalModLoader());
void Hooks(bool on){if(on)ModsManager.ModHooks["OnAnimateModel"]=hook;else ModsManager.ModHooks.Remove("OnAnimateModel");}
bool done=false;
Window.Frame+=()=>{if(done)return;done=true;try{
    typeof(Time).GetProperty("FrameDuration").SetValue(null,1f/60);
    LightingManager.Initialize();
    foreach(var pair in new[]{(typeof(AirBlock),0),(typeof(ScKnifeBlock),700),(typeof(ScGunBlock),701),(typeof(ScGrenadeBlock),702),(typeof(ScTacticalShieldBlock),705)}){var block=(Block)Activator.CreateInstance(pair.Item1);block.BlockIndex=pair.Item2;BlocksManager.Blocks[pair.Item2]=block;BlocksManager.BlockTypeToIndex[pair.Item1]=pair.Item2;}
    foreach(string role in new[]{"ct","t"}){
        using var input=File.OpenRead(Path.Combine(root,"src/ScCsgoTactical/Assets/Models/ScCsgoTactical/"+role+".glb"));var model=Model.Load(GltfLoader.Load(input),true);loadedModels.Add(model);ScActorAnimations.Ensure(model);
        caches["Fixture/Model"]=[model];caches["Models/ScCsgoTactical/"+role]=[model];caches["Fixture/Config.json"]=[File.ReadAllText(Path.Combine(root,"src/ScCsgoTactical/Assets/Animations/ScTactical.json"))];
        var a=new Matrix?[model.Bones.Count];var b=new Matrix?[model.Bones.Count];var player=new AnimationPlayer();long nativeBytes=0,optimizedBytes=0;double nativeMs=0,optimizedMs=0;
        foreach(var clip in model.Animations){
            player.SetAnimation(model,clip);
            foreach(bool reverse in new[]{false,true}){
                player.StartPhase=reverse?1:0;player.EndPhase=reverse?0:1;player.Play(true);
                for(int i=0;i<80;i++){
                    player.Update(clip.Duration/31f);Array.Clear(a);Array.Clear(b);
                    player.SampleBoneTransforms(a);ScActorSampler.Sample(player,b);Equal(role+" "+clip.Name+" "+i,a,b);
                }
            }
            Check(role+" exact samples "+clip.Name,true);
        }
        player.SetAnimation(model,model.Animations.First(c=>c.Name=="aimwalk"));player.Play(true);
        for(int run=0;run<2;run++){
            var sw=Stopwatch.StartNew();long bytes=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<1000;i++){player.Update(1f/60);if(run==0)player.SampleBoneTransforms(a);else ScActorSampler.Sample(player,b);}
            if(run==0){nativeBytes=GC.GetAllocatedBytesForCurrentThread()-bytes;nativeMs=sw.Elapsed.TotalMilliseconds;}else{optimizedBytes=GC.GetAllocatedBytesForCurrentThread()-bytes;optimizedMs=sw.Elapsed.TotalMilliseconds;}
        }
        Check(role+" steady sampler allocation",optimizedBytes<1024);rows.Add(new{stage="sampler",role,nativeBytes,optimizedBytes,nativeMs,optimizedMs,calls=1000});
        var project=new Project();foreach(var sub in new Subsystem[]{new SubsystemSky(),new SubsystemTime(),new SubsystemGameInfo()}){sub.m_project=project;project.m_subsystems.Add(sub);}
        ComponentTacticalModel Component(bool armed=false){
            var body=new ComponentBody{Position=new(105,70,-213),BoxSize=new(.65f,1.8f,.65f)};
            var creature=new ComponentCreature{ComponentBody=body,ComponentHealth=new(){Health=1},ComponentSpawn=new(){SpawnDuration=0},ComponentLocomotion=new()};
            var component=new ComponentTacticalModel();creature.ComponentCreatureModel=component;
            var entity=(Entity)RuntimeHelpers.GetUninitializedObject(typeof(Entity));entity.m_project=project;entity.m_components=[component,body,creature,creature.ComponentHealth,creature.ComponentSpawn,creature.ComponentLocomotion];if(armed)entity.m_components.Add(new ComponentTacticalEnemy{Creature=creature,State=TacticalEnemyState.Create(TacticalRole.Rifle,"fixture",new Engine.Random(17))});foreach(var c in entity.m_components)c.m_entity=entity;
            component.Load(new ValuesDictionary{{"ModelName","Fixture/Model"},{"CastsShadow",true},{"PrepareOrder",0},{"BoundingSphereRadius",2f},{"AnimationConfigPath","Fixture/Config"}},null);return component;
        }
        foreach(bool armed in new[]{false,true}){
        var native=Component(armed);var fast=Component(armed);
        for(int i=0;i<360;i++){
            float speed=i<60?0:i<90?1:i<160?(i%8<4?0:4):i<220?.2f:0;
            foreach(var c in new[]{native,fast}){c.m_componentCreature.ComponentBody.Velocity=new Vector3(speed,0,0);c.m_componentCreature.ComponentHealth.Health=i>=280?0:1;if(armed)c.Entity.FindComponent<ComponentTacticalEnemy>().State.ReloadLeft=i>=170&&i<260?2.8f-(i-170)/60f:0;c.DeathPhase=i>=280?Math.Min(1,(i-280)/60f):0;frame.SetValue(c,-1);}
            Hooks(false);native.Animate();Hooks(true);fast.Animate();Equal(role+" integrated frame "+i,native.m_boneTransforms,fast.m_boneTransforms);
            var reference=new ComponentModel{m_model=model,m_boneTransforms=native.m_boneTransforms,ModelScale=1};var expected=new Matrix[model.Bones.Count];reference.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,expected);
            fast.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,fast.AbsoluteBoneTransformsForCamera);
            Check(role+" hierarchy "+i,expected.SequenceEqual(fast.AbsoluteBoneTransformsForCamera));
        }
        Check(role+" native controller state and death poses "+armed,true);
        }
        // Native state/transition/event code is still exercised; repeat cameras must not tick it twice.
        var camera=Component();Hooks(true);camera.Animate();float t=camera.AnimationController.Layers[0].Player.Time;camera.Animate();Check(role+" camera clock guard",camera.AnimationController.Layers[0].Player.Time==t);
        camera.DisableAnimation=true;frame.SetValue(camera,-1);camera.Animate();Check(role+" disabled animation clock",camera.AnimationController.Layers[0].Player.Time==t);
        foreach(int count in new[]{1,5,41})foreach(bool optimized in new[]{false,true}){
            var group=Enumerable.Range(0,count).Select(_=>Component(true)).ToArray();Hooks(optimized);
            foreach(var c in group){c.Animate();c.AnimationController.Update(.3f);}
            var sw=Stopwatch.StartNew();long bytes=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<180;i++)foreach(var c in group){frame.SetValue(c,-1);c.Animate();c.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,c.AbsoluteBoneTransformsForCamera);}
            rows.Add(new{stage="crowd",role,count,optimized,msPerFrame=sw.Elapsed.TotalMilliseconds/180,bytesPerFrame=(GC.GetAllocatedBytesForCurrentThread()-bytes)/180});
        }
        ScActorSampler.Clear();Array.Clear(a);Array.Clear(b);player.SampleBoneTransforms(a);ScActorSampler.Sample(player,b);Equal(role+" reentry",a,b);
    }
    var actions=new List<Action>();ScTacticalWarmup.Add(actions);foreach(var action in actions)action();Check("main thread warmup actions",actions.Count==7&&ScNpcWeaponRenderer.Prepare());
    Check("no swallowed hook errors",sink.Errors.Count==0);
    ScNpcWeaponRenderer.Clear();Check("shader reentry",ScNpcWeaponRenderer.Prepare());ScNpcWeaponRenderer.Clear();
}catch(Exception e){Console.Error.WriteLine(e);rows.Add(new{error=e.ToString()});exit=1;}finally{Hooks(false);foreach(var model in loadedModels)model.Dispose();Window.Close();}};
Window.Run(320,240,WindowMode.Fixed,"Actor sampling verification");
File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{failed=exit,checks,measurements=rows},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(new{failed=exit,checks=checks.Count,measurements=rows}));return exit;

sealed class ErrorSink:ILogSink {public readonly List<string> Errors=[];public void Log(LogType type,string message){if(type==LogType.Error)Errors.Add(message);}}
