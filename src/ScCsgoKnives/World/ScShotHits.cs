using Engine;
namespace Game;

/// <summary>One trigger pull's hits on one target (headshot-armor-balance-20260929 H1): the power that reached each
/// region, pellet by pellet, with the first pellet's point and direction. A shotgun's mixed head and chest pellets
/// stay apart here instead of turning the whole shot into a headshot; damage is still delivered once per target
/// (third-party per-attack hooks see one attack), and armour, sounds and statistics read the regions.
/// Head power already carries the head multiplier of whoever fired. Neck and Stomach are kept apart only for the world's
/// mode that reports them (CS2's hit groups, deathmatch round 5); nothing else ever adds to them.</summary>
public sealed class ScShotHits {
    public float Head, Body, Arm, Leg, Neck, Stomach;
    public int Pellets, HeadPellets;
    /// <summary>The most obstacles a pellet crossed before reaching this target (a world mode's penetration; 0 without).</summary>
    public int Crossed;
    public Vector3 Point, Direction;
    /// <summary>Where the first head pellet hit, and its direction (current-direction-20260929: the helmet spark belongs
    /// on the head even when the shot's first pellet struck the chest). Meaningful only with <see cref="AnyHead"/>.</summary>
    public Vector3 HeadPoint, HeadDirection;
    public ScHitPart First = ScHitPart.Unknown;
    public float Total => Head + Body + Arm + Leg + Neck + Stomach;
    /// <summary>The regions that received power, with that power.</summary>
    public IEnumerable<(ScHitPart Part, float Power)> Regions() {
        if (Head > 0) yield return (ScHitPart.Head, Head);
        if (Body > 0) yield return (ScHitPart.Body, Body);
        if (Arm > 0) yield return (ScHitPart.Arm, Arm);
        if (Leg > 0) yield return (ScHitPart.Leg, Leg);
        if (Neck > 0) yield return (ScHitPart.Neck, Neck);
        if (Stomach > 0) yield return (ScHitPart.Stomach, Stomach);
    }
    public bool AnyHead => HeadPellets > 0;
    public void Add(ScHitPart part, float power, Vector3 point, Vector3 direction) {
        if (Pellets == 0) { Point = point; Direction = direction; First = part; }
        Pellets++;
        switch (part) {
            case ScHitPart.Head: if (HeadPellets == 0) { HeadPoint = point; HeadDirection = direction; } Head += power; HeadPellets++; break;
            case ScHitPart.Arm: Arm += power; break;
            case ScHitPart.Leg: Leg += power; break;
            case ScHitPart.Neck: Neck += power; break;
            case ScHitPart.Stomach: Stomach += power; break;
            default: Body += power; break;
        }
    }
    public override string ToString() => $"head {Head:0.###} ({HeadPellets}) body {Body:0.###} arm {Arm:0.###} leg {Leg:0.###}{(Neck + Stomach > 0 ? $" neck {Neck:0.###} stomach {Stomach:0.###}" : "")} of {Pellets} pellet(s), first {First}";
}
