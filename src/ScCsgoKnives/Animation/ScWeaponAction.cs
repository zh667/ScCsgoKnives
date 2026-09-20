namespace Game;

// Presentation only. No ammunition, inventory mutation, or damage is performed by a reader.
// The owner and sequence identify an action; clocks are supplied by that entity's gameplay.
public enum ScWeaponActionKind { Idle, Draw, Inspect, Slash, Shoot, Reload, Attach, Detach, Prepare, Grenade }
public readonly record struct ScWeaponAction(string Asset, ScWeaponActionKind Kind, string Clip, long Sequence,
    float Elapsed, float Duration, float ClipTime, bool LoopedReload=false) {
    public float Progress => Duration > 0 ? Math.Clamp(Elapsed / Duration, 0, 1) : 0;
    public bool Active => Asset != null && Kind != ScWeaponActionKind.Idle && Duration > 0 && Elapsed < Duration;
    public float Weight => Active ? Math.Clamp(Math.Min(Elapsed / .10f, (Duration - Elapsed) / .12f), 0, 1) : 0;
}

public sealed class ScWeaponActionTimeline {
    string asset, clip;
    ScWeaponActionKind kind;
    double started;
    float duration;
    long sequence;
    public void Start(string weapon, ScWeaponActionKind action, string alias, double now, float seconds) {
        asset=weapon; kind=action; clip=alias; started=now; duration=Math.Max(0,seconds); sequence++;
    }
    public void Clear() { asset=null; kind=ScWeaponActionKind.Idle; duration=0; sequence++; }
    public ScWeaponAction Read(double now) {
        float elapsed=Math.Max(0,(float)(now-started));
        return new(asset,elapsed<duration?kind:ScWeaponActionKind.Idle,clip,sequence,elapsed,duration,elapsed);
    }
}
