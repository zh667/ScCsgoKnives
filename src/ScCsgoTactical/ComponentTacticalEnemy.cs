using Engine;
using TemplatesDatabase;
using GameEntitySystem;
namespace Game;

public sealed class ComponentTacticalEnemy : ComponentBehavior,IUpdateable,INoiseListener {
    public TacticalEnemyState State;
    public ComponentCreature Creature;
    public Vector3 Home;
    public ComponentBody TargetBody;
    ComponentPathfinding path;SubsystemTacticalEnemies director;SubsystemTerrain terrain;SubsystemBodies bodies;SubsystemTime time;
    readonly Engine.Random random=new();
    float senseLeft,pathLeft,lost,aim,plant,burstPause,retreat,search;int burst;
    bool seen;
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
    public UpdateOrder UpdateOrder=>UpdateOrder.Default;
    public override float ImportanceLevel=>State is not null&&Creature.ComponentHealth.Health>0?100:0;
    public override void Load(ValuesDictionary values,IdToEntityMap map){
        Creature=Entity.FindComponent<ComponentCreature>(true);path=Entity.FindComponent<ComponentPathfinding>(true);director=Project.FindSubsystem<SubsystemTacticalEnemies>(true);
        terrain=Project.FindSubsystem<SubsystemTerrain>(true);bodies=Project.FindSubsystem<SubsystemBodies>(true);time=Project.FindSubsystem<SubsystemTime>(true);
        string saved=values.GetValue("EnemyState","");if(saved.Length>0)Restore(saved);Home=values.GetValue("PatrolHome",Creature.ComponentBody.Position);
    }
    public string Capture(){State.Health=Creature.ComponentHealth.Health;return State.Encode();}
    public void Restore(string json){State=TacticalEnemyState.Decode(json);Creature.ComponentHealth.Health=State.Health;}
    public override void Save(ValuesDictionary values,EntityToIdMap map){if(State is null)throw new InvalidOperationException("敌方装备尚未初始化。");values.SetValue("EnemyState",Capture());values.SetValue("PatrolHome",Home);}
    public void Configure(TacticalEnemyState state,Vector3 position){State=state;Home=position;Creature.ComponentBody.Position=position;Creature.ConstantSpawn=false;}
    bool Friendly(ComponentBody body)=>body is null||body.Entity==Entity||body.Entity.FindComponent<ComponentTacticalEnemy>() is not null;
    public void Alert(ComponentBody attacker){if(Friendly(attacker)||attacker.Entity.FindComponent<ComponentHealth>() is not {Health:>0})return;TargetBody=attacker;lastSeen=attacker.Position;lost=6;aim=plant=0;Creature.ComponentBody.TargetCrouchFactor=0;}
    public void HearNoise(ComponentBody source,Vector3 position,float loudness){
        if(State is null||TargetBody is not null||source is null||Friendly(source)||loudness<.5f||Vector3.DistanceSquared(Creature.ComponentBody.Position,position)>40*40)return;
        // Sound starts a bounded search, never grants a through-wall shooting target.
        lastSeen=position+new Vector3(random.Float(-3,3),0,random.Float(-3,3));search=5;
    }
    bool Visible(ComponentBody target){
        var from=Creature.ComponentBody.Position+Vector3.UnitY*1.45f;var to=target.BoundingBox.Center();
        if(Project.FindSubsystem<SubsystemScGrenades>(true).SmokeBlocksSight(from,to))return false;
        if(terrain.Raycast(from,to,false,true,(v,d)=>ScGunRange.TerrainStopsBullet(v)).HasValue)return false;
        var body=bodies.Raycast(from,to,0,(b,d)=>b.Entity!=Entity);return body.HasValue&&body.Value.ComponentBody==target;
    }
    public void Update(float dt){
        if(State is null)return;
        string asset=GunSpec.All[State.Variant].Name;
        if(visualAsset!=asset){visualAsset=asset;actions.Start(asset,ScWeaponActionKind.Draw,"deploy",time.GameTime,.65f);}
        if(Creature.ComponentHealth.Health<=0){Died();return;}
        if(Creature.ComponentSpawn.IsDespawning){path.Stop();plant=0;Creature.ComponentBody.TargetCrouchFactor=0;return;}
        if(!IsActive)return;dt=Math.Clamp(dt,0,.5f);var body=Creature.ComponentBody;var spec=GunSpec.All[State.Variant];
        State.ShotLeft=Math.Max(0,State.ShotLeft-dt);State.GrenadeLeft=Math.Max(0,State.GrenadeLeft-dt);burstPause=Math.Max(0,burstPause-dt);search=Math.Max(0,search-dt);
        if(State.ReloadLeft>0){State.ReloadLeft=Math.Max(0,State.ReloadLeft-dt);if(State.ReloadLeft==0){int n=Math.Min(spec.Magazine-State.Rounds,State.Reserve);State.Rounds+=n;State.Reserve-=n;}return;}
        if(Project.FindSubsystem<SubsystemScGrenades>(true).IsBodyBlinded(body)){path.Stop();plant=aim=0;body.TargetCrouchFactor=0;seen=false;return;}
        if(retreat>0){retreat=Math.Max(0,retreat-dt);return;}
        senseLeft-=dt;pathLeft-=dt;
        float range=State.Role==TacticalRole.Sniper?64:40;
        if(TargetBody is not null&&(!TargetBody.IsAddedToProject||TargetBody.Entity.FindComponent<ComponentHealth>() is not {Health:>0}||Vector3.DistanceSquared(body.Position,TargetBody.Position)>range*range))TargetBody=null;
        if(senseLeft<=0){senseLeft=.35f+random.Float(0,.15f);
            if(TargetBody is null){
                var candidates=new DynamicArray<ComponentBody>();bodies.FindBodiesAroundPoint(body.Position.XZ,range,candidates);
                foreach(var b in candidates.Where(b=>b.Entity.FindComponent<ComponentPlayer>()!=null||b.Entity.FindComponent<ComponentTacticalCompanion>()!=null).OrderBy(b=>Vector3.DistanceSquared(b.Position,body.Position))){
                    if(b.Entity.FindComponent<ComponentHealth>() is not {Health:>0}||!Visible(b))continue;TargetBody=b;aim=0;break;
                }
            }
            seen=TargetBody!=null&&Visible(TargetBody);
            if(seen){lastSeen=TargetBody.Position;lost=6;}else{lost-=.5f;if(lost<=0){TargetBody=null;aim=0;}}
        }
        bool clear=TargetBody!=null&&seen;float distance=TargetBody is null?float.MaxValue:Vector3.Distance(body.Position,TargetBody.Position);
        if(!clear||State.Role==TacticalRole.Sniper&&body.Velocity.XZ.LengthSquared()>.09f)aim=0;else aim+=dt;
        if(pathLeft<=0){pathLeft=.65f;
            if(TargetBody is null){if(search>0)path.SetDestination(lastSeen,.45f,3,160,true,false,true,null);else if(Vector3.DistanceSquared(body.Position,Home)>16)path.SetDestination(Home,.45f,2,160,true,false,true,null);else path.Stop();}
            else if(!clear||distance>(State.Role==TacticalRole.Close?9:State.Role==TacticalRole.Sniper?40:22))path.SetDestination(lastSeen,.65f,3,200,true,false,true,TargetBody);
            else path.Stop();
        }
        if(TargetBody is not null){var delta=lastSeen-body.Position;delta.Y=0;if(delta.LengthSquared()>.01f)body.Rotation=Quaternion.CreateFromYawPitchRoll(MathF.Atan2(-delta.X,-delta.Z),0,0);}
        if(State.Bomb&&clear&&distance>=9&&distance<=22&&body.StandingOnValue.HasValue&&body.Velocity.LengthSquared()<.2f){
            path.Stop();plant+=dt;body.TargetCrouchFactor=1;
            if(plant>=3.2f){if(Visible(TargetBody)&&Project.FindSubsystem<SubsystemTacticalBombs>(true).TryPlant(body.Position,MathF.Atan2(body.Matrix.Forward.X,body.Matrix.Forward.Z))){State.Bomb=false;path.SetDestination(body.Position-body.Matrix.Forward*15,.8f,1,200,true,false,true,null);State.ShotLeft=3;retreat=6;}
                plant=0;body.TargetCrouchFactor=0;}return;
        }
        if(plant>0){plant=0;body.TargetCrouchFactor=0;}
        if(!clear)return;
        TryGrenade(distance);
        if(State.Rounds==0){if(State.Reserve>0){State.ReloadLeft=State.Role==TacticalRole.Machine?4:2.8f;Play("reload");}else if(pathLeft<=.1f)path.SetDestination(body.Position-body.Matrix.Forward*10,.8f,1,160,true,false,true,null);return;}
        if(State.ShotLeft>0||burstPause>0||aim<(State.Role==TacticalRole.Sniper?1.4f:.55f))return;
        if(State.Role==TacticalRole.Close&&distance>18)return;
        if(!Visible(TargetBody)){seen=false;aim=0;return;}
        Shoot();State.ShotLeft=Math.Max(ScGunGrowth.ShotInterval(State.Variant,spec.CycleSeconds,0),State.Role==TacticalRole.Sniper?.65f:.12f);
        if(++burst>=(State.Role==TacticalRole.Machine?8:State.Role==TacticalRole.Sniper?1:3)){burst=0;burstPause=State.Role==TacticalRole.Sniper?.6f:.75f;}
    }
    void Play(string kind){
        actions.Start(GunSpec.All[State.Variant].Name,kind=="shot"?ScWeaponActionKind.Shoot:ScWeaponActionKind.Reload,kind=="shot"?"shoot":"reload",time.GameTime,kind=="shot"?.16f:State.ReloadLeft);
        if(kind=="shot")Project.FindSubsystem<SubsystemAudio>(true).PlaySound(SubsystemScGunBlockBehavior.ExtensionShotSound(GunSpec.All[State.Variant],false),.8f,0,Creature.ComponentBody.Position,20,true);
    }
    void Shoot(){
        var spec=GunSpec.All[State.Variant];var body=Creature.ComponentBody;Vector3 from=body.Position+Vector3.UnitY*1.45f;
        float error=State.Role==TacticalRole.Sniper?.15f:.45f;error+=body.Velocity.Length()*.12f;
        var point=TargetBody.BoundingBox.Center()+new Vector3(random.Float(-error,error),random.Float(-error,error),random.Float(-error,error));
        var direction=Vector3.Normalize(point-from);float range=State.Role==TacticalRole.Sniper?64:State.Role==TacticalRole.Close?18:40;
        State.Rounds--;Play("shot");var hit=bodies.Raycast(from,from+direction*range,0,(b,d)=>b.Entity!=Entity);var wall=terrain.Raycast(from,from+direction*range,false,true,(v,d)=>ScGunRange.TerrainStopsBullet(v));
        if(!hit.HasValue||Friendly(hit.Value.ComponentBody)||wall.HasValue&&wall.Value.Distance<hit.Value.Distance)return;
        // Use the balanced Lv0 whole-shot budget. Enemy templates have neither skin nor counter growth.
        var stats=EffectiveGunStats.ResolveLevel(spec,State.DisplayValue,false,0);
        float power=stats.Power*stats.Falloff(spec,hit.Value.Distance);
        ComponentMiner.AttackBody(new ScSurvivalBalance.BulletAttack(hit.Value.ComponentBody,Entity,from+direction*hit.Value.Distance,direction,power){AttackSoundVolume=0});
    }
    void TryGrenade(float distance){
        if(State.Grenades<=0||State.GrenadeLeft>0||distance<9||distance>20||!director.CanThrow(State.Squad))return;
        var body=Creature.ComponentBody;Vector3 start=body.Position+Vector3.UnitY*1.5f,target=TargetBody.Position+Vector3.UnitY*.4f;
        if(director.Enemies.Any(e=>e!=this&&Vector3.DistanceSquared(e.Creature.ComponentBody.Position,target)<64))return;
        const float flight=1.1f;Vector3 velocity=(target-start)/flight+Vector3.UnitY*(10*flight/2);Vector3 prior=start;
        for(int i=1;i<=8;i++){float t=flight*i/8;Vector3 next=start+velocity*t-Vector3.UnitY*(5*t*t);if(terrain.Raycast(prior,next,false,true,(v,d)=>ScGunRange.TerrainStopsBullet(v)).HasValue)return;prior=next;}
        if(Project.FindSubsystem<SubsystemScGrenades>(true).TryThrowHostile(State.Grenade,start,velocity)){State.Grenades--;State.GrenadeLeft=18;director.Threw(State.Squad);}
    }
    public void Died(){
        if(State is null||State.LootDone)return;State.LootDone=true;path.Stop();
        var drops=Project.FindSubsystem<SubsystemPickables>(true);var pos=Creature.ComponentBody.BoundingBox.Center();var spec=GunSpec.All[State.Variant];
        void Drop(int v,int count)=>drops.AddPickable(v,count,pos,null,null,Entity);
        if(random.Float(0,1)<.6f)Drop(ScAmmoBlock.Value(ScReloadTransaction.AmmoKind(spec)),random.Int(1,2));
        if(random.Float(0,1)<.4f)Drop(ScWeaponMaterialBlock.Value(random.Float(0,1)<.7f?0:random.Int(1,3)),random.Int(1,2));
        if(random.Float(0,1)>=.03f)return;
        var inv=Entity.FindComponent<ComponentTacticalInventory>(true);inv.AddSlotItems(0,State.DisplayValue,1);
        var mutation=ScGunMutation.Prepare(inv,0,ScGunHolders.Key(inv,0),out _);
        var result=mutation?.Commit(r=>{r.Rounds=Math.Min(State.Rounds,spec.Magazine/4);r.Durability=Math.Max(1,(int)(r.MaxDurability*.4f));});
        if(result==ScGunResult.Success){inv.DropAllItems(pos);return;}
        inv.RemoveSlotItems(0,1);Drop(ScWeaponMaterialBlock.Value(0),2);
    }
}
