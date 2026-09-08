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
    }
    public static int Value(int kind) => Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScWeaponMaterialBlock>(true), 0, kind);
    public override string GetDisplayName(SubsystemTerrain terrain, int value) => Names[Math.Clamp(Terrain.ExtractData(value), 0, Names.Length - 1)];
    public override string GetDescription(int value) => Terrain.ExtractData(value) == Paint
        ? "在武器装配台为枪械更换 CS2 涂装。涂装只改外观，不改伤害、射速、弹匣、耐久或充能。"
        : "在武器装配台选择型号组装。材料和等级不足时不扣料。";
    // Meshes 2..5 are the four assembly materials; 7 is the paint tin (6 is the bench).
    protected override int MeshKind(int value) => Terrain.ExtractData(value) == Paint ? 7 : 2 + Math.Clamp(Terrain.ExtractData(value), 0, 3);
    public override int GetFaceTextureSlot(int face, int value) => 0;
    public override int GetTextureSlotCount(int value) => 1;
    public override IEnumerable<int> GetCreativeValues() => Enumerable.Range(0, Names.Length).Select(Value);
    public override IEnumerable<CraftingRecipe> GetProceduralCraftingRecipes() {
        yield return ScAmmoBlock.Recipe(Value(0), 1, Names[0], ["ironingot", "ironingot", "ironingot", "ironingot", "coalchunk"]);
        yield return ScAmmoBlock.Recipe(Value(1), 1, Names[1], ["sccsgomaterial:0", "copperingot", "copperingot", "germaniumchunk"]);
        yield return ScAmmoBlock.Recipe(Value(2), 1, Names[2], ["leather", "leather", "planks"]);
        yield return ScAmmoBlock.Recipe(Value(3), 1, Names[3], ["glass", "glass", "copperingot", "germaniumchunk"], 2);
        yield return ScAmmoBlock.Recipe(Value(Paint), 2, Names[Paint], ["pigment:0", "pigment:1", "canvas", "copperingot"], 2);
    }
}
