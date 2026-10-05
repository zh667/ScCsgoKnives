using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Engine;

namespace Game;

/// <summary>Resource residency of the CS packages after loading (2026-10-01 memory round).
/// The 1.9.3.1 engine keeps, for every enabled mod, the whole package file in a MemoryStream
/// (ModsManager.GetDecipherStream, held as ModEntity.ModArchive.ZipFileStream) and every Assets member extracted into its own
/// MemoryStream (ModEntity.CombineContent, held as ContentInfo.ContentStream) for the life of the process; for the full
/// package that is about 0.5 GiB plus 0.6 GiB that is rarely read again. Both run before any mod code, so the loading peak
/// stays; once loading has finished, the CS packages' own copies are replaced by re-readable sources over the package file
/// on disk (the same bytes: the package is a plain zip and is checked against the engine's copy first). A member is read
/// again only when a reader asks for it, kept while it is being used, and released within a byte budget or when idle.
/// Other mods' resources and the engine's own are never touched; nothing is decoded differently.</summary>
public static class ScLazyContent {
    /// <summary>The CS packages; the test automation and other mods are excluded.</summary>
    public static readonly string[] Packages = ["zh667.ScCsgoKnives", "zh667.ScCsgoTactical", "zh667.ScCsgoResources", "zh667.ScCsgoAppearance", "zh667.ScCsgoVoice"];
    /// <summary>Members smaller than this stay in memory (their re-read would cost more than they hold).</summary>
    public const int MinimumBytes = 4096;
    // Materialized members: above High the least recently used ones that are not being read are released down to Low;
    // any member idle for IdleSeconds is released. Budget from the 2026-10-01 baseline (largest single member 22 MB, a
    // gun switch reads 1-3 textures of up to 7 MB).
    public const long High = 64L << 20, Low = 32L << 20;
    public const double IdleSeconds = 20, InUseSeconds = 2;

    /// <summary>off: the engine's copies are kept (A/B diagnostics); textures: only texture members; full (default).
    /// Read once from the SCCS_RESIDENCY environment variable; not a player setting.</summary>
    public static string Mode { get; private set; } = "full";
    public static bool Installed { get; private set; }
    public static int StreamCount, Pinned, Failures;
    public static long ReleasedStreamBytes, ReleasedArchiveBytes, Loads, Releases, LoadedBytes, PinnedBytes;
    public static long MaterializedBytes { get { lock (s_gate) return s_materialized; } }
    public static long PeakMaterializedBytes { get { lock (s_gate) return s_peak; } }
    public static double LoadMilliseconds;

    static readonly object s_gate = new();
    static readonly LinkedList<ScLazyContentStream> s_recent = new();
    static long s_materialized, s_peak;
    static double s_nextPump;
    internal static readonly List<ScPackageSource> Sources = [];

    /// <summary>Called on the first main-menu entry, after every loading action of every mod has run. Never throws into the
    /// engine's hook: whatever could not be replaced stays as the engine loaded it.</summary>
    public static void Install() {
        if (Installed) return;
        Installed = true;
        try { InstallCore(); }
        catch (Exception e) when (e is not OutOfMemoryException) {
            Failures++; KnifeLog.Warning($"[CS_MEM] residency: install stopped, the rest stays as loaded: {e.GetType().Name}: {e.Message}");
        }
    }

    static void InstallCore() {
        Mode = (Environment.GetEnvironmentVariable("SCCS_RESIDENCY") ?? "full").Trim().ToLowerInvariant() switch { "off" => "off", "textures" => "textures", _ => "full" };
        ScMemoryReport.Hook();
        Window.Frame += Pump;
        if (Mode == "off") { KnifeLog.Information("[CS_MEM] residency: off (SCCS_RESIDENCY), the engine's copies are kept"); ScMemoryReport.Log("install"); return; }
        var resources = typeof(ContentManager).GetField("Resources", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as System.Collections.IDictionary;
        if (resources == null) { KnifeLog.Warning("[CS_MEM] residency: ContentManager.Resources not found, nothing replaced"); return; }
        ScMemoryReport.Log("loaded");
        long tick = System.Diagnostics.Stopwatch.GetTimestamp();
        // The engine ran CombineContent in ModList order and a later mod's member replaced an earlier one at the same path:
        // a member is only replaced from the package whose copy the engine actually kept.
        var owners = new Dictionary<string, ModEntity>(StringComparer.Ordinal);
        foreach (var entity in ModsManager.ModList.ToArray())
            if (entity?.ModFiles != null) foreach (var name in entity.ModFiles.Keys) if (name.StartsWith("Assets/", StringComparison.Ordinal)) owners[name] = entity;
        foreach (var entity in ModsManager.ModList.ToArray()) {
            if (entity?.modInfo == null || entity.IsDisabled || !Packages.Contains(entity.modInfo.PackageName) || entity.ModArchive?.ZipFileStream == null) continue;
            try { InstallPackage(entity, resources, owners); }
            catch (Exception e) when (e is not OutOfMemoryException) {
                Failures++; KnifeLog.Warning($"[CS_MEM] residency: {entity.modInfo.PackageName} kept as loaded: {e.GetType().Name}: {e.Message}");
            }
        }
        if (Mode == "full") ScTextureResidency.Install(ModsManager.ModList.Where(m => m?.modInfo != null && !m.IsDisabled && Packages.Contains(m.modInfo.PackageName))
            .SelectMany(m => m.ModFiles.Keys).Where(n => n.StartsWith("Assets/", StringComparison.Ordinal) && ScResourceKinds.IsTexture(n)).Select(n => n.Substring(7)).Distinct());
        Compact();
        KnifeLog.Information($"[CS_MEM] residency: mode={Mode} packages={Sources.Count} streams={StreamCount} released streams {ReleasedStreamBytes >> 20} MiB, package copies {ReleasedArchiveBytes >> 20} MiB, "
            + $"in {System.Diagnostics.Stopwatch.GetElapsedTime(tick).TotalMilliseconds:0} ms");
        ScMemoryReport.Log("install");
    }

    static void InstallPackage(ModEntity entity, System.Collections.IDictionary resources, Dictionary<string, ModEntity> owners) {
        var held = entity.ModArchive.ZipFileStream;
        var source = ScPackageSource.Open(entity.ModFilePath, held);
        int replaced = 0; long streamBytes = 0;
        foreach (var (name, entry) in entity.ModFiles) {
            if (!name.StartsWith("Assets/", StringComparison.Ordinal) || entry.FileSize < MinimumBytes || !ReferenceEquals(owners.GetValueOrDefault(name), entity)) continue;
            if (entry.Method is not (ZipArchive.Compression.Store or ZipArchive.Compression.Deflate)) continue;
            string path = name.Substring(7);
            if (Mode == "textures" && !ScResourceKinds.IsTexture(path)) continue;
            // Only the engine's own extraction of this very member (another mod may have replaced the path).
            if (resources[path] is not ContentInfo info || info.ContentStream is not MemoryStream old || old.GetType() != typeof(MemoryStream)) continue;
            long capacity = HeldBytes(old);
            if (old.CanRead && old.Length != entry.FileSize) continue;
            info.ContentStream = new ScLazyContentStream(source, entry, path);   // Position 0, like SetContentStream
            replaced++; streamBytes += capacity;
        }
        // The engine's archive object keeps working (GetFile, ExtractFile) over the file instead of its in-memory copy.
        long archive = HeldBytes(held as MemoryStream);
        entity.ModArchive.ZipFileStream = source.OpenSharedFileStream();
        lock (Sources) Sources.Add(source);
        StreamCount += replaced; ReleasedStreamBytes += streamBytes; ReleasedArchiveBytes += archive;
        KnifeLog.Diagnostic($"[CS_MEM] residency: {entity.modInfo.PackageName} {replaced} members ({streamBytes >> 20} MiB) and the package copy ({archive >> 20} MiB) now read from {Storage.GetFileName(entity.ModFilePath)}");
    }

    /// <summary>The released buffers were allocated during loading between objects that stay, so the GC keeps their space
    /// committed as large-object-heap fragmentation (2026-10-01 measurement: live bytes −1064 MiB, heap −606 MiB). One
    /// compacting collection, here on the main menu once per session and only after at least 64 MiB were released, gives it
    /// back; gameplay never forces a collection.</summary>
    static void Compact() {
        long released = ReleasedStreamBytes + ReleasedArchiveBytes;
        if (released < 64L << 20) return;
        long tick = System.Diagnostics.Stopwatch.GetTimestamp();
        var before = GC.GetGCMemoryInfo();
        // CoreCLR (desktop) keeps freed large-object space committed unless asked to compact it once. The Android runtime
        // (Mono) has no such setting and throws PlatformNotSupportedException (2026-10-01, Redmi Note 9 Pro, 1.9.3.1); its
        // GC returns large objects individually, so the collection alone is enough there.
        try { System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce; }
        catch (PlatformNotSupportedException) { }
        try { GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true); }
        catch (Exception e) when (e is PlatformNotSupportedException or NotSupportedException) { GC.Collect(); }
        var after = GC.GetGCMemoryInfo();
        CompactMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(tick).TotalMilliseconds;
        KnifeLog.Diagnostic($"[CS_MEM] residency: one compacting collection after releasing {released >> 20} MiB: heap {before.HeapSizeBytes >> 20} -> {after.HeapSizeBytes >> 20} MiB, "
            + $"committed {before.TotalCommittedBytes >> 20} -> {after.TotalCommittedBytes >> 20} MiB in {CompactMilliseconds:0} ms");
    }
    public static double CompactMilliseconds;

    static long HeldBytes(MemoryStream stream) {
        if (stream == null) return 0;
        try { return stream.CanRead ? stream.Capacity : stream.GetBuffer().Length; } catch { return 0; }
    }

    // Lock order: a stream's own lock, then s_gate (never the other way round).
    internal static void Materialized(ScLazyContentStream stream, long bytes, double milliseconds) {
        lock (s_gate) {
            s_materialized += bytes; if (s_materialized > s_peak) s_peak = s_materialized; stream.Counted = bytes;
            stream.Node ??= new LinkedListNode<ScLazyContentStream>(stream);
            if (stream.Node.List == null) s_recent.AddLast(stream.Node);
            Loads++; LoadedBytes += bytes; LoadMilliseconds += milliseconds;
        }
    }
    internal static void Touched(ScLazyContentStream stream) {
        lock (s_gate) if (stream.Node?.List != null) { s_recent.Remove(stream.Node); s_recent.AddLast(stream.Node); }
    }
    internal static void Dropped(ScLazyContentStream stream, long bytes, bool pinned) {
        lock (s_gate) {
            if (stream.Node?.List != null) s_recent.Remove(stream.Node);
            if (pinned) { Pinned++; PinnedBytes += bytes; } else Releases++;
            s_materialized -= bytes; stream.Counted = 0;
        }
    }

    /// <summary>Once a second on the main thread: releases idle members and keeps the materialized total within the budget.
    /// A release only drops this stream's buffer; the next read loads the member again.</summary>
    public static void Pump() {
        double now = Time.RealTime;
        if (now < s_nextPump) return;
        s_nextPump = now + 1;
        try {
            Trim(now);
            ScTextureResidency.Pump();
            ScMemoryReport.Tick();
        } catch (Exception e) when (e is not OutOfMemoryException) { KnifeDiagnostics.WarnOnce("residency-pump", "[CS_MEM] residency pump: " + e); }
    }

    public static void Trim(double now) {
        List<ScLazyContentStream> idle = [];
        lock (s_gate) {
            long total = s_materialized;
            for (var node = s_recent.First; node != null; node = node.Next) {
                var s = node.Value; double age = now - s.LastUse;
                if (age >= IdleSeconds || total > Low && s_materialized > High && age >= InUseSeconds) { idle.Add(s); total -= s.Counted; }
            }
        }
        foreach (var s in idle) s.Release(now);
    }
}

/// <summary>Kinds by path, as the engine's readers choose them by suffix.</summary>
public static class ScResourceKinds {
    public static bool IsTexture(string path) => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One CS package file, read again on demand. Its own handle (not the engine's), opened for reading with sharing
/// so the file can still be replaced or deleted outside the game; a member that changed underneath fails its CRC check
/// and is reported, never returned.</summary>
public sealed class ScPackageSource {
    public readonly string Path, SystemPath;
    readonly Stream m_file;
    readonly object m_gate = new();
    ScPackageSource(string path, string systemPath, Stream file) { Path = path; SystemPath = systemPath; m_file = file; }

    public static ScPackageSource Open(string path, Stream engineCopy) => OpenSystem(path, Storage.GetSystemPath(path), engineCopy);

    public static ScPackageSource OpenSystem(string path, string system, Stream engineCopy) {
        var file = OpenHandle(path, system);
        try {
            if (engineCopy == null || !engineCopy.CanSeek || file.Length != engineCopy.Length) throw new InvalidDataException("package file differs from the loaded package (size)");
            // The engine's copy is the file's bytes unless the package was enciphered (then every block differs): compare
            // the first and last 64 KiB (local header of the first member, central directory) and 16 blocks in between.
            long length = file.Length; byte[] a = new byte[65536], b = new byte[65536];
            var offsets = new List<long> { 0, Math.Max(0, length - a.Length) };
            for (int i = 1; i <= 16; i++) offsets.Add(length * i / 17);
            long keep = engineCopy.Position;
            try {
                foreach (long offset in offsets) {
                    int n = (int)Math.Min(a.Length, length - offset);
                    file.Position = offset; file.ReadExactly(a, 0, n);
                    engineCopy.Position = offset; engineCopy.ReadExactly(b, 0, n);
                    if (!a.AsSpan(0, n).SequenceEqual(b.AsSpan(0, n))) throw new InvalidDataException("package file differs from the loaded package (content at " + offset + ")");
                }
            } finally { engineCopy.Position = keep; }
            return new ScPackageSource(path, system, file);
        } catch { file.Dispose(); throw; }
    }

    /// <summary>A read handle that lets the file be replaced or deleted outside the game; where the platform does not allow
    /// opening the path directly (Android storage rules), the engine's own Storage.OpenFile, as it opened the package.</summary>
    static Stream OpenHandle(string path, string systemPath) {
        Stream stream;
        try { stream = new FileStream(systemPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.RandomAccess); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { stream = Storage.OpenFile(path, OpenFileMode.Read); }
        if (!stream.CanSeek) { stream.Dispose(); throw new NotSupportedException("package stream cannot seek"); }
        return stream;
    }

    /// <summary>Closes this source's handle (checks only; the game keeps its sources for the whole session).</summary>
    public void Dispose() { lock (m_gate) m_file.Dispose(); }

    /// <summary>A separate handle for the engine's archive object (its reads seek, so it must not share a position).</summary>
    public Stream OpenSharedFileStream() => OpenHandle(Path, SystemPath);

    /// <summary>The member's bytes, exactly as ZipArchive.ExtractFile writes them, verified against the zip CRC-32.</summary>
    public byte[] Read(ZipArchiveEntry entry) {
        byte[] data = new byte[entry.FileSize];
        lock (m_gate) {
            Span<byte> header = stackalloc byte[4];
            m_file.Position = entry.HeaderOffset; m_file.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x04034b50) throw new InvalidDataException("local header");
            m_file.Position = entry.FileOffset;
            if (entry.Method == ZipArchive.Compression.Store) m_file.ReadExactly(data);
            else {
                using var inflate = new DeflateStream(m_file, CompressionMode.Decompress, leaveOpen: true);
                inflate.ReadExactly(data);
            }
        }
        if (ScCrc32.Compute(data) != entry.Crc32) throw new InvalidDataException("CRC-32 (the package file was changed while the game is running?)");
        return data;
    }
}

/// <summary>A ContentInfo stream whose bytes are read from the package on demand. The engine requires a MemoryStream that
/// reports CanRead and CanWrite (ContentInfo.Duplicate hands out this same instance at position 0), so this derives from
/// MemoryStream and overrides every member that reads or exposes data; the base MemoryStream stays empty and unused.
/// Readers share one position, exactly as with the engine's stream. GetBuffer/TryGetBuffer hand out the backing array:
/// such a member is pinned and never released. A write (no reader does that) turns it into an ordinary owned copy.</summary>
public sealed class ScLazyContentStream : MemoryStream {
    readonly ScPackageSource m_source;
    readonly ZipArchiveEntry m_entry;
    public readonly string ResourcePath;
    readonly object m_gate = new();
    byte[] m_data;
    MemoryStream m_owned;
    long m_position;
    bool m_pinned, m_closed;
    internal LinkedListNode<ScLazyContentStream> Node;
    internal double LastUse;
    internal long Counted;   // bytes in ScLazyContent's total, guarded by its lock

    public ScLazyContentStream(ScPackageSource source, ZipArchiveEntry entry, string path) { m_source = source; m_entry = entry; ResourcePath = path; }

    /// <summary>Bytes this stream holds right now (diagnostics: the measurement reads this property by name).</summary>
    public long MaterializedBytes { get { lock (m_gate) return m_owned?.Capacity ?? m_data?.Length ?? 0; } }

    byte[] Data() {
        // Caller holds m_gate.
        LastUse = Time.RealTime;
        if (m_data != null) { ScLazyContent.Touched(this); return m_data; }
        long tick = System.Diagnostics.Stopwatch.GetTimestamp();
        try { m_data = m_source.Read(m_entry); }
        catch (Exception e) when (e is not OutOfMemoryException) {
            Interlocked.Increment(ref ScLazyContent.Failures);
            KnifeDiagnostics.WarnOnce("lazy-read-" + ResourcePath, $"[CS_MEM] {ResourcePath}: package member could not be read again ({e.Message}); restart the game after replacing the package.");
            throw new IOException("CS资源读取失败：" + ResourcePath + "（模组文件在游戏运行中被替换或损坏，请重启游戏）", e);
        }
        ScLazyContent.Materialized(this, m_data.Length, System.Diagnostics.Stopwatch.GetElapsedTime(tick).TotalMilliseconds);
        return m_data;
    }
    public void Release(double now) {
        lock (m_gate) {
            if (m_data == null || m_pinned || m_owned != null || now - LastUse < ScLazyContent.InUseSeconds) return;
            long bytes = m_data.Length; m_data = null;
            ScLazyContent.Dropped(this, bytes, false);
        }
    }
    MemoryStream Owned() {
        // Caller holds m_gate.
        if (m_owned == null) {
            var data = Data();
            m_owned = new MemoryStream(); m_owned.Write(data, 0, data.Length); m_owned.Position = m_position;
            ScLazyContent.Dropped(this, data.Length, true); m_data = null;
        }
        return m_owned;
    }
    void Open() { if (m_closed) throw new ObjectDisposedException(null, "Cannot access a closed Stream."); }

    public override bool CanRead => !m_closed;
    public override bool CanSeek => !m_closed;
    public override bool CanWrite => !m_closed;
    public override long Length { get { lock (m_gate) { Open(); return m_owned?.Length ?? m_entry.FileSize; } } }
    public override long Position {
        get { lock (m_gate) { Open(); return m_owned?.Position ?? m_position; } }
        set { lock (m_gate) { Open(); ArgumentOutOfRangeException.ThrowIfNegative(value); if (m_owned != null) m_owned.Position = value; else m_position = value; } }
    }
    public override int Capacity {
        get { lock (m_gate) { Open(); return m_owned?.Capacity ?? (int)m_entry.FileSize; } }
        set { lock (m_gate) { Open(); Owned().Capacity = value; } }
    }
    public override long Seek(long offset, SeekOrigin origin) {
        lock (m_gate) {
            Open();
            if (m_owned != null) return m_owned.Seek(offset, origin);
            long target = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => m_position + offset, SeekOrigin.End => m_entry.FileSize + offset, _ => throw new ArgumentException(null, nameof(origin)) };
            if (target < 0) throw new IOException("An attempt was made to move the position before the beginning of the stream.");
            return m_position = target;
        }
    }
    public override int Read(byte[] buffer, int offset, int count) { ValidateBufferArguments(buffer, offset, count); return Read(buffer.AsSpan(offset, count)); }
    public override int Read(Span<byte> buffer) {
        lock (m_gate) {
            Open();
            if (m_owned != null) return m_owned.Read(buffer);
            if (buffer.Length == 0 || m_position >= m_entry.FileSize) return 0;
            var data = Data();
            int n = (int)Math.Min(buffer.Length, data.Length - m_position);
            data.AsSpan((int)m_position, n).CopyTo(buffer); m_position += n;
            return n;
        }
    }
    public override int ReadByte() {
        lock (m_gate) {
            Open();
            if (m_owned != null) return m_owned.ReadByte();
            if (m_position >= m_entry.FileSize) return -1;
            return Data()[m_position++];
        }
    }
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
        token.IsCancellationRequested ? Task.FromCanceled<int>(token) : Task.FromResult(Read(buffer, offset, count));
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) =>
        token.IsCancellationRequested ? ValueTask.FromCanceled<int>(token) : new ValueTask<int>(Read(buffer.Span));
    public override void CopyTo(Stream destination, int bufferSize) {
        ArgumentNullException.ThrowIfNull(destination);
        lock (m_gate) {
            Open();
            if (m_owned != null) { m_owned.CopyTo(destination, bufferSize); return; }
            if (m_position >= m_entry.FileSize) return;
            var data = Data(); int start = (int)m_position;
            m_position = data.Length;
            destination.Write(data, start, data.Length - start);
        }
    }
    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken token) {
        if (token.IsCancellationRequested) return Task.FromCanceled(token);
        CopyTo(destination, bufferSize); return Task.CompletedTask;
    }
    public override byte[] GetBuffer() {
        lock (m_gate) {
            if (m_owned != null) return m_owned.GetBuffer();
            var data = Data();
            if (!m_pinned) { m_pinned = true; ScLazyContent.Dropped(this, data.Length, true); }
            return data;
        }
    }
    public override bool TryGetBuffer(out ArraySegment<byte> buffer) {
        lock (m_gate) {
            if (m_owned != null) return m_owned.TryGetBuffer(out buffer);
            buffer = new ArraySegment<byte>(GetBuffer(), 0, (int)m_entry.FileSize); return true;
        }
    }
    public override byte[] ToArray() { lock (m_gate) return m_owned != null ? m_owned.ToArray() : (byte[])Data().Clone(); }
    public override void WriteTo(Stream stream) {
        ArgumentNullException.ThrowIfNull(stream);
        lock (m_gate) { Open(); if (m_owned != null) m_owned.WriteTo(stream); else stream.Write(Data()); }
    }
    public override void Write(byte[] buffer, int offset, int count) { lock (m_gate) { Open(); Owned().Write(buffer, offset, count); } }
    public override void Write(ReadOnlySpan<byte> buffer) { lock (m_gate) { Open(); Owned().Write(buffer); } }
    public override void WriteByte(byte value) { lock (m_gate) { Open(); Owned().WriteByte(value); } }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token) { Write(buffer, offset, count); return Task.CompletedTask; }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default) { Write(buffer.Span); return ValueTask.CompletedTask; }
    public override void SetLength(long value) { lock (m_gate) { Open(); Owned().SetLength(value); } }
    public override void Flush() { }
    public override Task FlushAsync(CancellationToken token) => Task.CompletedTask;
    protected override void Dispose(bool disposing) {
        // As with the engine's MemoryStream, a reader that disposes the shared stream (the Ogg decoder does) closes it for
        // later readers too; unlike it, the bytes are dropped instead of being kept behind a closed stream.
        if (disposing) lock (m_gate) {
            if (!m_closed) {
                m_closed = true;
                if (m_data != null && !m_pinned) { long bytes = m_data.Length; m_data = null; ScLazyContent.Dropped(this, bytes, false); }
            }
        }
        base.Dispose(disposing);
    }
}

/// <summary>Zip CRC-32 (IEEE 802.3, reflected), slice-by-8.</summary>
static class ScCrc32 {
    static readonly uint[] Table = Build();
    static uint[] Build() {
        var t = new uint[8 * 256];
        for (uint i = 0; i < 256; i++) { uint c = i; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[i] = c; }
        for (int i = 0; i < 256; i++) for (int s = 1; s < 8; s++) t[s * 256 + i] = (t[(s - 1) * 256 + i] >> 8) ^ t[t[(s - 1) * 256 + i] & 0xFF];
        return t;
    }
    public static uint Compute(ReadOnlySpan<byte> data) {
        uint crc = 0xFFFFFFFFu; var t = Table; int i = 0;
        for (; i + 8 <= data.Length; i += 8) {
            uint a = crc ^ BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i)), b = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i + 4));
            crc = t[7 * 256 + (a & 0xFF)] ^ t[6 * 256 + ((a >> 8) & 0xFF)] ^ t[5 * 256 + ((a >> 16) & 0xFF)] ^ t[4 * 256 + (a >> 24)]
                ^ t[3 * 256 + (b & 0xFF)] ^ t[2 * 256 + ((b >> 8) & 0xFF)] ^ t[256 + ((b >> 16) & 0xFF)] ^ t[b >> 24];
        }
        for (; i < data.Length; i++) crc = t[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }
}
