using Engine;
using Engine.Graphics;

namespace Game;

public sealed class ScAmmoBlock : ScSupplyBlock {
    public const int Magazine = 0, Shell = 1;
    public ScAmmoBlock() {
        DefaultDisplayName = "通用弹匣"; DefaultCategory = "Weapons";
        CraftingId = "sccsgoammo"; IsPlaceable = false; IsCollidable = false;
        MaxStacking = 40; DefaultTextureSlot = 0;
    }
    public static int Value(int kind) => Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScAmmoBlock>(true), 0, kind);
    public override string GetDisplayName(SubsystemTerrain terrain, int value) => Terrain.ExtractData(value) == Shell ? "霰弹" : "通用弹匣";
    public override string GetDescription(int value) => Terrain.ExtractData(value) == Shell
        ? "管式霰弹枪每次插入消耗 1 颗；MAG-7 每次换匣消耗 5 颗，丢弃旧匣余弹。"
        : "容量 35 发以内每次换弹消耗 1 个；P90/野牛 2 个，M249 3 个，内格夫 5 个。旧匣余弹作废。";
    protected override int MeshKind(int value) => Terrain.ExtractData(value) == Shell ? 1 : 0;
    public override int GetFaceTextureSlot(int face, int value) => 0;
    public override int GetTextureSlotCount(int value) => 1;
    public override IEnumerable<int> GetCreativeValues() { yield return Value(Magazine); yield return Value(Shell); }
    public override IEnumerable<CraftingRecipe> GetProceduralCraftingRecipes() {
        yield return Recipe(Value(Magazine), 2, "通用弹匣 ×2", ["ironingot", "copperingot", "copperingot", "gunpowder", "gunpowder", "gunpowder"]);
        yield return Recipe(Value(Shell), 8, "霰弹 ×8", ["ironingot", "copperingot", "gunpowder", "gunpowder", "canvas"]);
    }
    internal static CraftingRecipe Recipe(int result, int count, string description, string[] materials, int level = 1) {
        var recipe = new CraftingRecipe { ResultValue = result, ResultCount = count, RequiredPlayerLevel = level, Description = description };
        Array.Copy(materials, recipe.Ingredients, materials.Length);
        return recipe;
    }
}
