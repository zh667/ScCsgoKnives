using Engine;
using Engine.Graphics;

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
/// rig moves the grip; the arm pivots about its elbow to follow, and never rolls,
/// because a forearm cannot spin about its own axis and the reference arm never
/// does.
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
    static readonly Vector3[] s_gripOffsets = [
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

    // Only the shadow daggers put a weapon record in the left hand; every other
    // knife leaves it empty, so that hand is drawn at its own bone with no
    // offset. Having no left offset at all is what stopped the second dagger
    // from ever reaching its hand.
    static readonly Vector3 ShadowDaggerLeftGrip = new(0.5970f, 0.1456f, -0.2224f);
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
    static readonly FistSpec s_clawFist = new(0.6443f, 0.8120f, 34.5f, 0.88f, 1.18f, 0.2677f, 0.8378f, -54.0f, 1.60f, 0.098f, 1.72f, 0.0865f);
    static readonly Dictionary<string, FistSpec> s_measuredFists = new() {
        ["m9"] = new(0.7099f, 0.8426f, 7.5f, 0.95f, 1.00f),
        ["butterfly"] = new(0.7234f, 0.8065f, 5.5f, 0.50f, 1.045f),
        ["tactical"] = new(0.7224f, 0.8426f, 6.0f, 0.76f, 1.045f),
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
    static readonly bool[] s_logged = new bool[s_count];
    // Where each knife's idle left grip lands in view space once corrected; the
    // left elbow is projected down from here so the left arm pivots rather than
    // slides when a clip moves the hand.
    static readonly Vector3[] s_idleLeftGrips = new Vector3[s_count];
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
            usable[variant] = forearm >= MinUsableForearm;
            if (!usable[variant]) {
                Log.Warning($"[ScCsgoKnives] {CsmcKnifeRig.GetAssetName(variant)} has a degenerate left forearm ({forearm:0.###}); left arm disabled.");
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

    /// <summary>Forces the screen-space anchor to be resolved again on the next frame.</summary>
    public static void InvalidateProjection() => s_projX = 0f;   // any value the camera cannot report

    /// <summary>Lets the composition report print again after a tuning change.</summary>
    public static void ResetCompositionLog() => Array.Clear(s_compositionNextLog);

    /// <summary>
    /// Keeps the view-space anchor in step with the camera. Returns true when it
    /// moved, so the caller knows the placements need rebuilding.
    /// </summary>
    static bool SyncProjection(Camera camera) {
        float fx = camera.ProjectionMatrix.M11, fy = camera.ProjectionMatrix.M22;
        if (!float.IsFinite(fx) || !float.IsFinite(fy) || fx <= 0.0001f || fy <= 0.0001f) return false;
        if (fx == s_projX && fy == s_projY) return false;
        s_projX = fx;
        s_projY = fy;
        s_handAnchor = ToViewSpace(KnifeTuning.AnchorScreenX, KnifeTuning.AnchorScreenY, KnifeTuning.AnchorDepth);
        Log.Information(
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
    public static bool Draw(ComponentFirstPersonModel firstPerson, Camera camera, int variant, KnifeRigPose pose) {
        if (pose is null) return false;
        EnsureLoaded();
        KnifeTuning.Poll();
        // Field of view is a player setting and the aspect follows the window, so
        // the screen-space anchor has to be re-resolved whenever either changes.
        if (SyncProjection(camera)) RebuildPlacements();
        variant = Math.Clamp(variant, 0, s_count - 1);
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
        Matrix placement = s_placement[variant];
        if (pose.ClipAlias.StartsWith("inspect", StringComparison.Ordinal)) {
            Vector3 grip = Vector3.Transform(s_gripOffsets[variant], pose.GetBinding("hand_r"));
            placement = Matrix.CreateTranslation((s_idleGrips[variant] - grip) * (1f - KnifeTuning.InspectTravelScale)) * placement;
        }
        Matrix root = placement * post;

        float light = LightingManager.LightIntensityByLightValue[Math.Clamp(firstPerson.m_itemLight, 0, 15)];
        LogComposition(firstPerson, variant, pose, placement, post);
        DrawHands(firstPerson, camera, variant, pose, placement, post, light);
        foreach (Part part in s_parts[variant]) {
            DrawModel(part.Model, s_baseColor[variant], pose.GetBinding(part.Binding) * root, camera, light,
                SamplerState.LinearClamp, RasterizerState.CullNoneScissor, applyBoneTransform: true);
        }

        if (!s_logged[variant]) {
            s_logged[variant] = true;
            Log.Information(
                $"[ScCsgoKnives] first-person render active: asset={s_assetNames[variant]}, clip={pose.SourceClip}, "
                + $"parts=[{string.Join(',', s_parts[variant].Select(part => part.Binding))}], root={KnifeDiagnostics.MatrixSummary(root)}."
            );
        }
        return true;
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
            if (!SolveArm(pose, placement * post, post, variant, left, out ArmFrame arm)) { line.Append($"{label}=degenerate "); continue; }
            Vector2? hand = ToScreen(arm.Grip);
            Vector2? cap = ToScreen(arm.Seat);
            Vector2? elbow = ToScreen(arm.Elbow);
            if (hand is null || cap is null || elbow is null) { line.Append($"{label}=behind eye "); continue; }
            line.Append($"{label}: grip=({hand.Value.X:0.###},{hand.Value.Y:0.###}) cap=({cap.Value.X:0.###},{cap.Value.Y:0.###}) ");
            line.Append($"lean={arm.Lean:+0.0;-0.0}deg width={arm.ViewWidth * s_projX / (2f * -arm.Grip.Z):0.###} overshoot={OvershootFor(variant, left):0.##}w ");
            line.Append($"elbow=({elbow.Value.X:0.###},{elbow.Value.Y:0.###}) depth={-arm.Grip.Z:0.###} ");
        }
        line.Append("| MCCS m9 photo: grip=(0.710,0.843) cap=(0.699,0.692) lean=+7.5, left grip=(0.326,0.901) cap=(0.381,0.823) lean=-51.5.");
        Log.Information(line.ToString());
    }

    static void DrawHands(ComponentFirstPersonModel firstPerson, Camera camera, int variant, KnifeRigPose pose, Matrix placement, Matrix post, float light) {
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
        DrawArm(camera, hand, skin, light, pose, placement * post, post, variant, false);
        if (s_leftArmUsable[variant]) DrawArm(camera, hand, skin, light, pose, placement * post, post, variant, true);
    }

    /// <summary>One arm's box for this frame, before it becomes a matrix.</summary>
    struct ArmFrame {
        public Vector3 Grip, Elbow, Seat, Axis, Side, Up;
        public float Lean, ViewWidth, Overshoot, Reach;
    }

    /// <summary>
    /// Solves one arm. <paramref name="placement"/> must already include the body
    /// motion, and <paramref name="post"/> is that motion on its own: the elbow is
    /// fixed to the body, not to the hand, so it is projected from where the idle
    /// grip sits after the body motion rather than from this frame's grip.
    /// </summary>
    static bool SolveArm(KnifeRigPose pose, Matrix placement, Matrix post, int variant, bool left, out ArmFrame arm) {
        arm = default;
        string bone = left ? "l" : "r";
        Matrix wrist = pose.GetBinding($"hand_{bone}") * placement;
        Vector3 grip = Vector3.Transform(left ? s_leftGripOffsets[variant] : s_gripOffsets[variant], wrist);
        if (left) grip += s_leftHandCorrection[variant];

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
        Vector3 idleGrip = Vector3.Transform(left ? s_idleLeftGrips[variant] : AnchorFor(variant), post);
        Vector3 elbow = ProjectDownArm(idleGrip, lean, NearFor(variant, left));
        Vector3 span = elbow - grip;
        float reach = span.Length();
        if (!float.IsFinite(reach) || reach < 0.0001f) return false;
        Vector3 axis = span / reach;

        // Face-on: the box's broad face looks at the eye whatever the knife does,
        // which is how every reference photo shows it. The handle passes through the
        // box's middle, so which way it points no longer decides how the arm is
        // rolled -- rolling the arm to keep the handle flat on one face is what spun
        // the forearm right round during an inspect.
        Vector3 side = ProjectOntoPlane(Vector3.Normalize(grip), axis);
        Vector3 up = Vector3.Normalize(Vector3.Cross(side, axis));

        // CS:MC's arm box is a fixed size, not one scaled to the forearm, so the width
        // is the fraction of the frame the reference arm covers at the hand, resolved
        // against the live projection at the hand's depth. The fist reaches past the
        // grip by a measured fraction of that width; that is what buries the handle.
        float depth = MathF.Max(-grip.Z, 0.01f);
        float viewWidth = ScreenWidthFor(variant, left) * 2f * depth / MathF.Max(s_projX, 0.0001f);
        float overshoot = viewWidth * OvershootFor(variant, left);
        arm = new ArmFrame {
            Grip = grip, Elbow = elbow, Axis = axis, Side = side, Up = up, Lean = lean,
            ViewWidth = viewWidth, Overshoot = overshoot, Reach = reach,
            Seat = grip - axis * overshoot,
        };
        return true;
    }

    /// <summary>
    /// Draws one arm. <paramref name="placement"/> must already include the body
    /// motion: the arm's lean and its run off the bottom of the frame are fitted in
    /// screen space, so anything applied after this point would invalidate both.
    /// Building it before the body motion and multiplying afterwards is what left
    /// the arms as short stubs detached from the knife while switching weapons.
    /// </summary>
    static void DrawArm(Camera camera, Model model, Texture2D skin, float light, KnifeRigPose pose, Matrix placement, Matrix post, int variant, bool left) {
        if (!SolveArm(pose, placement, post, variant, left, out ArmFrame arm)) {
            KnifeDiagnostics.WarnOnce($"arm-degenerate-{(left ? "l" : "r")}", "Arm collapsed to zero length; skipped.");
            return;
        }

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
        DrawModel(model, skin, world, camera, light, SamplerState.PointClamp,
            RasterizerState.CullNoneScissor, applyBoneTransform: false);
        if (!s_handsLogged && !left) {
            s_handsLogged = true;
            Log.Information(
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
                Log.Error($"[ScCsgoKnives] failed to load {s_assetNames[variant]}: {e.Message}");
            }
        }
        s_loaded = true;
    }

    /// <summary>Recomputes every knife's placement after a tuning change.</summary>
    public static void RebuildPlacements() {
        if (!s_loaded) return;
        for (int variant = 0; variant < s_count; variant++) {
            try {
                BuildPlacement(variant);
            }
            catch (Exception e) {
                Log.Error($"[ScCsgoKnives] failed to place {s_assetNames[variant]}: {e.Message}");
            }
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
        Matrix idleHand = CsmcKnifeRig.Sample(variant, "idle", 0f).GetBinding("hand_r");
        s_idleGrips[variant] = Vector3.Transform(s_gripOffsets[variant], idleHand);
        Vector3 idleGrip = Vector3.Transform(s_gripOffsets[variant], idleHand * orientation);
        s_placement[variant] = orientation * Matrix.CreateTranslation(AnchorFor(variant) - idleGrip);
        SolveLeftHandCorrection(variant);
    }

    static void LoadVariant(int variant) {
        string asset = s_assetNames[variant];
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
        FistSpec fist = s_fist[variant];
        (float leftX, float leftY) = LeftTargetFor(variant);
        Log.Information(
            $"[ScCsgoKnives] placement {asset}: idleGrip={Format(idleGrip)}, anchor={Format(AnchorFor(variant))}, "
            + $"knifeScale={scale:0.###} (x{fist.Scale:0.###}), fist lean={LeanFor(variant, false):0.#} overshoot={OvershootFor(variant, false):0.##}w, "
            + $"leftTarget=({leftX:0.###},{leftY:0.###}) leftLean={LeanFor(variant, true):0.#}, leftHandCorrection={Format(s_leftHandCorrection[variant])}."
        );
    }

    static void DrawModel(Model model, Texture2D texture, Matrix world, Camera camera, float light, SamplerState sampler, RasterizerState rasterizer, bool applyBoneTransform) {
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
        ComponentFirstPersonModel.LitShader.Transforms.Projection = camera.ProjectionMatrix;

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
