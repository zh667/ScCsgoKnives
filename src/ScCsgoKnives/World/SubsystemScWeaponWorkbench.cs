using Engine;
namespace Game;

public sealed class SubsystemScWeaponWorkbench : SubsystemBlockBehavior {
    // Both game modes expose the same operations. Creative only changes their costs, not availability.
    internal static object[] MainMenuItems() => [RepairMenu.Instance, SkinMenu.Instance, CounterMenu.Instance, OwnedAttributesMenu.Instance, AttributesMenu.Instance, .. ScWeaponCrafting.All];
    // A HUD toast is behind the workshop cover. Keep refusals visible until acknowledged.
    internal static Dialog NoticeDialog(string title, string detail, Action back) =>
        new ScWorkbenchConfirmDialog(title, detail, "返回", null, _ => back());
    public override int[] HandledBlocks => [BlocksManager.GetBlockIndex<ScWeaponWorkbenchBlock>(true)];
    public override bool OnInteract(TerrainRaycastResult hit, ComponentMiner miner) {
        ComponentPlayer player = miner.ComponentPlayer;
        if (player is null) return false;
        var terrain = Project.FindSubsystem<SubsystemTerrain>(true);
        Point3 position = new(hit.CellFace.X, hit.CellFace.Y, hit.CellFace.Z);
        bool Available() => player.ComponentHealth.Health > 0 && Vector3.Distance(player.ComponentBody.Position, new Vector3(position)) < 6
            && Terrain.ExtractContents(terrain.Terrain.GetCellValue(position.X, position.Y, position.Z)) == HandledBlocks[0];
        bool Creative() => Project.FindSubsystem<SubsystemGameInfo>(true).WorldSettings.GameMode == GameMode.Creative;
        string Name(ScWeaponCrafting.Entry e) => BlocksManager.Blocks[Terrain.ExtractContents(e.Value)].GetDisplayName(terrain, e.Value);
        string Level(ScWeaponCrafting.Entry e) => !Creative() && CraftingRecipesManager.EnableLevelRestrictions ? $"  制作等级 {e.Level}" : "";
        string ValueName(int value) => BlocksManager.Blocks[Terrain.ExtractContents(value)].GetDisplayName(terrain, value);
        string MaterialLines(IReadOnlyDictionary<int, int> materials) => string.Join("\n", materials.Select(m => $"{ValueName(m.Key)} ×{m.Value}（现有 {ScInventoryTransaction.Count(miner.Inventory, m.Key)}）"));
        void Notice(string title, string detail, Action back) {
            KnifeLog.Information($"workbench notice: mode={(Creative()?"creative":"survival")} page={title} reason={detail}");
            DialogsManager.ShowDialog(player.GuiWidget, NoticeDialog(title,detail,back));
        }
        var navigation = new Dictionary<string,ScWorkbenchSelectionDialog.Navigation>();
        Dialog Selection(string title, System.Collections.IEnumerable items, float rowHeight, Func<object,string> label, Action<object> selected) {
            ScWorkbenchSelectionDialog dialog=null;
            dialog=new ScWorkbenchSelectionDialog(title,items,rowHeight,label,item=>{
                navigation[title]=dialog.CaptureNavigation();
                bool available=Available();
                KnifeLog.Information($"workbench select: mode={(Creative()?"creative":"survival")} page={title} item={label(item)} inventory={miner.Inventory?.GetType().Name} slots={miner.Inventory?.SlotsCount} available={available}");
                if(available)selected(item);
                else Notice("装配台暂不可用","你已离装配台太远、装配台已被移除，或角色已无法操作。请靠近有效的装配台后重新打开。",()=>{});
            },miner.Inventory,Creative());
            dialog.RestoreNavigation(navigation.GetValueOrDefault(title));
            if(title!= "武器装配台 · 组装 / 维修 / 涂装 / 计数器")dialog.BackAction=title.EndsWith("· 选择涂装",StringComparison.Ordinal)?ShowSkinGuns:ShowList;
            return dialog;
        }
        void ShowList() {
            if (!Available()) return;
            object[] items = MainMenuItems();
            DialogsManager.ShowDialog(player.GuiWidget, Selection("武器装配台 · 组装 / 维修 / 涂装 / 计数器", items, 56,
                (Func<object, string>)(item => item is RepairMenu ? "维修背包中的枪械" : item is SkinMenu ? "更换枪械涂装"
                    : item is CounterMenu ? "安装击杀计数器" : item is OwnedAttributesMenu ? "查看当前武器属性" : item is AttributesMenu ? "武器图鉴／等级预览"
                    : Name((ScWeaponCrafting.Entry)item) + Level((ScWeaponCrafting.Entry)item)), item => {
                    if (item is RepairMenu) { ShowRepair(); return; }
                    if (item is SkinMenu) { ShowSkinGuns(); return; }
                    if (item is CounterMenu) { ShowCounterGuns(); return; }
                    if (item is OwnedAttributesMenu) {
                        DialogsManager.ShowDialog(player.GuiWidget,new ScOwnedGunAttributesDialog(miner.Inventory,Available,ShowList));return;
                    }
                    if (item is AttributesMenu) { ShowAttributes(); return; }
                    var entry = (ScWeaponCrafting.Entry)item;
                    var materials = entry.Materials();
                    string detail = string.Join("\n", materials.Select(m => $"{BlocksManager.Blocks[Terrain.ExtractContents(m.Key)].GetDisplayName(terrain, m.Key)} ×{m.Value}（现有 {ScInventoryTransaction.Count(miner.Inventory, m.Key)}）"));
                    detail += entry.Knife ? "\n左键轻刀 7 / 右键重刀 12" : $"\n空枪交付 · 容量 {GunSpec.All[entry.Variant].Magazine} · {(GunSpec.All[entry.Variant].Pellets > 1 ? "每发总" : "单发")}攻击力 {ScSurvivalBalance.Power(entry.Name)}";
                    detail += Level(entry);
                    DialogsManager.ShowDialog(player.GuiWidget, new ScWorkbenchConfirmDialog(Name(entry), detail, "组装", "返回", button => {
                        if (button == MessageDialogButton.Button1 && Available()) {
                            bool levelOk = Creative() || !CraftingRecipesManager.EnableLevelRestrictions || player.PlayerData.Level >= entry.Level;
                            bool crafted = levelOk && ScWeaponCrafting.TryCraft(miner.Inventory, entry.Value, Creative() ? new Dictionary<int, int>() : materials);
                            Notice(crafted ? "组装完成" : "组装未完成", crafted ? "组装完成：" + Name(entry) : levelOk ? "材料不足或没有成品空位，未扣除材料。" : $"制作需要等级 {entry.Level}，未扣除材料。", ShowList);
                            return;
                        }
                        ShowList();
                    }));
                }));
        }
        // M4 repair: pick the actual gun (slot shown, so two of the same model are told apart), see the exact
        // cost, confirm; the transaction re-checks the target and materials and rolls back on any shortfall.
        void ShowRepair() {
            if (!Available()) return;
            var candidates = ScWeaponRepair.Candidates(miner.Inventory).ToArray();
            if (candidates.Length == 0) { Notice("维修 · 没有可用枪械", "背包里没有需要维修的枪械。\n请将受损的 CS 枪械放入玩家背包或快捷栏；满耐久枪不会出现在维修列表中。箱子里的枪不参与此列表。", ShowList); return; }
            DialogsManager.ShowDialog(player.GuiWidget, Selection("武器装配台 · 维修", candidates, 56,
                (Func<object, string>)(item => { var c = (ScWeaponRepair.Candidate)item; return $"{ValueName(c.Value)} · 耐久 {ScGunDurability.PercentText(c.Durability, c.Full)} · 第 {c.Slot + 1} 格"; }), item => {
                    var c = (ScWeaponRepair.Candidate)item;
                    var entry = ScWeaponCrafting.Find(c.Value);
                    // The quote freezes the record revision, the priced durability and the materials; the click re-checks all of it.
                    var quote = ScWeaponRepair.Prepare(c, entry, Creative(), ScWeaponMaterialBlock.Value);
                    if (quote is null) { Notice("维修 · 状态不可用", "这把枪的状态无法读取，请重新选择。没有扣除材料。", ShowRepair); return; }
                    string detail = $"当前耐久 {quote.Durability} / {quote.Full}（{ScGunDurability.PercentText(quote.Durability, quote.Full)}）→ 维修后 {quote.Full} / {quote.Full}\n" + (Creative() ? "创造模式：免费" : quote.Cost.Count == 0 ? "无需材料" : MaterialLines(quote.Cost))
                        + "\n只恢复耐久，不改变余弹、消音器和型号。";
                    DialogsManager.ShowDialog(player.GuiWidget, new ScWorkbenchConfirmDialog(ValueName(c.Value), detail, "维修", "返回", button => {
                        if (button == MessageDialogButton.Button1 && Available()) {
                            var result = ScWeaponRepair.TryRepair(miner.Inventory, quote, ScGunHolders.PlayerKey(player, quote.Slot));
                            KnifeLog.Information($"gun repair: {ValueName(c.Value)} slot {quote.Slot} record {quote.Id} rev {quote.Revision} {quote.Durability}/{quote.Full} cost {string.Join(",", quote.Cost.Select(m => m.Value))} -> {result}");
                            Notice(result == ScGunResult.Success ? "维修完成" : "维修未完成", result == ScGunResult.Success ? "维修完成：" + ValueName(c.Value)
                                : result == ScGunResult.StateChanged ? "枪械状态已变化，报价作废，请重新选择。" : ScGunMutation.Explain(result) + "，未扣除材料。", ShowRepair);
                            return;
                        }
                        ShowRepair();
                    }));
                }));
        }
        // Finishes: pick the gun, then the finish. Appearance only - the dialog states the ammo, durability
        // and silencer that carry over, and the transaction is what guarantees it.
        void ShowSkinGuns() {
            if (!Available()) return;
            var guns = ScWeaponSkinning.Candidates(miner.Inventory).ToArray();
            if (guns.Length == 0) { Notice("涂装 · 没有可用枪械", "背包里没有支持更换涂装的枪械。\n请将带有可用涂装的 CS 枪械放入玩家背包或快捷栏。目前只有部分型号有涂装；箱子里的枪不参与此列表。", ShowList); return; }
            DialogsManager.ShowDialog(player.GuiWidget, Selection("更换涂装 · 选择枪械", guns, 56,
                (Func<object, string>)(item => { var c = (ScWeaponSkinning.Candidate)item; return $"{ValueName(c.Value)} · {ScGunSkinCatalog.NameOf(c.SkinId)} · 第 {c.Slot + 1} 格"; }),
                item => ShowSkins((ScWeaponSkinning.Candidate)item)));
        }
        void ShowSkins(ScWeaponSkinning.Candidate gun) {
            if (!Available()) return;
            // The factory look is offered as an entry of its own so a finish can be stripped.
            object[] options = [.. ScGunSkinCatalog.For(gun.Variant), FactoryLook.Instance];
            DialogsManager.ShowDialog(player.GuiWidget, Selection($"{ValueName(gun.Value)} · 选择涂装", options, 56,
                (Func<object, string>)(item => item is FactoryLook ? "原厂外观（拆除涂装）"
                    : $"{((ScGunSkin)item).Name}{(((ScGunSkin)item).PaintId == gun.SkinId ? "（当前）" : "")}  {ScGunNames.Tier(((ScGunSkin)item).Tier)}"), item => {
                    var skin = item as ScGunSkin;
                    var quote = ScWeaponSkinning.Prepare(miner.Inventory, gun.Slot, skin, Creative(), ScWeaponMaterialBlock.Value);
                    if (quote is null) { Notice("涂装 · 无法更换", "这把枪已经是该外观，或背包中的枪械状态已变化。没有扣除材料，请重新选择枪械。", ShowSkinGuns); return; }
                    string state = GunSpec.TryGetSnapshot(Terrain.ExtractData(gun.Value), out var s)
                        ? $"当前 {s.Rounds} 发 · 耐久 {ScGunDurability.PercentText(s.Durability, s.MaxDurability)} · 消音器{(s.SilencerOff ? "已拆" : "在位")}" : "";
                    string detail = $"{ScGunSkinCatalog.NameOf(quote.FromSkinId)} → {(skin?.Name ?? "原厂外观")}\n{state}\n"
                        + (Creative() ? "创造模式：免费" : quote.Cost.Count == 0 ? "无需材料" : MaterialLines(quote.Cost))
                        + (skin is null ? "\n去皮后取消皮肤的基础伤害 +50%。" : $"\n皮肤基础伤害比原厂 +50%；安装计数器后，等级加成在此基础上计算，Lv{ScGunGrowth.MaxLevel}伤害为该基础的{ScGunGrowth.DamageMultiplier(ScGunGrowth.MaxLevel):0.##}倍。")
                        + "\n弹量、耐久、消音器、充能、计数器、击杀与等级保持不变。"
                        + (skin is { Approximate: true } ? "\n说明：官方 light 图标与本地烘焙材质；磨损和珠光效果不保证与 CS2 完全一致。" : "");
                    DialogsManager.ShowDialog(player.GuiWidget, new ScWorkbenchConfirmDialog(skin?.Name ?? "原厂外观", detail, "更换", "返回", button => {
                        if (button == MessageDialogButton.Button1 && Available()) {
                            var result = ScWeaponSkinning.Apply(miner.Inventory, quote, ScGunHolders.PlayerKey(player, quote.Slot));
                            KnifeLog.Information($"gun skin: slot {quote.Slot} record {quote.Id} rev {quote.Revision} {quote.FromSkinId} -> {skin?.PaintId ?? 0} cost {string.Join(",", quote.Cost.Select(m => m.Value))} -> {result}");
                            Notice(result == ScGunResult.Success ? "涂装完成" : "涂装未完成", result == ScGunResult.Success ? "涂装完成：" + (skin?.Name ?? "原厂外观")
                                : result == ScGunResult.StateChanged ? "枪械状态已变化，报价作废，请重新选择。" : ScGunMutation.Explain(result) + "，未扣除材料。",
                                ShowSkinGuns);
                            return;
                        }
                        ShowSkinGuns();
                    }));
                }));
        }
        // The attribute page is the same reusable view the item help opens, with the gun in hand selected.
        void ShowAttributes() {
            int value = miner.Inventory?.GetSlotValue(miner.Inventory.ActiveSlotIndex) ?? 0;
            if (Terrain.ExtractContents(value) != BlocksManager.GetBlockIndex<ScGunBlock>(true) || !ScGunBlock.IsKnown(value)) {
                var guns = ScGunCounter.Candidates(miner.Inventory).ToArray();
                // The catalogue is read-only and does not require owning a gun.
                value = guns.Length > 0 ? guns[0].Value : ScGunAttributes.TemplateValue(0);
            }
            DialogsManager.HideAllDialogs();
            ScreensManager.m_screens["RecipaediaRecipes"] = new ScGunAttributesScreen();
            ScreensManager.SwitchScreen("RecipaediaRecipes", value);
        }

        // Installing a kill counter: pick the actual gun, see the exact components, confirm. The transaction
        // re-checks the target and the materials and rolls back on any shortfall, so a refused install pays nothing.
        void ShowCounterGuns() {
            if (!Available()) return;
            var registry = ScGunRegistry.Current;
            if (registry is null || registry.Disabled) { Notice("计数器 · 暂不可用", "本世界的枪械记录不可用或已停用，无法安装计数器。请检查世界加载日志；没有扣除材料。", ShowList); return; }
            var guns = ScGunCounter.Candidates(miner.Inventory).Where(c => !c.Installed).ToArray();
            if (guns.Length == 0) { Notice("计数器 · 没有可用枪械", "背包里没有可安装计数器的枪械。\n请将尚未安装计数器的 CS 枪械放入玩家背包或快捷栏；已安装的不会重复安装，箱子里的枪不参与此列表。", ShowList); return; }
            if (registry.GrowthMode == ScGunGrowthMode.Unset) { ChooseGrowthMode(() => ShowCounterGuns()); return; }
            DialogsManager.ShowDialog(player.GuiWidget, Selection("安装击杀计数器 · 选择枪械", guns, 56,
                (Func<object, string>)(item => { var c = (ScGunCounter.Candidate)item; return $"{ValueName(c.Value)} · 第 {c.Slot + 1} 格"; }), item => {
                    var c = (ScGunCounter.Candidate)item;
                    var quote = ScGunCounter.Prepare(miner.Inventory, c.Slot, Creative());
                    if (quote is null) { Notice("计数器 · 无法安装", "这把枪已装有计数器、状态无法读取，或没有可用的官方计数器安装位。没有扣除材料，请重新选择。", ShowCounterGuns); return; }
                    bool levelOk = Creative() || !CraftingRecipesManager.EnableLevelRestrictions || player.PlayerData.Level >= ScGunGrowth.InstallLevel;
                    string state = GunSpec.TryGetSnapshot(Terrain.ExtractData(c.Value), out var s)
                        ? $"当前 {s.Rounds} 发 · 耐久 {ScGunDurability.PercentText(s.Durability, s.MaxDurability)} · 外观 {ScGunSkinCatalog.NameOf(s.SkinId)}" : "";
                    string detail = $"在原枪上安装，不更换枪械。\n{state}\n"
                        + (Creative() ? "创造模式：免费" : MaterialLines(quote.Cost))
                        + (Creative() || !CraftingRecipesManager.EnableLevelRestrictions ? "" : $"\n制作等级 {ScGunGrowth.InstallLevel}")
                        + "\n弹量、涂装、消音器、耐久与充能保持不变；计数从 0 开始，不补算历史击杀。"
                        + (ScGunStatTrak.ModuleAvailable ? "" : "\n（本版尚未包含 CS2 计数器模块模型，计数显示在属性页与物品说明中，枪身上不显示数字。）")
                        + (registry.GrowthMode == ScGunGrowthMode.CountAndGrow ? "\n本世界规则：计数 + 成长（每 100 次有效击杀升 1 级，累计 3000 满级 Lv30）。" : "\n本世界规则：仅计数，不提供成长加成。");
                    DialogsManager.ShowDialog(player.GuiWidget, new ScWorkbenchConfirmDialog(ValueName(c.Value), detail, "安装", "返回", button => {
                        if (button == MessageDialogButton.Button1 && Available()) {
                            var result = levelOk ? ScGunCounter.Apply(miner.Inventory, quote, ScGunHolders.PlayerKey(player, quote.Slot)) : ScGunResult.InsufficientMaterials;
                            KnifeLog.Information($"gun counter install: {ValueName(c.Value)} slot {quote.Slot} record {quote.Id} rev {quote.Revision} -> {result}");
                            Notice(result == ScGunResult.Success ? "安装完成" : "安装未完成", result == ScGunResult.Success ? "计数器已安装：" + ValueName(c.Value)
                                : !levelOk ? $"安装需要等级 {ScGunGrowth.InstallLevel}，未扣除材料。"
                                : result == ScGunResult.StateChanged ? "枪械状态已变化，请重新选择。" : ScGunMutation.Explain(result) + "，未扣除材料。",
                                ShowCounterGuns);
                            return;
                        }
                        ShowCounterGuns();
                    }));
                }));
        }

        // The world's counter rule is chosen once, before the first counter exists, and is not switchable in play:
        // switching would either take levels back or hand them out retroactively.
        void ChooseGrowthMode(Action then) {
            DialogsManager.ShowDialog(player.GuiWidget, new ScWorkbenchConfirmDialog("击杀计数器 · 本世界规则",
                "选择本世界的计数器规则。选定后不可在游戏中切换。\n\n计数 + 成长：每 100 次有效击杀升 1 级，累计 3000 次满级 Lv30；伤害、射速、射程、弹匣、耐久上限与充能随等级提升。\n仅计数：只记录并显示击杀数，不改变任何战斗数值。",
                "计数 + 成长", "仅计数", button => {
                    var mode = button == MessageDialogButton.Button1 ? ScGunGrowthMode.CountAndGrow : ScGunGrowthMode.CountOnly;
                    var registry = ScGunRegistry.Current;
                    if (registry is not null && registry.GrowthMode == ScGunGrowthMode.Unset) {
                        registry.GrowthMode = mode;
                        KnifeLog.Information("gun counter world rule set to " + mode);
                    }
                    then();
                }));
        }
        ShowList(); return true;
    }
    sealed class RepairMenu { public static readonly RepairMenu Instance = new(); }
    sealed class SkinMenu { public static readonly SkinMenu Instance = new(); }
    sealed class CounterMenu { public static readonly CounterMenu Instance = new(); }
    sealed class AttributesMenu { public static readonly AttributesMenu Instance = new(); }
    sealed class OwnedAttributesMenu { public static readonly OwnedAttributesMenu Instance = new(); }
    sealed class FactoryLook { public static readonly FactoryLook Instance = new(); }
}
