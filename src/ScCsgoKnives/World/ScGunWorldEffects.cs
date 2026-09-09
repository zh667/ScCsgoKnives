using Engine;
namespace Game;

/// <summary>Native musket-style world interactions, independent of the CS2 audio samples.</summary>
public static class ScGunWorldEffects {
    static readonly Engine.Random s_leafRandom = new();
    public static float LeafSample() => s_leafRandom.Float(0f,1f);
    public static float NoiseRange(bool silenced, bool taser) => taser ? 6f : silenced ? 12f : 40f;
    public static void NotifyNoise(SubsystemNoise noise, Vector3 position, bool silenced, bool taser) {
        // 1f is the native RunAway listener's threshold. Silencers reduce range, not that threshold.
        noise?.MakeNoise(position, taser ? .5f : 1f, NoiseRange(silenced, taser));
    }
    public static bool ShouldBreakLeaf(Block block, int value, float sample) => block is LeavesBlock
        && sample > block.GetProjectileResilience(value);
    public static int BreakLeaves(SubsystemTerrain terrain, IEnumerable<ScGunRange.LeafHit> leaves, float travel,
        HashSet<Point3> visited, Func<float> sample) {
        int broken = 0;
        foreach (var hit in leaves) {
            if (hit.Distance > travel || !visited.Add(hit.Cell)) continue;
            int current = terrain.Terrain.GetCellValue(hit.Cell.X, hit.Cell.Y, hit.Cell.Z);
            if (current != hit.Value || !ShouldBreakLeaf(BlocksManager.Blocks[Terrain.ExtractContents(current)], current, sample())) continue;
            // Same noDrop/showDebris flags as Projectile.OnHitTerrain; never delete neighbouring cells.
            terrain.DestroyCell(0, hit.Cell.X, hit.Cell.Y, hit.Cell.Z, 0, true, false);
            broken++;
        }
        return broken;
    }
}
