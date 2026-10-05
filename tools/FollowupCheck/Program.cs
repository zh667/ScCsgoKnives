// agent-followup-140 F1/F2/F4 native UI evidence: the real settings screen (enemy rules: Save/Cancel/Defaults for the
// new-world defaults and for a world, old settings JSON), and offscreen renders of the damage-direction HUD and the
// throw preview. Settings live in this tool's own app directory; the player's configuration is never opened.
// Offline renders, not game screenshots: Windows real-game capture remains the visual acceptance.
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Game;
using GameEntitySystem;

if(args.Length is not (2 or 3))throw new ArgumentException("FollowupCheck <Content.zip> <output folder> [core .scmod for mod textures]");
string contentPath=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
string corePackage=args.Length==3?Path.GetFullPath(args[2]):null;
Dispatcher.Initialize();
var checks=new List<string>();var failures=new List<string>();
void Check(string name,bool ok){if(ok)checks.Add(name);else failures.Add(name);}
T Blank<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
using var content=System.IO.Compression.ZipFile.OpenRead(contentPath);
var caches=(IDictionary<string,List<object>>)typeof(ContentManager).GetField("Caches",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
foreach(var entry in content.Entries.Where(e=>e.FullName.EndsWith(".xml"))){using var s=entry.Open();caches[entry.FullName.Replace("Assets/","")[..^4]]=[XElement.Load(s)];}

// Isolation first: every settings write below must land under this tool's directory.
string settingsFile=Path.GetFullPath(Storage.GetSystemPath(ScUiSettings.Path));
if(!settingsFile.StartsWith(Path.GetFullPath(AppContext.BaseDirectory),StringComparison.OrdinalIgnoreCase)){Console.WriteLine("FAIL settings path outside the tool: "+settingsFile);return 2;}

// ---- Old settings JSON: missing new fields take defaults, the old switch and unknown keys survive a save. ----
File.WriteAllText(settingsFile,"{\"NaturalEnemies\":false,\"FutureOnlyKey\":123}");
ScUiSettings.ResetAll();ScUiSettings.Load();
Check("old JSON keeps natural spawning off",!ScUiSettings.NaturalEnemies);
Check("old JSON gains 30 days / standard density",ScUiSettings.EnemyGraceDays==30&&ScUiSettings.EnemyDensity==ScEnemyDensity.Standard);
Check("old JSON gains damage HUD and throw preview on",ScUiSettings.DamageIndicator&&ScUiSettings.GrenadePreview);
Check("old JSON defaults to sniper crosshair and no starter gift",ScUiSettings.SniperHipCrosshair&&ScUiSettings.StarterPlan==ScStarterPlan.None);
Check("old JSON saves",ScUiSettings.Save());
Check("unknown key preserved on save",JsonNode.Parse(File.ReadAllText(settingsFile))?["FutureOnlyKey"]?.GetValue<int>()==123);
File.Delete(settingsFile);ScUiSettings.ResetAll();ScUiSettings.Load();
Check("fresh defaults: on, 30 days, standard",ScUiSettings.NaturalEnemies&&ScUiSettings.EnemyGraceDays==30&&ScUiSettings.EnemyDensity==ScEnemyDensity.Standard);

bool done=false;int exit=0;
Window.Frame+=()=>{if(done)return;done=true;try{
    using(var glyph=content.Entries.Single(e=>e.FullName.EndsWith("Fonts/Pericles.lst")).Open())
    using(var texture=content.Entries.Single(e=>e.FullName.EndsWith("Fonts/Pericles.webp")).Open())LabelWidget.BitmapFont=BitmapFont.Initialize(texture,glyph);
    caches["Fonts/Pericles"]=[LabelWidget.BitmapFont];
    using(var s=content.Entries.Single(e=>e.FullName.EndsWith("Textures/Gui/Panorama.webp")).Open())caches[PanoramaWidget.TexturePath]=[Texture2D.Load(s)];
    using(var tex=content.Entries.Single(e=>e.FullName.EndsWith("Atlases/AtlasTexture.webp")).Open())
    using(var r=new StreamReader(content.Entries.Single(e=>e.FullName.EndsWith("Atlases/Atlas.txt")).Open()))TextureAtlasManager.LoadAtlases(Texture2D.Load(tex),r.ReadToEnd());
    BlocksManager.Blocks[0]=new AirBlock{BlockIndex=0,IsCollidable=false};BlocksManager.Blocks[2]=new DirtBlock{BlockIndex=2,IsCollidable=true};
    ScreensManager.AddScreen("Settings",new FixtureScreen());
    var animation=typeof(ScreensManager).GetField("m_animationData",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
    // The settings screen leaves by switching to its back screen (here the fixture "Settings"). Reset the manager after
    // each check so every Save/Cancel starts from the same state (a repeated switch to the current screen is a no-op).
    void ResetScreens(){ScreensManager.CurrentScreen=null;ScreensManager.PreviousScreen=null;animation.SetValue(null,null);}
    bool Left(){bool left=ScreensManager.CurrentScreen is FixtureScreen;ResetScreens();return left;}

    object Field(object o,string n)=>o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);
    void Layout(Screen s,Vector2 size){for(int i=0;i<3;i++){s.Measure(size);s.Arrange(Vector2.Zero,size);}s.Update();s.Measure(size);s.Arrange(Vector2.Zero,size);}
    void Click(Screen s,string n){var b=(BevelledButtonWidget)Field(s,n);b.m_clickableWidget.IsClicked=true;s.Update();b.m_clickableWidget.IsClicked=false;}
    void Capture(Widget root,Vector2 size,string name){
        using var target=new RenderTarget2D((int)size.X,(int)size.Y,1,ColorFormat.Rgba8888,DepthFormat.Depth24Stencil8);
        Display.RenderTarget=target;Display.Viewport=new(0,0,(int)size.X,(int)size.Y);Display.ScissorRectangle=new(0,0,(int)size.X,(int)size.Y);
        Display.Clear(new Color(22,27,34),1,0);Widget.DrawWidgetsHierarchy(root);
        using(var file=File.Create(Path.Combine(output,$"{name}-{size.X}x{size.Y}.png")))RenderTarget2D.Save(target,file,ImageFileFormat.Png,false);
        Display.RenderTarget=null;
    }
    ScGunSettingsScreen Open(Vector2 size){ResetScreens();var screen=new ScGunSettingsScreen{WidgetsHierarchyInput=new WidgetInput()};screen.Enter([]);Layout(screen,size);return screen;}
    ScEnemyRules Working(ScGunSettingsScreen s)=>(ScEnemyRules)Field(s,"m_enemy");
    string Texts(ContainerWidget w)=>string.Join("|",w.AllChildren.OfType<LabelWidget>().Select(l=>l.Text));
    void ScrollTo(ScGunSettingsScreen s,Vector2 size,string field){
        var scroll=(ScrollPanelWidget)Field(s,"m_scroll");var label=(Widget)Field(s,field);
        scroll.ScrollPosition=Math.Max(0,label.GlobalBounds.Min.Y-scroll.GlobalBounds.Min.Y+scroll.ScrollPosition-40);Layout(s,size);
    }

    Check("world-size diagnostics removed from the screen",typeof(ScGunSettingsScreen).GetField("m_sizeTask",BindingFlags.NonPublic|BindingFlags.Instance)==null);
    foreach(var size in new[]{new Vector2(850,479),new Vector2(360,640)}){
        // ---- New-world defaults (main menu: no world open). ----
        GameManager.m_project=null;ScEnemyRulesBridge.Read=null;ScEnemyRulesBridge.Write=null;ScEnemyRulesBridge.Progress=null;
        var screen=Open(size);var text=Texts(screen);
        Check("enemy section present "+size,text.Contains("敌对小队")&&text.Contains("刷新密度")&&text.Contains("开档满 30 个游戏日后开始"));
        Check("no world-size entry "+size,!text.Contains("世界占用")&&!text.Contains("存档占用"));
        Check("defaults mode note "+size,!(bool)Field(screen,"m_enemyWorld"));
        ScrollTo(screen,size,"m_enemyDays");Capture(screen,size,"settings-enemy-defaults");
        Click(screen,"m_enemyMore");Click(screen,"m_enemyMore");Click(screen,"m_enemyDensity");((CheckboxWidget)Field(screen,"m_enemyNatural")).IsChecked=false;screen.Update();
        Check("working copy edited "+size,Working(screen)==new ScEnemyRules(false,32,ScEnemyDensity.Dense));
        Check("label follows edit "+size,Texts(screen).Contains("开档满 32 个游戏日后开始")&&Texts(screen).Contains(ScEnemySpawnPolicy.Label(ScEnemyDensity.Dense)));
        ScrollTo(screen,size,"m_enemyDays");Capture(screen,size,"settings-enemy-edited");
        Click(screen,"m_cancel");
        Check("cancel leaves "+size,Left());
        Check("cancel writes nothing "+size,ScUiSettings.NaturalEnemies&&ScUiSettings.EnemyGraceDays==30&&ScUiSettings.EnemyDensity==ScEnemyDensity.Standard);
        screen=Open(size);Check("reopen shows saved values "+size,Working(screen)==new ScEnemyRules(true,30,ScEnemyDensity.Standard));
        Click(screen,"m_enemyMore");Click(screen,"m_enemyMore");Click(screen,"m_enemyDensity");Click(screen,"m_save");
        Check("save leaves "+size,Left());
        ScUiSettings.ResetAll();ScUiSettings.Load();
        Check("save persists defaults "+size,ScUiSettings.NaturalEnemies&&ScUiSettings.EnemyGraceDays==32&&ScUiSettings.EnemyDensity==ScEnemyDensity.Dense);
        screen=Open(size);Click(screen,"m_defaults");Layout(screen,size);
        Check("defaults resets the working copy only "+size,Working(screen)==ScEnemyRules.Default&&ScUiSettings.EnemyGraceDays==32);
        Click(screen,"m_cancel");Left();ScUiSettings.ResetAll();ScUiSettings.Load();
        Check("defaults then cancel keeps saved "+size,ScUiSettings.EnemyGraceDays==32&&ScUiSettings.EnemyDensity==ScEnemyDensity.Dense);
        screen=Open(size);for(int i=0;i<40;i++)Click(screen,"m_enemyLess");
        Check("days clamp at 0 "+size,Working(screen).GraceDays==0&&Texts(screen).Contains("开档后立即允许"));
        Click(screen,"m_defaults");Layout(screen,size);Click(screen,"m_save");Left();
        screen=Open(size);((CheckboxWidget)Field(screen,"m_sniperHipCrosshair")).IsChecked=false;Click(screen,"m_starter");Click(screen,"m_cancel");Left();
        Check("new controls cancel without changing settings "+size,ScUiSettings.SniperHipCrosshair&&ScUiSettings.StarterPlan==ScStarterPlan.None);
        screen=Open(size);((CheckboxWidget)Field(screen,"m_sniperHipCrosshair")).IsChecked=false;Click(screen,"m_starter");Click(screen,"m_starter");Click(screen,"m_starter");Click(screen,"m_save");Left();
        ScUiSettings.ResetAll();ScUiSettings.Load();
        Check("new controls persist "+size,!ScUiSettings.SniperHipCrosshair&&ScUiSettings.StarterPlan==ScStarterPlan.Full);
        screen=Open(size);Click(screen,"m_defaults");Layout(screen,size);Click(screen,"m_save");Left();
        Check("new controls restore defaults "+size,ScUiSettings.SniperHipCrosshair&&ScUiSettings.StarterPlan==ScStarterPlan.None);
        ScStarterPlan? picked=null;var dialog=new ScStarterDialog(ScStarterPlan.None,p=>{picked=p;return true;});
        var dialogRoot=new CanvasWidget{Size=size};dialogRoot.Children.Add(dialog);dialogRoot.Measure(size);dialogRoot.Arrange(Vector2.Zero,size);Capture(dialogRoot,size,"starter-choice");
        var confirm=(BevelledButtonWidget)Field(dialog,"confirm");confirm.m_clickableWidget.IsClicked=true;dialog.Update();
        Check("starter confirmation defaults to no equipment "+size,picked==ScStarterPlan.None);

        // ---- A world is open: the screen edits that world's rules through the bridge, not the device defaults. ----
        var world=new ScEnemyRules(true,5,ScEnemyDensity.Sparse);ScEnemyRules? written=null;int writes=0;bool writable=true;
        GameManager.m_project=new Project();
        ScEnemyRulesBridge.Read=_=>world;ScEnemyRulesBridge.Write=(_,r)=>{writes++;if(!writable)return false;written=r;world=r;return true;};
        ScEnemyRulesBridge.Progress=(_,r)=>$"本世界已过 3.2 个游戏日；自然刷新{(r.Natural?$"将在 {Math.Max(0,r.GraceDays-3.2f):0.0} 个游戏日后开始":"已关闭")}。";
        screen=Open(size);text=Texts(screen);
        Check("world mode reads the world "+size,(bool)Field(screen,"m_enemyWorld")&&Working(screen)==world&&text.Contains("当前世界的规则")&&text.Contains("本世界已过"));
        ScrollTo(screen,size,"m_enemyDays");Capture(screen,size,"settings-enemy-world");
        Click(screen,"m_enemyMore");Click(screen,"m_cancel");Left();
        Check("world cancel writes nothing "+size,writes==0&&world.GraceDays==5);
        screen=Open(size);Click(screen,"m_enemyMore");Click(screen,"m_enemyDensity");Click(screen,"m_save");
        Check("world save writes the world only "+size,Left()&&writes==1&&written==new ScEnemyRules(true,6,ScEnemyDensity.Standard)&&ScUiSettings.EnemyGraceDays==30&&ScUiSettings.EnemyDensity==ScEnemyDensity.Standard);
        writable=false;screen=Open(size);Click(screen,"m_enemyMore");Click(screen,"m_save");
        Check("world write failure stays and says so "+size,!Left()&&((LabelWidget)Field(screen,"m_status")).Text.Contains("敌对小队规则未写入"));
        Capture(screen,size,"settings-enemy-world-write-failed");
        GameManager.m_project=null;ScEnemyRulesBridge.Read=null;ScEnemyRulesBridge.Write=null;ScEnemyRulesBridge.Progress=null;
    }

    // Dialog arbitration reads all parents, including a third-party startup dialog on the screen root.
    {
        var previousProject=GameManager.Project;var previousScreen=ScreensManager.CurrentScreen;
        try{
            var project=new Project();GameManager.m_project=project;ScreensManager.CurrentScreen=Blank<GameScreen>();
            var players=new SubsystemPlayers{m_project=project};project.m_subsystems.Add(players);
            var data=Blank<PlayerData>();data.PlayerIndex=0;data.m_stateMachine=new StateMachine();data.m_stateMachine.AddState("Playing",null,null,null);data.m_stateMachine.TransitionTo("Playing");
            var player=Blank<ComponentPlayer>();data.ComponentPlayer=player;player.PlayerData=data;player.ComponentHealth=new ComponentHealth{Health=1};player.ComponentGui=new ComponentGui();
            data.m_gameWidget=Blank<GameWidget>();data.m_gameWidget.GuiWidget=new CanvasWidget();
            players.m_playersData.Add(data);players.m_componentPlayers.Add(player);
            var starter=new SubsystemScStarterEquipment{m_project=project};var inv=new ComponentInventory();inv.m_slots.Add(new());
            starter.TryGrant(GameMode.Survival,PlayerData.SpawnMode.InitialNoIntro,0,1,inv,(_,_)=>{});
            var update=typeof(SubsystemScStarterEquipment).GetMethod("UpdateChoice",BindingFlags.NonPublic|BindingFlags.Instance);
            void Ready()=>typeof(SubsystemScStarterEquipment).GetField("m_quietSince",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(starter,Time.RealTime-2);
            var other=new Dialog();DialogsManager.m_dialogs.Add(other);Ready();update.Invoke(starter,null);
            Check("starter waits for another mod dialog on any parent",Field(starter,"m_dialog") is null);
            DialogsManager.m_dialogs.Remove(other);DialogsManager.m_animationData[other]=new DialogsManager.AnimationData();Ready();update.Invoke(starter,null);
            Check("starter waits for another mod exit animation",Field(starter,"m_dialog") is null);
            DialogsManager.m_animationData.Remove(other);Ready();update.Invoke(starter,null);
            Check("starter appears after other startup dialogs finish",Field(starter,"m_dialog") is ScStarterDialog);
            starter.Dispose();DialogsManager.m_dialogs.Clear();DialogsManager.m_animationData.Clear();
        }finally{GameManager.m_project=previousProject;ScreensManager.CurrentScreen=previousScreen;}
    }

    // ---- current-direction-20260929 §2: the protection HUD (textures from the candidate core package). ----
    if(corePackage is not null){
        using var core=System.IO.Compression.ZipFile.OpenRead(corePackage);
        foreach(var n in new[]{"hud_armor","hud_helmet","hud_cs2_armor","hud_cs2_armor_helmet","Hits/helmet_spark"}){
            var entry=core.Entries.FirstOrDefault(e=>e.FullName=="Assets/Textures/ScCsgoKnives/"+n+".png")??core.Entries.First(e=>e.FullName=="Assets/Textures/ScCsgoKnives/"+n+".webp");
            using var input=entry.Open();using var bytes=new MemoryStream();input.CopyTo(bytes);bytes.Position=0;caches["Textures/ScCsgoKnives/"+n]=[Texture2D.Load(bytes)];
        }
        ScArmorState S(string text){ScArmorState.TryDecode(text,out var st);return st;}
        // deathmatch round 4 ("全部仿照CS2"): one badge - CS2's shield, or the shield with the helmet on it - and the value(s)
        // beside it (the CS world's two budgets together, the helmet's above; CS2's single value in a deathmatch); nothing
        // without protection; a used-up piece is gone
        var states=new (string Name,ScArmorReadout Readout,bool Shown,string Badge,string Values)[]{
            ("none",ScArmorReadout.Of(ScArmorState.None),false,null,""),("half",ScArmorReadout.Of(S("1|1,150,150|0,0,0")),true,"hud_cs2_armor","150"),
            ("full",ScArmorReadout.Of(S("1|1,150,150|1,100,100")),true,"hud_cs2_armor_helmet","100/150"),("full-body-spent",ScArmorReadout.Of(S("1|1,0,150|1,62,100")),true,"hud_helmet","62"),
            ("full-low",ScArmorReadout.Of(S("1|1,28,150|1,19,100,500")),true,"hud_cs2_armor_helmet","19/28"),("helmet-spent",ScArmorReadout.Of(S("1|1,70,150|1,0,100")),true,"hud_cs2_armor","70"),
            ("cs2-kevlar-helmet",ScArmorReadout.Cs2(85,true),true,"hud_cs2_armor_helmet","85"),("cs2-kevlar",ScArmorReadout.Cs2(40,false),true,"hud_cs2_armor","40"),("cs2-none",ScArmorReadout.Cs2(0,true),false,null,"")};
        foreach(var size in new[]{new Vector2(960,540),new Vector2(360,640),new Vector2(1920,1080)}){
            foreach(var (name,readout,shown,badge,values) in states){
                var root=new CanvasWidget{Size=size};var hud=new ScArmorHud();ScUiSettings.ArmorHud=ScArmorHud.DefaultPosition();
                for(int i=0;i<2;i++){hud.ShowIn(root,readout);root.Measure(size);root.Arrange(Vector2.Zero,size);}
                Capture(root,size,"armor-hud-"+name);
                string got=hud.Single.IsVisible?hud.Single.Text:hud.Upper.IsVisible?hud.Upper.Text+"/"+hud.Lower.Text:"";
                bool fits=hud.Panel.IsVisible==shown&&(!shown||hud.Badge.Texture==badge&&got==values);
                Check("armor HUD (CS2 readout) "+name+" "+size+(fits?"":$" :: shown {hud.Panel.IsVisible} badge {hud.Badge.Texture} values {got}"),fits);
            }
            var custom=new CanvasWidget{Size=size};var turned=new ScArmorHud();ScUiSettings.ArmorHud=new ScHudPosition{Custom=true,X=.5f,Y=.2f,Scale=1.6f,Rotation=MathF.PI/18};
            for(int i=0;i<2;i++){turned.ShowIn(custom,ScArmorReadout.Of(S("1|1,120,150|1,40,100")));custom.Measure(size);custom.Arrange(Vector2.Zero,size);}
            Capture(custom,size,"armor-hud-custom-scaled-turned");ScUiSettings.ArmorHud=ScArmorHud.DefaultPosition();
        }
        Check("armor HUD position apart from the ammo HUD",!ReferenceEquals(ScUiSettings.ArmorHud,ScUiSettings.AmmoHud));
        // The layout editor with the protection HUD selected.
        foreach(var size in new[]{new Vector2(960,540),new Vector2(360,640)}){
            ResetScreens();var editor=new ScGunLayoutScreen{WidgetsHierarchyInput=new WidgetInput()};editor.Enter([]);Layout(editor,size);
            typeof(ScGunLayoutScreen).GetField("m_selected",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(editor,ScGunLayoutScreen.ArmorHudId);
            typeof(ScGunLayoutScreen).GetMethod("LoadSelected",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(editor,null);Layout(editor,size);
            Check("layout editor shows the armor HUD "+size,((Widget)Field(editor,"m_armorProxy")).IsVisible&&!((Widget)Field(editor,"m_hudProxy")).IsVisible);
            Capture(editor,size,"layout-armor-hud");
        }
        // The helmet spark, simulated and drawn by the real particle system (onlooker 3 m away; the own hit in front of the eye).
        foreach(var (view,own,eyeAt) in new[]{("onlooker",false,new Vector3(3,1.7f,0)),("own-hit",true,new Vector3(0,1.62f,0))}){
            var size=new Vector2(960,540);var point=new Vector3(0,1.62f,0);var incoming=own?new Vector3(0,0,1):new Vector3(0,0,-1);
            var camera=own?new FixtureCamera(eyeAt,eyeAt+new Vector3(0,0,-1),size):new FixtureCamera(eyeAt,point,size);
            var spark=(ScHelmetSpark)Activator.CreateInstance(typeof(ScHelmetSpark),BindingFlags.NonPublic|BindingFlags.Instance,null,[caches["Textures/ScCsgoKnives/Hits/helmet_spark"][0],point,incoming,own],null);
            var particles=Blank<SubsystemParticles>();particles.PrimitivesRenderer=new PrimitivesRenderer3D();spark.SubsystemParticles=particles;
            float t=0;foreach(float at in new[]{.02f,.06f,.12f}){while(t<at){spark.Simulate(.01f);t+=.01f;}
                using var target=new RenderTarget2D((int)size.X,(int)size.Y,1,ColorFormat.Rgba8888,DepthFormat.Depth24Stencil8);
                Display.RenderTarget=target;Display.Viewport=new(0,0,(int)size.X,(int)size.Y);Display.ScissorRectangle=new(0,0,(int)size.X,(int)size.Y);Display.Clear(new Color(58,64,72),1,0);
                spark.Draw(camera);particles.PrimitivesRenderer.Flush(camera.ViewProjectionMatrix);
                using(var file=File.Create(Path.Combine(output,$"helmet-spark-{view}-{at*1000:0}ms.png")))RenderTarget2D.Save(target,file,ImageFileFormat.Png,false);Display.RenderTarget=null;}
            Check("spark ends within 0.35 s "+view,Enumerable.Range(0,40).Any(_=>spark.Simulate(.01f)));
        }
    }

    // ---- F2 damage-direction HUD: real Draw into an offscreen target, one mark per sector plus a merged pair. ----
    foreach(var size in new[]{new Vector2(960,540),new Vector2(360,640)}){
        var player=Blank<ComponentPlayer>();player.ComponentHealth=new ComponentHealth{Health=1};
        var camera=new FixtureCamera(new Vector3(0,1.6f,0),new Vector3(0,1.6f,-1),size);
        using var target=new RenderTarget2D((int)size.X,(int)size.Y,1,ColorFormat.Rgba8888,DepthFormat.Depth24Stencil8);
        void Hud(string name,double now){
            Display.RenderTarget=target;Display.Viewport=new(0,0,(int)size.X,(int)size.Y);Display.ScissorRectangle=new(0,0,(int)size.X,(int)size.Y);Display.Clear(new Color(70,76,84),1,0);
            var renderer=new PrimitivesRenderer2D();ScDamageIndicator.Draw(renderer,camera,player,now);
            using(var file=File.Create(Path.Combine(output,$"{name}-{size.X}x{size.Y}.png")))RenderTarget2D.Save(target,file,ImageFileFormat.Png,false);Display.RenderTarget=null;
        }
        ScDamageIndicator.Report(player,new Vector3(0,0,-1),10);ScDamageIndicator.Report(player,new Vector3(1,0,0),10);
        Hud("damage-front-right-fresh",10);Hud("damage-front-right-fading",10.5);
        ScDamageIndicator.Clear(player);
        // R3: a real hit from behind beside the weaker creative-mode preview from the left.
        ScDamageIndicator.Report(player,new Vector3(0,0,1),10);ScDamageIndicator.ReportWith(player,new Vector3(-1,0,0),10,ScDamageIndicator.PreviewStrength);
        Hud("damage-back-real-left-creative-preview",10);
        Check("creative preview is the weaker mark "+size,ScDamageIndicator.Intensities(player,camera.ViewDirection,10) is {} mix&&mix[2]>.99f&&Math.Abs(mix[3]-ScDamageIndicator.PreviewStrength)<1e-3f);
        ScDamageIndicator.Clear(player);
        foreach(var from in new[]{new Vector3(0,0,-1),new Vector3(1,0,0),new Vector3(0,0,1),new Vector3(-1,0,0)})ScDamageIndicator.Report(player,from,20);
        Hud("damage-all-sectors",20);
        Check("four sectors lit "+size,ScDamageIndicator.Intensities(player,camera.ViewDirection,20) is {} a&&a.All(v=>v>.99f));
        Check("marks expire after 0.7 s "+size,ScDamageIndicator.Intensities(player,camera.ViewDirection,20.71)==null);
        ScDamageIndicator.Clear(player);
    }

    // ---- F4/R4 throw preview: real terrain, the live Launch/Predict, drawn by the real Draw. Bright and dark scenes,
    // the thrower's own eye, a side witness and a view from above; one case at three resolutions. ----
    {
        var project=new Project();var terrain=new SubsystemTerrain{Terrain=new Terrain()};terrain.m_project=project;project.m_subsystems.Add(terrain);
        for(int cx=-1;cx<=3;cx++)for(int cz=-2;cz<=1;cz++)terrain.Terrain.AllocateChunk(cx,cz).State=TerrainChunkState.Valid;
        var solidCells=new List<Point3>();void Put(int x,int y,int z){terrain.Terrain.SetCellValueFast(x,y,z,2);solidCells.Add(new(x,y,z));}
        for(int x=-4;x<=40;x++)for(int z=-12;z<=12;z++)Put(x,59,z);
        for(int y=60;y<=63;y++)for(int z=-12;z<=12;z++)Put(24,y,z); // wall 24 m ahead
        TerrainRaycastResult? Solid(Vector3 a,Vector3 b)=>terrain.Raycast(a,b,false,true,(v,_)=>BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v));
        bool Water(Vector3 p)=>false;
        var eye=new Vector3(.5f,61.62f,.5f);
        var scenes=new[]{("night",new Color(22,27,34),new Color(96,104,112),new Color(86,94,102)),("day-sand",new Color(150,200,245),new Color(226,210,154),new Color(214,198,142)),("day-grass",new Color(150,200,245),new Color(104,158,76),new Color(94,148,68))};
        var cases=new (string Name,int Kind,bool Low,Vector3 Direction,float Loaded)[]{("he-strong-level",0,false,new Vector3(1,.12f,.05f),1000),("smoke-weak-down",2,true,new Vector3(1,-.25f,-.1f),1000),
            ("molotov-strong-high",3,false,new Vector3(1,.45f,.15f),1000),("flash-strong-wall",1,false,new Vector3(1,.02f,0),1000),("he-strong-up-airburst",0,false,new Vector3(.5f,1,0),1000),("he-strong-cut-at-unloaded",0,false,new Vector3(1,.3f,0),9)};
        // current-direction-20260929 §5: the same predicted paths drawn three ways from the thrower's eye (presentation only).
        var variants=new (string Name,ScGrenadeTrajectory.HandPresentation Look)[]{("a-current",ScGrenadeTrajectory.HandPresentation.Current),("b-no-offset",ScGrenadeTrajectory.HandPresentation.None),
            ("c-short-join",new(ScGrenadeTrajectory.HandRight,ScGrenadeTrajectory.HandDown,.2f,1.5f,5)),
            ("d-full-join",new(ScGrenadeTrajectory.HandRight,ScGrenadeTrajectory.HandDown,1,0,1000))};
        foreach(var (name,kind,low,dir,strafe) in new[]{("he-strong-level",0,false,new Vector3(1,.12f,.05f),Vector3.Zero),("smoke-weak-down",2,true,new Vector3(1,-.25f,-.1f),Vector3.Zero),
                ("molotov-strong-high",3,false,new Vector3(1,.45f,.15f),Vector3.Zero),("he-strong-strafing",0,false,new Vector3(1,.2f,0),new Vector3(0,0,4))}){
            var d=Vector3.Normalize(dir);var launch=ScGrenadeBallistics.Launch(eye,d,strafe,low,(a,b)=>Solid(a,b)?.HitPoint());
            var path=ScGrenadeTrajectory.Predict(kind,launch.Position,launch.Velocity,Solid,Water,q=>true);
            File.AppendAllText(Path.Combine(output,"preview-ab.txt"),$"{name}: release {launch.Position} first rebound {(path.Corners.Count>0?path.Points[path.Corners[0]]:path.EndPoint)} end {path.EndPoint} ({path.Kind}, grounded {path.Grounded}) points {path.Points.Count}\n");
            foreach(var (variant,look) in variants)foreach(var size in new[]{new Vector2(1920,1080),new Vector2(960,540)}){
                ScGrenadeTrajectory.Presentation=look;
                try{
                    var camera=new FixtureCamera(eye,eye+d,size);
                    using var target=new RenderTarget2D((int)size.X,(int)size.Y,1,ColorFormat.Rgba8888,DepthFormat.Depth24Stencil8);
                    Display.RenderTarget=target;Display.Viewport=new(0,0,(int)size.X,(int)size.Y);Display.ScissorRectangle=new(0,0,(int)size.X,(int)size.Y);Display.Clear(new Color(150,200,245),1,0);
                    var ground=new PrimitivesRenderer3D();var batch=ground.FlatBatch(0,DepthStencilState.Default,RasterizerState.CullNone,BlendState.Opaque);
                    foreach(var c in solidCells){var a0=new Vector3(c.X,c.Y,c.Z);var b0=a0+Vector3.One;var top=((c.X+c.Z)&1)==0?new Color(226,210,154):new Color(214,198,142);var wall=new Color((byte)(top.R*.7f),(byte)(top.G*.7f),(byte)(top.B*.7f));
                        batch.QueueQuad(new(a0.X,b0.Y,a0.Z),new(b0.X,b0.Y,a0.Z),new(b0.X,b0.Y,b0.Z),new(a0.X,b0.Y,b0.Z),top);
                        batch.QueueQuad(new(a0.X,a0.Y,a0.Z),new(a0.X,a0.Y,b0.Z),new(a0.X,b0.Y,b0.Z),new(a0.X,b0.Y,a0.Z),wall);batch.QueueQuad(new(a0.X,a0.Y,b0.Z),new(b0.X,a0.Y,b0.Z),new(b0.X,b0.Y,b0.Z),new(a0.X,b0.Y,b0.Z),wall);}
                    ground.Flush(camera.ViewProjectionMatrix);
                    var renderer=new PrimitivesRenderer3D();ScGrenadeTrajectory.Draw(renderer,camera,path);
                    using(var file=File.Create(Path.Combine(output,$"preview-ab-{name}-{variant}-{size.X}x{size.Y}.png")))RenderTarget2D.Save(target,file,ImageFileFormat.Png,false);Display.RenderTarget=null;
                }finally{ScGrenadeTrajectory.Presentation=ScGrenadeTrajectory.HandPresentation.Current;}
            }
        }
        foreach(var (name,kind,low,dir,loadedTo) in cases){
            bool Loaded(Vector3 q)=>q.X<loadedTo&&terrain.Terrain.GetChunkAtCell(Terrain.ToCell(q.X),Terrain.ToCell(q.Z)) is {State:>TerrainChunkState.InvalidContents4};
            var d=Vector3.Normalize(dir);var launch=ScGrenadeBallistics.Launch(eye,d,Vector3.Zero,low,(a,b)=>Solid(a,b)?.HitPoint());
            var path=ScGrenadeTrajectory.Predict(kind,launch.Position,launch.Velocity,Solid,Water,Loaded);
            File.AppendAllText(Path.Combine(output,"preview.txt"),$"{name}: kind {kind} low {low} end {path.Kind} at {path.EndPoint} after {path.Time:0.00}s bounces {path.Bounces} points {path.Points.Count} corners {path.Corners.Count} grounded {path.Grounded}\n");
            Check("preview has a path "+name,path.Points.Count>=2&&path.Points.Count<=ScGrenadeTrajectory.MaxPoints+1);
            foreach(var (scene,sky,ground0,ground1) in scenes){
                if(scene!="day-sand"&&name is not ("he-strong-level" or "smoke-weak-down"))continue;
                foreach(var (view,size) in new[]{("eye",new Vector2(960,540)),("witness",new Vector2(960,540)),("top",new Vector2(960,540)),("eye-1080p",new Vector2(1920,1080)),("eye-360p",new Vector2(640,360))}){
                    if(view.StartsWith("eye-")&&(name!="he-strong-level"||scene!="day-sand"))continue;
                    var mid=(launch.Position+path.EndPoint)*.5f;
                    var camera=view=="witness"?new FixtureCamera(eye+new Vector3(-3.5f,2.2f,5.5f),eye+new Vector3(9,-1.5f,0),size)
                        :view=="top"?new FixtureCamera(new Vector3(mid.X,mid.Y+22,mid.Z+.01f),mid,size):new FixtureCamera(eye,eye+d,size);
                    using var target=new RenderTarget2D((int)size.X,(int)size.Y,1,ColorFormat.Rgba8888,DepthFormat.Depth24Stencil8);
                    Display.RenderTarget=target;Display.Viewport=new(0,0,(int)size.X,(int)size.Y);Display.ScissorRectangle=new(0,0,(int)size.X,(int)size.Y);Display.Clear(sky,1,0);
                    var ground=new PrimitivesRenderer3D();var batch=ground.FlatBatch(0,DepthStencilState.Default,RasterizerState.CullNone,BlendState.Opaque);
                    foreach(var c in solidCells){var a=new Vector3(c.X,c.Y,c.Z);var b=a+Vector3.One;var top=((c.X+c.Z)&1)==0?ground0:ground1;var wall=new Color((byte)(top.R*.7f),(byte)(top.G*.7f),(byte)(top.B*.7f));
                        batch.QueueQuad(new(a.X,b.Y,a.Z),new(b.X,b.Y,a.Z),new(b.X,b.Y,b.Z),new(a.X,b.Y,b.Z),top);
                        batch.QueueQuad(new(a.X,a.Y,a.Z),new(a.X,a.Y,b.Z),new(a.X,b.Y,b.Z),new(a.X,b.Y,a.Z),wall);batch.QueueQuad(new(a.X,a.Y,b.Z),new(b.X,a.Y,b.Z),new(b.X,b.Y,b.Z),new(a.X,b.Y,b.Z),wall);}
                    ground.Flush(camera.ViewProjectionMatrix);
                    var renderer=new PrimitivesRenderer3D();ScGrenadeTrajectory.Draw(renderer,camera,path);
                    using(var file=File.Create(Path.Combine(output,$"preview-{name}-{scene}-{view}.png")))RenderTarget2D.Save(target,file,ImageFileFormat.Png,false);Display.RenderTarget=null;
                }
            }
        }
    }
}catch(Exception e){Console.Error.WriteLine(e);failures.Add(e.GetType().Name+": "+e.Message);exit=1;}finally{Display.RenderTarget=null;Window.Close();}};
Window.Run(480,320,WindowMode.Fixed,"Followup UI diagnostic (isolated)");
File.WriteAllText(Path.Combine(output,"followup.json"),JsonSerializer.Serialize(new{failures,checks,settingsFile,
    scope="Native offline UI/HUD/preview renders with isolated settings; not a game screenshot"},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"followup checks={checks.Count} failures={failures.Count}");foreach(var f in failures)Console.WriteLine("FAIL "+f);
return failures.Count==0&&exit==0?0:1;

sealed class FixtureScreen:Screen{}
sealed class FixtureCamera:Camera {
    readonly Vector3 eye,forward,up,right;readonly Matrix view,projection;readonly Vector2 size;
    public FixtureCamera(Vector3 eye,Vector3 target,Vector2 size):base(null){
        this.eye=eye;this.size=size;forward=Vector3.Normalize(target-eye);right=Vector3.Normalize(Vector3.Cross(forward,Vector3.UnitY));up=Vector3.Cross(right,forward);
        view=Matrix.CreateLookAt(eye,target,Vector3.UnitY);projection=Matrix.CreatePerspectiveFieldOfView(1.1f,size.X/size.Y,.1f,200);
    }
    public override Vector3 ViewPosition=>eye;public override Vector3 ViewDirection=>forward;public override Vector3 ViewUp=>up;public override Vector3 ViewRight=>right;
    public override Matrix ViewMatrix=>view;public override Matrix InvertedViewMatrix=>Matrix.Invert(view);public override Matrix ProjectionMatrix=>projection;
    public override Matrix ScreenProjectionMatrix=>projection;public override Matrix InvertedProjectionMatrix=>Matrix.Invert(projection);
    public override Matrix ViewProjectionMatrix=>view*projection;public override Vector2 ViewportSize=>size;public override Matrix ViewportMatrix=>Matrix.Identity;
    public override BoundingFrustum ViewFrustum=>new(view*projection);public override bool UsesMovementControls=>false;public override bool IsEntityControlEnabled=>false;
    public override void Update(float dt){}
}
