using Engine;
namespace Game;

/// <summary>Per-player scope projection/input. Never write global graphics/control settings:
/// those settings are saved by vanilla while paused and on application deactivation.</summary>
public sealed class ScScopeCamera(SubsystemScGunBlockBehavior guns, SubsystemPlayers players) : IUpdateable {
    // After all input sources (mouse/gamepad/touch), before ComponentPlayer consumes the result.
    public UpdateOrder UpdateOrder => (UpdateOrder)(-9);
    public void Update(float dt) {
        foreach (var p in players.ComponentPlayers) {
            if (!ScGunBindings.Available(p)) { guns.SuspendScope(p); continue; }
            float zoom = guns.ScopeMagnification(p);
            if (zoom > 1f) {
                p.ComponentInput.m_playerInput.Look /= zoom;
                p.ComponentInput.m_playerInput.CameraLook /= zoom;
            }
        }
    }
    public static Matrix ZoomProjection(Matrix projection, float magnification) {
        if (!float.IsFinite(magnification) || magnification <= 1f || !float.IsFinite(projection.M22) || projection.M22 <= 0) return projection;
        float halfFov = MathF.Atan(1f / projection.M22);
        float scale = MathF.Tan(halfFov) / MathF.Tan(halfFov / magnification);
        projection.M11 *= scale; projection.M22 *= scale;
        return projection;
    }
}
