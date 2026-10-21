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
    /// <summary>Actual shots retain their owner so hearing enemies engage the shooter, not a nearby bystander.</summary>
    public static void NotifyGunshot(SubsystemNoise noise,ComponentBody shooter,bool silenced,bool taser){
        if(shooter is not null)noise?.MakeNoise(shooter,taser?.5f:1f,NoiseRange(silenced,taser));
    }
    /// <summary>Explosions make the engine's explosion noise (2026-10-21 player request: "希望动物听到手雷爆炸声会吓到逃跑"):
    /// loudness 1 - the native RunAway listener's threshold - over ranges taken from the engine's own explosion tiers
    /// (SubsystemExplosions: 30-130 blocks by pressure; an unsilenced shot is 40), and every animal within that range is
    /// scared off (<see cref="Startle"/>). Only the authority does it: the creatures' behaviour runs there, and a client's
    /// copy of a blast must not be heard twice.</summary>
    public const float HeNoiseRange = 50, FlashNoiseRange = 40, BombNoiseRange = 70;
    public static void NotifyExplosion(SubsystemNoise noise, Vector3 position, float range) {
        if (!ScNet.IsAuthority || noise is null) return;
        noise.MakeNoise(position, 1f, range);
        Startle(noise.m_subsystemBodies, position, range);
    }
    /// <summary>The importance the engine gives an animal's run-away behaviour when it was hurt or its attacker is close.</summary>
    public const float ScareImportance = 100;
    /// <summary>An animal that hears a blast runs from it. The noise alone is not enough in the real game (2026-10-21, isolated
    /// 1.9.3.1: a cow 18 blocks from an HE blast stayed "Inactive" and only drifted a block - cattle answer any loud noise
    /// with their slow cattle drive, importance 7, above the run-away's 5 for a heard noise): so every creature within range
    /// that has the game's own run-away behaviour gets the engine's "attacked" importance and the blast as the place to
    /// run from - it then runs at full speed to a safe place, exactly as the engine makes a hurt animal do. Creatures
    /// without that behaviour (birds fly off from the noise itself; players, CS agents) are untouched. Returns how many.</summary>
    public static int Startle(SubsystemBodies bodies, Vector3 position, float range) {
        if (bodies is null) return 0;
        var near = new DynamicArray<ComponentBody>(); bodies.FindBodiesAroundPoint(new Vector2(position.X, position.Z), range, near);
        int scared = 0;
        for (int i = 0; i < near.Count; i++) {
            var body = near.Array[i];
            if (!(Vector3.DistanceSquared(body.Position, position) < range * range) || body.Entity.FindComponent<ComponentPlayer>() is not null) continue;
            if (body.Entity.FindComponent<ComponentRunAwayBehavior>() is not { } escape) continue;
            escape.m_lastNoiseSourcePosition = position;
            escape.m_importanceLevel = MathF.Max(escape.m_importanceLevel, ScareImportance);
            scared++;
        }
        return scared;
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
