using Engine;

namespace Game;

/// <summary>
/// The clock the knife animation and the arm's temporal state run on. Real time
/// in play; a virtual clock stepped one frame at a time by the capture run
/// (KnifeQa) and by the headless sweep (tools/ArmPreview), so every frame is
/// exactly 1/fps apart however long a capture takes, and an offline run
/// reproduces the game's numbers frame for frame.
/// </summary>
public static class KnifeClock {
    public static bool Virtual;
    public static double VirtualNow;
    public static float VirtualDt = 1f / 30f;

    /// <summary>
    /// Whether this render advances the arm's temporal state (carried sides,
    /// stillness, slews). Off during a capture run except for the captured
    /// render itself, so the picture saved and the numbers logged are one frame
    /// and the game's own redraws in between change nothing.
    /// </summary>
    public static bool Commit = true;

    public static double Now => Virtual ? VirtualNow : Time.RealTime;
    public static float Dt => Virtual ? VirtualDt : Time.FrameDuration;

    public static void Tick() => VirtualNow += VirtualDt;

    public static void Reset(float dt) {
        Virtual = true;
        VirtualNow = 0;
        VirtualDt = dt;
        Commit = true;
    }

    public static void Release() {
        Virtual = false;
        Commit = true;
    }
}
