using Engine;
using Engine.Graphics;

namespace Game;

/// <summary>
/// The CS guns (AK-47, M4A1-S, AWP) as one block with a variant per gun. Its
/// variants map onto the rig manifest after the knives: asset = KnifeCount + variant.
/// Block data also carries the magazine and the M4A1-S silencer state (GunSpec).
/// </summary>
public class ScGunBlock : ScNoDurabilityBlock {
    static readonly int s_count = GunSpec.All.Length;
    static readonly string[] s_names = GunSpec.All.Select(spec => spec.Name).ToArray();
    readonly BlockMesh[] m_meshes = Enumerable.Range(0, s_count).Select(_ => new BlockMesh()).ToArray();
    readonly Texture2D[] m_textures = new Texture2D[s_count];
    readonly Texture2D[] m_slotTextures = new Texture2D[s_count];

    /// <summary>Rig manifest index of a gun variant.</summary>
    public static int AssetIndex(int variant) => variant >= 0 && variant < s_count ? CsmcKnifeRig.KnifeCount + variant : -1;
    public static int GetVariant(int value) => GunSpec.GetVariant(Terrain.ExtractData(value));
    public static string GetAssetName(int variant) => s_names[Math.Clamp(variant, 0, s_names.Length - 1)];
    static readonly GunSpec Unknown = new() { Name = "unknown", Magazine = 0 };
    public static bool IsKnown(int value) => GetVariant(value) < s_count;
    public static GunSpec SpecOf(int value) => IsKnown(value) ? GunSpec.All[GetVariant(value)] : Unknown;

    public override void Initialize() {
        for (int i = 0; i < s_count; i++) {
            try {
                m_textures[i] = LoadTexture(s_names[i] + "_hd");
                m_slotTextures[i] = LoadTexture(s_names[i] + "_slot") ?? m_textures[i];
                var parts = Cs2Rig.GetMeshParts(s_names[i]);
                if (parts.Count > 0) {
                    foreach (string part in parts) {
                        ObjModel model = ContentManager.Get<ObjModel>($"Models/ScCsgoKnives/{s_names[i]}_cs2_{part}");
                        foreach (ModelMesh mesh in model.Meshes) {
                            Matrix transform = BlockMesh.GetBoneAbsoluteTransform(mesh.ParentBone);
                            foreach (ModelMeshPart meshPart in mesh.MeshParts)
                                m_meshes[i].AppendModelMeshPart(meshPart, transform, false, false, true, false, Color.White);
                        }
                    }
                }
                else AppendRigidMesh(m_meshes[i], s_names[i]);
                Log.Information($"[ScCsgoKnives] gun asset {s_names[i]}: vertices={m_meshes[i].Vertices.Count}, "
                                + $"texture={(m_textures[i] is null ? "none" : $"{m_textures[i].Width}x{m_textures[i].Height}")}.");
            }
            catch (Exception e) {
                Log.Error($"[ScCsgoKnives] failed to load block assets for gun {s_names[i]}: {e.Message}");
            }
        }
        base.Initialize();
    }

    static Texture2D LoadTexture(string name) {
        try { return ContentManager.Get<Texture2D>($"Textures/ScCsgoKnives/{name}"); }
        catch { return null; }
    }

    /// <summary>
    /// The inventory and in-hand mesh for a gun that ships as rigid parts.
    ///
    /// Each part is baked at its bind pose - the item in the hotbar is not animated -
    /// and the whole thing is normalised into the unit cube the block renderer expects,
    /// the way the OBJ pieces already were by cs2_glb_to_obj.
    /// </summary>
    static void AppendRigidMesh(BlockMesh into, string asset) {
        Cs2RigidMesh mesh = Cs2RigidMesh.For(asset);
        if (mesh is null) return;

        Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
        foreach (Cs2SkinnedMesh.Vertex v in mesh.Vertices) {
            lo = Vector3.Min(lo, v.Position);
            hi = Vector3.Max(hi, v.Position);
        }
        Vector3 size = hi - lo;
        float extent = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        if (!(extent > 1e-4f)) return;
        Vector3 centre = (lo + hi) * 0.5f;
        float scale = 1f / extent;

        // BlockMesh indexes with ushort, so the vertices are shared rather than
        // written per triangle: the M4A4 has 45,422 triangles, and three vertices each
        // would be 136,266 of them against a 65,535 ceiling. Shared, the largest gun
        // CS2 ships is the M249 at 54,973, which fits.
        if (mesh.VertexCount > ushort.MaxValue) {
            Log.Warning($"[ScCsgoKnives] {asset} has {mesh.VertexCount} vertices, more than a "
                        + "ushort index holds; its inventory mesh is left empty.");
            return;
        }
        foreach (Cs2SkinnedMesh.Vertex v in mesh.Vertices) {
            into.Vertices.Add(new BlockMeshVertex {
                Position = (v.Position - centre) * scale,
                Color = Color.White,
                TextureCoordinates = v.TextureCoordinate,
            });
        }
        foreach (Cs2RigidMesh.Part part in mesh.Parts) {
            foreach (int index in part.Indices) into.Indices.Add((ushort)index);
        }
    }

    public override void GenerateTerrainVertices(BlockGeometryGenerator generator, TerrainGeometry geometry, int value, int x, int y, int z) { }

    public override void DrawBlock(PrimitivesRenderer3D primitivesRenderer, int value, Color color, float size, ref Matrix matrix, DrawBlockEnvironmentData environmentData) {
        int variant = GetVariant(value);
        if (!IsKnown(value)) {
            BlocksManager.DrawFlatBlock(primitivesRenderer, value, size, ref matrix, ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/survival_unknown"), color, false, environmentData);
            return;
        }
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
        if (!IsKnown(value)) return $"未知枪械（型号 {GetVariant(value)}，保留数据）";
        if (LanguageControl.TryGetBlock($"{nameof(ScGunBlock)}:{GetVariant(value)}", "DisplayName", out string result)) return result;
        return base.GetDisplayName(subsystemTerrain, value);
    }

    public override RecipaediaRecipesScreen GetBlockRecipeScreen(int value) => new ScAssemblyRecipesScreen();

    public override string GetDescription(int value) {
        if (LanguageControl.TryGetBlock($"{nameof(ScGunBlock)}:{GetVariant(value)}", "Description", out string result)) return result + ScWeaponCrafting.Help(value);
        return base.GetDescription(value) + ScWeaponCrafting.Help(value);
    }

}
