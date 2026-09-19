using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>CS2 weapon silhouette, cropped by loaded fraction without stretching.</summary>
public sealed class ScMagazineWidget : CanvasWidget {
    public float Fraction;
    public string Icon = "ak47";
    public readonly LabelWidget Count = new() { FontScale=.55f, DropShadow=true, IsHitTestVisible=false,
        HorizontalAlignment=WidgetAlignment.Far, VerticalAlignment=WidgetAlignment.Far, Margin=new Vector2(1,0) };
    public ScMagazineWidget() { Size=new Vector2(116,56);HorizontalAlignment=WidgetAlignment.Center;IsHitTestVisible=false;Children.Add(Count); }
    public static float ClampFraction(float value)=>float.IsFinite(value)?Math.Clamp(value,0,1):0;
    public static Color FillColor(float fraction)=>fraction<=.2f?new Color(245,75,65):fraction<=.5f?new Color(245,205,65):new Color(105,225,120);
    public override void MeasureOverride(Vector2 available) { base.MeasureOverride(available);IsDrawRequired=true; }
    public override void Draw(DrawContext dc) {
        var texture=ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/hud_weapon_"+Icon);
        var batch=dc.PrimitivesRenderer2D.TexturedBatch(texture,false,0,null,null,BlendState.NonPremultiplied,SamplerState.LinearClamp);
        int first=batch.TriangleVertices.Count;float fraction=ClampFraction(Fraction);
        Vector2 origin=new(2,2),size=new(112,36);
        batch.QueueQuad(origin,origin+size,0,Vector2.Zero,Vector2.One,new Color(95,100,110)*GlobalColorTransform);
        if(fraction>0)batch.QueueQuad(origin,origin+new Vector2(size.X*fraction,size.Y),0,
            Vector2.Zero,new Vector2(fraction,1),FillColor(fraction)*GlobalColorTransform);
        batch.TransformTriangles(GlobalTransform,first);
    }
}
