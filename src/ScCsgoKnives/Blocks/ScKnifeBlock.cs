using Engine;
using Engine.Graphics;

namespace Game;

public class ScKnifeBlock : Block {
    static readonly int s_count = CsmcKnifeRig.AssetCount;
    static readonly string[] s_names = Enumerable.Range(0, s_count).Select(CsmcKnifeRig.GetAssetName).ToArray();
    readonly BlockMesh[] m_meshes = Enumerable.Range(0, s_count).Select(_ => new BlockMesh()).ToArray();
    readonly Texture2D[] m_textures = new Texture2D[s_count];
    readonly Texture2D[] m_slotTextures = new Texture2D[s_count];
    readonly Vector3[] m_boundsMin = new Vector3[s_count];
    readonly Vector3[] m_boundsMax = new Vector3[s_count];
    readonly bool[] m_firstPersonLogged = new bool[s_count];

    public override void Initialize() {
        for (int i = 0; i < s_names.Length; i++) {
            try {
                LoadVariant(i);
            }
            catch (Exception e) {
                Log.Error($"[ScCsgoKnives] failed to load block assets for {s_names[i]}: {e.Message}");
            }
        }
        base.Initialize();
    }

    void LoadVariant(int i) {
        {
            m_textures[i] = ContentManager.Get<Texture2D>($"Textures/ScCsgoKnives/{s_names[i]}");
            m_slotTextures[i] = ContentManager.Get<Texture2D>($"Textures/ScCsgoKnives/{s_names[i]}_slot");
            // Folding knives ship one record per moving piece; the inventory
            // mesh is all of them baked together in the rest pose.
            foreach (string binding in CsmcKnifeRig.GetMeshParts(i)) {
                ObjModel model = ContentManager.Get<ObjModel>($"Models/ScCsgoKnives/{s_names[i]}_{binding}");
                foreach (ModelMesh mesh in model.Meshes) {
                    Matrix transform = BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone);
                    foreach (ModelMeshPart part in mesh.MeshParts)
                        m_meshes[i].AppendModelMeshPart(part, transform, false, false, true, false, Color.White);
                }
            }
            (m_boundsMin[i], m_boundsMax[i]) = CalculateBounds(m_meshes[i]);
            Log.Information(
                $"[ScCsgoKnives] asset {s_names[i]}: vertices={m_meshes[i].Vertices.Count}, indices={m_meshes[i].Indices.Count}, "
                + $"texture={m_textures[i].Width}x{m_textures[i].Height}, slot={m_slotTextures[i].Width}x{m_slotTextures[i].Height}, "
                + $"bounds={FormatBounds(m_boundsMin[i], m_boundsMax[i])}."
            );
        }
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
        if (environmentData?.DrawBlockMode == DrawBlockMode.FirstPerson && !KnifeDiagnostics.IsFinite(matrix)) {
            KnifeDiagnostics.WarnOnce($"{s_names[variant]}-draw-matrix-invalid", $"{s_names[variant]} first-person draw matrix is not finite; skipped model draw.");
            return;
        }
        if (environmentData?.DrawBlockMode == DrawBlockMode.FirstPerson && !m_firstPersonLogged[variant]) {
            m_firstPersonLogged[variant] = true;
            Log.Information($"[ScCsgoKnives] block first-person fallback: value={value} (0x{value:X}), data={Terrain.ExtractData(value)}, variant={variant}, name={s_names[variant]}.");
            Matrix sizedMatrix = Matrix.CreateScale(size) * matrix;
            (Vector3 viewMin, Vector3 viewMax) = TransformBounds(m_boundsMin[variant], m_boundsMax[variant], sizedMatrix);
            Log.Information(
                $"[ScCsgoKnives] first-person {s_names[variant]}: size={size:0.###}, light={environmentData.Light}, "
                + $"matrix={KnifeDiagnostics.MatrixSummary(matrix)}, viewBounds={FormatBounds(viewMin, viewMax)}."
            );
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

    public static int GetVariant(int value) => Terrain.ExtractData(value) & 0x1F;

    public static string GetAssetName(int variant) => s_names[Math.Clamp(variant, 0, s_names.Length - 1)];

    static bool IsModelPreview(DrawBlockEnvironmentData environmentData) =>
        environmentData?.GetType().FullName == "Game.ScCsgoBoxModelPreviewEnvironmentData";

    static (Vector3 Min, Vector3 Max) CalculateBounds(BlockMesh mesh) {
        if (mesh.Vertices.Count == 0) return (Vector3.Zero, Vector3.Zero);
        Vector3 min = new(float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity);
        for (int i = 0; i < mesh.Vertices.Count; i++) {
            Vector3 p = mesh.Vertices.Array[i].Position;
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return (min, max);
    }

    static (Vector3 Min, Vector3 Max) TransformBounds(Vector3 min, Vector3 max, Matrix matrix) {
        Vector3 transformedMin = new(float.PositiveInfinity);
        Vector3 transformedMax = new(float.NegativeInfinity);
        for (int x = 0; x < 2; x++) {
            for (int y = 0; y < 2; y++) {
                for (int z = 0; z < 2; z++) {
                    Vector3 p = Vector3.Transform(
                        new Vector3(x == 0 ? min.X : max.X, y == 0 ? min.Y : max.Y, z == 0 ? min.Z : max.Z),
                        matrix
                    );
                    transformedMin = Vector3.Min(transformedMin, p);
                    transformedMax = Vector3.Max(transformedMax, p);
                }
            }
        }
        return (transformedMin, transformedMax);
    }

    static string FormatBounds(Vector3 min, Vector3 max) =>
        $"min=({min.X:0.###},{min.Y:0.###},{min.Z:0.###}) max=({max.X:0.###},{max.Y:0.###},{max.Z:0.###})";
}
