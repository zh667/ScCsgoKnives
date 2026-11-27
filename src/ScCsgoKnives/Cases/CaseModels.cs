using System.Text.Json.Serialization;

// Merged from the unpublished standalone ScCsgoBox 0.3.5 (zh667/ScCsgoBox 6af872a, MIT, based on Reclizer's CsgoBox) in
// 1.6.0 (feedback-1124-170 D16-D21): class names, block names, the subsystem template and the Assets/ScCsgoBox layout are kept.

namespace Game;

[JsonConverter(typeof(JsonStringEnumConverter<CaseRarity>))]
public enum CaseRarity {
    Common,
    Uncommon,
    Rare,
    Epic,
    Legendary
}

public sealed class RarityWeights {
    public double Common { get; set; } = 70;
    public double Uncommon { get; set; } = 20;
    public double Rare { get; set; } = 7;
    public double Epic { get; set; } = 2.5;
    public double Legendary { get; set; } = 0.5;

    public double Get(CaseRarity rarity) => rarity switch {
        CaseRarity.Common => Common,
        CaseRarity.Uncommon => Uncommon,
        CaseRarity.Rare => Rare,
        CaseRarity.Epic => Epic,
        CaseRarity.Legendary => Legendary,
        _ => 0
    };
}

public sealed class RewardDefinition {
    public CaseRarity Rarity { get; set; }
    public string OwnerMod { get; set; } = string.Empty;
    public string Block { get; set; } = string.Empty;
    public int Data { get; set; }
    public bool ClearData { get; set; }
    public int Count { get; set; } = 1;
    public double Weight { get; set; } = 1;

    [JsonIgnore]
    public int Value { get; set; }

    /// <summary>1.6.0 (feedback-1124-170 D17): a CS gun (also a painted or counter template) is only drawn for a player
    /// whose level could craft it. 0: anyone.</summary>
    [JsonIgnore]
    public int MinPlayerLevel { get; set; }

    /// <summary>Supplied by code (CsCaseRewards), not by a case or integration file.</summary>
    [JsonIgnore]
    public bool Generated { get; set; }

    public int GetItemValue() => ClearData ? Terrain.ReplaceData(Value, 0) : Value;

    public string GetDisplayName() {
        int value = GetItemValue();
        return BlocksManager.Blocks[Terrain.ExtractContents(value)].GetDisplayName(null, value);
    }
}

public sealed class CaseIntegrationDefinition {
    public string Id { get; set; } = string.Empty;
    public string TargetCase { get; set; } = string.Empty;
    public List<string> RequiresMods { get; set; } = [];
    public List<RewardDefinition> Rewards { get; set; } = [];
}

public sealed class CaseDefinition {
    public string Id { get; set; } = string.Empty;
    public int Data { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string KeyBlock { get; set; } = "ScCaseKeyBlock";
    public double CreatureDropChance { get; set; }
    public RarityWeights Weights { get; set; } = new();
    public List<RewardDefinition> Rewards { get; set; } = [];

    [JsonIgnore]
    public int KeyBlockIndex { get; set; }

    public string GetDisplayName() => LanguageControl.TryGet(out string localized, "ScCsgoBox", "Cases", Id, "DisplayName")
        ? localized
        : DisplayName;
}

public sealed record CaseRoll(RewardDefinition Reward, IReadOnlyList<RewardDefinition> Reel, int WinningIndex);
