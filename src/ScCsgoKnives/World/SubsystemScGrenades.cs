using Engine;
using Engine.Graphics;
using Engine.Audio;
using TemplatesDatabase;
namespace Game;

public sealed class SubsystemScGrenades : SubsystemBlockBehavior, IUpdateable, IDrawable {
    sealed class AreaAttack(ComponentBody body,GameEntitySystem.Entity owner,Vector3 point,Vector3 direction,float power)
        : ProjectileAttackment(body,owner,point,direction,power,null) {
        public override bool DisableFriendlyFire() => Attacker!=Target && base.DisableFriendlyFire();
    }
    sealed class Preparation {
        public ScThrowTransaction Transaction;
        public int Kind, Slot, ReturnSlot = -1, Stage = 0;
        public bool Low, Released, FromButton;
        public long CommittedRevision;
        public ScGrenadePreparation Timeline;
    }
    sealed class Blindness { public double Until, ImmuneUntil; public float Duration; }
    readonly Dictionary<ComponentPlayer, Preparation> m_preparing = [];
    readonly Dictionary<ComponentPlayer, ScSlotHistory> m_slots = [];
    readonly Dictionary<(ComponentPlayer Player, bool Low), bool> m_throwButtons = [];
    readonly Dictionary<ComponentBody, Blindness> m_blind = [];
    readonly Dictionary<int, Blindness> m_savedBlind = [];
    readonly HashSet<int> m_reducedFlash = [];
    readonly List<ScGrenadeState> m_active = [];
    readonly List<ScSmokeDisturbance> m_disturbances = [];
    readonly HashSet<ScGrenadeState> m_justReleased = [];
    readonly PrimitivesRenderer3D m_renderer = new();
    readonly PrimitivesRenderer2D m_overlay = new();
    readonly DrawBlockEnvironmentData m_environment = new();
    readonly Texture2D[] m_effectTextures = new Texture2D[4];
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
    public int ViewmodelValue(ComponentPlayer p, int value) => m_preparing.TryGetValue(p,out var prep) && prep.Released
        && p.ComponentMiner.Inventory.ActiveSlotIndex==prep.Slot && p.ComponentMiner.ActiveBlockValue==0 && Operable(p) ? ScGrenadeBlock.Value(prep.Kind) : value;
    static bool Operable(ComponentPlayer p) => p.ComponentHealth.Health > 0 && p.ComponentGui.ModalPanelWidget is null && !DialogsManager.HasDialogs(p.GuiWidget);
    static Vector3 Eye(ComponentBody b) => b.Position + Vector3.UnitY * b.BoxSize.Y * .85f;
    public override void Load(ValuesDictionary values) {
        base.Load(values); m_time=Project.FindSubsystem<SubsystemTime>(true); m_terrain=Project.FindSubsystem<SubsystemTerrain>(true);
        m_players=Project.FindSubsystem<SubsystemPlayers>(true); m_bodies=Project.FindSubsystem<SubsystemBodies>(true); m_info=Project.FindSubsystem<SubsystemGameInfo>(true);
        var saved=values.GetValue<ValuesDictionary>("Grenades",null);
        if (saved is not null) foreach (var item in saved) {
            if (item.Value is ValuesDictionary d && ScGrenadeState.Load(d) is ScGrenadeState state && ScGrenadeState.CanAdd(m_active,state.Owner)) m_active.Add(state);
        }
        var openings=values.GetValue<ValuesDictionary>("SmokeDisturbances",null);
        if (openings is not null) foreach (var item in openings) {
            if (item.Value is ValuesDictionary d && ScSmokeDisturbance.Load(d) is ScSmokeDisturbance opening && ScSmokeDisturbance.CanAdd(m_disturbances)) m_disturbances.Add(opening);
        }
        foreach (string s in values.GetValue<string>("ReducedFlash", "").Split(',')) if (int.TryParse(s,out int i)) m_reducedFlash.Add(i);
        var flashes=values.GetValue<ValuesDictionary>("Blindness",null);
        if (flashes is not null) foreach (var pair in flashes) if (int.TryParse(pair.Key,out int id) && pair.Value is ValuesDictionary d) {
            float left=d.GetValue<float>("Left",0),immune=d.GetValue<float>("Immune",0),duration=d.GetValue<float>("Duration",0);
            if (float.IsFinite(left) && float.IsFinite(immune) && float.IsFinite(duration))
                m_savedBlind[id]=new Blindness {Until=m_time.GameTime+Math.Clamp(left,0,2),ImmuneUntil=m_time.GameTime+Math.Clamp(immune,0,5),Duration=Math.Clamp(duration,.01f,2)};
        }
    }
    public override void Save(ValuesDictionary values) {
        base.Save(values); var saved=new ValuesDictionary();
        for (int i=0;i<m_active.Count;i++) saved.SetValue(i.ToString(),m_active[i].Save());
        values.SetValue("Grenades",saved); values.SetValue("ReducedFlash",string.Join(",",m_reducedFlash));
        var openings=new ValuesDictionary();
        for (int i=0;i<m_disturbances.Count;i++) if (m_disturbances[i].Active) openings.SetValue(i.ToString(),m_disturbances[i].Save());
        values.SetValue("SmokeDisturbances",openings);
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
    public void SetThrowButton(ComponentPlayer player, bool low, bool pressed, bool clicked, bool cancelled) {
        m_throwButtons[(player, low)] = pressed;
        if (cancelled) {
            if (m_preparing.TryGetValue(player, out var prep) && prep.FromButton && prep.Low == low) Cancel(player, prep);
            return;
        }
        if (pressed || clicked) RequestThrow(player, low, true);
    }
    public void RequestThrow(ComponentPlayer player, bool low, bool fromButton = false) {
        if (!Holding(player) || !Operable(player) || m_preparing.ContainsKey(player)) return;
        int kind=ScGrenadeBlock.Kind(player.ComponentMiner.ActiveBlockValue);
        if (!ScGrenadeBlock.Enabled(kind)) return;
        if (!ScGrenadeState.CanAdd(m_active,player.PlayerData.PlayerIndex)) { Message(player,"活动投掷物已达上限，未消耗物品。"); return; }
        var model=player.Entity.FindComponent<ComponentFirstPersonModel>();
        if (KnifeAnimationController.IsBusy(model)) return;
        string asset=ScGrenadeBlock.Assets[kind], alias=low?"throwLow":"throwHigh";
        float pull=Cs2Rig.Duration(asset,"pullpin");
        var inv=player.ComponentMiner.Inventory;
        m_preparing[player]=new Preparation { Transaction=new ScThrowTransaction(inv),Kind=kind,Low=low,Slot=inv.ActiveSlotIndex,
            ReturnSlot=m_slots.TryGetValue(player,out var history)?history.Previous:-1, FromButton=fromButton,
            Timeline=new ScGrenadePreparation(m_time.GameTime,pull,Cs2Rig.GrenadeReleaseTime(asset,alias),Cs2Rig.Duration(asset,alias)) };
        KnifeAnimationController.GrenadeAction(player,"pullpin");
        AudioManager.PlaySound("Audio/ScCsgoKnives/"+asset+"_pin",1,0,0);
        KnifeLog.Information($"grenade prepare: {asset} slot {inv.ActiveSlotIndex} previous slot {m_preparing[player].ReturnSlot} low={low} button={fromButton}");
    }
    static void Message(ComponentPlayer p,string text) => p.ComponentGui.DisplaySmallMessage(text,Color.White,true,false);
    void Cancel(ComponentPlayer p, Preparation prep) {
        prep.Transaction.Cancel();m_preparing.Remove(p);
        if (p.ComponentMiner.Inventory.ActiveSlotIndex==prep.Slot && (Holding(p) || p.ComponentMiner.ActiveBlockValue==0)) KnifeAnimationController.CancelAction(p);
    }
    public void Update(float dt) {
        if (m_savedBlind.Count>0) {
            foreach (var body in m_bodies.Bodies) if (m_savedBlind.Remove(body.Entity.Id,out var blind) && blind.ImmuneUntil>m_time.GameTime) m_blind[body]=blind;
            foreach (var pair in m_savedBlind.Where(p=>p.Value.ImmuneUntil<=m_time.GameTime).ToArray()) m_savedBlind.Remove(pair.Key);
        }
        foreach (var p in m_players.ComponentPlayers) {
            if (!m_slots.TryGetValue(p,out var history)) m_slots[p]=history=new ScSlotHistory();
            history.Observe(p.ComponentMiner.Inventory.ActiveSlotIndex,m_time.GameTime);
        }
        foreach (var pair in m_preparing.ToArray()) {
            var p=pair.Key; var prep=pair.Value;
            if (!Window.IsActive || !Operable(p) || (!prep.Released && !prep.Transaction.Valid)
                || prep.Released && (p.ComponentMiner.Inventory.ActiveSlotIndex!=prep.Slot
                    || ScInventoryTransaction.Revision(p.ComponentMiner.Inventory)!=prep.CommittedRevision)) { Cancel(p,prep); continue; }
            var input=p.ComponentInput.PlayerInput;
            bool pressed=prep.FromButton?m_throwButtons.GetValueOrDefault((p,prep.Low)):prep.Low?input.Aim.HasValue:input.Dig.HasValue || input.Hit.HasValue;
            prep.Timeline.Step(m_time.GameTime,pressed);
            int stage=prep.Timeline.Stage(m_time.GameTime);
            if (stage != prep.Stage) {
                prep.Stage=stage;
                KnifeAnimationController.GrenadeAction(p,stage==0?"pullpin":stage==1?(prep.Low?"holdLow":"holdHigh"):(prep.Low?"throwLow":"throwHigh"),
                    prep.Timeline.Elapsed(m_time.GameTime));
            }
            if (!prep.Released && m_time.GameTime>=prep.Timeline.ReleaseAt) {
                var camera=p.GameWidget.ActiveCamera;
                Vector3 direction=ScGrenadeBallistics.Direction(camera.ViewDirection,prep.Low);
                Vector3 origin=camera.ViewPosition;
                // F09: the crosshair ray picks the target; the grenade leaves from the hand (the posed third-person fist when
                // that model was drawn, else just ahead of the eye) and is then aimed at that target. Walls between the
                // body and the hand, or the hand and the flight, pull the start point back rather than spawning behind them.
                Vector3 chest=p.ComponentBody.Position+Vector3.UnitY*p.ComponentBody.BoxSize.Y*.7f;
                Vector3 aimTarget=SolidRay(origin,origin+direction*48)?.HitPoint() ?? origin+direction*48;
                Vector3 pos=ScThirdPerson.TryGetFist(p.Entity.FindComponent<ComponentHumanModel>(),out Vector3 fist) ? fist : origin+direction*.45f;
                var reach=SolidRay(chest,pos); if (reach.HasValue) pos=Vector3.Lerp(chest,reach.Value.HitPoint(),.8f);
                Vector3 toTarget=aimTarget-pos; if (toTarget.LengthSquared()>.01f) direction=Vector3.Normalize(toTarget);
                var wall=SolidRay(pos,pos+direction*.45f); if (wall.HasValue) pos=wall.Value.HitPoint()-direction*.10f; else pos+=direction*.1f;
                var state=new ScGrenadeState { Kind=prep.Kind,Owner=p.PlayerData.PlayerIndex,Position=pos,
                    Velocity=ScGrenadeBallistics.LaunchVelocity(direction,p.ComponentBody.Velocity,prep.Low),Remaining=ScGrenadeBallistics.Fuse(prep.Kind) };
                if (!prep.Transaction.Commit(m_info.WorldSettings.GameMode==GameMode.Creative,
                    ()=>ScGrenadeState.CanAdd(m_active,state.Owner),()=>{ m_active.Add(state);m_justReleased.Add(state);return true; })) {
                    Message(p,"投掷取消：物品已移动或活动数量已满，未消耗物品。");Cancel(p,prep);continue;
                }
                prep.Released=true;
                prep.CommittedRevision=ScInventoryTransaction.Revision(p.ComponentMiner.Inventory);
                KnifeLog.Information($"grenade release: {ScGrenadeBlock.Assets[prep.Kind]} speed {state.Velocity.Length():0.0} (player {p.ComponentBody.Velocity.Length():0.0}) low={prep.Low} at {pos}");
                AudioManager.PlaySound("Audio/ScCsgoKnives/"+ScGrenadeBlock.Assets[prep.Kind]+"_throw",1,0,0);
            }
            if (m_time.GameTime>=prep.Timeline.EndAt) {
                m_preparing.Remove(p);
                var inv=p.ComponentMiner.Inventory;
                // F02: a finished throw returns to the slot held before the grenade slot, whatever it holds now;
                // with no such slot the player stays put. A manual switch, an open screen or an inventory
                // change already cancelled the preparation above, so nothing is switched then.
                if (prep.Released && inv.ActiveSlotIndex==prep.Slot && (Holding(p)||inv.GetSlotCount(prep.Slot)==0)) {
                    int target=ScGrenadeBallistics.FollowUpSlot(inv.SlotsCount,prep.Slot,prep.ReturnSlot);
                    KnifeLog.Information($"grenade follow-up: thrown slot {prep.Slot} previous slot {prep.ReturnSlot} -> {(target>=0?"slot "+target:"stay")} holding={Holding(p)}");
                    if (target>=0) inv.ActiveSlotIndex=target;
                    else if (Holding(p)) KnifeAnimationController.GrenadeAction(p,"deploy");
                }
            }
        }
        // Order inside one update: fire validity (water/smoke extinguish) -> grenade motion -> heat trigger
        // -> fuse and smoke growth. The order is fixed here, not by list position.
        UpdateFire(dt);
        foreach (var opening in m_disturbances) opening.Remaining-=ScGrenadeBallistics.Step(dt);
        m_disturbances.RemoveAll(o=>!o.Active);
        foreach (var s in m_active.ToArray()) {
            if (m_justReleased.Remove(s)) continue; // this frame's elapsed time preceded the release
            // Physics, age and fuse advance by one clamped step, so a stall cannot pop a grenade that barely moved (F03).
            float step=ScGrenadeBallistics.Step(dt);
            s.Age+=step;
            if (!s.Effect) {
                for (float remaining=step;remaining>0;) { float sub=Math.Min(.02f,remaining);Move(s,sub);remaining-=sub; }
                if (s.Kind is 3 or 4) {
                    if (Water(s.Position)) { RemoveEffect(s,true);continue; }
                    if (s.Grounded) { Detonate(s);continue; }
                }
                if (s.Kind==2) {
                    // F05: a smoke grenade that reaches a live, reachable fire area pops now, once, wherever it is.
                    var fire=m_active.FirstOrDefault(f=>ScFireArea.IsFire(f) && ScFireArea.Heats(f,s) && Clear(f.Position+Vector3.UnitY*.15f,s.Position));
                    if (fire is not null) {
                        KnifeLog.Information($"grenade smoke heated by fire kind {fire.Kind} at {fire.Position}: pops early at {s.Position} after {s.Age:0.00} s");
                        Detonate(s);continue;
                    }
                }
            }
            if (s.Effect && s.Kind==5 && (int)s.Age>(int)(s.Age-step)) DecoyPulse(s);
            s.Remaining-=step;
            if (s.Remaining<=0) {
                s.Remaining=0;
                if (!s.Effect && s.Kind is 2 or 5 && !ScGrenadeBallistics.Settled(s)) {
                    // F04: smoke and decoy pop only after resting on support. The timeout covers a grenade
                    // that never comes to rest (wedged, endless slope); it is not a normal trigger path.
                    if (s.Age<ScGrenadeBallistics.SettleTimeout) continue;
                    KnifeLog.Warning($"grenade kind {s.Kind} never settled within {ScGrenadeBallistics.SettleTimeout:0} s at {s.Position}; popping in place");
                }
                if (!s.Effect && s.Kind is 2 or 5) KnifeLog.Information($"grenade kind {s.Kind} pops: age {s.Age:0.00} s rested {s.Rested:0.00} s at {s.Position}");
                if (!s.Effect) Detonate(s); else RemoveEffect(s,false);
            }
        }
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
                var p=pair.Key.Entity.FindComponent<ComponentPlayer>();
                if (p is not null) { var overlay=p.Entity.FindComponent<ComponentScreenOverlays>(); overlay.Message="闪光影响中";overlay.MessageFactor=1; }
            }
        }
    }
    TerrainRaycastResult? SolidRay(Vector3 a,Vector3 b) => m_terrain.Raycast(a,b,false,true,(value,_)=>BlocksManager.Blocks[Terrain.ExtractContents(value)].IsCollidable_(value));
    bool Clear(Vector3 a,Vector3 b) => !SolidRay(a,b).HasValue;
    bool Water(Vector3 p) => BlocksManager.Blocks[Terrain.ExtractContents(m_terrain.Terrain.GetCellValue(Terrain.ToCell(p.X),Terrain.ToCell(p.Y),Terrain.ToCell(p.Z)))] is WaterBlock;
    void Move(ScGrenadeState s,float dt) {
        if (s.Grounded && Clear(s.Position,s.Position-Vector3.UnitY*.15f)) { s.Grounded=false;s.Rested=0; }
        if (s.Grounded) { s.Rested+=dt;return; }
        bool water=Water(s.Position);
        s.Velocity+=Vector3.UnitY*(water?-3f:-10f)*dt;
        s.Velocity*=MathF.Exp(-(water?3:.08f)*dt);
        Vector3 next=s.Position+s.Velocity*dt;
        var hit=SolidRay(s.Position,next);
        var bodyHit=m_bodies.Raycast(s.Position,next,.08f,(b,_)=>s.Age>.3f || b.Entity.FindComponent<ComponentPlayer>()?.PlayerData.PlayerIndex!=s.Owner);
        if (bodyHit.HasValue && (!hit.HasValue || bodyHit.Value.Distance<hit.Value.Distance)) {
            Vector3 direction=s.Velocity.LengthSquared()>.001f?Vector3.Normalize(s.Velocity):Vector3.UnitY;
            s.Position=bodyHit.Value.HitPoint()-direction*.10f;s.Velocity=-s.Velocity*.3f;return;
        }
        if (hit.HasValue) {
            Vector3 normal=CellFace.FaceToVector3(hit.Value.CellFace.Face);
            s.Position=hit.Value.HitPoint()+normal*.06f;
            if (s.Kind is not (3 or 4) && s.Velocity.LengthSquared()>1 && s.Age>=s.NextBounceSound) {
                s.NextBounceSound=s.Age+.15f;
                string sound=ScGrenadeBlock.Assets[s.Kind is 0 or 1 or 2?s.Kind:1];
                Project.FindSubsystem<SubsystemAudio>(true).PlaySound("Audio/ScCsgoKnives/"+sound+"_bounce",.35f,0,s.Position,3,true);
            }
            s.Velocity=(s.Velocity-2*Vector3.Dot(s.Velocity,normal)*normal)*.48f;
            if (s.Kind is 3 or 4 && normal.Y>.5f) { s.Grounded=true;s.Velocity=Vector3.Zero; }
            if (normal.Y>.5f && s.Velocity.LengthSquared()<.5f) { s.Grounded=true;s.Velocity=Vector3.Zero; }
        } else s.Position=next;
    }
    bool Friendly(ScGrenadeState s,ComponentBody body) {
        var target=body.Entity.FindComponent<ComponentPlayer>();
        return target is null || target.PlayerData.PlayerIndex==s.Owner || m_info.WorldSettings.IsFriendlyFireEnabled;
    }
    void Damage(ScGrenadeState s,ComponentBody body,float power,bool fire=false) {
        if (power<=0 || !Friendly(s,body)) return;
        var owner=m_players.ComponentPlayers.FirstOrDefault(p=>p.PlayerData.PlayerIndex==s.Owner);
        Vector3 direction=Eye(body)-s.Position; direction=direction.LengthSquared()>.0001f?Vector3.Normalize(direction):Vector3.UnitY;
        var attack=new AreaAttack(body,owner?.Entity,Eye(body),direction,power) {
            StunTimeSet=0,StunTimeAdd=0,ImpulseFactor=0,AllowImpulseAndStunWhenDamageIsZero=false,
            EnableHitValueParticleSystem=!fire,AttackSoundVolume=fire?0:1 };
        ComponentMiner.AttackBody(attack);
    }
    void Detonate(ScGrenadeState s) {
        if (s.Kind is 3 or 4) {
            // A bounded airborne timeout may ignite a reachable floor below it,
            // never an unsupported sphere of fire in mid-air.
            var floor=SolidRay(s.Position+Vector3.UnitY*.1f,s.Position-Vector3.UnitY*4);
            if (!floor.HasValue || CellFace.FaceToVector3(floor.Value.CellFace.Face).Y<.5f) { RemoveEffect(s,false);return; }
            s.Position=floor.Value.HitPoint()+Vector3.UnitY*.06f;
            if (Water(s.Position)) { RemoveEffect(s,true);return; }
            s.Effect=true;s.Remaining=ScFireArea.Lifetime(s.Kind);s.Age=0;s.Velocity=Vector3.Zero;
            Project.FindSubsystem<SubsystemAudio>(true).PlaySound("Audio/ScCsgoKnives/"+ScGrenadeBlock.Assets[s.Kind]+"_explode",1,0,s.Position,6,true);
            return;
        }
        if (s.Kind==5) { s.Effect=true;s.Remaining=10;s.Age=0;s.Velocity=Vector3.Zero;DecoyPulse(s);return; }
        if (s.Kind==2) {
            s.Effect=true;s.Remaining=ScSmokeVolume.Lifetime;s.Age=0;s.Velocity=Vector3.Zero;
            Project.FindSubsystem<SubsystemAudio>(true).PlaySound("Audio/ScCsgoKnives/grenade_smokegrenade_emit",.8f,0,s.Position,6,true);
            return;
        }
        foreach (var body in m_bodies.Bodies.ToArray()) {
            Vector3 point=Eye(body); float distance=Vector3.Distance(s.Position,point);
            if (!Friendly(s,body) || distance>(s.Kind==0?4:16) || !Clear(s.Position,point)) continue;
            if (s.Kind==0) Damage(s,body,ScGrenadeState.HePower(distance));
            if (s.Kind==1 && (!m_blind.TryGetValue(body,out var old) || m_time.GameTime>=old.ImmuneUntil)) {
                var p=body.Entity.FindComponent<ComponentPlayer>();
                Vector3 forward=p?.GameWidget.ActiveCamera.ViewDirection??body.Matrix.Forward;
                float facing=distance>.01f?Vector3.Dot(forward,(s.Position-point)/distance):1;
                float duration=ScGrenadeState.FlashDuration(distance,facing);
                if (duration>.05f) m_blind[body]=new Blindness {Until=m_time.GameTime+duration,Duration=duration,ImmuneUntil=m_time.GameTime+duration+3};
            }
        }
        if (s.Kind==0) {
            // F06: the blast blows a temporary opening into every live smoke it can reach with a clear line; walls and floors stop it.
            Vector3 blast=s.Position+Vector3.UnitY*.3f;
            int cleared=m_active.Count(smoke=>smoke.Effect && smoke.Kind==2 && smoke.Remaining>0
                && Vector3.Distance(blast,ScSmokeVolume.Center(smoke))<ScSmokeDisturbance.Radius+ScSmokeVolume.CurrentRadius(smoke) && Clear(blast,ScSmokeVolume.Center(smoke)));
            if (cleared>0 && ScSmokeDisturbance.CanAdd(m_disturbances)) {
                m_disturbances.Add(new ScSmokeDisturbance { Center=blast });
                KnifeLog.Information($"HE at {blast} opens {cleared} smoke(s): radius {ScSmokeDisturbance.Radius} m, hold {ScSmokeDisturbance.Hold} s, recovery {ScSmokeDisturbance.Recovery} s");
            }
        }
        s.Effect=true;s.Remaining=s.Kind==0?ScGrenadeVisuals.BlastLifetime:ScGrenadeVisuals.FlashLifetime;s.Age=0;
        Project.FindSubsystem<SubsystemAudio>(true).PlaySound("Audio/ScCsgoKnives/"+ScGrenadeBlock.Assets[s.Kind]+"_explode",1,0,s.Position,8,true);
    }
    void DecoyPulse(ScGrenadeState s) {
        Project.FindSubsystem<SubsystemAudio>(true).PlaySound("Audio/ScCsgoKnives/ak47_fire_1",.6f,0,s.Position,5,true);
        foreach (var body in m_bodies.Bodies) if (Vector3.DistanceSquared(body.Position,s.Position)<=18*18 && Clear(s.Position+Vector3.UnitY*.2f,Eye(body)))
            body.Entity.FindComponent<ComponentScDecoyBehavior>()?.HearDecoy(s.Position);
    }
    void RemoveEffect(ScGrenadeState s,bool extinguished) {
        m_active.Remove(s);m_justReleased.Remove(s);m_firePoints.Remove(s);
        if (extinguished) Project.FindSubsystem<SubsystemAudio>(true).PlaySound("Audio/ScCsgoKnives/grenade_fire_extinguish",.7f,0,s.Position,4,true);
        else if (s.Kind==2 && s.Effect) Project.FindSubsystem<SubsystemAudio>(true).PlaySound("Audio/ScCsgoKnives/grenade_smokegrenade_clear",.6f,0,s.Position,4,true);
    }
    void UpdateFire(float dt) {
        foreach (var s in m_active.Where(ScFireArea.IsFire).ToArray()) {
            bool extinguish=Water(s.Position) || m_active.Any(smoke=>ScFireArea.SmokeTouches(s,smoke) && Clear(s.Position+Vector3.UnitY*.1f,smoke.Position+Vector3.UnitY*.1f));
            if (extinguish) RemoveEffect(s,true);
        }
        var fires=m_active.Where(ScFireArea.IsFire).ToArray();
        if (fires.Length>0) {
            foreach (var body in m_bodies.Bodies.ToArray()) {
                if (Water(body.Position) || body.Entity.FindComponent<ComponentHealth>() is null) continue;
                var hit=ScFireArea.Exposure(fires,body.Position,dt,s=>Friendly(s,body) && Clear(s.Position+Vector3.UnitY*.15f,body.Position+Vector3.UnitY*.2f));
                if (hit.Source is not null) Damage(hit.Source,body,hit.Power,true);
            }
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
            foreach (var s in m_active) {
                if (Vector3.DistanceSquared(camera.ViewPosition,s.Position)>80*80 || (s.Effect && s.Kind!=5)) continue;
                Matrix matrix=Matrix.CreateRotationY(s.Age*6)*Matrix.CreateTranslation(s.Position);
                m_environment.Light=15; m_environment.DrawBlockMode=DrawBlockMode.ThirdPerson;
                ((ScGrenadeBlock)BlocksManager.Blocks[BlocksManager.GetBlockIndex<ScGrenadeBlock>()]).DrawProjectile(m_renderer,s.Kind,ref matrix,m_environment);
            }
            m_renderer.Flush(camera.ViewProjectionMatrix);
        } else if (drawOrder==EffectsDrawOrder) {
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
            float fade=Math.Clamp((float)(blind.Until-m_time.GameTime)/Math.Max(.01f,blind.Duration),0,1); if (fade<=0) return;
            bool reduced=m_reducedFlash.Contains(player.PlayerData.PlayerIndex);
            Color color=reduced?new Color(70,75,85,(int)(110*fade)):new Color(255,255,255,(int)(245*fade));
            Overlay(camera,color);
        }
    }
    void Overlay(Camera camera,Color color) {
        var batch=m_overlay.FlatBatch(0,DepthStencilState.None,null,BlendState.AlphaBlend);
        Vector2 size=new(camera.ViewportSize.X,camera.ViewportSize.Y);
        batch.QueueQuad(Vector2.Zero,size,0,color);batch.TransformTriangles(camera.ViewportMatrix);batch.Flush();
    }
    void DrawSprites(Camera camera,ScGrenadeState s,List<ScGrenadeVisuals.Sprite> sprites) {
        foreach(var sprite in sprites.OrderByDescending(p=>Vector3.DistanceSquared(camera.ViewPosition,p.Position))) {
            if (!Clear(s.Position+Vector3.UnitY*.1f,sprite.Position)) continue;
            int key=sprite.Texture;
            var texture=m_effectTextures[key]??=ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/"+ScGrenadeVisuals.Textures[key]);
            var batch=m_renderer.TexturedBatch(texture,false,sprite.Additive?2:0,DepthStencilState.DepthRead,
                RasterizerState.CullNoneScissor,sprite.Additive?BlendState.Additive:BlendState.AlphaBlend,SamplerState.LinearClamp);
            Vector3 right=camera.ViewRight,up=sprite.Upright?Vector3.UnitY:camera.ViewUp;
            if(sprite.Upright) { right=new Vector3(right.X,0,right.Z);right=right.LengthSquared()>.001f?Vector3.Normalize(right):Vector3.UnitX; }
            float c=MathF.Cos(sprite.Rotation),n=MathF.Sin(sprite.Rotation);
            Vector3 r=(right*c+up*n)*sprite.Width,u=(up*c-right*n)*sprite.Height,p=sprite.Position;
            float x=key==3?0:(sprite.Frame%4)*.25f+.004f,y=key==3?0:(sprite.Frame/4)*.25f+.004f,span=key==3?1:.242f;
            batch.QueueQuad(p-r-u,p+r-u,p+r+u,p-r+u,new Vector2(x,y+span),new Vector2(x+span,y+span),new Vector2(x+span,y),new Vector2(x,y),sprite.Color);
        }
    }
    void DrawSmoke(Camera camera,ScGrenadeState s) {
        var sprites=ScGrenadeVisuals.Smoke(s,Vector3.Distance(camera.ViewPosition,ScSmokeVolume.Center(s)));
        if (m_disturbances.Count>0) // F06: sprites inside an HE opening fade with the same clearing value the AI sight query uses
            for (int i=0;i<sprites.Count;i++) {
                float keep=1-ScSmokeDisturbance.Clearing(m_disturbances,sprites[i].Position);
                if (keep<1) sprites[i]=sprites[i] with { Color=new Color(sprites[i].Color.R,sprites[i].Color.G,sprites[i].Color.B,(int)(sprites[i].Color.A*keep)) };
            }
        DrawSprites(camera,s,sprites);
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
        if (m_fireLoop is not null) { m_fireLoop.Stop();m_fireLoop.Dispose();Project.FindSubsystem<SubsystemAudio>()?.m_sounds.Remove(m_fireLoop);m_fireLoop=null; }
        m_preparing.Clear();m_throwButtons.Clear();m_active.Clear();m_justReleased.Clear();m_blind.Clear();m_savedBlind.Clear();m_firePoints.Clear();base.Dispose();
    }
}
