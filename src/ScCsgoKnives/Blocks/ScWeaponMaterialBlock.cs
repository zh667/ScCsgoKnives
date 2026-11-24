using Engine;
using Engine.Graphics;
namespace Game;

public sealed class ScWeaponMaterialBlock : ScSupplyBlock {
    /// <summary>Kinds are the block's data value and are stored in worlds; append only.</summary>
    public const int Blank = 0, Mechanism = 1, Grip = 2, Optics = 3, Paint = 4;
    /// <summary>Built-in names when the language files lack an entry (1.6.0 renames: display only, kinds unchanged).
    /// Player-facing text comes from <see cref="NameOf"/>.</summary>
    public static readonly string[] Fallback = ["金属件", "机械零件", "握把", "瞄准镜", "涂装材料"];
    public static int KindCount => Fallback.Length;
    /// <summary>The kind's display name in the current language (ScCsgoKnives/Items/Material0..4).</summary>
    public static string NameOf(int kind) { int k = Math.Clamp(kind, 0, Fallback.Length - 1); return ScWeaponText.Get("Material" + k, Fallback[k]); }
    public ScWeaponMaterialBlock() {
        DefaultDisplayName = Fallback[0]; DefaultCategory = ScWeaponText.Category; CraftingId = "sccsgomaterial";
        IsPlaceable = false; IsCollidable = false; MaxStacking = 40;
        FirstPersonOffset = new(.32f, -.48f, -.62f);
    }
    public static int Value(int kind) => Terrain.MakeBlockValue(BlocksManager.GetBlockIndex<ScWeaponMaterialBlock>(true), 0, kind);
    public override string GetDisplayName(SubsystemTerrain terrain, int value) => NameOf(Terrain.ExtractData(value));
    public override string GetDescription(int value) => Terrain.ExtractData(value) == Paint
        ? "在武器装配台更换 CS2 涂装。皮肤基础伤害比原厂提高 50%，等级加成在此基础上计算；换肤保留弹量、耐久、充能和计数等级。"
        : "在武器装配台的配件制作分类中制作，可选择制作数量；也在此组装枪械。材料和等级不足时不扣料。";
    // Meshes 2..5 are the four assembly materials; 7 is the paint tin (6 is the bench).
    protected override int MeshKind(int value) => Terrain.ExtractData(value) == Paint ? 7 : 2 + Math.Clamp(Terrain.ExtractData(value), 0, 3);
    public override int GetFaceTextureSlot(int face, int value) => 0;
    public override int GetTextureSlotCount(int value) => 1;
    public override IEnumerable<int> GetCreativeValues() => Enumerable.Range(0, KindCount).Select(Value);
    public override RecipaediaRecipesScreen GetBlockRecipeScreen(int value) => new ScAssemblyRecipesScreen();
    public override IEnumerable<CraftingRecipe> GetProceduralCraftingRecipes() {
        yield break; // No inexpensive grid recipe may bypass the workbench economy.
    }
}
