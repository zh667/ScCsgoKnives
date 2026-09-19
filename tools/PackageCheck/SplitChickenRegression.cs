using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Engine.Graphics;
using Engine.Input;
using Engine.Media;
using Engine.Animation;
using Game;
using TemplatesDatabase;

static class SplitChickenRegression {
    internal record Result(string Name,bool Ok,string Detail);
    static T Blank<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    const BindingFlags Private=BindingFlags.Static|BindingFlags.NonPublic;
    internal static List<Result> Run(Assembly mod,string package) {
        List<Result> results=[];
        void Test(string name,Action body){try{body();results.Add(new("split-chicken/"+name,true,""));}catch(Exception e){results.Add(new("split-chicken/"+name,false,e.ToString()));}}
        void Assert(bool value,string reason){if(!value)throw new Exception(reason);}
        object Call(string type,string method,params object[] args)=>mod.GetType("Game."+type,true).GetMethod(method).Invoke(null,args);
        ComponentPlayer Player(WidgetInputDevice device){var p=Blank<ComponentPlayer>();p.PlayerData=Blank<PlayerData>();var w=Blank<GameWidget>();w.PlayerData=p.PlayerData;p.PlayerData.ComponentPlayer=p;p.PlayerData.m_gameWidget=w;w.WidgetsHierarchyInput=new WidgetInput(device);return p;}
        var viewport=Display.Viewport;
        try {
            // Physical-pixel fixtures reflect native camera caches, avoiding a GPU/ViewWidget constructor.
            foreach(var screen in new[]{new Point2(1920,1080),new Point2(1335,751)})
            foreach(var grid in new[]{new Point2(1,1),new Point2(2,1),new Point2(1,2),new Point2(2,2)})
            for(int y=0;y<grid.Y;y++)for(int x=0;x<grid.X;x++) {
                int px=x,py=y;
                Test($"projection/{screen}/{grid}/{x}/{y}",()=>{
                    Display.Viewport=new Viewport(0,0,screen.X,screen.Y);
                    var size=new Vector2(screen.X/(float)grid.X,screen.Y/(float)grid.Y);
                    var origin=new Vector2(px*size.X,py*size.Y);
                    var transform=Matrix.CreateTranslation(origin.X,origin.Y,0);
                    var camera=Blank<FppCamera>();camera.m_viewportSize=size;camera.m_viewportMatrix=transform;
                    // Deliberately contaminated native matrix: custom aspect must come from viewport size.
                    camera.m_projectionMatrix=Matrix.CreateScale(37,13,1);
                    var projection=(Matrix)Call("Cs2Placement","Projection",camera);
                    var fov=(float)Call("Cs2Placement","FovYDegrees",(float)mod.GetType("Game.KnifeTuning").GetField("Cs2ViewmodelFov").GetValue(null));
                    var local=Matrix.CreatePerspectiveFieldOfView(MathUtils.DegToRad(fov),size.X/size.Y,.02f,64);
                    foreach(var v in new[]{new Vector3(0,0,-1),new Vector3(.2f,-.15f,-1),new Vector3(-.1f,.1f,-2)}) {
                        var q=Vector4.Transform(new Vector4(v,1),projection);var l=Vector4.Transform(new Vector4(v,1),local);
                        var actual=new Vector2((q.X/q.W+1)*screen.X/2,(1-q.Y/q.W)*screen.Y/2);
                        var expected=origin+new Vector2((l.X/l.W+1)*size.X/2,(1-l.Y/l.W)*size.Y/2);
                        Assert(Vector2.Distance(actual,expected)<.002f,"viewmodel differs from native pane mapping");
                    }
                    var clip=(Rectangle)Call("ScCameraViewport","Clip",size,transform,new Rectangle(0,0,screen.X,screen.Y));
                    Assert(clip.Left==(int)MathF.Floor(origin.X)&&clip.Top==(int)MathF.Floor(origin.Y)&&clip.Right==(int)MathF.Ceiling(origin.X+size.X)&&clip.Bottom==(int)MathF.Ceiling(origin.Y+size.Y),"pane clip spills/misses");
                    Assert((float)Call("ScCameraViewport","ScopeDiameter",size)==Math.Min(size.X,size.Y),"scope clipped in portrait split");
                    var overlay=(Matrix)Call("ScCameraViewport","Overlay",camera);
                    var center=Vector3.Transform(new Vector3(size.X/2,size.Y/2,0),overlay);
                    Assert(Math.Abs(center.X-((origin.X+size.X/2)/screen.X*2-1))<.0001f&&Math.Abs(center.Y-(1-(origin.Y+size.Y/2)/screen.Y*2))<.0001f,"scope belongs to other pane");
                });
            }
            foreach(var size in new[]{new Vector2(640,720),new Vector2(960,270)})Test("reduced-render-target/"+size,()=>{
                Display.Viewport=new Viewport(0,0,(int)size.X,(int)size.Y);
                var camera=Blank<FppCamera>();camera.m_viewportSize=size;camera.m_viewportMatrix=Matrix.Identity;
                var p=(Matrix)Call("Cs2Placement","Projection",camera);var c=Vector4.Transform(new Vector4(0,0,-1,1),p);
                Assert(Math.Abs(c.X)+Math.Abs(c.Y)<.00001f&&Math.Abs(p.M22/p.M11-size.X/size.Y)<.00001f,"scaled target mapped twice");
            });
        }finally{Display.Viewport=viewport;}
        var renderer=mod.GetType("Game.CsmcFirstPersonRenderer");
        Test("per-player-scope-and-muzzle",()=>{
            var a=Player(WidgetInputDevice.GamePad1);var b=Player(WidgetInputDevice.GamePad2);
            Call("CsmcFirstPersonRenderer","ClearScopes");
            Call("CsmcFirstPersonRenderer","SetPlayerScope",a,true,8f,true);
            Assert((bool)Call("CsmcFirstPersonRenderer","ScopeActiveFor",a)&&!(bool)Call("CsmcFirstPersonRenderer","ScopeActiveFor",b),"A scope leaked to B");
            Call("CsmcFirstPersonRenderer","SetPlayerScope",b,true,2f,false);
            Call("CsmcFirstPersonRenderer","SetPlayerScope",a,false,1f,true);
            Assert(!(bool)Call("CsmcFirstPersonRenderer","ScopeActiveFor",a)&&(bool)Call("CsmcFirstPersonRenderer","ScopeActiveFor",b),"A unscoping cancelled B");
            var camera=Blank<FppCamera>();camera.GameWidget=b.GameWidget;
            Assert(!(bool)Call("CsmcFirstPersonRenderer","ScopeOverlayFor",camera),"undrawn/stale scope shows");
            renderer.GetMethod("SelectScope",Private).Invoke(null,[camera]);
            var state=renderer.GetField("s_drawingScope",Private).GetValue(null);state.GetType().GetField("Frame").SetValue(state,Time.FrameIndex);
            Assert((bool)Call("CsmcFirstPersonRenderer","ScopeOverlayFor",camera),"current scope invisible");
            camera.GameWidget=a.GameWidget;Assert(!(bool)Call("CsmcFirstPersonRenderer","ScopeOverlayFor",camera),"B mask leaked to A");
            Call("CsmcFirstPersonRenderer","MuzzleFlash",a,.05f,"A",null,false);
            Call("CsmcFirstPersonRenderer","MuzzleFlash",b,.1f,"B",null,false);
            renderer.GetMethod("SelectEffects",Private).Invoke(null,[a]);var effect=renderer.GetField("s_effect",Private).GetValue(null);
            Assert((string)effect.GetType().GetField("Bone").GetValue(effect)=="A","B shot overwrites A muzzle");
            renderer.GetMethod("SelectEffects",Private).Invoke(null,[b]);effect=renderer.GetField("s_effect",Private).GetValue(null);
            Assert((string)effect.GetType().GetField("Bone").GetValue(effect)=="B","A draw overwrites B muzzle");
            Call("CsmcFirstPersonRenderer","ClearScopes");Assert(!(bool)Call("CsmcFirstPersonRenderer","ScopeActiveFor",b),"world dispose retains scope");
        });
        Test("native-controller-defaults-chords-isolation",()=>{
            var binding=mod.GetType("Game.ScGamepadBindings");var keys=(Dictionary<string,string>)binding.GetField("Keys").GetValue(null);var savedKeys=keys.ToArray();
            var mapping=SettingsManager.GamepadMappingSettings;float threshold=SettingsManager.GamepadTriggerThreshold;
            var states=(Array)typeof(GamePad).GetField("m_states",Private).GetValue(null);
            var snapshots=states.Cast<object>().Select(s=>s.GetType().GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).Select(f=>(f,value:f.GetValue(s) is Array a?a.Clone():f.GetValue(s))).ToArray()).ToArray();
            var frame=typeof(Time).GetProperty("FrameIndex");var oldFrame=frame.GetValue(null);
            void Next()=>frame.SetValue(null,Time.FrameIndex+1);
            void Button(int pad,GamePadButton key,bool down)=>((bool[])states.GetValue(pad).GetType().GetField("Buttons").GetValue(states.GetValue(pad)))[(int)key]=down;
            void Last(int pad,GamePadButton key,bool down)=>((bool[])states.GetValue(pad).GetType().GetField("LastButtons").GetValue(states.GetValue(pad)))[(int)key]=down;
            bool Down(ComponentPlayer p,string id,bool once=true)=>(bool)Call("ScGamepadBindings","Down",p,id,once);
            try {
                keys.Clear();SettingsManager.InitializeGamepadMappingSettings();SettingsManager.GamepadTriggerThreshold=.5f;
                foreach(var s in states){s.GetType().GetField("IsConnected").SetValue(s,true);foreach(var n in new[]{"Buttons","LastButtons","Triggers","LastTriggers"})Array.Clear((Array)s.GetType().GetField(n).GetValue(s));s.GetType().GetField("ModifierKeyOfCurrentCombo").SetValue(s,null);}
                foreach(var pair in new[]{("fire","TriggerRight"),("scope","TriggerLeft"),("reload","LeftShoulder+X"),("inspect","LeftShoulder+Y"),("knife_heavy","RightThumb"),("c4_timer","LeftShoulder+X"),("plant_c4","TriggerRight")})Assert((string)Call("ScGamepadBindings","Get",pair.Item1)==pair.Item2,"wrong default "+pair.Item1);
                var a=Player(WidgetInputDevice.GamePad1);var b=Player(WidgetInputDevice.GamePad2);var both=Player(WidgetInputDevice.GamePad1|WidgetInputDevice.GamePad2);
                Button(0,GamePadButton.LeftShoulder,true);Button(1,GamePadButton.X,true);Next();Assert(!Down(both,"reload"),"chord combined buttons from different controllers");
                Button(1,GamePadButton.X,false);Button(0,GamePadButton.X,true);Next();
                Assert(Down(a,"reload")&&!Down(b,"reload")&&!a.GameWidget.Input.IsGamepadDownOnce("ToggleInventory"),"reload opens backpack or controls other player");
                Next();Assert(Down(a,"reload",false)&&!Down(a,"reload"),"held reload repeats");
                Last(0,GamePadButton.LeftShoulder,true);Button(0,GamePadButton.LeftShoulder,false);Button(0,GamePadButton.X,false);Next();Assert(!Down(a,"reload")&&!a.GameWidget.Input.IsGamepadDownOnce("EditItem"),"LB release edits block");
                Button(0,GamePadButton.X,true);Next();Assert(a.GameWidget.Input.IsGamepadDownOnce("ToggleInventory")&&!Down(a,"reload"),"single X no longer opens backpack");
                Button(0,GamePadButton.X,false);Button(0,GamePadButton.LeftShoulder,true);Button(0,GamePadButton.Y,true);Next();Assert(Down(a,"inspect")&&!a.GameWidget.Input.IsGamepadDownOnce("ToggleClothing"),"inspect opens clothing");
                Button(0,GamePadButton.Y,false);Button(0,GamePadButton.LeftShoulder,false);Button(0,GamePadButton.RightThumb,true);Next();Assert(Down(a,"knife_heavy")&&!Down(b,"knife_heavy"),"melee player isolation");
                a.GameWidget.Input.Clear();Next();Assert(!Down(a,"knife_heavy"),"cleared input still acts");
                Button(0,GamePadButton.LeftShoulder,true);Button(0,GamePadButton.X,true);var c=Player(WidgetInputDevice.GamePad1);
                SettingsManager.GamepadMappingSettings.SetValue("EditItem",new ValuesDictionary{{"ModifierKey",GamePadButton.LeftShoulder},{"ActionKey",GamePadButton.X}});Next();Assert(!Down(c,"reload"),"new vanilla chord conflict ignored");
                keys["reload"]="";Assert((string)Call("ScGamepadBindings","Get","reload")=="","explicit disabled binding replaced");keys["reload"]="A";Assert((string)Call("ScGamepadBindings","Get","reload")=="A","custom binding replaced");
                Assert((bool)Call("ScGamepadBindings","NativeAimBinding","scope"),"native LT should handle scope once");
            }finally{
                keys.Clear();foreach(var p in savedKeys)keys[p.Key]=p.Value;SettingsManager.GamepadMappingSettings=mapping;SettingsManager.GamepadTriggerThreshold=threshold;frame.SetValue(null,oldFrame);
                for(int i=0;i<states.Length;i++)foreach(var p in snapshots[i]){if(p.value is Array a)a.CopyTo((Array)p.f.GetValue(states.GetValue(i)),0);else p.f.SetValue(states.GetValue(i),p.value);}
            }
        });
        Test("native-chicken-animation-loops-in-place",()=>{
            using var zip=ZipFile.OpenRead(package);using var stream=zip.GetEntry("Assets/Models/ScCsgoKnives/chicken.glb").Open();using var bytes=new MemoryStream();stream.CopyTo(bytes);bytes.Position=0;var data=GltfLoader.Load(bytes);
            using var model=new Model{ModelData=data,Skin=data.Skin,Animations=data.Animations};
            foreach(var b in data.Bones)model.m_bones.Add(new ModelBone{Model=model,Index=model.m_bones.Count,Name=b.Name,Transform=b.Transform});
            int root=model.m_bones.FindIndex(b=>b.Name=="root_motion");Assert(root>=0,"missing source skeleton");
            foreach(var clip in data.Animations){
                Assert(!clip.Channels.Any(c=>c.TargetBoneName=="root_motion"&&c.Property==ModelAnimation.AnimationProperty.Translation),"CS2 travel still active");
                Assert(clip.Channels.Any(c=>c.TargetBoneName=="root"&&c.Property==ModelAnimation.AnimationProperty.Translation),"natural body motion removed");
                var player=new AnimationPlayer();player.SetAnimation(model,clip);player.Play(true);bool moves=false;Matrix?[] previous=null;
                for(int i=0;i<721;i++){
                    player.Update(clip.Duration/60);var pose=new Matrix?[model.m_bones.Count];player.SampleAtTime(player.Time,pose);
                    Assert((pose[root]??model.m_bones[root].Transform).Translation.Length()<.00001f,"body drifts from collision/shadow at loop "+i);
                    if(previous is not null)moves|=pose.Where((p,j)=>j!=root&&p.HasValue&&previous[j].HasValue&&p.Value!=previous[j].Value).Any();previous=pose;
                }
                Assert(moves,"walking/running limbs frozen "+clip.Name);
            }
        });
        return results;
    }
}
