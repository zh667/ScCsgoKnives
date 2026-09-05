using System.IO;
using System.Reflection;
using Engine;
using Engine.Graphics;

namespace Game;

/// <summary>
/// CS2's arms and gloves, skinned on the CPU.
///
/// Survivalcraft has no GPU skinning, so every frame each vertex is transformed by
/// its four bone matrices and the result handed to Display.DrawUserIndexed. The two
/// primitives (bare arm, fingerless glove) index one shared vertex block, so that is
/// 6274 transforms per frame, not 12548.
///
///     view = sum_j w_j * (vertex * inverseBind_j * boneAbsolute_j)
///
/// The asset (AnimationData/cs2_arms.skin, tools/cs2_glb_to_skinned.py) already
/// carries the inverse binds scaled to the rig's inches, so nothing is converted
/// here. Bone absolutes come from <see cref="Cs2Rig"/>; the four forearm twist
/// bones are synthesised, see <see cref="Twist"/>.
/// </summary>
public sealed class Cs2SkinnedMesh {
    public struct Vertex {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TextureCoordinate;
    }

    public sealed class Primitive {
        public string Material;
        public int[] Indices;
    }

    public static readonly VertexDeclaration Declaration = new(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementSemantic.Position),
        new VertexElement(12, VertexElementFormat.Vector3, VertexElementSemantic.Normal),
        new VertexElement(24, VertexElementFormat.Vector2, VertexElementSemantic.TextureCoordinate));

    public string[] Joints;
    public Matrix[] InverseBind;
    public Primitive[] Primitives;

    Vector3[] m_position;
    Vector3[] m_normal;
    Vector2[] m_uv;
    byte[] m_bones;          // 4 joint indices per vertex
    float[] m_weights;       // 4 weights per vertex
    Vertex[] m_skinned;
    Matrix[] m_bone;         // inverseBind * absolute, per joint, rebuilt per frame

    /// <summary>
    /// CS2's forearm twist bones. weapon_arms.vmdl drives each with an
    /// AnimConstraintTiltTwist: slave arm_lower_&lt;side&gt;_TWIST at weight 0.5 and
    /// _TWIST1 at 1.0, input the matching hand, input and slave axis 0 (the bone's
    /// own X). Those weights are read from the file. The reading of "tilt twist" as
    /// a swing-twist split of the hand's rotation about X, with the slave taking
    /// `weight` of the twist, is this port's interpretation - Valve's constraint
    /// source is not available - and is what the stage 4 report marks as the one
    /// modelled behaviour here.
    /// </summary>
    static readonly (string Bone, string Parent, string Input, float Weight)[] Twist = [
        ("arm_lower_L_TWIST", "arm_lower_L", "hand_L", 0.5f),
        ("arm_lower_L_TWIST1", "arm_lower_L", "hand_L", 1.0f),
        ("arm_lower_R_TWIST", "arm_lower_R", "hand_R", 0.5f),
        ("arm_lower_R_TWIST1", "arm_lower_R", "hand_R", 1.0f),
    ];

    const string Resource = "AnimationData.cs2_arms.skin";
    static Cs2SkinnedMesh s_arms;
    static bool s_tried;
    static readonly Dictionary<string, Cs2SkinnedMesh> s_weapons = new(StringComparer.Ordinal);

    /// <summary>
    /// A weapon that ships as one skinned mesh, by asset name. All 22 knives are:
    /// CS2 gives each a single primitive whose moving parts ride bones inside the
    /// clip's own skeleton - the butterfly weights blade, lock and rear, the folders
    /// weight blade, the push dagger weapon_l and weapon_r - so there is nothing to
    /// bind as a separate mesh part the way the guns' rigid pieces are.
    ///
    /// Null for the guns, which have no Skinned entry in their rig.
    /// </summary>
    public static Cs2SkinnedMesh Weapon(string asset) {
        if (asset is null) return null;
        if (s_weapons.TryGetValue(asset, out Cs2SkinnedMesh hit)) return hit;
        Cs2SkinnedMesh mesh = null;
        string resource = Cs2Rig.SkinnedResource(asset);
        if (!string.IsNullOrEmpty(resource)) {
            try {
                mesh = Load("AnimationData." + resource);
                KnifeLog.Information(
                    $"[ScCsgoKnives] CS2 weapon mesh {asset}: {mesh.Joints.Length} joints, "
                    + $"{mesh.Skinned.Length} vertices, "
                    + $"{string.Join(", ", mesh.Primitives.Select(p => $"{p.Material} {p.Indices.Length / 3}t"))}."
                );
            }
            catch (Exception e) {
                KnifeDiagnostics.WarnOnce($"cs2-weapon-mesh-{asset}",
                    $"Could not read {resource}: {e.Message}");
            }
        }
        s_weapons[asset] = mesh;
        return mesh;
    }

    public static Cs2SkinnedMesh Arms {
        get {
            if (s_tried) return s_arms;
            s_tried = true;
            try {
                s_arms = Load(Resource);
                KnifeLog.Information(
                    $"[ScCsgoKnives] CS2 arms: {s_arms.Joints.Length} joints, "
                    + $"{s_arms.Skinned.Length} shared vertices, "
                    + $"{string.Join(", ", s_arms.Primitives.Select(p => $"{p.Material} {p.Indices.Length / 3}t"))}."
                );
            }
            catch (Exception e) {
                KnifeDiagnostics.WarnOnce("cs2-arms", $"Could not read {Resource}: {e.Message}");
            }
            return s_arms;
        }
    }

    static Cs2SkinnedMesh Load(string resource) {
        Assembly assembly = typeof(Cs2SkinnedMesh).Assembly;
        string name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(resource, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Missing embedded {resource}.");
        using Stream stream = assembly.GetManifestResourceStream(name);
        using BinaryReader reader = new(stream);
        if (new string(reader.ReadChars(8)) != "SCK2SKIN") throw new InvalidDataException("bad magic");
        if (reader.ReadUInt32() != 2u) throw new InvalidDataException("unsupported version");

        var mesh = new Cs2SkinnedMesh();
        int joints = reader.ReadUInt16();
        mesh.Joints = new string[joints];
        mesh.InverseBind = new Matrix[joints];
        for (int i = 0; i < joints; i++) {
            mesh.Joints[i] = ReadString(reader);
            mesh.InverseBind[i] = ReadMatrix(reader);
        }
        mesh.m_bone = new Matrix[joints];

        int vertexCount = reader.ReadInt32();
        mesh.m_position = new Vector3[vertexCount];
        mesh.m_normal = new Vector3[vertexCount];
        mesh.m_uv = new Vector2[vertexCount];
        mesh.m_bones = new byte[vertexCount * 4];
        mesh.m_weights = new float[vertexCount * 4];
        for (int v = 0; v < vertexCount; v++) {
            mesh.m_position[v] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            mesh.m_normal[v] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            mesh.m_uv[v] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            for (int k = 0; k < 4; k++) mesh.m_bones[v * 4 + k] = reader.ReadByte();
            for (int k = 0; k < 4; k++) mesh.m_weights[v * 4 + k] = reader.ReadSingle();
        }

        int primitives = reader.ReadUInt16();
        mesh.Primitives = new Primitive[primitives];
        for (int p = 0; p < primitives; p++) {
            string material = ReadString(reader);
            int indexCount = reader.ReadInt32();
            int[] indices = new int[indexCount];
            for (int i = 0; i < indexCount; i++) indices[i] = (int)reader.ReadUInt32();
            mesh.Primitives[p] = new Primitive { Material = material, Indices = indices };
        }
        mesh.m_skinned = new Vertex[vertexCount];
        return mesh;
    }

    static string ReadString(BinaryReader reader) {
        int length = reader.ReadUInt16();
        return System.Text.Encoding.UTF8.GetString(reader.ReadBytes(length));
    }

    static Matrix ReadMatrix(BinaryReader reader) {
        Matrix m = default;
        m.M11 = reader.ReadSingle(); m.M12 = reader.ReadSingle(); m.M13 = reader.ReadSingle(); m.M14 = reader.ReadSingle();
        m.M21 = reader.ReadSingle(); m.M22 = reader.ReadSingle(); m.M23 = reader.ReadSingle(); m.M24 = reader.ReadSingle();
        m.M31 = reader.ReadSingle(); m.M32 = reader.ReadSingle(); m.M33 = reader.ReadSingle(); m.M34 = reader.ReadSingle();
        m.M41 = reader.ReadSingle(); m.M42 = reader.ReadSingle(); m.M43 = reader.ReadSingle(); m.M44 = reader.ReadSingle();
        return m;
    }

    /// <summary>Rebuild the per-joint skinning matrices for one pose. Returns false if the rig has none of them.</summary>
    public bool SetPose(Cs2Rig.Pose pose, Matrix placement) {
        if (pose is null) return false;
        int resolved = 0;
        for (int i = 0; i < Joints.Length; i++) {
            if (!TryBone(pose, Joints[i], out Matrix absolute)) {
                m_bone[i] = default;                    // all-zero: contributes nothing
                continue;
            }
            m_bone[i] = InverseBind[i] * absolute * placement;
            resolved++;
        }
        return resolved > 0;
    }

    public float UnresolvedWeight(Cs2Rig.Pose pose) {
        float total=0;
        for (int i=0;i<m_bones.Length;i++) if (m_weights[i]>0 && !TryBone(pose,Joints[m_bones[i]],out _)) total+=m_weights[i];
        return total;
    }

    public bool VertexUsesJoint(int vertex,Func<string,bool> predicate) {
        for (int k=0;k<4;k++) { int i=vertex*4+k;if (m_weights[i]>0 && predicate(Joints[m_bones[i]])) return true; }
        return false;
    }

    static bool TryBone(Cs2Rig.Pose pose, string name, out Matrix absolute) {
        if (pose.Bones.TryGetValue(name, out absolute)) return true;
        foreach ((string bone, string parent, string input, float weight) in Twist) {
            if (bone != name) continue;
            if (!pose.Bones.TryGetValue(parent, out Matrix parentAbsolute)
                || !pose.Bones.TryGetValue(input, out Matrix inputAbsolute)) break;
            Cs2SkinnedMesh mesh = s_arms;
            int index = mesh is null ? -1 : Array.IndexOf(mesh.Joints, bone);
            int parentIndex = mesh is null ? -1 : Array.IndexOf(mesh.Joints, parent);
            if (index < 0 || parentIndex < 0) break;
            // Rest pose of the twist bone in its parent's frame, from the bind matrices.
            Matrix rest = Matrix.Invert(mesh.InverseBind[index]) * mesh.InverseBind[parentIndex];
            Quaternion local = Quaternion.CreateFromRotationMatrix(inputAbsolute * Matrix.Invert(parentAbsolute));
            // Swing-twist split about X: the twist part keeps only the x and w terms.
            var twist = new Quaternion(local.X, 0f, 0f, local.W);
            float length = twist.Length();
            twist = length > 1e-6f ? twist / length : Quaternion.Identity;
            if (twist.W < 0f) twist = new Quaternion(-twist.X, -twist.Y, -twist.Z, -twist.W);
            absolute = rest * Matrix.CreateFromQuaternion(Quaternion.Slerp(Quaternion.Identity, twist, weight)) * parentAbsolute;
            return true;
        }
        return false;
    }

    /// <summary>Skin every vertex into the reusable array. Call after SetPose.</summary>
    public void Skin() {
        for (int v = 0; v < m_position.Length; v++) {
            Vector3 p = m_position[v];
            Vector3 n = m_normal[v];
            Vector3 sp = Vector3.Zero;
            Vector3 sn = Vector3.Zero;
            int b = v * 4;
            for (int k = 0; k < 4; k++) {
                float w = m_weights[b + k];
                if (w <= 0f) continue;
                ref Matrix m = ref m_bone[m_bones[b + k]];
                if (m.M44 == 0f) continue;              // joint not in the rig
                sp += w * Vector3.Transform(p, m);
                sn += w * Vector3.TransformNormal(n, m);
            }
            float length = sn.Length();
            m_skinned[v] = new Vertex {
                Position = sp,
                Normal = length > 1e-6f ? sn / length : Vector3.UnitY,
                TextureCoordinate = m_uv[v]
            };
        }
    }

    public Vertex[] Skinned => m_skinned;
}
