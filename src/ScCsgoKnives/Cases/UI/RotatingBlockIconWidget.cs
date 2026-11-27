using Engine;

namespace Game;

/// <summary>Marker environment used by compatible content mods to request a 3D reveal model.</summary>
public sealed class ScCsgoBoxModelPreviewEnvironmentData : DrawBlockEnvironmentData;

public sealed class RotatingBlockIconWidget : BlockIconWidget {
    Vector2? m_lastDrag;
    float m_yaw;
    float m_pitch;
    float m_distance;
    bool m_viewInitialized;

    public bool AutoRotate { get; set; } = true;

    public RotatingBlockIconWidget() {
        DrawBlockEnvironmentData = new ScCsgoBoxModelPreviewEnvironmentData();
        IsHitTestVisible = true;
    }

    public override void Update() {
        EnsureViewInitialized();

        if (Input.Press.HasValue) {
            if (m_lastDrag.HasValue) {
                Vector2 delta = Input.Press.Value - m_lastDrag.Value;
                m_yaw += 0.012f * delta.X;
                m_pitch = MathUtils.Clamp(m_pitch - 0.008f * delta.Y, -1.15f, 1.15f);
                m_lastDrag = Input.Press.Value;
                Input.Clear();
            }
            else if (HitTestGlobal(Input.Press.Value) == this) {
                m_lastDrag = Input.Press.Value;
            }
        }
        else {
            m_lastDrag = null;
            if (AutoRotate) m_yaw += 0.42f * Time.FrameDuration;
        }

        float horizontal = m_distance * MathF.Cos(m_pitch);
        Vector3 eye = new(
            horizontal * MathF.Sin(m_yaw),
            m_distance * MathF.Sin(m_pitch),
            horizontal * MathF.Cos(m_yaw)
        );
        CustomViewMatrix = Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY);
    }

    void EnsureViewInitialized() {
        if (m_viewInitialized) return;

        Vector3 eye = BlocksManager.Blocks[Contents].GetIconViewOffset(Value, DrawBlockEnvironmentData);
        m_distance = MathUtils.Max(eye.Length(), 1f);
        m_yaw = MathF.Atan2(eye.X, eye.Z);
        m_pitch = MathF.Asin(MathUtils.Clamp(eye.Y / m_distance, -1f, 1f));
        m_viewInitialized = true;
    }
}
