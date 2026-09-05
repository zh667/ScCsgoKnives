using Engine;
using Engine.Graphics;
namespace Game;

public sealed class ScWeaponMaterialBlock : ScSupplyBlock {
    public static readonly string[] Names = ["金属坯件", "精密机构", "握持组件", "光学组件"];
    public ScWeaponMaterialBlock() {
        DefaultDisplayName = Names[0]; DefaultCategory = "Items"; CraftingId = "sccsgomaterial";
        IsPlaceable = false; IsCollidable = false; MaxStacking = 40;
    }
    public static int Value(int kind) => Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScWeaponMaterialBlock>(true), 0, kind);
    public override string GetDisplayName(SubsystemTerrain terrain, int value) => Names[Math.Clamp(Terrain.ExtractData(value), 0, 3)];
    public override string GetDescription(int value) => "在武器装配台选择型号组装。材料和等级不足时不扣料。";
    protected override int MeshKind(int value) => 2 + Math.Clamp(Terrain.ExtractData(value), 0, 3);
    public override int GetFaceTextureSlot(int face, int value) => 0;
    public override int GetTextureSlotCount(int value) => 1;
    public override IEnumerable<int> GetCreativeValues() => Enumerable.Range(0, 4).Select(Value);
    public override IEnumerable<CraftingRecipe> GetProceduralCraftingRecipes() {
        yield return ScAmmoBlock.Recipe(Value(0), 1, Names[0], ["ironingot", "ironingot", "ironingot", "ironingot", "coalchunk"]);
        yield return ScAmmoBlock.Recipe(Value(1), 1, Names[1], ["sccsgomaterial:0", "copperingot", "copperingot", "germaniumchunk"]);
        yield return ScAmmoBlock.Recipe(Value(2), 1, Names[2], ["leather", "leather", "planks"]);
        yield return ScAmmoBlock.Recipe(Value(3), 1, Names[3], ["glass", "glass", "copperingot", "germaniumchunk"], 2);
    }
}
