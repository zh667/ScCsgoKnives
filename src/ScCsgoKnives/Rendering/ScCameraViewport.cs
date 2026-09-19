using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>Native camera projections include the view widget's split-screen transform.
/// Custom viewmodel projections must include that same transform exactly once.</summary>
public static class ScCameraViewport {
    public static float Aspect(Camera camera) {
        Vector2 size=camera.ViewportSize;
        return size.X>0 && size.Y>0 ? size.X/size.Y : 16f/9f;
    }
    public static Matrix MapProjection(Matrix local,Vector2 size,Matrix pixelsToTarget,Viewport target) => local
        * MatrixUtils.CreateScaleTranslation(size.X*.5f,-size.Y*.5f,size.X*.5f,size.Y*.5f)
        * pixelsToTarget
        * MatrixUtils.CreateScaleTranslation(2f/target.Width,-2f/target.Height,-1f,1f);
    public static Matrix Projection(Camera camera,Matrix local) => MapProjection(local,camera.ViewportSize,camera.ViewportMatrix,Display.Viewport);
    public static Matrix Overlay(Camera camera)=>camera.ViewportMatrix*PrimitivesRenderer2D.ViewportMatrix();
    public static float ScopeDiameter(Vector2 size)=>MathF.Min(size.X,size.Y);
    public static Rectangle Clip(Vector2 size,Matrix transform,Rectangle previous) {
        Vector2 a=Vector2.Transform(Vector2.Zero,transform),b=Vector2.Transform(new Vector2(size.X,0),transform),
            c=Vector2.Transform(size,transform),d=Vector2.Transform(new Vector2(0,size.Y),transform);
        var min=Vector2.Min(Vector2.Min(a,b),Vector2.Min(c,d));var max=Vector2.Max(Vector2.Max(a,b),Vector2.Max(c,d));
        int x=(int)MathF.Floor(min.X),y=(int)MathF.Floor(min.Y);
        return Rectangle.Intersection(previous,new Rectangle(x,y,(int)MathF.Ceiling(max.X)-x,(int)MathF.Ceiling(max.Y)-y));
    }
}
