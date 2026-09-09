using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>Draw the existing world's view only. Never update GameScreen, reparent live controls,
/// or forward editor input to the world. The simulation stays paused on settings screens.</summary>
public sealed class ScGunWorldBackground : Widget {
    readonly DrawContext m_worldContext = new();
    RenderTarget2D m_worldTarget;
    /// <summary>Testable state boundary: even an exception from a world renderer must not leak its
    /// viewport or scroll clipping into the settings widgets drawn afterwards.</summary>
    internal static void WithPreservedDisplay(Action draw) {
        var target = Display.RenderTarget; var viewport = Display.Viewport; var scissor = Display.ScissorRectangle;
        try { draw(); }
        finally { if (!ReferenceEquals(Display.RenderTarget, target)) Display.RenderTarget = target; Display.Viewport = viewport; Display.ScissorRectangle = scissor; }
    }
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
            dc.PrimitivesRenderer3D.Flush(Matrix.Identity);
            dc.PrimitivesRenderer2D.Flush();
            int width = Math.Max(1, Display.Viewport.Width), height = Math.Max(1, Display.Viewport.Height);
            if (m_worldTarget is null || m_worldTarget.Width != width || m_worldTarget.Height != height) {
                m_worldTarget?.Dispose();
                m_worldTarget = new RenderTarget2D(width, height, 1, ColorFormat.Rgba8888, DepthFormat.Depth24Stencil8);
            }
            game.LayoutTransform = GlobalTransform; game.RenderTransform = Matrix.Identity; game.ColorTransform = Color.White;
            game.Measure(ActualSize); game.Arrange(Vector2.Zero, ActualSize);
            // Never pass the enclosing UI's batches into a nested ViewWidget. World drawing changes
            // viewport/scissor, and may flush batches; later labels must not inherit a tiny world viewport.
            WithPreservedDisplay(() => {
                Display.RenderTarget = m_worldTarget;
                Display.ScissorRectangle = new Rectangle(0, 0, width, height);
                Display.Clear(new Color(28,36,42), 1f, 0);
                foreach (var view in game.AllChildren.OfType<ViewWidget>()) view.Draw(m_worldContext);
                m_worldContext.PrimitivesRenderer3D.Flush(Matrix.Identity);
                m_worldContext.PrimitivesRenderer2D.Flush();
            });
            var batch = dc.PrimitivesRenderer2D.TexturedBatch(m_worldTarget, false, 0,
                DepthStencilState.None, RasterizerState.CullNoneScissor, BlendState.Opaque, SamplerState.LinearClamp);
            int first = batch.TriangleVertices.Count;
            batch.QueueQuad(Vector2.Zero, ActualSize, 0, Vector2.Zero, Vector2.One, Color.White);
            batch.TransformTriangles(GlobalTransform, first);
            dc.PrimitivesRenderer2D.Flush();
        }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("layout-world-background", "layout world background: " + e.Message); }
        finally {
            game.LayoutTransform = oldLayout; game.RenderTransform = oldRender; game.ColorTransform = oldColor;
            m_worldContext.PrimitivesRenderer2D.Clear(); m_worldContext.PrimitivesRenderer3D.Clear();
        }
    }
    public override void Dispose() { m_worldTarget?.Dispose(); m_worldTarget = null; base.Dispose(); }
}
