using System.Text.Json;
using System.Text.Json.Serialization;
namespace Game;

/// <summary>Ids of the deathmatch package (deathmatch-addon).</summary>
public static class DmIds {
    public const string Package = "zh667.ScCsgoDeathmatch";
    /// <summary>The mode's id in a world's marker (ScWorldModes) and in the handshake.</summary>
    public const string Mode = "zh667.ScCsgoDeathmatch/deathmatch";
    public const string ModeName = "死亡竞赛";
    public const string Subsystem = "ScDeathmatch";
    /// <summary>The persisted layout of the package's world data. A newer one is kept as saved and not run.</summary>
    public const int Schema = 1;
}

/// <summary>What the host sets for the matches of one arena world. The values the user fixed are not here but in
/// <see cref="DmFixed"/>: they are not the host's to change.</summary>
public sealed record DmRules {
    /// <summary>Length of a match in minutes (host; default 10, 1-60).</summary>
    public int Minutes { get; init; } = 10;
    /// <summary>Throwables may be put in a loadout (host; default off).</summary>
    public bool Grenades { get; init; }
    public const int MinMinutes = 1, MaxMinutes = 60;
    public DmRules Normalize() => this with { Minutes = Math.Clamp(Minutes, MinMinutes, MaxMinutes) };
    [JsonIgnore] public bool Valid => Minutes is >= MinMinutes and <= MaxMinutes;
    static readonly JsonSerializerOptions s_json = new() { WriteIndented = false };
    public string Encode() => JsonSerializer.Serialize(this, s_json);
    public static bool TryDecode(string text, out DmRules rules) {
        rules = new();
        if (string.IsNullOrWhiteSpace(text)) return true;
        try { rules = JsonSerializer.Deserialize<DmRules>(text, s_json) ?? new(); return rules.Valid; }
        catch (JsonException) { rules = new(); return false; }
    }
}

/// <summary>The values of this version's deathmatch that are not settings. "User" marks what the user fixed (DM-05, DM-06,
/// DM-08); the others are engineering defaults of the design (§2), none of them claimed to be CS2's.</summary>
public static class DmFixed {
    /// <summary>User: every life starts with 100 health, 100 armour and a helmet.</summary>
    public const int Health = 100, Armour = 100;
    /// <summary>User: protection lasts 3 s from the moment the life can act; an attack ends it first.</summary>
    public const double ProtectionSeconds = 3;
    /// <summary>Default: the death view before the next life is prepared.</summary>
    public const double DeathViewSeconds = 2;
    /// <summary>Default: seconds between the host's start and the first lives.</summary>
    public const double CountdownSeconds = 5;
    /// <summary>Default: how long the results stay before the lobby returns.</summary>
    public const double ResultsSeconds = 12;
    /// <summary>Default: a client that does not confirm a prepared spawn within this becomes a spectator.</summary>
    public const double ReadyTimeoutSeconds = 10;
    /// <summary>Default: outside the arena for this long counts as one environment death.</summary>
    public const double OutOfBoundsSeconds = 3;
    /// <summary>Default assist rule: at least this much health damage to that life within this many seconds before its end.</summary>
    public const int AssistDamage = 20; public const double AssistSeconds = 10;
    /// <summary>Default per-life throwables when the host allows them: at most 3 in all, 2 flashbangs, 1 of each other kind.</summary>
    public const int GrenadesPerLife = 3, FlashPerLife = 2, OtherGrenadeEach = 1;
    /// <summary>Design limit of players in one match (the platform's own limit applies as well).</summary>
    public const int MaxPlayers = 16;
    /// <summary>Default first choice of distance between a spawn and the nearest enemy, in blocks (relaxed when no point has it).</summary>
    public const float SpawnDistance = 12;
}
