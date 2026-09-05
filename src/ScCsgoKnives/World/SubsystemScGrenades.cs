using Engine;
using Engine.Graphics;
using TemplatesDatabase;
namespace Game;

public sealed class SubsystemScGrenades : SubsystemBlockBehavior, IUpdateable, IDrawable {
    sealed class Preparation {
        public ScThrowTransaction Transaction;
        public int Kind, Slot, ReturnSlot = -1, Stage = -1;
        public bool Low, Released;
        public double Start;
        public float Pull, ThrowStart, Release, End;
    }
    sealed class Blindness { public double Until, ImmuneUntil; public float Duration; }
    readonly Dictionary<ComponentPlayer, Preparation> m_preparing = [];
    readonly Dictionary<ComponentPlayer, int> m_lastWeapon = [];
    readonly Dictionary<ComponentBody, Blindness> m_blind = [];
    readonly HashSet<int> m_reducedFlash = [];
    readonly List<ScGrenadeState> m_active = [];
    readonly PrimitivesRenderer3D m_renderer = new();
    readonly PrimitivesRenderer2D m_overlay = new();
    readonly DrawBlockEnvironmentData m_environment = new();
    SubsystemTime m_time;
    SubsystemTerrain m_terrain;
    SubsystemPlayers m_players;
    SubsystemBodies m_bodies;
    SubsystemGameInfo m_info;
    public override int[] HandledBlocks => [BlocksManager.GetBlockIndex<ScGrenadeBlock>()];
    public UpdateOrder UpdateOrder => UpdateOrder.Default;
    public int[] DrawOrders => [10, 1102];
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
        foreach (string s in values.GetValue<string>("ReducedFlash", "").Split(',')) if (int.TryParse(s,out int i)) m_reducedFlash.Add(i);
    }
    public override void Save(ValuesDictionary values) {
        base.Save(values); var saved=new ValuesDictionary();
        for (int i=0;i<m_active.Count;i++) saved.SetValue(i.ToString(),m_active[i].Save());
        values.SetValue("Grenades",saved); values.SetValue("ReducedFlash",string.Join(",",m_reducedFlash));
        // Preparations are intentionally absent: inventory has changed only for released throws.
    }
    public void RequestThrow(ComponentPlayer player, bool low) {
        if (!Holding(player) || !Operable(player) || m_preparing.ContainsKey(player)) return;
        int kind=ScGrenadeBlock.Kind(player.ComponentMiner.ActiveBlockValue);
        if (!ScGrenadeBlock.Enabled(kind)) return;
        if (!ScGrenadeState.CanAdd(m_active,player.PlayerData.PlayerIndex)) { Message(player,"活动投掷物已达上限，未消耗物品。"); return; }
        var model=player.Entity.FindComponent<ComponentFirstPersonModel>();
        if (KnifeAnimationController.IsBusy(model)) return;
        string asset=ScGrenadeBlock.Assets[kind], alias=low?"throwLow":"throwHigh";
        float pull=Cs2Rig.Duration(asset,"pullpin"), start=pull+.12f;
        var inv=player.ComponentMiner.Inventory;
        m_preparing[player]=new Preparation { Transaction=new ScThrowTransaction(inv),Kind=kind,Low=low,Slot=inv.ActiveSlotIndex,
            ReturnSlot=m_lastWeapon.GetValueOrDefault(player,-1), Start=m_time.GameTime,Pull=pull,ThrowStart=start,
            Release=start+Cs2Rig.GrenadeReleaseTime(asset,alias),End=start+Cs2Rig.Duration(asset,alias) };
        KnifeAnimationController.GrenadeAction(player,"pullpin");
        AudioManager.PlaySound("Audio/ScCsgoKnives/"+asset+"_pin",1,0,0);
    }
    static void Message(ComponentPlayer p,string text) => p.ComponentGui.DisplaySmallMessage(text,Color.White,true,false);
    void Cancel(ComponentPlayer p, Preparation prep) { prep.Transaction.Cancel(); m_preparing.Remove(p); KnifeAnimationController.CancelAction(p); }
    public void Update(float dt) {
        foreach (var p in m_players.ComponentPlayers) {
            if (!Holding(p)) {
                int contents=Terrain.ExtractContents(p.ComponentMiner.ActiveBlockValue);
                if (contents == BlocksManager.GetBlockIndex<ScKnifeBlock>(true) || contents == BlocksManager.GetBlockIndex<ScGunBlock>(true)) m_lastWeapon[p]=p.ComponentMiner.Inventory.ActiveSlotIndex;
            }
        }
        foreach (var pair in m_preparing.ToArray()) {
            var p=pair.Key; var prep=pair.Value;
            if (!Operable(p) || (!prep.Released && !prep.Transaction.Valid)
                || prep.Released && p.ComponentMiner.Inventory.ActiveSlotIndex!=prep.Slot) { Cancel(p,prep); continue; }
            float elapsed=(float)(m_time.GameTime-prep.Start);
            int stage=elapsed<prep.Pull?0:elapsed<prep.ThrowStart?1:2;
            if (stage != prep.Stage) {
                prep.Stage=stage;
                KnifeAnimationController.GrenadeAction(p,stage==0?"pullpin":stage==1?(prep.Low?"holdLow":"holdHigh"):(prep.Low?"throwLow":"throwHigh"),
                    Math.Max(0,elapsed-(stage==0?0:stage==1?prep.Pull:prep.ThrowStart)));
            }
            if (!prep.Released && elapsed>=prep.Release) {
                var camera=p.GameWidget.ActiveCamera;
                Vector3 direction=Vector3.Normalize(camera.ViewDirection + Vector3.UnitY*(prep.Low?.08f:.18f));
                Vector3 origin=camera.ViewPosition, pos=origin+direction*.45f;
                var wall=SolidRay(origin,pos); if (wall.HasValue) pos=wall.Value.HitPoint()-direction*.10f;
                var state=new ScGrenadeState { Kind=prep.Kind,Owner=p.PlayerData.PlayerIndex,Position=pos,
                    Velocity=direction*(prep.Low?6:14)+p.ComponentBody.Velocity*.5f };
                if (!prep.Transaction.Commit(m_info.WorldSettings.GameMode==GameMode.Creative,
                    ()=>ScGrenadeState.CanAdd(m_active,state.Owner),()=>{ m_active.Add(state);return true; })) {
                    Message(p,"投掷取消：物品已移动或活动数量已满，未消耗物品。");Cancel(p,prep);continue;
                }
                prep.Released=true;
                AudioManager.PlaySound("Audio/ScCsgoKnives/"+ScGrenadeBlock.Assets[prep.Kind]+"_throw",1,0,0);
            }
            if (elapsed>=prep.End) {
                m_preparing.Remove(p);
                var inv=p.ComponentMiner.Inventory;
                if (inv.ActiveSlotIndex==prep.Slot && (Holding(p)||inv.GetSlotCount(prep.Slot)==0)) {
                    if (prep.ReturnSlot>=0 && inv.GetSlotCount(prep.ReturnSlot)>0) inv.ActiveSlotIndex=prep.ReturnSlot;
                    else if (Holding(p)) KnifeAnimationController.GrenadeAction(p,"deploy");
                }
            }
        }
        foreach (var s in m_active.ToArray()) {
            s.Age+=dt;
            if (!s.Effect) {
                float simulated=Math.Min(dt,.5f);
                for (float remaining=simulated;remaining>0;) { float step=Math.Min(.02f,remaining);Move(s,step);remaining-=step; }
            }
            s.Remaining-=dt;
            if (s.Remaining<=0) { s.Remaining=0; if (!s.Effect) Detonate(s); else m_active.Remove(s); }
        }
        foreach (var pair in m_blind.ToArray()) {
            if (m_time.GameTime>=pair.Value.ImmuneUntil || !m_bodies.Bodies.Contains(pair.Key)) { m_blind.Remove(pair.Key);continue; }
            if (m_time.GameTime<pair.Value.Until) {
                var chase=pair.Key.Entity.FindComponent<ComponentChaseBehavior>();
                if (chase?.m_target is not null) chase.StopAttack();
                var p=pair.Key.Entity.FindComponent<ComponentPlayer>();
                if (p is not null) { var overlay=p.Entity.FindComponent<ComponentScreenOverlays>(); overlay.Message="闪光影响中";overlay.MessageFactor=1; }
            }
        }
    }
    TerrainRaycastResult? SolidRay(Vector3 a,Vector3 b) => m_terrain.Raycast(a,b,false,true,(value,_)=>BlocksManager.Blocks[Terrain.ExtractContents(value)].IsCollidable_(value));
    bool Clear(Vector3 a,Vector3 b) => !SolidRay(a,b).HasValue;
    bool Water(Vector3 p) => BlocksManager.Blocks[Terrain.ExtractContents(m_terrain.Terrain.GetCellValue(Terrain.ToCell(p.X),Terrain.ToCell(p.Y),Terrain.ToCell(p.Z)))] is WaterBlock;
    void Move(ScGrenadeState s,float dt) {
        if (s.Grounded && Clear(s.Position,s.Position-Vector3.UnitY*.15f)) s.Grounded=false;
        if (s.Grounded) return;
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
            s.Velocity=(s.Velocity-2*Vector3.Dot(s.Velocity,normal)*normal)*.48f;
            if (normal.Y>.5f && s.Velocity.LengthSquared()<.5f) { s.Grounded=true;s.Velocity=Vector3.Zero; }
        } else s.Position=next;
    }
    bool Friendly(ScGrenadeState s,ComponentBody body) {
        var target=body.Entity.FindComponent<ComponentPlayer>();
        return target is null || target.PlayerData.PlayerIndex==s.Owner || m_info.WorldSettings.IsFriendlyFireEnabled;
    }
    void Damage(ScGrenadeState s,ComponentBody body,float power) {
        if (power<=0 || !Friendly(s,body)) return;
        var owner=m_players.ComponentPlayers.FirstOrDefault(p=>p.PlayerData.PlayerIndex==s.Owner);
        Vector3 direction=Eye(body)-s.Position; direction=direction.LengthSquared()>.0001f?Vector3.Normalize(direction):Vector3.UnitY;
        var attack=new ProjectileAttackment(body,owner?.Entity,Eye(body),direction,power,null) {
            StunTimeSet=0,StunTimeAdd=0,ImpulseFactor=0,AllowImpulseAndStunWhenDamageIsZero=false };
        ComponentMiner.AttackBody(attack);
    }
    void Detonate(ScGrenadeState s) {
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
        s.Effect=true;s.Remaining=.25f;s.Age=0;
        Project.FindSubsystem<SubsystemAudio>(true).PlaySound("Audio/ScCsgoKnives/"+ScGrenadeBlock.Assets[s.Kind]+"_explode",1,0,s.Position,8,true);
    }
    public void ScoreTarget(ComponentChaseBehavior chase,ref float score) {
        if (m_blind.TryGetValue(chase.m_componentCreature.ComponentBody,out var blind) && m_time.GameTime<blind.Until) score=0;
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
                if (Vector3.DistanceSquared(camera.ViewPosition,s.Position)>80*80) continue;
                if (!s.Effect) {
                    Matrix matrix=Matrix.CreateRotationY(s.Age*6)*Matrix.CreateTranslation(s.Position);
                    m_environment.Light=15; m_environment.DrawBlockMode=DrawBlockMode.ThirdPerson;
                    BlocksManager.Blocks[BlocksManager.GetBlockIndex<ScGrenadeBlock>()].DrawBlock(m_renderer,ScGrenadeBlock.Value(s.Kind),Color.White,.35f,ref matrix,m_environment);
                } else {
                    float radius=.4f+s.Age*5;
                    var batch=m_renderer.FlatBatch(0,DepthStencilState.DepthRead,RasterizerState.CullNoneScissor,BlendState.AlphaBlend);
                    Vector3 r=camera.ViewRight*radius,u=camera.ViewUp*radius;
                    batch.QueueQuad(s.Position-r-u,s.Position+r-u,s.Position+r+u,s.Position-r+u,new Color(255,225,150,(int)(150*s.Remaining/.25f)));
                }
            }
            m_renderer.Flush(camera.ViewProjectionMatrix);
        } else {
            var player=camera.GameWidget.PlayerData.ComponentPlayer;
            if (player is null || !m_blind.TryGetValue(player.ComponentBody,out var blind)) return;
            float fade=Math.Clamp((float)(blind.Until-m_time.GameTime)/Math.Max(.01f,blind.Duration),0,1); if (fade<=0) return;
            bool reduced=m_reducedFlash.Contains(player.PlayerData.PlayerIndex);
            var batch=m_overlay.FlatBatch(0,DepthStencilState.None,null,BlendState.AlphaBlend);
            Vector2 size=new(camera.ViewportSize.X,camera.ViewportSize.Y);
            Color color=reduced?new Color(70,75,85,(int)(110*fade)):new Color(255,255,255,(int)(245*fade));
            batch.QueueQuad(Vector2.Zero,size,0,color);batch.TransformTriangles(camera.ViewportMatrix);batch.Flush();
        }
    }
}
