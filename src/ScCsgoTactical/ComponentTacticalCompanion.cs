using Engine;
using GameEntitySystem;
using TemplatesDatabase;
namespace Game;

public enum TacticalOrder { Follow, Guard, Cover }
/// <summary>Bounded defensive AI. No world-wide target scan and no automatic attack on neutral creatures.</summary>
public sealed class ComponentTacticalCompanion : ComponentBehavior,IUpdateable {
    public int OwnerIndex=-1;
    public TacticalOrder Order;
    public bool CeaseFire,DeathHandled;
    public Vector3 GuardPosition;
    public ComponentCreature Creature;
    public ComponentTacticalInventory Inventory;
    ComponentPathfinding path;
    SubsystemTime time;
    SubsystemPlayers players;
    ComponentBody threat;
    double threatUntil,nextPath,nextShot,reloadAt;
    ScReloadTransaction reload;
    public string Status="跟随";
    public bool PanelOpen;
    public UpdateOrder UpdateOrder=>UpdateOrder.Default;
    public ComponentPlayer Owner=>players.ComponentPlayers.FirstOrDefault(p=>p.PlayerData.PlayerIndex==OwnerIndex&&p.ComponentHealth.Health>0);
    public override float ImportanceLevel=>!DeathHandled&&Creature.ComponentHealth.Health>0&&Owner is not null?50:0;
    public override void Load(ValuesDictionary v,IdToEntityMap map){
        Creature=Entity.FindComponent<ComponentCreature>(true);Inventory=Entity.FindComponent<ComponentTacticalInventory>(true);
        path=Entity.FindComponent<ComponentPathfinding>(true);time=Project.FindSubsystem<SubsystemTime>(true);players=Project.FindSubsystem<SubsystemPlayers>(true);
        int schema=v.GetValue("TacticalSchema",1);if(schema!=1)throw new InvalidOperationException("战术同伴存档需要相应版本拓展包。");
        OwnerIndex=v.GetValue("OwnerIndex",-1);int order=v.GetValue("Order",0);if(order<0||order>2)throw new InvalidOperationException("战术同伴指令数据异常。");
        Order=(TacticalOrder)order;CeaseFire=v.GetValue("CeaseFire",false);DeathHandled=v.GetValue("DeathHandled",false);GuardPosition=v.GetValue("GuardPosition",Vector3.Zero);
        Creature.ConstantSpawn=true;
    }
    public override void Save(ValuesDictionary v,EntityToIdMap map){v.SetValue("TacticalSchema",1);v.SetValue("OwnerIndex",OwnerIndex);v.SetValue("Order",(int)Order);v.SetValue("CeaseFire",CeaseFire);v.SetValue("GuardPosition",GuardPosition);v.SetValue("DeathHandled",DeathHandled);}
    public bool OwnedBy(ComponentPlayer p)=>p is not null&&p.PlayerData.PlayerIndex==OwnerIndex;
    public void Command(TacticalOrder order){Order=order;GuardPosition=Creature.ComponentBody.Position;nextPath=0;path.Stop();}
    public bool Friendly(ComponentBody b)=>b==null||b.Entity==Entity||b.Entity.FindComponent<ComponentPlayer>()!=null||b.Entity.FindComponent<ComponentTacticalCompanion>()!=null||b.Entity.FindComponent<ComponentMount>() is {} mount&&b.ChildBodies.Count>0;
    public void Alert(ComponentBody b){if(Friendly(b)||b.Entity.FindComponent<ComponentHealth>() is not {Health:>0})return;threat=b;threatUntil=time.GameTime+10;}
    public void Died(){if(DeathHandled)return;reload?.Cancel();reload=null;path.Stop();Inventory.DropAllItems(Creature.ComponentBody.BoundingBox.Center());DeathHandled=true;}
    public void Update(float dt){
        if(Creature.ComponentHealth.Health<=0){Died();return;}
        var owner=Owner;if(!IsActive||owner is null){path.Stop();reload?.Cancel();reload=null;Status="等待主人";return;}
        PanelOpen=owner.ComponentGui.ModalPanelWidget is TacticalPanel panel&&panel.Companion==this;
        if(PanelOpen){path.Stop();reload?.Cancel();reload=null;Status="整理装备";return;}
        double now=time.GameTime;var body=Creature.ComponentBody;
        if(threat is not null&&(!threat.IsAddedToProject||threat.Entity.FindComponent<ComponentHealth>() is not {Health:>0}||now>threatUntil||Vector3.DistanceSquared(body.Position,threat.Position)>32*32))threat=null;
        bool shield=ScShieldProtection.Holding(body,out _);
        if(now>=nextPath){nextPath=now+.5;
            Vector3 dest=Order switch{TacticalOrder.Guard=>GuardPosition,TacticalOrder.Cover=>owner.ComponentBody.Position+owner.ComponentBody.Matrix.Forward*2.5f,_=>owner.ComponentBody.Position};
            float radius=Order==TacticalOrder.Follow?1.8f:.6f;
            if(Vector3.DistanceSquared(body.Position,dest)>80*80){path.Stop();Status="距离过远，原地等待";return;}
            if(Vector3.DistanceSquared(body.Position,dest)>radius*radius)path.SetDestination(dest,shield?.35f:.7f,radius,250,true,false,true,owner.ComponentBody);else path.Stop();
            if(shield||threat!=null){var forward=threat!=null?threat.Position-body.Position:owner.ComponentBody.Matrix.Forward;forward.Y=0;
                if(forward.LengthSquared()>.01f)body.Rotation=Quaternion.CreateFromYawPitchRoll(MathF.Atan2(-forward.X,-forward.Z),0,0);}
        }
        Status=shield?"举盾掩护":CeaseFire?"停火":Order switch{TacticalOrder.Guard=>"原地警戒",TacticalOrder.Cover=>"前方掩护",_=>"跟随"};
        if(shield||CeaseFire){reload?.Cancel();reload=null;return;}
        Shoot(now,owner);
    }
    void Shoot(double now,ComponentPlayer owner){
        if(!ScInventoryTransaction.IsWeaponSlot(Inventory,0))return;
        int value=Inventory.GetSlotValue(0);if(!EffectiveGunStats.TrySnapshotValue(value,out var state)||state.Durability<=0){Status="枪械需要维修";return;}
        var spec=GunSpec.All[state.Variant];var stats=EffectiveGunStats.Resolve(spec,value,false);
        if(reload is not null){Status="换弹中";if(!reload.Valid){reload.Cancel();reload=null;return;}if(now>=reloadAt){if(ScReloadTransaction.IsTube(spec.Name)){if(!reload.InsertShell()||GunSpec.GetRounds(Terrain.ExtractData(reload.Expected))>=stats.Capacity)reload=null;else reloadAt=now+.65;}else{reload.InsertMagazine();reload=null;}nextShot=now+.25;}return;}
        if(state.Rounds<=0){
            if(spec.RechargeSeconds>0){if(state.RechargeReadyAt>=0&&now>=state.RechargeReadyAt){var tx=ScGunMutation.Prepare(Inventory,0,ScGunHolders.Key(Inventory,0),out _);tx?.Commit(r=>{r.Rounds=1;r.RechargeReadyAt=-1;r.RechargeCycleSeconds=0;});}return;}
            int ammo=ScAmmoBlock.Value(ScReloadTransaction.AmmoKind(spec)),cost=ScReloadTransaction.IsTube(spec.Name)?1:ScReloadTransaction.RequiredFor(spec,stats.Capacity);
            if(ScInventoryTransaction.Count(Inventory,ammo)<cost&&state.ReserveOverflowRounds<=0){Status="弹药不足";return;}
            reload=new(Inventory,0,value,ammo,cost,stats.Capacity,ScGunHolders.Key(Inventory,0));reload.Discard();reloadAt=now+(ScReloadTransaction.IsTube(spec.Name)?.65:2.8);return;
        }
        if(threat is null||now<nextShot)return;
        var start=Creature.ComponentBody.Position+Vector3.UnitY*1.45f;var end=threat.BoundingBox.Center();var delta=end-start;float length=delta.Length();if(length<.1f||length>Math.Min(32,stats.Range))return;
        var bodies=Project.FindSubsystem<SubsystemBodies>(true);var terrain=Project.FindSubsystem<SubsystemTerrain>(true);
        var hit=bodies.Raycast(start,end,0,(b,d)=>b.Entity!=Entity);var wall=terrain.Raycast(start,end,false,true,(v,d)=>ScGunRange.TerrainStopsBullet(v));
        if(!hit.HasValue||hit.Value.ComponentBody!=threat||wall.HasValue&&wall.Value.Distance<hit.Value.Distance)return;
        var mutation=ScGunMutation.Prepare(Inventory,0,ScGunHolders.Key(Inventory,0),out _);if(mutation==null)return;
        if(mutation.Commit(r=>{r.Rounds=Math.Max(0,r.Rounds-1);r.Durability=Math.Max(0,r.Durability-1);if(spec.RechargeSeconds>0&&r.Rounds==0){r.RechargeCycleSeconds=stats.RechargeSeconds;r.RechargeReadyAt=now+stats.RechargeSeconds;}})!=ScGunResult.Success)return;
        nextShot=now+Math.Max(.12,stats.CycleSeconds); // companion cap prevents frame-bound bursts on weaker phones
        var credit=ScGunKillCredit.For(Terrain.ExtractData(mutation.Expected),false,0);var health=threat.Entity.FindComponent<ComponentHealth>();float before=health.Health;
        // All pellets are aimed at the visible torso. Keep total power and growth, never multiply shotgun damage by pellet count.
        var attack=new ScSurvivalBalance.GunAttack(threat,Entity,start+Vector3.Normalize(delta)*hit.Value.Distance,Vector3.Normalize(delta),stats.Power*stats.Falloff(spec,length));
        ScProjectileDefense.Apply(threat,attack);ComponentMiner.AttackBody(attack);
        if(spec.RechargeSeconds>0)ScElectricStun.Apply(threat,before,health.Health,now);
        if(before>0&&health.Health<=0&&credit!=null&&ScGunKillRules.Counts(threat,owner,false,out _))ScGunRegistry.Current.Kills.Enqueue(credit.RecordId,credit.Variant);
        Project.FindSubsystem<SubsystemAudio>(true).PlaySound(SubsystemScGunBlockBehavior.ExtensionShotSound(spec,!state.SilencerOff),.6f,0,start,8,true);
        Status="还击";
    }
}
