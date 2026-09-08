using Engine;
namespace Game;

public sealed class SubsystemScWeaponWorkbench : SubsystemBlockBehavior {
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
        void ShowList() {
            if (!Available()) return;
            object[] items = [RepairMenu.Instance, SkinMenu.Instance, .. ScWeaponCrafting.All];
            DialogsManager.ShowDialog(player.GuiWidget, new ListSelectionDialog("武器装配台 · 组装 / 维修 / 涂装", items, 56,
                (Func<object, string>)(item => item is RepairMenu ? "维修背包中的枪械" : item is SkinMenu ? "更换枪械涂装" : Name((ScWeaponCrafting.Entry)item) + Level((ScWeaponCrafting.Entry)item)), item => {
                    if (item is RepairMenu) { ShowRepair(); return; }
                    if (item is SkinMenu) { ShowSkinGuns(); return; }
                    var entry = (ScWeaponCrafting.Entry)item;
                    var materials = entry.Materials();
                    string detail = string.Join("\n", materials.Select(m => $"{BlocksManager.Blocks[Terrain.ExtractContents(m.Key)].GetDisplayName(terrain, m.Key)} ×{m.Value}（现有 {ScInventoryTransaction.Count(miner.Inventory, m.Key)}）"));
                    detail += entry.Knife ? "\n左键轻刀 7 / 右键重刀 12" : $"\n空枪交付 · 容量 {GunSpec.All[entry.Variant].Magazine} · {(GunSpec.All[entry.Variant].Pellets > 1 ? "每发总" : "单发")}攻击力 {ScSurvivalBalance.Power(entry.Name)}";
                    detail += Level(entry);
                    DialogsManager.ShowDialog(player.GuiWidget, new MessageDialog(Name(entry), detail, "组装", "返回", button => {
                        if (button == MessageDialogButton.Button1 && Available()) {
                            bool levelOk = Creative() || !CraftingRecipesManager.EnableLevelRestrictions || player.PlayerData.Level >= entry.Level;
                            bool crafted = levelOk && ScWeaponCrafting.TryCraft(miner.Inventory, entry.Value, Creative() ? new Dictionary<int, int>() : materials);
                            player.ComponentGui.DisplaySmallMessage(crafted ? "组装完成：" + Name(entry) : levelOk ? "材料不足或没有成品空位，未扣除材料。" : $"制作需要等级 {entry.Level}，未扣除材料。", crafted ? Color.White : Color.Red, true, false);
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
            if (candidates.Length == 0) { player.ComponentGui.DisplaySmallMessage("背包里没有需要维修的枪械。", Color.White, true, false); ShowList(); return; }
            DialogsManager.ShowDialog(player.GuiWidget, new ListSelectionDialog("武器装配台 · 维修", candidates, 56,
                (Func<object, string>)(item => { var c = (ScWeaponRepair.Candidate)item; return $"{ValueName(c.Value)} · 耐久 {ScGunDurability.PercentText(c.Durability, c.Full)} · 第 {c.Slot + 1} 格"; }), item => {
                    var c = (ScWeaponRepair.Candidate)item;
                    var entry = ScWeaponCrafting.Find(c.Value);
                    // The quote freezes the record revision, the priced durability and the materials; the click re-checks all of it.
                    var quote = ScWeaponRepair.Prepare(c, entry, Creative(), ScWeaponMaterialBlock.Value);
                    if (quote is null) { player.ComponentGui.DisplaySmallMessage("这把枪的状态无法读取。", Color.Red, true, false); ShowRepair(); return; }
                    string detail = $"当前耐久 {quote.Durability} / {quote.Full}（{ScGunDurability.PercentText(quote.Durability, quote.Full)}）→ 维修后 {quote.Full} / {quote.Full}\n" + (Creative() ? "创造模式：免费" : quote.Cost.Count == 0 ? "无需材料" : MaterialLines(quote.Cost))
                        + "\n只恢复耐久，不改变余弹、消音器和型号。";
                    DialogsManager.ShowDialog(player.GuiWidget, new MessageDialog(ValueName(c.Value), detail, "维修", "返回", button => {
                        if (button == MessageDialogButton.Button1 && Available()) {
                            var result = ScWeaponRepair.TryRepair(miner.Inventory, quote, ScGunHolders.PlayerKey(player, quote.Slot));
                            KnifeLog.Information($"gun repair: {ValueName(c.Value)} slot {quote.Slot} record {quote.Id} rev {quote.Revision} {quote.Durability}/{quote.Full} cost {string.Join(",", quote.Cost.Select(m => m.Value))} -> {result}");
                            player.ComponentGui.DisplaySmallMessage(result == ScGunResult.Success ? "维修完成：" + ValueName(c.Value)
                                : result == ScGunResult.StateChanged ? "枪械状态已变化，报价作废，请重新选择。" : ScGunMutation.Explain(result) + "，未扣除材料。", result == ScGunResult.Success ? Color.White : Color.Red, true, false);
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
            if (guns.Length == 0) { player.ComponentGui.DisplaySmallMessage("背包里没有可更换涂装的枪械。", Color.White, true, false); ShowList(); return; }
            DialogsManager.ShowDialog(player.GuiWidget, new ListSelectionDialog("更换涂装 · 选择枪械", guns, 56,
                (Func<object, string>)(item => { var c = (ScWeaponSkinning.Candidate)item; return $"{ValueName(c.Value)} · {ScGunSkinCatalog.NameOf(c.SkinId)} · 第 {c.Slot + 1} 格"; }),
                item => ShowSkins((ScWeaponSkinning.Candidate)item)));
        }
        void ShowSkins(ScWeaponSkinning.Candidate gun) {
            if (!Available()) return;
            // The factory look is offered as an entry of its own so a finish can be stripped.
            object[] options = [.. ScGunSkinCatalog.For(gun.Variant), FactoryLook.Instance];
            DialogsManager.ShowDialog(player.GuiWidget, new ListSelectionDialog($"{ValueName(gun.Value)} · 选择涂装", options, 56,
                (Func<object, string>)(item => item is FactoryLook ? "原厂外观（拆除涂装）"
                    : $"{((ScGunSkin)item).Name}{(((ScGunSkin)item).PaintId == gun.SkinId ? "（当前）" : "")}  {((ScGunSkin)item).Tier}"), item => {
                    var skin = item as ScGunSkin;
                    var quote = ScWeaponSkinning.Prepare(miner.Inventory, gun.Slot, skin, Creative(), ScWeaponMaterialBlock.Value);
                    if (quote is null) { player.ComponentGui.DisplaySmallMessage("这把枪已经是该外观，未扣除材料。", Color.White, true, false); ShowSkins(gun); return; }
                    string state = GunSpec.TryGetSnapshot(Terrain.ExtractData(gun.Value), out var s)
                        ? $"当前 {s.Rounds} 发 · 耐久 {ScGunDurability.PercentText(s.Durability, s.MaxDurability)} · 消音器{(s.SilencerOff ? "已拆" : "在位")}" : "";
                    string detail = $"{ScGunSkinCatalog.NameOf(quote.FromSkinId)} → {(skin?.Name ?? "原厂外观")}\n{state}\n"
                        + (Creative() ? "创造模式：免费" : quote.Cost.Count == 0 ? "无需材料" : MaterialLines(quote.Cost))
                        + "\n涂装只改外观：弹量、耐久、消音器和充能保持不变。"
                        + (skin is { Approximate: true } ? "\n注意：该涂装为配色近似，图案位置与 CS2 原版不同。" : "");
                    DialogsManager.ShowDialog(player.GuiWidget, new MessageDialog(skin?.Name ?? "原厂外观", detail, "更换", "返回", button => {
                        if (button == MessageDialogButton.Button1 && Available()) {
                            var result = ScWeaponSkinning.Apply(miner.Inventory, quote, ScGunHolders.PlayerKey(player, quote.Slot));
                            KnifeLog.Information($"gun skin: slot {quote.Slot} record {quote.Id} rev {quote.Revision} {quote.FromSkinId} -> {skin?.PaintId ?? 0} cost {string.Join(",", quote.Cost.Select(m => m.Value))} -> {result}");
                            player.ComponentGui.DisplaySmallMessage(result == ScGunResult.Success ? "涂装完成：" + (skin?.Name ?? "原厂外观")
                                : result == ScGunResult.StateChanged ? "枪械状态已变化，报价作废，请重新选择。" : ScGunMutation.Explain(result) + "，未扣除材料。",
                                result == ScGunResult.Success ? Color.White : Color.Red, true, false);
                        }
                        ShowSkinGuns();
                    }));
                }));
        }
        ShowList(); return true;
    }
    sealed class RepairMenu { public static readonly RepairMenu Instance = new(); }
    sealed class SkinMenu { public static readonly SkinMenu Instance = new(); }
    sealed class FactoryLook { public static readonly FactoryLook Instance = new(); }
}
