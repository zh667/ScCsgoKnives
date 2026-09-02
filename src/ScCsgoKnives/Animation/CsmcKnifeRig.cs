using System.IO;
using System.Reflection;
using System.Text.Json;
using Engine;

namespace Game;

/// <summary>
/// Exact runtime representation of CSMC's Source2 weapon animation pipeline.
/// Matrices stored by CSMC are JOML column matrices. Reading their column-major
/// arrays into Engine's row matrices transposes them, which also reverses every
/// multiplication in the same way as CSMC's renderer.
/// </summary>
public static class CsmcKnifeRig {
    sealed class Curve {
        public string Interpolation { get; set; }
        public float[] Times { get; set; }
        public float[][] Values { get; set; }

        public Vector3 SampleVector(float time, Vector3 fallback) {
            if (Times is null || Values is null || Times.Length == 0 || Values.Length == 0) return fallback;
            (int lo, int hi, float factor) = FindKeys(Times, time);
            Vector3 a = ReadVector(Values[lo], fallback);
            Vector3 b = ReadVector(Values[hi], fallback);
            return lo == hi ? a : Vector3.Lerp(a, b, factor);
        }

        public Quaternion SampleQuaternion(float time, Quaternion fallback) {
            if (Times is null || Values is null || Times.Length == 0 || Values.Length == 0) return fallback;
            (int lo, int hi, float factor) = FindKeys(Times, time);
            Quaternion a = ReadQuaternion(Values[lo], fallback);
            Quaternion b = ReadQuaternion(Values[hi], fallback);
            return lo == hi ? a : Quaternion.Normalize(Quaternion.Slerp(a, b, factor));
        }
    }

    sealed class BoneCurves {
        public Curve Rotation { get; set; }
        public Curve Translation { get; set; }
        public Curve Scale { get; set; }
    }

    sealed class Clip {
        public string SourceName { get; set; }
        public float Duration { get; set; }
        public Dictionary<string, BoneCurves> Bones { get; set; }
    }

    sealed class SkeletonBone {
        public int Index { get; set; }
        public string Name { get; set; }
        public int Parent { get; set; }
        public int[] Children { get; set; }
        public float[] Matrix { get; set; }
        public float[] Translation { get; set; }
        public float[] Rotation { get; set; }
        public float[] Scale { get; set; }
    }

    sealed class Binding {
        public string Name { get; set; }
        public int BoneIndex { get; set; }
        public float[] RightMatrix { get; set; }
        public float[] ReferenceMatrix { get; set; }
        public float[] LeftMatrix { get; set; }
    }

    sealed class AnimationFile {
        public string Format { get; set; }
        public float[] MeshCenter { get; set; }
        public float MeshNormalizationScale { get; set; }
        public float SourceReferenceScale { get; set; }
        public string[] MeshParts { get; set; }
        public List<Binding> Bindings { get; set; }
        public List<SkeletonBone> Skeleton { get; set; }
        public Dictionary<string, Clip> Clips { get; set; }
    }

    sealed class Asset {
        public string Name;
        public AnimationFile File;
        public Dictionary<string, int> BoneIndices;
        public Dictionary<string, Binding> Bindings;
        public Matrix Normalization;
        public Matrix InverseNormalization;
    }

    readonly struct SourcePose {
        public readonly Vector3 Translation;
        public readonly Quaternion Rotation;
        public readonly Vector3 Scale;

        public SourcePose(Vector3 translation, Quaternion rotation, Vector3 scale) {
            Translation = translation;
            Rotation = rotation;
            Scale = scale;
        }
    }

    sealed class ManifestEntry {
        public string Name { get; set; }
        public string[] MeshParts { get; set; }
        public float SourceReferenceScale { get; set; }
    }

    static readonly ManifestEntry[] s_manifest = LoadManifest();
    static readonly string[] s_names = s_manifest.Select(entry => entry.Name).ToArray();

    public static int AssetCount => s_names.Length;
    // Rigs are deserialised on first use: all twenty-two together are 20 MB of
    // animation curves, and a player only ever holds one knife at a time.
    static readonly Asset[] s_assets = new Asset[s_names.Length];

    public static string GetAssetName(int variant) => Entry(variant).Name;

    /// <summary>Mesh records this weapon draws, in CSMC's own draw order.</summary>
    public static IReadOnlyList<string> GetMeshParts(int variant) => Entry(variant).MeshParts;

    /// <summary>
    /// Largest extent of the untouched CSMC mesh. Every record is normalised to
    /// the same 1.25 unit box, so this is the only thing that still says how
    /// long the weapon really is, and it is what keeps a kukri from ending up
    /// the same size as a pair of shadow daggers.
    /// </summary>
    public static float GetSourceReferenceScale(int variant) => Entry(variant).SourceReferenceScale;

    static ManifestEntry Entry(int variant) => s_manifest[Math.Clamp(variant, 0, s_manifest.Length - 1)];

    public static float GetDuration(int variant, string clipAlias) {
        Asset asset = GetAsset(variant);
        return asset.File.Clips.TryGetValue(clipAlias, out Clip clip) ? clip.Duration : 0f;
    }

    public static bool HasClip(int variant, string clipAlias) => GetAsset(variant).File.Clips.ContainsKey(clipAlias);

    public static KnifeRigPose Sample(int variant, string clipAlias, float time, bool loop = false) {
        Asset asset = GetAsset(variant);
        if (!asset.File.Clips.TryGetValue(clipAlias, out Clip clip)) clip = asset.File.Clips["idle"];
        if (loop && clip.Duration > 0f) {
            time %= clip.Duration;
            if (time < 0f) time += clip.Duration;
        }
        else time = MathUtils.Clamp(time, 0f, Math.Max(0f, clip.Duration));

        Matrix[] absolute = CalculateAbsolute(asset, clip, time);
        var parts = new Dictionary<string, Matrix>(StringComparer.Ordinal);
        var bones = new Dictionary<string, Matrix>(StringComparer.Ordinal);
        var attachments = new Dictionary<string, Vector3>(StringComparer.Ordinal);
        foreach (SkeletonBone bone in asset.File.Skeleton) {
            if (bone.Index < 0 || bone.Index >= absolute.Length) continue;
            // Bone poses do not contain the mesh-record unit conversion found
            // in Binding.LeftMatrix. They are the correct attachment frames
            // for external geometry such as Survivalcraft's player arms.
            Matrix normalizedBone = asset.InverseNormalization * absolute[bone.Index] * asset.Normalization;
            if (KnifeDiagnostics.IsFinite(normalizedBone)) bones[bone.Name] = normalizedBone;
            else KnifeDiagnostics.WarnOnce($"rig-{asset.Name}-bone-{bone.Name}-invalid", $"CSMC bone {asset.Name}/{bone.Name} produced a non-finite matrix.");

            // CSMC b$4jb/b$4jy does not use a bone matrix origin as the
            // first-person arm endpoint. It takes the animated absolute bone
            // translation, converts Source's XYZ basis to mesh ZXY, divides
            // the 0.0254 metre conversion, then applies the mesh
            // normalization matrix. Keeping this separate is essential:
            // conjugating the bone matrix put hand_r/hand_l behind SC's
            // camera even while the knife itself was visible.
            Vector3 sourceTranslation = new(absolute[bone.Index].M41, absolute[bone.Index].M42, absolute[bone.Index].M43);
            Vector3 meshPoint = new(
                sourceTranslation.Z / 0.0254f,
                sourceTranslation.X / 0.0254f,
                sourceTranslation.Y / 0.0254f
            );
            Vector3 normalizedPoint = Vector3.Transform(meshPoint, asset.Normalization);
            if (float.IsFinite(normalizedPoint.X) && float.IsFinite(normalizedPoint.Y) && float.IsFinite(normalizedPoint.Z))
                attachments[bone.Name] = normalizedPoint;
        }
        foreach (Binding binding in asset.File.Bindings) {
            if (binding.BoneIndex < 0 || binding.BoneIndex >= absolute.Length) continue;
            // CSMC/JOML: left * boneAbsolute * right.
            // Engine row-vector transpose: right^T * boneAbsolute^T * left^T.
            Matrix sourcePose = ReadSourceMatrix(binding.RightMatrix)
                * absolute[binding.BoneIndex]
                * ReadSourceMatrix(binding.LeftMatrix);
            Matrix normalizedPose = asset.InverseNormalization * sourcePose * asset.Normalization;
            if (KnifeDiagnostics.IsFinite(normalizedPose)) parts[binding.Name] = normalizedPose;
            else KnifeDiagnostics.WarnOnce($"rig-{asset.Name}-{binding.Name}-invalid", $"CSMC binding {asset.Name}/{binding.Name} produced a non-finite matrix.");
        }
        return new KnifeRigPose(asset.Name, clipAlias, clip.SourceName, clip.Duration, asset.File.SourceReferenceScale, parts, bones, attachments);
    }

    static Matrix[] CalculateAbsolute(Asset asset, Clip clip, float time) {
        Matrix[] local = new Matrix[asset.File.Skeleton.Count];
        Matrix[] absolute = new Matrix[local.Length];
        bool[] complete = new bool[local.Length];
        bool[] active = new bool[local.Length];
        for (int i = 0; i < local.Length; i++) local[i] = SampleLocal(asset.File.Skeleton[i], clip, time);
        for (int i = 0; i < local.Length; i++) Calculate(i);
        return absolute;

        Matrix Calculate(int index) {
            if (complete[index]) return absolute[index];
            if (active[index]) throw new InvalidDataException($"CSMC skeleton cycle at bone {index}.");
            active[index] = true;
            SkeletonBone bone = asset.File.Skeleton[index];
            absolute[index] = bone.Parent >= 0 && bone.Parent < local.Length
                ? local[index] * Calculate(bone.Parent)
                : local[index];
            active[index] = false;
            complete[index] = true;
            return absolute[index];
        }
    }

    static Matrix SampleLocal(SkeletonBone bone, Clip clip, float time) {
        SourcePose rest = new(
            ReadVector(bone.Translation, Vector3.Zero),
            ReadQuaternion(bone.Rotation, Quaternion.Identity),
            ReadVector(bone.Scale, Vector3.One)
        );
        if (clip.Bones is null || !clip.Bones.TryGetValue(bone.Name, out BoneCurves curves))
            return bone.Matrix is { Length: >= 16 } ? ReadSourceMatrix(bone.Matrix) : CreateMatrix(rest);
        SourcePose pose = new(
            curves.Translation?.SampleVector(time, rest.Translation) ?? rest.Translation,
            curves.Rotation?.SampleQuaternion(time, rest.Rotation) ?? rest.Rotation,
            curves.Scale?.SampleVector(time, rest.Scale) ?? rest.Scale
        );
        return CreateMatrix(pose);
    }

    static Matrix CreateMatrix(SourcePose pose) =>
        Matrix.CreateScale(pose.Scale)
        * Matrix.CreateFromQuaternion(pose.Rotation)
        * Matrix.CreateTranslation(pose.Translation);

    // A JOML float[16] is column-major. Sequential assignment to Engine's
    // row-major fields intentionally returns the transpose used by row vectors.
    static Matrix ReadSourceMatrix(float[] values) {
        if (values is not { Length: >= 16 }) return Matrix.Identity;
        return new Matrix {
            M11 = values[0], M12 = values[1], M13 = values[2], M14 = values[3],
            M21 = values[4], M22 = values[5], M23 = values[6], M24 = values[7],
            M31 = values[8], M32 = values[9], M33 = values[10], M34 = values[11],
            M41 = values[12], M42 = values[13], M43 = values[14], M44 = values[15]
        };
    }

    static Asset Load(string name) {
        Assembly assembly = typeof(CsmcKnifeRig).Assembly;
        string suffix = $"AnimationData.{name}.csmc.animation.json";
        string resource = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (resource is null) throw new InvalidOperationException($"Missing embedded CSMC rig resource {suffix}.");
        using Stream stream = assembly.GetManifestResourceStream(resource);
        AnimationFile file = JsonSerializer.Deserialize<AnimationFile>(stream);
        if (file?.Format != "ScCsgoKnives.CsmcAnimation/2" || file.Skeleton is null || file.Bindings is null || file.Clips is null || !file.Clips.ContainsKey("idle"))
            throw new InvalidDataException($"Invalid CSMC rig resource {suffix}.");
        Vector3 center = ReadVector(file.MeshCenter, Vector3.Zero);
        Matrix normalization = Matrix.CreateTranslation(-center) * Matrix.CreateScale(file.MeshNormalizationScale);
        Asset asset = new() {
            Name = name,
            File = file,
            BoneIndices = file.Skeleton.ToDictionary(bone => bone.Name, bone => bone.Index, StringComparer.Ordinal),
            Bindings = file.Bindings.ToDictionary(binding => binding.Name, StringComparer.Ordinal),
            Normalization = normalization,
            InverseNormalization = Matrix.Invert(normalization)
        };
        Log.Information(
            $"[ScCsgoKnives] exact CSMC rig {name}: format={file.Format}, parts=[{string.Join(',', file.MeshParts)}], "
            + $"bindings={file.Bindings.Count}, bones={file.Skeleton.Count}, clips=[{string.Join(',', file.Clips.Keys)}], "
            + $"normalizationCenter=({center.X:0.###},{center.Y:0.###},{center.Z:0.###}), normalizationScale={file.MeshNormalizationScale:0.######}."
        );
        return asset;
    }

    static Asset GetAsset(int variant) {
        int index = Math.Clamp(variant, 0, s_assets.Length - 1);
        return s_assets[index] ??= Load(s_names[index]);
    }

    static ManifestEntry[] LoadManifest() {
        Assembly assembly = typeof(CsmcKnifeRig).Assembly;
        string resource = assembly.GetManifestResourceNames().First(n => n.EndsWith("AnimationData.knives.json", StringComparison.OrdinalIgnoreCase));
        using Stream stream = assembly.GetManifestResourceStream(resource);
        ManifestEntry[] entries = JsonSerializer.Deserialize<ManifestEntry[]>(stream)
            ?? throw new InvalidDataException("Empty ScCsgoKnives rig manifest.");
        Log.Information($"[ScCsgoKnives] rig manifest: {entries.Length} knives = [{string.Join(",", entries.Select(e => e.Name))}].");
        return entries;
    }

    static (int Lo, int Hi, float Factor) FindKeys(float[] times, float time) {
        if (times.Length == 1 || time <= times[0]) return (0, 0, 0f);
        int last = times.Length - 1;
        if (time >= times[last]) return (last, last, 0f);
        int hi = Array.BinarySearch(times, time);
        if (hi >= 0) return (hi, hi, 0f);
        hi = ~hi;
        int lo = hi - 1;
        float factor = (time - times[lo]) / Math.Max(0.000001f, times[hi] - times[lo]);
        return (lo, hi, factor);
    }

    static Vector3 ReadVector(float[] values, Vector3 fallback) =>
        values is { Length: >= 3 } ? new Vector3(values[0], values[1], values[2]) : fallback;

    static Quaternion ReadQuaternion(float[] values, Quaternion fallback) {
        if (values is not { Length: >= 4 }) return fallback;
        Quaternion value = new(values[0], values[1], values[2], values[3]);
        float lengthSquared = value.X * value.X + value.Y * value.Y + value.Z * value.Z + value.W * value.W;
        return float.IsFinite(lengthSquared) && lengthSquared > 0.000001f ? Quaternion.Normalize(value) : fallback;
    }
}

public sealed class KnifeRigPose {
    readonly IReadOnlyDictionary<string, Matrix> m_bindings;
    readonly IReadOnlyDictionary<string, Matrix> m_bones;
    readonly IReadOnlyDictionary<string, Vector3> m_attachments;

    public KnifeRigPose(string assetName, string clipAlias, string sourceClip, float duration, float sourceReferenceScale, IReadOnlyDictionary<string, Matrix> bindings, IReadOnlyDictionary<string, Matrix> bones, IReadOnlyDictionary<string, Vector3> attachments) {
        AssetName = assetName;
        ClipAlias = clipAlias;
        SourceClip = sourceClip;
        Duration = duration;
        SourceReferenceScale = sourceReferenceScale;
        m_bindings = bindings;
        m_bones = bones;
        m_attachments = attachments;
    }

    public string AssetName { get; }
    public string ClipAlias { get; }
    public string SourceClip { get; }
    public float Duration { get; }
    public float SourceReferenceScale { get; }
    public IReadOnlyDictionary<string, Matrix> Bindings => m_bindings;
    public IReadOnlyDictionary<string, Matrix> Bones => m_bones;
    public IReadOnlyDictionary<string, Vector3> Attachments => m_attachments;

    public Matrix GetBinding(string name) => m_bindings.TryGetValue(name, out Matrix value) ? value : Matrix.Identity;
    public Matrix GetBone(string name) => m_bones.TryGetValue(name, out Matrix value) ? value : Matrix.Identity;
    public Vector3 GetAttachment(string name) => m_attachments.TryGetValue(name, out Vector3 value) ? value : Vector3.Zero;
    public Vector3 GetBindingOrigin(string name) {
        Matrix value = GetBinding(name);
        return new Vector3(value.M41, value.M42, value.M43);
    }
}
