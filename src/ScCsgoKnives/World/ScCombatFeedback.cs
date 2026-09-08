using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>Short-lived, per-player feedback from confirmed health changes.</summary>
public sealed class ScCombatFeedback {
    public sealed record Kill(string Target, string Weapon, float Distance, double At);
    public readonly List<Kill> Kills = [];
    public const int HeadshotOutcome = 3; // Outcome() yields 0 none, 1 hit, 2 kill; 3 is a confirmed non-lethal head hit
    public static readonly Color HeadshotColor = new(255, 210, 50);
    public double HitAt { get; private set; } = -100;
    public double KillAt { get; private set; } = -100;
    /// <summary>Kind (1 hit, 2 kill, 3 headshot) and time of the newest confirmed event; the marker draws only this one.</summary>
    public int LastKind { get; private set; }
    public double LastAt { get; private set; } = -100;
    static int Rank(int kind) => kind == 2 ? 3 : kind == HeadshotOutcome ? 2 : kind == 1 ? 1 : 0;
    readonly PrimitivesRenderer2D m_renderer = new();
    public static int Outcome(float before, float after) => float.IsFinite(before) && float.IsFinite(after)
        && before > 0 && after < before ? (after <= 0 ? 2 : 1) : 0;
    public static string Clean(string text) {
        string result = new((text ?? "").Where(c => !char.IsControl(c)).Take(36).ToArray());
        return string.IsNullOrWhiteSpace(result) ? "未知" : result;
    }
    public void Record(int outcome, string target, string weapon, float distance, double now) {
        if (outcome <= 0) return;
        HitAt = now;
        // Bodies hit by one trigger pull report at the same instant: kill > headshot > hit. A later
        // event always replaces the marker, so a kill's red hold never hides the next yellow head hit.
        if (now == LastAt) { if (Rank(outcome) > Rank(LastKind)) LastKind = outcome; }
        else { LastKind = outcome; LastAt = now; }
        if (outcome != 2) return;
        KillAt = now;
        Kills.Insert(0, new(Clean(target), Clean(weapon), float.IsFinite(distance) ? Math.Max(0, distance) : 0, now));
        if (Kills.Count > 3) Kills.RemoveAt(3);
    }
    public void Draw(Camera camera, double now) {
        var player = camera.GameWidget.PlayerData.ComponentPlayer;
        if (player is null || player.ComponentHealth.Health <= 0 || player.ComponentGui.ModalPanelWidget is not null
            || DialogsManager.HasDialogs(player.GuiWidget)) return;
        Vector2 size = camera.ViewportSize, center = size * .5f;
        float scale = Math.Clamp(Math.Min(size.X / 960f, size.Y / 540f), .55f, 2.5f);
        bool killed = LastKind == 2;
        float age = (float)(now - LastAt), duration = killed ? .45f : .22f;
        if (LastKind > 0 && age >= 0 && age < duration) {
            var batch = m_renderer.FlatBatch(0, DepthStencilState.None, RasterizerState.CullNoneScissor, BlendState.AlphaBlend);
            Color color = (killed ? new Color(255, 65, 55) : LastKind == HeadshotOutcome ? HeadshotColor : Color.White) * Math.Min(1, (duration - age) / .1f);
            foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 }) {
                Vector2 direction = new(x, y), side = new Vector2(-y, x) * (1.05f * scale);
                Vector2 a = center + direction * (7 * scale), b = center + direction * (13 * scale);
                batch.QueueQuad(a - side, b - side, b + side, a + side, 0, color);
            }
            batch.TransformTriangles(camera.ViewportMatrix); batch.Flush();
        }
        Kills.RemoveAll(k => now - k.At > 3.2);
        // The kill panel is a display switch: turning it off clears what is on screen now and never replays the
        // messages of the time it was off. Counting, growth and damage do not read this flag.
        if (!ScUiSettings.KillFeed) { Kills.Clear(); return; }
        if (Kills.Count == 0) return;
        var font = m_renderer.FontBatch(LabelWidget.BitmapFont, 1, DepthStencilState.None, null, BlendState.AlphaBlend);
        for (int i = 0; i < Kills.Count; i++) {
            Kill k = Kills[i];
            float opacity = Math.Clamp((float)(3.2 - (now - k.At)) / .4f, 0, 1);
            Vector2 p = new(center.X, center.Y + (62 + i * 42) * scale);
            void Text(string text, Vector2 at, Color color, float fontSize) {
                float width = LabelWidget.BitmapFont.MeasureText(text, new Vector2(fontSize * scale), Vector2.Zero).X;
                float fitted = fontSize * scale * Math.Min(1, size.X * .88f / Math.Max(1, width));
                font.QueueText(text, at + new Vector2(scale), 0, Color.Black * opacity, TextAnchor.HorizontalCenter,
                    new Vector2(fitted), Vector2.Zero);
                font.QueueText(text, at, 0, color * opacity, TextAnchor.HorizontalCenter,
                    new Vector2(fitted), Vector2.Zero);
            }
            Text("击杀  " + k.Target, p, new Color(255, 90, 75), .58f);
            Text($"{k.Weapon}  ·  {k.Distance:0.0} 米", p + new Vector2(0, 18 * scale), Color.White, .44f);
        }
        font.TransformTriangles(camera.ViewportMatrix); font.Flush();
    }
}
