using System.Security.Cryptography;

namespace Game;

public static class CaseRoller {
    public const int ReelLength = 50;
    public const int MinimumWinningIndex = 35;
    public const int MaximumWinningIndex = 44;

    public static CaseRoll Roll(CaseDefinition definition) => RollFor(definition, _ => true, new System.Random(RandomNumberGenerator.GetInt32(int.MaxValue)));

    /// <summary>A roll among the rewards <paramref name="eligible"/> allows (1.7.0: guns by the opener's level). The winner and
    /// the reel come from the same eligible pool.</summary>
    public static CaseRoll RollFor(CaseDefinition definition, Func<RewardDefinition, bool> eligible, System.Random random) {
        RewardDefinition winner = PickReward(definition, eligible, random);
        int winningIndex = random.Next(MinimumWinningIndex, MaximumWinningIndex + 1);
        List<RewardDefinition> reel = new(ReelLength);
        for (int i = 0; i < ReelLength; i++) reel.Add(PickReward(definition, eligible, random));
        reel[winningIndex] = winner;
        return new CaseRoll(winner, reel, winningIndex);
    }

    /// <summary>Each rarity's chance with the given rewards allowed. A rarity whose rewards are all filtered out passes its
    /// weight down to the next lower rarity that still has some (1.7.0, D17), so a roll is never empty and the higher
    /// rarities never become more likely.</summary>
    public static Dictionary<CaseRarity, double> EffectiveWeights(CaseDefinition definition, Func<RewardDefinition, bool> eligible) {
        var result = new Dictionary<CaseRarity, double>();
        double carry = 0;
        foreach (CaseRarity rarity in Enum.GetValues<CaseRarity>().Reverse()) {
            double weight = Math.Max(0, definition.Weights.Get(rarity)) + carry;
            if (definition.Rewards.Any(x => x.Rarity == rarity && eligible(x))) { if (weight > 0) result[rarity] = weight; carry = 0; }
            else carry = weight;
        }
        // Nothing below took the carried weight: give it to the lowest populated rarity.
        if (carry > 0 && result.Count > 0) { var lowest = result.Keys.Min(); result[lowest] += carry; }
        return result;
    }

    static RewardDefinition PickReward(CaseDefinition definition, Func<RewardDefinition, bool> eligible, System.Random random) {
        var weights = EffectiveWeights(definition, eligible);
        CaseRarity[] available = weights.Keys.OrderBy(r => r).ToArray();
        if (available.Length == 0) throw new InvalidOperationException("No reward of this case is available.");
        double rarityTotal = available.Sum(r => weights[r]);
        double rarityRoll = random.NextDouble() * rarityTotal;
        CaseRarity rarity = available[^1];
        foreach (CaseRarity candidate in available) {
            rarityRoll -= weights[candidate];
            if (rarityRoll <= 0) { rarity = candidate; break; }
        }

        RewardDefinition[] rewards = definition.Rewards.Where(x => x.Rarity == rarity && eligible(x)).ToArray();
        double rewardRoll = random.NextDouble() * rewards.Sum(x => x.Weight);
        foreach (RewardDefinition reward in rewards) {
            rewardRoll -= reward.Weight;
            if (rewardRoll <= 0) return reward;
        }
        return rewards[^1];
    }
}
