using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        [JsonPropertyName("Damage")]
        public float Damage { get; set; }
        [JsonPropertyName("HeadshotMultiplier")]
        public float HeadshotMultiplier { get; set; }
        [JsonPropertyName("ArmorRatio")]
        public float ArmorRatio { get; set; }
        [JsonPropertyName("Penetration")]
        public float Penetration { get; set; }
        [JsonPropertyName("Magazine")]
        public int Magazine { get; set; }
        [JsonPropertyName("ReserveClips")]
        public int ReserveClips { get; set; }
        [JsonPropertyName("CycleSeconds")]
        public float CycleSeconds { get; set; }
        [JsonPropertyName("FullAuto")]
        public bool FullAuto { get; set; }
        [JsonPropertyName("RangeUnits")]
        public float RangeUnits { get; set; }
        [JsonPropertyName("RangeModifier")]
        public float RangeModifier { get; set; }
        [JsonPropertyName("MaxSpeed")]
        public float[] MaxSpeed { get; set; }
        /// <summary>Pellets per shot: 1 for everything but the shotguns (Nova 9, MAG-7 and Sawed-Off 8, XM1014 6).</summary>
        [JsonPropertyName("Pellets")]
        public int Pellets { get; set; } = 1;
        /// <summary>Only the Glock-18 and the FAMAS have one.</summary>
        [JsonPropertyName("HasBurstMode")]
        public bool HasBurstMode { get; set; }
        [JsonPropertyName("BurstCycleSeconds")]
        public float? BurstCycleSeconds { get; set; }
        [JsonPropertyName("BurstShotSeconds")]
        public float? BurstShotSeconds { get; set; }
        /// <summary>
        /// CS2 carries the burst's cycle time and the gap between its shots but not how
        /// many it fires. Three is Counter-Strike's burst and the generator writes it as
        /// BurstShotsAssumed, named so the estimate is visible in the data.
        /// </summary>
        [JsonPropertyName("BurstShotsAssumed")]
        public int BurstShotsAssumed { get; set; }
        /// <summary>WEAPONSILENCER_NONE, _DETACHABLE (M4A1-S, USP-S) or _INTEGRATED (MP5-SD).</summary>
        [JsonPropertyName("SilencerType")]
        public string SilencerType { get; set; }
        /// <summary>CS2's own sound events, by WEAPON_SOUND_* key.</summary>
        [JsonPropertyName("ShootSounds")]
        public Dictionary<string, string> ShootSounds { get; set; }
        [JsonPropertyName("ZoomFov")]
        public float?[] ZoomFov { get; set; }

        public bool SilencerDetachable => SilencerType == "WEAPONSILENCER_DETACHABLE";
        public bool SilencerIntegrated => SilencerType == "WEAPONSILENCER_INTEGRATED";
        /// <summary>The event CS2 plays for a shot, silenced or not; null when unknown.</summary>
        public string ShootSound(bool silenced) {
            if (ShootSounds is null) return null;
            if (silenced && ShootSounds.TryGetValue("WEAPON_SOUND_SPECIAL1", out string special)) return special;
            return ShootSounds.TryGetValue("WEAPON_SOUND_SINGLE", out string single) ? single : null;
        }
        [JsonPropertyName("ZoomLevels")]
        public int ZoomLevels { get; set; }
        /// <summary>
        /// CS2's m_flZoomTime per level: how long it interpolates to each zoomed FOV.
        /// The AWP's are all 0.05, three frames at 60 fps, which is why its scope
        /// reads as instant. 0.17.1 held the lens overlay back for a 0.25 s aim blend
        /// while the world FOV changed on the key frame.
        /// </summary>
        [JsonPropertyName("ZoomSeconds")]
        public float?[] ZoomSeconds { get; set; }
        [JsonPropertyName("HideViewModelWhenZoomed")]
        public bool HideViewModelWhenZoomed { get; set; }
        [JsonPropertyName("RecoveryTimeStand")]
        public float RecoveryTimeStand { get; set; }
        [JsonPropertyName("SpreadDegrees")]
        public float SpreadDegrees { get; set; }
        [JsonPropertyName("SpreadDegreesAlternate")]
        public float SpreadDegreesAlternate { get; set; }
        [JsonPropertyName("MoveSpreadDegrees")]
        public float MoveSpreadDegrees { get; set; }
        [JsonPropertyName("KickPitchDegrees")]
        public float KickPitchDegrees { get; set; }
        [JsonPropertyName("KickPitchDegreesAlternate")]
        public float KickPitchDegreesAlternate { get; set; }
        [JsonPropertyName("KickYawDegrees")]
        public float KickYawDegrees { get; set; }
        [JsonPropertyName("KickRecoverPerSecond")]
        public float KickRecoverPerSecond { get; set; }
    }

    sealed class WeaponsFile {
        [JsonPropertyName("Format")]
        public string Format { get; set; }
        [JsonPropertyName("UnitsPerMetre")]
        public float UnitsPerMetre { get; set; }
        [JsonPropertyName("FalloffUnits")]
        public float FalloffUnits { get; set; }
        [JsonPropertyName("Guns")]
        public Dictionary<string, Gun> Guns { get; set; }
    }

    const string Resource = "AnimationData.cs2_weapons.json";
    const string ExpectedFormat = "ScCsgoKnives.Cs2Weapons/1";

    /// <summary>Null when the file loaded; the reason otherwise. See Cs2Effects.LoadError.</summary>
    public static string LoadError { get; private set; } = "not loaded";

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
                LoadError = $"no embedded {Resource}";
                KnifeDiagnostics.WarnOnce("cs2-weapons-missing", $"No embedded {Resource}.");
                return null;
            }
            using Stream stream = assembly.GetManifestResourceStream(name);
            // Pinned by JsonPropertyName; PropertyNameCaseInsensitive would not have
            // caught a separator change, which is how Cs2Effects broke in 0.16.4.
            WeaponsFile file = JsonSerializer.Deserialize<WeaponsFile>(stream);
            if (file?.Format != ExpectedFormat || file.Guns is null) {
                LoadError = $"{Resource} is not {ExpectedFormat}";
                KnifeDiagnostics.WarnOnce("cs2-weapons-format", $"{Resource} is not {ExpectedFormat}.");
                return null;
            }
            KnifeLog.Information("[ScCsgoKnives] CS2 weapon data: " + string.Join("; ", file.Guns.Select(kv =>
                $"{kv.Key} dmg={kv.Value.Damage:0.#} falloff={kv.Value.RangeModifier:0.##}/500u "
                + $"spread={kv.Value.SpreadDegrees:0.###}deg kick={kv.Value.KickPitchDegrees:0.###}deg")));
            LoadError = null;
            return file;
        }
        catch (Exception e) {
            LoadError = $"{e.GetType().Name}: {e.Message}";
            KnifeDiagnostics.WarnOnce("cs2-weapons-load", $"Could not read {Resource}: {e.Message}");
            return null;
        }
    }
}
