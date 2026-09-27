using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Engine;
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
    var fireProject=new Project();var fireAudio=new SubsystemAudio();fireAudio.m_project=fireProject;fireProject.m_subsystems.Add(fireAudio);
    var grenades=new SubsystemScGrenades();grenades.m_project=fireProject;
    typeof(SubsystemScGrenades).GetField("m_terrain",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(grenades,new NoFloor());
    var airborne=new ScGrenadeState{Kind=3,Id=9,Position=new(0,30,0),Age=3,Remaining=0};
    var activeGrenades=(System.Collections.IList)typeof(SubsystemScGrenades).GetField("m_active",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(grenades);activeGrenades.Add(airborne);
    typeof(SubsystemScGrenades).GetMethod("Detonate",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(grenades,[airborne]);
    var bursts=(System.Collections.IList)typeof(SubsystemScGrenades).GetField("fireBursts",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(grenades);
    Check("unsupported air detonation emits visual but no floating damage zone",bursts.Count==1&&activeGrenades.Count==0);
    // Native settings use a detached runner's application directory, never player's config.
    string settingsPath=Storage.GetSystemPath(ScUiSettings.Path);
    Check("settings fixture stays in task workspace",Path.GetFullPath(settingsPath).StartsWith(Path.GetFullPath(".tmp/gameplay-audio-130-20260927"),StringComparison.OrdinalIgnoreCase));
    ScUiSettings.AmmoHud=new(){Custom=true,X=.25f,Y=.7f};ScUiSettings.NaturalEnemies=false;
    Check("settings saved",ScUiSettings.Save());ScUiSettings.ResetAll();ScUiSettings.Load();
    Check("HUD and spawn switch roundtrip",ScUiSettings.AmmoHud.Custom&&ScUiSettings.AmmoHud.X==.25f&&ScUiSettings.AmmoHud.Y==.7f&&!ScUiSettings.NaturalEnemies);
    foreach(var size in new[]{new Vector2(850,479),new Vector2(360,640)}){
        var screen=new ScGunSettingsScreen{WidgetsHierarchyInput=new WidgetInput()};
        screen.Enter([]);screen.Measure(size);screen.Arrange(Vector2.Zero,size);screen.Update();
        var snapshot=(ScHudPosition)ScUiSettings.AmmoHud.Copy();
        var x=(SliderWidget)typeof(ScGunSettingsScreen).GetField("m_hudX",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(screen);
        x.Value=80;screen.Update();Check("edit remains transactional "+size,ScUiSettings.AmmoHud.X==snapshot.X);
        for(int i=0;i<3;i++){screen.Measure(size);screen.Arrange(Vector2.Zero,size);}
        var save=(Widget)typeof(ScGunSettingsScreen).GetField("m_save",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(screen);
        Check("fixed save button on screen "+size,save.GlobalBounds.Max.Y<=size.Y&&save.ActualSize.Y>=48);
        using var target=new RenderTarget2D((int)size.X,(int)size.Y,1,ColorFormat.Rgba8888,DepthFormat.Depth24Stencil8);
        Display.RenderTarget=target;Display.Viewport=new(0,0,(int)size.X,(int)size.Y);Display.ScissorRectangle=new(0,0,(int)size.X,(int)size.Y);
        Display.Clear(new Color(22,27,34),1,0);Widget.DrawWidgetsHierarchy(screen);
        using(var file=File.Create(Path.Combine(output,$"settings-{size.X}x{size.Y}.png")))RenderTarget2D.Save(target,file,ImageFileFormat.Png,false);
        screen.Leave();
    }
    var atlasEntry=zip.Entries.Single(e=>e.FullName.Contains("grenade_fireburst_atlas."));
    using(var source=atlasEntry.Open()){var image=Image.Load(source);Check("native fireburst atlas decodes",image.Width is 512 or 1024&&image.Pixels.Any(p=>p.A>0));image.Dispose();}
    // Silence-only real OpenAL buffers: exercise sources/pitch and release without audible spam.
    typeof(Mixer).GetMethod("Initialize",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
    Check("OpenAL fixture initialized",Mixer.m_isInitialized);
    var buffer=new SoundBuffer(new short[44100],0,44100,1,22050);
    caches["Audio/CSFixtureSilence"]=[buffer];SettingsManager.SoundsVolume=.05f;
    int originalCount=Mixer.m_soundsToStopPoll.Count;object owner=new();
    for(int i=0;i<200;i++)Check("rapid switch owned source "+i,ScOwnedAudio.Play("Audio/CSFixtureSilence",1,0,"draw",owner,true)&&ScOwnedAudio.Count==1&&buffer.UseCount==1);
    var sound=Mixer.m_soundsToStopPoll.Single(s=>s.SoundBuffer==buffer);
    Mixer.AL.GetSourceProperty((uint)sound.m_source,Silk.NET.OpenAL.SourceFloat.Pitch,out float pitch);
    Check("actual AL pitch remains normal",pitch==1&&sound.Pitch==1);
    Check("rapid switches do not accumulate sources",Mixer.m_soundsToStopPoll.Count==originalCount+1);
    ScOwnedAudio.Clear();Check("explicit cleanup returns buffer use count",buffer.UseCount==0&&Mixer.m_soundsToStopPoll.Count==originalCount);
    for(int i=0;i<30;i++)ScOwnedAudio.Play("Audio/CSFixtureSilence",1,0,"world");
    Check("crowd audio bounded before allocation",ScOwnedAudio.Count==12&&buffer.UseCount==12);
    ScOwnedAudio.Clear();buffer.Dispose();
}catch(Exception e){failed=1;Console.Error.WriteLine(e);}finally{ScOwnedAudio.Clear();KnifeClock.Virtual=false;Window.Close();}};
Window.Run(900,650,WindowMode.Fixed,"Gameplay settings verification");
File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{failed,checks,scope="Native detached widgets/animation/OpenAL silence, not Android listening QA."},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"followup checks={checks.Count} failed={failed}");return failed;

sealed class NoFloor:SubsystemTerrain {
    public override TerrainRaycastResult? Raycast(Vector3 a,Vector3 b,bool interaction,bool air,Func<int,float,bool> filter)=>null;
}
