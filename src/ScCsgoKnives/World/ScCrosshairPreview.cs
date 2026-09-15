using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>Geometry preview without touching live settings or requiring a world/camera.</summary>
public sealed class ScCrosshairPreview : CanvasWidget {
    readonly RectangleWidget[] m_lines = Enumerable.Range(0,4).Select(_=>new RectangleWidget { IsHitTestVisible=false, OutlineThickness=0 }).ToArray();
    readonly RectangleWidget m_image = new() { IsHitTestVisible=false, OutlineThickness=0 };
    public ScCrosshairShape Shape = new();
    public string CrossStyle = ScUiSettings.StyleVanilla;
    public Color Tint = Color.White;
    public ScCrosshairPreview() {
        Size = new Vector2(240,200); HorizontalAlignment=WidgetAlignment.Center; ClampToBounds=true;
        foreach(var line in m_lines)Children.Add(line); Children.Add(m_image);
    }
    public override void MeasureOverride(Vector2 available) {
        float w=Math.Min(Size.X,available.X), h=Size.Y;
        // Fit the maximum allowed cross (336 logical units), with a fixed scale across slider values.
        // The preview therefore grows visibly, but can never cover neighbouring controls.
        var s=Shape.Normalize();float scale=s.Scale*Math.Min(1f,(Math.Min(w,h)-16)/336f);
        Vector2 c=new(w/2,h/2);
        foreach(var line in m_lines){line.IsVisible=CrossStyle==ScUiSettings.StyleCross;line.FillColor=Tint;}
        float gap=s.Gap*scale, length=s.Length*scale, half=s.Width*.5f*scale;
        Vector2[] positions=[c+new Vector2(gap,-half),c+new Vector2(-gap-length,-half),c+new Vector2(-half,gap),c+new Vector2(-half,-gap-length)];
        for(int i=0;i<4;i++){m_lines[i].Size=i<2?new Vector2(length,half*2):new Vector2(half*2,length);SetWidgetPosition(m_lines[i],positions[i]);}
        m_image.IsVisible=CrossStyle!=ScUiSettings.StyleCross;m_image.FillColor=Tint;
        m_image.Subtexture=CrossStyle==ScUiSettings.StyleVanilla?ContentManager.Get<Subtexture>("Textures/Atlas/Crosshair"):null;
        float diameter=(CrossStyle==ScUiSettings.StyleVanilla?22:s.Dot)*scale;
        m_image.Size=new Vector2(diameter);SetWidgetPosition(m_image,c-new Vector2(diameter/2));
        base.MeasureOverride(new Vector2(w,h));
    }
}
