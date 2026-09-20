using Engine;
using Engine.Input;
namespace Game;

public sealed class SubsystemScTactical : SubsystemBlockBehavior {
    readonly HashSet<ComponentTacticalCompanion> companions=[];
    public IEnumerable<ComponentTacticalCompanion> Companions=>companions;
    public override void OnEntityAdded(GameEntitySystem.Entity entity){base.OnEntityAdded(entity);if(entity.FindComponent<ComponentTacticalCompanion>() is {} c)companions.Add(c);}
    public override void OnEntityRemoved(GameEntitySystem.Entity entity){if(entity.FindComponent<ComponentTacticalCompanion>() is {} c)companions.Remove(c);base.OnEntityRemoved(entity);}
    public override int[] HandledBlocks=>[BlocksManager.GetBlockIndex<ScTacticalBeaconBlock>(true),BlocksManager.GetBlockIndex<ScTacticalSquadBlock>(true)];
    public override bool OnUse(Ray3 ray,ComponentMiner miner){
        var player=miner.Entity.FindComponent<ComponentPlayer>();if(player is null)return false;
        var inv=miner.Inventory;int slot=inv.ActiveSlotIndex,value=inv.GetSlotValue(slot);
        if(Terrain.ExtractContents(value)==HandledBlocks[1]){
            if(inv is not ComponentCreativeInventory){Message(player,"敌队演练信标仅限创造模式使用。");return true;}
            var target=miner.Raycast<TerrainRaycastResult>(ray,RaycastMode.Interaction,true,false,false,12);
            if(!target.HasValue||target.Value.CellFace.Face!=4){Message(player,"对准 12 格内的开阔地面召唤敌队。");return true;}
            var ground=target.Value.CellFace;int count=Terrain.ExtractData(value)==1?5:3;
            var director=Project.FindSubsystem<SubsystemTacticalEnemies>(true);int made=director.SpawnManual(new Point3(ground.X,ground.Y,ground.Z),count);
            Message(player,made==count?$"已生成 {count} 人敌对 T 小队。":"未生成："+director.ManualFailure);return true;
        }
        if(Terrain.ExtractContents(value)!=HandledBlocks[0])return false;
        int kind=Terrain.ExtractData(value);if(kind==3){Repair(player);return true;}if(kind<0||kind>2)return true;
        if(kind==0){Message(player,"救援同伴已移除，此旧信标不再召唤。已有同伴可取回装备后解散。");return true;}
        if(companions.Any(c=>c.OwnerIndex==player.PlayerData.PlayerIndex&&!c.DeathHandled)){Message(player,"已有一名同伴，请先收回装备并解散。");return true;}
        var hit=miner.Raycast<TerrainRaycastResult>(ray,RaycastMode.Interaction,true,false,false,5);
        if(!hit.HasValue||hit.Value.CellFace.Face!=4){Message(player,"请对准 5 格内有足够空间的地面上表面。");return true;}
        var cell=hit.Value.CellFace;var pos=new Vector3(cell.X+.5f,cell.Y+1.05f,cell.Z+.5f);
        var terrain=Project.FindSubsystem<SubsystemTerrain>(true);for(int y=0;y<2;y++)if(BlocksManager.Blocks[Terrain.ExtractContents(terrain.Terrain.GetCellValue(cell.X,cell.Y+1+y,cell.Z))].IsCollidable){Message(player,"上方空间不足。");return true;}
        var bodies=new DynamicArray<ComponentBody>();Project.FindSubsystem<SubsystemBodies>(true).FindBodiesAroundPoint(new Vector2(pos.X,pos.Z),1,bodies);
        if(bodies.Any(b=>Vector3.DistanceSquared(b.Position,pos)<1)){Message(player,"落点被生物占用。");return true;}
        GameEntitySystem.Entity entity=null;
        try {
            entity=DatabaseManager.CreateEntity(Project,new[]{"ScTacticalHostage","ScTacticalCT","ScTacticalT"}[kind],true);
            entity.FindComponent<ComponentBody>(true).Position=pos;var c=entity.FindComponent<ComponentTacticalCompanion>(true);c.OwnerIndex=player.PlayerData.PlayerIndex;c.GuardPosition=pos;
            entity.FindComponent<ComponentSpawn>(true).SpawnDuration=.3f;
            // Construct first. A template/resource failure must not consume a beacon.
            Project.AddEntity(entity);
            if(inv is not ComponentCreativeInventory&&inv.RemoveSlotItems(slot,1)!=1){Project.RemoveEntity(entity,true);Message(player,"信标状态已变化，请重试。");return true;}
        }catch(Exception e){if(entity?.IsAddedToProject==true)Project.RemoveEntity(entity,true);Log.Error("[CS Tactical] summon failed: "+e);Message(player,"召唤失败，信标未消耗。请检查日志。");return true;}
        ScInventoryTransaction.Changed(inv);Message(player,"同伴已加入。对准它按 E／交互键管理装备和指令。");
        return true;
    }
    static void Message(ComponentPlayer p,string text)=>p.ComponentGui.DisplaySmallMessage(text,Color.White,false,false);
    static void Repair(ComponentPlayer player){
        var inv=player.ComponentMiner.Inventory;
        if(inv is not ComponentInventoryBase slots){Message(player,"创造模式请直接取用新盾牌。");return;}
        for(int i=0;i<Math.Min(10,inv.SlotsCount);i++){
            int v=inv.GetSlotValue(i);if(inv.GetSlotCount(i)!=1||!ScTacticalShieldBlock.IsShield(v)||ScTacticalShieldBlock.Wear(v)<=0)continue;
            int active=inv.ActiveSlotIndex;if(active==i||inv.GetSlotCount(active)<=0)return;
            // Both slots belong to the same ordinary inventory, with no callbacks between the two writes.
            slots.m_slots[active].Count--;slots.m_slots[i].Value=Terrain.ReplaceData(v,Math.Max(0,ScTacticalShieldBlock.Wear(v)-1000));ScInventoryTransaction.Changed(inv);Message(player,"防爆盾已维修。");return;
        }
        Message(player,"请把受损盾牌放入快捷栏。");
    }
    public static bool Open(ComponentPlayer player){
        if(player is null||!ScGunBindings.Available(player))return false;
        var camera=player.GameWidget.ActiveCamera;var hit=player.ComponentMiner.Raycast<BodyRaycastResult>(new Ray3(camera.ViewPosition,camera.ViewDirection),RaycastMode.Interaction,true,true,false,3);
        var c=hit?.ComponentBody.Entity.FindComponent<ComponentTacticalCompanion>();if(c is null||c.DeathHandled)return false;
        if(!c.OwnedBy(player)){Message(player,"这是其他玩家的同伴。");return true;}
        player.ComponentGui.ModalPanelWidget=new TacticalPanel(player,c);return true;
    }
    public static void RegisterRecipes(){
        static Dictionary<int,int> Cost(params (string id,int count)[] parts)=>parts.ToDictionary(p=>ScComponentCrafting.Resolve(p.id),p=>p.count);
        ScWorkbenchExtension.Register(new("tactical-shield","防爆盾","战术拓展",()=>Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScTacticalShieldBlock>(true)),()=>Cost(("ironingot",40),("copperingot",8),("glass",4),("leather",8))));
        ScWorkbenchExtension.Register(new("tactical-defuser","拆弹钳","战术拓展",()=>Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScTacticalDefuserBlock>(true)),()=>Cost(("ironingot",4),("copperingot",2))));
        for(int i=1;i<4;i++){int kind=i;ScWorkbenchExtension.Register(new("tactical-beacon-"+i,ScTacticalBeaconBlock.Names[i],"战术拓展",()=>Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScTacticalBeaconBlock>(true),0,kind),()=>kind==3?Cost(("ironingot",12),("leather",4)):Cost(("ironingot",20),("copperingot",12),("germaniumchunk",8),("canvas",8))));}
    }
}
