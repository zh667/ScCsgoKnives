using System.Text.Json;
using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>Native legacy geometry and its material set resolve together in every gun view.
/// This is presentation metadata only; paint IDs, gun variants and stored instances are unchanged.</summary>
public static class ScGunNativeMesh {
    public sealed class Part {
        public string Name { get; set; }
        public string Bone { get; set; }
        public string Material { get; set; }
        public float[] RightMatrix { get; set; }
        public ObjModel Model;
        public Texture2D Texture;
        public Matrix World(Cs2Rig.Pose pose) {
            if (!pose.Bones.TryGetValue(Bone, out var bone)) throw new InvalidOperationException("Missing native gun bone " + Bone);
            var v = RightMatrix;
            return new Matrix { M11=v[0], M12=v[1], M13=v[2], M14=v[3], M21=v[4], M22=v[5], M23=v[6], M24=v[7],
                M31=v[8], M32=v[9], M33=v[10], M34=v[11], M41=v[12], M42=v[13], M43=v[14], M44=v[15] } * bone;
        }
    }
    static readonly Dictionary<string, Part[]> s_parts = Read();
    static readonly HashSet<string> s_loaded = [];
    static readonly HashSet<string> s_failed = [];
    static Dictionary<string, Part[]> Read() {
        using var stream = typeof(ScGunNativeMesh).Assembly.GetManifestResourceStream("Game.AnimationData.gun_native_meshes.json");
        return JsonSerializer.Deserialize<Dictionary<string, Part[]>>(stream);
    }
    public static Part[] Parts(string asset) => s_parts.TryGetValue(asset, out var p) ? p : [];
    public static bool UsesLegacy(string asset, string material) => ScGunSkinCatalog.All.Any(s => s.Gun == asset && s.PaintId != 1177 && s.Material == material);
    public static string ModelPath(string asset, Part part) => $"Models/ScCsgoKnives/{asset}_legacy_cs2_{part.Name}";
    public static SamplerState WorldSampler => SamplerState.LinearWrap;

    /// <summary>BlocksManager.DrawMeshBlock forces PointClamp. Native CS2 UV tiles need repeat
    /// sampling in dropped/third-person views as well as the first-person PBR shader.</summary>
    public static void DrawWorld(PrimitivesRenderer3D renderer, BlockMesh mesh, Texture2D texture,
        Color color, float size, ref Matrix matrix, DrawBlockEnvironmentData env) {
        env ??= BlocksManager.m_defaultEnvironmentData;
        var batch = renderer.TexturedBatch(texture, true, 0, null, RasterizerState.CullCounterClockwiseScissor, null, WorldSampler);
        Matrix transform = env.ViewProjectionMatrix is Matrix projection ? matrix * projection : matrix;
        if (size != 1) transform = Matrix.CreateScale(size) * transform;
        bool projected = transform.M14 != 0 || transform.M24 != 0 || transform.M34 != 0 || transform.M44 != 1;
        Vector4 tint = new(color);
        float intensity = LightingManager.LightIntensityByLightValue[Math.Clamp(env.Light, 0, 15)];
        int first = batch.TriangleVertices.Count;
        for (int i = 0; i < mesh.Vertices.Count; i++) {
            var v = mesh.Vertices.Array[i];
            Vector3 position;
            if (projected) {
                Vector4 p = Vector4.Transform(new Vector4(v.Position, 1), transform);
                position = new Vector3(p.X, p.Y, p.Z) / p.W;
            }
            else position = Vector3.Transform(v.Position, transform);
            float light = v.IsEmissive ? 1 : intensity;
            var shaded = new Color((byte)(v.Color.R*tint.X*light), (byte)(v.Color.G*tint.Y*light),
                (byte)(v.Color.B*tint.Z*light), (byte)(v.Color.A*tint.W));
            batch.TriangleVertices.Add(new VertexPositionColorTexture(position, shaded, v.TextureCoordinates));
        }
        for (int i = 0; i < mesh.Indices.Count; i++) batch.TriangleIndices.Add(first + mesh.Indices.Array[i]);
    }

    public static Part[] Resolve(string asset, int skin, out Texture2D texture, out string material) {
        texture = ScGunVisualMaterial.Load(asset, skin, out material);
        if (!UsesLegacy(asset, material)) return null;
        if (!s_failed.Contains(asset)) {
            try {
                if (!s_loaded.Contains(asset)) {
                    if (Parts(asset).Length == 0) throw new InvalidOperationException("No native parts");
                    foreach (var part in Parts(asset)) {
                        part.Model = ContentManager.Get<ObjModel>(ModelPath(asset, part));
                        if (part.Material is not null) part.Texture = ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/" + part.Material);
                    }
                    s_loaded.Add(asset);
                }
                return Parts(asset);
            }
            catch (Exception e) {
                s_failed.Add(asset);
                KnifeDiagnostics.WarnOnce("native-gun-" + asset, $"Native geometry for {asset} unavailable: {e.Message}; using factory geometry AND material.");
            }
        }
        texture = ScGunVisualMaterial.Load(asset, 0, out material);
        return null;
    }
}
