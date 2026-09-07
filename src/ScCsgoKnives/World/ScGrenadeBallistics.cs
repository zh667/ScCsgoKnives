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
    public const float FuseSeconds = 1.5f, FireFuseSeconds = 2f; // unchanged
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
    /// <summary>F02 (user, 2026-09-07): once a throw has finished, go back to the slot the player held
    /// before the grenade slot, whatever it holds now; -1 (stay) when there is no such slot.</summary>
    public static int FollowUpSlot(int slotsCount, int thrownSlot, int previousSlot)
        => previousSlot >= 0 && previousSlot < slotsCount && previousSlot != thrownSlot ? previousSlot : -1;
}
