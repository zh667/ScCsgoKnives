using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Engine;
using Engine.Graphics;
using Game;
using GameEntitySystem;

// video-feedback-20260929 R1: the R8's trigger through the real UpdateGun / AimPressed / Fire path of the packaged DLL,
// driven by synthetic per-frame input. Every committed shot is explained by the evidence trail (press, cock, release,
// cancel, commit, ammunition), not by counting animations.
static class RevolverInputRegression {
    internal record Result(string Name,bool Ok,string Detail);
    sealed class Inventory:IInventory {
        public int[] Values=new int[12],Counts=new int[12];
        public Project Project=>null;public int SlotsCount=>12;public int VisibleSlotsCount {get;set;}=10;public int ActiveSlotIndex {get;set;}
        public int GetSlotValue(int s)=>Values[s];public int GetSlotCount(int s)=>Counts[s];public int GetSlotCapacity(int s,int v)=>100;public int GetSlotProcessCapacity(int s,int v)=>0;
        public void AddSlotItems(int s,int v,int n){Values[s]=v;Counts[s]+=n;}public int RemoveSlotItems(int s,int n){n=Math.Min(n,Counts[s]);Counts[s]-=n;return n;}
        public void ProcessSlotItems(int s,int v,int n,int p,out int rv,out int rn){rv=rn=0;}public void DropAllItems(Vector3 p){}
    }
    sealed class Audio:SubsystemAudio {public override void PlaySound(string n,float v,float pitch,Vector3 p,float d,bool delay){}public override void PlayRandomSound(string n,float v,float pitch,Vector3 p,float d,bool delay){}}
    sealed class Eye:Camera {
        public Eye():base(null){}
        public override Vector3 ViewPosition=>new(0,80,0);public override Vector3 ViewDirection=>Vector3.UnitY;public override Vector3 ViewUp=>Vector3.UnitZ;public override Vector3 ViewRight=>Vector3.UnitX;
        public override Matrix ViewMatrix=>Matrix.Identity;public override Matrix InvertedViewMatrix=>Matrix.Identity;public override Matrix ProjectionMatrix=>Matrix.Identity;public override Matrix ScreenProjectionMatrix=>Matrix.Identity;
        public override Matrix InvertedProjectionMatrix=>Matrix.Identity;public override Matrix ViewProjectionMatrix=>Matrix.Identity;public override Vector2 ViewportSize=>new(1280,720);public override Matrix ViewportMatrix=>Matrix.Identity;
        public override BoundingFrustum ViewFrustum=>new(Matrix.Identity);public override bool UsesMovementControls=>false;public override bool IsEntityControlEnabled=>true;/* the player's own camera: control allowed (round 3 gate) */public override void Update(float dt){}
    }
    internal static List<Result> Run(Assembly mod){
        List<Result> results=[];
        void Test(string name,Func<string> test){try{results.Add(new("r8-input/"+name,true,test()));}catch(Exception e){results.Add(new("r8-input/"+name,false,e.ToString()));}}
        void Require(bool ok,string why){if(!ok)throw new Exception(why);}
        const BindingFlags Any=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        Type T(string n)=>mod.GetType("Game."+n,true);
        object F(object o,string n)=>o.GetType().GetField(n,Any).GetValue(o);
        void Set(object o,string n,object v)=>o.GetType().GetField(n,Any).SetValue(o,v);
        object Call(string t,string m,params object[] a)=>T(t).GetMethod(m,Any).Invoke(null,a);
        object Invoke(object o,string m,params object[] a)=>o.GetType().GetMethod(m,Any).Invoke(o,a);
        U Blank<U>()=>(U)RuntimeHelpers.GetUninitializedObject(typeof(U));
        var savedTypes=new Dictionary<Type,int>(BlocksManager.BlockTypeToIndex);var savedNames=new Dictionary<string,int>(BlocksManager.BlockNameToIndex);
        var registryField=T("ScGunRegistry").GetField("Current");var oldRegistry=registryField.GetValue(null);
        var locator=T("ScGunMutation").GetField("HolderLocator");var oldLocator=locator.GetValue(null);
        var clock=T("KnifeClock");bool oldVirtual=(bool)clock.GetField("Virtual").GetValue(null);double oldTime=(double)clock.GetField("VirtualNow").GetValue(null);float oldVolume=SettingsManager.SoundsVolume;
        var window=typeof(Window).GetField("m_state",BindingFlags.Static|BindingFlags.NonPublic);var oldWindow=window.GetValue(null);
        var oldScreen=ScreensManager.CurrentScreen;var oldAnimation=ScreensManager.m_animationData;var oldRoot=ScreensManager.RootWidget;
        var frameIndex=typeof(Time).GetProperty("FrameIndex");int oldFrame=Time.FrameIndex;
        var trail=T("ScRevolverTrigger");
        try{
            clock.GetField("Virtual").SetValue(null,true);SettingsManager.SoundsVolume=0;locator.SetValue(null,null);
            window.SetValue(null,Enum.Parse(window.FieldType,"Active"));ScreensManager.CurrentScreen=null;ScreensManager.m_animationData=null;ScreensManager.RootWidget=new CanvasWidget();
            foreach(var pair in new[]{("ScKnifeBlock",700),("ScGunBlock",701),("ScGrenadeBlock",702),("ScAmmoBlock",703)}){BlocksManager.BlockTypeToIndex[T(pair.Item1)]=pair.Item2;BlocksManager.BlockNameToIndex[pair.Item1]=pair.Item2;}
            var specs=((Array)T("GunSpec").GetField("All").GetValue(null)).Cast<object>().ToArray();
            var spec=specs.Single(s=>(string)F(s,"Name")=="revolver");int variant=Array.IndexOf(specs,spec);
            float primaryCycle=(float)F(spec,"CycleSeconds"),alternateCycle=(float)F(spec,"CycleSecondsAlternate");
            Require(primaryCycle==.5f&&alternateCycle==.4f&&(bool)F(spec,"Automatic"),$"R8 parameters changed: {primaryCycle}/{alternateCycle}");

            // One player holding a loaded R8, everything Fire touches, and a frame driver.
            var fixture=new Func<int,bool,(object Behavior,object State,ComponentPlayer Player,ComponentFirstPersonModel Model,Inventory Inv,SubsystemTime Time)>((rounds,creative)=>{
                var registry=Activator.CreateInstance(T("ScGunRegistry"));registryField.SetValue(null,registry);
                var inv=new Inventory();inv.Values[0]=Terrain.MakeBlockValue(701,0,(int)Call("GunSpec","MakeData",variant,rounds,false));inv.Counts[0]=1;
                inv.Values[10]=(int)Call("ScAmmoBlock","Value",(int)Call("ScReloadTransaction","AmmoKind",spec));inv.Counts[10]=100;
                var player=Blank<ComponentPlayer>();player.ComponentMiner=Blank<ComponentMiner>();player.ComponentMiner.Inventory=inv;
                player.ComponentHealth=new ComponentHealth{Health=1};player.ComponentInput=Blank<ComponentInput>();player.ComponentInput.m_playerInput=new PlayerInput();
                player.ComponentGui=Blank<ComponentGui>();player.ComponentGui.m_modalPanelContainerWidget=new CanvasWidget();
                player.ComponentBody=new ComponentBody{Position=new Vector3(0,78,0),BoxSize=new Vector3(.65f,1.8f,.65f)};player.ComponentLocomotion=new ComponentLocomotion();
                var widget=Blank<GameWidget>();widget.GuiWidget=new CanvasWidget();widget.m_activeCamera=new Eye();player.PlayerData=Blank<PlayerData>();player.PlayerData.m_gameWidget=widget;
                var model=Blank<ComponentFirstPersonModel>();model.m_componentPlayer=player;
                var entity=Blank<Entity>();entity.m_components=[model,player.ComponentBody];player.m_entity=entity;player.ComponentBody.m_entity=entity;
                var project=new Project();var time=new SubsystemTime{m_gameTime=1000};var info=new SubsystemGameInfo{WorldSettings=Blank<WorldSettings>()};info.WorldSettings.GameMode=creative?GameMode.Creative:GameMode.Survival;
                var terrain=new SubsystemTerrain{Terrain=new Terrain()};var bodies=new SubsystemBodies();var audio=new Audio();
                foreach(var s in new Subsystem[]{time,info,terrain,bodies,audio}){s.m_project=project;project.m_subsystems.Add(s);}
                entity.m_project=project;
                var behavior=Activator.CreateInstance(T("SubsystemScGunBlockBehavior"));((Subsystem)behavior).m_project=project;
                Set(behavior,"m_time",time);Set(behavior,"m_registry",registry);Set(behavior,"m_terrain",terrain);Set(behavior,"m_bodies",bodies);Set(behavior,"m_audio",audio);
                ((IDictionary)F(behavior,"m_brokenNoticeAt"))[player]=1e9; // UI text is outside this headless fixture
                var stateType=T("SubsystemScGunBlockBehavior").GetNestedType("GunState",BindingFlags.NonPublic);var state=Activator.CreateInstance(stateType,true);
                ((IDictionary)F(behavior,"m_states"))[player]=state;
                Set(state,"LastValue",inv.Values[0]);Invoke(F(state,"Selection"),"Observe",inv,0,inv.Values[0],true);
                // Draw finished long ago on the animation clock.
                clock.GetField("VirtualNow").SetValue(null,900d);Call("KnifeAnimationController","Update",model,inv.Values[0]);clock.GetField("VirtualNow").SetValue(null,1000d);Call("KnifeAnimationController","Update",model,inv.Values[0]);
                trail.GetMethod("Clear").Invoke(null,null);
                return(behavior,state,player,model,inv,time);
            });
            int Rounds(Inventory inv)=>(int)Call("GunSpec","GetRounds",Terrain.ExtractData(inv.Values[0]));
            string Clip(ComponentFirstPersonModel m)=>(string)Call("KnifeAnimationController","CurrentClip",m);
            (bool Shown,float Seconds) Hammer(ComponentFirstPersonModel m){object[] a=[m,0f];bool shown=(bool)T("KnifeAnimationController").GetMethod("PrepareShown").Invoke(null,a);return(shown,(float)a[1]);}
            List<(int Frame,double T,string Event,int Rounds,double Deadline)> Trail()=>((IEnumerable)trail.GetProperty("Entries").GetValue(null)).Cast<object>()
                .Select(e=>((int)e.GetType().GetProperty("Frame").GetValue(e),(double)e.GetType().GetProperty("GameTime").GetValue(e),(string)e.GetType().GetProperty("Event").GetValue(e),(int)e.GetType().GetProperty("Rounds").GetValue(e),(double)e.GetType().GetProperty("Deadline").GetValue(e))).ToList();
            // Drives frames: primary(t) and alternate(t) are the raw held states at time t (seconds from the start).
            // nativeInput uses the mouse rays (Hit on the press frame, Dig while held); otherwise the on-screen fire button.
            // alternateFirst: the aim hook runs before the gun update in a frame, as in the game; false tests the other order.
            List<string> Drive((object Behavior,object State,ComponentPlayer Player,ComponentFirstPersonModel Model,Inventory Inv,SubsystemTime Time) f,double fps,double seconds,Func<double,bool> primary,Func<double,bool> alternate=null,
                    bool nativeInput=true,bool alternateFirst=true,double animationClockRate=1,Action<double> each=null){
                var log=new List<string>();bool was=false;int frames=(int)Math.Round(seconds*fps);int seen=0;
                for(int i=0;i<=frames;i++){
                    double t=i/fps;frameIndex.SetValue(null,100000+i);f.Time.m_gameTime=1000+t;f.Time.m_gameTimeDelta=(float)(1/fps);clock.GetField("VirtualNow").SetValue(null,1000+t*animationClockRate);
                    bool held=primary(t),alt=alternate?.Invoke(t)==true;var ray=new Ray3(new Vector3(0,80,0),Vector3.UnitY);
                    f.Player.ComponentInput.m_playerInput=nativeInput?new PlayerInput{Dig=held?ray:null,Hit=held&&!was?ray:null}:new PlayerInput();
                    Invoke(f.Behavior,"SetFireButton",f.Player,!nativeInput&&held);was=held;
                    each?.Invoke(t);
                    if(alternateFirst)Invoke(f.Behavior,"AimPressed",f.Player,alt,true);
                    Invoke(f.Behavior,"UpdateGun",f.Player,f.State,f.Player.ComponentMiner.ActiveBlockValue,(float)(1/fps));
                    if(!alternateFirst)Invoke(f.Behavior,"AimPressed",f.Player,alt,true);
                    Call("KnifeAnimationController","Update",f.Model,f.Player.ComponentMiner.ActiveBlockValue);
                    var events=Trail();
                    for(;seen<events.Count;seen++)log.Add($"frame {events[seen].Frame-100000} t={events[seen].T-1000:0.000} {events[seen].Event} rounds={events[seen].Rounds} deadline={(events[seen].Deadline<0?-1:events[seen].Deadline-1000):0.000} held={held} alt={alt} clip={Clip(f.Model)}");
                    if(events.Count>=250){trail.GetMethod("Clear").Invoke(null,null);seen=0;}
                }
                return log;
            }
            List<double> Times(List<string> log,string what)=>log.Where(l=>l.Contains(" "+what+" ")).Select(l=>double.Parse(l.Split("t=")[1].Split(' ')[0],System.Globalization.CultureInfo.InvariantCulture)).ToList();
            string Summary(List<string> log,int rounds)=>$"rounds {rounds}; "+string.Join(" | ",log.Take(14))+(log.Count>14?$" | ... {log.Count} events":"");

            foreach(double fps in new[]{30d,60,120})foreach(bool native in new[]{true,false})foreach(int ms in new[]{20,50,100,300}){
                Test($"short-press-{ms}ms-never-fires/{fps}fps/{(native?"mouse":"button")}",()=>{
                    var f=fixture(8,false);var log=Drive(f,fps,2,t=>t>=.1&&t<.1+ms/1000d,nativeInput:native);
                    // A press shorter than one frame may fall between frames; when it was seen, it cocked and was cancelled.
                    Require(Rounds(f.Inv)==8&&Times(log,"shot-primary").Count==0&&Times(log,"shot-alternate").Count==0,"a short press fired: "+Summary(log,Rounds(f.Inv)));
                    Require(Times(log,"cock").Count==Times(log,"cancel-released").Count,"cocking left pending after release: "+Summary(log,Rounds(f.Inv)));
                    Require((double)F(f.State,"PrepareUntil")==-1&&!Hammer(f.Model).Shown,"hammer still drawn after release");
                    return Summary(log,Rounds(f.Inv));
                });
            }
            foreach(double fps in new[]{30d,60,120})Test($"tap-then-press-does-not-ride-the-old-deadline/{fps}fps",()=>{
                var f=fixture(8,false);var log=Drive(f,fps,1.5,t=>t>=.1&&t<.15||t>=.55);
                var shots=Times(log,"shot-primary");var cocks=Times(log,"cock");
                Require(cocks.Count>=2&&shots.Count==1&&Rounds(f.Inv)==7,"expected one shot from the second, held press: "+Summary(log,Rounds(f.Inv)));
                Require(shots[0]>=cocks[1]+primaryCycle-1e-6&&shots[0]<=cocks[1]+primaryCycle+1.01/fps,$"shot at {shots[0]:0.000} is not one cocking time after the second press at {cocks[1]:0.000}: "+Summary(log,Rounds(f.Inv)));
                return Summary(log,Rounds(f.Inv));
            });
            foreach(double fps in new[]{5d,30,60,120})foreach(bool native in new[]{true,false})Test($"held-primary-cadence/{fps}fps/{(native?"mouse":"button")}",()=>{
                var f=fixture(8,false);var log=Drive(f,fps,3.2,t=>t>=.1,nativeInput:native);var shots=Times(log,"shot-primary");
                var gaps=shots.Zip(shots.Skip(1),(a,b)=>b-a).ToList();
                // First shot one cocking time after the press; then one shot per cycle time, as accepted before this change.
                Require(shots.Count>0&&shots[0]>=.1+primaryCycle-1e-6&&shots[0]<=.1+primaryCycle+2.01/fps,"first cocked shot mistimed: "+Summary(log,Rounds(f.Inv)));
                Require(gaps.All(g=>g>=primaryCycle-1e-6&&g<=primaryCycle+2.01/fps),$"cadence outside [{primaryCycle}, {primaryCycle}+2 frames]: {string.Join(",",gaps.Select(g=>g.ToString("0.000")))}");
                Require(Rounds(f.Inv)==8-shots.Count&&Times(log,"commit-primary").Count==shots.Count,"ammunition and commits disagree: "+Summary(log,Rounds(f.Inv)));
                Console.WriteLine($"[r8] held primary {fps} fps {(native?"mouse":"button")}: shots at {string.Join(", ",shots.Select(s=>s.ToString("0.000")))}; gaps {string.Join(", ",gaps.Select(g=>g.ToString("0.000")))}");
                return Summary(log,Rounds(f.Inv));
            });
            Test("click-spam-10hz-never-fires",()=>{
                var f=fixture(8,false);var log=Drive(f,60,3,t=>t%.1<.05);
                Require(Rounds(f.Inv)==8&&Times(log,"shot-primary").Count==0,"spammed clicks fired: "+Summary(log,Rounds(f.Inv)));
                Require(Times(log,"cock").Count>=25&&Times(log,"cancel-released").Count==Times(log,"cock").Count,"clicks not all cocked and cancelled: "+Summary(log,Rounds(f.Inv)));
                return Summary(log,Rounds(f.Inv));
            });
            Test("hammer-shown-over-recoil-and-shot-clip-not-overwritten",()=>{
                var f=fixture(8,false);var seen=new List<(double T,string Clip,bool Hammer,float Seconds)>();
                var log=Drive(f,60,1.6,t=>t>=.1,each:t=>{var h=Hammer(f.Model);seen.Add((t,Clip(f.Model),h.Shown,h.Seconds));});
                var shots=Times(log,"shot-primary");Require(shots.Count>=2,"needs two shots: "+Summary(log,Rounds(f.Inv)));
                var before=seen.Where(s=>s.T>.12&&s.T<shots[0]-.02).ToList();
                Require(before.All(s=>s.Hammer)&&before.Zip(before.Skip(1),(a,b)=>b.Seconds>=a.Seconds).All(x=>x)&&before[^1].Seconds>=1f/3-.03f,"hammer not drawn progressively to full cock before the shot");
                var recoil=seen.Where(s=>s.T>shots[0]+.03&&s.T<shots[0]+.3).ToList();
                Require(recoil.All(s=>s.Clip=="shoot1"),"the shot clip was replaced during its recoil: "+string.Join(",",recoil.Select(s=>s.Clip).Distinct()));
                Require(recoil.All(s=>s.Hammer),"the next cocking is not shown while the recoil plays");
                return $"clips {string.Join(",",seen.Select(s=>s.Clip).Distinct())}; "+Summary(log,Rounds(f.Inv));
            });
            foreach(bool first in new[]{true,false})Test($"alternate-is-its-own-input/{(first?"aim-hook-first":"gun-update-first")}",()=>{
                var f=fixture(8,false);var log=Drive(f,60,3,t=>false,t=>t>=.1&&t%.1<.05,alternateFirst:first);var shots=Times(log,"shot-alternate");
                var gaps=shots.Zip(shots.Skip(1),(a,b)=>b-a).ToList();
                Require(shots.Count>=6&&Times(log,"shot-primary").Count==0&&Rounds(f.Inv)==8-shots.Count,"fanned shots wrong: "+Summary(log,Rounds(f.Inv)));
                Require(shots[0]<=.1+2.01/60&&gaps.All(g=>g>=alternateCycle-1e-6),$"fanned cadence below its cycle time: {string.Join(",",gaps.Select(g=>g.ToString("0.000")))}");
                return Summary(log,Rounds(f.Inv));
            });
            foreach(bool first in new[]{true,false})Test($"primary-held-with-alternate-spam/{(first?"aim-hook-first":"gun-update-first")}",()=>{
                var f=fixture(8,false);var log=Drive(f,60,3,t=>t>=.1,t=>t>=.1&&t%.1<.05,alternateFirst:first);
                var all=log.Where(l=>l.Contains(" shot-")).Select(l=>(Frame:int.Parse(l.Split(' ')[1]),Alternate:l.Contains("shot-alternate"))).ToList();
                Require(all.Select(s=>s.Frame).Distinct().Count()==all.Count,"two shots committed in one frame: "+Summary(log,Rounds(f.Inv)));
                Require(Rounds(f.Inv)==8-all.Count,"ammunition and shots disagree: "+Summary(log,Rounds(f.Inv)));
                // No fanned shot while the hammer is being drawn for the primary.
                foreach(var line in log.Where(l=>l.Contains(" commit-alternate ")))Require(!line.Contains("clip=prepareShoot"),"fanned shot during cocking");
                int minimum=all.Zip(all.Skip(1),(a,b)=>b.Frame-a.Frame).DefaultIfEmpty(0).Min();
                Console.WriteLine($"[r8] primary held + alternate spam ({(first?"aim hook first":"gun update first")}): {all.Count(s=>!s.Alternate)} primary, {all.Count(s=>s.Alternate)} alternate shots in 3 s; smallest gap between any two shots {minimum} frames at 60 fps; refused while cocking {log.Count(l=>l.Contains("alternate-refused-cocking"))}, same frame {log.Count(l=>l.Contains("alternate-refused-same-frame"))}, interval {log.Count(l=>l.Contains("alternate-refused-interval"))}");
                return $"smallest gap {minimum} frames; "+Summary(log,Rounds(f.Inv));
            });
            foreach(double fps in new[]{30d,60,120})Test($"cocked-shot-then-immediate-fan-waits-the-alternate-cycle/{fps}fps",()=>{
                // Hold the primary until it fires, let go, and click the alternate in the very next frames.
                var f=fixture(8,false);double fired=-1;
                var log=Drive(f,fps,2,t=>fired<0,t=>fired>=0&&t%(4/fps)<2/fps,each:t=>{if(fired<0&&Rounds(f.Inv)<8)fired=t;});
                var primary=Times(log,"shot-primary");var alternate=Times(log,"shot-alternate");
                Require(primary.Count==1&&alternate.Count>=1,"sequence did not run: "+Summary(log,Rounds(f.Inv)));
                Require(alternate[0]-primary[0]>=alternateCycle-1e-6&&alternate[0]-primary[0]<=alternateCycle+4.01/fps,$"fanned shot {alternate[0]-primary[0]:0.000} s after the cocked shot: "+Summary(log,Rounds(f.Inv)));
                Console.WriteLine($"[r8] cocked shot then alternate clicks at {fps} fps: primary at {primary[0]:0.000}, first fanned shot at {alternate[0]:0.000} (gap {alternate[0]-primary[0]:0.000} s); refused for interval {log.Count(l=>l.Contains("alternate-refused-interval"))}");
                return Summary(log,Rounds(f.Inv));
            });
            Test("menu-during-cocking-cancels-and-nothing-fires-on-closing",()=>{
                var f=fixture(8,false);var modal=new CanvasWidget();
                var log=Drive(f,60,2,t=>t>=.1,each:t=>{var panel=f.Player.ComponentGui.m_modalPanelContainerWidget;if(t>=.3&&t<.9&&panel.Children.Count==0)panel.Children.Add(modal);if(t>=.9&&panel.Children.Count>0)panel.Children.Remove(modal);});
                var shots=Times(log,"shot-primary");var cocks=Times(log,"cock");
                Require(Times(log,"cancel-menu").Count==1&&shots.All(s=>s>=.9+primaryCycle-1e-6),"a shot was banked behind the menu: "+Summary(log,Rounds(f.Inv)));
                Require(cocks.Count>=2&&shots.Count>=1&&Math.Abs(shots[0]-(cocks[1]+primaryCycle))<=2.01/60,"no fresh cocking after the menu closed: "+Summary(log,Rounds(f.Inv)));
                return Summary(log,Rounds(f.Inv));
            });
            Test("switching-away-cancels",()=>{
                var f=fixture(8,false);int ak=Array.FindIndex(specs,s=>(string)F(s,"Name")=="ak47");
                f.Inv.Values[1]=Terrain.MakeBlockValue(701,0,(int)Call("GunSpec","MakeData",ak,0,false));f.Inv.Counts[1]=1;f.Inv.Counts[10]=0;
                var log=Drive(f,60,1.5,t=>t>=.1&&t<.3,each:t=>f.Inv.ActiveSlotIndex=t>=.3?1:0);
                Require(Times(log,"shot-primary").Count==0&&Rounds(f.Inv)==8&&(double)F(f.State,"PrepareUntil")==-1,"shot or pending cocking after switching: "+Summary(log,Rounds(f.Inv)));
                Require(log.Any(l=>l.Contains(" cancel-")),"no cancel recorded: "+Summary(log,Rounds(f.Inv)));
                return Summary(log,Rounds(f.Inv));
            });
            Test("last-round-and-empty",()=>{
                // Ammunition is in the inventory: after the last round the held trigger starts the reload (2.27 s), no shot.
                var f=fixture(1,false);var log=Drive(f,60,2,t=>t>=.1);
                Require(Times(log,"shot-primary").Count==1&&Rounds(f.Inv)==0&&Times(log,"cock").Count==1,"last round: "+Summary(log,Rounds(f.Inv)));
                var e=fixture(0,false);var empty=Drive(e,60,1.5,t=>t>=.1,t=>t>=.2&&t<.3);
                Require(Times(empty,"cock").Count==0&&Times(empty,"shot-primary").Count==0&&Times(empty,"shot-alternate").Count==0&&Rounds(e.Inv)==0,"empty gun cocked or fired: "+Summary(empty,Rounds(e.Inv)));
                return Summary(log,Rounds(f.Inv))+" || empty: "+Summary(empty,Rounds(e.Inv));
            });
            Test("creative-and-animation-clock-independent",()=>{
                // The animation clock runs at half speed and then stops: the rule follows game time only.
                foreach(double rate in new[]{.5,0d}){
                    var f=fixture(8,true);var log=Drive(f,60,1.3,t=>t>=.1,animationClockRate:rate);var shots=Times(log,"shot-primary");
                    Require(shots.Count==2&&Math.Abs(shots[0]-(.1+primaryCycle))<=2.01/60&&Math.Abs(shots[1]-shots[0]-primaryCycle)<=2.01/60,$"animation clock rate {rate} changed the shots: "+Summary(log,Rounds(f.Inv)));
                }
                return "shots follow game time at animation clock rates 0.5 and 0";
            });
            Test("growth-level-keeps-its-interval-rule",()=>{
                var interval=T("ScGunGrowth").GetMethod("ShotInterval");var parts=new List<string>();
                foreach(int level in new[]{0,10,30,50}){
                    float expected=(float)interval.Invoke(null,[variant,primaryCycle,level]);
                    var f=fixture(8,true);
                    // A registry record of that level, as play leaves it.
                    if(level>0){
                        var registry=registryField.GetValue(null);var registryType=registry.GetType();
                        int id=(int)registryType.GetMethod("Allocate").Invoke(registry,[variant,8,false,700,2250,0]);
                        var record=registryType.GetMethod("Get",Any).Invoke(registry,[id]);var rt=record.GetType();
                        rt.GetField("CounterInstalled").SetValue(record,true);rt.GetField("AppliedGrowthLevel").SetValue(record,level);rt.GetField("GrowthRulesVersion").SetValue(record,(int)T("ScGunGrowth").GetField("RulesVersion").GetRawConstantValue());
                        f.Inv.Values[0]=Terrain.MakeBlockValue(701,0,(int)Call("GunSpec","WithId",variant,id));Set(f.State,"LastValue",f.Inv.Values[0]);Invoke(F(f.State,"Selection"),"Observe",f.Inv,0,f.Inv.Values[0],true);
                        clock.GetField("VirtualNow").SetValue(null,900d);Call("KnifeAnimationController","Update",f.Model,f.Inv.Values[0]);clock.GetField("VirtualNow").SetValue(null,1000d);Call("KnifeAnimationController","Update",f.Model,f.Inv.Values[0]);
                        trail.GetMethod("Clear").Invoke(null,null);
                    }
                    int effective=(int)Call("EffectiveGunStats","LevelOf",f.Inv.Values[0]);expected=(float)interval.Invoke(null,[variant,primaryCycle,effective]);
                    var log=Drive(f,120,.1+expected*3+.2,t=>t>=.1);var shots=Times(log,"shot-primary");
                    Require(effective==level,$"fixture level {level} read back as {effective}");
                    Require(shots.Count>=2&&Math.Abs(shots[0]-(.1+expected))<=2.01/120&&Math.Abs(shots[1]-shots[0]-expected)<=2.01/120,$"level {effective}: interval {expected:0.000} not kept: "+Summary(log,Rounds(f.Inv)));
                    parts.Add($"level {effective}: {expected:0.000}s");
                }
                return string.Join(", ",parts);
            });
        }catch(Exception e){results.Add(new("r8-input/fixture",false,e.ToString()));}
        finally{
            frameIndex.SetValue(null,oldFrame);
            BlocksManager.BlockTypeToIndex.Clear();foreach(var p in savedTypes)BlocksManager.BlockTypeToIndex[p.Key]=p.Value;
            BlocksManager.BlockNameToIndex.Clear();foreach(var p in savedNames)BlocksManager.BlockNameToIndex[p.Key]=p.Value;
            registryField.SetValue(null,oldRegistry);locator.SetValue(null,oldLocator);clock.GetField("Virtual").SetValue(null,oldVirtual);clock.GetField("VirtualNow").SetValue(null,oldTime);SettingsManager.SoundsVolume=oldVolume;
            window.SetValue(null,oldWindow);ScreensManager.CurrentScreen=oldScreen;ScreensManager.m_animationData=oldAnimation;ScreensManager.RootWidget=oldRoot;
        }
        return results;
    }
}
