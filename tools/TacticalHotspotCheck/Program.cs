// agent-followup-140 F5: frozen-input CPU/allocation baseline for the audited risk points and the new F3/F4/F2 costs.
// Offline native harness on the real components and static code paths: fixed terrain, fixed seeds and counts, the same
// package build. Rows marked replica re-run a private expression verbatim because its owner needs a full world. Not a
// game frame-time measurement: GPU, vsync, physics and other mods are excluded, and a desktop CPU is not a phone.
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

if(args.Length!=3)throw new ArgumentException("TacticalHotspotCheck <Assets folder> <Content.zip> <output folder>");
string assets=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[2]);Directory.CreateDirectory(output);
using var content=ZipFile.OpenRead(args[1]);
string Read(string suffix){using var r=new StreamReader(content.Entries.Single(e=>e.FullName.EndsWith(suffix)).Open());return r.ReadToEnd();}
AnimationTemplateManager.LoadFromJsonNode(JsonNode.Parse(Read("Simple.template.json")));
var caches=(IDictionary<string,List<object>>)typeof(ContentManager).GetField("Caches",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
{string name="Animations/ScCsgoTactical/ct.scanim";var info=new ContentInfo(name);info.SetContentStream(new MemoryStream(File.ReadAllBytes(Path.Combine(assets,name))));ContentManager.Add(info);}
var frameDuration=typeof(Time).GetProperty("FrameDuration");var frameIndex=typeof(Time).GetProperty("FrameIndex");
void Frame(float dt){frameDuration.SetValue(null,dt);frameIndex.SetValue(null,Time.FrameIndex+1);}
BlocksManager.Blocks[0]=new AirBlock{BlockIndex=0,IsCollidable=false};BlocksManager.Blocks[2]=new DirtBlock{BlockIndex=2,IsCollidable=true};
var rows=new List<object>();var failures=new List<string>();const int Trials=7;

// One micro row: warm-up, then Trials timed batches; median/min/max per call, allocation per call, and the CPU share at
// the stated call rate (ms of CPU per second of play; 1000 ms = one full core).
void Micro(string name,string code,bool replica,double rate,string basis,int ops,Action op){
    for(int i=0;i<Math.Max(10,ops/5);i++)op();
    var per=new double[Trials];long bytes=0;
    for(int t=0;t<Trials;t++){long a=GC.GetAllocatedBytesForCurrentThread();var sw=Stopwatch.StartNew();for(int i=0;i<ops;i++)op();per[t]=sw.Elapsed.TotalMilliseconds*1000/ops;bytes+=GC.GetAllocatedBytesForCurrentThread()-a;}
    Array.Sort(per);double median=per[Trials/2];
    rows.Add(new{kind="micro",name,code,replica,medianUs=Math.Round(median,3),minUs=Math.Round(per[0],3),maxUs=Math.Round(per[^1],3),
        spreadPct=Math.Round((per[^1]-per[0])/Math.Max(1e-9,median)*100,1),bytesPerCall=bytes/(double)(Trials*ops),rate,basis,
        msPerSecondAtRate=Math.Round(median*rate/1000,4),cpuPctAtRate=Math.Round(median*rate/1000/10,4)});
}

(Project Project,SubsystemTime Time,SubsystemTerrain Terrain) NewWorld(bool wall){
    var project=new Project();var time=new SubsystemTime();var terrain=new SubsystemTerrain{Terrain=new Terrain()};
    foreach(var sub in new Subsystem[]{new SubsystemSky(),time,new SubsystemGameInfo(),terrain}){sub.m_project=project;project.m_subsystems.Add(sub);}
    for(int cx=4;cx<=9;cx++)for(int cz=-15;cz<=-11;cz++)terrain.Terrain.AllocateChunk(cx,cz).State=TerrainChunkState.Valid;
    for(int x=70;x<=150;x++)for(int z=-235;z<=-180;z++)terrain.Terrain.SetCellValueFast(x,69,z,2);
    if(wall)for(int y=70;y<=74;y++)for(int z=-235;z<=-180;z++)terrain.Terrain.SetCellValueFast(135,y,z,2);
    return(project,time,terrain);
}
TerrainRaycastResult? Solid(SubsystemTerrain t,Vector3 a,Vector3 b)=>t.Raycast(a,b,false,true,(value,_)=>BlocksManager.Blocks[Terrain.ExtractContents(value)].IsCollidable_(value));

bool done=false;int exit=0;
Window.Frame+=()=>{if(done)return;done=true;try{
    LightingManager.Initialize();
    // As in the game: CS actors are sampled through the OnAnimateModel hook (TacticalModLoader -> ScActorSampler).
    var animateHook=new ModsManager.ModHook("OnAnimateModel");animateHook.Add(new TacticalModLoader());ModsManager.ModHooks["OnAnimateModel"]=animateHook;
    using var stream=File.OpenRead(Path.Combine(assets,"Models/ScCsgoTactical/ct.glb"));
    using var model=Model.Load(GltfLoader.Load(stream),true);ScActorAnimations.Ensure(model);
    caches["Fixture/Model"]=[model];caches["Fixture/Config.json"]=[File.ReadAllText(Path.Combine(assets,"Animations/ScTactical.json"))];

    // ---- F3 corpses: the real ComponentTacticalModel per frame (Animate = parameters, controller, sampler; then bone hierarchy). ----
    (ComponentTacticalModel Model,ComponentCreature Creature) Actor(Project project,Vector3 at,string config="Fixture/Config"){
        var body=new ComponentBody{Position=at,BoxSize=new Vector3(.65f,1.8f,.65f),Rotation=Quaternion.Identity,StandingOnValue=2};var locomotion=new ComponentLocomotion();
        var creature=new ComponentCreature{ComponentBody=body,ComponentHealth=new ComponentHealth{Health=1},ComponentSpawn=new ComponentSpawn{SpawnDuration=0},ComponentLocomotion=locomotion};
        var component=new ComponentTacticalModel();var entity=(Entity)RuntimeHelpers.GetUninitializedObject(typeof(Entity));entity.m_project=project;
        entity.m_components=[component,body,creature,creature.ComponentHealth,creature.ComponentSpawn,locomotion];foreach(var c in entity.m_components)c.m_entity=entity;
        creature.ComponentHealth.m_componentCreature=creature;
        component.Load(new ValuesDictionary{{"ModelName","Fixture/Model"},{"CastsShadow",true},{"PrepareOrder",0},{"BoundingSphereRadius",2f},{"AnimationConfigPath",config}},null);
        return(component,creature);
    }
    var budgets=typeof(TacticalRagdoll).GetField("s_active",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
    foreach(var (count,fallback) in new[]{(0,false),(1,false),(3,false),(6,false),(8,false),(6,true)}){
        var alive=new List<double>();var falling=new List<double>();var settled=new List<double>();long aliveBytes=0,fallingBytes=0,settledBytes=0;int ragdolls=0;
        for(int repeat=0;repeat<5;repeat++){
            var w=NewWorld(false);var actors=Enumerable.Range(0,count).Select(i=>Actor(w.Project,new Vector3(75.5f+i*3,70,-212.5f))).ToList();
            if(fallback){var budget=budgets.GetType().GetMethod("GetOrCreateValue").Invoke(budgets,[w.Project]);budget.GetType().GetField("Active").SetValue(budget,TacticalRagdoll.MaxActive);}
            double deadAt=-1;
            void Step(){Frame(1/60f);w.Time.m_gameTime+=1/60.0;
                foreach(var a in actors){if(deadAt>=0)a.Model.DeathPhase=Math.Min(1,(float)((w.Time.m_gameTime-deadAt)/1.2));
                    a.Model.Animate();a.Model.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,a.Model.AbsoluteBoneTransformsForCamera);}}
            (double Ms,long Bytes) Measure(int frames){long b=GC.GetAllocatedBytesForCurrentThread();var sw=Stopwatch.StartNew();for(int f=0;f<frames;f++)Step();return(sw.Elapsed.TotalMilliseconds/frames,(GC.GetAllocatedBytesForCurrentThread()-b)/frames);}
            for(int f=0;f<30;f++)Step();
            var m=Measure(60);alive.Add(m.Ms);aliveBytes+=m.Bytes;
            deadAt=w.Time.m_gameTime;foreach(var a in actors){a.Model.DeathCauseOffset=new Vector3(0,0,1);a.Creature.ComponentHealth.Health=0;}
            m=Measure(60);falling.Add(m.Ms);fallingBytes+=m.Bytes;ragdolls=TacticalRagdoll.ActiveCount(w.Project)-(fallback?TacticalRagdoll.MaxActive:0);
            for(int f=0;f<360;f++)Step(); // past the 6 s sleep limit
            m=Measure(60);settled.Add(m.Ms);settledBytes+=m.Bytes;
            foreach(var a in actors)a.Model.OnEntityRemoved();
        }
        double Med(List<double> v){v.Sort();return Math.Round(v[v.Count/2],4);}
        rows.Add(new{kind="frame",name=fallback?$"corpses-{count}-budget-full-collapse":$"corpses-{count}",code="ComponentTacticalModel per frame: Animate (hooked game sampler) + ProcessBoneHierarchy (60 fps steps, 5 repeats, median)",
            actors=count,ragdollsStarted=ragdolls,aliveMsPerFrame=Med(alive),fallingFirstSecondMsPerFrame=Med(falling),settledAfter7sMsPerFrame=Med(settled),
            aliveBytesPerFrame=aliveBytes/5,fallingBytesPerFrame=fallingBytes/5,settledBytesPerFrame=settledBytes/5,
            spreadFallingMs=Math.Round(falling.Max()-falling.Min(),4)});
        if(!fallback&&ragdolls!=Math.Min(count,TacticalRagdoll.MaxActive))failures.Add($"corpses-{count}: {ragdolls} ragdolls started");
        if(fallback&&ragdolls!=0)failures.Add("budget-full scenario still started a ragdoll");
    }

    // ---- Living actor per-stage cost: the F3 airborne rules against the legacy gait-only rules (same model/clips). ----
    caches["Fixture/ConfigLegacy.json"]=[File.ReadAllText(Path.Combine(assets,"Animations/ScTacticalHostage.json"))];
    foreach(var (label,config) in new[]{("air-rules","Fixture/Config"),("legacy-gait-rules","Fixture/ConfigLegacy")}){
        var w=NewWorld(false);var a=Actor(w.Project,new Vector3(75.5f,70,-212.5f),config);var m=a.Model;
        void Full(){Frame(1/60f);w.Time.m_gameTime+=1/60.0;m.Animate();m.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,m.AbsoluteBoneTransformsForCamera);}
        for(int f=0;f<30;f++)Full();
        if(!m.Animated)failures.Add($"alive-{label}: the game sampler did not take the frame");
        const string basis="20 living agents x 60 fps";
        Micro($"alive-{label}-frame-advance","harness only: Time.FrameIndex/FrameDuration reflection setters (subtract from the rows below)",false,1200,basis,5000,()=>Frame(1/60f));
        Micro($"alive-{label}-sync","ComponentTacticalModel.SyncAnimationParameters alone (also runs inside Animate)",false,1200,basis,5000,()=>{Frame(1/60f);m.SyncAnimationParameters();});
        Micro($"alive-{label}-animate","ComponentTacticalModel.Animate: parameters, hook, controller update (rules), ScActorSampler",false,1200,basis,5000,()=>{Frame(1/60f);m.Animate();});
        Micro($"alive-{label}-bones","ProcessBoneHierarchy for one camera",false,1200,basis,5000,()=>m.ProcessBoneHierarchy(model.RootBone,Matrix.Identity,m.AbsoluteBoneTransformsForCamera));
        Micro($"alive-{label}-total","one living actor frame: all of the above",false,1200,basis,5000,Full);
        m.OnEntityRemoved();
    }

    // ---- F4 preview: Launch + Predict exactly as UpdatePreview (real terrain ray casts), per refresh. ----
    {
        var w=NewWorld(true);var terrain=w.Terrain;
        Func<Vector3,Vector3,TerrainRaycastResult?> solid=(a,b)=>Solid(terrain,a,b);
        bool Water(Vector3 p)=>BlocksManager.Blocks[Terrain.ExtractContents(terrain.Terrain.GetCellValue(Terrain.ToCell(p.X),Terrain.ToCell(p.Y),Terrain.ToCell(p.Z)))] is WaterBlock;
        bool Loaded(Vector3 q)=>terrain.Terrain.GetChunkAtCell(Terrain.ToCell(q.X),Terrain.ToCell(q.Z)) is {State:>TerrainChunkState.InvalidContents4};
        var view=new Vector3(100.5f,71.6f,-212.5f);
        foreach(int kind in new[]{0,1,2,3,4,5})foreach(bool low in new[]{false,true})foreach(var (label,dir) in new[]{("level",Vector3.Normalize(new Vector3(1,.15f,0))),("down",Vector3.Normalize(new Vector3(1,-.5f,0)))}){
            ScGrenadeTrajectory.Path path=null;
            Micro($"preview-kind{kind}-{(low?"weak":"strong")}-{label}","ScGrenadeBallistics.Launch + ScGrenadeTrajectory.Predict (live UpdatePreview lambdas)",false,12,"one preparing local player, 12 Hz ceiling while input changes (4 Hz when still)",200,()=>{
                var launch=ScGrenadeBallistics.Launch(view,dir,Vector3.Zero,low,(a,b)=>solid(a,b)?.HitPoint());
                path=ScGrenadeTrajectory.Predict(kind,launch.Position,launch.Velocity,solid,Water,Loaded);});
            rows.Add(new{kind="preview-path",name=$"preview-kind{kind}-{(low?"weak":"strong")}-{label}",end=path.Kind.ToString(),seconds=path.Time,path.Bounces,points=path.Points.Count});
            if(path.Points.Count>ScGrenadeTrajectory.MaxPoints+1)failures.Add("preview exceeded point budget");
        }
    }

    // ---- Audited AI risk points. ----
    {
        var project=new Project();
        for(int i=0;i<100;i++){var filler=new SubsystemTime();filler.m_project=project;project.m_subsystems.Add(filler);} // a modded world has ~100 subsystems; lookup is linear
        var bombs=new SubsystemTacticalBombs();var c4=new SubsystemScC4();var grenades=new SubsystemScGrenades();
        foreach(var s in new Subsystem[]{bombs,c4,grenades}){s.m_project=project;project.m_subsystems.Add(s);}
        var at=new Vector3(10,70,10);
        Micro("zones-none","TacticalDanger.Zones(...).ToList() per active NPC per frame, no dangers",false,1200,"20 active agents x 60 fps",20000,()=>TacticalDanger.Zones(project,at,true).ToList());
        bombs.Bombs.Add(new SubsystemTacticalBombs.Bomb());bombs.Bombs.Add(new SubsystemTacticalBombs.Bomb());
        var active=(List<ScGrenadeState>)typeof(SubsystemScGrenades).GetField("m_active",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(grenades);
        active.Add(new ScGrenadeState{Kind=3,Effect=true,Remaining=5,Position=new Vector3(14,70,10)});active.Add(new ScGrenadeState{Kind=4,Effect=true,Remaining=5,Position=new Vector3(4,70,12)});
        Micro("zones-2bombs-2fires","TacticalDanger.Zones(...).ToList() per active NPC per frame, 2 bombs + 2 fires",false,1200,"20 active agents x 60 fps",20000,()=>TacticalDanger.Zones(project,at,true).ToList());
    }
    {
        var w=NewWorld(true);Func<Vector3,Vector3,bool> clear=(a,b)=>!Solid(w.Terrain,a,b).HasValue;
        var smokes=new List<ScGrenadeState>();var disturbances=new List<ScSmokeDisturbance>();
        for(int n=1;n<=3;n++){
            smokes.Add(new ScGrenadeState{Kind=2,Effect=true,Remaining=10,Age=5,Position=new Vector3(100+n*4,70,-212.5f)});
            var list=smokes.ToList();
            Micro($"smoke-blocks-{n}-through","ScSmokeVolume.Blocks, 20 m sight line through the smoke(s)",false,1200,"20 bodies with a target x 60 fps",5000,()=>ScSmokeVolume.Blocks(list,new Vector3(95.5f,71.5f,-212.5f),new Vector3(115.5f,71.5f,-212.5f),clear,disturbances));
            Micro($"smoke-blocks-{n}-clear","ScSmokeVolume.Blocks, 20 m sight line beside the smoke(s)",false,1200,"20 bodies with a target x 60 fps",5000,()=>ScSmokeVolume.Blocks(list,new Vector3(95.5f,71.5f,-202.5f),new Vector3(115.5f,71.5f,-202.5f),clear,disturbances));
        }
    }
    {
        var spawn=new SubsystemSpawn();int chunks=0;
        for(int x=-10;x<=10;x++)for(int z=-10;z<=10;z++){var c=new SpawnChunk{Point=new Point2(x,z)};
            for(int k=0;k<3;k++)c.SpawnsData.Add(new SpawnEntityData{TemplateName=k==0?"ScTacticalEnemy":"Wolf_Gray",Position=new Vector3(x*16+k*5,70,z*16+k*3)});spawn.m_chunks[c.Point]=c;chunks++;}
        var p=new Vector3(3.5f,71.1f,5.5f);float spacing=60;
        Micro("sleeping-squad-scan",$"replica SubsystemTacticalEnemies.Suitable sleeping LINQ, {chunks} visited chunks x 3 entries, no match",true,10,"assumed 10 natural candidates reaching this check per second",2000,()=>{
            var sleeping=spawn.m_chunks.Values.SelectMany(c=>c.SpawnsData).Where(e=>e.TemplateName=="ScTacticalEnemy");
            if(sleeping.Any(e=>Vector3.DistanceSquared(e.Position,p)<spacing*spacing&&e.Position.Y<0))throw new Exception("fixture");});
    }
    {
        // AgentVoice's 0.5 s scan: per agent, the reflective combat probe over its components.
        var agents=new List<Entity>();
        for(int i=0;i<20;i++){var entity=(Entity)RuntimeHelpers.GetUninitializedObject(typeof(Entity));var list=new List<Component>();
            for(int k=0;k<24;k++)list.Add(k%2==0?new ComponentBody():new ComponentHealth());list.Add(i%2==0?new ComponentTacticalEnemy():new ComponentTacticalCompanion());
            entity.m_components=list;agents.Add(entity);}
        Micro("voice-combat-scan","replica AgentVoice combat probe (GetType().Name + GetField per component) for 20 agents x 25 components",true,2,"one scan every 0.5 s",200,()=>{
            int combat=0;foreach(var e in agents)if(e.Components.Any(c=>c.GetType().Name=="ComponentTacticalEnemy"&&c.GetType().GetField("TargetBody")?.GetValue(c)!=null||c.GetType().Name=="ComponentTacticalCompanion"&&c.GetType().GetField("threat",BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(c)!=null))combat++;
            if(combat<0)throw new Exception();});
    }
    {
        var player=(ComponentPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ComponentPlayer));var forward=new Vector3(0,0,-1);
        Micro("damage-hud-idle","ScDamageIndicator.Intensities with no marks (every drawn frame)",false,60,"one player x 60 fps",100000,()=>ScDamageIndicator.Intensities(player,forward,10));
        for(int i=0;i<4;i++)ScDamageIndicator.Report(player,new Vector3(MathF.Sin(i*1.6f),0,MathF.Cos(i*1.6f)),10);
        Micro("damage-hud-4marks","ScDamageIndicator.Intensities with 4 live marks",false,60,"one player x 60 fps while marks fade",100000,()=>ScDamageIndicator.Intensities(player,forward,10.1));
    }
}catch(Exception e){Console.Error.WriteLine(e);failures.Add(e.GetType().Name+": "+e.Message);exit=1;}finally{Window.Close();}};
Window.Run(320,240,WindowMode.Fixed,"CS hotspot baseline (isolated)");
var machine=new{Environment.ProcessorCount,os=Environment.OSVersion.ToString(),runtime=Environment.Version.ToString(),Stopwatch.Frequency,tacticalMvid=typeof(TacticalRagdoll).Module.ModuleVersionId,coreMvid=typeof(ScGrenadeTrajectory).Module.ModuleVersionId};
File.WriteAllText(Path.Combine(output,"hotspots.json"),JsonSerializer.Serialize(new{failures,machine,rows,
    scope="Frozen-input native CPU/allocation baseline on Windows desktop; not game frame time, GPU, vsync or phone. Replica rows re-run a private expression verbatim."},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"hotspot rows={rows.Count} failures={failures.Count}");foreach(var f in failures)Console.WriteLine("FAIL "+f);
return failures.Count==0&&exit==0?0:1;
