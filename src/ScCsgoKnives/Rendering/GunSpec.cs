using Engine;

namespace Game;

/// <summary>
/// Gameplay numbers per gun. The CS:MC client carries none of these (the server owns
/// weapon stats), so they start from Counter-Strike 2's published values and are
/// exposed in the tuning file to be replaced by measured MCCS values later.
/// Damage is in Survivalcraft attack power (a musket ball is 80); CS damage 36 on a
/// 100-health player maps to 0.36 of a Survivalcraft player's health.
/// </summary>
public sealed class GunSpec {
    public string Name;
    public int Magazine;
    /// <summary>Seconds between shots (CS2 weapons.vdata: AK-47 and M4A1-S 0.1, AWP 1.455).</summary>
    public float CycleSeconds;
    public bool Automatic;
    /// <summary>Survivalcraft attack power per hit at close range.</summary>
    public float AttackPower;
    /// <summary>Camera kick per shot in degrees (pitch up, yaw scatter) and how fast it settles back.</summary>
    public float KickPitchDegrees, KickYawDegrees, KickRecoverPerSecond;
    /// <summary>Random cone half-angle in degrees for the hit ray.</summary>
    public float SpreadDegrees;
    public bool HasSilencer;
    /// <summary>Scope magnifications; empty for iron sights only.</summary>
    public float[] ZoomLevels = [];
    public string MuzzleBone = "muzzle";
    public string SilencedMuzzleBone = "muzzle2";
    public float RangeBlocks = 64f;
    /// <summary>
    /// Per-gun multiplier on KnifeTuning.PbrGunEnvIntensity, fitted offline (tools/pbr_emulate.py,
    /// 2026-09-04, source2_vmat sets) so each gun's masked first-person brightness matches its MCCS
    /// recording: base 0.25 fits the M4A1-S and AWP, the AK-47's bright steel wants 0.8 of it.
    /// </summary>
    public float EnvScale = 1f;

    public static readonly GunSpec[] All = [
        new() {
            Name = "ak47", Magazine = 30, CycleSeconds = 0.1f, Automatic = true, AttackPower = 36f,
            KickPitchDegrees = 1.6f, KickYawDegrees = 0.5f, KickRecoverPerSecond = 9f, SpreadDegrees = 0.35f,
            EnvScale = 0.8f,
        },
        new() {
            Name = "m4a1s", Magazine = 20, CycleSeconds = 0.1f, Automatic = true, AttackPower = 38f,
            KickPitchDegrees = 1.15f, KickYawDegrees = 0.35f, KickRecoverPerSecond = 9f, SpreadDegrees = 0.3f, HasSilencer = true,
        },
        new() {
            // CS2 weapons.vdata (local extract 2026-09-04): m_flCycleTime 1.455, m_nDamage 115,
            // m_iMaxClip1 5. Scope: m_nZoomFOV1 40, m_nZoomFOV2 10 against CS2's 90 base, i.e.
            // magnifications 90/40 = 2.25 and 90/10 = 9 (not the earlier guessed 4/8).
            Name = "awp", Magazine = 5, CycleSeconds = 1.455f, Automatic = false, AttackPower = 115f,
            KickPitchDegrees = 6f, KickYawDegrees = 1f, KickRecoverPerSecond = 6f, SpreadDegrees = 0.1f,
            ZoomLevels = [2.25f, 9f],
        },
    ];

    public static GunSpec ForAsset(string assetName) => Array.Find(All, spec => spec.Name == assetName);

    // ---- block data layout: variant (2 bits), magazine rounds (6 bits), silencer off (1 bit) ----
    public const int VariantMask = 0x3;
    public static int GetVariant(int data) => data & VariantMask;
    public static int GetRounds(int data) => (data >> 2) & 0x3F;
    public static int SetRounds(int data, int rounds) => (data & ~(0x3F << 2)) | ((Math.Clamp(rounds, 0, 63) & 0x3F) << 2);
    public static bool GetSilencerOff(int data) => ((data >> 8) & 1) != 0;
    public static int SetSilencerOff(int data, bool off) => off ? data | (1 << 8) : data & ~(1 << 8);
    public static int MakeData(int variant, int rounds, bool silencerOff = false) => SetSilencerOff(SetRounds(variant & VariantMask, rounds), silencerOff);
}
