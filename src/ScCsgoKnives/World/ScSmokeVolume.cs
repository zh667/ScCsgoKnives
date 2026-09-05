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
    public static bool Blocks(IEnumerable<ScGrenadeState> states,Vector3 eye,Vector3 target,Func<Vector3,Vector3,bool> clear=null) {
        Vector3 segment=target-eye;float length2=segment.LengthSquared();if (length2<=1.5f*1.5f) return false;
        return states.Any(s=> {
            if (!s.Effect || s.Kind!=2 || s.Remaining<=0 || InsideLength(eye,target,Center(s),CurrentRadius(s))<=.5f) return false;
            Vector3 point=eye+segment*Math.Clamp(Vector3.Dot(Center(s)-eye,segment)/length2,0,1);
            return clear is null || clear(s.Position+Vector3.UnitY*.1f,point);
        });
    }
    public static int SpriteCount(float distance) => distance<18?24:distance<40?12:6;
}
