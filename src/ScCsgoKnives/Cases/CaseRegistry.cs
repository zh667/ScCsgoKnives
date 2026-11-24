using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Engine;

namespace Game;

public static class CaseRegistry {
    static readonly Dictionary<int, CaseDefinition> s_byData = [];
    static bool s_loaded;

    public static IReadOnlyCollection<CaseDefinition> All {
        get { EnsureLoaded(); return s_byData.Values; }
    }

    public static void EnsureLoaded() {
        if (!s_loaded) Reload();
    }

    public static void Reload() {
        s_loaded = true;
        s_byData.Clear();
        JsonSerializerOptions options = new() {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        foreach (ContentInfo content in ContentManager.List("ScCsgoBox/Cases")) {
            if (!string.Equals(content.ContentSuffix, ".json", StringComparison.OrdinalIgnoreCase)) continue;
            try {
                Stream stream = content.Duplicate();
                stream.Position = 0;
                using StreamReader reader = new(stream, leaveOpen: true);
                CaseDefinition definition = JsonSerializer.Deserialize<CaseDefinition>(reader.ReadToEnd(), options);
                ValidateAndAdd(definition, content.AbsolutePath);
            }
            catch (Exception ex) {
                Log.Error($"[ScCsgoBox] Failed to load case '{content.AbsolutePath}': {ex}");
            }
        }

        if (s_byData.Count == 0) {
            Log.Warning("[ScCsgoBox] No valid case configuration was loaded; installing built-in fallback.");
            // 1.6.0: a fallback whose rewards cannot resolve either leaves no case at all, never an exception out of the
            // creative catalogue or a block's display name.
            try { ValidateAndAdd(CreateFallback(), "built-in fallback"); }
            catch (Exception ex) { Log.Warning($"[ScCsgoBox] No case available: {ex.Message}"); }
        }
        int integrationRewards = LoadIntegrations(options);
        // 1.6.0: the CS weapons' own rewards (components, ammunition, throwables, painted and counter guns, guns), by tier.
        try { integrationRewards += CsCaseRewards.Append(s_byData.Values); }
        catch (Exception ex) { Log.Error($"[ScCsgoBox] CS case rewards unavailable: {ex}"); }
        Log.Information($"[ScCsgoBox] Loaded {s_byData.Count} case definition(s) with {integrationRewards} integration reward(s).");
    }

    public static CaseDefinition GetByData(int data) {
        EnsureLoaded();
        return s_byData.TryGetValue(data, out CaseDefinition definition) ? definition : null;
    }

    static void ValidateAndAdd(CaseDefinition definition, string source) {
        if (definition is null) throw new InvalidOperationException("Empty case definition.");
        if (definition.Data is < 0 or > 15) throw new InvalidOperationException("Case data must be in range 0..15.");
        if (s_byData.ContainsKey(definition.Data)) throw new InvalidOperationException($"Duplicate case data {definition.Data}.");
        if (string.IsNullOrWhiteSpace(definition.Id)) throw new InvalidOperationException("Case id is required.");
        if (string.IsNullOrWhiteSpace(definition.DisplayName)) definition.DisplayName = definition.Id;
        if (definition.CreatureDropChance is < 0 or > 1) throw new InvalidOperationException("Creature drop chance must be in range 0..1.");

        definition.KeyBlockIndex = BlocksManager.GetBlockIndex(definition.KeyBlock, true);
        definition.Rewards ??= [];
        definition.Rewards.RemoveAll(reward => !TryPrepareReward(reward, source));
        if (definition.Rewards.Count == 0) throw new InvalidOperationException("At least one valid reward is required.");

        double availableWeight = Enum.GetValues<CaseRarity>()
            .Where(r => definition.Rewards.Any(x => x.Rarity == r))
            .Sum(definition.Weights.Get);
        if (availableWeight <= 0) throw new InvalidOperationException("At least one populated rarity needs a positive weight.");
        s_byData.Add(definition.Data, definition);
    }

    static bool TryPrepareReward(RewardDefinition reward, string source) {
        try {
            if (reward is null || string.IsNullOrWhiteSpace(reward.Block)) return false;
            if (reward.Data is < 0 or > 1023 || reward.Count <= 0 || reward.Weight <= 0) return false;
            if (reward.ClearData) reward.Data = 0;
            int blockIndex = ResolveBlockIndex(reward);
            reward.Value = Terrain.MakeBlockValue(blockIndex, 0, reward.Data);
            return true;
        }
        catch (Exception ex) {
            Log.Warning($"[ScCsgoBox] Ignoring invalid reward in '{source}': {ex.Message}");
            return false;
        }
    }

    static int LoadIntegrations(JsonSerializerOptions options) {
        int addedRewards = 0;
        foreach (ContentInfo content in ContentManager.List("ScCsgoBox/Integrations")) {
            if (!string.Equals(content.ContentSuffix, ".json", StringComparison.OrdinalIgnoreCase)) continue;
            try {
                Stream stream = content.Duplicate();
                stream.Position = 0;
                using StreamReader reader = new(stream, leaveOpen: true);
                CaseIntegrationDefinition integration = JsonSerializer.Deserialize<CaseIntegrationDefinition>(reader.ReadToEnd(), options);
                addedRewards += ApplyIntegration(integration, content.AbsolutePath);
            }
            catch (Exception ex) {
                Log.Error($"[ScCsgoBox] Failed to load integration '{content.AbsolutePath}': {ex}");
            }
        }
        return addedRewards;
    }

    static int ApplyIntegration(CaseIntegrationDefinition integration, string source) {
        if (integration is null) throw new InvalidOperationException("Empty integration definition.");
        if (string.IsNullOrWhiteSpace(integration.Id)) throw new InvalidOperationException("Integration id is required.");
        if (string.IsNullOrWhiteSpace(integration.TargetCase)) throw new InvalidOperationException("Integration targetCase is required.");

        string[] missingMods = (integration.RequiresMods ?? [])
            .Where(packageName => !IsModLoaded(packageName))
            .ToArray();
        if (missingMods.Length > 0) {
            Log.Information($"[ScCsgoBox] Integration '{integration.Id}' inactive; missing mod(s): {string.Join(", ", missingMods)}.");
            return 0;
        }

        CaseDefinition target = s_byData.Values.FirstOrDefault(definition =>
            string.Equals(definition.Id, integration.TargetCase, StringComparison.OrdinalIgnoreCase));
        if (target is null) throw new InvalidOperationException($"Target case '{integration.TargetCase}' was not found.");

        int added = 0;
        foreach (RewardDefinition reward in integration.Rewards ?? []) {
            if (!TryPrepareReward(reward, source)) continue;
            bool duplicate = target.Rewards.Any(existing =>
                existing.Value == reward.Value &&
                existing.Count == reward.Count &&
                existing.Rarity == reward.Rarity);
            if (duplicate) continue;
            target.Rewards.Add(reward);
            added++;
        }
        Log.Information($"[ScCsgoBox] Integration '{integration.Id}' active; added {added} reward(s) to '{target.Id}'.");
        return added;
    }

    static bool IsModLoaded(string packageName) =>
        !string.IsNullOrWhiteSpace(packageName) &&
        ModsManager.ModList.Any(mod => string.Equals(mod.modInfo?.PackageName, packageName, StringComparison.OrdinalIgnoreCase));

    static int ResolveBlockIndex(RewardDefinition reward) {
        if (string.IsNullOrWhiteSpace(reward.OwnerMod))
            return BlocksManager.GetBlockIndex(reward.Block, true);

        BlockAllocateData allocation = BlocksManager.BlocksAllocateData.FirstOrDefault(candidate =>
            candidate.Allocated &&
            string.Equals(candidate.Block?.GetType().Name, reward.Block, StringComparison.Ordinal) &&
            string.Equals(candidate.ModEntity?.modInfo?.PackageName, reward.OwnerMod, StringComparison.OrdinalIgnoreCase));
        if (allocation?.Block is null)
            throw new KeyNotFoundException($"Block <{reward.OwnerMod}:{reward.Block}> is not found.");
        int blockIndex = allocation.Block.BlockIndex;
        if (blockIndex < 0 || blockIndex >= BlocksManager.Blocks.Length)
            throw new InvalidOperationException($"Block <{reward.OwnerMod}:{reward.Block}> has invalid index {blockIndex}.");
        return blockIndex;
    }

    static CaseDefinition CreateFallback() => new() {
        Id = "fallback",
        Data = 0,
        DisplayName = "Survivor Case",
        CreatureDropChance = 0.06,
        Rewards = [
            new RewardDefinition { Rarity = CaseRarity.Common, Block = "CoalChunkBlock", Count = 4 },
            new RewardDefinition { Rarity = CaseRarity.Rare, Block = "IronMacheteBlock" },
            new RewardDefinition { Rarity = CaseRarity.Legendary, Block = "MusketBlock" }
        ]
    };
}
