using System.Reflection;
using System.Runtime.CompilerServices;
using System.IO.Compression;
using System.Xml.Linq;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

// Load every tactical/core type from the delivered packages, never project references.
static class TacticalEnemyRegression {
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static T Blank<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    /// <summary>An attack as the engine hands it to the ProcessAttackment hook (built without the engine's constructor,
    /// which words a cause of death from a full creature the fixtures do not have).</summary>
    static Attackment Strike(ComponentBody target,Entity attacker,float power=2){var a=Blank<Attackment>();a.Target=target.Entity;a.Attacker=attacker;a.HitPoint=target.Position;a.HitDirection=Vector3.UnitZ;a.AttackPower=power;a.DictionaryForOtherMods=new ValuesDictionary();return a;}
    static void Set(object o,string name,object value)=>o.GetType().GetField(name,Fields).SetValue(o,value);
    static Entity E(Project p,params Component[] components){var e=Blank<Entity>();e.m_project=p;e.m_isAddedToProject=true;e.m_components=components.ToList();foreach(var c in components)c.m_entity=e;p.m_entities[e]=true;return e;}
    static ValuesDictionary Round(ValuesDictionary v){var x=new XElement("Values");v.Save(x);var r=new ValuesDictionary();r.ApplyOverrides(XElement.Parse(x.ToString()));return r;}
    sealed class Audio:SubsystemAudio {public int Shots;public readonly List<(string Name,float Volume)> Played=[];public override void PlaySound(string n,float v,float pitch,Vector3 p,float d,bool delay){Shots++;Played.Add((n,v));}}
    sealed class Ground:SubsystemTerrain {public bool Blocked;public float? Floor;
        public override TerrainRaycastResult? Raycast(Vector3 a,Vector3 b,bool i,bool s,Func<int,float,bool> f){
            if(Blocked)return new TerrainRaycastResult{Distance=.5f};
            // An optional floor plane (the top face of the cells below it), met only by rays that go down through it.
            if(Floor is {} y&&a.Y>=y&&b.Y<y){var d=b-a;float t=(a.Y-y)/(a.Y-b.Y);return new TerrainRaycastResult{Ray=new Ray3(a,Vector3.Normalize(d)),Distance=t*d.Length(),CellFace=new CellFace(Terrain.ToCell(a.X),(int)y-1,Terrain.ToCell(a.Z),4)};}
            return null;}}
    sealed class Drops:SubsystemPickables {public readonly List<Pickable> Items=[];public override Pickable AddPickable(int value,int count,Vector3 p,Vector3? vel,Matrix? m,Entity owner){var item=new Pickable{Value=value,Count=count,Position=p};Items.Add(item);return item;}}
    sealed class Health:ComponentHealth {public override void Injure(Injury i){i.Attackment.EnableHitValueParticleSystem=false;base.Injure(i);}}
    sealed class HalfBlock:DirtBlock {public override BoundingBox[] GetCustomCollisionBoxes(SubsystemTerrain t,int value)=>[new BoundingBox(Vector3.Zero,new Vector3(1,.5f,1))];}
    sealed class Eye:Camera {
        readonly Vector3 eye,forward;readonly Matrix view,projection;
        public Eye(Vector3 eye,Vector3 forward):base(null){this.eye=eye;this.forward=Vector3.Normalize(forward);view=Matrix.CreateLookAt(eye,eye+this.forward,Vector3.UnitY);projection=Matrix.CreatePerspectiveFieldOfView(1.2f,16f/9,.1f,256);}
        public override Vector3 ViewPosition=>eye;public override Vector3 ViewDirection=>forward;public override Vector3 ViewUp=>Vector3.UnitY;public override Vector3 ViewRight=>Vector3.Normalize(Vector3.Cross(forward,Vector3.UnitY));
        public override Matrix ViewMatrix=>view;public override Matrix InvertedViewMatrix=>Matrix.Invert(view);public override Matrix ProjectionMatrix=>projection;public override Matrix ScreenProjectionMatrix=>projection;
        public override Matrix InvertedProjectionMatrix=>Matrix.Invert(projection);public override Matrix ViewProjectionMatrix=>view*projection;public override Vector2 ViewportSize=>new(1280,720);public override Matrix ViewportMatrix=>Matrix.Identity;
        public override BoundingFrustum ViewFrustum=>new(view*projection);public override bool UsesMovementControls=>false;public override bool IsEntityControlEnabled=>false;public override void Update(float dt){}
    }
    sealed class LineLog(List<string> lines):ILogSink {public void Log(LogType type,string message){if(message.Contains("[CS_SPAWN]"))lines.Add(message);}}
    sealed class SpawnLog:ILogSink {public readonly List<string> Lines=[];public void Log(LogType type,string message){if(message.StartsWith("[CS_SPAWN] natural status",StringComparison.Ordinal))Lines.Add(message);}}
    sealed class LeafBlock:DirtBlock {} // a plain solid block for older fixtures; natural placement uses the real leaves below
    // current-direction-20260929: an inventory that hands out one item per removal (a short removal must be put back),
    // and a clothing item that takes 20% of what reaches it and counts how often the native pass asks it.
    sealed class Stingy:ComponentInventory {public override int RemoveSlotItems(int slot,int count)=>base.RemoveSlotItems(slot,Math.Min(count,1));}
    sealed class ClothData:ClothingData {public ClothData(){Layer=1;DisplayName="fixture cloth";}
        public override void ApplyArmorProtection(ComponentClothing c,List<int> before,List<int> after,int sequence,Attackment a,ref float power){Cloth.Calls++;power*=.8f;}}
    sealed class Cloth:Block {public static int Calls;static ClothData data;public override ClothingData GetClothingData(int value)=>data??=new ClothData();public override bool CanWear(int value)=>true;
        public override void GenerateTerrainVertices(BlockGeometryGenerator g,TerrainGeometry t,int value,int x,int y,int z){}public override void DrawBlock(PrimitivesRenderer3D r,int value,Color color,float size,ref Matrix m,DrawBlockEnvironmentData env){}}
    sealed class NaturalWorld {public SpawnProject P;public dynamic Director;public SubsystemTime Time;public Ground Terrain;public SubsystemBodies Bodies;public SubsystemGameInfo Info;public SubsystemCreatureSpawn Spawn;public ComponentPlayer Player;public Subsystem Rules;public Func<int> Created;}
    sealed class SpawnProject:Project {
        public Func<Entity> Factory;public Action<Entity> Added,Removed;
        public override Entity CreateEntity(ValuesDictionary v,int id=0)=>Factory==null?base.CreateEntity(v,id):Factory();
        public override void AddEntity(Entity e){if(Factory==null){base.AddEntity(e);return;}e.m_isAddedToProject=true;m_entities[e]=true;Added?.Invoke(e);}
        public override void RemoveEntity(Entity e,bool dispose){if(Factory==null){base.RemoveEntity(e,dispose);return;}e.m_isAddedToProject=false;m_entities.Remove(e);Removed?.Invoke(e);}
    }
    internal record Result(string Name,bool Ok,string Detail);
    internal static List<Result> Run(Assembly core,Assembly dlc,string corePath,string dlcPath,bool includeRendering=true,string vanillaContent=null,Func<string,bool> filter=null){
        var results=new List<Result>();
        void Test(string n,Action a){if(filter is not null&&!filter(n))return;try{a();results.Add(new("enemy/"+n,true,""));}catch(Exception ex){results.Add(new("enemy/"+n,false,ex.ToString()));}}
        void TestD(string n,Func<string> a){if(filter is not null&&!filter(n))return;try{results.Add(new("enemy/"+n,true,a()));}catch(Exception ex){results.Add(new("enemy/"+n,false,ex.ToString()));}}
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        Type C(string n)=>core.GetType("Game."+n,true);Type T(string n)=>dlc.GetType("Game."+n,true);
        // Release 1.4.0 (round 12): the spawn status and squad lines these checks read are test diagnostics, written only
        // while KnifeLog.Diagnostics is on (SCCS_DIAGNOSTICS=1); players' logs leave them out.
        // A core without the switch (before 1.4.0 r12c) logs them always; the suite still runs and reports.
        var diagnostics=C("KnifeLog").GetField("Diagnostics");bool diagnosticsBefore=(bool?)diagnostics?.GetValue(null)??true;diagnostics?.SetValue(null,true);
        dynamic New(string n,params object[] args)=>Activator.CreateInstance(T(n),args);
        dynamic State(int role,int seed=12)=>(object)T("TacticalEnemyState").GetMethod("Create").Invoke(null,[Enum.ToObject(T("TacticalRole"),role),"fixture-squad",new Engine.Random(seed)]);
        dynamic Decode(string s)=>T("TacticalEnemyState").GetMethod("Decode").Invoke(null,[s]);
        var rf=C("ScGunRegistry").GetField("Current");var original=rf.GetValue(null);
        void FreshRegistry()=>rf.SetValue(null,Activator.CreateInstance(C("ScGunRegistry")));
        (Project P,dynamic Director,SubsystemTime Time,Ground Terrain,SubsystemBodies Bodies,SubsystemGameInfo Info,SubsystemCreatureSpawn Spawn,SubsystemSpawn Sleeping,Audio Audio,Drops Drops) World(){
            var p=new SpawnProject();var t=new SubsystemTime();var terrain=new Ground{Terrain=new Terrain()};var bodies=new SubsystemBodies();var info=new SubsystemGameInfo{WorldSettings=Blank<WorldSettings>()};
            info.WorldSettings.GameMode=GameMode.Survival;info.WorldSettings.EnvironmentBehaviorMode=EnvironmentBehaviorMode.Living;var spawn=new SubsystemCreatureSpawn{m_subsystemTerrain=terrain,m_subsystemBodies=bodies,m_subsystemSky=new SubsystemSky()};var sleeping=new SubsystemSpawn();spawn.m_subsystemSpawn=sleeping;
            var audio=new Audio();var drops=new Drops();dynamic director=New("SubsystemTacticalEnemies");var grenades=(Subsystem)Activator.CreateInstance(C("SubsystemScGrenades"));Set(grenades,"m_time",t);Set(grenades,"m_terrain",terrain);Set(grenades,"m_bodies",bodies);
            var armor=(Subsystem)Activator.CreateInstance(C("SubsystemScArmor"));
            foreach(var s in new Subsystem[]{t,terrain,bodies,info,spawn,sleeping,audio,drops,new SubsystemPlayers(),new SubsystemTimeOfDay(),grenades,armor,(Subsystem)director}){s.m_project=p;p.m_subsystems.Add(s);}
            armor.Load(new ValuesDictionary());director.Load(new ValuesDictionary());return(p,director,t,terrain,bodies,info,spawn,sleeping,audio,drops);
        }
        (dynamic Enemy,ComponentCreature Creature,ComponentInventoryBase Inv,ComponentPathfinding Path) Enemy(Project p,int role=1,int seed=12){
            dynamic enemy=New("ComponentTacticalEnemy");var body=new ComponentBody{Position=new Vector3(0,60,0),BoxSize=new Vector3(.65f,1.8f,.65f),Mass=75};var health=new ComponentHealth{Health=1};var locomotion=new ComponentLocomotion();var spawn=new ComponentSpawn();var creature=new ComponentCreature{ComponentBody=body,ComponentHealth=health,ComponentLocomotion=locomotion,ComponentSpawn=spawn,m_killVerbs=["shot"]};health.m_componentCreature=creature;
            var path=new ComponentPathfinding{m_componentPilot=new ComponentPilot{m_componentCreature=creature}};var inv=(ComponentInventoryBase)New("ComponentTacticalInventory");inv.Load(new ValuesDictionary{{"SlotsCount",5},{"Slots",new ValuesDictionary()}},null);var selector=new ComponentBehaviorSelector();
            var parts=new List<Component>{(Component)enemy,body,health,creature,locomotion,spawn,path,path.m_componentPilot,inv,selector};
            E(p,parts.ToArray());enemy.Load(new ValuesDictionary(),null);enemy.Configure(State(role,seed),body.Position);Set(enemy,"random",new Engine.Random(seed));selector.Load(new(),null);selector.Update(.1f);return(enemy,creature,inv,path);
        }
        var savedTypes=BlocksManager.BlockTypeToIndex.ToArray();var savedNames=BlocksManager.BlockNameToIndex.ToArray();var savedBlocks=(Block[])BlocksManager.Blocks.Clone();
        try{
            // Death drops now resolve real vanilla materials; the isolated runner has no BlocksManager.Initialize.
            foreach(var (index,block) in new (int,Block)[]{(40,new IronIngotBlock{CraftingId="ironingot"}),(41,new CopperIngotBlock{CraftingId="copperingot"}),(42,new CoalChunkBlock{CraftingId="coalchunk"}),(710,(Block)Activator.CreateInstance(C("ScChickenEggBlock")))}){
                block.BlockIndex=index;BlocksManager.Blocks[index]=block;BlocksManager.BlockTypeToIndex[block.GetType()]=index;BlocksManager.BlockNameToIndex[block.GetType().Name]=index;
            }
            FreshRegistry();
            Test("feedback/flash-detonation-on-screen-beyond-radius-with-cover",()=>{
                foreach(var (z,wall,expected) in new[]{(-200f,false,true),(-200f,true,false),(200f,false,false),(-20f,false,true)}){
                    var f=World();f.Terrain.Blocked=wall;
                    var player=Blank<ComponentPlayer>();player.PlayerData=Blank<PlayerData>();
                    var body=new ComponentBody{Position=new Vector3(0,60,0),BoxSize=new Vector3(.65f,1.8f,.65f)};
                    player.ComponentBody=body;player.PlayerData.m_gameWidget=Blank<GameWidget>();
                    player.PlayerData.m_gameWidget.m_activeCamera=new Eye(new Vector3(0,61.5f,0),-Vector3.UnitZ);
                    E(f.P,player,body);f.Bodies.AddBody(body);
                    var grenade=Activator.CreateInstance(C("ScGrenadeState"));Set(grenade,"Kind",1);Set(grenade,"Owner",-2);Set(grenade,"Position",new Vector3(0,61.5f,z));
                    var system=f.P.m_subsystems.Single(s=>s.GetType()==C("SubsystemScGrenades"));
                    C("SubsystemScGrenades").GetMethod("Detonate",Fields).Invoke(system,[grenade]);
                    RequireBlind();
                    void RequireBlind()=>Check((bool)C("SubsystemScGrenades").GetMethod("IsBodyBlinded").Invoke(system,[body])==expected,$"flash z={z}, wall={wall}: expected blind={expected}");
                }
            });
            Test("feedback/distant-sniper-stops-tracks-and-shoots-after-reload",()=>{
                foreach(float distance in new[]{80f,160,256}){
                    var f=World();var e=Enemy(f.P,0);e.Enemy.State.Grenades=0;e.Enemy.State.Bomb=false;
                    var target=new ComponentBody{Position=new Vector3(0,60,-distance),BoxSize=new Vector3(.8f,1.8f,.8f),Mass=75};
                    var health=new Health{Health=1,AttackResilience=10000,AttackResilienceFactor=1};
                    var creature=new ComponentCreature{ComponentBody=target,ComponentHealth=health,m_killVerbs=["shot"],m_subsystemPlayerStats=new SubsystemPlayerStats()};
                    health.m_componentCreature=creature;E(f.P,target,health,creature);f.Bodies.AddBody(target);
                    e.Enemy.Alert(target);e.Enemy.State.Rounds=0;e.Enemy.State.Reserve=5;e.Enemy.State.ReloadLeft=2.8f;
                    for(int i=0;i<20;i++){f.Time.m_gameTime+=.1;((IUpdateable)e.Enemy).Update(.1f);}
                    Check(e.Enemy.TargetBody==target&&e.Path.Destination is null&&e.Enemy.State.Rounds==0,"reload lost target, chased a visible distant target or refilled early");
                    for(int i=0;i<35;i++){f.Time.m_gameTime+=.1;((IUpdateable)e.Enemy).Update(.1f);}
                    Check(e.Enemy.TargetBody==target&&e.Enemy.State.Rounds<5&&e.Enemy.State.Rounds+e.Enemy.State.Reserve<5&&health.Health<1,$"sniper never resumed actual distant damage after reload: distance={distance}, target={e.Enemy.TargetBody==target}, rounds={e.Enemy.State.Rounds}, reserve={e.Enemy.State.Reserve}, hp={health.Health}, gun={e.Enemy.State.Variant}");
                }
            });
            Test("feedback/source-less-gunshot-evades-and-smg-strafes",()=>{
                var f=World();f.Terrain.Floor=60;var e=Enemy(f.P,2);
                BlocksManager.Blocks[0]=new AirBlock{IsCollidable=false};BlocksManager.Blocks[2]=new DirtBlock{BlockIndex=2,IsCollidable=true};
                for(int cx=0;cx<2;cx++)for(int cz=0;cz<2;cz++)f.Terrain.Terrain.AllocateChunk(cx,cz).State=TerrainChunkState.Valid;
                for(int x=0;x<32;x++)for(int z=0;z<32;z++)f.Terrain.Terrain.SetCellValueFast(x,59,z,2);
                var body=e.Creature.ComponentBody;body.Position=new Vector3(12.5f,60,12.5f);e.Enemy.Home=body.Position;e.Enemy.State.Grenades=0;
                ((INoiseListener)e.Enemy).HearNoise(null,new Vector3(12.5f,60,4.5f),1);
                ((IUpdateable)e.Enemy).Update(.1f);
                Check(e.Path.Destination is not null&&e.Enemy.TargetBody is null,"position-only gunshot ignored or granted hostility");
                var target=new ComponentBody{Position=new Vector3(12.5f,60,4.5f),BoxSize=new Vector3(.8f,1.8f,.8f),Mass=75};
                var health=new Health{Health=1,AttackResilience=10000,AttackResilienceFactor=1};var creature=new ComponentCreature{ComponentBody=target,ComponentHealth=health,m_subsystemPlayerStats=new SubsystemPlayerStats(),m_killVerbs=["shot"]};health.m_componentCreature=creature;E(f.P,target,health,creature);f.Bodies.AddBody(target);
                var guns=((Array)C("GunSpec").GetField("All").GetValue(null)).Cast<object>().ToArray();
                e.Enemy.State.Variant=Array.FindIndex(guns,g=>(string)g.GetType().GetField("Name").GetValue(g)=="mp9");
                e.Enemy.Alert(target);Set(e.Enemy,"coverLeft",0f);Set(e.Enemy,"pathLeft",0f);e.Path.Stop();
                int rounds=e.Enemy.State.Rounds;
                for(int i=0;i<22;i++){f.Time.m_gameTime+=.1;((IUpdateable)e.Enemy).Update(.1f);}
                Check(e.Path.Destination is Vector3 at&&Math.Abs(at.X-body.Position.X)>1&&e.Enemy.State.Rounds<rounds,"SMG did not combine lateral movement with firing");
                f.Terrain.Blocked=true;
                var cover=T("TacticalCombatMovement").GetMethod("Strafe").Invoke(null,[f.Terrain,body,target.Position,1]);
                Check(cover is null,"combat step accepted an obstructed landing");
            });
            Test("feedback/swimming-shore-jump-cooldown-and-headroom",()=>{
                foreach(float immersion in new[]{.6f,.9f}){
                    var f=World();var e=Enemy(f.P);var body=e.Creature.ComponentBody;
                    BlocksManager.Blocks[0]=new AirBlock{IsCollidable=false};BlocksManager.Blocks[2]=new DirtBlock{BlockIndex=2,IsCollidable=true};
                    f.Terrain.Terrain.AllocateChunk(0,0).State=TerrainChunkState.Valid;
                    body.Position=new Vector3(8.5f,60,8.5f);body.ImmersionFactor=immersion;body.StandingOnValue=null;
                    f.Terrain.Terrain.SetCellValueFast(8,60,7,2);
                    var method=T("TacticalNavigation").GetMethod("StepAssist");object[] args=[e.Creature,f.Terrain,new Vector3(8.5f,61,6.5f),0d,1d];
                    Check((bool)method.Invoke(null,args)&&e.Creature.ComponentLocomotion.JumpOrder==1,"water excluded from shore jump");
                    Check(!(bool)method.Invoke(null,args),"jump cooldown ignored");
                    f.Terrain.Terrain.SetCellValueFast(8,62,7,2);args[4]=2d;
                    Check(!(bool)method.Invoke(null,args),"jump into low ceiling accepted");
                }
                {
                    var f=World();var e=Enemy(f.P);var body=e.Creature.ComponentBody;
                    f.Terrain.Terrain.AllocateChunk(0,0).State=TerrainChunkState.Valid;
                    body.Position=new Vector3(8.5f,60,8.5f);body.ImmersionFactor=.85f;body.ImmersionDepth=1.4f;body.StandingOnValue=null;
                    f.Terrain.Terrain.SetCellValueFast(8,60,7,2);f.Terrain.Terrain.SetCellValueFast(8,61,7,2);
                    var method=T("TacticalNavigation").GetMethod("StepAssist");object[] args=[e.Creature,f.Terrain,new Vector3(8.5f,62,6.5f),0d,1d];
                    Check((bool)method.Invoke(null,args),"normal bank rejected because swimming feet are below waterline");
                    f.Terrain.Terrain.SetCellValueFast(8,62,7,2);args[4]=2d;
                    Check(!(bool)method.Invoke(null,args),"waterline-relative cliff accepted");
                }
            });
            Test("survival-and-creative-three-five-squad-placement-budget-and-rollback",()=>{
                var old=DatabaseManager.m_valueDictionaries.GetValueOrDefault("ScTacticalEnemy");DatabaseManager.m_valueDictionaries["ScTacticalEnemy"]=new ValuesDictionary();
                try{
                    foreach(int count in new[]{3,5})foreach(bool snow in new[]{false,true})foreach(bool creative in new[]{false,true}){
                        var f=World();var p=(SpawnProject)f.P;f.Info.WorldSettings.GameMode=creative?GameMode.Creative:GameMode.Survival;
                        BlocksManager.Blocks[0]=new AirBlock{IsCollidable=false};BlocksManager.Blocks[2]=new DirtBlock{BlockIndex=2,IsCollidable=true};
                        for(int cx=0;cx<4;cx++)for(int cz=0;cz<4;cz++)f.Terrain.Terrain.AllocateChunk(cx,cz).State=TerrainChunkState.Valid;
                        BlocksManager.Blocks[61]=new SnowBlock{BlockIndex=61,IsCollidable=false};
                        // agent-feedback-20260928 P1: challenge squads appear 18-28 blocks away in the chosen direction.
                        for(int x=1;x<63;x++)for(int z=1;z<63;z++){f.Terrain.Terrain.SetCellValueFast(x,60,z,2);if(snow)f.Terrain.Terrain.SetCellValueFast(x,61,z,61);f.Terrain.Terrain.SetTopHeight(x,z,snow?61:60);}
                        var player=Blank<ComponentPlayer>();player.ComponentBody=new ComponentBody{Position=new Vector3(8.5f,61,8.5f),BoxSize=new Vector3(.65f,1.8f,.65f)};E(p,player,player.ComponentBody);p.FindSubsystem<SubsystemPlayers>(true).m_componentPlayers.Add(player);f.Bodies.AddBody(player.ComponentBody);
                        int created=0;p.Factory=()=>{created++;var e=Enemy(p).Creature.Entity;p.m_entities.Remove(e);e.m_isAddedToProject=false;return e;};
                        p.Added=e=>{f.Director.OnEntityAdded(e);f.Bodies.AddBody(e.FindComponent<ComponentBody>(true));};p.Removed=e=>{f.Director.OnEntityRemoved(e);f.Bodies.RemoveBody(e.FindComponent<ComponentBody>(true));};
                        var beacon=new ComponentInventory();beacon.m_slots.Add(new(){Value=709,Count=1});
                        C("ScGunRegistry").GetField("RecoveryOwner").SetValue(rf.GetValue(null),(Func<IInventory,string>)(_=>"fixture/squad"));
                        bool spawned=(bool)C("ScCraftBatch").GetMethod("TryUseItem").Invoke(null,[beacon,0,709,(Func<bool>)(()=> (int)f.Director.SpawnManual(new Point3(12,snow?61:60,8),count)==count)]);
                        Check(spawned&&created==count&&beacon.GetSlotCount(0)==0,"manual whole squad not created or beacon not consumed on snow");
                        var members=((System.Collections.IEnumerable)f.Director.Enemies).Cast<dynamic>().ToArray();Check(members.Length==count&&members.Select(e=>(string)e.State.Squad).Distinct().Count()==1&&members.Select(e=>(int)e.State.Role).Distinct().Count()==count,"wrong shared squad or roles");
                        Check(members.All(e=>Math.Abs((float)e.Creature.ComponentBody.Position.Y-61.1f)<.01f),"manual squad spawns in ground");
                        Check(members.Any(e=>Vector2.Distance(((Vector3)e.Creature.ComponentBody.Position).XZ,new Vector2(12.5f,8.5f))<.1f)
                            &&members.All(e=>Vector2.Distance(((Vector3)e.Creature.ComponentBody.Position).XZ,new Vector2(12.5f,8.5f))<6),"squad not placed around clicked ground");
                        Check(members.All(e=>(float)e.State.Warmup==3f),"summoned squad has no warning window");
                        Check(members.All(e=>(string)e.State.Source=="manual"),"summoned squad not tagged as manual");
                        int oldLimit=SubsystemCreatureSpawn.m_totalLimit;
                        try{SubsystemCreatureSpawn.m_totalLimit=0;f.Director.MaxActive=count;Check((int)f.Director.SpawnManual(new Point3(12,60,8),3)==3&&created==count+3,"manual spawn still applies population limit");}finally{SubsystemCreatureSpawn.m_totalLimit=oldLimit;}
                        f.Director.MaxActive=10;foreach(var e in p.Entities.ToArray())p.RemoveEntity(e,false);
                        int attempt=0;p.Factory=()=>{if(++attempt==2)throw new Exception("injected entity factory failure");var e=Enemy(p).Creature.Entity;p.m_entities.Remove(e);e.m_isAddedToProject=false;return e;};
                        beacon.m_slots[0]=new(){Value=709,Count=1};
                        Check(!(bool)C("ScCraftBatch").GetMethod("TryUseItem").Invoke(null,[beacon,0,709,(Func<bool>)(()=> (int)f.Director.SpawnManual(new Point3(8,60,8),count)==count)])&&beacon.GetSlotCount(0)==1&&p.Entities.Count==0,"failed summon spent beacon or leaked members");
                        attempt=0;
                        Check((int)f.Director.SpawnManual(new Point3(8,60,8),count)==0&&p.Entities.Count==0,"failed squad leaked partial members");
                        Check((int)f.Director.SpawnManual(new Point3(8,60,8),4)==0,"unsupported squad size accepted");
                    }
                }finally{if(old==null)DatabaseManager.m_valueDictionaries.Remove("ScTacticalEnemy");else DatabaseManager.m_valueDictionaries["ScTacticalEnemy"]=old;}
            });
            Test("manual-floor-under-roof-grass-half-block-and-headroom",()=>{
                var f=World();var terrain=f.Terrain.Terrain;var chunk=terrain.AllocateChunk(0,0);chunk.State=TerrainChunkState.InvalidLight;
                BlocksManager.Blocks[0]=new AirBlock{IsCollidable=false};BlocksManager.Blocks[2]=new DirtBlock{IsCollidable=true};BlocksManager.Blocks[62]=new HalfBlock{IsCollidable=true};BlocksManager.Blocks[63]=new AirBlock{IsCollidable=false};
                var method=T("SubsystemTacticalEnemies").GetMethod("ManualPosition",Fields);
                Vector3? Position(){object[] args=[8,60,8,Vector3.Zero];return (bool)method.Invoke((object)f.Director,args)?(Vector3)args[3]:null;}
                terrain.SetCellValueFast(8,60,8,2);terrain.SetCellValueFast(8,61,8,63);terrain.SetCellValueFast(8,70,8,2);terrain.SetTopHeight(8,8,70);
                Check(Position() is Vector3 floor&&Math.Abs(floor.Y-61.1f)<.01f,"grass/roof/light invalidation incorrectly blocks clicked floor");
                terrain.SetCellValueFast(8,60,8,62);Check(Position() is Vector3 step&&Math.Abs(step.Y-60.6f)<.01f,"half-block support treated as full block");
                terrain.SetCellValueFast(8,62,8,2);terrain.SetCellValueFast(8,63,8,2);terrain.SetCellValueFast(8,64,8,2);Check(Position()==null,"spawn intersects low ceiling");
                Check((int)f.Director.SpawnManual(new Point3(8,60,8),5)==0&&((string)f.Director.ManualFailure).Contains("可站立"),"placement failure hidden behind population warning");
            });
            Test("34-weapon-pool-no-registry-allocation",()=>{
                var names=new HashSet<string>();var pools=(string[][])T("TacticalEnemyState").GetField("Pools").GetValue(null);
                for(int role=0;role<5;role++)for(int seed=0;seed<160;seed++){dynamic s=State(role,seed);dynamic spec=((Array)C("GunSpec").GetField("All").GetValue(null)).GetValue((int)s.Variant);names.Add((string)spec.Name);Check(pools[role].Contains((string)spec.Name)&&s.Rounds==spec.Magazine&&s.Reserve==spec.Magazine*3&&s.Bomb==(role==4),"role/equipment mismatch");_=(int)s.DisplayValue;Check(!s.Encode().Contains("DisplayValue"),"transient player item value serialized");}
                Check(names.Count==34&&!names.Contains("taser"),"missing weapon coverage");Check((int)C("ScGunRegistry").GetProperty("Next").GetValue(rf.GetValue(null))==1,"NPC templates consumed gun numbers");
            });
            Test("equipment-world-xml-and-native-unload-two-rounds",()=>{
                var f=World();var e=Enemy(f.P,4);e.Enemy.State.Rounds=2;e.Enemy.State.Reserve=7;e.Enemy.State.Grenades=0;e.Enemy.State.Bomb=false;e.Enemy.State.ReloadLeft=1.25f;e.Creature.ComponentHealth.Health=.42f;
                string expected=e.Enemy.Capture();
                for(int i=0;i<2;i++){var v=new ValuesDictionary();e.Enemy.Save(v,null);e.Enemy.Load(Round(v),null);Check(e.Enemy.Capture()==expected,"world save changed equipment");
                    var data=new SpawnEntityData{TemplateName="ScTacticalEnemy",EntityId=17,Position=e.Creature.ComponentBody.Position,Data="foreign=ok"};T("SubsystemTacticalEnemies").GetMethod("SaveSpawn").Invoke(null,[e.Creature.ComponentSpawn,data]);data.Data+="|other=ok";T("SubsystemTacticalEnemies").GetMethod("SaveSpawn").Invoke(null,[e.Creature.ComponentSpawn,data]);Check(data.Data.Contains("foreign=ok")&&data.Data.Contains("|other=ok"),"discarded another mod extension");
                    string native=f.Sleeping.SaveSpawnsData([data]);var read=new List<SpawnEntityData>();f.Sleeping.LoadSpawnsData(native,read);T("SubsystemTacticalEnemies").GetMethod("ReadSpawn").Invoke(null,[e.Creature.Entity,read.Single()]);Check(e.Enemy.Capture()==expected&&e.Enemy.Home==data.Position,"native unload refilled/re-rolled loadout");}
                Check(f.Sleeping.SaveSpawnsData([new SpawnEntityData{TemplateName="ScTacticalEnemy",Data=expected}]).Length<900,"per-enemy save unexpectedly huge");
            });
            Test("future-invalid-equipment-refused-not-rerolled",()=>{
                string json=State(0).Encode();foreach(string bad in new[]{json.Replace("\"Schema\":1","\"Schema\":9"),json.Replace("\"Rounds\":10","\"Rounds\":-1"),"{}"}){
                    if(bad==json)continue;bool refused=false;try{Decode(bad);}catch{refused=true;}Check(refused,"accepted invalid/future loadout");}
                var f=World();var e=Enemy(f.P);bool missing=false;try{T("SubsystemTacticalEnemies").GetMethod("ReadSpawn").Invoke(null,[e.Creature.Entity,new SpawnEntityData()]);}catch{missing=true;}Check(missing,"missing unload state silently rerolled");
            });
            Test("native-despawn-fade-does-not-refill-or-resurrect",()=>{
                var f=World();var e=Enemy(f.P);var data=new SpawnEntityData{TemplateName="ScTacticalEnemy",EntityId=e.Creature.Entity.Id,Position=e.Creature.ComponentBody.Position};
                var chunk=f.Sleeping.GetOrCreateSpawnChunk(new Point2(0,0));chunk.SpawnsData.Add(data);f.Sleeping.m_spawnEntityDatas[data.EntityId]=data;
                T("SubsystemTacticalEnemies").GetMethod("SaveSpawn").Invoke(null,[e.Creature.ComponentSpawn,data]);e.Creature.ComponentSpawn.DespawnTime=0;e.Enemy.State.ReloadLeft=1.2f;e.Creature.ComponentHealth.Health=.2f;
                ((IUpdateable)e.Enemy).Update(.5f);Check(e.Enemy.State.ReloadLeft==1.2f,"AI continues consuming/refilling during native fade");f.Director.OnEntityRemoved(e.Creature.Entity);
                T("SubsystemTacticalEnemies").GetMethod("ReadSpawn").Invoke(null,[e.Creature.Entity,data]);Check(e.Creature.ComponentHealth.Health==.2f&&e.Enemy.State.ReloadLeft==1.2f,"fade snapshot discarded last health/state");
                e.Creature.ComponentHealth.Health=0;e.Enemy.Died();f.Director.OnEntityRemoved(e.Creature.Entity);Check(chunk.SpawnsData.Count==0&&!f.Sleeping.m_spawnEntityDatas.ContainsKey(data.EntityId),"dead fading enemy remains eligible to respawn");
            });
            // agent-followup-140 F1: per-world switch, whole-day wait and density; the old device switch is a one-time default.
            Test("world-rules-days-density-source-and-persistence",()=>{
                var f=World();BlocksManager.Blocks[0]=new AirBlock{IsCollidable=false};BlocksManager.Blocks[2]=new DirtBlock{BlockIndex=2,IsCollidable=true};
                // current-direction-20260929: the suitability search places the whole squad, so the fixture has a ground patch.
                var chunk=f.Terrain.Terrain.AllocateChunk(0,0);chunk.State=TerrainChunkState.Valid;for(int x=1;x<16;x++)for(int z=1;z<16;z++){f.Terrain.Terrain.SetCellValueFast(x,60,z,2);f.Terrain.Terrain.SetTopHeight(x,z,60);}
                var point=f.Spawn.ProcessSpawnPoint(new Point3(8,60,8),SpawnLocationType.Surface);f.Director.Register();var registered=f.Spawn.m_creatureTypes.Single(c=>c.Name=="ScTacticalEnemy");
                bool Suitable()=>registered.SpawnSuitabilityFunction(registered,point.Value)>0;
                Subsystem NewRules(ValuesDictionary v){var r=(Subsystem)Activator.CreateInstance(T("SubsystemTacticalEnemyRules"));r.m_project=f.P;r.Load(v);return r;}
                var natural=C("ScUiSettings").GetField("NaturalEnemies");natural.SetValue(null,false);Subsystem rules;
                try{rules=NewRules(new ValuesDictionary());}finally{natural.SetValue(null,true);}
                f.P.m_subsystems.Add(rules);dynamic live=rules;
                Check(!(bool)live.Rules.Natural&&(bool)live.FromDefaults,"old device 'off' not carried into an uninitialised world");
                double day=f.P.FindSubsystem<SubsystemTimeOfDay>(true).DayDuration;f.Info.TotalElapsedGameTime=40*day;Check(!Suitable(),"switched-off world spawned");
                object Rules(bool on,int days,int density)=>Activator.CreateInstance(C("ScEnemyRules"),[on,days,Enum.ToObject(C("ScEnemyDensity"),density)]);
                void Set(object r)=>T("SubsystemTacticalEnemyRules").GetMethod("Set").Invoke(rules,[r]);
                Set(Rules(true,30,1));
                foreach(var (days,ok) in new[]{(29.999,false),(30.0,true),(30.001,true)}){f.Info.TotalElapsedGameTime=days*day;Check(Suitable()==ok,"wait boundary wrong at day "+days);}
                var remaining=C("ScEnemySpawnPolicy").GetMethod("RemainingSeconds");Check((double)remaining.Invoke(null,[29.5*day,day,30])==.5*day&&(double)remaining.Invoke(null,[31*day,day,30])==0,"displayed wait disagrees with the gate");
                Set(Rules(true,0,1));f.Info.TotalElapsedGameTime=0;Check(Suitable(),"0-day wait not immediate");
                Set(Rules(true,0,0));var extra=new List<dynamic>();
                for(int i=0;i<3;i++){var e=Enemy(f.P);e.Creature.ComponentBody.Position=new Vector3(2000+i*3,60,0);f.Director.OnEntityAdded(e.Creature.Entity);extra.Add(e.Enemy);}
                Check(!Suitable(),"sparse cap (5) exceeded by 3 existing + 3 new");
                foreach(var e in extra)e.State.Source="manual";Check(Suitable(),"manual challenge squads fill the natural cap");
                foreach(var e in extra)e.State.Source=null;Set(Rules(true,0,2));Check(Suitable(),"dense cap (15) refused 6 members");
                var saved=new ValuesDictionary();rules.Save(saved);
                for(int round=0;round<2;round++){var back=NewRules(Round(saved));dynamic b=back;Check((bool)b.Rules.Natural&&(int)b.Rules.GraceDays==0&&(int)b.Rules.Density==2&&!(bool)b.FromDefaults,"world rules lost on save/load "+round);saved=new ValuesDictionary();back.Save(saved);}
                var bad=Round(saved);bad.SetValue("GraceDays",999);bad.SetValue("Density",7);dynamic clamp=NewRules(bad);Check((int)clamp.Rules.GraceDays==365&&(int)clamp.Rules.Density==1,"out-of-range rules not normalised");
                var future=Round(saved);future.SetValue("Schema",2);bool refused=false;try{NewRules(future);}catch{refused=true;}Check(refused,"future rules schema accepted");
            });
            Test("30-elapsed-days-setting-native-surface-mode-population-and-sleeping-squad",()=>{
                var f=World();BlocksManager.Blocks[0]=new AirBlock{IsCollidable=false};BlocksManager.Blocks[2]=new DirtBlock{BlockIndex=2,IsCollidable=true};
                var chunk=f.Terrain.Terrain.AllocateChunk(0,0);chunk.State=TerrainChunkState.Valid;for(int x=1;x<16;x++)for(int z=1;z<16;z++){f.Terrain.Terrain.SetCellValueFast(x,60,z,2);f.Terrain.Terrain.SetTopHeight(x,z,60);}
                var point=f.Spawn.ProcessSpawnPoint(new Point3(8,60,8),SpawnLocationType.Surface);Check(point==new Point3(8,61,8),"unexpected native spawn convention: "+point);f.Director.Register();f.Director.Register();var registered=f.Spawn.m_creatureTypes.Single(c=>c.Name=="ScTacticalEnemy");
                bool Suitable()=>registered.SpawnSuitabilityFunction(registered,point.Value)>0;
                Check(!Suitable(),"natural enemies spawned before grace period");
                double daySeconds=f.P.FindSubsystem<SubsystemTimeOfDay>(true).DayDuration;
                f.Info.TotalElapsedGameTime=30*daySeconds;
                Check(Suitable(),"native first-free-cell rejected after grace period");
                var setting=C("ScUiSettings").GetField("NaturalEnemies");setting.SetValue(null,false);
                try{Check(!Suitable(),"disabled natural spawning ignored");}finally{setting.SetValue(null,true);}
                foreach(var mode in new[]{GameMode.Creative,GameMode.Harmless}){f.Info.WorldSettings.GameMode=mode;Check(!Suitable(),"peaceful mode spawned enemies");}f.Info.WorldSettings.GameMode=GameMode.Survival;
                f.Info.WorldSettings.EnvironmentBehaviorMode=EnvironmentBehaviorMode.Static;Check(!Suitable(),"static ecology ignored");f.Info.WorldSettings.EnvironmentBehaviorMode=EnvironmentBehaviorMode.Living;
                Check(((Array)T("SubsystemTacticalEnemies").GetMethod("Roles").Invoke(null,[29,30])).Length==3&&((Array)T("SubsystemTacticalEnemies").GetMethod("Roles").Invoke(null,[30,30])).Length==5,"day boundary");
                var saved=new SpawnEntityData{TemplateName="ScTacticalEnemy",Position=new Vector3(20,61,8)};f.Sleeping.GetOrCreateSpawnChunk(new Point2(1,0)).SpawnsData.Add(saved);Check(!Suitable(),"spawned over unloaded squad");f.Sleeping.m_chunks.Clear();
                f.Sleeping.m_spawnEntityDatas[9]=saved;Check(Suitable(),"stale engine entity cache permanently prevents new encounters");
                f.Director.MaxActive=5;for(int i=0;i<3;i++){var e=Enemy(f.P);e.Creature.ComponentBody.Position=new Vector3(1000+i*3,60,0);f.Director.OnEntityAdded(e.Creature.Entity);}Check(!Suitable(),"partial squad exceeds active cap");
            });
            // video-feedback-20260929 S0: the whole natural chain - native periodic scheduler -> weighted candidate ->
            // CS suitability -> whole-squad commit - not the manual beacon and not a single gate function.
            var templateType=new DatabaseObjectType("EntityTemplate","","",0,false,false,256,false);templateType.InitializeRelations(null,null,null);
            // The native scheduler reads every body's template name (type counts); fixture entities get one.
            Entity Named(Entity e,string template){var v=new ValuesDictionary();v.m_databaseObject=new DatabaseObject(templateType,template);e.m_valuesDictionary=v;return e;}
            NaturalWorld Natural(int seed,int density=2){
                var f=World();var p=(SpawnProject)f.P;
                BlocksManager.Blocks[0]=new AirBlock{IsCollidable=false};BlocksManager.Blocks[2]=new DirtBlock{BlockIndex=2,IsCollidable=true};
                BlocksManager.Blocks[61]=new SnowBlock{BlockIndex=61,IsCollidable=false};BlocksManager.Blocks[12]=new OakLeavesBlock{BlockIndex=12,IsCollidable=true};
                BlocksManager.Blocks[19]=new TallGrassBlock{BlockIndex=19,IsCollidable=false};BlocksManager.Blocks[9]=new OakWoodBlock{BlockIndex=9,IsCollidable=true};BlocksManager.Blocks[3]=new GraniteBlock{BlockIndex=3,IsCollidable=true};
                BlocksManager.Blocks[18]=new WaterBlock{BlockIndex=18,IsCollidable=false};BlocksManager.Blocks[127]=new CactusBlock{BlockIndex=127,IsCollidable=true};BlocksManager.Blocks[104]=new FireBlock{BlockIndex=104,IsCollidable=false};
                for(int cx=0;cx<8;cx++)for(int cz=0;cz<8;cz++)f.Terrain.Terrain.AllocateChunk(cx,cz).State=TerrainChunkState.Valid;
                for(int x=0;x<128;x++)for(int z=0;z<128;z++){f.Terrain.Terrain.SetCellValueFast(x,60,z,2);f.Terrain.Terrain.SetTopHeight(x,z,60);}
                var player=Blank<ComponentPlayer>();player.ComponentBody=new ComponentBody{Position=new Vector3(64.5f,61,64.5f),BoxSize=new Vector3(.65f,1.8f,.65f)};
                var widget=Blank<GameWidget>();widget.m_activeCamera=new Eye(new Vector3(64.5f,62.6f,64.5f),Vector3.UnitX);var data=Blank<PlayerData>();data.m_gameWidget=widget;player.PlayerData=data;
                Named(E(p,player,player.ComponentBody),"MalePlayer");p.FindSubsystem<SubsystemPlayers>(true).m_componentPlayers.Add(player);f.Bodies.AddBody(player.ComponentBody);
                var views=Blank<SubsystemGameWidgets>();views.m_gameWidgets=[widget];
                SubsystemCreatureSpawn spawn=f.Spawn;spawn.m_subsystemViews=views;spawn.m_subsystemGameInfo=f.Info;spawn.m_subsystemTime=f.Time;spawn.m_subsystemSeasons=new SubsystemSeasons();spawn.m_random=new Game.Random(seed);
                var rules=(Subsystem)Activator.CreateInstance(T("SubsystemTacticalEnemyRules"));rules.m_project=p;rules.Load(new ValuesDictionary());p.m_subsystems.Add(rules);
                T("SubsystemTacticalEnemyRules").GetMethod("Set").Invoke(rules,[Activator.CreateInstance(C("ScEnemyRules"),[true,0,Enum.ToObject(C("ScEnemyDensity"),density)])]);
                f.Director.Register();
                int created=0;p.Factory=()=>{created++;var e=Named(Enemy(p).Creature.Entity,"ScTacticalEnemy");p.m_entities.Remove(e);e.m_isAddedToProject=false;return e;};
                p.Added=e=>{f.Director.OnEntityAdded(e);f.Bodies.AddBody(e.FindComponent<ComponentBody>(true));};p.Removed=e=>{f.Director.OnEntityRemoved(e);f.Bodies.RemoveBody(e.FindComponent<ComponentBody>(true));};
                return new NaturalWorld{P=p,Director=f.Director,Time=f.Time,Terrain=f.Terrain,Bodies=f.Bodies,Info=f.Info,Spawn=f.Spawn,Player=player,Rules=rules,Created=()=>created};
            }
            // Game time advances in 30 s steps; the native periodic event (every 60 s) and the CS director both run.
            void Minutes(NaturalWorld f,int minutes,Func<bool> stop=null){SubsystemTime time=f.Time;SubsystemCreatureSpawn spawn=f.Spawn;
                for(int i=0;i<minutes*2&&stop?.Invoke()!=true;i++){time.m_gameTime+=30;time.m_gameTimeDelta=30;spawn.Update(30);f.Director.Update(30f);}}
            ComponentCreature Animal(SpawnProject p,NaturalWorld f,Vector3 at){var body=new ComponentBody{Position=at,BoxSize=new Vector3(.8f)};var creature=new ComponentCreature{ComponentBody=body,ComponentHealth=new ComponentHealth{Health=1},ConstantSpawn=false};Named(E(p,body,creature,creature.ComponentHealth),"Fixture_Animal");f.Bodies.AddBody(body);return creature;}
            void SetCooldown(NaturalWorld f,float seconds)=>Set((object)f.Director,"spawnCooldown",seconds);
            string Progress(Subsystem rules,NaturalWorld f)=>(string)T("SubsystemTacticalEnemyRules").GetMethod("Ready").Invoke(null,[(object)f.Director,((dynamic)rules).Rules]);
            void WithLimits(int total,int area,Action body){int oldTotal=SubsystemCreatureSpawn.m_totalLimit,oldArea=SubsystemCreatureSpawn.m_areaLimit;
                var oldTemplate=DatabaseManager.m_valueDictionaries.GetValueOrDefault("ScTacticalEnemy");DatabaseManager.m_valueDictionaries["ScTacticalEnemy"]=new ValuesDictionary();
                try{SubsystemCreatureSpawn.m_totalLimit=total;SubsystemCreatureSpawn.m_areaLimit=area;body();}
                finally{SubsystemCreatureSpawn.m_totalLimit=oldTotal;SubsystemCreatureSpawn.m_areaLimit=oldArea;if(oldTemplate==null)DatabaseManager.m_valueDictionaries.Remove("ScTacticalEnemy");else DatabaseManager.m_valueDictionaries["ScTacticalEnemy"]=oldTemplate;}}
            void BudgetTooSmall(string label,int limit){
                foreach(int animals in new[]{0,1})foreach(bool five in new[]{false,true}){
                    var n=Natural(7+animals);var f=n;int squad=five?5:3;
                    if(five)f.Info.TotalElapsedGameTime=31*f.P.FindSubsystem<SubsystemTimeOfDay>(true).DayDuration;
                    for(int i=0;i<animals;i++)Animal(n.P,f,new Vector3(8+i*4,61,8));
                    int entities=n.P.Entities.Count;var sink=new SpawnLog();Log.AddLogSink(sink);
                    try{Minutes(f,600);}finally{Log.RemoveLogSink(sink);}
                    Check(n.Created()==0&&n.P.Entities.Count==entities&&((System.Collections.IEnumerable)f.Director.Enemies).Cast<object>().Count()==0,$"{label}: limit {limit} with {animals} animals created {n.Created()} members or left partial entities");
                    Check(SubsystemCreatureSpawn.m_totalLimit==limit,$"{label}: CS changed the global creature limit to {SubsystemCreatureSpawn.m_totalLimit}");
                    // The player is a creature too: with one more animal the native scheduler is idle and never asks CS.
                    int present=1+animals;string blocker=f.Director.BlockerFor(((dynamic)n.Rules).Rules).ToString();
                    Check(blocker=="BudgetImpossible",$"{label}: blocker {blocker} instead of BudgetImpossible");
                    string text=Progress(n.Rules,f);
                    Check(text.Contains($"总上限为 {limit}")&&text.Contains($"{squad} 人小队")&&text.Contains("原版默认 26")&&!text.Contains("规则允许生成"),$"{label}: settings text hides the blocker: {text}");
                    Check(sink.Lines.Count is >0 and <=300&&sink.Lines.Any(l=>l.Contains("blocker=BudgetImpossible")&&l.Contains($"budget={present}+{squad}/{limit}")),$"{label}: no bounded status line names the budget: {string.Join(" | ",sink.Lines.Take(2))}");
                    bool asked=sink.Lines.Any(l=>l.Contains("budget-impossible="));
                    Check(present>=limit?!asked&&sink.Lines.All(l=>l.Contains("offeredByNative=0")&&l.Contains("offered CS no candidate")):asked,$"{label}: native offers misreported with {present} creatures under limit {limit}: {string.Join(" | ",sink.Lines.Take(2))}");
                }
            }
            Test("natural-native-scheduler-whole-squad-under-vanilla-budget",()=>WithLimits(26,3,()=>{
                {
                    // Release 1.4.0: with the diagnostics off a squad still appears, and no spawn status or squad line is written.
                    Check(diagnostics!=null,"the core has no KnifeLog.Diagnostics switch");diagnostics.SetValue(null,false);var lines=new List<string>();var any=new LineLog(lines);Log.AddLogSink(any);
                    try{var n=Natural(1);var f=n;for(int minutes=0;minutes<3000&&n.Created()==0;minutes++)Minutes(f,1,()=>n.Created()>0);Minutes(f,3);
                        Check(n.Created()==3,"no squad with the diagnostics off");
                        Check(!lines.Any(l=>l.Contains("natural status")||l.Contains("natural squad")||l.Contains("naturalEnabled")),"spawn diagnostics in a player's log: "+string.Join(" | ",lines.Take(3)));}
                    finally{Log.RemoveLogSink(any);diagnostics.SetValue(null,true);}
                }
                var waits=new List<int>();
                foreach(int seed in new[]{1,2,3,4,5,6,7,8}){
                    var n=Natural(seed);var f=n;var sink=new SpawnLog();Log.AddLogSink(sink);int minutes=0;
                    // Stop inside the minute the squad appears: the dense cooldown (40 s) is shorter than the native period (60 s).
                    try{for(;minutes<3000&&n.Created()==0;minutes++)Minutes(f,1,()=>n.Created()>0);}finally{Log.RemoveLogSink(sink);}
                    Check(n.Created()==3,$"seed {seed}: no whole three-member squad within 3000 game minutes (created {n.Created()})");waits.Add(minutes);
                    var members=((System.Collections.IEnumerable)f.Director.Enemies).Cast<dynamic>().ToArray();
                    // current-direction-20260929 §2: 28-50 blocks away (anchor 32-44), out of the open view on this flat ground (the
                    // sides are free), a three-second warning, and a patrol home 18-26 blocks from where the player stood.
                    Check(members.Length==3&&members.Select(e=>(string)e.State.Squad).Distinct().Count()==1&&members.All(e=>(string)e.State.Source=="natural"&&(float)e.State.Warmup==3f),"natural squad not whole, not one squad, not tagged natural or without its warning");
                    var eye=(Camera)n.Player.GameWidget.ActiveCamera;var at=n.Player.ComponentBody.Position;
                    Check(members.All(e=>Vector3.Distance((Vector3)e.Creature.ComponentBody.Position,at) is >=28 and <=50&&!eye.ViewFrustum.Intersection(new BoundingSphere((Vector3)e.Creature.ComponentBody.Position+Vector3.UnitY,1))),"natural member outside 28-50 blocks or inside the player's open view on open ground");
                    Check(members.All(e=>Vector2.Distance(((Vector3)e.Home).XZ,at.XZ) is >=14 and <=30),"natural squad has no patrol home near the encounter zone: "+string.Join(",",members.Select(e=>Vector2.Distance(((Vector3)e.Home).XZ,at.XZ).ToString("0.0"))));
                    dynamic seen=((System.Collections.IEnumerable)f.Director.Encounters).Cast<object>().Last();Check((float)seen.Distance is >=32 and <=44&&!(bool)seen.InView&&(bool)seen.Patrols,"encounter record: distance "+seen.Distance);
                    Check(f.Director.BlockerFor(((dynamic)n.Rules).Rules).ToString()=="Cooldown"&&Progress(n.Rules,f).Contains("最短间隔"),"cooldown after a squad not reported");
                    Log.AddLogSink(sink);try{Minutes(f,3);}finally{Log.RemoveLogSink(sink);}Check(sink.Lines.Any(l=>System.Text.RegularExpressions.Regex.IsMatch(l,"squadsCreated=[1-9]")),"created squad missing from the status log: "+string.Join(" | ",sink.Lines.TakeLast(3)));
                    // Later squads respect spacing/cap/budget and stay whole.
                    Minutes(f,600);int count=((System.Collections.IEnumerable)f.Director.Enemies).Cast<object>().Count();
                    Check(count%3==0&&count<=15&&n.Created()==count,"later natural squads partial or above the dense cap: "+count);
                }
                waits.Sort();Console.WriteLine($"[natural-chain] vanilla budget, CS the only surface candidate, flat open ground: game minutes to the first squad over 8 seeds: min {waits[0]}, median {waits[4]}, max {waits[^1]} (native period 60 s; no third-party rate gate)");
            }));
            Test("natural-budget-smaller-than-squad-reports-blocker-and-never-splits",()=>WithLimits(2,1,()=>BudgetTooSmall("simulated limit 2",2)));
            Test("natural-budget-full-then-free-and-chunk-path",()=>WithLimits(4,3,()=>{
                var n=Natural(3);var f=n;var animals=new[]{Animal(n.P,f,new Vector3(8,61,8)),Animal(n.P,f,new Vector3(12,61,8))};
                Check(f.Director.BlockerFor(((dynamic)n.Rules).Rules).ToString()=="BudgetFull"&&Progress(n.Rules,f).Contains("暂时容纳不下 3 人小队"),"full budget (3 creatures of 4) not reported");
                Minutes(f,300);Check(n.Created()==0,"squad created above the global budget");
                foreach(var a in animals){f.Bodies.RemoveBody(a.ComponentBody);n.P.m_entities.Remove(a.Entity);}
                Check(f.Director.BlockerFor(((dynamic)n.Rules).Rules).ToString()=="None"&&Progress(n.Rules,f).Contains("规则允许生成"),"freed budget still reported as blocked");
                // Newly visited chunk path of the native scheduler (ten attempts per chunk), behind the player.
                SubsystemCreatureSpawn spawn=f.Spawn;for(int round=0;round<400&&n.Created()==0;round++)spawn.SpawnChunkCreatures(new SpawnChunk{Point=new Point2(1,round%8)},10,false);
                Check(n.Created()==3,"native chunk path never produced a whole squad under a budget of 4");
            }));
            // current-direction-20260929 §2: WHERE a natural squad appears. The native offer only decides that one may; CS picks
            // a safe standing place 32-44 blocks from the nearest player, out of sight first (sides, behind cover), else in open
            // view no nearer than 32; the refusals of a search that finds nothing are itemised.
            TestD("natural-placement-band-cover-view-patrol-and-refusals",()=>{var detail=new List<string>();WithLimits(26,3,()=>{
                var n=Natural(5);var f=n;var t=(Terrain)f.Terrain.Terrain;SubsystemCreatureSpawn spawn=f.Spawn;var registered=spawn.m_creatureTypes.Single(c=>c.Name=="ScTacticalEnemy");
                Set((object)f.Director,"random",new Engine.Random(5));var player=n.Player.ComponentBody.Position;var eye=(Camera)n.Player.GameWidget.ActiveCamera;
                var planMethod=T("SubsystemTacticalEnemies").GetMethod("PlanNatural");string refusal=null;
                dynamic Plan(Point3 support){f.Time.m_gameTime+=1;object[] a=[support,3,null];var r=planMethod.Invoke((object)f.Director,a);refusal=(string)a[2];return r;}
                bool Open(Vector3 p)=>eye.ViewFrustum.Intersection(new BoundingSphere(p+Vector3.UnitY,1))&&!f.Terrain.Blocked;
                void Band(dynamic plan,string what){Check(plan is not null,what+": no place ("+refusal+")");float d=Vector2.Distance(((Vector3)plan.Anchor).XZ,player.XZ);
                    Check(d is >=32 and <=44.5f,$"{what}: anchor {d:0.0} blocks away");foreach(Vector3 m in plan.Members)Check(Vector3.Distance(m,player)>=28&&Vector3.Distance(m,player)<=50&&(!Open(m)||Vector3.Distance(m,player)>=32),$"{what}: member at {Vector3.Distance(m,player):0.0} blocks, open {Open(m)}");}
                // Ahead in plain sight: the squad goes to the side, out of the view.
                var ahead=Plan(new Point3(100,60,64));Band(ahead,"offer ahead");Check(!(bool)ahead.InView&&Math.Abs(((Vector3)ahead.Anchor).Z-player.Z)>20,"an offer in plain sight was not moved out of the view: "+ahead.Anchor);
                Check(ahead.Patrol is Vector3 walk&&Math.Abs(Vector2.Distance(walk.XZ,player.XZ)-18)<1.5f,"no patrol point 18 blocks short of the player: "+ahead.Patrol);
                detail.Add($"offer ahead in view -> side {Vector2.Distance(((Vector3)ahead.Anchor).XZ,player.XZ):0.0} blocks, out of view, patrol to 18");
                // Behind the player: the offered direction itself is out of view and is kept.
                var behind=Plan(new Point3(20,60,64));Band(behind,"offer behind");Check(((Vector3)behind.Anchor).X<player.X-30,"an offer behind the player was not kept in its direction: "+behind.Anchor);
                // Cover ahead (every sight line blocked): the offered direction is out of sight and kept.
                f.Terrain.Blocked=true;var covered=Plan(new Point3(100,60,64));f.Terrain.Blocked=false;Band(covered,"offer ahead behind cover");Check(((Vector3)covered.Anchor).X>player.X+30&&!(bool)covered.InView,"cover ahead not used: "+covered.Anchor);
                // Only a strip ahead is dry: the squad appears in open view, no nearer than 32 blocks, with the warning.
                for(int x=0;x<128;x++)for(int z=0;z<128;z++){float d=Vector2.Distance(new Vector2(x+.5f,z+.5f),player.XZ);bool strip=x>=95&&x<=112&&z>=56&&z<=72;if(d>=20&&d<=56&&!strip){t.SetCellValueFast(x,60,z,18);}}
                var open=Plan(new Point3(100,60,64));Band(open,"only ahead dry");Check((bool)open.InView,"the in-view fallback was not used");
                int made=n.Created();Check(registered.SpawnFunction(registered,new Point3(100,61,64))==3&&n.Created()==made+3,"no squad in the in-view fallback");
                var squad=((System.Collections.IEnumerable)f.Director.Enemies).Cast<dynamic>().ToArray();Check(squad.All(e=>(float)e.State.Warmup==3f&&Vector3.Distance((Vector3)e.Creature.ComponentBody.Position,player)>=32),"an in-view squad without its warning or nearer than 32");
                dynamic record=((System.Collections.IEnumerable)f.Director.Encounters).Cast<object>().Last();Check((bool)record.InView,"the encounter record does not say in view");
                detail.Add($"only ahead dry -> in open view at {Vector2.Distance(((Vector3)open.Anchor).XZ,player.XZ):0.0}, warning 3 s");
                // Nothing dry within reach: refused, itemised by the best candidate's reason.
                foreach(var e in squad)n.P.RemoveEntity((Entity)e.Creature.Entity,false);
                for(int x=95;x<=112;x++)for(int z=56;z<=72;z++)t.SetCellValueFast(x,60,z,18);
                Check(Plan(new Point3(100,60,64))==null&&refusal=="placement:fluid","an all-water band was not refused as fluid: "+refusal);
                f.Time.m_gameTime+=200;f.Director.Update(0f);var before=((IReadOnlyDictionary<string,int>)f.Director.RecentRefusals).GetValueOrDefault("placement:fluid");
                SetCooldown(f,0);Check(registered.SpawnSuitabilityFunction(registered,new Point3(100,61,64))==0,"an all-water band was suitable");
                f.Time.m_gameTime+=200;f.Director.Update(0f);Check(((IReadOnlyDictionary<string,int>)f.Director.RecentRefusals).GetValueOrDefault("placement:fluid")==before+1,"the refusal was not itemised");
                detail.Add("all-water band refused as placement:fluid (itemised)");
            });
            WithLimits(26,3,()=>{
                // Footing and member rules on one candidate (grass, snow, canopy, roof, overhang, fluid, hazard, walled, uneven).
                var n=Natural(6);var f=n;var t=(Terrain)f.Terrain.Terrain;
                (string Reason,Vector3 At,int Support) Footing(int x,int nearY,int z){object[] a=[x,nearY,z,null,null];var r=(string)T("SubsystemTacticalEnemies").GetMethod("Footing").Invoke((object)f.Director,a);return(r,(Vector3)a[3],(int)a[4]);}
                var candidate=T("SubsystemTacticalEnemies").GetMethod("NaturalCandidate",Fields);
                string Candidate(int x,int z){object[] a=[x,z,3,n.Player.ComponentBody.Position,null];var r=candidate.Invoke((object)f.Director,a);return r is null?(string)a[4]:null;}
                t.SetCellValueFast(20,61,20,61);var snow=Footing(20,60,20);Check(snow.Reason==null&&snow.Support==60&&Math.Abs(snow.At.Y-61.01f)<.02f,$"snow footing {snow}");
                t.SetCellValueFast(22,61,20,19);Check(Footing(22,60,20).Reason==null,"tall grass refused");
                t.SetCellValueFast(24,66,24,12);t.SetTopHeight(24,24,66);var canopy=Footing(24,66,24);Check(canopy.Reason==null&&canopy.Support==60,"ground under a canopy not found: "+canopy);
                t.SetCellValueFast(36,64,36,3);t.SetTopHeight(36,36,64);Check(Footing(36,60,36).Reason=="under-cover","standing under a stone roof accepted");
                for(int y=62;y<=64;y++)t.SetCellValueFast(40,y,40,3);t.SetTopHeight(40,40,64);Check(Footing(40,60,40).Reason=="headroom","a low overhang accepted");
                t.SetCellValueFast(44,61,44,18);Check(Footing(44,60,44).Reason=="fluid","standing in water accepted");
                t.SetCellValueFast(48,60,48,127);Check(Footing(48,60,48).Reason=="hazard","standing on a cactus accepted");
                t.SetCellValueFast(50,61,50,104);Check(Footing(50,60,50).Reason=="hazard","standing in fire accepted");
                foreach(var (dx,dz) in new[]{(3,0),(-3,0),(0,3),(0,-3),(3,3),(-3,-3)}){for(int y=61;y<=66;y++)t.SetCellValueFast(20+dx,y,110+dz,2);t.SetTopHeight(20+dx,110+dz,66);}
                Check(Candidate(20,110)=="squad-placement:headroom","members walled in: "+Candidate(20,110));
                foreach(var (dx,dz) in new[]{(3,0),(-3,0),(0,3),(0,-3),(3,3),(-3,-3)}){for(int y=55;y<=60;y++)t.SetCellValueFast(28+dx,y,100+dz,0);t.SetCellValueFast(28+dx,54,100+dz,2);t.SetCellValueFast(28+dx,60,100+dz,12);t.SetTopHeight(28+dx,100+dz,60);}
                Check(Candidate(28,100)=="squad-placement:uneven","members far below a canopy: "+Candidate(28,100));
                for(int x=6;x<=14;x++)for(int z=74;z<=82;z++){t.SetCellValueFast(x,65,z,12);t.SetTopHeight(x,z,65);}Check(Candidate(10,78)==null,"a whole squad under a canopy refused: "+Candidate(10,78));
                Animal(n.P,f,new Vector3(30.5f,61.1f,20.5f));Check(Candidate(30,20)=="placement:occupied","an occupied candidate accepted: "+Candidate(30,20));
                detail.Add("footing: snow, grass, under canopy accepted; roof, overhang, water, cactus, fire refused; walled and uneven members, occupied anchor refused");
            });
            WithLimits(26,3,()=>{
                // How far native offers are (34-68, the engine puts creatures past 60 to sleep) and where squads now stand.
                var n=Natural(9);var f=n;var player=n.Player.ComponentBody.Position;var r=new Game.Random(2026);int beyond=0,count=200;var distances=new List<float>();
                Set((object)f.Director,"random",new Engine.Random(9));var planMethod=T("SubsystemTacticalEnemies").GetMethod("PlanNatural");
                for(int i=0;i<count;i++){int x=(int)player.X+r.Sign()*r.Int(24,48),z=(int)player.Z+r.Sign()*r.Int(24,48);if(new Vector2(x+.5f-player.X,z+.5f-player.Z).Length()>60)beyond++;
                    f.Time.m_gameTime+=1;object[] a=[new Point3(Math.Clamp(x,1,126),60,Math.Clamp(z,1,126)),3,null];dynamic plan=planMethod.Invoke((object)f.Director,a);Check(plan is not null,"no place for an offer: "+a[2]);
                    foreach(Vector3 m in plan.Members){float d=Vector2.Distance(m.XZ,player.XZ);distances.Add(d);Check(d<=49,"a member beyond 49 blocks: "+d);}}
                detail.Add($"native offers beyond the 60-block sleep radius: {beyond}/{count} (old placement put those squads to sleep at once); members now {distances.Min():0.0}-{distances.Max():0.0} blocks");
            });
            WithLimits(26,3,()=>{
                // The encounter chain on the fixture: a natural squad walks its patrol leg (placed at its home here: the fixture
                // has no physics), notices and engages the player; one that sleeps first is logged as slept unmet.
                var n=Natural(3);var f=n;var sink=new SpawnLog();Set((object)f.Director,"random",new Engine.Random(3));
                SubsystemCreatureSpawn spawn=f.Spawn;var registered=spawn.m_creatureTypes.Single(c=>c.Name=="ScTacticalEnemy");
                var lines=new List<string>();var logSink=new LineLog(lines);Log.AddLogSink(logSink);
                try{
                    Check(registered.SpawnFunction(registered,new Point3(100,61,64))==3,"no natural squad");
                    var members=((System.Collections.IEnumerable)f.Director.Enemies).Cast<dynamic>().ToArray();var player=n.Player.ComponentBody;
                    var alive=new ComponentHealth{Health=1};alive.m_entity=n.Player.Entity;n.Player.Entity.m_components.Add(alive); // a target must be alive
                    foreach(var m in members){var b=(ComponentBody)m.Creature.ComponentBody;b.Position=(Vector3)m.Home;var d=(player.Position-b.Position).XZ;b.Rotation=Quaternion.CreateFromYawPitchRoll(MathF.Atan2(-d.X,-d.Y),0,0);m.State.Warmup=0f;}
                    // mpc3-feedback-subworld-20261002 (N1/N2): facing the visible player from its patrol home the natural squad
                    // stays neutral; the player attacks one member, and the squad turns on that player and fires.
                    for(int i=0;i<40;i++){f.Time.m_gameTime+=.25;foreach(var m in members)m.Update(.25f);}
                    dynamic quiet=((System.Collections.IEnumerable)f.Director.Encounters).Cast<object>().Last();
                    Check((double)quiet.Seen<0&&(double)quiet.Engaged<0&&members.All(m=>m.TargetBody==null),$"a natural squad engaged a player it merely faced: seen {quiet.Seen}, engaged {quiet.Engaged}");
                    if(n.Player.Entity.FindComponent<ComponentBody>() is null)n.Player.Entity.m_components.Add(player);
                    ComponentBody struck=members[0].Creature.ComponentBody;
                    ((ModLoader)Activator.CreateInstance(T("TacticalModLoader"))).ProcessAttackment(Strike(struck,n.Player.Entity,2));
                    Check(members.All(m=>ReferenceEquals((ComponentBody)m.TargetBody,player)),"the attacked natural squad did not all take the attacking player");
                    for(int i=0;i<40&&((System.Collections.IEnumerable)f.Director.Encounters).Cast<dynamic>().Last().Engaged<0;i++){f.Time.m_gameTime+=.25;foreach(var m in members)m.Update(.25f);}
                    dynamic e=((System.Collections.IEnumerable)f.Director.Encounters).Cast<object>().Last();
                    Check((double)e.Seen>=0&&(double)e.Engaged>=(double)e.Seen,$"the attacked squad at its patrol home did not turn on/engage the player: seen {e.Seen}, engaged {e.Engaged}");
                    detail.Add($"at the patrol home: neutral for 10 s in view of the player; attacked, turned on the player {(double)e.Seen-(double)e.Created:0.0} s and fired {(double)e.Engaged-(double)e.Created:0.0} s after appearing");
                    f.Time.m_gameTime+=400;SetCooldown(f,0);foreach(var m in members)n.P.RemoveEntity((Entity)m.Creature.Entity,false);
                    Check(registered.SpawnFunction(registered,new Point3(20,61,64))==3,"no second natural squad");
                    var second=((System.Collections.IEnumerable)f.Director.Enemies).Cast<dynamic>().First();n.P.RemoveEntity((Entity)second.Creature.Entity,false);
                    dynamic slept=((System.Collections.IEnumerable)f.Director.Encounters).Cast<object>().Last();Check((double)slept.Slept>=0&&(double)slept.Seen<0,"a squad put to sleep before any encounter was not recorded");
                    Check(lines.Count(l=>l.Contains("natural squad"))is >=4 and <=12&&lines.Any(l=>l.Contains("first saw a player"))&&lines.Any(l=>l.Contains("before any encounter")),"bounded encounter log lines missing: "+string.Join(" | ",lines.Where(l=>l.Contains("natural squad"))));
                    f.Time.m_gameTime+=200;Log.AddLogSink(sink);try{f.Director.Update(0f);}finally{Log.RemoveLogSink(sink);}
                    Check(sink.Lines.Any(l=>l.Contains("encounters: last 2: seen 1, engaged 1, slept unmet 1")),"status line without the encounter summary: "+string.Join(" | ",sink.Lines));
                }finally{Log.RemoveLogSink(logSink);}
                detail.Add("encounter log: created / first saw / first fired / slept unmet, and the 2-minute summary");
            });return string.Join("; ",detail);});
            if(Environment.GetEnvironmentVariable("SC_SLOWER_CHECK_PACKAGE") is {Length:>0} slower)Test("natural-installed-slower-package-limits-read-only",()=>{
                Check(File.Exists(slower),"installed package not found: "+slower);
                int oldTotal=SubsystemCreatureSpawn.m_totalLimit,oldArea=SubsystemCreatureSpawn.m_areaLimit,oldConstant=SubsystemCreatureSpawn.m_totalLimitConstant,oldChallenging=SubsystemCreatureSpawn.m_totalLimitConstantChallenging,oldAreaConstant=SubsystemCreatureSpawn.m_areaLimitConstant;
                var oldTemplate=DatabaseManager.m_valueDictionaries.GetValueOrDefault("ScTacticalEnemy");DatabaseManager.m_valueDictionaries["ScTacticalEnemy"]=new ValuesDictionary();
                var libraries=new Dictionary<string,Assembly>();ResolveEventHandler resolve=(_,e)=>libraries.GetValueOrDefault(new AssemblyName(e.Name).Name);AppDomain.CurrentDomain.AssemblyResolve+=resolve;
                try{
                    byte[] original=File.ReadAllBytes(slower);
                    using(var zip=new System.IO.Compression.ZipArchive(new MemoryStream(original),System.IO.Compression.ZipArchiveMode.Read))foreach(var entry in zip.Entries.Where(e=>e.Name.EndsWith(".dll",StringComparison.OrdinalIgnoreCase))){using var stream=entry.Open();using var bytes=new MemoryStream();stream.CopyTo(bytes);var a=Assembly.Load(bytes.ToArray());libraries[a.GetName().Name]=a;}
                    var type=libraries.Values.SelectMany(a=>{try{return a.GetTypes();}catch(ReflectionTypeLoadException e){return e.Types.Where(x=>x!=null);}}).Single(x=>typeof(ModLoader).IsAssignableFrom(x)&&!x.IsAbstract);
                    // Only its own limit hook, as the game calls it after loading. No Harmony patching, no world, no install;
                    // its 0.1x random gate therefore stays outside this test (it changes frequency, not feasibility).
                    ((ModLoader)Activator.CreateInstance(type)).OnLoadingFinished(new List<Action>());
                    int limit=SubsystemCreatureSpawn.m_totalLimit;Console.WriteLine($"[natural-chain] installed package {Path.GetFileName(slower)} sha256 {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original)).ToLowerInvariant()} sets total limit {oldTotal}->{limit}, area limit {oldArea}->{SubsystemCreatureSpawn.m_areaLimit}");
                    Check(limit<3,"the installed package no longer sets a limit below the smallest squad ("+limit+"); re-evaluate the diagnosis");
                    BudgetTooSmall("installed package limits",limit);
                    Check(File.ReadAllBytes(slower).AsSpan().SequenceEqual(original),"third-party package changed on disk");
                }finally{
                    AppDomain.CurrentDomain.AssemblyResolve-=resolve;SubsystemCreatureSpawn.m_totalLimit=oldTotal;SubsystemCreatureSpawn.m_areaLimit=oldArea;SubsystemCreatureSpawn.m_totalLimitConstant=oldConstant;SubsystemCreatureSpawn.m_totalLimitConstantChallenging=oldChallenging;SubsystemCreatureSpawn.m_areaLimitConstant=oldAreaConstant;
                    if(oldTemplate==null)DatabaseManager.m_valueDictionaries.Remove("ScTacticalEnemy");else DatabaseManager.m_valueDictionaries["ScTacticalEnemy"]=oldTemplate;
                }
            });
            // agent-followup-140 F3 / video-feedback-20260929 R1, on the packaged model, clip cache and config of this edition.
            foreach(string role in new[]{"ct","t"})Test("air-rules-hold-pose-and-ten-second-fall-on-engine-controller/"+role,()=>{
                Check(vanillaContent!=null,"vanilla content not supplied");
                using(var vanilla=ZipFile.OpenRead(vanillaContent))using(var template=vanilla.Entries.Single(e=>e.FullName.EndsWith("Simple.template.json",StringComparison.OrdinalIgnoreCase)).Open())Engine.Animation.AnimationTemplateManager.LoadFromJsonNode(System.Text.Json.Nodes.JsonNode.Parse(template));
                using var zip=ZipFile.OpenRead(dlcPath);
                byte[] Bytes(string path){using var s=zip.GetEntry(path)?.Open()??throw new Exception("Missing "+path);using var m=new MemoryStream();s.CopyTo(m);return m.ToArray();}
                using var stream=new MemoryStream(Bytes("Assets/Models/ScCsgoTactical/"+role+".glb"));var data=GltfLoader.Load(stream);
                using var model=new Model{ModelData=data,Skin=data.Skin,Animations=data.Animations};
                foreach(var b in data.Bones)model.m_bones.Add(new ModelBone{Model=model,Index=model.m_bones.Count,Name=b.Name,Transform=b.Transform});
                for(int i=0;i<data.Bones.Count;i++){int parent=data.Bones[i].ParentBoneIndex;if(parent>=0){model.m_bones[i].ParentBone=model.m_bones[parent];model.m_bones[parent].m_childBones.Add(model.m_bones[i]);}else model.m_rootBone=model.m_bones[i];}
                var clips=(List<Engine.Animation.ModelAnimation>)T("ScActorAnimations").GetMethod("Read").Invoke(null,[new MemoryStream(Bytes("Assets/Animations/ScCsgoTactical/"+role+".scanim")),model.m_bones.Select(b=>b.Name).ToArray()]);
                data.Animations=clips;model.Animations=clips;
                var loader=new Engine.Animation.AnimationConfigLoader();var cfg=loader.LoadFromJsonNode(System.Text.Json.Nodes.JsonNode.Parse(Bytes("Assets/Animations/ScTactical.json")));var controller=loader.CreateController(cfg,model);
                string Picked(bool air,bool armed,bool crouch,bool rising,bool moving,float speed){var p=controller.Parameters;
                    p.SetBool("IsDead",false);p.SetBool("Shield",false);p.SetBool("Airborne",air);p.SetBool("Armed",armed);p.SetBool("Crouch",crouch);
                    p.SetBool("AirRising",rising);p.SetBool("AirMoving",moving);p.SetFloat("SpeedAbs",speed);controller.Velocity=new Vector3(speed,0,0);
                    controller.Update(.02f);return controller.m_layerAnimationRef.TryGetValue("Base",out var r)?r?.Source:null;}
                foreach(var (air,armed,crouch,rising,moving,speed,expected) in new[]{(true,false,false,true,false,4f,"jump"),(true,false,false,true,true,5f,"jumprun"),(true,false,false,false,false,1f,"air"),
                    (true,false,false,false,true,3.2f,"airrun"),(true,false,true,true,false,4f,"crouchjump"),(true,false,true,false,false,1f,"crouchair"),(true,true,false,true,false,4f,"aimjump"),
                    (true,true,false,true,true,5f,"aimjumprun"),(true,true,false,false,true,3.2f,"aimairrun"),(true,true,true,false,false,1f,"aimcrouchair"),(false,true,false,false,false,0f,"aim"),(false,false,false,false,false,0f,"idle")}){
                    string picked=Picked(air,armed,crouch,rising,moving,speed);Check(picked==expected,$"airborne rules picked {picked} instead of {expected} (air {air} armed {armed} crouch {crouch} rising {rising} moving {moving})");}
                foreach(string alias in new[]{"jump","jumprun","air","airrun","crouchjump","crouchair","aimjump","aimjumprun","aimair","aimairrun","aimcrouchjump","aimcrouchair"}){
                    var reference=cfg.Animations[alias];Check(reference.PreservePose&&reference.LoopValue is false,$"{alias} would loop or drop its pose in a long fall");
                    Check(clips.Any(c=>c.Name==alias),$"{alias} missing from the packaged clip cache");}
                // A ten-second fall: after the clip has played once the legs must not move again, and no pose is lost.
                var legs=new[]{"leg_upper_L","leg_lower_L","ankle_L","leg_upper_R","leg_lower_R","ankle_R"}.Select(n=>model.FindBone(n,true).Index).ToArray();
                foreach(var (armed,moving) in new[]{(false,false),(false,true),(true,false),(true,true)}){
                    Picked(false,armed,false,false,false,0);controller.Update(.3f);Picked(true,armed,false,false,moving,20);
                    Matrix?[] previous=null;float worst=0,entry=0;
                    for(int frame=0;frame<600;frame++){
                        controller.Update(1/60f);var pose=new Matrix?[data.Bones.Count];controller.ComputeBoneTransforms(pose);Check(legs.All(i=>pose[i].HasValue),"fall pose lost at frame "+frame);
                        if(previous!=null)foreach(int i in legs){previous[i].Value.Decompose(out _,out var a,out _);pose[i].Value.Decompose(out _,out var b,out _);float degrees=MathUtils.RadToDeg(2*MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(a,b)),0,1)));if(frame>40)worst=Math.Max(worst,degrees);else entry=Math.Max(entry,degrees);}
                        previous=pose;
                    }
                    Check(worst<.05f,$"legs still move {worst:0.00} degrees per frame in a long fall (armed {armed}, moving {moving})");
                    Check(entry<20,$"entering the fall snaps the legs {entry:0.0} degrees in one frame (armed {armed}, moving {moving})");
                }
            });
            Test("air-state-once-per-frame-latch-and-hysteresis",()=>{
                dynamic air=Activator.CreateInstance(T("TacticalAirState"));
                void Step(int frame,bool grounded,Vector3 v,int times=1){for(int i=0;i<times;i++)air.Advance(frame,1/60f,grounded,v);}
                for(int f=0;f<6;f++)Step(f,false,new Vector3(3,0,0),5); // five cameras animate the same frame
                Check(!(bool)air.Airborne&&Math.Abs((float)air.AirTime-6/60f)<1e-4f,"air time advanced more than once per frame: "+(float)air.AirTime);
                Step(6,false,new Vector3(3,4,0));Step(7,false,new Vector3(3,4,0));Step(8,false,new Vector3(3,4,0));Check((bool)air.Airborne&&(bool)air.Moving&&(bool)air.Rising,"take-off state wrong");
                // Horizontal speed crossing 2 in mid-air never changes the variant; vertical speed hovering near its thresholds never flips it.
                int flips=0;bool rising=true,moving=true;
                for(int f=9;f<400;f++){float t=f/60f;Step(f,false,new Vector3(2+.6f*MathF.Sin(t*40),.35f+.3f*MathF.Sin(t*31),0));if((bool)air.Rising!=rising){flips++;rising=(bool)air.Rising;}if((bool)air.Moving!=moving){flips++;moving=(bool)air.Moving;}}
                Check(flips<=1&&(bool)air.Moving&&!(bool)air.Rising,"speed jitter flipped the air variant "+flips+" times");
                Step(400,false,new Vector3(0,3,0));Check((bool)air.Rising,"a real upward launch in mid-air not recognised");
                Step(401,true,Vector3.Zero);Check(!(bool)air.Airborne&&!(bool)air.Rising&&!(bool)air.Moving,"landing did not clear the state");
                for(int f=402;f<405;f++)Step(f,false,Vector3.Zero);Check(!(bool)air.Airborne,"a three-frame step counted as airborne");
            });
            Test("finite-ammo-reload-through-native-ai-no-registry",()=>{
                FreshRegistry();var f=World();var e=Enemy(f.P);e.Enemy.State.Rounds=0;e.Enemy.State.Reserve=3;e.Enemy.State.ReloadLeft=.2f;((IUpdateable)e.Enemy).Update(.1f);Check(e.Enemy.State.Rounds==0&&e.Enemy.State.Reserve==3,"ammo committed before reload finish");((IUpdateable)e.Enemy).Update(.2f);Check(e.Enemy.State.Rounds==3&&e.Enemy.State.Reserve==0,"reload created/lost ammo");Check(Enumerable.Range(0,5).All(i=>e.Inv.GetSlotCount(i)==0),"enemy holds allocated player inventory gun");
            });
            foreach(bool playerTarget in new[]{false,true})Test("shoot-wall-friendly-body-cadence-player-control/"+playerTarget,()=>{
                var f=World();var e=Enemy(f.P);e.Enemy.State.Grenades=0;
                var target=new ComponentBody{Position=new Vector3(0,60,-8),BoxSize=new Vector3(.8f,1.8f,.8f),Mass=75};var health=new Health{Health=1,AttackResilience=1000,AttackResilienceFactor=1};var creature=new ComponentCreature{ComponentBody=target,ComponentHealth=health};health.m_componentCreature=creature;E(f.P,target,health,creature);f.Bodies.AddBody(target);e.Enemy.Alert(target);
                var motion=new ComponentLocomotion();motion.m_entity=target.Entity;target.Entity.m_components.Add(motion);
                if(playerTarget){var player=Blank<ComponentPlayer>();player.PlayerData=Blank<PlayerData>();player.m_entity=target.Entity;target.Entity.m_components.Add(player);creature.m_subsystemPlayerStats=new SubsystemPlayerStats();}
                // video-feedback-20260929 R3: the whole chain, enemy bullet -> the player's health -> the direction mark.
                // The enemy stands at the origin, the player 8 m to its north (-Z) looking north: the shot comes from behind.
                float[] Marks()=>playerTarget?(float[])C("ScDamageIndicator").GetMethod("Intensities").Invoke(null,[target.Entity.FindComponent<ComponentPlayer>(),new Vector3(0,0,-1),f.Time.GameTime]):null;
                int before=e.Enemy.State.Rounds;f.Terrain.Blocked=true;for(int i=0;i<20;i++)((IUpdateable)e.Enemy).Update(.1f);Check(e.Enemy.State.Rounds==before&&health.Health==1,"shot through wall");
                Check(Marks()==null,"a shot stopped by a wall left a direction mark");
                f.Terrain.Blocked=false;e.Enemy.Alert(target);var ally=Enemy(f.P);ally.Creature.ComponentBody.Position=new Vector3(0,60,-3);f.Bodies.AddBody(ally.Creature.ComponentBody);for(int i=0;i<20;i++)((IUpdateable)e.Enemy).Update(.1f);Check(e.Enemy.State.Rounds==before&&health.Health==1,"shot through friendly body");f.Bodies.RemoveBody(ally.Creature.ComponentBody);
                e.Enemy.Alert(target);for(int i=0;i<18;i++)((IUpdateable)e.Enemy).Update(.1f);int spent=before-e.Enemy.State.Rounds;Check(spent>0&&spent<=6&&health.Health<1&&health.Health>0,"native cadence/damage broken: "+spent+" / "+health.Health);
                if(playerTarget){var marks=Marks();Check(marks!=null&&marks[2]>0&&marks.Count(x=>x>0)==1,"the enemy's bullet hurt the player but no mark points behind: "+(marks==null?"none":string.Join(",",marks)));}
                Check(playerTarget?target.m_totalImpulse==Vector3.Zero&&motion.StunTime==0:target.m_totalImpulse.LengthSquared()>0&&motion.StunTime>.0f,"player bullet control remains or creature control lost");
                var grenades=f.P.m_subsystems.Single(s=>s.GetType()==C("SubsystemScGrenades"));var blindType=C("SubsystemScGrenades").GetNestedType("Blindness",BindingFlags.NonPublic);var blind=Activator.CreateInstance(blindType);Set(blind,"Until",50d);Set(blind,"ImmuneUntil",60d);var dict=(System.Collections.IDictionary)C("SubsystemScGrenades").GetField("m_blind",Fields).GetValue(grenades);dict.Add(e.Creature.ComponentBody,blind);before=e.Enemy.State.Rounds;for(int i=0;i<30;i++)((IUpdateable)e.Enemy).Update(.1f);Check(before==e.Enemy.State.Rounds&&e.Path.Destination is null,"blinded enemy keeps firing/chasing");
            });
            // mpc3-feedback-subworld-20261002 (N1): nobody is engaged unprovoked. Coming near (in front or behind, in full view),
            // a shot fired into the air and time passing grant no target; being attacked allows the bounded retaliation.
            // (Replaces agent-followup-140 F2 / video-feedback-20260929 R3's proactive notice at 32/36 m.)
            Test("neutral-until-attacked-then-bounded-retaliation-and-release",()=>{
                var f=World();var e=Enemy(f.P);e.Enemy.State.Grenades=0;e.Creature.ComponentBody.Rotation=Quaternion.Identity;
                var target=new ComponentBody{Position=new Vector3(0,60,-25),BoxSize=new Vector3(.8f,1.8f,.8f),Mass=75};var health=new Health{Health=1,AttackResilience=1000,AttackResilienceFactor=1};var creature=new ComponentCreature{ComponentBody=target,ComponentHealth=health};health.m_componentCreature=creature;E(f.P,target,health,creature);
                var player=Blank<ComponentPlayer>();player.PlayerData=Blank<PlayerData>();player.m_entity=target.Entity;target.Entity.m_components.Add(player);creature.m_subsystemPlayerStats=new SubsystemPlayerStats();f.Bodies.AddBody(target);
                void Run(int n){for(int i=0;i<n;i++){f.Time.m_gameTime+=.1;((IUpdateable)e.Enemy).Update(.1f);}}
                void Place(Vector3 p){target.Position=p;f.Bodies.UpdateBody(target);}
                int rounds=e.Enemy.State.Rounds;
                foreach(float d in new[]{64f,48f,40f,33f,31f,20f,8f,3f,-5f,-15f}){Place(new Vector3(0,60,-d));Run(15);Check(e.Enemy.TargetBody==null&&e.Enemy.State.Rounds==rounds&&health.Health==1,"an unprovoked enemy took a target or fired at a visible player "+d+" m away");}
                // A shot into the air next to it: it may walk a bounded search toward the sound; it takes no target and fires nothing.
                Place(new Vector3(0,60,-12));((INoiseListener)e.Enemy).HearNoise(target,target.Position,1f);Run(60);
                Check(e.Enemy.TargetBody==null&&!(bool)e.Enemy.Retaliating&&e.Enemy.State.Rounds==rounds&&health.Health==1,"a gunshot heard nearby made a neutral enemy hostile");
                // A sniper is neutral like the rest (it used to notice at 36 m).
                var sniper=Enemy(f.P,0);sniper.Enemy.State.Grenades=0;sniper.Creature.ComponentBody.Rotation=Quaternion.Identity;int sniperRounds=sniper.Enemy.State.Rounds;
                Place(new Vector3(0,60,-30));for(int i=0;i<20;i++)((IUpdateable)sniper.Enemy).Update(.1f);Check(sniper.Enemy.TargetBody==null&&sniper.Enemy.State.Rounds==sniperRounds,"an unprovoked sniper took a target");
                sniper.Creature.ComponentHealth.Health=0;
                // Attacked: a retaliation, bounded by sight, its range and the memory of a hidden attacker, as before.
                Place(new Vector3(0,60,-38));e.Enemy.Alert(target);rounds=e.Enemy.State.Rounds;Run(25);
                Check((bool)e.Enemy.Retaliating&&e.Enemy.State.Rounds<rounds,"no retaliation against a 38 m attacker");
                f.Terrain.Blocked=true;rounds=e.Enemy.State.Rounds;float hp=health.Health;Run(95);Check(e.Enemy.TargetBody==null&&e.Enemy.State.Rounds==rounds&&health.Health==hp,"retaliation never forgot a hidden attacker, or fired through the wall");f.Terrain.Blocked=false;
                Run(30);Check(e.Enemy.TargetBody==null&&e.Enemy.State.Rounds==rounds,"an enemy that had let its attacker go engaged again unprovoked");
                foreach(float distance in new[]{45f,128,256}){Place(new Vector3(0,60,-distance));e.Enemy.Alert(target);Run(3);Check(e.Enemy.TargetBody==target,"distant retaliation discarded");}
            });
            // mpc3-feedback-subworld-20261002 (N2): one of them is attacked - everyone of them within 32 m of the one that was
            // hit takes that same attacker, whatever its squad or source; nobody farther, nobody dead, and it does not spread.
            Test("one-attacked-all-within-32-take-the-same-attacker-once",()=>{
                var f=World();var loader=(ModLoader)Activator.CreateInstance(T("TacticalModLoader"));
                dynamic Other(string squad,int role,int seed)=>(object)T("TacticalEnemyState").GetMethod("Create").Invoke(null,[Enum.ToObject(T("TacticalRole"),role),squad,new Engine.Random(seed)]);
                (dynamic Enemy,ComponentCreature Creature,ComponentInventoryBase Inv,ComponentPathfinding Path) Member(Vector3 at,dynamic state=null){
                    var m=Enemy(f.P);if(state is not null)m.Enemy.Configure(state,at);m.Creature.ComponentBody.Position=at;m.Enemy.Home=at;m.Enemy.State.Grenades=0;f.Bodies.AddBody(m.Creature.ComponentBody);f.Director.OnEntityAdded(m.Creature.Entity);return m;
                }
                (ComponentBody Body,Health Health,ComponentPlayer Player) Person(Vector3 at){
                    var body=new ComponentBody{Position=at,BoxSize=new Vector3(.8f,1.8f,.8f),Mass=75};var health=new Health{Health=1,AttackResilience=1000,AttackResilienceFactor=1};var creature=new ComponentCreature{ComponentBody=body,ComponentHealth=health,m_subsystemPlayerStats=new SubsystemPlayerStats(),m_killVerbs=["shot"]};health.m_componentCreature=creature;
                    var player=Blank<ComponentPlayer>();player.PlayerData=Blank<PlayerData>();E(f.P,body,health,creature,player);f.Bodies.AddBody(body);return(body,health,player);
                }
                void Run(double seconds,params dynamic[] who){for(double t=0;t<seconds;t+=.1){f.Time.m_gameTime+=.1;foreach(var e in who)((IUpdateable)e).Update(.1f);}}
                var victim=Member(new Vector3(0,60,0));
                var mate=Member(new Vector3(10,60,0));                                              // same squad
                var natural=Member(new Vector3(6,60,30.4f),Other("natural-squad",2,31));natural.Enemy.State.Source="natural";   // 31 m from the victim
                var summoned=Member(new Vector3(-22,60,22),Other("summoned-squad",3,32));summoned.Enemy.State.Source="manual";
                var legacy=Member(new Vector3(-12,60,-12),Other("old-save-squad",4,33));legacy.Enemy.State.Source=null;   // a save from before the source was recorded
                var far=Member(new Vector3(0,60,33),Other("far-squad",1,34));                      // 33 m from the victim
                var relay=Member(new Vector3(0,60,60),Other("relay-squad",1,35));                  // 29 m from "natural", 60 from the victim
                var dead=Member(new Vector3(5,60,5),Other("dead-squad",1,36));dead.Creature.ComponentHealth.Health=0;
                // (Everyone told is also within its own retaliation range (40) of the shooter, with a clear line: joining in does
                // not lift that bound.)
                var shooter=Person(new Vector3(0,60,-8));var bystander=Person(new Vector3(3,60,-14));
                var all=new[]{victim,mate,natural,summoned,legacy,far,relay};
                // Standing among them, in view of all of them, for five seconds: nothing.
                Run(5,all.Select(m=>(object)m.Enemy).ToArray());
                Check(all.All(m=>m.Enemy.TargetBody==null)&&shooter.Health.Health==1&&bystander.Health.Health==1,"neutral squads engaged players standing near them");
                // No attacker (a fall, fire) and an attack with no power: nobody is provoked.
                loader.ProcessAttackment(Strike(victim.Creature.ComponentBody,null,2));
                loader.ProcessAttackment(Strike(victim.Creature.ComponentBody,shooter.Body.Entity,0));
                Check(all.All(m=>m.Enemy.TargetBody==null),"damage without an attacker, or an attack without power, provoked someone");
                // One of their own hurts the victim (a squad mate's bomb): no one turns on a squad mate.
                loader.ProcessAttackment(Strike(victim.Creature.ComponentBody,mate.Creature.Entity,2));
                Check(all.All(m=>m.Enemy.TargetBody==null),"an enemy's own damage to a squad mate provoked the squads");
                // The shooter attacks the victim, through the hook the engine calls.
                int told=(int)f.Director.Provoked(victim.Enemy,null);Check(told==0,"Provoked told someone without an attacker");
                loader.ProcessAttackment(Strike(victim.Creature.ComponentBody,shooter.Body.Entity,2));
                foreach(var (m,name) in new[]{(victim,"the one that was hit"),(mate,"its squad mate 10 m away"),(natural,"a natural squad's member 31 m away"),(summoned,"a summoned squad's member 31 m away, in another direction"),(legacy,"a member of a squad saved without a source")})
                    Check(ReferenceEquals((ComponentBody)m.Enemy.TargetBody,shooter.Body)&&(bool)m.Enemy.Retaliating,name+" did not take the attacker as its target");
                Check(far.Enemy.TargetBody==null,"an enemy 33 m from the one that was hit joined in (the range is 32)");
                Check(relay.Enemy.TargetBody==null,"the alarm spread from an enemy that was told to one 60 m from the victim");
                Check(dead.Enemy.TargetBody==null,"a dead enemy took a target");
                // They fire at the attacker, and only at the attacker: the bystander in view is left alone.
                int before=all.Take(5).Sum(m=>(int)m.Enemy.State.Rounds);Run(6,all.Select(m=>(object)m.Enemy).ToArray());
                Check(all.Take(5).Sum(m=>(int)m.Enemy.State.Rounds)<before&&shooter.Health.Health<1,"the provoked squads never fired at their attacker");
                Check(bystander.Health.Health==1&&all.All(m=>!ReferenceEquals((ComponentBody)m.Enemy.TargetBody,bystander.Body)),"a player who attacked nobody was engaged");
                Check(far.Enemy.TargetBody==null&&relay.Enemy.TargetBody==null,"the fight spread beyond the 32 m of the first hit while it went on");
                // A second hit on a mate does not restart the others' aim, and a new attacker does not pull away those already retaliating.
                loader.ProcessAttackment(Strike(mate.Creature.ComponentBody,bystander.Body.Entity,2));
                Check(ReferenceEquals((ComponentBody)mate.Enemy.TargetBody,bystander.Body),"the one that was hit did not turn on the one who hit it");
                Check(ReferenceEquals((ComponentBody)victim.Enemy.TargetBody,shooter.Body)&&ReferenceEquals((ComponentBody)natural.Enemy.TargetBody,shooter.Body),"enemies already retaliating against a living attacker were pulled onto another");
                // A killing blow still provokes the others (measured from where the victim stood).
                var g=World();f=g;var lone=Member(new Vector3(0,60,0),Other("a",1,41));var witness=Member(new Vector3(8,60,0),Other("b",1,42));var killer=Person(new Vector3(0,60,-15));
                lone.Creature.ComponentHealth.Health=0;loader.ProcessAttackment(Strike(lone.Creature.ComponentBody,killer.Body.Entity,50));
                Check(ReferenceEquals((ComponentBody)witness.Enemy.TargetBody,killer.Body),"killing one of them in one blow provoked nobody");
                // Walls and the memory of a hidden attacker still bound it: told of an attacker it cannot see, it fires nothing and lets go.
                var h=World();f=h;var hit=Member(new Vector3(0,60,0),Other("a",1,51));var behind=Member(new Vector3(6,60,0),Other("b",1,52));var hidden=Person(new Vector3(0,60,-20));
                h.Terrain.Blocked=true;int rounds=(int)behind.Enemy.State.Rounds+(int)hit.Enemy.State.Rounds;
                loader.ProcessAttackment(Strike(hit.Creature.ComponentBody,hidden.Body.Entity,2));
                Check(ReferenceEquals((ComponentBody)behind.Enemy.TargetBody,hidden.Body),"not told of an attacker behind a wall");
                Run(12,hit.Enemy,behind.Enemy);
                Check((int)behind.Enemy.State.Rounds+(int)hit.Enemy.State.Rounds==rounds&&hidden.Health.Health==1&&behind.Enemy.TargetBody==null&&hit.Enemy.TargetBody==null,"provoked enemies fired through a wall or never let a hidden attacker go");
                Check((float)T("ComponentTacticalEnemy").GetField("ProvokeRange").GetRawConstantValue()==32f,"the provoke range is no longer the recorded candidate value (32)");
            });
            Test("summoned-warmup-holds-aim-then-fires-attack-preserves-it",()=>{
                var f=World();var e=Enemy(f.P);e.Enemy.State.Grenades=0;e.Enemy.State.Warmup=3f;
                var target=new ComponentBody{Position=new Vector3(0,60,-8),BoxSize=new Vector3(.8f,1.8f,.8f),Mass=75};var health=new Health{Health=1,AttackResilience=1000,AttackResilienceFactor=1};var creature=new ComponentCreature{ComponentBody=target,ComponentHealth=health};health.m_componentCreature=creature;E(f.P,target,health,creature);f.Bodies.AddBody(target);
                e.Enemy.TargetBody=target;int before=e.Enemy.State.Rounds;
                for(int i=0;i<28;i++)((IUpdateable)e.Enemy).Update(.1f);Check(e.Enemy.State.Rounds==before&&health.Health==1,"fired during the warning window");
                for(int i=0;i<15;i++)((IUpdateable)e.Enemy).Update(.1f);Check(e.Enemy.State.Rounds<before,"never engaged after the warning window");
                e.Enemy.State.Warmup=3f;e.Enemy.Alert(target);Check((float)e.Enemy.State.Warmup==3f,"being attacked removed the preparation window");
                string json=e.Enemy.State.Encode();Check((float)Decode(json.Replace("\"Warmup\":3","\"Warmup\":2.5")).Warmup==2.5f&&(float)Decode(json.Replace(",\"Warmup\":3","")).Warmup==0f,"warmup not saved or old saves not accepted");
            });
            Test("summoned-squad-faces-its-summoner-and-stays-neutral-until-attacked",()=>{
                var old=DatabaseManager.m_valueDictionaries.GetValueOrDefault("ScTacticalEnemy");DatabaseManager.m_valueDictionaries["ScTacticalEnemy"]=new ValuesDictionary();
                try{
                    foreach(int count in new[]{3,5}){
                        var f=World();var p=(SpawnProject)f.P;
                        BlocksManager.Blocks[0]=new AirBlock{IsCollidable=false};BlocksManager.Blocks[2]=new DirtBlock{BlockIndex=2,IsCollidable=true};
                        for(int cx=0;cx<4;cx++)for(int cz=0;cz<4;cz++)f.Terrain.Terrain.AllocateChunk(cx,cz).State=TerrainChunkState.Valid;
                        for(int x=1;x<63;x++)for(int z=1;z<63;z++){f.Terrain.Terrain.SetCellValueFast(x,60,z,2);f.Terrain.Terrain.SetTopHeight(x,z,60);}
                        var player=Blank<ComponentPlayer>();player.PlayerData=Blank<PlayerData>();player.ComponentBody=new ComponentBody{Position=new Vector3(8.5f,61,30.5f),BoxSize=new Vector3(.65f,1.8f,.65f),Mass=75};
                        var health=new Health{Health=1,AttackResilience=1000,AttackResilienceFactor=1};var creature=new ComponentCreature{ComponentBody=player.ComponentBody,ComponentHealth=health,m_subsystemPlayerStats=new SubsystemPlayerStats(),m_killVerbs=["shot"]};health.m_componentCreature=creature;
                        E(p,player,player.ComponentBody,health,creature);p.FindSubsystem<SubsystemPlayers>(true).m_componentPlayers.Add(player);f.Bodies.AddBody(player.ComponentBody);
                        p.Factory=()=>{var e=Enemy(p).Creature.Entity;p.m_entities.Remove(e);e.m_isAddedToProject=false;return e;};
                        p.Added=e=>{f.Director.OnEntityAdded(e);f.Bodies.AddBody(e.FindComponent<ComponentBody>(true));};p.Removed=e=>{f.Director.OnEntityRemoved(e);f.Bodies.RemoveBody(e.FindComponent<ComponentBody>(true));};
                        Check((int)f.Director.SpawnManual(new Point3(20,60,30),count)==count,"squad not created");
                        var members=((System.Collections.IEnumerable)f.Director.Enemies).Cast<dynamic>().ToArray();
                        foreach(var e in members){
                            ComponentBody body=e.Creature.ComponentBody;var to=Vector2.Normalize((player.ComponentBody.Position-body.Position).XZ);
                            Check(Vector2.Dot(Vector2.Normalize(body.Matrix.Forward.XZ),to)>.99f,"summoned member does not face its challenger");
                            e.State.Grenades=0;
                        }
                        // Farthest real position: 30 blocks (ManualFar + 2). Move the challenger back until the farthest member is 30 away.
                        float farthest=members.Max(e=>Vector2.Distance(((Vector3)e.Creature.ComponentBody.Position).XZ,player.ComponentBody.Position.XZ));
                        Check(farthest<=30.01f,"member beyond the documented farthest position: "+farthest);
                        int rounds=members.Sum(e=>(int)e.State.Rounds);
                        void Run(double seconds){for(double t=0;t<seconds;t+=.1){f.Time.m_gameTime+=.1;foreach(var e in members)((IUpdateable)e).Update(.1f);}}
                        Run(2.8);Check(members.Sum(e=>(int)e.State.Rounds)==rounds&&health.Health==1,"summoned squad fired inside its three-second warning");
                        // mpc3-feedback-subworld-20261002 (N1): a summoned squad is neutral like a natural one. The end of its warning
                        // window (and of the search toward its summoner that follows) grants no target.
                        Run(14);Check(members.All(e=>e.TargetBody==null)&&members.Sum(e=>(int)e.State.Rounds)==rounds&&health.Health==1,"a summoned squad turned hostile by itself once its warning window was over");
                        // Its summoner attacks one member: the whole squad (every member is within 32 m of it) answers.
                        var loader=(ModLoader)Activator.CreateInstance(T("TacticalModLoader"));ComponentBody first=members[0].Creature.ComponentBody;
                        loader.ProcessAttackment(Strike(first,player.Entity,2));
                        Check(members.All(e=>ReferenceEquals((ComponentBody)e.TargetBody,player.ComponentBody)),"an attacked summoned squad did not all take its attacker");
                        Run(4);Check(members.Sum(e=>(int)e.State.Rounds)<rounds,"the attacked summoned squad never fired back");
                        // Behind a wall the squad gets no target: it only searches toward where the challenger stood.
                        var g=World();var q=(SpawnProject)g.P;
                        for(int cx=0;cx<4;cx++)for(int cz=0;cz<4;cz++)g.Terrain.Terrain.AllocateChunk(cx,cz).State=TerrainChunkState.Valid;
                        for(int x=1;x<63;x++)for(int z=1;z<63;z++){g.Terrain.Terrain.SetCellValueFast(x,60,z,2);g.Terrain.Terrain.SetTopHeight(x,z,60);}
                        var hidden=Blank<ComponentPlayer>();hidden.PlayerData=Blank<PlayerData>();hidden.ComponentBody=new ComponentBody{Position=new Vector3(8.5f,61,30.5f),BoxSize=new Vector3(.65f,1.8f,.65f),Mass=75};
                        var hiddenHealth=new Health{Health=1,AttackResilience=1000,AttackResilienceFactor=1};var hiddenCreature=new ComponentCreature{ComponentBody=hidden.ComponentBody,ComponentHealth=hiddenHealth,m_subsystemPlayerStats=new SubsystemPlayerStats()};hiddenHealth.m_componentCreature=hiddenCreature;
                        E(q,hidden,hidden.ComponentBody,hiddenHealth,hiddenCreature);q.FindSubsystem<SubsystemPlayers>(true).m_componentPlayers.Add(hidden);g.Bodies.AddBody(hidden.ComponentBody);
                        q.Factory=()=>{var e=Enemy(q).Creature.Entity;q.m_entities.Remove(e);e.m_isAddedToProject=false;return e;};
                        q.Added=e=>{g.Director.OnEntityAdded(e);g.Bodies.AddBody(e.FindComponent<ComponentBody>(true));};q.Removed=e=>{g.Director.OnEntityRemoved(e);g.Bodies.RemoveBody(e.FindComponent<ComponentBody>(true));};
                        Check((int)g.Director.SpawnManual(new Point3(20,60,30),count)==count,"squad not created behind the wall");
                        g.Terrain.Blocked=true;var blind=((System.Collections.IEnumerable)g.Director.Enemies).Cast<dynamic>().ToArray();int before=blind.Sum(e=>(int)e.State.Rounds);
                        for(double t=0;t<8;t+=.1){g.Time.m_gameTime+=.1;foreach(var e in blind)((IUpdateable)e).Update(.1f);}
                        Check(blind.All(e=>e.TargetBody==null)&&blind.Sum(e=>(int)e.State.Rounds)==before&&hiddenHealth.Health==1,"summoned squad locked or shot a challenger it cannot see");
                    }
                }finally{if(old==null)DatabaseManager.m_valueDictionaries.Remove("ScTacticalEnemy");else DatabaseManager.m_valueDictionaries["ScTacticalEnemy"]=old;}
            });
            Test("manual-squad-keeps-neutrality-at-near-and-distant-clicks",()=>{
                // Manual placement is now at the clicked ground. No obsolete summoner-distance constants apply.
                // Neutrality must still hold for every role after warmup, near the player as well as far away.
                foreach(int role in new[]{0,1,2,3,4}){
                    foreach(float d in new[]{2f,18,30,80,256}){
                        var f=World();var e=Enemy(f.P,role);e.Enemy.State.Grenades=0;
                        var target=new ComponentBody{Position=new Vector3(d,60,0),BoxSize=new Vector3(.8f,1.8f,.8f),Mass=75};var health=new Health{Health=1,AttackResilience=1000,AttackResilienceFactor=1};var creature=new ComponentCreature{ComponentBody=target,ComponentHealth=health,m_subsystemPlayerStats=new SubsystemPlayerStats()};health.m_componentCreature=creature;
                        var player=Blank<ComponentPlayer>();player.PlayerData=Blank<PlayerData>();E(f.P,target,health,creature,player);f.Bodies.AddBody(target);
                        e.Enemy.State.Warmup=3f;e.Enemy.Investigate(target.Position,11f);
                        for(int i=0;i<140;i++){f.Time.m_gameTime+=.1;((IUpdateable)e.Enemy).Update(.1f);}
                        Check(e.Enemy.TargetBody==null&&health.Health==1,$"role {role} summoned {d} m away engaged its summoner unprovoked");
                    }
                }
            });
            Test("squad-bomb-hold-outside-then-evacuate-without-chasing-back",()=>{
                var f=World();var bombs=(Subsystem)Activator.CreateInstance(T("SubsystemTacticalBombs"));bombs.m_project=f.P;f.P.m_subsystems.Add(bombs);
                var bomb=Activator.CreateInstance(T("SubsystemTacticalBombs").GetNestedType("Bomb"));dynamic charge=bomb.GetType().GetField("Charge").GetValue(bomb);charge.Position=new Vector3(0,60,0);charge.Remaining=40f;
                ((System.Collections.IList)T("SubsystemTacticalBombs").GetField("Bombs").GetValue(bombs)).Add(bomb);
                var e=Enemy(f.P);e.Enemy.State.Grenades=0;
                var target=new ComponentBody{Position=new Vector3(0,60,-6),BoxSize=new Vector3(.8f,1.8f,.8f),Mass=75};var health=new Health{Health=1,AttackResilience=1000,AttackResilienceFactor=1};var creature=new ComponentCreature{ComponentBody=target,ComponentHealth=health};health.m_componentCreature=creature;E(f.P,target,health,creature);f.Bodies.AddBody(target);
                e.Enemy.Alert(target);((IUpdateable)e.Enemy).Update(.1f);
                Check(e.Path.Destination is Vector3 hold&&Vector2.Distance(hold.XZ,Vector2.Zero)>=15,"planter stays inside its own bomb radius");
                target.Position=new Vector3(0,60,-4);e.Creature.ComponentBody.Position=new Vector3(20,60,0);for(int i=0;i<8;i++)((IUpdateable)e.Enemy).Update(.1f);
                Check(e.Path.Destination is not Vector3 chase||Vector2.Distance(chase.XZ,Vector2.Zero)>=15,"chase leads back into a live bomb radius");
                charge.Remaining=10f;e.Creature.ComponentBody.Position=new Vector3(2,60,1);int before=e.Enemy.State.Rounds;for(int i=0;i<10;i++)((IUpdateable)e.Enemy).Update(.1f);
                Check(e.Path.Destination is Vector3 exit&&Vector2.Distance(exit.XZ,Vector2.Zero)>=15&&e.Enemy.State.Rounds==before,"no evacuation in the final seconds, or kept fighting inside the radius");
            });
            // r2-c4-completion-20260929: enemy throws and plants are visible actions on the gameplay clock, committed once.
            (dynamic Enemy,ComponentCreature Creature,ComponentBody Target,ComponentHealth TargetHealth,Subsystem Grenades,System.Collections.IList Active,Func<int,float,List<string>> Run) Thrower(int role,float distance){
                // The held grenade/C4 values need the core block indices (restored after the suite).
                foreach(var (name,index) in new[]{("ScGrenadeBlock",720),("ScC4Block",721)}){BlocksManager.BlockTypeToIndex[C(name)]=index;BlocksManager.BlockNameToIndex[name]=index;}
                var f=World();var e=Enemy(f.P,role);var g=f.P.m_subsystems.Single(s=>s.GetType()==C("SubsystemScGrenades"));
                Set(g,"m_players",f.P.FindSubsystem<SubsystemPlayers>(true));Set(g,"m_info",f.Info);
                e.Creature.ComponentBody.Rotation=Quaternion.Identity;e.Creature.ComponentBody.StandingOnValue=2;e.Enemy.State.Warmup=0f;e.Enemy.State.GrenadeLeft=0f;
                var target=new ComponentBody{Position=new Vector3(0,60,-distance),BoxSize=new Vector3(.8f,1.8f,.8f),Mass=75};var health=new Health{Health=1,AttackResilience=1e6f,AttackResilienceFactor=1};
                var creature=new ComponentCreature{ComponentBody=target,ComponentHealth=health};health.m_componentCreature=creature;E(f.P,target,health,creature);f.Bodies.AddBody(target);f.Bodies.AddBody(e.Creature.ComponentBody);
                var active=(System.Collections.IList)C("SubsystemScGrenades").GetField("m_active",Fields).GetValue(g);
                List<string> Run(int frames,float dt){var log=new List<string>();for(int i=0;i<frames;i++){f.Time.m_gameTime+=dt;((IUpdateable)e.Enemy).Update(dt);
                    dynamic phase=e.Enemy.ThrowPhase;dynamic plant=e.Enemy.PlantPhase;
                    // crouch: the enemy's own crouch intent (NPC bodies cannot crouch natively); eased: its eased factor.
                    log.Add($"t={f.Time.GameTime:0.000} throw={(phase.Active?$"s{phase.Stage}{(phase.Released?"R":"")}":"-")} plant={(plant.Active?$"{plant.Seconds:0.00}{(plant.Placed?"P":"")}":"-")} held={e.Enemy.PresentedValue} grenades={e.Enemy.State.Grenades} active={active.Count} rounds={e.Enemy.State.Rounds} crouch={((bool)e.Enemy.Crouching?1:0)} eased={(float)e.Enemy.CrouchFactor:0.00} action={e.Enemy.VisualAction.Kind}");}return log;}
                e.Enemy.Alert(target);return(e.Enemy,e.Creature,target,health,g,active,Run);
            }
            TestD("enemy-grenade-visible-throw-releases-once-and-redraws",()=>{
                var w=Thrower(1,15);w.Enemy.State.Grenades=1;w.Enemy.State.Grenade=0;int rounds=w.Enemy.State.Rounds;int grenade=(int)C("ScGrenadeBlock").GetMethod("Value").Invoke(null,[0]);int gun=w.Enemy.State.DisplayValue;
                var log=w.Run(240,1/60f);string Trace()=>string.Join(" | ",log.Where((_,i)=>i%6==0||log[i].Contains("R ")).Take(40));
                int start=log.FindIndex(l=>!l.Contains("throw=-"));Check(start>=0,"no throw started: "+Trace());
                int release=log.FindIndex(l=>l.Contains("active=1"));Check(release>start,"no grenade released: "+Trace());
                double seconds=(release-start)/60.0;Check(Math.Abs(seconds-(.6+.2+.27))<=2/60.0,$"released {seconds:0.000} s after the start instead of 1.07 s: "+Trace());
                Check(log.Take(release).All(l=>l.Contains("active=0")&&l.Contains("grenades=1")),"a grenade existed or was spent before the release frame: "+Trace());
                Check(log[release].Contains("grenades=0")&&log.Skip(release).All(l=>l.Contains("grenades=0")&&!l.Contains("active=2")),"the grenade was not spent exactly once at the release: "+Trace());
                Check(log.Skip(start).Take(release-start).All(l=>l.Contains("held="+grenade)),"the enemy did not hold the grenade until the release: "+Trace());
                int end=log.FindIndex(release,l=>l.Contains("throw=-"));Check(end>release,"the throw never ended: "+Trace());
                Check(log[end].Contains("held="+gun)&&log[end].Contains("action=Draw"),"the gun did not come back with its draw: "+Trace());
                int fired=log.FindIndex(l=>!l.Contains("rounds="+rounds));Check(fired<0||fired>=end+(int)(.6*60),"fired during the throw or before the gun was drawn again: "+Trace());
                // The grenade left outside the thrower's own box and flies toward the target.
                dynamic state=w.Active[0];Vector3 at=state.Position;var own=w.Creature.ComponentBody;
                Check(Vector2.Distance(at.XZ,own.Position.XZ)>own.BoxSize.X*.5f,"the grenade started inside the thrower: "+at);
                return $"start frame {start}, release {release} ({seconds:0.000} s), gun back at {end}; "+Trace();
            });
            TestD("enemy-grenade-start-outside-own-body-flies-to-target",()=>{
                // The old start (1.5 m above the feet) lay inside the thrower's box; recorded for evidence, the new one must travel.
                var w=Thrower(1,15);var g=w.Grenades;var own=w.Creature.ComponentBody;Vector3 aim=w.Target.Position+Vector3.UnitY*.4f;
                float Travel(Vector3 start){w.Active.Clear();var v=(aim-start)/1.1f+Vector3.UnitY*5.5f;Check((bool)C("SubsystemScGrenades").GetMethod("TryThrowHostile").Invoke(g,[0,start,v]),"throw refused");
                    dynamic s=w.Active[0];float before=Vector2.Distance(((Vector3)s.Position).XZ,aim.XZ);for(int i=0;i<10;i++){s.Remaining=5f;((IUpdateable)g).Update(.02f);}return before-Vector2.Distance(((Vector3)s.Position).XZ,aim.XZ);}
                float old=Travel(own.Position+Vector3.UnitY*1.5f);float now=Travel((Vector3)T("ComponentTacticalEnemy").GetMethod("ThrowOrigin").Invoke(null,[own]));
                Check(now>2,$"the grenade from the new start moved only {now:0.00} m toward the target in 0.2 s");
                return $"toward the target in 0.2 s: old start {old:0.00} m (negative = bounced off the thrower), new start {now:0.00} m";
            });
            foreach(string interrupt in new[]{"death","blind","target-dies"})TestD("enemy-grenade-interrupted-before-release-spends-nothing/"+interrupt,()=>{
                var w=Thrower(1,15);w.Enemy.State.Grenades=1;var log=w.Run(20,1/60f);Check((bool)w.Enemy.ThrowPhase.Active,"fixture: no throw started: "+string.Join(" | ",log));
                if(interrupt=="death")w.Creature.ComponentHealth.Health=0;
                if(interrupt=="blind"){var blindType=C("SubsystemScGrenades").GetNestedType("Blindness",BindingFlags.NonPublic);var blind=Activator.CreateInstance(blindType);Set(blind,"Until",1e9);Set(blind,"ImmuneUntil",1e9);((System.Collections.IDictionary)C("SubsystemScGrenades").GetField("m_blind",Fields).GetValue(w.Grenades)).Add(w.Creature.ComponentBody,blind);}
                if(interrupt=="target-dies")w.TargetHealth.Health=0;
                log.AddRange(w.Run(120,1/60f));
                Check(w.Active.Count==0&&w.Enemy.State.Grenades==1,"a grenade was thrown or spent after the "+interrupt+": "+string.Join(" | ",log.Where((_,i)=>i%10==0)));
                Check(!(bool)w.Enemy.ThrowPhase.Active,"the throw did not end");
                return string.Join(" | ",log.Where((_,i)=>i%15==0));
            });
            TestD("enemy-grenade-save-mid-throw-keeps-the-grenade",()=>{
                var w=Thrower(1,15);w.Enemy.State.Grenades=1;w.Run(30,1/60f);Check((bool)w.Enemy.ThrowPhase.Active,"fixture: no throw");
                var copy=Enemy(w.Creature.Entity.Project);copy.Enemy.Restore((string)w.Enemy.Capture());
                Check(copy.Enemy.State.Grenades==1&&!(bool)copy.Enemy.ThrowPhase.Active,"a save during the throw lost the grenade or restored a half throw");
                return "grenade kept, no action restored";
            });
            (dynamic Enemy,ComponentCreature Creature,Func<int,float,List<string>> Run,Func<int> Bombs,Subsystem System) Planter(float distance){
                var w=Thrower(4,distance);var project=w.Creature.Entity.Project;((Ground)project.FindSubsystem<SubsystemTerrain>(true)).Floor=60;
                var c4=(Subsystem)Activator.CreateInstance(C("SubsystemScC4"));c4.m_project=project;project.m_subsystems.Add(c4);
                var bombs=(Subsystem)Activator.CreateInstance(T("SubsystemTacticalBombs"));bombs.m_project=project;project.m_subsystems.Add(bombs);bombs.Load(new ValuesDictionary());
                w.Enemy.State.Grenades=0;Check((bool)w.Enemy.State.Bomb,"fixture: the demolition role carries no bomb");
                var list=(System.Collections.IList)T("SubsystemTacticalBombs").GetField("Bombs").GetValue(bombs);
                return(w.Enemy,w.Creature,w.Run,()=>list.Count,bombs);
            }
            TestD("enemy-plant-shows-c4-commits-once-recovers-empty-then-redraws",()=>{
                var w=Planter(15);int c4=(int)C("ScC4Block").GetProperty("Value").GetValue(null);int gun=w.Enemy.State.DisplayValue;int rounds=w.Enemy.State.Rounds;
                var log=new List<string>();for(int i=0;i<300;i++){log.AddRange(w.Run(1,1/60f));log[^1]+=$" bombs={w.Bombs()}";}
                string Trace()=>string.Join(" | ",log.Where((_,i)=>i%10==0));
                int start=log.FindIndex(l=>!l.Contains("plant=-"));Check(start>=0,"no plant started: "+Trace());
                int commit=log.FindIndex(l=>l.Contains("bombs=1"));Check(commit>start,"no bomb planted: "+Trace());
                double seconds=(commit-start+1)/60.0;Check(Math.Abs(seconds-3.2)<=2/60.0,$"planted after {seconds:0.000} s instead of 3.2 s: "+Trace());
                Check(log.Skip(start).Take(commit-start).All(l=>l.Contains("held="+c4)&&l.Contains("crouch=1")&&!l.Contains("P ")),"the planter did not hold the C4 crouched until the commit: "+Trace());
                Check(log[commit].Contains("P ")&&log.Skip(commit).All(l=>!l.Contains("bombs=2")),"the commit frame is not the placed frame, or a second bomb: "+Trace());
                int end=log.FindIndex(commit,l=>l.Contains("plant=-"));Check(end>commit&&Math.Abs((end-commit)/60.0-.8)<=2/60.0,"the recovery did not last 0.8 s: "+Trace());
                Check(log[end].Contains("held="+gun)&&log[end].Contains("action=Draw"),"the gun did not come back with its draw: "+Trace());
                Check(log.Take(end).All(l=>l.Contains("rounds="+rounds)),"fired while planting or recovering: "+Trace());
                // The eased crouch is down within half a second and stays down through the commit; it rises after the recovery.
                Check(log.Skip(start+31).Take(commit-start-31).All(l=>l.Contains("eased=1.00"))&&log.Skip(end+31).Take(10).All(l=>l.Contains("eased=0.00")),"the planter's crouch did not ease down before the commit or back up after: "+Trace());
                ((Subsystem)w.System).Dispose();
                return $"plant from frame {start}, bomb at {commit} ({seconds:0.000} s), gun back at {end}";
            });
            foreach(string interrupt in new[]{"damage","death"})TestD("enemy-plant-interrupted-before-commit-plants-nothing/"+interrupt,()=>{
                var w=Planter(15);var log=w.Run(90,1/60f);Check(log.Any(l=>!l.Contains("plant=-")),"fixture: no plant: "+string.Join(" | ",log.Where((_,i)=>i%10==0)));
                if(interrupt=="damage")w.Enemy.Alert(w.Creature.Entity.Project.FindSubsystem<SubsystemBodies>(true).Bodies.First(b=>b!=w.Creature.ComponentBody));
                else w.Creature.ComponentHealth.Health=0;
                log.AddRange(w.Run(interrupt=="death"?240:2,1/60f));
                Check(w.Bombs()==0||interrupt=="damage"&&log.Last().Contains("plant=")&&!log.Last().Contains("plant=-"),"a bomb appeared after the "+interrupt+": "+string.Join(" | ",log.Where((_,i)=>i%10==0)));
                if(interrupt=="damage")Check(log.Last().Contains("plant=-")||log.Last().Contains("plant=0.0"),"the plant was not abandoned when shot: "+log.Last());
                else Check(w.Bombs()==0,"a dead planter planted");
                ((Subsystem)w.System).Dispose();
                return string.Join(" | ",log.Where((_,i)=>i%15==0));
            });
            // headshot-armor-balance-20260929 H2: hostile gunfire against players uses its own close-range table; a
            // shotgun's pellets share it; creatures and companions keep the survival scale. Live Shoot, health read back.
            TestD("hostile-fire-players-use-the-table-shotgun-shares-it-others-keep-survival",()=>{
                var table=T("TacticalHostileBalance").GetMethod("PlayerShot");var parts=new List<string>();
                foreach(var (gun,expected) in new[]{("glock18",1.8f),("deagle",3f),("mp9",1.4f),("ak47",2.2f),("m4a1s",2.2f),("negev",1.8f),("g3sg1",3f),("ssg08",4f),("awp",5.5f),("nova",4.2f),("mag7",4.2f),("xm1014",3.2f)})
                    Check((float?)table.Invoke(null,[gun])==expected,$"{gun}: table {table.Invoke(null,[gun])} instead of {expected}");
                var specs=(Array)C("GunSpec").GetField("All").GetValue(null);int Index(string gun){for(int i=0;i<specs.Length;i++)if((string)((dynamic)specs.GetValue(i)).Name==gun)return i;throw new Exception("no gun "+gun);}
                var shoot=T("ComponentTacticalEnemy").GetMethod("Shoot",Fields);
                foreach(string gun in new[]{"ak47","nova"})foreach(bool player in new[]{true,false}){
                    var f=World();var e=Enemy(f.P,1);e.Enemy.State.Variant=Index(gun);e.Enemy.State.Rounds=1000;e.Creature.ComponentBody.Rotation=Quaternion.Identity;
                    var target=new ComponentBody{Position=new Vector3(0,60,-6),BoxSize=new Vector3(.65f,1.8f,.65f),Mass=75};var health=new Health{Health=1,AttackResilience=100,AttackResilienceFactor=1};
                    var creature=new ComponentCreature{ComponentBody=target,ComponentHealth=health};health.m_componentCreature=creature;
                    if(player){var who=Blank<ComponentPlayer>();who.PlayerData=Blank<PlayerData>();E(f.P,target,health,creature,who);}else E(f.P,target,health,creature);
                    f.Bodies.AddBody(target);f.Bodies.AddBody(e.Creature.ComponentBody);e.Enemy.Alert(target);
                    dynamic spec=specs.GetValue(Index(gun));int pellets=Math.Max(1,(int)spec.Pellets);
                    dynamic stats=C("EffectiveGunStats").GetMethod("ResolveLevel").Invoke(null,[(object)spec,(int)e.Enemy.State.DisplayValue,false,0]);
                    float budget=player?(float)(float?)table.Invoke(null,[gun]):(float)stats.Power,falloff=(float)stats.Falloff(spec,6f-.325f);
                    float share=budget/pellets/100;var losses=new List<float>();
                    for(int i=0;i<60;i++){health.Health=1;shoot.Invoke((object)e.Enemy,null);float loss=1-health.Health;if(loss>1e-6f)losses.Add(loss);}
                    Check(losses.Count>0,$"{gun} {(player?"player":"creature")}: no shot of 60 landed");
                    // Each landed pellet costs budget/pellets x falloff (distance 5.7-6.3 m); a shot never costs more than the whole budget.
                    Check(losses.All(l=>l<=budget/100*1.001f),$"{gun} {(player?"player":"creature")}: a shot cost more than its whole budget {budget}: {string.Join(",",losses.Take(8))}");
                    if(pellets==1)Check(losses.All(l=>Math.Abs(l-share*falloff)<1e-4f),$"{gun} {(player?"player":"creature")}: a hit did not cost exactly {share*falloff:0.#####}: {string.Join(",",losses.Take(8))}");
                    else Check(losses.All(l=>l>=share*.9f)&&losses.Any(l=>l>share*1.5f),$"{gun} {(player?"player":"creature")}: pellet shares of {share:0.#####} not seen (one pellet at least, several sometimes): {string.Join(",",losses.Take(8))}");
                    parts.Add($"{gun} {(player?"player":"creature")}: budget {budget} over {pellets} pellet(s), {losses.Count}/60 landed, health lost per shot {losses.Min():0.####}-{losses.Max():0.####}");
                }
                return string.Join("; ",parts);
            });
            // headshot-armor-balance-20260929 H4: one CS2 feedback sound per shot per target, from what the shot did.
            TestD("hit-sounds-one-per-shot-from-what-it-did",()=>{
                var f=World();var shooter=Enemy(f.P,1);var parts=new List<string>();
                var target=new ComponentBody{Position=new Vector3(0,60,-5),BoxSize=new Vector3(.65f,1.8f,.65f),Mass=75};var health=new Health{Health=1,AttackResilience=100,AttackResilienceFactor=1};
                var creature=new ComponentCreature{ComponentBody=target,ComponentHealth=health};health.m_componentCreature=creature;E(f.P,target,health,creature);f.Bodies.AddBody(target);
                var gunAttack=C("ScSurvivalBalance").GetNestedType("GunAttack");var part=C("ScHitPart");var hitsType=C("ScShotHits");
                object Shot(params string[] pellets){var h=Activator.CreateInstance(hitsType);foreach(var p in pellets)hitsType.GetMethod("Add").Invoke(h,[Enum.Parse(part,p),2f,new Vector3(0,61.6f,-4.7f),-Vector3.UnitZ]);return h;}
                List<(string Name,float Volume)> Deliver(object hits,float resilience=100){f.Audio.Played.Clear();health.Health=1;health.AttackResilience=resilience;f.Time.m_gameTime+=1;
                    var attack=(Attackment)Activator.CreateInstance(gunAttack,[target,shooter.Creature.Entity,new Vector3(0,61.6f,-4.7f),-Vector3.UnitZ,(float)(dynamic)hitsType.GetProperty("Total").GetValue(hits)]);
                    gunAttack.GetProperty("Hits").SetValue(attack,hits);C("ScDamageIndicator").GetMethod("AttackBody").Invoke(null,[attack]);return f.Audio.Played.ToList();}
                string Names(List<(string Name,float Volume)> l)=>string.Join(",",l.Select(x=>$"{x.Name}@{x.Volume:0.##}"));
                const string head="Audio/ScCsgoKnives/Hits/headshot_noarmor";
                var a=Deliver(Shot("Head"));Check(a.Count(x=>x.Name==head)==1&&a.Where(x=>x.Name!=head).All(x=>x.Volume==0),"a hurting head shot: "+Names(a));parts.Add("head "+Names(a));
                var b=Deliver(Shot("Body"));Check(b.All(x=>!x.Name.Contains("/Hits/")),"a body shot must keep only the native impact: "+Names(b));parts.Add("body "+Names(b));
                var c=Deliver(Shot("Head"),float.PositiveInfinity);Check(c.All(x=>!x.Name.Contains("/Hits/")),"a head shot that did no damage made a sound: "+Names(c));
                var d=Deliver(Shot("Body","Head","Body","Head","Arm"));Check(d.Count(x=>x.Name.Contains("/Hits/"))==1,"a mixed shotgun shot must sound once: "+Names(d));parts.Add("shotgun "+Names(d));
                var e=Deliver(Shot("Head"));f.Audio.Played.Clear();health.Health=1;f.Time.m_gameTime+=.03;
                var again=(Attackment)Activator.CreateInstance(gunAttack,[target,shooter.Creature.Entity,new Vector3(0,61.6f,-4.7f),-Vector3.UnitZ,2f]);gunAttack.GetProperty("Hits").SetValue(again,Shot("Head"));
                C("ScDamageIndicator").GetMethod("AttackBody").Invoke(null,[again]);Check(f.Audio.Played.All(x=>!x.Name.Contains("/Hits/")),"two head hits within 0.06 s sounded twice");
                return string.Join("; ",parts);
            });
            // current-direction-20260929 §1: protection is numbers in SubsystemScArmor: no item, clothing slot or visible gear.
            Type ArmorStateType=C("ScArmorState"),ArmorConfigType=C("ScArmorConfig");
            // dynamic, not object: a dynamic call binds an object-typed argument as object (no overload takes it).
            dynamic ArmorState(int config)=>ArmorStateType.GetMethod("For").Invoke(null,[Enum.ToObject(ArmorConfigType,config)]);
            dynamic ArmorDecode(string text){object[] a=[text,null];return (bool)ArmorStateType.GetMethod("TryDecode").Invoke(null,a)?a[1]:null;}
            string Enc(object state)=>(string)ArmorStateType.GetMethod("Encode").Invoke(state,null);
            int Config(object state)=>(int)ArmorStateType.GetProperty("Config").GetValue(state);
            dynamic ArmorOf(Project p)=>p.m_subsystems.First(x=>x.GetType()==C("SubsystemScArmor"));
            object ShotOf(params (string Part,float Power)[] regions){var h=Activator.CreateInstance(C("ScShotHits"));foreach(var r in regions)C("ScShotHits").GetMethod("Add").Invoke(h,[Enum.Parse(C("ScHitPart"),r.Part),r.Power,new Vector3(0,61.5f,.3f),-Vector3.UnitZ]);return h;}
            Attackment GunShot(ComponentBody target,Entity shooter,object hits){var gunAttack=C("ScSurvivalBalance").GetNestedType("GunAttack");
                var a=(Attackment)Activator.CreateInstance(gunAttack,[target,shooter,new Vector3(0,61.5f,.3f),-Vector3.UnitZ,(float)(dynamic)C("ScShotHits").GetProperty("Total").GetValue(hits)]);gunAttack.GetProperty("Hits").SetValue(a,hits);return a;}
            TestD("armor-values-configurations-costs-and-encoding",()=>{
                var rules=C("ScArmorRules");float Absorb(float p,int k,float left)=>(float)rules.GetMethod("Absorb").Invoke(null,[p,k,left]);
                Check(Absorb(10,0,150)==5&&Absorb(10,1,100)==6&&Absorb(10,1,3)==3&&Absorb(10,0,0)==0,"absorption: body 50%, head 60%, never above what is left");
                int Kind(string part)=>(int)rules.GetMethod("KindFor").Invoke(null,[Enum.Parse(C("ScHitPart"),part)]);
                Check(Kind("Head")==1&&Kind("Body")==0&&Kind("Arm")==0&&Kind("Leg")==-1,"coverage: head protection the head, body protection torso and arms, legs none");
                object none=ArmorState(0),half=ArmorState(1),full=ArmorState(2);
                Check(Enc(none)=="1|0,0,0|0,0,0"&&Enc(half)=="1|1,150,150|0,0,0"&&Enc(full)=="1|1,150,150|1,100,100"&&Config(none)==0&&Config(half)==1&&Config(full)==2,"configurations: "+Enc(none)+" "+Enc(half)+" "+Enc(full));
                var spent=ArmorDecode("1|1,0,150|1,40,100");string described=spent is null?"unreadable":(string)ArmorStateType.GetMethod("Describe").Invoke(spent,null);
                Check(spent is not null&&Config(spent)==2&&described.StartsWith("全甲")&&described.Contains("头部 40/100")&&described.Contains("躯干防护已耗尽"),"a used-up body protection must stay full armour with the real head value: "+described);
                foreach(var bad in new[]{"","1|1,151,150|0,0,0","1|0,5,0|0,0,0","2|1,1,1|0,0,0","1|1,-1,150|0,0,0","1|1,150,150","1|x,1,1|0,0,0","1|1,1,0|0,0,0","1|1,148,150,0|0,0,0","1|1,0,150,5|0,0,0","1|1,150,150,1000|0,0,0","1|0,0,0,5|0,0,0"})Check(ArmorDecode(bad) is null,"invalid protection text accepted: "+bad);
                // Exact wear (thousandths): a partly used unit is written as a fourth field and read back unchanged.
                var partial=ArmorDecode("1|1,148,150,500|1,1,100,700");Check(partial is not null&&Enc(partial)=="1|1,148,150,500|1,1,100,700"&&Math.Abs((float)((dynamic)partial).Vest.Remaining-147.5f)<1e-4f&&Math.Abs((float)((dynamic)partial).Helmet.Remaining-.3f)<1e-4f,"partial wear not kept exactly");
                var wb=C("ScArmorWorkbench");var ops=wb.GetNestedType("Operation");
                string Ops(object state)=>string.Join(",",((System.Collections.IEnumerable)wb.GetMethod("Available").Invoke(null,[state])).Cast<object>().Select(o=>o.ToString()));
                Check(Ops(none)=="MakeVest,MakeFull"&&Ops(half)=="AddHelmet"&&Ops(full)==""&&Ops(spent)=="Repair"&&Ops(ArmorDecode("1|0,0,0|1,100,100"))=="MakeVest",$"operations: none {Ops(none)}; half {Ops(half)}; full {Ops(full)}; used {Ops(spent)}");
                object Result(string op,object st)=>wb.GetMethod("Result").Invoke(null,[Enum.Parse(ops,op),st]);
                Check(Result("MakeFull",half)==null&&Result("AddHelmet",none)==null&&Result("MakeVest",full)==null&&Result("Repair",full)==null,"an operation outside the three configurations, or a repair of unused protection, was allowed");
                Check(Enc(Result("MakeVest",none))==Enc(half)&&Enc(Result("MakeFull",none))==Enc(full)&&Enc(Result("AddHelmet",half))==Enc(full)&&Enc(Result("Repair",spent))==Enc(full),"operation results");
                var ids=new Dictionary<string,int>{["ironingot"]=1,["copperingot"]=2,["canvas"]=3,["leather"]=4};
                string Cost(string op,object st){var d=(System.Collections.IDictionary)wb.GetMethod("Cost").Invoke(null,[Enum.Parse(ops,op),st,(Func<string,int>)(id=>ids[id])]);return string.Join(",",new[]{1,2,3,4}.Select(k=>d.Contains(k)?d[k]:0));}
                string R(string text)=>Cost("Repair",ArmorDecode(text));
                Check(Cost("MakeVest",none)=="8,4,6,4"&&Cost("AddHelmet",half)=="6,2,2,2"&&Cost("MakeFull",none)=="14,6,8,6",$"making costs (iron, copper, canvas, leather): body {Cost("MakeVest",none)}, head {Cost("AddHelmet",half)}, full {Cost("MakeFull",none)}");
                Check(R("1|1,150,150,1|0,0,0")=="1,1,1,1"&&R("1|1,148,150,500|0,0,0")=="1,1,1,1","a partly used unit is repaired for its rounded-up share: "+R("1|1,150,150,1|0,0,0")+" / "+R("1|1,148,150,500|0,0,0"));
                Check(R("1|1,0,150|0,0,0")=="4,2,3,2"&&R("1|1,150,150|1,0,100")=="3,1,1,1"&&R("1|1,75,150|0,0,0")=="2,1,2,1"&&R("1|1,0,150|1,0,100")=="7,3,4,3"&&R("1|1,150,150|1,100,100")=="0,0,0,0",
                    $"repair costs: body used up {R("1|1,0,150|0,0,0")}, head used up {R("1|1,150,150|1,0,100")}, half-used body {R("1|1,75,150|0,0,0")}, both used up {R("1|1,0,150|1,0,100")}, unused {R("1|1,150,150|1,100,100")}");
                return $"three configurations only; used-up body kept as full armour ({described}); make body 8,4,6,4 / head 6,2,2,2 / full 14,6,8,6; repair body {R("1|1,0,150|0,0,0")}, head {R("1|1,150,150|1,0,100")}, half body {R("1|1,75,150|0,0,0")}, both {R("1|1,0,150|1,0,100")}";
            });
            TestD("armor-workbench-commit-takes-every-material-or-nothing",()=>{
                var f=World();dynamic store=ArmorOf(f.P);var wb=C("ScArmorWorkbench");var ops=wb.GetNestedType("Operation");
                int M(int k)=>Terrain.MakeBlockValue(2,0,k);var ids=new Dictionary<string,int>{["ironingot"]=M(1),["copperingot"]=M(2),["canvas"]=M(3),["leather"]=M(4)};
                ComponentInventory Bag(ComponentInventory inv,int iron,int copper=10,int canvas=10,int leather=10){for(int i=0;i<8;i++)inv.m_slots.Add(new());if(iron>0)inv.AddSlotItems(1,M(1),iron);if(copper>0)inv.AddSlotItems(2,M(2),copper);if(canvas>0)inv.AddSlotItems(3,M(3),canvas);if(leather>0)inv.AddSlotItems(4,M(4),leather);return inv;}
                string Counts(ComponentInventory inv)=>string.Join(",",Enumerable.Range(1,4).Select(i=>inv.GetSlotCount(i)));
                object Quote(string key,string op,bool free=false)=>wb.GetMethod("Prepare").Invoke(null,[(object)store,key,Enum.Parse(ops,op),free,(Func<string,int>)(id=>ids[id])]);
                bool Commit(IInventory inv,object q)=>(bool)wb.GetMethod("TryCommit").Invoke(null,[inv,(object)store,q]);
                string State(string key)=>Enc(store.Get(key));
                bool Set(string key,string text)=>(bool)store.TryReplace(key,store.Get(key),ArmorDecode(text));
                var poor=Bag(new ComponentInventory(),7);Check(!Commit(poor,Quote("player-0","MakeVest"))&&Counts(poor)=="7,10,10,10"&&State("player-0")=="1|0,0,0|0,0,0","short of iron: "+Counts(poor)+" "+State("player-0"));
                var rich=Bag(new ComponentInventory(),14,6,8,6);Check(Commit(rich,Quote("player-0","MakeFull"))&&Counts(rich)=="0,0,0,0"&&State("player-0")=="1|1,150,150|1,100,100","the full package must take 14,6,8,6 once and give both protections: "+Counts(rich)+" "+State("player-0"));
                Check(Quote("player-0","MakeVest")==null&&Quote("player-0","AddHelmet")==null&&Quote("player-0","MakeFull")==null&&Quote("player-0","Repair")==null,"full unused armour was offered another operation");
                Check(Set("player-0","1|1,0,150|1,100,100"),"fixture wear");var stale=Quote("player-0","Repair");Check(Set("player-0","1|1,10,150|1,100,100"),"fixture second wear");
                var bag=Bag(new ComponentInventory(),10);Check(!Commit(bag,stale)&&Counts(bag)=="10,10,10,10"&&State("player-0")=="1|1,10,150|1,100,100","a quote for an earlier state committed: "+Counts(bag)+" "+State("player-0"));
                var fresh=Quote("player-0","Repair");var d=(System.Collections.IDictionary)fresh.GetType().GetProperty("Cost").GetValue(fresh);string want=string.Join(",",new[]{1,2,3,4}.Select(k=>10-(d.Contains(M(k))?(int)d[M(k)]:0)));
                Check(Commit(bag,fresh)&&State("player-0")=="1|1,150,150|1,100,100"&&Counts(bag)==want,$"the repair did not take exactly its quote: {Counts(bag)} instead of {want}");
                var stingy=(Stingy)Bag(new Stingy(),0);stingy.AddSlotItems(1,M(1),4);stingy.AddSlotItems(5,M(1),4);
                Check(!Commit(stingy,Quote("player-1","MakeVest"))&&stingy.GetSlotCount(1)==4&&stingy.GetSlotCount(5)==4&&Counts(stingy)=="4,10,10,10"&&State("player-1")=="1|0,0,0|0,0,0","a short removal was not put back: "+Counts(stingy)+" slot5 "+stingy.GetSlotCount(5));
                var empty=Bag(new ComponentInventory(),0,0,0,0);Check(Commit(empty,Quote("player-2","MakeVest",true))&&State("player-2")=="1|1,150,150|0,0,0","a creative (free) configuration was refused");
                Check(Quote("player-3","AddHelmet")==null,"head protection offered without body protection");
                Check(Set("player-4","1|1,150,150|0,0,0")&&Commit(Bag(new ComponentInventory(),6,2,2,2),Quote("player-4","AddHelmet"))&&State("player-4")=="1|1,150,150|1,100,100","half armour did not become full with the head cost");
                return $"short: nothing taken; full package 14,6,8,6 once; stale quote refused; repair took its quote ({want} left of 10 each); short removal put back; creative free; half -> full for 6,2,2,2";
            });
            TestD("armor-store-saves-keeps-unreadable-refuses-future-and-death-rule",()=>{
                var f=World();dynamic store=ArmorOf(f.P);
                Check((bool)store.TryCreate("player-0",ArmorDecode("1|1,37,150|1,5,100"))&&(bool)store.TryCreate("enemy-abc-1",ArmorState(1))&&!(bool)store.TryCreate("enemy-abc-1",ArmorState(2)),"create, and create only once");
                var saved=new ValuesDictionary();store.Save(saved);saved.GetValue<ValuesDictionary>("Wearers").SetValue("player-7","9|future");string xml=null;
                for(int round=0;round<2;round++){
                    var again=(Subsystem)Activator.CreateInstance(C("SubsystemScArmor"));again.m_project=f.P;again.Load(Round(saved));var next=new ValuesDictionary();again.Save(next);
                    var x=new XElement("Values");next.Save(x);xml??=x.ToString();Check(x.ToString()==xml,"the second save differs");saved=next;dynamic a=again;
                    Check(Enc(a.Get("player-0"))=="1|1,37,150|1,5,100"&&Enc(a.Get("enemy-abc-1"))=="1|1,150,150|0,0,0","values changed in a save/load round: "+x);
                    Check(!(bool)a.Readable("player-7")&&!(bool)a.TryCreate("player-7",ArmorState(2))&&saved.GetValue<ValuesDictionary>("Wearers").GetValue<string>("player-7")=="9|future","an unreadable entry was overwritten or dropped");
                }
                bool refused=false;try{((Subsystem)Activator.CreateInstance(C("SubsystemScArmor"))).Load(new ValuesDictionary{{"Schema",2}});}catch(Exception e) when(e is InvalidOperationException or TargetInvocationException){refused=true;}
                Check(refused,"a newer protection schema was loaded");
                var player=Blank<ComponentPlayer>();player.PlayerData=Blank<PlayerData>();
                store.PlayerDied(player,false);Check(Config(store.Get("player-0"))==2,"a death that keeps the inventory cleared the protection");
                store.PlayerDied(player,true);Check(Config(store.Get("player-0"))==0&&Config(store.Get("enemy-abc-1"))==1,"a dropping death did not clear the player's protection, or cleared another wearer's");
                return "two save/load rounds identical; unreadable entry kept verbatim and never overwritten; schema 2 refused; keep-inventory death keeps, dropping death clears";
            });
            TestD("armor-regions-once-sounds-and-native-clothing-after",()=>{
                var parts=new List<string>();var f=World();dynamic store=ArmorOf(f.P);C("ScHitSounds").GetMethod("Initialize").Invoke(null,null);
                var e=Enemy(f.P,1,12);string key=(string)e.Enemy.ArmorKey;store.Remove(key);Check((bool)store.TryCreate(key,ArmorState(2)),"fixture armour");
                var health=(ComponentHealth)e.Creature.ComponentHealth;health.AttackResilience=100;health.AttackResilienceFactor=1;health.Health=1;var shooter=Enemy(f.P,1,13).Creature.Entity;
                void Deliver(Attackment a)=>C("ScDamageIndicator").GetMethod("AttackBody").Invoke(null,[a]);
                // Planned when the injury is computed, committed only when the engine applies it (current-direction-20260929 §3).
                var mixed=GunShot(e.Creature.ComponentBody,shooter,ShotOf(("Head",10),("Body",10),("Leg",10)));float injury=mixed.CalculateInjuryAmount(),again=mixed.CalculateInjuryAmount();
                Check(Math.Abs(injury-.19f)<1e-4f&&again==injury&&Enc(store.Get(key))=="1|1,150,150|1,100,100",$"a query must plan (0.19) but not wear: {injury}/{again}, values {Enc(store.Get(key))}");
                Deliver(mixed);Check(Math.Abs(health.Health-.81f)<1e-4f&&Enc(store.Get(key))=="1|1,145,150|1,94,100",$"the applied injury must commit head -6, body -5 once: health {health.Health}, values {Enc(store.Get(key))}");
                float third=mixed.CalculateInjuryAmount();Check(third==injury&&Enc(store.Get(key))=="1|1,145,150|1,94,100","a query after the commit wore again");
                dynamic absorbed=mixed.GetType().GetProperty("Absorbed").GetValue(mixed);Check((float)absorbed.Helmet==6&&(float)absorbed.Vest==5,"absorption not recorded on the attack");
                parts.Add($"full armour mixed shot: planned {injury:0.###}, committed once on the applied injury (head 100->94, body 150->145)");
                // Invulnerable: the injury is not applied, nothing is worn and nothing is heard.
                store.TryReplace(key,store.Get(key),ArmorState(2));health.IsInvulnerable=true;f.Audio.Played.Clear();f.Time.m_gameTime+=1;
                Deliver(GunShot(e.Creature.ComponentBody,shooter,ShotOf(("Head",5),("Body",5))));health.IsInvulnerable=false;
                Check(Enc(store.Get(key))=="1|1,150,150|1,100,100"&&f.Audio.Played.All(x=>!x.Name.Contains("/Hits/")),"an invulnerable target's protection was worn or sounded: "+Enc(store.Get(key)));
                List<string> Sounds(string text,params (string Part,float Power)[] regions){if(text is not null)store.TryReplace(key,store.Get(key),ArmorDecode(text));f.Audio.Played.Clear();f.Time.m_gameTime+=1;health.Health=1;
                    Deliver(GunShot(e.Creature.ComponentBody,shooter,ShotOf(regions)));return f.Audio.Played.Select(x=>x.Name).Where(n=>n.Contains("/Hits/")).ToList();}
                const string bare="Audio/ScCsgoKnives/Hits/headshot_noarmor",helmet="Audio/ScCsgoKnives/Hits/helmet_dink",kevlar="Audio/ScCsgoKnives/Hits/kevlar";
                var s1=Sounds(null,("Head",5));var s2=Sounds(null,("Body",5));Check(s1.SequenceEqual([helmet])&&s2.SequenceEqual([kevlar]),"full armour: head "+string.Join(",",s1)+" body "+string.Join(",",s2));
                var s3=Sounds("1|1,150,150|0,0,0",("Head",5));Check(s3.SequenceEqual([bare])&&Enc(store.Get(key))=="1|1,150,150|0,0,0","half armour head shot: "+string.Join(",",s3));
                var s4=Sounds("1|1,150,150|1,0,100",("Head",5));var s5=Sounds(null,("Body",5));Check(s4.SequenceEqual([bare])&&s5.SequenceEqual([kevlar])&&Enc(store.Get(key))=="1|1,148,150,500|1,0,100","used-up head protection: head "+string.Join(",",s4)+" body "+string.Join(",",s5)+" "+Enc(store.Get(key)));
                var s6=Sounds("1|1,0,150|1,100,100",("Body",5));var s7=Sounds(null,("Head",5));Check(s6.Count==0&&s7.SequenceEqual([helmet])&&Enc(store.Get(key)).EndsWith("|1,97,100"),"used-up body protection with head left: body "+string.Join(",",s6)+" head "+string.Join(",",s7)+" "+Enc(store.Get(key)));
                var s8=Sounds("1|1,150,150|1,100,100",("Leg",5));Check(s8.Count==0&&Enc(store.Get(key))=="1|1,150,150|1,100,100","a leg shot wore protection or sounded");
                var shotgun=Sounds(null,("Head",2),("Body",2),("Body",2),("Arm",2),("Leg",2));Check(shotgun.Count==1&&shotgun[0]==helmet&&Enc(store.Get(key))=="1|1,147,150|1,99,100,200","a mixed shotgun shot: "+string.Join(",",shotgun)+" "+Enc(store.Get(key)));
                // A last sliver: this shot absorbs what is left (dink), the next one finds none (bare head).
                var sliver=Sounds("1|1,150,150|1,1,100,700",("Head",5));var after=Sounds(null,("Head",5));
                Check(sliver.SequenceEqual([helmet])&&after.SequenceEqual([bare])&&Enc(store.Get(key)).EndsWith("|1,0,100"),"the last sliver: "+string.Join(",",sliver)+" then "+string.Join(",",after)+" "+Enc(store.Get(key)));
                f.Audio.Played.Clear();f.Time.m_gameTime+=1;store.TryReplace(key,store.Get(key),ArmorState(2));var blocked=GunShot(e.Creature.ComponentBody,shooter,ShotOf(("Head",5)));blocked.AttackPower=0;Deliver(blocked);
                Check(f.Audio.Played.All(x=>!x.Name.Contains("/Hits/"))&&Enc(store.Get(key))=="1|1,150,150|1,100,100","a blocked shot wore protection or sounded");
                var old=Enemy(f.P,3,77);var oh=(ComponentHealth)old.Creature.ComponentHealth;oh.AttackResilience=100;oh.AttackResilienceFactor=1;
                float plain=GunShot(old.Creature.ComponentBody,shooter,ShotOf(("Head",10),("Body",10))).CalculateInjuryAmount();Check(Math.Abs(plain-.2f)<1e-4f&&Config(store.Get((string)old.Enemy.ArmorKey))==0,"an enemy without an entry was protected: "+plain);
                parts.Add("sounds: full head dink / body kevlar; half head bare; used-up head bare (body kevlar, exact wear 147.5); used-up body silent (head dink); legs none; shotgun one dink at 98.8 head; the last sliver dinks once, then bare; blocked, invulnerable and old enemy nothing");
                // A player: CS protection first, then the native clothing pass exactly once on what is left (any mod's clothing).
                if(ClothingSlot.ClothingSlotsByInt.Count<4){ClothingSlot.ClothingSlots.Clear();ClothingSlot.ClothingSlotsByInt.Clear();foreach(var n in new[]{"Head","Torso","Legs","Feet"})ClothingSlot.AddClothingSlot(n);}
                const int ClothIndex=723;BlocksManager.Blocks[ClothIndex]=new Cloth{BlockIndex=ClothIndex};
                var body=new ComponentBody{Position=new Vector3(0,60,-5),BoxSize=new Vector3(.65f,1.8f,.65f),Mass=75};var ph=new Health{Health=1,AttackResilience=100,AttackResilienceFactor=1};
                var pc=new ComponentCreature{ComponentBody=body,ComponentHealth=ph};ph.m_componentCreature=pc;
                var clothing=Blank<ComponentClothing>();clothing.m_componentBody=body;clothing.m_subsystemGameInfo=f.Info;
                var rfield=typeof(ComponentClothing).GetField("m_random");rfield.SetValue(clothing,Activator.CreateInstance(rfield.FieldType));
                clothing.m_clothes=ClothingSlot.ClothingSlotsByInt.Values.ToDictionary(slot=>slot,_=>new List<int>{Terrain.MakeBlockValue(ClothIndex)});clothing.InsulationBySlots=ClothingSlot.ClothingSlotsByInt.Values.ToDictionary(slot=>slot,slot=>slot.BasicInsulation);
                var who=Blank<ComponentPlayer>();who.PlayerData=Blank<PlayerData>();E(f.P,body,ph,pc,clothing,who);
                Check((bool)store.TryCreate("player-0",ArmorState(2)),"fixture player armour");
                Cloth.Calls=0;var ps=GunShot(body,shooter,ShotOf(("Head",10),("Body",10),("Leg",10)));float pi=ps.CalculateInjuryAmount();Deliver(ps);
                Check(Cloth.Calls==1&&Math.Abs(pi-.152f)<1e-4f&&Math.Abs(ph.Health-(1-.152f))<1e-4f&&Enc(store.Get("player-0"))=="1|1,145,150|1,94,100",$"player: CS 30 -> 19 then clothing x0.8 once = 0.152: injury {pi}, health {ph.Health}, clothing asked {Cloth.Calls} time(s), values {Enc(store.Get("player-0"))}");
                var factors=Blank<ComponentFactors>();factors.m_entity=body.Entity;body.Entity.m_components.Add(factors);var rf2=typeof(ComponentFactors).GetProperty("ResilienceFactor");var factorParts=new List<string>();
                foreach(float factor in new[]{.8f,1.25f,2f}){rf2.SetValue(factors,factor);store.TryReplace("player-0",store.Get("player-0"),ArmorState(2));Cloth.Calls=0;
                    float fi=GunShot(body,shooter,ShotOf(("Head",10),("Body",10),("Leg",10))).CalculateInjuryAmount();Check(Math.Abs(fi-.152f/factor)<1e-4f&&Cloth.Calls==1,$"resilience factor {factor}: injury {fi} instead of {.152f/factor}");factorParts.Add($"{factor}:{fi:0.####}");}
                body.Entity.m_components.Remove(factors);store.TryReplace("player-0",store.Get("player-0"),ArmorState(2));
                f.Info.WorldSettings.GameMode=GameMode.Creative;ph.Health=1;Deliver(GunShot(body,shooter,ShotOf(("Head",10))));f.Info.WorldSettings.GameMode=GameMode.Survival;
                Check(Enc(store.Get("player-0"))=="1|1,150,150|1,100,100","a creative player's protection was worn");
                parts.Add($"player with clothing: CS first (30 -> 19), clothing once (x0.8) = {pi:0.###}, committed on the applied injury; resilience factor once ({string.Join(" ",factorParts)}); creative wears nothing");
                return string.Join("; ",parts);
            });
            // current-direction-20260929 §3: ordinary melee and other projectiles, through the ProcessAttackment seam.
            TestD("armor-channels-melee-projectile-exact-wear-and-unknown-channels",()=>{
                var parts=new List<string>();var f=World();dynamic store=ArmorOf(f.P);C("ScHitSounds").GetMethod("Initialize").Invoke(null,null);
                var before=C("SubsystemScArmor").GetMethod("BeforeNative");
                var e=Enemy(f.P,1,12);string key=(string)e.Enemy.ArmorKey;store.Remove(key);Check((bool)store.TryCreate(key,ArmorState(2)),"fixture armour");
                var health=(ComponentHealth)e.Creature.ComponentHealth;health.AttackResilience=100;health.AttackResilienceFactor=1;var bear=Enemy(f.P,3,40).Creature.Entity;var target=e.Creature.ComponentBody;
                float Hit(Attackment a){health.Health=1;f.Time.m_gameTime+=1;before.Invoke(null,[a]);ComponentMiner.AttackBody(a);return 1-health.Health;}
                // Melee: body 25%, never the head; the native pass then works on the rest.
                float melee=Hit(new MeleeAttackment(target,bear,target.Position+Vector3.UnitY*1.6f,-Vector3.UnitZ,20));
                Check(Math.Abs(melee-.15f)<1e-4f&&Enc(store.Get(key))=="1|1,145,150|1,100,100",$"melee 20: injury {melee} (15/100), values {Enc(store.Get(key))}");
                // Exact wear: 400 hits of 1 (absorbing 0.25 each) wear the same 100 as one big hit.
                store.TryReplace(key,store.Get(key),ArmorState(2));for(int i=0;i<400;i++)Hit(new MeleeAttackment(target,bear,target.Position,-Vector3.UnitZ,1));
                Check(Enc(store.Get(key))=="1|1,50,150|1,100,100","400 small melee hits must wear exactly 100: "+Enc(store.Get(key)));
                // A projectile without trusted geometry: body 40%.
                store.TryReplace(key,store.Get(key),ArmorState(2));float arrow=Hit(new ProjectileAttackment(target,bear,target.Position+Vector3.UnitY,-Vector3.UnitZ,10,null));
                Check(Math.Abs(arrow-.06f)<1e-4f&&Enc(store.Get(key))=="1|1,146,150|1,100,100",$"projectile 10 on the body: injury {arrow}, values {Enc(store.Get(key))}");
                // A projectile whose hit geometry says head: 50% of the head protection, the dink.
                var seam=C("ScGunHitTest").GetField("PoseProvider");var previous=seam.GetValue(null);
                var headBox=Activator.CreateInstance(C("ScPartBox"),[new BoundingBox(new Vector3(-.3f,1.4f,-.3f),new Vector3(.3f,1.9f,.3f)),Matrix.CreateTranslation(target.Position),true,Enum.Parse(C("ScHitPart"),"Head")]);
                var bodyBox=Activator.CreateInstance(C("ScPartBox"),[new BoundingBox(new Vector3(-.3f,0,-.3f),new Vector3(.3f,1.4f,.3f)),Matrix.CreateTranslation(target.Position),false,Enum.Parse(C("ScHitPart"),"Body")]);
                var boxes=Array.CreateInstance(C("ScPartBox"),2);boxes.SetValue(headBox,0);boxes.SetValue(bodyBox,1);
                var provider=System.Linq.Expressions.Expression.Lambda(seam.FieldType,System.Linq.Expressions.Expression.Constant(boxes),System.Linq.Expressions.Expression.Parameter(typeof(ComponentBody))).Compile();
                seam.SetValue(null,provider);
                try{
                    store.TryReplace(key,store.Get(key),ArmorState(2));f.Audio.Played.Clear();
                    float head=Hit(new ProjectileAttackment(target,bear,target.Position+new Vector3(0,1.7f,.3f),-Vector3.UnitZ,10,null));
                    Check(Math.Abs(head-.05f)<1e-4f&&Enc(store.Get(key))=="1|1,150,150|1,95,100"&&f.Audio.Played.Count(x=>x.Name=="Audio/ScCsgoKnives/Hits/helmet_dink")==1,$"projectile 10 on the head: injury {head}, values {Enc(store.Get(key))}, sounds {string.Join(",",f.Audio.Played.Select(x=>x.Name))}");
                }finally{seam.SetValue(null,previous);}
                // Melee never gives a CS sound; unknown channels (a plain Attackment, fire, falls) are untouched.
                store.TryReplace(key,store.Get(key),ArmorState(2));f.Audio.Played.Clear();Hit(new MeleeAttackment(target,bear,target.Position,-Vector3.UnitZ,8));
                Check(f.Audio.Played.All(x=>!x.Name.Contains("/Hits/")),"a melee hit played CS hit feedback");
                store.TryReplace(key,store.Get(key),ArmorState(2));health.Health=1;health.Injure(.3f,null,false,"fall");Check(Enc(store.Get(key))=="1|1,150,150|1,100,100"&&Math.Abs(health.Health-.7f)<1e-5f,"a direct injury used protection");
                var plainAttack=new Attackment(target,bear,target.Position,-Vector3.UnitZ,10);Check(Math.Abs(Hit(plainAttack)-.1f)<1e-4f&&Enc(store.Get(key))=="1|1,150,150|1,100,100","an unclassified attack used protection");
                parts.Add($"melee 20 -> {melee:0.###} (body 25%); 400 x melee 1 wore exactly 100; projectile body 40% ({arrow:0.###}), head 50% with dink ({0.05:0.###}); falls, direct injuries and unclassified attacks untouched");
                return string.Join("; ",parts);
            });
            TestD("enemy-armor-drawn-once-three-configurations-kept-through-sleep-save-and-death",()=>{
                var parts=new List<string>();var draw=T("SubsystemTacticalEnemies").GetMethod("DrawArmor");
                string Seq(int seed){var r=new Engine.Random(seed);return string.Join("",Enumerable.Range(0,40).Select(_=>(int)draw.Invoke(null,[r])));}
                Check(Seq(42)==Seq(42)&&Seq(42)!=Seq(43),"draws are not reproducible from a fixed seed");
                var counts=new int[4];var many=new Engine.Random(20260929);for(int i=0;i<3000;i++)counts[(int)draw.Invoke(null,[many])]++;
                Check(counts[3]==0&&counts.Take(3).All(c=>c is >880 and <1120),$"3000 draws: none {counts[0]}, half {counts[1]}, full {counts[2]}, head only {counts[3]}");
                parts.Add($"seed 42 {Seq(42)[..12]}...; 3000 draws none/half/full {counts[0]}/{counts[1]}/{counts[2]}, head only 0");
                var old=DatabaseManager.m_valueDictionaries.GetValueOrDefault("ScTacticalEnemy");DatabaseManager.m_valueDictionaries["ScTacticalEnemy"]=new ValuesDictionary();
                try{
                    string Squads(int seed){
                        var f=World();var p=(SpawnProject)f.P;dynamic store=ArmorOf(p);
                        BlocksManager.Blocks[0]=new AirBlock{IsCollidable=false};BlocksManager.Blocks[2]=new DirtBlock{BlockIndex=2,IsCollidable=true};
                        for(int cx=0;cx<4;cx++)for(int cz=0;cz<4;cz++)f.Terrain.Terrain.AllocateChunk(cx,cz).State=TerrainChunkState.Valid;
                        for(int x=1;x<63;x++)for(int z=1;z<63;z++){f.Terrain.Terrain.SetCellValueFast(x,60,z,2);f.Terrain.Terrain.SetTopHeight(x,z,60);}
                        var player=Blank<ComponentPlayer>();player.ComponentBody=new ComponentBody{Position=new Vector3(8.5f,61,8.5f),BoxSize=new Vector3(.65f,1.8f,.65f)};E(p,player,player.ComponentBody);p.FindSubsystem<SubsystemPlayers>(true).m_componentPlayers.Add(player);f.Bodies.AddBody(player.ComponentBody);
                        p.Factory=()=>{var en=Enemy(p).Creature.Entity;p.m_entities.Remove(en);en.m_isAddedToProject=false;return en;};
                        p.Added=en=>{f.Director.OnEntityAdded(en);f.Bodies.AddBody(en.FindComponent<ComponentBody>(true));};p.Removed=en=>{f.Director.OnEntityRemoved(en);f.Bodies.RemoveBody(en.FindComponent<ComponentBody>(true));};
                        Set((object)f.Director,"random",new Engine.Random(seed));
                        var codes=new List<string>();
                        foreach(int size in new[]{5,5,3}){
                            Check((int)f.Director.SpawnManual(new Point3(12,60,8),size)==size,"summoned squad not created");f.Time.m_gameTime+=100;
                        }
                        var members=((System.Collections.IEnumerable)f.Director.Enemies).Cast<dynamic>().ToArray();
                        foreach(var m in members)codes.Add(Config(store.Get((string)m.ArmorKey)).ToString());
                        Check(codes.Count==13&&codes.All(c=>c is "0" or "1" or "2"),"not one configuration per member: "+string.Join("",codes));
                        // One member: damaged, then sleep (native despawn record), woken as a new entity, saved and loaded twice.
                        var full=members.FirstOrDefault(m=>Config(store.Get((string)m.ArmorKey))==2);
                        if(full is not null){string k=(string)full.ArmorKey;store.TryReplace(k,store.Get(k),ArmorDecode("1|1,0,150|1,60,100"));
                            var data=new SpawnEntityData{Data="other|x"};T("SubsystemTacticalEnemies").GetMethod("SaveSpawn").Invoke(null,[((ComponentCreature)full.Creature).ComponentSpawn,data]);
                            var woke=Enemy(p,1,30);T("SubsystemTacticalEnemies").GetMethod("ReadSpawn").Invoke(null,[woke.Creature.Entity,data]);
                            Check((string)woke.Enemy.ArmorKey==k&&Enc(store.Get(k))=="1|1,0,150|1,60,100","sleep and wake-up changed the protection: "+Enc(store.Get(k)));
                            var saved=new ValuesDictionary();store.Save(saved);for(int round=0;round<2;round++){var again=(Subsystem)Activator.CreateInstance(C("SubsystemScArmor"));again.m_project=p;again.Load(Round(saved));saved=new ValuesDictionary();again.Save(saved);
                                Check(Enc(((dynamic)again).Get(k))=="1|1,0,150|1,60,100","a save/load round refilled or re-drew the protection");}
                            Check(!(bool)store.TryCreate(k,ArmorState(2))&&Enc(store.Get(k))=="1|1,0,150|1,60,100","an existing enemy was drawn again");
                            int before=f.Drops.Items.Count;((ComponentCreature)full.Creature).ComponentHealth.Health=0;full.Died();full.Died();
                            Check(Config(store.Get(k))==0&&f.Drops.Items.Skip(before).All(i=>Terrain.ExtractContents(i.Value)!=0),"death left the protection or dropped something unexpected");}
                        // Pruning: a sleeping member keeps its entry, an entry of an enemy that exists nowhere goes.
                        var sleeper=members.First(m=>(int)m.Creature.ComponentHealth.Health>0||(float)m.Creature.ComponentHealth.Health>0);string sk=(string)sleeper.ArmorKey;store.Remove(sk);store.TryCreate(sk,ArmorState(1));
                        var record=new SpawnEntityData{TemplateName="ScTacticalEnemy",Position=new Vector3(40,61,40),Data=""};T("SubsystemTacticalEnemies").GetMethod("SaveSpawn").Invoke(null,[((ComponentCreature)sleeper.Creature).ComponentSpawn,record]);
                        f.Sleeping.GetOrCreateSpawnChunk(new Point2(2,2)).SpawnsData.Add(record);f.Director.Enemies.Remove(sleeper);
                        store.TryCreate("enemy-ffffffffffffffffffffffffffffffff-2",ArmorState(2));
                        int pruned=(int)f.Director.PruneArmor();Check(pruned==1&&Config(store.Get("enemy-ffffffffffffffffffffffffffffffff-2"))==0&&Config(store.Get(sk))==1,$"prune removed {pruned}; sleeping member kept {Config(store.Get(sk))}");
                        record.Data="garbage";store.TryCreate("enemy-eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee-2",ArmorState(2));
                        Check((int)f.Director.PruneArmor()==0&&Config(store.Get("enemy-eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee-2"))==2,"pruned while a sleeping record was unreadable");
                        return string.Join("",codes);
                    }
                    string a=Squads(7),b=Squads(7),c=Squads(8);
                    Check(a==b&&a!=c,$"squad draws not reproducible from the director seed: {a} / {b} / {c}");
                    var all=(a+c+Squads(9)+Squads(10)).GroupBy(ch=>ch).ToDictionary(g=>g.Key,g=>g.Count());
                    Check(all.Keys.Count==3,"three configurations not all seen over 52 members: "+string.Join(",",all.Select(kv=>kv.Key+"="+kv.Value)));
                    parts.Add($"summoned squads (5,5,3) seed 7: {a}, seed 8: {c} (0 none, 1 half, 2 full; mixed squads, none forced complete); damaged full armour kept 0/150 + 60/100 through sleep, wake-up and two saves; never re-drawn; death ends it; entries of vanished enemies pruned, sleeping ones kept, nothing pruned while a record is unreadable");
                }finally{if(old==null)DatabaseManager.m_valueDictionaries.Remove("ScTacticalEnemy");else DatabaseManager.m_valueDictionaries["ScTacticalEnemy"]=old;}
                return string.Join("; ",parts);
            });
            TestD("player-hit-regions-follow-the-eye-and-hostile-head-counts-1.25",()=>{
                // A player: head on the first-person eye, torso, legs; crouching lowers the head; lying down has none.
                var f=World();var body=new ComponentBody{Position=new Vector3(0,60,0),BoxSize=new Vector3(.65f,1.77f,.65f),Mass=75};body.Rotation=Quaternion.Identity;
                var health=new Health{Health=1,AttackResilience=100,AttackResilienceFactor=1};var creature=new ComponentCreature{ComponentBody=body,ComponentHealth=health};health.m_componentCreature=creature;
                var who=Blank<ComponentPlayer>();who.PlayerData=Blank<PlayerData>();var model=Blank<ComponentHumanModel>();model.m_componentCreature=creature;
                E(f.P,body,health,creature,who,model);f.Bodies.AddBody(body);
                object Ray(float y,float z=-3)=>C("ScGunHitTest").GetMethod("Raycast").Invoke(null,[new List<ComponentBody>{body},null,new Vector3(0,y,z),Vector3.Normalize(new Vector3(0,0,-z)),10f]);
                string Part(object hit)=>hit?.GetType().GetProperty("Part").GetValue(hit).ToString()??"miss";
                float standEye=60+.875f*1.77f;var frame=typeof(Time).GetProperty("FrameIndex");
                void Eye(float y){Set(model,"m_eyePosition",(Vector3?)new Vector3(0,y,0));frame.SetValue(null,Time.FrameIndex+1);}
                Eye(standEye);var stand=new[]{Part(Ray(standEye+.1f)),Part(Ray(60+1.1f)),Part(Ray(60+.4f)),Part(Ray(standEye+.4f))};
                Check(stand.SequenceEqual(new[]{"Head","Body","Leg","miss"}),"standing: head, chest, thigh and above the head resolve to "+string.Join(",",stand));
                float crouchEye=60+.45f*.875f*1.77f;Eye(crouchEye);var crouched=new[]{Part(Ray(crouchEye+.05f)),Part(Ray(standEye+.1f))};
                Check(crouched[0]=="Head"&&crouched[1]!="Head","crouched: the head did not follow the eye: "+string.Join(",",crouched));
                Eye(60+.2f*.875f*1.77f);Check(Part(Ray(60+.2f))=="Body","lying down: not the body fallback: "+Part(Ray(60+.2f)));
                // Hostile fire: with the eye at the aim point, head pellets cost x1.25 of a body pellet.
                Eye(60+.9f);var e=Enemy(f.P,1);var specs=(Array)C("GunSpec").GetField("All").GetValue(null);int ak=-1;for(int i=0;i<specs.Length;i++)if((string)((dynamic)specs.GetValue(i)).Name=="ak47")ak=i;
                e.Enemy.State.Variant=ak;e.Enemy.State.Rounds=1000;e.Creature.ComponentBody.Position=new Vector3(0,60,6);f.Bodies.AddBody(e.Creature.ComponentBody);e.Enemy.Alert(body);
                var shoot=T("ComponentTacticalEnemy").GetMethod("Shoot",Fields);var losses=new List<float>();
                for(int i=0;i<200;i++){health.Health=1;frame.SetValue(null,Time.FrameIndex+1);Set(model,"m_eyePosition",(Vector3?)new Vector3(0,60.9f,0));shoot.Invoke((object)e.Enemy,null);if(1-health.Health>1e-6f)losses.Add(1-health.Health);}
                var kinds=losses.Select(l=>MathF.Round(l*10000)/10000).Distinct().OrderBy(l=>l).ToArray();
                Check(kinds.Contains(.022f)&&kinds.Contains(.0275f)&&kinds.All(k=>k is .022f or .0275f),"hostile AK hits on a player are not 2.2 body / 2.75 head: "+string.Join(",",kinds));
                return $"stand {string.Join(",",stand)}; crouched {string.Join(",",crouched)}; AK losses {string.Join(",",kinds)} over {losses.Count} hits";
            });
            Test("hostile-grenade-unlimited-count-fuse-smoke-visibility-and-player-damage-filter",()=>{
                var f=World();var g=f.P.m_subsystems.Single(s=>s.GetType()==C("SubsystemScGrenades"));dynamic grenade=g;
                // Published feedback 1.7.1 (now public 1.3.0) removed count caps; validate input instead.
                for(int i=0;i<5;i++)Check(grenade.TryThrowHostile(i,new Vector3(0,61,0),new Vector3(0,4,-12)),"legal throw refused");
                Check(!grenade.TryThrowHostile(6,Vector3.Zero,Vector3.One)&&!grenade.TryThrowHostile(-1,Vector3.Zero,Vector3.One)&&!grenade.TryThrowHostile(0,new Vector3(float.NaN,0,0),Vector3.One),"invalid kind/position accepted");
                var states=((System.Collections.IEnumerable)C("SubsystemScGrenades").GetField("m_active",Fields).GetValue(g)).Cast<object>().ToArray();dynamic fire=states[3];Check(fire.Remaining==3f&&fire.Owner==-2,"wrong enemy fire fuse/owner");
                var player=Blank<ComponentPlayer>();var body=new ComponentBody();E(f.P,player,body);var friendly=C("SubsystemScGrenades").GetMethod("Friendly",Fields);Check((bool)friendly.Invoke(g,[states[0],body]),"enemy grenade immunity inherited player friendly-fire setting");
                dynamic smoke=states[2];smoke.Effect=true;smoke.Age=5f;smoke.Remaining=10f;smoke.Position=new Vector3(0,61,0);Check(grenade.SmokeBlocksSight(new Vector3(-4,61,0),new Vector3(4,61,0)),"NPC ignores dense smoke");
            });
            Test("death-once-empty-inventory-and-full-registry-fallback",()=>{
                int exhausted=(int)C("GunSpec").GetField("LastId").GetRawConstantValue()+1;
                FreshRegistry();C("ScGunRegistry").GetProperty("Next").SetValue(rf.GetValue(null),exhausted);var f=World();int material=0;
                for(int i=0;i<160;i++){var e=Enemy(f.P,1,i);e.Creature.ComponentHealth.Health=0;e.Enemy.Died();int count=f.Drops.Items.Count;e.Enemy.Died();((IUpdateable)e.Enemy).Update(.1f);Check(f.Drops.Items.Count==count&&e.Enemy.State.LootDone&&Enumerable.Range(0,5).All(s=>e.Inv.GetSlotCount(s)==0),"death duplicated reward/left inventory");}
                Check(f.Drops.Items.Count>50&&f.Drops.Items.All(i=>Terrain.ExtractContents(i.Value)!=701),"full registry drops malformed gun");material=f.Drops.Items.Count(i=>Terrain.ExtractContents(i.Value)==708);Check(material>0&&(int)C("ScGunRegistry").GetProperty("Next").GetValue(rf.GetValue(null))==exhausted,"registry wrap/missing material reward");
            });
            Test("supplies-and-chicken-eggs-without-gun-drops-or-registry-allocation",()=>{
                FreshRegistry();var f=World();
                for(int i=0;i<160;i++){var e=Enemy(f.P,1,i);e.Creature.ComponentHealth.Health=0;e.Enemy.State.Rounds=2;e.Enemy.Died();}
                Check(f.Drops.Items.All(p=>Terrain.ExtractContents(p.Value)!=701),"enemy dropped a gun");
                int egg=BlocksManager.BlockTypeToIndex[C("ScChickenEggBlock")];
                Check(f.Drops.Items.Any(p=>Terrain.ExtractContents(p.Value)==egg)&&f.Drops.Items.Any(p=>Terrain.ExtractContents(p.Value)==708)
                    &&f.Drops.Items.Any(p=>Terrain.ExtractContents(p.Value)==702)&&f.Drops.Items.Any(p=>Terrain.ExtractContents(p.Value)<700),"missing eggs/ammo/parts/basic materials");
                Check((int)C("ScGunRegistry").GetProperty("Next").GetValue(rf.GetValue(null))==1,"loot consumed registry numbers");
            });
            Test("defuse-10s-5s-deadline-ties-low-fps-and-interruption",()=>{
                foreach(bool kit in new[]{false,true}){float seconds=kit?5:10;dynamic clock=New("TacticalDefuseClock",kit);Check(clock.Duration==seconds&&clock.Enough(seconds+.1f)&&!clock.Enough(seconds),"wrong duration/warning boundary");
                    Check(clock.Advance(40f,seconds-1,true).ToString()=="Active"&&clock.Advance(41-seconds,1f,true).ToString()=="Defused","normal completion");
                    clock=New("TacticalDefuseClock",kit);Check(clock.Advance(seconds,seconds+2,true).ToString()=="Exploded","tie incorrectly defused");clock=New("TacticalDefuseClock",kit);Check(clock.Advance(seconds+1,seconds+2,true).ToString()=="Defused","large frame resolved in update order");
                    clock=New("TacticalDefuseClock",kit);clock.Advance(40f,2f,true);Check(clock.Advance(38f,1f,false).ToString()=="Cancelled","release/modal/move validation not cancelling");}
                dynamic press=New("TacticalDefusePress");Check(press.Step(true,true)&&!press.Step(true,true),"held press repeats");press.Cancel();Check(!press.Step(true,true),"cancel instantly restarts");press.Step(false,false);Check(press.Step(true,true),"release does not rearm");press.Step(false,false);press.Step(true,false);Check(!press.Step(true,true),"enter target while already held starts defusing");
            });
            Test("bomb-subsystem-persist-two-xml-rounds-no-timer-reset",()=>{
                var f=World();dynamic bombs=New("SubsystemTacticalBombs");((Subsystem)bombs).m_project=f.P;f.P.m_subsystems.Add((Subsystem)bombs);bombs.Load(new ValuesDictionary());
                var bt=T("SubsystemTacticalBombs").GetNestedType("Bomb");dynamic b=Activator.CreateInstance(bt);b.Charge.Remaining=17.5f;b.Charge.BeepLeft=99f;bombs.Bombs.Add(b);
                try{f.Time.m_gameTime=1.5;((IUpdateable)bombs).Update(.1f);Check(b.Charge.Remaining==16f,"fuse used clamped dt instead of game timeline");
                    for(int i=0;i<2;i++){var v=new ValuesDictionary();bombs.Save(v);bombs.Dispose();bombs=New("SubsystemTacticalBombs");((Subsystem)bombs).m_project=f.P;bombs.Load(Round(v));Check(bombs.Bombs.Count==1&&bombs.Bombs[0].Charge.Remaining==16f&&bombs.Bombs[0].Clock==null&&bombs.Bombs[0].Defuser==null,"save reset fuse/resumed defuse");}
                    bombs.Bombs[0].Charge.BeepLeft=99f;f.Time.m_gameTime=30;((IUpdateable)bombs).Update(.1f);Check(bombs.Bombs.Count==0&&f.Audio.Shots==1,"expiry missing/double explosion");((IUpdateable)bombs).Update(.1f);Check(f.Audio.Shots==1,"explosion repeated");
                }finally{bombs.Dispose();}
                Check(!(bool)C("ScWeaponActionGate").GetMethod("Blocks").Invoke(null,[null]),"disposed DLC leaves action gate subscribed");
            });
            if(includeRendering)Test("kit-native-item-draw-carry-and-cs2-sounds",()=>{
                using var zip=ZipFile.OpenRead(dlcPath);using var png=(zip.GetEntry("Assets/Textures/ScCsgoTactical/defuser_item.png")??zip.GetEntry("Assets/Textures/ScCsgoTactical/defuser_item.webp")).Open();var img=Image.Load(png);Check(img.Pixels.Count(c=>c.A>0)>1000,"empty kit atlas");var block=BlocksManager.Blocks[707];
                foreach(var mode in Enum.GetValues<DrawBlockMode>()){var renderer=new PrimitivesRenderer3D();var m=Matrix.Identity;block.DrawBlock(renderer,707,Color.White,1,ref m,new DrawBlockEnvironmentData{Light=15,DrawBlockMode=mode});var v=renderer.TexturedBatches.Single().TriangleVertices.ToArray();Check(v.Length>24&&v.All(p=>p.TexCoord.X>=0&&p.TexCoord.X<=1),"invalid kit mesh/UV");}
                var p=Blank<ComponentPlayer>();var inv=new ComponentInventory();for(int i=0;i<36;i++)inv.m_slots.Add(new());p.ComponentMiner=new ComponentMiner{Inventory=inv};inv.m_slots[35]=new(){Count=1,Value=707};Check((bool)T("SubsystemTacticalBombs").GetMethod("HasKit").Invoke(null,[p]),"kit only recognizes hotbar");inv.m_slots[35].Count=0;Check(!(bool)T("SubsystemTacticalBombs").GetMethod("HasKit").Invoke(null,[p]),"empty slot grants kit");
                using var cz=ZipFile.OpenRead(corePath);foreach(string sound in new[]{"c4_beep2","c4_warning","c4_trigger_trip","c4_disarmstart","c4_disarmfinish"})Check(cz.GetEntry("Assets/Audio/ScCsgoKnives/"+sound+".ogg")!=null,"missing CS2 sound "+sound);
            });
        }finally{rf.SetValue(null,original);diagnostics?.SetValue(null,diagnosticsBefore);
            Array.Copy(savedBlocks,BlocksManager.Blocks,savedBlocks.Length);
            BlocksManager.BlockTypeToIndex.Clear();foreach(var p in savedTypes)BlocksManager.BlockTypeToIndex[p.Key]=p.Value;
            BlocksManager.BlockNameToIndex.Clear();foreach(var p in savedNames)BlocksManager.BlockNameToIndex[p.Key]=p.Value;}
        return results;
    }
}
