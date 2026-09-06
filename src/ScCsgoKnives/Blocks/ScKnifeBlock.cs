using Engine;
using Engine.Graphics;

namespace Game;

public class ScKnifeBlock : ScNoDurabilityBlock {
    static readonly int s_count = CsmcKnifeRig.KnifeCount;
    static readonly string[] s_names = Enumerable.Range(0, s_count).Select(CsmcKnifeRig.GetAssetName).ToArray();
    sealed record ItemModel(BlockMesh Mesh, Vector3 Min, Vector3 Max);
    readonly ScResourceCache<int, ItemModel> m_models = new("knife-items", 12, 2000);
    readonly bool[] m_firstPersonLogged = new bool[s_count];

    ItemModel Model(int variant) {
        if (m_models.TryGetValue(variant, out var hit)) return hit;
        Cs2SkinnedMesh source = Cs2SkinnedMesh.Weapon(s_names[variant])
            ?? throw new InvalidOperationException($"Missing CS2 knife mesh: {s_names[variant]}");
        if (!source.SetPose(Cs2Rig.Sample(s_names[variant], "idle", 0f), Cs2Placement.Placement()))
            throw new InvalidOperationException($"Invalid CS2 knife pose: {s_names[variant]}");
        source.Skin();
        var mesh = new BlockMesh();
        Cs2BlockMesh.Append(mesh, source.Skinned, source.Primitives.SelectMany(p => p.Indices));
        var bounds = CalculateBounds(mesh);
        return m_models[variant] = new ItemModel(mesh, bounds.Min, bounds.Max);
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
        // Old wear data can contain an out-of-range model. Preserve the item without indexing its assets.
        if (!IsKnown(value)) {
            BlocksManager.DrawFlatBlock(primitivesRenderer, value, size, ref matrix,
                ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/survival_unknown"), color, false, environmentData);
            return;
        }
        if (environmentData?.DrawBlockMode == DrawBlockMode.UI && !IsModelPreview(environmentData)) {
            BlocksManager.DrawFlatBlock(
                primitivesRenderer,
                value,
                1.45f * size,
                ref matrix,
                ContentManager.Get<Texture2D>($"Textures/ScCsgoKnives/{s_names[variant]}_slot"),
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
        ItemModel model;
        try { model = Model(variant); }
        catch (Exception e) {
            KnifeDiagnostics.WarnOnce($"knife-item-{variant}", $"Could not build {s_names[variant]} item model: {e.Message}");
            return;
        }
        if (environmentData?.DrawBlockMode == DrawBlockMode.FirstPerson && !m_firstPersonLogged[variant]) {
            m_firstPersonLogged[variant] = true;
            Log.Information($"[ScCsgoKnives] block first-person fallback: value={value} (0x{value:X}), data={Terrain.ExtractData(value)}, variant={variant}, name={s_names[variant]}.");
            Matrix sizedMatrix = Matrix.CreateScale(size) * matrix;
            (Vector3 viewMin, Vector3 viewMax) = TransformBounds(model.Min, model.Max, sizedMatrix);
            Log.Information(
                $"[ScCsgoKnives] first-person {s_names[variant]}: size={size:0.###}, light={environmentData.Light}, "
                + $"matrix={KnifeDiagnostics.MatrixSummary(matrix)}, viewBounds={FormatBounds(viewMin, viewMax)}."
            );
        }
        BlocksManager.DrawMeshBlock(primitivesRenderer, model.Mesh, ContentManager.Get<Texture2D>($"Textures/ScCsgoKnives/{s_names[variant]}_cs2"), color, size, ref matrix, environmentData);
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
        if (!IsKnown(value)) return $"未知刀具（型号 {GetVariant(value)}，保留数据）";
        if (LanguageControl.TryGetBlock($"{nameof(ScKnifeBlock)}:{GetVariant(value)}", "DisplayName", out string result)) return result;
        return base.GetDisplayName(subsystemTerrain, value);
    }

    public override RecipaediaRecipesScreen GetBlockRecipeScreen(int value) => new ScAssemblyRecipesScreen();

    public override string GetDescription(int value) {
        if (!IsKnown(value)) return "无法识别的刀具型号，原始物品数据已保留。";
        if (LanguageControl.TryGetBlock($"{nameof(ScKnifeBlock)}:{GetVariant(value)}", "Description", out string result)) return result + ScWeaponCrafting.Help(value);
        return base.GetDescription(value) + ScWeaponCrafting.Help(value);
    }

    public static bool IsKnown(int value) => GetVariant(value) < s_count;

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
