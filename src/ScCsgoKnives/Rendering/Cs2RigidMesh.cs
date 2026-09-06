using System.IO;
using System.Reflection;
using Engine;
using Engine.Graphics;

namespace Game;

/// <summary>
/// A CS2 gun: rigid pieces, one bone each, plus whatever few triangles genuinely blend.
///
/// This is not the arms' problem and it is not solved the arms' way. Measured over the
/// 32 guns' body_hd meshes, 850,590 of 850,629 vertices carry a single influence at
/// weight 1 - only the MAC-10's strap, 39 vertices, blends - and 145 triangles in all
/// span two bones. The arms are 70% blended. So a gun is drawn as groups, each with
/// its own matrix, and costs nothing per vertex; only the residue is skinned.
///
/// Vertices are kept in the bind pose and never rewritten. Each part is drawn with
/// world = inverseBind[joint] * boneAbsolute[joint] * placement * post, which is the
/// single-influence case of the skinning sum, so the existing
/// KnifePbrRenderer.TryDrawSkinned draws it unchanged.
/// </summary>
public sealed class Cs2RigidMesh {
    public sealed class Part {
        public int Joint;
        public string Material;
        public int[] Indices;
    }

    public string[] Joints;
    public Matrix[] InverseBind;
    /// <summary>Bind-pose vertices, shared by every rigid part. Never modified.</summary>
    public Cs2SkinnedMesh.Vertex[] Vertices;
    public Part[] Parts;

    /// <summary>The triangles that really do blend, if any. Skinned per frame.</summary>
    public Cs2SkinnedMesh.Vertex[] BlendedBind;
    public byte[] BlendedJoints;
    public float[] BlendedWeights;
    public Cs2SkinnedMesh.Primitive[] BlendedParts;
    Cs2SkinnedMesh.Vertex[] m_blendedSkinned;

    Matrix[] m_bone;
    /// <summary>Joints the rig has not got, drawn on the weapon root instead.</summary>
    public string[] Substituted { get; private set; } = [];

    /// <summary>
    /// Bones a piece falls back to when its own is not animated. weapon_offset is the
    /// root of every CS2 gun's own skeleton; weapon and root_motion are its parents.
    /// </summary>
    static readonly string[] RootBones = ["weapon_offset", "weapon", "root_motion"];

    public int VertexCount => Vertices.Length;
    public int TriangleCount => Parts.Sum(p => p.Indices.Length) / 3
                                + (BlendedParts?.Sum(p => p.Indices.Length) / 3 ?? 0);
    public int BlendedTriangleCount => BlendedParts?.Sum(p => p.Indices.Length) / 3 ?? 0;
    public Cs2SkinnedMesh.Vertex[] BlendedSkinned => m_blendedSkinned;

    static readonly ScResourceCache<string, Cs2RigidMesh> s_cache = new("rigid-weapons", 8);

    /// <summary>Loads the .cs2.parts a gun's rig names, or null when it has none.</summary>
    public static Cs2RigidMesh For(string asset) {
        if (asset is null) return null;
        if (s_cache.TryGetValue(asset, out Cs2RigidMesh hit)) return hit;
        Cs2RigidMesh mesh = null;
        string resource = Cs2Rig.PartsResource(asset);
        if (!string.IsNullOrEmpty(resource)) {
            try {
                mesh = Load("AnimationData." + resource);
                KnifeLog.Information(
                    $"[ScCsgoKnives] CS2 gun mesh {asset}: {mesh.Joints.Length} joints, "
                    + $"{mesh.VertexCount} rigid vertices, {mesh.Parts.Length} parts "
                    + $"[{string.Join(',', mesh.Parts.Select(p => mesh.Joints[p.Joint]))}], "
                    + $"{mesh.BlendedTriangleCount} blended triangles.");
            }
            catch (Exception e) {
                KnifeDiagnostics.WarnOnce($"cs2-parts-{asset}", $"Could not read {resource}: {e.Message}");
            }
        }
        s_cache[asset] = mesh;
        return mesh;
    }

    static Cs2RigidMesh Load(string resource) {
        Assembly assembly = typeof(Cs2RigidMesh).Assembly;
        string name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resource, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Missing embedded {resource}.");
        using Stream stream = assembly.GetManifestResourceStream(name);
        using BinaryReader reader = new(stream);
        if (new string(reader.ReadChars(8)) != "SCK2PART") throw new InvalidDataException("bad magic");
        if (reader.ReadUInt32() != 1u) throw new InvalidDataException("unsupported version");

        var mesh = new Cs2RigidMesh();
        int joints = reader.ReadUInt16();
        mesh.Joints = new string[joints];
        mesh.InverseBind = new Matrix[joints];
        for (int i = 0; i < joints; i++) {
            mesh.Joints[i] = ReadString(reader);
            mesh.InverseBind[i] = ReadMatrix(reader);
        }
        mesh.m_bone = new Matrix[joints];

        int vertices = reader.ReadInt32();
        mesh.Vertices = new Cs2SkinnedMesh.Vertex[vertices];
        for (int i = 0; i < vertices; i++) mesh.Vertices[i] = ReadVertex(reader);

        int parts = reader.ReadUInt16();
        mesh.Parts = new Part[parts];
        for (int i = 0; i < parts; i++) {
            int joint = reader.ReadUInt16();
            string material = ReadString(reader);
            int count = reader.ReadInt32();
            int[] indices = new int[count];
            for (int k = 0; k < count; k++) indices[k] = (int)reader.ReadUInt32();
            mesh.Parts[i] = new Part { Joint = joint, Material = material, Indices = indices };
        }

        int blended = reader.ReadInt32();
        if (blended > 0) {
            mesh.BlendedBind = new Cs2SkinnedMesh.Vertex[blended];
            mesh.BlendedJoints = new byte[blended * 4];
            mesh.BlendedWeights = new float[blended * 4];
            for (int i = 0; i < blended; i++) {
                mesh.BlendedBind[i] = ReadVertex(reader);
                for (int k = 0; k < 4; k++) mesh.BlendedJoints[i * 4 + k] = reader.ReadByte();
                for (int k = 0; k < 4; k++) mesh.BlendedWeights[i * 4 + k] = reader.ReadSingle();
            }
            mesh.m_blendedSkinned = new Cs2SkinnedMesh.Vertex[blended];
        }
        int blendedParts = reader.ReadUInt16();
        mesh.BlendedParts = new Cs2SkinnedMesh.Primitive[blendedParts];
        for (int i = 0; i < blendedParts; i++) {
            string material = ReadString(reader);
            int count = reader.ReadInt32();
            int[] indices = new int[count];
            for (int k = 0; k < count; k++) indices[k] = (int)reader.ReadUInt32();
            mesh.BlendedParts[i] = new Cs2SkinnedMesh.Primitive { Material = material, Indices = indices };
        }
        return mesh;
    }

    static string ReadString(BinaryReader reader) =>
        System.Text.Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadUInt16()));

    static Cs2SkinnedMesh.Vertex ReadVertex(BinaryReader reader) => new() {
        Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
        Normal = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
        TextureCoordinate = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
    };

    static Matrix ReadMatrix(BinaryReader reader) {
        Matrix m = default;
        m.M11 = reader.ReadSingle(); m.M12 = reader.ReadSingle(); m.M13 = reader.ReadSingle(); m.M14 = reader.ReadSingle();
        m.M21 = reader.ReadSingle(); m.M22 = reader.ReadSingle(); m.M23 = reader.ReadSingle(); m.M24 = reader.ReadSingle();
        m.M31 = reader.ReadSingle(); m.M32 = reader.ReadSingle(); m.M33 = reader.ReadSingle(); m.M34 = reader.ReadSingle();
        m.M41 = reader.ReadSingle(); m.M42 = reader.ReadSingle(); m.M43 = reader.ReadSingle(); m.M44 = reader.ReadSingle();
        return m;
    }

    /// <summary>
    /// Per-joint matrices for one pose. Guns carry no TWIST bones - those are the arms'
    /// AnimConstraintTiltTwist - so a plain lookup is enough and a joint the rig has not
    /// got leaves an all-zero matrix, which PartWorld reports as unusable.
    /// </summary>
    public bool SetPose(Cs2Rig.Pose pose, Matrix placement) {
        if (pose is null) return false;

        // A piece whose bone the clips do not animate - the M4A4's `sight` is one -
        // is rigidly attached to the weapon, so the weapon root's own matrix places it
        // exactly. The vertices are in bind-world space, so
        //   InverseBind[j] * absolute[j] = InverseBind[j] * bindLocal(j->root) * absolute[root]
        // and bindLocal(j->root) = inverse(InverseBind[j]) * InverseBind[root], which
        // cancels the InverseBind[j] and leaves the root's matrix. Dropping the part
        // instead, which is what a missing bone used to do, deleted the sight.
        Matrix root = default;
        foreach (string name in RootBones) {
            if (pose.Bones.TryGetValue(name, out Matrix rootAbsolute)) {
                int index = Array.IndexOf(Joints, name);
                root = index >= 0 ? InverseBind[index] * rootAbsolute * placement
                                  : rootAbsolute * placement;
                break;
            }
        }

        int resolved = 0;
        List<string> substituted = null;
        for (int i = 0; i < Joints.Length; i++) {
            if (pose.Bones.TryGetValue(Joints[i], out Matrix absolute)) {
                m_bone[i] = InverseBind[i] * absolute * placement;
                resolved++;
                continue;
            }
            m_bone[i] = root;
            if (root.M44 != 0f) {
                (substituted ??= []).Add(Joints[i]);
            }
        }
        Substituted = substituted?.ToArray() ?? [];
        return resolved > 0;
    }

    /// <summary>The world matrix for a part, or false when its bone is not in the rig.</summary>
    public bool TryPartWorld(Part part, out Matrix world) {
        world = m_bone[part.Joint];
        return world.M44 != 0f;
    }

    /// <summary>Skin the blended residue. Cheap: at most 183 triangles, on one gun.</summary>
    public void SkinBlended() {
        if (BlendedBind is null) return;
        for (int v = 0; v < BlendedBind.Length; v++) {
            Vector3 p = BlendedBind[v].Position, n = BlendedBind[v].Normal;
            Vector3 sp = Vector3.Zero, sn = Vector3.Zero;
            int b = v * 4;
            for (int k = 0; k < 4; k++) {
                float w = BlendedWeights[b + k];
                if (w <= 0f) continue;
                ref Matrix m = ref m_bone[BlendedJoints[b + k]];
                if (m.M44 == 0f) continue;
                sp += w * Vector3.Transform(p, m);
                sn += w * Vector3.TransformNormal(n, m);
            }
            float length = sn.Length();
            m_blendedSkinned[v] = new Cs2SkinnedMesh.Vertex {
                Position = sp,
                Normal = length > 1e-6f ? sn / length : Vector3.UnitY,
                TextureCoordinate = BlendedBind[v].TextureCoordinate,
            };
        }
    }
}
