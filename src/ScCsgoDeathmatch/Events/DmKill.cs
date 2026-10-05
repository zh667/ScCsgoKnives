using System.Text.Json.Serialization;
namespace Game;

public enum DmDeathCause { Kill, Suicide, Environment, OutOfBounds }

/// <summary>How a life ended (DM-09, design §11): made once by the authority from what it settled itself, and the one
/// source of the score, the feed and the competitive counters. Identity: (MatchId, VictimKey, VictimLife) - a life ends
/// once. Each "how" flag is a fact of the attack that ended it, frozen when that attack was made; a flag that could not
/// be established is false and nothing is shown for it.</summary>
public sealed record DmKill {
    public int MatchId { get; init; }
    /// <summary>The event's number in its match, from 1 (clients apply each number once).</summary>
    public int Sequence { get; init; }
    public string VictimKey { get; init; }
    public string VictimName { get; init; } = "";
    public int VictimLife { get; init; }
    /// <summary>Null: nobody is credited (the environment, leaving the arena, the victim's own throwable).</summary>
    public string KillerKey { get; init; }
    public string KillerName { get; init; } = "";
    public int KillerLife { get; init; }
    public DmDeathCause Cause { get; init; }
    /// <summary>The weapon's asset name ("ak47", "knife_karambit", "grenade_hegrenade"); "" when there is none.</summary>
    public string Weapon { get; init; } = "";
    /// <summary>The gun model (GunSpec index) the kill counts for on the killer's counter; -1 when it was not a gun.</summary>
    public int GunVariant { get; init; } = -1;
    public bool Headshot { get; init; }
    public bool NoScope { get; init; }
    public bool ThroughSmoke { get; init; }
    public bool Penetration { get; init; }
    public bool AttackerBlind { get; init; }
    public IReadOnlyList<string> Assists { get; init; } = [];
    /// <summary>Seconds since the match began.</summary>
    public double At { get; init; }
    /// <summary>The kill gave the killer a point.</summary>
    [JsonIgnore] public bool Scored => Cause == DmDeathCause.Kill && KillerKey is not null;
}

/// <summary>One line of the scoreboard.</summary>
public sealed record DmScore(string Key, string Name, int Kills, int Deaths, int Assists, bool Connected, bool Playing) {
    /// <summary>The place by kills alone: equal kills share a place (DM-06: ties stand, no tie-break is invented).</summary>
    public int Place { get; init; }
}

/// <summary>The result of a finished or stopped match, kept in the world until the next one ends.</summary>
public sealed record DmResult {
    public int MatchId { get; init; }
    /// <summary>"time" (ran its length), "stopped" (the host ended it), "empty" (everyone left), "interrupted" (the world closed).</summary>
    public string Reason { get; init; } = "";
    /// <summary>False for a practice: fewer than two players ever took part.</summary>
    public bool Formal { get; init; }
    public double Seconds { get; init; }
    public IReadOnlyList<DmScore> Scores { get; init; } = [];
}

public static class DmScores {
    /// <summary>The board's order: kills, then fewer deaths, then the stable key - an order to draw in, while the place is
    /// by kills only.</summary>
    public static List<DmScore> Rank(IEnumerable<DmScore> rows) {
        var ordered = rows.OrderByDescending(r => r.Kills).ThenBy(r => r.Deaths).ThenBy(r => r.Key, StringComparer.Ordinal).ToList();
        for (int i = 0; i < ordered.Count; i++)
            ordered[i] = ordered[i] with { Place = i > 0 && ordered[i - 1].Kills == ordered[i].Kills ? ordered[i - 1].Place : i + 1 };
        return ordered;
    }
}
