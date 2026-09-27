using Engine;
namespace Game;

public sealed class ScHudPosition {
    public bool Custom {get;set;}
    public float X {get;set;}=.9f;
    public float Y {get;set;}=.8f;
    public float Scale {get;set;}=1;
    public float Rotation {get;set;}
    public ScHudPosition Normalize(){X=float.IsFinite(X)?Math.Clamp(X,0,1):.9f;Y=float.IsFinite(Y)?Math.Clamp(Y,0,1):.8f;
        Scale=float.IsFinite(Scale)?Math.Clamp(Scale,.5f,2.5f):1;Rotation=float.IsFinite(Rotation)?MathF.IEEERemainder(Rotation,MathF.PI*2):0;return this;}
    public ScHudPosition Copy()=>new(){Custom=Custom,X=X,Y=Y,Scale=Scale,Rotation=Rotation};
    public Vector2 Extent(Vector2 size){float c=MathF.Abs(MathF.Cos(Rotation)),s=MathF.Abs(MathF.Sin(Rotation));return new Vector2(size.X*c+size.Y*s,size.X*s+size.Y*c)*Scale;}
    public Vector2 Position(Vector2 area,Vector2 size){
        var extent=Vector2.Max(size,Extent(size));var half=Vector2.Min(extent/2,area/2);
        return new Vector2(Math.Clamp(X*area.X,half.X,area.X-half.X),Math.Clamp(Y*area.Y,half.Y,area.Y-half.Y))-size/2;
    }
    public Matrix Transform(Vector2 size)=>Matrix.CreateTranslation(-size.X/2,-size.Y/2,0)*Matrix.CreateScale(Scale)*Matrix.CreateRotationZ(Rotation)*Matrix.CreateTranslation(size.X/2,size.Y/2,0);
}
public static class ScEnemySpawnPolicy {
    public const int GraceDays=30;
    public static bool Allows(bool enabled,double elapsed,double daySeconds)=>enabled&&double.IsFinite(elapsed)&&double.IsFinite(daySeconds)&&daySeconds>0&&elapsed>=GraceDays*daySeconds;
}
