using System.IO;
using System.Reflection;
using System.Text.Json;
using Engine;

namespace Game;

/// <summary>
/// CS2's gameplay numbers, from AnimationData/cs2_weapons.json (tools/cs2_weapons.py,
/// out of weapon_&lt;gun&gt;.vdata).
///
/// Damage in Survivalcraft attack power is CS damage unchanged: a Survivalcraft
/// player has 1.0 health and CS damage 36 costs 0.36 of it, the same fraction a
/// 36-damage hit costs a 100-health CS player. Falloff follows CS's
/// damage * RangeModifier^(units/500); the "/500" is community documentation of the
/// formula (counterstrike.fandom.com/wiki/Damage_dropoff and the Steam guide
/// 2599082552 agree), not the SDK source, and is recorded as such.
///
/// Two numbers here are not literal readings, and cs2_weapons.json says so:
/// spread is atan(m_flSpread + m_flInaccuracy*) - a reading of what those
/// dimensionless perturbations mean - and the recoil scale is anchored to the AK's
/// previously hand-fitted 1.6 degrees, so only the RATIOS between guns are CS2's.
/// </summary>
public static class Cs2Weapons {
    public sealed class Gun {
        public float Damage { get; set; }
        public float HeadshotMultiplier { get; set; }
        public float ArmorRatio { get; set; }
        public float Penetration { get; set; }
        public int Magazine { get; set; }
        public int ReserveClips { get; set; }
        public float CycleSeconds { get; set; }
        public bool FullAuto { get; set; }
        public float RangeUnits { get; set; }
        public float RangeModifier { get; set; }
        public float[] MaxSpeed { get; set; }
        public float?[] ZoomFov { get; set; }
        public float RecoveryTimeStand { get; set; }
        public float SpreadDegrees { get; set; }
        public float SpreadDegreesAlternate { get; set; }
        public float MoveSpreadDegrees { get; set; }
        public float KickPitchDegrees { get; set; }
        public float KickPitchDegreesAlternate { get; set; }
        public float KickYawDegrees { get; set; }
        public float KickRecoverPerSecond { get; set; }
    }

    sealed class WeaponsFile {
        public string Format { get; set; }
        public float UnitsPerMetre { get; set; }
        public float FalloffUnits { get; set; }
        public Dictionary<string, Gun> Guns { get; set; }
    }

    const string Resource = "AnimationData.cs2_weapons.json";
    const string ExpectedFormat = "ScCsgoKnives.Cs2Weapons/1";

    static readonly WeaponsFile s_file = Load();

    /// <summary>Gameplay numbers follow GunNumbers, not GunProfile: the look and the feel switch separately.</summary>
    public static bool Active => KnifeTuning.GunNumbers >= 0.5f && s_file?.Guns is { Count: > 0 };

    public static Gun Get(string gun) =>
        gun is not null && s_file?.Guns is not null && s_file.Guns.TryGetValue(gun, out Gun g) ? g : null;

    /// <summary>
    /// Damage after CS's range falloff, for a shot that travelled `metres`.
    /// Returns the fallback unchanged when the cs2 profile is off or the gun is unknown.
    /// </summary>
    public static float DamageAt(string gun, float metres, float fallback) {
        Gun g = Active ? Get(gun) : null;
        if (g is null || g.RangeModifier <= 0f || g.RangeModifier >= 1f) return g?.Damage ?? fallback;
        float units = MathUtils.Max(metres, 0f) * s_file.UnitsPerMetre;
        return g.Damage * MathF.Pow(g.RangeModifier, units / s_file.FalloffUnits);
    }

    /// <summary>
    /// Cone half-angle in degrees for this shot. CS raises inaccuracy continuously with
    /// speed rather than at a threshold, so the standing and moving values from the
    /// vdata are interpolated by the player's speed as a fraction of the gun's own
    /// m_flMaxSpeed. Both endpoints are read; the linear blend between them is this
    /// port's choice, since CS's own curve is in code the export does not carry.
    /// </summary>
    public static float SpreadDegrees(string gun, bool alternate, float metresPerSecond, float fallback) {
        Gun g = Active ? Get(gun) : null;
        if (g is null) return fallback;
        float standing = alternate ? g.SpreadDegreesAlternate : g.SpreadDegrees;
        float max = g.MaxSpeed is { Length: > 0 } ? g.MaxSpeed[0] / s_file.UnitsPerMetre : 0f;
        if (max <= 0.01f) return standing;
        float f = MathUtils.Saturate(MathUtils.Max(metresPerSecond, 0f) / max);
        return MathUtils.Lerp(standing, g.MoveSpreadDegrees, f);
    }

    public static (float Pitch, float Yaw, float Recover) Kick(string gun, bool alternate,
        float pitch, float yaw, float recover) {
        Gun g = Active ? Get(gun) : null;
        if (g is null) return (pitch, yaw, recover);
        return (alternate ? g.KickPitchDegreesAlternate : g.KickPitchDegrees,
                g.KickYawDegrees, g.KickRecoverPerSecond);
    }

    static WeaponsFile Load() {
        try {
            Assembly assembly = typeof(Cs2Weapons).Assembly;
            string name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(Resource, StringComparison.OrdinalIgnoreCase));
            if (name is null) {
                KnifeDiagnostics.WarnOnce("cs2-weapons-missing", $"No embedded {Resource}.");
                return null;
            }
            using Stream stream = assembly.GetManifestResourceStream(name);
            WeaponsFile file = JsonSerializer.Deserialize<WeaponsFile>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (file?.Format != ExpectedFormat || file.Guns is null) {
                KnifeDiagnostics.WarnOnce("cs2-weapons-format", $"{Resource} is not {ExpectedFormat}.");
                return null;
            }
            KnifeLog.Information("[ScCsgoKnives] CS2 weapon data: " + string.Join("; ", file.Guns.Select(kv =>
                $"{kv.Key} dmg={kv.Value.Damage:0.#} falloff={kv.Value.RangeModifier:0.##}/500u "
                + $"spread={kv.Value.SpreadDegrees:0.###}deg kick={kv.Value.KickPitchDegrees:0.###}deg")));
            return file;
        }
        catch (Exception e) {
            KnifeDiagnostics.WarnOnce("cs2-weapons-load", $"Could not read {Resource}: {e.Message}");
            return null;
        }
    }
}
