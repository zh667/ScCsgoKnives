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
    internal static void WithOpaquePreview(Widget view, Action draw) {
        // ScreensManager leaves the outgoing GameScreen's cached global alpha faded.
        // Correct only the sampled ViewWidget color, without re-arranging the game tree.
        var color = view.m_globalColorTransform;
        try { view.m_globalColorTransform = Color.White; draw(); }
        finally { view.m_globalColorTransform = color; }
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
        // Reuse the last GAME layout, never re-arrange the live screen inside another screen.
        // Its absolute transforms are used by BasePerspectiveCamera. A second UI-scale transform
        // makes the projection look zoomed and leaves cached child geometry behind on return.
        float oldView = SettingsManager.ViewAngle, oldSensitivity = SettingsManager.LookSensitivity;
        try {
            dc.PrimitivesRenderer3D.Flush(Matrix.Identity);
            dc.PrimitivesRenderer2D.Flush();
            int width = Math.Max(1, Display.Viewport.Width), height = Math.Max(1, Display.Viewport.Height);
            if (m_worldTarget is null || m_worldTarget.Width != width || m_worldTarget.Height != height) {
                m_worldTarget?.Dispose();
                m_worldTarget = new RenderTarget2D(width, height, 1, ColorFormat.Rgba8888, DepthFormat.Depth24Stencil8);
            }
            // Never pass the enclosing UI's batches into a nested ViewWidget. World drawing changes
            // viewport/scissor, and may flush batches; later labels must not inherit a tiny world viewport.
            WithPreservedDisplay(() => {
                Display.RenderTarget = m_worldTarget;
                Display.ScissorRectangle = new Rectangle(0, 0, width, height);
                Display.Clear(new Color(28,36,42), 1f, 0);
                foreach (var view in game.AllChildren.OfType<ViewWidget>()) {
                    var gui = view.GameWidget.GuiWidget; bool drawGui = gui.IsDrawEnabled;
                    try { WithOpaquePreview(view, () => view.Draw(m_worldContext)); }
                    finally { gui.IsDrawEnabled = drawGui; }
                }
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
            // Guard against third-party render hooks as well; invalidate any offscreen projection.
            SettingsManager.ViewAngle = oldView; SettingsManager.LookSensitivity = oldSensitivity;
            foreach (var view in game.AllChildren.OfType<ViewWidget>()) view.GameWidget.ActiveCamera.PrepareForDrawing(null);
            m_worldContext.PrimitivesRenderer2D.Clear(); m_worldContext.PrimitivesRenderer3D.Clear();
        }
    }
    public override void Dispose() { m_worldTarget?.Dispose(); m_worldTarget = null; base.Dispose(); }
}
