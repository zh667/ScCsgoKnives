using Engine;
namespace Game;

/// <summary>Central throw tunables for community plan items F02/F03/F04 (0.29.0).
/// Each number is either taken from the CS:GO SDK throw code or marked as an estimate
/// calibrated to Survivalcraft's one-metre blocks. Physics and the fuse share <see cref="Step"/>.</summary>
public static class ScGrenadeBallistics {
    // CS:GO SDK, CBaseCSGrenade::ThrowGrenade: vecThrow = forward * flVel + playerVelocity * 1.25f,
    // flVel capped at 750 u/s (about 14 m/s at 1.905 cm per unit). 0.28.x already launched at
    // 14 blocks/s and the user found it slow, so the strong throw rises to 20 (估计, feel-based);
    // the weak throw keeps the SDK strong:weak throw-strength ratio of 1:0.5.
    public const float StrongSpeed = 20f;   // 0.28.x: 14
    public const float WeakSpeed = 10f;     // 0.28.x: 6
    // Lift is added to the view vector before normalising; it is not an angle (0.28.x values kept).
    public const float StrongLift = .18f, WeakLift = .08f;
    public const float PlayerVelocityShare = 1.25f; // 0.28.x: .5; CS:GO SDK factor, sampled once at release
    public const float FuseSeconds = 1.5f, FireFuseSeconds = 3f;
    // F04: smoke/decoy pop only after resting on support for SettleHold (估计). SettleTimeout is
    // the abnormal-case cap for a grenade that never comes to rest, not a normal trigger path.
    public const float SettleHold = .15f;
    public const float SettleTimeout = 12f;
    // One frame simulates at most this much game time; the fuse consumes the same clamped step.
    public const float MaxStep = .5f;

    public static Vector3 Direction(Vector3 view, bool low) => Vector3.Normalize(view + Vector3.UnitY * (low ? WeakLift : StrongLift));
    public static Vector3 LaunchVelocity(Vector3 direction, Vector3 playerVelocity, bool low) => direction * (low ? WeakSpeed : StrongSpeed) + playerVelocity * PlayerVelocityShare;
    public static float Fuse(int kind) => kind is 3 or 4 ? FireFuseSeconds : FuseSeconds;
    public static float Step(float dt) => float.IsFinite(dt) ? Math.Clamp(dt, 0, MaxStep) : 0;
    public static bool Settled(ScGrenadeState s) => s.Grounded && s.Rested >= SettleHold;
    public static bool BodyCollisionAllowed(ScGrenadeState s) => s.Kind != 2 || !s.SmokeBodyBounceUsed;
    public static void BounceFromBody(ScGrenadeState s, Vector3 hitPoint) {
        Vector3 direction=s.Velocity.LengthSquared()>.001f?Vector3.Normalize(s.Velocity):Vector3.UnitY;
        s.Position=hitPoint-direction*.10f;s.Velocity=-s.Velocity*.3f;
        if(s.Kind==2)s.SmokeBodyBounceUsed=true;
    }
    /// <summary>Release state shared by the real throw and the preview: aim-point corrected direction from the view,
    /// the release origin and the launch velocity (player velocity sampled now).</summary>
    public static (Vector3 Position,Vector3 Velocity) Launch(Vector3 viewPosition,Vector3 viewDirection,Vector3 playerVelocity,bool low,Func<Vector3,Vector3,Vector3?> solidHitPoint)
        => LaunchFrom(SubsystemScGrenades.ReleaseOrigin(viewPosition),viewPosition,viewDirection,playerVelocity,low,solidHitPoint);
    /// <summary>As <see cref="Launch"/>, leaving from <paramref name="origin"/> while the aim point is still what the
    /// view ray shows. In first person origin and view position are the same point and this is exactly Launch; a
    /// third-person camera aims along its own ray but the grenade leaves the thrower, not the camera behind them.</summary>
    public static (Vector3 Position,Vector3 Velocity) LaunchFrom(Vector3 origin,Vector3 viewPosition,Vector3 viewDirection,Vector3 playerVelocity,bool low,Func<Vector3,Vector3,Vector3?> solidHitPoint) {
        Vector3 direction=Direction(viewDirection,low);
        Vector3 aimTarget=solidHitPoint(viewPosition,viewPosition+direction*48) ?? viewPosition+direction*48;
        Vector3 pos=origin;
        Vector3 toTarget=aimTarget-pos; if (toTarget.LengthSquared()>.01f) direction=Vector3.Normalize(toTarget);
        return (pos,LaunchVelocity(direction,playerVelocity,low));
    }
    public enum StepEvent { None, Body, Surface }
    /// <summary>One flight sub-step (at most 0.02 s) with no side effects: the caller supplies terrain, water and
    /// body queries and plays the bounce sound when <paramref name="bounceSound"/> is set. The real grenade and the
    /// preview both use this, so they cannot drift apart.</summary>
    public static StepEvent Integrate(ScGrenadeState s,float dt,Func<Vector3,Vector3,TerrainRaycastResult?> solid,Func<Vector3,bool> water,
            Func<Vector3,Vector3,BodyRaycastResult?> bodies,out bool bounceSound) {
        bounceSound=false;
        if (s.Grounded && !solid(s.Position,s.Position-Vector3.UnitY*.15f).HasValue) { s.Grounded=false;s.Rested=0; }
        if (s.Grounded) { s.Rested+=dt;return StepEvent.None; }
        bool inWater=water(s.Position);
        s.Velocity+=Vector3.UnitY*(inWater?-3f:-10f)*dt;
        s.Velocity*=MathF.Exp(-(inWater?3:.08f)*dt);
        Vector3 next=s.Position+s.Velocity*dt;
        var hit=solid(s.Position,next);
        var bodyHit=BodyCollisionAllowed(s)&&bodies is not null?bodies(s.Position,next):null;
        if (bodyHit.HasValue && (!hit.HasValue || bodyHit.Value.Distance<hit.Value.Distance)) {
            BounceFromBody(s,bodyHit.Value.HitPoint());return StepEvent.Body;
        }
        if (!hit.HasValue) { s.Position=next;return StepEvent.None; }
        Vector3 normal=CellFace.FaceToVector3(hit.Value.CellFace.Face);
        s.Position=hit.Value.HitPoint()+normal*.06f;
        if (s.Kind is not (3 or 4) && s.Velocity.LengthSquared()>1 && s.Age>=s.NextBounceSound) { s.NextBounceSound=s.Age+.15f;bounceSound=true; }
        s.Velocity=(s.Velocity-2*Vector3.Dot(s.Velocity,normal)*normal)*.48f;
        if (s.Kind is 3 or 4 && normal.Y>.5f) { s.Grounded=true;s.Velocity=Vector3.Zero; }
        if (normal.Y>.5f && s.Velocity.LengthSquared()<.5f) { s.Grounded=true;s.Velocity=Vector3.Zero; }
        return StepEvent.Surface;
    }
    /// <summary>F02 (user, 2026-09-07): once a throw has finished, go back to the slot the player held
    /// before the grenade slot, whatever it holds now; -1 (stay) when there is no such slot.</summary>
    public static int FollowUpSlot(int slotsCount, int thrownSlot, int previousSlot)
        => previousSlot >= 0 && previousSlot < slotsCount && previousSlot != thrownSlot ? previousSlot : -1;
}
