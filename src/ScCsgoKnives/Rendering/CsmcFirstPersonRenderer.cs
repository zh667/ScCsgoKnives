using System.Collections.Generic;
using Engine;
using Engine.Graphics;
using Engine.Input;

namespace Game;

/// <summary>
/// Draws CSMC weapon records with their Source2 binding matrices, and puts
/// Survivalcraft's own first-person hand box around each knife's grip as a fist.
///
/// The composition is what the CS:MC photos show, measured off them per knife
/// (tools/fistsolve.py, tools/fistfit.py): a face-on box arm whose axis runs from
/// a fixed off-screen elbow to the grip, with the knife's handle passing through
/// the box's centre and the box reaching most of its own width past the grip, so
/// only the guard and the pommel show on either side of the fist. The knife's own
/// rig moves the grip; the arm pivots about its elbow to follow and rolls with the
/// wrist so the handle stays along one face of the fist (ResolveRoll).
/// </summary>
public static class CsmcFirstPersonRenderer {
    sealed class Part {
        public string Binding;
        public Model Model;
    }

    // The composition is anchored on the idle grip rather than on the weapon
    // record's own origin. CSMC appends its weapon matrix to Minecraft's
    // first-person hand matrix, which we do not have; using the record origin
    // as the anchor instead pushed the whole rig metres down the view axis.
    // Both measured off the reference client: its grip sits at screen
    // (0.64, 0.80) with the knife spanning 29% of the frame width, where ours
    // sat at (0.78, 0.71) and 21%. The anchor moves the whole composition down
    // and left to match, which also carries the left hand out to the frame edge
    // where CS:MC keeps it.
    // Live values; see KnifeTuning for the file that drives them.

    // CSMC normalises every mesh record to the same 1.25 unit box and then
    // applies a per-weapon scale we cannot read out of the obfuscated jar.
    // SourceReferenceScale is the untouched extent of the record, so scaling by
    // it against the fleet mean restores the real size relationship: an M9
    // bayonet is half again the length of a karambit.
    const float ReferenceSourceScale = 13.618f;

    // Models/FirstPersonHand is eight vertices: a 7.87 x 7.87 x 27.56 box that
    // vanilla draws at 0.01, so 0.0787 across and 0.2756 long, gripping end at
    // local -Z. KnifeTuning.ArmScreenWidth widens it to the fraction of the frame
    // CS:MC's arm actually covers, resolved against the live projection.
    const float HandModelScale = 0.01f;
    const float HandBoxLength = 0.27559f;
    const float HandBoxWidth = 0.07874f;


    // Never let the arm shrink to a stub when the hand swings near the
    // entrance; the surplus runs off-screen behind the shoulder.


    // Sink the grip this far into the palm face so the handle reads as held
    // rather than balanced on the surface. A little clipping is what the
    // reference client shows too.


    // CSMC b$4ll enters at (0.58,-0.78,-0.70) and (-0.70,-0.82,-0.72). SC
    // renders first person at 80 degrees vertical against Minecraft's 70, so
    // both are pushed out by the tangent ratio to stay as far off screen.


    // A usable forearm bone is 0.66 to 1.86 long across the fleet; the kukri
    // converts to 0.22, so its hand bones sit nowhere near its mesh and its
    // left hand lands above the top of the screen.
    //
    // This is measured in the rig's own unscaled bone space, so anywhere it is
    // compared against a placed forearm the placement scale has to be divided back
    // out first. Comparing it directly against view-space lengths made the test
    // depend on KnifeTuning.KnifeScale: at 0.42 every knife silently failed it and
    // fell back to the fitted lean, which looked like a working bone-driven arm.
    const float MinUsableForearm = 0.4f;

    // Where each hand holds its weapon, in that hand's own bone frame: the point on
    // the knife that sits at the centre of the fist. Solved by tools/fistsolve.py
    // against the CS:MC photos -- the whole visible silhouette of each knife, not
    // just its tip -- for the four knives that have photos, as the mesh-slab
    // centroid at an axial fraction f of the knife (0 = butt). The three straight
    // knives solve to f = 0.19..0.27, the middle of the handle, so the rest share
    // 0.235; the karambit solves to 0.32, the middle of its handle too, with the
    // ring left of the fist and the blade right of it exactly as its photo shows.
    //
    // 0.10.0 had the karambit at f = 0.05, the ring, from a tip-distance-only solve
    // that could not tell "small knife held by the ring" from "big knife held by
    // the handle". The silhouette can.
    static readonly Vector3[] s_knifeGripOffsets = [
        new(0.0471f, 0.0544f, -0.0904f),    // karambit  (solved f=0.323)
        new(0.2157f, 0.0669f, -0.3250f),    // m9  (solved f=0.190)
        new(0.1953f, 0.0845f, -0.2164f),    // butterfly  (solved f=0.270)
        new(0.2615f, 0.0698f, -0.2780f),    // bayonet  (shared f=0.235)
        new(0.1736f, 0.0795f, -0.2732f),    // bowie  (shared f=0.235)
        new(0.1873f, 0.0590f, -0.2479f),    // canis  (shared f=0.235)
        new(0.2282f, 0.0698f, -0.3027f),    // cord  (shared f=0.235)
        new(0.2721f, 0.0610f, -0.1989f),    // css  (shared f=0.235)
        new(0.1994f, 0.0710f, -0.2525f),    // default_ct  (shared f=0.235)
        new(0.2508f, 0.0725f, -0.1787f),    // default_t  (shared f=0.235)
        new(0.1979f, 0.1020f, -0.2768f),    // falchion  (shared f=0.235)
        new(0.2424f, 0.0811f, -0.2460f),    // flip  (shared f=0.235)
        new(0.1466f, 0.0717f, -0.2382f),    // gut  (shared f=0.235)
        new(0.6512f, 0.5096f, -0.9796f),    // kukri  (kept)
        new(0.2191f, 0.1114f, -0.2745f),    // navaja  (shared f=0.235)
        new(0.1805f, 0.0797f, -0.2563f),    // outdoor  (shared f=0.235)
        new(0.7868f, 0.1805f, -0.0681f),    // push  (kept)
        new(0.2553f, 0.0797f, -0.2585f),    // skeleton  (shared f=0.235)
        new(0.2253f, 0.0935f, -0.2796f),    // stiletto  (shared f=0.235)
        new(0.1502f, 0.0630f, -0.2564f),    // tactical  (solved f=0.244)
        new(0.0364f, 0.0450f, -0.2201f),    // talon  (claw f=0.323 like the karambit)
        new(0.2011f, 0.0731f, -0.2593f),    // ursus  (shared f=0.235)
    ];

    // Which way the handle runs, butt to tip, in the HELD MESH PART's own space
    // (weapon_hand_r), placed by that part's binding each frame -- not the hand
    // bone's: the inspects turn the knife in the fingers, and at the hold pose the
    // M9's part sits 32 degrees off hand_r, the karambit's 27. Generated by
    // tools/knife_hulls.py as the principal axis of the gripped stretch. Roll mode 2
    // turns the fist so this lies flat along one of its faces.
    const string HeldPart = "weapon_hand_r";
    const string LeftHeldPart = "weapon_hand_l";
    /// <summary>The butterfly's T latch; hidden by default, see KnifeTuning.ShowButterflyLatch.</summary>
    const string LatchPart = "v_weapon_lock";
    /// <summary>The M4A1-S silencer mesh part (CS:MC binding name).</summary>
    const string SilencerPart = "v_weapon_silencer";
    /// <summary>
    /// Parts drawn with their own material. The AWP's second body record (48 faces, UVs in the
    /// u 1..2 tile) is the scope lens: on the gun's texture it showed the substrate's wrapped
    /// pixels as a circuit-board pattern. It gets CS2's shared scope lens material instead
    /// (scope_awp.vmat: default colour, metalness 0, scope roughness).
    /// </summary>
    static readonly Dictionary<(string Asset, string Part), string> s_partMaterials = new() {
        [("awp", "weapon_hand_r__2")] = "awp_lens",
    };
    static readonly Dictionary<string, Texture2D> s_partBases = new(StringComparer.Ordinal);
    static string PartMaterial(int variant, string part) => s_partMaterials.TryGetValue((s_assetNames[variant], part), out string m) ? m : null;
    static Texture2D PartBaseTexture(string material) {
        if (!s_partBases.TryGetValue(material, out Texture2D t)) {
            try { t = ContentManager.Get<Texture2D>($"Textures/ScCsgoKnives/{material}"); }
            catch (Exception e) { KnifeDiagnostics.WarnOnce($"part-material-{material}", $"material {material} missing ({e.Message})"); t = null; }
            s_partBases[material] = t;
        }
        return t;
    }

    /// <summary>
    /// The silencer part is skipped once the block data says it is off. The detach clip
    /// carries it away and commits "off" at its end; the attach clip brings it back while
    /// the data still says "off", so it is drawn during that clip. 0.15.x drew it always,
    /// so it snapped back onto the barrel the moment the detach clip ended.
    /// </summary>
    static bool SilencerHidden(ComponentFirstPersonModel firstPerson, int variant) {
        if (!CsmcKnifeRig.IsGun(variant)) return false;
        GunSpec spec = GunSpec.ForAsset(CsmcKnifeRig.GetAssetName(variant));
        if (spec is null || !spec.HasSilencer) return false;
        ComponentMiner miner = firstPerson?.Entity.FindComponent<ComponentMiner>();
        if (miner is null) return false;
        int value = miner.ActiveBlockValue;
        if (Terrain.ExtractContents(value) != BlocksManager.GetBlockIndex<ScGunBlock>()) return false;
        return GunSpec.GetSilencerOff(Terrain.ExtractData(value)) && !KnifeAnimationController.IsAttaching(firstPerson);
    }
    /// <summary>Knife handle direction; guns have no fist-solver handle (their arms follow the bones).</summary>
    static Vector3 HandleDirection(int variant) => variant < s_handleDirections.Length && !CsmcKnifeRig.IsGun(variant) ? s_handleDirections[variant] : Vector3.Zero;
    static readonly Vector3[] s_handleDirections = [
        new(-0.0744f, 0.0028f, 0.9972f),      // karambit  (28 deg off the whole knife)
        new(0.0000f, 0.0000f, 1.0000f),       // m9  (1 deg off the whole knife)
        new(-0.1426f, -0.0000f, 0.9898f),     // butterfly  (5 deg off the whole knife)
        new(-0.0000f, -0.0000f, 1.0000f),     // bayonet  (1 deg off the whole knife)
        new(-0.0093f, 0.0008f, 1.0000f),      // bowie  (2 deg off the whole knife)
        new(-0.1881f, -0.0018f, 0.9821f),     // canis  (11 deg off the whole knife)
        new(0.0145f, -0.0000f, 0.9999f),      // cord  (4 deg off the whole knife)
        new(-0.0105f, 0.0000f, 0.9999f),      // css  (1 deg off the whole knife)
        new(-0.0000f, 0.0379f, 0.9993f),      // default_ct  (2 deg off the whole knife)
        new(-0.0000f, 0.0000f, 1.0000f),      // default_t  (0 deg off the whole knife)
        new(-0.2202f, -0.0150f, 0.9753f),     // falchion  (10 deg off the whole knife)
        new(-0.0426f, 0.0000f, 0.9991f),      // flip  (2 deg off the whole knife)
        new(-0.0634f, 0.0029f, 0.9980f),      // gut  (5 deg off the whole knife)
        new(-0.8213f, 0.4120f, -0.3947f),     // kukri  (kept)
        new(-0.2874f, 0.0018f, 0.9578f),      // navaja  (11 deg off the whole knife)
        new(-0.1178f, 0.0000f, 0.9930f),      // outdoor  (2 deg off the whole knife)
        new(0.9692f, 0.2251f, -0.1000f),      // push  (kept)
        new(-0.0118f, -0.0029f, 0.9999f),     // skeleton  (1 deg off the whole knife)
        new(-0.0111f, 0.0805f, 0.9967f),      // stiletto  (5 deg off the whole knife)
        new(0.0390f, 0.0000f, 0.9992f),       // tactical  (3 deg off the whole knife)
        new(0.1420f, -0.0054f, 0.9898f),      // talon  (12 deg off the whole knife)
        new(-0.1668f, -0.0018f, 0.9860f),     // ursus  (8 deg off the whole knife)
    ];
    // Only the shadow daggers put a knife in the left hand; every other left hand is empty.
    static readonly Vector3 ShadowDaggerLeftHandle = new(0.9856f, 0.1088f, -0.1298f);
    static readonly Vector3[] s_leftHandleDirections = Enumerable.Range(0, CsmcKnifeRig.AssetCount)
        .Select(v => CsmcKnifeRig.GetAssetName(v) == "push" ? ShadowDaggerLeftHandle : Vector3.Zero)
        .ToArray();

    // Only the shadow daggers put a weapon record in the left hand; every other
    // knife leaves it empty, so that hand is drawn at its own bone with no
    // offset. Having no left offset at all is what stopped the second dagger
    // from ever reaching its hand.
    static readonly Vector3 ShadowDaggerLeftGrip = new(0.5970f, 0.1456f, -0.2224f);
    // Guns hold their grip at the hand bone itself: CS:MC's animation already puts hand_r on
    // the pistol grip and hand_l on the handguard, so there is nothing to solve for.
    static readonly Vector3[] s_gripOffsets = Enumerable.Range(0, CsmcKnifeRig.AssetCount)
        .Select(v => v < s_knifeGripOffsets.Length && !CsmcKnifeRig.IsGun(v) ? s_knifeGripOffsets[v] : Vector3.Zero)
        .ToArray();
    static readonly Vector3[] s_leftGripOffsets = Enumerable.Range(0, CsmcKnifeRig.AssetCount)
        .Select(v => CsmcKnifeRig.GetAssetName(v) == "push" ? ShadowDaggerLeftGrip : Vector3.Zero)
        .ToArray();

    // The lookat clips lift the wrist about a quarter of the screen height,
    // which reads as raising the knife into your own face at SC's framing.
    // Damping is a rigid pull back towards the idle grip, so the knife still
    // turns over exactly as authored; only how far the rig travels changes.


    static readonly int s_count = CsmcKnifeRig.AssetCount;
    static readonly string[] s_assetNames = Enumerable.Range(0, s_count).Select(CsmcKnifeRig.GetAssetName).ToArray();

    /// <summary>
    /// Where a knife's fist sits and how it is drawn, measured off that knife's
    /// CS:MC photo. NaN means "use the tuning file's shared value".
    /// </summary>
    readonly struct FistSpec {
        public FistSpec(float anchorX, float anchorY, float lean, float overshoot, float scale,
                        float leftX = float.NaN, float leftY = float.NaN, float leftLean = float.NaN,
                        float leftNear = float.NaN, float leftWidth = float.NaN,
                        float near = float.NaN, float width = float.NaN) {
            AnchorX = anchorX; AnchorY = anchorY; Lean = lean; Overshoot = overshoot; Scale = scale;
            LeftX = leftX; LeftY = leftY; LeftLean = leftLean; LeftNear = leftNear; LeftWidth = leftWidth;
            Near = near; Width = width;
        }
        /// <summary>The right arm's taper and screen width, when the photo differs from the shared ones.</summary>
        public readonly float Near, Width;
        /// <summary>Screen fraction where the grip -- the fist's centre -- rests at idle.</summary>
        public readonly float AnchorX, AnchorY;
        /// <summary>The right arm's bearing, degrees from straight down, + leaning right.</summary>
        public readonly float Lean;
        /// <summary>How far the fist reaches past the grip, as a fraction of the arm's width.</summary>
        public readonly float Overshoot;
        /// <summary>Per-weapon scale on top of KnifeTuning.KnifeScale; CS:MC has one per weapon too.</summary>
        public readonly float Scale;
        /// <summary>Idle left-hand screen position and left arm bearing, taper and width, when the photo differs from the shared ones.</summary>
        public readonly float LeftX, LeftY, LeftLean, LeftNear, LeftWidth;
    }

    // Measured per knife by tools/fistfit.py: the fist box is fitted to the photo's
    // arm silhouette (IoU 0.88..0.95), the grip is where the handle's centre line
    // crosses the box's axis. The straight knives agree with each other to within a
    // few percent of the frame and share the tuning defaults; the karambit does not:
    // CS:MC animates its arms per weapon, and for the reverse-grip claw it moves the
    // whole right arm a tenth of the frame left and leans it 35 degrees. The talon
    // is the other claw and has no photo, so it borrows the karambit's.
    static readonly FistSpec s_sharedFist = new(float.NaN, float.NaN, float.NaN, float.NaN, 1.03f);
    // The claw's left arm is different too: a third nearer the eye at the elbow end,
    // so it widens to twice its cap by the bottom of the frame.
    static readonly FistSpec s_clawFist = new(0.6443f, 0.8120f, 36.0f, 0.82f, 1.18f, 0.2677f, 0.8378f, -54.0f, 1.65f, 0.097f, 1.57f, 0.0855f);
    static readonly Dictionary<string, FistSpec> s_measuredFists = new() {
        ["m9"] = new(0.7099f, 0.8426f, 8.5f, 0.89f, 1.00f),
        ["butterfly"] = new(0.7234f, 0.8065f, 5.0f, 0.42f, 1.045f),
        ["tactical"] = new(0.7224f, 0.8426f, 7.0f, 0.74f, 1.045f),
        ["karambit"] = s_clawFist,
        ["talon"] = s_clawFist,
    };
    static readonly FistSpec[] s_fist = s_assetNames
        .Select(name => s_measuredFists.TryGetValue(name, out FistSpec spec) ? spec : s_sharedFist)
        .ToArray();

    static float Or(float value, float fallback) => float.IsNaN(value) ? fallback : value;

    /// <summary>The view-space point this knife's idle grip is pinned to.</summary>
    static Vector3 AnchorFor(int variant) => ToViewSpace(
        Or(s_fist[variant].AnchorX, KnifeTuning.AnchorScreenX),
        Or(s_fist[variant].AnchorY, KnifeTuning.AnchorScreenY),
        KnifeTuning.AnchorDepth);

    static float LeanFor(int variant, bool left) => left
        ? Or(s_fist[variant].LeftLean, KnifeTuning.LeftArmLean)
        : Or(s_fist[variant].Lean, KnifeTuning.RightArmLean);

    static float OvershootFor(int variant, bool left) =>
        left ? KnifeTuning.ArmPalmOvershoot : Or(s_fist[variant].Overshoot, KnifeTuning.ArmPalmOvershoot);

    static float NearFor(int variant, bool left) => left
        ? Or(s_fist[variant].LeftNear, KnifeTuning.LeftArmNear)
        : Or(s_fist[variant].Near, KnifeTuning.RightArmNear);

    static float ScreenWidthFor(int variant, bool left) => left
        ? Or(s_fist[variant].LeftWidth, KnifeTuning.LeftArmScreenWidth)
        : Or(s_fist[variant].Width, KnifeTuning.ArmScreenWidth);

    static (float X, float Y) LeftTargetFor(int variant) => (
        Or(s_fist[variant].LeftX, KnifeTuning.LeftHandTargetScreenX),
        Or(s_fist[variant].LeftY, KnifeTuning.LeftHandTargetScreenY));
    static readonly Part[][] s_parts = new Part[s_count][];
    static readonly Texture2D[] s_baseColor = new Texture2D[s_count];
    static readonly Matrix[] s_placement = new Matrix[s_count];
    static readonly Vector3[] s_idleGrips = new Vector3[s_count];
    // The right grip in the HELD MESH PART's frame (weapon_hand_r), converted from
    // the hand-bone table at idle, and whether that part exists for the knife. The
    // fist is attached to the part, not to hand_r: the CS:GO inspects move the knife
    // relative to the hand bone (the M9's part turns 57 degrees and shifts 0.28
    // units mid-inspect), and a fist left on hand_r fell behind the knife -- "the
    // knife moves faster than the hand". CS:MC's fist follows its weapon bone, and
    // in its recordings the fist's cap stays on the handle's base throughout.
    static readonly Vector3[] s_gripOffsetsPart = new Vector3[s_count];
    static readonly bool[] s_heldPartUsable = new bool[s_count];

    /// <summary>The bone the right fist is attached to for this knife.</summary>
    static string RightWristBone(int variant) => s_heldPartUsable[variant] ? HeldPart : "hand_r";

    /// <summary>The right grip in that bone's frame.</summary>
    static Vector3 RightGrip(int variant) => s_heldPartUsable[variant] ? s_gripOffsetsPart[variant] : s_gripOffsets[variant];

    /// <summary>Converts the hand-frame grip into the held part's frame, using their idle relation.</summary>
    static void SolveHeldPartGrip(int variant) {
        KnifeRigPose idle = CsmcKnifeRig.Sample(variant, "idle", 0f);
        s_heldPartUsable[variant] = false;
        s_gripOffsetsPart[variant] = s_gripOffsets[variant];
        if (!CsmcKnifeRig.GetMeshParts(variant).Contains(HeldPart)) return;
        Matrix hand = idle.GetBinding("hand_r");
        Matrix part = idle.GetBinding(HeldPart);
        Matrix partInverse = Matrix.Invert(part);
        if (!KnifeDiagnostics.IsFinite(partInverse)) return;
        Vector3 inPart = Vector3.Transform(s_gripOffsets[variant], hand * partInverse);
        if (!float.IsFinite(inPart.X) || !float.IsFinite(inPart.Y) || !float.IsFinite(inPart.Z)) return;
        s_gripOffsetsPart[variant] = inPart;
        s_heldPartUsable[variant] = true;
    }
    static readonly bool[] s_logged = new bool[s_count];
    // Where each knife's idle left grip lands in view space once corrected; the
    // left elbow is projected down from here so the left arm pivots rather than
    // slides when a clip moves the hand.
    static readonly Vector3[] s_idleLeftGrips = new Vector3[s_count];

    // Each arm's idle face-on side, carried in that arm's hand bone frame, and the
    // side it was drawn with last frame. Two slots per knife: [variant * 2] right,
    // [variant * 2 + 1] left. See ResolveRoll.
    static readonly Vector3[] s_rollRef = new Vector3[s_count * 2];
    static readonly Vector3[] s_lastSide = new Vector3[s_count * 2];
    // Per slot, the roll the rigid carry rests at in each clip that holds (radians,
    // by clip alias); the square-to-eye ramps to exactly this. See MeasureHolds.
    static readonly Dictionary<string, float>[] s_holdAngles = new Dictionary<string, float>[s_count * 2];
    // Set while MeasureHolds plays the clips through SolveArm: ResolveRoll then
    // returns the rigid side untouched, with no squaring, slew or state.
    static bool s_measuring;
    // Last frame's clearance off the grip, so the fist slides to follow a handle
    // the fingers are turning rather than jumping to it.
    static readonly float[] s_lastClearance = new float[s_count * 2];
    // The followed handle direction in the hand's frame and last frame's raw one; see FollowHandle.
    static readonly Vector3[] s_handleFollow = new Vector3[s_count * 2];
    static readonly Vector3[] s_handlePrev = new Vector3[s_count * 2];
    // The handle direction the idle pose gives in the hand's frame -- the rigid
    // handle 0.11.2 laid flat -- and the handle's half-thickness, from its hull.
    static readonly Vector3[] s_handleIdleHand = new Vector3[s_count * 2];
    static readonly float[] s_handleRadius = new float[s_count];
    // The re-grip correction currently applied to each arm's roll, radians; see ResolveRoll.
    static readonly float[] s_reGrip = new float[s_count * 2];
    // Which way round the handle-flat direction is taken (+1/-1), which side of the
    // knife the box sits (a direction, orbiting the knife when that changes), the
    // squaring-to-the-eye correction in use, and the wrist's stillness; see ResolveRoll.
    static readonly float[] s_flatSign = new float[s_count * 2];
    static readonly Vector3[] s_offsetDir = new Vector3[s_count * 2];
    static readonly float[] s_square = new float[s_count * 2];
    static readonly float[] s_stillness = new float[s_count * 2];
    static readonly Vector3[] s_prevPalm = new Vector3[s_count * 2];
    const float ClearanceSlewPerSecond = 0.6f;   // view units; about four fist widths a second
    // The inverse of each arm's idle hand matrix, so a frame can tell how far the
    // wrist has turned from its idle pose.
    static readonly Matrix[] s_idleWristInverse = new Matrix[s_count * 2];

    // Where the wrist-carried roll fades out into face-on, in units of how much of
    // the carried direction survives projection off the arm's axis. Below
    // RollFadeStart the two are parallel enough that the roll means nothing.
    const float RollFadeStart = 0.06f;
    const float RollFadeEnd = 0.28f;
    // How fast the box may travel round the knife to its other side, degrees a second.
    const float OrbitDegreesPerSecond = 900f;

    /// <summary>The angle from a to b about axis, both perpendicular to it, in (-pi, pi].</summary>
    static float SignedAngle(Vector3 a, Vector3 b, Vector3 axis) =>
        MathF.Atan2(Vector3.Dot(Vector3.Cross(a, b), axis), Vector3.Dot(a, b));

    /// <summary>v turned about axis by angle, kept perpendicular to the axis.</summary>
    static Vector3 Turn(Vector3 v, Vector3 axis, float angle) =>
        ProjectOntoPlane(v * MathF.Cos(angle) + Vector3.Cross(axis, v) * MathF.Sin(angle), axis);
    static readonly bool[] s_leftArmUsable = BuildLeftArmUsable();

    /// <summary>
    /// A knife whose forearm bone converts to almost nothing has hand bones
    /// that sit nowhere near its mesh, and its left hand ends up above the top
    /// of the screen. Checked once, at load, rather than guessed per frame.
    /// </summary>
    static bool[] BuildLeftArmUsable() {
        var usable = new bool[CsmcKnifeRig.AssetCount];
        for (int variant = 0; variant < usable.Length; variant++) {
            KnifeRigPose idle = CsmcKnifeRig.Sample(variant, "idle", 0f);
            float forearm = (idle.GetBindingOrigin("hand_l") - idle.GetBindingOrigin("arm_lower_l")).Length();
            usable[variant] = forearm >= MinUsableForearm || CsmcKnifeRig.IsGun(variant);
            if (!usable[variant]) {
                KnifeLog.Warning($"[ScCsgoKnives] {CsmcKnifeRig.GetAssetName(variant)} has a degenerate left forearm ({forearm:0.###}); left arm disabled.");
            }
        }
        return usable;
    }
    static bool s_handsLogged;
    static bool s_loaded;

    // The anchor is stored as a screen position because Survivalcraft's field of
    // view is 80 * SettingsManager.ViewAngle and the aspect follows the window.
    // Turning it into a view-space point needs the live projection, so it is
    // recomputed whenever that projection changes rather than baked in at load.
    static Vector3 s_handAnchor = new(0.4341f, -0.3135f, -0.72f);
    // Survivalcraft's default: 80 degrees vertical at 16:9 (BasePerspectiveCamera
    // uses 80 * SettingsManager.ViewAngle). Seeded so the load-time diagnostics can
    // measure the arm before a camera exists; SyncProjection replaces it on frame one.
    static float s_projX = 0.670359f, s_projY = 1.191754f;
    public static float ProjX => s_projX;
    public static float ProjY => s_projY;

    /// <summary>Forces the screen-space anchor to be resolved again on the next frame.</summary>
    public static void InvalidateProjection() => s_projX = 0f;   // any value the camera cannot report

    /// <summary>Lets the composition report print again after a tuning change.</summary>
    public static void ResetCompositionLog() => Array.Clear(s_compositionNextLog);

    /// <summary>
    /// Keeps the view-space anchor in step with the camera. Returns true when it
    /// moved, so the caller knows the placements need rebuilding.
    /// </summary>
    /// <summary>
    /// The projection the composition is solved and drawn in: the game camera's, or in
    /// exact mode the weapon FOV at the camera's aspect. Screen-space tunables (anchors,
    /// arm widths) resolve against this, so the fists keep their on-screen size and
    /// place whatever the knife's projection is.
    /// </summary>
    static (float fx, float fy) EffectiveProjection(float cameraFx, float cameraFy) {
        if (!Exact) return (cameraFx, cameraFy);
        float fy = 1f / MathF.Tan(MathUtils.DegToRad(s_weaponFov) * 0.5f);
        return (fy * cameraFx / cameraFy, fy);
    }

    static bool SyncProjection(Camera camera) {
        float cameraFx = camera.ProjectionMatrix.M11, cameraFy = camera.ProjectionMatrix.M22;
        if (!float.IsFinite(cameraFx) || !float.IsFinite(cameraFy) || cameraFx <= 0.0001f || cameraFy <= 0.0001f) return false;
        (float fx, float fy) = EffectiveProjection(cameraFx, cameraFy);
        if (fx == s_projX && fy == s_projY) return false;
        s_projX = fx;
        s_projY = fy;
        s_handAnchor = ToViewSpace(KnifeTuning.AnchorScreenX, KnifeTuning.AnchorScreenY, KnifeTuning.AnchorDepth);
        KnifeLog.Information(
            $"[ScCsgoKnives] projection changed (fx={fx:0.####}, fy={fy:0.####}, vertical fov={2f * MathF.Atan(1f / fy) * 180f / MathF.PI:0.#}deg); "
            + $"anchor ({KnifeTuning.AnchorScreenX:0.###},{KnifeTuning.AnchorScreenY:0.###}) resolves to {Format(s_handAnchor)}."
        );
        return true;
    }

    // Per-knife view-space correction that puts each knife's idle left hand on the
    // reference position. Computed at placement time, applied every frame, so the
    // animation still moves the hand -- only its resting point is pinned, the same
    // way the right hand's grip is pinned to the anchor.
    static readonly Vector3[] s_leftHandCorrection = new Vector3[s_count];
    static readonly Vector3[] s_exactIdleGrips = new Vector3[s_count];

    /// <summary>
    /// Where the idle left grip would land, and the view-space delta that moves it
    /// onto the tuned target. Screen-space target for the same reason the anchor is:
    /// the field of view is a player setting.
    /// </summary>
    static void SolveLeftHandCorrection(int variant) {
        s_leftHandCorrection[variant] = Vector3.Zero;
        if (!s_leftArmUsable[variant]) return;
        Matrix idleHand = CsmcKnifeRig.Sample(variant, "idle", 0f).GetBinding("hand_l");
        Vector3 grip = Vector3.Transform(s_leftGripOffsets[variant], idleHand * s_placement[variant]);
        s_idleLeftGrips[variant] = grip;
        float depth = -grip.Z;
        if (!(depth > 0.01f) || !float.IsFinite(depth)) return;
        // Pinned in depth as well as on screen. The rig's own hand_l sits anywhere
        // from 0.44 to 0.98 deep across the fleet, and the arm's overshoot is a 3D
        // length along an axis that mostly runs in depth, so at 0.98 the fist's cap
        // landed 0.02 of the frame short of where the same box put it at 0.55 -- the
        // depth the reference arm was fitted at. Nothing is held in that hand, so
        // its depth is free to be whatever the fit used.
        (float targetX, float targetY) = LeftTargetFor(variant);
        Vector3 target = ToViewSpace(targetX, targetY, KnifeTuning.LeftHandDepth);
        Vector3 delta = target - grip;
        if (KnifeDiagnostics.IsFinite(new Matrix { M41 = delta.X, M42 = delta.Y, M43 = delta.Z, M44 = 1f })) {
            s_leftHandCorrection[variant] = delta;
            s_idleLeftGrips[variant] = target;
        }
    }

    static Vector3 ToViewSpace(float screenX, float screenY, float depth) =>
        new((screenX - 0.5f) * 2f * depth / s_projX, (0.5f - screenY) * 2f * depth / s_projY, -depth);

    /// <summary>
    /// The elbow: straight down the screen from the idle grip at the arm's fixed
    /// lean, far enough to leave the frame, and nearer the eye than the hand so the
    /// arm widens toward the bottom the way the references do.
    /// </summary>
    static Vector3 ProjectDownArm(Vector3 grip, float leanDegrees, float near) {
        float depth = -grip.Z;
        if (!(depth > 0.01f)) return grip + new Vector3(0f, -1f, 0f);
        float screenX = grip.X * s_projX / depth * 0.5f + 0.5f;
        float screenY = 0.5f - grip.Y * s_projY / depth * 0.5f;
        float aspect = s_projY / s_projX;
        float lean = MathUtils.DegToRad(leanDegrees);
        float stepX = MathF.Sin(lean) / aspect, stepY = MathF.Cos(lean);
        float run = (KnifeTuning.ArmExitY - screenY) / MathF.Max(stepY, 0.05f);
        run = MathUtils.Clamp(run, 0.05f, 4f);
        return ToViewSpace(screenX + stepX * run, screenY + stepY * run, depth / MathUtils.Max(near, 0.1f));
    }

    /// <summary>
    /// The bearing of this knife's forearm bone on screen, in the same degrees-from-
    /// straight-down convention KnifeTuning's fallback leans use.
    ///
    /// Only a short step down the bone is projected, not the bone's far end: the far
    /// end is routinely behind the eye, where the projection is meaningless. Falls
    /// back to the fitted lean for a knife whose forearm bone converts to nothing --
    /// the kukri's is a fifth the length of everyone else's.
    /// </summary>
    static float ForearmScreenLean(Vector3 grip, Vector3 elbow, float rigScale, string asset, string bone, bool left) {
        float fallback = left ? KnifeTuning.LeftArmLean : KnifeTuning.RightArmLean;
        Vector3 span = elbow - grip;
        float forearm = span.Length();
        // forearm is in view units; MinUsableForearm is in the rig's own units.
        if (!float.IsFinite(forearm) || forearm < MinUsableForearm * MathF.Max(rigScale, 0.0001f)) {
            KnifeDiagnostics.WarnOnce($"arm-bone-degenerate-{asset}-{bone}",
                $"{asset} arm_lower_{bone} is {forearm:0.###} long; using the fitted lean instead.");
            return fallback;
        }
        Vector2? hand = ToScreen(grip);
        if (hand is null) return fallback;
        // Step only as far as stays comfortably in front of the eye.
        Vector3 axis = span / forearm;
        float step = forearm;
        float floorDepth = -grip.Z * 0.5f;
        for (int i = 0; i < 12 && -(grip + axis * step).Z < floorDepth; i++) step *= 0.5f;
        Vector2? far = ToScreen(grip + axis * step);
        if (far is null) return fallback;
        float dx = (far.Value.X - hand.Value.X) * (s_projY / s_projX);
        float dy = far.Value.Y - hand.Value.Y;
        if (dx * dx + dy * dy < 1e-8f) return fallback;
        float lean = MathUtils.RadToDeg(MathF.Atan2(dx, dy));
        // An arm pointing up the screen is a bone we do not understand; keep it down.
        return MathF.Abs(lean) > 100f ? fallback : lean;
    }

    /// <summary>
    /// Returns false when this variant has no usable assets, so the caller can
    /// leave Survivalcraft's own item rendering in place instead of showing
    /// nothing at all.
    /// </summary>
    /// <summary>
    /// The knife's placement for this pose: the idle placement, pulled back
    /// toward the idle grip through an inspect by 1 - InspectTravelScale. One
    /// function for the draw, the hold measurement and the headless sweep --
    /// measured without the pullback, the hold came out 4 degrees short of what
    /// the draw reached, and the fist pinned to straight-behind before the knife
    /// had stopped.
    /// </summary>
    /// <summary>CS:MC's own transform chain instead of the fitted composition (KnifeTuning.ExactChain).</summary>
    static bool Exact => KnifeTuning.ExactChain > 0.5f;
    /// <summary>CS:MC's stretched arm boxes instead of the fist solver; off by default (the player preferred the fists).</summary>
    static bool ExactArms => Exact && KnifeTuning.ExactArms > 0.5f;
    /// <summary>Guns: both hands ride their bones (grip and handguard), so they always use CS:MC's arm frame.</summary>
    static bool BoneArms(int variant) => Exact && (KnifeTuning.ExactArms > 0.5f || CsmcKnifeRig.IsGun(variant));

    /// <summary>
    /// The knife's view-space placement exactly as CS:MC 5.10 builds it (reverse
    /// engineered, CSMCReverse/work/firstperson-chain.md): the knife family's hip
    /// offset and roll, then the fixed weapon transform translate(-0.22,0.42,-0.18)
    /// Rx90 Ry180 Rz270, scaled by this knife's meshbin reference scale over the
    /// AK-47's. Engine row-vector order, so the chain reads right to left.
    /// </summary>
    /// <summary>0 = hip, 1 = aimed down the sights; drives the table's hip/aim offset and FOV blend (b$4ap, b$4au).</summary>
    public static float AimProgress;
    static bool s_scoped;
    static float s_scopeMagnification = 1f;
    static double s_flashUntil = -1, s_flashStart = -1, s_smokeUntil = -1;
    static float s_flashSeconds = 0.06f;
    static string s_flashBone = "muzzle";
    static float s_flashRoll;
    static Texture2D s_scopeTexture, s_fireAtlas, s_smokeAtlas;
    /// <summary>CS:MC's muzzle flash sprites (CSMCTextureResources: particle/muzzle_flash/fire_gas_seq0 and wispy_steam_seq3), 8 columns per atlas.</summary>
    const int FireFrames = 32, SmokeFrames = 26, AtlasColumns = 8, AtlasRows = 4;
    const float SmokeSeconds = 0.45f;
    static PrimitivesRenderer2D s_primitives2D;
    static PrimitivesRenderer3D s_primitives3D;
    /// <summary>Seconds the aim blend takes from hip to sights (CS:GO's scope-in is a quarter second).</summary>
    const float AimSeconds = 0.25f;

    /// <summary>Scope on: the weapon eases into its aim pose and FOV, then the scope overlay replaces it.</summary>
    public static void SetScope(bool on, float magnification) {
        s_scoped = on;
        s_scopeMagnification = magnification;
    }

    /// <summary>Show the muzzle flash at the given bone for a few frames.</summary>
    public static void MuzzleFlash(float seconds, string bone) {
        s_flashStart = KnifeClock.Now;
        s_flashSeconds = MathF.Max(seconds, 0.001f);
        s_flashUntil = s_flashStart + seconds;
        s_smokeUntil = s_flashUntil + SmokeSeconds;
        s_flashBone = bone;
        s_flashRoll = new Random().Float(0f, MathF.PI * 2f);
    }

    static void AdvanceAim() {
        float dt = MathUtils.Clamp(KnifeClock.Dt, 0f, 0.1f);
        float target = s_scoped ? 1f : 0f;
        AimProgress = MathUtils.Saturate(AimProgress + MathF.Sign(target - AimProgress) * dt / AimSeconds);
        if (MathF.Abs(AimProgress - target) < 0.01f) AimProgress = target;
    }

    /// <summary>The per-weapon field of view CS:MC draws this weapon through: lerp(hip, aim) of its table row, times viewFov/70.</summary>
    public static float WeaponFovDegrees(int variant) {
        if (KnifeTuning.ExactWeaponFovDegrees > 0.5f) return KnifeTuning.ExactWeaponFovDegrees;
        CsmcKnifeRig.WeaponTableEntry row = CsmcKnifeRig.GetTable(variant);
        return MathUtils.Lerp(row.FovHip, row.FovAim, MathUtils.Saturate(AimProgress)) * KnifeTuning.ExactHandFovDegrees / 70f;
    }
    static float s_weaponFov = 48f;

    static Matrix ExactPlacement(int variant) {
        float scale = CsmcKnifeRig.GetSourceReferenceScale(variant) / MathF.Max(KnifeTuning.ExactReferenceScale, 0.0001f);
        if (KnifeTuning.ExactScaleOverride > 0.0001f) scale = KnifeTuning.ExactScaleOverride;
        // b$4an: translate(lerp(hip, aim, aimProgress)); translate(global); rotate roll.
        CsmcKnifeRig.WeaponTableEntry row = CsmcKnifeRig.GetTable(variant);
        Vector3 hip = Vector3.Lerp(row.HipOffset, row.AimOffset, MathUtils.Saturate(AimProgress)) + new Vector3(
            KnifeTuning.ExactHipX + KnifeTuning.ExactGlobalX,
            KnifeTuning.ExactHipY + KnifeTuning.ExactGlobalY,
            KnifeTuning.ExactHipZ + KnifeTuning.ExactGlobalZ);
        float rollDegrees = row.RollDegrees + KnifeTuning.ExactRollDegrees;
        Matrix centre = KnifeTuning.ExactMeshCenterOffset > 0.5f
            ? Matrix.CreateTranslation(CsmcKnifeRig.GetMeshCenterOffset(variant))
            : Matrix.Identity;
        Matrix mirror = KnifeTuning.ExactMirrorX > 0.5f ? Matrix.CreateScale(-1f, 1f, 1f) : Matrix.Identity;
        return centre
            * Matrix.CreateScale(scale)
            * Matrix.CreateRotationZ(MathUtils.DegToRad(270f))
            * Matrix.CreateRotationY(MathUtils.DegToRad(180f))
            * Matrix.CreateRotationX(MathUtils.DegToRad(90f))
            * Matrix.CreateTranslation(KnifeTuning.ExactWeaponTX, KnifeTuning.ExactWeaponTY, KnifeTuning.ExactWeaponTZ)
            * Matrix.CreateRotationX(MathUtils.DegToRad(rollDegrees))
            * Matrix.CreateTranslation(hip)
            * mirror
            * Matrix.CreateTranslation(KnifeTuning.ExactHandX, KnifeTuning.ExactHandY, KnifeTuning.ExactHandZ);
    }

    /// <summary>
    /// A perspective with the given vertical field of view, near plane 0.05, at the
    /// camera's aspect. Minecraft's hand pass is the fixed 70 degrees (the arms); CS:MC
    /// draws the weapon itself through its own per-weapon FOV (48 for knives).
    /// </summary>
    static Matrix ExactProjection(Camera camera, float fovDegrees) {
        float aspect = camera.ProjectionMatrix.M22 / camera.ProjectionMatrix.M11;
        if (!float.IsFinite(aspect) || aspect <= 0.01f) aspect = 16f / 9f;
        return Matrix.CreatePerspectiveFieldOfView(MathUtils.DegToRad(fovDegrees), aspect, 0.05f, 64f);
    }

    /// <summary>The weapon projection's scale factors, for the headless tools (same aspect as the hand pass).</summary>
    public static float WeaponProjY => s_projY;
    public static float WeaponProjX => s_projX;

    static Vector3 Normalized(Vector3 v) {
        float l = v.Length();
        return float.IsFinite(l) && l > 0.00001f ? v / l : Vector3.UnitY;
    }

    /// <summary>
    /// CS:MC's forearm (b$4jd): Minecraft's arm box stretched from a fixed view-space
    /// anchor to the pose's hand bone. The box spans -0.125..0.625 of its stretch
    /// along that line (Minecraft's arm cube), width 4/16 x 0.82, and is twisted
    /// about the line by the wrist's own roll (reference axis picked the way CS:MC
    /// picks it), plus CS:MC's constant 45 and +-90 degree offsets.
    /// </summary>
    /// <summary>
    /// How far MC's arm box runs past the wrist along the arm line: to the knuckles (the
    /// fist's far edge), taken from the rig as the middle finger's base joint plus half its
    /// first phalanx. The fingertips overshoot (the hand is bent around the grip), the wrist
    /// falls short (0.15.1: the sleeve stopped before the handguard).
    /// </summary>
    static float FingerReach(KnifeRigPose pose, Matrix placementWithPost, int variant, bool left, Vector3 hand, Vector3 dir) {
        string side = left ? "_l" : "_r";
        Vector3 j0 = Vector3.Transform(pose.GetBoneFrameOrigin("finger_middle_0" + side), placementWithPost);
        Vector3 j1 = Vector3.Transform(pose.GetBoneFrameOrigin("finger_middle_1" + side), placementWithPost);
        Vector3 seg = j1 - j0;
        float len = seg.Length();
        if (!float.IsFinite(len) || len < 0.00001f) return 0f;
        Vector3 knuckles = j0 + seg * 0.5f;
        float reach = Vector3.Dot(knuckles - hand, dir);
        return float.IsFinite(reach) ? MathF.Max(reach, 0f) : 0f;
    }

    static bool ExactArmFrame(KnifeRigPose pose, Matrix placementWithPost, int variant, bool left, out ArmFrame arm, out float twistDegrees) {
        arm = default;
        twistDegrees = 0f;
        Matrix wrist = pose.GetBoneFrame(left ? "hand_l" : "hand_r") * placementWithPost;
        Vector3 hand = new(wrist.M41, wrist.M42, wrist.M43);
        Vector3 anchor = left
            ? new Vector3(KnifeTuning.ExactArmAnchorLX, KnifeTuning.ExactArmAnchorLY, KnifeTuning.ExactArmAnchorLZ)
            : new Vector3(KnifeTuning.ExactArmAnchorRX, KnifeTuning.ExactArmAnchorRY, KnifeTuning.ExactArmAnchorRZ);
        Vector3 span = hand - anchor;
        float length = span.Length();
        if (!float.IsFinite(length) || length < 0.0001f) return false;
        Vector3 dir = span / length;
        float stretch = MathUtils.Clamp(length / MathF.Max(KnifeTuning.ExactArmBaseLength, 0.0001f), KnifeTuning.ExactArmStretchMin, KnifeTuning.ExactArmStretchMax);

        // CS:MC works in the weapon's base space; these are its axes seen in view space.
        Vector3 baseX = Normalized(Vector3.TransformNormal(Vector3.UnitX, placementWithPost));
        Vector3 baseY = Normalized(Vector3.TransformNormal(Vector3.UnitY, placementWithPost));
        Vector3 baseZ = Normalized(Vector3.TransformNormal(Vector3.UnitZ, placementWithPost));
        // Wrist twist about the arm line: reference axis by CS:MC's |dir.x| < 0.85 rule.
        bool useX = MathF.Abs(Vector3.Dot(dir, baseX)) < 0.85f;
        Vector3 reference = Normalized(ProjectOntoPlane(useX ? baseX : baseZ, dir));
        Vector3 turned = Normalized(ProjectOntoPlane(Vector3.TransformNormal(useX ? Vector3.UnitX : Vector3.UnitZ, wrist), dir));
        float twist = SignedAngle(reference, turned, dir);
        // Shortest-arc rotation taking the base Y axis onto the arm line (JOML rotationTo).
        Vector3 localX = baseX;
        Vector3 arcAxis = Vector3.Cross(baseY, dir);
        float arcLength = arcAxis.Length();
        float cos = MathUtils.Clamp(Vector3.Dot(baseY, dir), -1f, 1f);
        if (arcLength > 0.00001f) {
            float angle = MathF.Acos(cos);
            Matrix arc = Matrix.CreateFromAxisAngle(arcAxis / arcLength, angle);
            if (Vector3.Dot(Vector3.TransformNormal(baseY, arc), dir) < 0.999f) arc = Matrix.CreateFromAxisAngle(arcAxis / arcLength, -angle);
            localX = Normalized(Vector3.TransformNormal(baseX, arc));
        }
        else if (cos < 0f) {
            localX = -baseX;
        }
        float phi = MathF.PI + twist + MathUtils.DegToRad(KnifeTuning.ExactArmTwistOffsetDegrees) + MathUtils.DegToRad(left ? 90f : -90f);
        Vector3 side = Normalized(Turn(localX, dir, phi));
        Vector3 axis = -dir;                                   // the box is seated at the hand end and runs back to the anchor
        Vector3 up = Normalized(Vector3.Cross(side, axis));
        // MC's arm box ends at the fist's far edge, not the wrist: extend past the hand bone
        // by the knuckles' reach along the arm line (FingerReach, from the rig). 0.15.0 had
        // something like it by accident (the mesh-centre bug pushed the AK hand 7 in along the
        // bone); 0.15.1 lost it and the hand floated short of the handguard.
        float reach = KnifeTuning.ExactArmBaseLength * stretch + FingerReach(pose, placementWithPost, variant, left, hand, dir);
        float overshoot = 0.125f * stretch;                       // the cube's 2/16 past the anchor
        // Width: CS:MC's own box is 0.205 (ExactArmWidth); 0 uses the fist solver's width,
        // the fraction of the frame the player is used to, resolved at the hand's depth.
        float width = KnifeTuning.ExactArmWidth;
        if (width <= 0.0001f) width = ScreenWidthFor(variant, left) * 2f / MathF.Max(s_projX, 0.0001f) * MathF.Max(-hand.Z, 0.05f);
        arm = new ArmFrame {
            Grip = hand, Elbow = anchor, Seat = anchor + dir * reach, Axis = axis, Side = side, Up = up, Lean = 0f,
            ViewWidth = width, Overshoot = overshoot, Reach = reach,
        };
        twistDegrees = MathUtils.RadToDeg(twist);
        return true;
    }

    static QaSample ExactSample(in ArmFrame arm, float twistDegrees, Matrix wrist) => new() {
        Valid = true, Grip = arm.Grip, Elbow = arm.Elbow, Seat = arm.Seat, Axis = arm.Axis, Side = arm.Side, Up = arm.Up,
        Width = arm.ViewWidth, Reach = arm.Reach, Overshoot = arm.Overshoot, Clearance = 0f,
        RigidDeg = twistDegrees, ResolvedDeg = twistDegrees, Stillness = 1f, HoldDeg = 0f, WeaponHand = wrist, HandR = wrist,
    };

    static Matrix PlacementFor(KnifeRigPose pose, int variant) {
        if (Exact) return ExactPlacement(variant);
        Matrix placement = s_placement[variant];
        if (pose.ClipAlias.StartsWith("inspect", StringComparison.Ordinal)) {
            Vector3 grip = Vector3.Transform(RightGrip(variant), pose.GetBinding(RightWristBone(variant)));
            placement = Matrix.CreateTranslation((s_idleGrips[variant] - grip) * (1f - KnifeTuning.InspectTravelScale)) * placement;
        }
        return placement;
    }

    /// <summary>
    /// F7 cycles the diagnostic views for the on-device texture investigation: base colour,
    /// normals, roughness, metalness (the shader's debug outputs), then the guns with a flat
    /// normal map (the knives' exact path), then back to normal rendering.
    /// </summary>
    static int s_diagMode;
    static readonly string[] s_diagNames = ["正常渲染", "诊断 1/5：底色", "诊断 2/5：法线", "诊断 3/5：粗糙度", "诊断 4/5：金属度", "诊断 5/5：枪用平法线（同刀）"];
    static void PollDiagnosticKey(ComponentFirstPersonModel firstPerson) {
        if (!Keyboard.IsKeyDownOnce(Key.F7)) return;
        s_diagMode = (s_diagMode + 1) % s_diagNames.Length;
        KnifeTuning.PbrDebug = s_diagMode is >= 1 and <= 4 ? s_diagMode : 0f;
        KnifePbrRenderer.FlatGunNormal = s_diagMode == 5;
        firstPerson?.Entity.FindComponent<ComponentPlayer>()?.ComponentGui.DisplaySmallMessage(s_diagNames[s_diagMode], Color.White, true, false);
        KnifeLog.Information($"[ScCsgoKnives] diagnostic view {s_diagMode}: {s_diagNames[s_diagMode]}");
    }

    public static bool Draw(ComponentFirstPersonModel firstPerson, Camera camera, int variant, KnifeRigPose pose) {
        if (pose is null) return false;
        EnsureLoaded();
        KnifeTuning.Poll();
        PollDiagnosticKey(firstPerson);
        variant = Math.Clamp(variant, 0, s_count - 1);
        AdvanceAim();
        // Field of view is a player setting and the aspect follows the window, so
        // the screen-space anchor has to be re-resolved whenever either changes;
        // in exact mode the weapon's own FOV (per table row, blended by aim) too.
        s_weaponFov = WeaponFovDegrees(variant);
        if (SyncProjection(camera)) RebuildPlacements();
        EnsurePlacement(variant);
        if (s_parts[variant] is null || s_baseColor[variant] is null) {
            KnifeDiagnostics.WarnOnce($"assets-missing-{s_assetNames[variant]}",
                $"{s_assetNames[variant]} has no usable first-person assets; falling back to Survivalcraft item rendering.");
            return false;
        }

        // Moves the whole composition -- knife and hands alike -- so the grip
        // stays attached while the body moves. CSMC clips own the draw, inspect
        // and slash motion, so SC's poke transform is the one piece of vanilla
        // that must not be multiplied on top.
        Matrix post = CreateBodyMotion(firstPerson)
            * Matrix.CreateFromYawPitchRoll(firstPerson.m_lagAngles.X, firstPerson.m_lagAngles.Y, 0f);
        Matrix placement = PlacementFor(pose, variant);
        Matrix root = placement * post;

        float light = LightingManager.LightIntensityByLightValue[Math.Clamp(firstPerson.m_itemLight, 0, 15)];
        LogComposition(firstPerson, variant, pose, placement, post);
        // Through the scope: CS hides the weapon and shows the lens overlay once the aim blend completes.
        if (s_scoped && AimProgress >= 0.999f) {
            // The mask itself is drawn by SubsystemScGunBlockBehavior at draw order 350, after the
            // sky (105) and particles (300): drawn here, in the first-person pass, the sky dome
            // painted over it whenever the player looked up (0.15.8 "对着天空变透明").
            s_overlayFrame = Time.FrameIndex;
            return true;
        }
        // CS:MC draws the weapon through its own per-weapon projection (48 degrees for
        // every knife). The fists are solved in view space onto the knife's grip and
        // drawn through the same projection so they stay on the handle; only CS:MC's
        // own stretched arm boxes (ExactArms) use Minecraft's 70 degree hand pass.
        // No depth clear here: Survivalcraft's first-person draw already squeezes the
        // viewport depth range (MaxDepth x 0.1) so the composition draws in front of
        // the world, and a clear at this draw order let the sky dome paint over the
        // terrain (the "world turned white" of 0.14.0).
        Matrix projection = Exact ? ExactProjection(camera, s_weaponFov) : camera.ProjectionMatrix;
        Matrix weaponProjection = projection;
        Matrix handProjection = Exact && ExactArms ? ExactProjection(camera, KnifeTuning.ExactHandFovDegrees) : projection;
        DrawHands(firstPerson, camera, handProjection, variant, pose, placement, post, light);
        KnifePbrRenderer.Lighting lighting = KnifePbrRenderer.FirstPersonLighting(camera, light);
        bool hideSilencer = SilencerHidden(firstPerson, variant);
        foreach (Part part in s_parts[variant]) {
            if (part.Binding == LatchPart && KnifeTuning.ShowButterflyLatch <= 0.5f) continue;
            if (hideSilencer && part.Binding == SilencerPart) continue;
            string material = PartMaterial(variant, part.Binding);
            Texture2D partBase = material is null ? s_baseColor[variant] : PartBaseTexture(material);
            // A part held in the left hand (the shadow daggers' second blade) goes
            // where the left fist goes: the fist is pinned to its reference position
            // by a view-space correction, and without the same shift the dagger was
            // left floating where the rig's hand_l bone is.
            Matrix world = part.Binding == LeftHeldPart && !BoneArms(variant)
                ? pose.GetBinding(part.Binding) * placement * Matrix.CreateTranslation(s_leftHandCorrection[variant]) * post
                : pose.GetBinding(part.Binding) * root;
            // PBR when the shader and this knife's maps are available; the plain
            // lit draw otherwise, so a device that cannot compile it still sees a knife.
            if (!KnifePbrRenderer.TryDrawPart(part.Model, partBase, variant, world, weaponProjection,
                    camera.InvertedViewMatrix, in lighting, applyBoneTransform: true, material)) {
                DrawModel(part.Model, s_baseColor[variant], world, camera, weaponProjection, light,
                    SamplerState.LinearWrap, RasterizerState.CullNoneScissor, applyBoneTransform: true);
            }
        }

        if (s_smokeUntil > KnifeClock.Now) DrawMuzzleFlash(pose, root, weaponProjection);

        if (!s_logged[variant]) {
            s_logged[variant] = true;
            KnifeLog.Information(
                $"[ScCsgoKnives] first-person render active: asset={s_assetNames[variant]}, clip={pose.SourceClip}, "
                + $"parts=[{string.Join(',', s_parts[variant].Select(part => part.Binding))}], root={KnifeDiagnostics.MatrixSummary(root)}."
            );
        }
        return true;
    }

    /// <summary>CS:MC's scope lens (gui/scope.png: transparent lens, black surround) over the zoomed world, letterboxed square.</summary>
    /// <summary>
    /// CS:MC's scope view, measured on MCCS_VIDEO/AWP.mp4 (13.5 s, 1920x1084): the whole
    /// frame black except a circular window centred on the screen, diameter 96.5 % of the
    /// frame height, edge feathered over about 20 px, and a one-pixel crosshair. No scope
    /// texture, no letterbox (0.15.x drew the jar's scope.png in a square instead).
    /// </summary>
    static int s_overlayFrame = -1;
    /// <summary>True while the scoped view (gun hidden, mask due) is active for the frame being drawn.</summary>
    public static bool ScopeOverlayActive => s_scoped && AimProgress >= 0.999f && Time.FrameIndex - s_overlayFrame <= 1;

    public static void DrawScopeOverlay() {
        try {
            s_primitives2D ??= new PrimitivesRenderer2D();
            float w = Display.Viewport.Width, h = Display.Viewport.Height;
            Vector2 c = new(w * 0.5f, h * 0.5f);
            float radius = 0.4825f * h;
            float feather = 0.0185f * h;
            const int segments = 128, rings = 6;
            // Cull nothing: the fan and ring triangles below are wound both ways, and with the default
            // back-face culling the mask simply did not appear (0.15.3-0.15.7: scoped view showed only the sky).
            FlatBatch2D fill = s_primitives2D.FlatBatch(0, DepthStencilState.None, RasterizerState.CullNoneScissor, BlendState.Opaque);
            FlatBatch2D soft = s_primitives2D.FlatBatch(1, DepthStencilState.None, RasterizerState.CullNoneScissor, BlendState.NonPremultiplied);
            float far = MathF.Max(w, h) * 1.5f;
            for (int i = 0; i < segments; i++) {
                float a0 = MathF.PI * 2f * i / segments, a1 = MathF.PI * 2f * (i + 1) / segments;
                Vector2 d0 = new(MathF.Cos(a0), MathF.Sin(a0)), d1 = new(MathF.Cos(a1), MathF.Sin(a1));
                // Opaque black from the feathered edge out past the screen.
                fill.QueueTriangle(c + d0 * radius, c + d0 * far, c + d1 * far, 0f, Color.Black);
                fill.QueueTriangle(c + d0 * radius, c + d1 * far, c + d1 * radius, 0f, Color.Black);
                // Feather: rings from transparent (inside) to opaque at the radius.
                for (int r = 0; r < rings; r++) {
                    float rIn = radius - feather * (rings - r) / rings, rOut = radius - feather * (rings - r - 1) / rings;
                    Color col = new Color((byte)0, (byte)0, (byte)0, (byte)(255f * (r + 0.5f) / rings));
                    soft.QueueTriangle(c + d0 * rIn, c + d0 * rOut, c + d1 * rOut, 0f, col);
                    soft.QueueTriangle(c + d0 * rIn, c + d1 * rOut, c + d1 * rIn, 0f, col);
                }
            }
            // Reticle cross; thickness from the tuning file, scaled to the frame height.
            float half = MathF.Max(0.5f, KnifeTuning.ScopeLinePx * h / 1080f * 0.5f);
            Color line = new Color((byte)0, (byte)0, (byte)0, (byte)200);
            soft.QueueQuad(new Vector2(c.X - half, c.Y - radius), new Vector2(c.X + half, c.Y + radius), 0f, line);
            soft.QueueQuad(new Vector2(c.X - radius, c.Y - half), new Vector2(c.X + radius, c.Y + half), 0f, line);
            s_primitives2D.Flush();
        }
        catch (Exception e) {
            KnifeDiagnostics.WarnOnce("scope-overlay", $"Scope overlay failed: {e.Message}");
        }
    }

    /// <summary>An additive sprite at the muzzle bone, facing the eye, for the frames after a shot.</summary>
    /// <summary>
    /// The shot's flash and smoke, CS:MC's own sprite sequences as view-facing quads at the
    /// muzzle bone. Sized from MCCS_VIDEO/AK47.mp4 (11.3 s): the fireball spans about 130 px
    /// of a 1920 px frame at the AK's muzzle depth, i.e. 0.13 view units, and the sprite's
    /// fireball fills about 70 % of its frame. Silenced (muzzle2) shots get a small, dim flash.
    /// </summary>
    static void DrawMuzzleFlash(KnifeRigPose pose, Matrix root, Matrix projection) {
        try {
            s_fireAtlas ??= ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/muzzle_fire");
            s_smokeAtlas ??= ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/muzzle_smoke");
            s_primitives3D ??= new PrimitivesRenderer3D();
            Matrix muzzle = pose.GetBoneFrame(s_flashBone) * root;
            Vector3 p = new(muzzle.M41, muzzle.M42, muzzle.M43);
            if (!(p.Z < -0.05f)) return;
            double now = KnifeClock.Now;
            float t = (float)(now - s_flashStart);
            bool silenced = s_flashBone == "muzzle2";
            float c = MathF.Cos(s_flashRoll), sn = MathF.Sin(s_flashRoll);
            Vector3 rightDir = new(c, sn, 0f), upDir = new(-sn, c, 0f);
            if (now < s_flashUntil) {
                float u = MathUtils.Saturate(t / s_flashSeconds);
                int frame = 8 + (int)(u * 12.99f);                      // the sequence's full-size frames
                float half = (silenced ? 0.035f : 0.09f);
                Color tint = silenced ? new Color(160, 160, 160, 255) : Color.White;
                QueueSprite(s_fireAtlas, FireFrames, frame, p, rightDir * half, upDir * half, tint, BlendState.Additive);
            }
            if (now < s_smokeUntil && !silenced) {
                float su = MathUtils.Saturate((t - s_flashSeconds * 0.5f) / SmokeSeconds);
                int frame = (int)(su * (SmokeFrames - 0.01f));
                float half = 0.06f + 0.05f * su;
                Vector3 centre = p + new Vector3(0f, 0.05f * su, 0f);
                byte alpha = (byte)(150f * (1f - su));
                QueueSprite(s_smokeAtlas, SmokeFrames, frame, centre, rightDir * half, upDir * half, new Color((byte)255, (byte)255, (byte)255, alpha), BlendState.NonPremultiplied);
            }
            s_primitives3D.Flush(projection);
        }
        catch (Exception e) {
            KnifeDiagnostics.WarnOnce("muzzle-flash", $"Muzzle flash failed: {e.Message}");
        }
    }

    static void QueueSprite(Texture2D atlas, int frames, int frame, Vector3 p, Vector3 right, Vector3 up, Color color, BlendState blend) {
        frame = Math.Clamp(frame, 0, frames - 1);
        float u0 = (frame % AtlasColumns) / (float)AtlasColumns, v0 = (frame / AtlasColumns) / (float)AtlasRows;
        float u1 = u0 + 1f / AtlasColumns, v1 = v0 + 1f / AtlasRows;
        TexturedBatch3D batch = s_primitives3D.TexturedBatch(atlas, false, 0, DepthStencilState.DepthRead, RasterizerState.CullNoneScissor, blend, SamplerState.LinearClamp);
        batch.QueueQuad(p - right - up, p + right - up, p + right + up, p - right + up, new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0), new Vector2(u0, v0), color);
    }

    /// <summary>
    /// The walk bob and the equip dip, copied from vanilla ComponentFirstPersonModel.
    /// Minecraft applies the same two motions inside the matrix CSMC appends its
    /// weapon transform to; dropping them is why the knife sat glued to the
    /// camera while walking and popped in without a swap when switching items.
    /// </summary>
    static Matrix CreateBodyMotion(ComponentFirstPersonModel firstPerson) {
        Matrix motion = Matrix.Identity;
        // Vanilla dips the held item by 0.8 while swapping. For a plain SC item that
        // dip *is* the equip animation; for us the CS:GO deploy clip already raises
        // the knife, so applying both drops the whole composition off the bottom of
        // the screen -- measured, the grip leaves the frame for 53% of the swap.
        // Same reasoning that keeps SC's poke transform out: whoever owns the motion
        // owns it alone. KnifeTuning.SwapDipScale can put some of it back.
        if (firstPerson.m_swapAnimationTime > 0f && KnifeTuning.SwapDipScale > 0f) {
            float swap = MathF.Pow(MathF.Sin(firstPerson.m_swapAnimationTime * MathF.PI), 3f) * KnifeTuning.SwapDipScale;
            motion *= Matrix.CreateTranslation(0f, -0.8f * swap, 0.2f * swap);
        }
        ComponentCreatureModel mount = firstPerson.m_componentRider.Mount?.Entity.FindComponent<ComponentCreatureModel>();
        if (mount != null) {
            float phase = mount.MovementAnimationPhase * MathF.PI * 2f + 0.5f;
            Vector3 sway = new(0f, 0.02f * MathF.Sin(phase), 0.02f * MathF.Sin(phase));
            return motion * Matrix.CreateRotationX(0.05f * MathF.Sin(phase)) * Matrix.CreateTranslation(sway);
        }
        float walk = firstPerson.m_componentPlayer.ComponentCreatureModel.MovementAnimationPhase * MathF.PI * 2f;
        Vector3 bob = new(0.03f * MathF.Sin(walk), 0.02f * MathF.Sin(walk * 2f), 0.02f * MathF.Sin(walk));
        return motion * Matrix.CreateRotationZ(bob.X) * Matrix.CreateTranslation(bob);
    }


    /// <summary>
    /// Projects a view-space point to a screen fraction (x right, y down), or null
    /// when it is behind the eye.
    /// </summary>
    static Vector2? ToScreen(Vector3 view) {
        float depth = -view.Z;
        if (!(depth > 0.0001f)) return null;
        return new Vector2(view.X * s_projX / depth * 0.5f + 0.5f, 0.5f - view.Y * s_projY / depth * 0.5f);
    }

    const double CompositionLogInterval = 20.0;
    static readonly double[] s_compositionNextLog = new double[s_count];

    /// <summary>
    /// Reports the composition in the same screen-space terms the CS:MC references
    /// were measured in, so the two can be compared without segmenting screenshots.
    /// Only logged for a settled frame -- mid-swap numbers are not comparable to a
    /// static reference, which is exactly what made the first round of photos
    /// impossible to read.
    /// </summary>
    static void LogComposition(ComponentFirstPersonModel firstPerson, int variant, KnifeRigPose pose, Matrix placement, Matrix post) {
        if (firstPerson.m_swapAnimationTime > 0f) return;
        if (!pose.ClipAlias.StartsWith("idle", StringComparison.Ordinal)) return;
        // Repeats rather than firing once per variant. Logged once, the line lands at
        // whatever moment the knife first settled -- which in two rounds of logs was
        // always minutes before the screenshots, so the numbers could never be lined
        // up against the picture they were supposed to explain.
        if (Time.RealTime < s_compositionNextLog[variant]) return;
        s_compositionNextLog[variant] = Time.RealTime + CompositionLogInterval;

        var line = new System.Text.StringBuilder();
        line.Append($"[ScCsgoKnives] composition {s_assetNames[variant]} (clip={pose.SourceClip}): ");
        line.Append($"fov={2f * MathF.Atan(1f / s_projY) * 180f / MathF.PI:0.#}deg aspect={s_projY / s_projX:0.###} knifeScale={RigScale(variant) * ReferenceSourceScale / CsmcKnifeRig.GetSourceReferenceScale(variant):0.###} | ");
        for (int side = 0; side < 2; side++) {
            bool left = side == 1;
            string label = left ? "left" : "right";
            if (left && !s_leftArmUsable[variant]) { line.Append("left arm disabled"); break; }
            if (!SolveArm(pose, placement * post, post, variant, left, false, out ArmFrame arm)) { line.Append($"{label}=degenerate "); continue; }
            Vector2? hand = ToScreen(arm.Grip);
            Vector2? cap = ToScreen(arm.Seat);
            Vector2? elbow = ToScreen(arm.Elbow);
            if (hand is null || cap is null || elbow is null) { line.Append($"{label}=behind eye "); continue; }
            line.Append($"{label}: grip=({hand.Value.X:0.###},{hand.Value.Y:0.###}) cap=({cap.Value.X:0.###},{cap.Value.Y:0.###}) ");
            line.Append($"lean={arm.Lean:+0.0;-0.0}deg width={arm.ViewWidth * s_projX / (2f * -arm.Grip.Z):0.###} overshoot={OvershootFor(variant, left):0.##}w ");
            line.Append($"elbow=({elbow.Value.X:0.###},{elbow.Value.Y:0.###}) depth={-arm.Grip.Z:0.###} ");
        }
        line.Append("| MCCS m9 photo: grip=(0.710,0.843) cap=(0.699,0.692) lean=+7.5, left grip=(0.326,0.901) cap=(0.381,0.823) lean=-51.5.");
        KnifeLog.Information(line.ToString());
    }

    static void DrawHands(ComponentFirstPersonModel firstPerson, Camera camera, Matrix projection, int variant, KnifeRigPose pose, Matrix placement, Matrix post, float light) {
        // placement * post is the space the knife is actually drawn in.
        // Survivalcraft's own hand box, the one players see whenever they hold
        // nothing. Reusing it keeps the arms in the game's art style, takes the
        // player's own skin, and ships no geometry with the mod.
        Model hand = firstPerson.m_handModel;
        Texture2D skin = firstPerson.m_componentPlayer.ComponentCreatureModel.TextureOverride
            ?? firstPerson.m_componentPlayer.ComponentClothing.InnerClothedTexture;
        if (hand is null || skin is null) {
            KnifeDiagnostics.WarnOnce("hands-missing", "Hand attachment skipped because SC's first-person hand model or player skin is missing.");
            return;
        }
        if (BoneArms(variant)) {
            DrawArmExact(camera, projection, hand, skin, light, pose, placement * post, variant, false);
            if (s_leftArmUsable[variant]) DrawArmExact(camera, projection, hand, skin, light, pose, placement * post, variant, true);
            return;
        }
        DrawArm(camera, projection, hand, skin, light, pose, placement * post, post, variant, false);
        if (s_leftArmUsable[variant]) DrawArm(camera, projection, hand, skin, light, pose, placement * post, post, variant, true);
    }

    /// <summary>One arm's box for this frame, before it becomes a matrix.</summary>
    struct ArmFrame {
        public Vector3 Grip, Elbow, Seat, Axis, Side, Up;
        public float Lean, ViewWidth, Overshoot, Reach;
    }

    /// <summary>
    /// One arm's solved frame with the roll's internals, kept from the last
    /// committed solve for the capture run (KnifeQa) and the headless sweep.
    /// </summary>
    public struct QaSample {
        public bool Valid;
        public Vector3 Grip, Elbow, Seat, Axis, Side, Up;
        public float Width, Reach, Overshoot, Clearance;
        public float RigidDeg, ResolvedDeg, Stillness, HoldDeg;
        public Matrix WeaponHand, HandR;
    }
    public static QaSample LastRight, LastLeft;
    static float s_qaRigidDeg, s_qaResolvedDeg, s_qaStillness, s_qaHoldDeg;

    /// <summary>
    /// Solves one arm. <paramref name="placement"/> must already include the body
    /// motion, and <paramref name="post"/> is that motion on its own: the elbow is
    /// fixed to the body, not to the hand, so it is projected from where the idle
    /// grip sits after the body motion rather than from this frame's grip.
    /// </summary>
    static bool SolveArm(KnifeRigPose pose, Matrix placement, Matrix post, int variant, bool left, bool commit, out ArmFrame arm) {
        arm = default;
        string bone = left ? "l" : "r";
        Matrix wrist = pose.GetBinding(left ? "hand_l" : RightWristBone(variant)) * placement;
        Vector3 grip = Vector3.Transform(left ? s_leftGripOffsets[variant] : RightGrip(variant), wrist);
        if (left) grip += s_leftHandCorrection[variant];
        // The fist's POSITION follows the held part, so it stays on the handle's base
        // when the fingers move the knife. Its ROLL follows the true wrist: the held
        // part spins with the knife through an inspect (145 degrees mid-twirl on the
        // butterfly), and carrying the roll reference in that frame made the arm turn
        // with the blade -- and keep turning. The wrist rolls, it does not spin.
        Matrix rollFrame = pose.GetBinding(left ? "hand_l" : "hand_r") * placement;

        // The elbow sits off the bottom of the frame on the arm's fitted bearing,
        // projected from the idle grip. A clip that lifts the hand then swings the
        // arm about the elbow the way a forearm moves, instead of sliding a box of
        // fixed direction across the screen.
        float lean = LeanFor(variant, left);
        if (KnifeTuning.ArmLeanFromBone > 0.0001f) {
            Matrix forearmBone = pose.GetBinding($"arm_lower_{bone}") * placement;
            Vector3 boneEnd = new(forearmBone.M41, forearmBone.M42, forearmBone.M43);
            float fromBone = ForearmScreenLean(grip, boneEnd, RigScale(variant), pose.AssetName, bone, left);
            lean = MathUtils.Lerp(lean, fromBone, MathUtils.Saturate(KnifeTuning.ArmLeanFromBone));
        }
        Vector3 idleGrip = Vector3.Transform(left ? s_idleLeftGrips[variant] : (Exact ? s_exactIdleGrips[variant] : AnchorFor(variant)), post);
        Vector3 elbow = ProjectDownArm(idleGrip, lean, NearFor(variant, left));
        Vector3 span = elbow - grip;
        float reach = span.Length();
        if (!float.IsFinite(reach) || reach < 0.0001f) return false;
        Vector3 axis = span / reach;

        // At idle the box's broad face looks at the eye, which is how every reference
        // photo shows it. From there it rolls with the wrist; see ResolveRoll.
        Vector3 faceOn = ProjectOntoPlane(Vector3.Normalize(grip), axis);
        // clearance: once the handle lies along the face, how far the box sits off
        // the grip so the handle's surface, not its centre line, rests on the face.
        Vector3 side = ResolveRoll(faceOn, pose, placement, grip, rollFrame, axis, variant, left, commit, out float clearance, out Vector3 offsetDir);
        Vector3 up = Vector3.Normalize(Vector3.Cross(side, axis));

        // CS:MC's arm box is a fixed size, not one scaled to the forearm, so the width
        // is the fraction of the frame the reference arm covers, resolved against the
        // live projection at the depth of the box's own centre. That centre sits
        // FistGripFace half-widths (plus the clearance) off the grip along the line of
        // sight (side points away from the eye), which makes the width appear on both
        // sides of the equation; solved for it. The fist reaches past the grip by a
        // measured fraction of that width; that is what buries the handle.
        float depth = MathF.Max(-grip.Z, 0.01f);
        float face = MathUtils.Clamp(KnifeTuning.FistGripFace, -1f, 1f);
        float perDepth = ScreenWidthFor(variant, left) * 2f / MathF.Max(s_projX, 0.0001f);
        float viewWidth = perDepth * (depth + clearance * offsetDir.Z) / MathF.Max(1f - 0.5f * face * perDepth * offsetDir.Z, 0.2f);
        float overshoot = viewWidth * OvershootFor(variant, left);
        arm = new ArmFrame {
            Grip = grip, Elbow = elbow, Axis = axis, Side = side, Up = up, Lean = lean,
            ViewWidth = viewWidth, Overshoot = overshoot, Reach = reach,
            Seat = grip - offsetDir * (face * 0.5f * viewWidth + clearance) - axis * overshoot,
        };
        if (commit) {
            QaSample sample = new() {
                Valid = true, Grip = grip, Elbow = elbow, Seat = arm.Seat, Axis = axis, Side = side, Up = up,
                Width = viewWidth, Reach = reach, Overshoot = overshoot, Clearance = clearance,
                RigidDeg = left ? 0f : s_qaRigidDeg, ResolvedDeg = left ? 0f : s_qaResolvedDeg,
                Stillness = left ? 0f : s_qaStillness, HoldDeg = left ? 0f : s_qaHoldDeg,
                WeaponHand = wrist, HandR = rollFrame,
            };
            if (left) LastLeft = sample; else LastRight = sample;
        }
        return true;
    }

    /// <summary>
    /// Which way the box is rolled about the arm, by KnifeTuning.ArmRollMode.
    ///
    /// Mode 1 is CS:MC's own rule (b$4la rotates a reference direction by the live
    /// hand matrix and measures it about the arm): the idle face-on side is carried
    /// in the hand bone's frame, so idle is exactly the fitted composition and the
    /// change in wrist roll is followed rigidly. Rigid is the catch: the handle is
    /// not flat on the fist's face at idle -- it crosses it at thirty degrees on the
    /// M9 -- so once the wrist turns the far face round, the handle crosses that one.
    ///
    /// Mode 2 rolls the box so the handle lies along the face instead, blended in as
    /// the wrist leaves its idle pose. With FistGripFace putting that face at the
    /// handle, the handle rests on the fist through the whole inspect -- what the
    /// reference shows at the end of one -- and idle is still the fitted picture.
    ///
    /// Both fade to face-on where their direction lines up with the arm (the roll is
    /// undefined there), and neither turns faster than RollSlewDegreesPerSecond.
    /// </summary>
    static Vector3 ResolveRoll(Vector3 faceOn, KnifeRigPose pose, Matrix placement, Vector3 grip, Matrix wrist, Vector3 axis, int variant, bool left, bool commit, out float clearance, out Vector3 offsetDir) {
        int slot = variant * 2 + (left ? 1 : 0);
        int mode = (int)MathF.Round(KnifeTuning.ArmRollMode);
        Vector3 previous = s_lastSide[slot];
        Vector3 resolved = faceOn;
        clearance = 0f;
        offsetDir = faceOn;
        if (mode >= 1 && s_rollRef[slot].LengthSquared() > 0.5f) {
            Vector3 carried = Vector3.TransformNormal(s_rollRef[slot], wrist);
            carried -= axis * Vector3.Dot(carried, axis);
            float strength = carried.Length();
            if (float.IsFinite(strength) && strength > 0.0001f) {
                float weight = MathUtils.Saturate((strength - RollFadeStart) / (RollFadeEnd - RollFadeStart));
                resolved = ProjectOntoPlane(Vector3.Lerp(faceOn, carried / strength, weight), axis);
            }
        }
        if (s_measuring) { offsetDir = resolved; return resolved; }
        float rigidRoll = SignedAngle(faceOn, resolved, axis);
        // How still the wrist is: the rigid palm's roll rate about the arm, smoothed.
        // Measured on the rigid side, before any squaring, so the squaring's own turn
        // never counts as wrist motion (measured after it, the squaring held itself
        // off). Squaring the fist to the eye is only for the hold of an inspect, never
        // for a swing that happens to pass through the right orientation -- and never
        // for the approach to a hold. The rigid side IS the knife's turn; anything
        // added to it mid-motion is the fist visibly turning at a different rate from
        // the blade (the M9's approach turned the fist 84 degrees to the knife's 34).
        float dt = MathUtils.Clamp(KnifeClock.Dt, 0.001f, 0.1f);
        Vector3 prevPalm = s_prevPalm[slot];
        float stillness = s_stillness[slot];
        if (prevPalm.LengthSquared() > 0.5f) {
            float rate = MathUtils.RadToDeg(MathF.Abs(SignedAngle(ProjectOntoPlane(prevPalm, axis), resolved, axis))) / dt;
            float still = MathUtils.Saturate((90f - rate) / 60f);
            stillness += (still - stillness) * (1f - MathF.Exp(-dt / 0.2f));
        }
        if (commit) { s_prevPalm[slot] = resolved; s_stillness[slot] = stillness; }
        if (mode == 1 && s_rollRef[slot].LengthSquared() > 0.5f && KnifeTuning.SquareAtHold > 0.0001f) {
            // The rigid turn stops short of straight-behind at the hold (127 degrees on
            // the M9); the reference's box is straight behind the knife there. The 52
            // degrees still owed have to be added somewhere, and wherever they go the
            // fist moves differently from the blade: as a curve in the angle that
            // saturated early, the fist surged and then coasted; once the wrist had
            // settled, it kept turning a third of a second after the knife had stopped.
            // So spread them evenly: a straight line in the rigid angle from
            // SquareFromDegrees to the angle this clip actually rests at. The fist
            // then turns at a constant multiple of the wrist's rate and stops in the
            // frame the knife does. Only clips that rest at their extreme are squared;
            // a slash passes its extreme at speed and stays rigid. See MeasureHolds.
            float hold = KnifeTuning.SquareFullDegrees > 0.5f
                ? MathUtils.DegToRad(KnifeTuning.SquareFullDegrees)
                : HoldAngleFor(slot, pose.ClipAlias);
            float from = MathUtils.DegToRad(KnifeTuning.SquareFromDegrees);
            float angle = SignedAngle(faceOn, resolved, axis);
            float size = MathF.Abs(angle);
            if (hold > from + 0.01f && size > from) {
                float weight = MathUtils.Saturate(KnifeTuning.SquareAtHold);
                if (KnifeTuning.SquareGateByStillness > 0.5f) weight *= stillness;
                // How the owed angle is spread over the approach. Linear (SquareEase 0)
                // turns the fist at a constant 1.55x the wrist on the M9, which reads as
                // the hand running ahead of the blade. Eased (SquareEase 1, smoothstep)
                // starts and ends at the wrist's own rate -- the fist leaves idle and
                // stops at the hold exactly with the knife -- and hides the extra turn in
                // the fastest part of the swing, where the eye cannot follow either.
                float progress = size >= hold ? 1f : (size - from) / (hold - from);
                float ease = MathUtils.Saturate(KnifeTuning.SquareEase);
                float eased = MathUtils.Lerp(progress, progress * progress * (3f - 2f * progress), ease);
                float target = size >= hold ? MathF.PI : size + (MathF.PI - hold) * eased;
                float extra = (target - size) * weight;
                resolved = Turn(faceOn, axis, angle + MathF.CopySign(extra, angle));
                // With the box straight behind the knife, the handle still tilts into it:
                // the face passes through the handle's centre line and the handle is not
                // flat on the palm (28 degrees on the M9), so its lower half sank into the
                // box. The reference shows the whole handle in front of the face. Sit the
                // box back by the handle's depth under the face, by the same progress.
                float smooth = MathUtils.Saturate((size - from) / (hold - from)) * weight;
                if (!left && smooth > 0.0001f) {
                    float gripDepth = -grip.Z;
                    if (gripDepth > 0.2f) {
                        float limit = ScreenWidthFor(variant, left) * 2f * gripDepth / MathF.Max(s_projX, 0.0001f);
                        clearance = smooth * MathF.Min(HandleDepth(variant, pose, placement, grip, resolved), limit);
                    }
                }
            }
        }

        Vector3 handleLocal = left ? s_leftHandleDirections[variant] : HandleDirection(variant);
        if (mode >= 2 && handleLocal.LengthSquared() > 0.5f) {
            // The handle as the idle pose fixes it in the hand: rigid to the wrist. Which
            // way round its perpendicular is taken does not change the box (a box is
            // symmetric) -- only which side of the knife the box sits and which way the
            // blend turns. Taken nearest the carried palm, with hysteresis, so the blend
            // never has to turn more than about a right angle and never crosses zero:
            // the zero crossing of a straight lerp is what flashed the arm at the end of
            // a deploy, when the sign carried from mid-clip was the wrong way round.
            Vector3 rigidLocal = left ? handleLocal : s_handleIdleHand[slot];
            if (rigidLocal.LengthSquared() < 0.5f) rigidLocal = handleLocal;
            Vector3 rigid = Vector3.TransformNormal(rigidLocal, wrist);
            Vector3 flat = Vector3.Cross(axis, rigid);
            float strength = flat.Length();
            if (float.IsFinite(strength) && strength > 0.0001f) {
                flat /= strength;
                float sign = s_flatSign[slot] == 0f ? 1f : s_flatSign[slot];
                if (Vector3.Dot(flat * sign, resolved) < -0.35f) sign = -sign;
                if (commit) s_flatSign[slot] = sign;
                flat *= sign;
                float weight = WristDeparture(slot, wrist)
                    * MathUtils.Saturate((strength - RollFadeStart) / (RollFadeEnd - RollFadeStart));
                resolved = Turn(resolved, axis, weight * SignedAngle(resolved, flat, axis));

                if (!left) {
                    // The fingers re-grip the knife during an inspect (32 degrees on the M9
                    // at the hold), so the handle actually drawn sits off the rigid one.
                    // Follow it -- through FollowHandle, which holds still while the knife
                    // is twirled -- by no more than ReGripDegrees, and only as a slow drift.
                    Vector3 followed = Vector3.TransformNormal(FollowHandle(slot, pose, handleLocal, commit, out _), wrist);
                    Vector3 actual = Vector3.Cross(axis, followed);
                    float actualStrength = actual.Length();
                    float applied = s_reGrip[slot];
                    if (float.IsFinite(actualStrength) && actualStrength > 0.0001f) {
                        actual /= actualStrength;
                        if (Vector3.Dot(actual, resolved) < 0f) actual = -actual;
                        float raw = SignedAngle(resolved, actual, axis);
                        if (MathF.Abs(raw) > MathUtils.DegToRad(80f)) raw = (applied >= 0f ? 1f : -1f) * MathF.Abs(raw);
                        float bound = MathUtils.DegToRad(MathF.Max(KnifeTuning.ReGripDegrees, 0f));
                        float target = MathUtils.Clamp(raw, -bound, bound) * weight;
                        float drift = MathUtils.DegToRad(KnifeTuning.ReGripDegreesPerSecond) * dt;
                        applied = MathUtils.Clamp(target, applied - drift, applied + drift);
                    }
                    if (commit) s_reGrip[slot] = applied;
                    resolved = Turn(resolved, axis, applied);

                    // Square the holding face to the eye at the hold of an inspect: once the
                    // wrist has carried the palm to within a right angle of facing the eye
                    // and has come to rest, finish the turn. CS:MC has no such rule -- its
                    // arm is rigid to the wrist and its ending look is whatever its idle
                    // offsets leave -- but the reference look at the end of an inspect is
                    // the knife shown flat on the palm, square to the view.
                    Vector3 square = -faceOn;
                    float toSquare = SignedAngle(resolved, square, axis);
                    float squareWeight = MathUtils.Saturate((MathUtils.DegToRad(90f) - MathF.Abs(toSquare)) / MathUtils.DegToRad(45f))
                        * weight * stillness * MathUtils.Saturate(KnifeTuning.SquareAtHold);
                    float squareStep = MathUtils.DegToRad(120f) * dt;
                    float squared = MathUtils.Clamp(toSquare * squareWeight, s_square[slot] - squareStep, s_square[slot] + squareStep);
                    if (commit) s_square[slot] = squared;
                    resolved = Turn(resolved, axis, squared);

                    // Rest the handle's surface, not its centre line, on the face: a fixed
                    // half-thickness off the grip, no per-frame tracking.
                    clearance = weight * s_handleRadius[variant] * RigScale(variant);
                }
            }
        }
        // Which side of the knife the box sits is the carried palm's back, which the
        // side vector now always points away from; when a twirl turns the handle right
        // round, the box orbits to the other side of the knife rather than jumping.
        Vector3 stored = s_offsetDir[slot];
        offsetDir = resolved;
        if (stored.LengthSquared() > 0.5f) {
            Vector3 from = ProjectOntoPlane(stored, axis);
            float orbit = SignedAngle(from, resolved, axis);
            float orbitLimit = MathUtils.DegToRad(OrbitDegreesPerSecond) * dt;
            offsetDir = Turn(from, axis, MathUtils.Clamp(orbit, -orbitLimit, orbitLimit));
        }
        if (commit) s_offsetDir[slot] = offsetDir;
        if (previous.LengthSquared() > 0.0001f) {
            Vector3 last = ProjectOntoPlane(previous, axis);
            float turn = MathF.Acos(MathUtils.Clamp(Vector3.Dot(resolved, last), -1f, 1f));
            float limit = MathUtils.DegToRad(KnifeTuning.RollSlewDegreesPerSecond)
                * MathUtils.Clamp(KnifeClock.Dt, 0.001f, 0.1f);
            if (turn > limit && turn > 0.0001f) {
                // Turn about the axis by the limit, toward the resolved side.
                Vector3 tangent = Vector3.Cross(axis, last);
                float direction = Vector3.Dot(tangent, resolved) < 0f ? -1f : 1f;
                resolved = ProjectOntoPlane(last * MathF.Cos(limit) + tangent * (direction * MathF.Sin(limit)), axis);
            }
        }
        float step = ClearanceSlewPerSecond * MathUtils.Clamp(KnifeClock.Dt, 0.001f, 0.1f);
        clearance = MathUtils.Clamp(clearance, s_lastClearance[slot] - step, s_lastClearance[slot] + step);
        if (commit) {
            s_lastSide[slot] = resolved;
            s_lastClearance[slot] = clearance;
            if (!left) {
                s_qaRigidDeg = MathUtils.RadToDeg(rigidRoll);
                s_qaResolvedDeg = MathUtils.RadToDeg(SignedAngle(faceOn, resolved, axis));
                s_qaStillness = stillness;
                s_qaHoldDeg = MathUtils.RadToDeg(HoldAngleFor(slot, pose.ClipAlias));
            }
        }
        return resolved;
    }

    /// <summary>
    /// The handle's direction in the hand bone's frame, followed only while the
    /// fingers are not turning the knife. The knife is not rigid to hand_r: the
    /// inspects re-grip it (the M9's held part sits 32 degrees off the hand at the
    /// hold pose) and twirl it in between. Following the part outright spun the
    /// fist with every twirl; following the hand alone laid the wrong handle on
    /// the fist. So the part's direction is tracked with a short time constant
    /// while it turns slower than HandleFollowRate, and held still while it turns
    /// faster -- the fist keeps the last steady grip through a twirl and settles on
    /// the new one within a fraction of a second after it.
    /// </summary>
    static Vector3 FollowHandle(int slot, KnifeRigPose pose, Vector3 handleLocal, bool commit, out bool steady) {
        steady = false;
        Matrix hand = pose.GetBinding("hand_r");
        Matrix inverse = Matrix.Invert(hand);
        if (!KnifeDiagnostics.IsFinite(inverse)) return s_handleFollow[slot];
        Vector3 now = Vector3.TransformNormal(handleLocal, pose.GetBinding(HeldPart) * inverse);
        float length = now.Length();
        if (!float.IsFinite(length) || length < 0.0001f) return s_handleFollow[slot];
        now /= length;
        float dt = MathUtils.Clamp(KnifeClock.Dt, 0.001f, 0.1f);
        Vector3 previous = s_handlePrev[slot];
        float rate = previous.LengthSquared() > 0.5f
            ? MathUtils.RadToDeg(MathF.Acos(MathUtils.Clamp(Vector3.Dot(now, previous), -1f, 1f))) / dt
            : 0f;
        Vector3 follow = s_handleFollow[slot];
        if (follow.LengthSquared() < 0.5f) follow = now;
        steady = rate <= KnifeTuning.HandleFollowRate;
        if (steady) {
            float blend = 1f - MathF.Exp(-dt / MathF.Max(KnifeTuning.HandleFollowSeconds, 0.01f));
            Vector3 candidate = Vector3.Lerp(follow, now, blend);
            if (candidate.LengthSquared() > 0.09f) follow = Vector3.Normalize(candidate);
        }
        if (commit) {
            s_handlePrev[slot] = now;
            s_handleFollow[slot] = follow;
        }
        return follow;
    }

    /// <summary>
    /// How far the handle's surface reaches below the fist's face: the deepest
    /// handle-hull point under the plane through the grip with normal
    /// <paramref name="side"/>, each hull part placed by its own binding. Zero when
    /// the handle is entirely on the outside.
    /// </summary>
    static float HandleDepth(int variant, KnifeRigPose pose, Matrix placement, Vector3 grip, Vector3 side) {
        float depth = 0f;
        foreach (KnifeHulls.Part part in KnifeHulls.Parts(variant)) {
            if (part.Handle.Length == 0) continue;
            Matrix world = pose.GetBinding(part.Binding) * placement;
            foreach (Vector3 point in part.Handle) {
                depth = MathF.Max(depth, -Vector3.Dot(Vector3.Transform(point, world) - grip, side));
            }
        }
        return float.IsFinite(depth) ? depth : 0f;
    }

    /// <summary>0 at the idle wrist pose, 1 once the wrist has turned RollBlendDegrees from it.</summary>
    static float WristDeparture(int slot, Matrix wrist) {
        Matrix delta = s_idleWristInverse[slot] * wrist;
        float sx = new Vector3(delta.M11, delta.M12, delta.M13).Length();
        float sy = new Vector3(delta.M21, delta.M22, delta.M23).Length();
        float sz = new Vector3(delta.M31, delta.M32, delta.M33).Length();
        if (!(sx > 0.000001f) || !(sy > 0.000001f) || !(sz > 0.000001f)) return 0f;
        float trace = delta.M11 / sx + delta.M22 / sy + delta.M33 / sz;
        float angle = MathUtils.RadToDeg(MathF.Acos(MathUtils.Clamp((trace - 1f) * 0.5f, -1f, 1f)));
        if (!float.IsFinite(angle)) return 0f;
        float full = MathF.Max(KnifeTuning.RollBlendDegrees, 1f);
        return MathUtils.Saturate((angle - 0.2f * full) / (0.8f * full));
    }

    static float HoldAngleFor(int slot, string clip) =>
        s_holdAngles[slot] is { } holds && holds.TryGetValue(clip, out float angle) ? angle : 0f;

    /// <summary>
    /// Plays each inspect clip through the rigid carry at 30 fps and records, per
    /// arm, the largest roll it reaches -- but only if the wrist dwells there: at
    /// least a quarter of a second within five degrees of that extreme. A hold does;
    /// a swing merely passes through its extreme, however smoothly (its rate is zero
    /// at the turning point too, which is why dwell is the test, not rate). The
    /// square-to-eye in ResolveRoll ramps to exactly this angle.
    /// </summary>
    static void MeasureHolds(int variant) {
        for (int side = 0; side < 2; side++) {
            bool left = side == 1;
            int slot = variant * 2 + side;
            Dictionary<string, float> holds = new(StringComparer.Ordinal);
            s_holdAngles[slot] = holds;
            if (left && !s_leftArmUsable[variant]) continue;
            if (s_rollRef[slot].LengthSquared() < 0.5f) continue;
            s_measuring = true;
            try {
                foreach (string clip in CsmcKnifeRig.GetClipAliases(variant)) {
                    if (!clip.StartsWith("inspect", StringComparison.Ordinal)) continue;
                    float duration = CsmcKnifeRig.GetDuration(variant, clip);
                    if (!(duration > 0.05f)) continue;
                    const float step = 1f / 30f;
                    List<float> sizes = new();
                    for (float t = 0f; t <= duration + 0.0001f; t += step) {
                        KnifeRigPose pose = CsmcKnifeRig.Sample(variant, clip, MathF.Min(t, duration));
                        if (!SolveArm(pose, PlacementFor(pose, variant), Matrix.Identity, variant, left, false, out ArmFrame arm)) { sizes.Add(0f); continue; }
                        Vector3 faceOn = ProjectOntoPlane(Vector3.Normalize(arm.Grip), arm.Axis);
                        sizes.Add(MathF.Abs(SignedAngle(faceOn, arm.Side, arm.Axis)));
                    }
                    if (sizes.Count == 0) continue;
                    float best = sizes.Max();
                    int dwell = sizes.Count(size => size >= best - MathUtils.DegToRad(5f));
                    if (best > 0.01f && dwell * step >= 0.25f) holds[clip] = best;
                }
            }
            finally { s_measuring = false; }
            if (holds.Count > 0 && !left)
                KnifeLog.Information($"[ScCsgoKnives] {s_assetNames[variant]} holds: " + string.Join(", ", holds.Select(h => $"{h.Key}={MathUtils.RadToDeg(h.Value):0}deg")));
        }
    }

    /// <summary>Records each arm's idle face-on side in its hand bone's frame, and that frame's inverse; see ResolveRoll.</summary>
    static void SolveRollReferences(int variant) {
        KnifeRigPose idle = CsmcKnifeRig.Sample(variant, "idle", 0f);
        for (int side = 0; side < 2; side++) {
            bool left = side == 1;
            int slot = variant * 2 + side;
            s_rollRef[slot] = Vector3.Zero;
            s_lastSide[slot] = Vector3.Zero;
            s_lastClearance[slot] = 0f;
            s_handleFollow[slot] = Vector3.Zero;
            s_handlePrev[slot] = Vector3.Zero;
            s_handleIdleHand[slot] = Vector3.Zero;
            s_reGrip[slot] = 0f;
            s_flatSign[slot] = 1f;
            s_offsetDir[slot] = Vector3.Zero;
            s_square[slot] = 0f;
            s_stillness[slot] = 1f;
            s_prevPalm[slot] = Vector3.Zero;
            s_idleWristInverse[slot] = Matrix.Identity;
            if (left && !s_leftArmUsable[variant]) continue;
            // The roll reference lives in the true wrist's frame (hand_r), never the
            // held part's: see SolveArm. s_idleWristInverse pairs with it in WristDeparture.
            Matrix wrist = idle.GetBinding(left ? "hand_l" : "hand_r") * s_placement[variant];
            Matrix inverse = Matrix.Invert(wrist);
            if (!KnifeDiagnostics.IsFinite(inverse)) continue;
            s_idleWristInverse[slot] = inverse;
            if (!SolveArm(idle, s_placement[variant], Matrix.Identity, variant, left, false, out ArmFrame arm)) continue;
            Vector3 local = Vector3.TransformNormal(arm.Side, inverse);
            float length = local.Length();
            if (float.IsFinite(length) && length > 0.0001f) s_rollRef[slot] = local / length;
            if (!left && !CsmcKnifeRig.IsGun(variant)) {
                s_handleIdleHand[slot] = FollowHandle(slot, idle, HandleDirection(variant), true, out _);
                s_handleRadius[variant] = HandleRadius(variant, HandleDirection(variant));
            }
        }
    }

    /// <summary>
    /// The handle's half-thickness: the median distance of its hull points from its
    /// own axis through the grip. A constant per knife, so the fist sits a fixed
    /// distance off the handle's centre line instead of sliding after a handle the
    /// fingers are moving.
    /// </summary>
    static float HandleRadius(int variant, Vector3 handleLocal) {
        var points = new List<Vector3>();
        foreach (KnifeHulls.Part part in KnifeHulls.Parts(variant)) points.AddRange(part.Handle);
        if (points.Count == 0) return 0f;
        // distances from the handle's own centre line: through the hull's centroid, along the handle
        Vector3 centre = Vector3.Zero;
        foreach (Vector3 point in points) centre += point;
        centre /= points.Count;
        var distances = new List<float>(points.Count);
        foreach (Vector3 point in points) {
            Vector3 offset = point - centre;
            Vector3 across = offset - handleLocal * Vector3.Dot(offset, handleLocal);
            float d = across.Length();
            if (float.IsFinite(d)) distances.Add(d);
        }
        if (distances.Count == 0) return 0f;
        distances.Sort();
        return distances[distances.Count / 2];
    }

    /// <summary>How far the inspect clip rolls the right arm away from face-on, for the log.</summary>
    static float InspectRollDegrees(int variant) {
        float duration = CsmcKnifeRig.GetDuration(variant, "inspect");
        float most = 0f;
        for (int i = 0; i <= 24; i++) {
            KnifeRigPose sample = CsmcKnifeRig.Sample(variant, "inspect", duration * i / 24f);
            if (!SolveArm(sample, s_placement[variant], Matrix.Identity, variant, false, false, out ArmFrame arm)) continue;
            Vector3 faceOn = ProjectOntoPlane(Vector3.Normalize(arm.Grip), arm.Axis);
            float turn = MathUtils.RadToDeg(MathF.Acos(MathUtils.Clamp(Vector3.Dot(arm.Side, faceOn), -1f, 1f)));
            if (float.IsFinite(turn)) most = MathF.Max(most, turn);
        }
        return most;
    }

    /// <summary>
    /// Draws one arm. <paramref name="placement"/> must already include the body
    /// motion: the arm's lean and its run off the bottom of the frame are fitted in
    /// screen space, so anything applied after this point would invalidate both.
    /// Building it before the body motion and multiplying afterwards is what left
    /// the arms as short stubs detached from the knife while switching weapons.
    /// </summary>
    static void DrawArm(Camera camera, Matrix projection, Model model, Texture2D skin, float light, KnifeRigPose pose, Matrix placement, Matrix post, int variant, bool left) {
        if (!SolveArm(pose, placement, post, variant, left, KnifeClock.Commit, out ArmFrame arm)) {
            KnifeDiagnostics.WarnOnce($"arm-degenerate-{(left ? "l" : "r")}", "Arm collapsed to zero length; skipped.");
            return;
        }
        DrawArmFrame(camera, projection, model, skin, light, in arm, left);
    }

    static void DrawArmExact(Camera camera, Matrix projection, Model model, Texture2D skin, float light, KnifeRigPose pose, Matrix placementWithPost, int variant, bool left) {
        if (!ExactArmFrame(pose, placementWithPost, variant, left, out ArmFrame arm, out float twist)) {
            KnifeDiagnostics.WarnOnce($"arm-degenerate-{(left ? "l" : "r")}", "Arm collapsed to zero length; skipped.");
            return;
        }
        if (KnifeClock.Commit) {
            Matrix wrist = pose.GetBoneFrame(left ? "hand_l" : "hand_r") * placementWithPost;
            if (left) LastLeft = ExactSample(in arm, twist, wrist); else LastRight = ExactSample(in arm, twist, wrist);
        }
        DrawArmFrame(camera, projection, model, skin, light, in arm, left);
    }

    static void DrawArmFrame(Camera camera, Matrix projection, Model model, Texture2D skin, float light, in ArmFrame arm, bool left) {

        // Models/FirstPersonHand spans local x 0..7.87, y -3.94..3.94, z -27.56..0: it
        // is one-sided in x. Seating its origin on the grip, as every build up to
        // 0.10.0 did, put the handle on the box's face instead of through its
        // middle -- the clipping every screenshot showed. Shift the origin half a
        // width so the box is centred on the grip and the handle runs through it.
        float width = arm.ViewWidth / HandBoxWidth;
        float thickness = left ? -width : width;
        Vector3 origin = arm.Seat - arm.Side * (0.5f * arm.ViewWidth * MathF.Sign(thickness));
        float stretch = (arm.Reach + arm.Overshoot) / HandBoxLength;
        Matrix frame = new() {
            M11 = arm.Side.X, M12 = arm.Side.Y, M13 = arm.Side.Z, M14 = 0f,
            M21 = arm.Up.X, M22 = arm.Up.Y, M23 = arm.Up.Z, M24 = 0f,
            M31 = arm.Axis.X, M32 = arm.Axis.Y, M33 = arm.Axis.Z, M34 = 0f,
            M41 = origin.X, M42 = origin.Y, M43 = origin.Z, M44 = 1f
        };
        Matrix world = Matrix.CreateScale(HandModelScale)
            * Matrix.CreateScale(thickness, width, stretch)
            * Matrix.CreateTranslation(0f, 0f, HandBoxLength * stretch)
            * frame;

        // Vanilla draws this model with World[0] = scale(0.01) * ... and never
        // touches the parent bone. FirstPersonHand.dae already carries that
        // 0.01 in its node transform, so applying both shrank the arm a
        // hundredfold, which is why 0.5.0 showed no arms at all.
        DrawModel(model, skin, world, camera, projection, light, SamplerState.PointClamp,
            RasterizerState.CullNoneScissor, applyBoneTransform: false);
        if (!s_handsLogged && !left) {
            s_handsLogged = true;
            KnifeLog.Information(
                $"[ScCsgoKnives] arms attached: grip={Format(arm.Grip)}, cap={Format(arm.Seat)}, elbow={Format(arm.Elbow)}, lean={arm.Lean:0.#}deg, "
                + $"reach={arm.Reach:0.###}, width={arm.ViewWidth:0.###}, overshoot={arm.Overshoot:0.###}, skin={skin.Width}x{skin.Height}, meshes={model.Meshes.Count}, light={light:0.###}."
            );
        }
    }

    static Vector3 ProjectOntoPlane(Vector3 value, Vector3 normal) {
        Vector3 projected = value - normal * Vector3.Dot(value, normal);
        float length = projected.Length();
        if (!float.IsFinite(length) || length < 0.00001f) {
            projected = Vector3.Cross(normal, Vector3.UnitY);
            length = projected.Length();
            if (!float.IsFinite(length) || length < 0.00001f) return Vector3.UnitX;
        }
        return projected / length;
    }

    static string Format(Vector3 value) => $"({value.X:0.###},{value.Y:0.###},{value.Z:0.###})";

    static void EnsureLoaded() {
        if (s_loaded) return;
        for (int variant = 0; variant < s_count; variant++) {
            // One unreadable model must not take the other knives down with it.
            try {
                LoadVariant(variant);
            }
            catch (Exception e) {
                KnifeLog.Error($"[ScCsgoKnives] failed to load {s_assetNames[variant]}: {e.Message}");
            }
        }
        s_loaded = true;
    }

    /// <summary>Recomputes every knife's placement after a tuning change.</summary>
    /// <summary>A frame of the headless sweep: both arms as solved, and every mesh part's world matrix.</summary>
    public sealed class HeadlessFrame {
        public float T;
        public string Clip;
        public QaSample Right, Left;
        public Dictionary<string, Matrix> Parts = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// Runs the arm maths without the game (tools/ArmPreview): the projection
    /// defaults to the fitted one, logs go to the console, nothing is loaded from
    /// ContentManager. The placement, roll references and holds are built exactly
    /// as in play, and the temporal state runs on the virtual clock.
    /// </summary>
    public static void InitHeadless(float fx, float fy) {
        KnifeLog.ToConsole = true;
        if (fx > 0.0001f && fy > 0.0001f) (s_projX, s_projY) = EffectiveProjection(fx, fy);
        s_handAnchor = ToViewSpace(KnifeTuning.AnchorScreenX, KnifeTuning.AnchorScreenY, KnifeTuning.AnchorDepth);
    }

    public static List<HeadlessFrame> SweepHeadless(int variant, string clip, int fps) {
        variant = Math.Clamp(variant, 0, s_count - 1);
        KnifeClock.Reset(1f / fps);
        s_weaponFov = WeaponFovDegrees(variant);
        (s_projX, s_projY) = EffectiveProjection(s_projX * s_projY / MathF.Max(s_projY, 0.0001f), s_projY);
        BuildPlacement(variant);
        List<HeadlessFrame> frames = new();
        float duration = CsmcKnifeRig.GetDuration(variant, clip);
        int count = (int)MathF.Round(duration * fps);
        for (int i = 0; i <= count; i++) {
            float t = MathF.Min(i / (float)fps, duration);
            KnifeRigPose pose = CsmcKnifeRig.Sample(variant, clip, t);
            Matrix placement = PlacementFor(pose, variant);
            HeadlessFrame frame = new() { T = t, Clip = pose.ClipAlias };
            if (BoneArms(variant)) {
                if (ExactArmFrame(pose, placement, variant, false, out ArmFrame right, out float rightTwist)) frame.Right = ExactSample(in right, rightTwist, pose.GetBoneFrame("hand_r") * placement);
                if (s_leftArmUsable[variant] && ExactArmFrame(pose, placement, variant, true, out ArmFrame leftArm, out float leftTwist)) frame.Left = ExactSample(in leftArm, leftTwist, pose.GetBoneFrame("hand_l") * placement);
            }
            else {
                if (SolveArm(pose, placement, Matrix.Identity, variant, false, true, out _)) frame.Right = LastRight;
                if (s_leftArmUsable[variant] && SolveArm(pose, placement, Matrix.Identity, variant, true, true, out _)) frame.Left = LastLeft;
            }
            foreach (string part in CsmcKnifeRig.GetMeshParts(variant)) {
                frame.Parts[part] = part == LeftHeldPart && !BoneArms(variant)
                    ? pose.GetBinding(part) * placement * Matrix.CreateTranslation(s_leftHandCorrection[variant])
                    : pose.GetBinding(part) * placement;
            }
            frames.Add(frame);
            KnifeClock.Tick();
        }
        KnifeClock.Release();
        return frames;
    }

    /// <summary>
    /// A projection change only marks every placement stale; each variant is rebuilt the next
    /// time it is drawn (EnsurePlacement). Rebuilding all 25 at once re-measured every knife's
    /// inspect holds on each step of the AWP's quarter-second scope ease, which froze the game
    /// for the whole zoom (0.15.x "开镜卡").
    /// </summary>
    static readonly bool[] s_placementStale = new bool[s_count];

    public static void RebuildPlacements() {
        if (!s_loaded) return;
        for (int variant = 0; variant < s_count; variant++) s_placementStale[variant] = true;
    }

    static void EnsurePlacement(int variant) {
        if (!s_placementStale[variant]) return;
        s_placementStale[variant] = false;
        try {
            BuildPlacement(variant);
        }
        catch (Exception e) {
            KnifeLog.Error($"[ScCsgoKnives] failed to place {s_assetNames[variant]}: {e.Message}");
        }
    }

    /// <summary>
    /// How much the rig is shrunk to reach the screen; see BuildPlacement. The
    /// per-weapon factor is CS:MC's own: its photos show the karambit drawn at
    /// nearly the M9's size on screen although it is a much shorter knife.
    /// </summary>
    static float RigScale(int variant) =>
        KnifeTuning.KnifeScale * s_fist[variant].Scale * CsmcKnifeRig.GetSourceReferenceScale(variant) / ReferenceSourceScale;

    static void BuildPlacement(int variant) {
        SolveHeldPartGrip(variant);
        float scale = RigScale(variant);
        // The trailing view-space pitch stands in for the tilt Minecraft's own
        // first-person hand matrix gave CS:MC's composition; the grip is pinned
        // after it, so it rotates every knife about its own grip.
        Matrix orientation = Matrix.CreateScale(scale)
            * Matrix.CreateRotationZ(MathUtils.DegToRad(270f))
            * Matrix.CreateRotationY(MathUtils.DegToRad(180f))
            * Matrix.CreateRotationX(MathUtils.DegToRad(90f))
            * Matrix.CreateRotationX(MathUtils.DegToRad(KnifeTuning.KnifePitchDegrees))
            * Matrix.CreateRotationY(MathUtils.DegToRad(KnifeTuning.KnifeYawDegrees));
        KnifeRigPose idlePose = CsmcKnifeRig.Sample(variant, "idle", 0f);
        Matrix idleHand = idlePose.GetBinding("hand_r");
        s_idleGrips[variant] = Vector3.Transform(s_gripOffsets[variant], idleHand);
        Vector3 idleGrip = Vector3.Transform(s_gripOffsets[variant], idleHand * orientation);
        s_placement[variant] = orientation * Matrix.CreateTranslation(AnchorFor(variant) - idleGrip);
        if (Exact) {
            // The exact chain owns the placement; the fist solver still hangs its elbow
            // off the idle grip, so that grip is where the chain actually puts it.
            s_placement[variant] = ExactPlacement(variant);
            s_exactIdleGrips[variant] = Vector3.Transform(RightGrip(variant), idlePose.GetBinding(RightWristBone(variant)) * s_placement[variant]);
        }
        // Guns: both arms ride their bones, so none of the fist solver's per-knife
        // measurements (left-hand pin, roll references, hold detection) apply.
        if (CsmcKnifeRig.IsGun(variant)) return;
        SolveLeftHandCorrection(variant);
        SolveRollReferences(variant);
        MeasureHolds(variant);
    }

    static void LoadVariant(int variant) {
        string asset = s_assetNames[variant];
        SolveHeldPartGrip(variant);
        float scale = RigScale(variant);

        // Decompiled CSMC b$4lx, converted from JOML column-vector order to
        // Engine row-vector order: T*Rx*Ry*Rz*S -> S*Rz*Ry*Rx*T. Its own
        // translation is dropped in favour of the hand anchor below, and the
        // trailing pitch stands in for MC's first-person hand tilt.
        Matrix orientation = Matrix.CreateScale(scale)
            * Matrix.CreateRotationZ(MathUtils.DegToRad(270f))
            * Matrix.CreateRotationY(MathUtils.DegToRad(180f))
            * Matrix.CreateRotationX(MathUtils.DegToRad(90f))
            * Matrix.CreateRotationX(MathUtils.DegToRad(KnifeTuning.KnifePitchDegrees))
            * Matrix.CreateRotationY(MathUtils.DegToRad(KnifeTuning.KnifeYawDegrees));

        s_baseColor[variant] = ContentManager.Get<Texture2D>($"Textures/ScCsgoKnives/{asset}");
        s_parts[variant] = CsmcKnifeRig.GetMeshParts(variant).Select(binding => new Part {
            Binding = binding,
            Model = ContentManager.Get<ObjModel>($"Models/ScCsgoKnives/{asset}_{binding}")
        }).ToArray();

        // Pin the idle grip to the resolved screen anchor so every clip animates around a
        // fixed, reachable point, and all knives sit at the same place.
        Matrix idleHand = CsmcKnifeRig.Sample(variant, "idle", 0f).GetBinding("hand_r");
        s_idleGrips[variant] = Vector3.Transform(s_gripOffsets[variant], idleHand);
        Vector3 idleGrip = Vector3.Transform(s_gripOffsets[variant], idleHand * orientation);
        s_placement[variant] = orientation * Matrix.CreateTranslation(AnchorFor(variant) - idleGrip);
        SolveLeftHandCorrection(variant);
        SolveRollReferences(variant);
        MeasureHolds(variant);
        FistSpec fist = s_fist[variant];
        (float leftX, float leftY) = LeftTargetFor(variant);
        KnifeLog.Information(
            $"[ScCsgoKnives] placement {asset}: idleGrip={Format(idleGrip)}, anchor={Format(AnchorFor(variant))}, "
            + $"knifeScale={scale:0.###} (x{fist.Scale:0.###}), fist lean={LeanFor(variant, false):0.#} overshoot={OvershootFor(variant, false):0.##}w, "
            + $"leftTarget=({leftX:0.###},{leftY:0.###}) leftLean={LeanFor(variant, true):0.#}, leftHandCorrection={Format(s_leftHandCorrection[variant])}, "
            + $"inspect rolls the right arm up to {InspectRollDegrees(variant):0}deg from face-on."
        );
    }

    /// <summary>
    /// The knife's mesh parts (by CSMC binding name) and base colour, for the
    /// inspect showcase, which draws the same geometry under its own camera.
    /// </summary>
    internal static bool TryGetInspectParts(int variant, out List<(string Binding, Model Model)> parts, out Texture2D baseColor) {
        EnsureLoaded();
        parts = null;
        baseColor = null;
        if (variant < 0 || variant >= s_count || s_parts[variant] is null || s_baseColor[variant] is null) return false;
        parts = s_parts[variant].Select(p => (p.Binding, (Model)p.Model)).ToList();
        baseColor = s_baseColor[variant];
        return true;
    }

    static void DrawModel(Model model, Texture2D texture, Matrix world, Camera camera, Matrix projection, float light, SamplerState sampler, RasterizerState rasterizer, bool applyBoneTransform) {
        if (!KnifeDiagnostics.IsFinite(world)) {
            KnifeDiagnostics.WarnOnce("model-matrix-invalid", "First-person model matrix is not finite; draw skipped.");
            return;
        }
        Display.DepthStencilState = DepthStencilState.Default;
        Display.RasterizerState = rasterizer;
        ComponentFirstPersonModel.LitShader.Texture = texture;
        ComponentFirstPersonModel.LitShader.SamplerState = sampler;
        ComponentFirstPersonModel.LitShader.MaterialColor = Vector4.One;
        ComponentFirstPersonModel.LitShader.AmbientLightColor = new Vector3(light * LightingManager.LightAmbient);
        ComponentFirstPersonModel.LitShader.DiffuseLightColor1 = new Vector3(light);
        ComponentFirstPersonModel.LitShader.DiffuseLightColor2 = new Vector3(light);
        ComponentFirstPersonModel.LitShader.LightDirection1 = Vector3.TransformNormal(LightingManager.DirectionToLight1, camera.ViewMatrix);
        ComponentFirstPersonModel.LitShader.LightDirection2 = Vector3.TransformNormal(LightingManager.DirectionToLight2, camera.ViewMatrix);
        ComponentFirstPersonModel.LitShader.Transforms.View = Matrix.Identity;
        ComponentFirstPersonModel.LitShader.Transforms.Projection = projection;

        foreach (ModelMesh mesh in model.Meshes) {
            ComponentFirstPersonModel.LitShader.Transforms.World[0] = applyBoneTransform
                ? BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone) * world
                : world;
            foreach (ModelMeshPart part in mesh.MeshParts) {
                Display.DrawIndexed(
                    PrimitiveType.TriangleList,
                    ComponentFirstPersonModel.LitShader,
                    part.VertexBuffer,
                    part.IndexBuffer,
                    part.StartIndex,
                    part.IndicesCount
                );
            }
        }
    }
}
