using System.IO;
using System.Reflection;
using SixLabors.ImageSharp.PixelFormats;
using Engine.Graphics;
using Image = Engine.Media.Image;

namespace Game;

/// <summary>The decoded CPU copy of the CS textures (2026-10-01 memory round). The engine's Texture2DReader decodes a
/// texture to an Image, uploads it and keeps that Image twice by reference: in ContentManager.Caches under the file name and
/// as the texture's Tag; it is never read again for these textures. Neither device-reset path in the inspected engines
/// (desktop 1.9.3.1, Android 1.9.2.2) uses it: Texture2D.HandleDeviceReset only allocates an empty texture, and nothing
/// calls GLWrapper.HandleContextLost. Once a CS texture is on the GPU its Image is released; the package member stays
/// re-readable, and on a device reset every CS texture (released or not) is uploaded again from it, so a texture is never
/// left empty after one. Only Textures/ScCsgoKnives and Textures/ScCsgoTactical (weapons, effects, HUD, agents' items):
/// player-appearance textures that another mod may sample are left as they are.</summary>
public static class ScTextureResidency {
    static readonly string[] Roots = ["Textures/ScCsgoKnives/", "Textures/ScCsgoTactical/"];
    public static int ReleasedImages, Restored, RestoreFailures, Unpinned, PoolTrims;
    public static long ReleasedBytes;
    static long s_releasedSinceTrim;
    static double s_lastTrim;
    public static bool Enabled { get; private set; }
    static readonly Dictionary<string, string[]> s_stems = new(StringComparer.Ordinal);   // stem -> its file names
    static IDictionary<string, List<object>> s_caches;
    static object s_sync;

    public static bool Owned(string path) => Roots.Any(r => path.StartsWith(r, StringComparison.Ordinal));

    /// <summary>The CS texture members of the loaded packages (paths with suffix).</summary>
    public static void Install(IEnumerable<string> textures) {
        var type = typeof(ContentManager);
        s_caches = type.GetField("Caches", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as IDictionary<string, List<object>>;
        s_sync = type.GetField("syncObj", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) ?? new object();
        if (s_caches == null) { KnifeLog.Warning("[CS_MEM] textures: ContentManager.Caches not found, decoded copies kept"); return; }
        foreach (var group in textures.Where(Owned).GroupBy(p => p.Substring(0, p.LastIndexOf('.'))))
            s_stems[group.Key] = group.ToArray();
        Enabled = true;
        Display.DeviceReset += Restore;
    }

    /// <summary>Once a second (ScLazyContent.Pump): releases the Image of every CS texture that is already uploaded.</summary>
    public static void Pump() {
        if (!Enabled) return;
        foreach (var (stem, files) in s_stems) {
            if (!s_caches.TryGetValue(stem, out var list)) continue;
            Texture2D texture = null;
            lock (s_sync) foreach (var o in list) if (o is Texture2D t && t.GetType() == typeof(Texture2D)) { texture = t; break; }
            if (texture == null || texture.m_isDisposed || texture.Tag is not Image image || texture.MipLevelsCount != 1) continue;
            lock (s_sync) {
                list.RemoveAll(o => ReferenceEquals(o, image));
                foreach (var file in files) if (s_caches.TryGetValue(file, out var aliases)) aliases.RemoveAll(o => ReferenceEquals(o, image));
            }
            texture.Tag = null;
            long bytes = (long)image.Width * image.Height * 4;
            ReleasedBytes += bytes; ReleasedImages++; s_releasedSinceTrim += bytes;
            Unpin(image);
            image.Dispose();
        }
        TrimPool();
    }

    /// <summary>The engine's Texture2D.Load(Image) / SetData(Image&lt;Rgba32&gt;) pins the pixel memory (Memory.Pin()) and drops
    /// the handle, so ImageSharp never frees that buffer, disposed or not: an unmanaged buffer keeps the pin as a reference,
    /// a pooled managed array keeps a pinned GCHandle (ImageSharp 3.1.12, decompiled 2026-10-01; seen on the phone as native
    /// heap growing while decoded copies were "released"). One upload is one pin; Unpin balances it before the Image goes.</summary>
    static void Unpin(Image image) {
        try {
            // The image's Memory comes from a view whose own Unpin is unsupported; the pin goes to the buffer underneath and
            // is recorded in the MemoryHandle. Pin once more and dispose that handle twice (a struct copy holds the same
            // buffer): the first Unpin undoes this pin, the second the engine's. Buffers whose pin takes no reference (pool
            // segments) do nothing either time.
            if (!image.m_trueImage.DangerousTryGetSinglePixelMemory(out Memory<Rgba32> memory)) return;
            var handle = memory.Pin(); var engines = handle;
            handle.Dispose(); engines.Dispose(); Unpinned++;
        }
        catch (Exception e) when (e is not OutOfMemoryException) { KnifeDiagnostics.WarnOnce("texture-unpin", "[CS_MEM] pixel buffer not unpinned: " + e.Message); }
    }

    /// <summary>Freed pixel buffers go back to ImageSharp's pool, which keeps them for reuse and trims only slowly. After at
    /// least 8 MiB of CS textures were released, and at most every 10 s, the pool's unused blocks are returned (the next
    /// decode allocates afresh; nothing in use is touched).</summary>
    static void TrimPool() {
        double now = Engine.Time.RealTime;
        if (s_releasedSinceTrim < 8L << 20 || now - s_lastTrim < 10) return;
        s_releasedSinceTrim = 0; s_lastTrim = now;
        try { SixLabors.ImageSharp.Configuration.Default.MemoryAllocator.ReleaseRetainedResources(); PoolTrims++; }
        catch (Exception e) when (e is not OutOfMemoryException) { KnifeDiagnostics.WarnOnce("texture-pool-trim", "[CS_MEM] image pool not trimmed: " + e.Message); }
    }

    /// <summary>After a device reset: the engine has re-created every texture empty; the CS ones get their pixels back.</summary>
    static void Restore() {
        int restored = 0, failed = 0;
        foreach (var (stem, files) in s_stems) {
            if (!s_caches.TryGetValue(stem, out var list)) continue;
            Texture2D texture = null;
            lock (s_sync) foreach (var o in list) if (o is Texture2D t && t.GetType() == typeof(Texture2D)) { texture = t; break; }
            if (texture == null || texture.m_isDisposed || texture.MipLevelsCount != 1) continue;
            try {
                // Every upload through the engine pins the pixels once and keeps the pin: balanced right after (see Unpin).
                if (texture.Tag is Image kept) { texture.SetData(0, kept.m_trueImage); Unpin(kept); }
                else {
                    // The shared ContentInfo stream: never disposed here (that would close it for every later reader).
                    var stream = files.Select(ContentManager.GetStream).FirstOrDefault(s => s != null) ?? throw new FileNotFoundException(stem);
                    var image = Image.Load(stream);
                    try { texture.SetData(0, image.m_trueImage); Unpin(image); } finally { image.Dispose(); }
                }
                restored++;
            }
            catch (Exception e) when (e is not OutOfMemoryException) {
                failed++; KnifeDiagnostics.WarnOnce("texture-restore-" + stem, $"[CS_MEM] {stem}: not restored after the device reset: {e.Message}");
            }
        }
        Restored += restored; RestoreFailures += failed;
        KnifeLog.Information($"[CS_MEM] device reset: {restored} CS textures uploaded again, {failed} failed");
    }

    /// <summary>Diagnostics: CS textures in the engine cache, their GPU bytes (engine formula) and decoded copies still held.</summary>
    public static (int Textures, long GpuBytes, int Decoded, long DecodedBytes) Measure() {
        int textures = 0, decoded = 0; long gpu = 0, cpu = 0;
        if (s_caches == null) return default;
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var (stem, files) in s_stems)
            foreach (var key in files.Prepend(stem)) {
                if (!s_caches.TryGetValue(key, out var list)) continue;
                lock (s_sync) foreach (var o in list) {
                    if (o == null || !seen.Add(o)) continue;
                    if (o is Texture2D t) { textures++; gpu += t.GetGpuMemoryUsage(); if (t.Tag is Image ti && seen.Add(ti)) { decoded++; cpu += (long)ti.Width * ti.Height * 4; } }
                    else if (o is Image i) { decoded++; cpu += (long)i.Width * i.Height * 4; }
                }
            }
        return (textures, gpu, decoded, cpu);
    }
}
