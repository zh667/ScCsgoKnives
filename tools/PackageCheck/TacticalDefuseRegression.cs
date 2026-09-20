using System.Reflection;
using System.Runtime.CompilerServices;
using System.IO.Compression;
using System.Xml.Linq;
using Engine;
using Engine.Input;
using Engine.Graphics;
using Engine.Media;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

static class TacticalDefuseRegression {
    static T Blank<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    sealed class Audio:SubsystemAudio {public readonly List<string> Sounds=[];public override void PlaySound(string n,float v,float pitch,Vector3 p,float d,bool delay)=>Sounds.Add(n);}
    sealed class Ground:SubsystemTerrain {public bool Blocked;public override TerrainRaycastResult? Raycast(Vector3 a,Vector3 b,bool i,bool s,Func<int,float,bool> f)=>Blocked?new TerrainRaycastResult{Distance=.1f}:null;}
    sealed class Gui:ComponentGui {public readonly List<string> Messages=[];public override void DisplaySmallMessage(string text,Color c,bool b,bool s)=>Messages.Add(text);}
    internal static List<TacticalRegression.Result> Run(Assembly core,Assembly dlc,string content){
        var results=new List<TacticalRegression.Result>();
        Type T(string n)=>dlc.GetType("Game."+n,true);Type C(string n)=>core.GetType("Game."+n,true);
        void Check(bool b,string why){if(!b)throw new Exception(why);}
        var flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var window=typeof(Window).GetField("m_state",BindingFlags.Static|BindingFlags.NonPublic);var oldWindow=window.GetValue(null);
        var screen=ScreensManager.CurrentScreen;var root=ScreensManager.RootWidget;var animation=ScreensManager.m_animationData;float volume=SettingsManager.SoundsVolume;
        var caches=(IDictionary<string,List<object>>)typeof(ContentManager).GetField("Caches",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);var oldCaches=caches.ToArray();var atlas=TextureAtlasManager.m_subtextures.ToArray();var font=LabelWidget.m_bitmapFont;
        var keys=(Dictionary<string,string>)C("ScGunBindings").GetField("Keys").GetValue(null);var oldKeys=new Dictionary<string,string>(keys);var mappings=SettingsManager.KeyboardMappingSettings;
        int frame=Time.FrameIndex;var frameProperty=typeof(Time).GetProperty("FrameIndex");
        try{
            window.SetValue(null,Enum.Parse(window.FieldType,"Active"));ScreensManager.CurrentScreen=null;ScreensManager.RootWidget=new CanvasWidget();ScreensManager.m_animationData=null;SettingsManager.SoundsVolume=0;SettingsManager.KeyboardMappingSettings=null;keys["plant_c4"]="E";
            using var vanilla=ZipFile.OpenRead(content);var texture=Blank<Texture2D>();
            foreach(var e in vanilla.Entries.Where(e=>e.FullName.StartsWith("Assets/")&&e.FullName.Contains('.'))){string key=e.FullName[7..];key=key[..key.LastIndexOf('.')];if(e.FullName.EndsWith(".xml")){using var s=e.Open();var xml=XElement.Load(s);caches[key]=[xml];foreach(var a in xml.DescendantsAndSelf().Attributes().Where(a=>a.Value.StartsWith("{Textures/Atlas/")&&a.Value.EndsWith('}')))TextureAtlasManager.m_subtextures[a.Value[1..^1]]=new Subtexture(texture,Vector2.Zero,Vector2.One);}else if(e.FullName.EndsWith(".png"))caches[key]=[texture];}
            using(var glyphs=vanilla.Entries.Single(e=>e.FullName.EndsWith("Fonts/Pericles.lst")).Open())LabelWidget.BitmapFont=BitmapFont.Initialize((Texture2D)null,glyphs);caches["Fonts/Pericles"]=[LabelWidget.BitmapFont];
            foreach(string scenario in new[]{"hand","kit","release","move","wall","modal","death","lost-kit","late","tie","other-player","touch"}){
                dynamic bombs=null;
                try{
                    Keyboard.ProcessKeyUp(Key.E);Touch.Clear();
                    var p=new Project();var time=new SubsystemTime();var terrain=new Ground{Terrain=new Terrain()};var players=new SubsystemPlayers();var audio=new Audio();bombs=Activator.CreateInstance(T("SubsystemTacticalBombs"));
                    foreach(var s in new Subsystem[]{time,terrain,players,audio,new SubsystemBodies(),(Subsystem)bombs}){s.m_project=p;p.m_subsystems.Add(s);}bombs.Load(new ValuesDictionary());
                    var player=Blank<ComponentPlayer>();player.PlayerData=Blank<PlayerData>();player.ComponentBody=new ComponentBody();player.ComponentHealth=new ComponentHealth{Health=1};var gui=new Gui{m_modalPanelContainerWidget=new CanvasWidget(),ControlsContainerWidget=new CanvasWidget()};player.ComponentGui=gui;
                    var input=new WidgetInput(scenario=="touch"?WidgetInputDevice.Touch:WidgetInputDevice.Keyboard);var widget=Blank<GameWidget>();widget.GuiWidget=new CanvasWidget{WidgetsHierarchyInput=input};widget.WidgetsHierarchyInput=new WidgetInput(input.Devices);player.PlayerData.m_gameWidget=widget;widget.GuiWidget.Children.Add(gui.ControlsContainerWidget);
                    var camera=new FppCamera(widget);camera.SetupPerspectiveCamera(new Vector3(0,1.5f,0),Vector3.Normalize(new Vector3(0,-1.4f,-1)),Vector3.UnitY);widget.m_activeCamera=camera;
                    var inv=new ComponentInventory();for(int i=0;i<36;i++)inv.m_slots.Add(new());player.ComponentMiner=new ComponentMiner{Inventory=inv};bool kit=scenario is "kit" or "lost-kit";if(kit)inv.m_slots[25]=new(){Value=707,Count=1};
                    var entity=Blank<Entity>();entity.m_project=p;entity.m_components=[player,gui,player.ComponentBody,player.ComponentHealth,player.ComponentMiner,inv];foreach(var c in entity.m_components)c.m_entity=entity;players.m_componentPlayers.Add(player);
                    dynamic bomb=Activator.CreateInstance(T("SubsystemTacticalBombs").GetNestedType("Bomb"));bomb.Charge.Position=new Vector3(0,.1f,-1);bomb.Charge.Remaining=scenario=="late"?8:scenario=="tie"?10:40;bomb.Charge.BeepLeft=99f;bombs.Bombs.Add(bomb);
                    void Update(double now){time.m_gameTime=now;frameProperty.SetValue(null,Time.FrameIndex+1);((IUpdateable)bombs).Update(.016f);}
                    Update(0);Check(ReferenceEquals((object)bombs.Target(player),(object)bomb),"native camera target missed bomb");
                    var h=((System.Collections.IDictionary)T("SubsystemTacticalBombs").GetField("hud",flags).GetValue((object)bombs))[player];var panel=(StackPanelWidget)h.GetType().GetField("Panel").GetValue(h);var button=(BevelledButtonWidget)h.GetType().GetField("Button").GetValue(h);var label=(LabelWidget)h.GetType().GetField("Label").GetValue(h);
                    foreach(var size in new[]{new Vector2(420,360),new Vector2(480,540),new Vector2(850,480)}){widget.GuiWidget.Measure(size);widget.GuiWidget.Arrange(Vector2.Zero,size);widget.GuiWidget.Measure(size);widget.GuiWidget.Arrange(Vector2.Zero,size);Check(panel.GlobalBounds.Min.X>=0&&panel.GlobalBounds.Min.Y>=0&&panel.GlobalBounds.Max.X<=size.X+.1f&&panel.GlobalBounds.Max.Y<=size.Y+.1f,"defuse HUD overflows viewport "+size);}
                    if(scenario=="late")Check(label.Text.Contains("时间不足")&&gui.Messages.Count==1,"insufficient time lacks warning");
                    if(scenario=="other-player"){
                        bomb.Defuser=Blank<ComponentPlayer>();bomb.Clock=(dynamic)Activator.CreateInstance(T("TacticalDefuseClock"),new object[]{false});Update(.1); // departed owner is cancelled before a new operation
                        Check(bomb.Defuser==null,"departed defuser locks bomb");
                    }
                    Vector2 touch=button.GlobalBounds.Center();
                    if(scenario=="touch"){Check(button.IsVisible&&button.ActualSize.Y>=48,"mobile button missing/unreadable");Touch.ProcessTouchPressed(101,new Vector2(3,3));Touch.ProcessTouchPressed(102,touch);}else Keyboard.ProcessKeyDown(Key.E);
                    frameProperty.SetValue(null,Time.FrameIndex+1);Check((bool)bombs.Blocking(player),"defuse does not reserve weapon action on press");
                    var playerInput=new ComponentInput{m_componentPlayer=player};playerInput.m_entity=entity;playerInput.m_playerInput=new PlayerInput{ToggleInventory=true,Hit=new Ray3(Vector3.Zero,Vector3.UnitZ),Aim=new Ray3(Vector3.Zero,Vector3.UnitZ)};
                    if(scenario!="touch"){bombs.Input(playerInput);Check(!playerInput.m_playerInput.ToggleInventory&&!playerInput.m_playerInput.Hit.HasValue&&!playerInput.m_playerInput.Aim.HasValue,"E simultaneously opens inventory/fires");}
                    Update(0);Check(bomb.Defuser==player&&bomb.Clock.Duration==(kit?5f:10f),"press failed to start correct timer");
                    Update(1);
                    if(scenario=="release")Keyboard.ProcessKeyUp(Key.E);
                    if(scenario=="move")player.ComponentBody.Position=Vector3.UnitX;
                    if(scenario=="wall")terrain.Blocked=true;
                    if(scenario=="modal")gui.m_modalPanelContainerWidget.Children.Add(new CanvasWidget());
                    if(scenario=="death")player.ComponentHealth.Health=0;
                    if(scenario=="lost-kit")inv.m_slots[25].Count=0;
                    bool cancel=scenario is "release" or "move" or "wall" or "modal" or "death" or "lost-kit";
                    Update(2);
                    if(cancel){Check(bomb.Defuser==null&&bomb.Clock==null&&bombs.Bombs.Count==1,"invalid operation not cancelled");player.ComponentBody.Position=Vector3.Zero;terrain.Blocked=false;gui.m_modalPanelContainerWidget.Children.Clear();player.ComponentHealth.Health=1;Update(2.1);Check(bomb.Defuser==null,"held input auto-restarted after cancel");}
                    else{Update(scenario=="kit"?5:11);Check(bombs.Bombs.Count==0,"defuse/explosion did not finalize");bool expired=scenario is "late" or "tie";Check(audio.Sounds.Count(s=>s.EndsWith("c4_explode_close_01"))==(expired?1:0)&&audio.Sounds.Count(s=>s.EndsWith("c4_disarmfinish"))==(expired?0:1),"wrong or double outcome");if(scenario!="touch")Check((bool)bombs.Blocking(player),"still-held defuse key leaks into new attack after success");}
                    Keyboard.ProcessKeyUp(Key.E);Touch.Clear();Update(12);Check(!(bool)bombs.Blocking(player),"released action remains locked");
                    results.Add(new("defuse-live/"+scenario,true,"native WidgetInput/camera, packaged Update; audio device muted"));
                }catch(Exception ex){results.Add(new("defuse-live/"+scenario,false,ex.ToString()));}
                finally{bombs?.Dispose();Keyboard.ProcessKeyUp(Key.E);Touch.Clear();}
            }
        }finally{
            window.SetValue(null,oldWindow);ScreensManager.CurrentScreen=screen;ScreensManager.RootWidget=root;ScreensManager.m_animationData=animation;SettingsManager.SoundsVolume=volume;SettingsManager.KeyboardMappingSettings=mappings;frameProperty.SetValue(null,frame);keys.Clear();foreach(var k in oldKeys)keys[k.Key]=k.Value;
            caches.Clear();foreach(var c in oldCaches)caches[c.Key]=c.Value;TextureAtlasManager.m_subtextures.Clear();foreach(var a in atlas)TextureAtlasManager.m_subtextures[a.Key]=a.Value;LabelWidget.m_bitmapFont=font;
        }
        return results;
    }
}
