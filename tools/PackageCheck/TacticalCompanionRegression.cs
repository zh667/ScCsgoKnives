using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Engine;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

// agent-feedback-20260928 P0: a recruited companion unloaded by distance must come back with the same owner,
// orders, health, exact slots and original gun IDs. The installed 1.3.0 DLL returned an empty native unload payload
// (owner -1, empty inventory); these cases exercise the replacement path through the delivered assemblies.
static class TacticalCompanionRegression {
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static T Blank<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    static ValuesDictionary Round(ValuesDictionary v){var x=new XElement("Values");v.Save(x);var r=new ValuesDictionary();r.ApplyOverrides(XElement.Parse(x.ToString()));return r;}
    static string Xml(ValuesDictionary v){var x=new XElement("Values");v.Save(x);return x.ToString(SaveOptions.DisableFormatting);}
    sealed class LedgerProject:Project {
        public Func<int,Entity> Factory;public int FailNext;public int Created;
        public readonly List<Subsystem> Listeners=[];
        public override Entity CreateEntity(ValuesDictionary v,int id=0){if(FailNext>0){FailNext--;throw new Exception("injected rebuild failure");}Created++;return Factory(id);}
        public override void AddEntity(Entity e){e.m_isAddedToProject=true;m_entities[e]=true;foreach(var s in Listeners)s.OnEntityAdded(e);}
        public override void RemoveEntity(Entity e,bool dispose){e.m_isAddedToProject=false;m_entities.Remove(e);foreach(var s in Listeners)s.OnEntityRemoved(e);}
    }
    internal static List<TacticalRegression.Result> Run(Assembly core,Assembly dlc){
        var results=new List<TacticalRegression.Result>();
        void Test(string n,Action a){try{a();results.Add(new("companion-lifecycle/"+n,true,""));}catch(Exception ex){results.Add(new("companion-lifecycle/"+n,false,ex.ToString()));}}
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        Type C(string n)=>core.GetType("Game."+n,true);Type T(string n)=>dlc.GetType("Game."+n,true);
        var rf=C("ScGunRegistry").GetField("Current");var originalRegistry=rf.GetValue(null);
        // The full tactical run loads the real database; the AI-only run registers a named stand-in for the lookup.
        var previousTemplate=DatabaseManager.m_valueDictionaries.GetValueOrDefault("ScTacticalCT");
        var template=previousTemplate;
        if(template?.DatabaseObject?.Name!="ScTacticalCT"){template=new ValuesDictionary();var type=new DatabaseObjectType("EntityTemplate","","",0,false,false,256,false);type.InitializeRelations(null,null,null);template.m_databaseObject=new DatabaseObject(type,"ScTacticalCT");DatabaseManager.m_valueDictionaries["ScTacticalCT"]=template;}
        (LedgerProject P,Subsystem Ledger,Subsystem Tactical,SubsystemSpawn Native,SubsystemTerrain Terrain,SubsystemTime Time) World(){
            var p=new LedgerProject();var time=new SubsystemTime();var terrain=new SubsystemTerrain{Terrain=new Terrain()};var native=new SubsystemSpawn();
            var tactical=(Subsystem)Activator.CreateInstance(T("SubsystemScTactical"));var ledger=(Subsystem)Activator.CreateInstance(T("SubsystemTacticalCompanions"));
            var armor=(Subsystem)Activator.CreateInstance(C("SubsystemScArmor"));
            foreach(var s in new Subsystem[]{time,terrain,native,new SubsystemPlayers(),new SubsystemBodies(),new SubsystemPickables(),tactical,ledger,armor}){s.m_project=p;p.m_subsystems.Add(s);}
            tactical.Load(new());ledger.Load(new());armor.Load(new());p.Listeners.Add(tactical);p.Listeners.Add(ledger);
            var chunk=terrain.Terrain.AllocateChunk(0,0);chunk.State=TerrainChunkState.Valid;
            return(p,ledger,tactical,native,terrain,time);
        }
        Entity Build(LedgerProject p,int id,bool loadDefaults=true){
            var npc=(Component)Activator.CreateInstance(T("ComponentTacticalCompanion"));
            var inv=(ComponentInventoryBase)Activator.CreateInstance(T("ComponentTacticalInventory"));inv.Load(new ValuesDictionary{{"SlotsCount",5},{"Slots",new ValuesDictionary()}},null);
            var body=new ComponentBody{BoxSize=new Vector3(.65f,1.8f,.65f),Position=new Vector3(8,61,8),Rotation=Quaternion.Identity};var health=new ComponentHealth{Health=1};var spawn=new ComponentSpawn();
            var creature=new ComponentCreature{DisplayName="CT · SAS",ComponentBody=body,ComponentHealth=health,ComponentLocomotion=new ComponentLocomotion(),ComponentSpawn=spawn,ConstantSpawn=true};health.m_componentCreature=creature;spawn.ComponentCreature=creature;spawn.AutoDespawn=true;
            var pilot=new ComponentPilot{m_componentCreature=creature};var path=new ComponentPathfinding{m_componentPilot=pilot};
            var e=Blank<Entity>();e.m_project=p;e.Id=id;e.m_valuesDictionary=template;e.m_components=[npc,inv,body,health,creature,spawn,pilot,path];foreach(var c in e.m_components)c.m_entity=e;
            if(loadDefaults)npc.Load(new ValuesDictionary(),null); // template defaults, exactly as a native respawn
            return e;
        }
        dynamic Npc(Entity e)=>e.Components.Single(c=>c.GetType().Name=="ComponentTacticalCompanion");
        ComponentInventoryBase Inv(Entity e)=>(ComponentInventoryBase)e.Components.Single(c=>c.GetType().Name=="ComponentTacticalInventory");
        int FreshGun(string name,int rounds){var specs=(Array)C("GunSpec").GetField("All").GetValue(null);int variant=Enumerable.Range(0,specs.Length).Single(i=>(string)C("GunSpec").GetField("Name").GetValue(specs.GetValue(i))==name);
            return Terrain.MakeBlockValue(701,0,(int)C("GunSpec").GetMethod("MakeData").Invoke(null,[variant,rounds,false]));}
        void Allocate(ComponentInventoryBase inv){
            object[] args=[inv,0,(string)C("ScGunHolders").GetMethod("Key").Invoke(null,[inv,0]),Enum.ToObject(C("ScGunResult"),0)];
            var tx=C("ScGunMutation").GetMethod("Prepare").Invoke(null,args)??throw new Exception("companion gun transaction refused "+args[3]);
            var commit=tx.GetType().GetMethod("Commit");var arg=System.Linq.Expressions.Expression.Parameter(C("ScGunRecord"));
            var change=System.Linq.Expressions.Expression.Lambda(commit.GetParameters()[0].ParameterType,System.Linq.Expressions.Expression.Assign(System.Linq.Expressions.Expression.Field(arg,"Rounds"),System.Linq.Expressions.Expression.Constant(13)),arg).Compile();
            Check(commit.Invoke(tx,[change,0,0,null]).ToString()=="Success","gun record allocation failed");
        }
        List<string> HolderKeys(Project p,int gunValue){int id=(int)C("GunSpec").GetMethod("GetId").Invoke(null,[Terrain.ExtractData(gunValue)]);
            return ((System.Collections.IEnumerable)C("ScGunHolders").GetMethod("Scan").Invoke(null,[p,701])).Cast<object>().Where(h=>(int)h.GetType().GetProperty("Id").GetValue(h)==id).Select(h=>(string)h.GetType().GetProperty("Key").GetValue(h)).ToList();}
        (Entity E,int Gun) Equipped(LedgerProject p){
            var e=Build(p,4242);var npc=Npc(e);var inv=Inv(e);p.AddEntity(e); // a durable recovery owner is required before any gun write
            npc.OwnerIndex=0;npc.Order=(dynamic)Enum.ToObject(T("TacticalOrder"),1);npc.CeaseFire=true;npc.GuardPosition=new Vector3(3,61,4);
            inv.AddSlotItems(0,FreshGun("m4a1s",20),1);Allocate(inv);inv.AddSlotItems(1,702,37);inv.AddSlotItems(3,702,5);
            var body=e.FindComponent<ComponentBody>(true);body.Rotation=Quaternion.CreateFromYawPitchRoll(1.1f,0,0);e.FindComponent<ComponentHealth>(true).Health=.42f;
            return(e,inv.GetSlotValue(0));
        }
        var far=new[]{new Vector2(500,500)};var near=new[]{new Vector2(10,10)};
        try{
            rf.SetValue(null,Activator.CreateInstance(C("ScGunRegistry")));
            Test("native-unload-disabled-for-companions",()=>{
                var w=World();var e=Build(w.P,11);w.P.AddEntity(e);
                Check(!e.FindComponent<ComponentSpawn>(true).AutoDespawn,"native DespawnChunks would still build the empty unload record for a companion");
                var data=new SpawnEntityData{TemplateName="ScTacticalCT",EntityId=11};T("SubsystemTacticalEnemies").GetMethod("ReadSpawn").Invoke(null,[e,data]);
                var first=Blank<ComponentPlayer>();first.PlayerData=Blank<PlayerData>();first.PlayerData.PlayerIndex=0;
                Check((bool)Npc(e).OwnerMissing&&!(bool)Npc(e).OwnedBy(first),"legacy empty respawn claimed an owner");
            });
            Test("unload-and-return-exact-state-two-save-rounds",()=>{
                var w=World();var f=Equipped(w.P);int gun=f.Gun;var ledgerType=T("SubsystemTacticalCompanions");
                for(int round=0;round<2;round++){
                    w.P.Factory=id=>Build(w.P,id);
                    ((dynamic)w.Ledger).Step(far);
                    Check(!w.P.Entities.Any(),"companion still live beyond the unload radius");Check((int)((dynamic)w.Ledger).DormantCount==1,"no dormant record written");
                    Check(HolderKeys(w.P,gun).SequenceEqual(new[]{"dormant-companion:4242:Slot0"}),"dormant gun invisible to or duplicated in holder scan: "+string.Join(",",HolderKeys(w.P,gun)));
                    var saved=new ValuesDictionary();w.Ledger.Save(saved);
                    // Save/reload with no live entity, then a second independent reload of that XML.
                    for(int reload=0;reload<2;reload++){var fresh=(Subsystem)Activator.CreateInstance(ledgerType);fresh.m_project=w.P;var read=Round(saved);fresh.Load(read);var again=new ValuesDictionary();fresh.Save(again);Check(Xml(again)==Xml(read),"dormant ledger changed on save/load");
                        w.P.m_subsystems[w.P.m_subsystems.IndexOf(w.Ledger)]=fresh;w.P.Listeners[w.P.Listeners.IndexOf(w.Ledger)]=fresh;w.Ledger=fresh;saved=again;}
                    ((dynamic)w.Ledger).Step(near);
                    var back=w.P.Entities.Single();var npc=Npc(back);var inv=Inv(back);
                    Check(back.Id==4242,"entity ID changed, recovery owners would no longer resolve");
                    Check((int)npc.OwnerIndex==0&&(int)npc.Order==1&&(bool)npc.CeaseFire&&(Vector3)npc.GuardPosition==new Vector3(3,61,4)&&!(bool)npc.DeathHandled,"owner/orders lost");
                    Check(Math.Abs(back.FindComponent<ComponentHealth>(true).Health-.42f)<1e-6f,"health refilled or lost");
                    Check(inv.GetSlotValue(0)==gun&&inv.GetSlotCount(0)==1&&inv.GetSlotCount(1)==37&&inv.GetSlotCount(2)==0&&inv.GetSlotCount(3)==5&&inv.GetSlotCount(4)==0,"slots not restored exactly");
                    Check(HolderKeys(w.P,gun).Count==1&&!HolderKeys(w.P,gun)[0].StartsWith("dormant"),"live and dormant copies both hold the gun");
                    Check(!back.FindComponent<ComponentSpawn>(true).AutoDespawn,"respawned companion re-enabled native unload");
                    Check((int)((dynamic)w.Ledger).DormantCount==0,"record not consumed after a successful return");
                }
            });
            Test("rebuild-failure-keeps-record-bounded-retry-and-reload",()=>{
                var w=World();var f=Equipped(w.P);w.P.Factory=id=>Build(w.P,id);((dynamic)w.Ledger).Step(far);
                w.P.FailNext=99;for(int i=0;i<8;i++){w.Time.m_gameTime=i*1000;((dynamic)w.Ledger).Step(near);}
                Check(!w.P.Entities.Any()&&(int)((dynamic)w.Ledger).DormantCount==1,"failed rebuild dropped the record or leaked an entity");
                dynamic entry=((System.Collections.IEnumerable)((dynamic)w.Ledger).Entries).Cast<object>().Single();Check(entry.Blocked!=null&&(int)entry.Failures==5,"retries unbounded or never blocked");
                var saved=new ValuesDictionary();w.Ledger.Save(saved);var fresh=(Subsystem)Activator.CreateInstance(T("SubsystemTacticalCompanions"));fresh.m_project=w.P;fresh.Load(Round(saved));
                w.P.m_subsystems[w.P.m_subsystems.IndexOf(w.Ledger)]=fresh;w.P.Listeners[w.P.Listeners.IndexOf(w.Ledger)]=fresh;w.P.FailNext=0;((dynamic)fresh).Step(near);
                Check(w.P.Entities.Count()==1&&Inv(w.P.Entities.Single()).GetSlotValue(0)==f.Gun,"blocked record not retried after reload");
            });
            // current-direction-20260929 §2: a companion's protection is keyed by its entity ID (saved, never reused) and follows it
            // through sleep, wake-up and saves; only its owner, near it, can set it up at the workbench, and death or dismissal ends it.
            Test("companion-armor-stable-key-sleep-save-owner-target-death-dismiss",()=>{
                var w=World();var f=Equipped(w.P);var npc=Npc(f.E);dynamic store=w.P.m_subsystems.First(x=>x.GetType()==C("SubsystemScArmor"));
                dynamic State(string text){object[] a=[text,null];C("ScArmorState").GetMethod("TryDecode").Invoke(null,a);return a[1];}
                string Enc(object st)=>(string)C("ScArmorState").GetMethod("Encode").Invoke(st,null);
                string key=(string)npc.ArmorKey;Check(key=="companion-4242","companion key: "+key);
                // Only the owner, near: the workbench target list.
                ComponentPlayer Player(int index,Vector3 at){var p=Blank<ComponentPlayer>();p.PlayerData=Blank<PlayerData>();p.PlayerData.PlayerIndex=index;p.ComponentBody=new ComponentBody{Position=at,BoxSize=new Vector3(.65f,1.8f,.65f)};p.ComponentHealth=new ComponentHealth{Health=1};var pe=Blank<Entity>();pe.m_project=w.P;pe.m_components=[p];p.m_entity=pe;return p;}
                var targets=T("TacticalArmorTargets").GetMethod("For");
                object[] For(ComponentPlayer p)=>((System.Collections.IEnumerable)targets.Invoke(null,[p])).Cast<object>().ToArray();
                var owner=Player(0,new Vector3(10,61,8));var stranger=Player(1,new Vector3(10,61,8));var farOwner=Player(0,new Vector3(40,61,8));
                var list=For(owner);Check(list.Length==1&&(string)((dynamic)list[0]).Key==key,"the owner nearby must see exactly this companion");
                Check(For(stranger).Length==0&&For(farOwner).Length==0,"another player's companion or one out of range was offered");
                // A full package for the companion, paid by the owner; the target re-check runs before any material is taken.
                var wb=C("ScArmorWorkbench");var ops=wb.GetNestedType("Operation");
                BlocksManager.Blocks[2]??=new DirtBlock{BlockIndex=2}; // plain stackable material stand-ins
                int M(int k)=>Terrain.MakeBlockValue(2,0,k);var ids=new Dictionary<string,int>{["ironingot"]=M(1),["copperingot"]=M(2),["canvas"]=M(3),["leather"]=M(4)};
                ComponentInventory Bag(){var inv=new ComponentInventory();for(int i=0;i<8;i++)inv.m_slots.Add(new());inv.AddSlotItems(1,M(1),14);inv.AddSlotItems(2,M(2),6);inv.AddSlotItems(3,M(3),8);inv.AddSlotItems(4,M(4),6);return inv;}
                var quote=wb.GetMethod("Prepare").Invoke(null,[(object)store,key,Enum.Parse(ops,"MakeFull"),false,(Func<string,int>)(id=>ids[id])]);
                var check=(Func<string>)list[0].GetType().GetProperty("Check").GetValue(list[0]);var commit=wb.GetMethod("TryCommitFor");
                f.E.FindComponent<ComponentBody>(true).Position=new Vector3(30,61,8);var bag=Bag();
                Check(!(bool)commit.Invoke(null,[bag,(object)store,quote,check])&&bag.GetSlotCount(1)==14&&Enc(store.Get(key))=="1|0,0,0|0,0,0","a companion that walked away was equipped or materials taken");
                f.E.FindComponent<ComponentBody>(true).Position=new Vector3(8,61,8);
                Check((bool)commit.Invoke(null,[bag,(object)store,quote,check])&&bag.GetSlotCount(1)==0&&Enc(store.Get(key))=="1|1,150,150|1,100,100","the owner's full package for the companion failed");
                // Damage, sleep far away, two saves, wake: the same entity ID and the same values; never refilled.
                store.TryReplace(key,store.Get(key),State("1|1,120,150|1,40,100"));
                w.P.Factory=id=>Build(w.P,id);((dynamic)w.Ledger).Step(far);Check(!w.P.Entities.Any(),"companion did not sleep");
                var saved=new ValuesDictionary();store.Save(saved);
                for(int round=0;round<2;round++){var again=(Subsystem)Activator.CreateInstance(C("SubsystemScArmor"));again.m_project=w.P;again.Load(Round(saved));saved=new ValuesDictionary();again.Save(saved);}
                Check(Xml(saved).Contains("companion-4242")&&Xml(saved).Contains("1|1,120,150|1,40,100"),"sleeping companion's protection lost in saves: "+Xml(saved));
                Check((int)((dynamic)w.Ledger).PruneArmor()==0&&Enc(store.Get(key))=="1|1,120,150|1,40,100","a sleeping companion's protection was pruned");
                ((dynamic)w.Ledger).Step(near);var back=w.P.Entities.Single();Check((string)Npc(back).ArmorKey==key&&Enc(store.Get(key))=="1|1,120,150|1,40,100","woken companion lost or refilled its protection");
                // Stale entries of companions that exist nowhere are pruned; a stranger never inherits anything.
                store.TryCreate("companion-999",State("1|1,150,150|0,0,0"));Check((int)((dynamic)w.Ledger).PruneArmor()==1&&Enc(store.Get("companion-999"))=="1|0,0,0|0,0,0"&&Enc(store.Get(key))=="1|1,120,150|1,40,100","prune removed the wrong entries");
                // Death and dismissal end it.
                Npc(back).Died();Check(Enc(store.Get(key))=="1|0,0,0|0,0,0","a dead companion kept its protection");
                var other=Build(w.P,5151);w.P.AddEntity(other);Npc(other).OwnerIndex=0;string otherKey=(string)Npc(other).ArmorKey;store.TryCreate(otherKey,State("1|1,150,150|0,0,0"));
                Npc(other).EndArmor();Check(Enc(store.Get(otherKey))=="1|0,0,0|0,0,0","dismissal kept the protection");
            });
            Test("dead-panel-and-fading-companions-are-not-moved",()=>{
                var w=World();var f=Equipped(w.P);var npc=Npc(f.E);
                npc.PanelOpen=true;((dynamic)w.Ledger).Step(far);Check(w.P.Entities.Count()==1,"unloaded while equipment panel is open");npc.PanelOpen=false;
                f.E.FindComponent<ComponentHealth>(true).Health=0;((dynamic)w.Ledger).Step(far);Check(w.P.Entities.Count()==1&&(int)((dynamic)w.Ledger).DormantCount==0,"dead companion stored as alive");
            });
            Test("corrupt-and-future-records-kept-verbatim-never-spawned",()=>{
                var w=World();var f=Equipped(w.P);w.P.Factory=id=>Build(w.P,id);((dynamic)w.Ledger).Step(far);
                var saved=new ValuesDictionary();w.Ledger.Save(saved);var dormant=saved.GetValue<ValuesDictionary>("Dormant");var entry=dormant.GetValue<ValuesDictionary>("4242");entry.SetValue("Health",7f);
                var bad=Round(saved);var fresh=(Subsystem)Activator.CreateInstance(T("SubsystemTacticalCompanions"));fresh.m_project=w.P;fresh.Load(bad);
                ((dynamic)fresh).Step(near);Check(!w.P.Entities.Any(),"invalid record spawned a guessed companion");
                var again=new ValuesDictionary();fresh.Save(again);Check(Xml(again)==Xml(bad),"invalid record not preserved verbatim");
                Check(((System.Collections.IEnumerable)((dynamic)fresh).HeldItems()).Cast<object>().Count()==3,"invalid record's guns hidden from holder scans");
                var future=Round(saved);future.SetValue("Schema",2);bool refused=false;try{((Subsystem)Activator.CreateInstance(T("SubsystemTacticalCompanions"))).Load(future);}catch{refused=true;}Check(refused,"future ledger schema accepted");
            });
            Test("old-build-fade-save-keeps-entity-and-drops-empty-record",()=>{
                var w=World();var e=Build(w.P,77);Npc(e).OwnerIndex=0;var spawn=e.FindComponent<ComponentSpawn>(true);spawn.DespawnTime=12;
                var record=new SpawnEntityData{TemplateName="ScTacticalCT",EntityId=77,Data=""};w.Native.GetOrCreateSpawnChunk(new Point2(0,0)).SpawnsData.Add(record);w.Native.m_spawnEntityDatas[77]=record;
                w.P.AddEntity(e);
                Check(!spawn.IsDespawning&&w.Native.m_chunks.Values.All(c=>c.SpawnsData.Count==0)&&!w.Native.m_spawnEntityDatas.ContainsKey(77),"saved-mid-fade companion still removed or duplicated by its empty record");
            });
            // agent-feedback-20260928 P1: area effects follow factions, not CT/T appearance.
            Test("area-friendly-fire-by-faction-and-world-setting",()=>{
                T("TacticalModLoader").GetMethod("RegisterFactions").Invoke(null,null);
                var w=World();var ally=Build(w.P,31);w.P.AddEntity(ally);Npc(ally).OwnerIndex=0;
                var enemy=(Component)Activator.CreateInstance(T("ComponentTacticalEnemy"));var enemyBody=new ComponentBody();var foe=Blank<Entity>();foe.m_project=w.P;foe.m_components=[enemy,enemyBody];enemy.m_entity=foe;enemyBody.m_entity=foe;
                var owner=Blank<ComponentPlayer>();owner.PlayerData=Blank<PlayerData>();owner.PlayerData.PlayerIndex=0;var ownerBody=new ComponentBody();var me=Blank<Entity>();me.m_project=w.P;me.m_components=[owner,ownerBody];owner.m_entity=me;ownerBody.m_entity=me;
                var other=Blank<ComponentPlayer>();other.PlayerData=Blank<PlayerData>();other.PlayerData.PlayerIndex=1;var otherBody=new ComponentBody();var them=Blank<Entity>();them.m_project=w.P;them.m_components=[other,otherBody];other.m_entity=them;otherBody.m_entity=them;
                var wolf=new ComponentBody();var animal=Blank<Entity>();animal.m_project=w.P;animal.m_components=[wolf];wolf.m_entity=animal;
                var grenades=(Subsystem)Activator.CreateInstance(C("SubsystemScGrenades"));var info=new SubsystemGameInfo{WorldSettings=Blank<WorldSettings>()};grenades.GetType().GetField("m_info",Fields).SetValue(grenades,info);
                var friendly=C("SubsystemScGrenades").GetMethod("Friendly",Fields);var state=C("ScGrenadeState");
                bool Allowed(int from,ComponentBody target){var s=Activator.CreateInstance(state);state.GetField("Owner").SetValue(s,from);return (bool)friendly.Invoke(grenades,[s,target]);}
                var companionBody=ally.FindComponent<ComponentBody>(true);
                foreach(bool ff in new[]{false,true}){info.WorldSettings.IsFriendlyFireEnabled=ff;
                    Check(Allowed(0,companionBody)==ff,"player fire/HE/flash reaches a recruited ally regardless of friendly fire="+ff);
                    Check(Allowed(1,companionBody)==ff,"another player's area ignores the world setting for allies");
                    Check(Allowed(0,ownerBody),"self damage removed");Check(Allowed(0,otherBody)==ff,"player-vs-player rule changed");
                    Check(Allowed(-2,companionBody)&&Allowed(-2,ownerBody),"enemy areas no longer hurt players/allies");
                    Check(!Allowed(-2,enemyBody),"enemy fire hurts its own side");Check(Allowed(0,enemyBody)&&Allowed(0,wolf)&&Allowed(-2,wolf),"ordinary targets changed");}
            });
            Test("companion-leaves-player-c4-radius-before-detonation",()=>{
                var w=World();var e=Build(w.P,55);var npc=Npc(e);npc.OwnerIndex=0;
                var selector=new ComponentBehaviorSelector();selector.m_entity=e;e.m_components.Add(selector);w.P.AddEntity(e);
                var owner=Blank<ComponentPlayer>();owner.PlayerData=Blank<PlayerData>();owner.PlayerData.PlayerIndex=0;owner.ComponentBody=new ComponentBody{Position=new Vector3(9,61,8)};owner.ComponentHealth=new ComponentHealth{Health=1};
                owner.ComponentGui=Blank<ComponentGui>();owner.ComponentGui.m_modalPanelContainerWidget=new CanvasWidget();var me=Blank<Entity>();me.m_project=w.P;me.m_components=[owner,owner.ComponentBody,owner.ComponentHealth,owner.ComponentGui];foreach(var c in me.m_components)c.m_entity=me;
                w.P.FindSubsystem<SubsystemPlayers>(true).m_componentPlayers.Add(owner);
                selector.Load(new(),null);selector.Update(.1f); // the owner makes the companion the active behavior
                var c4=(Subsystem)Activator.CreateInstance(C("SubsystemScC4"));c4.m_project=w.P;w.P.m_subsystems.Add(c4);
                var charge=Activator.CreateInstance(C("ScC4Charge"));C("ScC4Charge").GetField("Position").SetValue(charge,new Vector3(8,61,8));C("ScC4Charge").GetField("Remaining").SetValue(charge,5f);
                ((System.Collections.IList)C("SubsystemScC4").GetField("charges",Fields).GetValue(c4)).Add(charge);
                w.Time.m_gameTime=1;((IUpdateable)npc).Update(.1f);
                var path=e.FindComponent<ComponentPathfinding>(true);
                Check(path.Destination is Vector3 exit&&Vector2.Distance(exit.XZ,new Vector2(8,8))>=32&&((string)npc.Status).Contains("撤离"),"companion follows its owner into a player C4 about to explode");
                C("ScC4Charge").GetField("Remaining").SetValue(charge,20f);C("ScC4Charge").GetField("Radius").SetValue(charge,8f);w.Time.m_gameTime=3;((IUpdateable)npc).Update(.1f);
                Check(path.Destination is not Vector3 follow||Vector2.Distance(follow.XZ,new Vector2(8,8))>=8,"companion walks into a live bomb radius to follow");
            });
            // P4: an order confirmation is spoken when the order really changes, even while the panel is open.
            Test("order-confirmation-voice-once-per-real-change",()=>{
                var w=World();var e=Build(w.P,61);w.P.AddEntity(e);var npc=Npc(e);var heard=new List<string>();
                var evt=C("ScAgentVoice").GetEvent("Event");Action<Entity,string,string> ear=(who,role,action)=>{if(ReferenceEquals(who,e))heard.Add(role+"/"+action);};
                evt.AddEventHandler(null,ear);
                try{
                    var command=T("ComponentTacticalCompanion").GetMethod("Command");object Order(int i)=>Enum.ToObject(T("TacticalOrder"),i);
                    npc.PanelOpen=true;command.Invoke((object)npc,new object[]{Order(1)});command.Invoke((object)npc,new object[]{Order(1)});command.Invoke((object)npc,new object[]{Order(2)});command.Invoke((object)npc,new object[]{Order(0)});
                    Check(heard.SequenceEqual(new[]{"ct/wait","ct/inposition","ct/follow"}),"order voice missing, repeated or deferred: "+string.Join(",",heard));
                }finally{evt.RemoveEventHandler(null,ear);}
            });
        }finally{rf.SetValue(null,originalRegistry);if(previousTemplate is null)DatabaseManager.m_valueDictionaries.Remove("ScTacticalCT");else DatabaseManager.m_valueDictionaries["ScTacticalCT"]=previousTemplate;}
        return results;
    }
}
