using Engine;
using Engine.Graphics;
namespace Game;

// Compose the vanilla mesh instead of inheriting CraftingTableBlock's static Index field.
public sealed class ScWeaponWorkbenchBlock : Block {
    public ScWeaponWorkbenchBlock() {
        DefaultDisplayName = "武器装配台"; DefaultCategory = "Construction"; CraftingId = "sccsgoworkbench";
        DefaultIsInteractive = true; MaxStacking = 40; DefaultSoundMaterialName = "Metal";
    }
    Block Mesh => BlocksManager.Blocks[BlocksManager.GetBlockIndex<CraftingTableBlock>(true)];
    public override void GenerateTerrainVertices(BlockGeometryGenerator generator, TerrainGeometry geometry, int value, int x, int y, int z) => Mesh.GenerateTerrainVertices(generator, geometry, value, x, y, z);
    public override void DrawBlock(PrimitivesRenderer3D renderer, int value, Color color, float size, ref Matrix matrix, DrawBlockEnvironmentData env) => Mesh.DrawBlock(renderer, value, color, size, ref matrix, env);
    public override string GetDescription(int value) => "交互后选择枪械或刀具，查看材料与等级再组装；枪械以空枪交付。";
    public override IEnumerable<CraftingRecipe> GetProceduralCraftingRecipes() {
        yield return ScAmmoBlock.Recipe(Terrain.MakeBlockValue(BlockIndex), 1, "武器装配台", ["ironingot", "ironingot", "ironingot", "ironingot", "copperingot", "copperingot", "planks", "planks", "planks"]);
    }
}
