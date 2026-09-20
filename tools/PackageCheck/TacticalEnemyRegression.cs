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
    static void Set(object o,string name,object value)=>o.GetType().GetField(name,Fields).SetValue(o,value);
    static Entity E(Project p,params Component[] components){var e=Blank<Entity>();e.m_project=p;e.m_isAddedToProject=true;e.m_components=components.ToList();foreach(var c in components)c.m_entity=e;p.m_entities[e]=true;return e;}
    static ValuesDictionary Round(ValuesDictionary v){var x=new XElement("Values");v.Save(x);var r=new ValuesDictionary();r.ApplyOverrides(XElement.Parse(x.ToString()));return r;}
    sealed class Audio:SubsystemAudio {public int Shots;public override void PlaySound(string n,float v,float pitch,Vector3 p,float d,bool delay)=>Shots++;}
    sealed class Ground:SubsystemTerrain {public bool Blocked;public override TerrainRaycastResult? Raycast(Vector3 a,Vector3 b,bool i,bool s,Func<int,float,bool> f)=>Blocked?new TerrainRaycastResult{Distance=.5f}:null;}
    sealed class Drops:SubsystemPickables {public readonly List<Pickable> Items=[];public override Pickable AddPickable(int value,int count,Vector3 p,Vector3? vel,Matrix? m,Entity owner){var item=new Pickable{Value=value,Count=count,Position=p};Items.Add(item);return item;}}
    sealed class Health:ComponentHealth {public override void Injure(Injury i){i.Attackment.EnableHitValueParticleSystem=false;base.Injure(i);}}
    internal static List<TacticalRegression.Result> Run(Assembly core,Assembly dlc,string corePath,string dlcPath){
        var results=new List<TacticalRegression.Result>();
        void Test(string n,Action a){try{a();results.Add(new("enemy/"+n,true,""));}catch(Exception ex){results.Add(new("enemy/"+n,false,ex.ToString()));}}
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        Type C(string n)=>core.GetType("Game."+n,true);Type T(string n)=>dlc.GetType("Game."+n,true);
        dynamic New(string n,params object[] args)=>Activator.CreateInstance(T(n),args);
        dynamic State(int role,int seed=12)=>(object)T("TacticalEnemyState").GetMethod("Create").Invoke(null,[Enum.ToObject(T("TacticalRole"),role),"fixture-squad",new Engine.Random(seed)]);
        dynamic Decode(string s)=>T("TacticalEnemyState").GetMethod("Decode").Invoke(null,[s]);
        var rf=C("ScGunRegistry").GetField("Current");var original=rf.GetValue(null);
        void FreshRegistry()=>rf.SetValue(null,Activator.CreateInstance(C("ScGunRegistry")));
        (Project P,dynamic Director,SubsystemTime Time,Ground Terrain,SubsystemBodies Bodies,SubsystemGameInfo Info,SubsystemCreatureSpawn Spawn,SubsystemSpawn Sleeping,Audio Audio,Drops Drops) World(){
            var p=new Project();var t=new SubsystemTime();var terrain=new Ground{Terrain=new Terrain()};var bodies=new SubsystemBodies();var info=new SubsystemGameInfo{WorldSettings=Blank<WorldSettings>()};
            info.WorldSettings.GameMode=GameMode.Survival;info.WorldSettings.EnvironmentBehaviorMode=EnvironmentBehaviorMode.Living;var spawn=new SubsystemCreatureSpawn{m_subsystemTerrain=terrain,m_subsystemBodies=bodies,m_subsystemSky=new SubsystemSky()};var sleeping=new SubsystemSpawn();spawn.m_subsystemSpawn=sleeping;
            var audio=new Audio();var drops=new Drops();dynamic director=New("SubsystemTacticalEnemies");var grenades=(Subsystem)Activator.CreateInstance(C("SubsystemScGrenades"));Set(grenades,"m_time",t);Set(grenades,"m_terrain",terrain);Set(grenades,"m_bodies",bodies);
            foreach(var s in new Subsystem[]{t,terrain,bodies,info,spawn,sleeping,audio,drops,new SubsystemPlayers(),new SubsystemTimeOfDay(),grenades,(Subsystem)director}){s.m_project=p;p.m_subsystems.Add(s);}
            director.Load(new ValuesDictionary());return(p,director,t,terrain,bodies,info,spawn,sleeping,audio,drops);
        }
        (dynamic Enemy,ComponentCreature Creature,ComponentInventoryBase Inv,ComponentPathfinding Path) Enemy(Project p,int role=1,int seed=12){
            dynamic enemy=New("ComponentTacticalEnemy");var body=new ComponentBody{Position=new Vector3(0,60,0),BoxSize=new Vector3(.65f,1.8f,.65f),Mass=75};var health=new ComponentHealth{Health=1};var locomotion=new ComponentLocomotion();var spawn=new ComponentSpawn();var creature=new ComponentCreature{ComponentBody=body,ComponentHealth=health,ComponentLocomotion=locomotion,ComponentSpawn=spawn,m_killVerbs=["shot"]};health.m_componentCreature=creature;
            var path=new ComponentPathfinding{m_componentPilot=new ComponentPilot{m_componentCreature=creature}};var inv=(ComponentInventoryBase)New("ComponentTacticalInventory");inv.Load(new ValuesDictionary{{"SlotsCount",5},{"Slots",new ValuesDictionary()}},null);var selector=new ComponentBehaviorSelector();
            E(p,(Component)enemy,body,health,creature,locomotion,spawn,path,path.m_componentPilot,inv,selector);enemy.Load(new ValuesDictionary(),null);enemy.Configure(State(role,seed),body.Position);Set(enemy,"random",new Engine.Random(seed));selector.Load(new(),null);selector.Update(.1f);return(enemy,creature,inv,path);
        }
        try{
            FreshRegistry();
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
            Test("day29-30-native-surface-point-mode-population-and-sleeping-squad",()=>{
                var f=World();BlocksManager.Blocks[0]=new AirBlock{IsCollidable=false};BlocksManager.Blocks[2]=new DirtBlock{BlockIndex=2,IsCollidable=true};
                var chunk=f.Terrain.Terrain.AllocateChunk(0,0);chunk.State=TerrainChunkState.Valid;f.Terrain.Terrain.SetCellValueFast(8,60,8,2);f.Terrain.Terrain.SetTopHeight(8,8,60);
                var point=f.Spawn.ProcessSpawnPoint(new Point3(8,60,8),SpawnLocationType.Surface);Check(point==new Point3(8,61,8),"unexpected native spawn convention: "+point);f.Director.Register();f.Director.Register();var registered=f.Spawn.m_creatureTypes.Single(c=>c.Name=="ScTacticalEnemy");
                bool Suitable()=>registered.SpawnSuitabilityFunction(registered,point.Value)>0;
                Check(Suitable(),"native first-free-cell rejected");
                foreach(var mode in new[]{GameMode.Creative,GameMode.Harmless}){f.Info.WorldSettings.GameMode=mode;Check(!Suitable(),"peaceful mode spawned enemies");}f.Info.WorldSettings.GameMode=GameMode.Survival;
                f.Info.WorldSettings.EnvironmentBehaviorMode=EnvironmentBehaviorMode.Static;Check(!Suitable(),"static ecology ignored");f.Info.WorldSettings.EnvironmentBehaviorMode=EnvironmentBehaviorMode.Living;
                Check(((Array)T("SubsystemTacticalEnemies").GetMethod("Roles").Invoke(null,[29,30])).Length==3&&((Array)T("SubsystemTacticalEnemies").GetMethod("Roles").Invoke(null,[30,30])).Length==5,"day boundary");
                var saved=new SpawnEntityData{TemplateName="ScTacticalEnemy",Position=new Vector3(20,61,8)};f.Sleeping.GetOrCreateSpawnChunk(new Point2(1,0)).SpawnsData.Add(saved);Check(!Suitable(),"spawned over unloaded squad");f.Sleeping.m_chunks.Clear();
                f.Sleeping.m_spawnEntityDatas[9]=saved;Check(Suitable(),"stale engine entity cache permanently prevents new encounters");
                f.Director.MaxActive=5;for(int i=0;i<3;i++){var e=Enemy(f.P);e.Creature.ComponentBody.Position=new Vector3(1000+i*3,60,0);f.Director.OnEntityAdded(e.Creature.Entity);}Check(!Suitable(),"partial squad exceeds active cap");
            });
            Test("finite-ammo-reload-through-native-ai-no-registry",()=>{
                FreshRegistry();var f=World();var e=Enemy(f.P);e.Enemy.State.Rounds=0;e.Enemy.State.Reserve=3;e.Enemy.State.ReloadLeft=.2f;((IUpdateable)e.Enemy).Update(.1f);Check(e.Enemy.State.Rounds==0&&e.Enemy.State.Reserve==3,"ammo committed before reload finish");((IUpdateable)e.Enemy).Update(.2f);Check(e.Enemy.State.Rounds==3&&e.Enemy.State.Reserve==0,"reload created/lost ammo");Check(Enumerable.Range(0,5).All(i=>e.Inv.GetSlotCount(i)==0),"enemy holds allocated player inventory gun");
            });
            Test("shoot-wall-friendly-body-and-cadence",()=>{
                var f=World();var e=Enemy(f.P);e.Enemy.State.Grenades=0;
                var target=new ComponentBody{Position=new Vector3(0,60,-8),BoxSize=new Vector3(.8f,1.8f,.8f),Mass=75};var health=new Health{Health=1,AttackResilience=1000,AttackResilienceFactor=1};var creature=new ComponentCreature{ComponentBody=target,ComponentHealth=health};health.m_componentCreature=creature;E(f.P,target,health,creature);f.Bodies.AddBody(target);e.Enemy.Alert(target);
                int before=e.Enemy.State.Rounds;f.Terrain.Blocked=true;for(int i=0;i<20;i++)((IUpdateable)e.Enemy).Update(.1f);Check(e.Enemy.State.Rounds==before&&health.Health==1,"shot through wall");
                f.Terrain.Blocked=false;e.Enemy.Alert(target);var ally=Enemy(f.P);ally.Creature.ComponentBody.Position=new Vector3(0,60,-3);f.Bodies.AddBody(ally.Creature.ComponentBody);for(int i=0;i<20;i++)((IUpdateable)e.Enemy).Update(.1f);Check(e.Enemy.State.Rounds==before&&health.Health==1,"shot through friendly body");f.Bodies.RemoveBody(ally.Creature.ComponentBody);
                e.Enemy.Alert(target);for(int i=0;i<18;i++)((IUpdateable)e.Enemy).Update(.1f);int spent=before-e.Enemy.State.Rounds;Check(spent>0&&spent<=6&&health.Health<1&&health.Health>0,"native cadence/damage broken: "+spent+" / "+health.Health);
                var grenades=f.P.m_subsystems.Single(s=>s.GetType()==C("SubsystemScGrenades"));var blindType=C("SubsystemScGrenades").GetNestedType("Blindness",BindingFlags.NonPublic);var blind=Activator.CreateInstance(blindType);Set(blind,"Until",50d);Set(blind,"ImmuneUntil",60d);var dict=(System.Collections.IDictionary)C("SubsystemScGrenades").GetField("m_blind",Fields).GetValue(grenades);dict.Add(e.Creature.ComponentBody,blind);before=e.Enemy.State.Rounds;for(int i=0;i<30;i++)((IUpdateable)e.Enemy).Update(.1f);Check(before==e.Enemy.State.Rounds&&e.Path.Destination is null,"blinded enemy keeps firing/chasing");
            });
            Test("hostile-grenade-cap-fuse-smoke-visibility-and-player-damage-filter",()=>{
                var f=World();var g=f.P.m_subsystems.Single(s=>s.GetType()==C("SubsystemScGrenades"));dynamic grenade=g;
                for(int i=0;i<4;i++)Check(grenade.TryThrowHostile(i,new Vector3(0,61,0),new Vector3(0,4,-12)),"legal throw refused");Check(!grenade.TryThrowHostile(4,Vector3.Zero,Vector3.One)&&!grenade.TryThrowHostile(6,Vector3.Zero,Vector3.One),"throw budget/kind bypass");
                var states=((System.Collections.IEnumerable)C("SubsystemScGrenades").GetField("m_active",Fields).GetValue(g)).Cast<object>().ToArray();dynamic fire=states[3];Check(fire.Remaining==2f&&fire.Owner==-2,"wrong enemy fire fuse/owner");
                var player=Blank<ComponentPlayer>();var body=new ComponentBody();E(f.P,player,body);var friendly=C("SubsystemScGrenades").GetMethod("Friendly",Fields);Check((bool)friendly.Invoke(g,[states[0],body]),"enemy grenade immunity inherited player friendly-fire setting");
                dynamic smoke=states[2];smoke.Effect=true;smoke.Age=5f;smoke.Remaining=10f;smoke.Position=new Vector3(0,61,0);Check(grenade.SmokeBlocksSight(new Vector3(-4,61,0),new Vector3(4,61,0)),"NPC ignores dense smoke");
            });
            Test("death-once-empty-inventory-and-full-registry-fallback",()=>{
                FreshRegistry();C("ScGunRegistry").GetProperty("Next").SetValue(rf.GetValue(null),1023);var f=World();int material=0;
                for(int i=0;i<160;i++){var e=Enemy(f.P,1,i);e.Creature.ComponentHealth.Health=0;e.Enemy.Died();int count=f.Drops.Items.Count;e.Enemy.Died();((IUpdateable)e.Enemy).Update(.1f);Check(f.Drops.Items.Count==count&&e.Enemy.State.LootDone&&Enumerable.Range(0,5).All(s=>e.Inv.GetSlotCount(s)==0),"death duplicated reward/left inventory");}
                Check(f.Drops.Items.Count>50&&f.Drops.Items.All(i=>Terrain.ExtractContents(i.Value)!=701),"full registry drops malformed gun");material=f.Drops.Items.Count(i=>Terrain.ExtractContents(i.Value)==708);Check(material>0&&(int)C("ScGunRegistry").GetProperty("Next").GetValue(rf.GetValue(null))==1023,"registry wrap/missing material reward");
            });
            Test("rare-gun-drop-real-registry-worn-no-counter",()=>{
                FreshRegistry();var f=World();
                for(int i=0;i<160;i++){var e=Enemy(f.P,1,i);e.Creature.ComponentHealth.Health=0;e.Enemy.State.Rounds=2;e.Enemy.Died();}
                var guns=f.Drops.Items.Where(p=>Terrain.ExtractContents(p.Value)==701).ToArray();Check(guns.Length>0&&guns.Length<20,"rare gun outcome missing or too common");
                foreach(var gun in guns){object[] args=[Terrain.ExtractData(gun.Value),null];Check((bool)C("GunSpec").GetMethod("TryGetSnapshot").Invoke(null,args),"dropped gun becomes question mark");dynamic state=args[1];Check(!state.CounterInstalled&&state.KillCount==0&&state.AppliedGrowthLevel==0&&state.Rounds==2&&state.Durability>0&&state.Durability<state.MaxDurability,"drop gained counter/ammo or lost wear");}
                Check((int)C("ScGunRegistry").GetProperty("Next").GetValue(rf.GetValue(null))==guns.Length+1,"non-gun loot consumed registry numbers");
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
            Test("kit-native-item-draw-carry-and-cs2-sounds",()=>{
                using var zip=ZipFile.OpenRead(dlcPath);using var png=zip.GetEntry("Assets/Textures/ScCsgoTactical/defuser.png").Open();var img=Image.Load(png);Check(img.Pixels.Count(c=>c.A>0)>1000,"empty kit icon");var block=BlocksManager.Blocks[707];Set(block,"icon",Blank<Texture2D>());
                foreach(var mode in Enum.GetValues<DrawBlockMode>()){var renderer=new PrimitivesRenderer3D();var m=Matrix.Identity;block.DrawBlock(renderer,707,Color.White,1,ref m,new DrawBlockEnvironmentData{Light=15,DrawBlockMode=mode});var v=renderer.TexturedBatches.Single().TriangleVertices.ToArray();Check(v.Min(p=>p.TexCoord.X)==0&&v.Max(p=>p.TexCoord.X)==1,"kit crops to atlas");}
                var p=Blank<ComponentPlayer>();var inv=new ComponentInventory();for(int i=0;i<36;i++)inv.m_slots.Add(new());p.ComponentMiner=new ComponentMiner{Inventory=inv};inv.m_slots[35]=new(){Count=1,Value=707};Check((bool)T("SubsystemTacticalBombs").GetMethod("HasKit").Invoke(null,[p]),"kit only recognizes hotbar");inv.m_slots[35].Count=0;Check(!(bool)T("SubsystemTacticalBombs").GetMethod("HasKit").Invoke(null,[p]),"empty slot grants kit");
                using var cz=ZipFile.OpenRead(corePath);foreach(string sound in new[]{"c4_beep2","c4_warning","c4_trigger_trip","c4_disarmstart","c4_disarmfinish"})Check(cz.GetEntry("Assets/Audio/ScCsgoKnives/"+sound+".ogg")!=null,"missing CS2 sound "+sound);
            });
        }finally{rf.SetValue(null,original);}
        return results;
    }
}
