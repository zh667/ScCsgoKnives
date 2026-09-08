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
    /// <summary>Chunk edge in cells. A step never skips a column, so no seam can be missed.</summary>
    const int ChunkSize = 16;
    /// <summary>Never walk more columns than this in one shot, whatever the range asks for.</summary>
    const int MaxColumns = 128;

    public static bool IsLoaded(Terrain terrain, int cellX, int cellZ) {
        var chunk = terrain?.GetChunkAtCell(cellX, cellZ);
        return chunk is not null && chunk.State >= TerrainChunkState.InvalidLight;
    }

    /// <summary>The distance at which the continuously loaded terrain ends, clamped to <paramref name="maximum"/>.
    /// Returns 0 when the shooter is not standing in a loaded column at all.</summary>
    public static float LoadedLimit(Terrain terrain, Vector3 start, Vector3 direction, float maximum) {
        if (!float.IsFinite(maximum) || maximum <= 0) return 0;
        float length = direction.Length();
        if (!float.IsFinite(length) || length < 1e-6f) return 0;
        // Headless callers pass no terrain; there is then nothing to stop the ray short of its own limit.
        if (terrain is null) return maximum;
        Vector3 step = direction / length;
        int cellX = Terrain.ToCell(start.X), cellZ = Terrain.ToCell(start.Z);
        if (!IsLoaded(terrain, cellX, cellZ)) return 0;
        // 2D DDA over chunk columns: advance to the next column boundary, test it, and stop at the first gap.
        float x = start.X, z = start.Z;
        int chunkX = cellX >> 4, chunkZ = cellZ >> 4;
        int stepX = step.X > 0 ? 1 : step.X < 0 ? -1 : 0;
        int stepZ = step.Z > 0 ? 1 : step.Z < 0 ? -1 : 0;
        float toX = stepX == 0 ? float.PositiveInfinity : (((chunkX + (stepX > 0 ? 1 : 0)) * ChunkSize) - x) / step.X;
        float toZ = stepZ == 0 ? float.PositiveInfinity : (((chunkZ + (stepZ > 0 ? 1 : 0)) * ChunkSize) - z) / step.Z;
        float deltaX = stepX == 0 ? float.PositiveInfinity : ChunkSize / Math.Abs(step.X);
        float deltaZ = stepZ == 0 ? float.PositiveInfinity : ChunkSize / Math.Abs(step.Z);
        float travelled = 0;
        for (int i = 0; i < MaxColumns; i++) {
            float next = MathF.Min(toX, toZ);
            if (!float.IsFinite(next) || next >= maximum) return maximum;
            if (toX <= toZ) { chunkX += stepX; toX += deltaX; } else { chunkZ += stepZ; toZ += deltaZ; }
            travelled = next;
            // A hair past the boundary is inside the next column; nudging avoids testing the one just left.
            if (!IsLoaded(terrain, (chunkX << 4) + 1, (chunkZ << 4) + 1)) return Math.Clamp(travelled, 0, maximum);
        }
        return Math.Clamp(travelled, 0, maximum);
    }
}
