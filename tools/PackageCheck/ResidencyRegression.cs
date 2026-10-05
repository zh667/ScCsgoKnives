using System.IO.Compression;
using System.Reflection;

/// <summary>Resource residency (2026-10-01 memory round): the re-readable package members that replace the engine's copies
/// after loading must return exactly the engine's bytes for this package (deflated and stored members, every kind), decode
/// to the same pixels, behave like the engine's MemoryStream where readers rely on it (ContentInfo.Duplicate, seeks,
/// buffers, dispose, writes), stay within the byte budget, and report a package changed on disk instead of returning
/// wrong bytes.</summary>
static class ResidencyRegression {
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod, string package) {
        var results = new List<Result>();
        void Check(string name, bool ok, string detail = "") => results.Add(new("residency/" + name, ok, detail));
        var sourceType = mod.GetType("Game.ScPackageSource", true);
        var streamType = mod.GetType("Game.ScLazyContentStream", true);
        var lazyType = mod.GetType("Game.ScLazyContent", true);
        var openSystem = sourceType.GetMethod("OpenSystem", BindingFlags.Public | BindingFlags.Static);
        var trim = lazyType.GetMethod("Trim", BindingFlags.Public | BindingFlags.Static);
        long Materialized() => (long)lazyType.GetProperty("MaterializedBytes").GetValue(null);
        long Low() => (long)lazyType.GetField("Low").GetValue(null);
        MemoryStream Lazy(object source, Game.ZipArchiveEntry entry, string path) => (MemoryStream)Activator.CreateInstance(streamType, source, entry, path);
        void Trim(double ahead) => trim.Invoke(null, [Engine.Time.RealTime + ahead]);
        var opened = new List<object>();
        object Open(string path, Stream copy) {
            try { var s = openSystem.Invoke(null, [path, path, copy]); opened.Add(s); return s; } catch (TargetInvocationException e) { throw e.InnerException; }
        }

        // 1. This package, as the engine holds it (GetDecipherStream's byte copy, Game.ZipArchive over it).
        var engineCopy = new MemoryStream(File.ReadAllBytes(package));
        var archive = Game.ZipArchive.Open(engineCopy, true);
        var entries = archive.ReadCentralDir().Where(e => e.FilenameInZip.StartsWith("Assets/") && e.FileSize >= 4096).ToList();
        var source = Open(package, engineCopy);
        var sample = entries.OrderByDescending(e => e.FileSize).Take(24)
            .Concat(entries.GroupBy(e => Path.GetExtension(e.FilenameInZip).ToLowerInvariant() + e.Method).SelectMany(g => g.Take(6)))
            .Concat(entries.Where((e, i) => i % Math.Max(1, entries.Count / 40) == 0)).Distinct().ToList();
        int same = 0; var differ = new List<string>(); long bytes = 0;
        foreach (var e in sample) {
            var original = new MemoryStream(); archive.ExtractFile(e, original);
            var lazy = Lazy(source, e, e.FilenameInZip[7..]);
            var read = new MemoryStream(); lazy.CopyTo(read);
            Trim(100); lazy.Position = e.FileSize / 3; var tail = new byte[e.FileSize - e.FileSize / 3]; lazy.ReadExactly(tail);
            bool ok = read.ToArray().AsSpan().SequenceEqual(original.ToArray()) && tail.AsSpan().SequenceEqual(original.ToArray().AsSpan((int)(e.FileSize / 3)))
                && lazy.Length == original.Length && lazy.CanRead && lazy.CanWrite && lazy.CanSeek;
            if (ok) same++; else differ.Add(e.FilenameInZip);
            bytes += e.FileSize;
        }
        var kinds = sample.Select(e => Path.GetExtension(e.FilenameInZip).ToLowerInvariant() + (e.Method == Game.ZipArchive.Compression.Store ? "/stored" : "/deflated")).Distinct().Order();
        Check("package-members-read-again-are-the-engines-bytes", differ.Count == 0 && same > 0,
            $"{same}/{sample.Count} members ({bytes >> 20} MiB) of {entries.Count}; kinds {string.Join(" ", kinds)}; differ {string.Join(", ", differ.Take(5))}");
        // Decoding through the engine's own Image.Load: the same pixels.
        var textures = entries.Where(e => e.FilenameInZip.StartsWith("Assets/Textures/") && (e.FilenameInZip.EndsWith(".png") || e.FilenameInZip.EndsWith(".webp")))
            .OrderByDescending(e => e.FileSize).Take(2).Concat(entries.Where(e => e.FilenameInZip.EndsWith(".png") || e.FilenameInZip.EndsWith(".webp")).Take(1)).Distinct().ToList();
        var pixelDiffs = new List<string>();
        foreach (var e in textures) {
            var original = new MemoryStream(); archive.ExtractFile(e, original); original.Position = 0;
            var a = Engine.Media.Image.Load(original); var b = Engine.Media.Image.Load(Lazy(source, e, e.FilenameInZip[7..]));
            bool equal = a.Width == b.Width && a.Height == b.Height;
            for (int y = 0; equal && y < a.Height; y++) for (int x = 0; x < a.Width; x++) if (a.GetPixel(x, y) != b.GetPixel(x, y)) { equal = false; break; }
            if (!equal) pixelDiffs.Add(e.FilenameInZip);
            a.Dispose(); b.Dispose();
        }
        Check("textures-decode-to-the-same-pixels", textures.Count > 0 && pixelDiffs.Count == 0, $"{textures.Count} textures ({string.Join(", ", textures.Select(t => Path.GetFileName(t.FilenameInZip)))}); differ {string.Join(", ", pixelDiffs)}");
        Trim(100);

        // Decoded texture copies: the engine's upload (Texture2D.Load(Image) → SetData(Image<Rgba32>)) pins the pixel memory and
        // never unpins it, so ImageSharp would keep a 1024² or 2048² buffer after Dispose; ScTextureResidency balances that pin
        // before disposing (ImageSharp's own count of unreleased allocations must come back).
        {
            var unpin = mod.GetType("Game.ScTextureResidency", true).GetMethod("Unpin", BindingFlags.NonPublic | BindingFlags.Static);
            var leaks = new List<string>();
            foreach (int size in new[] { 1024, 2048 }) {
                int Count() => SixLabors.ImageSharp.Diagnostics.MemoryDiagnostics.TotalUndisposedAllocationCount;
                void EnginePin(Engine.Media.Image img) { img.m_trueImage.DangerousTryGetSinglePixelMemory(out Memory<SixLabors.ImageSharp.PixelFormats.Rgba32> m); m.Pin(); }
                int c0 = Count(); var a = new Engine.Media.Image(size, size); EnginePin(a); a.Dispose(); int leaked = Count() - c0;
                int c1 = Count(); var b = new Engine.Media.Image(size, size); EnginePin(b); unpin.Invoke(null, [b]); b.Dispose(); int after = Count() - c1;
                leaks.Add($"{size}²: engine pin leaks {leaked}, released after unpin {after == 0}");
                if (after != 0) leaks.Add("FAIL");
            }
            Check("released-texture-pixels-are-freed-despite-the-engines-pin", !leaks.Contains("FAIL"), string.Join("; ", leaks));
        }

        // 2. Stream contract on a small synthetic package (members of several kinds, three of 24 MiB for the budget).
        string dir = Path.Combine(Path.GetTempPath(), "sccs-residency-" + Environment.ProcessId); Directory.CreateDirectory(dir);
        try {
            string zipPath = Path.Combine(dir, "contract.scmod"); var rnd = new System.Random(11);
            byte[] Rand(int n) { var b = new byte[n]; rnd.NextBytes(b); return b; }
            byte[] Text(int n) { var b = new byte[n]; for (int i = 0; i < n; i++) b[i] = (byte)("abcdefgh "[(i * 7 + i / 13) % 9]); return b; }
            var members = new (string Name, byte[] Data, CompressionLevel Level)[] {
                ("Assets/m.obj", Text(1_500_000), CompressionLevel.Optimal), ("Assets/x.ogg", Rand(300_000), CompressionLevel.NoCompression),
                ("Assets/b1.bin", Rand(24 << 20), CompressionLevel.Fastest), ("Assets/b2.bin", Rand(24 << 20), CompressionLevel.NoCompression), ("Assets/b3.bin", Text(24 << 20), CompressionLevel.Optimal) };
            using (var fs = File.Create(zipPath)) using (var za = new System.IO.Compression.ZipArchive(fs, ZipArchiveMode.Create))
                foreach (var m in members) { var e = za.CreateEntry(m.Name, m.Level); using var s = e.Open(); s.Write(m.Data); }
            var copy = new MemoryStream(File.ReadAllBytes(zipPath)); var za2 = Game.ZipArchive.Open(copy, true);
            var es = za2.ReadCentralDir().ToDictionary(e => e.FilenameInZip); var src = Open(zipPath, copy);
            var obj = es["Assets/m.obj"];
            var l1 = Lazy(src, obj, "m.obj"); l1.Position = 77;
            var info = new Game.ContentInfo("m.obj"); info.SetContentStream(l1);
            var dup = info.Duplicate();
            Check("contentinfo-accepts-and-duplicates-it", ReferenceEquals(dup, l1) && dup.Position == 0);
            l1.Seek(-10, SeekOrigin.End); var t10 = new byte[10]; l1.ReadExactly(t10); l1.Position = 5; l1.Seek(7, SeekOrigin.Current); int b12 = l1.ReadByte();
            Check("seek-end-current-position", t10.AsSpan().SequenceEqual(members[0].Data.AsSpan(members[0].Data.Length - 10)) && b12 == members[0].Data[12] && l1.Position == 13);
            var pin = Lazy(src, obj, "m.obj"); var buf = pin.GetBuffer(); Trim(100);
            Check("getbuffer-pins-the-member", pin.TryGetBuffer(out var seg) && seg.Array == buf && seg.Count == pin.Length && buf.AsSpan(0, (int)pin.Length).SequenceEqual(members[0].Data));
            var w = Lazy(src, obj, "m.obj"); w.Position = w.Length; w.Write([1, 2, 3]);
            Check("a-write-makes-an-owned-copy", w.Length == obj.FileSize + 3 && w.ToArray()[^1] == 3);
            var d = Lazy(src, obj, "m.obj"); d.ReadByte(); d.Dispose(); bool refused = false;
            try { new Game.ContentInfo("m.obj") { ContentStream = d }.Duplicate(); } catch (Exception) { refused = true; }
            Check("dispose-closes-it-like-a-memorystream", !d.CanRead && refused);
            var big = new[] { "b1", "b2", "b3" }.Select(n => Lazy(src, es[$"Assets/{n}.bin"], n)).ToArray();
            Trim(100); foreach (var l in big) l.ReadByte();
            long before = Materialized(); Trim(.5); long inUse = Materialized(); Trim(3); long after = Materialized();
            Check("budget-keeps-members-in-use-then-releases-to-the-low-mark", before >= 72L << 20 && inUse == before && after <= Low(), $"{before >> 20}/{inUse >> 20}/{after >> 20} MiB");
            Check("a-released-member-reads-again", big.All(l => { l.Position = 0; return l.ReadByte() >= 0; }));
            Trim(100);
            // Readers on worker threads while the main thread keeps trimming (the stream lock is always taken before the
            // budget lock): no deadlock within 20 s, every read returns the right bytes.
            var shared = new[] { "Assets/m.obj", "Assets/x.ogg" }.Select(n => (Data: members.First(m => m.Name == n).Data, Entry: es[n])).ToArray();
            int wrong = 0, reads = 0; var stop = DateTime.UtcNow.AddSeconds(1.5);
            var workers = Enumerable.Range(0, 6).Select(w => System.Threading.Tasks.Task.Run(() => {
                var r = new System.Random(w); var buf = new byte[4096]; var own = shared.Select(x => Lazy(src, x.Entry, "c")).ToArray();
                while (DateTime.UtcNow < stop) {
                    int k = r.Next(shared.Length); var (data, entry) = shared[k]; var l = own[k];
                    int at = r.Next(data.Length - buf.Length); l.Position = at; l.ReadExactly(buf);
                    if (!buf.AsSpan().SequenceEqual(data.AsSpan(at, buf.Length))) System.Threading.Interlocked.Increment(ref wrong);
                    System.Threading.Interlocked.Increment(ref reads);
                }
            })).ToArray();
            while (DateTime.UtcNow < stop) Trim(3);
            bool finished = System.Threading.Tasks.Task.WaitAll(workers, TimeSpan.FromSeconds(20));
            Check("concurrent-readers-and-trimming", finished && wrong == 0 && reads > 0, $"{reads} reads, {wrong} wrong, finished {finished}");
            Trim(100);
            // A package replaced while the game runs: opened identical, changed on disk afterwards.
            string changed = Path.Combine(dir, "changed.scmod"); File.Copy(zipPath, changed, true);
            var ogg = es["Assets/x.ogg"]; var srcChanged = Open(changed, copy);
            using (var f = new FileStream(changed, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) { f.Position = ogg.FileOffset + 1000; f.WriteByte(0x5A); f.WriteByte(0xA5); }
            bool caught = false; try { Lazy(srcChanged, ogg, "x.ogg").ReadByte(); } catch (IOException) { caught = true; }
            Check("a-member-changed-on-disk-is-reported-not-returned", caught);
            bool enciphered = false; try { Open(zipPath, new MemoryStream(copy.ToArray().Reverse().ToArray())); } catch (InvalidDataException) { enciphered = true; }
            Check("an-enciphered-engine-copy-is-refused", enciphered);
        } finally {
            foreach (var s in opened) sourceType.GetMethod("Dispose").Invoke(s, null);
            try { Directory.Delete(dir, true); } catch { }
        }
        return results;
    }
}
