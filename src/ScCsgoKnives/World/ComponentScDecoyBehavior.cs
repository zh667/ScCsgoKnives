using Engine;
using GameEntitySystem;
using TemplatesDatabase;
namespace Game;

/// <summary>Bounded investigation through the native AI selector, including mods inheriting AICreature.</summary>
public sealed class ComponentScDecoyBehavior : ComponentBehavior,IUpdateable {
    ComponentCreature m_creature;
    ComponentPathfinding m_path;
    SubsystemTime m_time;
    SubsystemTerrain m_terrain;
    ComponentChaseBehavior m_chase;
    ComponentMount m_mount;
    ComponentBehavior[] m_escapes=[];
    bool m_supported;
    readonly ScDecoyResponse m_response=new();
    Vector3 m_target;
    double m_until;
    bool m_wasActive;
    float m_health;
    float m_importance;
    bool Ready => m_creature?.ComponentHealth is not null && m_creature.ComponentBody is not null && m_path is not null && m_time is not null
        && m_supported;
    bool Protected() => !Ready || m_creature.ComponentHealth.Health<.5f || m_creature.ComponentBody.IsEmbeddedInIce
        || m_mount?.Rider is not null
        || m_escapes.Any(b=>b.IsActive)
        || m_chase?.Target is {} target
            && Vector3.DistanceSquared(target.ComponentBody.Position,m_creature.ComponentBody.Position)<36;
    public override float ImportanceLevel => Ready && m_until>m_time.GameTime && !Protected() ? m_importance : 0;
    public UpdateOrder UpdateOrder => UpdateOrder.Default;
    public override void Load(ValuesDictionary values,IdToEntityMap map) {
        m_creature=Entity.FindComponent<ComponentCreature>();m_path=Entity.FindComponent<ComponentPathfinding>();m_time=Project.FindSubsystem<SubsystemTime>(true);
        m_terrain=Project.FindSubsystem<SubsystemTerrain>(false);
        m_chase=Entity.FindComponent<ComponentChaseBehavior>();m_mount=Entity.FindComponent<ComponentMount>();
        m_escapes=Entity.FindComponents<ComponentBehavior>().Where(b=>b is IComponentEscapeBehavior).ToArray();
        m_supported=Entity.FindComponent<ComponentBehaviorSelector>() is not null&&Entity.FindComponent<ComponentPlayer>() is null;
        double remaining=values.GetValue<double>("DecoyRecovery",0);m_response.Next=m_time.GameTime+(double.IsFinite(remaining)?Math.Clamp(remaining,0,18):18);
    }
    public override void Save(ValuesDictionary values,EntityToIdMap map) {
        values.SetValue("DecoyRecovery",Math.Max(0,m_response.Next-m_time.GameTime));
        // Reload cancels investigation but cannot clear the anti-chain cooldown.
    }
    public void HearDecoy(Vector3 position) {
        if (Protected() || m_until>m_time.GameTime || !ScDecoyResponse.Investigates(m_creature.Category)
            || !float.IsFinite(position.X+position.Y+position.Z)) return;
        bool water=(m_creature.Category&(CreatureCategory.WaterPredator|CreatureCategory.WaterOther))!=0;
        if(water) {
            position.Y=m_creature.ComponentBody.Position.Y;
            if(m_terrain?.Terrain is null || BlocksManager.Blocks[Terrain.ExtractContents(m_terrain.Terrain.GetCellValue(Terrain.ToCell(position.X),Terrain.ToCell(position.Y),Terrain.ToCell(position.Z)))] is not WaterBlock)return;
        } else if((m_creature.Category&CreatureCategory.Bird)!=0 && Entity.FindComponent<ComponentLocomotion>()?.FlySpeed>0)position.Y+=2;
        if (!m_response.TryStart(m_time.GameTime))return;
        m_target=position;m_until=m_time.GameTime+ScDecoyResponse.Duration;m_health=m_creature.ComponentHealth.Health;
        // Standard pursuit is 200; only a distant target can be distracted. Do not clear its target or state.
        m_importance=m_chase?.Target is not null?201:12;
    }
    public void Update(float dt) {
        if(!Ready)return;
        if (Protected() || m_creature.ComponentHealth.Health<m_health) m_until=0;
        if (IsActive && m_time.GameTime<m_until) {
            if (!m_wasActive) m_path.SetDestination(m_target,.7f,2f,300,true,false,true,null);
            if(m_creature.ComponentCreatureModel is {} model)model.LookAtOrder=m_target;
            if (m_path.IsStuck) m_until=0;
        }
        if (IsActive && m_until<=m_time.GameTime) { m_path.Stop();IsActive=false; }
        if (m_wasActive && !IsActive) {
            // Clear only our own leftover destination, never a new owner's navigation order.
            if(m_path.Destination==m_target)m_path.Stop();
            m_until=0;
        }
        m_wasActive=IsActive;
    }
}
