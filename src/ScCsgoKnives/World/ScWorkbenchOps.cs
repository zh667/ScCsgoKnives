using Engine;
using GameEntitySystem;
namespace Game;

/// <summary>The weapon workbench's commits for a player the server acts for (a remote multiplayer client). Each one repeats
/// what the workbench dialog does locally — the same quote functions on this player's own inventory and this world — and
/// refuses when the quote the player saw is no longer the one this state gives (item moved, record revision changed,
/// protection changed). Nothing is guessed or synthesized.</summary>
public static class ScWorkbenchOps {
    const float Reach = 6.5f;

    static bool Creative(Project project) => project.FindSubsystem<SubsystemGameInfo>(true).WorldSettings.GameMode == GameMode.Creative;

    /// <summary>The player is alive and next to a weapon workbench at <paramref name="bench"/>.</summary>
    public static bool AtBench(ComponentPlayer player, Point3 bench) {
        var terrain = player.Project.FindSubsystem<SubsystemTerrain>(true).Terrain;
        return player.ComponentHealth.Health > 0 && Vector3.Distance(player.ComponentBody.Position, new Vector3(bench)) < Reach
            && Terrain.ExtractContents(terrain.GetCellValue(bench.X, bench.Y, bench.Z)) == BlocksManager.GetBlockIndex<ScWeaponWorkbenchBlock>(true);
    }

    public static ScWorkbenchResult Execute(ComponentPlayer player, ScWorkbenchOp op) {
        if (!AtBench(player, op.Bench)) return new(-1, "你已离装配台太远、装配台已被移除，或角色已无法操作；未扣除材料。");
        var project = player.Project;
        var inventory = player.ComponentMiner.Inventory;
        bool creative = Creative(project);
        string holder = op.Slot >= 0 ? ScGunHolders.PlayerKey(player, op.Slot) : "";
        bool slotHolds = op.Slot >= 0 && op.Slot < inventory.SlotsCount && inventory.GetSlotValue(op.Slot) == op.Expected;
        switch (op.Kind) {
            case ScWorkbenchOpKind.Craft: {
                object item = SubsystemScWeaponWorkbench.MainMenuItems().FirstOrDefault(i => ScWorkbenchSelectionDialog.IsCraftable(i) && ScWorkbenchSelectionDialog.ValueOf(i) == op.Value);
                if (item is null || op.Quantity < 1 || op.Quantity > ScCraftBatch.Maximum) return new(0, "没有这个配方。");
                if (!creative && item is ScWorkbenchRecipe { CreativeOnly: true }) return new(0, "仅创造模式领取，无生存制作配方。");
                int level = ScWorkbenchSelectionDialog.LevelOf(item);
                if (!creative && CraftingRecipesManager.EnableLevelRestrictions && player.PlayerData.Level < level) return new(0, $"需要人物等级 {level}。");
                var cost = creative ? new Dictionary<int, int>() : ScWorkbenchSelectionDialog.UnitCostOf(item);
                int results = ScWorkbenchSelectionDialog.ResultCountOf(item, creative);
                return new(ScCraftBatch.TryCraftBatch(inventory, op.Value, cost, op.Quantity, results) ? 1 : 0);
            }
            case ScWorkbenchOpKind.Repair: {
                if (!slotHolds) return new((int)ScGunResult.StateChanged);
                var candidate = ScWeaponRepair.Candidates(inventory).FirstOrDefault(c => c.Slot == op.Slot && c.Value == op.Expected);
                if (candidate is null) return new((int)ScGunResult.StateChanged);
                var quote = ScWeaponRepair.Prepare(candidate, ScWeaponCrafting.Find(candidate.Value), creative, ScWeaponMaterialBlock.Value);
                if (quote is null || quote.Revision != op.Revision) return new((int)ScGunResult.StateChanged);
                return new((int)ScWeaponRepair.TryRepair(inventory, quote, holder));
            }
            case ScWorkbenchOpKind.GunSkin: {
                if (!slotHolds) return new((int)ScGunResult.StateChanged);
                var skin = op.Arg == 0 ? null : ScGunSkinCatalog.Find(op.Arg);
                if (op.Arg != 0 && skin is null) return new((int)ScGunResult.Invalid);
                var quote = ScWeaponSkinning.Prepare(inventory, op.Slot, skin, creative, ScWeaponMaterialBlock.Value);
                if (quote is null || quote.Revision != op.Revision) return new((int)ScGunResult.StateChanged);
                return new((int)ScWeaponSkinning.Apply(inventory, quote, holder));
            }
            case ScWorkbenchOpKind.KnifeSkin: {
                if (!slotHolds) return new(0);
                var knife = ScKnifeSkinning.Candidates(inventory).FirstOrDefault(c => c.Slot == op.Slot && c.Value == op.Expected);
                var quote = knife is null ? null : ScKnifeSkinning.Prepare(inventory, knife, op.Arg, creative);
                return new(quote is not null && ScKnifeSkinning.Apply(inventory, quote, creative) ? 1 : 0);
            }
            case ScWorkbenchOpKind.Counter: {
                if (!slotHolds) return new((int)ScGunResult.StateChanged);
                if (!creative && CraftingRecipesManager.EnableLevelRestrictions && player.PlayerData.Level < ScGunGrowth.InstallLevel) return new((int)ScGunResult.InsufficientMaterials, $"安装需要等级 {ScGunGrowth.InstallLevel}");
                var quote = ScGunCounter.Prepare(inventory, op.Slot, creative);
                if (quote is null || quote.Revision != op.Revision) return new((int)ScGunResult.StateChanged);
                return new((int)ScGunCounter.Apply(inventory, quote, holder));
            }
            case ScWorkbenchOpKind.CreativeLevel:
                if (!creative) return new((int)ScGunResult.Invalid);
                if (!slotHolds) return new((int)ScGunResult.StateChanged);
                return new((int)ScGunGrowthService.SetCreativeLevel(inventory, op.Slot, holder, op.Arg, project.FindSubsystem<SubsystemTime>(true).GameTime));
            case ScWorkbenchOpKind.GrowthMode: {
                var registry = ScGunRegistry.Current;
                if (registry is null || registry.GrowthMode != ScGunGrowthMode.Unset || !Enum.IsDefined((ScGunGrowthMode)op.Arg) || (ScGunGrowthMode)op.Arg == ScGunGrowthMode.Unset) return new(0);
                registry.GrowthMode = (ScGunGrowthMode)op.Arg;
                KnifeLog.Information("gun counter world rule set to " + registry.GrowthMode + " by player " + player.PlayerData.PlayerIndex);
                return new(1);
            }
            case ScWorkbenchOpKind.MaterializeHotbar:
                if (!creative) return new(0);
                SubsystemScWeaponWorkbench.MaterializeHotbar(player);
                return new(1);
            case ScWorkbenchOpKind.Armor: {
                var store = project.FindSubsystem<SubsystemScArmor>(false);
                var target = ScArmorWorkbench.TargetsFor(player).FirstOrDefault(t => t.Key == op.Target);
                if (store is null || target is null) return new(0, "对象已离开或不再属于你。");
                if (target.Check() is { } gone) return new(0, gone);
                if (!creative && CraftingRecipesManager.EnableLevelRestrictions && player.PlayerData.Level < ScArmorWorkbench.Level) return new(0, $"防护装备需要人物等级 {ScArmorWorkbench.Level}。");
                if (!Enum.IsDefined((ScArmorWorkbench.Operation)op.Arg)) return new(0);
                var quote = ScArmorWorkbench.Prepare(store, target.Key, (ScArmorWorkbench.Operation)op.Arg, creative);
                if (quote is null || quote.Before.Encode() != op.State) return new(0, "防护状态已改变，请重新选择。");
                return new(ScArmorWorkbench.TryCommitFor(inventory, store, quote, target.Check) ? 1 : 0);
            }
        }
        return new(-1, "未知操作");
    }
}
