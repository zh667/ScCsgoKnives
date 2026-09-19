using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Engine;
using Engine.Media;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

static class C4Regression {
    sealed class FloorTerrain : SubsystemTerrain {
        public override TerrainRaycastResult? Raycast(Vector3 start, Vector3 end, bool interaction, bool air, Func<int,float,bool> action) =>
            new TerrainRaycastResult { Ray=new Ray3(start,-Vector3.UnitY),Distance=.25f,Value=1,CellFace=new CellFace(0,0,0,4) };
    }
    sealed class AudioProbe : SubsystemAudio {
        public readonly List<string> Sounds=[];
        public readonly List<float> BeepPitches=[];
        public override void PlaySound(string name,float volume,float pitch,Vector3 position,float distance,bool delay){Sounds.Add(name);if(name.Contains("c4_beep"))BeepPitches.Add(pitch);}
    }
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod, string package) {
        List<Result> results = [];
        void Test(string n, Func<bool> f) { try { results.Add(new("c4/" + n, f(), n)); } catch(Exception e) { results.Add(new("c4/" + n, false, e.ToString())); } }
        object Call(string t, string m, params object[] args) => mod.GetType("Game." + t).GetMethod(m).Invoke(null, args);
        var charge = mod.GetType("Game.ScC4Charge");
        var rig = mod.GetType("Game.CsmcKnifeRig");
        Test("native-creative-inventory-load-and-weapons-filter",()=>{
            var oldBlocks=(Block[])BlocksManager.m_blocks.Clone();var types=new Dictionary<Type,int>(BlocksManager.BlockTypeToIndex);var names=new Dictionary<string,int>(BlocksManager.BlockNameToIndex);
            try {
                Array.Fill(BlocksManager.m_blocks,new AirBlock());
                int index=700;
                foreach(var t in mod.GetTypes().Where(t=>t.IsSubclassOf(typeof(Block))&&!t.IsAbstract)) {
                    var b=(Block)Activator.CreateInstance(t);b.BlockIndex=index;BlocksManager.m_blocks[index]=b;BlocksManager.BlockTypeToIndex[t]=index;BlocksManager.BlockNameToIndex[t.Name]=index++;
                }
                using var zip=System.IO.Compression.ZipFile.OpenRead(package);using var reader=new StreamReader(zip.GetEntry("Assets/ScCsgoKnivesBlocksData.csv").Open());
                BlocksManager.LoadBlocksData(reader.ReadToEnd());
                var d=new ValuesDictionary();d.SetValue("ActiveSlotIndex",0);d.SetValue("OpenSlotsCount",10);d.SetValue("CategoryIndex",0);d.SetValue("PageIndex",0);
                var inv=new ComponentCreativeInventory();inv.Load(d,null);
                var c4=(Block)BlocksManager.Blocks[BlocksManager.BlockNameToIndex["ScC4Block"]];
                var weapons=Enumerable.Range(inv.OpenSlotsCount,inv.SlotsCount-inv.OpenSlotsCount).Select(inv.GetSlotValue).Where(v=>BlocksManager.Blocks[Terrain.ExtractContents(v)].GetCategory(v)=="CS武器").ToArray();
                int[] matches=weapons.Select((v,i)=>(v,i)).Where(x=>Terrain.ExtractContents(x.v)==c4.BlockIndex).Select(x=>x.i).ToArray();
                results.Add(new("c4/creative-position",matches.Length==1,$"C4 index among CS武器={string.Join(',',matches)}, weapon entries={weapons.Length}, display name={c4.GetDisplayName(null,c4.GetCreativeValues().Single())}"));
                return matches.Length==1&&inv.GetSlotCount(inv.m_slots.IndexOf(weapons[matches[0]]))>0;
            } finally { Array.Copy(oldBlocks,BlocksManager.m_blocks,oldBlocks.Length);BlocksManager.BlockTypeToIndex.Clear();foreach(var p in types)BlocksManager.BlockTypeToIndex[p.Key]=p.Value;BlocksManager.BlockNameToIndex.Clear();foreach(var p in names)BlocksManager.BlockNameToIndex[p.Key]=p.Value; }
        });
        Test("append-only-equipment-no-gun-levels", () => (int)rig.GetProperty("C4Index").GetValue(null) == 63
            && (int)rig.GetProperty("AssetCount").GetValue(null) == 64
            && ((Array)mod.GetType("Game.GunSpec").GetField("All").GetValue(null)).Length == 35
            && !charge.GetFields().Any(f => f.Name.Contains("Level") || f.Name.Contains("GunId")));
        Test("20-seconds-once-with-two-native-xml-roundtrips", () => {
            object c = Activator.CreateInstance(charge);
            charge.GetField("Position").SetValue(c,new Vector3(10,20,-30)); charge.GetField("Owner").SetValue(c,7);
            for(int i=0;i<2;i++) {
                if((bool)charge.GetMethod("Tick").Invoke(c,[3.25f])) return false;
                var xml=new XElement("Values"); ((ValuesDictionary)charge.GetMethod("Save").Invoke(c,null)).Save(xml);
                var d=new ValuesDictionary();d.ApplyOverrides(XElement.Parse(xml.ToString()));c=charge.GetMethod("Load").Invoke(null,[d]);
            }
            if((float)charge.GetField("Remaining").GetValue(c)!=13.5f || (int)charge.GetField("Owner").GetValue(c)!=7) return false;
            return !(bool)charge.GetMethod("Tick").Invoke(c,[13.49f]) && (bool)charge.GetMethod("Tick").Invoke(c,[.02f])
                && !(bool)charge.GetMethod("Tick").Invoke(c,[20f]);
        });
        foreach(float remaining in new[]{-1,21,float.NaN,float.PositiveInfinity}) Test("reject-corrupt-fuse/"+remaining,()=>{
            var c=Activator.CreateInstance(charge); var d=(ValuesDictionary)charge.GetMethod("Save").Invoke(c,null);d.SetValue("Remaining",remaining);
            try { charge.GetMethod("Load").Invoke(null,[d]);return false; } catch(TargetInvocationException e) { return e.InnerException is InvalidOperationException; }
        });
        Test("5000-centre-1250-half-radius-zero-at-32",()=> (float)Call("ScC4Charge","DamageAt",0f,5000f,32f)==5000
            && (float)Call("ScC4Charge","DamageAt",16f,5000f,32f)==1250 && (float)Call("ScC4Charge","DamageAt",32f,5000f,32f)==0
            && (float)Call("ScC4Charge","DamageAt",float.NaN,5000f,32f)==0);
        Test("actual-cs2-plant-four-seconds-and-event-timings",()=>{
            var events=((IEnumerable)Call("Cs2Rig","Events","c4","plant")).Cast<object>().ToArray();
            return (float)Call("Cs2Rig","Duration","c4","plant")==4f
                && events.Any(e=>(string)e.GetType().GetProperty("Name").GetValue(e)=="c4.initiate")
                && events.Count(e=>(string)e.GetType().GetProperty("Name").GetValue(e)=="c4.keypressquiet")>=6;
        });
        Test("separate-screen-mesh-and-nine-password-stages",()=>{
            var mesh=Call("Cs2SkinnedMesh","Weapon","c4");var parts=((IEnumerable)mesh.GetType().GetField("Primitives").GetValue(mesh)).Cast<object>().ToArray();
            if(parts.Length!=2 || !parts.Any(p=>(string)p.GetType().GetField("Material").GetValue(p)=="weapon_c4_digits"))return false;
            float[] times=[0,.24f,.67f,.97f,1.24f,1.47f,1.67f,1.97f,2.17f];
            return times.Select((t,i)=>(Vector2)Call("ScC4Visuals","ScreenOffset","plant_c4",t)==new Vector2(0,i/16f)).All(x=>x);
        });
        Test("32-block-pressure-wave-and-bounded-sprites",()=>{
            if((float)Call("ScC4Blast","WaveRadius",.6f,32f)!=16 || (float)Call("ScC4Blast","WaveRadius",1.2f,32f)!=32)return false;
            for(float age=0;age<3;age+=.025f) {
                var sprites=((IEnumerable)Call("ScC4Blast","Sprites",Vector3.Zero,age,0f)).Cast<object>().ToArray();
                if(sprites.Length>32)return false;
                foreach(var s in sprites)if(!float.IsFinite((float)s.GetType().GetProperty("Width").GetValue(s)))return false;
            }
            return true;
        });
        foreach(string clip in new[]{"idle","deploy","inspect","plant"}) foreach(float t in new[]{0f,.8f,2f,3.2f,4f}) Test($"native-skin-and-hands/{clip}/{t}",()=>{
            var pose=Call("Cs2Rig","Sample","c4",clip,t); var mesh=Call("Cs2SkinnedMesh","Weapon","c4");
            var placement=Call("Cs2Placement","Placement");
            if(!(bool)mesh.GetType().GetMethod("SetPose").Invoke(mesh,[pose,placement]))return false;
            var arms=mesh.GetType().GetProperty("Arms").GetValue(null);
            if(!(bool)arms.GetType().GetMethod("SetPose").Invoke(arms,[pose,placement]))return false;
            if((float)mesh.GetType().GetMethod("UnresolvedWeight").Invoke(mesh,[pose])>0 || (float)arms.GetType().GetMethod("UnresolvedWeight").Invoke(arms,[pose])>0)return false;
            arms.GetType().GetMethod("Skin").Invoke(arms,null);
            mesh.GetType().GetMethod("Skin").Invoke(mesh,null);
            var vertices=(Array)mesh.GetType().GetProperty("Skinned").GetValue(mesh);
            var positions=vertices.Cast<object>().Select(v=>(Vector3)v.GetType().GetField("Position").GetValue(v)).ToArray();
            return positions.Length>100 && positions.All(v=>float.IsFinite(v.X+v.Y+v.Z)) && positions.Max(v=>v.X)-positions.Min(v=>v.X)>.05f;
        });
        var oldTypes=new Dictionary<Type,int>(BlocksManager.BlockTypeToIndex);var oldBlock=BlocksManager.Blocks[705];
        var blockType=mod.GetType("Game.ScC4Block");
        Test("world-mesh-metres-floor-contact",()=>{
            var mesh=(BlockMesh)Call("ScC4Block","BuildWorldMesh");
            Vector3 lo=new(float.MaxValue),hi=new(float.MinValue);
            foreach(var v in mesh.Vertices){lo=Vector3.Min(lo,v.Position);hi=Vector3.Max(hi,v.Position);}
            Vector3 span=hi-lo;
            results.Add(new("c4/world-mesh-dimensions",true,$"metres={span}; triangles={mesh.Indices.Count/3}"));
            return Math.Abs(lo.Y)<.00001&&span.Y is >.08f and <.10f&&Math.Max(span.X,span.Z) is >.26f and <.28f;
        });
        try {
            BlocksManager.BlockTypeToIndex[blockType]=705;BlocksManager.Blocks[705]=(Block)Activator.CreateInstance(blockType);
            int value=(int)blockType.GetProperty("Value").GetValue(null);
            Test("local-fpp-suppresses-extra-hand-weapon-only",()=>{
                T Blank<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
                foreach(var name in new[]{"ScKnifeBlock","ScGunBlock","ScGrenadeBlock"})BlocksManager.BlockTypeToIndex[mod.GetType("Game."+name)]=710+Array.IndexOf(new[]{"ScKnifeBlock","ScGunBlock","ScGrenadeBlock"},name);
                var e=Blank<Entity>();var creature=Blank<ComponentCreature>();creature.m_entity=e;
                var human=Blank<ComponentHumanModel>();human.m_entity=e;human.m_componentMiner=Blank<ComponentMiner>();
                var inv=new ComponentInventory();inv.m_slots.Add(new());inv.AddSlotItems(0,value,1);human.m_componentMiner.Inventory=inv;
                var widget=Blank<GameWidget>();widget.Target=creature;var camera=new FppCamera(widget);widget.m_activeCamera=camera;
                if(!(bool)Call("ScThirdPerson","Draw",human,camera))return false;
                widget.Target=Blank<ComponentCreature>();widget.Target.m_entity=Blank<Entity>();
                if((bool)Call("ScThirdPerson","Draw",human,camera))return false;
                widget.Target=creature;widget.m_activeCamera=new TppCamera(widget);
                if((bool)Call("ScThirdPerson","Draw",human,widget.m_activeCamera))return false;
                widget.m_activeCamera=camera;inv.RemoveSlotItems(0,1);
                return !(bool)Call("ScThirdPerson","Draw",human,camera);
            });
            Test("native-icon-faces-viewer",()=>{
                var air=BlocksManager.Blocks[0];
                try {
                    BlocksManager.Blocks[0]=new AirBlock();
                    var widget=new BlockIconWidget{Value=value};
                    var facing=Vector3.TransformNormal(Vector3.UnitZ,widget.m_viewMatrix);
                    return Math.Abs(facing.Z)>.999f && BlocksManager.Blocks[705].GetDisplayOrder(value)<220;
                } finally { BlocksManager.Blocks[0]=air; }
            });
            Test("plant-survives-first-render-and-password-hand-motion",()=>{
                T Blank<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
                var clock=mod.GetType("Game.KnifeClock");
                var vf=clock.GetField("Virtual");var tf=clock.GetField("VirtualNow");var oldV=vf.GetValue(null);var oldT=tf.GetValue(null);
                try {
                    vf.SetValue(null,true);tf.SetValue(null,0d);
                    foreach(var name in new[]{"ScKnifeBlock","ScGunBlock","ScGrenadeBlock"})BlocksManager.BlockTypeToIndex[mod.GetType("Game."+name)]=710+Array.IndexOf(new[]{"ScKnifeBlock","ScGunBlock","ScGrenadeBlock"},name);
                    var p=Blank<ComponentPlayer>();p.ComponentMiner=Blank<ComponentMiner>();var inv=new ComponentInventory();inv.m_slots.Add(new());inv.AddSlotItems(0,value,1);p.ComponentMiner.Inventory=inv;
                    var entity=Blank<Entity>();p.m_entity=entity;var model=Blank<ComponentFirstPersonModel>();model.m_componentPlayer=p;entity.m_components=[model];
                    Call("KnifeAnimationController","C4Action",p,"plant");
                    foreach(double t in new[]{0,.24,.67,1.24,1.97,2.17,3.21,3.9}) {
                        tf.SetValue(null,t);if(t>3.2)inv.RemoveSlotItems(0,1);
                        var pose=Call("KnifeAnimationController","Update",model,value);
                        if((string)pose.GetType().GetProperty("ClipAlias").GetValue(pose)!="plant"||Math.Abs((float)pose.GetType().GetProperty("RequestedTime").GetValue(pose)-t)>.001)return false;
                    }
                    var a=Call("Cs2Rig","Sample","c4","plant",.67f);var b=Call("Cs2Rig","Sample","c4","plant",1.24f);
                    var bones=(IDictionary)a.GetType().GetField("Bones").GetValue(a);var other=(IDictionary)b.GetType().GetField("Bones").GetValue(b);
                    return bones.Keys.Cast<string>().Where(k=>k.Contains("hand")||k.Contains("finger")).Any(k=>!bones[k].Equals(other[k]));
                } finally { vf.SetValue(null,oldV);tf.SetValue(null,oldT); }
            });
            foreach(string scenario in new[]{"plant","release","move","slot","inventory","menu"}) Test("live-subsystem/"+scenario,()=>{
                var window=typeof(Window).GetField("m_state",BindingFlags.Static|BindingFlags.NonPublic);
                var oldWindow=window.GetValue(null);var oldScreen=ScreensManager.CurrentScreen;var oldRoot=ScreensManager.RootWidget;var oldAnimation=ScreensManager.m_animationData;var oldFont=LabelWidget.m_bitmapFont;
                T Blank<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
                try {
                    window.SetValue(null,Enum.Parse(window.FieldType,"Active"));ScreensManager.CurrentScreen=null;ScreensManager.m_animationData=null;ScreensManager.RootWidget=new CanvasWidget();
                    LabelWidget.BitmapFont=Blank<BitmapFont>();
                    var project=new Project();var time=new SubsystemTime();var audio=new AudioProbe();var players=new SubsystemPlayers();
                    var world=Blank<WorldSettings>();world.GameMode=GameMode.Survival;var info=Blank<SubsystemGameInfo>();info.WorldSettings=world;
                    var system=(Subsystem)Activator.CreateInstance(mod.GetType("Game.SubsystemScC4"));
                    foreach(var s in new Subsystem[]{time,audio,players,info,new FloorTerrain(),new SubsystemBodies(),new SubsystemParticles(),system}) { s.m_project=project;project.m_subsystems.Add(s); }
                    var player=Blank<ComponentPlayer>();player.PlayerData=Blank<PlayerData>();player.PlayerData.PlayerIndex=1;
                    var widget=Blank<GameWidget>();widget.GuiWidget=new CanvasWidget();widget.WidgetsHierarchyInput=new WidgetInput(WidgetInputDevice.Keyboard);player.PlayerData.m_gameWidget=widget;
                    player.ComponentGui=Blank<ComponentGui>();player.ComponentGui.m_modalPanelContainerWidget=new CanvasWidget();player.ComponentGui.ControlsContainerWidget=new CanvasWidget();
                    player.ComponentHealth=new ComponentHealth{Health=1};player.ComponentBody=new ComponentBody{StandingOnValue=1,CanCrouch=true};player.ComponentMiner=Blank<ComponentMiner>();
                    var inv=new ComponentInventory();for(int i=0;i<3;i++)inv.m_slots.Add(new());inv.AddSlotItems(0,value,1);player.ComponentMiner.Inventory=inv;
                    var entity=Blank<Entity>();player.m_entity=entity;entity.m_project=project;var model=Blank<ComponentFirstPersonModel>();model.m_componentPlayer=player;entity.m_components=[model];players.m_componentPlayers.Add(player);
                    system.Load(new ValuesDictionary());
                    void Button(bool down)=>system.GetType().GetMethod("SetPlantButton").Invoke(system,[player,down]);
                    void Update(double t){time.m_gameTime=t;((IUpdateable)system).Update(.016f);}
                    Button(true);Update(0);Update(2);
                    if(inv.GetSlotCount(0)!=1 || player.ComponentBody.TargetCrouchFactor!=1 || !(bool)system.GetType().GetMethod("IsPlanting").Invoke(system,[player]))return false;
                    if(scenario=="release")Button(false);
                    if(scenario=="move")player.ComponentBody.Velocity=Vector3.UnitX;
                    if(scenario=="slot")inv.ActiveSlotIndex=1;
                    if(scenario=="inventory")player.ComponentMiner.Inventory=new ComponentInventory();
                    if(scenario=="menu")player.ComponentGui.m_modalPanelContainerWidget.Children.Add(new CanvasWidget());
                    Update(3.21);
                    var saved=new ValuesDictionary();system.Save(saved);int count=saved.GetValue<ValuesDictionary>("Charges").Count;
                    bool planted=scenario=="plant";
                    if(count!=(planted?1:0)||inv.GetSlotCount(0)!=(planted?0:1))return false;
                    if(!planted && player.ComponentBody.TargetCrouchFactor!=0)return false;
                    if(planted) {
                        if(!audio.Sounds.Any(n=>n.EndsWith("c4_initiate"))||!audio.Sounds.Any(n=>n.EndsWith("c4_plant")))return false;
                        Update(4.1);if(player.ComponentBody.TargetCrouchFactor!=0)return false;
                        Update(13.3);Update(21.8);Update(23.1);system.Save(saved);
                        var charges=saved.GetValue<ValuesDictionary>("Charges");
                        if(charges.Count!=1||charges.GetValue<ValuesDictionary>("0").GetValue<float>("Remaining") is <=0 or >.12f)return false;
                        if(audio.Sounds.Count(s=>s.EndsWith("c4_warning"))!=1 || !audio.Sounds.Any(s=>s.EndsWith("c4_beep2")))return false;
                        if(audio.BeepPitches.Any(p=>p!=0)||audio.Sounds.Any(s=>s.Contains("c4_beep3")))return false;
                        Update(23.3);Update(23.5);system.Save(saved);
                        if(saved.GetValue<ValuesDictionary>("Charges").Count!=0||audio.Sounds.Count(s=>s.EndsWith("c4_explode_close_01"))!=1)return false;
                        var blasts=(ICollection)system.GetType().GetField("blasts",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(system);
                        if(blasts.Count!=1)return false;
                        Update(26);if(blasts.Count!=0)return false;
                    }
                    system.Dispose();return true;
                } finally { window.SetValue(null,oldWindow);ScreensManager.CurrentScreen=oldScreen;ScreensManager.RootWidget=oldRoot;ScreensManager.m_animationData=oldAnimation;LabelWidget.BitmapFont=oldFont; }
            });
            foreach(string mode in new[]{"success","creative","cancel","moved","capacity","spawn-false","spawn-throws"}) Test("atomic-plant/"+mode,()=>{
                var inv=new ComponentInventory();for(int i=0;i<3;i++)inv.m_slots.Add(new());inv.AddSlotItems(0,value,1);
                var tx=Activator.CreateInstance(mod.GetType("Game.ScThrowTransaction"),inv);
                if(mode=="cancel")tx.GetType().GetMethod("Cancel").Invoke(tx,null);
                if(mode=="moved")inv.ActiveSlotIndex=1;
                int spawns=0;bool Commit()=>(bool)tx.GetType().GetMethod("Commit").Invoke(tx,[mode=="creative",(Func<bool>)(()=>mode!="capacity"),(Func<bool>)(()=>{
                    if(mode=="spawn-throws")throw new InvalidOperationException();if(mode=="spawn-false")return false;spawns++;return true;
                })]);
                bool result=false;try{result=Commit();}catch(TargetInvocationException){if(mode!="spawn-throws")throw;}
                return result==(mode is "success" or "creative") && !Commit() && spawns==(result?1:0) && inv.GetSlotCount(0)==(mode=="success"?0:1);
            });
            Test("old-plant-cancel-does-not-cancel-new-action",()=>{
                T Blank<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
                var p=Blank<ComponentPlayer>();p.ComponentMiner=Blank<ComponentMiner>();var inv=new ComponentInventory();inv.m_slots.Add(new());inv.AddSlotItems(0,value,1);p.ComponentMiner.Inventory=inv;
                var entity=Blank<Entity>();p.m_entity=entity;var model=Blank<ComponentFirstPersonModel>();model.m_componentPlayer=p;entity.m_components=[model];
                long first=(long)Call("KnifeAnimationController","C4Action",p,"plant"),second=(long)Call("KnifeAnimationController","C4Action",p,"plant");
                Call("KnifeAnimationController","CancelC4Action",p,first);
                var state=mod.GetType("Game.KnifeAnimationController").GetMethod("StateFor",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,[model]);
                if(state.GetType().GetField("Action").GetValue(state).ToString()!="Grenade")return false;
                Call("KnifeAnimationController","CancelC4Action",p,second);
                return state.GetType().GetField("Action").GetValue(state).ToString()=="Idle";
            });
        } finally { BlocksManager.Blocks[705]=oldBlock;BlocksManager.BlockTypeToIndex.Clear();foreach(var kv in oldTypes)BlocksManager.BlockTypeToIndex[kv.Key]=kv.Value; }
        foreach(var size in new[]{new Vector2(850,478),new Vector2(1187,637),new Vector2(480,850)})Test("hud-right-avoids-controls/"+size,()=>{
            var obstacles=new[]{new BoundingRectangle(new Vector2(size.X/2-150,size.Y-70),new Vector2(size.X/2+150,size.Y)),new BoundingRectangle(new Vector2(size.X-160,size.Y-230),size)};
            var extent=new Vector2(100,92);var p=(Vector2)Call("ScAmmoHud","FindCorner",size,extent,obstacles);
            return Math.Abs(p.X-(size.X-extent.X-12))<.01 && p.Y>=0&&p.X+extent.X<=size.X&&p.Y+extent.Y<=size.Y && obstacles.All(r=>p.X>=r.Max.X||p.X+extent.X<=r.Min.X||p.Y>=r.Max.Y||p.Y+extent.Y<=r.Min.Y);
        });
        Test("actual-world-batch-ignores-material-alpha",()=>{
            var renderer=new Engine.Graphics.PrimitivesRenderer3D();
            var texture=(Engine.Graphics.Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Engine.Graphics.Texture2D));
            var batch=(Engine.Graphics.TexturedBatch3D)Call("ScC4Block","OpaqueBatch",renderer,texture);
            var mesh=(BlockMesh)Call("ScC4Block","BuildWorldMesh");
            mod.GetType("Game.ScC4Block").GetMethod("DrawOpaque",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,[renderer,mesh,texture,Color.White,1f,Matrix.Identity,new DrawBlockEnvironmentData{Light=15}]);
            return !batch.UseAlphaTest&&ReferenceEquals(batch.BlendState,Engine.Graphics.BlendState.Opaque)
                && batch.TriangleIndices.Count==mesh.Indices.Count && batch.TriangleVertices.All(v=>v.Color.A==255);
        });
        Test("mobile-hud-readable-wear-moved-to-inventory",()=>{
            var font=LabelWidget.m_bitmapFont;
            try {
                LabelWidget.BitmapFont=(BitmapFont)RuntimeHelpers.GetUninitializedObject(typeof(BitmapFont));
                var hud=Activator.CreateInstance(mod.GetType("Game.ScAmmoHud"));var ht=hud.GetType();
                var main=(LabelWidget)ht.GetField("Main").GetValue(hud);float desktop=main.FontScale;
                ht.GetMethod("ApplyDeviceScale").Invoke(hud,[true]);
                var readout=Activator.CreateInstance(mod.GetType("Game.ScAmmoReadout"),["66 / 165 发 · 弹匣 ×2255","",false,false,false,"耐久 100%",0]);
                ht.GetMethod("Show").Invoke(hud,[readout]);var wear=(LabelWidget)ht.GetField("Wear").GetValue(hud);
                return main.FontScale<desktop&&main.FontScale>=.55f&&!wear.IsVisible;
            } finally { LabelWidget.BitmapFont=font; }
        });
        if(Environment.GetEnvironmentVariable("SC_C4_PREVIEW") is {} preview) {
            Directory.CreateDirectory(preview);
            foreach(string frame in new[]{"planted","plant-0","plant-1","plant-2"}) {
                bool world=frame=="planted";float t=world?3.2f:frame=="plant-0"?0:frame=="plant-1"?.97f:2.17f;
                var pose=world?Call("ScC4Visuals","WorldPose",true):Call("Cs2Rig","Sample","c4","plant",t);
                var mesh=Call("Cs2SkinnedMesh","Weapon","c4");var type=mesh.GetType();
                type.GetMethod("SetPose").Invoke(mesh,[pose,world?Matrix.CreateScale(.0254f):Call("Cs2Placement","Placement")]);
                type.GetMethod("Skin").Invoke(mesh,null);
                var vertices=((Array)type.GetProperty("Skinned").GetValue(mesh)).Cast<object>().Select(v=>{
                    var p=(Vector3)v.GetType().GetField("Position").GetValue(v);var n=(Vector3)v.GetType().GetField("Normal").GetValue(v);var uv=(Vector2)v.GetType().GetField("TextureCoordinate").GetValue(v);
                    return new[]{p.X,p.Y,p.Z,n.X,n.Y,n.Z,uv.X,uv.Y};
                }).ToArray();
                var groups=((IEnumerable)type.GetField("Primitives").GetValue(mesh)).Cast<object>().Select(p=>new{material=(string)p.GetType().GetField("Material").GetValue(p),indices=(int[])p.GetType().GetField("Indices").GetValue(p)}).ToArray();
                var off=(Vector2)Call("ScC4Visuals","ScreenOffset","plant",t);
                foreach(int i in groups.Where(g=>g.material=="weapon_c4_digits").SelectMany(g=>g.indices).Distinct())vertices[i][7]+=off.Y;
                File.WriteAllText(Path.Combine(preview,frame+".json"),System.Text.Json.JsonSerializer.Serialize(new{vertices,groups}));
            }
        }
        return results;
    }
}
