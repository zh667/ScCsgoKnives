using Engine;
using TemplatesDatabase;
namespace Game;

/// <summary>C4 has its own fuse state and never enters GunRegistry.</summary>
public sealed class ScC4Charge {
    public const float PlantSeconds = 3.2f, DefaultFuse = 20, DefaultDamage = 5000, DefaultRadius = 32;
    public int Owner;
    public Vector3 Position;
    public float Yaw, Remaining = DefaultFuse, Fuse = DefaultFuse, Power = DefaultDamage, Radius = DefaultRadius;
    public float BeepLeft;
    public bool WarningPlayed, TriggerPlayed;
    public static float DamageAt(float distance, float power, float radius) =>
        !float.IsFinite(distance + power + radius) || distance < 0 || radius <= 0 || power <= 0 || distance >= radius ? 0 : power * MathF.Pow(1 - distance / radius, 2);
    public bool Tick(float dt) {
        if (!float.IsFinite(dt) || dt <= 0 || Remaining <= 0) return false;
        Remaining = Math.Max(0, Remaining - dt); BeepLeft -= dt;
        return Remaining == 0;
    }
    public ValuesDictionary Save() {
        var d = new ValuesDictionary(); d.SetValue("Owner", Owner); d.SetValue("Position", Position); d.SetValue("Yaw", Yaw);
        d.SetValue("Remaining", Remaining); d.SetValue("Fuse", Fuse); d.SetValue("Power", Power); d.SetValue("Radius", Radius); return d;
    }
    public static ScC4Charge Load(ValuesDictionary d) {
        var c = new ScC4Charge { Owner = d.GetValue<int>("Owner"), Position = d.GetValue<Vector3>("Position"), Yaw = d.GetValue<float>("Yaw"),
            Remaining = d.GetValue<float>("Remaining"), Fuse = d.GetValue<float>("Fuse"), Power = d.GetValue<float>("Power"), Radius = d.GetValue<float>("Radius") };
        if (!float.IsFinite(c.Position.X + c.Position.Y + c.Position.Z + c.Yaw + c.Remaining + c.Fuse + c.Power + c.Radius)
            || c.Fuse < 1 || c.Fuse > 300 || c.Remaining < 0 || c.Remaining > c.Fuse || c.Power < 1 || c.Power > 10000 || c.Radius < 1 || c.Radius > 64)
            throw new InvalidOperationException("Invalid saved C4 state; refusing to reset its fuse.");
        return c;
    }
}
