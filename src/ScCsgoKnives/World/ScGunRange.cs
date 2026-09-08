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
