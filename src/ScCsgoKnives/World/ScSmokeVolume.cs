using Engine;
namespace Game;

public static class ScSmokeVolume {
    // CS2's smoke reaches its working volume quickly, holds that volume, then fades as a
    // turbulent cloud. It does not visibly collapse into a small ball during dissipation.
    public const float Radius=2.75f, HalfHeight=1.75f, Lifetime=18, GrowthSeconds=.72f, DissipationSeconds=1.25f;
    public static Vector3 Center(ScGrenadeState s) => s.Position+Vector3.UnitY*HalfHeight;
    public static float Growth(ScGrenadeState s) {
        float t=Math.Clamp(s.Age/GrowthSeconds,0,1);
        // Ease-out expansion: fast initial bloom, with a soft settle instead of a linear pop.
        return 1-MathF.Pow(1-t,2.4f);
    }
    public static float Dissipation(ScGrenadeState s) {
        float t=Math.Clamp(s.Remaining/DissipationSeconds,0,1);
        // Smooth fade in the final second; the radius remains almost full while it fades.
        return t*t*(3-2*t);
    }
    public static float CurrentRadius(ScGrenadeState s) => Radius*Growth(s)*(.93f+.07f*Dissipation(s));
    public static float CurrentHeight(ScGrenadeState s) => CurrentRadius(s)*HalfHeight/Radius;
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
        Vector3 offset=point-Center(s);offset.Y*=Radius/HalfHeight;
        float inside=Math.Clamp((radius-offset.Length())/.35f,0,1);
        return inside*Dissipation(s)*(1-ScSmokeDisturbance.Clearing(disturbances,point,s));
    }
    /// <summary>Smoke-filled length of the segment: the same Density the overlay and the sprites use, integrated every
    /// 0.25 m, so the AI's sight edge is the soft edge the player sees and an HE opening reads the same for both.</summary>
    public static float EffectiveInsideLength(Vector3 start,Vector3 end,ScGrenadeState s,IEnumerable<ScSmokeDisturbance> disturbances) {
        if (InsideLength(start,end,Center(s),CurrentRadius(s)+.5f)<=0) return 0; // segment clear of the soft edge too
        Vector3 delta=end-start;float length=delta.Length();if (length<.001f) return 0;
        Vector3 direction=delta/length;const float step=.25f;float sum=0;
        for (float t=step*.5f;t<length;t+=step) sum+=step*Density(s,start+direction*t,disturbances);
        return sum;
    }
    public static bool Blocks(IEnumerable<ScGrenadeState> states,Vector3 eye,Vector3 target,Func<Vector3,Vector3,bool> clear=null,IEnumerable<ScSmokeDisturbance> disturbances=null) {
        Vector3 segment=target-eye;float length2=segment.LengthSquared();if (length2<=1.5f*1.5f) return false;
        return states.Any(s=> {
            if (!s.Effect || s.Kind!=2 || s.Remaining<=0 || EffectiveInsideLength(eye,target,s,disturbances)<=.5f) return false;
            Vector3 point=eye+segment*Math.Clamp(Vector3.Dot(Center(s)-eye,segment)/length2,0,1);
            return clear is null || clear(s.Position+Vector3.UnitY*.1f,point);
        });
    }
    /// <summary>Per-shell sprite count by distance (×2 shells). Far counts rose in 0.32.0 so a distant sphere still overlaps enough to occlude.</summary>
    public static int SpriteCount(float distance) => distance<18?32:distance<40?24:16;
}
