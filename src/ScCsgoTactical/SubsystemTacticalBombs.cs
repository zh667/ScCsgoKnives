using Engine;
using Engine.Input;
using Engine.Audio;
using Engine.Graphics;
using TemplatesDatabase;
using GameEntitySystem;
namespace Game;

/// <summary>Enemy bombs have an independent save stream; player C4 data and fuse settings stay untouched.</summary>
public sealed class SubsystemTacticalBombs : Subsystem,IUpdateable,IDrawable {
    public sealed class BlastAttack(ComponentBody body,Vector3 point,Vector3 direction,float damage)
        : ProjectileAttackment(body,null,point,direction,damage,null);
    public sealed class Bomb {
        public ScC4Charge Charge=new(){Fuse=40,Remaining=40,Owner=-2,Power=250,Radius=12};
        public ComponentPlayer Defuser;
        public TacticalDefuseClock Clock;
        public bool Kit;
        public Vector3 Start;
        public Sound Cue,Disarm;
    }
    sealed class Hud {
        public StackPanelWidget Panel;
        public LabelWidget Label;
        public ValueBarWidget Bar;
        public BevelledButtonWidget Button;
        public int Warning;
        public readonly ScWeaponButtonInput Input=new();
        public readonly TacticalDefusePress Press=new();
        public long InputFrame=-1;
        public bool SuppressUntilRelease;
    }
    public readonly List<Bomb> Bombs=[];
    readonly List<ScC4Blast> blasts=[];
    readonly Dictionary<ComponentPlayer,Hud> hud=[];
    readonly PrimitivesRenderer3D renderer=new();
    SubsystemTime time;SubsystemTerrain terrain;SubsystemPlayers players;SubsystemAudio audio;SubsystemBodies bodies;
    double lastTime;
    public UpdateOrder UpdateOrder=>UpdateOrder.Default;
    public int[] DrawOrders=>[10];
    public override void Load(ValuesDictionary values) {
        base.Load(values);if(values.GetValue("Schema",1)!=1)throw new InvalidOperationException("敌方 C4 存档版本不受支持。");
        var saved=values.GetValue<ValuesDictionary>("Bombs",null);
        if(saved is not null)foreach(var pair in saved){var c=ScC4Charge.Load((ValuesDictionary)pair.Value);if(c.Fuse!=40||c.Owner!=-2)throw new InvalidOperationException("敌方 C4 存档数值异常。");Bombs.Add(new(){Charge=c});}
        if(Bombs.Count>8)throw new InvalidOperationException("敌方 C4 数量异常。");
        time=Project.FindSubsystem<SubsystemTime>(true);terrain=Project.FindSubsystem<SubsystemTerrain>(true);players=Project.FindSubsystem<SubsystemPlayers>(true);
        audio=Project.FindSubsystem<SubsystemAudio>(true);bodies=Project.FindSubsystem<SubsystemBodies>(true);lastTime=time.GameTime;
        ScWeaponActionGate.Reserved+=Blocking;
    }
    public override void Save(ValuesDictionary values) {
        base.Save(values);values.SetValue("Schema",1);var saved=new ValuesDictionary();for(int i=0;i<Bombs.Count;i++)saved.SetValue(i.ToString(),Bombs[i].Charge.Save());values.SetValue("Bombs",saved);
    }
    public bool TryPlant(Vector3 position,float yaw) {
        if(Bombs.Count>=8||!ScGrenadeState.Finite(position)||!float.IsFinite(yaw))return false;
        var floor=terrain.Raycast(position+Vector3.UnitY*.3f,position-Vector3.UnitY*1.5f,false,true,(v,d)=>BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v));
        if(!floor.HasValue||floor.Value.CellFace.Face!=4)return false;
        var bomb=new Bomb();bomb.Charge.Position=Project.FindSubsystem<SubsystemScC4>(true).VisiblePlantPosition(floor.Value.HitPoint());bomb.Charge.Yaw=yaw;Bombs.Add(bomb);
        audio.PlaySound("Audio/ScCsgoKnives/c4_plant",1,0,bomb.Charge.Position,12,true);return true;
    }
    public static bool HasKit(ComponentPlayer p) {
        var inv=p.ComponentMiner.Inventory;int count=inv is ComponentCreativeInventory?Math.Min(10,inv.SlotsCount):inv.SlotsCount;
        int index=BlocksManager.GetBlockIndex<ScTacticalDefuserBlock>(true);
        for(int i=0;i<count;i++)if(inv.GetSlotCount(i)>0&&Terrain.ExtractContents(inv.GetSlotValue(i))==index)return true;
        return false;
    }
    public Bomb Target(ComponentPlayer p) {
        if(!ReferenceEquals(p.Project,Project)||!ScGunBindings.ContextAvailable(p))return null;
        var camera=p.GameWidget.ActiveCamera;var ray=new Ray3(camera.ViewPosition,camera.ViewDirection);Bomb best=null;float nearest=2.5f;
        foreach(var b in Bombs){var box=new BoundingBox(b.Charge.Position-new Vector3(.32f,.12f,.32f),b.Charge.Position+new Vector3(.32f,.35f,.32f));
            if(Vector3.DistanceSquared(p.ComponentBody.Position,b.Charge.Position)>3*3)continue;
            float? distance=ray.Intersection(box);if(!distance.HasValue||distance.Value>=nearest)continue;
            var wall=terrain.Raycast(ray.Position,ray.Position+ray.Direction*distance.Value,false,true,(v,d)=>BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v));
            if(wall.HasValue&&wall.Value.Distance+.05f<distance.Value)continue;nearest=distance.Value;best=b;
        }return best;
    }
    bool TouchDown(ComponentPlayer p){
        if(!hud.TryGetValue(p,out var h))return false;
        if(h.InputFrame!=Time.FrameIndex){h.InputFrame=Time.FrameIndex;h.Input.Sample(h.Button,true,h.Panel.IsVisible&&h.Button.IsVisible&&ScGunBindings.ContextAvailable(p));}
        return h.Input.Pressed;
    }
    bool Down(ComponentPlayer p)=>TouchDown(p)
        ||Enum.TryParse<Key>(ScGunBindings.Get(ScGunFunctions.Plant),out var key)&&key!=Key.Null&&p.GameWidget.Input.IsKeyDown(key)
        ||ScGamepadBindings.Down(p,ScGunFunctions.Plant,false);
    public bool Blocking(ComponentPlayer p)=>ReferenceEquals(p?.Project,Project)&&ScGunBindings.ContextAvailable(p)
        &&(Bombs.Any(b=>b.Defuser==p)||Down(p)&&(hud.TryGetValue(p,out var h)&&h.SuppressUntilRelease||Target(p)!=null));
    public void Input(ComponentInput input) {
        var p=input.m_componentPlayer;if(!Blocking(p))return;
        // E is also vanilla inventory. Consume that physical key only; other inventory controls can cancel.
        var native=SettingsManager.KeyboardMappingSettings is null?Key.E:SettingsManager.GetKeyboardMapping("ToggleInventory",false);
        bool sharedKey=Enum.TryParse<Key>(ScGunBindings.Get(ScGunFunctions.Plant),out var key)&&Equals(native,key)&&p.GameWidget.Input.IsKeyDown(key);
        if(sharedKey)input.m_playerInput.ToggleInventory=false;
        else if(input.m_playerInput.ToggleInventory){foreach(var b in Bombs.Where(b=>b.Defuser==p))Cancel(b);if(hud.TryGetValue(p,out var h))h.Press.Cancel();}
        input.m_playerInput.EditItem=false;input.m_playerInput.Interact=null;
        input.m_playerInput.Hit=null;input.m_playerInput.Dig=null;input.m_playerInput.Aim=null;
    }
    Hud View(ComponentPlayer p) {
        if(hud.TryGetValue(p,out var h))return h;
        h=new(){Panel=new StackPanelWidget{Direction=LayoutDirection.Vertical,HorizontalAlignment=WidgetAlignment.Center,VerticalAlignment=WidgetAlignment.Center,Margin=new Vector2(0,85)},
            Label=ScGunUi.Label("",.62f),Bar=new ValueBarWidget{BarsCount=40,BarSize=new Vector2(5,5),Spacing=1},Button=ScGunUi.Button("按住拆除 C4",170)};
        h.Label.DropShadow=true;h.Label.IsHitTestVisible=false;h.Panel.Children.Add(h.Label);h.Panel.Children.Add(h.Bar);h.Panel.Children.Add(h.Button);
        p.ComponentGui.ControlsContainerWidget.Children.Add(h.Panel);hud[p]=h;return h;
    }
    void StopSound(ref Sound sound){if(sound is null)return;sound.Stop();sound.Dispose();audio.m_sounds.Remove(sound);sound=null;}
    Sound Play(string path,Vector3 position,float volume){if(SettingsManager.SoundsVolume<=0)return null;var s=audio.CreateSound("Audio/ScCsgoKnives/"+path);s.Volume=SettingsManager.SoundsVolume*volume*audio.CalculateVolume(audio.CalculateListenerDistance(position),8);s.Play();return s;}
    void Cancel(Bomb b){if(b.Defuser is {} p&&hud.TryGetValue(p,out var h)){h.Press.Cancel();h.SuppressUntilRelease=true;}b.Defuser=null;b.Clock=null;StopSound(ref b.Disarm);}
    void Remove(Bomb b){Cancel(b);StopSound(ref b.Cue);Bombs.Remove(b);}
    public void Update(float dt) {
        float elapsed=(float)Math.Max(0,time.GameTime-lastTime);lastTime=time.GameTime;
        // Existing operations resolve against pre-tick fuse; new presses begin at this frame's game time.
        foreach(var b in Bombs.ToArray()){
            var c=b.Charge;bool defused=false;
            if(b.Defuser is {} p){bool valid=players.ComponentPlayers.Contains(p)&&ScGunBindings.ContextAvailable(p)&&Down(p)&&Target(p)==b
                    &&Vector3.DistanceSquared(b.Start,p.ComponentBody.Position)<.16f&&(!b.Kit||HasKit(p));
                var outcome=b.Clock.Advance(c.Remaining,elapsed,valid);
                if(outcome==DefuseResult.Defused){Remove(b);audio.PlaySound("Audio/ScCsgoKnives/c4_disarmfinish",1,0,c.Position,16,false);p.ComponentGui.DisplaySmallMessage("C4 已拆除",Color.Green,false,false);defused=true;}
                else if(outcome==DefuseResult.Cancelled)Cancel(b);
            }
            if(defused)continue;
            if(c.Remaining<=0||c.Tick(elapsed)){Remove(b);Explode(c);continue;}
            if(c.NextCue() is string cue){StopSound(ref b.Cue);b.Cue=Play(cue,c.Position,.85f);}
        }
        foreach(var p in players.ComponentPlayers){var b=Target(p);var h=View(p);h.Panel.IsVisible=b!=null;
            bool down=Down(p);if(!down)h.SuppressUntilRelease=false;
            bool start=h.Press.Step(down,b is not null&&b.Defuser is null);
            if(b is null){h.Warning=0;h.Input.Cancel();continue;}
            bool kit=HasKit(p);float needed=b.Defuser==p?b.Clock.Needed:kit?5:10;float margin=b.Charge.Remaining-needed;
            bool other=b.Defuser!=null&&b.Defuser!=p;h.Button.IsVisible=!other&&p.GameWidget.Input.Devices.HasFlag(WidgetInputDevice.Touch);
            h.Bar.IsVisible=b.Defuser==p;h.Bar.Value=b.Clock is null?0:b.Clock.Elapsed/b.Clock.Duration;
            h.Label.Color=margin<=0?Color.Red:margin<1?Color.Yellow:Color.White;
            string action=other?"队友正在拆除":b.Defuser==p?"正在拆除":$"按住 {ScGunBindings.Get(ScGunFunctions.Plant)} 拆除";
            h.Label.Text=$"{action} · {(kit?"拆弹钳":"徒手")}\n爆炸 {b.Charge.Remaining:F1}s / 拆除 {needed:F1}s"+(margin<=0?"\n时间不足！":margin<1?"\n时间紧迫":"");
            int warning=margin<=0?2:margin<1?1:0;
            if(warning>h.Warning&&!other)p.ComponentGui.DisplaySmallMessage(warning==2?"拆除时间不足！仍可按住尝试。":"拆除时间紧迫！",warning==2?Color.Red:Color.Yellow,true,false);h.Warning=warning;
            if(start){b.Defuser=p;b.Kit=kit;b.Clock=new(kit);b.Start=p.ComponentBody.Position;b.Disarm=Play("c4_disarmstart",b.Charge.Position,.75f);}
        }
        foreach(var p in hud.Keys.Where(p=>!players.ComponentPlayers.Contains(p)).ToArray()){hud[p].Panel.ParentWidget?.Children.Remove(hud[p].Panel);hud.Remove(p);}
        blasts.RemoveAll(b=>time.GameTime-b.Started>ScC4Blast.Lifetime);
    }
    void Explode(ScC4Charge c) {
        if(blasts.Count>=8)blasts.RemoveAt(0);blasts.Add(new(c.Position,c.Radius,time.GameTime));
        foreach(var body in bodies.Bodies.ToArray()){
            var point=body.BoundingBox.Center();var delta=point-c.Position;float damage=ScC4Charge.DamageAt(delta.Length(),c.Power,c.Radius);if(damage<=0)continue;
            if(terrain.Raycast(c.Position+Vector3.UnitY*.15f,point,false,true,(v,d)=>ScGunRange.TerrainStopsBullet(v)).HasValue)continue;
            ComponentMiner.AttackBody(new BlastAttack(body,point,delta.LengthSquared()>.001f?Vector3.Normalize(delta):Vector3.UnitY,damage){AttackSoundVolume=0,ImpulseFactor=0});
        }
        audio.PlaySound("Audio/ScCsgoKnives/c4_explode_close_01",2,0,c.Position,32,true);
    }
    public void Draw(Camera camera,int order) {
        var block=(ScC4Block)BlocksManager.Blocks[BlocksManager.GetBlockIndex<ScC4Block>(true)];
        foreach(var b in Bombs){if(Vector3.DistanceSquared(camera.ViewPosition,b.Charge.Position)>128*128)continue;var m=Matrix.CreateRotationY(b.Charge.Yaw)*Matrix.CreateTranslation(b.Charge.Position);
            block.DrawPlanted(renderer,Color.White,ref m,new DrawBlockEnvironmentData{SubsystemTerrain=terrain,Light=15});}
        foreach(var b in blasts)b.Draw(renderer,camera,time.GameTime);renderer.Flush(camera.ViewProjectionMatrix);
    }
    public override void Dispose(){ScWeaponActionGate.Reserved-=Blocking;foreach(var b in Bombs.ToArray())Remove(b);foreach(var h in hud.Values)h.Panel.ParentWidget?.Children.Remove(h.Panel);hud.Clear();base.Dispose();}
}
