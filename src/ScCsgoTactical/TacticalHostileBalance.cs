using Engine;
namespace Game;

/// <summary>Hostile NPC gunfire against players (headshot-armor-balance-20260929 H2, experimental first table). Enemy
/// guns used the players' survival budget (an AK 15, an AWP 57 against creatures), which against a player's attack
/// resilience (10 x 1.25 in survival, x0.8 for a female body) took 120-150% of a fresh player's health per rifle
/// bullet. Players get this whole-shot budget at close range instead; the gun's own distance falloff still applies and
/// a shotgun's budget is shared by its pellets, never multiplied by them. Head pellets on a player count x1.25 (enemies
/// do not aim for heads). Companions, hostages and creatures keep the old survival scale. Identity comes from the
/// shooter's hostile component, never from a T appearance.</summary>
public static class TacticalHostileBalance {
    public const float PlayerHeadMultiplier = 1.25f;
    /// <summary>Close-range whole-shot budget against a player, by gun class; null for a gun outside the table (the
    /// caller then keeps the survival budget and logs it once).</summary>
    public static float? PlayerShot(string gun) => gun switch {
        "glock18" or "hkp2000" or "p250" or "usp_silencer" or "fiveseven" or "tec9" or "cz75a" or "elite" => 1.8f,
        "deagle" or "revolver" => 3.0f,
        "mac10" or "mp9" or "mp7" or "ump45" or "mp5sd" or "p90" or "bizon" => 1.4f,
        "ak47" or "m4a4" or "m4a1s" or "aug" or "sg556" or "galilar" or "famas" => 2.2f,
        "m249" or "negev" => 1.8f,
        "scar20" or "g3sg1" => 3.0f,
        "ssg08" => 4.0f,
        "awp" => 5.5f,
        "nova" or "sawedoff" or "mag7" => 4.2f,
        "xm1014" => 3.2f,
        _ => null
    };
}

/// <summary>One NPC bullet or pellet (H1): terrain first, then the nearest creature part along the ray through the same
/// hit test the player's guns use (skinned CT/T joints, players' logical regions, vanilla meshes, body fallback).
/// Only bodies near the ray are examined.</summary>
public static class TacticalGunfire {
    /// <summary>Half-angle of an NPC shotgun's pellet cone in radians (估计: about CS2's close-range spread).</summary>
    public const float PelletCone = .05f;
    public readonly record struct Hit(ComponentBody Body, float Distance, ScHitPart Part);
    [ThreadStatic] static DynamicArray<ComponentBody> s_near;
    public static Hit? Trace(GameEntitySystem.Project project, ComponentBody shooter, Vector3 from, Vector3 direction, float range) {
        var terrain = project.FindSubsystem<SubsystemTerrain>(true); var bodies = project.FindSubsystem<SubsystemBodies>(true);
        var wall = terrain.Raycast(from, from + direction * range, false, true, (v, d) => ScGunRange.TerrainStopsBullet(v));
        float limit = wall.HasValue ? MathF.BitDecrement(wall.Value.Distance) : range;
        Vector3 end = from + direction * limit;
        var near = s_near ??= new DynamicArray<ComponentBody>(); near.Clear();
        bodies.FindBodiesInArea(new Vector2(Math.Min(from.X, end.X) - 2, Math.Min(from.Z, end.Z) - 2), new Vector2(Math.Max(from.X, end.X) + 2, Math.Max(from.Z, end.Z) + 2), near);
        var hit = ScGunHitTest.Raycast(near, shooter, from, direction, limit);
        return hit is { } h ? new Hit(h.Body, h.Distance, h.Part == ScHitPart.Unknown ? ScHitPart.Body : h.Part) : null;
    }
    public static Vector3 Scatter(Vector3 direction, float cone, Engine.Random random) {
        var side = Vector3.Cross(direction, Math.Abs(direction.Y) < .95f ? Vector3.UnitY : Vector3.UnitX); side = Vector3.Normalize(side);
        var up = Vector3.Cross(side, direction);
        float angle = random.Float(0, MathF.PI * 2), radius = cone * MathF.Sqrt(random.Float(0, 1));
        return Vector3.Normalize(direction + side * (MathF.Cos(angle) * radius) + up * (MathF.Sin(angle) * radius));
    }
}
