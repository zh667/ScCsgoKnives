using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>The short spark of a bullet that really struck head protection (current-direction-20260929 §4), rebuilt for
/// this engine from CS2's impact_helmet_headshot (15 cyan-white trails living 0.05–0.3 s, two brighter child sparks of
/// about 0.1 s, one additive glow of about 0.1 s). Source2 particles do not run here, so only the look is taken: colours
/// and lifetimes from the CS2 definitions, sizes and speeds chosen in metres for this engine (not converted Source
/// velocities), the streak and flare images derived from CS2's own sparks/yellowflare textures
/// (Textures/ScCsgoKnives/Hits/helmet_spark: a 2x2 atlas — streak, flare, streak, small flare). Additive, depth-tested
/// (never through walls), one system per shot and target, at most <see cref="MaxActive"/> alive, none farther than
/// <see cref="MaxDistance"/> from every view. The local player's own hit is moved in front of the eye, smaller and without
/// the flare, so it never covers the screen.</summary>
public sealed class ScHelmetSpark : ParticleSystem<ScHelmetSpark.Spark> {
    public const string TexturePath = "Textures/ScCsgoKnives/Hits/helmet_spark";
    public const int MaxActive = 8, Streaks = 12, Children = 2;
    public const float MaxDistance = 64, Gravity = 5.7f;
    public sealed class Spark : Particle { public Vector3 Velocity; public float Age, Life, Length, Width; public Color Tint; public bool Streak; }
    /// <summary>Spark systems alive in <paramref name="particles"/> (counted there, so an unloaded world leaves nothing behind).</summary>
    public static int ActiveIn(SubsystemParticles particles) => particles?.m_particleSystems?.Keys.Count(k => k is ScHelmetSpark) ?? 0;
    readonly Engine.Random m_random = new();
    /// <summary>Adds a spark at <paramref name="point"/> for a bullet travelling along <paramref name="direction"/>; false when
    /// culled (too far, too many alive, texture unavailable).</summary>
    public static bool Emit(GameEntitySystem.Project project, Vector3 point, Vector3 direction, bool ownView) {
        var particles = project?.FindSubsystem<SubsystemParticles>(false);
        if (particles is null || ActiveIn(particles) >= MaxActive || !float.IsFinite(point.X + point.Y + point.Z)) return false;
        if (project.FindSubsystem<SubsystemGameWidgets>(false)?.GameWidgets is { Count: > 0 } views && views.All(w => w.ActiveCamera is null || Vector3.DistanceSquared(w.ActiveCamera.ViewPosition, point) > MaxDistance * MaxDistance)) return false;
        Texture2D texture;
        try { texture = ContentManager.Get<Texture2D>(TexturePath); }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("helmet-spark-texture", "helmet spark texture unavailable: " + e.Message); return false; }
        particles.AddParticleSystem(new ScHelmetSpark(texture, point, direction, ownView));
        return true;
    }
    ScHelmetSpark(Texture2D texture, Vector3 point, Vector3 direction, bool ownView) : base(Streaks + Children + 1) {
        Texture = texture; TextureSlotsCount = 2;
        var incoming = direction.LengthSquared() > 1e-6f ? Vector3.Normalize(direction) : -Vector3.UnitZ;
        // The own hit: in front of the eye toward the shooter, smaller, no flare.
        if (ownView) point -= incoming * .35f;
        float scale = ownView ? .5f : 1;
        for (int i = 0; i < Particles.Length; i++) {
            var p = Particles[i]; p.IsActive = true; p.UseAdditiveBlending = true; p.Age = 0;
            if (i < Streaks + Children) {
                bool child = i >= Streaks;
                // Mostly back toward the shooter and upward, spread wide (CS2: sideways ±, strongly upward).
                var spread = new Vector3(m_random.Float(-1, 1), m_random.Float(.3f, 1.2f), m_random.Float(-1, 1));
                var dir = Vector3.Normalize(-incoming * .8f + spread);
                p.Velocity = dir * (child ? m_random.Float(4, 7) : m_random.Float(2.5f, 6)) * (ownView ? .6f : 1);
                p.Life = child ? m_random.Float(.09f, .1f) : m_random.Float(.05f, .3f);
                // The atlas keeps CS2's narrow streaks (about a fifth of the cell wide): a 0.06 quad shows a ~1.2 cm core.
                p.Length = (child ? .22f : m_random.Float(.08f, .2f)) * scale; p.Width = (child ? .045f : .06f) * scale;
                p.Tint = child ? Lerp(new Color(77, 254, 239), new Color(175, 224, 255), m_random.Float(0, 1)) : Lerp(new Color(103, 255, 242), new Color(82, 216, 255), m_random.Float(0, 1));
                p.Streak = true; p.TextureSlot = child ? 2 : 0; p.BillboardingMode = ParticleBillboardingMode.None;
                p.Position = point + dir * .02f;
            } else {
                p.IsActive = !ownView; p.Streak = false; p.Life = m_random.Float(.1f, .13f); p.Velocity = Vector3.Zero;
                p.Tint = new Color(190, 240, 255); p.TextureSlot = 1; p.BillboardingMode = ParticleBillboardingMode.Camera;
                p.Position = point; p.Size = new Vector2(.12f); p.Rotation = m_random.Float(0, MathF.PI * 2);
            }
            p.Color = p.Tint;
        }
    }
    static Color Lerp(Color a, Color b, float t) => new((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    public override bool Simulate(float dt) {
        dt = Math.Clamp(dt, 0, .1f); bool alive = false;
        foreach (var p in Particles) {
            if (!p.IsActive) continue;
            p.Age += dt;
            if (p.Age >= p.Life) { p.IsActive = false; continue; }
            alive = true; float fade = 1 - p.Age / p.Life;
            if (p.Streak) { p.Velocity.Y -= Gravity * dt; p.Velocity *= MathF.Max(0, 1 - .5f * dt); p.Position += p.Velocity * dt; }
            else p.Size = new Vector2(.12f * (1 - .5f * p.Age / p.Life));
            p.Color = p.Tint * fade;
        }
        return !alive;
    }
    /// <summary>Streaks lie along their velocity and face the camera (Source2 renders trails; here a stretched quad).</summary>
    public override void Draw(Camera camera) {
        foreach (var p in Particles) {
            if (!p.IsActive || !p.Streak) continue;
            var along = p.Velocity.LengthSquared() > 1e-6f ? Vector3.Normalize(p.Velocity) : Vector3.UnitY;
            var toCamera = camera.ViewPosition - p.Position;
            var side = Vector3.Cross(along, toCamera); side = side.LengthSquared() > 1e-8f ? Vector3.Normalize(side) : Vector3.UnitX;
            float length = p.Length * MathF.Min(1, .4f + p.Velocity.Length() / 6);
            p.Up = along * (length / 2); p.Right = side * (p.Width / 2);
        }
        base.Draw(camera);
    }
}
