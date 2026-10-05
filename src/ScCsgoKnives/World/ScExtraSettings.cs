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
public enum ScEnemyDensity { Sparse, Standard, Dense }
/// <summary>Natural enemy-squad rules of one world. The start is counted in accumulated game time (whole in-game days
/// of the engine's current DayDuration), never the calendar day shown. Density changes frequency/space limits only.</summary>
public readonly record struct ScEnemyRules(bool Natural,int GraceDays,ScEnemyDensity Density) {
    public const int MaxGraceDays=365;
    public static readonly ScEnemyRules Default=new(true,ScEnemySpawnPolicy.GraceDays,ScEnemyDensity.Standard);
    public ScEnemyRules Normalize()=>new(Natural,Math.Clamp(GraceDays,0,MaxGraceDays),Enum.IsDefined(Density)?Density:ScEnemyDensity.Standard);
}
/// <summary>Implemented by the agents package; null while it is not installed (the Lite core alone).</summary>
public static class ScEnemyRulesBridge {
    public static Func<GameEntitySystem.Project,ScEnemyRules?> Read;
    public static Func<GameEntitySystem.Project,ScEnemyRules,bool> Write;
    /// <summary>Progress of the given rules in the given world, for display only.</summary>
    public static Func<GameEntitySystem.Project,ScEnemyRules,string> Progress;
}
public static class ScEnemySpawnPolicy {
    public const int GraceDays=30;
    public static bool Allows(bool enabled,double elapsed,double daySeconds)=>AllowsAfter(enabled,elapsed,daySeconds,GraceDays);
    public static bool AllowsAfter(bool enabled,double elapsed,double daySeconds,int graceDays)=>enabled&&double.IsFinite(elapsed)&&double.IsFinite(daySeconds)&&daySeconds>0&&graceDays>=0&&elapsed>=graceDays*daySeconds;
    /// <summary>Game seconds still to wait; the same function the gate uses, so the display never disagrees with it.</summary>
    public static double RemainingSeconds(double elapsed,double daySeconds,int graceDays)=>!double.IsFinite(elapsed)||!double.IsFinite(daySeconds)||daySeconds<=0?double.PositiveInfinity:Math.Max(0,Math.Max(0,graceDays)*daySeconds-elapsed);
    /// <summary>First-pass density table (experimental values): natural members cap, game seconds between natural
    /// squads, minimum spacing between natural squads. Even Sparse fits one five-member squad.</summary>
    public static (int Cap,float Cooldown,float Spacing) Limits(ScEnemyDensity density)=>density switch{
        ScEnemyDensity.Sparse=>(5,120,128),ScEnemyDensity.Dense=>(15,40,72),_=>(10,60,96)};
    public static string Label(ScEnemyDensity density)=>density switch{ScEnemyDensity.Sparse=>"稀少",ScEnemyDensity.Dense=>"较多",_=>"标准"};
}
