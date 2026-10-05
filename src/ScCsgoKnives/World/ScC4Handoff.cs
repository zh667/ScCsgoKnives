using Engine;
namespace Game;

/// <summary>The moment a held C4 becomes a planted charge, as drawn (r2-c4-completion-20260929). CS2's planting clip never
/// rests the bomb on the ground: its socket bottoms out at ankle height and the brick stays about 0.17 m up, a little in
/// front of the feet, while the charge appears at its gameplay spot on the ground at the commit. Third-person drawing
/// records where a bomb being planted was last drawn; a charge planted at that spot starts there and settles onto its own
/// pose within <see cref="SettleSeconds"/>, so it never pops. Presentation only: the charge's position, fuse, defuse target
/// and saves are never moved. Without a recorded hold (first person, a loaded world, a camera arriving late) the charge is
/// simply drawn at its spot.</summary>
public static class ScC4Handoff {
    public const float SettleSeconds = .2f, SpotRadius = .6f;
    readonly record struct Held(Vector3 Spot, Vector3 Centre, double At);
    static readonly List<Held> s_held = [];
    static Vector3? s_plantedCentre;
    /// <summary>A bomb being planted toward <paramref name="spot"/> was drawn with its centre at <paramref name="centre"/>.</summary>
    public static void Holding(Vector3 spot, Vector3 centre, double now) {
        if (!float.IsFinite(centre.X + centre.Y + centre.Z + spot.X + spot.Z)) return;
        lock (s_held) {
            s_held.RemoveAll(h => now - h.At > 1 || now < h.At || Vector2.DistanceSquared(h.Spot.XZ, spot.XZ) < SpotRadius * SpotRadius);
            if (s_held.Count < 16) s_held.Add(new(spot, centre, now));
        }
    }
    static Vector3 PlantedCentre() {
        if (s_plantedCentre is { } known) return known;
        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        foreach (var group in ScC4Visuals.WorldGroups(true)) foreach (var v in group.Mesh.Vertices) { lo = Vector3.Min(lo, v.Position); hi = Vector3.Max(hi, v.Position); }
        return (s_plantedCentre = lo.X <= hi.X ? (lo + hi) * .5f : Vector3.Zero).Value;
    }
    /// <summary>Drawing matrix of a charge planted <paramref name="age"/> seconds ago whose own drawing matrix is
    /// <paramref name="planted"/>: from the held bomb's last drawn centre, eased onto the planted pose.</summary>
    public static Matrix Settled(Matrix planted, float age, double now) {
        if (!(age >= 0 && age < SettleSeconds)) return planted;
        Held? found = null;
        lock (s_held) foreach (var h in s_held)
            if (Vector2.DistanceSquared(h.Spot.XZ, planted.Translation.XZ) < SpotRadius * SpotRadius && now - h.At <= age + .3f && h.At <= now) found = h;
        if (found is not { } held) return planted;
        float t = age / SettleSeconds, ease = t * t * (3 - 2 * t);
        var m = planted; m.Translation += (held.Centre - Vector3.Transform(PlantedCentre(), planted)) * (1 - ease);
        return m;
    }
}
