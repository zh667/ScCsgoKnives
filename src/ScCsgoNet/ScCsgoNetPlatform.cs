using System.Reflection;
using System.Text.Json;
using Engine;
using Game.Network;
namespace Game;

// Round 3 (post-mp-bugs-20260930 item 4): the 1.9.3.2 multiplayer platform as a player installs it, plus this mod, must
// be playable. Two platform defects stand in the way and are corrected through the platform's own public surfaces only -
// no platform file is rewritten, no third-party package is touched, nothing is patched in memory:
//
//  1. CommandCompatNet's freeze (the CompatNet internal mod, where the platform ships it: Windows). Corrected by the
//     optional payload Net/ScCsgoNetCompat.bin (src/ScCsgoNetCompat), which the core loads only when CompatNet exists and
//     its contract matches; it reports through ScNet.CompatStatus / ScNet.CommandGateInstalled. A platform without
//     CompatNet (the Android build) has no such adapter and needs no gate.
//  2. Late-join terrain. The server sends a client the difference between each chunk it has generated and the seed's base
//     terrain (TerrainSyncChunkListPacket), once per chunk in range. The client applies that on its main thread while its
//     own generator may still be writing the same chunk, so cells edited before the client joined can end up regenerated
//     (mp17: 295 and 42 cells of a pre-built arena). The adapter observes every chunk list the server sends (the platform's
//     own OnNetworkTerrainSyncChunkList hook), verifies each chunk by hash with that client after it has settled, and
//     repairs a differing chunk's columns through the platform's own cell packet, a bounded number of times.
//
// The terrain correction is gated on the platform's contract (the types and members it relies on). The platform build
// (assembly MVIDs) is reported as verified or unverified, to the log and once to the player. An upstream build known to
// have fixed a defect lists its MVID in FixedIn and gets no correction for it.
public static class ScPlatformCompat {
    public const double CommandWindow = 2.5;
    public sealed record Build(string Survivalcraft, string CompatNet, string Multiplayer);
    /// <summary>Builds this adapter was tested against (assembly MVIDs of the 1.9.3.2_MP preview as built from its source).</summary>
    public static readonly Build[] Verified = [
        new("d5c1a418-7d4f-4c3b-83aa-383619b23978", "52747411-eca3-406c-9428-86320a2fe54d", "b3822a2d-94f5-4803-b4b9-2b05ac00accc"),
    ];
    /// <summary>Upstream builds where a defect is fixed: no correction is applied for it there.</summary>
    public static readonly Dictionary<string, string[]> FixedIn = new() { ["command-freeze"] = [], ["late-join-terrain"] = [] };

    public static Build Current { get; private set; }
    public static bool BuildVerified { get; private set; }
    /// <summary>The command-adapter gate lives in the optional CompatNet payload; its state is the core's.</summary>
    public static string CommandGateStatus => ScNet.CompatStatus;
    public static string TerrainStatus { get; private set; } = "not installed";
    public static bool CommandGateInstalled => ScNet.CommandGateInstalled;
    public static bool TerrainInstalled { get; private set; }
    /// <summary>The platform has the CompatNet internal mod (Windows builds; the Android build ships Multiplayer only).</summary>
    public static bool HasCompatNet => !string.IsNullOrEmpty(Current?.CompatNet);
    public static string Summary => $"platform {(BuildVerified ? "verified" : "unverified")} ({Short(Current?.Survivalcraft)}/{(HasCompatNet ? Short(Current.CompatNet) : "no CompatNet")}/{Short(Current?.Multiplayer)}); CompatNet corrections: {CommandGateStatus}; terrain: {TerrainStatus}";
    static string Short(string s) => s is null ? "?" : s.Length > 8 ? s[..8] : s;

    /// <summary>Once per process. Idempotent.</summary>
    public static void Install(ScCsgoNetAdapter adapter) {
        var compatNet = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Survivalcraft.CompatNet");
        Current = new(typeof(NetworkManager).Assembly.ManifestModule.ModuleVersionId.ToString(), compatNet?.ManifestModule.ModuleVersionId.ToString() ?? "",
            typeof(TerrainSyncChunkListPacket).Assembly.ManifestModule.ModuleVersionId.ToString());
        BuildVerified = Verified.Any(b => b == Current);
        TerrainStatus = FixedIn["late-join-terrain"].Contains(Current.Multiplayer) ? "upstream fixed: reconciliation off"
            : TerrainContract() is { } why ? "contract differs, reconciliation off: " + why : "reconciliation on (verify by hash after each chunk list, bounded resend)";
        TerrainInstalled = TerrainStatus.StartsWith("reconciliation on");
        Log.Information($"[ScCsgoNet] platform {(BuildVerified ? "verified" : "unverified")} ({Short(Current.Survivalcraft)}/{(HasCompatNet ? Short(Current.CompatNet) : "no CompatNet")}/{Short(Current.Multiplayer)}); terrain: {TerrainStatus}");
    }

    static string TerrainContract() {
        if (typeof(ModLoader).GetMethod("OnNetworkTerrainSyncChunkList", [typeof(TerrainUpdater), typeof(int), typeof(List<TerrainChunk>)]) is null) return "no OnNetworkTerrainSyncChunkList hook";
        if (typeof(TerrainSyncChunkListPacket).GetConstructor([typeof(List<TerrainChunk>)]) is null) return "TerrainSyncChunkListPacket shape changed";
        return null;
    }

    /// <summary>What the player is told once about this platform, or null when there is nothing to say: only a correction
    /// that could not be installed. That this platform build is not on the verified list while its contract matched and
    /// the corrections are in place is a diagnosis, not something a player can act on: it stays in the log (Install's
    /// "[ScCsgoNet] platform unverified (...)" line) and is no longer shown (user request 2026-10-02, the mpc3 screenshot:
    /// "CS 联机：此联机平台构建（...）未经验证；兼容修正已按契约启用。"). A refused network layer or a protocol mismatch
    /// has its own notices (ScNet.BlockedMessage, ScNet.HostTick) and is untouched.</summary>
    public static string PlayerNotice() =>
        HasCompatNet && !CommandGateInstalled && !CommandGateStatus.StartsWith("upstream fixed") ? "CS 联机：平台冻结修正未能启用（" + CommandGateStatus + "）。" : null;
}

/// <summary>Late-join terrain reconciliation over the platform's own chunk delivery (see the file comment).</summary>
public sealed class ScTerrainReconcile {
    /// <summary>A resent chunk is verified again after <see cref="SettleSeconds"/> × 2^resends (1.5, 3, 6, 12, 24, 48 s: about
    /// 95 s in all): a client still generating the chunk's neighbours re-paints their trees' overhang into a chunk it has
    /// just re-applied (the generator records each tree's brush and paints it into the neighbouring chunks), so an early
    /// re-apply is undone until the neighbourhood is generated; the last attempts land after it is. A chunk that verified
    /// equal is checked again <see cref="Rechecks"/> times (<see cref="RecheckSeconds"/>) for the same reason.</summary>
    public const double SettleSeconds = 1.5, ReportTimeout = 4.0, PendingRetry = 1.5;
    public const int MaxResends = 6, MaxPendings = 40, ChunksPerMessage = 16, ResendsPerSecond = 8, Rechecks = 2;
    public static readonly double[] RecheckSeconds = [12, 45];
    /// <summary>A chunk that differs is repaired exactly, without re-sending the platform's diff (<see cref="ColumnAfterResends"/>
    /// = 0): a diff re-apply regenerates the whole chunk in place on the client (the player standing on it fell through the
    /// regenerated ground in the r3j platform runs) and cannot converge near modified terrain anyway; the column repair only
    /// changes the cells that differ. The platform's
    /// sync data is a diff against a base it regenerates on each side, and the generator reads the live terrain (top cell,
    /// neighbours) while generating, so near a carved or built area the two bases differ and diffs can never agree. The
    /// client then hashes the chunk's 256 columns, the server sends the cells of the differing columns through the platform's
    /// own TerrainChangeCellListPacket (≤ <see cref="ColumnsPerRound"/> columns a round, ≤ <see cref="MaxColumnRounds"/> rounds),
    /// and verifies again.</summary>
    public const int ColumnAfterResends = 0, ColumnsPerRound = 32, MaxColumnRounds = 6;
    public sealed record Entry(int X, int Z, ulong Hash);
    public sealed record Verify(List<Entry> Chunks, List<int[]> Columns);
    public sealed record ColumnReport(int X, int Z, ulong[] Hashes);
    public sealed record Report(List<int[]> Same, List<int[]> Differs, List<int[]> Pending, List<ColumnReport> Columns);
    sealed class Watch { public Point2 Coords; public double DueAt; public int Resends, Pendings, Rechecked, ColumnRounds, SameAsBefore; public bool Asked, ColumnMode; public string LastDiffering = ""; }
    readonly Dictionary<int, Dictionary<Point2, Watch>> m_watch = [];
    readonly Dictionary<int, double> m_lastResendSecond = [];
    readonly Dictionary<int, int> m_resendsThisSecond = [];
    public int Verified, Resent, GivenUp, Reported, RepairedColumns;
    public string Last = "";

    public static ulong HashChunk(TerrainChunk chunk) {
        // FNV-1a over the cells without their light (the client computes light itself).
        ulong h = 14695981039346656037;
        int[] cells = chunk.Cells;
        for (int i = 0; i < cells.Length; i++) { h ^= (uint)Terrain.ReplaceLight(cells[i], 0); h *= 1099511628211; }
        return h;
    }
    /// <summary>FNV-1a over one column (x, z within the chunk, y 0..255), light stripped.</summary>
    public static ulong HashColumn(TerrainChunk chunk, int x, int z) {
        ulong h = 14695981039346656037;
        int[] cells = chunk.Cells;
        for (int y = 0; y < 256; y++) { h ^= (uint)Terrain.ReplaceLight(cells[TerrainChunk.CalculateCellIndex(x, y, z)], 0); h *= 1099511628211; }
        return h;
    }
    public static ulong[] HashColumns(TerrainChunk chunk) {
        var hashes = new ulong[256];
        for (int x = 0; x < 16; x++) for (int z = 0; z < 16; z++) hashes[x * 16 + z] = HashColumn(chunk, x, z);
        return hashes;
    }

    // ---- server
    public void Sent(int playerIndex, List<TerrainChunk> chunks) {
        if (!m_watch.TryGetValue(playerIndex, out var watches)) m_watch[playerIndex] = watches = [];
        double due = Time.RealTime + SettleSeconds;
        foreach (var chunk in chunks) {
            if (!watches.TryGetValue(chunk.Coords, out var w)) watches[chunk.Coords] = w = new Watch { Coords = chunk.Coords };
            w.DueAt = due; w.Asked = false; w.Rechecked = 0;
        }
    }
    public void Forget(int playerIndex) { m_watch.Remove(playerIndex); m_lastResendSecond.Remove(playerIndex); m_resendsThisSecond.Remove(playerIndex); }
    public void Clear() { m_watch.Clear(); m_lastResendSecond.Clear(); m_resendsThisSecond.Clear(); }
    /// <summary>Server tick: asks each client to verify the chunks whose settle time is up (bounded per message); a chunk in
    /// column mode is asked for its column hashes as well.</summary>
    public void ServerTick(Func<int, ClientSession> sessionOf, Action<ClientSession, byte[]> send) {
        double now = Time.RealTime;
        var terrain = GameManager.Project?.FindSubsystem<SubsystemTerrain>(false)?.Terrain; if (terrain is null) return;
        foreach (var (playerIndex, watches) in m_watch.ToList()) {
            var session = sessionOf(playerIndex);
            if (session is null) { m_watch.Remove(playerIndex); continue; }
            var due = watches.Values.Where(w => now >= w.DueAt).Take(ChunksPerMessage).ToList();
            if (due.Count == 0) continue;
            var entries = new List<Entry>(); var columns = new List<int[]>();
            foreach (var w in due) {
                var chunk = terrain.GetChunkAtCoords(w.Coords.X, w.Coords.Y);
                if (chunk is null || chunk.ThreadState <= TerrainChunkState.InvalidContents4) { watches.Remove(w.Coords); continue; } // gone on the server: nothing to hold the client to
                if (w.Asked && ++w.Pendings > MaxPendings) { watches.Remove(w.Coords); GivenUp++; continue; } // never reported back
                entries.Add(new(w.Coords.X, w.Coords.Y, HashChunk(chunk)));
                if (w.ColumnMode) columns.Add([w.Coords.X, w.Coords.Y]);
                w.Asked = true; w.DueAt = now + ReportTimeout;
            }
            if (entries.Count > 0) { send(session, JsonSerializer.SerializeToUtf8Bytes(new Verify(entries, columns))); Verified += entries.Count; }
        }
    }
    /// <summary>Server: a client's answer. A differing chunk is re-sent through the platform's own packet, a bounded
    /// number of times and at a bounded rate; after <see cref="ColumnAfterResends"/> resends its differing columns are
    /// repaired cell by cell; an unsettled one is asked again later.</summary>
    public void OnReport(int playerIndex, Report report, ClientSession session) {
        if (report is null || !m_watch.TryGetValue(playerIndex, out var watches)) return;
        Reported++;
        var terrain = GameManager.Project?.FindSubsystem<SubsystemTerrain>(false)?.Terrain; if (terrain is null) return;
        double now = Time.RealTime;
        foreach (var c in report.Same ?? []) {
            if (!watches.TryGetValue(new Point2(c[0], c[1]), out var w)) continue;
            w.ColumnMode = false;
            if (w.Rechecked >= Rechecks) { watches.Remove(w.Coords); continue; }
            w.DueAt = now + RecheckSeconds[w.Rechecked++]; w.Asked = false;   // equal now; asked again after the client's neighbourhood has generated
        }
        foreach (var c in report.Pending ?? []) if (watches.TryGetValue(new Point2(c[0], c[1]), out var w)) { w.DueAt = now + PendingRetry; w.Asked = false; }
        var columnsOf = (report.Columns ?? []).ToDictionary(r => new Point2(r.X, r.Z), r => r.Hashes);
        var resend = new List<TerrainChunk>(); int repaired = 0;
        foreach (var c in report.Differs ?? []) {
            var coords = new Point2(c[0], c[1]);
            if (!watches.TryGetValue(coords, out var w)) continue;
            var chunk = terrain.GetChunkAtCoords(coords.X, coords.Y);
            if (chunk is null) { watches.Remove(coords); continue; }
            if (w.ColumnMode) {
                if (!columnsOf.TryGetValue(coords, out var theirs) || theirs is null || theirs.Length != 256) { w.DueAt = now + PendingRetry; w.Asked = false; continue; } // asked for columns next tick
                var ours = HashColumns(chunk); var changes = new List<CellChange>(); int columns = 0;
                // Which columns differ now, and whether they are the ones repaired in the round before: the same columns again
                // means the repair does not take on that client (or it changes them back); other columns each round means
                // the terrain there keeps changing between the hash and the repair (mp-state-consistency-20261002, X02).
                string differing = string.Join(" ", Enumerable.Range(0, 256).Where(i => ours[i] != theirs[i]).Select(i => $"{i / 16},{i % 16}"));
                if (w.ColumnRounds > 0 && differing == w.LastDiffering) w.SameAsBefore++;
                w.LastDiffering = differing;
                if (w.ColumnRounds >= MaxColumnRounds) {
                    watches.Remove(coords); GivenUp++;
                    int still = differing.Length == 0 ? 0 : differing.Split(' ').Length;
                    Log.Warning($"[ScCsgoNet] terrain: chunk {coords} still differs on player {playerIndex} after {MaxColumnRounds} column repairs; given up ({still} column(s) differ now: {(differing.Length > 120 ? differing[..120] + " ..." : differing)}; the same columns as the round before in {w.SameAsBefore} of {MaxColumnRounds} rounds)");
                    continue;
                }
                for (int i = 0; i < 256 && columns < ColumnsPerRound; i++) {
                    if (ours[i] == theirs[i]) continue;
                    int x = i / 16, z = i % 16; columns++;
                    for (int y = 0; y < 256; y++) changes.Add(new CellChange { X = chunk.Origin.X + x, Y = y, Z = chunk.Origin.Y + z, Value = Terrain.ReplaceLight(chunk.Cells[TerrainChunk.CalculateCellIndex(x, y, z)], 0) });
                }
                if (columns == 0) { w.ColumnMode = false; w.DueAt = now + SettleSeconds; w.Asked = false; continue; } // column hashes agree although the chunk hash did not: verify again as a whole
                for (int offset = 0; offset < changes.Count; offset += TerrainChangeCellListPacket.MaximumCellChanges)
                    NetworkManager.Queue(new TerrainChangeCellListPacket(changes.GetRange(offset, Math.Min(TerrainChangeCellListPacket.MaximumCellChanges, changes.Count - offset))) { To = session });
                w.ColumnRounds++; repaired += columns; RepairedColumns += columns; w.DueAt = now + SettleSeconds * 2; w.Asked = false;
                Last = $"repaired {columns} column(s) of chunk {coords} on player {playerIndex} (round {w.ColumnRounds})"; KnifeLog.Diagnostic("[ScCsgoNet] terrain: " + Last);
                continue;
            }
            if (w.Resends >= ColumnAfterResends) { w.ColumnMode = true; w.DueAt = now + PendingRetry; w.Asked = false; continue; } // diffs did not converge: switch to exact column repair
            double second = Math.Floor(now);
            if (m_lastResendSecond.GetValueOrDefault(playerIndex) != second) { m_lastResendSecond[playerIndex] = second; m_resendsThisSecond[playerIndex] = 0; }
            if (m_resendsThisSecond[playerIndex] >= ResendsPerSecond) { w.DueAt = now + PendingRetry; w.Asked = false; continue; }
            m_resendsThisSecond[playerIndex]++; w.Resends++; w.DueAt = now + SettleSeconds * Math.Pow(2, w.Resends - 1); w.Asked = false; w.Rechecked = 0;
            resend.Add(chunk);
        }
        if (resend.Count > 0) {
            NetworkManager.Queue(new TerrainSyncChunkListPacket(resend) { To = session });
            Resent += resend.Count; Last = $"resent {resend.Count} chunk(s) to player {playerIndex}: {string.Join(" ", resend.Select(c => c.Coords))}";
            KnifeLog.Diagnostic("[ScCsgoNet] terrain: " + Last);
        }
    }

    // ---- client
    /// <summary>Client: hashes the chunks it has settled and answers; a chunk still generating or not allocated is pending.
    /// Column hashes are added for the chunks the server asked them for.</summary>
    public static Report Answer(Verify verify) {
        var terrain = GameManager.Project?.FindSubsystem<SubsystemTerrain>(false)?.Terrain;
        var report = new Report([], [], [], []);
        if (terrain is null || verify?.Chunks is null) return report;
        var wanted = new HashSet<Point2>((verify.Columns ?? []).Select(c => new Point2(c[0], c[1])));
        foreach (var e in verify.Chunks) {
            var chunk = terrain.GetChunkAtCoords(e.X, e.Z);
            if (chunk is null || chunk.ThreadState < TerrainChunkState.InvalidLight || chunk.State < TerrainChunkState.InvalidLight) { report.Pending.Add([e.X, e.Z]); continue; }
            bool same = HashChunk(chunk) == e.Hash;
            (same ? report.Same : report.Differs).Add([e.X, e.Z]);
            if (!same && wanted.Contains(new Point2(e.X, e.Z))) report.Columns.Add(new ColumnReport(e.X, e.Z, HashColumns(chunk)));
        }
        return report;
    }
}
