using System.Text.Json;
using Engine;
using Engine.Graphics;
namespace Game;

/// <summary>Authentic CS2 module, with UV-selected digits (no substitute font, no per-count texture).
/// Geometry is Source inches. Each caller supplies the final frame for its actual weapon body.</summary>
public static class ScStatTrakRenderer {
    public sealed class Geometry {
        public string Name { get; set; }
        public float[] Positions { get; set; }
        public float[] Uvs { get; set; }
        public int[] Indices { get; set; }
    }
    public static readonly Geometry[] Parts = Read();
    static Geometry[] Read() {
        using var stream = typeof(ScStatTrakRenderer).Assembly.GetManifestResourceStream("Game.AnimationData.stattrak_mesh.json");
        return JsonSerializer.Deserialize<Geometry[]>(stream);
    }
    static readonly long[] Divisors = [100000, 10000, 1000, 100, 10, 1];
    public static Vector2 DigitUv(Vector2 encoded, long kills) {
        int slot = Math.Clamp((int)MathF.Floor(encoded.X) - 1, 0, 5);
        int digit = (int)(Math.Clamp(kills, 0, ScGunStatTrak.PanelMaximum) / Divisors[slot] % 10);
        // Original columns 0..5 are encoded in fractional U; row 0 starts at V=-1.
        float localU = encoded.X - MathF.Floor(encoded.X) - slot / 16f;
        return new Vector2(localU + (digit + 1) / 16f, encoded.Y + 1);
    }
    static BlockMesh s_body, s_display;
    static long s_digits = -1;
    static readonly PrimitivesRenderer3D s_firstPerson = new();
    static readonly Dictionary<(string Asset, bool Legacy), Matrix> s_itemFrames = [];
    static readonly Dictionary<(string Asset, bool Legacy), Matrix> s_worldFrames = [];
    static readonly HashSet<(string Asset, bool Legacy)> s_logged = [];
    static Texture2D Texture(string name) => ContentManager.Get<Texture2D>("Textures/ScCsgoKnives/" + name);
    static void EnsureMeshes(long kills) {
        if (s_body is null) {
            s_body = Build(Parts.Single(p => p.Name == "stattrak_module"), false);
            s_display = Build(Parts.Single(p => p.Name == "stattrak_module_display"), true);
        }
        kills = Math.Clamp(kills, 0, ScGunStatTrak.PanelMaximum);
        if (kills == s_digits) return;
        var source = Parts.Single(p => p.Name == "stattrak_module_display");
        for (int i = 0; i < s_display.Vertices.Count; i++)
            s_display.Vertices.Array[i].TextureCoordinates = DigitUv(new(source.Uvs[2*i], source.Uvs[2*i+1]), kills);
        s_digits = kills;
    }
    static BlockMesh Build(Geometry geometry, bool emissive) {
        var mesh = new BlockMesh();
        for (int i = 0; i < geometry.Positions.Length / 3; i++) mesh.Vertices.Add(new BlockMeshVertex {
            Position = new(geometry.Positions[3*i], geometry.Positions[3*i+1], geometry.Positions[3*i+2]),
            TextureCoordinates = new(geometry.Uvs[2*i], geometry.Uvs[2*i+1]), Color = Color.White, IsEmissive = emissive,
        });
        foreach (int index in geometry.Indices) mesh.Indices.Add((ushort)index);
        return mesh;
    }
    static bool Counter(int value, string asset, out long kills) {
        kills = 0;
        if (Terrain.ExtractContents(value) != BlocksManager.GetBlockIndex<ScGunBlock>(true)
            || !GunSpec.TryGetSnapshot(Terrain.ExtractData(value), out var snapshot) || !snapshot.CounterInstalled
            || GunSpec.All[snapshot.Variant].Name != asset || !ScGunStatTrak.ModuleAvailable) return false;
        kills = snapshot.KillCount;
        return true;
    }
    public static bool AttachmentWorld(string asset, bool legacy, Cs2Rig.Pose pose, out Matrix matrix) {
        var placement = ScGunStatTrak.For(asset, legacy);
        matrix = Matrix.Identity;
        if (placement is null || pose is null || !pose.Bones.TryGetValue(placement.Bone, out var bone)) return false;
        matrix = placement.Matrix * bone;
        return true;
    }
    public static void DrawFirstPerson(int value, string asset, bool legacy, Cs2Rig.Pose pose, Matrix root,
        Matrix projection, Camera camera, int variant, in KnifePbrRenderer.Lighting lighting, float light) {
        if (!Counter(value, asset, out long kills) || !AttachmentWorld(asset, legacy, pose, out var attachment)) return;
        EnsureMeshes(kills);
        Matrix world = attachment * root;
        var body = Texture(ScGunStatTrak.ModuleMaterial);
        if (!KnifePbrRenderer.TryDrawPart(ContentManager.Get<ObjModel>(ScGunStatTrak.ModuleModel), body, variant,
                world, projection, camera.InvertedViewMatrix, in lighting, true, ScGunStatTrak.ModuleMaterial))
            Queue(s_body, body, world, new Color(light, light, light), s_firstPerson);
        Queue(s_display, Texture(ScGunStatTrak.DigitAtlas), world, Color.White, s_firstPerson);
        s_firstPerson.Flush(projection);
        if (s_logged.Add((asset,legacy))) KnifeLog.Trace($"CS2 StatTrak draw: {asset}, body={(legacy ? "legacy" : "HD")}, bone={ScGunStatTrak.For(asset,legacy).Bone}, official module 330+48 triangles, count={kills}");
    }
    static void Queue(BlockMesh mesh, Texture2D texture, Matrix matrix, Color color, PrimitivesRenderer3D renderer) {
        var batch = renderer.TexturedBatch(texture, false, 0, DepthStencilState.Default,
            RasterizerState.CullNoneScissor, BlendState.Opaque, SamplerState.LinearClamp);
        for (int i = 0; i < mesh.Indices.Count; i += 3) {
            var a = mesh.Vertices.Array[mesh.Indices.Array[i]];
            var b = mesh.Vertices.Array[mesh.Indices.Array[i+1]];
            var c = mesh.Vertices.Array[mesh.Indices.Array[i+2]];
            batch.QueueTriangle(Vector3.Transform(a.Position, matrix), Vector3.Transform(b.Position, matrix),
                Vector3.Transform(c.Position, matrix), a.TextureCoordinates, b.TextureCoordinates, c.TextureCoordinates, color);
        }
    }
    public static void DrawThirdPerson(int value, string asset, bool legacy, Matrix view, PrimitivesRenderer3D renderer, float light) {
        if (!Counter(value, asset, out long kills)) return;
        if (!s_worldFrames.TryGetValue((asset,legacy), out var local)) {
            var pose = Cs2Rig.Sample(asset, "idle", 0);
            if (!AttachmentWorld(asset, legacy, pose, out var attachment)) return;
            s_worldFrames[(asset,legacy)] = local = attachment * ScThirdPersonWeapon.LocalPlacement(pose);
        }
        EnsureMeshes(kills);
        Matrix world = local * view;
        Queue(s_body, Texture(ScGunStatTrak.ModuleMaterial), world, new Color(light,light,light), renderer);
        Queue(s_display, Texture(ScGunStatTrak.DigitAtlas), world, Color.White, renderer);
    }

    /// <summary>Module -> the same normalized bind frame as ScGunBlock.Model, not third-person metres.</summary>
    public static bool ItemMatrix(string asset, bool legacy, out Matrix matrix) {
        if (s_itemFrames.TryGetValue((asset,legacy),out matrix)) return true;
        matrix = Matrix.Identity;
        var pose = Cs2Rig.Sample(asset, "idle", 0);
        var placement = ScGunStatTrak.For(asset, legacy);
        if (!AttachmentWorld(asset, legacy, pose, out var attachment)) return false;
        if (legacy) {
            var part = ScGunNativeMesh.Parts(asset).FirstOrDefault(p => p.Bone == placement.Bone);
            if (part is null) return false;
            matrix = attachment * Matrix.Invert(part.World(pose));
        }
        else if (Cs2Rig.GetMeshParts(asset).FirstOrDefault(p => p.StartsWith(placement.Bone, StringComparison.Ordinal)) is string body) {
            matrix = attachment * Matrix.Invert(pose.GetPart(body));
        }
        else {
            var rigid = Cs2RigidMesh.For(asset);
            int joint = rigid is null ? -1 : Array.IndexOf(rigid.Joints, placement.Bone);
            if (joint < 0) return false;
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            foreach (var vertex in rigid.Vertices) { lo = Vector3.Min(lo, vertex.Position); hi = Vector3.Max(hi, vertex.Position); }
            Vector3 size = hi - lo;
            float extent = Math.Max(size.X, Math.Max(size.Y, size.Z));
            if (!(extent > 1e-4f)) return false;
            matrix = placement.Matrix * Matrix.Invert(rigid.InverseBind[joint])
                * Matrix.CreateTranslation(-(lo + hi) * .5f) * Matrix.CreateScale(1 / extent);
        }
        if (!KnifeDiagnostics.IsFinite(matrix)) return false;
        s_itemFrames[(asset,legacy)] = matrix;
        return true;
    }
    public static void DrawItem(int value, string asset, bool legacy, PrimitivesRenderer3D renderer,
        Color color, float size, ref Matrix matrix, DrawBlockEnvironmentData env) {
        if (!Counter(value, asset, out long kills) || !ItemMatrix(asset, legacy, out var attachment)) return;
        EnsureMeshes(kills);
        Matrix world = attachment * Matrix.CreateScale(size) * matrix;
        ScGunNativeMesh.DrawWorld(renderer, s_body, Texture(ScGunStatTrak.ModuleMaterial), color, 1, ref world, env);
        ScGunNativeMesh.DrawWorld(renderer, s_display, Texture(ScGunStatTrak.DigitAtlas), color, 1, ref world, env);
    }
    /// <summary>Offline QA export of the packaged renderer's actual frames and UV computation, no GPU.</summary>
    public static string PreviewJson() {
        static float[] Floats(Matrix m) => [m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,
            m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
        var frames = new Dictionary<string,float[]>();
        foreach (var spec in GunSpec.All) if (ItemMatrix(spec.Name,false,out var frame)) frames[spec.Name] = Floats(frame);
        foreach (string asset in new[]{"awp","m4a1s"}) if (ItemMatrix(asset,true,out var frame)) frames[asset+"_legacy"] = Floats(frame);
        var digits = Parts.Single(p => p.Name == "stattrak_module_display");
        var examples = new Dictionary<string,float[]>();
        foreach (long count in new long[]{0,99,100,999,1000,654321,1000000}) {
            var uv = new List<float>();
            for (int i=0; i<digits.Uvs.Length; i+=2) {
                var v = DigitUv(new(digits.Uvs[i],digits.Uvs[i+1]),count); uv.Add(v.X); uv.Add(v.Y);
            }
            examples[count.ToString()] = uv.ToArray();
        }
        return JsonSerializer.Serialize(new { parts=Parts, frames, examples });
    }
}
