using Engine;
using TemplatesDatabase;
namespace Game;

/// <summary>Saved flight/effect state. Remaining time is never inferred from owner or inventory.</summary>
public sealed class ScGrenadeState {
    public int Kind, Owner;
    /// <summary>World-unique per grenade (assigned by the subsystem, saved) so an HE opening can name the smokes it reached.</summary>
    public int Id;
    public Vector3 Position, Velocity;
    public float Remaining = 1.5f, Age;
    /// <summary>Seconds spent grounded on support; cleared whenever the grenade leaves the ground (F04).</summary>
    public float Rested;
    public float NextBounceSound;
    public bool Effect, Grounded;
    public static bool CanAdd(IEnumerable<ScGrenadeState> states, int owner) => states.Count() < 16 && states.Count(s => s.Owner == owner) < 4;
    public static float HePower(float distance) => 24 * Math.Clamp(1 - distance / 4, 0, 1);
    public static float FlashDuration(float distance, float facing) => 2 * Math.Clamp(1 - distance / 16, 0, 1) * (.15f + .85f * Math.Clamp((facing + .2f) / 1.2f, 0, 1));
    public ValuesDictionary Save() {
        var d = new ValuesDictionary(); d.SetValue("Kind", Kind); d.SetValue("Owner", Owner); d.SetValue("Id", Id); d.SetValue("Position", Position);
        d.SetValue("Velocity", Velocity); d.SetValue("Remaining", Remaining); d.SetValue("Age", Age);
        d.SetValue("Effect", Effect); d.SetValue("Grounded", Grounded); d.SetValue("Rested", Rested); return d;
    }
    public static ScGrenadeState Load(ValuesDictionary d) {
        var s = new ScGrenadeState { Kind=d.GetValue<int>("Kind"), Owner=d.GetValue<int>("Owner"), Id=d.GetValue<int>("Id",0), Position=d.GetValue<Vector3>("Position"),
            Velocity=d.GetValue<Vector3>("Velocity"), Remaining=d.GetValue<float>("Remaining"), Age=d.GetValue<float>("Age",0),
            Effect=d.GetValue<bool>("Effect",false), Grounded=d.GetValue<bool>("Grounded",false), Rested=d.GetValue<float>("Rested",0) };
        if (s.Kind < 0 || s.Kind >= 6 || !float.IsFinite(s.Remaining) || s.Remaining < 0 || s.Remaining > 30 || !float.IsFinite(s.Age) || !float.IsFinite(s.Rested) || s.Rested < 0
            || !Finite(s.Position) || !Finite(s.Velocity)) return null;
        return s;
    }
    public static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
}
