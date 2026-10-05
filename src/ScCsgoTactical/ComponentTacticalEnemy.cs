using Engine;
using TemplatesDatabase;
using GameEntitySystem;
namespace Game;

public sealed class ComponentTacticalEnemy : ComponentBehavior,IUpdateable,INoiseListener,IScArmorKey {
    public TacticalEnemyState State;
    public ComponentCreature Creature;
    public Vector3 Home;
    public ComponentBody TargetBody;
    ComponentPathfinding path;SubsystemTacticalEnemies director;SubsystemTerrain terrain;SubsystemBodies bodies;SubsystemTime time;
    readonly Engine.Random random=new();
    float senseLeft,pathLeft,lost,aim,plant,burstPause,retreat,search,fleeLeft;int burst,fleeAttempt;double nextJump;
    /// <summary>True while the current target attacked this enemy first: a bounded retaliation, not proactive aggro.</summary>
    public bool Retaliating;
    // Engagement policy. User request 2026-10-02 (mpc3 feedback): "野生匪和手动召唤出来的匪都默认中立；一个受到攻击后，
    // 附近所有匪共同攻击同一个攻击者". Nobody is engaged unprovoked any more, whatever the squad's source (natural, summoned,
    // or an older save without one): coming near, a shot fired into the air and the end of a summoned squad's warning
    // window grant no target. An effective attack on one of them, by an attacker that exists and is not one of them, makes
    // every living one within <see cref="ProvokeRange"/> of the one that was hit take that same attacker as its target
    // (SubsystemTacticalEnemies.Provoked: once per attack, from the victim's position; the ones told do not tell others
    // in turn). Walls and smoke still block shots, and hidden attackers are forgotten after the memory window.
    // The 2026-10-06 request removes the retaliation distance cap. The earlier policy (agent-followup-140
    // F2, widened by video-feedback-20260929 R3) had every member look for players and companions on its own; the
    // "everyone nearby joins in" the player saw came from that, so it is now explicit.
    public const float LeashRange=48,RetaliationMemory=8,ProactiveMemory=6;
    /// <summary>How far from the one that was hit the others join in: the distance at which a member used to notice a
    /// player by itself (32), a candidate value: the user named "nearby" without a number.</summary>
    public const float ProvokeRange=32;
    public static float DisengageRange(TacticalRole role)=>role==TacticalRole.Sniper?44:40;
    public static float RetaliationRange(TacticalRole role)=>float.PositiveInfinity;
    /// <summary>A summoned squad looks toward the one who summoned it and, once its warning window is over, walks a bounded
    /// search toward that position. It grants no target, and since 2026-10-02 neither does arriving there: the squad is
    /// neutral until one of them is attacked.</summary>
    public void Investigate(Vector3 position,float seconds){
        var body=Creature.ComponentBody;var d=(position-body.Position).XZ;
        if(d.LengthSquared()>.01f)body.Rotation=Quaternion.CreateFromYawPitchRoll(MathF.Atan2(-d.X,-d.Y),0,0);
        if(TargetBody is null){lastSeen=position;search=Math.Max(search,Math.Clamp(seconds,0,30));}
    }
    bool seen;
    int strafeSide=1;
    float strafeLeft;
    Vector3 noisePosition;
    float coverLeft,noiseCooldown;
    ComponentBody lastVoiceTarget;
    // ---- visible actions (r2-c4-completion-20260929) ----
    // A grenade used to appear at once 1.5 m above the feet, inside the thrower's own box, while the enemy kept its gun;
    // the bomb was planted with the gun in hand and no visible crouch. Both now run as actions on the gameplay clock:
    // what is drawn (held item, arms) reads the same state that commits the grenade or the charge, exactly once.
    /// <summary>Seconds of a throw: pulling the pin, holding, the swing up to the release (CS2's overhand throw lets
    /// go 0.27 s in), the follow-through, then the gun is drawn again before it can fire.</summary>
    public const float ThrowPull=.6f,ThrowHold=.2f,ThrowWind=.27f,ThrowFollow=.8f,RedrawSeconds=.65f;
    sealed class ThrowAction {public int Kind;public double Started,ThrowAt,ReleaseAt,EndAt;public bool Released;}
    ThrowAction throwing;
    /// <summary>Seconds the empty hands recover after a bomb is set down (the end of CS2's plant clip).</summary>
    public const float PlantRecovery=ScPlantPhase.EndSeconds-ScPlantPhase.PlantSeconds;
    double plantedAt=-1;Vector3 plantedPosition;long plantSequence;
    /// <summary>The throw being drawn (default when none), from the same timeline that releases the grenade.</summary>
    public ScThrowPhase ThrowPhase {get{
        if(throwing is not {} t||State is null||time is null)return default;double now=time.GameTime;
        int stage=now<t.Started+ThrowPull?0:now<t.ThrowAt?1:2;
        float pull=(float)Math.Clamp((now-t.Started)/ThrowPull,0,1),wind=stage<2?0:(float)Math.Clamp((now-t.ThrowAt)/ThrowWind,0,1),follow=stage<2||!t.Released?0:(float)Math.Clamp((now-t.ReleaseAt)/ThrowFollow,0,1);
        return new(ScGrenadeBlock.Value(t.Kind),ScGrenadeBlock.Assets[t.Kind],stage,false,t.Released,pull,stage==1?(float)(now-t.Started-ThrowPull):0,wind,follow);
    }}
    /// <summary>The plant being drawn (default when none): operating while the charge is not yet committed, then the recovery.</summary>
    public ScPlantPhase PlantPhase=>State is null||time is null?default:plantedAt>=0?new(ScC4Block.Value,true,(float)(ScPlantPhase.PlantSeconds+time.GameTime-plantedAt),plantSequence,plantedPosition)
        :plant>0?new(ScC4Block.Value,false,plant,plantSequence,Creature.ComponentBody.Position):default;
    /// <summary>Planting and its recovery crouch the enemy (r2-c4-completion-20260929). An NPC body cannot crouch natively:
    /// ComponentBody.CanCrouch is set only for players, and standing up again reads a ComponentRider the NPC templates do
    /// not have, so every TargetCrouchFactor written for an enemy was silently 0 in game (c03). The crouch is the enemy's
    /// own instead: this intent, eased by <see cref="CrouchFactor"/> at the engine's 2 per second on the game clock; the
    /// actor's crouch clips and the crouched plant follow it. The physics body keeps standing size.</summary>
    public bool Crouching=>plant>0||plantedAt>=0;
    public float CrouchFactor{get;private set;}
    /// <summary>The item the enemy is seen holding: the grenade or bomb of a running action, otherwise its gun.</summary>
    public int PresentedValue=>State is null?0:throwing is {} t?ScGrenadeBlock.Value(t.Kind):plantedAt>=0||plant>0?ScC4Block.Value:State.DisplayValue;
    /// <summary>After an action the gun comes back with its draw; it cannot fire before that ends.</summary>
    void Redraw(){if(State is null)return;actions.Start(GunSpec.All[State.Variant].Name,ScWeaponActionKind.Draw,"deploy",time.GameTime,RedrawSeconds);State.ShotLeft=Math.Max(State.ShotLeft,RedrawSeconds);}
    /// <summary>Ends a throw that has not released: nothing is thrown and no grenade is spent.</summary>
    void CancelThrow(){if(throwing is null)return;throwing=null;if(Creature.ComponentHealth.Health>0)Redraw();}
    void AbandonPlant(){if(plant<=0&&plantedAt<0)return;plant=0;plantedAt=-1;Creature.ComponentBody.TargetCrouchFactor=0;if(Creature.ComponentHealth.Health>0)Redraw();}
    readonly ScWeaponActionTimeline actions=new();
    string visualAsset;
    public ScWeaponAction VisualAction {
        get {
            if(State==null||Creature?.ComponentHealth.Health<=0)return default;
            var a=actions.Read(time?.GameTime??0);
            if(State.ReloadLeft>0){float duration=State.Role==TacticalRole.Machine?4:2.8f;return new(GunSpec.All[State.Variant].Name,ScWeaponActionKind.Reload,"reload",a.Sequence,Math.Max(0,duration-State.ReloadLeft),duration,Math.Max(0,duration-State.ReloadLeft));}
            return a.Kind==ScWeaponActionKind.Reload?default:a;
        }
    }
    Vector3 lastSeen;
    // ---- multiplayer (TacticalNet): the server runs the enemy; a client draws these copies of its visible state
    long Signature(ScWeaponAction a)=>HashCode.Combine(a.Sequence,a.Kind==ScWeaponActionKind.Idle,State.ReloadLeft>0,throwing is null?0:throwing.Released?2:1,plant>0,plantedAt>=0,plantSequence);
    void Publish(){
        double now=time.GameTime;var a=actions.Read(now);
        TacticalNet.Publish(Entity,Signature(a),w=>{
            w.Byte(1);TacticalNet.WriteAction(w,a);w.Float(State.ReloadLeft);
            w.Bool(throwing is not null);if(throwing is {} t)w.Byte((byte)t.Kind).Float((float)(now-t.Started)).Bool(t.Released);
            w.Float(plant).Float(plantedAt>=0?(float)(now-plantedAt):-1).Vector3(plantedPosition).Long(plantSequence);
        });
    }
    /// <summary>Client: the server's visible state of this enemy.</summary>
    public void ApplyNetwork(ScNetReader r){
        var a=TacticalNet.ReadAction(r);float reloadLeft=Math.Clamp(r.Float(),0,10);
        ThrowAction t=null;
        if(r.Bool()){int kind=r.Byte();float age=Math.Clamp(r.Float(),0,10);bool released=r.Bool();
            if(kind<ScGrenadeBlock.Assets.Length){double started=(time?.GameTime??0)-age;t=new ThrowAction{Kind=kind,Started=started,ThrowAt=started+ThrowPull+ThrowHold,ReleaseAt=started+ThrowPull+ThrowHold+ThrowWind,EndAt=started+ThrowPull+ThrowHold+ThrowWind+ThrowFollow,Released=released};}}
        float planting=Math.Clamp(r.Float(),0,ScPlantPhase.PlantSeconds),plantedAge=r.Float();Vector3 planted=r.Vector3();long sequence=r.Long();
        if(State is null)return;
        double now=time?.GameTime??0;actions.Mirror(a.Asset,a.Kind,a.Clip,now-a.Elapsed,a.Duration,a.Sequence);
        State.ReloadLeft=reloadLeft;throwing=t;plant=planting;plantedAt=plantedAge>=0?now-Math.Min(plantedAge,PlantRecovery):-1;plantedPosition=planted;plantSequence=sequence;
    }
    /// <summary>Client, each frame: the mirrored actions run on to their ends here; the server's next state corrects them.</summary>
    public void ClientTick(float dt){
        if(State is null||time is null)return;dt=Math.Clamp(dt,0,.5f);double now=time.GameTime;
        CrouchFactor=Crouching?Math.Min(1,CrouchFactor+2*dt):Math.Max(0,CrouchFactor-2*dt);
        if(State.ReloadLeft>0)State.ReloadLeft=Math.Max(0,State.ReloadLeft-dt);
        if(throwing is {} t&&now>=t.EndAt)throwing=null;
        if(plant>0)plant=Math.Min(ScPlantPhase.PlantSeconds,plant+dt);
        if(plantedAt>=0&&now-plantedAt>=PlantRecovery)plantedAt=-1;
    }
    public UpdateOrder UpdateOrder=>UpdateOrder.Default;
    public override float ImportanceLevel=>State is not null&&Creature.ComponentHealth.Health>0?100:0;
    public override void Load(ValuesDictionary values,IdToEntityMap map){
        Creature=Entity.FindComponent<ComponentCreature>(true);path=Entity.FindComponent<ComponentPathfinding>(true);director=Project.FindSubsystem<SubsystemTacticalEnemies>(true);
        terrain=Project.FindSubsystem<SubsystemTerrain>(true);bodies=Project.FindSubsystem<SubsystemBodies>(true);time=Project.FindSubsystem<SubsystemTime>(true);
        string saved=values.GetValue("EnemyState","");if(saved.Length>0)Restore(saved);Home=values.GetValue("PatrolHome",Creature.ComponentBody.Position);
    }
    /// <summary>First-pass role tiers (agent-feedback-20260928): the health fraction is unchanged, so injured squads
    /// keep their ratio. Tune from play tests against ordinary creatures and player guns.</summary>
    public static float RoleResilience(TacticalRole role)=>role switch{TacticalRole.Sniper or TacticalRole.Demolition=>100,TacticalRole.Machine=>160,_=>120};
    void ApplyRole(){if(State is not null)Creature.ComponentHealth.AttackResilience=RoleResilience(State.Role);}
    public string Capture(){State.Health=Creature.ComponentHealth.Health;return State.Encode();}
    public void Restore(string json){State=TacticalEnemyState.Decode(json);Creature.ComponentHealth.Health=State.Health;ApplyRole();}
    public override void Save(ValuesDictionary values,EntityToIdMap map){if(State is null)throw new InvalidOperationException("敌方装备尚未初始化。");values.SetValue("EnemyState",Capture());values.SetValue("PatrolHome",Home);}
    public void Configure(TacticalEnemyState state,Vector3 position){State=state;ApplyRole();Home=position;Creature.ComponentBody.Position=position;Creature.ConstantSpawn=false;ScAgentVoice.Emit(Entity,"t","spawn");}
    /// <summary>This enemy's protection values in SubsystemScArmor (current-direction-20260929): its squad and role name it
    /// (a squad never has two members of one role), so the values follow it through saves, sleep and wake-up without any
    /// field of its own. Drawn once when the enemy is created (SubsystemTacticalEnemies.CreateSquad).</summary>
    public string ArmorKey=>State is null?null:SubsystemScArmor.EnemyKey(State.Squad,(int)State.Role);
    bool Friendly(ComponentBody body)=>body is null||body.Entity==Entity||body.Entity.FindComponent<ComponentTacticalEnemy>() is not null;
    /// <summary>One of the others nearby was attacked (SubsystemTacticalEnemies.Provoked): this one takes the same attacker.
    /// It keeps aiming when it is already on that attacker (a second hit on a squad mate does not restart its aim), and
    /// keeps an attacker it is already retaliating against while that one is alive.</summary>
    public void Provoke(ComponentBody attacker){
        if(State is null||Creature.ComponentHealth.Health<=0||Friendly(attacker)||attacker.Entity.FindComponent<ComponentHealth>() is not {Health:>0})return;
        if(ReferenceEquals(TargetBody,attacker)&&Retaliating){lost=Math.Max(lost,RetaliationMemory);return;}
        if(TargetBody is not null&&Retaliating&&TargetBody.IsAddedToProject&&TargetBody.Entity.FindComponent<ComponentHealth>() is {Health:>0})return;
        Alert(attacker);
    }
    public void Alert(ComponentBody attacker){if(Friendly(attacker)||attacker.Entity.FindComponent<ComponentHealth>() is not {Health:>0})return;NoteEncounter(attacker,"seen");TargetBody=attacker;Retaliating=true;lastSeen=attacker.Position;lost=RetaliationMemory;aim=0;seen=false;senseLeft=0;if(plant>0)AbandonPlant();else Creature.ComponentBody.TargetCrouchFactor=0;}
    public void HearNoise(ComponentBody source,Vector3 position,float loudness){
        // Native position-only gunshots have no source body. Footsteps (.25) and our own squad are ignored.
        if(State is null||noiseCooldown>0||source is not null&&Friendly(source)||loudness<.5f||Vector3.DistanceSquared(Creature.ComponentBody.Position,position)>40*40)return;
        noisePosition=position;coverLeft=2.5f;noiseCooldown=3;pathLeft=0;NoteEncounter(source,"heard");
    }
    /// <summary>Encounter observation for the director's bounded log (players only).</summary>
    void NoteEncounter(ComponentBody body,string what){if(State is not null&&body?.Entity.FindComponent<ComponentPlayer>() is not null)director?.Note(State.Squad,what);}
    bool Visible(ComponentBody target){
        var from=Creature.ComponentBody.Position+Vector3.UnitY*1.45f;var to=target.BoundingBox.Center();
        if(Project.FindSubsystem<SubsystemScGrenades>(true).SmokeBlocksSight(from,to))return false;
        if(terrain.Raycast(from,to,false,true,(v,d)=>ScGunRange.TerrainStopsBullet(v)).HasValue)return false;
        var body=bodies.Raycast(from,to,0,(b,d)=>b.Entity!=Entity);return body.HasValue&&body.Value.ComponentBody==target;
    }
    public void Update(float dt){
        using var timing=ScTacticalPerformance.Measure(Entity?.Project,ScTacticalPerformance.Stage.EnemyAI);
        if(State is null||!ScNet.IsAuthority)return; // a multiplayer client: ClientTick from SubsystemTacticalEnemies
        Publish();
        CrouchFactor=Crouching?Math.Min(1,CrouchFactor+2*Math.Clamp(dt,0,.5f)):Math.Max(0,CrouchFactor-2*Math.Clamp(dt,0,.5f));
        string asset=GunSpec.All[State.Variant].Name;
        if(visualAsset!=asset){visualAsset=asset;actions.Start(asset,ScWeaponActionKind.Draw,"deploy",time.GameTime,.65f);}
        // Dying mid-action: nothing more leaves the hand (a grenade not yet released is not thrown, a bomb not yet
        // committed is not planted).
        if(Creature.ComponentHealth.Health<=0){throwing=null;plant=0;plantedAt=-1;Died();return;}
        if(Creature.ComponentSpawn.IsDespawning){path.Stop();throwing=null;plant=0;plantedAt=-1;Creature.ComponentBody.TargetCrouchFactor=0;return;}
        if(!IsActive){CancelThrow();if(plant>0)AbandonPlant();return;}dt=Math.Clamp(dt,0,.5f);var body=Creature.ComponentBody;var spec=GunSpec.All[State.Variant];
        State.Warmup=Math.Max(0,State.Warmup-dt);
        State.ShotLeft=Math.Max(0,State.ShotLeft-dt);State.GrenadeLeft=Math.Max(0,State.GrenadeLeft-dt);burstPause=Math.Max(0,burstPause-dt);search=Math.Max(0,search-dt);
        coverLeft=Math.Max(0,coverLeft-dt);noiseCooldown=Math.Max(0,noiseCooldown-dt);
        // Reload owns ammo timing only. Sensing, retaliation and navigation keep running throughout it.
        if(State.ReloadLeft>0){State.ReloadLeft=Math.Max(0,State.ReloadLeft-dt);if(State.ReloadLeft==0){int n=Math.Min(spec.Magazine-State.Rounds,State.Reserve);State.Rounds+=n;State.Reserve-=n;State.ShotLeft=Math.Max(State.ShotLeft,.15f);}}
        // Danger outranks blindness and combat: a squad knows its own bombs, and nobody stands in fire on purpose.
        var zones=TacticalDanger.Zones(Project,body.Position,true).ToList();fleeLeft-=dt;
        if(TacticalDanger.Urgent(zones,body.Position) is {} danger){
            aim=0;CancelThrow();AbandonPlant();body.TargetCrouchFactor=0;
            if(path.IsStuck){fleeAttempt++;fleeLeft=0;}
            if(fleeLeft<=0||!path.Destination.HasValue){fleeLeft=.65f;path.SetDestination(TacticalDanger.Exit(zones,danger,body.Position,body.Matrix.Forward,fleeAttempt),.9f,1.5f,200,true,true,true,null);}
            return;
        }
        fleeAttempt=0;
        if(Project.FindSubsystem<SubsystemScGrenades>(true).IsBodyBlinded(body)){path.Stop();aim=0;CancelThrow();AbandonPlant();body.TargetCrouchFactor=0;seen=false;return;}
        // A throw or the recovery after planting runs to its end; nothing else is done meanwhile.
        if(throwing is not null){AdvanceThrow();return;}
        if(plantedAt>=0){path.Stop();if(time.GameTime-plantedAt>=PlantRecovery){plantedAt=-1;body.TargetCrouchFactor=0;Redraw();pathLeft=0;}return;}
        if(retreat>0){retreat=Math.Max(0,retreat-dt);return;}
        senseLeft-=dt;pathLeft-=dt;strafeLeft-=dt;
        if(TargetBody is not null){
            // Proactive targets are released beyond the disengage range or when chasing too far from home;
            // a retaliation lasts to its own range. Separate acquire/release distances prevent flip-flopping.
            float limit=Retaliating?RetaliationRange(State.Role):DisengageRange(State.Role);
            if(!TargetBody.IsAddedToProject||TargetBody.Entity.FindComponent<ComponentHealth>() is not {Health:>0}
                ||Vector3.DistanceSquared(body.Position,TargetBody.Position)>limit*limit
                ||!Retaliating&&Vector3.DistanceSquared(body.Position,Home)>LeashRange*LeashRange)Drop();
        }
        if(senseLeft<=0){senseLeft=State.Role==TacticalRole.Sniper?.15f:.35f+random.Float(0,.15f);
            // Neutral by default: no one is looked for here. A target only comes from an attack (Alert, Provoke).
            seen=TargetBody!=null&&Visible(TargetBody);
            if(seen){lastSeen=TargetBody.Position;lost=Retaliating?RetaliationMemory:ProactiveMemory;}
        }
        if(TargetBody is not null&&!seen){lost-=dt;if(lost<=0)Drop();}
        bool clear=TargetBody!=null&&seen;float distance=TargetBody is null?float.MaxValue:Vector3.Distance(body.Position,TargetBody.Position);
        if(clear&&TargetBody!=lastVoiceTarget){lastVoiceTarget=TargetBody;ScAgentVoice.Emit(Entity,"t","spotted");}
        if(TargetBody==null)lastVoiceTarget=null;
        // A summoned squad's warning window: no aim accumulates, so fire starts only after the full aim time that follows.
        if(!clear||State.Warmup>0||State.Role==TacticalRole.Sniper&&body.Velocity.XZ.LengthSquared()>.09f)aim=0;else aim+=dt;
        // Chasing never leads back into a known bomb radius or fire; inside one (before the evacuation time) walk out while still fighting.
        void Go(Vector3 dest,float speed,float range,int limit,ComponentBody avoid)=>TacticalNavigation.Navigate(path,TacticalDanger.Clamp(zones,dest,body.Position),speed,range,limit,true,false,true,avoid);
        if(pathLeft<=0){pathLeft=.65f;
            if(zones.FirstOrDefault(z=>z.Bomb&&TacticalDanger.Horizontal(body.Position,z.Center)<z.Radius+TacticalDanger.BombMargin) is {Bomb:true} hold)
                path.SetDestination(TacticalDanger.Exit(zones,hold,body.Position,body.Matrix.Forward,fleeAttempt),.65f,1.5f,200,true,true,true,null);
            else if(coverLeft>0&&State.Warmup<=0){
                if(!path.Destination.HasValue||path.IsStuck){
                    if(TacticalCombatMovement.Cover(terrain,body,noisePosition) is {} cover)Go(cover,.9f,.6f,160,null);
                    else path.Stop();
                }
            }
            else if(TargetBody is null){if(search>0&&State.Warmup<=0)Go(lastSeen,.45f,3,160,null);else if(search>0)path.Stop();else if(Vector3.DistanceSquared(body.Position,Home)>16)Go(Home,.45f,2,160,null);else path.Stop();}
            else if(clear&&State.Role==TacticalRole.Sniper)path.Stop();
            else if(clear&&State.Warmup<=0&&TacticalCombatMovement.IsSmg(spec.Name)&&distance<=24){
                if(strafeLeft<=0||path.IsStuck||!path.Destination.HasValue){
                    strafeSide=-strafeSide;strafeLeft=1.2f+random.Float(0,.6f);
                    var step=TacticalCombatMovement.Strafe(terrain,body,lastSeen,strafeSide)??TacticalCombatMovement.Strafe(terrain,body,lastSeen,-strafeSide);
                    if(step is {} at)Go(at,.85f,.6f,120,TargetBody);else path.Stop();
                }
            }
            else if(!clear||distance>(State.Role==TacticalRole.Close?9:22))Go(lastSeen,.65f,3,200,TargetBody);
            else path.Stop();
        }
        TacticalNavigation.StepAssist(Creature,terrain,path.Destination,ref nextJump,time.GameTime);
        // One steering source: the native pilot turns while walking; a standing enemy turns toward its target smoothly.
        if((TargetBody is not null||search>0)&&!path.Destination.HasValue){var delta=lastSeen-body.Position;delta.Y=0;
            if(delta.LengthSquared()>.01f)Creature.ComponentLocomotion.TurnOrder=new Vector2(Math.Clamp(Vector2.Angle(body.Matrix.Forward.XZ,delta.XZ)*.8f,-.6f,.6f),0);}
        if(State.ReloadLeft>0)return;
        if(State.Bomb&&clear&&distance>=9&&distance<=22&&body.StandingOnValue.HasValue&&body.Velocity.LengthSquared()<.2f){
            if(plant<=0)plantSequence++;
            path.Stop();plant+=dt;body.TargetCrouchFactor=1;
            // After planting, the squad-wide danger rules keep every member outside the radius until it resolves.
            if(plant>=ScPlantPhase.PlantSeconds){
                if(Visible(TargetBody)&&Project.FindSubsystem<SubsystemTacticalBombs>(true).PlantFrom(body.Position,MathF.Atan2(body.Matrix.Forward.X,body.Matrix.Forward.Z),State.Rewardable)){
                    State.Bomb=false;State.ShotLeft=3;pathLeft=0;plant=0;plantedAt=time.GameTime;plantedPosition=body.Position; // the hands recover empty, crouched
                }else AbandonPlant();
            }
            return;
        }
        if(plant>0)AbandonPlant();
        if(!clear||State.Warmup>0)return;
        TryGrenade(distance);if(throwing is not null)return;
        if(State.Rounds==0){if(State.Reserve>0){State.ReloadLeft=State.Role==TacticalRole.Machine?4:2.8f;Play("reload");}else if(pathLeft<=.1f)Go(body.Position-body.Matrix.Forward*10,.8f,1,160,null);return;}
        if(State.ShotLeft>0||burstPause>0||aim<(State.Role==TacticalRole.Sniper?1.4f:.55f))return;
        if(!Retaliating&&State.Role==TacticalRole.Close&&distance>18)return;
        if(!Visible(TargetBody)){seen=false;aim=0;return;}
        NoteEncounter(TargetBody,"engaged");Shoot();State.ShotLeft=Math.Max(ScGunGrowth.ShotInterval(State.Variant,spec.CycleSeconds,0),State.Role==TacticalRole.Sniper?.65f:.12f);
        if(++burst>=(State.Role==TacticalRole.Machine?8:State.Role==TacticalRole.Sniper?1:3)){burst=0;burstPause=State.Role==TacticalRole.Sniper?.6f:.75f;}
    }
    void Drop(){TargetBody=null;Retaliating=false;aim=0;seen=false;}
    void Play(string kind){
        actions.Start(GunSpec.All[State.Variant].Name,kind=="shot"?ScWeaponActionKind.Shoot:ScWeaponActionKind.Reload,kind=="shot"?"shoot":"reload",time.GameTime,kind=="shot"?.16f:State.ReloadLeft);
        if(kind=="shot"){string shot=SubsystemScGunBlockBehavior.ExtensionShotSound(GunSpec.All[State.Variant],false);
            Project.FindSubsystem<SubsystemAudio>(true).PlaySound(shot,.8f,0,Creature.ComponentBody.Position,20,true);TacticalNet.Sound(shot,.8f,Creature.ComponentBody.Position,20);}
    }
    void Shoot(){
        var spec=GunSpec.All[State.Variant];var body=Creature.ComponentBody;Vector3 from=body.Position+Vector3.UnitY*1.45f;
        float error=State.Role==TacticalRole.Sniper?.15f:.45f;error+=body.Velocity.Length()*.12f;
        var point=TargetBody.BoundingBox.Center()+new Vector3(random.Float(-error,error),random.Float(-error,error),random.Float(-error,error));
        var direction=Vector3.Normalize(point-from);float range=Retaliating?Vector3.Distance(from,point)+2:State.Role==TacticalRole.Sniper?64:State.Role==TacticalRole.Close?18:40;
        State.Rounds--;Play("shot");
        // The balanced Lv0 whole-shot budget (enemy templates have neither skin nor counter growth); against a player
        // the hostile table instead (H2). A shotgun's pellets share the budget, each traced on its own (H1).
        var stats=EffectiveGunStats.ResolveLevel(spec,State.DisplayValue,false,0);
        int pellets=Math.Max(1,spec.Pellets);var shots=new Dictionary<ComponentBody,ScShotHits>();
        for(int i=0;i<pellets;i++){
            var d=pellets>1?TacticalGunfire.Scatter(direction,TacticalGunfire.PelletCone,random):direction;
            if(TacticalGunfire.Trace(Project,body,from,d,range) is not {} hit||Friendly(hit.Body))continue;
            bool player=hit.Body.Entity.FindComponent<ComponentPlayer>() is not null;
            float budget=player?PlayerBudget(spec,stats.Power):stats.Power;
            // Retaliation extends the trace and the damage curve together. Keep the far-range floor instead of the
            // handling table's zero beyond its normal range (otherwise distant shots are only an animation).
            float power=budget/pellets*stats.Falloff(spec,Retaliating?Math.Min(hit.Distance,stats.Range):hit.Distance)*(player&&hit.Part==ScHitPart.Head?TacticalHostileBalance.PlayerHeadMultiplier:1);
            if(!shots.TryGetValue(hit.Body,out var landed))shots[hit.Body]=landed=new ScShotHits();
            landed.Add(hit.Part,power,from+d*hit.Distance,d);
        }
        foreach(var (target,landed) in shots){
            var attack=new ScSurvivalBalance.BulletAttack(target,Entity,landed.Point,landed.Direction,landed.Total,
                ScBulletProjectile.For(Entity,ScBulletProjectile.RoundOf(spec),landed.Point,landed.Direction,landed.Total,spec.RechargeSeconds>0,Project.FindSubsystem<SubsystemTime>(true).GameTime)){AttackSoundVolume=0,Hits=landed};
            // The same narrow third-party contracts as the player's and companions' shots (post-mp-bugs-20260930 §3):
            // a stray squad bullet gives an audited boss no unrestricted damage path. Everyone else is untouched.
            ScProjectileDefense.Apply(target,attack);ScDamageIndicator.AttackBody(attack);
        }
    }
    static float PlayerBudget(GunSpec spec,float survival){
        if(TacticalHostileBalance.PlayerShot(spec.Name) is float table)return table;
        KnifeDiagnostics.WarnOnce("hostile-player-budget-"+spec.Name,$"hostile gun {spec.Name} is not in the player table; survival budget {survival} kept");
        return survival;
    }
    /// <summary>Where an enemy's grenade leaves: at shoulder height in front of and to the right of the body, outside its
    /// own collision box (the old start, 1.5 m above the feet, was inside it).</summary>
    public static Vector3 ThrowOrigin(ComponentBody body){var f=body.Matrix.Forward;f.Y=0;f=f.LengthSquared()>1e-6f?Vector3.Normalize(f):-Vector3.UnitZ;var r=Vector3.Cross(f,Vector3.UnitY);
        return body.Position+Vector3.UnitY*1.5f+f*(body.BoxSize.Z*.5f+.15f)+r*.2f;}
    /// <summary>The launch toward the current target, or null when it would hit a wall, land among the squad or leave
    /// from inside a wall. Checked when the throw starts and again at the release.</summary>
    Vector3? ThrowVelocity(out Vector3 start){
        var body=Creature.ComponentBody;start=ThrowOrigin(body);Vector3 target=TargetBody.Position+Vector3.UnitY*.4f;
        if(director.Enemies.Any(e=>e!=this&&Vector3.DistanceSquared(e.Creature.ComponentBody.Position,target)<64))return null;
        if(terrain.Raycast(body.Position+Vector3.UnitY*1.45f,start,false,true,(v,d)=>ScGunRange.TerrainStopsBullet(v)).HasValue)return null;
        const float flight=1.1f;Vector3 velocity=(target-start)/flight+Vector3.UnitY*(10*flight/2);Vector3 prior=start;
        for(int i=1;i<=8;i++){float t=flight*i/8;Vector3 next=start+velocity*t-Vector3.UnitY*(5*t*t);if(terrain.Raycast(prior,next,false,true,(v,d)=>ScGunRange.TerrainStopsBullet(v)).HasValue)return null;prior=next;}
        return velocity;
    }
    void TryGrenade(float distance){
        if(State.Grenades<=0||State.GrenadeLeft>0||distance<9||distance>20||!director.CanThrow(State.Squad)||ThrowVelocity(out _) is null)return;
        // The squad slot is taken now so two members do not start together; the grenade itself is spent at the release.
        double now=time.GameTime;throwing=new ThrowAction{Kind=State.Grenade,Started=now,ThrowAt=now+ThrowPull+ThrowHold,ReleaseAt=now+ThrowPull+ThrowHold+ThrowWind,EndAt=now+ThrowPull+ThrowHold+ThrowWind+ThrowFollow};
        director.Threw(State.Squad);path.Stop();
        Project.FindSubsystem<SubsystemAudio>(true).PlaySound("Audio/ScCsgoKnives/"+ScGrenadeBlock.Assets[State.Grenade]+"_pin",.8f,0,Creature.ComponentBody.Position,12,true);
        TacticalNet.Sound("Audio/ScCsgoKnives/"+ScGrenadeBlock.Assets[State.Grenade]+"_pin",.8f,Creature.ComponentBody.Position,12);
    }
    void AdvanceThrow(){
        var t=throwing;var body=Creature.ComponentBody;double now=time.GameTime;path.Stop();
        if(TargetBody is not null){var delta=(TargetBody.Position-body.Position).XZ;if(delta.LengthSquared()>.01f)Creature.ComponentLocomotion.TurnOrder=new Vector2(Math.Clamp(Vector2.Angle(body.Matrix.Forward.XZ,delta)*.8f,-.6f,.6f),0);}
        if(!t.Released&&now>=t.ReleaseAt){
            // The one commit: the target must still be there and the path clear; otherwise nothing is thrown or spent.
            float d=TargetBody is null?float.MaxValue:Vector3.Distance(body.Position,TargetBody.Position);
            if(TargetBody is null||TargetBody.Entity.FindComponent<ComponentHealth>() is not {Health:>0}||d<7||d>24||ThrowVelocity(out var start) is not {} velocity
                ||!Project.FindSubsystem<SubsystemScGrenades>(true).TryThrowHostile(t.Kind,start,velocity)){CancelThrow();return;}
            t.Released=true;State.Grenades--;State.GrenadeLeft=18;
            Project.FindSubsystem<SubsystemAudio>(true).PlaySound("Audio/ScCsgoKnives/"+ScGrenadeBlock.Assets[t.Kind]+"_throw",.9f,0,body.Position,16,true);
            TacticalNet.Sound("Audio/ScCsgoKnives/"+ScGrenadeBlock.Assets[t.Kind]+"_throw",.9f,body.Position,16);
            ScAgentVoice.Emit(Entity,"t",ScGrenadeBlock.Assets[t.Kind]);
        }
        if(now>=t.EndAt){throwing=null;Redraw();}
    }
    public void Died(){
        if(State is null||State.LootDone)return;State.LootDone=true;
        if(!ScNet.IsAuthority)return; // a multiplayer client replays the death; the server drops the loot
        path.Stop();
        var drops=Project.FindSubsystem<SubsystemPickables>(true);var pos=Creature.ComponentBody.BoundingBox.Center();var spec=GunSpec.All[State.Variant];
        void Drop(int v,int count)=>drops.AddPickable(v,count,pos,null,null,Entity);
        // Its protection ends with it; protection is never an item, so nothing more drops (current-direction-20260929).
        Project.FindSubsystem<SubsystemScArmor>(false)?.Remove(ArmorKey);
        if(random.Float(0,1)<.6f)Drop(ScAmmoBlock.Value(ScReloadTransaction.AmmoKind(spec)),random.Int(1,2));
        if(random.Float(0,1)<.4f)Drop(ScWeaponMaterialBlock.Value(random.Float(0,1)<.7f?0:random.Int(1,3)),random.Int(1,2));
        if(random.Float(0,1)<.5f)Drop(ScComponentCrafting.Resolve(random.Int(0,2) switch{0=>"ironingot",1=>"copperingot",_=>"coalchunk"}),random.Int(1,2));
        if(random.Float(0,1)<.08f)Drop(Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScChickenEggBlock>(true)),1);
    }
}
