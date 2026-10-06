using Engine;
namespace Game;

/// <summary>Screen bounds for a remote player's flash test. No far plane: a visible burst has no distance cutoff.</summary>
public sealed record ScFlashView(Vector3 Position,Vector3 Forward,Vector3 Right,Vector3 Up,float HalfWidth,float HalfHeight) {
    public bool Valid=>ScGrenadeState.Finite(Position)&&ScGrenadeState.Finite(Forward)&&ScGrenadeState.Finite(Right)&&ScGrenadeState.Finite(Up)
        &&Math.Abs(Forward.LengthSquared()-1)<.02f&&Math.Abs(Right.LengthSquared()-1)<.02f&&Math.Abs(Up.LengthSquared()-1)<.02f
        &&Math.Abs(Vector3.Dot(Forward,Right))<.02f&&Math.Abs(Vector3.Dot(Forward,Up))<.02f&&Math.Abs(Vector3.Dot(Right,Up))<.02f
        &&float.IsFinite(HalfWidth)&&float.IsFinite(HalfHeight)&&HalfWidth is >0 and <20&&HalfHeight is >0 and <20;
    public bool Contains(Vector3 point){
        var d=point-Position;float depth=Vector3.Dot(d,Forward);
        return depth>0&&Math.Abs(Vector3.Dot(d,Right))<=depth*HalfWidth&&Math.Abs(Vector3.Dot(d,Up))<=depth*HalfHeight;
    }
    public static ScFlashView From(Camera camera)=>new(camera.ViewPosition,camera.ViewDirection,camera.ViewRight,camera.ViewUp,
        Math.Abs(1/camera.ProjectionMatrix.M11),Math.Abs(1/camera.ProjectionMatrix.M22));
}
