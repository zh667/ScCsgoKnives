using Engine;
using GameEntitySystem;
namespace Game;

/// <summary>Known local dangers for agents: armed bombs (their actual radius and remaining fuse) and burning areas.
/// No map-wide knowledge: a bomb is known to its own side and otherwise only inside hearing range of its beeping.
/// Priority for callers: dead/disabled, then an evacuation returned here, then blindness, combat and orders.</summary>
public static class TacticalDanger {
    /// <summary>Seconds before detonation at which everyone inside a bomb radius evacuates, whatever they were doing.</summary>
    public const float EvacuateSeconds=12;
    /// <summary>Extra distance kept outside a bomb radius, and the hearing range of a beeping bomb beyond it.</summary>
    public const float BombMargin=3,HearingMargin=8,FireExit=1.6f;
    public readonly record struct Zone(Vector3 Center,float Radius,float Remaining,bool Bomb);
    public static IEnumerable<Zone> Zones(Project project,Vector3 at,bool enemySide){
        if(project is null)yield break;
        if(project.FindSubsystem<SubsystemTacticalBombs>(false) is {} squad)foreach(var b in squad.Bombs){var c=b.Charge;
            if(c.Remaining>0&&(enemySide||Horizontal(at,c.Position)<=c.Radius+HearingMargin))yield return new(c.Position,c.Radius,c.Remaining,true);}
        if(project.FindSubsystem<SubsystemScC4>(false) is {} player)foreach(var c in player.Charges)
            if(c.Remaining>0&&(!enemySide||Horizontal(at,c.Position)<=c.Radius+HearingMargin))yield return new(c.Position,c.Radius,c.Remaining,true);
        if(project.FindSubsystem<SubsystemScGrenades>(false) is {} grenades)foreach(var f in grenades.FireAreas())yield return new(f.Position,f.Radius,float.PositiveInfinity,false);
    }
    public static float Horizontal(Vector3 a,Vector3 b)=>Vector2.Distance(a.XZ,b.XZ);
    /// <summary>Walking time (with margin) needed to clear a bomb radius from here; never less than the fixed evacuation window.</summary>
    public static float EvacuateAt(Zone z,Vector3 at,float speed=3.5f)=>Math.Max(EvacuateSeconds,(z.Radius+BombMargin-Horizontal(at,z.Center))/Math.Max(.5f,speed)+3);
    /// <summary>True when the agent must leave now: inside fire, or inside a bomb radius whose fuse reached the evacuation time.</summary>
    public static bool MustLeave(Zone z,Vector3 at)=>z.Bomb?Horizontal(at,z.Center)<z.Radius+BombMargin&&z.Remaining<=EvacuateAt(z,at)
        :Horizontal(at,z.Center)<=z.Radius+.3f&&Math.Abs(at.Y-z.Center.Y)<1.8f;
    /// <summary>A point must not be chosen as a destination while any known zone covers it.</summary>
    public static bool Inside(IEnumerable<Zone> zones,Vector3 p)=>zones.Any(z=>Horizontal(p,z.Center)<z.Radius+(z.Bomb?BombMargin:.5f));
    /// <summary>The most urgent zone the agent must leave, or null.</summary>
    public static Zone? Urgent(IReadOnlyList<Zone> zones,Vector3 at){
        Zone? best=null;float key=float.MaxValue;
        foreach(var z in zones)if(MustLeave(z,at)){float k=z.Bomb?z.Remaining:-1;if(k<key){key=k;best=z;}}
        return best;
    }
    /// <summary>Exit candidate outside every known zone: straight away from the danger first, then fanning out.
    /// <paramref name="attempt"/> advances when the navigator reports the previous exit blocked.</summary>
    public static Vector3 Exit(IReadOnlyList<Zone> zones,Zone from,Vector3 at,Vector3 fallbackForward,int attempt){
        var away=(at-from.Center).XZ;if(away.LengthSquared()<.01f)away=-fallbackForward.XZ;if(away.LengthSquared()<.01f)away=Vector2.UnitX;away=Vector2.Normalize(away);
        float distance=from.Bomb?from.Radius+BombMargin+3:from.Radius+FireExit;
        float[] fan=[0,40,-40,80,-80,120,-120,180];
        for(int i=0;i<fan.Length;i++){
            float angle=MathUtils.DegToRad(fan[(i+attempt)%fan.Length]);float cos=MathF.Cos(angle),sin=MathF.Sin(angle);
            var dir=new Vector2(away.X*cos-away.Y*sin,away.X*sin+away.Y*cos);
            var p=new Vector3(from.Center.X+dir.X*distance,at.Y,from.Center.Z+dir.Y*distance);
            if(!Inside(zones,p))return p;
        }
        return new Vector3(from.Center.X+away.X*distance,at.Y,from.Center.Z+away.Y*distance);
    }
    /// <summary>Clamps a desired destination out of known zones onto the safe ring around the covering zone.</summary>
    public static Vector3 Clamp(IReadOnlyList<Zone> zones,Vector3 desired,Vector3 at){
        foreach(var z in zones){float r=z.Radius+(z.Bomb?BombMargin:.5f);if(Horizontal(desired,z.Center)>=r)continue;
            var d=(at-z.Center).XZ;if(d.LengthSquared()<.01f)d=(desired-z.Center).XZ;if(d.LengthSquared()<.01f)d=Vector2.UnitX;d=Vector2.Normalize(d);
            desired=new Vector3(z.Center.X+d.X*(r+1),desired.Y,z.Center.Z+d.Y*(r+1));}
        return desired;
    }
}
