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
    bool Safe(Point3 point,Vector3 p,bool visibility)=>SafeFor(point,p,visibility,28);
    bool SafeFor(Point3 point,Vector3 p,bool visibility,float minimumDistance){
        var t=terrain.Terrain;var chunk=t.GetChunkAtCell(point.X,point.Z);if(chunk is null||chunk.State<=TerrainChunkState.InvalidPropagatedLight||point.Y<2||point.Y>250)return false;
        if(point.Y<t.GetTopHeight(point.X,point.Z)-1)return false;
        for(int y=1;y<=2;y++){int v=t.GetCellValue(point.X,point.Y+y,point.Z);if(Terrain.ExtractContents(v)!=0)return false;}
        int ground=t.GetCellValue(point.X,point.Y,point.Z);if(!BlocksManager.Blocks[Terrain.ExtractContents(ground)].IsCollidable_(ground))return false;
        foreach(var player in players.ComponentPlayers){var camera=player.GameWidget.ActiveCamera;float d=Vector3.DistanceSquared(p,player.ComponentBody.Position);
            if(d<minimumDistance*minimumDistance)return false;
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
        return CreateSquad(roles,locations);
    }
    public string ManualFailure {get;private set;}="";
    public int SpawnManual(Point3 ground,int count){
        ManualFailure="";
        if(count is not (3 or 5)){ManualFailure="信标类型无效。";return 0;}
        // Manual beacons do not share natural-spawn population, visibility or distance gates.
        var locations=new List<Vector3>();
        for(int radius=0;radius<=8;radius++)for(int dx=-radius;dx<=radius;dx++)for(int dz=-radius;dz<=radius;dz++){
            if(Math.Max(Math.Abs(dx),Math.Abs(dz))!=radius)continue;
            if(!ManualPosition(ground.X+dx,ground.Y,ground.Z+dz,out var pos))continue;
            if(locations.Any(p=>Vector3.DistanceSquared(p,pos)<2.25f))continue;
            locations.Add(pos);
            if(locations.Count==count)return CreateSquad(Roles(count==5?FiveMemberDay:0,FiveMemberDay),locations);
        }
        ManualFailure="附近没有足够的可站立位置，请换一处地面（需要人物高度，不能在实体内生成）。";return 0;
    }
    bool ManualPosition(int x,int clickedY,int z,out Vector3 position){
        position=default;var t=terrain.Terrain;var chunk=t.GetChunkAtCell(x,z);
        if(chunk is null||chunk.State<TerrainChunkState.InvalidLight)return false;
        // Search near the clicked floor, not the column's top: roofs and foliage are not floors.
        for(int y=Math.Min(251,clickedY+3);y>=Math.Max(1,clickedY-4);y--){
            int value=t.GetCellValue(x,y,z);var block=BlocksManager.Blocks[Terrain.ExtractContents(value)];
            if(!block.IsCollidable_(value)||block.ShouldAvoid(value))continue;
            var boxes=block.GetCustomCollisionBoxes(terrain,value);if(boxes.Length==0)continue;
            float top=boxes.Where(b=>b.Min.X<=.5f&&b.Max.X>=.5f&&b.Min.Z<=.5f&&b.Max.Z>=.5f).Select(b=>b.Max.Y).DefaultIfEmpty(0).Max();
            if(top<=0)continue;
            var pos=new Vector3(x+.5f,y+top+.1f,z+.5f);var bounds=new BoundingBox(pos-new Vector3(.325f,0,.325f),pos+new Vector3(.325f,1.8f,.325f));
            bool blocked=false;
            for(int cy=y;cy<=Math.Min(255,Terrain.ToCell(bounds.Max.Y));cy++){
                int v=t.GetCellValue(x,cy,z);var b=BlocksManager.Blocks[Terrain.ExtractContents(v)];
                if(b is FluidBlock){blocked=true;break;}if(!b.IsCollidable_(v))continue;
                foreach(var box in b.GetCustomCollisionBoxes(terrain,v))if(bounds.Intersection(new BoundingBox(box.Min+new Vector3(x,cy,z),box.Max+new Vector3(x,cy,z)))){blocked=true;break;}
                if(blocked)break;
            }
            if(blocked)continue;
            var bodies=new DynamicArray<ComponentBody>();Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(pos.XZ,2,bodies);
            if(bodies.Any(b=>bounds.Intersection(b.BoundingBox)))continue;
            position=pos;return true;
        }
        return false;
    }
    int CreateSquad(TacticalRole[] roles,List<Vector3> locations){
        string squad=Guid.NewGuid().ToString("N");var made=new List<Entity>();
        try{
            for(int i=0;i<roles.Length;i++){var e=DatabaseManager.CreateEntity(Project,Template,true);made.Add(e);e.FindComponent<ComponentTacticalEnemy>(true).Configure(TacticalEnemyState.Create(roles[i],squad,random),locations[i]);}
            foreach(var e in made)Project.AddEntity(e);spawnCooldown=180;return made.Count;
        }catch(Exception error){foreach(var e in made){if(e.IsAddedToProject)Project.RemoveEntity(e,true);else e.Dispose();}ManualFailure="小队实体创建失败，请查看游戏日志。";Log.Warning("[CS Tactical] 小队生成已撤销："+error);return 0;}
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
