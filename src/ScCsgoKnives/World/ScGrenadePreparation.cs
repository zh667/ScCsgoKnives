namespace Game;

/// <summary>Input release, not a timer, authorizes a throw. No live grenade exists here.</summary>
public sealed class ScGrenadePreparation(double startedAt, float pullSeconds, float releaseSeconds, float throwSeconds, bool autoRelease = false) {
    // Preserve the reflection-bound four-argument constructor used by older package diagnostics.
    public ScGrenadePreparation(double startedAt, float pullSeconds, float releaseSeconds, float throwSeconds)
        : this(startedAt, pullSeconds, releaseSeconds, throwSeconds, false) { }
    /// <summary>
    /// Quick throw keeps the authored pull/hold animation at its original speed. It only changes the
    /// release policy: once the preparation clip reaches its end, the projectile is released immediately.
    /// The previous implementation shortened pullpin to 0.15/0.20 seconds, which made the hand jump
    /// between unrelated clip frames and looked like a broken animation.
    /// </summary>
    public static ScGrenadePreparation Create(double now,float pull,float release,float duration,bool quick,bool molotov,double deployDelay=0) =>
        quick?new(now,pull,release,duration,true):new(now,pull,release,duration);
    public double StartedAt { get; } = startedAt;
    public double ReadyAt { get; } = startedAt + pullSeconds;
    public double ThrowStartedAt { get; private set; } = double.PositiveInfinity;
    public bool ReleaseRequested { get; private set; }
    public bool Throwing => double.IsFinite(ThrowStartedAt);
    public double ReleaseAt => ThrowStartedAt + releaseSeconds;
    public double EndAt => ThrowStartedAt + throwSeconds;
    public void Step(double now, bool pressed) {
        if (!pressed) ReleaseRequested = true;
        if (autoRelease && now >= ReadyAt) ReleaseRequested = true;
        if (!Throwing && ReleaseRequested && now >= ReadyAt) ThrowStartedAt = now;
    }
    public int Stage(double now) => Throwing ? 2 : now < ReadyAt ? 0 : 1;
    public float Elapsed(double now) => (float)Math.Max(0, now - (Throwing ? ThrowStartedAt : now < ReadyAt ? StartedAt : ReadyAt));
    public float ClipElapsed(double now,float originalPull,float originalRelease,float originalThrow) {
        float elapsed=Elapsed(now);
        if(!Throwing)return Stage(now)==0?(float)Math.Clamp((now-StartedAt)/Math.Max(.00001,ReadyAt-StartedAt),0,1)*originalPull:elapsed;
        if(elapsed<=releaseSeconds&&releaseSeconds>0)return elapsed/releaseSeconds*originalRelease;
        return originalRelease+(elapsed-releaseSeconds)/Math.Max(.00001f,throwSeconds-releaseSeconds)*(originalThrow-originalRelease);
    }
}
