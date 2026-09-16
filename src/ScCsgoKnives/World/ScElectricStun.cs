using System.Runtime.CompilerServices;
using GameEntitySystem;
namespace Game;

/// <summary>Short, non-stacking control after confirmed injury. Never disables physics or an entity's updates.</summary>
public static class ScElectricStun {
    public const float Duration=2.5f, Interval=5f;
    sealed class State {public double Until, Next;}
    static ConditionalWeakTable<Entity,State> states=new();
    public static void Clear() => states=new();
    public static bool Apply(ComponentBody body,float before,float after,double now) => ApplyAttack(body,before,after,now,Duration);
    public static bool ApplyAttack(ComponentBody body,float before,float after,double now,float duration) {
        if(!float.IsFinite(duration)||duration<=0)return false;
        duration=Math.Min(duration,Duration);
        if(body?.Entity is not {} entity || !float.IsFinite(before+after) || !(before>after && after>0) || !double.IsFinite(now))return false;
        var locomotion=entity.FindComponent<ComponentLocomotion>();
        if(locomotion is null)return false;
        var state=states.GetOrCreateValue(entity);
        if(now<state.Next)return false;
        state.Until=now+duration;state.Next=now+Interval;
        locomotion.StunTime=Math.Max(locomotion.StunTime,duration);
        return true;
    }
    public static bool ActiveAt(Entity entity,double now) => entity is not null && states.TryGetValue(entity,out var state)
        && now<state.Until && entity.FindComponent<ComponentHealth>() is {Health:>0};
    public static bool Active(Entity entity) => entity?.Project?.FindSubsystem<SubsystemTime>(false) is {} time && ActiveAt(entity,time.GameTime);
    public static void FilterAttack(Attackment attack) {
        if(!Active(attack?.Attacker))return;
        attack.AttackPower=0;attack.ImpulseFactor=0;attack.StunTimeSet=0;attack.StunTimeAdd=0;
        attack.AllowImpulseAndStunWhenDamageIsZero=false;attack.AttackSoundVolume=0;
    }
}
