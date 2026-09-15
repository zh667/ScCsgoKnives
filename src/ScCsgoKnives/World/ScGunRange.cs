using Engine;
namespace Game;

/// <summary>How far a maximum-level gun may actually reach.
///
/// Lv10 removes the weapon's own distance limit, not the world's: the shot follows the ray only while the
/// terrain under it is continuously loaded. The walk steps chunk by chunk and stops at the first column that is
/// absent or not yet valid, so a bullet never crosses an unloaded hole to find a target behind it, and nothing
/// here asks the terrain updater to load anything. Walls and the nearest target still stop the shot first, and
/// no part of this aims, curves or sees through anything.</summary>
public static class ScGunRange {
    /// <summary>Foliage is not ballistic cover. Explicit vanilla types preserve walls, trunks, glass and the solid
    /// dirt cube named GrassBlock; on top of those, any block that is not collidable is passed through, which is
    /// exactly the rule the game's own projectiles use (`Block.IsCollidable_`). That is what lets a mod's plant -
    /// which is not a vanilla `CrossBlock` and so used to stop CS bullets - stop behaving like a wall.
    ///
    /// The value-less overload is for the startup diagnostic only; the live trace always uses the value so a
    /// state-dependent collidability (an open gate, a raised trapdoor) is respected.</summary>
    /// <summary>The single public predicate the diagnostics and regressions bind to by name. The live trace uses
    /// <see cref="TerrainStopsBullet"/>, which additionally consults the value's own collidability.</summary>
    public static bool StopsBullet(Block block) => block is not null
        && block is not AirBlock and not FluidBlock and not LeavesBlock and not CrossBlock
            and not FallenLeavesBlock and not IvyBlock and not WaterPlantBlock
        && block.IsCollidable;
    public static bool TerrainStopsBullet(int value) => Terrain.ExtractContents(value) != 0
        && StopsBulletFor(BlocksManager.Blocks[Terrain.ExtractContents(value)], value);
    /// <summary>Same test as <see cref="StopsBullet(Block)"/>, but against the value so a state-dependent
    /// collidability is respected. Its own name keeps `GetMethod("StopsBullet")` unambiguous.</summary>
    static bool StopsBulletFor(Block block, int value) => block is not null
        && block is not AirBlock and not FluidBlock and not LeavesBlock and not CrossBlock
            and not FallenLeavesBlock and not IvyBlock and not WaterPlantBlock
        && block.IsCollidable_(value);

    /// <summary>Shared by every gun, mode and pellet, using the live subsystem (not its static test overload).</summary>
    public static TerrainRaycastResult? TraceBullet(SubsystemTerrain terrain, Vector3 start, Vector3 direction, float range,
        BulletTrace observation = null) {
        // A shooter already under the surface must not splash on every cell of the ray; the water impact is only
        // for a shot entering water from air.
        bool startInWater = false;
        var world = terrain?.Terrain;
        if (world is not null) {
            int at = world.GetCellValue(Terrain.ToCell(start.X), Terrain.ToCell(start.Y), Terrain.ToCell(start.Z));
            startInWater = BlocksManager.Blocks[Terrain.ExtractContents(at)] is WaterBlock;
        }
        return TraceTerrain((a, b) => terrain.Raycast(a, b, false, true, (value, distance) => {
            bool stops = TerrainStopsBullet(value);
            observation?.Observe(value, stops);
            Block block = BlocksManager.Blocks[Terrain.ExtractContents(value)];
            // A transparent block from another assembly that still stops a bullet is the shape of the reported
            // "mod plant blocks CS bullets" bug. Log each distinct type once so it can be added deliberately.
            if (stops && block is not null && block.GetType().Assembly != typeof(Block).Assembly && block.IsPlacementTransparent_(value))
                KnifeDiagnostics.WarnOnce("bullet-blocker/" + block.GetType().FullName,
                    $"[GUN_FOLIAGE] a transparent mod block stops bullets: {block.GetType().FullName}; treat it as foliage if it is a plant");
            if (observation is not null && block is LeavesBlock) {
                Vector3 point = a + Vector3.Normalize(b - a) * (distance + .001f);
                observation.Leaves.Add(new(new Point3(Terrain.ToCell(point.X), Terrain.ToCell(point.Y), Terrain.ToCell(point.Z)), value, Vector3.Distance(a, start) + distance));
            }
            if (!startInWater && observation is not null && observation.Fluids.Count == 0 && block is WaterBlock) {
                Vector3 point = a + Vector3.Normalize(b - a) * distance;
                observation.Fluids.Add(new(new Point3(Terrain.ToCell(point.X), Terrain.ToCell(point.Y), Terrain.ToCell(point.Z)), value, point));
            }
            return stops;
        }), start, direction, range);
    }

    /// <summary>Bounded observation of the actual predicate; never changes hit tests or RNG.</summary>
    public sealed class BulletTrace {
        public int IgnoredVegetation;
        public string LastBlocker;
        public readonly List<string> PassedTypes = [];
        [System.Text.Json.Serialization.JsonIgnore] public readonly List<LeafHit> Leaves = [];
        /// <summary>The first water surface this shot enters from air, for the splash effect. Water never stops the
        /// bullet; the entry point is all the effect needs.</summary>
        [System.Text.Json.Serialization.JsonIgnore] public readonly List<FluidHit> Fluids = [];
        public void Observe(int value, bool stops) {
            var block = BlocksManager.Blocks[Terrain.ExtractContents(value)];
            if (stops) { LastBlocker = block.GetType().FullName; return; }
            if (block is AirBlock or FluidBlock) return;
            IgnoredVegetation++;
            string type = block.GetType().FullName;
            if (PassedTypes.Count < 8 && !PassedTypes.Contains(type)) PassedTypes.Add(type);
        }
    }
    public readonly record struct LeafHit(Point3 Cell, int Value, float Distance);
    public readonly record struct FluidHit(Point3 Cell, int Value, Vector3 Point);
    static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    /// <summary>The engine silently truncates each terrain ray to 1000 cells. Segment a long
    /// loaded-world shot into <=512-cell engine calls, retaining global hit distance and ray.
    /// Otherwise a target beyond 1000 could be hit through a wall the engine never tested.</summary>
    public static TerrainRaycastResult? TraceTerrain(Func<Vector3,Vector3,TerrainRaycastResult?> raycast,
        Vector3 start, Vector3 direction, float maximum) {
        if (raycast is null || !float.IsFinite(maximum) || maximum <= 0 || !Finite(start) || !Finite(direction)) return null;
        float length = direction.Length();
        if (!float.IsFinite(length) || length < 1e-6f) return null;
        direction /= length;
        for (double at=0; at<maximum; at+=512) {
            double until = Math.Min(maximum,at+512);
            var hit = raycast(start + direction*(float)at, start + direction*(float)until);
            if (hit is not { } result) continue;
            if (!float.IsFinite(result.Distance) || result.Distance < 0 || result.Distance > until-at) continue;
            result.Distance += (float)at;
            result.Ray = new Ray3(start,direction);
            return result;
        }
        return null;
    }
    /// <summary>Chunk edge in cells. A step never skips a column, so no seam can be missed.</summary>
    const int ChunkSize = 16;

    public static bool IsLoaded(Terrain terrain, int cellX, int cellZ) {
        var chunk = terrain?.GetChunkAtCell(cellX, cellZ);
        return chunk is not null && chunk.State >= TerrainChunkState.InvalidLight;
    }

    /// <summary>The distance at which the continuously loaded terrain ends, clamped to <paramref name="maximum"/>.
    /// Returns 0 when the shooter is not standing in a loaded column at all.</summary>
    public static float LoadedLimit(Terrain terrain, Vector3 start, Vector3 direction, float maximum) {
        if (terrain is null) return float.IsFinite(maximum) && maximum > 0
            && Finite(direction) && direction.LengthSquared() > 1e-12f ? maximum : 0;
        return TraceLoaded((x, z) => {
            var chunk = terrain.GetChunkAtCoords(x, z);
            return chunk is not null && chunk.State >= TerrainChunkState.InvalidLight;
        }, terrain.AllocatedChunks.Length, start, direction, maximum);
    }

    /// <summary>Each iteration visits a new column. Allocated column count proves termination;
    /// it is not an arbitrary range budget. Also stop at the engine's Y=0/256 boundaries.</summary>
    public static float TraceLoaded(Func<int, int, bool> loaded, int columnCount, Vector3 start, Vector3 direction, float maximum) {
        if (loaded is null || columnCount <= 0 || !Finite(start)
            || !Finite(direction) || start.Y < 0 || start.Y >= 256) return 0;
        if (!float.IsFinite(maximum) || maximum <= 0) return 0;
        float length = direction.Length();
        if (!float.IsFinite(length) || length < 1e-6f) return 0;
        Vector3 step = direction / length;
        if (step.Y > 0) maximum = Math.Min(maximum, (256 - start.Y) / step.Y);
        else if (step.Y < 0) maximum = Math.Min(maximum, -start.Y / step.Y);
        int cellX = Terrain.ToCell(start.X), cellZ = Terrain.ToCell(start.Z);
        if (!loaded(cellX >> 4, cellZ >> 4)) return 0;
        // 2D DDA over chunk columns: advance to the next column boundary, test it, and stop at the first gap.
        float x = start.X, z = start.Z;
        int chunkX = cellX >> 4, chunkZ = cellZ >> 4;
        int stepX = step.X > 0 ? 1 : step.X < 0 ? -1 : 0;
        int stepZ = step.Z > 0 ? 1 : step.Z < 0 ? -1 : 0;
        double toX = stepX == 0 ? double.PositiveInfinity : ((((double)chunkX + (stepX > 0 ? 1 : 0)) * ChunkSize) - x) / step.X;
        double toZ = stepZ == 0 ? double.PositiveInfinity : ((((double)chunkZ + (stepZ > 0 ? 1 : 0)) * ChunkSize) - z) / step.Z;
        double deltaX = stepX == 0 ? double.PositiveInfinity : ChunkSize / Math.Abs((double)step.X);
        double deltaZ = stepZ == 0 ? double.PositiveInfinity : ChunkSize / Math.Abs((double)step.Z);
        double travelled = 0;
        for (int i = 0; i < columnCount; i++) {
            double next = Math.Min(toX, toZ);
            if (!double.IsFinite(next) || next >= maximum) return maximum;
            // At an exact corner the ray enters the diagonal, not a side column.
            bool crossX = toX <= toZ, crossZ = toZ <= toX;
            if (crossX) { chunkX += stepX; toX += deltaX; }
            if (crossZ) { chunkZ += stepZ; toZ += deltaZ; }
            travelled = next;
            // A hair past the boundary is inside the next column; nudging avoids testing the one just left.
            if (!loaded(chunkX, chunkZ)) return (float)Math.Clamp(travelled, 0, maximum);
        }
        // A concurrently changing terrain / inconsistent provider fails closed.
        return (float)Math.Clamp(travelled, 0, maximum);
    }
}
