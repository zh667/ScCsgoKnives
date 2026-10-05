using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Engine;
namespace Game;

/// <summary>One authored respawn candidate: where the feet stand and which way the player looks (yaw in radians, 0 = -Z).</summary>
public sealed record DmSpawnPoint(int Id, float X, float Y, float Z, float Yaw, string Label = "") {
    [JsonIgnore] public Vector3 Position => new(X, Y, Z);
    [JsonIgnore] public Point3 Cell => new(Terrain.ToCell(X), Terrain.ToCell(Y), Terrain.ToCell(Z));
}

/// <summary>The host's arena: a box of cells (both corners inclusive), the preparation / spectator place and the respawn
/// candidates (DM-04). Immutable; an edit makes a new revision. Stored as JSON text in the package's own save group.</summary>
public sealed record DmArenaDefinition {
    public int Revision { get; init; }
    public bool HasRegion { get; init; }
    public int MinX { get; init; } public int MinY { get; init; } public int MinZ { get; init; }
    public int MaxX { get; init; } public int MaxY { get; init; } public int MaxZ { get; init; }
    public bool HasLobby { get; init; }
    public float LobbyX { get; init; } public float LobbyY { get; init; } public float LobbyZ { get; init; } public float LobbyYaw { get; init; }
    public IReadOnlyList<DmSpawnPoint> Spawns { get; init; } = [];
    public int NextSpawnId { get; init; } = 1;

    [JsonIgnore] public Vector3 Lobby => new(LobbyX, LobbyY, LobbyZ);
    public DmArenaDefinition WithRegion(Point3 a, Point3 b) => this with { Revision = Revision + 1, HasRegion = true,
        MinX = Math.Min(a.X, b.X), MinY = Math.Min(a.Y, b.Y), MinZ = Math.Min(a.Z, b.Z), MaxX = Math.Max(a.X, b.X), MaxY = Math.Max(a.Y, b.Y), MaxZ = Math.Max(a.Z, b.Z) };
    public DmArenaDefinition WithLobby(Vector3 position, float yaw) => this with { Revision = Revision + 1, HasLobby = true, LobbyX = position.X, LobbyY = position.Y, LobbyZ = position.Z, LobbyYaw = yaw };
    public DmArenaDefinition AddSpawn(Vector3 position, float yaw, string label = "") => this with { Revision = Revision + 1, NextSpawnId = NextSpawnId + 1,
        Spawns = [.. Spawns, new DmSpawnPoint(NextSpawnId, position.X, position.Y, position.Z, yaw, label ?? "")] };
    public DmArenaDefinition RemoveSpawn(int id) => Spawns.Any(s => s.Id == id) ? this with { Revision = Revision + 1, Spawns = Spawns.Where(s => s.Id != id).ToArray() } : this;
    /// <summary>Whether a point is inside the box (a cell is the unit cube from its integer corner).</summary>
    public bool Contains(Vector3 p) => HasRegion && p.X >= MinX && p.X < MaxX + 1 && p.Y >= MinY && p.Y < MaxY + 1 && p.Z >= MinZ && p.Z < MaxZ + 1;
    public bool ContainsCell(Point3 c) => HasRegion && c.X >= MinX && c.X <= MaxX && c.Y >= MinY && c.Y <= MaxY && c.Z >= MinZ && c.Z <= MaxZ;

    static readonly JsonSerializerOptions s_json = new() { WriteIndented = false };
    public string Encode() => JsonSerializer.Serialize(this, s_json);
    public static bool TryDecode(string text, out DmArenaDefinition arena) {
        arena = new();
        if (string.IsNullOrWhiteSpace(text)) return true;
        try {
            var read = JsonSerializer.Deserialize<DmArenaDefinition>(text, s_json) ?? new();
            if (read.Spawns is null) return false;
            bool finite = float.IsFinite(read.LobbyX + read.LobbyY + read.LobbyZ + read.LobbyYaw) && read.Spawns.All(s => s is not null && float.IsFinite(s.X + s.Y + s.Z + s.Yaw));
            bool ids = finite && read.Spawns.Select(s => s.Id).Distinct().Count() == read.Spawns.Count && read.Spawns.All(s => s.Id > 0 && s.Id < read.NextSpawnId);
            if (!finite || !ids || read.Spawns.Count > DmArenaRules.MaxSpawns) return false;
            arena = read; return true;
        }
        catch (JsonException) { arena = new(); return false; }
    }
}

/// <summary>What the arena checks need to know about the world: nothing else is read, so the same checks run on the
/// server's terrain and in the offline tests.</summary>
public interface IDmWorldProbe {
    /// <summary>The cell's chunk is loaded and its contents can be trusted.</summary>
    bool Loaded(Point3 cell);
    /// <summary>A body cannot stand in this cell (it collides).</summary>
    bool Solid(Point3 cell);
    /// <summary>Fire, magma or another cell that hurts whoever stands in it.</summary>
    bool Hazard(Point3 cell);
    /// <summary>A fluid a player would be swimming in.</summary>
    bool Fluid(Point3 cell);
}

public enum DmArenaIssueCode { NoRegion, RegionTooLarge, NoLobby, LobbyUnsafe, TooFewSpawns, SpawnOutside, SpawnBlocked, SpawnNoGround, SpawnHazard, SpawnUnloaded, SpawnDuplicate, SpawnsClustered }
/// <summary>One finding about the arena. <see cref="Blocks"/>: a match cannot start while it stands; otherwise advice.</summary>
public sealed record DmArenaIssue(DmArenaIssueCode Code, bool Blocks, int SpawnId, string Message);

public static class DmArenaRules {
    /// <summary>Engineering limits (not user decisions): a box edge in cells, and the number of authored points.</summary>
    public const int MaxEdge = 256, MaxSpawns = 128;
    /// <summary>UTF-8 JSON budget on the wire: leaves 16 KiB of the existing 64 KiB packet for rules, notices and framing.
    /// Does not change the saved arena format or truncate authored labels.</summary>
    public const int MaxNetworkBytes = 48 * 1024;
    /// <summary>Two points closer than this are the same place for the purpose of "at least two different points".</summary>
    public const float DistinctDistance = 1.5f;
    /// <summary>All points within this of each other: the author is told the arena has, in effect, one spawn area.</summary>
    public const float ClusterDistance = 6f;

    /// <summary>Whether a player can be put with the feet at <paramref name="position"/>: loaded, two free cells for the body,
    /// something solid directly below, nothing harmful and no fluid in the body's cells. Returns the reason otherwise.</summary>
    public static DmArenaIssueCode? Standable(IDmWorldProbe world, Vector3 position) {
        var feet = new Point3(Terrain.ToCell(position.X), Terrain.ToCell(position.Y), Terrain.ToCell(position.Z));
        var head = new Point3(feet.X, feet.Y + 1, feet.Z); var below = new Point3(feet.X, feet.Y - 1, feet.Z);
        if (!world.Loaded(feet) || !world.Loaded(head) || !world.Loaded(below)) return DmArenaIssueCode.SpawnUnloaded;
        if (world.Solid(feet) || world.Solid(head)) return DmArenaIssueCode.SpawnBlocked;
        if (world.Hazard(feet) || world.Hazard(head) || world.Hazard(below) || world.Fluid(feet) || world.Fluid(head)) return DmArenaIssueCode.SpawnHazard;
        if (!world.Solid(below)) return DmArenaIssueCode.SpawnNoGround;
        return null;
    }

    /// <summary>Everything wrong or doubtful about the arena as it stands in this world (design §4.1 step 4). Unloaded
    /// terrain is reported as such: a point is never called valid on terrain that was not read.</summary>
    public static List<DmArenaIssue> Validate(DmArenaDefinition arena, IDmWorldProbe world) {
        var issues = new List<DmArenaIssue>();
        if (!arena.HasRegion) { issues.Add(new(DmArenaIssueCode.NoRegion, true, 0, "还没有设置竞技区域的两个角点")); return issues; }
        if (arena.MaxX - arena.MinX >= MaxEdge || arena.MaxY - arena.MinY >= MaxEdge || arena.MaxZ - arena.MinZ >= MaxEdge)
            issues.Add(new(DmArenaIssueCode.RegionTooLarge, true, 0, $"竞技区域每边不能超过 {MaxEdge} 格"));
        if (!arena.HasLobby) issues.Add(new(DmArenaIssueCode.NoLobby, true, 0, "还没有设置准备/观战位置"));
        else if (Standable(world, arena.Lobby) is { } lobby) issues.Add(new(DmArenaIssueCode.LobbyUnsafe, true, 0, "准备/观战位置不能站人：" + Describe(lobby)));
        var good = new List<DmSpawnPoint>();
        foreach (var spawn in arena.Spawns) {
            if (!arena.Contains(spawn.Position)) { issues.Add(new(DmArenaIssueCode.SpawnOutside, false, spawn.Id, $"复活点 #{spawn.Id}{Name(spawn)} 在竞技区域之外")); continue; }
            if (Standable(world, spawn.Position) is { } why) { issues.Add(new(why, false, spawn.Id, $"复活点 #{spawn.Id}{Name(spawn)} {Describe(why)}")); continue; }
            if (good.FirstOrDefault(g => Vector3.Distance(g.Position, spawn.Position) < DistinctDistance) is { } twin) {
                issues.Add(new(DmArenaIssueCode.SpawnDuplicate, false, spawn.Id, $"复活点 #{spawn.Id}{Name(spawn)} 与 #{twin.Id} 几乎在同一位置")); continue; }
            good.Add(spawn);
        }
        if (good.Count < 2) issues.Add(new(DmArenaIssueCode.TooFewSpawns, true, 0, $"至少需要两个不同位置的合法复活点（现在 {good.Count} 个）"));
        else if (good.All(a => good.All(b => Vector3.Distance(a.Position, b.Position) <= ClusterDistance)))
            issues.Add(new(DmArenaIssueCode.SpawnsClustered, false, 0, "所有复活点都挤在一处，建议分散到地图各处"));
        return issues;
    }
    /// <summary>The points a match can use right now (inside, standable, no near twin), in authored order.</summary>
    public static List<DmSpawnPoint> Usable(DmArenaDefinition arena, IDmWorldProbe world) {
        var good = new List<DmSpawnPoint>();
        if (!arena.HasRegion) return good;
        foreach (var spawn in arena.Spawns)
            if (arena.Contains(spawn.Position) && Standable(world, spawn.Position) is null && !good.Any(g => Vector3.Distance(g.Position, spawn.Position) < DistinctDistance)) good.Add(spawn);
        return good;
    }
    public static bool CanStart(IEnumerable<DmArenaIssue> issues) => !issues.Any(i => i.Blocks);
    static string Name(DmSpawnPoint s) => string.IsNullOrEmpty(s.Label) ? "" : "（" + s.Label + "）";
    static string Describe(DmArenaIssueCode code) => code switch {
        DmArenaIssueCode.SpawnBlocked => "被方块挡住（身体或头部位置不是空的）",
        DmArenaIssueCode.SpawnNoGround => "脚下没有可站立的方块",
        DmArenaIssueCode.SpawnHazard => "处在火、岩浆或液体里",
        DmArenaIssueCode.SpawnUnloaded => "所在地形尚未加载，无法确认",
        _ => code.ToString()
    };
}
