using Engine;
using GameEntitySystem;
using TemplatesDatabase;
namespace Game;

/// <summary>Optional land-animal investigation; normal chase/escape behaviours outrank it.</summary>
public sealed class ComponentScDecoyBehavior : ComponentBehavior,IUpdateable {
    ComponentCreature m_creature;
    ComponentPathfinding m_path;
    SubsystemTime m_time;
    readonly ScDecoyResponse m_response=new();
    Vector3 m_target;
    double m_until;
    bool m_wasActive;
    float m_health;
    public override float ImportanceLevel => m_until>m_time.GameTime && m_creature.ComponentHealth.Health>0 ? 6 : 0;
    public UpdateOrder UpdateOrder => UpdateOrder.Default;
    public override void Load(ValuesDictionary values,IdToEntityMap map) {
        m_creature=Entity.FindComponent<ComponentCreature>(true);m_path=Entity.FindComponent<ComponentPathfinding>(true);m_time=Project.FindSubsystem<SubsystemTime>(true);
        double remaining=values.GetValue<double>("DecoyRecovery",0);m_response.Next=m_time.GameTime+(double.IsFinite(remaining)?Math.Clamp(remaining,0,18):18);
    }
    public override void Save(ValuesDictionary values,EntityToIdMap map) {
        values.SetValue("DecoyRecovery",Math.Max(0,m_response.Next-m_time.GameTime));
        // Reload cancels investigation but cannot clear the anti-chain cooldown.
    }
    public void HearDecoy(Vector3 position) {
        if (m_creature.ComponentHealth.Health<=0 || !m_response.TryStart(m_time.GameTime)) return;
        if (!ScDecoyResponse.Investigates(m_creature.Category)) {
            Entity.FindComponent<ComponentRunAwayBehavior>()?.HearNoise(null,position,1);
            return;
        }
        // Never take an animal out of an actual fight or override a rider.
        if (m_creature.ComponentHealth.Health<.5f || Entity.FindComponent<ComponentChaseBehavior>()?.m_target is not null || Entity.FindComponent<ComponentMount>()?.Rider is not null) return;
        m_target=position;m_until=m_time.GameTime+6;m_health=m_creature.ComponentHealth.Health;
    }
    public void Update(float dt) {
        if (m_creature.ComponentHealth.Health<m_health || m_creature.ComponentHealth.Health<=0) m_until=0;
        if (IsActive && m_time.GameTime<m_until) {
            if (!m_wasActive) m_path.SetDestination(m_target,.55f,1.8f,300,true,false,true,null);
            m_creature.ComponentCreatureModel.LookAtOrder=m_target;
            if (m_path.IsStuck) m_until=0;
        }
        if (IsActive && m_until<=m_time.GameTime) { m_path.Stop();IsActive=false; }
        if (m_wasActive && !IsActive) m_until=0; // a stronger behaviour wins; do not resume stale attraction
        m_wasActive=IsActive;
    }
}
