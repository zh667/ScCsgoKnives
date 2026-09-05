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
    public static bool SmokeTouches(ScGrenadeState fire,ScGrenadeState smoke) => ScFireArea.IsFire(fire) && smoke.Effect && smoke.Kind==2 && smoke.Remaining>0
        && Vector3.Distance(fire.Position,ScSmokeVolume.Center(smoke)) < Radius(fire.Kind)+ScSmokeVolume.CurrentRadius(smoke);
}
