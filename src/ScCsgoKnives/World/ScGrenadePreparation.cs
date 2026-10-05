namespace Game;

/// <summary>The timeline of one throw. Input release, not a timer, authorizes it; no live grenade exists here.
/// The rule is CS2's own (quick-throw-20261002; its viewmodel_grenade graph enters the Throw state from any state with no
/// blend, and its weapon data has no tap/hold threshold): pressing starts the pull, letting go throws - at once, wherever
/// the pull has got to. Held through the pull, the grenade stays held ready until the release, however long that is.
/// A throw asked for while the grenade was still being drawn starts no earlier than the draw would have ended
/// (<see cref="EarliestThrowAt"/>), and never later than a full pull.</summary>
public sealed class ScGrenadePreparation(double startedAt, float pullSeconds, float releaseSeconds, float throwSeconds) {
    /// <param name="deployDelay">Seconds of the draw still to run when the throw was asked for (0: the grenade was in hand).</param>
    public static ScGrenadePreparation Create(double now,float pull,float release,float duration,bool quick,bool molotov,double deployDelay=0) =>
        new(now,pull,release,duration) { EarliestThrowAt = now + Math.Clamp(deployDelay,0,pull) };
    public double StartedAt { get; } = startedAt;
    public double ReadyAt { get; } = startedAt + pullSeconds;
    /// <summary>The release is acted on from this moment (the draw gate); the start of the pull unless set by <see cref="Create"/>.</summary>
    public double EarliestThrowAt { get; private init; } = startedAt;
    public double ThrowStartedAt { get; private set; } = double.PositiveInfinity;
    public bool ReleaseRequested { get; private set; }
    public bool Throwing => double.IsFinite(ThrowStartedAt);
    /// <summary>The throw began before the pull was through: a quick throw (the pull clip is cut, as CS2 cuts it).</summary>
    public bool Quick => Throwing && ThrowStartedAt < ReadyAt;
    public double ReleaseAt => ThrowStartedAt + releaseSeconds;
    public double EndAt => ThrowStartedAt + throwSeconds;
    public void Step(double now, bool pressed) {
        if (!pressed) ReleaseRequested = true;
        if (!Throwing && ReleaseRequested && now >= EarliestThrowAt) ThrowStartedAt = now;
    }
    public int Stage(double now) => Throwing ? 2 : now < ReadyAt ? 0 : 1;
    /// <summary>0..1: how far the pull has got; it stops where a quick throw cut it, and is 1 from a full pull on.</summary>
    public float Pulled(double now) => (float)Math.Clamp((Math.Min(now, ThrowStartedAt) - StartedAt) / Math.Max(1e-5, ReadyAt - StartedAt), 0, 1);
    public float Elapsed(double now) => (float)Math.Max(0, now - (Throwing ? ThrowStartedAt : now < ReadyAt ? StartedAt : ReadyAt));
    public float ClipElapsed(double now,float originalPull,float originalRelease,float originalThrow) {
        float elapsed=Elapsed(now);
        if(!Throwing)return Stage(now)==0?(float)Math.Clamp((now-StartedAt)/Math.Max(.00001,ReadyAt-StartedAt),0,1)*originalPull:elapsed;
        if(elapsed<=releaseSeconds&&releaseSeconds>0)return elapsed/releaseSeconds*originalRelease;
        return originalRelease+(elapsed-releaseSeconds)/Math.Max(.00001f,throwSeconds-releaseSeconds)*(originalThrow-originalRelease);
    }
}
