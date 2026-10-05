using Engine;
using Engine.Input;
using Engine.Audio;
using Engine.Graphics;
using TemplatesDatabase;
using GameEntitySystem;
namespace Game;

/// <summary>Enemy bombs have an independent save stream; player C4 data and fuse settings stay untouched.
/// Defusing is unified: an armed player charge (owned by SubsystemScC4, never copied here) is offered through a
/// transient proxy with the same hold/kit/cancel rules. Each side keeps its own collection, fuse, power and radius.</summary>
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
        /// <summary>Proxy for an armed player charge; never saved or ticked here.</summary>
        public bool PlayerCharge;
        /// <summary>Stable identity and reward source recorded when the squad created it. Old saves: no reward.</summary>
        public string BombId="";public bool Rewardable;
    }
    /// <summary>One-time reward for defusing an armed enemy-squad bomb in survival. First-pass values.</summary>
    public static readonly (int Kind,int Count)[] DefuseReward=[(ScWeaponMaterialBlock.Blank,2),(ScWeaponMaterialBlock.Mechanism,1)];
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
        if(saved is not null)foreach(var pair in saved){var d=(ValuesDictionary)pair.Value;var c=ScC4Charge.Load(d);if(c.Fuse!=40||c.Owner!=-2)throw new InvalidOperationException("敌方 C4 存档数值异常。");
            string id=d.GetValue("BombId","");Bombs.Add(new(){Charge=c,BombId=id,Rewardable=id.Length>0&&d.GetValue("Rewardable",false)});}
        if(Bombs.Count>8)throw new InvalidOperationException("敌方 C4 数量异常。");
        time=Project.FindSubsystem<SubsystemTime>(true);terrain=Project.FindSubsystem<SubsystemTerrain>(true);players=Project.FindSubsystem<SubsystemPlayers>(true);
        audio=Project.FindSubsystem<SubsystemAudio>(true);bodies=Project.FindSubsystem<SubsystemBodies>(true);lastTime=time.GameTime;
        ScWeaponActionGate.Reserved+=Blocking;
    }
    public override void Save(ValuesDictionary values) {
        base.Save(values);values.SetValue("Schema",1);var saved=new ValuesDictionary();
        for(int i=0;i<Bombs.Count;i++){var d=Bombs[i].Charge.Save();if(Bombs[i].BombId.Length>0){d.SetValue("BombId",Bombs[i].BombId);d.SetValue("Rewardable",Bombs[i].Rewardable);}saved.SetValue(i.ToString(),d);}
        values.SetValue("Bombs",saved);
    }
    public bool TryPlant(Vector3 position,float yaw)=>PlantFrom(position,yaw,false);
    /// <summary>Plants with the squad's reward source recorded at creation, never inferred at defuse time.</summary>
    public bool PlantFrom(Vector3 position,float yaw,bool rewardable) {
        if(Bombs.Count>=8||!ScGrenadeState.Finite(position)||!float.IsFinite(yaw))return false;
        var floor=terrain.Raycast(position+Vector3.UnitY*.3f,position-Vector3.UnitY*1.5f,false,true,(v,d)=>BlocksManager.Blocks[Terrain.ExtractContents(v)].IsCollidable_(v));
        if(!floor.HasValue||floor.Value.CellFace.Face!=4)return false;
        var bomb=new Bomb{BombId=Guid.NewGuid().ToString("N"),Rewardable=rewardable};bomb.Charge.Position=Project.FindSubsystem<SubsystemScC4>(true).VisiblePlantPosition(floor.Value.HitPoint());bomb.Charge.Yaw=yaw;Bombs.Add(bomb);
        audio.PlaySound("Audio/ScCsgoKnives/c4_plant",1,0,bomb.Charge.Position,12,true);TacticalNet.Sound("Audio/ScCsgoKnives/c4_plant",1,bomb.Charge.Position,12);return true;
    }
    /// <summary>Creative players always have the kit (5 s): the catalogue is not something they carry. Survival checks
    /// every slot of the inventory actually carried.</summary>
    public static bool HasKit(ComponentPlayer p) {
        if(p.ComponentMiner.Inventory is ComponentCreativeInventory||p.Entity?.Project?.FindSubsystem<SubsystemGameInfo>(false)?.WorldSettings.GameMode==GameMode.Creative)return true;
        var inv=p.ComponentMiner.Inventory;int count=inv.SlotsCount;
        int index=BlocksManager.GetBlockIndex<ScTacticalDefuserBlock>(true);
        for(int i=0;i<count;i++)if(inv.GetSlotCount(i)>0&&Terrain.ExtractContents(inv.GetSlotValue(i))==index)return true;
        return false;
    }
    readonly Dictionary<ScC4Charge,Bomb> playerCharges=new(ReferenceEqualityComparer.Instance);
    public IEnumerable<Bomb> Defusable=>Bombs.Concat(playerCharges.Values);
    /// <summary>Own charges always; another player's only while the world allows friendly fire. Enemy bombs always.</summary>
    public bool MayDefuse(ComponentPlayer p,Bomb b)=>!b.PlayerCharge||b.Charge.Owner==p.PlayerData.PlayerIndex
        ||Project.FindSubsystem<SubsystemGameInfo>(false)?.WorldSettings.IsFriendlyFireEnabled==true;
    void SyncPlayerCharges(){
        var live=Project.FindSubsystem<SubsystemScC4>(false)?.Charges;
        foreach(var pair in playerCharges.ToArray())if(live is null||!live.Contains(pair.Key)||pair.Key.Remaining<=0){Cancel(pair.Value);playerCharges.Remove(pair.Key);}
        if(live is not null)foreach(var c in live)if(c.Remaining>0&&!playerCharges.ContainsKey(c))playerCharges[c]=new Bomb{Charge=c,PlayerCharge=true};
    }
    public Bomb Target(ComponentPlayer p) {
        if(!ReferenceEquals(p.Project,Project)||!ScGunBindings.ContextAvailable(p))return null;
        // This process's camera, or (multiplayer server) the view the player's client sent.
        if(TacticalNet.View(p) is not {} ray)return null;Bomb best=null;float nearest=2.5f;
        foreach(var b in Defusable){if(!MayDefuse(p,b))continue;var box=new BoundingBox(b.Charge.Position-new Vector3(.32f,.12f,.32f),b.Charge.Position+new Vector3(.32f,.35f,.32f));
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
    bool Down(ComponentPlayer p)=>TacticalNet.Defuse(p) is {} remote?remote.Held:TouchDown(p)
        ||Enum.TryParse<Key>(ScGunBindings.Get(ScGunFunctions.Plant),out var key)&&key!=Key.Null&&p.GameWidget.Input.IsKeyDown(key)
        ||ScGamepadBindings.Down(p,ScGunFunctions.Plant,false);
    public bool Blocking(ComponentPlayer p)=>ReferenceEquals(p?.Project,Project)&&ScGunBindings.ContextAvailable(p)
        &&(Defusable.Any(b=>b.Defuser==p)||Down(p)&&(Press(p) is {SuppressUntilRelease:true}||Target(p)!=null));
    /// <summary>A player's press state: the HUD of a player shown here, or (multiplayer server) a remote client's player's.</summary>
    Hud Press(ComponentPlayer p)=>p is not null&&(hud.TryGetValue(p,out var h)||remote.TryGetValue(p,out h))?h:null;
    readonly Dictionary<ComponentPlayer,Hud> remote=[];
    public void Input(ComponentInput input) {
        var p=input.m_componentPlayer;if(!Blocking(p))return;
        // E is also vanilla inventory. Consume that physical key only; other inventory controls can cancel.
        var native=SettingsManager.KeyboardMappingSettings is null?Key.E:SettingsManager.GetKeyboardMapping("ToggleInventory",false);
        bool sharedKey=Enum.TryParse<Key>(ScGunBindings.Get(ScGunFunctions.Plant),out var key)&&Equals(native,key)&&p.GameWidget.Input.IsKeyDown(key);
        if(sharedKey)input.m_playerInput.ToggleInventory=false;
        else if(input.m_playerInput.ToggleInventory){foreach(var b in Defusable.Where(b=>b.Defuser==p).ToArray())Cancel(b);if(hud.TryGetValue(p,out var h))h.Press.Cancel();}
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
    // mp-user-logs-20261002: a sound the engine cannot decode (an Android host: SoundData rejected c4_disarmstart, the whole
    // bomb update failed for that frame) costs the sound only, never the defuse it accompanies.
    Sound Play(string path,Vector3 position,float volume){
        if(SettingsManager.SoundsVolume<=0)return null;
        try{var s=audio.CreateSound("Audio/ScCsgoKnives/"+path);s.Volume=SettingsManager.SoundsVolume*volume*audio.CalculateVolume(audio.CalculateListenerDistance(position),8);s.Play();return s;}
        catch(Exception e){KnifeDiagnostics.WarnOnce("tactical-bomb-sound-"+path,$"[CS Tactical] sound {path} unavailable: {e.GetType().Name}: {e.Message}");return null;}
    }
    void Cancel(Bomb b){if(Press(b.Defuser) is {} h){h.Press.Cancel();h.SuppressUntilRelease=true;}b.Defuser=null;b.Clock=null;StopSound(ref b.Disarm);}
    /// <summary>A notice as single player shows it here, or sent to the client of a remote player.</summary>
    static void Notice(ComponentPlayer p,string text,Color color,bool blink){if(ScNet.IsLocal(p))p.ComponentGui.DisplaySmallMessage(text,color,blink,false);else ScNetFeedback.Tell(p,text,color);}
    void Remove(Bomb b){Cancel(b);StopSound(ref b.Cue);Bombs.Remove(b);}
    /// <summary>Defused enemy bomb: removal and the reward receipt happen in the same frame, so any save holds both or
    /// neither. The receipt is the core's durable grant, delivered when the finisher's inventory has room.</summary>
    void Reward(ComponentPlayer p,Bomb b){
        if(!b.Rewardable||b.PlayerCharge||Project.FindSubsystem<SubsystemGameInfo>(false) is not {} info||info.WorldSettings.GameMode==GameMode.Creative)return;
        var registry=ScGunRegistry.Current;string owner=ScGunHolders.RecoveryOwner(Project,p.ComponentMiner.Inventory);
        if(registry is null||registry.Disabled||owner is null){Log.Warning($"[CS Tactical] defuse reward for bomb {b.BombId} not recorded: no durable destination.");return;}
        registry.Recovery.Grant(owner,DefuseReward.Select(r=>(ScWeaponMaterialBlock.Value(r.Kind),r.Count)),"defused enemy bomb "+b.BombId);
        Notice(p,"拆弹奖励：金属坯件 ×2、精密机构 ×1（背包满时空出位置后自动发放）",Color.Green,false);
    }
    public void Update(float dt) {
        float elapsed=(float)Math.Max(0,time.GameTime-lastTime);lastTime=time.GameTime;
        SyncPlayerCharges();
        // Multiplayer (TacticalNet): the server resolves every defuse and blast; a client counts down its copy of the
        // bombs (beeps), shows its own HUD and sends its defuse key and view.
        bool authority=ScNet.IsAuthority;
        if(authority){
            // Player charges tick and explode in SubsystemScC4; only their defuse is resolved here.
            foreach(var b in playerCharges.Values.ToArray()){
                if(b.Defuser is not {} p)continue;
                var outcome=b.Clock.Advance(b.Charge.Remaining,elapsed,Valid(p,b));
                if(outcome==DefuseResult.Defused){Cancel(b);if(Project.FindSubsystem<SubsystemScC4>(true).Disarm(b.Charge)){playerCharges.Remove(b.Charge);Notice(p,"C4 已拆除",Color.Green,false);}}
                else if(outcome!=DefuseResult.Active)Cancel(b);
            }
            // Existing operations resolve against pre-tick fuse; new presses begin at this frame's game time.
            foreach(var b in Bombs.ToArray()){
                var c=b.Charge;bool defused=false;
                if(b.Defuser is {} p){
                    var outcome=b.Clock.Advance(c.Remaining,elapsed,Valid(p,b));
                    if(outcome==DefuseResult.Defused){Remove(b);audio.PlaySound("Audio/ScCsgoKnives/c4_disarmfinish",1,0,c.Position,16,false);TacticalNet.Sound("Audio/ScCsgoKnives/c4_disarmfinish",1,c.Position,16);
                        Notice(p,"C4 已拆除",Color.Green,false);Reward(p,b);defused=true;}
                    else if(outcome==DefuseResult.Cancelled)Cancel(b);
                }
                if(defused)continue;
                if(c.Remaining<=0||c.Tick(elapsed)){Remove(b);Explode(c);continue;}
                if(c.NextCue() is string cue){StopSound(ref b.Cue);b.Cue=Play(cue,c.Position,.85f);}
            }
        }else foreach(var b in Bombs){var c=b.Charge;if(c.Remaining<=0)continue;c.Tick(elapsed);if(c.Remaining>0&&c.NextCue() is string cue){StopSound(ref b.Cue);b.Cue=Play(cue,c.Position,.85f);}}
        foreach(var p in players.ComponentPlayers){
            if(!ScNet.IsLocal(p)){if(authority&&TacticalNet.Defuse(p) is not null)StepRemote(p);continue;} // another client's player: its client and the server
            var b=Target(p);var h=View(p);h.Panel.IsVisible=b!=null;
            bool down=Down(p);if(!down)h.SuppressUntilRelease=false;
            if(!authority&&TacticalNet.View(p) is {} view)TacticalNet.SendDefuse(down,view);
            bool start=h.Press.Step(down,b is not null&&b.Defuser is null);
            if(b is null){h.Warning=0;h.Input.Cancel();continue;}
            bool kit=HasKit(p);float needed=b.Defuser==p&&b.Clock is not null?b.Clock.Needed:kit?5:10;float margin=b.Charge.Remaining-needed;
            bool other=b.Defuser!=null&&b.Defuser!=p;h.Button.IsVisible=!other&&p.GameWidget.Input.Devices.HasFlag(WidgetInputDevice.Touch);
            h.Bar.IsVisible=b.Defuser==p;h.Bar.Value=b.Clock is null?0:b.Clock.Elapsed/b.Clock.Duration;
            h.Label.Color=margin<=0?Color.Red:margin<1?Color.Yellow:Color.White;
            string action=other?"队友正在拆除":b.Defuser==p?"正在拆除":$"按住 {ScGunBindings.Get(ScGunFunctions.Plant)} 拆除";
            h.Label.Text=$"{action} · {(kit?"拆弹钳":"徒手")}\n爆炸 {b.Charge.Remaining:F1}s / 拆除 {needed:F1}s"+(margin<=0?"\n时间不足！":margin<1?"\n时间紧迫":"");
            int warning=margin<=0?2:margin<1?1:0;
            if(warning>h.Warning&&!other)p.ComponentGui.DisplaySmallMessage(warning==2?"拆除时间不足！仍可按住尝试。":"拆除时间紧迫！",warning==2?Color.Red:Color.Yellow,true,false);h.Warning=warning;
            if(start&&authority)Begin(p,b,kit);
        }
        foreach(var p in hud.Keys.Where(p=>!players.ComponentPlayers.Contains(p)).ToArray()){hud[p].Panel.ParentWidget?.Children.Remove(hud[p].Panel);hud.Remove(p);}
        foreach(var p in remote.Keys.Where(p=>!players.ComponentPlayers.Contains(p)||!ScNet.IsRemoteDriven(p)).ToArray())remote.Remove(p);
        blasts.RemoveAll(b=>time.GameTime-b.Started>ScC4Blast.Lifetime);
        if(authority)Publish();
    }
    bool Valid(ComponentPlayer p,Bomb b)=>players.ComponentPlayers.Contains(p)&&ScGunBindings.ContextAvailable(p)&&Down(p)&&Target(p)==b
        &&Vector3.DistanceSquared(b.Start,p.ComponentBody.Position)<.16f&&(!b.Kit||HasKit(p));
    void Begin(ComponentPlayer p,Bomb b,bool kit){
        b.Defuser=p;b.Kit=kit;b.Clock=new(kit);b.Start=p.ComponentBody.Position;b.Disarm=Play("c4_disarmstart",b.Charge.Position,.75f);
        TacticalNet.Sound("Audio/ScCsgoKnives/c4_disarmstart",.75f,b.Charge.Position,8);
    }
    /// <summary>Server: a remote client's player presses and holds the defuse key as a local player does (no HUD here).</summary>
    void StepRemote(ComponentPlayer p){
        if(!remote.TryGetValue(p,out var h))remote[p]=h=new Hud();
        var b=Target(p);bool down=Down(p);if(!down)h.SuppressUntilRelease=false;
        if(h.Press.Step(down,b is not null&&b.Defuser is null)&&b is not null)Begin(p,b,HasKit(p));
    }
    // ---- multiplayer: the bombs and their defuses, server to clients
    double publishAt;long published=-1;
    void Publish(){
        if(!ScNet.IsHost||ScNet.Peers.Count==0){published=-1;return;}
        var defusing=playerCharges.Values.Where(b=>b.Defuser is not null).ToArray();
        long signature=Bombs.Count+64L*defusing.Length;foreach(var b in Bombs.Concat(defusing))signature=signature*31+(b.Defuser?.PlayerData.PlayerIndex??-1);
        if(signature==published&&(Time.RealTime<publishAt||Bombs.Count==0&&defusing.Length==0))return;
        published=signature;publishAt=Time.RealTime+.25;TacticalNet.BombsSent++;
        ScNet.Broadcast(TacticalNet.OpBombs,w=>{
            w.Int(Bombs.Count);
            foreach(var b in Bombs)w.String(b.BombId).Vector3(b.Charge.Position).Float(b.Charge.Yaw).Float(b.Charge.Remaining).Int(b.Defuser?.PlayerData.PlayerIndex??-1).Bool(b.Kit).Float(b.Clock?.Elapsed??0);
            w.Int(defusing.Length);
            foreach(var b in defusing)w.Int(b.Charge.Owner).Vector3(b.Charge.Position).Int(b.Defuser.PlayerData.PlayerIndex).Bool(b.Kit).Float(b.Clock?.Elapsed??0);
        });
    }
    ComponentPlayer PlayerOf(int index)=>index<0?null:players.ComponentPlayers.FirstOrDefault(p=>p.PlayerData.PlayerIndex==index);
    void ShowDefuse(Bomb b,int defuser,bool kit,float elapsed){
        var p=PlayerOf(defuser);
        if(p is null){if(b.Defuser is not null)Cancel(b);return;}
        if(b.Clock is null||b.Kit!=kit||b.Defuser!=p)b.Clock=new(kit);
        b.Defuser=p;b.Kit=kit;b.Clock.MirrorElapsed(elapsed);
    }
    /// <summary>Client: the server's bombs (kept object by object, so the beeps carry on) and every defuse in progress.</summary>
    public void ApplyNetworkBombs(ScNetReader r){
        TacticalNet.BombsApplied++;
        int n=r.Count(8);var next=new List<Bomb>(n);
        for(int i=0;i<n;i++){
            string id=r.String(64);Vector3 position=r.Vector3();float yaw=r.Float(),remaining=r.Float();int defuser=r.Int();bool kit=r.Bool();float elapsed=r.Float();
            var b=Bombs.FirstOrDefault(x=>x.BombId==id&&!next.Contains(x))??new Bomb{BombId=id};
            b.Charge.Position=position;b.Charge.Yaw=yaw;b.Charge.Remaining=Math.Clamp(remaining,0,b.Charge.Fuse);
            ShowDefuse(b,defuser,kit,elapsed);next.Add(b);
        }
        foreach(var gone in Bombs.Where(b=>!next.Contains(b)).ToArray())Remove(gone);
        Bombs.Clear();Bombs.AddRange(next);
        int m=r.Count(32);var states=new List<(int Owner,Vector3 Position,int Defuser,bool Kit,float Elapsed)>(m);
        for(int i=0;i<m;i++)states.Add((r.Int(),r.Vector3(),r.Int(),r.Bool(),r.Float()));
        SyncPlayerCharges();
        foreach(var b in playerCharges.Values){
            int k=states.FindIndex(x=>x.Owner==b.Charge.Owner&&Vector3.DistanceSquared(x.Position,b.Charge.Position)<1e-4f);
            if(k>=0)ShowDefuse(b,states[k].Defuser,states[k].Kit,states[k].Elapsed);else if(b.Defuser is not null)Cancel(b);
        }
    }
    /// <summary>Client: a blast the server reported (its sound arrives as a world sound).</summary>
    public void ShowBlast(Vector3 position,float radius){if(blasts.Count>=8)blasts.RemoveAt(0);blasts.Add(new(position,radius,time.GameTime));}
    void Explode(ScC4Charge c) {
        if(blasts.Count>=8)blasts.RemoveAt(0);blasts.Add(new(c.Position,c.Radius,time.GameTime));
        foreach(var body in bodies.Bodies.ToArray()){
            var point=body.BoundingBox.Center();var delta=point-c.Position;float damage=ScC4Charge.DamageAt(delta.Length(),c.Power,c.Radius);if(damage<=0)continue;
            if(terrain.Raycast(c.Position+Vector3.UnitY*.15f,point,false,true,(v,d)=>ScGunRange.TerrainStopsBullet(v)).HasValue)continue;
            ScDamageIndicator.AttackBody(new BlastAttack(body,point,delta.LengthSquared()>.001f?Vector3.Normalize(delta):Vector3.UnitY,damage){AttackSoundVolume=0,ImpulseFactor=0});
        }
        audio.PlaySound("Audio/ScCsgoKnives/c4_explode_close_01",2,0,c.Position,32,true);
        TacticalNet.Sound("Audio/ScCsgoKnives/c4_explode_close_01",2,c.Position,32);TacticalNet.Blast(c.Position,c.Radius);
    }
    public void Draw(Camera camera,int order) {
        var block=(ScC4Block)BlocksManager.Blocks[BlocksManager.GetBlockIndex<ScC4Block>(true)];
        foreach(var b in Bombs){if(Vector3.DistanceSquared(camera.ViewPosition,b.Charge.Position)>128*128)continue;var m=ScC4Handoff.Settled(Matrix.CreateRotationY(b.Charge.Yaw)*Matrix.CreateTranslation(b.Charge.Position),b.Charge.Fuse-b.Charge.Remaining,time.GameTime);
            block.DrawPlanted(renderer,Color.White,ref m,new DrawBlockEnvironmentData{SubsystemTerrain=terrain,Light=15});}
        foreach(var b in blasts)b.Draw(renderer,camera,time.GameTime);renderer.Flush(camera.ViewProjectionMatrix);
    }
    public override void Dispose(){ScWeaponActionGate.Reserved-=Blocking;foreach(var b in Bombs.ToArray())Remove(b);foreach(var b in playerCharges.Values)Cancel(b);playerCharges.Clear();foreach(var h in hud.Values)h.Panel.ParentWidget?.Children.Remove(h.Panel);hud.Clear();remote.Clear();base.Dispose();}
}
