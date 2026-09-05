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
        void ShowList() {
            if (!Available()) return;
            DialogsManager.ShowDialog(player.GuiWidget, new ListSelectionDialog("武器装配台 · 选择型号", ScWeaponCrafting.All, 56,
                (Func<object, string>)(item => Name((ScWeaponCrafting.Entry)item) + Level((ScWeaponCrafting.Entry)item)), item => {
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
        ShowList(); return true;
    }
}
