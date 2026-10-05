using Engine;
namespace Game;

/// <summary>Companions at the weapon workbench's "防护装备" (current-direction-20260929 §2): the player's own living
/// companions within <see cref="Range"/> blocks, never another player's or an ownerless one, and no remote equipment. The
/// re-check before every commit requires the very same companion (same entity, same ID), alive, still owned by this player
/// and still in range; otherwise nothing is paid.</summary>
public static class TacticalArmorTargets {
    public const float Range=8;
    static bool registered;
    public static void Register(){if(registered)return;registered=true;ScArmorWorkbench.TargetProviders.Add(For);}
    public static IEnumerable<ScArmorWorkbench.Target> For(ComponentPlayer player){
        var tactical=player?.Entity?.Project?.FindSubsystem<SubsystemScTactical>(false);if(tactical is null)yield break;
        foreach(var c in tactical.Companions.Where(c=>Valid(c,player)).OrderBy(c=>Vector3.DistanceSquared(c.Creature.ComponentBody.Position,player.ComponentBody.Position))){
            var entity=c.Entity;int id=entity.Id;string name=$"{c.Creature.DisplayName}（同伴 #{id}）";
            yield return new(SubsystemScArmor.CompanionKey(id),name,()=>ReferenceEquals(c.Entity,entity)&&entity.Id==id&&entity.IsAddedToProject&&Valid(c,player)?null:"这名同伴已离开、死亡、不属于你或距离超过 "+Range+" 格。");
        }
    }
    static bool Valid(ComponentTacticalCompanion c,ComponentPlayer player)=>c is not null&&!c.DeathHandled&&c.OwnedBy(player)&&c.Creature?.ComponentHealth.Health>0&&player.ComponentHealth.Health>0
        &&Vector3.DistanceSquared(c.Creature.ComponentBody.Position,player.ComponentBody.Position)<=Range*Range;
}
