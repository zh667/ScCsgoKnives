using Engine;
using TemplatesDatabase;
using GameEntitySystem;
using System.Text;
namespace Game;

public sealed class SubsystemTacticalEnemies : Subsystem,IUpdateable {
    public const string Template="ScTacticalEnemy";
    public readonly HashSet<ComponentTacticalEnemy> Enemies=[];
    readonly Dictionary<string,double> grenades=new();
    readonly Engine.Random random=new();
    SubsystemCreatureSpawn spawn;SubsystemTerrain terrain;SubsystemPlayers players;SubsystemTime time;SubsystemGameInfo info;
    float spawnCooldown;
    public int FiveMemberDay=30,MaxActive=10;
    public UpdateOrder UpdateOrder=>UpdateOrder.Default;
    public override void Load(ValuesDictionary values){base.Load(values);if(values.GetValue("Schema",1)!=1)throw new InvalidOperationException("敌方小队存档版本不受支持。");
        FiveMemberDay=values.GetValue("FiveMemberDay",30);MaxActive=values.GetValue("MaxActive",10);spawnCooldown=values.GetValue("SpawnCooldown",0f);
        if(FiveMemberDay<1||FiveMemberDay>10000||MaxActive<5||MaxActive>20||!float.IsFinite(spawnCooldown)||spawnCooldown<0||spawnCooldown>600)throw new InvalidOperationException("敌方小队设置异常。");
        spawn=Project.FindSubsystem<SubsystemCreatureSpawn>(true);terrain=Project.FindSubsystem<SubsystemTerrain>(true);players=Project.FindSubsystem<SubsystemPlayers>(true);time=Project.FindSubsystem<SubsystemTime>(true);info=Project.FindSubsystem<SubsystemGameInfo>(true);
    }
    public override void Save(ValuesDictionary values){base.Save(values);values.SetValue("Schema",1);values.SetValue("FiveMemberDay",FiveMemberDay);values.SetValue("MaxActive",MaxActive);values.SetValue("SpawnCooldown",spawnCooldown);}
    public void Register(){if(spawn.m_creatureTypes.Any(c=>c.Name==Template))return;
        spawn.m_creatureTypes.Add(new(Template,SpawnLocationType.Surface,true,false){SpawnSuitabilityFunction=(_,p)=>Suitable(p)? .12f:0,SpawnFunction=(_,p)=>SpawnSquad(p)});
    }
    public override void OnEntityAdded(Entity e){if(e.FindComponent<ComponentTacticalEnemy>() is {} enemy)Enemies.Add(enemy);}
    public override void OnEntityRemoved(Entity e){if(e.FindComponent<ComponentTacticalEnemy>() is {} enemy){
        Enemies.Remove(enemy);
        // Native despawn snapshots precede a two-second fade. Keep the pending snapshot current;
        // a death during that fade must not restore an alive, newly lootable copy later.
        var native=Project.FindSubsystem<SubsystemSpawn>(true);
        foreach(var chunk in native.m_chunks.Values)foreach(var data in chunk.SpawnsData.Where(d=>d.EntityId==e.Id&&d.TemplateName==Template).ToArray()){
            if(enemy.Creature.ComponentHealth.Health<=0||enemy.State.LootDone){chunk.SpawnsData.Remove(data);native.m_spawnEntityDatas.Remove(e.Id);}
            else SaveSpawn(enemy.Creature.ComponentSpawn,data);
        }
    }}
    public void Update(float dt){spawnCooldown=Math.Max(0,spawnCooldown-Math.Max(0,dt));foreach(var key in grenades.Keys.Where(k=>!Enemies.Any(e=>e.State?.Squad==k)).ToArray())grenades.Remove(key);}
    public bool CanThrow(string squad)=>!grenades.TryGetValue(squad,out double at)||time.GameTime>=at;
    public void Threw(string squad)=>grenades[squad]=time.GameTime+8;
    public static TacticalRole[] Roles(int day,int fiveDay)=>day>=fiveDay?[TacticalRole.Sniper,TacticalRole.Rifle,TacticalRole.Close,TacticalRole.Machine,TacticalRole.Demolition]:[TacticalRole.Sniper,TacticalRole.Rifle,TacticalRole.Close];
    int Day=>1+(int)(info.TotalElapsedGameTime/Math.Max(1,Project.FindSubsystem<SubsystemTimeOfDay>(true).DayDuration));
    bool ModeAllowed=>info.WorldSettings.EnvironmentBehaviorMode==EnvironmentBehaviorMode.Living&&info.WorldSettings.GameMode>=GameMode.Survival;
    bool Suitable(Point3 point){
        if(!ModeAllowed||spawnCooldown>0||Enemies.Count+Roles(Day,FiveMemberDay).Length>MaxActive)return false;
        // Native suitability receives the first free cell ABOVE the support block.
        point.Y--;Vector3 p=new(point.X+.5f,point.Y+1.1f,point.Z+.5f);
        if(Enemies.Any(e=>e.Creature.ComponentHealth.Health>0&&Vector3.DistanceSquared(e.Creature.ComponentBody.Position,p)<96*96))return false;
        var sleeping=Project.FindSubsystem<SubsystemSpawn>(true).m_chunks.Values.SelectMany(c=>c.SpawnsData).Where(e=>e.TemplateName==Template);
        if(sleeping.Any(e=>Vector3.DistanceSquared(e.Position,p)<96*96))return false;
        return Safe(point,p,true);
    }
    bool Safe(Point3 point,Vector3 p,bool visibility){
        var t=terrain.Terrain;var chunk=t.GetChunkAtCell(point.X,point.Z);if(chunk is null||chunk.State<=TerrainChunkState.InvalidPropagatedLight||point.Y<2||point.Y>250)return false;
        if(point.Y<t.GetTopHeight(point.X,point.Z)-1)return false;
        for(int y=1;y<=2;y++){int v=t.GetCellValue(point.X,point.Y+y,point.Z);if(Terrain.ExtractContents(v)!=0)return false;}
        int ground=t.GetCellValue(point.X,point.Y,point.Z);if(!BlocksManager.Blocks[Terrain.ExtractContents(ground)].IsCollidable_(ground))return false;
        foreach(var player in players.ComponentPlayers){var camera=player.GameWidget.ActiveCamera;float d=Vector3.DistanceSquared(p,player.ComponentBody.Position);
            if(d<28*28)return false;
            if(visibility&&camera.ViewFrustum.Intersection(new BoundingSphere(p+Vector3.UnitY,1))&&!terrain.Raycast(camera.ViewPosition,p+Vector3.UnitY,false,true,(v,r)=>ScGunRange.TerrainStopsBullet(v)).HasValue)return false;
        }
        var bodies=new DynamicArray<ComponentBody>();Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(p.XZ,1,bodies);return bodies.All(b=>Vector3.DistanceSquared(b.Position,p)>1);
    }
    public int SpawnSquad(Point3 point){
        if(!Suitable(point))return 0;
        point.Y--;var roles=Roles(Day,FiveMemberDay);
        // Native group spawns may cross the local soft threshold; keep the global budget hard.
        if(spawn.CountCreatures(false)+roles.Length>SubsystemCreatureSpawn.m_totalLimit)return 0;
        var locations=new List<Vector3>();
        foreach(var offset in new[]{new Point2(0,0),new Point2(3,0),new Point2(-3,0),new Point2(0,3),new Point2(0,-3),new Point2(3,3),new Point2(-3,-3)}){
            int x=point.X+offset.X,z=point.Z+offset.Y;if(terrain.Terrain.GetChunkAtCell(x,z) is null)continue;
            var ground=new Point3(x,terrain.Terrain.GetTopHeight(x,z),z);var p=new Vector3(x+.5f,ground.Y+1.1f,z+.5f);
            if(Math.Abs(ground.Y-point.Y)>3||!Safe(ground,p,true))continue;locations.Add(p);if(locations.Count==roles.Length)break;
        }
        if(locations.Count!=roles.Length)return 0;
        string squad=Guid.NewGuid().ToString("N");var made=new List<Entity>();
        try{
            for(int i=0;i<roles.Length;i++){var e=DatabaseManager.CreateEntity(Project,Template,true);made.Add(e);e.FindComponent<ComponentTacticalEnemy>(true).Configure(TacticalEnemyState.Create(roles[i],squad,random),locations[i]);}
            foreach(var e in made)Project.AddEntity(e);spawnCooldown=180;return made.Count;
        }catch(Exception error){foreach(var e in made){if(e.IsAddedToProject)Project.RemoveEntity(e,true);else e.Dispose();}Log.Warning("[CS Tactical] 小队生成已撤销："+error.Message);return 0;}
    }
    const string Marker="|SCT_ENEMY1:";
    public static void SaveSpawn(ComponentSpawn spawn,SpawnEntityData data){
        if(spawn.Entity.FindComponent<ComponentTacticalEnemy>() is not {} enemy)return;
        string original=data.Data??"";int index=original.IndexOf(Marker,StringComparison.Ordinal);
        if(index>=0){int end=original.IndexOf('|',index+Marker.Length);original=original[..index]+(end>=0?original[end..]:"");}
        data.Data=original+Marker+Convert.ToBase64String(Encoding.UTF8.GetBytes(enemy.Capture()));
    }
    public static void ReadSpawn(Entity entity,SpawnEntityData data){
        if(entity.FindComponent<ComponentTacticalEnemy>() is not {} enemy)return;
        string text=data.Data??"";int start=text.IndexOf(Marker,StringComparison.Ordinal);if(start<0)throw new InvalidOperationException("敌方小队暂存装备缺失，拒绝重新随机。");
        string encoded=text[(start+Marker.Length)..].Split('|')[0];enemy.Restore(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));enemy.Home=data.Position;
    }
}
