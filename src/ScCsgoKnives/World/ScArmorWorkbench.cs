namespace Game;

/// <summary>"防护装备" at the weapon workbench (current-direction-20260929 §1): make, configure, view and repair the player's
/// body and head protection values in this world (SubsystemScArmor). No item is handed out, no clothing slot is used and
/// nothing is drawn on the character. Three configurations: none → body protection (half armour), half → add head
/// protection (full armour), or none → full armour in one step; head protection alone is never made. Player level 3;
/// first-round costs: body protection iron ingot 8, copper ingot 4, canvas 6, leather 4; head protection 6, 2, 2, 2 (full
/// armour: their sum). A repair restores every configured protection to its budget for 40% of its making cost times the
/// used share, each material of each protection rounded up; an unused protection costs nothing and is not offered. A
/// quote is frozen to the player's exact state; the commit re-checks it and the materials, then takes every material and
/// writes the new state, or changes nothing. Creative: free, as the workbench is for guns.</summary>
public static class ScArmorWorkbench {
    public const int Level = 3;
    public const int RepairShareNumerator = 2, RepairShareDenominator = 5; // 40%
    public static readonly (string Id, int Vest, int Helmet)[] Recipe = [("ironingot", 8, 6), ("copperingot", 4, 2), ("canvas", 6, 2), ("leather", 4, 2)];
    public enum Operation { MakeVest, MakeFull, AddHelmet, Repair }
    public static void Register() => ScWorkbenchExtension.RegisterAction(new("armor", "防护装备", "装备", Open));
    /// <summary>The operations offered for <paramref name="state"/>: only moves between the three configurations, and a
    /// repair while anything configured is used.</summary>
    public static IEnumerable<Operation> Available(ScArmorState state) {
        if (!state.Vest.Owned) yield return Operation.MakeVest;
        if (!state.Vest.Owned && !state.Helmet.Owned) yield return Operation.MakeFull;
        if (state.Vest.Owned && !state.Helmet.Owned) yield return Operation.AddHelmet;
        if (state.Damaged) yield return Operation.Repair;
    }
    /// <summary>The state after <paramref name="op"/>, or null when <paramref name="state"/> does not allow it.</summary>
    public static ScArmorState Result(Operation op, ScArmorState state) => op switch {
        Operation.MakeVest when !state.Vest.Owned => state with { Vest = ScArmorPiece.New(ScArmorRules.Vest) },
        Operation.MakeFull when !state.Vest.Owned && !state.Helmet.Owned => ScArmorState.For(ScArmorConfig.Full),
        Operation.AddHelmet when state.Vest.Owned && !state.Helmet.Owned => state with { Helmet = ScArmorPiece.New(ScArmorRules.Helmet) },
        Operation.Repair when state.Damaged => state.Repaired,
        _ => null
    };
    /// <summary>Materials of <paramref name="op"/> from <paramref name="state"/> (value → count; none for an unused protection).</summary>
    public static Dictionary<int, int> Cost(Operation op, ScArmorState state, Func<string, int> resolve = null) {
        resolve ??= ScComponentCrafting.Resolve;
        var cost = new Dictionary<int, int>();
        void Add(string id, int count) { if (count <= 0) return; int value = resolve(id); cost[value] = cost.GetValueOrDefault(value) + count; }
        foreach (var (id, vest, helmet) in Recipe)
            switch (op) {
                case Operation.MakeVest: Add(id, vest); break;
                case Operation.MakeFull: Add(id, vest + helmet); break;
                case Operation.AddHelmet: Add(id, helmet); break;
                case Operation.Repair: Add(id, RepairShare(vest, state.Vest)); Add(id, RepairShare(helmet, state.Helmet)); break;
            }
        return cost;
    }
    /// <summary>ceil(<paramref name="amount"/> × 40% × used / budget) for one material of one protection (used counted in
    /// thousandths, so a partly worn unit is paid for, and an unused protection costs nothing).</summary>
    public static int RepairShare(int amount, ScArmorPiece piece) {
        if (!piece.Owned || piece.MissingMilli <= 0) return 0;
        long numerator = (long)amount * RepairShareNumerator * piece.MissingMilli, denominator = (long)RepairShareDenominator * piece.Capacity * 1000;
        return (int)((numerator + denominator - 1) / denominator);
    }
    public sealed record Quote(string Key, Operation Op, ScArmorState Before, ScArmorState After, IReadOnlyDictionary<int, int> Cost);
    /// <summary>A quote for the wearer's current state, or null when the operation is not allowed now.</summary>
    public static Quote Prepare(SubsystemScArmor store, string key, Operation op, bool free, Func<string, int> resolve = null) {
        if (store is null || !store.Readable(key)) return null;
        var before = store.Get(key);
        return Result(op, before) is { } after ? new(key, op, before, after, free ? new Dictionary<int, int>() : Cost(op, before, resolve)) : null;
    }
    /// <summary>Takes every material of <paramref name="quote"/> and writes its state, or changes nothing: the wearer must
    /// still have exactly the quoted state and the inventory every material (a partial removal is put back).</summary>
    public static bool TryCommit(IInventory inventory, SubsystemScArmor store, Quote quote) => TryCommitFor(inventory, store, quote, null);
    /// <summary>As <see cref="TryCommit"/>, with the target re-checked first (<paramref name="check"/>: null when the target
    /// is still valid, else why not): a companion must still be the same, alive, the payer's own and near.</summary>
    public static bool TryCommitFor(IInventory inventory, SubsystemScArmor store, Quote quote, Func<string> check) {
        if (ScNet.IsRemoteClient) return false; // a remote multiplayer client's commit is made by the server (ScWorkbenchOps)
        inventory = ScInventoryIdentity.Inventory(inventory);
        if (check?.Invoke() is not null) return false;
        if (quote is null || store is null || store.Get(quote.Key) != quote.Before || !store.Readable(quote.Key)) return false;
        if (quote.Cost.Count > 0 && inventory is null or ComponentCreativeInventory) return false;
        foreach (var (value, count) in quote.Cost) if (count < 0 || ScInventoryTransaction.Count(inventory, value) < count) return false;
        var removed = new List<(int Slot, int Value, int Count)>();
        void Rollback() { foreach (var (slot, value, count) in removed) inventory.AddSlotItems(slot, value, count); }
        foreach (var (value, count) in quote.Cost) {
            int remaining = count;
            for (int i = 0; i < inventory.SlotsCount && remaining > 0; i++) {
                if (inventory.GetSlotValue(i) != value || inventory.GetSlotCount(i) <= 0) continue;
                int amount = Math.Min(remaining, inventory.GetSlotCount(i)), actual = inventory.RemoveSlotItems(i, amount);
                if (actual > 0) removed.Add((i, value, actual));
                remaining -= actual;
                if (actual != amount) break;
            }
            if (remaining > 0) { Rollback(); return false; }
        }
        if (!store.TryReplace(quote.Key, quote.Before, quote.After)) { Rollback(); return false; }
        if (removed.Count > 0) ScInventoryTransaction.Changed(inventory);
        return true;
    }
    public static string Title(Operation op, ScArmorState state) => op switch {
        Operation.MakeVest => state.Helmet.Owned ? "制作躯干防护（背心）→ 全甲" : "制作躯干防护（背心）→ 半甲",
        Operation.MakeFull => "制作全甲（背心 + 头部防护）",
        Operation.AddHelmet => "加装头部防护 → 全甲",
        _ => "维修防护到满值"
    };
    public static string Rules(bool creative) =>
        "防护装备是数值，不是物品：不占背包或衣物栏，不改变人物外观，也不会掉落。\n"
        + $"头部防护：CS 枪械子弹命中头部时吸收 {ScArmorRules.Reduction(ScArmorRules.Helmet) * 100:0}%，吸收量 {ScArmorRules.Budget(ScArmorRules.Helmet)}。\n"
        + $"躯干防护（背心）：命中躯干和手臂时吸收 {ScArmorRules.Reduction(ScArmorRules.Vest) * 100:0}%，吸收量 {ScArmorRules.Budget(ScArmorRules.Vest)}。腿部不受保护。\n"
        + "吸收量是攻击力单位，不是生命值；用尽后该部位不再防护，可在这里维修。\n"
        + "其他生物的普通近战：躯干防护吸收 25%（近战不判头部，不消耗头部防护）；其他模组的物理投射物：命中头部时头部防护吸收 50%，否则躯干防护吸收 40%。"
        + "火焰、中毒、溺水、饥饿、坠落、爆炸、魔法和直接改生命的效果不受影响。CS 防护先结算，原版或其他模组的衣物再结算一次。以上比例为首轮试验值。\n"
        + "三种配置：无甲、半甲（只有背心）、全甲（背心 + 头部防护）；不能只做头部防护。\n"
        + "属于当前世界的你自己，或你附近自己的同伴（材料由你支付）；读档、换世界不会补满。玩家死亡时若背包掉落，防护清零；同伴死亡或解散时其防护结束（都不产生掉落物）。\n"
        + $"人物等级 {Level}。首轮试验材料：背心 铁锭 8、铜锭 4、帆布 6、皮革 4；头部防护 铁锭 6、铜锭 2、帆布 2、皮革 2。"
        + "维修到满值：对应制作材料的 40% × 缺损比例，每项向上取整。"
        + (creative ? "\n创造模式：免费配置；创造模式的玩家不会受伤，防护也不会消耗。" : "");
    sealed class RulesEntry { public static readonly RulesEntry Instance = new(); }
    /// <summary>Who the protection is for: the player or a companion (key, name, and a re-check run before every commit:
    /// null while the target is still valid, else why not).</summary>
    public sealed record Target(string Key, string Name, Func<string> Check);
    /// <summary>Further targets near a player (the agents package registers the player's own nearby companions).</summary>
    public static readonly List<Func<ComponentPlayer, IEnumerable<Target>>> TargetProviders = [];
    public static IEnumerable<Target> TargetsFor(ComponentPlayer player) {
        yield return new(SubsystemScArmor.PlayerKey(player.PlayerData.PlayerIndex), "自己", () => player.ComponentHealth.Health > 0 ? null : "你已死亡。");
        foreach (var provider in TargetProviders.ToArray()) {
            IEnumerable<Target> more;
            try { more = provider(player)?.ToArray() ?? []; } catch (Exception e) { KnifeDiagnostics.WarnOnce("armor-targets", "protection targets unavailable: " + e.Message); continue; }
            foreach (var t in more) yield return t;
        }
    }
    static void Open(ComponentPlayer player, Action back) {
        var store = player.Project.FindSubsystem<SubsystemScArmor>(false);
        void Notice(string title, string detail, Action then) => DialogsManager.ShowDialog(player.GuiWidget, SubsystemScWeaponWorkbench.NoticeDialog(title, detail, then));
        if (store is null || player.PlayerData is null) { Notice("防护装备", "这个世界没有载入防护装备数据。", back); return; }
        var targets = TargetsFor(player).ToArray();
        if (targets.Length == 1) { OpenFor(player, targets[0], back, back); return; }
        var dialog = new ScWorkbenchSelectionDialog("防护装备 · 选择对象", targets, 56,
            o => { var t = (Target)o; return t.Name + " · " + store.Get(t.Key).Describe(); },
            o => OpenFor(player, (Target)o, () => Open(player, back), back), player.ComponentMiner?.Inventory, false) { BackAction = back };
        DialogsManager.ShowDialog(player.GuiWidget, dialog);
    }
    static void OpenFor(ComponentPlayer player, Target target, Action back, Action exit) {
        var store = player.Project.FindSubsystem<SubsystemScArmor>(false);
        bool creative = player.Project.FindSubsystem<SubsystemGameInfo>(true).WorldSettings.GameMode == GameMode.Creative;
        void Notice(string title, string detail, Action then) => DialogsManager.ShowDialog(player.GuiWidget, SubsystemScWeaponWorkbench.NoticeDialog(title, detail, then));
        void Reopen() => OpenFor(player, target, back, exit);
        string key = target.Key;
        if (target.Check() is { } gone) { Notice("防护装备", gone, back); return; }
        if (!store.Readable(key)) { Notice("防护装备", "这个对象保存的防护数据来自更新的版本，当前版本不能修改；数据已原样保留。", back); return; }
        var state = store.Get(key); var inventory = player.ComponentMiner?.Inventory;
        string Name(int value) => BlocksManager.Blocks[Terrain.ExtractContents(value)].GetDisplayName(null, value);
        var items = new List<object> { RulesEntry.Instance };
        items.AddRange(Available(state).Cast<object>());
        var dialog = new ScWorkbenchSelectionDialog($"防护装备 · {target.Name} · {state.Describe()}", items, 56,
            o => o is Operation op ? Title(op, state) : "说明 · 当前 " + state.Describe(),
            o => {
                if (o is not Operation op) { Notice("防护装备说明", $"对象：{target.Name}\n当前：{state.Describe()}\n\n{Rules(creative)}", Reopen); return; }
                if (!creative && CraftingRecipesManager.EnableLevelRestrictions && player.PlayerData.Level < Level) { Notice("人物等级不足", $"防护装备需要人物等级 {Level}。", Reopen); return; }
                var quote = Prepare(store, key, op, creative);
                if (quote is null) { Notice("未完成", "防护状态已改变，请重新选择。", Reopen); return; }
                string lines = quote.Cost.Count == 0 ? "无需材料（创造模式免费）。" : string.Join("\n", quote.Cost.Select(m => $"{Name(m.Key)} ×{m.Value}（现有 {ScInventoryTransaction.Count(inventory, m.Key)}）"));
                string detail = $"对象：{target.Name}（材料由你支付）\n当前：{quote.Before.Describe()}\n完成后：{quote.After.Describe()}\n\n材料：\n{lines}\n\n"
                    + (op == Operation.Repair ? "维修材料为对应制作材料的 40% × 缺损比例，每项向上取整。\n" : "")
                    + "确认后一次扣除全部材料并生效；材料不足、对象离开或防护状态已改变时不扣任何材料。";
                DialogsManager.ShowDialog(player.GuiWidget, new ScWorkbenchConfirmDialog(Title(op, state) + " · " + target.Name, detail, "确认", "返回", button => {
                    if (button != MessageDialogButton.Button1) { Reopen(); return; }
                    if (!ReferenceEquals(inventory, player.ComponentMiner?.Inventory) || player.ComponentHealth.Health <= 0) { Notice("未完成", "背包已改变，请重新选择。", Reopen); return; }
                    if (target.Check() is { } why) { Notice("未完成", why + "未扣除任何材料。", back); return; }
                    ScNetWorkbench.Run(new ScWorkbenchOp(ScWorkbenchOpKind.Armor, SubsystemScWeaponWorkbench.BenchOf(player), Arg: (int)op, Target: key, State: quote.Before.Encode()),
                        () => new(TryCommitFor(inventory, store, quote, target.Check) ? 1 : 0),
                        r => Notice(r.Code == 1 ? "完成" : "未完成", r.Code == 1 ? $"{target.Name}的防护：" + (ScNet.IsRemoteClient ? quote.After : store.Get(key)).Describe()
                            : r.Detail is { Length: > 0 } d ? d : "材料不足或防护状态已改变，未扣除任何材料。", Reopen));
                }));
            }, inventory, creative) { BackAction = back };
        dialog.SetMaterialQuote(o => o is Operation op && !creative ? Cost(op, state) : []);
        DialogsManager.ShowDialog(player.GuiWidget, dialog);
    }
}
