using System.Globalization;
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
        ScTacticalPerformance.Start(Project);
        var rules=Rules;KnifeLog.Diagnostic($"[CS_SPAWN] naturalEnabled={rules.Natural} graceDays={rules.GraceDays} density={rules.Density} elapsedSeconds={info.TotalElapsedGameTime:F1}");
    }
    public override void Save(ValuesDictionary values){base.Save(values);values.SetValue("Schema",1);values.SetValue("FiveMemberDay",FiveMemberDay);values.SetValue("MaxActive",MaxActive);values.SetValue("SpawnCooldown",spawnCooldown);}
    public void Register(){if(spawn.m_creatureTypes.Any(c=>c.Name==Template))return;
        spawn.m_creatureTypes.Add(new(Template,SpawnLocationType.Surface,true,false){SpawnSuitabilityFunction=(_,p)=>Suitable(p)? .35f:0,SpawnFunction=(_,p)=>SpawnSquad(p)});
    }
    public override void OnEntityAdded(Entity e){if(e.FindComponent<ComponentTacticalEnemy>() is {} enemy)Enemies.Add(enemy);}
    public override void OnEntityRemoved(Entity e){if(e.FindComponent<ComponentTacticalEnemy>() is {} enemy){
        Enemies.Remove(enemy);
        if(!ScNet.IsAuthority)return; // a multiplayer client's copy: the server keeps the sleep records
        // Native despawn snapshots precede a two-second fade. Keep the pending snapshot current;
        // a death during that fade must not restore an alive, newly lootable copy later.
        var native=Project.FindSubsystem<SubsystemSpawn>(true);
        if(enemy.State is {} state&&enemy.Creature.ComponentHealth.Health>0&&!state.LootDone)Note(state.Squad,"slept");
        foreach(var chunk in native.m_chunks.Values)foreach(var data in chunk.SpawnsData.Where(d=>d.EntityId==e.Id&&d.TemplateName==Template).ToArray()){
            if(enemy.Creature.ComponentHealth.Health<=0||enemy.State.LootDone){chunk.SpawnsData.Remove(data);native.m_spawnEntityDatas.Remove(e.Id);}
            else SaveSpawn(enemy.Creature.ComponentSpawn,data);
        }
    }}
    public override void Dispose(){ScTacticalPerformance.Finish(Project);base.Dispose();}
    public bool MayHuntPlayers=>info.WorldSettings.GameMode!=GameMode.Creative;
    /// <summary>Only living players in this world's bounded active neighbourhood; never targets another world's body.</summary>
    public ComponentBody NearestPlayer(Vector3 position,float range)=>players.ComponentPlayers
        .Where(p=>p.Entity?.Project==Project&&p.ComponentHealth?.Health>0&&p.ComponentBody?.IsAddedToProject==true)
        .Select(p=>p.ComponentBody).Where(b=>Vector3.DistanceSquared(position,b.Position)<=range*range)
        .OrderBy(b=>Vector3.DistanceSquared(position,b.Position)).FirstOrDefault();
    /// <summary>This world's rules; without the rules subsystem (older fixtures) the device default switch, 30 days
    /// and the saved MaxActive cap apply exactly as before.</summary>
    public ScEnemyRules Rules=>Project.FindSubsystem<SubsystemTacticalEnemyRules>(false)?.Rules??new ScEnemyRules(ScUiSettings.NaturalEnemies,ScEnemySpawnPolicy.GraceDays,ScEnemyDensity.Standard);
    (int Cap,float Cooldown,float Spacing) Limits=>Project.FindSubsystem<SubsystemTacticalEnemyRules>(false) is {} r?ScEnemySpawnPolicy.Limits(r.Rules.Density):(MaxActive,60,96);
    public static bool CountsAsNatural(ComponentTacticalEnemy e)=>e.State?.Source!="manual";
    // Natural-spawn observability (video-feedback-20260929 S0). "Allowed by the rules" is not "able to spawn": the
    // native scheduler offers candidates only while the game's global creature budget has room, picks among all
    // creature types by weight, and a squad is committed whole or not at all. Bounded: fixed reason keys, at most
    // four sample points per window, one summary per two minutes and only after events or a changed blocking state.
    readonly Dictionary<string,int> rejections=new(),recent=new();double nextRejectionLog;
    readonly List<string> samples=[];int offered,squads,loggedEncounters;string loggedState="";
    bool Reject(string reason,Point3? at=null){
        rejections[reason]=rejections.GetValueOrDefault(reason)+1;
        if(at is {} p&&samples.Count<4)samples.Add($"{reason}@{p.X},{p.Y},{p.Z}");
        return false;
    }
    /// <summary>The population budget natural squads are subject to. The limit is the game's global static
    /// (vanilla 26); settings or another mod may have changed it. CS reads it and never edits it.</summary>
    public readonly record struct NaturalBudget(int Limit,int Creatures,int Squad){
        public const int VanillaLimit=26;
        /// <summary>No whole squad can ever fit under this limit, whatever else happens.</summary>
        public bool Impossible=>Squad>Limit;
        public bool Fits=>Creatures+Squad<=Limit;
    }
    public enum NaturalBlocker { None, Off, Mode, Waiting, BudgetImpossible, BudgetFull, SquadCap, Cooldown }
    public NaturalBudget Budget=>new(SubsystemCreatureSpawn.m_totalLimit,spawn.CountCreatures(false),Roles(Day,FiveMemberDay).Length);
    /// <summary>First condition that currently prevents any natural squad, in the order the gate checks them.
    /// None still depends on terrain, sight lines and the native random scheduler.</summary>
    public NaturalBlocker BlockerFor(ScEnemyRules rules){
        if(!rules.Natural)return NaturalBlocker.Off;
        if(!ModeAllowed)return NaturalBlocker.Mode;
        if(!ScEnemySpawnPolicy.AllowsAfter(true,info.TotalElapsedGameTime,Project.FindSubsystem<SubsystemTimeOfDay>(true).DayDuration,rules.GraceDays))return NaturalBlocker.Waiting;
        var budget=Budget;
        if(budget.Impossible)return NaturalBlocker.BudgetImpossible;
        if(Enemies.Count(CountsAsNatural)+budget.Squad>ScEnemySpawnPolicy.Limits(rules.Density).Cap)return NaturalBlocker.SquadCap;
        if(!budget.Fits)return NaturalBlocker.BudgetFull;
        return spawnCooldown>0?NaturalBlocker.Cooldown:NaturalBlocker.None;
    }
    public int NaturalCount=>Enemies.Count(CountsAsNatural);
    public float CooldownLeft=>spawnCooldown;
    /// <summary>Refusals since the world was loaded, by reason (fixed key set), for the settings page.</summary>
    public IReadOnlyDictionary<string,int> RecentRefusals=>recent;
    void LogNatural(){
        var rules=Rules;var blocker=BlockerFor(rules);var budget=Budget;var limits=Limits;
        string state=$"blocker={blocker} rules={rules.Natural}/{rules.GraceDays}d/{rules.Density} budget={budget.Creatures}+{budget.Squad}/{budget.Limit}";
        bool events=offered>0||squads>0||rejections.Count>0||encounters.Count!=loggedEncounters;loggedEncounters=encounters.Count;
        if(events||state!=loggedState){
            loggedState=state;
            KnifeLog.Diagnostic($"[CS_SPAWN] natural status (2 min): {state} (vanilla limit {NaturalBudget.VanillaLimit}; CS never changes it) csNatural={NaturalCount}/{limits.Cap} cooldownLeft={spawnCooldown:0}s "
                +$"offeredByNative={offered} squadsCreated={squads} refusals: {(rejections.Count==0?"none":string.Join(", ",rejections.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>p.Key+"="+p.Value)))}"
                +(samples.Count>0?" samples: "+string.Join(" ",samples):"")+" encounters: "+EncounterSummary()
                +(offered==0&&blocker is NaturalBlocker.None or NaturalBlocker.BudgetFull or NaturalBlocker.BudgetImpossible?" (the native scheduler offered CS no candidate in this window: population budget, its random gate or no spawn point)":""));
        }
        foreach(var pair in rejections)recent[pair.Key]=recent.GetValueOrDefault(pair.Key)+pair.Value;
        rejections.Clear();samples.Clear();offered=squads=0;
    }
    /// <summary>Server: <paramref name="victim"/> was hit by an effective attack of <paramref name="attacker"/>. Every other
    /// living enemy within ComponentTacticalEnemy.ProvokeRange of the victim takes that attacker, whatever its squad or
    /// source. Measured from where the victim stands (also when the hit killed it), told once: an enemy that is told
    /// tells nobody, so a fight does not spread from group to group across the map. No attacker (a fall, fire, drowning)
    /// or an attacker of their own kind (a squad mate's bomb) provokes nobody. Returns how many were told.</summary>
    public int Provoked(ComponentTacticalEnemy victim,ComponentBody attacker){
        if(!ScNet.IsAuthority||victim?.Creature?.ComponentBody is not {} at||attacker is null||attacker.Entity.FindComponent<ComponentTacticalEnemy>() is not null
            ||attacker.Entity.FindComponent<ComponentHealth>() is not {Health:>0})return 0;
        int told=0;float range=ComponentTacticalEnemy.ProvokeRange;
        foreach(var e in Enemies){
            if(ReferenceEquals(e,victim)||e.State is null||e.Creature.ComponentHealth.Health<=0||Vector3.DistanceSquared(e.Creature.ComponentBody.Position,at.Position)>range*range)continue;
            e.Provoke(attacker);told++;
        }
        return told;
    }
    public void Update(float dt){
        ScTexturePreparation.Pump();
        ScTacticalPerformance.Frame(Project,Enemies.Count,Project.FindSubsystem<SubsystemScTactical>()?.CompanionCount??0);
        using var timing=ScTacticalPerformance.Measure(Project,ScTacticalPerformance.Stage.Director);
        if(!ScNet.IsAuthority){foreach(var e in Enemies)e.ClientTick(dt);return;} // a multiplayer client shows the server's squads
        spawnCooldown=Math.Max(0,spawnCooldown-Math.Max(0,dt));
        if(time.GameTime>=nextRejectionLog){nextRejectionLog=time.GameTime+120;LogNatural();}
        foreach(var key in grenades.Keys.Where(k=>!Enemies.Any(e=>e.State?.Squad==k)).ToArray())grenades.Remove(key);
        if(time.GameTime>=nextArmorPrune){nextArmorPrune=time.GameTime+600;PruneArmor();}
    }
    double nextArmorPrune=5;
    /// <summary>Removes the protection entries of enemies that no longer exist anywhere: neither alive in the world nor
    /// sleeping in a spawn record (an enemy removed without dying, or a sleeping record the engine discarded after its
    /// chunk went unvisited for a long time). Conservative: nothing is removed while any sleeping record is unreadable.
    /// Returns the number removed.</summary>
    public int PruneArmor(){
        var armor=Project.FindSubsystem<SubsystemScArmor>(false);if(armor is null)return 0;
        var known=new HashSet<string>(StringComparer.Ordinal);
        foreach(var e in Enemies)if(e.ArmorKey is {} k)known.Add(k);
        foreach(var data in Project.FindSubsystem<SubsystemSpawn>(true).m_chunks.Values.SelectMany(c=>c.SpawnsData).Where(d=>d.TemplateName==Template)){
            string text=data.Data??"";int start=text.IndexOf(Marker,StringComparison.Ordinal);
            try{var state=TacticalEnemyState.Decode(Encoding.UTF8.GetString(Convert.FromBase64String(text[(start+Marker.Length)..].Split('|')[0])));known.Add(SubsystemScArmor.EnemyKey(state.Squad,(int)state.Role));}
            catch(Exception){return 0;}
        }
        var stale=armor.Keys.Where(k=>k.StartsWith("enemy-",StringComparison.Ordinal)&&!known.Contains(k)).ToArray();
        foreach(var k in stale)armor.Remove(k);
        if(stale.Length>0)KnifeLog.Diagnostic($"[CS_ARMOR] removed the protection of {stale.Length} enemies that no longer exist");
        return stale.Length;
    }
    public bool CanThrow(string squad)=>!grenades.TryGetValue(squad,out double at)||time.GameTime>=at;
    public void Threw(string squad)=>grenades[squad]=time.GameTime+8;
    public static TacticalRole[] Roles(int day,int fiveDay)=>day>=fiveDay?[TacticalRole.Sniper,TacticalRole.Rifle,TacticalRole.Close,TacticalRole.Machine,TacticalRole.Demolition]:[TacticalRole.Sniper,TacticalRole.Rifle,TacticalRole.Close];
    int Day=>1+(int)(info.TotalElapsedGameTime/Math.Max(1,Project.FindSubsystem<SubsystemTimeOfDay>(true).DayDuration));
    bool ModeAllowed=>info.WorldSettings.EnvironmentBehaviorMode==EnvironmentBehaviorMode.Living&&info.WorldSettings.GameMode>=GameMode.Survival;
    bool Suitable(Point3 point){
        offered++;
        var rules=Rules;var limits=Limits;
        if(!rules.Natural)return Reject("off");
        if(!ScEnemySpawnPolicy.AllowsAfter(true,info.TotalElapsedGameTime,Project.FindSubsystem<SubsystemTimeOfDay>(true).DayDuration,rules.GraceDays))return Reject("waiting");
        if(!ModeAllowed)return Reject("mode");if(spawnCooldown>0)return Reject("cooldown");
        // Manual challenge squads never fill the natural cap; squads of unknown origin are counted conservatively.
        if(Enemies.Count(CountsAsNatural)+Roles(Day,FiveMemberDay).Length>limits.Cap)return Reject("cap");
        // The whole squad must fit the game's global budget (SpawnSquad keeps it hard). Refusing here lets the
        // native scheduler give this candidate to another creature instead of wasting it on a squad that cannot commit.
        var budget=Budget;if(!budget.Fits)return Reject(budget.Impossible?"budget-impossible":"budget-full");
        // Native suitability receives the first free cell ABOVE the support block.
        point.Y--;
        return (pending=PlanNatural(point,Roles(Day,FiveMemberDay).Length,out string refusal)) is not null||Reject(refusal,point);
    }
    // ---- where a natural squad appears (current-direction-20260929 §2) ----
    // The native scheduler offers a surface point 24-48 blocks away on each axis (34-68 horizontally), and the engine puts
    // every creature farther than 60 blocks from all views to sleep: a squad offered on a diagonal slept at once, and one
    // that stayed awake stood where it appeared, outside the 32/36-block notice range. The native offer still decides
    // WHETHER a squad appears (its period, population budget and weighted draw, Slower included); CS only chooses WHERE:
    // a safe standing place 32-44 blocks from the nearest player, preferring one the player cannot see (to the side,
    // behind cover), then one in open view but no nearer than 32 blocks, which fades in (native two-second spawn fade)
    // and holds fire for the three-second warning. The squad then walks one bounded leg toward where that player was
    // (to 18 blocks short of it) and waits there; it never follows the player live and nobody is moved instantly.
    public const float NaturalNear=32,NaturalFar=44,NaturalWarmup=3,PatrolStop=18,NaturalMinimum=28;
    static readonly float[] NaturalDistances=[36,40,33,44];
    static readonly float[] NaturalAngles=[90,-90,60,-60,120,-120,30,-30,150,-150,0,180];
    public sealed record NaturalPlan(List<Vector3> Members,Vector3 Anchor,Vector3 Player,float Distance,bool InView,Vector3? Patrol);
    NaturalPlan pending; // the plan of the last suitability check, which SpawnSquad runs right before using it
    /// <summary>The whole squad's places for a natural offer at support cell <paramref name="offered"/>, or null with the
    /// refusal of the best candidate. Searched afresh on every call (never cached: the world may change in between).</summary>
    public NaturalPlan PlanNatural(Point3 offered,int count,out string refusal){
        using var timing=ScTacticalPerformance.Measure(Project,ScTacticalPerformance.Stage.Placement);
        return SearchNatural(offered,count,out refusal);
    }
    NaturalPlan SearchNatural(Point3 offered,int count,out string refusal){
        refusal=null;
        var people=players.ComponentPlayers.Where(p=>p.ComponentBody is not null).ToArray();
        var o=new Vector3(offered.X+.5f,offered.Y+1,offered.Z+.5f);
        // Nobody to meet (the native scheduler offers only around views; a chunk path may not): the offer itself.
        if(people.Length==0)return NaturalCandidate(offered.X,offered.Z,count,o,out refusal);
        var player=people.OrderBy(p=>Vector3.DistanceSquared(p.ComponentBody.Position,o)).First();var center=player.ComponentBody.Position;
        Vector2 Unit(Vector2 v,Vector2 fallback)=>v.LengthSquared()>1e-4f?Vector2.Normalize(v):fallback;
        var forward=Unit((player.GameWidget?.ActiveCamera?.ViewDirection??player.ComponentBody.Matrix.Forward).XZ,Vector2.UnitX);
        var directions=new List<Vector2>{Unit((o-center).XZ,forward)};
        float jitter=random.Float(-15,15);int side=random.Bool()?1:-1;
        foreach(float a in NaturalAngles){float r=MathUtils.DegToRad(side*a+jitter);directions.Add(new(forward.X*MathF.Cos(r)-forward.Y*MathF.Sin(r),forward.X*MathF.Sin(r)+forward.Y*MathF.Cos(r)));}
        NaturalPlan open=null;
        foreach(var d in directions)foreach(float distance in NaturalDistances){
            var plan=NaturalCandidate((int)MathF.Floor(center.X+d.X*distance),(int)MathF.Floor(center.Z+d.Y*distance),count,center,out string why);
            if(plan is null){refusal??=why;continue;}
            if(!plan.InView)return plan with {Patrol=PatrolPoint(plan.Anchor,center)};
            open??=plan;
        }
        if(open is not null){refusal=null;return open with {Patrol=PatrolPoint(open.Anchor,center)};}
        refusal??="placement:none";return null;
    }
    NaturalPlan NaturalCandidate(int x,int z,int count,Vector3 center,out string why){
        var t=terrain.Terrain;why=null;
        if(t.GetChunkAtCell(x,z) is not {} chunk||chunk.State<=TerrainChunkState.InvalidPropagatedLight){why="placement:unloaded";return null;}
        if(PlacementRefusal(new Point3(x,t.GetTopHeight(x,z),z),NaturalNear,NaturalMinimum,out var anchor,out int leader) is {} r){why="placement:"+r;return null;}
        float spacing=Limits.Spacing;
        if(Enemies.Any(e=>e.Creature.ComponentHealth.Health>0&&Vector3.DistanceSquared(e.Creature.ComponentBody.Position,anchor)<spacing*spacing)){why="spacing";return null;}
        var sleeping=Project.FindSubsystem<SubsystemSpawn>(true).m_chunks.Values.SelectMany(c=>c.SpawnsData).Where(e=>e.TemplateName==Template);
        if(sleeping.Any(e=>Vector3.DistanceSquared(e.Position,anchor)<spacing*spacing)){why="spacing-sleeping";return null;}
        // Every member stands on real ground near the leader's (not on the column top, which may be a canopy); the
        // whole squad is found before anything is created.
        var locations=new List<Vector3>();string first=null;
        foreach(var offset in new[]{new Point2(0,0),new Point2(3,0),new Point2(-3,0),new Point2(0,3),new Point2(0,-3),new Point2(3,3),new Point2(-3,-3)}){
            if(PlacementRefusal(new Point3(x+offset.X,leader,z+offset.Y),NaturalNear,NaturalMinimum,out var p,out int ground) is {} refusal){first??=refusal;continue;}
            if(Math.Abs(ground-leader)>3){first??="uneven";continue;}
            if(locations.Any(q=>Vector3.DistanceSquared(q,p)<2.25f))continue;
            locations.Add(p);if(locations.Count==count)break;
        }
        if(locations.Count!=count){why="squad-placement:"+(first??"none");return null;}
        return new(locations,anchor,center,Vector2.Distance(anchor.XZ,center.XZ),locations.Any(InOpenView),null);
    }
    /// <summary>Where the squad walks after appearing: on the line toward the player, 18 blocks (else 22, 26) short of
    /// where the player stood, on real ground; null when there is none (the squad then waits where it appeared).</summary>
    Vector3? PatrolPoint(Vector3 anchor,Vector3 center){
        var dir=(anchor-center).XZ;float length=dir.Length();if(length<1)return null;dir/=length;
        foreach(float stop in new[]{PatrolStop,PatrolStop+4,PatrolStop+8}){
            if(stop>=length-2)break;
            int x=(int)MathF.Floor(center.X+dir.X*stop),z=(int)MathF.Floor(center.Z+dir.Y*stop);
            if(terrain.Terrain.GetChunkAtCell(x,z) is null)continue;
            if(Footing(x,terrain.Terrain.GetTopHeight(x,z),z,out var at,out _) is null)return at;
        }
        return null;
    }
    bool InOpenView(Vector3 p)=>players.ComponentPlayers.Any(player=>SeenBy(player,p));
    /// <summary>In this player's view: its camera here, or (multiplayer server) the view its client sent, with the same
    /// 60° half-angle as a default camera's frustum and the same terrain check.</summary>
    bool SeenBy(ComponentPlayer player,Vector3 p){
        if(player.GameWidget?.ActiveCamera is {} camera)return InView(camera,p);
        if(!ScNet.IsRemoteDriven(player)||TacticalNet.View(player) is not {} view)return false;
        var to=p+Vector3.UnitY-view.Position;float d=to.Length();
        return d>1e-3f&&Vector3.Dot(to/d,view.Direction)>=MathF.Cos(MathUtils.DegToRad(60))&&!terrain.Raycast(view.Position,p+Vector3.UnitY,false,true,(v,r)=>ScGunRange.TerrainStopsBullet(v)).HasValue;
    }
    bool InView(Camera camera,Vector3 p)=>camera.ViewFrustum.Intersection(new BoundingSphere(p+Vector3.UnitY,1))&&!terrain.Raycast(camera.ViewPosition,p+Vector3.UnitY,false,true,(v,r)=>ScGunRange.TerrainStopsBullet(v)).HasValue;
    /// <summary>How far below a tree canopy the real ground is looked for, in blocks.</summary>
    public const int CanopyDepth=12;
    /// <summary>Where a squad member can stand in column x,z near support height nearY (r2-c4-completion-20260929: cover
    /// such as grass and snow is fine, a canopy is looked under). Uses each block's own collision boxes: the support is
    /// the first solid block at or below nearY+2 (down to nearY-3, or CanopyDepth under leaves) whose box covers the
    /// column centre, not a hazard; the standing box (0.65 x 1.8) above it must be free of solid boxes and liquids; and
    /// anything solid higher up in the column may only be a tree (leaves or wood). A roof or an overhang still refuses.
    /// Null with the standing position and support height, or the refusal reason.</summary>
    public string Footing(int x,int nearY,int z,out Vector3 position,out int support){
        position=default;support=-1;var t=terrain.Terrain;var chunk=t.GetChunkAtCell(x,z);
        if(chunk is null||chunk.State<=TerrainChunkState.InvalidPropagatedLight)return "unloaded";
        int lowest=nearY-3;string refusal="soft-ground";
        for(int y=Math.Min(252,nearY+2);y>=Math.Max(2,lowest);y--){
            int value=t.GetCellValue(x,y,z);var block=BlocksManager.Blocks[Terrain.ExtractContents(value)];
            if(block is FluidBlock){refusal="fluid";break;} // standing in or on water, magma
            if(!block.IsCollidable_(value))continue;    // air, grass, flowers, snow layers: walked through
            if(block is LeavesBlock){lowest=Math.Min(lowest,y-CanopyDepth);continue;} // a canopy: its real ground is below
            if(block.ShouldAvoid(value)){refusal="hazard";break;}
            float top=block.GetCustomCollisionBoxes(terrain,value).Where(b=>b.Min.X<=.5f&&b.Max.X>=.5f&&b.Min.Z<=.5f&&b.Max.Z>=.5f).Select(b=>b.Max.Y).DefaultIfEmpty(0).Max();
            if(top<=0){refusal="soft-ground";break;}
            var at=new Vector3(x+.5f,y+top+.01f,z+.5f);
            if(at.Y<3||at.Y>250)return "height";
            var bounds=new BoundingBox(at-new Vector3(.325f,0,.325f),at+new Vector3(.325f,1.8f,.325f));
            for(int cy=y;cy<=Math.Min(255,Terrain.ToCell(bounds.Max.Y));cy++){
                int v=t.GetCellValue(x,cy,z);var b=BlocksManager.Blocks[Terrain.ExtractContents(v)];
                if(b is FluidBlock)return "fluid";
                if(cy>y&&b.ShouldAvoid(v))return "hazard";
                if(!b.IsCollidable_(v))continue;
                foreach(var box in b.GetCustomCollisionBoxes(terrain,v))
                    if(bounds.Intersection(new BoundingBox(box.Min+new Vector3(x,cy,z),box.Max+new Vector3(x,cy,z))))return "headroom";
            }
            for(int cy=Terrain.ToCell(bounds.Max.Y)+1,top2=t.GetTopHeight(x,z);cy<=top2&&cy<256;cy++){
                int v=t.GetCellValue(x,cy,z);var b=BlocksManager.Blocks[Terrain.ExtractContents(v)];
                if(b.IsCollidable_(v)&&b is not LeavesBlock and not WoodBlock)return "under-cover";
            }
            position=at;support=y;return null;
        }
        return refusal;
    }
    /// <summary>Null when a member may stand at the footing found near <paramref name="point"/> (the support block);
    /// otherwise which safety condition refused it: nearer than <paramref name="minimumDistance"/> to a player, in a
    /// player's open view nearer than <paramref name="openView"/> (infinity: never in open view), or occupied.</summary>
    string PlacementRefusal(Point3 point,float openView,float minimumDistance,out Vector3 p,out int support){
        if(Footing(point.X,point.Y,point.Z,out p,out support) is {} footing)return footing;
        foreach(var player in players.ComponentPlayers){float d=Vector3.DistanceSquared(p,player.ComponentBody.Position);
            if(d<minimumDistance*minimumDistance)return "near-player";
            if(d<openView*openView&&SeenBy(player,p))return "in-view";
        }
        var bodies=new DynamicArray<ComponentBody>();Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(p.XZ,1,bodies);var at=p;return bodies.All(b=>Vector3.DistanceSquared(b.Position,at)>1)?null:"occupied";
    }
    public int SpawnSquad(Point3 point){
        if(!Suitable(point))return 0;
        offered--; // the same candidate, already counted when the scheduler weighed it
        point.Y--;var roles=Roles(Day,FiveMemberDay);
        // Native group spawns may cross the local soft threshold; keep the global budget hard.
        if(spawn.CountCreatures(false)+roles.Length>SubsystemCreatureSpawn.m_totalLimit){Reject("budget-full",point);return 0;}
        if(pending is not {} plan||plan.Members.Count!=roles.Length){Reject("squad-placement:none",point);return 0;}
        int made=CreateSquad(roles,plan.Members,NaturalWarmup,"natural",null,plan);
        if(made>0)squads++;else Reject("squad-create",point);
        return made;
    }
    public string ManualFailure {get;private set;}="";
    public int SpawnManual(Point3 ground,int count){
        ManualFailure="";
        if(count is not (3 or 5)){ManualFailure="信标类型无效。";return 0;}
        // Manual beacons do not share natural-spawn population, visibility or distance gates.
        var locations=FindManualLocations(ground,count,out var challenger);
        if(locations.Count==count)return CreateSquad(Roles(count==5?FiveMemberDay:0,FiveMemberDay),locations,ManualWarmup,"manual",challenger);
        ManualFailure="对准位置附近没有足够的可站立位置，请选择开阔地面（需要人物高度，不能在实体内生成）。";return 0;
    }
    /// <summary>Manual squads use the clicked ground and retain the preparation window.</summary>
    public const float ManualWarmup=3;
    List<Vector3> FindManualLocations(Point3 ground,int count,out Vector3 challenger){
        using var timing=ScTacticalPerformance.Measure(Project,ScTacticalPerformance.Stage.Placement);
        var click=new Vector3(ground.X+.5f,ground.Y+1,ground.Z+.5f);
        var people=players.ComponentPlayers.Where(p=>p.ComponentBody is not null).Select(p=>p.ComponentBody.Position).ToArray();
        var user=people.Length==0?click:people.OrderBy(p=>Vector3.DistanceSquared(p,click)).First();
        challenger=user;
        var locations=new List<Vector3>();
        for(int radius=0;radius<=4&&locations.Count<count;radius++)for(int dx=-radius;dx<=radius&&locations.Count<count;dx++)for(int dz=-radius;dz<=radius&&locations.Count<count;dz++){
                if(Math.Max(Math.Abs(dx),Math.Abs(dz))!=radius)continue;
                if(!ManualPosition(ground.X+dx,ground.Y,ground.Z+dz,out var pos))continue;
                if(locations.Any(p=>Vector3.DistanceSquared(p,pos)<2.25f))continue;
                locations.Add(pos);
        }
        return locations;
    }
    bool ManualPosition(int x,int clickedY,int z,out Vector3 position)=>ManualFloor(x,clickedY+3,clickedY-4,z,out position);
    bool ManualFloor(int x,int highest,int lowest,int z,out Vector3 position){
        position=default;var t=terrain.Terrain;var chunk=t.GetChunkAtCell(x,z);
        if(chunk is null||chunk.State<TerrainChunkState.InvalidLight)return false;
        // Search near the clicked floor, not the column's top: roofs and foliage are not floors.
        for(int y=Math.Min(251,highest);y>=Math.Max(1,lowest);y--){
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
    /// <summary>Seconds a summoned squad searches toward its challenger after the warning window.</summary>
    public const float ManualSearch=8;
    /// <summary>The protection a new enemy is given (current-direction-20260929 §1, first-round experimental default): none,
    /// half (body protection only) or full (body and head protection), one third each, drawn on its own for every new
    /// member of a natural or summoned squad; a squad may mix and is never made complete. Full values; head protection
    /// alone never. Only at creation: enemies that exist are never drawn again, refilled or given any from their looks.</summary>
    public static ScArmorConfig DrawArmor(Engine.Random random)=>random.Int(0,2) switch{0=>ScArmorConfig.None,1=>ScArmorConfig.Half,_=>ScArmorConfig.Full};
    int CreateSquad(TacticalRole[] roles,List<Vector3> locations,float warmup,string source,Vector3? challenger=null,NaturalPlan natural=null){
        using var trace=ScTacticalPerformance.Spawn(Project,"squad",roles.Length);
        string squad=Guid.NewGuid().ToString("N");var made=new List<Entity>();
        var armor=Project.FindSubsystem<SubsystemScArmor>(false);var armored=new List<string>();
        try{
            for(int i=0;i<roles.Length;i++){
                Entity e;using(ScTacticalPerformance.Measure(Project,ScTacticalPerformance.Stage.EntityCreate))e=DatabaseManager.CreateEntity(Project,Template,true);
                made.Add(e);var enemy=e.FindComponent<ComponentTacticalEnemy>(true);
                using(ScTacticalPerformance.Measure(Project,ScTacticalPerformance.Stage.Configure))enemy.Configure(TacticalEnemyState.CreateWarm(roles[i],squad,random,warmup),locations[i]);
                var state=enemy.State;state.Rewardable=info.WorldSettings.GameMode!=GameMode.Creative;state.Source=source;
                if(challenger is {} at)enemy.Investigate(at,0);
                if(natural?.Patrol is {} patrol)enemy.Home=new Vector3(patrol.X+locations[i].X-natural.Anchor.X,patrol.Y,patrol.Z+locations[i].Z-natural.Anchor.Z);
                var config=DrawArmor(random);
                if(armor is not null&&config!=ScArmorConfig.None){
                    if(armor.TryCreate(enemy.ArmorKey,ScArmorState.For(config)))armored.Add(enemy.ArmorKey);
                    else KnifeDiagnostics.WarnOnce("enemy-armor-exists-"+enemy.ArmorKey,$"[CS Tactical] protection for {enemy.ArmorKey} already exists; kept, not drawn again");
                }
                using(ScTacticalPerformance.Measure(Project,ScTacticalPerformance.Stage.WeaponPrepare))
                    ScWeaponPreparation.Request(state.DisplayValue);
            }
            foreach(var e in made){using var timing=ScTacticalPerformance.Measure(Project,ScTacticalPerformance.Stage.AddEntity);Project.AddEntity(e);}
            spawnCooldown=source=="natural"?Limits.Cooldown:60;trace.Success=true;pending=null;
            if(natural is not null)Created(squad,natural,made.Count);
            return made.Count;
        }catch(Exception error){foreach(var e in made){if(e.IsAddedToProject)Project.RemoveEntity(e,true);else e.Dispose();}foreach(var key in armored)armor.Remove(key);ManualFailure="小队实体创建失败，请查看游戏日志。";Log.Warning("[CS Tactical] 小队生成已撤销："+error);return 0;}
    }
    // ---- natural encounters, observed (current-direction-20260929 §2): bounded, the 16 latest natural squads ----
    /// <summary>One natural squad from its creation to the first encounter with a player (game seconds; -1: not yet).</summary>
    public sealed class Encounter {public string Squad;public double Created;public float Distance;public bool InView;public bool Patrols;public double Seen=-1,Heard=-1,Engaged=-1,Slept=-1;}
    readonly List<Encounter> encounters=[];
    public IReadOnlyList<Encounter> Encounters=>encounters;
    void Created(string squad,NaturalPlan plan,int members){
        encounters.Add(new Encounter{Squad=squad,Created=time.GameTime,Distance=plan.Distance,InView=plan.InView,Patrols=plan.Patrol.HasValue});
        if(encounters.Count>16)encounters.RemoveAt(0);
        KnifeLog.Diagnostic($"[CS_SPAWN] natural squad {squad[..8]}: {members} members {plan.Distance:0.0} blocks from the player, {(plan.InView?"in open view (fade-in, 3 s warning)":"out of sight")}, "
            +(plan.Patrol is {} p?$"walks to {Vector2.Distance(p.XZ,plan.Player.XZ):0} blocks from where the player stood":"waits where it appeared"));
    }
    /// <summary>First "seen", "heard" or "engaged" of a natural squad toward a player, or "slept" (put to sleep by the
    /// engine before any encounter); each logged once per squad.</summary>
    public void Note(string squad,string what){
        var e=encounters.FindLast(x=>x.Squad==squad);if(e is null||time is null)return;double now=time.GameTime;
        string line=null;
        switch(what){
            case "seen" when e.Seen<0:e.Seen=now;line="first saw a player";break;
            case "heard" when e.Heard<0:e.Heard=now;line="first heard a player";break;
            case "engaged" when e.Engaged<0:e.Engaged=now;line="first fired at a player";break;
            case "slept" when e.Slept<0&&e.Seen<0&&e.Engaged<0:e.Slept=now;line="was put to sleep before any encounter";break;
        }
        if(line is not null)KnifeLog.Diagnostic($"[CS_SPAWN] natural squad {squad[..Math.Min(8,squad.Length)]} {line} {now-e.Created:0.0} s after appearing");
    }
    string EncounterSummary(){
        if(encounters.Count==0)return "none yet";
        return $"last {encounters.Count}: seen {encounters.Count(e=>e.Seen>=0)}, engaged {encounters.Count(e=>e.Engaged>=0)}, slept unmet {encounters.Count(e=>e.Slept>=0)}, "
            +$"in open view {encounters.Count(e=>e.InView)}, distance {encounters.Min(e=>e.Distance):0}-{encounters.Max(e=>e.Distance):0}";
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
