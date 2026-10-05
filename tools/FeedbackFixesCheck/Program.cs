using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Engine;
using Engine.Input;
using Engine.Audio;
using Engine.Graphics;
using Engine.Media;
using Game;
using GameEntitySystem;

string package=Path.GetFullPath(args[0]),contentPath=Path.GetFullPath(args[1]),output=Path.GetFullPath(args[2]);
Directory.CreateDirectory(output);Dispatcher.Initialize();
var checks=new List<string>();int failed=0;void Check(string n,bool ok){if(!ok)throw new Exception(n);checks.Add(n);}
T Blank<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
using var content=System.IO.Compression.ZipFile.OpenRead(contentPath);
using var zip=System.IO.Compression.ZipFile.OpenRead(package);
var caches=(IDictionary<string,List<object>>)typeof(ContentManager).GetField("Caches",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
foreach(var entry in content.Entries.Where(e=>e.FullName.EndsWith(".xml"))){using var s=entry.Open();caches[entry.FullName.Replace("Assets/","")[..^4]]=[XElement.Load(s)];}
Check("fire fuse starts with a three-second budget",ScGrenadeBallistics.Fuse(3)==3&&ScGrenadeBallistics.Fuse(4)==3&&ScGrenadeBallistics.Fuse(0)==1.5f);
Check("enemy day 30 elapsed boundary",!ScEnemySpawnPolicy.Allows(true,35999,1200)&&ScEnemySpawnPolicy.Allows(true,36000,1200)&&!ScEnemySpawnPolicy.Allows(false,999999,1200));
foreach(var area in new[]{new Vector2(850,479),new Vector2(360,640),new Vector2(2400,1080)})
foreach(float x in new[]{0f,.5f,1f})foreach(float y in new[]{0f,.5f,1f}){
    var p=new ScHudPosition{Custom=true,X=x,Y=y};var size=new Vector2(116,76);var at=p.Position(area,size);
    Check("HUD clamped "+area+x+y,at.X>=0&&at.Y>=0&&at.X+size.X<=area.X&&at.Y+size.Y<=area.Y);
}
foreach(float age in new[]{0f,.1f,.4f,.8f}){
    var burst=ScGrenadeVisuals.FireBurst(new(0,30,0),age,5);
    Check("airburst above unsupported ground "+age,burst.Count>0&&burst.All(s=>s.Texture==4&&s.Position.Y>=30));
}
Check("airburst expires",ScGrenadeVisuals.FireBurst(Vector3.Zero,1,0).Count==0);
ScUiSettings.ResetAll();Check("safe old-settings defaults",!ScUiSettings.AmmoHud.Custom&&ScUiSettings.NaturalEnemies);
var oldHud=JsonSerializer.Deserialize<ScHudPosition>("{\"Custom\":true,\"X\":0.2,\"Y\":0.6}");
Check("old HUD JSON gains identity scale and rotation",oldHud.Scale==1&&oldHud.Rotation==0&&oldHud.X==.2f);
foreach(float rotation in new[]{0f,.5f,1.57f,3.14f})foreach(float scale in new[]{.5f,1f,2.5f}){
    var h=new ScHudPosition{Custom=true,X=0,Y=1,Scale=scale,Rotation=rotation};var size=new Vector2(116,76);var area=new Vector2(360,640);var center=h.Position(area,size)+size/2;var half=h.Extent(size)/2;
    Check("rotated scaled HUD stays on screen "+rotation+scale,center.X-half.X>=-.001f&&center.Y-half.Y>=-.001f&&center.X+half.X<=area.X+.001f&&center.Y+half.Y<=area.Y+.001f);
}
bool done=false;Window.Frame+=()=>{if(done)return;done=true;try{
    using(var glyph=content.Entries.Single(e=>e.FullName.EndsWith("Fonts/Pericles.lst")).Open())
    using(var texture=content.Entries.Single(e=>e.FullName.EndsWith("Fonts/Pericles.webp")).Open())LabelWidget.BitmapFont=BitmapFont.Initialize(texture,glyph);
    caches["Fonts/Pericles"]=[LabelWidget.BitmapFont];
    using(var s=content.Entries.Single(e=>e.FullName.EndsWith("Textures/Gui/Panorama.webp")).Open())caches[PanoramaWidget.TexturePath]=[Texture2D.Load(s)];
    using(var s=content.Entries.Single(e=>e.FullName.EndsWith("Textures/FireParticle.webp")).Open())caches["Textures/FireParticle"]=[Texture2D.Load(s)];
    using(var tex=content.Entries.Single(e=>e.FullName.EndsWith("Atlases/AtlasTexture.webp")).Open())
    using(var r=new StreamReader(content.Entries.Single(e=>e.FullName.EndsWith("Atlases/Atlas.txt")).Open()))TextureAtlasManager.LoadAtlases(Texture2D.Load(tex),r.ReadToEnd());
    foreach(var (type,index) in new[]{(typeof(AirBlock),0),(typeof(ScKnifeBlock),700),(typeof(ScGunBlock),701),(typeof(ScGrenadeBlock),702)}){
        var block=(Block)Activator.CreateInstance(type);block.BlockIndex=index;BlocksManager.Blocks[index]=block;BlocksManager.BlockTypeToIndex[type]=index;
    }
    using(var icon=zip.Entries.Single(e=>e.FullName.Contains("/hud_weapon_m4a4.")).Open())caches["Textures/ScCsgoKnives/hud_weapon_m4a4"]=[Texture2D.Load(icon)];
    // Drive the real animation state while deploy is still running.
    var gui=Blank<ComponentGui>();gui.m_modalPanelContainerWidget=new CanvasWidget();gui.ControlsContainerWidget=new CanvasWidget();
    var widget=Blank<GameWidget>();widget.GuiWidget=new CanvasWidget();var pd=Blank<PlayerData>();pd.m_gameWidget=widget;
    var player=Blank<ComponentPlayer>();player.ComponentGui=gui;player.PlayerData=pd;player.ComponentMiner=Blank<ComponentMiner>();
    var inv=new ComponentCreativeInventory{OpenSlotsCount=10};inv.m_slots.Add(ScGrenadeBlock.Value(3));player.ComponentMiner.Inventory=inv;
    var entity=Blank<Entity>();player.m_entity=entity;
    var model=Blank<ComponentFirstPersonModel>();model.m_componentPlayer=player;entity.m_components=[model];model.m_entity=entity;
    KnifeClock.Virtual=true;KnifeClock.VirtualNow=0;SettingsManager.SoundsVolume=0;
    KnifeAnimationController.Update(model,inv.GetSlotValue(0));
    Check("grenade really is drawing",KnifeAnimationController.IsGrenadeDrawing(model)&&KnifeAnimationController.IsBusy(model));
    Check("deploy permits throw immediately",KnifeAnimationController.CanStartGrenade(model,inv.GetSlotValue(0)));
    KnifeAnimationController.GrenadeAction(player,"pullpin");
    Check("throw action replaced deploy",!KnifeAnimationController.IsGrenadeDrawing(model));
    var pose=KnifeAnimationController.Update(model,inv.GetSlotValue(0));
    Check("next render does not restart deploy",pose.ClipAlias=="pullpin");
    var prepare=ScGrenadePreparation.Create(0,.4f,.2f,.8f,false,true);
    prepare.Step(.1,true);Check("holding input does not auto throw",!prepare.Throwing);
    prepare.Step(.45,false);Check("release still owns throw authorization",prepare.Throwing&&Math.Abs(prepare.ReleaseAt-.65)<1e-6);
    KnifeClock.Virtual=false;KnifeAnimationController.ClearSession();
    var fireProject=new Project();var fireAudio=new SubsystemAudio{m_subsystemTime=new SubsystemTime()};fireAudio.m_project=fireProject;fireProject.m_subsystems.Add(fireAudio);
    var grenades=new SubsystemScGrenades();grenades.m_project=fireProject;
    typeof(SubsystemScGrenades).GetField("m_terrain",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(grenades,new NoFloor());
    var airborne=new ScGrenadeState{Kind=3,Id=9,Position=new(0,30,0),Age=3,Remaining=0};
    var activeGrenades=(System.Collections.IList)typeof(SubsystemScGrenades).GetField("m_active",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(grenades);activeGrenades.Add(airborne);
    typeof(SubsystemScGrenades).GetMethod("Detonate",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(grenades,[airborne]);
    var bursts=(System.Collections.IList)typeof(SubsystemScGrenades).GetField("fireBursts",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(grenades);
    Check("unsupported air detonation emits visual but no floating damage zone",bursts.Count==1&&activeGrenades.Count==0);
    Check("air fuse uses only dedicated air sound",fireAudio.m_queuedSounds.Count==1&&fireAudio.m_queuedSounds[0].Name.EndsWith("grenade_fire_airburst"));
    foreach(int kind in new[]{3,4}){
        typeof(SubsystemScGrenades).GetField("m_terrain",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(grenades,new Floor());
        var landed=new ScGrenadeState{Kind=kind,Id=10+kind,Grounded=true,Position=new(0,.06f,0),Age=.5f};activeGrenades.Add(landed);
        int count=bursts.Count,sounds=fireAudio.m_queuedSounds.Count;
        typeof(SubsystemScGrenades).GetMethod("Detonate",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(grenades,[landed]);
        Check("ground contact has supported fire without air visual "+kind,landed.Effect&&bursts.Count==count);
        Check("ground contact plays only own impact sound "+kind,fireAudio.m_queuedSounds.Count==sounds+1&&fireAudio.m_queuedSounds[^1].Name.EndsWith(ScGrenadeBlock.Assets[kind]+"_explode"));
        typeof(SubsystemScGrenades).GetMethod("Detonate",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(grenades,[landed]);
        Check("ground fire cannot trigger detonation twice "+kind,fireAudio.m_queuedSounds.Count==sounds+1&&bursts.Count==count);
    }
    using(var soundStream=zip.Entries.Single(e=>e.FullName.Contains("grenade_fire_airburst.")).Open()){
        using var memory=new MemoryStream();soundStream.CopyTo(memory);memory.Position=0;var data=SoundData.Load(memory);
        Check("air sound decodes with native reader",data.Data.Length>0&&data.SamplingFrequency>=32000);
    }
    // Scope state rejects both direct and pending inspections for all six scopes.
    KnifeClock.Virtual=true;SettingsManager.SoundsVolume=0;
    foreach(var gun in GunSpec.All.Where(g=>g.ZoomLevels.Length>0)){
        KnifeAnimationController.ClearSession();int variant=Array.IndexOf(GunSpec.All,gun);inv.m_slots[0]=Terrain.MakeBlockValue(701,0,GunSpec.WithId(variant,0));
        KnifeClock.VirtualNow=0;KnifeAnimationController.Update(model,inv.GetSlotValue(0));KnifeClock.VirtualNow=20;KnifeAnimationController.Update(model,inv.GetSlotValue(0));
        KnifeAnimationController.SetScoped(player,true);
        Check("scope refuses inspect "+gun.Name,!KnifeAnimationController.TriggerInspect(player));
        Check("scope keeps idle pose "+gun.Name,!KnifeAnimationController.Update(model,inv.GetSlotValue(0)).ClipAlias.StartsWith("inspect"));
        KnifeAnimationController.SetScoped(player,false);Check("unscoped permits inspect "+gun.Name,KnifeAnimationController.TriggerInspect(player));
        KnifeAnimationController.SetScoped(player,true);Check("scope interrupts active inspect "+gun.Name,!KnifeAnimationController.Update(model,inv.GetSlotValue(0)).ClipAlias.StartsWith("inspect"));
    }
    KnifeClock.Virtual=false;KnifeAnimationController.ClearSession();
    var roleComponent=new FixtureAppearance();roleComponent.m_entity=entity;entity.m_components.Add(roleComponent);
    foreach(string role in new string[]{null,"","other","ct","t"}){roleComponent.FirstPersonRole=role;Check("empty hand ownership "+(role??"null"),TacticalArms.OwnsEmptyHands(model)==(role is "ct" or "t"));}
    // Native settings use a detached runner's application directory, never player's config.
    string settingsPath=Storage.GetSystemPath(ScUiSettings.Path);
    Check("settings fixture stays in task workspace",Path.GetFullPath(settingsPath).StartsWith(Path.GetFullPath(".tmp/feedback-fixes-130-20260927"),StringComparison.OrdinalIgnoreCase));
    ScUiSettings.AmmoHud=new(){Custom=true,X=.25f,Y=.7f};ScUiSettings.NaturalEnemies=false;
    Check("settings saved",ScUiSettings.Save());ScUiSettings.ResetAll();ScUiSettings.Load();
    Check("HUD and spawn switch roundtrip",ScUiSettings.AmmoHud.Custom&&ScUiSettings.AmmoHud.X==.25f&&ScUiSettings.AmmoHud.Y==.7f&&!ScUiSettings.NaturalEnemies);
    object Field(object o,string n)=>o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);
    void Set(object o,string n,object v)=>o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(o,v);
    void Select(ScGunLayoutScreen s,string id){Set(s,"m_selected",id);typeof(ScGunLayoutScreen).GetMethod("LoadSelected",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(s,null);}
    void Layout(Screen s,Vector2 size){for(int i=0;i<3;i++){s.Measure(size);s.Arrange(Vector2.Zero,size);}s.Update();s.Measure(size);s.Arrange(Vector2.Zero,size);}
    void Click(Screen s,string n){var b=(BevelledButtonWidget)Field(s,n);b.m_clickableWidget.IsClicked=true;s.Update();b.m_clickableWidget.IsClicked=false;}
    void Capture(Screen screen,Vector2 size,string name){
        using var target=new RenderTarget2D((int)size.X,(int)size.Y,1,ColorFormat.Rgba8888,DepthFormat.Depth24Stencil8);
        Display.RenderTarget=target;Display.Viewport=new(0,0,(int)size.X,(int)size.Y);Display.ScissorRectangle=new(0,0,(int)size.X,(int)size.Y);
        Display.Clear(new Color(22,27,34),1,0);Widget.DrawWidgetsHierarchy(screen);
        using(var file=File.Create(Path.Combine(output,$"{name}-{size.X}x{size.Y}.png")))RenderTarget2D.Save(target,file,ImageFileFormat.Png,false);
        Display.RenderTarget=null;
    }
    foreach(var size in new[]{new Vector2(850,479),new Vector2(360,640)}){
        var screen=new ScGunSettingsScreen{WidgetsHierarchyInput=new WidgetInput()};screen.Enter([]);Layout(screen,size);
        // 2026-10-01 user request: the group number is the first row of the scrolling list, not a pinned header.
        var content=(ContainerWidget)Field(screen,"m_content");var scroll=(ScrollPanelWidget)Field(screen,"m_scroll");
        var group=content.AllChildren.OfType<LabelWidget>().Single(l=>l.Text.Contains("1087216872"));var groupAt=group.GlobalBounds.Min;
        Check("group is the first row of the scrolling list "+size,((ContainerWidget)content.Children.First()).AllChildren.Contains(group)&&group.GlobalBounds.Min.Y>=scroll.GlobalBounds.Min.Y&&group.GlobalBounds.Max.Y<size.Y);
        Check("HUD not on main settings "+size,typeof(ScGunSettingsScreen).GetField("m_hudX",BindingFlags.NonPublic|BindingFlags.Instance)==null);
        Capture(screen,size,"settings");scroll.ScrollPosition=500;Layout(screen,size);
        Check("group scrolls with the list (not pinned) "+size,group.GlobalBounds.Min.Y<groupAt.Y);
        var save=(Widget)Field(screen,"m_save");Check("save on screen "+size,save.GlobalBounds.Max.Y<=size.Y&&save.ActualSize.Y>=48);screen.Leave();
        foreach(bool left in new[]{false,true}){
            SettingsManager.LeftHandedLayout=left;ScUiSettings.AmmoHud=new(){Custom=true,X=.8f,Y=.3f};var savedHud=ScUiSettings.AmmoHud.Copy();
            var editor=new ScGunLayoutScreen{WidgetsHierarchyInput=new WidgetInput()};editor.Enter([]);Layout(editor,size);
            foreach(string id in ScGunFunctions.All){Select(editor,id);Layout(editor,size);Check("every selected button visible "+id+size+left,editor.PreviewVisible(id)&&((Dictionary<string,BevelledButtonWidget>)Field(editor,"m_proxies"))[id].IsVisible);
                if(!left&&id is ScGunFunctions.Plant or ScGunFunctions.C4Timer or ScGunFunctions.Voice)Capture(editor,size,id);
            }
            Select(editor,ScGunFunctions.Inspect);((CheckboxWidget)Field(editor,"m_enabled")).IsChecked=false;editor.Update();
            var proxies=(Dictionary<string,BevelledButtonWidget>)Field(editor,"m_proxies");
            Check("disabled preview keeps old dim behavior "+size+left,proxies[ScGunFunctions.Inspect].IsVisible&&proxies[ScGunFunctions.Inspect].Color.A==90);
            Check("HUD in editor selection "+size+left,((string[])typeof(ScGunLayoutScreen).GetField("Entries",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null)).Contains(ScGunLayoutScreen.HudId));
            Select(editor,ScGunLayoutScreen.HudId);Layout(editor,size);
            var sample=(ScAmmoHud)Field(editor,"m_hudSample");Check("real M4A4 HUD sample "+size+left,sample.Magazine.Icon=="m4a4"&&sample.Main.Text=="46 / 46"&&sample.Panel.IsVisible);
            Check("HUD visible button controls hidden "+size+left,((Widget)Field(editor,"m_hudProxy")).IsVisible&&!((Widget)Field(editor,"m_enabled")).IsVisible);
            if(!left)Capture(editor,size,"hud-editor");Set(editor,"m_collapsed",true);Layout(editor,size);
            var preview=(CanvasWidget)Field(editor,"m_preview");var hud=(ScHudPosition)Field(editor,"m_hud");var proxy=(CanvasWidget)Field(editor,"m_hudProxy");
            var center=hud.Position(preview.ActualSize,proxy.Size)+proxy.Size/2;
            editor.WidgetsHierarchyInput.Press=preview.WidgetToScreen(center);editor.Update();
            Check("HUD drag begins on preview "+size+left,(string)Field(editor,"m_dragging")==ScGunLayoutScreen.HudId);
            var moved=new Vector2(size.X*.6f,size.Y*.5f);editor.WidgetsHierarchyInput.Press=preview.WidgetToScreen(moved);editor.Update();editor.WidgetsHierarchyInput.Press=null;Layout(editor,size);hud=(ScHudPosition)Field(editor,"m_hud");
            Check("HUD follows touch drag "+size+left,Math.Abs(hud.X-.6f)<.011&&Math.Abs(hud.Y-.5f)<.011);
            Check("HUD transactional "+size+left,ScUiSettings.AmmoHud.X==savedHud.X&&ScUiSettings.AmmoHud.Y==savedHud.Y);
            if(!left)Capture(editor,size,"hud-dragged");Click(editor,"m_cancel");editor.Leave();
            Check("cancel preserves HUD "+size+left,ScUiSettings.AmmoHud.X==savedHud.X);
            editor=new ScGunLayoutScreen{WidgetsHierarchyInput=new WidgetInput()};editor.Enter([]);Layout(editor,size);Select(editor,ScGunLayoutScreen.HudId);
            Set(editor,"m_collapsed",true);Layout(editor,size);
            Check("HUD has no position sliders "+size+left,typeof(ScGunLayoutScreen).GetField("m_hudX",BindingFlags.NonPublic|BindingFlags.Instance)==null);
            var touches=(List<TouchLocation>)typeof(Touch).GetField("m_touchLocations",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
            void Fingers(Vector2 c,Vector2 d){touches.Clear();touches.Add(new(){Id=71,Position=c-d,State=TouchLocationState.Moved});touches.Add(new(){Id=92,Position=c+d,State=TouchLocationState.Moved});}
            hud=(ScHudPosition)Field(editor,"m_hud");center=hud.Position(size,proxy.Size)+proxy.Size/2;
            Fingers(center,new(20,0));editor.Update();var dest=new Vector2(size.X*.65f,size.Y*.55f);
            Fingers(dest,new(0,30));editor.Update();touches.Clear();Layout(editor,size);hud=(ScHudPosition)Field(editor,"m_hud");
            Check("native two-finger translation scale rotation "+size+left,Math.Abs(hud.X-.65f)<.001&&Math.Abs(hud.Y-.55f)<.001&&Math.Abs(hud.Scale-1.5f)<.001&&Math.Abs(hud.Rotation-MathF.PI/2)<.001);
            var wheelAt=hud.Position(size,proxy.Size)+proxy.Size/2;
            editor.WidgetsHierarchyInput.Scroll=new Vector3(wheelAt,1);editor.Update();editor.WidgetsHierarchyInput.Scroll=null;
            Check("desktop wheel scales "+size+left,Math.Abs(hud.Scale-1.65f)<.001);
            var keys=(bool[])typeof(Keyboard).GetField("m_keysDownArray",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);keys[(int)Key.Shift]=true;
            editor.WidgetsHierarchyInput.Scroll=new Vector3(wheelAt,1);editor.Update();editor.WidgetsHierarchyInput.Scroll=null;keys[(int)Key.Shift]=false;
            Check("desktop Shift wheel rotates "+size+left,Math.Abs(hud.Rotation-(MathF.PI/2+MathF.PI/36))<.001);
            var expected=hud.Copy();if(!left)Capture(editor,size,"hud-gesture");
            Click(editor,"m_save");editor.Leave();ScUiSettings.Load();
            Check("save HUD transform preserves spawn preference "+size+left,Math.Abs(ScUiSettings.AmmoHud.X-expected.X)<.001&&Math.Abs(ScUiSettings.AmmoHud.Y-expected.Y)<.001&&ScUiSettings.AmmoHud.Scale==expected.Scale&&ScUiSettings.AmmoHud.Rotation==expected.Rotation&&!ScUiSettings.NaturalEnemies);
            var realHud=new ScAmmoHud();var hudGui=Blank<ComponentGui>();hudGui.ControlsContainerWidget=new CanvasWidget();hudGui.ControlsContainerWidget.Measure(size);hudGui.ControlsContainerWidget.Arrange(Vector2.Zero,size);realHud.Attach(hudGui);
            realHud.Panel.Measure(proxy.Size);realHud.Panel.Arrange(Vector2.Zero,proxy.Size);typeof(ScAmmoHud).GetMethod("Position",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(realHud,null);
            Check("gameplay HUD applies same transform "+size+left,realHud.Panel.RenderTransform==expected.Transform(proxy.Size));realHud.Dispose();
            editor=new ScGunLayoutScreen{WidgetsHierarchyInput=new WidgetInput()};editor.Enter([]);Layout(editor,size);Select(editor,ScGunLayoutScreen.HudId);
            Click(editor,"m_resetOne");Layout(editor,size);Check("automatic HUD stays visible "+size+left,!((ScHudPosition)Field(editor,"m_hud")).Custom&&((Widget)Field(editor,"m_hudProxy")).IsVisible);
            Set(editor,"m_collapsed",true);Layout(editor,size);if(!left)Capture(editor,size,"hud-auto");var autoCorner=ScAmmoHud.FindCorner(size,proxy.Size,[]);
            editor.WidgetsHierarchyInput.Press=autoCorner+proxy.Size/2;editor.Update();editor.WidgetsHierarchyInput.Press=autoCorner+proxy.Size/2-new Vector2(30,30);editor.Update();editor.WidgetsHierarchyInput.Press=null;
            Check("drag automatic sample enables custom in working copy "+size+left,((ScHudPosition)Field(editor,"m_hud")).Custom&&((CheckboxWidget)Field(editor,"m_hudCustom")).IsChecked);editor.Leave();
        }
    }
    Check("owned audio override removed",typeof(ScUiSettings).Assembly.GetType("Game.ScOwnedAudio")==null);
    var atlasEntry=zip.Entries.Single(e=>e.FullName.Contains("grenade_fireburst_atlas."));
    using(var source=atlasEntry.Open()){var image=Image.Load(source);Check("native fireburst atlas decodes",image.Width is 512 or 1024&&image.Pixels.Any(p=>p.A>0));image.Dispose();}
}catch(Exception e){failed=1;Console.Error.WriteLine(e);}finally{KnifeClock.Virtual=false;Window.Close();}};
Window.Run(900,650,WindowMode.Fixed,"Gameplay settings verification");
File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{failed,checks,scope="Native detached widgets, HUD touch drag/save/cancel and retained grenade behavior. Android listening pending."},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"followup checks={checks.Count} failed={failed}");return failed;

sealed class NoFloor:SubsystemTerrain {
    public override TerrainRaycastResult? Raycast(Vector3 a,Vector3 b,bool interaction,bool air,Func<int,float,bool> filter)=>null;
}
sealed class Floor:SubsystemTerrain {
    public Floor(){Terrain=new Terrain();}
    public override TerrainRaycastResult? Raycast(Vector3 a,Vector3 b,bool interaction,bool air,Func<int,float,bool> filter){
        if(a.Y<0||b.Y>0)return null;var d=b-a;return new TerrainRaycastResult{Ray=new Ray3(a,Vector3.Normalize(d)),Distance=d.Length()*(-a.Y/d.Y),CellFace=new CellFace(0,0,0,4),Value=0};
    }
}

sealed class FixtureAppearance:Component,IScFirstPersonAppearance {public string FirstPersonRole {get;set;}}
