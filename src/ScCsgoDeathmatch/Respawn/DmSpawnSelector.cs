using Engine;
namespace Game;

/// <summary>What the selector knows about one enemy that is alive in the arena.</summary>
public readonly record struct DmEnemy(Vector3 Feet, Vector3 Eye);

/// <summary>Picks the next life's point from the host's authored candidates (DM-04, design §9). Never a random place in
/// the world and never the world's own spawn: with no acceptable candidate there is no point, and the player waits.
///
/// Hard filter (never relaxed): inside the arena, on loaded terrain, room for the body, ground below, nothing harmful
/// in the body's cells (DmArenaRules.Standable), not occupied by a body, not reserved for another pending life, not
/// in a place the caller marks dangerous right now (fire or an explosive about to act).
/// Safety, best first: no enemy sees the point and none is within the wanted distance; then no enemy sees it; then
/// the farthest from the nearest enemy that does. A point used very recently is passed over while another of the same
/// grade exists. Among the best grade one is drawn at random, so the same point is not handed out every time.</summary>
public static class DmSpawnSelector {
    public sealed class Context {
        public DmArenaDefinition Arena;
        public IDmWorldProbe World;
        public IReadOnlyList<DmEnemy> Enemies = [];
        /// <summary>Feet of every body that must not be spawned into (alive players and others standing in the arena).</summary>
        public IReadOnlyList<Vector3> Occupied = [];
        /// <summary>Points promised to other pending lives.</summary>
        public IReadOnlySet<int> Reserved = new HashSet<int>();
        /// <summary>Point id → the match time it was last used.</summary>
        public IReadOnlyDictionary<int, double> LastUsed = new Dictionary<int, double>();
        public double Now;
        /// <summary>True when nothing solid lies between the two points (an enemy at <c>from</c> sees <c>to</c>).</summary>
        public Func<Vector3, Vector3, bool> Sees = (_, _) => true;
        /// <summary>True when the place is dangerous at this moment for a reason the terrain does not show.</summary>
        public Func<Vector3, bool> Danger = _ => false;
        /// <summary>A number in [0, 1).</summary>
        public Func<float> Random = () => 0;
    }
    public const float OccupiedRadius = 1.2f, EyeHeight = 1.6f;
    /// <summary>A point used less than this long ago is "recent".</summary>
    public const double RecentSeconds = 6;

    public enum Grade { Hidden = 0, Unseen = 1, Seen = 2 }
    public readonly record struct Candidate(DmSpawnPoint Point, Grade Grade, float Nearest, bool Recent);

    /// <summary>Whether this point may be used at all right now (the hard filter).</summary>
    public static bool Acceptable(DmSpawnPoint point, Context c) =>
        c.Arena.Contains(point.Position) && DmArenaRules.Standable(c.World, point.Position) is null && !c.Reserved.Contains(point.Id)
        && !c.Occupied.Any(o => Vector3.Distance(o, point.Position) < OccupiedRadius) && !c.Danger(point.Position);

    public static List<Candidate> Grade_(Context c) {
        var list = new List<Candidate>();
        foreach (var point in c.Arena.Spawns) {
            if (!Acceptable(point, c)) continue;
            Vector3 eye = point.Position + new Vector3(0, EyeHeight, 0);
            float nearest = float.PositiveInfinity; bool seen = false;
            foreach (var enemy in c.Enemies) {
                nearest = Math.Min(nearest, Vector3.Distance(enemy.Feet, point.Position));
                if (!seen && c.Sees(enemy.Eye, eye)) seen = true;
            }
            var grade = seen ? Grade.Seen : nearest >= DmFixed.SpawnDistance ? Grade.Hidden : Grade.Unseen;
            bool recent = c.LastUsed.TryGetValue(point.Id, out double at) && c.Now - at < RecentSeconds;
            list.Add(new(point, grade, nearest, recent));
        }
        return list;
    }

    /// <summary>The point for a life that is to begin now, or null when no candidate passes the hard filter.</summary>
    public static DmSpawnPoint Choose(Context c) {
        var candidates = Grade_(c);
        if (candidates.Count == 0) return null;
        var best = candidates.Min(x => x.Grade);
        var pool = candidates.Where(x => x.Grade == best).ToList();
        if (pool.Any(x => !x.Recent)) pool = pool.Where(x => !x.Recent).ToList();
        if (best == Grade.Seen) {
            // every usable point is in some enemy's sight: the nearest enemy decides, and only the farthest few are drawn from
            float far = pool.Max(x => x.Nearest);
            pool = pool.Where(x => x.Nearest >= far - 2f).ToList();
        }
        pool.Sort((a, b) => a.Point.Id.CompareTo(b.Point.Id));
        int index = Math.Clamp((int)(c.Random() * pool.Count), 0, pool.Count - 1);
        return pool[index].Point;
    }
}
