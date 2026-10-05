using Engine;
namespace Game;

/// <summary>The character-side ray of a shot or a strike (first-person-eye-shot-20261001, user decisions 2026-10-01).
/// A gun's damage ray always leaves the eye. In first person it runs along the crosshair (the aim: FppCamera sits on the
/// eye, so what the crosshair covers is hit); in every other view (third person, debug, orbit, perspective-view mods such as
/// BumanCamera) along the character's own look, which is where the first-person crosshair would be: switching views moves
/// neither the shot line nor its impact, and a camera's position or turn never moves or bends a shot. Tracers, muzzle
/// flash and the Zeus effect keep leaving the drawn muzzle. A strike leaves the eye along the aim (a free camera's strike
/// along the character's look). History: round 4 (2026-10-01) used the vanilla musket origin (eye + body right 0.3 − up
/// 0.2) along the camera's direction; it missed a CT/T head aimed at by 0.35 m and drew odd third-person lines.</summary>
public static class ScAimRay {
    /// <summary>The vanilla gun origin's offsets from the eye in the body's frame (right, then down): visual fallback only.</summary>
    public const float OriginRight = .3f, OriginDown = .2f;
    public static Vector3 Eye(ComponentPlayer player, Vector3 fallback) =>
        player?.ComponentCreatureModel?.EyePosition ?? player?.ComponentBody?.Position ?? fallback;
    /// <summary>Where a shot's tracer and effects fall back to when no muzzle is drawn: the vanilla musket's ball origin, eye +
    /// body right × 0.3 − body up × 0.2 (the damage ray itself leaves the eye).</summary>
    public static Vector3 GunOrigin(ComponentPlayer player, Vector3 fallback) {
        Vector3 eye = Eye(player, fallback);
        if (player?.ComponentBody is not { } body) return eye;
        Matrix m = body.Matrix;
        return eye + m.Right * OriginRight - m.Up * OriginDown;
    }
    /// <summary>This process's own player views the world through a camera that flies or orbits by itself: the engine's
    /// DebugCamera and OrbitCamera and third-party perspective cameras such as BumanCamera ("生存开透视角"), all with
    /// <c>UsesMovementControls</c> and entity control. 1.9.3.1's ComponentInput then zeroes the character's own move and look
    /// input and gives the mouse to the camera, and builds every aim/hit/dig ray from the camera. Such a camera does not aim
    /// the character's weapon (2026-10-01 user report: in the debug and perspective views the tracer and the impact still
    /// followed the camera's turns).</summary>
    public static bool FreeCamera(ComponentPlayer player) =>
        player is not null && ScNetGuns.RemoteInput(player) is null
        && player.GameWidget?.ActiveCamera is { UsesMovementControls: true, IsEntityControlEnabled: true };
    /// <summary>This process's own player views the world in first person, through the engine's FppCamera (FppCamera.Update:
    /// the view is EyePosition along EyeRotation, so the crosshair is the eye's line).</summary>
    public static bool FirstPerson(ComponentPlayer player) =>
        player?.PlayerData is not null && player.GameWidget?.ActiveCamera is FppCamera { UsesMovementControls: false, IsEntityControlEnabled: true };
    /// <summary>Where the character itself looks (its eye's forward axis).</summary>
    public static Vector3? LookDirection(ComponentPlayer player) =>
        player?.ComponentCreatureModel is { } model ? Matrix.CreateFromQuaternion(model.EyeRotation).Forward : null;
    /// <summary>The shot's ray from the aim, from the eye: a shot in first person along the aim, a shot in any other view
    /// along the character's own look; a strike along the aim, or the character's look under a <see cref="FreeCamera"/>. A
    /// remote client's aim on the server arrives with the direction its own view chose; only the origin is the server's.
    /// An aim without a usable direction is returned as it is.</summary>
    public static Ray3 Resolve(ComponentPlayer player, Ray3 aim, bool melee = false) {
        bool own = player is not null && ScNetGuns.RemoteInput(player) is null;
        bool look = own && (melee ? FreeCamera(player) : !FirstPerson(player));
        Vector3 direction = look && LookDirection(player) is { } character ? character : aim.Direction;
        float length = direction.Length();
        if (!float.IsFinite(length) || length < 1e-6f) return aim;
        return new Ray3(Eye(player, aim.Position), direction / length);
    }
}
