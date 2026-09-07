using Engine;
namespace Game;

public static class ScSmokeVolume {
    public const float Radius=3, Lifetime=15;
    public static Vector3 Center(ScGrenadeState s) => s.Position+Vector3.UnitY*1.5f;
    public static float CurrentRadius(ScGrenadeState s) => Radius*Math.Clamp(s.Age/.6f,0,1)*Math.Clamp(s.Remaining/1.5f,0,1);
    /// <summary>Length of the finite eye-target segment inside the sphere, not an infinite ray.</summary>
    public static float InsideLength(Vector3 start,Vector3 end,Vector3 center,float radius) {
        Vector3 delta=end-start;float length=delta.Length();if (length<.001f || radius<=0) return 0;
        Vector3 direction=delta/length,offset=start-center;
        float projection=Vector3.Dot(offset,direction),disc=projection*projection-offset.LengthSquared()+radius*radius;
        if (disc<=0) return 0;
        float root=MathF.Sqrt(disc);return Math.Max(0,Math.Min(length,-projection+root)-Math.Max(0,-projection-root));
    }
    /// <summary>F07 unified density: 1 inside the current sphere, 0 outside, minus any HE opening (F06).
    /// Rendering, the inside overlay and the AI sight query all read this.</summary>
    public static float Density(ScGrenadeState s,Vector3 point,IEnumerable<ScSmokeDisturbance> disturbances=null) {
        if (!s.Effect || s.Kind!=2 || s.Remaining<=0) return 0;
        float radius=CurrentRadius(s); if (radius<=0) return 0;
        float inside=Math.Clamp((radius-Vector3.Distance(point,Center(s)))/.5f,0,1);
        return inside*(1-ScSmokeDisturbance.Clearing(disturbances,point));
    }
    /// <summary>Smoke-filled length of the segment once HE openings are subtracted; equals InsideLength with none.</summary>
    public static float EffectiveInsideLength(Vector3 start,Vector3 end,ScGrenadeState s,IEnumerable<ScSmokeDisturbance> disturbances) {
        float geometric=InsideLength(start,end,Center(s),CurrentRadius(s));
        if (geometric<=0 || disturbances is null || !disturbances.Any(d=>d.Active)) return geometric;
        Vector3 delta=end-start;float length=delta.Length();if (length<.001f) return 0;
        Vector3 direction=delta/length;const float step=.25f;float sum=0;
        for (float t=step*.5f;t<length;t+=step) {
            Vector3 point=start+direction*t;
            if (Vector3.Distance(point,Center(s))<=CurrentRadius(s)) sum+=step*(1-ScSmokeDisturbance.Clearing(disturbances,point));
        }
        return Math.Min(sum,geometric);
    }
    public static bool Blocks(IEnumerable<ScGrenadeState> states,Vector3 eye,Vector3 target,Func<Vector3,Vector3,bool> clear=null,IEnumerable<ScSmokeDisturbance> disturbances=null) {
        Vector3 segment=target-eye;float length2=segment.LengthSquared();if (length2<=1.5f*1.5f) return false;
        return states.Any(s=> {
            if (!s.Effect || s.Kind!=2 || s.Remaining<=0 || EffectiveInsideLength(eye,target,s,disturbances)<=.5f) return false;
            Vector3 point=eye+segment*Math.Clamp(Vector3.Dot(Center(s)-eye,segment)/length2,0,1);
            return clear is null || clear(s.Position+Vector3.UnitY*.1f,point);
        });
    }
    public static int SpriteCount(float distance) => distance<18?24:distance<40?12:6;
}
