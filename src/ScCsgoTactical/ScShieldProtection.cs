using Engine;
using GameEntitySystem;
namespace Game;

/// <summary>Shared geometric shield interception for any living holder exposing an active inventory.</summary>
public static class ScShieldProtection {
    sealed class HitClock { public double Next; }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IInventory,HitClock> Hits=new();
    public static IInventory Inventory(ComponentBody body)=>body?.Entity?.FindComponent<ComponentMiner>()?.Inventory
        ??body?.Entity?.Components.OfType<IInventory>().FirstOrDefault(i=>i.ActiveSlotIndex>=0);
    public static bool Holding(ComponentBody body,out IInventory inv){inv=Inventory(body);return body?.Entity?.FindComponent<ComponentHealth>() is {Health:>0}&&inv is not null&&inv.ActiveSlotIndex>=0&&inv.GetSlotCount(inv.ActiveSlotIndex)>0&&ScTacticalShieldBlock.IsShield(inv.GetSlotValue(inv.ActiveSlotIndex))&&ScTacticalShieldBlock.Wear(inv.GetSlotValue(inv.ActiveSlotIndex))<ScTacticalShieldBlock.Life;}
    public static Matrix Pose(ComponentBody body) {
        Vector3 forward=body.Matrix.Forward;forward.Y=0;forward=Vector3.Normalize(forward);
        return Matrix.CreateWorld(body.Position+Vector3.UnitY*(body.BoxSize.Y*.57f)+forward*(body.BoxSize.Z*.5f+.12f),forward,Vector3.UnitY);
    }
    public static bool Intersect(Vector3 start,Vector3 end,Matrix pose,out float fraction) {
        fraction=0;var d=end-start;float dot=Vector3.Dot(d,pose.Forward);
        if(!float.IsFinite(dot)||dot>=-1e-5f)return false;
        float t=Vector3.Dot(pose.Translation-start,pose.Forward)/dot;
        if(t<0||t>1)return false;var p=start+d*t-pose.Translation;
        if(Math.Abs(Vector3.Dot(p,pose.Right))>.46f||Math.Abs(Vector3.Dot(p,pose.Up))>.72f)return false;
        // The narrow viewing opening is reinforced transparent armor, not an unprotected hole.
        fraction=t;return true;
    }
    public static bool Spend(IInventory inv,float damage) {
        if(inv is null||!float.IsFinite(damage)||damage<=0)return false;
        int slot=inv.ActiveSlotIndex,value=inv.GetSlotValue(slot);if(!ScTacticalShieldBlock.IsShield(value)||ScTacticalShieldBlock.Wear(value)>=ScTacticalShieldBlock.Life)return false;
        if(inv is ComponentCreativeInventory)return true;
        int replacement=Terrain.ReplaceData(value,Math.Min(ScTacticalShieldBlock.Life,ScTacticalShieldBlock.Wear(value)+(int)Math.Clamp(MathF.Ceiling(damage),1,ScTacticalShieldBlock.Life)));
        if(inv is ComponentInventoryBase native){var s=native.m_slots[slot];if(s.Count!=1)return false;s.Value=replacement;ScInventoryTransaction.Changed(inv);return true;}
        return false; // Unknown holder stores require an explicit mutation adapter; never grant free protection.
    }
    public static void Filter(Attackment attack) {
        if(attack?.Target?.Project is not {} project||attack.AttackPower<=0||!float.IsFinite(attack.AttackPower)||attack.DictionaryForOtherMods.ContainsKey("ScTacticalShield"))return;
        bool directional=attack is ProjectileAttackment or MeleeAttackment;
        // Explosion origins and persistent fire are not bullet trajectories. Do not guess a source from the owner.
        if(!directional||attack is SubsystemTacticalBombs.BlastAttack||SubsystemScGrenades.IsAreaDamage(attack)||SubsystemScC4.IsBombDamage(attack))return;
        var direction=attack.HitDirection;if(!float.IsFinite(direction.LengthSquared())||direction.LengthSquared()<1e-8f)return;direction=Vector3.Normalize(direction);
        var attacker=attack.Attacker?.FindComponent<ComponentBody>();
        Vector3 end=attack.HitPoint,start=attacker?.BoundingBox.Center()??end-direction*3;
        // Follow the reported final trajectory, not the attacker's current facing/height.
        if(directional)start=end-direction*Math.Max(1,Vector3.Distance(start,end));
        var bodies=new DynamicArray<ComponentBody>();project.FindSubsystem<SubsystemBodies>(true).FindBodiesInArea(new Vector2(Math.Min(start.X,end.X)-1,Math.Min(start.Z,end.Z)-1),new Vector2(Math.Max(start.X,end.X)+1,Math.Max(start.Z,end.Z)+1),bodies);
        IInventory nearest=null;float best=2;
        foreach(var body in bodies)if(body.Entity!=attack.Attacker&&Holding(body,out var inventory)&&Intersect(start,end,Pose(body),out float t)&&t<best){best=t;nearest=inventory;}
        if(nearest is null||!Spend(nearest,attack.AttackPower))return;
        attack.DictionaryForOtherMods.SetValue("ScTacticalShield",true);
        attack.AttackPower=0;attack.m_injuryAmount=null;
        attack.ImpulseFactor=0;attack.StunTimeSet=0;attack.StunTimeAdd=0;attack.AllowImpulseAndStunWhenDamageIsZero=false;
        double now=project.FindSubsystem<SubsystemTime>(false)?.GameTime??0;var sound=Hits.GetOrCreateValue(nearest);
        if(now>=sound.Next){sound.Next=now+.08;project.FindSubsystem<SubsystemAudio>(false)?.PlayRandomSound("Audio/ScCsgoTactical/ShieldHit",.45f,0,start+(end-start)*best,6,true);}
    }
}
