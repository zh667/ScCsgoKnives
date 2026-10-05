using Engine;
using GameEntitySystem;
using TemplatesDatabase;
namespace Game;

public enum TacticalOrder { Follow, Guard, Cover }
/// <summary>Local hostile acquisition and owner assist; neutral creatures require an actual attack.</summary>
public sealed class ComponentTacticalCompanion : ComponentBehavior,IUpdateable,IScArmorKey {
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
    ComponentBody lastVoiceTarget;
    TacticalOrder? lastVoiceOrder;
    double threatUntil,nextPath,nextShot,reloadAt,nextScan,nextFlee,nextJump,unreachableNotice;int fleeAttempt,stuckStage;
    Vector3? ownerAnchor,detour;
    ScReloadTransaction reload;
    readonly ScWeaponActionTimeline actions=new();
    readonly ScHeldWeaponSelection visualSelection=new();
    string visualAsset;
    public ScWeaponAction VisualAction {
        get {var a=actions.Read(time?.GameTime??0);return Creature?.ComponentHealth.Health>0 && (a.Kind!=ScWeaponActionKind.Reload || reload!=null || mirroredReload)?a:default;}
    }
    // ---- multiplayer (TacticalNet): the server runs the companion; its owner's panel on a client reads these copies
    bool mirroredReload;
    /// <summary>Server: until when a remote owner's panel for this companion is open (its client repeats it while open).</summary>
    public double RemotePanelUntil;
    long Signature(ScWeaponAction a)=>HashCode.Combine(a.Sequence,a.Kind==ScWeaponActionKind.Idle,reload!=null,Order,CeaseFire,Status);
    void Publish(){
        var a=actions.Read(time.GameTime);
        TacticalNet.Publish(Entity,Signature(a),w=>{w.Byte(2);TacticalNet.WriteAction(w,a);w.Bool(reload!=null).Byte((byte)Order).Bool(CeaseFire).String(Status??"");});
    }
    /// <summary>Client: the server's state of this companion.</summary>
    public void ApplyNetwork(ScNetReader r){
        var a=TacticalNet.ReadAction(r);bool reloading=r.Bool();int order=r.Byte();bool cease=r.Bool();string status=r.String(64);
        double now=time?.GameTime??0;actions.Mirror(a.Asset,a.Kind,a.Clip,now-a.Elapsed,a.Duration,a.Sequence);
        mirroredReload=reloading;if(order<=2)Order=(TacticalOrder)order;CeaseFire=cease;Status=status;
    }
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
    /// <summary>Unknown owner, e.g. a shell respawned from an old build's empty unload record. Never auto-claimed.</summary>
    public bool OwnerMissing=>OwnerIndex<0;
    /// <summary>Leaves only committed inventory state before the entity moves into the dormant ledger.
    /// An unfinished reload has charged nothing yet, so cancelling it neither eats nor duplicates ammo.</summary>
    public void PrepareDormancy(){reload?.Cancel();reload=null;actions.Clear();threat=null;lastVoiceTarget=null;path?.Stop();}
    public void RestoreDormancy(SubsystemTacticalCompanions.Dormant d){
        OwnerIndex=d.OwnerIndex;Order=(TacticalOrder)d.Order;CeaseFire=d.CeaseFire;GuardPosition=d.GuardPosition;DeathHandled=false;
        Creature.ComponentHealth.Health=d.Health;
        if(Inventory.SlotsCount!=SubsystemTacticalCompanions.Slots)throw new InvalidOperationException("战术同伴装备栏格式不受支持。");
        for(int i=0;i<SubsystemTacticalCompanions.Slots;i++){var slot=Inventory.m_slots[i];slot.Value=d.Counts[i]>0?d.Values[i]:0;slot.Count=d.Counts[i];}
        for(int i=0;i<SubsystemTacticalCompanions.Slots;i++)if(Inventory.GetSlotCount(i)!=d.Counts[i]||d.Counts[i]>0&&Inventory.GetSlotValue(i)!=d.Values[i])throw new InvalidOperationException("同伴装备恢复校验失败。");
        lastVoiceOrder=Order;ScInventoryTransaction.Changed(Inventory);
    }
    public string VoiceRole=>Entity?.ValuesDictionary?.DatabaseObject?.Name switch{"ScTacticalCT"=>"ct","ScTacticalT"=>"t",_=>null};
    /// <summary>Confirms a real order change immediately (the equipment panel may still be open); repeats stay silent.</summary>
    public void Command(TacticalOrder order){
        bool changed=order!=Order;Order=order;GuardPosition=Creature.ComponentBody.Position;nextPath=0;path.Stop();
        if(changed){lastVoiceOrder=order;ScAgentVoice.Emit(Entity,VoiceRole,order switch{TacticalOrder.Follow=>"follow",TacticalOrder.Guard=>"wait",_=>"inposition"});}
    }
    public bool Friendly(ComponentBody b)=>b==null||b.Entity==Entity||b.Entity.FindComponent<ComponentPlayer>()!=null||b.Entity.FindComponent<ComponentTacticalCompanion>()!=null||b.Entity.FindComponent<ComponentMount>() is {} mount&&b.ChildBodies.Count>0;
    public void Alert(ComponentBody b){if(Friendly(b)||b.Entity.FindComponent<ComponentHealth>() is not {Health:>0})return;threat=b;threatUntil=time.GameTime+10;}
    bool Hostile(ComponentBody b,ComponentPlayer owner){
        if(Friendly(b)||b.Entity.FindComponent<ComponentCreature>() is not {} creature||creature.ComponentHealth.Health<=0)return false;
        // The squads are neutral until attacked (user rule 2026-10-02): a companion does not open fire on one for what it
        // is. It fights one that has turned on its owner, on itself or on another companion of that owner; a fight its
        // owner starts, or an attack on its owner or itself, reaches it through Alert (TacticalModLoader.ProcessAttackment).
        if(b.Entity.FindComponent<ComponentTacticalEnemy>() is {} squad)
            return squad.TargetBody is {} aimed&&(aimed.Entity==owner.Entity||aimed.Entity==Entity||aimed.Entity.FindComponent<ComponentTacticalCompanion>()?.OwnedBy(owner)==true);
        if((creature.Category&(CreatureCategory.LandPredator|CreatureCategory.WaterPredator))!=0)return true;
        // Include mod creatures using the native chase behavior, even if classified LandOther.
        return b.Entity.FindComponents<ComponentChaseBehavior>().Any(c=>c.Target==owner||c.Target==Creature||(c.m_autoChaseMask&owner.Category)!=0);
    }
    bool Visible(ComponentBody target){
        var start=Creature.ComponentBody.Position+Vector3.UnitY*1.45f;var end=target.BoundingBox.Center();
        if(Project.FindSubsystem<SubsystemScGrenades>(false)?.SmokeBlocksSight(start,end)==true)return false; // same smoke rule as enemies
        var hit=Project.FindSubsystem<SubsystemBodies>(true).Raycast(start,end,0,(b,d)=>b.Entity!=Entity);
        var wall=Project.FindSubsystem<SubsystemTerrain>(true).Raycast(start,end,false,true,(v,d)=>ScGunRange.TerrainStopsBullet(v));
        return hit.HasValue&&hit.Value.ComponentBody==target&&(!wall.HasValue||wall.Value.Distance>=hit.Value.Distance);
    }
    void Acquire(ComponentPlayer owner,double now){
        if(now<nextScan)return;nextScan=now+.5;
        var nearby=new DynamicArray<ComponentBody>();Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(Creature.ComponentBody.Position.XZ,24,nearby);
        foreach(var b in nearby.Where(b=>Hostile(b,owner)&&Vector3.DistanceSquared(b.Position,Creature.ComponentBody.Position)<=24*24&&Vector3.DistanceSquared(b.Position,owner.ComponentBody.Position)<=32*32).OrderBy(b=>Vector3.DistanceSquared(b.Position,Creature.ComponentBody.Position)))if(Visible(b)){Alert(b);break;}
    }
    /// <summary>A reachable-looking point 2.5 m to the side of the blocked direction, preferring a side without a wall.</summary>
    Vector3? Detour(ComponentBody body,Vector3 dest){
        var d=(dest-body.Position).XZ;if(d.LengthSquared()<.01f)return null;d=Vector2.Normalize(d);var terrain=Project.FindSubsystem<SubsystemTerrain>(true);
        foreach(var side in new[]{new Vector2(-d.Y,d.X),new Vector2(d.Y,-d.X)})if(TacticalNavigation.Probe(terrain,body,side)!=TacticalNavigation.Step.Wall)
            return body.Position+new Vector3(side.X,0,side.Y)*2.5f;
        return null;
    }
    void StopMoving(){
        path.Stop();var pilot=path.m_componentPilot;pilot.m_turnOrder=Vector2.Zero;pilot.m_walkOrder=null;pilot.m_swimOrder=null;pilot.m_flyOrder=null;
        Creature.ComponentLocomotion.TurnOrder=Vector2.Zero;Creature.ComponentLocomotion.WalkOrder=null;
    }
    public void Died(){if(DeathHandled)return;
        if(!ScNet.IsAuthority){DeathHandled=true;return;} // a multiplayer client replays the death; the server drops the items
        reload?.Cancel();reload=null;path.Stop();Inventory.DropAllItems(Creature.ComponentBody.BoundingBox.Center());DeathHandled=true;EndArmor();}
    /// <summary>This companion's protection values in SubsystemScArmor (current-direction-20260929): keyed by its entity ID,
    /// which the engine saves with it and never gives to another entity (the saved NextID only grows) and which a sleeping
    /// companion keeps when it wakes (SubsystemTacticalCompanions). Set up and repaired by its owner at the workbench.</summary>
    public string ArmorKey=>Entity is null?null:SubsystemScArmor.CompanionKey(Entity.Id);
    /// <summary>Death or dismissal ends the protection; nothing is refunded or dropped, and a new recruit starts without.</summary>
    public void EndArmor()=>Project?.FindSubsystem<SubsystemScArmor>(false)?.Remove(ArmorKey);
    public void Update(float dt){
        using var timing=ScTacticalPerformance.Measure(Project,ScTacticalPerformance.Stage.CompanionAI);
        if(!ScNet.IsAuthority)return; // a multiplayer client shows the server's companion (TacticalNet)
        if(Creature.ComponentHealth.Health<=0){Died();return;}
        Publish();
        int held=Inventory.GetSlotCount(0)>0?Inventory.GetSlotValue(0):0;
        string asset=ScInventoryTransaction.IsWeaponSlot(Inventory,0)?ScGunBlock.SpecOf(held).Name:null;
        bool changed=visualSelection.Observe(Inventory,0,held,ScInventoryTransaction.IsWeaponSlot(Inventory,0));
        if(changed||visualAsset!=asset){visualAsset=asset;actions.Start(asset,ScWeaponActionKind.Draw,"deploy",time.GameTime,.65f);}
        if(Creature.ComponentSpawn?.IsDespawning==true){StopMoving();reload?.Cancel();reload=null;return;}
        var owner=Owner;if(!IsActive||owner is null){StopMoving();reload?.Cancel();reload=null;actions.Clear();Status="等待主人";return;}
        PanelOpen=ScNet.IsLocal(owner)?owner.ComponentGui.ModalPanelWidget is TacticalPanel panel&&panel.Companion==this:Time.RealTime<RemotePanelUntil;
        if(PanelOpen){StopMoving();reload?.Cancel();reload=null;Status="整理装备";return;}
        double now=time.GameTime;var body=Creature.ComponentBody;
        // Danger outranks orders and combat: leave fire and a bomb radius whose fuse reached the evacuation time.
        var zones=TacticalDanger.Zones(Project,body.Position,false).ToList();
        if(TacticalDanger.Urgent(zones,body.Position) is {} danger){
            if(path.IsStuck){fleeAttempt++;nextFlee=0;}
            if(now>=nextFlee||!path.Destination.HasValue){nextFlee=now+.5;path.SetDestination(TacticalDanger.Exit(zones,danger,body.Position,body.Matrix.Forward,fleeAttempt),.8f,1.2f,250,false,true,true,null);}
            Status=danger.Bomb?"撤离爆炸范围":"离开火区";nextPath=now;return;
        }
        fleeAttempt=0;
        string voiceRole=VoiceRole;
        if(lastVoiceOrder.HasValue&&lastVoiceOrder.Value!=Order)ScAgentVoice.Emit(Entity,voiceRole,Order==TacticalOrder.Follow?"follow":"wait");
        lastVoiceOrder=Order;
        if(threat is not null&&(!threat.IsAddedToProject||threat.Entity.FindComponent<ComponentHealth>() is not {Health:>0}||now>threatUntil||Vector3.DistanceSquared(body.Position,threat.Position)>32*32))threat=null;
        bool shield=ScShieldProtection.Holding(body,out _);
        if(!CeaseFire&&threat is null)Acquire(owner,now);
        bool armed=ScInventoryTransaction.IsWeaponSlot(Inventory,0);float reach=1.7f;
        if(armed&&EffectiveGunStats.TrySnapshotValue(Inventory.GetSlotValue(0),out var gun))reach=Math.Min(24,EffectiveGunStats.Resolve(GunSpec.All[gun.Variant],Inventory.GetSlotValue(0),false).Range);
        bool fighting=!CeaseFire&&!shield&&threat!=null&&Vector3.DistanceSquared(body.Position,owner.ComponentBody.Position)<=20*20&&Vector3.DistanceSquared(threat.Position,owner.ComponentBody.Position)<=32*32;
        if(fighting&&threat!=lastVoiceTarget&&Visible(threat)){lastVoiceTarget=threat;ScAgentVoice.Emit(Entity,voiceRole,"spotted");}
        if(threat==null)lastVoiceTarget=null;
        if(fighting&&VisualAction.Kind==ScWeaponActionKind.Inspect)actions.Clear();
        // Follow the owner's last grounded position: a jumping owner is not a new floor to reach.
        var ownerBody=owner.ComponentBody;bool grounded=ownerBody.StandingOnValue.HasValue||ownerBody.StandingOnBody is not null;
        if(grounded||!ownerAnchor.HasValue||Vector3.DistanceSquared(ownerAnchor.Value,ownerBody.Position)>9)ownerAnchor=ownerBody.Position;
        if(now>=nextPath){nextPath=now+.5;
            Vector3 anchor=ownerAnchor.Value;
            Vector3 dest=Order switch{TacticalOrder.Guard=>GuardPosition,TacticalOrder.Cover=>anchor+ownerBody.Matrix.Forward*2.5f,_=>anchor};
            float radius=Order==TacticalOrder.Follow?1.8f:.6f;
            // Reaching a grounded owner means reaching the same floor, not just the same column.
            bool ignoreHeight=Order!=TacticalOrder.Guard&&!grounded;
            if(Vector3.DistanceSquared(body.Position,dest)>80*80){path.Stop();Status="距离过远，原地等待";return;}
            if(fighting){
                if(Vector3.DistanceSquared(body.Position,threat.Position)<=reach*reach&&Visible(threat))dest=body.Position;
                else if(Order!=TacticalOrder.Guard){dest=threat.Position;radius=armed?2:1.2f;ignoreHeight=true;}
            }
            dest=TacticalDanger.Clamp(zones,dest,body.Position); // wait on the safe ring instead of following into a live bomb radius or fire
            if(Vector2.DistanceSquared(body.Position.XZ,dest.XZ)>radius*radius||Math.Abs(body.Position.Y-dest.Y)>(ignoreHeight?1.5f:.9f)){
                // Let native navigation own rotation while walking. Do not continually restart its state machine.
                if(path.IsStuck){
                    // Bounded recovery: wait and re-plan, then one side detour, then report and pause; never roam or teleport.
                    stuckStage++;StopMoving();detour=null;nextPath=now+(stuckStage>=3?4:2);
                    if(stuckStage==2)detour=Detour(body,dest);
                    else if(stuckStage>=3){stuckStage=0;if(now>=unreachableNotice){unreachableNotice=now+20;TacticalNet.Tell(owner,"同伴暂时无法到达你的位置，请换条路或靠近一些。");}}
                }else{
                    if(detour.HasValue&&Vector2.DistanceSquared(body.Position.XZ,detour.Value.XZ)<1)detour=null;
                    TacticalNavigation.Navigate(path,detour??dest,shield?.35f:.7f,detour.HasValue?.8f:radius,250,false,ignoreHeight&&!detour.HasValue,true,fighting?threat:owner.ComponentBody,.75f);
                }
            }else{StopMoving();stuckStage=0;detour=null;}
        }
        TacticalNavigation.StepAssist(Creature,Project.FindSubsystem<SubsystemTerrain>(true),path.Destination,ref nextJump,now);
        if(!path.Destination.HasValue&&(shield||fighting)){
            var direction=fighting||shield&&threat!=null?threat.Position-body.Position:owner.ComponentBody.Matrix.Forward;
            if(direction.XZ.LengthSquared()>.01f)Creature.ComponentLocomotion.TurnOrder=new Vector2(Math.Clamp(Vector2.Angle(body.Matrix.Forward.XZ,direction.XZ)*.6f,-.35f,.35f),0);
        }
        Status=shield?"举盾掩护":CeaseFire?"停火":Order switch{TacticalOrder.Guard=>"原地警戒",TacticalOrder.Cover=>"前方掩护",_=>"跟随"};
        if(shield||CeaseFire){reload?.Cancel();reload=null;return;}
        if(armed)Shoot(now,owner);else if(fighting)Melee(now);
    }
    void Melee(double now){
        if(now<nextShot||Vector3.DistanceSquared(Creature.ComponentBody.Position,threat.Position)>1.7f*1.7f||!Visible(threat))return;
        var delta=threat.BoundingBox.Center()-Creature.ComponentBody.BoundingBox.Center();if(delta.LengthSquared()<.001f)return;
        nextShot=now+.8;ComponentMiner.AttackBody(new MeleeAttackment(threat,Entity,threat.BoundingBox.Center(),Vector3.Normalize(delta),6){AttackSoundVolume=0});Status="近战攻击";
    }
    void Shoot(double now,ComponentPlayer owner){
        if(!ScInventoryTransaction.IsWeaponSlot(Inventory,0))return;
        int value=Inventory.GetSlotValue(0);if(!EffectiveGunStats.TrySnapshotValue(value,out var state)||state.Durability<=0){Status="枪械需要维修";return;}
        var spec=GunSpec.All[state.Variant];var stats=EffectiveGunStats.Resolve(spec,value,false);
        if(reload is not null){Status="换弹中";if(!reload.Valid){reload.Cancel();reload=null;return;}if(now>=reloadAt){if(ScReloadTransaction.IsTube(spec.Name)){if(!reload.InsertShell()||GunSpec.GetRounds(Terrain.ExtractData(reload.Expected))>=stats.Capacity)reload=null;else {reloadAt=now+.65;actions.Start(spec.Name,ScWeaponActionKind.Reload,"reload",now,.65f);}}else{reload.InsertMagazine();reload=null;}nextShot=now+.25;}return;}
        if(state.Rounds<=0){
            if(spec.RechargeSeconds>0){if(state.RechargeReadyAt>=0&&now>=state.RechargeReadyAt){var tx=ScGunMutation.Prepare(Inventory,0,ScGunHolders.Key(Inventory,0),out _);tx?.Commit(r=>{r.Rounds=1;r.RechargeReadyAt=-1;r.RechargeCycleSeconds=0;});}return;}
            int ammo=ScAmmoBlock.Value(ScReloadTransaction.AmmoKind(spec)),cost=ScReloadTransaction.IsTube(spec.Name)?1:ScReloadTransaction.RequiredFor(spec,stats.Capacity);
            if(ScInventoryTransaction.Count(Inventory,ammo)<cost&&state.ReserveOverflowRounds<=0){Status="弹药不足";return;}
            reload=new(Inventory,0,value,ammo,cost,stats.Capacity,ScGunHolders.Key(Inventory,0));reload.Discard();reloadAt=now+(ScReloadTransaction.IsTube(spec.Name)?.65:2.8);
            actions.Start(spec.Name,ScWeaponActionKind.Reload,"reload",now,(float)(reloadAt-now));return;
        }
        if(threat is null||now<nextShot)return;
        var start=Creature.ComponentBody.Position+Vector3.UnitY*1.45f;var end=threat.BoundingBox.Center();var delta=end-start;float length=delta.Length();if(length<.1f||length>Math.Min(32,stats.Range))return;
        // The shared bullet trace (H1): terrain, then the nearest body part; only a clear line to the threat fires.
        if(TacticalGunfire.Trace(Project,Creature.ComponentBody,start,delta/length,length+.6f) is not {} hit||hit.Body!=threat)return;
        var mutation=ScGunMutation.Prepare(Inventory,0,ScGunHolders.Key(Inventory,0),out _);if(mutation==null)return;
        if(mutation.Commit(r=>{r.Rounds=Math.Max(0,r.Rounds-1);r.Durability=Math.Max(0,r.Durability-1);if(spec.RechargeSeconds>0&&r.Rounds==0){r.RechargeCycleSeconds=stats.RechargeSeconds;r.RechargeReadyAt=now+stats.RechargeSeconds;}})!=ScGunResult.Success)return;
        nextShot=now+Math.Max(.12,stats.CycleSeconds); // companion cap prevents frame-bound bursts on weaker phones
        actions.Start(spec.Name,ScWeaponActionKind.Shoot,"shoot",now,.16f);
        var credit=ScGunKillCredit.For(Terrain.ExtractData(mutation.Expected),false,0);var health=threat.Entity.FindComponent<ComponentHealth>();float before=health.Health;
        // All pellets are aimed at the visible torso. Keep total power and growth, never multiply shotgun damage by pellet count.
        // The part the ray reached counts the gun's own head multiplier (x2, the Zeus x1), as the player's guns do.
        var landed=new ScShotHits();Vector3 direction=delta/length;
        landed.Add(hit.Part,stats.Power*stats.Falloff(spec,length)*(hit.Part==ScHitPart.Head?stats.HeadMultiplier:1),start+direction*hit.Distance,direction);
        var attack=new ScSurvivalBalance.GunAttack(threat,Entity,landed.Point,direction,landed.Total,
            ScBulletProjectile.For(Entity,ScBulletProjectile.RoundOf(mutation.Expected),landed.Point,direction,landed.Total,spec.RechargeSeconds>0,now)){Hits=landed};
        ScProjectileDefense.Apply(threat,attack);ScDamageIndicator.AttackBody(attack);
        if(spec.RechargeSeconds>0)ScElectricStun.Apply(threat,before,health.Health,now);
        if(before>0&&health.Health<=0&&credit!=null&&ScGunKillRules.Counts(threat,owner,false,out _))ScGunRegistry.Current.Kills.Enqueue(credit.RecordId,credit.Variant);
        string shot=SubsystemScGunBlockBehavior.ExtensionShotSound(spec,!state.SilencerOff);
        Project.FindSubsystem<SubsystemAudio>(true).PlaySound(shot,.6f,0,start,8,true);TacticalNet.Sound(shot,.6f,start,8);
        Status="攻击";
    }
}
