using Engine;
using Engine.Graphics;

namespace Game;

public class ScKnifeBlock : Block {
    static readonly string[] s_names = ["karambit", "m9", "butterfly"];
    readonly BlockMesh[] m_meshes = [new(), new(), new()];
    readonly Texture2D[] m_textures = new Texture2D[3];
    readonly Texture2D[] m_slotTextures = new Texture2D[3];
    readonly BlockMesh[] m_butterflyParts = [new(), new(), new()];
    static readonly string[] s_butterflyPartNames = ["down", "up", "blade2"];

    public override void Initialize() {
        for (int i = 0; i < s_names.Length; i++) {
            m_textures[i] = ContentManager.Get<Texture2D>($"Textures/ScCsgoKnives/{s_names[i]}");
            m_slotTextures[i] = ContentManager.Get<Texture2D>($"Textures/ScCsgoKnives/{s_names[i]}_slot");
            ObjModel model = ContentManager.Get<ObjModel>($"Models/ScCsgoKnives/{s_names[i]}");
            foreach (ModelMesh mesh in model.Meshes) {
                Matrix transform = BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone);
                foreach (ModelMeshPart part in mesh.MeshParts)
                    m_meshes[i].AppendModelMeshPart(part, transform, false, false, true, false, Color.White);
            }
        }
        for (int i = 0; i < s_butterflyPartNames.Length; i++) {
            ObjModel model = ContentManager.Get<ObjModel>($"Models/ScCsgoKnives/butterfly_{s_butterflyPartNames[i]}");
            foreach (ModelMesh mesh in model.Meshes) {
                Matrix transform = BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone);
                foreach (ModelMeshPart part in mesh.MeshParts)
                    m_butterflyParts[i].AppendModelMeshPart(part, transform, false, false, true, false, Color.White);
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
        if (environmentData?.DrawBlockMode == DrawBlockMode.UI && !IsModelPreview(environmentData)) {
            BlocksManager.DrawFlatBlock(
                primitivesRenderer,
                value,
                1.45f * size,
                ref matrix,
                m_slotTextures[variant],
                color,
                false,
                environmentData
            );
            return;
        }
        if (variant == 2 && environmentData?.DrawBlockMode == DrawBlockMode.FirstPerson) {
            ComponentFirstPersonModel firstPerson = environmentData.Owner?.FindComponent<ComponentFirstPersonModel>();
            KnifeFramePose pose = KnifeAnimationController.GetCurrentPose(firstPerson);
            Matrix[] partPoses = [pose.ButterflyDown, pose.ButterflyUp, pose.ButterflyBlade];
            for (int i = 0; i < m_butterflyParts.Length; i++) {
                Matrix partMatrix = Matrix.CreateScale(1f / size) * partPoses[i] * Matrix.CreateScale(size) * matrix;
                BlocksManager.DrawMeshBlock(primitivesRenderer, m_butterflyParts[i], m_textures[variant], color, size, ref partMatrix, environmentData);
            }
            return;
        }
        BlocksManager.DrawMeshBlock(primitivesRenderer, m_meshes[variant], m_textures[variant], color, size, ref matrix, environmentData);
    }

    public override int GetTextureSlotCount(int value) => 1;

    public override int GetFaceTextureSlot(int face, int value) => 0;

    // The knife has its own deploy animation. Let the first-person component
    // switch to it immediately instead of also lowering it with SC's generic
    // half-second item-swap animation.
    public override bool IsSwapAnimationNeeded(int oldValue, int newValue) => false;

    public override Vector3 GetIconViewOffset(int value, DrawBlockEnvironmentData environmentData) =>
        IsModelPreview(environmentData) ? base.GetIconViewOffset(value, environmentData) : Vector3.UnitZ;

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

    static bool IsModelPreview(DrawBlockEnvironmentData environmentData) =>
        environmentData?.GetType().FullName == "Game.ScCsgoBoxModelPreviewEnvironmentData";
}
