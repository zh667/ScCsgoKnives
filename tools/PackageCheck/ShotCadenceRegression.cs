using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Engine;
using Engine.Graphics;
using Game;
using GameEntitySystem;

// mpd2-ammo-jitter-20261002 (OpenSpec weapon-state-consistency task 12.4): the shot schedule in single player, through the
// real UpdateGun / Fire path of the packaged DLL, driven by synthetic per-frame input (the fixture of RevolverInputRegression).
// Fire now counts the next shot's time from when this one was due (within SubsystemScGunBlockBehavior.ShotCarry), not from
// the frame it was taken in. That is shared by single player, a host's own player and a client's prediction, so it is
// checked here on its own: a held trigger gives the gun's own cadence at every frame rate from 20 up, never two shots in a
// frame, nothing caught up after a pause or at very low frame rates; the burst cycle and single shots follow the same rule;
// the R8's cocked shot (its own timing) is RevolverInputRegression's and is not touched.
// A build before this change fails the cadence items (it fired a held AK every 110 ms at 100 frames a second): that is the
// measured difference, reported as such.
static class ShotCadenceRegression {
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
        void Test(string name,Func<string> test){try{results.Add(new("shot-cadence/"+name,true,test()));}catch(Exception e){results.Add(new("shot-cadence/"+name,false,(e is TargetInvocationException{InnerException:{} inner}?inner:e).ToString()));}}
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
        try{
            clock.GetField("Virtual").SetValue(null,true);SettingsManager.SoundsVolume=0;locator.SetValue(null,null);
            window.SetValue(null,Enum.Parse(window.FieldType,"Active"));ScreensManager.CurrentScreen=null;ScreensManager.m_animationData=null;ScreensManager.RootWidget=new CanvasWidget();
            foreach(var pair in new[]{("ScKnifeBlock",700),("ScGunBlock",701),("ScGrenadeBlock",702),("ScAmmoBlock",703)}){BlocksManager.BlockTypeToIndex[T(pair.Item1)]=pair.Item2;BlocksManager.BlockNameToIndex[pair.Item1]=pair.Item2;}
            var specs=((Array)T("GunSpec").GetField("All").GetValue(null)).Cast<object>().ToArray();
            int Variant(string name)=>Array.FindIndex(specs,s=>(string)F(s,"Name")==name);
            float Interval(int variant,string field)=>(float)T("ScGunGrowth").GetMethod("ShotInterval").Invoke(null,[variant,(float)F(specs[variant],field),0]);
            double carry=T("SubsystemScGunBlockBehavior").GetField("ShotCarry")?.GetRawConstantValue() is double c?c:0;
            // One player holding a loaded gun (creative: no ammunition items needed, the magazine still counts down).
            var fixture=new Func<int,int,(object Behavior,object State,ComponentPlayer Player,ComponentFirstPersonModel Model,Inventory Inv,SubsystemTime Time)>((variant,rounds)=>{
                var registry=Activator.CreateInstance(T("ScGunRegistry"));registryField.SetValue(null,registry);
                var inv=new Inventory();inv.Values[0]=Terrain.MakeBlockValue(701,0,(int)Call("GunSpec","MakeData",variant,rounds,false));inv.Counts[0]=1;
                var player=Blank<ComponentPlayer>();player.ComponentMiner=Blank<ComponentMiner>();player.ComponentMiner.Inventory=inv;
                player.ComponentHealth=new ComponentHealth{Health=1};player.ComponentInput=Blank<ComponentInput>();player.ComponentInput.m_playerInput=new PlayerInput();
                player.ComponentGui=Blank<ComponentGui>();player.ComponentGui.m_modalPanelContainerWidget=new CanvasWidget();
                player.ComponentBody=new ComponentBody{Position=new Vector3(0,78,0),BoxSize=new Vector3(.65f,1.8f,.65f)};player.ComponentLocomotion=new ComponentLocomotion();
                var widget=Blank<GameWidget>();widget.GuiWidget=new CanvasWidget();widget.m_activeCamera=new Eye();player.PlayerData=Blank<PlayerData>();player.PlayerData.m_gameWidget=widget;
                var model=Blank<ComponentFirstPersonModel>();model.m_componentPlayer=player;
                var entity=Blank<Entity>();entity.m_components=[model,player.ComponentBody];player.m_entity=entity;player.ComponentBody.m_entity=entity;
                var project=new Project();var time=new SubsystemTime{m_gameTime=1000};var info=new SubsystemGameInfo{WorldSettings=Blank<WorldSettings>()};info.WorldSettings.GameMode=GameMode.Creative;
                var terrain=new SubsystemTerrain{Terrain=new Terrain()};var bodies=new SubsystemBodies();var audio=new Audio();
                foreach(var s in new Subsystem[]{time,info,terrain,bodies,audio}){s.m_project=project;project.m_subsystems.Add(s);}
                entity.m_project=project;
                var behavior=Activator.CreateInstance(T("SubsystemScGunBlockBehavior"));((Subsystem)behavior).m_project=project;
                Set(behavior,"m_time",time);Set(behavior,"m_registry",registry);Set(behavior,"m_terrain",terrain);Set(behavior,"m_bodies",bodies);Set(behavior,"m_audio",audio);
                ((IDictionary)F(behavior,"m_brokenNoticeAt"))[player]=1e9;
                var stateType=T("SubsystemScGunBlockBehavior").GetNestedType("GunState",BindingFlags.NonPublic);var state=Activator.CreateInstance(stateType,true);
                ((IDictionary)F(behavior,"m_states"))[player]=state;
                Set(state,"LastValue",inv.Values[0]);Invoke(F(state,"Selection"),"Observe",inv,0,inv.Values[0],true);
                clock.GetField("VirtualNow").SetValue(null,900d);Call("KnifeAnimationController","Update",model,inv.Values[0]);clock.GetField("VirtualNow").SetValue(null,1000d);Call("KnifeAnimationController","Update",model,inv.Values[0]);
                return(behavior,state,player,model,inv,time);
            });
            int Rounds(Inventory inv)=>(int)Call("GunSpec","GetRounds",Terrain.ExtractData(inv.Values[0]));
            // Drives frames whose durations come from frame(k); held(t) is the fire button at time t. Returns the time of each shot
            // (a round leaving the magazine) and the most shots taken in any one frame.
            (List<double> Shots,int MostPerFrame) Drive((object Behavior,object State,ComponentPlayer Player,ComponentFirstPersonModel Model,Inventory Inv,SubsystemTime Time) f,Func<int,double> frame,double seconds,Func<double,bool> held){
                var shots=new List<double>();int most=0;double t=0;
                for(int i=0;t<=seconds;i++){
                    double dt=frame(i);frameIndex.SetValue(null,300000+i);f.Time.m_gameTime=1000+t;f.Time.m_gameTimeDelta=(float)dt;clock.GetField("VirtualNow").SetValue(null,1000+t);
                    f.Player.ComponentInput.m_playerInput=new PlayerInput();Invoke(f.Behavior,"SetFireButton",f.Player,held(t));
                    int before=Rounds(f.Inv);
                    Invoke(f.Behavior,"UpdateGun",f.Player,f.State,f.Player.ComponentMiner.ActiveBlockValue,(float)dt);
                    Call("KnifeAnimationController","Update",f.Model,f.Player.ComponentMiner.ActiveBlockValue);
                    int taken=before-Rounds(f.Inv);for(int k=0;k<taken;k++)shots.Add(t);most=Math.Max(most,taken);
                    t+=dt;
                }
                return(shots,most);
            }
            Func<int,double> Fps(double fps){float dt=(float)(1/fps);return _=>dt;}
            double Noise(uint seed,int k){uint x=seed^(uint)(k*0x9E3779B1);x^=x>>16;x*=0x7FEB352D;x^=x>>15;x*=0x846CA68B;x^=x>>16;return x/4294967296.0;}
            Func<int,double> Uneven(double fps,double spread,uint seed)=>k=>(1/fps)*(1+spread*(2*Noise(seed,k)-1));
            List<double> Gaps(List<double> shots)=>shots.Zip(shots.Skip(1),(a,b)=>b-a).ToList();
            string Show(List<double> gaps)=>gaps.Count==0?"no gaps":$"gaps {gaps.Min()*1000:0.0}-{gaps.Max()*1000:0.0} ms, mean {gaps.Average()*1000:0.00} ms";

            int ak=Variant("ak47");float akInterval=Interval(ak,"CycleSeconds");
            foreach(double fps in new[]{20d,30,50,60,100,144}){
                Test($"held-automatic-keeps-the-guns-cadence/{fps}fps",()=>{
                    var f=fixture(ak,30);double hold=2.55,frame=1/fps;
                    var (shots,most)=Drive(f,Fps(fps),hold+.3,t=>t>=.1&&t<.1+hold);var gaps=Gaps(shots);
                    int expected=(int)Math.Floor(hold/akInterval+1e-6)+1;
                    Require(most<=1,"two shots in one frame");
                    // every shot in the first frame at or after its time on the gun's own schedule, counted from the first shot
                    Require(shots.Count==expected||shots.Count==expected-1,$"{shots.Count} shots in {hold} s (the cadence gives {expected}): {Show(gaps)}");
                    for(int k=0;k<shots.Count;k++){double due=shots[0]+k*(double)akInterval;Require(shots[k]>=due-1e-6&&shots[k]<due+frame+1e-6,$"shot {k+1} at {shots[k]-shots[0]:0.0000} s, due {k*akInterval:0.0000} (a frame is {frame:0.0000})");}
                    Require(Math.Abs(gaps.Average()-akInterval)<=frame/gaps.Count+1e-6,$"mean interval {gaps.Average():0.00000} is not the gun's {akInterval:0.00000}");
                    return $"{shots.Count} shots in {hold} s; {Show(gaps)} (cadence {akInterval*1000:0.0} ms)";
                });
            }
            Test("held-automatic-with-uneven-frames",()=>{
                var f=fixture(ak,30);double hold=2.55;
                var (shots,most)=Drive(f,Uneven(60,.35,7),hold+.3,t=>t>=.1&&t<.1+hold);var gaps=Gaps(shots);
                Require(most<=1&&shots.Count>=25&&Math.Abs(gaps.Average()-akInterval)<.0015&&gaps.Min()>=akInterval-1/60d*1.35-1e-6,$"{shots.Count} shots; {Show(gaps)}");
                return $"{shots.Count} shots; {Show(gaps)}";
            });
            foreach(double fps in new[]{5d,8,12}){
                Test($"slow-frames-catch-nothing-up/{fps}fps",()=>{
                    // Below 20 frames a second a shot can be later than ShotCarry: the next one is then counted from the frame, as before.
                    var f=fixture(ak,30);double hold=2.0;
                    var (shots,most)=Drive(f,Fps(fps),hold+.5,t=>t>=.1&&t<.1+hold);var gaps=Gaps(shots);
                    Require(most<=1&&gaps.All(g=>g>=akInterval-carry-1e-6)&&shots.Count<=(int)Math.Floor(hold/akInterval+1e-6)+1,$"{shots.Count} shots, most {most} a frame; {Show(gaps)}");
                    return $"{shots.Count} shots in {hold} s at {fps} fps; {Show(gaps)}; never two in a frame, none closer than the cadence less {carry*1000:0} ms";
                });
            }
            Test("a-pause-banks-nothing",()=>{
                var f=fixture(ak,30);
                var (shots,most)=Drive(f,Fps(60),2.2,t=>t>=.1&&t<.44||t>=1.3&&t<1.75);
                var second=shots.Where(s=>s>=1.3).ToList();var first=shots.Where(s=>s<1.3).ToList();
                Require(most<=1&&first.Count==4&&second.Count==5&&second[0]<1.3+1/60d+1e-6&&Gaps(second).All(g=>g>=akInterval-1/60d-1e-6),$"first press {first.Count} shots, second {second.Count}; second press {Show(Gaps(second))}");
                return $"after a 0.86 s pause the second press starts its own schedule: {second.Count} shots, {Show(Gaps(second))}";
            });
            Test("single-shots-tapped-faster-than-the-cadence",()=>{
                int deagle=Variant("deagle");float interval=Interval(deagle,"CycleSeconds");var f=fixture(deagle,7);
                // (the mouse path's press edge is the fire button going down)
                var (shots,most)=Drive(f,Fps(60),1.6,t=>t%.15<.05);var gaps=Gaps(shots);
                Require(most<=1&&shots.Count>=4&&gaps.All(g=>g>=interval-carry-1e-6)&&(shots.Count-1)*(double)interval<=shots[^1]-shots[0]+carry+1e-6,$"{shots.Count} shots; {Show(gaps)}; cadence {interval*1000:0.0} ms");
                return $"{shots.Count} shots from taps every 150 ms; {Show(gaps)} (cadence {interval*1000:0.0} ms): none closer than the cadence less {carry*1000:0} ms";
            });
            foreach(string name in new[]{"famas","glock18"})Test($"burst-cycle-keeps-its-time/{name}",()=>{
                int variant=Variant(name);float cycle=Interval(variant,"BurstCycleSeconds"),within=Interval(variant,"BurstShotSeconds");int magazine=(int)F(specs[variant],"Magazine");
                bool automatic=(bool)F(specs[variant],"Automatic");
                var f=fixture(variant,magazine);Set(f.State,"BurstMode",true);
                // The FAMAS repeats its burst while held; the Glock needs a press for each (pressed here every 90 ms).
                var (shots,most)=Drive(f,Fps(100),3*cycle+.45,t=>t>=.1&&(automatic||(t-.1)%.09<.04));
                Require(most<=1&&shots.Count>=9,$"{shots.Count} shots");
                var starts=Enumerable.Range(0,shots.Count/3).Select(i=>shots[i*3]).ToList();var between=Gaps(starts);
                if(automatic)Require(between.All(g=>Math.Abs(g-cycle)<=.0101)&&Math.Abs(between.Average()-cycle)<=.0101/between.Count+1e-6,$"burst starts {Show(between)}; cycle {cycle*1000:0.0} ms");
                else Require(between.All(g=>g>=cycle-carry-1e-6&&g<cycle+.09+.0101),$"burst starts {Show(between)}; cycle {cycle*1000:0.0} ms");
                for(int i=0;i+2<shots.Count;i+=3)Require(shots[i+1]-shots[i]>=within-1e-6&&shots[i+2]-shots[i+1]>=within-1e-6,"a burst's rounds closer than their interval");
                return $"{starts.Count} bursts of three; burst to burst {Show(between)} (cycle {cycle*1000:0.0} ms{(automatic?"":"; a press every 90 ms")})";
            });
        }catch(Exception e){results.Add(new("shot-cadence/fixture",false,e.ToString()));}
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
