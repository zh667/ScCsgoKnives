using System.Runtime.CompilerServices;
using Engine;

namespace Game;

/// <summary>How survivor cases are obtained (feedback-1124-170 D18; the standalone ScCsgoBox's rules with the 1.6.0 rates).
/// Outside creative mode and only where the world is decided (single player, or the multiplayer server):
/// <list type="bullet">
/// <item>a player's kill of an enemy T: 15 %; of another creature: the case file's own chance (3 %);</item>
/// <item>each new highest level a player reaches: 1 case for levels 2-10, 2 for 11-20, 3 from 21 (user decision 2026-11-25),
/// recorded per player and world (SubsystemScFirstCraft's ledger, shared by the world tree) so no level is ever rewarded
/// twice; the first time a world is seen the player's current level is recorded without a reward;</item>
/// <item>digging next to a grave's treasure marker: 1 per marker; digging a natural cairn: 1-2.</item>
/// </list>
/// Airdrops, defused enemy bombs and first-crafted guns grant theirs through their own transactions.</summary>
public static class CaseAcquisition {
    public const double HostileDropChance = .15;
    /// <summary>Cases for reaching one level.</summary>
    public static int CasesForLevel(int level) => level <= 1 ? 0 : level <= 10 ? 1 : level <= 20 ? 2 : 3;
    /// <summary>Cases for going from <paramref name="from"/> (already rewarded) to <paramref name="to"/>.</summary>
    public static int CasesForLevels(int from, int to) { int n = 0; for (int l = Math.Max(from, 1) + 1; l <= to; l++) n += CasesForLevel(l); return n; }
    /// <summary>Whether a dying creature is an enemy T; the agents package sets it (false without it).</summary>
    public static Func<ComponentHealth, bool> IsHostileAgent;
    /// <summary>Headless fixture: the random source; never assigned by gameplay.</summary>
    internal static Func<double> RollOverride;
    static readonly System.Random s_random = new();
    static readonly Point3[] s_neighbors = [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)];

    static double Roll() => RollOverride?.Invoke() ?? s_random.NextDouble();
    static bool Creative(GameEntitySystem.Project project) => project?.FindSubsystem<SubsystemGameInfo>(false)?.WorldSettings.GameMode == GameMode.Creative;
    public static CaseDefinition Survivor => CaseRegistry.All.OrderBy(c => c.Data).FirstOrDefault();
    public static int CaseValue(CaseDefinition definition = null) => Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScCaseBlock>(true), 0, (definition ?? Survivor)?.Data ?? 0);

    /// <summary>The chance of a case for a player's kill: an enemy T's or the case's ordinary creature chance.</summary>
    public static double DropChance(bool hostile, CaseDefinition definition) => hostile ? HostileDropChance : definition?.CreatureDropChance ?? 0;

    public static void CreatureDied(ComponentHealth health, Injury injury) {
        if (injury?.AttackerPlayer is null || health?.Entity is null || health.Entity.FindComponent<ComponentPlayer>() is not null) return;
        if (!ScNet.IsAuthority || Creative(health.Project)) return;
        var definition = Survivor; if (definition is null) return;
        bool hostile = IsHostileAgent?.Invoke(health) == true;
        if (Roll() >= DropChance(hostile, definition)) return;
        var body = health.Entity.FindComponent<ComponentBody>(true);
        health.Project.FindSubsystem<SubsystemPickables>(true).AddPickable(CaseValue(definition), 1, (body.BoundingBox.Min + body.BoundingBox.Max) / 2f, null, null, health.Entity);
    }

    /// <summary>The cases a player earns now: records <paramref name="current"/> as rewarded and returns how many cases go
    /// with it. A player seen for the first time is recorded without a reward (worlds from before this rule, D: no flood);
    /// a level at or below the record gives nothing (a level lost and regained).</summary>
    public static int LevelReward(ScFirstCraftLedger ledger, string owner, int current, bool creative) {
        if (ledger is null || string.IsNullOrEmpty(owner)) return 0;
        if (!ledger.TryGetRewardedLevel(owner, out int rewarded)) { ledger.RaiseRewardedLevel(owner, current); return 0; }
        if (current <= rewarded || !ledger.RaiseRewardedLevel(owner, current)) return 0;
        return creative ? 0 : CasesForLevels(rewarded, current);
    }
    public static void LevelUpdate(ComponentLevel level) {
        var player = level?.m_componentPlayer; if (player is null || !ScNet.IsAuthority) return;
        var ledger = player.Project?.FindSubsystem<SubsystemScFirstCraft>(false)?.Ledger; if (ledger is null) return;
        string owner = ScGunHolders.RecoveryOwner(player.Project, player.ComponentMiner?.Inventory);
        int count = LevelReward(ledger, owner, Math.Max(1, (int)Math.Floor(player.PlayerData.Level)), Creative(player.Project));
        if (count <= 0) return;
        Grant(player.ComponentMiner, count);
        Tell(player, "LevelReward", "升级奖励：获得荒野武器箱 ×{0}。", count);
    }

    public static void MinerDig(ComponentMiner miner, TerrainRaycastResult hit, float progress) {
        if (progress < 1f || miner?.ComponentPlayer is null || !ScNet.IsAuthority || Creative(miner.Project)) return;
        var terrain = miner.Project.FindSubsystem<SubsystemTerrain>(true);
        int marker = BlocksManager.GetBlockIndex<TreasureGeneratorBlock>(true), count = 0;
        foreach (var o in s_neighbors) if (terrain.Terrain.GetCellContents(hit.CellFace.X + o.X, hit.CellFace.Y + o.Y, hit.CellFace.Z + o.Z) == marker) count++;
        if (count <= 0) return;
        Grant(miner, count);
        Tell(miner.ComponentPlayer, "TreasureReward", "遗迹奖励：发现荒野武器箱 ×{0}。", count);
    }
    public static void BlockDug(ComponentMiner miner, int cellValue) {
        if (miner?.ComponentPlayer is null || !ScNet.IsAuthority || Creative(miner.Project)) return;
        if (Terrain.ExtractContents(cellValue) != BlocksManager.GetBlockIndex<CairnBlock>(true)) return;
        int count = Roll() < .5 ? 1 : 2;
        Grant(miner, count);
        Tell(miner.ComponentPlayer, "TreasureReward", "遗迹奖励：发现荒野武器箱 ×{0}。", count);
    }

    /// <summary>Into the inventory; what does not fit drops at the player's feet.</summary>
    public static void Grant(ComponentMiner miner, int count) {
        if (miner is null || count <= 0 || Survivor is null) return;
        int value = CaseValue();
        int remaining = ComponentInventoryBase.AcquireItems(miner.Inventory, value, count);
        if (remaining <= 0) return;
        miner.Project.FindSubsystem<SubsystemPickables>(true).AddPickable(value, remaining, miner.ComponentCreature.ComponentBody.Position + new Vector3(0, .5f, 0), null, null, miner.Entity);
    }
    static void Tell(ComponentPlayer player, string key, string fallback, int count) {
        string text = string.Format(LanguageControl.TryGet(out string t, "ScCsgoBox", "Message", key) ? t : fallback, count);
        if (ScNet.IsLocal(player)) player.ComponentGui.DisplaySmallMessage(text, Color.LightYellow, true, false); else ScNetFeedback.Tell(player, text, Color.LightYellow);
    }
}
