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
    /// <summary>A detachable silencer, the M4A1-S and USP-S; the MP5-SD's is integrated.</summary>
    public bool HasSilencer;
    /// <summary>Fired permanently silenced - CS2's WEAPONSILENCER_INTEGRATED, only the MP5-SD.</summary>
    public bool SilencedAlways;
    /// <summary>
    /// Pellets per shot. 1 for everything but the shotguns: Nova 9, MAG-7 and
    /// Sawed-Off 8, XM1014 6, from m_nNumBullets.
    /// </summary>
    public int Pellets = 1;
    /// <summary>
    /// A three-round burst on the fire-mode key. Only the Glock-18 and the FAMAS have
    /// one. CS2 gives the burst's cycle and the gap between its shots but not the count;
    /// three is Counter-Strike's burst and the one estimate here.
    /// </summary>
    public bool HasBurstMode;
    public float BurstCycleSeconds;
    public float BurstShotSeconds;
    public int BurstShots = 3;
    /// <summary>Scope magnifications; empty for iron sights only.</summary>
    public float[] ZoomLevels = [];
    /// <summary>
    /// CS2's m_bHideViewModelWhenZoomed: the snipers hide the gun behind the scope
    /// overlay; the AUG and SG 553 keep it drawn and aim down their own scope
    /// (ironsight clips), with no overlay.
    /// </summary>
    public bool ScopeHidesWeapon = true;
    /// <summary>
    /// CS2's m_bUnzoomsAfterShot: a scoped shot drops the scope for the cycle and
    /// re-zooms afterwards. True for the two bolt actions only; the SCAR-20, G3SG1,
    /// AUG and SG 553 fire with the scope up. 0.20.0 unscoped all six.
    /// </summary>
    public bool UnzoomsAfterShot;
    public string MuzzleBone = "muzzle";
    public string SilencedMuzzleBone = "muzzle2";
    /// <summary>The Dual Berettas' left gun: its shots flash and trace from muzzle_l.</summary>
    public string LeftMuzzleBone;
    /// <summary>
    /// CS2's second m_flCycleTime, the alternate fire's, where it differs from the
    /// first: the R8 Revolver fans the hammer on the aim key at 0.4 s against 0.5 s
    /// for the cocked shot. 0 for a gun with no alternate fire.
    /// </summary>
    public float CycleSecondsAlternate;
    /// <summary>
    /// Seconds until a gun with no reload has a fresh charge: the Zeus x27. CS2 does
    /// not write this in the vdata; 30 s is the game's own timing and is marked assumed.
    /// </summary>
    public float RechargeSeconds;
    /// <summary>
    /// False for the Zeus: its model spawns no muzzle particle and its tracer is a
    /// wire (weapon_tracers_taser), so neither the flash sprite nor the ribbon is drawn.
    /// </summary>
    public bool MuzzleEffects = true;
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
        // Everything below is CS2 weapons.vdata, read by tools/cs2_weapons.py and
        // reproduced here so GunSpec stays the single gameplay table. Appended only -
        // the index is the saved variant number, see FrozenOrder.
        new() {
            // CS2 weapons.vdata (local extract 2026-09-04): m_flCycleTime 1.455, m_nDamage 115,
            // m_iMaxClip1 5. Scope: m_nZoomFOV1 40, m_nZoomFOV2 10 against CS2's 90 base, i.e.
            // magnifications 90/40 = 2.25 and 90/10 = 9 (not the earlier guessed 4/8).
            Name = "awp", Magazine = 5, CycleSeconds = 1.455f, Automatic = false, AttackPower = 115f,
            KickPitchDegrees = 6f, KickYawDegrees = 1f, KickRecoverPerSecond = 6f, SpreadDegrees = 0.1f,
            ZoomLevels = [2.25f, 9f], UnzoomsAfterShot = true,   // m_bUnzoomsAfterShot true
        },
        // ---- appended 2026-09-05, all values from CS2 weapons.vdata ----
        new() {
            Name = "deagle", Magazine = 7, CycleSeconds = 0.225f, Automatic = false, AttackPower = 53f,
            KickPitchDegrees = 2.571f, KickYawDegrees = 1.285f, KickRecoverPerSecond = 1.23f,
            SpreadDegrees = 0.3552f,
        },
        new() {
            // Burst: m_flCycleTimeWhenInBurstMode 0.5, m_flTimeBetweenBurstShots 0.05.
            Name = "glock18", Magazine = 20, CycleSeconds = 0.15f, Automatic = false, AttackPower = 30f,
            KickPitchDegrees = 0.960f, KickYawDegrees = 0.167f, KickRecoverPerSecond = 5.00f,
            SpreadDegrees = 0.4354f,
            HasBurstMode = true, BurstCycleSeconds = 0.5f, BurstShotSeconds = 0.05f,
        },
        new() {
            Name = "usp_silencer", Magazine = 12, CycleSeconds = 0.17f, Automatic = false, AttackPower = 35f,
            KickPitchDegrees = 1.547f, KickYawDegrees = 0f, KickRecoverPerSecond = 2.86f,
            SpreadDegrees = 0.4240f, HasSilencer = true,
        },
        new() {
            Name = "m4a4", Magazine = 30, CycleSeconds = 0.09f, Automatic = true, AttackPower = 33f,
            KickPitchDegrees = 1.227f, KickYawDegrees = 0.704f, KickRecoverPerSecond = 2.95f,
            SpreadDegrees = 0.3151f,
        },
        new() {
            // Burst: 0.55 cycle, 0.075 between shots.
            Name = "famas", Magazine = 25, CycleSeconds = 0.09f, Automatic = true, AttackPower = 30f,
            KickPitchDegrees = 1.067f, KickYawDegrees = 0.533f, KickRecoverPerSecond = 4.00f,
            SpreadDegrees = 0.4692f,
            HasBurstMode = true, BurstCycleSeconds = 0.55f, BurstShotSeconds = 0.075f,
        },
        new() {
            Name = "mp9", Magazine = 30, CycleSeconds = 0.07f, Automatic = true, AttackPower = 26f,
            KickPitchDegrees = 1.120f, KickYawDegrees = 0.642f, KickRecoverPerSecond = 3.88f,
            SpreadDegrees = 0.5500f,
        },
        new() {
            Name = "p90", Magazine = 50, CycleSeconds = 0.07f, Automatic = true, AttackPower = 26f,
            KickPitchDegrees = 0.853f, KickYawDegrees = 0.489f, KickRecoverPerSecond = 2.69f,
            SpreadDegrees = 0.8393f,
        },
        new() {
            // m_nZoomFOV1 40 and m_nZoomFOV2 15 against CS2's 90, i.e. 2.25 and 6.
            Name = "ssg08", Magazine = 10, CycleSeconds = 1.25f, Automatic = false, AttackPower = 88f,
            KickPitchDegrees = 1.760f, KickYawDegrees = 0.306f, KickRecoverPerSecond = 7.04f,
            SpreadDegrees = 1.8317f, ZoomLevels = [2.25f, 6f], UnzoomsAfterShot = true,   // m_bUnzoomsAfterShot true
        },
        new() {
            Name = "fiveseven", Magazine = 20, CycleSeconds = 0.15f, Automatic = false, AttackPower = 32f,
            KickPitchDegrees = 1.333f, KickYawDegrees = 0.058f, KickRecoverPerSecond = 5.00f,
            SpreadDegrees = 0.6360f,
        },
        new() {
            Name = "hkp2000", Magazine = 13, CycleSeconds = 0.17f, Automatic = false, AttackPower = 35f,
            KickPitchDegrees = 1.387f, KickYawDegrees = 0.000f, KickRecoverPerSecond = 2.86f,
            SpreadDegrees = 0.3953f,
        },
        new() {
            Name = "p250", Magazine = 13, CycleSeconds = 0.15f, Automatic = false, AttackPower = 38f,
            KickPitchDegrees = 1.387f, KickYawDegrees = 0.121f, KickRecoverPerSecond = 2.90f,
            SpreadDegrees = 0.6360f,
        },
        new() {
            Name = "tec9", Magazine = 18, CycleSeconds = 0.12f, Automatic = false, AttackPower = 33f,
            KickPitchDegrees = 1.227f, KickYawDegrees = 0.613f, KickRecoverPerSecond = 2.56f,
            SpreadDegrees = 0.3953f,
        },
        new() {
            Name = "cz75a", Magazine = 12, CycleSeconds = 0.1f, Automatic = true, AttackPower = 31f,
            KickPitchDegrees = 1.653f, KickYawDegrees = 1.432f, KickRecoverPerSecond = 4.12f,
            SpreadDegrees = 0.7694f,
        },
        new() {
            Name = "mac10", Magazine = 30, CycleSeconds = 0.075f, Automatic = true, AttackPower = 29f,
            KickPitchDegrees = 0.960f, KickYawDegrees = 0.551f, KickRecoverPerSecond = 2.50f,
            SpreadDegrees = 0.7964f,
        },
        new() {
            Name = "mp7", Magazine = 30, CycleSeconds = 0.08f, Automatic = true, AttackPower = 30f,
            KickPitchDegrees = 0.853f, KickYawDegrees = 0.489f, KickRecoverPerSecond = 2.29f,
            SpreadDegrees = 0.6073f,
        },
        new() {
            Name = "ump45", Magazine = 25, CycleSeconds = 0.09f, Automatic = true, AttackPower = 35f,
            KickPitchDegrees = 1.227f, KickYawDegrees = 0.420f, KickRecoverPerSecond = 2.86f,
            SpreadDegrees = 0.8267f,
        },
        new() {
            Name = "bizon", Magazine = 64, CycleSeconds = 0.08f, Automatic = true, AttackPower = 27f,
            KickPitchDegrees = 0.960f, KickYawDegrees = 0.551f, KickRecoverPerSecond = 3.02f,
            SpreadDegrees = 0.8594f,
        },
        new() {
            // Integral suppressor: WEAPONSILENCER_INTEGRATED, the single shot sound is the suppressed one.
            Name = "mp5sd", Magazine = 30, CycleSeconds = 0.08f, Automatic = true, AttackPower = 28f,
            KickPitchDegrees = 0.853f, KickYawDegrees = 0.489f, KickRecoverPerSecond = 2.29f,
            SpreadDegrees = 0.6073f, SilencedAlways = true,
        },
        new() {
            Name = "galilar", Magazine = 35, CycleSeconds = 0.09f, Automatic = true, AttackPower = 30f,
            KickPitchDegrees = 1.120f, KickYawDegrees = 0.642f, KickRecoverPerSecond = 3.33f,
            SpreadDegrees = 0.5369f,
        },
        new() {
            // Scope: m_nZoomFOV1 40 and m_nZoomFOV2 15 against CS2's 90, i.e. 2.25 and 6. m_bUnzoomsAfterShot false: fires scoped.
            Name = "scar20", Magazine = 20, CycleSeconds = 0.25f, Automatic = true, AttackPower = 80f,
            KickPitchDegrees = 1.653f, KickYawDegrees = 0.428f, KickRecoverPerSecond = 1.84f,
            SpreadDegrees = 1.4951f, ZoomLevels = [2.25f, 6f],
        },
        new() {
            // Scope: m_nZoomFOV1 40 and m_nZoomFOV2 15 against CS2's 90, i.e. 2.25 and 6. m_bUnzoomsAfterShot false: fires scoped.
            Name = "g3sg1", Magazine = 20, CycleSeconds = 0.25f, Automatic = true, AttackPower = 80f,
            KickPitchDegrees = 1.600f, KickYawDegrees = 0.414f, KickRecoverPerSecond = 1.84f,
            SpreadDegrees = 1.4951f, ZoomLevels = [2.25f, 6f],
        },
        new() {
            // Scope: m_nZoomFOV1 45 against CS2's 90, i.e. 2. m_bHideViewModelWhenZoomed false: the gun stays drawn and aims down its own scope.
            Name = "aug", Magazine = 30, CycleSeconds = 0.1f, Automatic = true, AttackPower = 28f,
            KickPitchDegrees = 1.280f, KickYawDegrees = 0.640f, KickRecoverPerSecond = 2.33f,
            SpreadDegrees = 0.3094f, ZoomLevels = [2f], ScopeHidesWeapon = false,   // m_bUnzoomsAfterShot false: fires scoped
        },
        new() {
            // Scope: m_nZoomFOV1 45 against CS2's 90, i.e. 2. m_bHideViewModelWhenZoomed false: the gun stays drawn and aims down its own scope.
            Name = "sg556", Magazine = 30, CycleSeconds = 0.11f, Automatic = true, AttackPower = 30f,
            KickPitchDegrees = 1.493f, KickYawDegrees = 0.747f, KickRecoverPerSecond = 2.21f,
            SpreadDegrees = 0.3673f, ZoomLevels = [2f], ScopeHidesWeapon = false,   // m_bUnzoomsAfterShot false: fires scoped
        },
        new() {
            // m_nNumBullets 9.
            Name = "nova", Magazine = 8, CycleSeconds = 0.88f, Automatic = false, AttackPower = 26f,
            KickPitchDegrees = 7.627f, KickYawDegrees = 1.324f, KickRecoverPerSecond = 2.17f,
            SpreadDegrees = 2.6909f, Pellets = 9,
        },
        new() {
            // m_nNumBullets 6.
            Name = "xm1014", Magazine = 7, CycleSeconds = 0.35f, Automatic = true, AttackPower = 20f,
            KickPitchDegrees = 4.267f, KickYawDegrees = 0.741f, KickRecoverPerSecond = 1.97f,
            SpreadDegrees = 2.5766f, Pellets = 6,
        },
        new() {
            // m_nNumBullets 8.
            Name = "sawedoff", Magazine = 7, CycleSeconds = 0.85f, Automatic = false, AttackPower = 32f,
            KickPitchDegrees = 7.627f, KickYawDegrees = 1.324f, KickRecoverPerSecond = 2.17f,
            SpreadDegrees = 3.9472f, Pellets = 8,
        },
        new() {
            // m_nNumBullets 8.
            Name = "mag7", Magazine = 5, CycleSeconds = 0.85f, Automatic = false, AttackPower = 30f,
            KickPitchDegrees = 8.800f, KickYawDegrees = 1.528f, KickRecoverPerSecond = 2.50f,
            SpreadDegrees = 2.6909f, Pellets = 8,
        },
        new() {
            Name = "m249", Magazine = 100, CycleSeconds = 0.08f, Automatic = true, AttackPower = 32f,
            KickPitchDegrees = 1.333f, KickYawDegrees = 0.563f, KickRecoverPerSecond = 1.21f,
            SpreadDegrees = 0.5557f,
        },
        new() {
            Name = "negev", Magazine = 150, CycleSeconds = 0.075f, Automatic = true, AttackPower = 35f,
            KickPitchDegrees = 1.067f, KickYawDegrees = 0.000f, KickRecoverPerSecond = 3.33f,
            SpreadDegrees = 0.6973f,
        },
        new() {
            Name = "revolver", Magazine = 8, CycleSeconds = 0.5f, Automatic = true, AttackPower = 86f,
            KickPitchDegrees = 1.067f, KickYawDegrees = 0.365f, KickRecoverPerSecond = 1.11f,
            SpreadDegrees = 0.1444f,
            // m_flCycleTime [0.5, 0.4]: the second is the fanned shot on the aim key.
            CycleSecondsAlternate = 0.4f,
        },
        new() {
            Name = "elite", Magazine = 30, CycleSeconds = 0.12f, Automatic = false, AttackPower = 38f,
            KickPitchDegrees = 1.440f, KickYawDegrees = 0.250f, KickRecoverPerSecond = 1.90f,
            SpreadDegrees = 0.5157f,
            MuzzleBone = "muzzle_r", LeftMuzzleBone = "muzzle_l",
        },
        new() {
            Name = "taser", Magazine = 1, CycleSeconds = 0.15f, Automatic = false, AttackPower = 500f,
            KickPitchDegrees = 0.000f, KickYawDegrees = 0.000f, KickRecoverPerSecond = 0.00f,
            SpreadDegrees = 0.0000f,
            // m_flRange 120 in, i.e. 3.05 m; the rifles' 4096 stay on the default.
            // No reload clip and one round: recharges. 30 s is CS2's Zeus timing, not in the vdata - assumed.
            MuzzleEffects = false, RangeBlocks = 3.05f, RechargeSeconds = 30f,
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
