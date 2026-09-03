using System;
using System.Globalization;
using System.IO;
using System.Text;
using Engine;
using Engine.Graphics;

namespace Game;

/// <summary>
/// The capture run. With KnifeTuning.QaCapture on, the inspect key plays
/// draw -> idle -> inspect -> idle on a virtual 30 fps clock and, for every frame,
/// saves a screenshot and a line of the arm's numbers -- grip, axis, side, rigid
/// and resolved roll, clearance, stillness, the hold angle, the wrist and held
/// part bones -- to app:/ScreenCapture/ScCsgoKnivesQA/[knife]_[time]/. Between
/// steps the game keeps drawing the same frame with the arm's state frozen; only
/// the captured render commits, so the picture and the numbers are one frame.
/// </summary>
public static class KnifeQa {
    public const int Fps = 30;
    const float IdleSeconds = 1f;

    enum Phase { Draw, IdleA, Inspect, IdleB }

    static bool s_active;
    static Phase s_phase;
    static double s_phaseStart;
    static int s_frame;
    static string s_run, s_dir;
    static StreamWriter s_csv;
    static ComponentFirstPersonModel s_model;
    static int s_variant, s_width, s_height;

    public static bool Active => s_active;
    public static bool Armed => KnifeTuning.QaCapture > 0.5f;

    public static bool Begin(ComponentFirstPersonModel model, int variant) {
        if (s_active) return false;
        try {
            string root = "app:/ScreenCapture";
            if (!Storage.DirectoryExists(root)) Storage.CreateDirectory(root);
            string qa = Storage.CombinePaths(root, "ScCsgoKnivesQA");
            if (!Storage.DirectoryExists(qa)) Storage.CreateDirectory(qa);
            s_run = $"{CsmcKnifeRig.GetAssetName(variant)}_{DateTime.Now:yyyyMMdd_HHmmss}";
            s_dir = Storage.CombinePaths(qa, s_run);
            if (!Storage.DirectoryExists(s_dir)) Storage.CreateDirectory(s_dir);
            s_csv = new StreamWriter(Storage.OpenFile(Storage.CombinePaths(s_dir, "frames.csv"), OpenFileMode.Create), new UTF8Encoding(false));
            s_csv.WriteLine("frame,t,clip,clipTime,gripX,gripY,gripZ,axisX,axisY,axisZ,sideX,sideY,sideZ,upX,upY,upZ,seatX,seatY,seatZ,width,reach,overshoot,rigidDeg,resolvedDeg,clearance,stillness,holdDeg,weaponHandX,weaponHandY,weaponHandZ,handRX,handRY,handRZ");
        }
        catch (Exception e) {
            KnifeLog.Error($"[ScCsgoKnives] QA: cannot open the capture folder: {e.Message}");
            Cleanup();
            return false;
        }
        // Half the window at the same aspect, so the projection -- and with it every
        // placement -- is the one in play, and a rebuild is never triggered mid-run.
        s_width = Math.Max(640, Display.Viewport.Width / 2) & ~1;
        s_height = Math.Max(360, Display.Viewport.Height / 2) & ~1;
        s_model = model;
        s_variant = variant;
        s_frame = 0;
        s_phase = Phase.Draw;
        s_phaseStart = 0;
        s_active = true;
        KnifeClock.Reset(1f / Fps);
        KnifeClock.Commit = false;
        KnifeAnimationController.QaDraw(model, variant);
        KnifeLog.Information($"[ScCsgoKnives] QA: capture run started -> {s_dir} ({s_width}x{s_height} @ {Fps} fps)");
        return true;
    }

    /// <summary>
    /// One virtual frame, from the block behaviour's Update: advance the clock,
    /// render and capture once with the arm's state committing, log the numbers,
    /// move the plan along.
    /// </summary>
    public static void Step() {
        if (!s_active) return;
        if (s_model is null || GameManager.Project is null) { End("the world went away"); return; }
        KnifeClock.Tick();
        s_frame++;
        KnifeClock.Commit = true;
        try {
            ScreenCaptureManager.Capture(s_width, s_height, $"ScCsgoKnivesQA/{s_run}/f{s_frame:0000}.jpg");
        }
        catch (Exception e) {
            KnifeLog.Error($"[ScCsgoKnives] QA: capture failed at frame {s_frame}: {e.Message}");
            End("capture failed");
            return;
        }
        finally {
            KnifeClock.Commit = false;
        }
        try {
            CsmcFirstPersonRenderer.QaSample s = CsmcFirstPersonRenderer.LastRight;
            string clip = KnifeAnimationController.QaClip(s_model);
            float clipTime = KnifeAnimationController.QaClipTime(s_model);
            s_csv.WriteLine(string.Join(",",
                s_frame.ToString(CultureInfo.InvariantCulture),
                F((float)KnifeClock.Now), clip, F(clipTime),
                V(s.Grip), V(s.Axis), V(s.Side), V(s.Up), V(s.Seat),
                F(s.Width), F(s.Reach), F(s.Overshoot),
                F(s.RigidDeg), F(s.ResolvedDeg), F(s.Clearance), F(s.Stillness), F(s.HoldDeg),
                V(new Vector3(s.WeaponHand.M41, s.WeaponHand.M42, s.WeaponHand.M43)),
                V(new Vector3(s.HandR.M41, s.HandR.M42, s.HandR.M43))));
        }
        catch (Exception e) {
            KnifeLog.Error($"[ScCsgoKnives] QA: log failed at frame {s_frame}: {e.Message}");
        }

        double now = KnifeClock.Now;
        switch (s_phase) {
            case Phase.Draw:
                if (KnifeAnimationController.QaIsIdle(s_model)) { s_phase = Phase.IdleA; s_phaseStart = now; }
                break;
            case Phase.IdleA:
                if (now - s_phaseStart >= IdleSeconds) { KnifeAnimationController.QaInspect(s_model, s_variant); s_phase = Phase.Inspect; }
                break;
            case Phase.Inspect:
                if (KnifeAnimationController.QaIsIdle(s_model)) { s_phase = Phase.IdleB; s_phaseStart = now; }
                break;
            case Phase.IdleB:
                if (now - s_phaseStart >= IdleSeconds) End("done");
                break;
        }
        if (s_active && s_frame > Fps * 40) End("ran too long");
    }

    static void End(string why) {
        KnifeLog.Information($"[ScCsgoKnives] QA: capture run ended ({why}) after {s_frame} frames -> {s_dir}");
        Cleanup();
    }

    static void Cleanup() {
        try { s_csv?.Flush(); s_csv?.Dispose(); } catch { }
        s_csv = null;
        s_model = null;
        s_active = false;
        KnifeClock.Release();
    }

    static string F(float value) => value.ToString("0.#####", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => $"{F(v.X)},{F(v.Y)},{F(v.Z)}";
}
