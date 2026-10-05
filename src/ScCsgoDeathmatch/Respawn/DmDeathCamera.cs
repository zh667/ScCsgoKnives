using Engine;
namespace Game;

/// <summary>The death view (DM-05, design §8.1): for about two seconds the dead player's screen stays where the life
/// ended and turns to whoever ended it; with no killer it stays as it was. The player's body is already at the
/// preparation place by then - this camera is the only thing left at the scene - and the view returns to the player's
/// own camera when the next life begins or the time is up. A local presentation: it decides nothing.</summary>
public sealed class DmDeathCamera(GameWidget gameWidget) : BasePerspectiveCamera(gameWidget) {
    Vector3 m_eye, m_direction; Vector3? m_target; double m_until; Camera m_former;
    public override bool UsesMovementControls => false;
    public override bool IsEntityControlEnabled => false;
    public override void Activate(Camera previousCamera) {
        if (previousCamera is not DmDeathCamera) m_former = previousCamera;
        m_direction = previousCamera?.ViewDirection ?? Vector3.UnitX;
        SetupPerspectiveCamera(m_eye, m_direction, Vector3.UnitY);
    }
    public override void Update(float dt) {
        if (m_target is { } target && Vector3.DistanceSquared(target, m_eye) > .01f) {
            Vector3 wanted = Vector3.Normalize(target - m_eye);
            m_direction = Vector3.Normalize(Vector3.Lerp(m_direction, wanted, Math.Clamp(6 * dt, 0, 1)));
        }
        SetupPerspectiveCamera(m_eye, m_direction, Vector3.UnitY);
        if (Time.RealTime >= m_until) Leave();
    }
    void Leave() {
        if (!ReferenceEquals(GameWidget.ActiveCamera, this)) return;
        GameWidget.ActiveCamera = m_former is not null && GameWidget.Cameras.Contains(m_former) ? m_former : GameWidget.FindCamera<FppCamera>();
    }
    static DmDeathCamera Of(GameWidget widget) {
        if (widget.FindCamera<DmDeathCamera>(false) is { } present) return present;
        var camera = new DmDeathCamera(widget); widget.AddCamera(camera, _ => false);   // never offered in the camera list
        return camera;
    }
    public static void Show(ComponentPlayer player, Vector3 eye, Vector3? killerEye, double seconds) {
        if (player?.GameWidget is not { } widget) return;
        try {
            var camera = Of(widget);
            camera.m_eye = eye; camera.m_target = killerEye; camera.m_until = Time.RealTime + seconds;
            widget.ActiveCamera = camera;
        }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("dm-death-camera", "[CS_DM] death view not shown: " + e.Message); }
    }
    public static void End(ComponentPlayer player) {
        if (player?.GameWidget is { } widget && widget.ActiveCamera is DmDeathCamera camera) camera.Leave();
    }
    public static bool Active(ComponentPlayer player) => player?.GameWidget?.ActiveCamera is DmDeathCamera;
}
