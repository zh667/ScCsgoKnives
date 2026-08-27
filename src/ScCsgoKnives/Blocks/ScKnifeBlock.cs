using Engine;
using Engine.Graphics;

namespace Game;

public class ScKnifeBlock : Block {
    static readonly string[] s_names = ["karambit", "m9", "butterfly"];
    readonly BlockMesh[] m_meshes = [new(), new(), new()];
    readonly Texture2D[] m_textures = new Texture2D[3];

    public override void Initialize() {
        for (int i = 0; i < s_names.Length; i++) {
            m_textures[i] = ContentManager.Get<Texture2D>($"Textures/ScCsgoKnives/{s_names[i]}");
            ObjModel model = ContentManager.Get<ObjModel>($"Models/ScCsgoKnives/{s_names[i]}");
            foreach (ModelMesh mesh in model.Meshes) {
                Matrix transform = BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone);
                foreach (ModelMeshPart part in mesh.MeshParts)
                    m_meshes[i].AppendModelMeshPart(part, transform, false, false, true, false, Color.White);
            }
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
    ) {
        int variant = GetVariant(value);
        BlocksManager.DrawMeshBlock(primitivesRenderer, m_meshes[variant], m_textures[variant], color, size, ref matrix, environmentData);
    }

    public override IEnumerable<int> GetCreativeValues() {
        for (int variant = 0; variant < s_names.Length; variant++)
            yield return Terrain.MakeBlockValue(BlockIndex, 0, variant);
    }

    public override string GetDisplayName(SubsystemTerrain subsystemTerrain, int value) {
        if (LanguageControl.TryGetBlock($"{nameof(ScKnifeBlock)}:{GetVariant(value)}", "DisplayName", out string result)) return result;
        return base.GetDisplayName(subsystemTerrain, value);
    }

    public override string GetDescription(int value) {
        if (LanguageControl.TryGetBlock($"{nameof(ScKnifeBlock)}:{GetVariant(value)}", "Description", out string result)) return result;
        return base.GetDescription(value);
    }

    public static int GetVariant(int value) => Terrain.ExtractData(value) & 0xF;

    public static string GetAssetName(int variant) => s_names[Math.Clamp(variant, 0, s_names.Length - 1)];
}

