using Engine;
using Engine.Graphics;

namespace Game;

public class ScCaseBlock : Block {
    Texture2D m_texture;
    readonly BlockMesh m_standaloneMesh = new();

    public override void Initialize() {
        m_texture = ContentManager.Get<Texture2D>("Textures/ScCsgoBox/csgo_box");
        ObjModel model = ContentManager.Get<ObjModel>("Models/ScCsgoBox/csgo_box");
        foreach (ModelMesh mesh in model.Meshes) {
            Matrix transform = BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone);
            foreach (ModelMeshPart part in mesh.MeshParts)
                m_standaloneMesh.AppendModelMeshPart(part, transform, false, false, true, false, Color.White);
        }
        base.Initialize();
    }

    public override void GenerateTerrainVertices(BlockGeometryGenerator generator, TerrainGeometry geometry, int value, int x, int y, int z) { }

    public override void DrawBlock(
        PrimitivesRenderer3D primitivesRenderer,
        int value,
        Color color,
        float size,
        ref Matrix matrix,
        DrawBlockEnvironmentData environmentData
    ) => BlocksManager.DrawMeshBlock(primitivesRenderer, m_standaloneMesh, m_texture, color, 0.9f * size, ref matrix, environmentData);

    public override string GetDisplayName(SubsystemTerrain subsystemTerrain, int value) =>
        CaseRegistry.GetByData(Terrain.ExtractData(value))?.GetDisplayName() ?? base.GetDisplayName(subsystemTerrain, value);

    /// <summary>1.7.0 (feedback-1124-170 Q4): the standalone recipe with copper in place of iron.</summary>
    public override IEnumerable<CraftingRecipe> GetProceduralCraftingRecipes() {
        string text = LanguageControl.TryGet(out string t, "ScCsgoBox", "Recipe", "Case") ? t : "制作一个可开启的荒野武器箱";
        yield return ScAmmoBlock.Recipe(Terrain.MakeBlockValue(BlockIndex, 0, 0), 1, text,
            ["copperingot", "planks", "copperingot", "planks", "chest", "planks", "copperingot", "planks", "copperingot"]);
    }

    public override IEnumerable<int> GetCreativeValues() {
        foreach (CaseDefinition definition in CaseRegistry.All.OrderBy(x => x.Data))
            yield return Terrain.MakeBlockValue(BlockIndex, 0, definition.Data);
    }
}
