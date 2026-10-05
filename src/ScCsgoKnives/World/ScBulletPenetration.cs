using Engine;
namespace Game;

/// <summary>A world mode's bullet penetration (deathmatch round 6; the user: "穿墙穿透…按方块材质+厚度", "木制的都设为容易穿",
/// "子弹打穿一个人后，还要继续伤到后面的人"). The core walks the round through what stops it - blocks by their collision boxes,
/// bodies by the mode's hit capsules - and the mode prices each crossing; without a mode a round stops at the first thing it
/// meets, as always.</summary>
public interface IScPenetration {
    /// <summary>At most this many obstacles are crossed by one round (CS2: 4).</summary>
    int MaxCrossings { get; }
    /// <summary>The thickest obstacle a round crosses, metres (CS2: 90 units).</summary>
    float MaxThicknessMetres { get; }
    /// <summary>The damage a round of <paramref name="spec"/> carrying <paramref name="damage"/> loses crossing an obstacle
    /// that it entered at a block of value <paramref name="entryValue"/> and left at <paramref name="exitValue"/> (both -1:
    /// a body), <paramref name="thicknessMetres"/> thick along the round; null: the obstacle stops it.</summary>
    float? Loss(GunSpec spec, int entryValue, int exitValue, float thicknessMetres, float damage);
}

/// <summary>The geometry of a round's walk through obstacles (the pricing is the mode's, IScPenetration).</summary>
public static class ScBulletPenetration {
    public enum Kind { Block, Body }
    /// <summary>One obstacle crossed: where the round entered and left it (distances along the round from its start).</summary>
    public readonly record struct Crossing(Kind What, float Entry, float Exit, int EntryValue, int ExitValue);
    /// <summary>A body reached after crossing <paramref name="Crossed"/> obstacles, having lost <paramref name="Lost"/> damage.</summary>
    public readonly record struct BodyHit(ComponentBody Body, ScHitPart Part, float Distance, float Lost, int Crossed);
    /// <summary>The round beyond its first obstacle: what it crossed, whom it reached after that, where it stopped.</summary>
    public sealed class Walk {
        public readonly List<Crossing> Crossings = [];
        public readonly List<BodyHit> Hits = [];
        public float Travel;
        /// <summary>The block that stopped the round, when that is not the first obstacle (the caller shows the first).</summary>
        public (float Distance, int Value)? FinalBlock;
    }
    /// <summary>Sampling step inside an obstacle, metres (0.8 CS2 units), refined at the exit.</summary>
    public const float Step = .02f;
    public const float MaxBodyThickness = 1.5f;

    /// <summary>Whether a point lies inside the collision volume of a block that stops rounds.</summary>
    public static bool Inside(SubsystemTerrain subsystem, Vector3 p, out int value) {
        int x = Terrain.ToCell(p.X), y = Terrain.ToCell(p.Y), z = Terrain.ToCell(p.Z);
        value = subsystem.Terrain.GetCellValue(x, y, z);
        if (!ScGunRange.TerrainStopsBullet(value)) return false;
        var boxes = BlocksManager.Blocks[Terrain.ExtractContents(value)].GetCustomCollisionBoxes(subsystem, value);
        Vector3 local = p - new Vector3(x, y, z);
        foreach (var b in boxes)
            if (local.X >= b.Min.X && local.X <= b.Max.X && local.Y >= b.Min.Y && local.Y <= b.Max.Y && local.Z >= b.Min.Z && local.Z <= b.Max.Z) return true;
        return false;
    }
    /// <summary>Where a round that entered solid blocks at <paramref name="entry"/> leaves them (every adjacent block that
    /// stops rounds counts as the same obstacle, as CS2 traces to the exit of a solid); null when the solid is thicker than
    /// <paramref name="max"/> metres.</summary>
    public static float? ExitBlocks(SubsystemTerrain subsystem, Vector3 start, Vector3 dir, float entry, float max, out int exitValue) {
        exitValue = 0; float inside = entry;
        for (float s = entry + Step * .5f; s <= entry + max; s += Step) {
            if (Inside(subsystem, start + dir * s, out int v)) { inside = s; exitValue = v; continue; }
            float lo = inside, hi = s;
            for (int i = 0; i < 6; i++) { float mid = (lo + hi) * .5f; if (Inside(subsystem, start + dir * mid, out int w)) { lo = mid; exitValue = w; } else hi = mid; }
            if (exitValue == 0) Inside(subsystem, start + dir * (entry + Step * .25f), out exitValue);
            return hi;
        }
        return null;
    }
    /// <summary>Where a round that entered a body's capsules at <paramref name="entry"/> leaves them.</summary>
    public static float ExitCapsules(IReadOnlyList<ScHitCapsule> capsules, Vector3 start, Vector3 dir, float entry) {
        bool In(float s) {
            Vector3 p = start + dir * s;
            foreach (var c in capsules) {
                Vector3 ab = c.B - c.A; float ll = Vector3.Dot(ab, ab), u = ll > 1e-12f ? Math.Clamp(Vector3.Dot(p - c.A, ab) / ll, 0, 1) : 0;
                if (Vector3.DistanceSquared(p, c.A + ab * u) <= c.Radius * c.Radius) return true;
            }
            return false;
        }
        float inside = entry;
        for (float s = entry + Step * .5f; s <= entry + MaxBodyThickness; s += Step) {
            if (In(s)) { inside = s; continue; }
            float lo = inside, hi = s;
            for (int i = 0; i < 6; i++) { float mid = (lo + hi) * .5f; if (In(mid)) lo = mid; else hi = mid; }
            return hi;
        }
        return entry + MaxBodyThickness;
    }

    /// <summary>The round of <paramref name="spec"/> from <paramref name="start"/> along unit <paramref name="dir"/> beyond the
    /// first obstacle the caller found (<paramref name="firstBody"/> and/or <paramref name="firstBlock"/>, the nearer one is
    /// it). <paramref name="damageAt"/> is the round's damage at a distance before any crossing; the first body hit is the
    /// caller's to record. Every later body is reached with what the crossings left.</summary>
    public static Walk Continue(SubsystemTerrain subsystem, IEnumerable<ComponentBody> shootable, ComponentBody shooter, Vector3 start, Vector3 dir, float range,
        ScGunHitTest.Hit? firstBody, TerrainRaycastResult? firstBlock, IScPenetration rules, GunSpec spec, Func<float, float> damageAt,
        Func<ComponentBody, IReadOnlyList<Vector3>> rewind) {
        var walk = new Walk(); var excluded = new HashSet<ComponentBody>();
        float lost = 0; int crossed = 0; bool first = true;
        ScGunHitTest.Hit? body = firstBody; (float Distance, int Value)? block = firstBlock is { } fb ? (fb.Distance, fb.Value) : null;
        while (true) {
            bool isBody = body.HasValue && (!block.HasValue || body.Value.Distance <= block.Value.Distance);
            if (!isBody && !block.HasValue) { walk.Travel = range; break; }
            float entry = isBody ? body.Value.Distance : block.Value.Distance;
            float damage = damageAt(entry) - lost;
            if (isBody) {
                var b = body.Value;
                if (!first) walk.Hits.Add(new(b.Body, b.Part, entry, lost, crossed));
                excluded.Add(b.Body);
                if (damage <= 0 || crossed >= rules.MaxCrossings || ScModes.HitCapsules(b.Body) is not { Length: > 0 } capsules) { walk.Travel = entry; break; }
                float exit = ExitCapsules(capsules, start, dir, entry);
                if (rules.Loss(spec, -1, -1, exit - entry, damage) is not float loss || loss >= damage) { walk.Travel = entry; break; }
                lost += loss; crossed++; walk.Crossings.Add(new(Kind.Body, entry, exit, -1, -1));
                first = false; Next(exit);
            }
            else {
                var k = block.Value;
                if (!first) walk.FinalBlock = k;
                if (damage <= 0 || crossed >= rules.MaxCrossings || ExitBlocks(subsystem, start, dir, entry, rules.MaxThicknessMetres, out int exitValue) is not float exit
                    || rules.Loss(spec, k.Value, exitValue, exit - entry, damage) is not float loss || loss >= damage) { walk.Travel = entry; break; }
                walk.FinalBlock = null; lost += loss; crossed++; walk.Crossings.Add(new(Kind.Block, entry, exit, k.Value, exitValue));
                first = false; Next(exit);
            }
            if (walk.Travel > 0) break;
        }
        return walk;

        void Next(float from) {
            float remaining = range - from - 1e-3f;
            if (remaining <= 0) { walk.Travel = range; return; }
            Vector3 origin = start + dir * (from + 1e-3f);
            var t = ScGunRange.TraceBullet(subsystem, origin, dir, remaining);
            block = t is { } tt ? (tt.Distance + from + 1e-3f, tt.Value) : null;
            var hit = ScGunHitTest.RaycastCompensated(shootable.Where(x => !excluded.Contains(x)), shooter, origin, dir, t.HasValue ? MathF.BitDecrement(t.Value.Distance) : remaining, null, rewind);
            body = hit is { } h ? h with { Distance = h.Distance + from + 1e-3f } : null;
        }
    }
}
