// Controlled native CPU/GPU resource probe. No player world, no full-game FPS claim.
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

if(args.Length!=3)throw new ArgumentException("TacticalPerformanceCheck <repo> <Content.zip> <output>");
string root=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[2]);Directory.CreateDirectory(output);
var checks=new List<string>();var rows=new List<object>();
void Check(string name,bool value){if(!value)throw new Exception(name);checks.Add(name);}
var sink=new Sink();Log.AddLogSink(sink);
var diagnosticProject=new Project();ScTacticalPerformance.Start(diagnosticProject);
for(int i=0;i<100;i++){using var trace=ScTacticalPerformance.Spawn(diagnosticProject,"fixture",3);using var timing=ScTacticalPerformance.Measure(diagnosticProject,ScTacticalPerformance.Stage.EntityCreate);trace.Success=i!=99;}
Check("burst detail is rate limited",sink.Lines.Count(s=>s.Contains("spawn begin"))==1&&sink.Lines.Count(s=>s.Contains("spawn end"))==1);
try{using var timing=ScTacticalPerformance.Measure(diagnosticProject,ScTacticalPerformance.Stage.ModelLoad);throw new InvalidOperationException("fixture");}catch(InvalidOperationException){}
long alloc=GC.GetAllocatedBytesForCurrentThread();var sw=Stopwatch.StartNew();
for(int i=0;i<100000;i++){using var timing=ScTacticalPerformance.Measure(diagnosticProject,ScTacticalPerformance.Stage.EnemyAI);}
double overheadNs=sw.Elapsed.TotalMilliseconds*1e6/100000;long loggerBytes=GC.GetAllocatedBytesForCurrentThread()-alloc;
Check("hot scopes allocate less than one byte per call",loggerBytes<100000);
Check("no log per timed call",sink.Lines.Count==3);
var session=typeof(ScTacticalPerformance).GetMethod("For",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,[diagnosticProject]);
var frameMethod=session.GetType().GetMethod("Frame",BindingFlags.NonPublic|BindingFlags.Instance);
frameMethod.Invoke(session,[1,100d,4d,3,2]);frameMethod.Invoke(session,[1,999d,99d,3,2]);
Check("no early summary or duplicate frame log",sink.Lines.Count==3);
session.GetType().GetField("reportAt",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(session,Stopwatch.GetTimestamp()-11*Stopwatch.Frequency);
frameMethod.Invoke(session,[2,20d,3d,3,2]);
Check("periodic frame sample counted once and includes population",sink.Lines.Last().Contains("frames=2")&&sink.Lines.Last().Contains("engineAvgMs=60.00")&&sink.Lines.Last().Contains("over50/100/250=1/1/0")&&sink.Lines.Last().Contains("peakActors=5"));
ScTacticalPerformance.Finish(diagnosticProject);
Check("summary retains exception and all burst counts",sink.Lines.Last().Contains("spawnRequests=100")&&sink.Lines.Last().Contains("spawnFailed=1")&&sink.Lines.Last().Contains("detailSuppressed=99")&&sink.Lines.Last().Contains("ModelLoad:n=1")&&sink.Lines.Last().Contains("EnemyAI:n=100000"));
int emitted=sink.Lines.Count;ScTacticalPerformance.Finish(diagnosticProject);Check("dispose is idempotent",sink.Lines.Count==emitted);
ScTacticalPerformance.Start(diagnosticProject);ScTacticalPerformance.Finish(diagnosticProject);Check("reentry drops old counters",sink.Lines.Count==emitted+1);
using(ScTacticalPerformance.Measure(null,ScTacticalPerformance.Stage.Animate)){}
new ComponentTacticalEnemy().Update(0);new ComponentTacticalModel().SetModel(null);
Check("unattached empty components retain native no-op behavior",true);
rows.Add(new{stage="logger",overheadNs,allocatedBytes=loggerBytes,calls=100000});
using var content=ZipFile.OpenRead(args[1]);
string Read(string suffix){using var reader=new StreamReader(content.Entries.Single(e=>e.FullName.EndsWith(suffix)).Open());return reader.ReadToEnd();}
AnimationTemplateManager.LoadFromJsonNode(JsonNode.Parse(Read("Simple.template.json")));
var caches=(IDictionary<string,List<object>>)typeof(ContentManager).GetField("Caches",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
foreach(string role in new[]{"ct","t"}){string name="Animations/ScCsgoTactical/"+role+".scanim";var info=new ContentInfo(name);info.SetContentStream(new MemoryStream(File.ReadAllBytes(Path.Combine(root,"src/ScCsgoTactical/Assets",name))));ContentManager.Add(info);}
ContentManager.AddContentReader(new Game.IContentReader.ObjModelReader());
// Lazy streams: opening package resources never eagerly decodes all textures.
using var package=ZipFile.OpenRead(Path.Combine(root,"output/[API1.9]CS武器1.3.0-全量包.scmod"));
foreach(var e in package.Entries.Where(e=>e.FullName.StartsWith("Assets/Models/ScCsgoKnives/")&&e.FullName.EndsWith(".obj"))){var info=new ContentInfo(e.FullName[7..]);using var s=e.Open();var copy=new MemoryStream();s.CopyTo(copy);copy.Position=0;info.SetContentStream(copy);ContentManager.Add(info);}
bool done=false;int exit=0;
Window.Frame+=()=>{if(done)return;done=true;try{
    var hook=new ModsManager.ModHook("OnAnimateModel");hook.Add(new TacticalModLoader());ModsManager.ModHooks["OnAnimateModel"]=hook;
    LightingManager.Initialize();BlocksManager.BlockTypeToIndex[typeof(ScGunBlock)]=701;BlocksManager.Blocks[701]=new ScGunBlock{BlockIndex=701};
    BlocksManager.BlockTypeToIndex[typeof(ScTacticalShieldBlock)]=705;BlocksManager.Blocks[705]=new ScTacticalShieldBlock{BlockIndex=705};
    foreach(var pair in new[]{(typeof(AirBlock),0),(typeof(ScKnifeBlock),700),(typeof(ScGrenadeBlock),702)}){var block=(Block)Activator.CreateInstance(pair.Item1);block.BlockIndex=pair.Item2;BlocksManager.Blocks[pair.Item2]=block;BlocksManager.BlockTypeToIndex[pair.Item1]=pair.Item2;}
    foreach(string role in new[]{"ct","t"}){
        long start=GC.GetAllocatedBytesForCurrentThread();sw.Restart();
        using var input=File.OpenRead(Path.Combine(root,"src/ScCsgoTactical/Assets/Models/ScCsgoTactical/"+role+".glb"));
        using var model=Model.Load(GltfLoader.Load(input),true);ScActorAnimations.Ensure(model);
        rows.Add(new{role,stage="coldGeometryGpuAndAnimation",ms=sw.Elapsed.TotalMilliseconds,bytes=GC.GetAllocatedBytesForCurrentThread()-start,bones=model.Bones.Count});
        sw.Restart();foreach(var mesh in model.Meshes)foreach(var part in mesh.MeshParts)model.GetTexture(model.GetMaterial(part.MaterialIndex).BaseColorTexture.TextureIndex);
        rows.Add(new{role,stage="coldBodyTextures",ms=sw.Elapsed.TotalMilliseconds});
        caches["Fixture/Model"]=[model];caches["Fixture/Config.json"]=[File.ReadAllText(Path.Combine(root,"src/ScCsgoTactical/Assets/Animations/ScTactical.json"))];
        var project=new Project();foreach(var sub in new Subsystem[]{new SubsystemSky(),new SubsystemTime(),new SubsystemGameInfo()}){sub.m_project=project;project.m_subsystems.Add(sub);}
        foreach(int count in new[]{1,3,5,20}){
            var components=new List<ComponentTacticalModel>();start=GC.GetAllocatedBytesForCurrentThread();sw.Restart();
            for(int i=0;i<count;i++){
                var body=new ComponentBody{Position=new Vector3(i*3,70,0),BoxSize=new Vector3(.65f,1.8f,.65f)};
                var creature=new ComponentCreature{ComponentBody=body,ComponentHealth=new ComponentHealth{Health=1},ComponentSpawn=new ComponentSpawn{SpawnDuration=0},ComponentLocomotion=new ComponentLocomotion()};
                var component=new ComponentTacticalModel();creature.ComponentCreatureModel=component;
                var enemy=new ComponentTacticalEnemy{Creature=creature,State=TacticalEnemyState.Create((TacticalRole)(i%5),"fixture",new Engine.Random(i))};
                var entity=(Entity)RuntimeHelpers.GetUninitializedObject(typeof(Entity));entity.m_project=project;entity.m_components=[component,body,creature,creature.ComponentHealth,creature.ComponentSpawn,creature.ComponentLocomotion,enemy];foreach(var c in entity.m_components)c.m_entity=entity;
                component.Load(new ValuesDictionary{{"ModelName","Fixture/Model"},{"CastsShadow",true},{"PrepareOrder",0},{"BoundingSphereRadius",2f},{"AnimationConfigPath","Fixture/Config"}},null);components.Add(component);
            }
            rows.Add(new{role,count,stage="warmComponentLoad",ms=sw.Elapsed.TotalMilliseconds,bytes=GC.GetAllocatedBytesForCurrentThread()-start});
            var frame=typeof(ComponentTacticalModel).GetField("animatedFrame",BindingFlags.NonPublic|BindingFlags.Instance);
            foreach(var c in components)c.Animate();
            start=GC.GetAllocatedBytesForCurrentThread();sw.Restart();
            for(int f=0;f<120;f++)foreach(var c in components){frame.SetValue(c,-1);c.AnimationController.Update(1f/60);c.Animate();c.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,c.AbsoluteBoneTransformsForCamera);}
            rows.Add(new{role,count,stage="warmAnimationAndHierarchy",frames=120,msPerFrame=sw.Elapsed.TotalMilliseconds/120,bytesPerFrame=(GC.GetAllocatedBytesForCurrentThread()-start)/120});
            Check(role+"/"+count+" finite pose",components.All(c=>float.IsFinite(c.AbsoluteBoneTransformsForCamera[^1].M11)));
        }
        ScTacticalPerformance.Finish(project);
    }
    foreach(string asset in new[]{"awp","ak47","mp9","m249","deagle"}){
        sw.Restart();var weapon=ScThirdPersonWeapon.For(asset);double coldMs=sw.Elapsed.TotalMilliseconds;
        Check(asset+" builds full third person mesh",weapon!=null&&weapon.Vertices>0);
        sw.Restart();for(int i=0;i<1000;i++)CheckCache(weapon,ScThirdPersonWeapon.For(asset));
        rows.Add(new{asset,stage="weaponMesh",coldMs,warmMsPerCall=sw.Elapsed.TotalMilliseconds/1000,vertices=weapon.Vertices});
    }
}catch(Exception e){Console.Error.WriteLine(e);rows.Add(new{error=e.ToString()});exit=1;}finally{Window.Close();}};
Window.Run(320,240,WindowMode.Fixed,"CS crowd diagnostic (isolated)");
Log.RemoveLogSink(sink);File.WriteAllLines(Path.Combine(output,"diagnostic.log"),sink.Lines);
File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{failed=exit,checks,measurements=rows},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(new{failed=exit,checks=checks.Count,measurements=rows}));return exit;
void CheckCache(ScThirdPersonWeapon a,ScThirdPersonWeapon b){if(!ReferenceEquals(a,b))throw new Exception("warm mesh rebuilt");}
sealed class Sink:ILogSink {public readonly List<string> Lines=[];public void Log(LogType type,string message){if(message.StartsWith("[CS_PERF]"))Lines.Add(message);}}
