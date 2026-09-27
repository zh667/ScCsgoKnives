using Engine;
namespace Game;

public sealed class ScHudPosition {
    public bool Custom {get;set;}
    public float X {get;set;}=.9f;
    public float Y {get;set;}=.8f;
    public ScHudPosition Normalize(){X=float.IsFinite(X)?Math.Clamp(X,0,1):.9f;Y=float.IsFinite(Y)?Math.Clamp(Y,0,1):.8f;return this;}
    public ScHudPosition Copy()=>new(){Custom=Custom,X=X,Y=Y};
    public Vector2 Position(Vector2 area,Vector2 size)=>new(
        Math.Clamp(X*area.X-size.X/2,0,Math.Max(0,area.X-size.X)),
        Math.Clamp(Y*area.Y-size.Y/2,0,Math.Max(0,area.Y-size.Y)));
}
public static class ScEnemySpawnPolicy {
    public const int GraceDays=30;
    public static bool Allows(bool enabled,double elapsed,double daySeconds)=>enabled&&double.IsFinite(elapsed)&&double.IsFinite(daySeconds)&&daySeconds>0&&elapsed>=GraceDays*daySeconds;
}
