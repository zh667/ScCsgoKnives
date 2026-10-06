using Engine;
using Engine.Graphics;
using Engine.Audio;
using TemplatesDatabase;
namespace Game;

public sealed class SubsystemScGrenades : SubsystemBlockBehavior, IUpdateable, IDrawable {
    /// <summary>Bounded NPC throw, using the same flight, smoke/fire interaction and effect budgets.</summary>
    public bool TryThrowHostile(int kind,Vector3 position,Vector3 velocity) {
        if(kind<0||kind>4||!ScGrenadeState.Finite(position)||!ScGrenadeState.Finite(velocity)||!ScGrenadeState.CanAdd(m_active,-2))return false;
        var state=new ScGrenadeState{Kind=kind,Owner=-2,Position=position,Velocity=velocity,Remaining=ScGrenadeBallistics.Fuse(kind)};
        Register(state);m_active.Add(state);m_justReleased.Add(state);return true;
    }
    public static bool IsAreaDamage(Attackment attack)=>attack is AreaAttack;
    public void ChickenBlast(Vector3 position,int owner) {
        var state=new ScGrenadeState{Kind=0,Position=position,Owner=owner};
        Register(state);
        DetonateWithBudget(state, true); // retain the chicken blast budget; same walls/friendly-fire/smoke rules
        m_active.Add(state);
    }
    sealed class AreaAttack(ComponentBody body,GameEntitySystem.Entity owner,Vector3 point,Vector3 direction,float power)
        : ProjectileAttackment(body,owner,point,direction,power,null), IScAttackFacts {
        /// <summary>What the authority knows about the grenade this damage comes from (deathmatch-addon).</summary>
        public ScAttackFacts Facts { get; init; }
        // (The area's own rule - Friendly - already ran; a world's mode decides first, also for a thrower who has left.)
        public override bool DisableFriendlyFire() => ScModes.MayHurt(Attacker,Target,Facts?.AttackerPlayer??-1) switch { true=>false, false=>true, _=>Attacker!=Target && base.DisableFriendlyFire() };
        public override float CalculateInjuryAmount() => ScModes.OwnsInjuries(Target) ? AttackPower : base.CalculateInjuryAmount();
    }
    sealed class Preparation {
        public ScThrowTransaction Transaction;
        public int Kind, Slot, ReturnSlot = -1, Stage = 0;
        public bool Low, Released, FromButton;
        public int InputSource, PadMask;
        public long CommittedRevision;
        /// <summary>The first-person action the pull's sound belongs to (-1: none was started here).</summary>
        public long PinAction = -1;
        public ScGrenadePreparation Timeline;
        public float OriginalPull,OriginalRelease,OriginalThrow;
        // Throw preview cache (never touches the real throw): inputs of the last prediction and its result.
        public ScGrenadeTrajectory.Path Preview;public double PreviewAt=double.NegativeInfinity;
        public Vector3 PreviewView,PreviewDirection,PreviewVelocity;public bool PreviewLow;
    }
    /// <summary>Recomputes a player's own throw preview at most ~12 times a second, or at once when the aim, position,
    /// velocity or throw strength changed noticeably. Only while preparing and not yet released.</summary>
    void UpdatePreview(ComponentPlayer p,Preparation prep) {
        if(!ScUiSettings.GrenadePreview||prep.Released){prep.Preview=null;return;}
        var camera=p.GameWidget?.ActiveCamera;if(camera is null){prep.Preview=null;return;}
        double now=m_time.GameTime;var throwView=ThrowView(p,camera);Vector3 view=throwView.Origin+throwView.ViewPosition,direction=throwView.ViewDirection,velocity=p.ComponentBody.Velocity;
        bool changed=prep.Preview is null||prep.PreviewLow!=prep.Low||Vector3.Dot(direction,prep.PreviewDirection)<.99996f
            ||Vector3.DistanceSquared(view,prep.PreviewView)>.0025f||Vector3.DistanceSquared(velocity,prep.PreviewVelocity)>.04f;
        double age=now-prep.PreviewAt;
        if(prep.Preview is not null&&(changed?age<1/12.0:age<.25))return; // unchanged input still refreshes for terrain edits
        var launch=ScGrenadeBallistics.LaunchFrom(throwView.Origin,throwView.ViewPosition,direction,velocity,prep.Low,(a,b)=>SolidRay(a,b)?.HitPoint());
        prep.Preview=ScGrenadeTrajectory.Predict(prep.Kind,launch.Position,launch.Velocity,SolidRay,Water,
            q=>m_terrain.Terrain.GetChunkAtCell(Terrain.ToCell(q.X),Terrain.ToCell(q.Z)) is {State:>TerrainChunkState.InvalidContents4});
        prep.PreviewAt=now;prep.PreviewView=view;prep.PreviewDirection=direction;prep.PreviewVelocity=velocity;prep.PreviewLow=prep.Low;
    }
    // ImmuneUntil is retained for old save readers and bounded record cleanup only; it never gates a new flash.
    sealed class Blindness { public double Until, ImmuneUntil; public float Duration; }
    readonly Dictionary<ComponentPlayer, Preparation> m_preparing = [];
    readonly Dictionary<ComponentPlayer, ScSlotHistory> m_slots = [];
    readonly Dictionary<(ComponentPlayer Player, bool Low), int> m_throwSources = [];
    readonly Dictionary<ComponentPlayer, int> m_padMasks = [];
    readonly Dictionary<ComponentBody, Blindness> m_blind = [];
    readonly Dictionary<int, Blindness> m_savedBlind = [];
    readonly HashSet<int> m_reducedFlash = [];
    readonly List<ScGrenadeState> m_active = [];
    readonly List<ScSmokeDisturbance> m_disturbances = [];
    int m_nextGrenadeId = 1;
    void Register(ScGrenadeState s) { if (s.Id <= 0) s.Id = m_nextGrenadeId; m_nextGrenadeId = Math.Max(m_nextGrenadeId, s.Id + 1); }
    readonly HashSet<ScGrenadeState> m_justReleased = [];
    readonly PrimitivesRenderer3D m_renderer = new();
    readonly PrimitivesRenderer2D m_overlay = new();
    readonly DrawBlockEnvironmentData m_environment = new();
    readonly Texture2D[] m_effectTextures = new Texture2D[ScGrenadeVisuals.Textures.Length+1];   // + the generated smooth puff
    sealed class FireBurst {public Vector3 Position;public float Age;}
    readonly List<FireBurst> fireBursts=[];
    Sound m_fireLoop;
    readonly Dictionary<ScGrenadeState,List<Vector3>> m_firePoints=[];
    SubsystemTime m_time;
    SubsystemTerrain m_terrain;
    SubsystemPlayers m_players;
    SubsystemBodies m_bodies;
    SubsystemGameInfo m_info;
    public override int[] HandledBlocks => [BlocksManager.GetBlockIndex<ScGrenadeBlock>()];
    public UpdateOrder UpdateOrder => UpdateOrder.Default;
    /// <summary>Grenade bodies draw with the world's other projectiles (10). Smoke, fire and burst sprites draw at
    /// 310: after terrain alpha (100), creature models (99/201), shadows (200) and particles (300) - a sprite
    /// only reads depth, so anything drawn later would paint over the smoke (F07). The overlay is 2D at 1102.</summary>
    public const int EffectsDrawOrder = 310;
    public int[] DrawOrders => [10, EffectsDrawOrder, 1102];
    public static bool Holding(ComponentPlayer player) => Terrain.ExtractContents(player.ComponentMiner.ActiveBlockValue) == BlocksManager.GetBlockIndex<ScGrenadeBlock>(true);
    public static Vector3 ReleaseOrigin(Vector3 viewPosition) => viewPosition;
    /// <summary>Camera farther than this from the thrower's eye is a third-person (or spectating) view.</summary>
    public const float OwnEyeDistance=.75f;
    /// <summary>Where a player's throw leaves from and the view ray that aims it (video-feedback-20260929 R2), shared by
    /// the real release and the preview. First person: both are the camera, exactly as before. A camera away from the
    /// thrower still aims along its own ray - from beside the thrower onward, so nothing between the camera and the
    /// thrower is taken for the aim point - but the grenade leaves from the thrower's eye.</summary>
    public static (Vector3 Origin,Vector3 ViewPosition,Vector3 ViewDirection) ThrowView(ComponentPlayer p,Camera camera) {
        Vector3 position=camera.ViewPosition,direction=camera.ViewDirection;
        Vector3 eye=p.ComponentCreatureModel is {} model?model.EyePosition:Eye(p.ComponentBody);
        if(!ScGrenadeState.Finite(eye)||Vector3.DistanceSquared(position,eye)<=OwnEyeDistance*OwnEyeDistance)return(ReleaseOrigin(position),position,direction);
        return(eye,position+direction*Math.Max(0,Vector3.Dot(eye-position,direction)),direction);
    }
    /// <summary>Presentation of the throw this player is preparing, on the gameplay clock; default when there is none.
    /// Readers draw with it and change nothing.</summary>
    public ScThrowPhase ThrowPhase(ComponentPlayer p) {
        if(p is null||!m_preparing.TryGetValue(p,out var prep)||prep.Timeline is null)return default;
        double now=m_time.GameTime;var t=prep.Timeline;int stage=t.Stage(now);
        float pull=t.Pulled(now);
        float wind=stage<2?0:(float)Math.Clamp((now-t.ThrowStartedAt)/Math.Max(1e-5,t.ReleaseAt-t.ThrowStartedAt),0,1);
        float follow=stage<2?0:(float)Math.Clamp((now-t.ReleaseAt)/Math.Max(1e-5,t.EndAt-t.ReleaseAt),0,1);
        return new(ScGrenadeBlock.Value(prep.Kind),ScGrenadeBlock.Assets[prep.Kind],stage,prep.Low,prep.Released,pull,stage==1?(float)(now-t.ReadyAt):0,wind,follow);
    }
    public int ViewmodelValue(ComponentPlayer p, int value) => m_preparing.TryGetValue(p,out var prep) && prep.Released
        && p.ComponentMiner.Inventory.ActiveSlotIndex==prep.Slot && p.ComponentMiner.ActiveBlockValue==0 && Operable(p) ? ScGrenadeBlock.Value(prep.Kind) : value;
    static bool Operable(ComponentPlayer p) => p.ComponentHealth.Health > 0 && p.ComponentGui.ModalPanelWidget is null && !DialogsManager.HasDialogs(p.GuiWidget);
    static Vector3 Eye(ComponentBody b) => b.Position + Vector3.UnitY * b.BoxSize.Y * .85f;
    public override void Load(ValuesDictionary values) {
        base.Load(values); m_time=Project.FindSubsystem<SubsystemTime>(true); m_terrain=Project.FindSubsystem<SubsystemTerrain>(true);
        m_players=Project.FindSubsystem<SubsystemPlayers>(true); m_bodies=Project.FindSubsystem<SubsystemBodies>(true); m_info=Project.FindSubsystem<SubsystemGameInfo>(true);
        var saved=values.GetValue<ValuesDictionary>("Grenades",null);
        if (saved is not null) foreach (var item in saved) {
            if (item.Value is ValuesDictionary d && ScGrenadeState.Load(d) is ScGrenadeState state && ScGrenadeState.CanAdd(m_active,state.Owner)) { m_active.Add(state); Register(state); }
        }
        m_nextGrenadeId=Math.Max(m_nextGrenadeId,values.GetValue<int>("NextGrenadeId",1));
        var openings=values.GetValue<ValuesDictionary>("SmokeDisturbances",null);
        if (openings is not null) foreach (var item in openings) {
            if (item.Value is ValuesDictionary d && ScSmokeDisturbance.Load(d) is ScSmokeDisturbance opening && ScSmokeDisturbance.CanAdd(m_disturbances)) m_disturbances.Add(opening);
        }
        foreach (string s in values.GetValue<string>("ReducedFlash", "").Split(',')) if (int.TryParse(s,out int i)) m_reducedFlash.Add(i);
        var flashes=values.GetValue<ValuesDictionary>("Blindness",null);
        if (flashes is not null) foreach (var pair in flashes) if (int.TryParse(pair.Key,out int id) && pair.Value is ValuesDictionary d) {
            float left=d.GetValue<float>("Left",0),immune=d.GetValue<float>("Immune",0),duration=d.GetValue<float>("Duration",0);
            if (float.IsFinite(left) && float.IsFinite(immune) && float.IsFinite(duration))
                m_savedBlind[id]=new Blindness {Until=m_time.GameTime+Math.Clamp(left,0,ScGrenadeState.FlashMaximum),ImmuneUntil=m_time.GameTime+Math.Clamp(immune,0,ScGrenadeState.FlashMaximum+ScGrenadeState.FlashImmunity),Duration=Math.Clamp(duration,.01f,ScGrenadeState.FlashMaximum)};
        }
    }
    public override void Save(ValuesDictionary values) {
        base.Save(values); var saved=new ValuesDictionary();
        for (int i=0;i<m_active.Count;i++) saved.SetValue(i.ToString(),m_active[i].Save());
        values.SetValue("Grenades",saved); values.SetValue("ReducedFlash",string.Join(",",m_reducedFlash));
        var openings=new ValuesDictionary();
        for (int i=0;i<m_disturbances.Count;i++) if (m_disturbances[i].Active) openings.SetValue(i.ToString(),m_disturbances[i].Save());
        values.SetValue("SmokeDisturbances",openings); values.SetValue("NextGrenadeId",m_nextGrenadeId);
        var flashes=new ValuesDictionary();
        void SaveBlind(int id,Blindness blind) {
            if (blind.ImmuneUntil<=m_time.GameTime) return;
            var d=new ValuesDictionary();d.SetValue("Left",(float)Math.Max(0,blind.Until-m_time.GameTime));
            d.SetValue("Immune",(float)(blind.ImmuneUntil-m_time.GameTime));d.SetValue("Duration",blind.Duration);flashes.SetValue(id.ToString(),d);
        }
        foreach (var pair in m_savedBlind) SaveBlind(pair.Key,pair.Value);
        foreach (var pair in m_blind) SaveBlind(pair.Key.Entity.Id,pair.Value);
        values.SetValue("Blindness",flashes);
        // Preparations are intentionally absent: inventory has changed only for released throws.
    }
    public void SetThrowButton(ComponentPlayer player, bool low, bool pressed, bool clicked, bool cancelled, int sources = 2) {
        m_throwSources[(player, low)] = pressed || clicked ? sources : 0;
        if (cancelled) {
            if (m_preparing.TryGetValue(player, out var prep) && prep.FromButton && prep.Low == low) Cancel(player, prep);
            return;
        }
        if (pressed || clicked) RequestThrow(player, low, true);
    }
    public void RequestThrow(ComponentPlayer player, bool low, bool fromButton = false) {
        int requested = fromButton ? m_throwSources.GetValueOrDefault((player, low)) : 1;
        if (requested == 0 || !ThrowInputReady(player, requested) || !Holding(player) || !Operable(player) || m_preparing.ContainsKey(player)) {
            if (ScNet.IsRemoteClient) ScNet.Trace($"grenade request refused: requested {requested} ready {requested != 0 && ThrowInputReady(player, requested)} holding {Holding(player)} operable {Operable(player)} preparing {m_preparing.ContainsKey(player)}");
            return;
        }
        // mp-user-logs-20261002: only the server creates a grenade. A client whose CS network layer is not accepted starts
        // no throw at all (it showed the whole throw and nothing flew: "the grenade vanished") and is told why.
        if (ScNet.IsLocal(player) && ScNet.ClientBlocked) { ScNet.Trace("grenade request refused: client blocked"); ScNet.TellBlocked(player); return; }
        int source = m_releaseGates[player].AllowedSources & requested;
        source &= -source; // Keep the initiating source's release independent from other held controls.
        int kind=ScGrenadeBlock.Kind(player.ComponentMiner.ActiveBlockValue);
        if (!ScGrenadeBlock.Enabled(kind)) return;
        var model=player.Entity.FindComponent<ComponentFirstPersonModel>();
        if (!KnifeAnimationController.CanStartGrenade(model,player.ComponentMiner.ActiveBlockValue)) { if (ScNet.IsRemoteClient) ScNet.Trace("grenade request refused: viewmodel busy"); return; }
        // A throw may be asked for while the grenade is still being drawn (the pull replaces the draw at once), but the
        // grenade leaves no earlier than the draw would have ended: letting go throws at once only with it in hand.
        double drawing=KnifeAnimationController.GrenadeDrawRemaining(model);
        int padMask=source == 8 || !fromButton && (player.GameWidget.Input.IsGamepadDown("Dig") || player.GameWidget.Input.IsGamepadDown("Hit") || player.GameWidget.Input.IsGamepadDown("Aim")) ? ScGamepadBindings.ConnectedMask(player) : 0;
        Prepare(player,kind,low,fromButton,source,padMask,drawing);
        
        // Multiplayer: a remote client shows its throw and the server runs the same one from what it sends.
        if(ScNet.IsRemoteClient && player.GameWidget?.ActiveCamera is {} camera && !ScNetGrenades.SendStart(low,ThrowView(player,camera))) {
            // The request did not leave: no grenade will exist, so the throw shown here is taken back.
            if(m_preparing.TryGetValue(player,out var unsent))Cancel(player,unsent);
            ScNet.TellBlocked(player);
        }
    }
    Preparation Prepare(ComponentPlayer player,int kind,bool low,bool fromButton,int source,int padMask,double drawing=0) {
        string asset=ScGrenadeBlock.Assets[kind], alias=low?"throwLow":"throwHigh";
        float pull=Cs2Rig.Duration(asset,"pullpin");
        var inv=player.ComponentMiner.Inventory;
        var prep=m_preparing[player]=new Preparation { Transaction=new ScThrowTransaction(inv),Kind=kind,Low=low,Slot=inv.ActiveSlotIndex,
            ReturnSlot=m_slots.TryGetValue(player,out var history)?history.Previous:-1, FromButton=fromButton,
            InputSource=source, PadMask=padMask,
            OriginalPull=pull,OriginalRelease=Cs2Rig.GrenadeReleaseTime(asset,alias),OriginalThrow=Cs2Rig.Duration(asset,alias),
            Timeline=ScGrenadePreparation.Create(m_time.GameTime,pull,Cs2Rig.GrenadeReleaseTime(asset,alias),Cs2Rig.Duration(asset,alias),false,kind==3,drawing) };
        KnifeAnimationController.GrenadeAction(player,"pullpin");
        if(ScNet.IsLocal(player)) {
            // The pull's sound belongs to the pull: a throw that begins before the pull is through takes it away (as CS2
            // stops the clip's sound with the clip), instead of a pin being pulled after the grenade has left.
            var hands=player.Entity.FindComponent<ComponentFirstPersonModel>();
            prep.PinAction=KnifeAnimationController.ActionToken(hands);
            ScPresentationSound.PlayHeld(hands,prep.PinAction,asset+"_pin");
        }
        return prep;
    }
    /// <summary>Server: a remote client started a throw with the grenade it holds (its own checks ran on its side; the
    /// release still needs the item here, the same slot and an unchanged inventory).</summary>
    public void StartRemoteThrow(ComponentPlayer player, bool low) {
        // That client's previous throw may still be in its follow-through here: this end began it later than the client
        // did, by the link's delay, so the client's next start can arrive just before it ends. The grenade is out and the
        // rest is only shown, so the next throw begins (no earlier than ScNetGrenades.TailLead before that end).
        if (m_preparing.TryGetValue(player, out var last) && last.Released && m_time.GameTime >= last.Timeline.EndAt - ScNetGrenades.TailLead) m_preparing.Remove(player);
        if (!Holding(player) || player.ComponentHealth.Health <= 0 || m_preparing.ContainsKey(player)) {
            ScNet.Trace($"grenade P{player.PlayerData.PlayerIndex} start refused: holding {Holding(player)} (active {player.ComponentMiner.ActiveBlockValue}) preparing {m_preparing.ContainsKey(player)}"); return; }
        int kind=ScGrenadeBlock.Kind(player.ComponentMiner.ActiveBlockValue);
        if (!ScGrenadeBlock.Enabled(kind)) return;
        Prepare(player,kind,low,false,1,0);
        ScNet.Trace($"grenade P{player.PlayerData.PlayerIndex} prepared kind {kind}");
    }
    public void CancelRemoteThrow(ComponentPlayer player) { if (m_preparing.TryGetValue(player, out var prep) && !prep.Released) Cancel(player, prep); }
    /// <summary>Client: this player's own flash blindness as the server measured it.</summary>
    public void BlindLocal(ComponentPlayer player, float duration) => ApplyBlindness(player.ComponentBody,duration);
    /// <summary>Every hit restarts white; a weaker hit preserves the stronger hit's remaining time.</summary>
    float ApplyBlindness(ComponentBody body,float duration) {
        if (!float.IsFinite(duration) || duration <= .05f) return 0;
        double now=m_time.GameTime;
        double until=Math.Max(now+Math.Min(duration,ScGrenadeState.FlashMaximum),m_blind.TryGetValue(body,out var old)?old.Until:now);
        float remaining=(float)(until-now);
        m_blind[body]=new Blindness {Until=until,Duration=remaining,ImmuneUntil=until+ScGrenadeState.FlashImmunity};
        return remaining;
    }
    public void AddFireBurst(Vector3 position) { if (fireBursts.Count >= 24) fireBursts.RemoveAt(0); fireBursts.Add(new FireBurst { Position = position }); }
    /// <summary>Client: the server's grenades replace this client's copies (same objects kept by id, so local motion and
    /// fire patches carry on smoothly).</summary>
    public void ApplyNetworkSnapshot(ScNetReader r) {
        int n = r.Count(256);
        var byId = m_active.ToDictionary(s => s.Id);
        var seen = new List<ScGrenadeState>(n);
        for (int i = 0; i < n; i++) {
            int id = r.Int(); int kind = r.Byte(); int owner = r.Int();
            Vector3 position = r.Vector3(), velocity = r.Vector3(); float remaining = r.Float(), age = r.Float(); bool effect = r.Bool(), grounded = r.Bool();
            if (kind is < 0 or > 5) continue;
            if (!byId.TryGetValue(id, out var s)) s = new ScGrenadeState { Id = id };
            s.Kind = kind; s.Owner = owner; s.Position = position; s.Velocity = velocity; s.Remaining = remaining; s.Age = age; s.Effect = effect; s.Grounded = grounded;
            seen.Add(s);
        }
        foreach (var gone in m_active.Except(seen).ToArray()) { m_firePoints.Remove(gone); }
        m_active.Clear(); m_active.AddRange(seen);
        int m = r.Count(32);
        m_disturbances.Clear();
        for (int i = 0; i < m; i++) {
            var opening = new ScSmokeDisturbance { Center = r.Vector3(), Remaining = r.Float() };
            int k = r.Count(16);
            for (int j = 0; j < k; j++) opening.SmokeIds.Add(r.Int());
            m_disturbances.Add(opening);
        }
    }
    /// <summary>A grenade's world sound: here, and (server) for every multiplayer client.</summary>
    void WorldSound(string path, float volume, Vector3 position, float range) {
        Project.FindSubsystem<SubsystemAudio>(true).PlaySound(path, volume, 0, position, range, true);
        if (ScNet.IsHost) ScNetGrenades.Sound(path, volume, position, range);
    }
    static void Message(ComponentPlayer p,string text) => p.ComponentGui.DisplaySmallMessage(text,Color.White,true,false);
    readonly Dictionary<ComponentPlayer, ScTriggerReleaseGate> m_releaseGates = new();
    static readonly string[] ThrowActions = [ScGunFunctions.Fire, ScGunFunctions.ThrowStrong, ScGunFunctions.ThrowWeak];
    bool ThrowInputReady(ComponentPlayer p, int requested = 0) {
        if (!m_releaseGates.TryGetValue(p, out var gate)) m_releaseGates[p] = gate = new();
        var inv = p.ComponentMiner.Inventory;
        var input = p.ComponentInput.PlayerInput;
        int down = requested | (input.Dig.HasValue || input.Hit.HasValue || input.Aim.HasValue ? 1 : 0)
            | m_throwSources.GetValueOrDefault((p, false)) | m_throwSources.GetValueOrDefault((p, true));
        foreach (string action in ThrowActions) {
            if (ScGunBindings.KeyboardDown(p, action)) down |= 4;
            if (ScGamepadBindings.Down(p, action, false)) down |= 8;
        }
        if (p.Project.FindSubsystem<SubsystemScGunBlockBehavior>(false)?.FireButtonDown(p) == true) down |= 2;
        int pads = ScGamepadBindings.ConnectedMask(p);
        bool devicesStable = !m_padMasks.TryGetValue(p, out int previous) || previous == pads;
        m_padMasks[p] = pads;
        return gate.ObserveSources(inv, inv?.ActiveSlotIndex ?? -1, p.ComponentMiner.ActiveBlockValue, down, ScGunBindings.Available(p) && devicesStable, requested);
    }
    void Cancel(ComponentPlayer p, Preparation prep) {
        if (!prep.Released && ScNet.IsRemoteClient && ScNet.IsLocal(p)) ScNetGrenades.SendCancel();
        prep.Transaction.Cancel();m_preparing.Remove(p);
        if (p.ComponentMiner.Inventory.ActiveSlotIndex==prep.Slot && (Holding(p) || p.ComponentMiner.ActiveBlockValue==0)) KnifeAnimationController.CancelAction(p);
    }
    public void Update(float dt) {
        if (m_savedBlind.Count>0) {
            foreach (var body in m_bodies.Bodies) if (m_savedBlind.Remove(body.Entity.Id,out var blind) && blind.ImmuneUntil>m_time.GameTime) m_blind[body]=blind;
            foreach (var pair in m_savedBlind.Where(p=>p.Value.ImmuneUntil<=m_time.GameTime).ToArray()) m_savedBlind.Remove(pair.Key);
        }
        bool authority = ScNet.IsAuthority;
        foreach (var p in m_players.ComponentPlayers) {
            if (ScNet.IsLocal(p)) ThrowInputReady(p); // devices are this process's own players'
            if (!m_slots.TryGetValue(p,out var history)) m_slots[p]=history=new ScSlotHistory();
            history.Observe(p.ComponentMiner.Inventory.ActiveSlotIndex,m_time.GameTime);
        }
        foreach (var pair in m_preparing.ToArray()) {
            var p=pair.Key; var prep=pair.Value;
            var remote=ScNetGrenades.RemoteThrow(p);
            // A remote client that stopped renewing its held throw no longer holds it: cancelled, nothing thrown or taken.
            if (remote is not null && !prep.Released && ScNetGrenades.HeldExpired(p)) { Cancel(p,prep); continue; }
            // A remote client's player (server) has no menus here; its client reports whether it may act.
            if (!ScGunBindings.Available(p) || remote is null && !Operable(p) || (!prep.Released && (prep.PadMask != 0 && prep.PadMask != ScGamepadBindings.ConnectedMask(p) || !prep.Transaction.Valid))
                || prep.Released && (p.ComponentMiner.Inventory.ActiveSlotIndex!=prep.Slot
                    || authority && ScInventoryTransaction.Revision(p.ComponentMiner.Inventory)!=prep.CommittedRevision)) {
                if (remote is not null) ScNet.Trace($"grenade P{p.PlayerData.PlayerIndex} cancelled: available {ScGunBindings.Available(p)} valid {prep.Transaction.Valid} released {prep.Released} slot {p.ComponentMiner.Inventory.ActiveSlotIndex}/{prep.Slot}");
                Cancel(p,prep); continue; }
            var input=p.ComponentInput.PlayerInput;
            bool pressed=remote is not null ? remote.Pressed : prep.FromButton ? (m_throwSources.GetValueOrDefault((p,prep.Low)) & prep.InputSource) != 0
                : prep.Low ? input.Aim.HasValue : input.Dig.HasValue || input.Hit.HasValue;
            prep.Timeline.Step(m_time.GameTime,pressed);
            // (A client.) The server is told when this end's throw begins - its own input and its own gates decided that -
            // not what the button does: it throws when that arrives, and never has to tell a tap from a hold by the
            // spacing of the messages it happens to receive.
            if (ScNet.IsRemoteClient && !prep.Released && p.GameWidget?.ActiveCamera is {} own) ScNetGrenades.SendHeld(!prep.Timeline.Throwing,ThrowView(p,own));
            int stage=prep.Timeline.Stage(m_time.GameTime);
            if (stage != prep.Stage) {
                prep.Stage=stage;
                if (stage==2 && prep.Timeline.Quick && prep.PinAction>=0)
                    ScPresentationSound.Release(p.Entity.FindComponent<ComponentFirstPersonModel>(),"thrown before the pull was through",onlyAction:prep.PinAction);
                KnifeAnimationController.GrenadeAction(p,stage==0?"pullpin":stage==1?(prep.Low?"holdLow":"holdHigh"):(prep.Low?"throwLow":"throwHigh"),
                    prep.Timeline.ClipElapsed(m_time.GameTime,prep.OriginalPull,prep.OriginalRelease,prep.OriginalThrow));
            }
            KnifeAnimationController.ScrubGrenade(p,stage==0?"pullpin":stage==1?(prep.Low?"holdLow":"holdHigh"):(prep.Low?"throwLow":"throwHigh"),
                prep.Timeline.ClipElapsed(m_time.GameTime,prep.OriginalPull,prep.OriginalRelease,prep.OriginalThrow));
            try{UpdatePreview(p,prep);}catch(Exception e){prep.Preview=null;KnifeDiagnostics.WarnOnce("grenade-preview",e.Message);} // never affects the real throw
            bool thrownHere=false; // this process's own player let go this frame (one controller pulse, either branch)
            if (!prep.Released && m_time.GameTime>=prep.Timeline.ReleaseAt && !authority) {
                // A remote client's own throw: shown as released; the item and the grenade are the server's.
                prep.Released=true;
                thrownHere=true;
                prep.CommittedRevision=ScInventoryTransaction.Revision(p.ComponentMiner.Inventory);
                AudioManager.PlaySound("Audio/ScCsgoKnives/"+ScGrenadeBlock.Assets[prep.Kind]+"_throw",1,0,0);
            }
            if (!prep.Released && m_time.GameTime>=prep.Timeline.ReleaseAt) {
                // Spawn at the camera view position so the projectile truly leaves
                // from the crosshair.  The previous hand-origin solve made throws
                // appear offset on mobile and could place the grenade behind a wall.
                // A third-person camera is not where the thrower stands: see ThrowView.
                // A remote client's throw (server) leaves from the view that client sent.
                var throwView=remote is {HasView:true} sent ? (sent.Origin,sent.ViewPosition,sent.ViewDirection) : ThrowView(p,p.GameWidget.ActiveCamera);
                var (pos,velocity)=ScGrenadeBallistics.LaunchFrom(throwView.Item1,throwView.Item2,throwView.Item3,p.ComponentBody.Velocity,prep.Low,(a,b)=>SolidRay(a,b)?.HitPoint());
                var state=new ScGrenadeState { Kind=prep.Kind,Owner=p.PlayerData.PlayerIndex,Position=pos,
                    Velocity=velocity,Remaining=ScGrenadeBallistics.Fuse(prep.Kind) };
                // The world's mode may end a spawn protection first, or refuse the throw (nothing flies, nothing is taken).
                if (!ScModes.AcceptAttack(p,ScAttackKind.Throw)) { Cancel(p,prep);continue; }
                if (!prep.Transaction.Commit(m_info.WorldSettings.GameMode==GameMode.Creative && ScModes.Of(Project)?.CountsThrowables(p)!=true,
                    ()=>ScGrenadeState.CanAdd(m_active,state.Owner),()=>{ Register(state);m_active.Add(state);m_justReleased.Add(state);return true; })) {
                    ScNetFeedback.Tell(p,"投掷取消：物品已移动或活动数量已满，未消耗物品。",Color.White);Cancel(p,prep);continue;
                }
                ScModes.AttackCommitted(p,ScAttackKind.Throw,ScGrenadeBlock.Value(prep.Kind));
                prep.Released=true;
                thrownHere=ScNet.IsLocal(p);
                ScAgentVoice.EmitPlayer(p,ScGrenadeBlock.Assets[prep.Kind]); // only after the throw committed; cancels never speak
                prep.CommittedRevision=ScInventoryTransaction.Revision(p.ComponentMiner.Inventory);
                if (ScNet.IsLocal(p)) AudioManager.PlaySound("Audio/ScCsgoKnives/"+ScGrenadeBlock.Assets[prep.Kind]+"_throw",1,0,0);
                else WorldSound("Audio/ScCsgoKnives/"+ScGrenadeBlock.Assets[prep.Kind]+"_throw",.8f,pos,8);
                
                if (remote is not null) ScNet.Trace($"grenade P{p.PlayerData.PlayerIndex} released id {state.Id}");
            }
            if (thrownHere) ScControllerFeedback.Thrown(p);
            if (m_time.GameTime>=prep.Timeline.EndAt) {
                m_preparing.Remove(p);
                var inv=p.ComponentMiner.Inventory;
                // F02: a finished throw returns to the slot held before the grenade slot, whatever it holds now;
                // with no such slot the player stays put. A manual switch, an open screen or an inventory
                // change already cancelled the preparation above, so nothing is switched then.
                if (prep.Released && inv.ActiveSlotIndex==prep.Slot && (Holding(p)||inv.GetSlotCount(prep.Slot)==0)) {
                    int target=ScGrenadeBallistics.FollowUpSlot(inv.SlotsCount,prep.Slot,prep.ReturnSlot);
                    if (target>=0) inv.ActiveSlotIndex=target;
                    else if (Holding(p)) KnifeAnimationController.GrenadeAction(p,"deploy");
                }
            }
        }
        if (!authority) {foreach(var player in m_players.ComponentPlayers)ScNetGrenades.SendFlashView(player);UpdateMirror(dt); return; }
        // Order inside one update: fire validity (water/smoke extinguish) -> grenade motion -> heat trigger
        // -> fuse and smoke growth. The order is fixed here, not by list position.
        ExtinguishFires();
        foreach(var burst in fireBursts)burst.Age+=ScGrenadeBallistics.Step(dt);
        fireBursts.RemoveAll(b=>b.Age>=ScGrenadeVisuals.FireBurstLifetime);
        foreach (var opening in m_disturbances) opening.Remaining-=ScGrenadeBallistics.Step(dt);
        m_disturbances.RemoveAll(o=>!o.Active);
        foreach (var s in m_active.ToArray()) {
            if (m_justReleased.Remove(s)) continue; // this frame's elapsed time preceded the release
            // Physics, age and fuse advance by one clamped step, so a stall cannot pop a grenade that barely moved (F03).
            float step=ScGrenadeBallistics.Step(dt);
            s.Age+=step;
            if (!s.Effect) {
                for (float remaining=step;remaining>0;) {
                    float sub=Math.Min(.02f,remaining);Vector3 before=s.Position;Move(s,sub);remaining-=sub;
                    if(s.Kind==2 && m_active.Any(f=>ScFireArea.HeatedOnPath(f,before,s.Position,Clear))) {Detonate(s);break;}
                }
                if(s.Effect)continue;
                if (s.Kind is 3 or 4) {
                    if (Water(s.Position)) { RemoveEffect(s,true);continue; }
                    if (s.Grounded) { Detonate(s);continue; }
                }
                if (s.Kind==2) {
                    // F05: a smoke grenade that reaches a live, reachable fire area pops now, once, wherever it is.
                    var fire=m_active.FirstOrDefault(f=>ScFireArea.HeatedOnPath(f,s.Position,s.Position,Clear));
                    if (fire is not null) {
                        Detonate(s);continue;
                    }
                }
            }
            if (s.Effect && s.Kind==5 && (int)s.Age>(int)(s.Age-step)) DecoyPulse(s);
            s.Remaining-=step;
            if (s.Remaining<=0) {
                s.Remaining=0;
                if (!s.Effect && s.Kind is 2 or 5 && !ScGrenadeBallistics.Settled(s)) {
                    // F04: smoke and decoy pop only after resting on support. A grenade that never comes to rest
                    // (wedged, endless slope) is cleaned up after the timeout without any effect - never popped mid-air.
                    if (s.Age<ScGrenadeBallistics.SettleTimeout) continue;
                    KnifeLog.Warning($"grenade kind {s.Kind} id {s.Id} never settled within {ScGrenadeBallistics.SettleTimeout:0} s at {s.Position} (grounded={s.Grounded} rested={s.Rested:0.00} v={s.Velocity.Length():0.00}); removed without effect");
                    RemoveEffect(s,false);continue;
                }
                if (!s.Effect) Detonate(s); else RemoveEffect(s,false);
            }
        }
        UpdateFire(ScGrenadeBallistics.Step(dt)); // newly emitted smoke removes fire before this frame's damage
        if (m_active.Any(s=>s.Effect && s.Kind==2)) foreach (var body in m_bodies.Bodies) {
            var chase=body.Entity.FindComponent<ComponentChaseBehavior>();
            if (chase?.m_target is not null) ApplyChaseOcclusion(chase);
            // These vanilla sight behaviours have no scoring hook. Clear only a
            // hidden visual target; their sound/flee behaviours remain independent.
            var avoid=body.Entity.FindComponent<ComponentAvoidPlayerBehavior>();
            if (avoid?.m_target is not null && ScSmokeVolume.Blocks(m_active,Eye(body),Eye(avoid.m_target.ComponentBody),Clear,m_disturbances)) {
                bool active=avoid.IsActive;avoid.m_target=null;avoid.m_importanceLevel=0;
                if (active) avoid.m_componentPathfinding.Stop();
            }
            var find=body.Entity.FindComponent<ComponentFindPlayerBehavior>();
            if (find?.m_target is not null && ScSmokeVolume.Blocks(m_active,Eye(body),Eye(find.m_target.ComponentBody),Clear,m_disturbances)) {
                bool active=find.IsActive;find.m_target=null;find.m_importanceLevel=0;
                if (active) find.m_componentPathfinding.Stop();
            }
        }
        foreach (var pair in m_blind.ToArray()) {
            if (m_time.GameTime>=pair.Value.ImmuneUntil || !m_bodies.Bodies.Contains(pair.Key)) { m_blind.Remove(pair.Key);continue; }
            if (m_time.GameTime<pair.Value.Until) {
                var chase=pair.Key.Entity.FindComponent<ComponentChaseBehavior>();
                if (chase?.m_target is not null) { chase.m_componentPathfinding.Stop();chase.StopAttack(); }
            }
        }
        ScNetGrenades.ServerTick(m_active,m_disturbances);
    }
    /// <summary>A remote multiplayer client: the server's grenades move and age here between its snapshots; nothing detonates,
    /// burns or blinds anyone here (those results come from the server).</summary>
    void UpdateMirror(float dt) {
        float step=ScGrenadeBallistics.Step(dt);
        foreach(var burst in fireBursts)burst.Age+=step;
        fireBursts.RemoveAll(b=>b.Age>=ScGrenadeVisuals.FireBurstLifetime);
        foreach (var opening in m_disturbances) opening.Remaining-=step;
        m_disturbances.RemoveAll(o=>!o.Active);
        foreach (var s in m_active) {
            s.Age+=step;
            if (s.Effect) { s.Remaining=Math.Max(0,s.Remaining-step); continue; }
            for (float remaining=step;remaining>0;) { float sub=Math.Min(.02f,remaining); Move(s,sub); remaining-=sub; }
        }
        FireLoop(m_active.Where(ScFireArea.IsFire).ToArray());
        foreach (var pair in m_blind.ToArray()) {
            if (m_time.GameTime>=pair.Value.ImmuneUntil) { m_blind.Remove(pair.Key); continue; }
        }
    }
    TerrainRaycastResult? SolidRay(Vector3 a,Vector3 b) => m_terrain.Raycast(a,b,false,true,(value,_)=>BlocksManager.Blocks[Terrain.ExtractContents(value)].IsCollidable_(value));
    bool Clear(Vector3 a,Vector3 b) => !SolidRay(a,b).HasValue;
    bool Water(Vector3 p) => BlocksManager.Blocks[Terrain.ExtractContents(m_terrain.Terrain.GetCellValue(Terrain.ToCell(p.X),Terrain.ToCell(p.Y),Terrain.ToCell(p.Z)))] is WaterBlock;
    void Move(ScGrenadeState s,float dt) {
        ScGrenadeBallistics.Integrate(s,dt,SolidRay,Water,
            (a,b)=>m_bodies.Raycast(a,b,.08f,(body,_)=>s.Age>.3f || body.Entity.FindComponent<ComponentPlayer>()?.PlayerData.PlayerIndex!=s.Owner),out bool bounce);
        if (bounce) {
            string sound=ScGrenadeBlock.Assets[s.Kind is 0 or 1 or 2?s.Kind:1];
            Project.FindSubsystem<SubsystemAudio>(true).PlaySound("Audio/ScCsgoKnives/"+sound+"_bounce",.35f,0,s.Position,3,true);
        }
    }
    bool Friendly(ScGrenadeState s,ComponentBody body) {
        // A world's mode decides who its areas reach (deathmatch-addon); without one, the factions and the world setting.
        // (Asked only in a world that runs a mode: everywhere else this method reads exactly what it always read.)
        if(ScModes.Of(Project) is {} mode && mode.MayHurt(m_players?.ComponentPlayers.FirstOrDefault(p=>p.PlayerData.PlayerIndex==s.Owner)?.Entity,body.Entity,s.Owner) is bool ruled)return ruled;
        var side=ScFactions.Of(body.Entity);int index=body.Entity.FindComponent<ComponentPlayer>()?.PlayerData?.PlayerIndex??-1;
        // Only another player or a recruited ally consults the world setting; enemy areas and other creatures never do.
        if(s.Owner==ScFactions.EnemySource||side is ScFactions.Side.Neutral or ScFactions.Side.Enemy||side==ScFactions.Side.Player&&index==s.Owner)
            return ScFactions.AreaAllowed(s.Owner,side,index,false);
        return ScFactions.AreaAllowed(s.Owner,side,index,m_info.WorldSettings.IsFriendlyFireEnabled);
    }
    void Damage(ScGrenadeState s,ComponentBody body,float power,bool fire=false) {
        if (power<=0 || !Friendly(s,body)) return;
        var owner=m_players.ComponentPlayers.FirstOrDefault(p=>p.PlayerData.PlayerIndex==s.Owner);
        Vector3 direction=Eye(body)-s.Position; direction=direction.LengthSquared()>.0001f?Vector3.Normalize(direction):Vector3.UnitY;
        var attack=new AreaAttack(body,owner?.Entity,Eye(body),direction,power) {
            Facts=new ScAttackFacts{Kind=fire?ScAttackKind.Fire:ScAttackKind.Explosion,WeaponValue=ScGrenadeBlock.Value(s.Kind),Weapon=ScGrenadeBlock.Assets[s.Kind],
                AttackerPlayer=s.Owner,AttackId=s.Id,Distance=Vector3.Distance(s.Position,Eye(body))},
            StunTimeSet=0,StunTimeAdd=0,ImpulseFactor=0,AllowImpulseAndStunWhenDamageIsZero=false,
            EnableHitValueParticleSystem=!fire,AttackSoundVolume=fire?0:1 };
        ScDamageIndicator.AttackBody(attack);
    }
    void Detonate(ScGrenadeState s) => DetonateWithBudget(s, false);
    void DetonateWithBudget(ScGrenadeState s,bool chicken) {
        if (s.Kind is 3 or 4) {
            if(s.Effect)return;
            // Contact ignition and the airborne fuse are distinct presentations.
            // A bottle breaking on the floor must never emit the airborne fireball,
            // and a timeout in flight must never play the bottle's impact sound.
            bool airborne=!s.Grounded;
            if(airborne){
                AddFireBurst(s.Position);
                if(ScNet.IsHost)ScNetGrenades.Burst(s.Position);
            }
            string sound=airborne?"grenade_fire_airburst":ScGrenadeBlock.Assets[s.Kind]+"_explode";
            WorldSound("Audio/ScCsgoKnives/"+sound,1,s.Position,6);
            
            // A bounded airborne timeout may ignite a reachable floor below it,
            // never an unsupported sphere of fire in mid-air.
            var floor=SolidRay(s.Position+Vector3.UnitY*.1f,s.Position-Vector3.UnitY*4);
            if (!floor.HasValue || CellFace.FaceToVector3(floor.Value.CellFace.Face).Y<.5f) { RemoveEffect(s,false);return; }
            s.Position=floor.Value.HitPoint()+Vector3.UnitY*.06f;
            if (Water(s.Position)) { RemoveEffect(s,true);return; }
            s.Effect=true;s.Remaining=ScFireArea.Lifetime(s.Kind);s.Age=0;s.Velocity=Vector3.Zero;
            if(m_active.Any(smoke=>ScFireArea.SmokeExtinguishes(s,smoke,Clear))) {RemoveEffect(s,true);return;}
            return;
        }
        if (s.Kind==5) { s.Effect=true;s.Remaining=10;s.Age=0;s.Velocity=Vector3.Zero;DecoyPulse(s);return; }
        if (s.Kind==2) {
            s.Effect=true;s.Remaining=ScSmokeVolume.Lifetime;s.Age=0;s.Velocity=Vector3.Zero;
            ExtinguishFires(); // remove contacted fire immediately, not on the next damage tick
            WorldSound("Audio/ScCsgoKnives/grenade_smokegrenade_emit",.8f,s.Position,6);
            return;
        }
        foreach (var body in m_bodies.Bodies.ToArray()) {
            Vector3 point=Eye(body); float distance=Vector3.Distance(s.Position,point);
            // Light reaches self, teammates and enemies regardless of damage/friendly-fire rules.
            if (s.Kind!=1 && !Friendly(s,body))continue;
            if(s.Kind==0&&(distance>(chicken?ScGrenadeState.ChickenRadius:ScGrenadeState.HeRadius)||!Clear(s.Position,point)))continue;
            if (s.Kind==0) Damage(s,body,chicken?ScGrenadeState.ChickenPower(distance):ScGrenadeState.HePower(distance));
            if (s.Kind==1) {
                var p=body.Entity.FindComponent<ComponentPlayer>();
                var camera=p is not null&&!ScNet.IsRemoteDriven(p)?p.GameWidget?.ActiveCamera:null;
                var remoteView=p is not null?ScNetGrenades.FlashView(p):null;
                var eye=camera?.ViewPosition??remoteView?.Position??point;
                if(!Clear(s.Position,eye))continue;
                // A remote client's player (server) faces where its client last aimed; it has no camera here.
                Vector3 forward=p is null ? body.Matrix.Forward : ScNetGuns.RemoteInput(p) is {HasAim:true} aimed ? aimed.Aim.Direction
                    : ScNetGrenades.RemoteThrow(p) is {HasView:true} viewed ? viewed.ViewDirection : p.GameWidget?.ActiveCamera?.ViewDirection ?? body.Matrix.Forward;
                if(remoteView is not null)forward=remoteView.Forward;
                float viewDistance=Vector3.Distance(s.Position,eye);
                float facing=viewDistance>.01f?Vector3.Dot(forward,(s.Position-eye)/viewDistance):1;
                bool onScreen=camera is not null?camera.ViewFrustum.Intersection(new BoundingSphere(s.Position,.05f)):
                    remoteView?.Contains(s.Position)==true;
                float duration=ScGrenadeState.VisibleFlashDuration(p is null?distance:viewDistance,facing,onScreen);
                if (duration>.05f) {
                    float remaining=ApplyBlindness(body,duration);
                    if (p is not null && ScNet.IsRemoteDriven(p)) ScNetGrenades.Blind(p,remaining);
                }
            }
        }
        if (s.Kind==0) {
            // F06: the blast blows a temporary opening into every live smoke it can reach with a clear line; walls and floors stop it.
            Vector3 blast=s.Position+Vector3.UnitY*.3f;
            var reached=m_active.Where(smoke=>smoke.Effect && smoke.Kind==2 && smoke.Remaining>0
                && Vector3.Distance(blast,ScSmokeVolume.Center(smoke))<ScSmokeDisturbance.Radius+ScSmokeVolume.CurrentRadius(smoke) && Clear(blast,ScSmokeVolume.Center(smoke))).ToArray();
            if (reached.Length>0 && ScSmokeDisturbance.CanAdd(m_disturbances)) {
                var opening=new ScSmokeDisturbance { Center=blast };
                foreach (var smoke in reached) opening.SmokeIds.Add(smoke.Id); // a smoke behind a wall is not on this list and stays whole
                m_disturbances.Add(opening);
            }
        }
        s.Effect=true;s.Remaining=s.Kind==0?ScGrenadeVisuals.BlastLifetime:ScGrenadeVisuals.FlashLifetime;s.Age=0;
        WorldSound("Audio/ScCsgoKnives/"+ScGrenadeBlock.Assets[s.Kind]+"_explode",1,s.Position,8);
    }
    void DecoyPulse(ScGrenadeState s) {
        WorldSound("Audio/ScCsgoKnives/ak47_fire_1",.6f,s.Position,5);
        foreach (var body in m_bodies.Bodies) if (Vector3.DistanceSquared(body.Position,s.Position)<=ScDecoyResponse.Radius*ScDecoyResponse.Radius && Clear(s.Position+Vector3.UnitY*.2f,Eye(body)))
            body.Entity.FindComponent<ComponentScDecoyBehavior>()?.HearDecoy(s.Position);
    }
    void RemoveEffect(ScGrenadeState s,bool extinguished) {
        m_active.Remove(s);m_justReleased.Remove(s);m_firePoints.Remove(s);
        if (extinguished) WorldSound("Audio/ScCsgoKnives/grenade_fire_extinguish",.7f,s.Position,4);
        else if (s.Kind==2 && s.Effect) WorldSound("Audio/ScCsgoKnives/grenade_smokegrenade_clear",.6f,s.Position,4);
    }
    void ExtinguishFires() {
        foreach (var s in m_active.Where(ScFireArea.IsFire).ToArray()) {
            bool extinguish=Water(s.Position) || m_active.Any(smoke=>ScFireArea.SmokeExtinguishes(s,smoke,Clear));
            if (extinguish) RemoveEffect(s,true);
        }
    }
    void UpdateFire(float dt) {
        ExtinguishFires();
        var fires=m_active.Where(ScFireArea.IsFire).ToArray();
        if (fires.Length>0) {
            foreach (var body in m_bodies.Bodies.ToArray()) {
                if (Water(body.Position) || body.Entity.FindComponent<ComponentHealth>() is null) continue;
                var hit=ScFireArea.Exposure(fires,body.Position,dt,s=>Friendly(s,body) && Clear(s.Position+Vector3.UnitY*.15f,body.Position+Vector3.UnitY*.2f));
                if (hit.Source is not null) Damage(hit.Source,body,hit.Power,true);
            }
        }
        FireLoop(fires);
    }
    void FireLoop(ScGrenadeState[] fires) {
        if (fires.Length>0) {
            var audio=Project.FindSubsystem<SubsystemAudio>(true);
            if (m_fireLoop is null) { m_fireLoop=audio.CreateSound("Audio/ScCsgoKnives/grenade_fire_loop");m_fireLoop.IsLooped=true; }
            float volume=fires.Max(s=>audio.CalculateVolume(audio.CalculateListenerDistance(s.Position),ScFireArea.Radius(s.Kind)));
            m_fireLoop.Volume=SettingsManager.SoundsVolume*.6f*volume;m_fireLoop.Play();
        } else m_fireLoop?.Pause();
    }
    public void ScoreTarget(ComponentChaseBehavior chase,ComponentCreature target,ref float score) {
        if (m_blind.TryGetValue(chase.m_componentCreature.ComponentBody,out var blind) && m_time.GameTime<blind.Until) score=0;
        if (target is not null && ScSmokeVolume.Blocks(m_active,Eye(chase.m_componentCreature.ComponentBody),Eye(target.ComponentBody),Clear,m_disturbances)) score=0;
    }
    public bool IsBodyBlinded(ComponentBody body)=>m_blind.TryGetValue(body,out var blind)&&m_time.GameTime<blind.Until;
    /// <summary>Grenades this process has (flying and effects; a remote client's copies), read by the two-process tests.</summary>
    public int ActiveCount(int kind = -1, bool? effect = null) => m_active.Count(s => (kind < 0 || s.Kind == kind) && (effect is null || s.Effect == effect));
    public bool SmokeBlocksSight(Vector3 from,Vector3 to)=>ScSmokeVolume.Blocks(m_active,from,to,Clear,m_disturbances);
    /// <summary>Burning areas (either side) for danger avoidance; agents should never stand in fire on purpose.</summary>
    public IEnumerable<(Vector3 Position,float Radius,int Owner)> FireAreas()=>m_active.Where(ScFireArea.IsFire).Select(s=>(s.Position,ScFireArea.Radius(s.Kind),s.Owner)).ToArray();
    public void ApplyChaseOcclusion(ComponentChaseBehavior chase) {
        if (chase.m_target is null) return;
        Vector3 eye=Eye(chase.m_componentCreature.ComponentBody),target=Eye(chase.m_target.ComponentBody);
        if (!ScSmokeVolume.Blocks(m_active,eye,target,Clear,m_disturbances)) return;
        // Scoring alone leaves three seconds of exact target path prediction in
        // vanilla chasing. Stop it now; sound behaviours may still respond.
        chase.m_componentPathfinding.Stop();chase.StopAttack();
    }
    public override bool OnEditInventoryItem(IInventory inventory,int slotIndex,ComponentPlayer player) {
        if (m_preparing.TryGetValue(player,out var prep)) Cancel(player,prep);
        string[] options=["检视投掷物",m_reducedFlash.Contains(player.PlayerData.PlayerIndex)?"开启普通闪光显示":"开启减弱闪光显示"];
        DialogsManager.ShowDialog(player.GuiWidget,new ListSelectionDialog("投掷物",options,64,item=>(string)item,item=> {
            if ((string)item==options[0]) KnifeAnimationController.TriggerInspect(player);
            else if (!m_reducedFlash.Add(player.PlayerData.PlayerIndex)) m_reducedFlash.Remove(player.PlayerData.PlayerIndex);
        }));return true;
    }
    public void Draw(Camera camera,int drawOrder) {
        if (drawOrder==10) {
            // Only the camera's own player sees their preview; nothing is recomputed here.
            if(camera.GameWidget?.PlayerData?.ComponentPlayer is {} viewer&&m_preparing.TryGetValue(viewer,out var own)&&!own.Released)
                ScGrenadeTrajectory.Draw(m_renderer,camera,own.Preview);
            foreach (var s in m_active) {
                if (Vector3.DistanceSquared(camera.ViewPosition,s.Position)>80*80 || (s.Effect && s.Kind!=5)) continue;
                Matrix matrix=Matrix.CreateRotationY(s.Age*6)*Matrix.CreateTranslation(s.Position);
                m_environment.Light=15; m_environment.DrawBlockMode=DrawBlockMode.ThirdPerson;
                ((ScGrenadeBlock)BlocksManager.Blocks[BlocksManager.GetBlockIndex<ScGrenadeBlock>()]).DrawProjectile(m_renderer,s.Kind,ref matrix,m_environment);
            }
            m_renderer.Flush(camera.ViewProjectionMatrix);
        } else if (drawOrder==EffectsDrawOrder) {
            foreach(var burst in fireBursts){
                float distance=Vector3.Distance(camera.ViewPosition,burst.Position);
                if(distance<80)DrawSprites(camera,new ScGrenadeState{Kind=3,Position=burst.Position},ScGrenadeVisuals.FireBurst(burst.Position,burst.Age,distance));
            }
            foreach (var s in m_active.OrderByDescending(s=>Vector3.DistanceSquared(camera.ViewPosition,s.Position))) {
                if (Vector3.DistanceSquared(camera.ViewPosition,s.Position)>80*80 || !s.Effect || s.Kind==5) continue;
                if (ScFireArea.IsFire(s)) {
                    DrawFire(camera,s);
                } else if (s.Kind==2) {
                    DrawSmoke(camera,s);
                } else {
                    bool reduced=m_reducedFlash.Contains(camera.GameWidget.PlayerData.PlayerIndex);
                    DrawSprites(camera,s,ScGrenadeVisuals.Burst(s.Position,s.Age,s.Kind==1,reduced,Vector3.Distance(camera.ViewPosition,s.Position)));
                }
            }
            m_renderer.Flush(camera.ViewProjectionMatrix);
        } else {
            var player=camera.GameWidget.PlayerData.ComponentPlayer;
            // F07: the inside overlay follows the unified density (fully opaque deep inside, HE openings included).
            float smoke=m_active.Where(s=>s.Effect && s.Kind==2 && Clear(s.Position+Vector3.UnitY*.1f,camera.ViewPosition)).Select(s=>ScSmokeVolume.Density(s,camera.ViewPosition,m_disturbances)).DefaultIfEmpty(0).Max();
            if (smoke>0) Overlay(camera,ScGrenadeVisuals.SmokeInside(smoke));
            if (player is null || !m_blind.TryGetValue(player.ComponentBody,out var blind)) return;
            float fade=ScGrenadeState.FlashOpacity((float)(blind.Until-m_time.GameTime),blind.Duration); if (fade<=0) return;
            bool reduced=m_reducedFlash.Contains(player.PlayerData.PlayerIndex);
            // A normal CS2 flash clips the view to white at its peak. Reduced-flash accessibility
            // mode remains intentionally dimmer, but the default must reach opaque white.
            Color color=reduced?new Color(70,75,85,(int)(110*fade)):new Color(255,255,255,(int)(255*fade));
            Overlay(camera,color);
        }
    }
    void Overlay(Camera camera,Color color) {
        var batch=m_overlay.FlatBatch(0,DepthStencilState.None,null,BlendState.NonPremultiplied);
        Vector2 size=new(camera.ViewportSize.X,camera.ViewportSize.Y);
        batch.QueueQuad(Vector2.Zero,size,0,color);batch.TransformTriangles(camera.ViewportMatrix);batch.Flush();
    }
    /// <summary>The point a view's smoke puffs face: the viewing player's character (its eye). The camera is only used for
    /// projection, depth order and clipping. A view without a character (none in the shipped game) falls back to the camera.</summary>
    static Vector3 ViewAnchor(Camera camera)=>camera.GameWidget?.PlayerData?.ComponentPlayer?.ComponentCreatureModel?.EyePosition??camera.ViewPosition;
    void DrawSprites(Camera camera,ScGrenadeState s,List<ScGrenadeVisuals.Sprite> sprites,Vector3? anchor=null) {
        foreach(var sprite in sprites.OrderByDescending(p=>Vector3.DistanceSquared(camera.ViewPosition,p.Position))) {
            if (!Clear(s.Position+Vector3.UnitY*.1f,sprite.Position)) continue;
            int key=sprite.Texture;
            var texture=m_effectTextures[key]??=key==ScGrenadeVisuals.SmoothPuffKey?SmoothPuffTexture():ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/"+ScGrenadeVisuals.Textures[key]);
            var batch=m_renderer.TexturedBatch(texture,false,sprite.Additive?2:0,DepthStencilState.DepthRead,
                RasterizerState.CullNoneScissor,ScGrenadeVisuals.SpriteBlend(sprite.Additive),SamplerState.LinearClamp);
            Vector3 right=camera.ViewRight,up=sprite.Upright?Vector3.UnitY:camera.ViewUp;
            if(sprite.Upright) { right=new Vector3(right.X,0,right.Z);right=right.LengthSquared()>.001f?Vector3.Normalize(right):Vector3.UnitX; }
            float c=MathF.Cos(sprite.Rotation),n=MathF.Sin(sprite.Rotation);
            Vector3 r=(right*c+up*n)*sprite.Width,u=(up*c-right*n)*sprite.Height,p=sprite.Position;
            float x=key==3?0:(sprite.Frame%4)*.25f+.004f,y=key==3?0:(sprite.Frame/4)*.25f+.004f,span=key==3?1:.242f;
            if(key==ScGrenadeVisuals.SmoothPuffKey) { x=(sprite.Frame%2)*.5f+.004f; y=(sprite.Frame/2%2)*.5f+.004f; span=.492f; }
            if(s.Kind==2 && s.Effect && (key==0||key==ScGrenadeVisuals.SmoothPuffKey)) {
                // Smoke puffs face the view's character, not the camera (ScGrenadeVisuals.SmokeQuads); the two extra quads give a
                // camera away from the character thickness and are weighted by how squarely they face it (SmokeQuadWeights):
                // in first person only the facing quad is drawn, the filled puff of r3k.
                Vector3 facingAnchor=anchor??ViewAnchor(camera);
                var quads=ScGrenadeVisuals.SmokeQuads(sprite,s,facingAnchor,camera.ViewRight);
                var weights=ScGrenadeVisuals.SmokeQuadWeights(sprite,s,facingAnchor,camera.ViewRight,camera.ViewPosition);
                for(int k=0;k<quads.Length;k++) {
                    if(weights[k]<=0) continue;
                    var (qr,qu)=quads[k];
                    Color color=weights[k]>=.999f?sprite.Color:new Color(sprite.Color.R,sprite.Color.G,sprite.Color.B,(byte)Math.Clamp((int)MathF.Round(sprite.Color.A*weights[k]),0,255));
                    batch.QueueQuad(p-qr-qu,p+qr-qu,p+qr+qu,p-qr+qu,new Vector2(x,y+span),new Vector2(x+span,y+span),new Vector2(x+span,y),new Vector2(x,y),color);
                }
                continue;
            }
            batch.QueueQuad(p-r-u,p+r-u,p+r+u,p-r+u,new Vector2(x,y+span),new Vector2(x+span,y+span),new Vector2(x+span,y),new Vector2(x,y),sprite.Color);
        }
    }
    /// <summary>The smooth puff atlas, generated on the drawing (main) thread the first time a smoke surface is drawn.</summary>
    static Texture2D SmoothPuffTexture() {
        int size=ScGrenadeVisuals.SmoothPuffCell*2; byte[] pixels=ScGrenadeVisuals.SmoothPuffPixels();
        // Not Texture2D.SetData(int, T[]): in the 1.9.3.1 engine it hands TexImage2D the address of its own pointer variable,
        // and uploading a real image that way crashed the game (r6a, 2026-10-01: access violation in GL.TexImage2D on the
        // first smoke drawn). Texture2D.Load(Image) uploads from the image's pinned pixel memory.
        var image=new Engine.Media.Image(size,size);
        for(int y=0;y<size;y++) for(int x=0;x<size;x++) { int o=(y*size+x)*4; image.SetPixel(x,y,new Color(pixels[o],pixels[o+1],pixels[o+2],pixels[o+3])); }
        return Texture2D.Load(image);
    }
    void DrawSmoke(Camera camera,ScGrenadeState s) {
        // Deep inside the cloud the overlay is opaque (F07): the puffs behind it are not drawn at all (mobile fill rate).
        if(Clear(s.Position+Vector3.UnitY*.1f,camera.ViewPosition) && ScSmokeVolume.Density(s,camera.ViewPosition,m_disturbances)>=.999f) return;
        var sprites=ScGrenadeVisuals.Smoke(s,Vector3.Distance(camera.ViewPosition,ScSmokeVolume.Center(s)));
        Vector3 anchor=ViewAnchor(camera);
        ScGrenadeVisuals.ApplySmokeOpenings(sprites,s,m_disturbances,anchor,camera.ViewRight);
        DrawSprites(camera,s,sprites,anchor);
    }
    void DrawFire(Camera camera,ScGrenadeState s) {
        if (!m_firePoints.TryGetValue(s,out var points)) {
            points=[];m_firePoints[s]=points;float radius=ScFireArea.Radius(s.Kind);
            for (float x=-radius+.4f;x<radius;x+=.8f) for (float z=-radius+.4f;z<radius;z+=.8f) {
                if (x*x+z*z>radius*radius) continue;
                Vector3 p=s.Position+new Vector3(x,0,z);
                var hit=SolidRay(p+Vector3.UnitY*.5f,p-Vector3.UnitY*.5f);
                if (hit.HasValue && CellFace.FaceToVector3(hit.Value.CellFace.Face).Y>.5f && !Water(p)
                    && Clear(s.Position+Vector3.UnitY*.2f,hit.Value.HitPoint()+Vector3.UnitY*.2f)) points.Add(hit.Value.HitPoint()+Vector3.UnitY*.03f);
            }
        }
        var supported=points.Where(p=>!Water(p) && !Clear(p+Vector3.UnitY*.05f,p-Vector3.UnitY*.10f)).ToArray();
        DrawSprites(camera,s,ScGrenadeVisuals.Fire(s,supported,Vector3.Distance(camera.ViewPosition,s.Position)));
    }

    public override void Dispose() {
        m_effectTextures[ScGrenadeVisuals.SmoothPuffKey]?.Dispose(); m_effectTextures[ScGrenadeVisuals.SmoothPuffKey]=null;   // generated, not a content asset
        if (m_fireLoop is not null) { m_fireLoop.Stop();m_fireLoop.Dispose();Project.FindSubsystem<SubsystemAudio>()?.m_sounds.Remove(m_fireLoop);m_fireLoop=null; }
        fireBursts.Clear();m_preparing.Clear();m_throwSources.Clear();m_releaseGates.Clear();m_padMasks.Clear();m_slots.Clear();m_active.Clear();m_justReleased.Clear();m_blind.Clear();m_savedBlind.Clear();m_firePoints.Clear();base.Dispose();
    }
}
