using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Engine;
using Engine.Input;
using Engine.Media;
using Engine.Graphics;
using Engine.Animation;
using System.Text.Json.Nodes;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

static class Features130Regression {
    sealed class AudioProbe:SubsystemAudio {
        public int Explosions;
        public override void PlaySound(string name,float volume,float pitch,Vector3 position,float distance,bool delay){if(name.Contains("hegrenade_explode"))Explosions++;}
    }
    sealed class Idle:ComponentBehavior {public override float ImportanceLevel=>5;}
    internal record Result(string Name,bool Ok,string Detail);
    internal static List<Result> Run(Assembly mod,string package,string content) {
        List<Result> results=[];
        void Test(string name,Action check){try{check();results.Add(new("features130/"+name,true,""));}catch(Exception e){results.Add(new("features130/"+name,false,e.ToString()));}}
        void Assert(bool ok,string why){if(!ok)throw new Exception(why);}
        Type T(string n)=>mod.GetType("Game."+n,true);
        object Call(string t,string m,params object[] a)=>T(t).GetMethod(m).Invoke(null,a);
        object Field(object o,string n)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(o);
        void Set(object o,string n,object value)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(o,value);
        U Blank<U>()=>(U)RuntimeHelpers.GetUninitializedObject(typeof(U));
        using var zip=ZipFile.OpenRead(package);
        byte[] Bytes(string n){var entry=zip.GetEntry(n)??(n.EndsWith(".png")?zip.GetEntry(n[..^4]+".webp"):null);using var s=entry?.Open()??throw new Exception("Missing "+n);using var b=new MemoryStream();s.CopyTo(b);return b.ToArray();}
        var settings=T("ScUiSettings");var only=settings.GetField("ButtonOnlyFire");var buttons=settings.GetField("CustomButtons");
        bool oldOnly=(bool)only.GetValue(null),oldButtons=(bool)buttons.GetValue(null);
        var oldMapping=SettingsManager.KeyboardMappingSettings;
        var oldPadMapping=SettingsManager.GamepadMappingSettings;
        var window=typeof(Window).GetField("m_state",BindingFlags.Static|BindingFlags.NonPublic);var oldWindow=window.GetValue(null);
        bool[] keys=(bool[])typeof(Keyboard).GetField("m_keysDownArray",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public).GetValue(null);bool oldJ=keys[(int)Key.J];
        try {
            SettingsManager.InitializeGamepadMappingSettings();SettingsManager.InitializeKeyboardMappingSettings();SettingsManager.KeyboardMappingSettings.SetValue("Dig",Key.J);SettingsManager.KeyboardMappingSettings.SetValue("Hit",Key.J);
            window.SetValue(null,Enum.Parse(window.FieldType,"Active"));var input=new WidgetInput(WidgetInputDevice.Keyboard|WidgetInputDevice.Mouse);
            foreach(bool visible in new[]{false,true})foreach(bool restricted in new[]{false,true})Test($"touch-fire-independent/{visible}/{restricted}",()=>{
                buttons.SetValue(null,visible);only.SetValue(null,restricted);keys[(int)Key.J]=false;
                Assert((bool)Call("ScMobileControls","NativeGunFireAllowedFor",true,input)==!restricted,"blank touch depends on overlay");
                // SushiVirtualButtonWidget invokes these same native injection calls.
                Keyboard.ProcessKeyDown(Key.J);
                Assert((bool)Call("ScMobileControls","NativeGunFireAllowedFor",true,input),"mapped key blocked");
                Keyboard.ProcessKeyUp(Key.J);
                Assert((bool)Call("ScMobileControls","NativeGunFireAllowedFor",true,input)==!restricted,"mapped release left attack enabled");
                Assert((bool)Call("ScMobileControls","NativeGunFireAllowedFor",false,input),"desktop input restricted");
            });
        } finally{only.SetValue(null,oldOnly);buttons.SetValue(null,oldButtons);SettingsManager.KeyboardMappingSettings=oldMapping;SettingsManager.GamepadMappingSettings=oldPadMapping;window.SetValue(null,oldWindow);keys[(int)Key.J]=oldJ;}
        Test("knife-threefold-no-range-or-cadence-change",()=>{
            Assert((float)Call("ScKnifeStrike","Power",false)==21&&(float)Call("ScKnifeStrike","Power",true)==36,"wrong damage");
            Assert((float)Call("ScKnifeStrike","Range",false)==2.2f&&(float)Call("ScKnifeStrike","Range",true)==1.8f,"range changed");
            Assert((double)Call("ScKnifeStrike","Interval",false)==.45&&(double)Call("ScKnifeStrike","Interval",true)==1,"cadence changed");
        });
        Test("magazine-fractions-colors",()=>{
            Assert((float)Call("ScMagazineWidget","ClampFraction",float.NaN)==0&&(float)Call("ScMagazineWidget","ClampFraction",2f)==1,"unsafe fraction");
            var green=(Color)Call("ScMagazineWidget","FillColor",1f);var red=(Color)Call("ScMagazineWidget","FillColor",.1f);
            Assert(green.G>green.R&&red.R>red.G,"color thresholds");
            var yellow=(Color)Call("ScMagazineWidget","FillColor",.4f);
            Assert(yellow.R>200&&yellow.G>170&&yellow.B<100,"missing yellow ammunition state");
            foreach(string name in (string[])T("GunSpec").GetField("FrozenOrder").GetValue(null)) {
                using var s=new MemoryStream(Bytes("Assets/Textures/ScCsgoKnives/hud_weapon_"+name+".png"));var img=Image.Load(s);
                Assert(img.Width==448&&img.Height==144&&img.Pixels.Any(p=>p.A>0)&&img.Pixels.Any(p=>p.A==0),"missing CS2 silhouette "+name);
            }
            foreach(string n in new[]{"magazine","banana_mag","bizon_tube","box","generic_bullet","p90","revolver_loader","shotgun_shell"}){
                using var s=new MemoryStream(Bytes("Assets/Textures/ScCsgoKnives/hud_ammo_"+n+".png"));var img=Image.Load(s);
                Assert(img.Width==128&&img.Height==128&&img.Pixels.Any(p=>p.A>0)&&img.Pixels.Any(p=>p.A==0),"invalid source icon "+n);
            }
        });
        var oldTypes=new Dictionary<Type,int>(BlocksManager.BlockTypeToIndex);var oldNames=new Dictionary<string,int>(BlocksManager.BlockNameToIndex);
        var current=T("ScGunRegistry").GetField("Current");var oldRegistry=current.GetValue(null);var font=LabelWidget.m_bitmapFont;
        var oldAtlas=TextureAtlasManager.m_subtextures.ToArray();
        try {
            BlocksManager.BlockTypeToIndex[T("ScGunBlock")]=701;BlocksManager.BlockNameToIndex["ScGunBlock"]=701;
            BlocksManager.BlockTypeToIndex[T("ScGunSkinTemplateBlock")]=702;
            BlocksManager.BlockTypeToIndex[T("ScGunCounterTemplateBlock")]=703;
            var registry=Activator.CreateInstance(T("ScGunRegistry"));current.SetValue(null,registry);
            using(var native=ZipFile.OpenRead(content))using(var glyphs=native.Entries.Single(e=>e.FullName.EndsWith("Fonts/Pericles.lst")).Open())LabelWidget.BitmapFont=BitmapFont.Initialize((Texture2D)null,glyphs);
            foreach(string n in new[]{"ProgressBar","InteractiveItemOverlay","EditItemOverlay","FoodItemOverlay"})
                TextureAtlasManager.m_subtextures["Textures/Atlas/"+n]=new Subtexture(Blank<Texture2D>(),Vector2.Zero,Vector2.One);
            Test("inventory-percent-slot-reuse",()=>{
                int fresh=Terrain.MakeBlockValue(701,0,(int)Call("GunSpec","MakeData",0,30,false));
                var inv=new ComponentCreativeInventory{OpenSlotsCount=10};inv.m_slots.Add(fresh);
                var oldAir=BlocksManager.Blocks[0];InventorySlotWidget slot;
                try {BlocksManager.Blocks[0]=new AirBlock();slot=new InventorySlotWidget();}
                finally {BlocksManager.Blocks[0]=oldAir;}
                slot.m_inventory=inv;slot.m_slotIndex=0;slot.m_blockIconWidget.IsVisible=true;
                Call("ScInventoryWear","Update",slot);var label=slot.Children.OfType<LabelWidget>().Single(w=>w.Name=="ScGunWearPercent");
                Assert(label.Text=="100%"&&label.IsVisible&&!label.IsHitTestVisible&&label.HorizontalAlignment==WidgetAlignment.Near&&label.VerticalAlignment==WidgetAlignment.Far,"wear placement");
                int watermark=(int)registry.GetType().GetMethod("PeekNextId").Invoke(registry,null);
                foreach(var pair in new[]{("ScGunSkinTemplateBlock",702),("ScGunCounterTemplateBlock",703)}) {
                    var block=(Block)Activator.CreateInstance(T(pair.Item1));block.BlockIndex=pair.Item2;
                    foreach(int template in block.GetCreativeValues()) {
                        inv.m_slots[0]=template;Call("ScInventoryWear","Update",slot);
                        Assert(label.Text=="100%"&&label.IsVisible&&label.Color==Color.White,"template lacks wear "+template);
                    }
                }
                Assert((int)registry.GetType().GetMethod("PeekNextId").Invoke(registry,null)==watermark,"preview allocated gun IDs");
                var skins=((IEnumerable)T("ScGunSkinCatalog").GetField("All").GetValue(null)).Cast<object>();
                var names=(string[])T("GunSpec").GetField("FrozenOrder").GetValue(null);
                foreach(var skin in skins){int variant=Array.IndexOf(names,(string)skin.GetType().GetProperty("Gun").GetValue(skin));int paint=(int)skin.GetType().GetProperty("PaintId").GetValue(skin);
                    int id=(int)registry.GetType().GetMethod("Allocate").Invoke(registry,[variant,1,false,50,100,paint]);int data=(int)Call("GunSpec","WithId",variant,id);
                    inv.m_slots[0]=Terrain.MakeBlockValue(701,0,data);Call("ScInventoryWear","Update",slot);Assert(label.IsVisible&&label.Text=="50%","owned skin durability lost");}
                inv.m_slots[0]=0;Call("ScInventoryWear","Update",slot);Assert(!label.IsVisible,"stale percent on empty slot");
                inv.m_slots[0]=Terrain.MakeBlockValue(3);Call("ScInventoryWear","Update",slot);Assert(!label.IsVisible,"percent on vanilla item");
            });
            Test("right-hud-compact-magazine-count",()=>{
                var h=Activator.CreateInstance(T("ScAmmoHud"));var read=Activator.CreateInstance(T("ScAmmoReadout"),["old long text","",false,false,false,"耐久 100%",0]);
                foreach(var pair in new Dictionary<string,object>{{"Compact",true},{"LoadedText","15"},{"CapacityText","/ 30"},{"Fraction",.5f},{"ReserveCount","23"}})read.GetType().GetProperty(pair.Key).SetValue(read,pair.Value);
                h.GetType().GetMethod("Show").Invoke(h,[read]);var panel=(StackPanelWidget)Field(h,"Panel");var mag=Field(h,"Magazine");
                Assert(panel.HorizontalAlignment==WidgetAlignment.Far&&((LabelWidget)Field(h,"Main")).Text=="15 / 30","HUD position/text");
                Assert(((LabelWidget)Field(mag,"Count")).Text=="23"&&(float)Field(mag,"Fraction")==.5f&&!((LabelWidget)Field(h,"Wear")).IsVisible,"magazine count/fill");
            });
            Test("hud-hidden-touch-pad-does-not-reserve-space",()=>{
                foreach(var size in new[]{new Vector2(1187,637),new Vector2(850,478),new Vector2(480,850)})foreach(bool shown in new[]{false,true}) {
                    var host=new CanvasWidget{Size=size};var pad=new CanvasWidget{Name="LookPadContainer",Size=new(160),HorizontalAlignment=WidgetAlignment.Far,VerticalAlignment=WidgetAlignment.Far,MarginBottom=75};
                    var rectangle=new RectangleWidget{Name="LookRectangle",IsVisible=shown};pad.Children.Add(rectangle);host.Children.Add(pad);
                    var h=Activator.CreateInstance(T("ScAmmoHud"));var gui=Blank<ComponentGui>();gui.ControlsContainerWidget=host;h.GetType().GetMethod("Attach").Invoke(h,[gui]);
                    var read=Activator.CreateInstance(T("ScAmmoReadout"),["30 / 30","",false,false,false,"",0]);read.GetType().GetProperty("Compact").SetValue(read,true);read.GetType().GetProperty("LoadedText").SetValue(read,"30");read.GetType().GetProperty("CapacityText").SetValue(read,"/ 30");
                    for(int frame=0;frame<4;frame++){h.GetType().GetMethod("Show").Invoke(h,[read]);host.Measure(size);host.Arrange(Vector2.Zero,size);}
                    var panel=(Widget)Field(h,"Panel");Assert(panel.GlobalBounds.Max.X<=size.X&&panel.GlobalBounds.Min.Y>=0,"HUD outside screen");
                    if(shown)Assert(panel.GlobalBounds.Max.Y<=rectangle.GlobalBounds.Min.Y-5,"visible touch pad overlap");
                    else Assert(size.Y-panel.GlobalBounds.Max.Y<20,"hidden touch pad leaves large blank space");
                }
            });
        } finally {TextureAtlasManager.m_subtextures.Clear();foreach(var p in oldAtlas)TextureAtlasManager.m_subtextures[p.Key]=p.Value;current.SetValue(null,oldRegistry);LabelWidget.BitmapFont=font;BlocksManager.BlockTypeToIndex.Clear();foreach(var p in oldTypes)BlocksManager.BlockTypeToIndex[p.Key]=p.Value;BlocksManager.BlockNameToIndex.Clear();foreach(var p in oldNames)BlocksManager.BlockNameToIndex[p.Key]=p.Value;}
        Test("chicken-native-glb-reader-and-clips",()=>{
            using var stream=new MemoryStream(Bytes("Assets/Models/ScCsgoKnives/chicken.glb"));var data=GltfLoader.Load(stream);
            Assert(data.Skin is not null&&data.Bones.Count>20&&data.Meshes.Count>0,"missing native skin");
            Assert(data.Animations.Select(a=>a.Name).ToHashSet().SetEquals(new[]{"idle","walk","run"}),"missing source animations");
            foreach(var a in data.Animations)Assert(a.Duration>0&&a.Channels.Count>0,"empty animation "+a.Name);
            using var model=new Model{ModelData=data,Skin=data.Skin,Animations=data.Animations};
            foreach(var b in data.Bones)model.m_bones.Add(new ModelBone{Model=model,Index=model.m_bones.Count,Name=b.Name,Transform=b.Transform});
            for(int i=0;i<data.Bones.Count;i++)if(data.Bones[i].ParentBoneIndex>=0){var b=model.m_bones[i];b.ParentBone=model.m_bones[data.Bones[i].ParentBoneIndex];b.ParentBone.m_childBones.Add(b);}else model.m_rootBone=model.m_bones[i];
            var oldTemplates=AnimationTemplateManager.s_templates.ToArray();
            try {
            using var vanilla=ZipFile.OpenRead(content);using var template=vanilla.GetEntry("Assets/AnimationTemplates/Simple.template.json").Open();
            AnimationTemplateManager.LoadFromJsonNode(JsonNode.Parse(template));
            var loader=new AnimationConfigLoader();var config=loader.LoadFromJsonNode(JsonNode.Parse(Bytes("Assets/Animations/ScChicken.json")));
            var ctrl=loader.CreateController(config,model);
            foreach(var pair in new[]{(0f,"idle"),(1f,"walk"),(3f,"run")}) {
                ctrl.Parameters.SetBool("IsDead",false);ctrl.Parameters.SetFloat("SpeedAbs",pair.Item1);ctrl.Update(.5f);ctrl.Update(.5f);
                Assert(ctrl.Layers.Any(l=>l.AnimationPlayer?.Animation?.Name==pair.Item2),"actual controller did not play "+pair.Item2);
                var pose=new Matrix?[model.Bones.Count];foreach(var layer in ctrl.Layers)layer.SampleTransforms(pose,model);
                Assert(pose.Any(p=>p.HasValue),"no sampled skeletal transforms");
            }
            } finally {AnimationTemplateManager.s_templates.Clear();foreach(var p in oldTemplates)AnimationTemplateManager.s_templates[p.Key]=p.Value;}
        });
        Test("chicken-native-database-inheritance",()=>{
            if(content is null)throw new Exception("--vanilla-content required");
            var oldDb=DatabaseManager.m_gameDatabase;var oldV=new Dictionary<string,ValuesDictionary>(DatabaseManager.m_valueDictionaries);
            try {
                using var vanilla=ZipFile.OpenRead(content);using var s=vanilla.Entries.First(e=>e.FullName.EndsWith("Database.xml",StringComparison.Ordinal)).Open();var root=XElement.Load(s);
                using var addition=new MemoryStream(Bytes("Assets/ScChicken.xdb"));ModsManager.CombineDataBase(root,addition,"zh667.ScCsgoKnives");DatabaseManager.LoadDataBaseFromXml(root);
                var v=DatabaseManager.FindEntityValuesDictionary("ScCsgoChicken",true);
                Assert(!v.ContainsKey("ChaseBehavior")&&!v.ContainsKey("Miner"),"chicken can attack");
                Assert(v.GetValue<ValuesDictionary>("Locomotion").GetValue<float>("FlySpeed")==0,"flying chicken");
                Assert(v.GetValue<ValuesDictionary>("FlightlessBirdModel").GetValue<string>("AnimationConfigPath")=="Animations/ScChicken","animation config disconnected");
                Assert(v.GetValue<ValuesDictionary>("ScChicken").GetValue<string>("Class")=="Game.ComponentScChicken","behavior disconnected");
                Assert(v.GetValue<ValuesDictionary>("CreatureEggData").GetValue<int>("EggTypeIndex")==-1,"uses global egg ID");
                Assert(v.GetValue<ValuesDictionary>("FlightlessBirdModel").GetValue<float>("ModelScale")==1.6f&&v.GetValue<ValuesDictionary>("Body").GetValue<Vector3>("BoxSize").Y==.58f,"chicken size/collision not updated");
            } finally {DatabaseManager.m_gameDatabase=oldDb;DatabaseManager.m_valueDictionaries.Clear();foreach(var p in oldV)DatabaseManager.m_valueDictionaries[p.Key]=p.Value;}
        });
        Test("egg-native-first-person-and-hand-placement",()=>{
            using var vanilla=ZipFile.OpenRead(content);using var s=vanilla.Entries.Single(e=>e.FullName.EndsWith("BlocksData.txt")).Open();using var reader=new StreamReader(s);var lines=reader.ReadToEnd().Split('\n');
            var headers=lines[0].Trim().Split(';');var values=lines.Single(l=>l.StartsWith("EggBlock;")).Trim().Split(';');
            var egg=(Block)Activator.CreateInstance(T("ScChickenEggBlock"));
            foreach(string n in new[]{"FirstPersonScale","InHandScale"})Assert((float)typeof(Block).GetField(n).GetValue(egg)==float.Parse(values[Array.IndexOf(headers,n)],System.Globalization.CultureInfo.InvariantCulture),"egg scale differs from native "+n);
            Assert(egg.GetFirstPersonOffset(0)==new Vector3(.5f,-.5f,-.6f)&&egg.GetInHandOffset(0)==new Vector3(0,.12f,0),"egg at eye origin");
        });
        Test("chicken-death-classification-no-chain",()=>{
            var projectile=(Attackment)RuntimeHelpers.GetUninitializedObject(typeof(ProjectileAttackment));var melee=(Attackment)RuntimeHelpers.GetUninitializedObject(typeof(MeleeAttackment));
            var shot=(Attackment)RuntimeHelpers.GetUninitializedObject(T("ScSurvivalBalance").GetNestedType("GunAttack"));
            var area=(Attackment)RuntimeHelpers.GetUninitializedObject(T("SubsystemScGrenades").GetNestedType("AreaAttack",BindingFlags.NonPublic));
            var bomb=(Attackment)RuntimeHelpers.GetUninitializedObject(T("SubsystemScC4").GetNestedType("BombAttack",BindingFlags.NonPublic));
            Assert((bool)Call("ComponentScChicken","ExplodesFrom",shot),"CS bullet should explode");
            foreach(var a in new Attackment[]{melee,projectile,area,bomb,null})Assert(!(bool)Call("ComponentScChicken","ExplodesFrom",a),"nonbullet chain explosion");
        });
        foreach(bool bullet in new[]{false,true})Test("chicken-death-two-xml/"+bullet,()=>{
            var component=Activator.CreateInstance(T("ComponentScChicken"));var mark=T("ComponentScChicken").GetMethod("MarkDeath");
            Assert((bool)mark.Invoke(component,[bullet,4])&&!(bool)mark.Invoke(component,[true,7]),"duplicate death");
            var values=new ValuesDictionary();((Component)component).Save(values,null);
            for(int r=0;r<2;r++) {
                var xml=new XElement("Values");values.Save(xml);values=new ValuesDictionary();values.ApplyOverrides(XElement.Parse(xml.ToString()));
                Assert(values.GetValue<bool>("DeathHandled")&&values.GetValue<bool>("PendingBlast")==bullet&&values.GetValue<int>("BlastOwner")==4,"lost death state");
                var project=new Project();project.m_subsystems.Add(new SubsystemTime());project.m_subsystems.Add(new SubsystemPlayers());
                var next=(Component)Activator.CreateInstance(T("ComponentScChicken"));var entity=Blank<Entity>();entity.m_project=project;
                entity.m_components=[next,new ComponentCreature(),new ComponentPathfinding()];foreach(var c in entity.m_components)c.m_entity=entity;
                next.Load(values,null);Assert((bool)Field(next,"PendingBlast")==bullet&&!(bool)mark.Invoke(next,[true,7]),"reloaded component repeats death");
                values=new ValuesDictionary();next.Save(values,null);
            }
        });
        Test("chicken-follow-toggle-and-stop",()=>{
            var c=Activator.CreateInstance(T("ComponentScChicken"));var creature=Blank<ComponentCreature>();creature.ComponentHealth=new ComponentHealth{Health=1};Set(c,"creature",creature);
            var toggle=T("ComponentScChicken").GetMethod("ToggleFollow");
            Assert((bool)toggle.Invoke(c,[2])&&(int)Field(c,"FollowerPlayer")==2&&creature.ConstantSpawn,"follow failed");
            Assert((bool)toggle.Invoke(c,[2])&&(int)Field(c,"FollowerPlayer")==-1,"toggle stop failed");
            creature.ComponentHealth.Health=0;Assert(!(bool)toggle.Invoke(c,[2]),"dead chicken follows");
        });
        Test("chicken-real-selector-path-and-follow-xml",()=>{
            var project=new Project();var time=new SubsystemTime();var players=new SubsystemPlayers();project.m_subsystems.Add(time);project.m_subsystems.Add(players);
            var entity=Blank<Entity>();entity.m_project=project;var body=new ComponentBody();var health=new ComponentHealth{Health=1};
            var creature=new ComponentCreature{ComponentBody=body,ComponentHealth=health,ComponentCreatureModel=new ComponentFlightlessBirdModel()};
            var path=new ComponentPathfinding{m_componentPilot=new ComponentPilot{m_componentCreature=creature}};var selector=new ComponentBehaviorSelector();
            var c=(ComponentBehavior)Activator.CreateInstance(T("ComponentScChicken"));entity.m_components=[creature,body,health,path,selector,c,new Idle()];foreach(var component in entity.m_components)component.m_entity=entity;
            var player=Blank<ComponentPlayer>();player.PlayerData=Blank<PlayerData>();player.PlayerData.PlayerIndex=2;player.ComponentBody=new ComponentBody{Position=new Vector3(5,0,0)};player.ComponentHealth=new ComponentHealth{Health=1};players.m_componentPlayers.Add(player);
            c.Load(new(),null);selector.Load(new(),null);T("ComponentScChicken").GetMethod("ToggleFollow").Invoke(c,[2]);
            selector.Update(.1f);((IUpdateable)c).Update(.1f);Assert(c.IsActive&&path.Destination==player.ComponentBody.Position,"native path not following");
            var values=new ValuesDictionary();c.Save(values,null);for(int r=0;r<2;r++){var xml=new XElement("Values");values.Save(xml);values=new();values.ApplyOverrides(XElement.Parse(xml.ToString()));c.Load(values,null);Assert((int)Field(c,"FollowerPlayer")==2,"lost follower on reload");values=new();c.Save(values,null);}
            T("ComponentScChicken").GetMethod("ToggleFollow").Invoke(c,[2]);Assert(!c.IsActive&&path.Destination is null,"stop leaves walking order");
        });
        Test("chicken-pending-blast-dispatch-once-and-save",()=>{
            var project=new Project();var grenades=(Subsystem)Activator.CreateInstance(T("SubsystemScGrenades"));var audio=new AudioProbe();
            foreach(var s in new Subsystem[]{new SubsystemTime(),new SubsystemPlayers(),new SubsystemTerrain(),new SubsystemBodies(),new SubsystemGameInfo(),audio,grenades}){s.m_project=project;project.m_subsystems.Add(s);}grenades.Load(new());
            var entity=Blank<Entity>();entity.m_project=project;var c=(Component)Activator.CreateInstance(T("ComponentScChicken"));var body=new ComponentBody();
            var creature=new ComponentCreature{ComponentBody=body,ComponentHealth=new ComponentHealth{Health=0}};entity.m_components=[c,creature,new ComponentPathfinding()];foreach(var x in entity.m_components)x.m_entity=entity;
            c.Load(new(),null);T("ComponentScChicken").GetMethod("MarkDeath").Invoke(c,[true,-1]);((IUpdateable)c).Update(.01f);((IUpdateable)c).Update(.01f);
            Assert(audio.Explosions==1&&!((bool)Field(c,"PendingBlast")),"blast repeated or missing");var values=new ValuesDictionary();c.Save(values,null);c.Load(values,null);((IUpdateable)c).Update(.01f);Assert(audio.Explosions==1,"reload repeats blast");
            Assert((float)Call("ScGrenadeState","ChickenPower",0f)==48&&(float)Call("ScGrenadeState","ChickenPower",6f)==0,"wrong chicken blast curve");
            Assert((float)Call("ScGrenadeState","HePower",0f)==96&&(float)Call("ScGrenadeState","HePower",7.8f)==0,"wrong HE blast curve");
        });
        return results;
    }
}
