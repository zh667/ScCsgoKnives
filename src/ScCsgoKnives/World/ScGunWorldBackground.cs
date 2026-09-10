using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>One attempt per capture key, including failures. No per-frame render/retry loop in paused UI.</summary>
internal sealed class ScWorldBackgroundCaptureState {
    object m_project;
    int m_width, m_height;
    bool m_attempted;
    public bool Ready { get; private set; }
    public void Reset() { m_project = null; m_attempted = false; Ready = false; }
    public bool Ensure(object project, int width, int height, Action capture) {
        if (!ReferenceEquals(m_project, project) || m_width != width || m_height != height) Reset();
        if (!m_attempted) {
            m_project = project; m_width = width; m_height = height; m_attempted = true;
            capture(); // failure leaves Ready=false until explicit invalidation
            Ready = true;
        }
        return Ready;
    }
}

/// <summary>Frozen in-memory world image for settings, binding and layout pages. Never update GameScreen,
/// reparent live controls or forward editor input. Opening a page captures once; exiting frees the GPU image.</summary>
public sealed class ScGunWorldBackground : Widget {
    readonly DrawContext m_worldContext = new();
    readonly ScWorldBackgroundCaptureState m_capture = new();
    RenderTarget2D m_worldTarget;
    internal static void WithPreservedDisplay(Action draw) {
        var target = Display.RenderTarget; var viewport = Display.Viewport; var scissor = Display.ScissorRectangle;
        try { draw(); }
        finally { if (!ReferenceEquals(Display.RenderTarget, target)) Display.RenderTarget = target; Display.Viewport = viewport; Display.ScissorRectangle = scissor; }
    }
    internal static void WithOpaquePreview(Widget view, Action draw) {
        // ScreensManager leaves the outgoing GameScreen's cached global alpha faded.
        var color = view.m_globalColorTransform;
        try { view.m_globalColorTransform = Color.White; draw(); }
        finally { view.m_globalColorTransform = color; }
    }
    public ScGunWorldBackground() {
        IsHitTestVisible = false;
        Display.DeviceReset += ResetCapture;
    }
    public void ResetCapture() => m_capture.Reset();
    public void ReleaseCapture() {
        m_capture.Reset(); m_worldTarget?.Dispose(); m_worldTarget = null;
    }
    public override void MeasureOverride(Vector2 availableSize) { IsDrawRequired = true; }
    void Capture(ViewWidget[] views, int width, int height) {
        float oldView = SettingsManager.ViewAngle, oldSensitivity = SettingsManager.LookSensitivity;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try {
            m_worldTarget?.Dispose(); m_worldTarget = null;
            m_worldTarget = new RenderTarget2D(width, height, 1, ColorFormat.Rgba8888, DepthFormat.Depth24Stencil8);
            WithPreservedDisplay(() => {
                Display.RenderTarget = m_worldTarget;
                Display.Viewport = new Viewport(0, 0, width, height);
                Display.ScissorRectangle = new Rectangle(0, 0, width, height);
                Display.Clear(new Color(28, 36, 42), 1f, 0);
                // Reuse the last game layout, without measuring/arranging the live screen again.
                foreach (var view in views) {
                    var gui = view.GameWidget.GuiWidget; bool drawGui = gui.IsDrawEnabled;
                    try { WithOpaquePreview(view, () => view.Draw(m_worldContext)); }
                    finally { gui.IsDrawEnabled = drawGui; }
                }
                m_worldContext.PrimitivesRenderer3D.Flush(Matrix.Identity);
                m_worldContext.PrimitivesRenderer2D.Flush();
            });
            KnifeLog.Trace($"[CS_BACKGROUND_04112] captured {width}x{height}, views={views.Length}, ms={timer.Elapsed.TotalMilliseconds:0.0}; frozen until reopen/resize/reset");
        }
        finally {
            SettingsManager.ViewAngle = oldView; SettingsManager.LookSensitivity = oldSensitivity;
            foreach (var view in views) view.GameWidget.ActiveCamera.PrepareForDrawing(null);
            m_worldContext.PrimitivesRenderer2D.Clear(); m_worldContext.PrimitivesRenderer3D.Clear();
        }
    }
    void DrawFallback(DrawContext dc) {
        var batch = dc.PrimitivesRenderer2D.FlatBatch(); int first = batch.TriangleVertices.Count;
        batch.QueueQuad(Vector2.Zero, ActualSize, 0, new Color(28, 36, 42) * GlobalColorTransform);
        batch.TransformTriangles(GlobalTransform, first);
    }
    public override void Draw(DrawContext dc) {
        var project = GameManager.Project;
        var game = ScreensManager.FindScreen<Screen>("Game");
        if (project is null || game is null) {
            ReleaseCapture(); DrawFallback(dc); return;
        }
        dc.PrimitivesRenderer3D.Flush(Matrix.Identity);
        dc.PrimitivesRenderer2D.Flush();
        int width = Math.Max(1, Display.Viewport.Width), height = Math.Max(1, Display.Viewport.Height);
        try {
            bool ready = m_capture.Ensure(project, width, height, () => {
                var views = game.AllChildren.OfType<ViewWidget>().Where(v => v.ActualSize.X > 0 && v.ActualSize.Y > 0
                    && v.GameWidget?.PlayerData?.ComponentPlayer is not null && v.GameWidget.PlayerData.IsReadyForPlaying).ToArray();
                if (views.Length == 0) throw new InvalidOperationException("No ready game view for settings background.");
                Capture(views, width, height);
            });
            if (!ready) { DrawFallback(dc); return; }
            var batch = dc.PrimitivesRenderer2D.TexturedBatch(m_worldTarget, false, 0,
                DepthStencilState.None, RasterizerState.CullNoneScissor, BlendState.AlphaBlend, SamplerState.LinearClamp);
            int first = batch.TriangleVertices.Count;
            // Respect the settings screen's own transition fade instead of flashing an opaque quad.
            batch.QueueQuad(Vector2.Zero, ActualSize, 0, Vector2.Zero, Vector2.One, GlobalColorTransform);
            batch.TransformTriangles(GlobalTransform, first);
            dc.PrimitivesRenderer2D.Flush();
        }
        catch (Exception e) {
            KnifeLog.Warning("[CS_BACKGROUND_04112] capture failed; stable fallback until reopen/resize/reset: " + e);
            DrawFallback(dc);
        }
    }
    public override void Dispose() {
        Display.DeviceReset -= ResetCapture;
        ReleaseCapture(); base.Dispose();
    }
}
