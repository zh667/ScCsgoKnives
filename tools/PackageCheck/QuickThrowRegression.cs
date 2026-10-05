using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Engine.Graphics;
using Game;
using GameEntitySystem;

// quick-throw-20261002: letting go throws - at once, wherever the pull has got to; held, the whole preparation stays.
// Through the real SetThrowButton / RequestThrow / Update path of the packaged DLL, frame by frame (the fixture of
// ThrowPresentationRegression). The rule is CS2's own (its viewmodel_grenade graph enters Throw from any state with no
// blend; its weapon data has no tap/hold threshold), so there is no boundary at 150 ms or anywhere else: the checks ask
// that a release at ANY moment throws then, exactly once, with the strength of its button, and that holding never throws.
// A build before this change fails the tap items: it waited for the whole pull (about a second) after a tap.
static class QuickThrowRegression {
    internal record Result(string Name,bool Ok,string Detail);
    sealed class Inventory:IInventory {
        public int[] Values=new int[12],Counts=new int[12];
        public Project Project=>null;public int SlotsCount=>12;public int VisibleSlotsCount {get;set;}=10;public int ActiveSlotIndex {get;set;}
        public int GetSlotValue(int s)=>Counts[s]>0?Values[s]:0;public int GetSlotCount(int s)=>Counts[s];public int GetSlotCapacity(int s,int v)=>100;public int GetSlotProcessCapacity(int s,int v)=>0;
        public void AddSlotItems(int s,int v,int n){Values[s]=v;Counts[s]+=n;}public int RemoveSlotItems(int s,int n){n=Math.Min(n,Counts[s]);Counts[s]-=n;return n;}
        public void ProcessSlotItems(int s,int v,int n,int p,out int rv,out int rn){rv=rn=0;}public void DropAllItems(Vector3 p){}
    }
    sealed class Audio:SubsystemAudio {public override void PlaySound(string n,float v,float pitch,Vector3 p,float d,bool delay){}public override void PlayRandomSound(string n,float v,float pitch,Vector3 p,float d,bool delay){}}
    sealed class OpenTerrain:SubsystemTerrain {public List<(Vector3 From,Vector3 To)> Rays=[];public Func<Vector3,Vector3,TerrainRaycastResult?> Wall;
        public override TerrainRaycastResult? Raycast(Vector3 a,Vector3 b,bool i,bool s,Func<int,float,bool> f){Rays.Add((a,b));return Wall?.Invoke(a,b);}}
    sealed class View:Camera {
        public Vector3 At,Look=-Vector3.UnitZ;
        public View():base(null){}
        public override Vector3 ViewPosition=>At;public override Vector3 ViewDirection=>Look;public override Vector3 ViewUp=>Vector3.UnitY;public override Vector3 ViewRight=>Vector3.UnitX;
        public override Matrix ViewMatrix=>Matrix.Identity;public override Matrix InvertedViewMatrix=>Matrix.Identity;public override Matrix ProjectionMatrix=>Matrix.Identity;public override Matrix ScreenProjectionMatrix=>Matrix.Identity;
        public override Matrix InvertedProjectionMatrix=>Matrix.Identity;public override Matrix ViewProjectionMatrix=>Matrix.Identity;public override Vector2 ViewportSize=>new(1280,720);public override Matrix ViewportMatrix=>Matrix.Identity;
        public override BoundingFrustum ViewFrustum=>new(Matrix.Identity);public override bool UsesMovementControls=>false;public override bool IsEntityControlEnabled=>true;/* the player's own camera: control allowed (round 3 gate) */public override void Update(float dt){}
    }
    sealed record Frame(int Index,double T,bool Pressed,int Count,int Presented,bool Active,bool Released,int Stage,int Flying);
    internal static List<Result> Run(Assembly mod){
        List<Result> results=[];
        void Test(string name,Func<string> test){try{results.Add(new("quick-throw/"+name,true,test()));}catch(Exception e){results.Add(new("quick-throw/"+name,false,(e is TargetInvocationException{InnerException:{} inner}?inner:e).ToString()));}}
        void Require(bool ok,string why){if(!ok)throw new Exception(why);}
        const BindingFlags Any=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        Type T(string n)=>mod.GetType("Game."+n,true);
        object F(object o,string n)=>o.GetType().GetField(n,Any).GetValue(o);
        object P(object o,string n)=>o.GetType().GetProperty(n,Any).GetValue(o);
        void Set(object o,string n,object v)=>o.GetType().GetField(n,Any).SetValue(o,v);
        object Call(string t,string m,params object[] a)=>T(t).GetMethod(m,Any).Invoke(null,a);
        object Invoke(object o,string m,params object[] a)=>o.GetType().GetMethod(m,Any).Invoke(o,a);
        U Blank<U>()=>(U)RuntimeHelpers.GetUninitializedObject(typeof(U));
        var savedTypes=new Dictionary<Type,int>(BlocksManager.BlockTypeToIndex);var savedNames=new Dictionary<string,int>(BlocksManager.BlockNameToIndex);
        var clock=T("KnifeClock");bool oldVirtual=(bool)clock.GetField("Virtual").GetValue(null);double oldTime=(double)clock.GetField("VirtualNow").GetValue(null);float oldVolume=SettingsManager.SoundsVolume;
        var window=typeof(Window).GetField("m_state",BindingFlags.Static|BindingFlags.NonPublic);var oldWindow=window.GetValue(null);
        var oldScreen=ScreensManager.CurrentScreen;var oldAnimation=ScreensManager.m_animationData;var oldRoot=ScreensManager.RootWidget;
        var frameIndex=typeof(Time).GetProperty("FrameIndex");int oldFrame=Time.FrameIndex;
        var preview=T("ScUiSettings").GetField("GrenadePreview");bool oldPreview=(bool)preview.GetValue(null);
        var oldPads=SettingsManager.GamepadMappingSettings;
        string[] assets=(string[])T("ScGrenadeBlock").GetField("Assets").GetValue(null);
        try{
            clock.GetField("Virtual").SetValue(null,true);SettingsManager.SoundsVolume=0;preview.SetValue(null,true);
            window.SetValue(null,Enum.Parse(window.FieldType,"Active"));ScreensManager.CurrentScreen=null;ScreensManager.m_animationData=null;ScreensManager.RootWidget=new CanvasWidget();
            foreach(var pair in new[]{("ScKnifeBlock",700),("ScGunBlock",701),("ScGrenadeBlock",702),("ScAmmoBlock",703)}){BlocksManager.BlockTypeToIndex[T(pair.Item1)]=pair.Item2;BlocksManager.BlockNameToIndex[pair.Item1]=pair.Item2;}
            int Value(int kind)=>(int)Call("ScGrenadeBlock","Value",kind);
            var standing=new Vector3(8.5f,78,8.5f);Vector3 eye=standing+Vector3.UnitY*1.8f*.85f;

            // One player holding a stack of one throwable, the grenade subsystem with everything a throw touches.
            var fixture=new Func<int,int,bool,View,(object Grenades,ComponentPlayer Player,ComponentFirstPersonModel Model,ComponentHumanModel Human,Inventory Inv,SubsystemTime Time,OpenTerrain Terrain,View Camera)>((kind,count,creative,camera)=>{
                var inv=new Inventory();inv.Values[0]=Value(kind);inv.Counts[0]=count;
                var player=Blank<ComponentPlayer>();player.ComponentMiner=Blank<ComponentMiner>();player.ComponentMiner.Inventory=inv;
                player.ComponentHealth=new ComponentHealth{Health=1};player.ComponentInput=Blank<ComponentInput>();player.ComponentInput.m_playerInput=new PlayerInput();
                player.ComponentGui=Blank<ComponentGui>();player.ComponentGui.m_modalPanelContainerWidget=new CanvasWidget();
                player.ComponentBody=new ComponentBody{Position=standing,BoxSize=new Vector3(.65f,1.8f,.65f)};player.ComponentLocomotion=new ComponentLocomotion();
                var widget=Blank<GameWidget>();widget.GuiWidget=new CanvasWidget();widget.m_activeCamera=camera;player.PlayerData=Blank<PlayerData>();player.PlayerData.m_gameWidget=widget;player.PlayerData.PlayerIndex=0;
                var model=Blank<ComponentFirstPersonModel>();model.m_componentPlayer=player;
                var human=Blank<ComponentHumanModel>();human.m_componentMiner=player.ComponentMiner;human.m_componentPlayer=player;player.ComponentMiner.ComponentPlayer=player; // as ComponentHumanModel.Load and ComponentMiner.Load set them
                var entity=Blank<Entity>();entity.m_components=[model,player,player.ComponentBody,human];player.m_entity=entity;player.ComponentBody.m_entity=entity;human.m_entity=entity;
                var project=new Project();var time=new SubsystemTime{m_gameTime=1000};var info=new SubsystemGameInfo{WorldSettings=Blank<WorldSettings>()};info.WorldSettings.GameMode=creative?GameMode.Creative:GameMode.Survival;
                var terrain=new OpenTerrain{Terrain=new Terrain()};for(int x=-4;x<=4;x++)for(int z=-4;z<=4;z++)terrain.Terrain.AllocateChunk(x,z).State=TerrainChunkState.Valid;
                var bodies=new SubsystemBodies();var audio=new Audio();var players=new SubsystemPlayers();players.m_componentPlayers.Add(player);
                var grenades=(Subsystem)Activator.CreateInstance(T("SubsystemScGrenades"));
                foreach(var s in new Subsystem[]{time,info,terrain,bodies,audio,players,grenades}){s.m_project=project;project.m_subsystems.Add(s);}
                entity.m_project=project;
                foreach(var (f,v) in new (string,object)[]{("m_time",time),("m_terrain",terrain),("m_bodies",bodies),("m_players",players),("m_info",info)})Set(grenades,f,v);
                // The draw finished long ago on the animation clock.
                clock.GetField("VirtualNow").SetValue(null,900d);Call("KnifeAnimationController","Update",model,inv.Values[0]);clock.GetField("VirtualNow").SetValue(null,1000d);Call("KnifeAnimationController","Update",model,inv.Values[0]);
                return(grenades,player,model,human,inv,time,terrain,camera);
            });
            View FirstPerson()=>new(){At=eye,Look=Vector3.Normalize(new Vector3(1,.25f,0))};
            var presented=T("ScThirdPerson").GetMethod("PresentedValue",Any);
            (int Value,bool Active,bool Released,int Stage) Shown((object Grenades,ComponentPlayer Player,ComponentFirstPersonModel Model,ComponentHumanModel Human,Inventory Inv,SubsystemTime Time,OpenTerrain Terrain,View Camera) f){
                object[] a=[f.Human,null];int value=(int)presented.Invoke(null,a);
                return(value,(bool)P(a[1],"Active"),(bool)P(a[1],"Released"),(int)P(a[1],"Stage"));
            }
            IList Flying(object grenades)=>(IList)F(grenades,"m_active");
            object Preparing(object grenades,ComponentPlayer player){var map=(IDictionary)F(grenades,"m_preparing");return map.Contains(player)?map[player]:null;}
            Vector3 Read(object state,string field)=>(Vector3)state.GetType().GetField(field).GetValue(state);
            bool Aim((Vector3 From,Vector3 To) ray)=>Math.Abs((ray.To-ray.From).Length()-48)<.01f; // the 48 m aim ray; flight rays are centimetres long
            string Clip(ComponentFirstPersonModel m)=>(string)Call("KnifeAnimationController","CurrentClip",m);
            int Made(object grenades)=>(int)F(grenades,"m_nextGrenadeId");
            // Drives frames: held(t) is the throw input at time t (seconds from the start). input: "button" the on-screen
            // throw button held; "click" the same button reporting a tap inside one frame; "mouse" the engine's rays, with
            // the request made each frame the button is down, as the mod's input hooks make it.
            var trail=new List<(int Index,string Clip,float Pull,int Made)>();
            List<Frame> Drive((object Grenades,ComponentPlayer Player,ComponentFirstPersonModel Model,ComponentHumanModel Human,Inventory Inv,SubsystemTime Time,OpenTerrain Terrain,View Camera) f,bool low,Func<double,bool> held,double seconds,
                    Func<int,double> frame=null,string input="button",Action<int,double> each=null,Func<double,bool> cancel=null){
                var frames=new List<Frame>();trail.Clear();double t=0;bool was=false;frame??=_=>1/60d;
                for(int i=0;t<=seconds;i++){
                    double dt=frame(i);frameIndex.SetValue(null,300000+i);f.Time.m_gameTime=1000+t;f.Time.m_gameTimeDelta=(float)dt;clock.GetField("VirtualNow").SetValue(null,1000+t);
                    bool pressed=held(t),cancelled=cancel?.Invoke(t)==true;var ray=new Ray3(f.Camera.At,f.Camera.Look);
                    if(input=="mouse"){
                        f.Player.ComponentInput.m_playerInput=low?new PlayerInput{Aim=pressed?ray:null}:new PlayerInput{Dig=pressed?ray:null,Hit=pressed&&!was?ray:null};
                        Invoke(f.Grenades,"SetThrowButton",f.Player,low,false,false,false,2);
                        if(pressed)Invoke(f.Grenades,"RequestThrow",f.Player,low,false);
                    }
                    else{f.Player.ComponentInput.m_playerInput=new PlayerInput();Invoke(f.Grenades,"SetThrowButton",f.Player,low,input=="button"&&pressed,input=="click"&&pressed&&!was,cancelled,2);}
                    was=pressed;
                    ((IUpdateable)f.Grenades).Update((float)dt);
                    Call("KnifeAnimationController","Update",f.Model,f.Player.ComponentMiner.ActiveBlockValue);
                    each?.Invoke(i,t);
                    var shown=Shown(f);
                    frames.Add(new(i,t,pressed,f.Inv.Counts[f.Inv.ActiveSlotIndex],shown.Value,shown.Active,shown.Released,shown.Stage,Flying(f.Grenades).Count));
                    trail.Add((i,Clip(f.Model),(float)P(Invoke(f.Grenades,"ThrowPhase",f.Player),"Pull"),Made(f.Grenades)));
                    t+=dt;
                }
                return frames;
            }
            Func<double,bool> Press(double from,double seconds)=>t=>t>=from-1e-9&&t<from+seconds-1e-9;
            Func<int,double> Fps(double fps){double dt=1/fps;return _=>dt;}
            double Noise(uint seed,int k){uint x=seed^(uint)(k*0x9E3779B1);x^=x>>16;x*=0x7FEB352D;x^=x>>15;x*=0x846CA68B;x^=x>>16;return x/4294967296.0;}
            float Seconds(int kind,string alias)=>(float)Call("Cs2Rig","Duration",assets[kind],alias);
            float ReleaseTime(int kind,bool low)=>(float)Call("Cs2Rig","GrenadeReleaseTime",assets[kind],low?"throwLow":"throwHigh");
            // The first frame the input is up again after having been down, and the frame the grenade exists from.
            (Frame Up,Frame Thrown) Marks(List<Frame> frames){
                int down=frames.FindIndex(x=>x.Pressed);Require(down>=0,"the input was never down");
                var up=frames.Skip(down).FirstOrDefault(x=>!x.Pressed);Require(up!=null,"the input never went up");
                var thrown=frames.FirstOrDefault(x=>x.Flying>0);Require(thrown!=null,"nothing was thrown: "+Log(frames));
                return(up,thrown);
            }
            string Log(List<Frame> frames){
                // Only the frames where something changes.
                var lines=new List<string>();Frame last=null;
                foreach(var x in frames){if(last==null||x.Pressed!=last.Pressed||x.Count!=last.Count||x.Presented!=last.Presented||x.Active!=last.Active||x.Released!=last.Released||x.Stage!=last.Stage||x.Flying!=last.Flying)
                    lines.Add($"frame {x.Index} t={x.T:0.000} button={(x.Pressed?1:0)} count={x.Count} shown={(x.Presented==0?"empty":"grenade")} throw={(x.Active?"stage "+x.Stage+(x.Released?" released":""):"none")} flying={x.Flying}");last=x;}
                return string.Join(" | ",lines);
            }
            // The contract every throw keeps, whatever the stack: returns the release and end frames.
            (Frame Release,Frame End) Contract(List<Frame> frames,int value,int before,int after,bool heldAfter){
                string log=Log(frames);
                var release=frames.FirstOrDefault(x=>x.Flying>0);Require(release!=null,"nothing was thrown: "+log);
                Require(frames.All(x=>x.Flying<=1),"more than one grenade left the hand: "+log);
                var released=frames.First(x=>x.Released);
                Require(released.Index==release.Index,$"the hand let go on frame {released.Index} but the grenade exists from frame {release.Index}: "+log);
                Require(frames[release.Index-1].Count==before&&release.Count==after,$"the stack went {frames[release.Index-1].Count} -> {release.Count} at the release, expected {before} -> {after}: "+log);
                var started=frames.First(x=>x.Active);
                foreach(var x in frames.Where(x=>x.Index>=started.Index&&x.Index<release.Index))
                    Require(x.Presented==value&&x.Active&&!x.Released,$"frame {x.Index}: before the release the hand must hold the throwable: "+log);
                var end=frames.FirstOrDefault(x=>x.Index>release.Index&&!x.Active);Require(end!=null,"the throw never ended: "+log);
                foreach(var x in frames.Where(x=>x.Index>=release.Index&&x.Index<end.Index))
                    Require(x.Presented==value&&x.Active&&x.Released,$"frame {x.Index}: between the release and the end of the action the hand must be known empty (a second grenade in the hand is a ghost): "+log);
                foreach(var x in frames.Where(x=>x.Index>=end.Index)){
                    Require(x.Count==after,$"frame {x.Index}: the stack changed after the throw: "+log);
                    Require(heldAfter?x.Presented==value:x.Presented==0,$"frame {x.Index}: after the action the hand shows {(x.Presented==0?"nothing":"a throwable")}: "+log);
                }
                return(release,end);
            }

            const double At=.2;double frame60=1/60d;
            string[] strength=["strong","weak"];
            // ---- a tap
            for(int k=0;k<assets.Length;k++)foreach(bool low in new[]{false,true}){
                int kind=k;
                Test($"tap-throws-at-once/{assets[kind]}/{strength[low?1:0]}",()=>{
                    var f=fixture(kind,2,false,FirstPerson());var frames=Drive(f,low,Press(At,.05),3.5);
                    var (release,end)=Contract(frames,Value(kind),2,1,true);var (up,thrown)=Marks(frames);
                    float wait=ReleaseTime(kind,low),pull=Seconds(kind,"pullpin");
                    Require(thrown.T-up.T<=wait+frame60+1e-6,$"the grenade left {thrown.T-up.T:0.000} s after the button went up; the throw's own release moment is {wait:0.000} s: "+Log(frames));
                    Require(thrown.T-At<pull*.5,$"a tap waited {thrown.T-At:0.000} s (the pull is {pull:0.000} s): "+Log(frames));
                    Require(frames.All(x=>x.Stage!=1),"a tap passed through the held-ready stage: "+Log(frames));
                    var clips=trail.Where(x=>frames[x.Index].Active).Select(x=>x.Clip).Distinct().ToList();string throwing=low?"throwLow":"throwHigh";
                    Require(clips.SequenceEqual(new[]{"pullpin",throwing}),$"first-person clips of a tap: {string.Join(" > ",clips)} (expected pullpin > {throwing})");
                    Require(Made(f.Grenades)-1==1,$"{Made(f.Grenades)-1} grenades were made");
                    return $"button down {At:0.000}, up {up.T:0.000}, grenade {thrown.T:0.000} ({(thrown.T-up.T)*1000:0} ms after the release; release moment of the clip {wait*1000:0} ms; pull {pull*1000:0} ms); action ended {end.T:0.000}";
                });
            }
            // ---- a hold
            foreach(var (kind,low) in new[]{(0,false),(1,true),(2,false),(3,false),(3,true),(4,false),(5,true)}){
                Test($"hold-keeps-the-pull-and-the-ready-pose/{assets[kind]}/{strength[low?1:0]}",()=>{
                    var f=fixture(kind,2,false,FirstPerson());double hold=2.5;var frames=Drive(f,low,Press(At,hold),6);
                    Contract(frames,Value(kind),2,1,true);var (up,thrown)=Marks(frames);float pull=Seconds(kind,"pullpin"),wait=ReleaseTime(kind,low);
                    var pulling=frames.Where(x=>x.Active&&x.Stage==0).ToList();var ready=frames.Where(x=>x.Stage==1).ToList();
                    Require(frames.Where(x=>x.Pressed).All(x=>x.Flying==0),"a grenade left while the button was held: "+Log(frames));
                    Require(Math.Abs(pulling.Count*frame60-pull)<=2*frame60,$"the pull was shown for {pulling.Count*frame60:0.000} s, its clip is {pull:0.000} s: "+Log(frames));
                    Require(ready.Count>0&&Math.Abs(ready.Count*frame60-(hold-pull))<=3*frame60,$"held ready for {ready.Count*frame60:0.000} s of a {hold} s hold with a {pull:0.000} s pull: "+Log(frames));
                    Require(thrown.T-up.T<=wait+frame60+1e-6&&thrown.T>=up.T-1e-9,$"the grenade left {thrown.T-up.T:0.000} s after the release: "+Log(frames));
                    var clips=trail.Where(x=>frames[x.Index].Active).Select(x=>x.Clip).Distinct().ToList();
                    Require(clips.SequenceEqual(new[]{"pullpin",low?"holdLow":"holdHigh",low?"throwLow":"throwHigh"}),$"first-person clips of a hold: {string.Join(" > ",clips)}");
                    Require(trail.Where(x=>frames[x.Index].Stage>=1).All(x=>x.Pull==1),"the phase of a full pull does not say so");
                    return $"pull shown {pulling.Count*frame60:0.000} s, held ready {ready.Count*frame60:0.000} s, grenade {(thrown.T-up.T)*1000:0} ms after the release; clips {string.Join(" > ",clips)}";
                });
            }
            Test("held-never-throws-by-itself",()=>{
                var f=fixture(0,2,false,FirstPerson());var frames=Drive(f,false,t=>t>=At,40,Fps(20));
                Require(frames.All(x=>x.Flying==0&&x.Count==2)&&Made(f.Grenades)==1&&frames.Last().Stage==1&&frames.Last().Active,"a throw held for 40 s threw, spent or ended: "+Log(frames));
                return "40 s held: nothing thrown, nothing spent, still held ready";
            });
            // ---- the release at any moment: there is no boundary in the hold's length
            foreach(bool low in new[]{false,true})Test($"release-at-any-moment-throws-then/{strength[low?1:0]}",()=>{
                float pull=Seconds(0,"pullpin"),wait=ReleaseTime(0,low);var lines=new List<string>();
                foreach(double hold in new[]{frame60,2*frame60,.05,.1,.149,.15,.151,.2,.3,.6,pull-frame60,pull,pull+frame60,pull+.5,2.0}){
                    var f=fixture(0,2,false,FirstPerson());var frames=Drive(f,low,Press(At,hold),hold+3.5);
                    Contract(frames,Value(0),2,1,true);var (up,thrown)=Marks(frames);
                    Require(thrown.T-up.T<=wait+frame60+1e-6&&thrown.T>=up.T-1e-9,$"held {hold:0.000} s: the grenade left {thrown.T-up.T:0.000} s after the release (release moment {wait:0.000} s): "+Log(frames));
                    bool ready=frames.Any(x=>x.Stage==1);
                    Require(ready==(up.T-At>=pull+frame60)||Math.Abs(up.T-At-pull)<=frame60+1e-6,$"held {hold:0.000} s (pull {pull:0.000}): held-ready stage {(ready?"shown":"not shown")}: "+Log(frames));
                    Require(Made(f.Grenades)-1==1,$"held {hold:0.000} s: {Made(f.Grenades)-1} grenades");
                    lines.Add($"{hold*1000:0}ms>{(thrown.T-up.T)*1000:0}ms{(ready?"(ready)":"")}");
                }
                return "hold > grenade after the release: "+string.Join(" ",lines);
            });
            // ---- frame rates
            foreach(var (name,rate,step) in new (string,double,Func<int,double>)[]{("20fps",20,Fps(20)),("30fps",30,Fps(30)),("60fps",60,Fps(60)),("144fps",144,Fps(144)),("uneven-60fps",60*.65,k=>1/60d*(1+.35*(2*Noise(5,k)-1)))})
            foreach(bool low in new[]{false,true})Test($"tap-at-{name}/{strength[low?1:0]}",()=>{
                var f=fixture(1,2,false,FirstPerson());var frames=Drive(f,low,Press(At,.06),3.5,step);
                Contract(frames,Value(1),2,1,true);var (up,thrown)=Marks(frames);float wait=ReleaseTime(1,low);
                Require(thrown.T-up.T<=wait+1/rate+1e-6,$"the grenade left {thrown.T-up.T:0.000} s after the release (release moment {wait:0.000} s, a frame {1/rate:0.000} s): "+Log(frames));
                Require(Made(f.Grenades)-1==1,$"{Made(f.Grenades)-1} grenades");
                return $"up {up.T:0.000}, grenade {thrown.T:0.000} ({(thrown.T-up.T)*1000:0} ms)";
            });
            // ---- a tap inside one frame, and the mouse
            foreach(bool low in new[]{false,true})Test($"touch-tap-inside-one-frame/{strength[low?1:0]}",()=>{
                var f=fixture(0,2,false,FirstPerson());var frames=Drive(f,low,Press(At,frame60*.5),3.5,input:"click");
                var (release,end)=Contract(frames,Value(0),2,1,true);
                Require(release.T-At<=ReleaseTime(0,low)+2*frame60+1e-6&&Made(f.Grenades)-1==1,$"a tap inside one frame threw at {release.T:0.000} ({Made(f.Grenades)-1} grenades): "+Log(frames));
                return $"tapped at {At:0.000}, grenade {release.T:0.000}";
            });
            Vector3 Velocity(object state)=>Read(state,"Velocity");
            // (The mouse path asks the engine whether a gamepad holds the same action; its mapping table must exist.)
            if(SettingsManager.GamepadMappingSettings==null)SettingsManager.InitializeGamepadMappingSettings();
            foreach(bool low in new[]{false,true})Test($"mouse-{(low?"right-weak":"left-strong")}-tap-and-hold-throw-alike",()=>{
                (Vector3 V,float Fuse,double After) Throw(double hold,string input){
                    var f=fixture(0,2,false,FirstPerson());object state=null;Vector3 v=default;float fuse=0;
                    var frames=Drive(f,low,Press(At,hold),hold+3,input:input,each:(i,t)=>{if(state==null&&Flying(f.Grenades).Count>0){state=Flying(f.Grenades)[0];v=Velocity(state);fuse=(float)state.GetType().GetField("Remaining").GetValue(state);}});
                    Contract(frames,Value(0),2,1,true);var (up,thrown)=Marks(frames);Require(Made(f.Grenades)-1==1,$"{input}, held {hold}: {Made(f.Grenades)-1} grenades");
                    return(v,fuse,thrown.T-up.T);
                }
                var tap=Throw(.05,"mouse");var held=Throw(2,"mouse");var button=Throw(2,"button");
                var expected=((Vector3 Position,Vector3 Velocity))Call("ScGrenadeBallistics","Launch",eye,FirstPerson().Look,Vector3.Zero,low,(Func<Vector3,Vector3,Vector3?>)((a,b)=>null));
                Require(tap.V==held.V&&held.V==button.V&&tap.V==expected.Velocity,$"launch velocity: tap {tap.V}, hold {held.V}, button hold {button.V}, the {strength[low?1:0]} throw {expected.Velocity}");
                Require(tap.Fuse==held.Fuse&&tap.Fuse==button.Fuse,$"fuse: tap {tap.Fuse}, hold {held.Fuse}");
                Require(tap.After<=ReleaseTime(0,low)+frame60+1e-6,$"the mouse tap's grenade left {tap.After:0.000} s after the release");
                return $"speed {tap.V.Length():0.00} and fuse {tap.Fuse:0.00} s the same for a tap and a hold; tap: {tap.After*1000:0} ms after the release";
            });
            Test("strong-and-weak-stay-different",()=>{
                Vector3 Throw(bool low){var f=fixture(0,2,false,FirstPerson());Vector3 v=default;bool seen=false;Drive(f,low,Press(At,.05),2,each:(i,t)=>{if(!seen&&Flying(f.Grenades).Count>0){seen=true;v=Velocity(Flying(f.Grenades)[0]);}});Require(seen,"nothing thrown");return v;}
                Vector3 strong=Throw(false),weak=Throw(true);
                Require(strong.Length()>weak.Length()*1.5f,$"a quick strong throw {strong.Length():0.00} m/s, a quick weak one {weak.Length():0.00} m/s");
                return $"quick strong {strong.Length():0.00} m/s, quick weak {weak.Length():0.00} m/s";
            });
            // ---- one grenade, one item
            for(int k=0;k<assets.Length;k++){
                int kind=k;
                Test("tap-with-the-last-one-leaves-an-empty-hand/"+assets[kind],()=>{
                    var f=fixture(kind,1,false,FirstPerson());var frames=Drive(f,false,Press(At,.05),3.5);
                    var (release,end)=Contract(frames,Value(kind),1,0,false);
                    Require(Made(f.Grenades)-1==1&&Flying(f.Grenades).Cast<object>().All(s=>(int)s.GetType().GetField("Kind").GetValue(s)==kind),"the wrong grenade, or not exactly one");
                    return $"released {release.T:0.000}, hand empty, action ended {end.T:0.000}";
                });
            }
            Test("tap-in-creative-keeps-the-stack",()=>{
                var f=fixture(3,4,true,FirstPerson());var frames=Drive(f,false,Press(At,.05),3.5);
                Contract(frames,Value(3),4,4,true);Require(Made(f.Grenades)-1==1,$"{Made(f.Grenades)-1} grenades");
                return "one molotov thrown, stack still 4";
            });
            Test("taps-in-a-row-throw-one-each-and-none-in-between",()=>{
                // A press every 0.25 s for 9 s: the presses that fall inside a running throw, or before the next grenade is in
                // hand again, throw nothing.
                var f=fixture(1,9,false,FirstPerson());var made=new List<double>();int last=1;
                var frames=Drive(f,false,t=>t>=At&&(t-At)%.25<.05,9,each:(i,t)=>{int n=Made(f.Grenades);if(n!=last){made.Add(t);last=n;}});
                int thrown=Made(f.Grenades)-1;var gaps=made.Zip(made.Skip(1),(a,b)=>b-a).ToList();
                Require(thrown>=3&&f.Inv.Counts[0]==9-thrown,$"{thrown} grenades made, stack {f.Inv.Counts[0]} of 9");
                float clip=Seconds(1,"throwHigh");
                Require(gaps.All(g=>g>=clip-1e-6),$"two throws {gaps.Min():0.000} s apart; one throw's action is {clip:0.000} s");
                return $"{thrown} throws from {frames.Count(x=>x.Pressed&&!frames[Math.Max(0,x.Index-1)].Pressed)} presses in 9 s; between throws {gaps.Min():0.000}-{gaps.Max():0.000} s (the throw's action {clip:0.000} s, then the next one is drawn)";
            });
            // ---- nothing committed is thrown; nothing thrown comes back
            Test("switching-away-before-the-release-moment-cancels",()=>{
                // The weak HE throw lets go 0.133 s into its clip: the slot is changed in between.
                float wait=ReleaseTime(0,true);Require(wait>3*frame60,$"the weak HE release moment is {wait}");
                var f=fixture(0,2,false,FirstPerson());double upAt=At+.05;
                var frames=Drive(f,true,Press(At,.05),3,each:(i,t)=>{if(t>=upAt+frame60)f.Inv.ActiveSlotIndex=1;});
                Require(frames.All(x=>x.Flying==0)&&f.Inv.Counts[0]==2&&Made(f.Grenades)==1,"switching away before the release moment threw or spent a grenade: "+Log(frames));
                Require(frames.Any(x=>x.Stage==2)&&!frames.Last().Active,"the quick throw was not begun and then cancelled: "+Log(frames));
                return Log(frames);
            });
            Test("cancelled-or-dead-before-the-release-throws-nothing",()=>{
                var f=fixture(0,2,false,FirstPerson());var frames=Drive(f,false,Press(At,.1),3,cancel:t=>t>=At+.1-1e-9&&t<At+.1+frame60-1e-9);
                Require(frames.All(x=>x.Flying==0&&x.Count==2)&&!frames.Last().Active,"a cancelled pull threw or spent: "+Log(frames));
                var g=fixture(0,2,false,FirstPerson());var dead=Drive(g,true,Press(At,.05),3,each:(i,t)=>{if(t>=At+.05)g.Player.ComponentHealth.Health=0;});
                Require(dead.All(x=>x.Flying==0)&&g.Inv.Counts[0]==2,"a dead player's quick throw left the hand: "+Log(dead));
                return "cancelled during the pull: nothing; dead between the release of the button and the release moment: nothing";
            });
            // ---- the draw gate
            Test("a-tap-while-the-grenade-is-still-drawn-throws-when-it-is-in-hand",()=>{
                var f=fixture(0,2,false,FirstPerson());f.Inv.Values[1]=Value(1);f.Inv.Counts[1]=2;
                double drawn=.1,tap=.2;float draw=(float)Call("CsmcKnifeRig","GetProfileDuration",(int)Call("KnifeAnimationController","ResolveVariant",Value(1)),"deploy");
                float pull=Seconds(1,"pullpin");
                var frames=Drive(f,false,Press(tap,.05),4,each:(i,t)=>{if(t>=drawn)f.Inv.ActiveSlotIndex=1;});
                var thrown=frames.FirstOrDefault(x=>x.Flying>0);Require(thrown!=null&&Made(f.Grenades)-1==1&&f.Inv.Counts[1]==1,"not exactly one flashbang thrown: "+Log(frames));
                double ready=Math.Min(drawn+frame60+draw,tap+pull);
                Require(thrown.T>=ready-2*frame60&&thrown.T<=ready+ReleaseTime(1,false)+3*frame60,$"taken out at {drawn:0.000} (draw {draw:0.000} s), tapped at {tap:0.000}: thrown at {thrown.T:0.000}, expected about {ready:0.000}: "+Log(frames));
                var started=frames.First(x=>x.Active);Require(started.T<=tap+frame60+1e-6,"the pull did not begin with the press: "+Log(frames));
                return $"taken out {drawn:0.000}, tapped {tap:0.000}: the pull begins at once, the grenade leaves at {thrown.T:0.000} (the draw would have ended {drawn+draw:0.000}; a full pull {tap+pull:0.000})";
            });
            // ---- what is seen
            Test("phase-and-third-person-arm-of-a-quick-throw",()=>{
                var motion=T("ScThirdPersonMotion").GetMethod("Throw",Any);object stance=T("ScThirdPersonStance").GetField("Grenade",Any)?.GetValue(null)??T("ScThirdPersonStance").GetProperty("Grenade",Any).GetValue(null);
                float cock=(float)T("ScThirdPersonMotion").GetField("HighCock").GetRawConstantValue();
                (float Highest,float Jump,float Pull) Arm(double hold){
                    var f=fixture(0,2,false,FirstPerson());float highest=0,jump=0,pull=-1;Vector2? before=null;
                    Drive(f,false,Press(At,hold),hold+3,each:(i,t)=>{
                        object phase=Invoke(f.Grenades,"ThrowPhase",f.Player);if(!(bool)P(phase,"Active")){before=null;return;}
                        object pose=motion.Invoke(null,[phase,stance]);var right=(Vector2)P(pose,"Right");
                        highest=Math.Max(highest,right.X);if(before is Vector2 b)jump=Math.Max(jump,Vector2.Distance(b,right));before=right;
                        if((int)P(phase,"Stage")==2)pull=(float)P(phase,"Pull");
                    });
                    return(highest,jump,pull);
                }
                var quick=Arm(.05);var full=Arm(2);
                Require(quick.Pull is > 0 and < .2f&&full.Pull==1,$"pull reported in the throw: quick {quick.Pull}, held {full.Pull}");
                Require(full.Highest>=cock-.05f,$"a held throw raises the arm to {full.Highest:0.00} (cocked {cock})");
                Require(quick.Highest<cock-.8f,$"a quick throw raised the arm to {quick.Highest:0.00} (the cocked pose is {cock}): the arm must not be raised first");
                Require(quick.Jump<=full.Jump+1e-3f,$"the arm of a quick throw moves {quick.Jump:0.00} rad in one frame, more than a held throw's {full.Jump:0.00}");
                return $"right arm raise: held throw up to {full.Highest:0.00} rad (cocked {cock}), quick throw at most {quick.Highest:0.00}; largest step in a frame {quick.Jump:0.00} rad (held throw {full.Jump:0.00}); pull where the quick throw began {quick.Pull:0.00}";
            });
        }finally{
            BlocksManager.BlockTypeToIndex.Clear();foreach(var pair in savedTypes)BlocksManager.BlockTypeToIndex[pair.Key]=pair.Value;BlocksManager.BlockNameToIndex.Clear();foreach(var pair in savedNames)BlocksManager.BlockNameToIndex[pair.Key]=pair.Value;
            clock.GetField("Virtual").SetValue(null,oldVirtual);clock.GetField("VirtualNow").SetValue(null,oldTime);SettingsManager.SoundsVolume=oldVolume;preview.SetValue(null,oldPreview);
            window.SetValue(null,oldWindow);ScreensManager.CurrentScreen=oldScreen;ScreensManager.m_animationData=oldAnimation;ScreensManager.RootWidget=oldRoot;frameIndex.SetValue(null,oldFrame);
            SettingsManager.GamepadMappingSettings=oldPads;
        }
        return results;
    }
}
