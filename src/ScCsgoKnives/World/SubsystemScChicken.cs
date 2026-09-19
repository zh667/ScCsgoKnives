using Engine;
using Engine.Input;
using TemplatesDatabase;
namespace Game;

public sealed class SubsystemScChicken : SubsystemBlockBehavior {
    public override int[] HandledBlocks=>[BlocksManager.GetBlockIndex<ScChickenEggBlock>(true)];
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldItem,Impact> impacts=new();
    sealed class Impact { public bool Handled; }
    public override bool OnHitAsProjectile(CellFace? cellFace,ComponentBody body,WorldItem item) {
        if(item is null || Terrain.ExtractContents(item.Value)!=HandledBlocks[0])return false;
        var impact=impacts.GetOrCreateValue(item);
        if(impact.Handled || item.ToRemove)return true;
        // Consumption belongs to native ThrowableBlockBehavior, once the projectile exists.
        // A body + terrain contact in one step must not hatch the same egg twice.
        impact.Handled=true;
        try {
            var entity=Project.FindSubsystem<SubsystemCreatureSpawn>(true).SpawnCreature(ComponentScChicken.Template,item.Position,true);
            if(entity?.FindComponent<ComponentSpawn>() is { } spawn)spawn.SpawnDuration=.25f;
        } catch(Exception e) { Log.Warning("[CS chicken] Egg spawn failed: "+e.Message); }
        return true;
    }
    public static void RegisterSpawn(GameEntitySystem.Project project) {
        var spawn=project.FindSubsystem<SubsystemCreatureSpawn>(false);
        if(spawn is null || spawn.m_creatureTypes.Any(t=>t.Name==ComponentScChicken.Template))return;
        spawn.m_creatureTypes.Add(new SubsystemCreatureSpawn.CreatureType(ComponentScChicken.Template,SpawnLocationType.Surface,true,false){
            SpawnSuitabilityFunction=(_,p)=>{
                var t=spawn.m_subsystemTerrain.Terrain;
                return p.Y>=t.GetTopHeight(p.X,p.Z) && t.GetTemperature(p.X,p.Z)>4
                    && t.GetHumidity(p.X,p.Z)>4 && Terrain.ExtractContents(t.GetCellValue(p.X,p.Y-1,p.Z))==8 ? .3f:0;
            },
            SpawnFunction=(type,p)=>spawn.SpawnCreatures(type,ComponentScChicken.Template,p,1).Count
        });
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComponentPlayer,Press> Presses=new();
    sealed class Press { public bool Down; }
    public static bool HandleFollow(ComponentInput input,WidgetInput widgets) {
        var player=input.m_componentPlayer;if(player is null)return false;
        var press=Presses.GetOrCreateValue(player);
        bool down=widgets.IsKeyDown(Key.E)||input.PlayerInput.Interact.HasValue;
        bool edge=down&&!press.Down;press.Down=down;
        if(!down || !ScGunBindings.Available(player) || ScC4Block.IsValue(player.ComponentMiner.ActiveBlockValue))return false;
        var camera=player.GameWidget.ActiveCamera;
        var ray=new Ray3(camera.ViewPosition,camera.ViewDirection);
        var hit=player.ComponentMiner.Raycast<BodyRaycastResult>(ray,RaycastMode.Interaction,true,true,false,3f);
        var chicken=hit?.ComponentBody.Entity.FindComponent<ComponentScChicken>();
        if(chicken is null || chicken.DeathHandled)return false;
        if(edge && chicken.ToggleFollow(player.PlayerData.PlayerIndex))
            player.ComponentGui.DisplaySmallMessage(chicken.FollowerPlayer<0?"小鸡停止跟随":"小鸡开始跟随",Color.White,false,false);
        input.m_playerInput.ToggleInventory=false;input.m_playerInput.EditItem=false;input.m_playerInput.Interact=null;
        return true;
    }
}
