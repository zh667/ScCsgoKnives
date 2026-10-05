using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Engine.Graphics;
using Game;
using GameEntitySystem;

// video-feedback-20260929 R2: what a throw looks like against what it does, through the real SetThrowButton / Update
// path of the packaged DLL, frame by frame. The item leaves the hand on the frame the transaction commits (the last
// one of a stack and a creative stack included), a third-person camera throws from the thrower and its preview starts
// where the real grenade starts, and first person launches exactly as before.
static class ThrowPresentationRegression {
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
        void Test(string name,Func<string> test){try{results.Add(new("throw-presentation/"+name,true,test()));}catch(Exception e){results.Add(new("throw-presentation/"+name,false,(e is TargetInvocationException{InnerException:{} inner}?inner:e).ToString()));}}
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
            // Drives frames at 60 a second: the on-screen throw button is held from 0.2 s until `until`, optionally cancelled at `cancelAt`.
            List<Frame> Drive((object Grenades,ComponentPlayer Player,ComponentFirstPersonModel Model,ComponentHumanModel Human,Inventory Inv,SubsystemTime Time,OpenTerrain Terrain,View Camera) f,bool low,double until,double seconds,double cancelAt=-1,Action<int,double> each=null,double fps=60){
                var frames=new List<Frame>();
                for(int i=0;i<=(int)Math.Round(seconds*fps);i++){
                    double t=i/fps;frameIndex.SetValue(null,200000+i);f.Time.m_gameTime=1000+t;f.Time.m_gameTimeDelta=(float)(1/fps);clock.GetField("VirtualNow").SetValue(null,1000+t);
                    bool pressed=t>=.2&&t<until&&(cancelAt<0||t<cancelAt),cancelled=cancelAt>=0&&t>=cancelAt&&t<cancelAt+1/fps;
                    Invoke(f.Grenades,"SetThrowButton",f.Player,low,pressed,false,cancelled,2);
                    ((IUpdateable)f.Grenades).Update((float)(1/fps));
                    Call("KnifeAnimationController","Update",f.Model,f.Player.ComponentMiner.ActiveBlockValue);
                    each?.Invoke(i,t);
                    var shown=Shown(f);
                    frames.Add(new(i,t,pressed,f.Inv.Counts[0],shown.Value,shown.Active,shown.Released,shown.Stage,Flying(f.Grenades).Count));
                }
                return frames;
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

            for(int k=0;k<assets.Length;k++){
                int kind=k;
                Test("last-one-leaves-an-empty-hand/"+assets[kind],()=>{
                    var f=fixture(kind,1,false,FirstPerson());var frames=Drive(f,false,1.9,4.5);
                    var (release,end)=Contract(frames,Value(kind),1,0,false);
                    return $"released frame {release.Index} t={release.T:0.000}, action ended frame {end.Index} t={end.T:0.000}; "+Log(frames);
                });
            }
            foreach(var (kind,low) in new[]{(0,false),(0,true),(3,false),(3,true),(2,false)}){
                Test($"stack-of-three-hides-the-next-one-until-the-action-ends/{assets[kind]}/{(low?"weak":"strong")}",()=>{
                    var f=fixture(kind,3,false,FirstPerson());var frames=Drive(f,low,1.9,4.5);
                    var (release,end)=Contract(frames,Value(kind),3,2,true);
                    return $"released frame {release.Index}, next one shown from frame {end.Index}; "+Log(frames);
                });
                Test($"creative-stack-hides-the-thrown-one-until-the-action-ends/{assets[kind]}/{(low?"weak":"strong")}",()=>{
                    var f=fixture(kind,4,true,FirstPerson());var frames=Drive(f,low,1.9,4.5);
                    var (release,end)=Contract(frames,Value(kind),4,4,true);
                    return $"released frame {release.Index}, stack still 4, shown again from frame {end.Index}; "+Log(frames);
                });
            }
            // r2-c4-completion-20260929: the posture never touches the throw's timeline. Crouching, standing up, jumping and
            // landing mid-throw change the drawn clip (CT/T crouched throws, the legs), never the release or its count.
            foreach(var (kind,low) in new[]{(0,false),(3,false),(2,true)})
            foreach(string posture in new[]{"crouch-then-stand","jump-and-land","crouch-and-jump"})
            Test($"posture-changes-mid-throw-never-move-the-release/{assets[kind]}/{(low?"weak":"strong")}/{posture}",()=>{
                var still=Drive(fixture(kind,2,false,FirstPerson()),low,1.9,4.5);
                var f=fixture(kind,2,false,FirstPerson());var body=f.Player.ComponentBody;
                var moving=Drive(f,low,1.9,4.5,each:(i,t)=>{
                    bool crouched=posture!="jump-and-land"&&t is > .5 and < 1.95;bool air=posture!="crouch-then-stand"&&t is > 1.7 and < 2.3;
                    body.TargetCrouchFactor=crouched?1:0;body.m_crouchFactor=crouched?1:0;body.StandingOnValue=air?null:2;body.Velocity=air?new Vector3(0,t<2?4:-4,0):Vector3.Zero;
                });
                var (r1,e1)=Contract(still,Value(kind),2,1,true);var (r2,e2)=Contract(moving,Value(kind),2,1,true);
                Require(r1.Index==r2.Index&&e1.Index==e2.Index,$"the posture moved the release ({r1.Index} -> {r2.Index}) or the end ({e1.Index} -> {e2.Index}): "+Log(moving));
                return $"release frame {r2.Index}, end {e2.Index} with {posture}";
            });
            Test("switching-item-before-the-release-cancels-after-it-keeps-the-throw",()=>{
                // Before the release: nothing is thrown or spent. After it: the grenade is out and the hand is simply empty.
                var f=fixture(0,2,false,FirstPerson());var before=Drive(f,false,3,3,each:(i,t)=>{if(t>=.9)f.Inv.ActiveSlotIndex=1;});
                Require(before.All(x=>x.Flying==0)&&f.Inv.Counts[0]==2,"switching away before the release threw or spent a grenade: "+Log(before));
                var g=fixture(0,2,false,FirstPerson());double released=-1;
                var after=Drive(g,false,1.9,4,each:(i,t)=>{if(released<0&&Flying(g.Grenades).Count>0)released=t;if(released>=0&&t>=released+.1)g.Inv.ActiveSlotIndex=1;});
                Require(released>0&&after.Last().Flying==1&&g.Inv.Counts[0]==1,"switching away after the release lost or duplicated the thrown grenade: "+Log(after));
                Require(after.Last(x=>x.T<released+.1).Released&&!after.Last().Active,"the throw was not ended by the switch after the release: "+Log(after));
                return $"before: {Log(before)} || after: released at {released:0.000}";
            });
            // quick-throw-20261002 (was quick-tap-throws-once-after-the-pull, which asked for the wait a tap no longer has).
            Test("quick-tap-throws-once-at-once",()=>{
                var f=fixture(0,2,false,FirstPerson());var frames=Drive(f,false,.25,4.5);
                var (release,end)=Contract(frames,Value(0),2,1,true);
                var prepared=frames.First(x=>x.Active);
                Require(release.T-.25<=.1+1e-6,$"a tap let go at 0.250 released at {release.T:0.000}: "+Log(frames));
                return $"tap at {prepared.T:0.000}, button up 0.250, released {release.T:0.000}; "+Log(frames);
            });
            Test("cancel-before-the-release-keeps-the-item-in-the-hand",()=>{
                var f=fixture(0,1,false,FirstPerson());var frames=Drive(f,false,3,3,cancelAt:.8);
                Require(frames.All(x=>x.Flying==0&&x.Count==1&&x.Presented==Value(0)&&!x.Released),"a cancelled throw consumed, threw or hid the item: "+Log(frames));
                Require(frames.Any(x=>x.Active)&&!frames.Last().Active,"the throw was not prepared and then cancelled: "+Log(frames));
                return Log(frames);
            });
            Test("first-person-launch-unchanged",()=>{
                foreach(bool low in new[]{false,true}){
                    // The camera at the eye, and one a hand's width off it (view bobbing): both are first person.
                    foreach(var offset in new[]{Vector3.Zero,new Vector3(.1f,.05f,-.2f)}){
                        var camera=FirstPerson();camera.At+=offset;var f=fixture(0,1,false,camera);object state=null;Vector3 start=default,velocity=default;
                        var frames=Drive(f,low,1.9,3,each:(i,t)=>{if(state==null&&Flying(f.Grenades).Count>0){state=Flying(f.Grenades)[0];start=Read(state,"Position");velocity=Read(state,"Velocity");}});
                        Require(state!=null,"nothing was thrown: "+Log(frames));
                        // The entry point every earlier version used, with the same view.
                        var expected=((Vector3 Position,Vector3 Velocity))Call("ScGrenadeBallistics","Launch",camera.At,camera.Look,Vector3.Zero,low,(Func<Vector3,Vector3,Vector3?>)((a,b)=>null));
                        var rays=f.Terrain.Rays.Where(Aim).ToList();
                        Require(rays.Count>0&&rays.All(r=>Vector3.Distance(r.From,camera.At)<1e-5f),"the aim ray does not start at the first-person camera");
                        Require(start==expected.Position&&velocity==expected.Velocity&&start==camera.At,$"first-person launch changed: {start} {velocity}, expected {expected.Position} {expected.Velocity}");
                    }
                }
                return "strong and weak: position and velocity equal ScGrenadeBallistics.Launch from the camera";
            });
            Test("third-person-leaves-the-thrower-and-the-preview-starts-there",()=>{
                var detail=new List<string>();
                foreach(var (back,up,low) in new[]{(4f,1.5f,false),(4f,1.5f,true),(8f,3f,false),(2f,.4f,false)}){
                    Vector3 look=Vector3.Normalize(new Vector3(1,.2f,0));
                    var camera=new View{At=eye-new Vector3(back,0,0)+Vector3.UnitY*up,Look=look};var f=fixture(0,1,false,camera);
                    object state=null;Vector3 start=default,velocity=default;object path=null;
                    // 50 frames a second: the live flight then takes the preview's own 0.02 s steps and the two can be compared exactly.
                    var frames=Drive(f,low,1.9,6,fps:50,each:(i,t)=>{
                        if(Preparing(f.Grenades,f.Player) is {} prep&&F(prep,"Preview") is {} shown)path=shown; // the last preview drawn before the release
                        if(state==null&&Flying(f.Grenades).Count>0){state=Flying(f.Grenades)[0];start=Read(state,"Position");velocity=Read(state,"Velocity");}
                    });
                    Require(state!=null,"nothing was thrown: "+Log(frames));Require(path!=null,"no preview was computed while preparing");
                    Require(Vector3.Distance(start,eye)<1e-4f,$"camera {back} m behind, {up} m above: the grenade left from {start}, the thrower's eye is {eye}, the camera {camera.At}");
                    var points=(List<Vector3>)F(path,"Points");
                    Require(Vector3.Distance(points[0],start)<1e-4f,$"the preview starts at {points[0]}, the real grenade at {start}");
                    // The same launch state gives the same flight: the live grenade against the preview's end.
                    var predicted=(Vector3)F(path,"EndPoint");var effect=state.GetType().GetField("Effect");
                    // HE: its fuse ends in the air over the open fixture; the state keeps the position where it went off.
                    Vector3 live=Read(state,"Position");bool burst=(bool)effect.GetValue(state)||Flying(f.Grenades).Count==0;
                    Require(burst,"the live grenade never went off within the run");
                    Require(Vector3.Distance(live,predicted)<.02f,$"the preview ends at {predicted}, the real grenade went off at {live}");
                    // Aim: along the camera's own ray, from beside the thrower onward.
                    var aims=f.Terrain.Rays.Where(Aim).ToList();
                    Require(aims.Count>0,"no aim ray was cast");
                    foreach(var ray in aims){
                        Require(Vector3.Cross(ray.From-camera.At,look).Length()<1e-3f,$"the aim ray start {ray.From} is not on the camera's ray");
                        Require(Vector3.Dot(ray.From-camera.At,look)>=Vector3.Dot(eye-camera.At,look)-1e-3f,$"the aim ray starts {Vector3.Dot(ray.From-camera.At,look):0.00} m along the camera ray, before the thrower at {Vector3.Dot(eye-camera.At,look):0.00} m: something between the camera and the thrower would be aimed at");
                    }
                    detail.Add($"camera {back} m behind {up} m above {(low?"weak":"strong")}: start {start} speed {velocity.Length():0.00}, preview end {predicted}, live end {live}");
                }
                return string.Join("; ",detail);
            });
            Test("third-person-wall-between-camera-and-thrower-is-not-the-aim-point",()=>{
                // A wall one metre in front of the camera, three metres behind the thrower; open ground ahead.
                Vector3 look=Vector3.UnitX;var camera=new View{At=eye-new Vector3(4,0,0)+Vector3.UnitY*1.5f,Look=look};var f=fixture(0,1,false,camera);
                object state=null; // the fixture has no solid cells; the rule is on where the aim ray starts
                var frames=Drive(f,false,1.9,3,each:(i,t)=>{if(state==null&&Flying(f.Grenades).Count>0)state=Flying(f.Grenades)[0];});
                Require(state!=null,"nothing was thrown: "+Log(frames));
                float wallAlong=1,throwerAlong=Vector3.Dot(eye-camera.At,look);
                var aims=f.Terrain.Rays.Where(Aim).ToList();
                Require(aims.Count>0&&aims.All(r=>Vector3.Dot(r.From-camera.At,look)>wallAlong+1),$"an aim ray starts before the wall at {wallAlong} m (thrower at {throwerAlong:0.00} m)");
                // With a wall ahead of the thrower the aim point is on it, and the grenade still flies from the thrower toward it.
                var target=eye+new Vector3(6,1,2);
                var launched=((Vector3 Position,Vector3 Velocity))Call("ScGrenadeBallistics","LaunchFrom",eye,camera.At+look*throwerAlong,look,Vector3.Zero,false,(Func<Vector3,Vector3,Vector3?>)((a,b)=>target));
                var straight=(Vector3)Call("ScGrenadeBallistics","LaunchVelocity",Vector3.Normalize(target-eye),Vector3.Zero,false);
                Require(launched.Position==eye&&Vector3.Distance(launched.Velocity,straight)<1e-3f,$"toward a wall ahead the launch is {launched.Velocity}, straight at that point from the thrower it is {straight}");
                return $"aim rays start {aims.Min(r=>Vector3.Dot(r.From-camera.At,look)):0.00} m along the camera ray; thrower at {throwerAlong:0.00} m";
            });
        }finally{
            BlocksManager.BlockTypeToIndex.Clear();foreach(var pair in savedTypes)BlocksManager.BlockTypeToIndex[pair.Key]=pair.Value;BlocksManager.BlockNameToIndex.Clear();foreach(var pair in savedNames)BlocksManager.BlockNameToIndex[pair.Key]=pair.Value;
            clock.GetField("Virtual").SetValue(null,oldVirtual);clock.GetField("VirtualNow").SetValue(null,oldTime);SettingsManager.SoundsVolume=oldVolume;preview.SetValue(null,oldPreview);
            window.SetValue(null,oldWindow);ScreensManager.CurrentScreen=oldScreen;ScreensManager.m_animationData=oldAnimation;ScreensManager.RootWidget=oldRoot;frameIndex.SetValue(null,oldFrame);
        }
        return results;
    }
}
