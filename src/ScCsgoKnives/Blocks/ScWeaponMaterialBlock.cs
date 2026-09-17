using Engine;
using Engine.Graphics;
namespace Game;

public sealed class ScWeaponMaterialBlock : ScSupplyBlock {
    /// <summary>Kinds are the block's data value and are stored in worlds; append only.</summary>
    public const int Blank = 0, Mechanism = 1, Grip = 2, Optics = 3, Paint = 4;
    public static readonly string[] Names = ["金属坯件", "精密机构", "握持组件", "光学组件", "涂装材料"];
    public ScWeaponMaterialBlock() {
        DefaultDisplayName = Names[0]; DefaultCategory = "Items"; CraftingId = "sccsgomaterial";
        IsPlaceable = false; IsCollidable = false; MaxStacking = 40;
        FirstPersonOffset = new(.32f, -.48f, -.62f);
    }
    public static int Value(int kind) => Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScWeaponMaterialBlock>(true), 0, kind);
    public override string GetDisplayName(SubsystemTerrain terrain, int value) => Names[Math.Clamp(Terrain.ExtractData(value), 0, Names.Length - 1)];
    public override string GetDescription(int value) => Terrain.ExtractData(value) == Paint
        ? "在武器装配台更换 CS2 涂装。皮肤基础伤害比原厂提高 50%，等级加成在此基础上计算；换肤保留弹量、耐久、充能和计数等级。"
        : "在武器装配台的配件制作分类中制作，可选择制作数量；也在此组装枪械。材料和等级不足时不扣料。";
    // Meshes 2..5 are the four assembly materials; 7 is the paint tin (6 is the bench).
    protected override int MeshKind(int value) => Terrain.ExtractData(value) == Paint ? 7 : 2 + Math.Clamp(Terrain.ExtractData(value), 0, 3);
    public override int GetFaceTextureSlot(int face, int value) => 0;
    public override int GetTextureSlotCount(int value) => 1;
    public override IEnumerable<int> GetCreativeValues() => Enumerable.Range(0, Names.Length).Select(Value);
    public override RecipaediaRecipesScreen GetBlockRecipeScreen(int value) => new ScAssemblyRecipesScreen();
    public override IEnumerable<CraftingRecipe> GetProceduralCraftingRecipes() {
        yield break; // No inexpensive grid recipe may bypass the workbench economy.
    }
}
