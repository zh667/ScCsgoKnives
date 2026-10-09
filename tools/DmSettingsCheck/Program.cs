// Native widget layout/click tests against the stamped release DLLs; no player world or settings file.
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Game;
using GameEntitySystem;

Dispatcher.Initialize();
using var content=System.IO.Compression.ZipFile.OpenRead(args[0]);
var output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
var checks=new List<object>();int failed=0;bool done=false;
void Check(string name,bool ok){checks.Add(new{name,ok});if(!ok){failed++;Console.WriteLine("FAIL "+name);}}
T Blank<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
var caches=(IDictionary<string,List<object>>)typeof(ContentManager).GetField("Caches",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
foreach(var e in content.Entries.Where(e=>e.FullName.EndsWith(".xml"))){using var s=e.Open();caches[e.FullName.Replace("Assets/","")[..^4]]=[XElement.Load(s)];}
Window.Frame+=()=>{if(done)return;done=true;try{
    using(var glyph=content.Entries.Single(e=>e.FullName.EndsWith("Fonts/Pericles.lst")).Open())
    using(var texture=content.Entries.Single(e=>e.FullName.EndsWith("Fonts/Pericles.webp")).Open())LabelWidget.BitmapFont=BitmapFont.Initialize(texture,glyph);
    caches["Fonts/Pericles"]=[LabelWidget.BitmapFont];
    using(var tex=content.Entries.Single(e=>e.FullName.EndsWith("Atlases/AtlasTexture.webp")).Open())
    using(var list=new StreamReader(content.Entries.Single(e=>e.FullName.EndsWith("Atlases/Atlas.txt")).Open()))TextureAtlasManager.LoadAtlases(Texture2D.Load(tex),list.ReadToEnd());
    using(var s=content.Entries.Single(e=>e.FullName.EndsWith("Textures/Gui/Panorama.webp")).Open())caches[PanoramaWidget.TexturePath]=[Texture2D.Load(s)];
    ScreensManager.RootWidget=new CanvasWidget{WidgetsHierarchyInput=new WidgetInput()};
    var back=new FixtureScreen();ScreensManager.AddScreen("Settings",back);var game=new FixtureScreen();ScreensManager.AddScreen("Game",game);
    var animation=typeof(ScreensManager).GetField("m_animationData",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
    void ResetScreens(){ScreensManager.CurrentScreen=null;ScreensManager.PreviousScreen=null;animation.SetValue(null,null);}
    object Field(object o,string name)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);
    void Layout(Screen s,Vector2 size){for(int i=0;i<3;i++){s.Measure(size);s.Arrange(Vector2.Zero,size);}s.Update();s.Measure(size);s.Arrange(Vector2.Zero,size);}
    ScGunSettingsScreen Open(Vector2 size){ResetScreens();ScreensManager.PreviousScreen=back;var s=new ScGunSettingsScreen{WidgetsHierarchyInput=new WidgetInput()};s.Enter([]);Layout(s,size);return s;}
    void Click(ScGunSettingsScreen s,string field){var b=(BevelledButtonWidget)Field(s,field);b.m_clickableWidget.IsClicked=true;s.Update();b.m_clickableWidget.IsClicked=false;}
    var request=typeof(DmHud).GetField("s_menuRequested",BindingFlags.Static|BindingFlags.NonPublic);
    var take=typeof(DmHud).GetMethod("TakeMenuRequest",BindingFlags.Static|BindingFlags.NonPublic);
    ScUiSettings.ResetAll();
    var loader=new DeathmatchModLoader();loader.__ModInitialize();
    Check("addon-registers-CS-settings-provider",ScDeathmatchSettings.Open?.Method==typeof(DeathmatchModLoader).GetMethod("OpenSettings"));
    Check("no-game-settings-override",typeof(DeathmatchModLoader).GetMethod("OnSettingsScreenCreated").DeclaringType==typeof(ModLoader));
    Check("no-game-settings-hook-registered",!ModsManager.m_tempModHooks.TryGetValue("OnSettingsScreenCreated",out var hooks)||!hooks.UnorderedItems.Any(i=>ReferenceEquals(i.Element,loader)));
    foreach(var size in new[]{new Vector2(320,568),new Vector2(360,640),new Vector2(640,360),new Vector2(850,479)}){
        GameManager.m_project=null;ScDeathmatchSettings.ResetProvider();
        var absent=Open(size);var absentButton=(ButtonWidget)Field(absent,"m_deathmatch");
        Check("without-addon-no-entry "+size,absentButton.ParentWidget is null);
        absent.Leave();
        loader.__ModInitialize();var screen=Open(size);var button=(BevelledButtonWidget)Field(screen,"m_deathmatch");
        Check("with-addon-entry-exists-on-CS-page "+size,screen.AllChildren.OfType<BevelledButtonWidget>().Count(b=>b.Text=="死亡竞赛设置")==1);
        var scroll=(ScrollPanelWidget)Field(screen,"m_scroll");
        scroll.ScrollPosition=Math.Max(0,button.GlobalBounds.Min.Y-scroll.GlobalBounds.Min.Y+scroll.ScrollPosition-12);Layout(screen,size);
        Check("entry-reachable-and-touch-sized "+size,button.ActualSize.Y>=48&&button.GlobalBounds.Min.X>=0&&button.GlobalBounds.Max.X<=size.X+1
            &&button.GlobalBounds.Min.Y>=scroll.GlobalBounds.Min.Y-1&&button.GlobalBounds.Max.Y<=scroll.GlobalBounds.Max.Y+1);
        request.SetValue(null,false);bool before=ScUiSettings.CustomButtons;
        ((CheckboxWidget)Field(screen,"m_buttons")).IsChecked=!before;
        ScreensManager.CurrentScreen=screen;Click(screen,"m_deathmatch");
        Check("outside-world-stays-on-settings "+size,ReferenceEquals(ScreensManager.CurrentScreen,screen)&&!(bool)request.GetValue(null));
        Check("outside-world-explains-required-world "+size,screen.AllChildren.OfType<LabelWidget>().Any(l=>l.Text?.Contains("进入一个世界后") is true));
        Check("entry-does-not-save-unrelated-settings "+size,ScUiSettings.CustomButtons==before);
        DialogsManager.HideAllDialogs();screen.Leave();
        GameManager.m_project=new Project();var dm=new SubsystemScDeathmatch();dm.m_project=GameManager.Project;GameManager.Project.m_subsystems.Add(dm);
        screen=Open(size);ScreensManager.CurrentScreen=screen;request.SetValue(null,false);Click(screen,"m_deathmatch");
        Check("world-entry-returns-to-game "+size,ReferenceEquals(ScreensManager.CurrentScreen,game)&&(bool)request.GetValue(null));
        Check("opening-entry-does-not-enable-arena "+size,!dm.Enabled);
        var player=Blank<ComponentPlayer>();
        Check("request-consumed-once-by-local-player "+size,(bool)take.Invoke(null,[player])&&!(bool)take.Invoke(null,[player]));
        screen.Leave();GameManager.m_project=null;
    }
    ScDeathmatchSettings.ResetProvider();Check("reload-clears-provider",ScDeathmatchSettings.Open is null);
    loader.__ModInitialize();Check("reload-reregisters-provider",ScDeathmatchSettings.Open is not null);
}catch(Exception e){failed++;checks.Add(new{name="fixture-completed",ok=false,error=e.ToString()});Console.WriteLine(e);}
finally{GameManager.m_project=null;Window.Close();}};
Window.Run(480,320,WindowMode.Fixed,"Deathmatch settings check (isolated)");
string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
File.WriteAllText(Path.Combine(output,"settings.json"),JsonSerializer.Serialize(new{failed,checks,
    core=Hash(typeof(ScGunSettingsScreen).Assembly.Location),deathmatch=Hash(typeof(DeathmatchModLoader).Assembly.Location),
    scope="Native API 1.9.3.1 widget construction/layout/clicks in an isolated diagnostic; no gameplay, user settings writes or original world"},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Settings checks: {checks.Count} total, {failed} failed");return failed==0?0:1;
sealed class FixtureScreen:Screen{}
