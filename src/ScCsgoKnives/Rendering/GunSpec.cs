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

    // ---- block data layout ----
    //
    // Old, and still read: silencer at bit 8, rounds at 2..7, variant at 0..1. Two
    // bits hold four guns, and CS2 has 35.
    //
    // New, and always written:
    //
    //     bit 14   13    12..6      5..0
    //     layout   silencer rounds  variant      64 guns, 0..127 rounds
    //
    // Widening the variant field in place would have eaten the rounds bits: a saved
    // AK-47 with 30 rounds is 120, and reading that with a six-bit variant gives gun
    // 56, which does not exist. Bit 14 says which layout a value is in. Old values
    // cannot be mistaken for new ones - the old encoder only ever set bits 0..8 - and
    // any write upgrades the value, because every setter re-encodes.
    //
    // Survivalcraft gives 18 bits here ((value & -16384) >> 14 in Terrain.cs), of
    // which bit 31 is the sign and an arithmetic shift would make the result negative,
    // so 17 are safe. This uses 15.
    const int LayoutBit = 1 << 14;
    const int NewVariantMask = 0x3F;
    const int NewRoundsMask = 0x7F;
    const int NewRoundsShift = 6;
    const int NewSilencerBit = 1 << 13;

    /// <summary>Kept for the old layout's four-gun field; new data uses six bits.</summary>
    public const int VariantMask = 0x3;

    static bool IsNewLayout(int data) => (data & LayoutBit) != 0;

    public static int GetVariant(int data) =>
        IsNewLayout(data) ? data & NewVariantMask : data & VariantMask;

    public static int GetRounds(int data) =>
        IsNewLayout(data) ? (data >> NewRoundsShift) & NewRoundsMask : (data >> 2) & 0x3F;

    public static bool GetSilencerOff(int data) =>
        IsNewLayout(data) ? (data & NewSilencerBit) != 0 : ((data >> 8) & 1) != 0;

    public static int MakeData(int variant, int rounds, bool silencerOff = false) =>
        LayoutBit
        | (variant & NewVariantMask)
        | ((Math.Clamp(rounds, 0, NewRoundsMask) & NewRoundsMask) << NewRoundsShift)
        | (silencerOff ? NewSilencerBit : 0);

    // Both setters re-encode, so touching a gun's ammo or silencer migrates it.
    public static int SetRounds(int data, int rounds) =>
        MakeData(GetVariant(data), rounds, GetSilencerOff(data));

    public static int SetSilencerOff(int data, bool off) =>
        MakeData(GetVariant(data), GetRounds(data), off);

    /// <summary>
    /// The variant number is this array's index and it is written into saved worlds,
    /// so entries may only be appended. Inserting one renumbers every gun after it and
    /// a saved AWP becomes whatever took its place; removing one does the same. A gun
    /// being retired stays in the array as a placeholder. Asserted by the self-test.
    /// </summary>
    public static readonly string[] FrozenOrder = ["ak47", "m4a1s", "awp"];
}
