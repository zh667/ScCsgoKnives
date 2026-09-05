using System.IO;
using System.Globalization;
using System.Text;
using Engine;

namespace Game;

/// <summary>
/// Live tuning for the first-person composition, read from a plain text file
/// next to the game and re-read once a second.
///
/// The defaults are not guesses. The arm widths and the knife scale were fitted
/// against static CS:MC screenshots -- each reference arm segmented by colour, its
/// medial axis and width profile measured, ours measured with the identical
/// statistic (tools/armstat.py, tools/ratiofit.py). The arm directions are not
/// fitted at all any more: they come from the rig's own arm_lower bones, and the
/// palm overshoot comes from CS:MC's arm model, source2_arms.geo.json.
///
/// The declarative mod Settings block would be the tidier home for these, but it
/// needs API 1.9.3 and this build reports 1.9.2.1, so a file it is.
/// </summary>
public static class KnifeTuning {
    public const string Path = "app:/ScCsgoKnivesTuning.txt";
    const double ReloadInterval = 1.0;

    /// <summary>
    /// Stamp of the built-in defaults, written into the tuning file and checked on
    /// load. Derived from the serialized defaults rather than bumped by hand: 0.9.1
    /// through 0.9.3 all changed defaults without touching a hand-bumped version,
    /// so the file 0.9.0 wrote kept overriding them -- the player ran three updates
    /// with 0.9.0's knife scale and arm widths and none of the fixes were visible.
    /// Any change to any default now invalidates the stale file automatically.
    /// </summary>
    static int s_version;

    /// <summary>
    /// Computed lazily: static fields initialize in declaration order, so an
    /// initializer here would hash the tunables before they exist. Poll() reads
    /// this before the first Apply(), so the stamp always reflects the defaults.
    /// </summary>
    public static int Version {
        get {
            if (s_version == 0) s_version = ComputeDefaultsStamp();
            return s_version;
        }
    }

    static int ComputeDefaultsStamp() {
        // FNV-1a: string.GetHashCode is randomized per process, so it cannot be
        // compared against a value written by an earlier run.
        unchecked {
            int hash = (int)2166136261;
            foreach (char c in Serialize(0)) {
                if (c is '\r' or '\n' or ' ') continue;
                hash = (hash ^ c) * 16777619;
            }
            return hash == 0 ? 1 : hash & 0x7FFFFFFF;
        }
    }

    // Only KnifeScale/AnchorDepth matters: scaling the rig and pushing it back by
    // the same factor reproduces the same image exactly. That ratio is what sets how
    // far the composition spreads across the frame -- how far the forearms lean, how
    // far out the left hand sits, and how long the knife looks.
    //
    // Set by the knife's own size on screen. CS:MC's red M9 is the only saturated red
    // thing in its frame, so its painted region segments cleanly; a width profile
    // along that region shows where the guard starts (the width jumps from 0.058 to
    // 0.085), which puts the BLADE at 0.288 of the frame width. Ours is measured the
    // same way off a screenshot -- steel is the only mid-grey thing in frame -- and
    // this value is solved so it lands on 0.288 too. Re-solve it if the grip table
    // changes: the grip is pinned to the anchor, so moving it along the handle moves
    // the whole knife in depth and changes how big it draws.
    //
    // 0.9.0 set this to 0.426 by comparing our whole knife's projected bounding box
    // against that 0.366 painted region, which is blade plus guard. Two mismatched
    // measurements in the same ratio: the knife shipped at half the size it should
    // have been. Measure the same feature on both sides or do not compare them.
    //
    // 0.11.0: solved again, this time by fitting the M9's whole visible silhouette to
    // its photo (tools/fistsolve.py) rather than one blade measurement -- the fist
    // moved from the cap of the arm to the middle of it, and the tip-only solve
    // could not tell where along the handle the knife sat. Per-weapon factors on
    // top of this live in CsmcFirstPersonRenderer's fist table.
    public static float KnifeScale = 0.670f;

    // Where the grip -- the centre of the fist, where the handle's centre line
    // crosses the arm's axis -- rests at idle, for knives without their own photo.
    // The three straight knives with photos put it at (0.710,0.843), (0.723,0.807)
    // and (0.722,0.843); this is their mean. Pinned as a screen fraction rather than
    // a view-space vector because Survivalcraft's field of view is
    // 80 * SettingsManager.ViewAngle and the aspect follows the window: a view-space
    // anchor would drift for anyone not playing at the default. x right, y down.
    //
    // Up to 0.10.0 this was (0.700,0.709), which is the top of the reference fist,
    // not its middle: the handle was pinned to the cap of the arm and lay across it.
    public static float AnchorScreenX = 0.7186f;
    public static float AnchorScreenY = 0.8306f;
    public static float AnchorDepth = 0.72f;

    // The arms' bearing on screen, degrees from straight down, positive leaning
    // right. Fitted by tools/fistfit.py as the box that best overlaps the photo's
    // arm silhouette (IoU 0.95 right, 0.91 left on the M9). Knives whose photo
    // shows a different arm carry their own in the renderer's fist table.
    //
    // These are constants on purpose. 0.9.0 drove the bearing from each knife's own
    // arm_lower bone instead, on the grounds that the bone is real animation data --
    // but the references say CS:MC does not use it for the on-screen direction: the
    // left arm's bearing has a standard deviation of 0.3 degrees across twenty of
    // them while its hand swings across a quarter of the screen. ArmLeanFromBone can
    // turn it back on for experiments.
    public static float RightArmLean = 6.8f;
    public static float LeftArmLean = -46.8f;
    // How much nearer the eye the elbow end is than the hand; sets the widening
    // toward the bottom of the frame. Same fit.
    public static float RightArmNear = 1.36f;
    public static float LeftArmNear = 1.51f;

    /// <summary>
    /// How much of the arm's bearing to take from the rig's arm_lower bone instead of
    /// the fitted angle above. 0 is the references' own behaviour; 1 follows the bone.
    /// </summary>
    public static float ArmLeanFromBone = 0f;

    /// <summary>
    /// Extra pitch of every knife about the view X axis, degrees. CS:MC appends its
    /// weapon transform to Minecraft's first-person hand matrix, which carries a
    /// tilt we never had. One shared angle for every knife, solved together with the
    /// yaw by fitting four knives' whole silhouettes to their photos at once
    /// (tools/fistsolve.py). 0.10.0's -25/+28 came from a tip-only solve against the
    /// wrong anchor; with the fist in the right place the residual tilt is small.
    /// </summary>
    //
    // 0.13.2 tried +10 (solved against the CS:MC inspect video by tools/holdcompare.py) and the
    // player rejected it in play; 0.13.3 is back at -14. Kept for the record: +10 solved against the CS:MC inspect VIDEO rather than idle
    // stills (tools/holdcompare.py). A still cannot separate this pitch from the other
    // composition parameters, and the wrong pitch turned the clip's "raise the knife"
    // into "push it away": at the hold our knife sat 75 px lower, 30% shorter and
    // tilted inward, with the handle buried. At +10 the hold's grip lands within 10 px
    // of the video's, the knife keeps its size (ratio 1.04 vs 1.0) and leans 10 deg
    // right (video 13). Idle moves by 40 px at the tip and 2 deg of lean.
    public static float KnifePitchDegrees = -14f;

    /// <summary>Companion yaw, same solve as the pitch.</summary>
    public static float KnifeYawDegrees = 1f;

    // Fraction of the viewport width the arm covers at the hand, resolved against
    // the live projection at the hand's depth -- screen space for the same reason
    // the anchor is. From the same silhouette fit as the leans: the reference
    // fist is 178 pixels across at 1920 wide, which the box reproduces at these
    // values once its own perspective is drawn.
    //
    // 0.10.0 shipped 0.066 and 0.051 off a statistic that measured something else;
    // the left arm in particular was two thirds of the reference's width, which is
    // "the arms look thin and long".
    public static float ArmScreenWidth = 0.0792f;
    public static float LeftArmScreenWidth = 0.086f;

    /// <summary>How far down the screen to run the arm, so it always leaves the frame.</summary>
    public static float ArmExitY = 1.30f;

    /// <summary>
    /// How the arm rolls once a clip turns the wrist. Idle is the fitted, face-on
    /// composition in every mode; only the change is followed.
    ///   0  never rolls: the knife turns through a fist that does not.
    ///   1  rolls with the hand bone the way CS:MC's b$4la does, rigidly: the handle
    ///      keeps whatever angle it had to the fist's face at idle (about thirty
    ///      degrees on the M9), so it still crosses the face on a turned wrist.
    ///   2  rolls so the handle lies flat along the fist's face, and FistGripFace
    ///      puts that face at the handle; the handle then rests on the fist instead
    ///      of passing through it, which is how the reference looks at the end of an
    ///      inspect. Blended in as the wrist leaves its idle pose, so idle is untouched.
    /// </summary>
    //
    // 1 is what the CS:MC recordings show frame by frame, with FistGripFace 1: the
    // box is offset to the back of the palm and rolls rigidly with the wrist. Being
    // square it never looks like it rolls; what changes is which side of the knife
    // it sits -- in front of the handle at idle (handle hidden), behind it at the
    // hold of an inspect (handle lying on the front face, sinking into the box's
    // top), the switch happening as the wrist turns through a right angle, with no
    // special handling at the ends of a clip. Mode 2's blends put that switch at
    // the end of the clip instead, which is the flash the arm showed there; mode 0
    // centres the box and buries the handle at the hold.
    public static float ArmRollMode = 1f;

    /// <summary>
    /// Where the grip sits across the fist, in half-widths along the line of sight:
    /// 0 is the box's centre, 1 its far face (the box sits between the eye and the
    /// handle, which hides the handle at idle and shows it lying on the fist once
    /// the wrist has turned the far face round), -1 its near face.
    /// </summary>
    public static float FistGripFace = 1f;

    /// <summary>
    /// How far the wrist has to turn from idle, in degrees, before the roll of mode 2
    /// is fully in; below a fifth of it the box stays face-on. Keeps idle and the
    /// breathing clips exactly as fitted.
    /// </summary>
    public static float RollBlendDegrees = 30f;

    /// <summary>
    /// The knife is not rigid to the hand bone: an inspect re-grips it in the
    /// fingers and twirls it in between. The fist follows the handle's new direction
    /// with this time constant, in seconds, but only while the handle turns slower
    /// than HandleFollowRate degrees a second; faster than that is a twirl, and the
    /// fist keeps the last steady grip until it is over.
    /// </summary>
    public static float HandleFollowSeconds = 0.15f;
    public static float HandleFollowRate = 120f;

    /// <summary>
    /// How far, in degrees, the fist may turn away from where the wrist-rigid rule
    /// puts it to follow the re-gripped handle. The re-grips measure 18 to 32
    /// degrees on the photographed knives; 0 is exactly 0.11.2's behaviour.
    /// </summary>
    public static float ReGripDegrees = 35f;
    /// <summary>How fast that correction may change, degrees a second: a drift, never a flick.</summary>
    public static float ReGripDegreesPerSecond = 90f;

    /// <summary>
    /// At the hold of an inspect the reference's box sits straight behind the
    /// knife, but the wrist's rigid turn stops short of that (127 degrees of 180 on
    /// the M9). The remainder is added as a straight line in the rigid angle, from
    /// SquareFromDegrees up to the angle the clip actually rests at (measured when
    /// the knife loads; SquareFullDegrees overrides it when above 0). Spread evenly
    /// over the approach, the fist turns at a constant multiple of the wrist's rate
    /// and stops in the same frame the knife does: added late it lagged the blade,
    /// added as a curve that saturated early it surged and coasted. Lower
    /// SquareFromDegrees spreads it thinner (0: 1.4x on the M9; 45: 1.6x; 90: 2.4x).
    /// SquareAtHold 1 on, 0 rigid. SquareGateByStillness 1 restores waiting for the
    /// wrist to settle before squaring (the fist then finishes after the knife).
    /// </summary>
    public static float SquareAtHold = 1f;
    public static float SquareFromDegrees = 45f;
    public static float SquareFullDegrees = 0f;
    public static float SquareGateByStillness = 0f;
    /// <summary>How the squaring's extra turn is spread: 0 = evenly (fist at a constant 1.55x the wrist on the M9), 1 = eased in and out (same rate as the wrist at the start and the hold).</summary>
    public static float SquareEase = 1f;

    /// <summary>
    /// 1: the inspect key runs the capture instead -- draw, idle, inspect, idle
    /// on a 30 fps virtual clock, a screenshot and a line of the arm's numbers
    /// per frame, to app:/ScreenCapture/ScCsgoKnivesQA/. See KnifeQa.
    /// </summary>
    public static float QaCapture = 0f;

    /// <summary>
    /// How fast the arm may roll about its own axis, in degrees a second. Simulated
    /// through the clips at 60 fps: the straight knives' inspects peak at 420-790, a
    /// slash at about 900-1030, and the balisong's flips at up to 3000. This lets
    /// every inspect but the balisong's through untouched, barely touches a slash,
    /// and stops the fist spinning with the butterfly's blade.
    /// </summary>
    public static float RollSlewDegreesPerSecond = 900f;

    /// <summary>
    /// How far the fist reaches past the grip, as a fraction of the arm's width.
    /// This is what buries the handle in the fist so only the guard and the pommel
    /// show, the way the reference does. Measured per photo with the grip on the
    /// far face: 0.89 on the M9, 0.82 on the karambit, 0.42 on the butterfly, 0.74
    /// on the huntsman; this is the value for knives without a photo and for the
    /// left hand.
    ///
    /// 0.10.0 took a sixth of the forearm bone instead, which came to a fifth of
    /// the arm's width: the fist stopped at the handle and the handle lay across
    /// the top of the arm.
    /// </summary>
    public static float ArmPalmOvershoot = 0.69f;

    // Where the left grip -- the centre of the left fist -- sits at idle, in screen
    // fractions, for knives without their own photo. Each knife gets its own
    // correction computed at load so its idle left hand lands exactly here: CS:MC's
    // arms are their own two-bone animation, not the weapon rig's hand_l, which is
    // why their left hand stays put across the straight knives while the rig's
    // hand_l roams across a third of the screen. The karambit's photo shows a
    // different left arm altogether; that one is in the renderer's fist table.
    public static float LeftHandTargetScreenX = 0.3206f;
    public static float LeftHandTargetScreenY = 0.9101f;
    // (far-face fit, tools/reference/fistfit_face.json)
    // The depth the left fist is built at -- the one its box was fitted at.
    public static float LeftHandDepth = 0.55f;

    /// <summary>
    /// How much of Survivalcraft's 0.8 equip dip to keep. The CS:GO deploy clip
    /// already raises the knife, so the default is none; raise it if the swap
    /// starts to feel weightless.
    /// </summary>
    public static float SwapDipScale = 0f;

    // 0.13.2 tried 1.6 with pitch +10, rejected in play; 0.13.3 is back at 0.55. With the pitch corrected the clip's own travel raises the
    // knife the right way, and the video's grip rises 150 px through the inspect where
    // 1.0 gives 85; 1.6 matches it. See KnifePitchDegrees.
    public static float InspectTravelScale = 0.55f;

    // ---- Exact CS:MC composition (reverse-engineered chain; see CSMCReverse/work/firstperson-chain.md) ----
    /// <summary>1 = place knife and arms by CS:MC's own transform chain (no fitted anchor/scale); 0 = the fitted composition of 0.13.x.</summary>
    public static float ExactChain = 1f;
    /// <summary>Minecraft/CS:MC hand pass field of view, vertical degrees (CS:MC setting default 70).</summary>
    public static float ExactHandFovDegrees = 70f;
    /// <summary>
    /// CS:MC draws the Source2 weapon itself through its own projection: the per-weapon
    /// FOV from the weapon table (48 for every knife, times viewFov/70) is turned into a
    /// perspective matrix (b$2ni) and applied to the queued weapon draws only; the arms
    /// stay in Minecraft's 70 degree hand pass. Measured against the CS:MC video this is
    /// what fixes both the knife size and the flatter perspective of the blade.
    /// </summary>
    public static float ExactWeaponFovDegrees = 0f;
    /// <summary>
    /// 1 draws CS:MC's stretched Minecraft arm boxes (anchored at its view-space
    /// constants, 70 degree hand pass) instead of the fist solver. Off: the player
    /// preferred the 0.13.x fists, and the boxes looked far too big in Survivalcraft.
    /// </summary>
    public static float ExactArms = 0f;
    /// <summary>
    /// The butterfly's T-shaped latch (mesh part v_weapon_lock). CS:MC draws it sticking
    /// out of the handle end exactly like this (MCCS video, 6.3 s), but on our grey
    /// knife it reads as a nail, so it is hidden unless set to 1.
    /// </summary>
    public static float ShowButterflyLatch = 0f;
    /// <summary>
    /// Offsets added to the weapon table's hip offset (x,y,z), roll and per-weapon FOV.
    /// The table values themselves come from CS:MC's registration rows (weapon_table.json);
    /// these stay 0 unless a knife needs a nudge. ExactWeaponFovDegrees 0 = the table's FOV.
    /// </summary>
    public static float ExactHipX = 0f;
    public static float ExactHipY = 0f;
    public static float ExactHipZ = 0f;
    /// <summary>Knife family roll about X after the hip offset, degrees.</summary>
    public static float ExactRollDegrees = 0f;
    /// <summary>CS:MC's global viewmodel offset setting (default 0; the server may push a per-player value).</summary>
    /// <summary>
    /// Extra eye-space translation on top of the weapon table's hip offset. CS:MC's own
    /// slot for this (settings viewX/Y/Z) defaults to 0. Until 0.15.0 this carried a
    /// fitted (0.36, -0.01, 0.185): that was exactly the difference between the dual
    /// Berettas' family row (which the chain had been reading) and the knives' own rows.
    /// </summary>
    public static float ExactGlobalX = 0f;
    public static float ExactGlobalY = 0f;
    public static float ExactGlobalZ = 0f;
    /// <summary>Fixed weapon transform: translate, then Rx90 Ry180 Rz270, then scale by the reference ratio.</summary>
    public static float ExactWeaponTX = -0.22f;
    public static float ExactWeaponTY = 0.42f;
    public static float ExactWeaponTZ = -0.18f;
    /// <summary>Per-weapon scale = this knife's meshbin reference scale / the AK-47's (legacy mesh 37.74615, hd 37.28675).</summary>
    public static float ExactReferenceScale = 37.74615f;
    /// <summary>Hypothesis switches for the reverse-engineered chain, settled against the CS:MC video (tools/holdcompare.py).</summary>
    /// <summary>Minecraft's own hand translate (0.56, -0.52, -0.72) applied before the knife family offset; 0 = not applied.</summary>
    public static float ExactHandX = 0f;
    public static float ExactHandY = 0f;
    public static float ExactHandZ = 0f;
    /// <summary>0 = per-weapon scale from the reference ratio; otherwise this absolute placement scale (hypothesis sweeps).</summary>
    public static float ExactScaleOverride = 0f;
    /// <summary>1 mirrors the composition left-right (CS:MC's hand-side flag convention).</summary>
    public static float ExactMirrorX = 0f;
    /// <summary>
    /// 1 cancels the mesh-centre term of the normalization (adds centre x scale back before
    /// the weapon scale). Read literally, CS:MC's chain shifts the whole hand-plus-knife
    /// composition by minus (mesh centre x scale x weapon scale), a different amount per
    /// knife: 0.11 up for the M9, 0.015 for the karambit, 0.06 for the butterfly. The MCCS
    /// videos show the hand at the same height for all three, and with this term cancelled
    /// the same global offset lands all three knives on their videos (M9 5 px rms; karambit
    /// and butterfly within about 20-50 px at idle). 0 restores the literal reading.
    /// </summary>
    public static float ExactMeshCenterOffset = 0f;
    /// <summary>Arm anchors in view space (CS:MC b$4jq): where the forearm box starts.</summary>
    public static float ExactArmAnchorRX = 0.58f;
    public static float ExactArmAnchorRY = -0.78f;
    public static float ExactArmAnchorRZ = -0.70f;
    public static float ExactArmAnchorLX = -0.7f;
    public static float ExactArmAnchorLY = -0.82f;
    public static float ExactArmAnchorLZ = -0.72f;
    /// <summary>Minecraft arm box: 4/16 wide times CS:MC's 0.82 (a slim skin is 3/16: 0.154).</summary>
    public static float ExactArmWidth = 0f;
    /// <summary>Arm length the Minecraft model has before stretching (10/16) and the stretch clamp.</summary>
    public static float ExactArmBaseLength = 0.625f;
    public static float ExactArmStretchMin = 0.65f;
    public static float ExactArmStretchMax = 4.8f;
    /// <summary>Compatibility key: all knives always use CS2 models, animation and real hands.</summary>
    public static float KnifeProfile { get => 1f; set { } } // CS2-only; ignore legacy tuning overrides.

    /// <summary>Twist added to the forearm box about its own axis after the wrist twist, degrees (CS:MC rotateY(45) then +-90).</summary>
    public static float ExactArmTwistOffsetDegrees = 45f;

    // ---- PBR material (Rendering/KnifePbrRenderer) ----
    /// <summary>1 = PBR shader; 0 = the plain lit shader (also what a compile failure falls back to).</summary>
    public static float PbrEnabled = 1f;
    /// <summary>RGBM range of the environment atlas. Scales every reflection; 6 puts the diffuse level near 1.0.</summary>
    public static float PbrEnvRange = 6f;
    public static float PbrEnvIntensity = 1f;
    /// <summary>
    /// Environment light on the guns relative to the knives, times GunSpec.EnvScale per gun.
    /// Fitted offline (tools/pbr_emulate.py, engine texture orientation, camera pitched at the
    /// sky like the recordings) so each gun's masked first-person brightness matches its MCCS
    /// recording within a few percent (source2_vmat sets): M4A1-S and AWP at 0.25, AK-47 at 0.2.
    /// </summary>
    public static float PbrGunEnvIntensity = 0.25f;
    /// <summary>Scope reticle line thickness in pixels at 1080p (scaled with the frame height). MCCS draws 1 px; the user asked for heavier lines.</summary>
    public static float ScopeLinePx = 3f;
    /// <summary>Survivalcraft's two fixed directional lights, on top of the environment.</summary>
    public static float PbrDirectIntensity = 0.5f;
    public static float PbrExposure = 1f;
    /// <summary>1 flips the normal map's green channel (DirectX-style maps).</summary>
    public static float PbrNormalFlipY = 0f;
    public static float PbrRoughnessBias = 0f;
    /// <summary>Turns the reflected environment around the vertical axis.</summary>
    public static float PbrEnvYawDegrees = 0f;
    /// <summary>How much of the environment's colour reaches the metal: 1 = full (blue sky tints the blade), 0 = grey reflections only.</summary>
    public static float PbrEnvSaturation = 0.25f;
    /// <summary>0 final; 1 base colour, 2 normals, 3 roughness, 4 metalness, 5 reflection only, 6 occlusion, 7 direct light only.</summary>
    /// <summary>Compatibility key: all guns always use the CS2 rendering route.</summary>
    public static float GunProfile { get => 1f; set { } } // CS2-only; ignore legacy tuning overrides.

    /// <summary>
    /// The player's CS2 viewmodel cvars, used by the cs2 profile. Defaults are this
    /// machine's own (D:\steam\userdata\1415980225, "name" "zh667"), not CS2's
    /// defaults of 60 / 1 / 1 / -1.
    /// </summary>
    public static float Cs2ViewmodelFov = 68f;
    public static float Cs2ViewmodelOffsetX = 2.5f;
    public static float Cs2ViewmodelOffsetY = 0f;
    public static float Cs2ViewmodelOffsetZ = -1.5f;
    /// <summary>
    /// Which gameplay numbers the three guns use: 0 = the values shipped since 0.15.10,
    /// 1 = CS2's own damage, range falloff, spread and recoil ratios from its vdata.
    ///
    /// Deliberately separate from GunProfile. Turning the CS2 *look* on must not change
    /// how the guns *play*: CS2's unscoped AWP spread is 4.63 degrees against the 0.10
    /// this ships, which is faithful but is a gameplay decision, not a rendering one.
    /// </summary>
    public static float GunNumbers = 0f;

    /// <summary>
    /// Compatibility key: CS2 hands and gloves are always drawn.
    /// </summary>
    public static float Cs2Arms { get => 1f; set { } } // CS2-only; ignore legacy tuning overrides.

    /// <summary>
    /// Which sound timings the guns use: 0 = the table timed off the bones (shipped
    /// through 0.15.10), 1 = CS2's own CNmClipDocEvent_Sound frames from
    /// AnimationData/cs2_sounds.json. Defaults to 0 until the CS2 times are checked
    /// against a recording; the two differ by up to 580 ms, so the switch is audible.
    /// </summary>
    public static float GunSoundProfile = 0f;

    public static float PbrDebug = 0f;

    static double s_nextPoll;
    static string s_lastContent;

    /// <summary>Cheap enough to call every frame; only touches disk once a second.</summary>
    public static void Poll() {
        if (Time.RealTime < s_nextPoll) return;
        s_nextPoll = Time.RealTime + ReloadInterval;
        try {
            if (!Storage.FileExists(Path)) {
                Write();
                return;
            }
            string content;
            using (Stream stream = Storage.OpenFile(Path, OpenFileMode.Read)) {
                content = new StreamReader(stream).ReadToEnd();
            }
            if (content == s_lastContent) return;
            s_lastContent = content;
            // A file written for the old entrance-point arm model has no key in
            // common with this one, so applying it would silently leave every new
            // value at its default. Replace it instead.
            if (ReadVersion(content) != Version) {
                KnifeLog.Information($"[ScCsgoKnives] tuning file was written by a build with different defaults; rewriting {Path}.");
                Write();
                s_lastContent = null;
                return;
            }
            Apply(content);
        }
        catch (Exception e) {
            KnifeDiagnostics.WarnOnce("tuning-io", $"Could not read {Path}: {e.Message}");
        }
    }

    static int ReadVersion(string content) {
        foreach (string raw in content.Split('\n')) {
            string line = raw.Trim();
            if (!line.StartsWith("TuningVersion", System.StringComparison.Ordinal)) continue;
            int equals = line.IndexOf('=');
            if (equals > 0 && int.TryParse(line[(equals + 1)..].Trim(), out int v)) return v;
        }
        return 0;
    }

    static void Apply(string content) {
        int applied = 0;
        foreach (string raw in content.Split('\n')) {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            int equals = line.IndexOf('=');
            if (equals <= 0) continue;
            string key = line[..equals].Trim();
            if (!float.TryParse(line[(equals + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) continue;
            if (!Set(key, value)) continue;
            applied++;
        }
        CsmcFirstPersonRenderer.InvalidateProjection();
        CsmcFirstPersonRenderer.ResetCompositionLog();
        CsmcFirstPersonRenderer.RebuildPlacements();
        KnifeLog.Information(
            $"[ScCsgoKnives] tuning reloaded ({applied} values): knifeScale={KnifeScale:0.###}, "
            + $"anchor=({AnchorScreenX:0.###},{AnchorScreenY:0.###})@{AnchorDepth:0.##}, "
            + $"lean R={RightArmLean:0.#} L={LeftArmLean:0.#}, near R={RightArmNear:0.###} L={LeftArmNear:0.###}, "
            + $"armWidth R={ArmScreenWidth:0.###} L={LeftArmScreenWidth:0.###}, fistOvershoot={ArmPalmOvershoot:0.###}w, "
            + $"pitch/yaw={KnifePitchDegrees:0.#}/{KnifeYawDegrees:0.#}, leftTarget=({LeftHandTargetScreenX:0.###},{LeftHandTargetScreenY:0.###})."
        );
    }

    /// <summary>Headless tools (tools/ArmPreview) set tunables from the command line through this.</summary>
    public static bool Override(string key, float value) => Set(key, value);

    static bool Set(string key, float v) {
        switch (key) {
            case "TuningVersion": return true;
            case nameof(KnifeScale): KnifeScale = v; return true;
            case nameof(KnifePitchDegrees): KnifePitchDegrees = v; return true;
            case nameof(KnifeYawDegrees): KnifeYawDegrees = v; return true;
            case nameof(AnchorScreenX): AnchorScreenX = v; return true;
            case nameof(AnchorScreenY): AnchorScreenY = v; return true;
            case nameof(AnchorDepth): AnchorDepth = v; return true;
            case nameof(RightArmLean): RightArmLean = v; return true;
            case nameof(LeftArmLean): LeftArmLean = v; return true;
            case nameof(RightArmNear): RightArmNear = v; return true;
            case nameof(LeftArmNear): LeftArmNear = v; return true;
            case nameof(ArmScreenWidth): ArmScreenWidth = v; return true;
            case nameof(LeftArmScreenWidth): LeftArmScreenWidth = v; return true;
            case nameof(ArmPalmOvershoot): ArmPalmOvershoot = v; return true;
            case nameof(ArmLeanFromBone): ArmLeanFromBone = v; return true;
            case nameof(LeftHandTargetScreenX): LeftHandTargetScreenX = v; return true;
            case nameof(LeftHandTargetScreenY): LeftHandTargetScreenY = v; return true;
            case nameof(LeftHandDepth): LeftHandDepth = v; return true;
            case nameof(ArmExitY): ArmExitY = v; return true;
            case nameof(ArmRollMode): ArmRollMode = v; return true;
            case nameof(FistGripFace): FistGripFace = v; return true;
            case nameof(RollBlendDegrees): RollBlendDegrees = v; return true;
            case nameof(HandleFollowSeconds): HandleFollowSeconds = v; return true;
            case nameof(HandleFollowRate): HandleFollowRate = v; return true;
            case nameof(ReGripDegrees): ReGripDegrees = v; return true;
            case nameof(ReGripDegreesPerSecond): ReGripDegreesPerSecond = v; return true;
            case nameof(SquareAtHold): SquareAtHold = v; return true;
            case nameof(SquareFromDegrees): SquareFromDegrees = v; return true;
            case nameof(SquareFullDegrees): SquareFullDegrees = v; return true;
            case nameof(SquareGateByStillness): SquareGateByStillness = v; return true;
            case nameof(SquareEase): SquareEase = v; return true;
            case nameof(QaCapture): QaCapture = v; return true;
            case nameof(RollSlewDegreesPerSecond): RollSlewDegreesPerSecond = v; return true;
            case nameof(SwapDipScale): SwapDipScale = v; return true;
            case nameof(InspectTravelScale): InspectTravelScale = v; return true;
            case nameof(ExactChain): ExactChain = v; return true;
            case nameof(ExactHandFovDegrees): ExactHandFovDegrees = v; return true;
            case nameof(ExactWeaponFovDegrees): ExactWeaponFovDegrees = v; return true;
            case nameof(ExactArms): ExactArms = v; return true;
            case nameof(ShowButterflyLatch): ShowButterflyLatch = v; return true;
            case nameof(ExactHipX): ExactHipX = v; return true;
            case nameof(ExactHipY): ExactHipY = v; return true;
            case nameof(ExactHipZ): ExactHipZ = v; return true;
            case nameof(ExactRollDegrees): ExactRollDegrees = v; return true;
            case nameof(ExactGlobalX): ExactGlobalX = v; return true;
            case nameof(ExactGlobalY): ExactGlobalY = v; return true;
            case nameof(ExactGlobalZ): ExactGlobalZ = v; return true;
            case nameof(ExactWeaponTX): ExactWeaponTX = v; return true;
            case nameof(ExactWeaponTY): ExactWeaponTY = v; return true;
            case nameof(ExactWeaponTZ): ExactWeaponTZ = v; return true;
            case nameof(ExactReferenceScale): ExactReferenceScale = v; return true;
            case nameof(ExactArmAnchorRX): ExactArmAnchorRX = v; return true;
            case nameof(ExactArmAnchorRY): ExactArmAnchorRY = v; return true;
            case nameof(ExactArmAnchorRZ): ExactArmAnchorRZ = v; return true;
            case nameof(ExactArmAnchorLX): ExactArmAnchorLX = v; return true;
            case nameof(ExactArmAnchorLY): ExactArmAnchorLY = v; return true;
            case nameof(ExactArmAnchorLZ): ExactArmAnchorLZ = v; return true;
            case nameof(ExactArmWidth): ExactArmWidth = v; return true;
            case nameof(ExactArmBaseLength): ExactArmBaseLength = v; return true;
            case nameof(ExactArmStretchMin): ExactArmStretchMin = v; return true;
            case nameof(ExactArmStretchMax): ExactArmStretchMax = v; return true;
            case nameof(KnifeProfile): KnifeProfile = v; return true;
            case nameof(ExactArmTwistOffsetDegrees): ExactArmTwistOffsetDegrees = v; return true;
            case nameof(ExactHandX): ExactHandX = v; return true;
            case nameof(ExactScaleOverride): ExactScaleOverride = v; return true;
            case nameof(ExactHandY): ExactHandY = v; return true;
            case nameof(ExactHandZ): ExactHandZ = v; return true;
            case nameof(ExactMirrorX): ExactMirrorX = v; return true;
            case nameof(ExactMeshCenterOffset): ExactMeshCenterOffset = v; return true;
            case nameof(PbrEnabled): PbrEnabled = v; return true;
            case nameof(PbrEnvRange): PbrEnvRange = v; return true;
            case nameof(PbrEnvIntensity): PbrEnvIntensity = v; return true;
            case nameof(PbrGunEnvIntensity): PbrGunEnvIntensity = v; return true;
            case nameof(ScopeLinePx): ScopeLinePx = v; return true;
            case nameof(PbrDirectIntensity): PbrDirectIntensity = v; return true;
            case nameof(PbrExposure): PbrExposure = v; return true;
            case nameof(PbrNormalFlipY): PbrNormalFlipY = v; return true;
            case nameof(PbrRoughnessBias): PbrRoughnessBias = v; return true;
            case nameof(PbrEnvYawDegrees): PbrEnvYawDegrees = v; return true;
            case nameof(PbrEnvSaturation): PbrEnvSaturation = v; return true;
            case nameof(GunProfile): GunProfile = v; return true;
            case nameof(GunNumbers): GunNumbers = v; return true;
            case nameof(Cs2Arms): Cs2Arms = v; return true;
            case nameof(Cs2ViewmodelFov): Cs2ViewmodelFov = v; return true;
            case nameof(Cs2ViewmodelOffsetX): Cs2ViewmodelOffsetX = v; return true;
            case nameof(Cs2ViewmodelOffsetY): Cs2ViewmodelOffsetY = v; return true;
            case nameof(Cs2ViewmodelOffsetZ): Cs2ViewmodelOffsetZ = v; return true;
            case nameof(GunSoundProfile): GunSoundProfile = v; return true;
            case nameof(PbrDebug): PbrDebug = v; return true;
            default: return false;
        }
    }

    /// <summary>Writes the file with the current values so there is something to edit.</summary>
    public static void Write() {
        try {
            using Stream stream = Storage.OpenFile(Path, OpenFileMode.Create);
            byte[] bytes = new UTF8Encoding(false).GetBytes(Serialize(Version));
            stream.Write(bytes, 0, bytes.Length);
            KnifeLog.Information($"[ScCsgoKnives] wrote tuning file {Path}; edit it and it reloads within a second.");
        }
        catch (Exception e) {
            KnifeDiagnostics.WarnOnce("tuning-write", $"Could not write {Path}: {e.Message}");
        }
    }

    static string Serialize(int version) {
        var text = new StringBuilder();
        text.AppendLine("# ScCsgoKnives 第一人称调参");
        text.AppendLine("# 保存后 1 秒内在游戏里生效，不用重启，不用重装。");
        text.AppendLine("# 所有刀和枪固定使用 CS2 模型、动画和真实手。旧方块手开关已停用。");
        text.AppendLine($"TuningVersion = {version}");
        text.AppendLine(Line(nameof(SwapDipScale), SwapDipScale));
        text.AppendLine(Line(nameof(PbrEnabled), PbrEnabled));
        text.AppendLine(Line(nameof(PbrEnvRange), PbrEnvRange));
        text.AppendLine(Line(nameof(PbrEnvIntensity), PbrEnvIntensity));
        text.AppendLine(Line(nameof(PbrGunEnvIntensity), PbrGunEnvIntensity));
        text.AppendLine(Line(nameof(ScopeLinePx), ScopeLinePx));
        text.AppendLine(Line(nameof(PbrDirectIntensity), PbrDirectIntensity));
        text.AppendLine(Line(nameof(PbrExposure), PbrExposure));
        text.AppendLine(Line(nameof(PbrNormalFlipY), PbrNormalFlipY));
        text.AppendLine(Line(nameof(PbrRoughnessBias), PbrRoughnessBias));
        text.AppendLine(Line(nameof(PbrEnvYawDegrees), PbrEnvYawDegrees));
        text.AppendLine("# 反射带多少环境的颜色：1 = 全带（天空会把刀染蓝），0 = 只反亮度不反颜色。");
        text.AppendLine(Line(nameof(PbrEnvSaturation), PbrEnvSaturation));
        text.AppendLine("# CS2 腰射 viewmodel 设置；AUG / SG 553 开镜使用单独校准的镜筒视角。");
        text.AppendLine(Line(nameof(Cs2ViewmodelFov), Cs2ViewmodelFov));
        text.AppendLine(Line(nameof(Cs2ViewmodelOffsetX), Cs2ViewmodelOffsetX));
        text.AppendLine(Line(nameof(Cs2ViewmodelOffsetY), Cs2ViewmodelOffsetY));
        text.AppendLine(Line(nameof(Cs2ViewmodelOffsetZ), Cs2ViewmodelOffsetZ));
        text.AppendLine("# 玩法数值单独一个开关：0 = 一直以来的值，1 = CS2 vdata 的伤害/衰减/散布/后坐。");
        text.AppendLine("# 与 GunProfile 分开是有意的：把画面换成 CS2 不应该顺带改手感（CS2 的 AWP 不开镜散布是 4.63 度）。");
        text.AppendLine(Line(nameof(GunNumbers), GunNumbers));
        text.AppendLine("# 枪的换弹/拉栓/消音器音效用哪套时间：0 = 旧的按骨骼位移量出来的表，1 = CS2 自己的事件帧（cs2_sounds.json）。");
        text.AppendLine("# 两套最多差 0.58 秒，听得出来。默认 0，等对着 CS2 录屏核过再改默认。");
        text.AppendLine(Line(nameof(GunSoundProfile), GunSoundProfile));
        text.AppendLine(Line(nameof(PbrDebug), PbrDebug));
        return text.ToString();
    }

    static string Line(string key, float value) => $"{key} = {value.ToString("0.####", CultureInfo.InvariantCulture)}";
}
