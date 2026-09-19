using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using Engine;
using Game;
using GameEntitySystem;

static class EggFeedbackRegression {
    internal record Result(string Name,bool Ok,string Detail);
    static T Blank<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    sealed class Inventory:IInventory {
        public int Value,Count=3;
        public Project Project=>null;
        public int SlotsCount=>1;
        public int VisibleSlotsCount {get;set;}=1;
        public int ActiveSlotIndex {get;set;}
        public int GetSlotValue(int s)=>Value;
        public int GetSlotCount(int s)=>Count;
        public int GetSlotCapacity(int s,int v)=>40;
        public int GetSlotProcessCapacity(int s,int v)=>0;
        public void AddSlotItems(int s,int v,int n){Value=v;Count+=n;}
        public int RemoveSlotItems(int s,int n){n=Math.Min(n,Count);Count-=n;return n;}
        public void ProcessSlotItems(int s,int v,int n,int p,out int rv,out int rn){rv=rn=0;}
        public void DropAllItems(Vector3 p){}
    }
    sealed class AudioProbe:SubsystemAudio {
        public int Throws;
        public override void PlaySound(string n,float v,float pitch,Vector3 pos,float d,bool delay){if(n=="Audio/Throw")Throws++;}
    }
    sealed class ProjectileProbe:SubsystemProjectiles {
        public int Calls;public bool Fail;public Projectile Last;
        public override Projectile FireProjectile(int value,Vector3 position,Vector3 velocity,Vector3 spin,ComponentCreature owner){
            Calls++;return Last=Fail?null:new Projectile{Value=value,Position=position,Velocity=velocity,Owner=owner};
        }
    }
    sealed class SpawnProbe:SubsystemCreatureSpawn {
        public int Calls;public string Template;public Vector3 Position;public bool Constant;public int Failure;
        public Entity Spawned;
        public override Entity SpawnCreature(string template,Vector3 position,bool constant){
            Calls++;Template=template;Position=position;Constant=constant;
            if(Failure==1)return null;if(Failure==2)throw new InvalidOperationException("injected spawn failure");
            Spawned=Blank<Entity>();Spawned.m_components=[new ComponentSpawn()];return Spawned;
        }
    }
    internal static List<Result> Run(Assembly mod,string package,string hapticsPackage) {
        List<Result> results=[];
        void Test(string name,Action action){try{action();results.Add(new("egg-feedback/"+name,true,""));}catch(Exception e){results.Add(new("egg-feedback/"+name,false,e.ToString()));}}
        void Assert(bool ok,string reason){if(!ok)throw new Exception(reason);}
        Type T(string name)=>mod.GetType("Game."+name,true);
        var eggType=T("ScChickenEggBlock");const int index=705;
        var oldBlock=BlocksManager.Blocks[index];bool hadIndex=BlocksManager.BlockTypeToIndex.TryGetValue(eggType,out int oldIndex);
        try {
            var egg=(Block)Activator.CreateInstance(eggType);egg.BlockIndex=index;
            BlocksManager.Blocks[index]=egg;BlocksManager.BlockTypeToIndex[eggType]=index;
            Test("native-throw-registration-no-click-spawn",()=>{
                Assert(egg.IsAimable&&egg.Behaviors=="ThrowableBlockBehavior","native aim/throw not registered");
                Assert(egg.ProjectileSpeed==14&&egg.ProjectileDamping==.8f&&egg.ProjectileTipOffset==.1f&&egg.DisintegratesOnHit&&egg.ProjectileStickProbability==0,"native egg ballistics differ");
                Assert(T("SubsystemScChicken").GetMethod("OnUse").DeclaringType==typeof(SubsystemBlockBehavior),"nearby click handler still intercepts");
            });
            foreach(bool failed in new[]{false,true})Test("actual-native-aim-release/"+failed,()=>{
                var inv=new Inventory{Value=Terrain.MakeBlockValue(index)};var body=new ComponentBody{Velocity=new Vector3(2,0,0)};
                var model=Blank<ComponentFlightlessBirdModel>();model.m_eyePosition=new Vector3(12,10,12);
                var creature=new ComponentCreature{ComponentBody=body,ComponentCreatureModel=model};
                var miner=new ComponentMiner{Inventory=inv,ComponentCreature=creature};
                var entity=Blank<Entity>();entity.m_components=[miner,creature];miner.m_entity=entity;creature.m_entity=entity;
                var shots=new ProjectileProbe{Fail=failed};var audio=new AudioProbe();
                var native=new SubsystemThrowableBlockBehavior{m_subsystemProjectiles=shots,m_subsystemAudio=audio};
                var ray=new Ray3(model.EyePosition,Vector3.UnitZ);
                native.OnAim(ray,miner,AimState.InProgress);native.OnAim(ray,miner,AimState.Cancelled);
                Assert(shots.Calls==0&&inv.Count==3&&audio.Throws==0,"cancel/hold consumed egg");
                native.OnAim(ray,miner,AimState.Completed);
                Assert(shots.Calls==1&&inv.Count==(failed?3:2)&&audio.Throws==(failed?0:1),"release failed to pay exactly once after projectile creation");
                if(!failed){Assert(shots.Last.Velocity==new Vector3(2,0,14)&&shots.Last.Owner==creature,"native motion/owner lost");
                    Assert(shots.Last.Position==model.EyePosition+body.Matrix.Right*.4f,"throw started at clicked terrain");}
            });
            foreach(bool bodyHit in new[]{false,true})foreach(int failure in new[]{0,1,2})Test($"native-projectile-impact-once/{bodyHit}/{failure}",()=>{
                var project=new Project();var spawn=new SpawnProbe{Failure=failure};project.m_subsystems.Add(spawn);spawn.m_project=project;
                var behavior=(SubsystemBlockBehavior)Activator.CreateInstance(T("SubsystemScChicken"));behavior.m_project=project;
                var native=new SubsystemThrowableBlockBehavior();
                var table=new SubsystemBlockBehaviors{m_blockBehaviorsByContents=new SubsystemBlockBehavior[BlocksManager.Blocks.Length][]};
                table.m_blockBehaviorsByContents[index]=[native,behavior];
                var projectiles=new SubsystemProjectiles{m_subsystemBlockBehaviors=table};
                var item=new Projectile{Value=Terrain.MakeBlockValue(index),Position=new Vector3(23,4,17)};
                typeof(Projectile).GetField("m_subsystemProjectiles",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(item,projectiles);
                Assert(item.ProcessOnHitAsProjectileBlockBehavior(bodyHit?null:new CellFace(23,3,17,4),bodyHit?new ComponentBody():null,.016f),"egg not destroyed");
                item.ProcessOnHitAsProjectileBlockBehavior(new CellFace(),new ComponentBody(),.016f);
                Assert(spawn.Calls==1&&spawn.Template=="ScCsgoChicken"&&spawn.Constant&&spawn.Position==item.Position,"wrong species/position or duplicate hatch");
                if(failure==0)Assert(spawn.Spawned.FindComponent<ComponentSpawn>().SpawnDuration==.25f,"slow default spawn");
                var other=new Projectile{Value=Terrain.MakeBlockValue(3)};
                Assert(!behavior.OnHitAsProjectile(null,null,other)&&spawn.Calls==1,"other eggs hijacked");
            });
        } finally {BlocksManager.Blocks[index]=oldBlock;if(hadIndex)BlocksManager.BlockTypeToIndex[eggType]=oldIndex;else BlocksManager.BlockTypeToIndex.Remove(eggType);}

        var bridge=T("ScControllerFeedback");var flags=BindingFlags.Static|BindingFlags.NonPublic;
        var pulse=bridge.GetField("pulse",flags);var initialized=bridge.GetField("initialized",flags);
        object oldPulse=pulse.GetValue(null),oldInit=initialized.GetValue(null);
        var window=typeof(Window).GetField("m_state",flags);var oldWindow=window.GetValue(null);
        var oldScreen=ScreensManager.CurrentScreen;var oldAnimation=ScreensManager.m_animationData;var oldRoot=ScreensManager.RootWidget;
        void Call(string method,params object[] args)=>bridge.GetMethod(method).Invoke(null,args);
        ComponentPlayer Player(WidgetInputDevice device){
            var p=Blank<ComponentPlayer>();p.PlayerData=Blank<PlayerData>();p.ComponentHealth=new ComponentHealth{Health=1};
            p.ComponentGui=Blank<ComponentGui>();p.ComponentGui.m_modalPanelContainerWidget=new CanvasWidget();
            var w=Blank<GameWidget>();w.GuiWidget=new CanvasWidget();w.WidgetsHierarchyInput=new WidgetInput(device);p.PlayerData.m_gameWidget=w;return p;
        }
        try {
            window.SetValue(null,Enum.Parse(window.FieldType,"Active"));ScreensManager.CurrentScreen=null;ScreensManager.m_animationData=null;ScreensManager.RootWidget=new CanvasWidget();
            Test("optional-absent-no-dependency",()=>{
                Assert(!mod.GetReferencedAssemblies().Any(a=>a.Name=="ControllerHaptics"),"hard assembly reference");
                using var zip=ZipFile.OpenRead(package);using var s=zip.GetEntry("modinfo.json").Open();using var meta=JsonDocument.Parse(s);
                Assert(!meta.RootElement.GetProperty("Dependencies").EnumerateObject().Any(p=>p.Name.Contains("Haptics",StringComparison.OrdinalIgnoreCase)),"required package dependency");
                Assert(!zip.Entries.Any(e=>e.FullName.EndsWith("ControllerHaptics.dll")),"provider DLL bundled");
                Assert(!AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="ControllerHaptics"),"absence test must run before loading actual provider");
                Call("Initialize");Call("Shot",null,"ak47");Assert(pulse.GetValue(null) is null,"missing provider not a no-op");
            });
            Test("player-device-profiles-and-menu-gates",()=>{
                var calls=new List<(ComponentPlayer Player,WidgetInputDevice Device,float Strength,int Ms,bool Trigger)>();
                pulse.SetValue(null,(Action<ComponentPlayer,WidgetInputDevice,float,int,bool>)((p,d,s,m,t)=>calls.Add((p,d,s,m,t))));
                var p=Player(WidgetInputDevice.GamePad2);var p2=Player(WidgetInputDevice.GamePad4);
                foreach(string gun in (string[])T("GunSpec").GetField("FrozenOrder").GetValue(null))Call("Shot",p,gun);
                Assert(calls.Count==35&&calls.All(c=>c.Player==p&&c.Device==WidgetInputDevice.GamePad2&&c.Trigger&&c.Strength>0&&c.Strength<=.55f&&c.Ms<=85),"gun profile/owner mask wrong");
                Call("Reloaded",p2);Call("KnifeHit",p2,true);Call("KnifeHit",p2,false);Call("Thrown",p2);
                Assert(calls.Count==39&&calls.Skip(35).All(c=>c.Player==p2&&c.Device==WidgetInputDevice.GamePad4&&!c.Trigger),"secondary event routing");
                p.ComponentGui.m_modalPanelContainerWidget.Children.Add(new CanvasWidget());Call("Shot",p,"ak47");
                p.ComponentGui.m_modalPanelContainerWidget.Children.Clear();p.ComponentHealth.Health=0;Call("Reloaded",p);
                Call("Thrown",(object)null);Assert(calls.Count==39,"menu/dead/null feedback leaked");
            });
            Test("provider-failure-isolated-once",()=>{
                int calls=0;pulse.SetValue(null,(Action<ComponentPlayer,WidgetInputDevice,float,int,bool>)((p,d,s,m,t)=>{calls++;throw new InvalidOperationException("injected hardware backend failure");}));
                var p=Player(WidgetInputDevice.GamePad1);Call("Shot",p,"ak47");Call("Shot",p,"ak47");Call("Reloaded",p);
                Assert(calls==1&&pulse.GetValue(null) is null,"provider exception repeats/escapes");
            });
            Test("packaged-gameplay-event-calls",()=>{
                foreach(var pair in new[]{("SubsystemScGunBlockBehavior","Fire","Shot"),("SubsystemScGunBlockBehavior","UpdateGun","Reloaded"),
                    ("SubsystemScKnifeBlockBehavior","Update","KnifeHit"),("SubsystemScGrenades","Update","Thrown")}) {
                    var caller=T(pair.Item1).GetMethod(pair.Item2,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                    Assert(CombatRegression.Calls(caller).Count(m=>m.DeclaringType==bridge&&m.Name==pair.Item3)==1,"missing/duplicate event hook "+pair);
                }
            });
            if(hapticsPackage is not null)Test("actual-controller-haptics120-player-api",()=>{
                using var zip=ZipFile.OpenRead(hapticsPackage);using var s=zip.GetEntry("ControllerHaptics.dll").Open();using var bytes=new MemoryStream();s.CopyTo(bytes);bytes.Position=0;
                var provider=AssemblyLoadContext.Default.LoadFromStream(bytes);Call("Initialize");
                var bound=(Delegate)pulse.GetValue(null);Assert(bound?.Method.DeclaringType?.Assembly==provider,"actual supplied provider not bound");
                Assert(bound.Method.GetParameters()[0].ParameterType==typeof(ComponentPlayer),"global instead of player-aware overload");
                var p=Player(WidgetInputDevice.Keyboard|WidgetInputDevice.Mouse);
                var tracker=provider.GetType("Game.ControllerInputStateTracker",true);var kind=provider.GetType("Game.ControllerInputKind",true);
                tracker.GetMethod("SetState",flags).Invoke(null,[p.PlayerData,Enum.Parse(kind,"KeyboardMouse")]);
                Call("Shot",p,"awp");Call("Reloaded",p);Call("KnifeHit",p,false);Call("Thrown",p);
                Assert(ReferenceEquals(bound,pulse.GetValue(null)),"actual API threw on keyboard player");
                // Real provider owns controller-vs-keyboard routing; verify it without driving physical motors.
                var allowed=tracker.GetMethod("ShouldAllowControllerHaptics",[typeof(ComponentPlayer),typeof(WidgetInputDevice)]);
                Assert(!(bool)allowed.Invoke(null,[p,WidgetInputDevice.GamePad1]),"keyboard player triggers controller");
                tracker.GetMethod("SetState",flags).Invoke(null,[p.PlayerData,Enum.Parse(kind,"Gamepad")]);
                Assert((bool)allowed.Invoke(null,[p,WidgetInputDevice.GamePad1]),"gamepad input rejected by provider");
            });
        } finally {pulse.SetValue(null,oldPulse);initialized.SetValue(null,oldInit);window.SetValue(null,oldWindow);ScreensManager.CurrentScreen=oldScreen;ScreensManager.m_animationData=oldAnimation;ScreensManager.RootWidget=oldRoot;}
        return results;
    }
}
