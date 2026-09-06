namespace Game;

/// <summary>Input release, not a timer, authorizes a throw. No live grenade exists here.</summary>
public sealed class ScGrenadePreparation(double startedAt, float pullSeconds, float releaseSeconds, float throwSeconds) {
    public double StartedAt { get; } = startedAt;
    public double ReadyAt { get; } = startedAt + pullSeconds;
    public double ThrowStartedAt { get; private set; } = double.PositiveInfinity;
    public bool ReleaseRequested { get; private set; }
    public bool Throwing => double.IsFinite(ThrowStartedAt);
    public double ReleaseAt => ThrowStartedAt + releaseSeconds;
    public double EndAt => ThrowStartedAt + throwSeconds;
    public void Step(double now, bool pressed) {
        if (!pressed) ReleaseRequested = true;
        if (!Throwing && ReleaseRequested && now >= ReadyAt) ThrowStartedAt = now;
    }
    public int Stage(double now) => Throwing ? 2 : now < ReadyAt ? 0 : 1;
    public float Elapsed(double now) => (float)Math.Max(0, now - (Throwing ? ThrowStartedAt : now < ReadyAt ? StartedAt : ReadyAt));
}
