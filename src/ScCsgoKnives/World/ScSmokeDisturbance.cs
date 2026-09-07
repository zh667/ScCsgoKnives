using Engine;
using TemplatesDatabase;
namespace Game;

/// <summary>A temporary opening blown into smoke by a high-explosive grenade (F06, 0.31.0). Fully open for
/// <see cref="Hold"/> seconds, then refills linearly over <see cref="Recovery"/>; the same value feeds the
/// smoke sprites, the inside overlay and the AI sight query, so what the player sees is what the AI sees.
/// Only the HE creates one; flash and decoy never do. Every number below is an estimate (估计).</summary>
public sealed class ScSmokeDisturbance {
    public const float Radius = 3f, Hold = 1.5f, Recovery = 2f, Total = Hold + Recovery, Rim = .5f;
    public const int MaxActive = 16;
    public Vector3 Center;
    public float Remaining = Total;
    public bool Active => Remaining > 0;
    /// <summary>0 = smoke intact, 1 = fully cleared at this point.</summary>
    public float Clearing(Vector3 point) {
        if (Remaining <= 0) return 0;
        float distance = Vector3.Distance(point, Center);
        if (distance >= Radius) return 0;
        float rim = Math.Clamp((Radius - distance) / Rim, 0, 1);
        float time = Remaining >= Recovery ? 1 : Remaining / Recovery;
        return rim * time;
    }
    public static float Clearing(IEnumerable<ScSmokeDisturbance> list, Vector3 point) {
        float best = 0;
        if (list is not null) foreach (var d in list) best = Math.Max(best, d.Clearing(point));
        return best;
    }
    public static bool CanAdd(IEnumerable<ScSmokeDisturbance> list) => list.Count() < MaxActive;
    public ValuesDictionary Save() { var d = new ValuesDictionary(); d.SetValue("Center", Center); d.SetValue("Remaining", Remaining); return d; }
    public static ScSmokeDisturbance Load(ValuesDictionary d) {
        var s = new ScSmokeDisturbance { Center = d.GetValue<Vector3>("Center"), Remaining = d.GetValue<float>("Remaining", 0) };
        if (!ScGrenadeState.Finite(s.Center) || !float.IsFinite(s.Remaining) || s.Remaining <= 0 || s.Remaining > Total) return null;
        return s;
    }
}
