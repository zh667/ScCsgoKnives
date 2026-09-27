using Engine;
using Engine.Input;
namespace Game;

/// <summary>Tracks a stable finger pair; changing fingers rebases without jumping.</summary>
public sealed class ScHudGesture {
    int first=-1,second=-1;
    Vector2 startMid,startCenter;
    float startDistance,startAngle;
    ScHudPosition start;
    public void Reset(){first=second=-1;start=null;}
    public bool Step(IEnumerable<TouchLocation> touches,ScHudPosition hud,Vector2 area,Vector2 size,
        Func<Vector2,Vector2> local,Func<Vector2,bool> eligible){
        var active=touches.Where(t=>t.State!=TouchLocationState.Released).ToArray();
        if(active.Length<2){Reset();return false;}
        var a=active.FirstOrDefault(t=>t.Id==first);var b=active.FirstOrDefault(t=>t.Id==second);
        if(start==null||!active.Any(t=>t.Id==first)||!active.Any(t=>t.Id==second)){
            Reset();var candidates=active.Where(t=>eligible(t.Position)).Take(2).ToArray();
            if(candidates.Length<2)return false;
            a=candidates[0];b=candidates[1];
            var pa=local(a.Position);var pb=local(b.Position);var center=hud.Position(area,size)+size/2;
            // At least one finger or their midpoint must start over the HUD.
            bool Hit(Vector2 p){var q=Vector2.Transform(p-center,Matrix.CreateRotationZ(-hud.Rotation))/hud.Scale;return MathF.Abs(q.X)<=size.X/2+20&&MathF.Abs(q.Y)<=size.Y/2+20;}
            if(!Hit(pa)&&!Hit(pb)&&!Hit((pa+pb)/2))return false;
            first=a.Id;second=b.Id;start=hud.Copy();startMid=(pa+pb)/2;startCenter=center;
            var d=pb-pa;startDistance=Math.Max(d.Length(),10);startAngle=MathF.Atan2(d.Y,d.X);return true;
        }
        var p=local(a.Position);var q2=local(b.Position);var delta=q2-p;
        var moved=startCenter+(p+q2)/2-startMid;hud.X=moved.X/area.X;hud.Y=moved.Y/area.Y;
        hud.Scale=start.Scale*delta.Length()/startDistance;hud.Rotation=start.Rotation+MathF.Atan2(delta.Y,delta.X)-startAngle;hud.Normalize();return true;
    }
}
