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

    public static float InspectTravelScale = 0.55f;

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
                Log.Information($"[ScCsgoKnives] tuning file was written by a build with different defaults; rewriting {Path}.");
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
        Log.Information(
            $"[ScCsgoKnives] tuning reloaded ({applied} values): knifeScale={KnifeScale:0.###}, "
            + $"anchor=({AnchorScreenX:0.###},{AnchorScreenY:0.###})@{AnchorDepth:0.##}, "
            + $"lean R={RightArmLean:0.#} L={LeftArmLean:0.#}, near R={RightArmNear:0.###} L={LeftArmNear:0.###}, "
            + $"armWidth R={ArmScreenWidth:0.###} L={LeftArmScreenWidth:0.###}, fistOvershoot={ArmPalmOvershoot:0.###}w, "
            + $"pitch/yaw={KnifePitchDegrees:0.#}/{KnifeYawDegrees:0.#}, leftTarget=({LeftHandTargetScreenX:0.###},{LeftHandTargetScreenY:0.###})."
        );
    }

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
            case nameof(RollSlewDegreesPerSecond): RollSlewDegreesPerSecond = v; return true;
            case nameof(SwapDipScale): SwapDipScale = v; return true;
            case nameof(InspectTravelScale): InspectTravelScale = v; return true;
            default: return false;
        }
    }

    /// <summary>Writes the file with the current values so there is something to edit.</summary>
    public static void Write() {
        try {
            using Stream stream = Storage.OpenFile(Path, OpenFileMode.Create);
            byte[] bytes = new UTF8Encoding(false).GetBytes(Serialize(Version));
            stream.Write(bytes, 0, bytes.Length);
            Log.Information($"[ScCsgoKnives] wrote tuning file {Path}; edit it and it reloads within a second.");
        }
        catch (Exception e) {
            KnifeDiagnostics.WarnOnce("tuning-write", $"Could not write {Path}: {e.Message}");
        }
    }

    static string Serialize(int version) {
        var text = new StringBuilder();
        text.AppendLine("# ScCsgoKnives 第一人称调参");
        text.AppendLine("# 保存后 1 秒内在游戏里生效，不用重启，不用重装。");
        text.AppendLine("# 下面的默认值是拿 MCCS 的 4 张天空截图（M9/爪子/蝴蝶/猎杀者）逐像素拟合出来的：");
        text.AppendLine("# 刀身整条轮廓对齐照片（tools/fistsolve.py），拳头盒子按手臂剪影重合度拟合（tools/fistfit.py）。");
        text.AppendLine("# 这 4 把刀各自量到的拳头位置/倾角/越过量/缩放写死在代码的 fist 表里，这里是其它刀共用的默认值。");
        text.AppendLine();
        text.AppendLine("# 改这行会让整个文件被重写回默认值，别动。");
        text.AppendLine($"TuningVersion = {version}");
        text.AppendLine();
        text.AppendLine("# 刀的大小（M9 的；其它刀在代码里有各自的倍率）。整套构图跟着一起缩放。");
        text.AppendLine(Line(nameof(KnifeScale), KnifeScale));
        text.AppendLine("# 所有刀共用的俯仰/偏航（度）。");
        text.AppendLine(Line(nameof(KnifePitchDegrees), KnifePitchDegrees));
        text.AppendLine(Line(nameof(KnifeYawDegrees), KnifeYawDegrees));
        text.AppendLine();
        text.AppendLine("# 待机时握把（=拳头中心，握柄中轴穿过手臂中轴的那一点）停在屏幕哪里（0~1 画面比例，x 向右，y 向下），以及离眼睛多远。");
        text.AppendLine("# 用画面比例而不是三维坐标，是因为 SC 的视角是 80*视野设置，玩家改了视野也不会跑位。");
        text.AppendLine(Line(nameof(AnchorScreenX), AnchorScreenX));
        text.AppendLine(Line(nameof(AnchorScreenY), AnchorScreenY));
        text.AppendLine(Line(nameof(AnchorDepth), AnchorDepth));
        text.AppendLine();
        text.AppendLine("# 手臂在画面上的固定倾角（度，0=竖直向下，正=向右倒）。手肘固定在画面外，动画抬手时手臂绕手肘转，不会自转。");
        text.AppendLine(Line(nameof(RightArmLean), RightArmLean));
        text.AppendLine(Line(nameof(LeftArmLean), LeftArmLean));
        text.AppendLine();
        text.AppendLine("# 手肘比手更靠近眼睛的倍数，决定手臂往画面下方变粗的程度。");
        text.AppendLine(Line(nameof(RightArmNear), RightArmNear));
        text.AppendLine(Line(nameof(LeftArmNear), LeftArmNear));
        text.AppendLine();
        text.AppendLine("# 手臂粗细：手的位置上手臂占画面宽度的比例。MCCS 的拳头 1920 宽下是 178 像素。");
        text.AppendLine(Line(nameof(ArmScreenWidth), ArmScreenWidth));
        text.AppendLine(Line(nameof(LeftArmScreenWidth), LeftArmScreenWidth));
        text.AppendLine("# 拳头越过握把多少（手臂宽度的倍数）。握柄埋在拳头里、两头只露护手和刀尾，靠的就是这个。");
        text.AppendLine(Line(nameof(ArmPalmOvershoot), ArmPalmOvershoot));
        text.AppendLine("# 手臂往画面下方伸到哪（>1 表示伸出画面外）。");
        text.AppendLine(Line(nameof(ArmExitY), ArmExitY));
        text.AppendLine("# 检视/挥砍时手臂怎么滚转：0 = 拳头永远正对相机；1 = 刚性跟着手腕（CS:MC b$4la 的做法，刀柄保持待机时和拳面的夹角）；");
        text.AppendLine("# 2 = 转到让刀柄平贴拳面（配合 FistGripFace 把那个面放在刀柄上，刀柄就躺在拳头上而不是穿过去）。三种待机都一样。");
        text.AppendLine(Line(nameof(ArmRollMode), ArmRollMode));
        text.AppendLine("# 握把在拳头截面上的位置（沿视线的半宽数）：0 = 盒子中心，1 = 远离眼睛的那个面（待机时握柄藏在拳头后面，手腕翻过来后躺在拳面上），-1 = 近面。");
        text.AppendLine(Line(nameof(FistGripFace), FistGripFace));
        text.AppendLine("# 模式 2 从手腕离开待机姿态多少度开始完全生效（低于它的 1/5 时保持正对相机），待机和呼吸动画因此不受影响。");
        text.AppendLine(Line(nameof(RollBlendDegrees), RollBlendDegrees));
        text.AppendLine("# 刀在检视里会被手指重新握持/转动。拳头以这个时间常数（秒）跟上握柄的新方向，但只在握柄转速低于下面这个度/秒时跟；更快就是在转刀，拳头保持上一个稳定握姿不动。");
        text.AppendLine(Line(nameof(HandleFollowSeconds), HandleFollowSeconds));
        text.AppendLine(Line(nameof(HandleFollowRate), HandleFollowRate));
        text.AppendLine("# 为了贴合被手指重新握过的握柄，拳头最多偏离“刚性跟手腕”位置多少度。0 = 完全回到 0.11.2 的做法。");
        text.AppendLine(Line(nameof(ReGripDegrees), ReGripDegrees));
        text.AppendLine("# 这个修正角每秒最多变多少度（只会慢慢漂，不会甩）。");
        text.AppendLine(Line(nameof(ReGripDegreesPerSecond), ReGripDegreesPerSecond));
        text.AppendLine("# 手腕把手心转过 SquareFrom 度之后开始把余角补完，到 SquareFull 度时拳头正好在刀正后方（MCCS 定格就是这样）。只和角度有关，不会滞后也不会甩。1 开 0 关。");
        text.AppendLine(Line(nameof(SquareAtHold), SquareAtHold));
        text.AppendLine(Line(nameof(SquareFromDegrees), SquareFromDegrees));
        text.AppendLine(Line(nameof(SquareFullDegrees), SquareFullDegrees));
        text.AppendLine(Line(nameof(SquareGateByStillness), SquareGateByStillness));
        text.AppendLine("# 手臂滚转的最大角速度（度/秒），只用来压掉穿过退化方向那一帧的跳变。");
        text.AppendLine(Line(nameof(RollSlewDegreesPerSecond), RollSlewDegreesPerSecond));
        text.AppendLine("# 手臂方向取骨骼的比例（0=固定倾角，1=跟骨骼），实验用。");
        text.AppendLine(Line(nameof(ArmLeanFromBone), ArmLeanFromBone));
        text.AppendLine();
        text.AppendLine("# 待机时左拳中心停在屏幕哪里（爪子刀在代码里有自己的）。");
        text.AppendLine(Line(nameof(LeftHandTargetScreenX), LeftHandTargetScreenX));
        text.AppendLine(Line(nameof(LeftHandTargetScreenY), LeftHandTargetScreenY));
        text.AppendLine(Line(nameof(LeftHandDepth), LeftHandDepth));
        text.AppendLine();
        text.AppendLine("# 保留多少 SC 原版的切换下沉（0~1）。CS:GO 的 deploy 动画已经在抬刀了，");
        text.AppendLine("# 两个叠加会把整套构图压出画面外，所以默认关掉。");
        text.AppendLine(Line(nameof(SwapDipScale), SwapDipScale));
        text.AppendLine();
        text.AppendLine("# 检视时整体位移的阻尼：1 = CS:GO 原始幅度，越小抬得越低。");
        text.AppendLine(Line(nameof(InspectTravelScale), InspectTravelScale));
        return text.ToString();
    }

    static string Line(string key, float value) => $"{key} = {value.ToString("0.####", CultureInfo.InvariantCulture)}";
}
