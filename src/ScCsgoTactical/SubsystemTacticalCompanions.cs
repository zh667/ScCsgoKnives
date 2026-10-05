using System.Globalization;
using Engine;
using GameEntitySystem;
using TemplatesDatabase;
namespace Game;

/// <summary>Owned companions never use native AutoDespawn: its unload record carries only the template, position and a
/// free-form string, and a respawn that throws still clears it. Out of camera range a companion is instead moved whole
/// into this saved ledger (owner, orders, health, the exact five slots and other mods' unload data) and recreated with
/// the same entity ID when a camera returns. A failed recreation keeps the entry; nothing is re-rolled or guessed.
/// The ledger is its own saved subsystem so older packages and the core-only edition keep it as opaque data.</summary>
public sealed class SubsystemTacticalCompanions : Subsystem,IUpdateable {
    public const int Schema=1,Slots=5;
    public const string HolderPrefix="dormant-companion:";
    static readonly string[] Templates=["ScTacticalHostage","ScTacticalCT","ScTacticalT"];
    public sealed class Dormant {
        public int EntityId;public string Template="";public Vector3 Position;public float Yaw;public bool ConstantSpawn=true;
        public int OwnerIndex=-1,Order;public bool CeaseFire;public Vector3 GuardPosition;public float Health=1;
        public readonly int[] Values=new int[Slots],Counts=new int[Slots];
        public string SpawnData="";
        /// <summary>Unreadable or future entry, saved back verbatim and never spawned.</summary>
        public ValuesDictionary Raw;
        public string Blocked;
        public int Failures;public double RetryAt;public bool Told;
    }
    readonly Dictionary<string,Dormant> ledger=new(StringComparer.Ordinal);
    SubsystemTime time;SubsystemGameWidgets widgets;SubsystemTerrain terrain;SubsystemSpawn native;SubsystemScTactical tactical;
    double next;
    public UpdateOrder UpdateOrder=>UpdateOrder.Default;
    public IEnumerable<Dormant> Entries=>ledger.Values;
    public int DormantCount=>ledger.Count;
    public override void Load(ValuesDictionary values){
        base.Load(values);
        int schema=values.GetValue("Schema",Schema);
        if(schema!=Schema)throw new InvalidOperationException("同伴远距保存数据版本不受支持，请使用相应版本探员包。");
        time=Project.FindSubsystem<SubsystemTime>(true);widgets=Project.FindSubsystem<SubsystemGameWidgets>(false);terrain=Project.FindSubsystem<SubsystemTerrain>(true);
        native=Project.FindSubsystem<SubsystemSpawn>(false);tactical=Project.FindSubsystem<SubsystemScTactical>(false);
        foreach(var pair in values.GetValue<ValuesDictionary>("Dormant",new())){
            var raw=pair.Value as ValuesDictionary;
            Dormant d;
            try{d=Read(pair.Key,raw);}
            catch(Exception e){d=new Dormant{Raw=raw,Blocked="记录无法读取："+e.Message};Log.Warning($"[CS Tactical] 同伴远距记录 {pair.Key} 已原样保留，未重建：{e.Message}");}
            ledger[pair.Key]=d;
        }
        ScGunHolders.DormantSources["zh667.ScCsgoTactical/companions"]=p=>p.FindSubsystem<SubsystemTacticalCompanions>(false)?.HeldItems()??[];
    }
    public override void Save(ValuesDictionary values){
        base.Save(values);values.SetValue("Schema",Schema);var saved=new ValuesDictionary();
        foreach(var pair in ledger)if(pair.Value.Raw is not null||pair.Value.EntityId>0)saved.SetValue(pair.Key,pair.Value.Raw??Write(pair.Value));
        values.SetValue("Dormant",saved);
    }
    public static string KeyOf(int entityId)=>entityId.ToString(CultureInfo.InvariantCulture);
    public static ValuesDictionary Write(Dormant d){
        var v=new ValuesDictionary();
        v.SetValue("Template",d.Template);v.SetValue("Position",d.Position);v.SetValue("Yaw",d.Yaw);v.SetValue("ConstantSpawn",d.ConstantSpawn);
        v.SetValue("OwnerIndex",d.OwnerIndex);v.SetValue("Order",d.Order);v.SetValue("CeaseFire",d.CeaseFire);v.SetValue("GuardPosition",d.GuardPosition);v.SetValue("Health",d.Health);
        v.SetValue("SpawnData",d.SpawnData??"");
        // Same layout as an inventory component, so detached save audits read these items like any other slot.
        var slots=new ValuesDictionary();v.SetValue("Slots",slots);v.SetValue("SlotsCount",Slots);
        for(int i=0;i<Slots;i++)if(d.Counts[i]>0){var s=new ValuesDictionary();s.SetValue("Contents",d.Values[i]);s.SetValue("Count",d.Counts[i]);slots.SetValue("Slot"+i.ToString(CultureInfo.InvariantCulture),s);}
        return v;
    }
    public static Dormant Read(string key,ValuesDictionary v){
        if(v is null)throw new InvalidOperationException("记录不是字典");
        if(!int.TryParse(key,NumberStyles.None,CultureInfo.InvariantCulture,out int id)||id<=0)throw new InvalidOperationException("实体编号无效");
        var d=new Dormant{EntityId=id,Template=v.GetValue<string>("Template",null),Position=v.GetValue("Position",new Vector3(float.NaN)),Yaw=v.GetValue("Yaw",float.NaN),
            ConstantSpawn=v.GetValue("ConstantSpawn",true),OwnerIndex=v.GetValue("OwnerIndex",int.MinValue),Order=v.GetValue("Order",-1),CeaseFire=v.GetValue("CeaseFire",false),
            GuardPosition=v.GetValue("GuardPosition",new Vector3(float.NaN)),Health=v.GetValue("Health",float.NaN),SpawnData=v.GetValue("SpawnData","")};
        if(!Templates.Contains(d.Template))throw new InvalidOperationException("模板不是战术同伴");
        if(d.OwnerIndex<-1||d.OwnerIndex>64)throw new InvalidOperationException("主人编号无效");
        if(d.Order is <0 or >2)throw new InvalidOperationException("指令无效");
        if(!Finite(d.Position)||!Finite(d.GuardPosition)||!float.IsFinite(d.Yaw))throw new InvalidOperationException("位置无效");
        if(!float.IsFinite(d.Health)||d.Health<=0||d.Health>1)throw new InvalidOperationException("生命值无效");
        if(v.GetValue("SlotsCount",Slots)!=Slots)throw new InvalidOperationException("装备栏格式不受支持");
        var slots=v.GetValue<ValuesDictionary>("Slots",null)??throw new InvalidOperationException("装备栏缺失");
        foreach(var pair in slots){
            int slot=pair.Key.StartsWith("Slot",StringComparison.Ordinal)&&int.TryParse(pair.Key[4..],NumberStyles.None,CultureInfo.InvariantCulture,out int n)?n:-1;
            if(slot<0||slot>=Slots||pair.Value is not ValuesDictionary s)throw new InvalidOperationException("装备栏位置无效");
            int count=s.GetValue("Count",-1);if(count<=0||count>1000||slot==0&&count!=1)throw new InvalidOperationException("装备数量无效");
            d.Values[slot]=s.GetValue<int>("Contents");d.Counts[slot]=count;
        }
        return d;
    }
    static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    public static bool Same(Dormant a,Dormant b)=>a.EntityId==b.EntityId&&a.Template==b.Template&&a.ConstantSpawn==b.ConstantSpawn&&a.OwnerIndex==b.OwnerIndex&&a.Order==b.Order
        &&a.CeaseFire==b.CeaseFire&&a.SpawnData==b.SpawnData&&a.Values.SequenceEqual(b.Values)&&a.Counts.SequenceEqual(b.Counts)
        &&Vector3.DistanceSquared(a.Position,b.Position)<1e-6f&&Vector3.DistanceSquared(a.GuardPosition,b.GuardPosition)<1e-6f&&Math.Abs(a.Yaw-b.Yaw)<1e-4f&&Math.Abs(a.Health-b.Health)<1e-6f;
    /// <summary>Gun items held by dormant companions, including readable slots of entries blocked for other reasons.</summary>
    public IEnumerable<(string Key,int Value,int Count)> HeldItems(){
        foreach(var pair in ledger){
            var d=pair.Value;
            if(d.Raw is not null){
                foreach(var slot in d.Raw.GetValue<ValuesDictionary>("Slots",new()))
                    if(slot.Value is ValuesDictionary s&&s.GetValue("Count",0)>0)yield return(HolderPrefix+pair.Key+":"+slot.Key,s.GetValue("Contents",0),s.GetValue("Count",0));
                continue;
            }
            for(int i=0;i<Slots;i++)if(d.Counts[i]>0)yield return(HolderPrefix+pair.Key+":Slot"+i.ToString(CultureInfo.InvariantCulture),d.Values[i],d.Counts[i]);
        }
    }
    public override void OnEntityAdded(Entity entity){
        if(entity.FindComponent<ComponentTacticalCompanion>() is not {} companion||entity.FindComponent<ComponentSpawn>() is not {} spawn)return;
        spawn.AutoDespawn=false;
        if(!ScNet.IsAuthority)return; // a multiplayer client's copy
        if(spawn.IsDespawning&&companion.Creature?.ComponentHealth.Health>0&&!companion.DeathHandled){
            // Older builds could save a companion in the middle of the native unload fade, together with an empty unload
            // record. Keep the complete saved entity and drop the empty record so the same ID cannot appear twice.
            spawn.DespawnTime=null;RemoveNativeRecords(entity.Id,entity.ValuesDictionary?.DatabaseObject?.Name);
            KnifeLog.Diagnostic($"[CS Tactical] companion {entity.Id}: cancelled an old native unload fade and kept the saved entity.");
        }
    }
    void RemoveNativeRecords(int id,string template){
        if(native is null)return;
        foreach(var chunk in native.m_chunks.Values)chunk.SpawnsData.RemoveAll(d=>d.EntityId==id&&d.TemplateName==template);
        native.m_spawnEntityDatas.Remove(id);
    }
    /// <summary>Where the game is watched from: this process's cameras and (multiplayer server) every remote client's player.</summary>
    Vector2[] Cameras(){
        var views=widgets?.GameWidgets.Select(w=>w.ActiveCamera?.ViewPosition.XZ).Where(v=>v.HasValue).Select(v=>v.Value)??[];
        if(ScNet.IsHost)views=views.Concat(Project.FindSubsystem<SubsystemPlayers>(true).ComponentPlayers.Where(ScNet.IsRemoteDriven).Select(p=>p.ComponentBody.Position.XZ));
        return views.ToArray();
    }
    public void Update(float dt){
        if(!ScNet.IsAuthority)return; // only the multiplayer server moves companions to and from the ledger
        if(time.GameTime<next)return;next=time.GameTime+1;
        Step(Cameras());
        if(time.GameTime>=nextArmorPrune){nextArmorPrune=time.GameTime+600;PruneArmor();}
    }
    double nextArmorPrune=5;
    /// <summary>Removes the protection entries of companions that exist nowhere any more: neither in the world nor asleep in
    /// this ledger (removed by other means than death or dismissal). Entity IDs are never reused, so an entry never passes
    /// to another companion; this only keeps the saved list from growing. Returns the number removed.</summary>
    public int PruneArmor(){
        var armor=Project.FindSubsystem<SubsystemScArmor>(false);if(armor is null)return 0;
        var known=new HashSet<string>(StringComparer.Ordinal);
        foreach(var c in tactical?.Companions??[])if(c.ArmorKey is {} k)known.Add(k);
        foreach(var pair in ledger)if(int.TryParse(pair.Key,NumberStyles.None,CultureInfo.InvariantCulture,out int id))known.Add(SubsystemScArmor.CompanionKey(id));
        var stale=armor.Keys.Where(k=>k.StartsWith("companion-",StringComparison.Ordinal)&&!known.Contains(k)).ToArray();
        foreach(var k in stale)armor.Remove(k);
        if(stale.Length>0)KnifeLog.Diagnostic($"[CS_ARMOR] removed the protection of {stale.Length} companions that no longer exist");
        return stale.Length;
    }
    /// <summary>One lifecycle pass for the given camera positions. Wake radius stays below the unload radius.</summary>
    public void Step(Vector2[] cameras){
        if(cameras is null||cameras.Length==0)return;
        float despawn=native?.DespawnRadius??60;float respawn=Math.Min(native?.SpawnRadius??48,despawn-6);
        foreach(var c in (tactical?.Companions??[]).ToArray())
            if(CanSleep(c)&&cameras.All(v=>Vector2.DistanceSquared(v,c.Creature.ComponentBody.Position.XZ)>despawn*despawn))Sleep(c);
        foreach(var pair in ledger.ToArray()){
            var d=pair.Value;
            if(d.Blocked is not null||d.Raw is not null||time.GameTime<d.RetryAt||!cameras.Any(v=>Vector2.DistanceSquared(v,d.Position.XZ)<respawn*respawn)||!Ready(d.Position))continue;
            Wake(pair.Key,d);
        }
    }
    bool Ready(Vector3 p)=>terrain.Terrain.GetChunkAtCell(Terrain.ToCell(p.X),Terrain.ToCell(p.Z)) is {State:>TerrainChunkState.InvalidPropagatedLight};
    public static bool CanSleep(ComponentTacticalCompanion c)=>c?.Entity?.IsAddedToProject==true&&c.Creature?.ComponentHealth.Health>0&&!c.DeathHandled&&!c.PanelOpen
        &&c.Creature.ComponentSpawn is {IsDespawning:false};
    /// <summary>Moves a live companion into the ledger. The entity is removed only after its record reads back identical.</summary>
    public bool Sleep(ComponentTacticalCompanion c){
        if(!CanSleep(c))return false;
        var entity=c.Entity;var body=c.Creature.ComponentBody;string key=KeyOf(entity.Id);
        if(ledger.ContainsKey(key)){Log.Warning($"[CS Tactical] companion {entity.Id} is live while a dormant record exists; kept both untouched.");return false;}
        c.PrepareDormancy();
        var forward=body.Matrix.Forward;
        var d=new Dormant{EntityId=entity.Id,Template=entity.ValuesDictionary.DatabaseObject.Name,Position=body.Position,Yaw=MathF.Atan2(-forward.X,-forward.Z),
            ConstantSpawn=c.Creature.ConstantSpawn,OwnerIndex=c.OwnerIndex,Order=(int)c.Order,CeaseFire=c.CeaseFire,GuardPosition=c.GuardPosition,Health=c.Creature.ComponentHealth.Health};
        for(int i=0;i<Slots;i++){d.Counts[i]=c.Inventory.GetSlotCount(i);d.Values[i]=d.Counts[i]>0?c.Inventory.GetSlotValue(i):0;}
        // Other mods receive the same unload hook the native despawn would have given them.
        var data=new SpawnEntityData{TemplateName=d.Template,Position=d.Position,ConstantSpawn=d.ConstantSpawn,Data=string.Empty,EntityId=entity.Id};
        ModsManager.HookAction("OnSaveSpawnData",loader=>{loader.OnSaveSpawnData(c.Creature.ComponentSpawn,data);return false;});
        d.SpawnData=data.Data??"";
        Dormant check;
        try{check=Read(key,RoundTrip(Write(d)));}catch(Exception e){check=null;Log.Warning($"[CS Tactical] companion {entity.Id} stays loaded: dormant record failed verification: {e.Message}");}
        if(check is null||!Same(d,check)){if(check is not null)Log.Warning($"[CS Tactical] companion {entity.Id} stays loaded: dormant record changed on read-back.");return false;}
        ledger[key]=d;
        try{Project.RemoveEntity(entity,true);}
        catch(Exception e){if(entity.IsAddedToProject)ledger.Remove(key);Log.Warning($"[CS Tactical] companion {entity.Id} unload failed: {e}");return false;}
        return true;
    }
    static ValuesDictionary RoundTrip(ValuesDictionary v){var x=new System.Xml.Linq.XElement("Values");v.Save(x);var r=new ValuesDictionary();r.ApplyOverrides(System.Xml.Linq.XElement.Parse(x.ToString()));return r;}
    /// <summary>Recreates one companion. Any failure keeps the entry for a bounded retry, then blocks it with a notice.</summary>
    public bool Wake(string key,Dormant d){
        if(Project.Entities.Any(e=>e.Id==d.EntityId))return false; // never two entities with one ID
        Entity entity=null;
        try{
            var values=DatabaseManager.FindEntityValuesDictionary(d.Template,true);
            entity=Project.CreateEntity(values,d.EntityId);
            var data=new SpawnEntityData{TemplateName=d.Template,Position=d.Position,ConstantSpawn=d.ConstantSpawn,Data=d.SpawnData,EntityId=d.EntityId};
            var created=entity;ModsManager.HookAction("OnReadSpawnData",loader=>{loader.OnReadSpawnData(created,data);return false;});
            var c=entity.FindComponent<ComponentTacticalCompanion>(true);
            c.RestoreDormancy(d);
            var body=entity.FindComponent<ComponentBody>(true);body.Position=d.Position;body.Rotation=Quaternion.CreateFromYawPitchRoll(d.Yaw,0,0);
            c.Creature.ConstantSpawn=d.ConstantSpawn;
            Project.AddEntity(entity);
            ledger.Remove(key);
            return true;
        }catch(Exception e){
            try{if(entity?.IsAddedToProject==true)Project.RemoveEntity(entity,true);else entity?.Dispose();}catch(Exception cleanup){Log.Warning("[CS Tactical] companion rebuild cleanup: "+cleanup.Message);}
            d.Failures++;d.RetryAt=time.GameTime+Math.Min(120,5*Math.Pow(2,d.Failures));
            if(d.Failures>=5)d.Blocked="重建失败："+e.Message;
            Log.Warning($"[CS Tactical] companion {d.EntityId} rebuild failed ({d.Failures}); record kept: {e}");
            if(d.Blocked is not null&&!d.Told){d.Told=true;foreach(var p in Project.FindSubsystem<SubsystemPlayers>(true).ComponentPlayers)ScNetFeedback.Tell(p,"一名同伴暂时无法重建，装备和主人记录已原样保留，请查看游戏日志。",Color.Orange);}
            return false;
        }
    }
}
