using Engine;
using Engine.Graphics;
using TemplatesDatabase;
using GameEntitySystem;
namespace Game;

/// <summary>World-owned falling deliveries; after landing the native chest owns all loot.
/// Clients receive presentation only. Neither drawing nor a repeated snapshot grants items.</summary>
public sealed class SubsystemScAirdrops : Subsystem,IUpdateable,IDrawable {
    public const ushort SnapshotOp=86;
    public const int Limit=3;
    public sealed class Drop {
        public string Id;
        public Vector3 Ground;
        public float Fall=12,Smoke=180;
        public bool Landed;
        public Vector3 Position=>Ground+Vector3.UnitY*Math.Max(0,Fall)*3;
        public void Validate(){
            if(!Guid.TryParseExact(Id,"N",out _)||!ScGrenadeState.Finite(Ground)||Ground.Y<1||Ground.Y>240
                ||!float.IsFinite(Fall+Smoke)||Fall<0||Fall>12||Smoke<0||Smoke>180||Landed&&Fall!=0)
                throw new InvalidOperationException("空投记录无效，拒绝重置。");
        }
        public ValuesDictionary Save(){var v=new ValuesDictionary();v.SetValue("Id",Id);v.SetValue("Ground",Ground);v.SetValue("Fall",Fall);v.SetValue("Smoke",Smoke);v.SetValue("Landed",Landed);return v;}
        public static Drop Load(ValuesDictionary v){var d=new Drop{Id=v.GetValue<string>("Id"),Ground=v.GetValue<Vector3>("Ground"),Fall=v.GetValue<float>("Fall"),Smoke=v.GetValue<float>("Smoke"),Landed=v.GetValue<bool>("Landed")};d.Validate();return d;}
    }
    public readonly List<Drop> Drops=[];
    readonly Engine.Random random=new();
    readonly PrimitivesRenderer3D renderer=new();
    SubsystemTerrain terrain;SubsystemPlayers players;SubsystemTime time;SubsystemGameInfo info;
    Texture2D smoke;
    float next=600,retry;
    double sent,received;
    public UpdateOrder UpdateOrder=>UpdateOrder.Default;
    public int[] DrawOrders=>[10];
    public override void Load(ValuesDictionary v){
        base.Load(v);if(v.GetValue("Schema",1)!=1)throw new InvalidOperationException("空投存档版本不受支持。");
        next=v.GetValue("Next",600f);if(!float.IsFinite(next)||next<0||next>900)throw new InvalidOperationException("空投计时无效。");
        foreach(var row in v.GetValue("Drops",new ValuesDictionary()))Drops.Add(Drop.Load((ValuesDictionary)row.Value));
        if(Drops.Count>Limit||Drops.Select(d=>d.Id).Distinct().Count()!=Drops.Count)throw new InvalidOperationException("空投记录重复或超限。");
        terrain=Project.FindSubsystem<SubsystemTerrain>(true);players=Project.FindSubsystem<SubsystemPlayers>(true);
        time=Project.FindSubsystem<SubsystemTime>(true);info=Project.FindSubsystem<SubsystemGameInfo>(true);
        smoke=ContentManager.Get<Texture2D>("Textures/ScCsgoTactical/airdrop_smoke");
    }
    public override void Save(ValuesDictionary v){
        base.Save(v);v.SetValue("Schema",1);v.SetValue("Next",next);var rows=new ValuesDictionary();
        for(int i=0;i<Drops.Count;i++)rows.SetValue(i.ToString(),Drops[i].Save());v.SetValue("Drops",rows);
    }
    public static void RegisterNetwork(){
        ScCompatibility.RegisterOwned("Subsystem","ScAirdropDirector");
        ScCompatibility.RegisterOwned("Subsystem","ScAirdrops");
        ScNet.OnClient(SnapshotOp,r=>GameManager.Project?.FindSubsystem<SubsystemScAirdrops>(false)?.Receive(r));
    }
    public void Receive(ScNetReader r){
        int n=r.Int();if(n<0||n>Limit)throw new InvalidDataException("airdrop count");var incoming=new List<Drop>();
        for(int i=0;i<n;i++){var d=new Drop{Id=r.String(32),Ground=r.Vector3(),Fall=r.Float(),Smoke=r.Float(),Landed=r.Bool()};d.Validate();incoming.Add(d);}
        r.Finish();if(incoming.Select(d=>d.Id).Distinct().Count()!=n)throw new InvalidDataException("duplicate airdrop");
        Drops.Clear();Drops.AddRange(incoming);received=Time.RealTime;
    }
    public bool TryStart(Vector3 ground){
        if(!ScNet.IsAuthority||Drops.Count>=Limit||!ScGrenadeState.Finite(ground)||ground.Y<1||ground.Y>240)return false;
        var p=Terrain.ToCell(ground);ground=new(p.X+.5f,p.Y,p.Z+.5f);
        if(Drops.Any(d=>Vector3.DistanceSquared(d.Ground,ground)<256)||!Clear(p))return false;
        // Commit the whole guard squad before announcing a flight. Finding five free
        // positions only after descent could leave a crate hovering at ground level.
        string id=Guid.NewGuid().ToString("N");
        if(!Project.FindSubsystem<SubsystemTacticalEnemies>(true).TrySpawnAirdropGuards(new(p.X,p.Y-1,p.Z),"airdrop-"+id))return false;
        Drops.Add(new(){Id=id,Ground=ground});
        foreach(var player in players.ComponentPlayers)TacticalNet.Tell(player,$"发现空投：{p.X}, {p.Z}。补给箱附近有武装守卫。");
        return true;
    }
    bool Clear(Point3 p){
        var t=terrain.Terrain;var chunk=t.GetChunkAtCell(p.X,p.Z);
        if(chunk is null||chunk.State<TerrainChunkState.InvalidLight)return false;
        int support=t.GetCellValue(p.X,p.Y-1,p.Z);var block=BlocksManager.Blocks[Terrain.ExtractContents(support)];
        return block.IsCollidable_(support)&&!block.ShouldAvoid(support)
            &&Enumerable.Range(p.Y,Math.Min(255,p.Y+37)-p.Y).All(y=>Terrain.ExtractContents(t.GetCellValue(p.X,y,p.Z))==0);
    }
    void FindDrop(){
        var living=players.ComponentPlayers.Where(p=>p.ComponentHealth.Health>0).ToArray();if(living.Length==0)return;
        var origin=living[random.Int(0,living.Length-1)].ComponentBody.Position;
        for(int i=0;i<12;i++){
            float angle=random.Float(0,MathF.PI*2),distance=random.Float(32,56);
            int x=Terrain.ToCell(origin.X+MathF.Cos(angle)*distance),z=Terrain.ToCell(origin.Z+MathF.Sin(angle)*distance);
            if(terrain.Terrain.GetChunkAtCell(x,z) is not {} chunk||chunk.State<TerrainChunkState.InvalidLight)continue;
            int y=terrain.Terrain.GetTopHeight(x,z)+1;
            if(TryStart(new(x+.5f,y,z+.5f)))return;
        }
    }
    bool Land(Drop drop){
        Point3 p=Terrain.ToCell(drop.Ground);
        if(!Clear(p)){
            if(terrain.Terrain.GetChunkAtCell(p.X,p.Z) is {State:>=TerrainChunkState.InvalidLight}){
                Drops.Remove(drop);KnifeLog.Warning("[CS_AIRDROP] landing cancelled: terrain changed at "+p);
            }
            return false;
        }
        var blockEntities=Project.FindSubsystem<SubsystemBlockEntities>(true);
        if(blockEntities.GetBlockEntity(p.X,p.Y,p.Z) is not null)return false;
        int value=Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScAirdropBlock>(true));
        try{
            terrain.ChangeCell(p.X,p.Y,p.Z,value);
            var chest=blockEntities.GetBlockEntity(p.X,p.Y,p.Z)?.Entity.FindComponent<ComponentChest>(true)
                ??throw new InvalidOperationException("空投箱实体未创建。");
            // Native chest AddSlotItems has no partial-capacity path for these empty slots.
            var rewards=RewardsFor(drop.Id);
            for(int slot=0;slot<rewards.Length;slot++){
                var (item,count)=rewards[slot];chest.AddSlotItems(slot,item,count);
                if(chest.GetSlotValue(slot)!=item||chest.GetSlotCount(slot)!=count)throw new InvalidOperationException("空投库存写入不完整。");
            }
            drop.Landed=true;drop.Fall=0;return true;
        }catch(Exception e){
            // Remove staged inventory before native block removal so rollback cannot emit rewards.
            var entity=blockEntities.GetBlockEntity(p.X,p.Y,p.Z);
            if(entity?.Entity.FindComponent<ComponentChest>() is {} chest)for(int i=0;i<chest.SlotsCount;i++)chest.RemoveSlotItems(i,chest.GetSlotCount(i));
            if(Terrain.ExtractContents(terrain.Terrain.GetCellValue(p.X,p.Y,p.Z))==Terrain.ExtractContents(value))terrain.ChangeCell(p.X,p.Y,p.Z,0);
            KnifeLog.Warning("[CS_AIRDROP] landing rolled back: "+e);return false;
        }
    }
    public static readonly string[] GunPool=["ak47","m4a1s","mp5sd","awp","glock18","xm1014","famas","ssg08"];
    /// <summary>One gun, two magazines, two small CS material stacks and two vanilla stacks.
    /// Fixed by delivery identity, so retrying a blocked landing never rerolls the reward.</summary>
    public static (int Value,int Count)[] RewardsFor(string id){
        var seed=Guid.ParseExact(id,"N").ToByteArray();
        int variant=Array.FindIndex(GunSpec.All,g=>g.Name==GunPool[seed[0]%GunPool.Length]);
        if(variant<0)throw new InvalidOperationException("空投枪械目录缺失。");
        int gun=Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScGunBlock>(true),0,GunSpec.WithId(variant,GunSpec.FreshFull));
        return [(gun,1),(ScAmmoBlock.Value(ScAmmoBlock.Magazine),2),
            (ScWeaponMaterialBlock.Value(ScWeaponMaterialBlock.Blank),1+seed[1]%3),
            (ScWeaponMaterialBlock.Value(1+seed[2]%4),1),
            (Terrain.MakeBlockValue(IronIngotBlock.Index),2),
            (Terrain.MakeBlockValue(seed[3]%2==0?CopperIngotBlock.Index:CoalChunkBlock.Index),2)];
    }
    public void Update(float dt){
        dt=Math.Max(0,dt);
        if(!ScNet.IsAuthority){if(Time.RealTime-received>3)Drops.Clear();return;}
        retry=Math.Max(0,retry-dt);
        foreach(var d in Drops.ToArray()){
            if(!d.Landed){d.Fall=Math.Max(0,d.Fall-dt);if(d.Fall==0&&retry==0){Land(d);retry=3;}}
            else{
                d.Smoke=Math.Max(0,d.Smoke-dt);var p=Terrain.ToCell(d.Ground);
                var chunk=terrain.Terrain.GetChunkAtCell(p.X,p.Z);
                if(d.Smoke==0||chunk is not null&&chunk.State>=TerrainChunkState.InvalidLight
                    &&Terrain.ExtractContents(terrain.Terrain.GetCellValue(p.X,p.Y,p.Z))!=BlocksManager.GetBlockIndex<ScAirdropBlock>(true))Drops.Remove(d);
            }
        }
        if(ScModes.Of(Project) is null&&info.WorldSettings.GameMode>=GameMode.Survival&&info.WorldSettings.EnvironmentBehaviorMode==EnvironmentBehaviorMode.Living
            &&Project.FindSubsystem<SubsystemTacticalEnemies>(true).Rules.Natural){
            next=Math.Max(0,next-dt);if(next==0){FindDrop();next=Drops.Count>0?random.Float(600,900):30;}
        }
        if(ScNet.IsHost&&Time.RealTime>=sent){sent=Time.RealTime+.5;ScNet.Broadcast(SnapshotOp,w=>{
            w.Int(Drops.Count);foreach(var d in Drops)w.String(d.Id).Vector3(d.Ground).Float(d.Fall).Float(d.Smoke).Bool(d.Landed);
        });}
    }
    public void Draw(Camera camera,int order){
        foreach(var d in Drops){
            float since=ScNet.IsRemoteClient?(float)Math.Clamp(Time.RealTime-received,0,3):0;
            var position=d.Ground+Vector3.UnitY*Math.Max(0,d.Fall-since)*3;
            if(Vector3.DistanceSquared(position,camera.ViewPosition)>160*160)continue;
            if(!d.Landed){var matrix=Matrix.CreateTranslation(position);TacticalItemMesh.Draw("airdrop",renderer,Color.White,1,ref matrix,new(){SubsystemTerrain=terrain,Light=15});}
            else if(d.Smoke>0)DrawSmoke(camera,d.Ground+Vector3.UnitY*.6f,d.Smoke);
        }
        renderer.Flush(camera.ViewProjectionMatrix);
    }
    void DrawSmoke(Camera camera,Vector3 origin,float remaining){
        // CS2 vertical smoke: 5 particles/s, 6–8 s life, original animated sprite.
        var batch=renderer.TexturedBatch(smoke,false,1,DepthStencilState.DepthRead,RasterizerState.CullNoneScissor,BlendState.NonPremultiplied,SamplerState.LinearClamp);
        for(int i=0;i<35;i++){
            float age=(float)((time.GameTime+i*.2)%7),phase=age/7;
            var p=origin+new Vector3(MathF.Sin(i*2.4f+age*.4f)*age*.1f,age*.8f,MathF.Cos(i*1.7f+age*.3f)*age*.1f);
            float size=.24f+age*.19f;var right=camera.ViewRight*size;var up=camera.ViewUp*size;
            int frame=Math.Min(63,(int)(phase*64));float u=(frame%8)/8f,v=(frame/8)/8f;
            float alpha=.32f*Math.Clamp(age/.4f,0,1)*Math.Clamp((7-age)/2,0,1)*Math.Clamp(remaining/7,0,1);
            var color=new Color(160,160,160,(int)(alpha*255));
            batch.QueueQuad(p-right-up,p+right-up,p+right+up,p-right+up,new(u,v+.125f),new(u+.125f,v+.125f),new(u+.125f,v),new(u,v),color);
        }
    }
    public override void Dispose(){Drops.Clear();base.Dispose();}
}
