using Engine;
using Engine.Graphics;

namespace Game;

/// <summary>
/// The CS guns (AK-47, M4A1-S, AWP) as one block with a variant per gun. Its
/// variants map onto the rig manifest after the knives: asset = KnifeCount + variant.
/// Block data also carries the magazine and the M4A1-S silencer state (GunSpec).
/// </summary>
public class ScGunBlock : Block {
    static readonly int s_count = GunSpec.All.Length;
    static readonly string[] s_names = GunSpec.All.Select(spec => spec.Name).ToArray();
    readonly BlockMesh[] m_meshes = Enumerable.Range(0, s_count).Select(_ => new BlockMesh()).ToArray();
    readonly Texture2D[] m_textures = new Texture2D[s_count];
    readonly Texture2D[] m_slotTextures = new Texture2D[s_count];

    /// <summary>Rig manifest index of a gun variant.</summary>
    public static int AssetIndex(int variant) => CsmcKnifeRig.KnifeCount + Math.Clamp(variant, 0, s_count - 1);
    public static int GetVariant(int value) => GunSpec.GetVariant(Terrain.ExtractData(value));
    public static string GetAssetName(int variant) => s_names[Math.Clamp(variant, 0, s_names.Length - 1)];
    public static GunSpec SpecOf(int value) => GunSpec.All[GetVariant(value)];

    public override void Initialize() {
        for (int i = 0; i < s_count; i++) {
            try {
                m_textures[i] = ContentManager.Get<Texture2D>($"Textures/ScCsgoKnives/{s_names[i]}");
                m_slotTextures[i] = ContentManager.Get<Texture2D>($"Textures/ScCsgoKnives/{s_names[i]}_slot");
                foreach (string part in CsmcKnifeRig.GetMeshParts(AssetIndex(i))) {
                    ObjModel model = ContentManager.Get<ObjModel>($"Models/ScCsgoKnives/{s_names[i]}_{part}");
                    foreach (ModelMesh mesh in model.Meshes) {
                        Matrix transform = BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone);
                        foreach (ModelMeshPart meshPart in mesh.MeshParts)
                            m_meshes[i].AppendModelMeshPart(meshPart, transform, false, false, true, false, Color.White);
                    }
                }
                Log.Information($"[ScCsgoKnives] gun asset {s_names[i]}: vertices={m_meshes[i].Vertices.Count}, texture={m_textures[i].Width}x{m_textures[i].Height}.");
            }
            catch (Exception e) {
                Log.Error($"[ScCsgoKnives] failed to load block assets for gun {s_names[i]}: {e.Message}");
            }
        }
        base.Initialize();
    }

    public override void GenerateTerrainVertices(BlockGeometryGenerator generator, TerrainGeometry geometry, int value, int x, int y, int z) { }

    public override void DrawBlock(PrimitivesRenderer3D primitivesRenderer, int value, Color color, float size, ref Matrix matrix, DrawBlockEnvironmentData environmentData) {
        int variant = GetVariant(value);
        if (environmentData?.DrawBlockMode == DrawBlockMode.UI) {
            BlocksManager.DrawFlatBlock(primitivesRenderer, value, 1.45f * size, ref matrix, m_slotTextures[variant], color, false, environmentData);
            return;
        }
        if (environmentData?.DrawBlockMode == DrawBlockMode.FirstPerson && !KnifeDiagnostics.IsFinite(matrix)) return;
        BlocksManager.DrawMeshBlock(primitivesRenderer, m_meshes[variant], m_textures[variant], color, size, ref matrix, environmentData);
    }

    public override int GetTextureSlotCount(int value) => 1;
    public override int GetFaceTextureSlot(int face, int value) => 0;
    /// <summary>The gun has its own draw animation; no generic half-second item swap on top.</summary>
    public override bool IsSwapAnimationNeeded(int oldValue, int newValue) => false;
    /// <summary>Right-click reaches SubsystemScGunBlockBehavior.OnAim: scope (AWP) or silencer (M4A1-S).</summary>
    public override bool IsAimable_(int value) => true;
    public override Vector3 GetIconViewOffset(int value, DrawBlockEnvironmentData environmentData) => Vector3.UnitZ;

    public override IEnumerable<int> GetCreativeValues() {
        for (int variant = 0; variant < s_count; variant++)
            yield return Terrain.MakeBlockValue(BlockIndex, 0, GunSpec.MakeData(variant, GunSpec.All[variant].Magazine));
    }

    public override string GetDisplayName(SubsystemTerrain subsystemTerrain, int value) {
        if (LanguageControl.TryGetBlock($"{nameof(ScGunBlock)}:{GetVariant(value)}", "DisplayName", out string result)) return result;
        return base.GetDisplayName(subsystemTerrain, value);
    }

    public override string GetDescription(int value) {
        if (LanguageControl.TryGetBlock($"{nameof(ScGunBlock)}:{GetVariant(value)}", "Description", out string result)) return result;
        return base.GetDescription(value);
    }

    /// <summary>
    /// Block data holds the magazine and silencer, never tool wear: Survivalcraft's damage
    /// bookkeeping (digging with the gun in hand) must not touch it.
    /// </summary>
    public override int GetDamage(int value) => 0;
    public override int SetDamage(int value, int damage) => value;
}
