using Engine;
namespace Game;

/// <summary>Local movement help for agents on top of native pathfinding. The native pilot jumps only at random
/// (5 % per tick, and only when walk speed is at least 1), so agents walking at 0.45–0.8 almost never climb a full
/// block. Here a jump is ordered only for a detected step in the walking direction: an obstacle above the smooth-rise
/// height and at most 1.25 high, with headroom over it and over the agent, and a cooldown. Never per-frame jumping,
/// block breaking or teleporting.</summary>
public static class TacticalNavigation {
    public const float StepCooldown=.45f,MaxStep=1.25f;
    static bool Solid(SubsystemTerrain terrain,int value)=>BlocksManager.Blocks[Terrain.ExtractContents(value)].IsCollidable_(value);
    /// <summary>Highest collision top in the column that starts below jump height, or <paramref name="feet"/> if none.</summary>
    public static float StepTop(SubsystemTerrain terrain,int x,int z,float feet){
        float top=feet;
        for(int y=Terrain.ToCell(feet+.05f);y<=Terrain.ToCell(feet+MaxStep);y++){
            int value=terrain.Terrain.GetCellValue(x,y,z);if(!Solid(terrain,value))continue;
            foreach(var box in BlocksManager.Blocks[Terrain.ExtractContents(value)].GetCustomCollisionBoxes(terrain,value))
                if(y+box.Min.Y<=feet+MaxStep)top=Math.Max(top,y+box.Max.Y);
        }
        return top;
    }
    /// <summary>Whether any collision box in the column overlaps the vertical span [from, to).</summary>
    public static bool Blocked(SubsystemTerrain terrain,int x,int z,float from,float to){
        for(int y=Terrain.ToCell(from);y<=Terrain.ToCell(to-.01f);y++){
            int value=terrain.Terrain.GetCellValue(x,y,z);if(!Solid(terrain,value))continue;
            foreach(var box in BlocksManager.Blocks[Terrain.ExtractContents(value)].GetCustomCollisionBoxes(terrain,value))
                if(y+box.Max.Y>from&&y+box.Min.Y<to)return true;
        }
        return false;
    }
    /// <summary>The kind of obstacle directly ahead in <paramref name="direction"/>.</summary>
    public enum Step { Clear, Smooth, Jump, Wall }
    public static Step Probe(SubsystemTerrain terrain,ComponentBody body,Vector2 direction){
        if(direction.LengthSquared()<.0001f)return Step.Clear;direction=Vector2.Normalize(direction);
        var probe=body.Position.XZ+direction*(body.BoxSize.X*.5f+.35f);int x=Terrain.ToCell(probe.X),z=Terrain.ToCell(probe.Y);
        float feet=body.Position.Y,top=StepTop(terrain,x,z,feet),rise=top-feet,height=body.BoxSize.Y;
        if(rise<=.02f)return Step.Clear;
        if(rise<=body.MaxSmoothRiseHeight+.02f)return Step.Smooth;
        if(rise>MaxStep)return Step.Wall;
        // Room to stand on the step, and room over the agent's own head for the jump arc.
        if(Blocked(terrain,x,z,top+.02f,top+height)||Blocked(terrain,Terrain.ToCell(body.Position.X),Terrain.ToCell(body.Position.Z),feet+height,top+height))return Step.Wall;
        return Step.Jump;
    }
    /// <summary>Orders one jump for a climbable step toward <paramref name="destination"/>; bounded by a cooldown.</summary>
    public static bool StepAssist(ComponentCreature creature,SubsystemTerrain terrain,Vector3? destination,ref double nextJump,double now){
        var body=creature?.ComponentBody;
        if(body is null||terrain is null||!destination.HasValue||now<nextJump||!body.StandingOnValue.HasValue||body.ImmersionFactor>.3f)return false;
        if((destination.Value-body.Position).XZ.LengthSquared()<.25f)return false;
        // The pilot steers toward its next waypoint, so the step that matters is the one in the facing direction.
        if(Probe(terrain,body,body.Matrix.Forward.XZ)!=Step.Jump)return false;
        creature.ComponentLocomotion.JumpOrder=1;nextJump=now+StepCooldown;return true;
    }
    /// <summary>Re-plans only for a real change; native SetDestination always restarts path search.</summary>
    public static void Navigate(ComponentPathfinding path,Vector3 destination,float speed,float range,int limit,bool random,bool ignoreHeight,bool raycast,ComponentBody avoid,float tolerance=1.2f){
        if(path.Destination is Vector3 current&&Vector3.DistanceSquared(current,destination)<=tolerance*tolerance&&path.Speed==speed&&path.IgnoreHeightDifference==ignoreHeight&&!path.IsStuck)return;
        path.SetDestination(destination,speed,range,limit,random,ignoreHeight,raycast,avoid);
    }
}
