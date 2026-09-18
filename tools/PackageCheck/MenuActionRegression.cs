using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Game;
using GameEntitySystem;

// Exercises the actual behavior update, not just the animation controller or a
// transaction in isolation: the old modal cancellation was between those tests.
static class MenuActionRegression {
    internal record Result(string Name, bool Ok, string Detail);
    sealed class Inventory : IInventory {
        public int[] Values = new int[12], Counts = new int[12];
        public Project Project => null;
        public int SlotsCount => 12;
        public int VisibleSlotsCount { get; set; } = 10;
        public int ActiveSlotIndex { get; set; }
        public int GetSlotValue(int s) => Values[s];
        public int GetSlotCount(int s) => Counts[s];
        public int GetSlotCapacity(int s,int v) => 100;
        public int GetSlotProcessCapacity(int s,int v) => 0;
        public void AddSlotItems(int s,int v,int n) { Values[s]=v; Counts[s]+=n; }
        public int RemoveSlotItems(int s,int n) { n=Math.Min(n,Counts[s]); Counts[s]-=n; return n; }
        public void ProcessSlotItems(int s,int v,int n,int p,out int rv,out int rn) { rv=rn=0; }
        public void DropAllItems(Vector3 p) { }
    }
    internal static List<Result> Run(Assembly mod) {
        List<Result> results=[];
        void Test(string name,Action test) { try { test(); results.Add(new("menu-action/"+name,true,"actual UpdateGun, native modal widget, packaged DLL")); }
            catch(Exception e) { results.Add(new("menu-action/"+name,false,e.ToString())); } }
        void Require(bool ok,string why) { if(!ok) throw new Exception(why); }
        Type T(string n)=>mod.GetType("Game."+n);
        object F(object o,string n)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(o);
        void Set(object o,string n,object v)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(o,v);
        object Call(string t,string m,params object[] a)=>T(t).GetMethod(m).Invoke(null,a);
        object Invoke(object o,string m,params object[] a)=>o.GetType().GetMethod(m,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(o,a);
        U Blank<U>()=>(U)RuntimeHelpers.GetUninitializedObject(typeof(U));
        var savedTypes=new Dictionary<Type,int>(BlocksManager.BlockTypeToIndex);
        var savedNames=new Dictionary<string,int>(BlocksManager.BlockNameToIndex);
        var registryField=T("ScGunRegistry").GetField("Current"); var oldRegistry=registryField.GetValue(null);
        var locator=T("ScGunMutation").GetField("HolderLocator"); var oldLocator=locator.GetValue(null);
        var clock=T("KnifeClock"); bool oldVirtual=(bool)clock.GetField("Virtual").GetValue(null);
        double oldTime=(double)clock.GetField("VirtualNow").GetValue(null); float oldVolume=SettingsManager.SoundsVolume;
        var window=typeof(Window).GetField("m_state",BindingFlags.Static|BindingFlags.NonPublic);var oldWindow=window.GetValue(null);
        var oldScreen=ScreensManager.CurrentScreen;var oldAnimation=ScreensManager.m_animationData;var oldRoot=ScreensManager.RootWidget;
        void Time(double t)=>clock.GetField("VirtualNow").SetValue(null,t);
        try {
            clock.GetField("Virtual").SetValue(null,true); SettingsManager.SoundsVolume=0; locator.SetValue(null,null);
            window.SetValue(null,Enum.Parse(window.FieldType,"Active"));ScreensManager.CurrentScreen=null;ScreensManager.m_animationData=null;ScreensManager.RootWidget=new CanvasWidget();
            foreach(var pair in new[]{("ScKnifeBlock",700),("ScGunBlock",701),("ScGrenadeBlock",702),("ScAmmoBlock",703)}) {
                BlocksManager.BlockTypeToIndex[T(pair.Item1)]=pair.Item2;BlocksManager.BlockNameToIndex[pair.Item1]=pair.Item2;
            }
            var specs=((Array)T("GunSpec").GetField("All").GetValue(null)).Cast<object>().ToArray();
            (object Behavior,object State,ComponentPlayer Player,ComponentFirstPersonModel Model,Inventory Inv,SubsystemTime Time,SubsystemGameInfo Info) Setup(object spec,bool creative) {
                var registry=Activator.CreateInstance(T("ScGunRegistry"));registryField.SetValue(null,registry);
                int variant=Array.IndexOf(specs,spec);var inv=new Inventory();
                inv.Values[0]=Terrain.MakeBlockValue(701,0,(int)Call("GunSpec","MakeData",variant,0,false));inv.Counts[0]=1;
                inv.Values[10]=(int)Call("ScAmmoBlock","Value",(int)Call("ScReloadTransaction","AmmoKind",spec));inv.Counts[10]=100;
                var player=Blank<ComponentPlayer>();player.ComponentMiner=Blank<ComponentMiner>();player.ComponentMiner.Inventory=inv;
                player.ComponentHealth=new ComponentHealth{Health=1};player.ComponentInput=Blank<ComponentInput>();
                player.ComponentInput.m_playerInput=new PlayerInput{Dig=new Ray3(Vector3.Zero,Vector3.UnitZ),Hit=new Ray3(Vector3.Zero,Vector3.UnitZ)};
                player.ComponentGui=Blank<ComponentGui>();player.ComponentGui.m_modalPanelContainerWidget=new CanvasWidget();
                var widget=Blank<GameWidget>();widget.GuiWidget=new CanvasWidget();player.PlayerData=Blank<PlayerData>();player.PlayerData.m_gameWidget=widget;
                var model=Blank<ComponentFirstPersonModel>();model.m_componentPlayer=player;
                var entity=Blank<Entity>();entity.m_components=[model];player.m_entity=entity;
                var project=new Project();var time=new SubsystemTime{m_gameTime=10};var info=new SubsystemGameInfo{WorldSettings=Blank<WorldSettings>()};info.WorldSettings.GameMode=creative?GameMode.Creative:GameMode.Survival;
                project.m_subsystems.Add(time);project.m_subsystems.Add(info);
                var behavior=Activator.CreateInstance(T("SubsystemScGunBlockBehavior"));((Subsystem)behavior).m_project=project;Set(behavior,"m_time",time);Set(behavior,"m_registry",registry);
                ((IDictionary)F(behavior,"m_brokenNoticeAt"))[player]=10d; // UI error text is outside this headless fixture.
                var state=Activator.CreateInstance(T("SubsystemScGunBlockBehavior").GetNestedType("GunState",BindingFlags.NonPublic),true);
                Set(state,"LastValue",inv.Values[0]);Invoke(F(state,"Selection"),"Observe",inv,0,inv.Values[0],true);
                Time(90);Call("KnifeAnimationController","Update",model,inv.Values[0]);Time(100);Call("KnifeAnimationController","Update",model,inv.Values[0]);
                Invoke(behavior,"StartReload",player,state,model,spec,inv.Values[0]);
                Require(F(state,"Reload") is not null,"reload did not start");
                player.ComponentGui.m_modalPanelContainerWidget.Children.Add(new CanvasWidget());
                Require(!(bool)Call("ScGunBindings","Available",player),"native modal must block input");
                return(behavior,state,player,model,inv,time,info);
            }
            void Tick(object b,object s,ComponentPlayer p,ComponentFirstPersonModel m,double at) {
                Time(at);Invoke(b,"UpdateGun",p,s,p.ComponentMiner.ActiveBlockValue,1/60f);
                Call("KnifeAnimationController","Update",m,p.ComponentMiner.ActiveBlockValue);
            }
            int Rounds(Inventory inv,int slot=0)=>(int)Call("GunSpec","GetRounds",Terrain.ExtractData(inv.Values[slot]));
            foreach(var spec in specs.Where(s=>(float)F(s,"RechargeSeconds")<=0))foreach(bool creative in new[]{false,true}) {
                string name=(string)F(spec,"Name");
                Test($"reload/{name}/{creative}",()=>{
                    var (b,s,p,m,inv,time,info)=Setup(spec,creative);bool tube=(bool)Call("ScReloadTransaction","IsTube",name);
                    double end=(double)F(s,"BusyUntil");
                    double first=tube?(double)((IList)F(s,"ShellTimes"))[0]:(double)F(s,"InsertAt");
                    Tick(b,s,p,m,100.001);Require(F(s,"Reload") is not null,"opening inventory cancelled reload");
                    Require(end>100,"deadline must share the animation clock");
                    Require(((IList)F(s,"Scheduled")).Count>0,"opening inventory removed all audio cues");
                    Tick(b,s,p,m,first-.001);Require(Rounds(inv)==0&&inv.Counts[10]==100,"ammo credited too early");
                    Tick(b,s,p,m,first+.001);Require(Rounds(inv)==(tube?1:(int)F(spec,"Magazine")),"insert event did not credit ammo behind menu");
                    Require((double)F(s,"BusyUntil")>first&&(bool)Call("KnifeAnimationController","IsBusy",m),"firing unlocked before bolt/outro");
                    Set(s,"FireAfterReload",true);Set(s,"PrepareUntil",0d);Set(s,"BurstRemaining",2);Set(s,"BurstNextAt",0d);
                    Tick(b,s,p,m,end+.01);Tick(b,s,p,m,end+.02);
                    int cost=creative?0:tube?(int)F(spec,"Magazine"):(int)Call("ScReloadTransaction","Required",spec);
                    Require(Rounds(inv)==(int)F(spec,"Magazine")&&inv.Counts[10]==100-cost,"wrong ammo payment or duplicate insert");
                    Require(F(s,"Reload") is null&&(double)F(s,"BusyUntil")==-1,"reload remains stuck");
                    Require(!(bool)F(s,"FireAfterReload")&&(int)F(s,"BurstRemaining")==0&&(double)F(s,"PrepareUntil")==-1,"attack banked behind menu");
                    Require(time.GameTime==10,"test must hold world clock frozen");
                });
            }
            var ak=specs.First(s=>(string)F(s,"Name")=="ak47");
            foreach(string name in new[]{"nova","xm1014","sawedoff"})Test("shortened-reload/"+name,()=>{
                var spec=specs.First(x=>(string)F(x,"Name")==name);var(b,s,p,m,inv,time,info)=Setup(spec,false);
                var times=(IList)F(s,"ShellTimes");double first=(double)times[0];
                Tick(b,s,p,m,first+.01);Require(Rounds(inv)==1,"first shell missing");
                Invoke(b,"CutReloadShort",p,s,spec,first+.01);
                int pending=((IList)F(s,"ShellTimes")).Count;double end=(double)F(s,"BusyUntil");
                Require(end>first&&end<120,"shortening mixed game and animation clocks");
                Tick(b,s,p,m,end+.01);
                Require(Rounds(inv)==1+pending&&inv.Counts[10]==99-pending,"shortened reload added or charged extra shells");
                Require(F(s,"Reload") is null&&!(bool)F(s,"FireAfterReload"),"menu did not suppress queued shot");
            });
            foreach(string change in new[]{"slot","remove","inventory","ammo","mode"})foreach(bool afterInsert in new[]{false,true})Test($"change/{change}/{afterInsert}",()=>{
                var(b,s,p,m,inv,time,info)=Setup(ak,false);var tx=F(s,"Reload");double at=(double)F(s,"InsertAt"),end=(double)F(s,"BusyUntil");
                if(afterInsert)Tick(b,s,p,m,at+.001);
                int paid=100-inv.Counts[10],value=inv.Values[0];
                if(change=="slot"){inv.Values[1]=value;inv.Counts[1]=1;inv.ActiveSlotIndex=1;}
                if(change=="remove")inv.Counts[0]=0;
                if(change=="inventory"){var other=new Inventory();other.Values[0]=value;other.Counts[0]=1;p.ComponentMiner.Inventory=other;}
                if(change=="ammo")inv.Counts[10]=0;
                if(change=="mode")info.WorldSettings.GameMode=GameMode.Creative;
                Tick(b,s,p,m,end+.01);
                Require(F(s,"Reload") is null,"stale reload retained");
                Require(inv.Values[0]==value&&Rounds(inv)==(afterInsert?30:0),"wrong gun changed or credited ammo lost");
                Require(inv.Counts[10]==(change=="ammo"?0:100-paid),"cancel charged another magazine");
            });
            foreach(string name in new[]{"m4a1s","usp_silencer"})Test("silencer/"+name,()=>{
                var spec=specs.First(x=>(string)F(x,"Name")==name);var(b,s,p,m,inv,time,info)=Setup(spec,false);
                Invoke(b,"CancelReload",p,s,true);Call("KnifeAnimationController","TriggerSilencer",p,false);
                Set(s,"BusyUntil",101d);Set(s,"SilencerPending",true);Set(s,"PendingSilencerOff",true);
                Tick(b,s,p,m,101.01);Require((bool)Call("GunSpec","GetSilencerOff",Terrain.ExtractData(inv.Values[0])),"silencer state blocked by menu");
                Require(!(bool)F(s,"SilencerPending")&&Rounds(inv)==0&&inv.Counts[10]==100,"attachment changed ammunition");
            });
        } finally {
            registryField.SetValue(null,oldRegistry);locator.SetValue(null,oldLocator);clock.GetField("Virtual").SetValue(null,oldVirtual);clock.GetField("VirtualNow").SetValue(null,oldTime);SettingsManager.SoundsVolume=oldVolume;
            window.SetValue(null,oldWindow);ScreensManager.CurrentScreen=oldScreen;ScreensManager.m_animationData=oldAnimation;ScreensManager.RootWidget=oldRoot;
            BlocksManager.BlockTypeToIndex.Clear();foreach(var p in savedTypes)BlocksManager.BlockTypeToIndex[p.Key]=p.Value;
            BlocksManager.BlockNameToIndex.Clear();foreach(var p in savedNames)BlocksManager.BlockNameToIndex[p.Key]=p.Value;
        }
        return results;
    }
}
