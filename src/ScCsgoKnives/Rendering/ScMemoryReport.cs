using System.IO;
using Engine;
using Engine.Graphics;

namespace Game;

/// <summary>[CS_MEM] lines in Game.log (2026-10-01 memory round): what the CS packages hold, so a player's log (desktop or
/// phone) shows residency without tools. Taken at install, world load, world exit, background/resume, and every 60 s in a
/// world when a figure moved by 8 MiB or more. Figures: lazy = package members read again on demand (held now / peak,
/// loads, releases, pinned); engine = CS members still held by the engine's own MemoryStreams; tex = CS textures in the
/// engine cache with their GPU bytes (engine formula, driver overhead not included) and the decoded CPU copies still held;
/// released = decoded copies freed after upload; gc = managed heap / committed; proc = working set (Windows) or
/// VmRSS / RssAnon / RssFile (Linux, Android); gpu = all graphics resources of the game (engine formula).</summary>
public static class ScMemoryReport {
    static double s_next;
    static long s_lastHeap, s_lastGpu;
    static bool s_hooked;

    public static void Hook() {
        if (s_hooked) return;
        s_hooked = true;
        Window.Deactivated += () => Log("background");
        Window.Activated += () => Log("resume");
    }

    public static void Log(string stage) {
        if (!KnifeLog.Diagnostics) return;
        try { KnifeLog.Information("[CS_MEM] " + Line(stage)); }
        catch (Exception e) when (e is not OutOfMemoryException) { KnifeDiagnostics.WarnOnce("cs-mem-report", "[CS_MEM] report failed: " + e.Message); }
    }

    /// <summary>In a world: a line when the heap or the GPU total moved by 8 MiB since the last one, at most once a minute.</summary>
    public static void Tick() {
        double now = Time.RealTime;
        if (!KnifeLog.Diagnostics || now < s_next || GameManager.Project == null) return;
        s_next = now + 60;
        var gi = GC.GetGCMemoryInfo(); long gpu = Display.GetGpuMemoryUsage();
        if (Math.Abs(gi.HeapSizeBytes - s_lastHeap) >= 8 << 20 || Math.Abs(gpu - s_lastGpu) >= 8 << 20) Log("periodic");
    }

    static string MiB(long bytes) => (bytes / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

    public static string Line(string stage) {
        var (engineStreams, engineBytes) = EngineHeld();
        var tex = ScTextureResidency.Measure();
        var gi = GC.GetGCMemoryInfo(); long gpu = Display.GetGpuMemoryUsage();
        s_lastHeap = gi.HeapSizeBytes; s_lastGpu = gpu;
        return $"stage={stage} mode={ScLazyContent.Mode} lazy={ScLazyContent.StreamCount} held={MiB(ScLazyContent.MaterializedBytes)}/peak {MiB(ScLazyContent.PeakMaterializedBytes)} MiB "
            + $"loads={ScLazyContent.Loads} ({MiB(ScLazyContent.LoadedBytes)} MiB, {ScLazyContent.LoadMilliseconds:0} ms) releases={ScLazyContent.Releases} pinned={ScLazyContent.Pinned} ({MiB(ScLazyContent.PinnedBytes)} MiB) failures={ScLazyContent.Failures} "
            + $"freed streams={MiB(ScLazyContent.ReleasedStreamBytes)} package={MiB(ScLazyContent.ReleasedArchiveBytes)} MiB engine={engineStreams} ({MiB(engineBytes)} MiB) "
            + $"tex={tex.Textures} gpu {MiB(tex.GpuBytes)} MiB decoded={tex.Decoded} ({MiB(tex.DecodedBytes)} MiB) released={ScTextureResidency.ReleasedImages} ({MiB(ScTextureResidency.ReleasedBytes)} MiB, unpinned {ScTextureResidency.Unpinned}, pool trims {ScTextureResidency.PoolTrims}) restored={ScTextureResidency.Restored} "
            + $"prep={ScTexturePreparation.PendingCount} ({MiB(ScTexturePreparation.ReservedBytes)} MiB) "
            + $"gc={MiB(gi.HeapSizeBytes)}/{MiB(gi.TotalCommittedBytes)} MiB proc={Process()} gpuAll={MiB(gpu)} MiB";
    }

    /// <summary>CS package members the engine still holds in its own MemoryStreams (small members, mode off, other mods' paths).</summary>
    static (int Count, long Bytes) EngineHeld() {
        int count = 0; long bytes = 0;
        foreach (var info in ContentManager.List()) {
            if (info.ContentStream is not MemoryStream ms || ms is ScLazyContentStream || !OwnedPaths.Contains(info.AbsolutePath)) continue;
            count++;
            try { bytes += ms.CanRead ? ms.Capacity : ms.GetBuffer().Length; } catch { }
        }
        return (count, bytes);
    }

    static HashSet<string> s_owned;
    static HashSet<string> OwnedPaths {
        get {
            if (s_owned != null) return s_owned;
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entity in ModsManager.ModList)
                if (entity?.modInfo != null && ScLazyContent.Packages.Contains(entity.modInfo.PackageName))
                    foreach (var name in entity.ModFiles.Keys) if (name.StartsWith("Assets/", StringComparison.Ordinal)) set.Add(name.Substring(7));
            return s_owned = set;
        }
    }

    static string Process() {
        try {
            if (File.Exists("/proc/self/status")) {
                string rss = "?", anon = "?", file = "?";
                foreach (var line in File.ReadLines("/proc/self/status")) {
                    if (line.StartsWith("VmRSS:")) rss = Kb(line); else if (line.StartsWith("RssAnon:")) anon = Kb(line); else if (line.StartsWith("RssFile:")) file = Kb(line);
                }
                return $"rss {rss}/anon {anon}/file {file} MiB";
            }
            using var p = System.Diagnostics.Process.GetCurrentProcess();
            return $"ws {MiB(p.WorkingSet64)}/private {MiB(p.PrivateMemorySize64)}/peak ws {MiB(p.PeakWorkingSet64)} MiB";
        } catch (Exception e) { return "unavailable (" + e.GetType().Name + ")"; }
    }
    static string Kb(string line) => long.TryParse(new string(line.Where(char.IsDigit).ToArray()), out long kb) ? MiB(kb * 1024) : "?";
}
