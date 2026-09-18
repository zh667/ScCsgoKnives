using Engine;
namespace Game;

public static class ScFireArea {
    public static bool IsFire(ScGrenadeState s) => s.Effect && s.Kind is 3 or 4 && s.Remaining>0;
    public static float Radius(int kind) => kind==3?2.5f:3;
    public static float Lifetime(int kind) => kind==3?6:7;
    public static bool Contains(ScGrenadeState s,Vector3 feet) {
        Vector3 d=feet-s.Position;return IsFire(s) && d.X*d.X+d.Z*d.Z<=Radius(s.Kind)*Radius(s.Kind) && d.Y>=-.5f && d.Y<=1.2f;
    }
    /// <summary>One power budget across overlapping zones, independent of frame rate.</summary>
    public static (ScGrenadeState Source,float Power) Exposure(IEnumerable<ScGrenadeState> states,Vector3 feet,float dt,Func<ScGrenadeState,bool> reachable) {
        ScGrenadeState source=null;float power=0;
        foreach (var s in states) if (Contains(s,feet) && reachable(s)) {
            float candidate=4*Math.Min(Math.Max(0,dt),s.Remaining);
            if (candidate>power) { source=s;power=candidate; }
        }
        return (source,power);
    }
    /// <summary>F05: a thrown, not yet popped smoke grenade inside a live fire area (same cylinder as creatures' feet) is heated.
    /// The caller adds the line-of-sight check from the fire's origin so a wall between them does not count.</summary>
    public static bool Heats(ScGrenadeState fire,ScGrenadeState smoke) => smoke.Kind==2 && !smoke.Effect && Contains(fire,smoke.Position);
    public static bool SmokeTouches(ScGrenadeState fire,ScGrenadeState smoke) => ScFireArea.IsFire(fire) && smoke.Effect && smoke.Kind==2 && smoke.Remaining>0
        && Vector3.Distance(fire.Position,ScSmokeVolume.Center(smoke)) < Radius(fire.Kind)+ScSmokeVolume.CurrentRadius(smoke);

    /// <summary>Use ground footprints and a ray above the floor. Walls and separate floors still block extinguishing.</summary>
    public static bool SmokeExtinguishes(ScGrenadeState fire,ScGrenadeState smoke,Func<Vector3,Vector3,bool> clear) {
        if(!IsFire(fire)||!smoke.Effect||smoke.Kind!=2||smoke.Remaining<=0)return false;
        Vector3 delta=smoke.Position-fire.Position;
        if(Math.Abs(delta.Y)>1.2f)return false;
        float horizontal=MathF.Sqrt(delta.X*delta.X+delta.Z*delta.Z);
        if(horizontal>Radius(fire.Kind)+ScSmokeVolume.CurrentRadius(smoke))return false;
        return clear(fire.Position+Vector3.UnitY*.4f,smoke.Position+Vector3.UnitY*.4f);
    }
    public static bool HeatedOnPath(ScGrenadeState fire,Vector3 from,Vector3 to,Func<Vector3,Vector3,bool> clear) {
        if(!IsFire(fire))return false;
        Vector3 segment=to-from;float length=segment.LengthSquared();
        Vector3 closest=from+segment*(length>1e-6f?Math.Clamp(Vector3.Dot(fire.Position-from,segment)/length,0,1):0);
        return Contains(fire,closest)&&clear(fire.Position+Vector3.UnitY*.4f,closest+Vector3.UnitY*.2f);
    }
}
