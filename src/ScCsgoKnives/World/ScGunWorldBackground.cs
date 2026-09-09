using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>Draw the existing world's view only. Never update GameScreen, reparent live controls,
/// or forward editor input to the world. The simulation stays paused on settings screens.</summary>
public sealed class ScGunWorldBackground : Widget {
    public ScGunWorldBackground() { IsHitTestVisible = false; }
    public override void MeasureOverride(Vector2 availableSize) { IsDrawRequired = true; }
    public override void Draw(DrawContext dc) {
        var game = ScreensManager.FindScreen<Screen>("Game");
        if (GameManager.Project is null || game is null) {
            var batch = dc.PrimitivesRenderer2D.FlatBatch(); int first = batch.TriangleVertices.Count;
            batch.QueueQuad(Vector2.Zero, ActualSize, 0, new Color(28, 36, 42)); batch.TransformTriangles(GlobalTransform, first);
            return;
        }
        // Retain the real GameWidget/camera and its last viewport. Re-layout for a resized window,
        // but draw only ViewWidgets: no pause menus, duplicate combat controls, or gameplay Update.
        var oldLayout = game.LayoutTransform; var oldRender = game.RenderTransform; var oldColor = game.ColorTransform;
        try {
            dc.PrimitivesRenderer2D.Flush();
            game.LayoutTransform = GlobalTransform; game.RenderTransform = Matrix.Identity; game.ColorTransform = Color.White;
            game.Measure(ActualSize); game.Arrange(Vector2.Zero, ActualSize);
            foreach (var view in game.AllChildren.OfType<ViewWidget>()) view.Draw(dc);
        }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("layout-world-background", "layout world background: " + e.Message); }
        finally { game.LayoutTransform = oldLayout; game.RenderTransform = oldRender; game.ColorTransform = oldColor; }
    }
}
