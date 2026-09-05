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
        /// <summary>Key into weapon_table.json (CS:MC's registration row); knives default to knife_&lt;name&gt;.</summary>
        public string Table { get; set; }
        public bool IsGun { get; set; }
        /// <summary>
        /// Drawn entirely from CS2: no CS:MC animation, no OBJ mesh parts. The guns
        /// added in 0.18.0 are all of these. They still need a manifest entry, because
        /// a variant number indexes the manifest and without one they resolved to the
        /// last gun in it - which is why the new guns came out as the AWP.
        /// </summary>
        public bool Cs2Only { get; set; }
    }

    /// <summary>
    /// One row of CS:MC's weapon registration table (decoded from the client jar by
    /// tools/apply_weapon_table.py): the first-person hip and aim offsets, the roll,
    /// and the hip and aim fields of view. Every weapon has its own row; the knives'
    /// rows carry hip only (aim = hip, no roll, one FOV).
    /// </summary>
    public sealed class WeaponTableEntry {
        public string Id { get; set; }
        public string Model { get; set; }
        public float[] Hip { get; set; }
        public float[] Aim { get; set; }
        public float RollDegrees { get; set; }
        public float FovHip { get; set; } = 48f;
        public float FovAim { get; set; } = 27f;
        public Vector3 HipOffset => Hip is { Length: >= 3 } ? new Vector3(Hip[0], Hip[1], Hip[2]) : Vector3.Zero;
        public Vector3 AimOffset => Aim is { Length: >= 3 } ? new Vector3(Aim[0], Aim[1], Aim[2]) : HipOffset;
    }

    static readonly Dictionary<string, WeaponTableEntry> s_table = LoadWeaponTable();

    static Dictionary<string, WeaponTableEntry> LoadWeaponTable() {
        Assembly assembly = typeof(CsmcKnifeRig).Assembly;
        string resource = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("AnimationData.weapon_table.json", StringComparison.OrdinalIgnoreCase));
        if (resource is null) return new Dictionary<string, WeaponTableEntry>(StringComparer.Ordinal);
        using Stream stream = assembly.GetManifestResourceStream(resource);
        return JsonSerializer.Deserialize<Dictionary<string, WeaponTableEntry>>(stream) ?? new Dictionary<string, WeaponTableEntry>(StringComparer.Ordinal);
    }

    /// <summary>CS:MC's registration row for this weapon; a row of zeros (and 48/27 degrees) when the table has none.</summary>
    public static WeaponTableEntry GetTable(int variant) {
        ManifestEntry entry = Entry(variant);
        string key = entry.Table ?? ("knife_" + entry.Name);
        if (s_table.TryGetValue(key, out WeaponTableEntry row)) return row;
        KnifeDiagnostics.WarnOnce($"table-missing-{key}", $"No CS:MC weapon table row for {key}; using zeros.");
        return new WeaponTableEntry { Hip = [0f, 0f, 0f], Aim = [0f, 0f, 0f] };
    }

    public static bool IsGun(int variant) => Entry(variant).IsGun;
    /// <summary>Knife variants come first in the combined manifest; guns follow.</summary>
    public static readonly string[] FrozenKnifeOrder = ["karambit", "m9", "butterfly", "bayonet", "bowie", "canis", "cord", "css", "default_ct", "default_t", "falchion", "flip", "gut", "kukri", "navaja", "outdoor", "push", "skeleton", "stiletto", "tactical", "talon", "ursus"];
    public static int KnifeCount => s_knifeCount;
    static int s_knifeCount;

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

    /// <summary>The mesh centre our OBJ export subtracted, in normalized mesh units (centre times the normalization scale).</summary>
    public static Vector3 GetMeshCenterOffset(int variant) {
        if (Entry(variant).Cs2Only) return Vector3.Zero;   // only the CS:MC exact chain reads it
        var file = GetAsset(variant).File;
        float[] c = file.MeshCenter;
        return c is { Length: >= 3 } ? new Vector3(c[0], c[1], c[2]) * file.MeshNormalizationScale : Vector3.Zero;
    }

    static ManifestEntry Entry(int variant) => s_manifest[Math.Clamp(variant, 0, s_manifest.Length - 1)];

    public static float GetDuration(int variant, string clipAlias) {
        // A CS2-only variant has no CS:MC clip; asking its CS:MC asset for one threw
        // "Missing embedded CSMC rig resource" out of the draw hook in 0.18.1.
        if (Entry(variant).Cs2Only) return Cs2Rig.Duration(Entry(variant).Name, clipAlias);
        Asset asset = GetAsset(variant);
        return asset.File.Clips.TryGetValue(clipAlias, out Clip clip) ? clip.Duration : 0f;
    }

    /// <summary>
    /// The clip length whichever profile is drawing owns, and the single source every
    /// consumer of timing must use: the animation controller's end-of-clip test, the
    /// gun's BusyUntil, and Cs2Rig's own sampling.
    ///
    /// The two rigs do not agree on length. Measured (tools/cs2_placement_selftest.py):
    /// the M4A1-S draw runs 1.1332 s in CS2 against 1.1000 in CS:MC, so its last frame
    /// was being cut; the AK reload is 2.4333 against 2.4667 and the AWP's shot 1.5999
    /// against 1.6667, so there the animation ended one or two frames before the state
    /// machine did. (The M4A1-S inspect is *not* affected: GetDuration returns CS:MC's
    /// Duration field, 5.3, not the 5.0 where its sampled curve stops.) Knives and
    /// GunProfile = 0 are unaffected: Cs2Placement.Active already requires a gun and the
    /// cs2 profile, so they fall through to GetDuration unchanged.
    /// </summary>
    public static float GetProfileDuration(int variant, string clipAlias) {
        if (Cs2Placement.Active(variant) || Entry(variant).Cs2Only) {
            float cs2 = Cs2Rig.Duration(GetAssetName(variant), clipAlias);
            if (cs2 > 0f) return cs2;
        }
        // A CS2-only variant has no CS:MC clip to fall back on; returning its CS:MC
        // duration would read another weapon's rig.
        return Entry(variant).Cs2Only ? 0f : GetDuration(variant, clipAlias);
    }

    public static bool HasClip(int variant, string clipAlias) =>
        Entry(variant).Cs2Only ? Cs2Rig.HasAlias(GetAssetName(variant), clipAlias)
            : TryGetAsset(variant)?.File.Clips.ContainsKey(clipAlias) ?? false;
    /// <summary>Every clip alias this knife's rig carries.</summary>
    public static IEnumerable<string> GetClipAliases(int variant) =>
        TryGetAsset(variant) is Asset asset ? asset.File.Clips.Keys : Enumerable.Empty<string>();

    /// <summary>
    /// Empty binding tables shared by every CS2-only pose; they never carry a matrix.
    /// </summary>
    static readonly IReadOnlyDictionary<string, Matrix> s_noMatrices = new Dictionary<string, Matrix>();
    static readonly IReadOnlyDictionary<string, Vector3> s_noPoints = new Dictionary<string, Vector3>();

    /// <summary>
    /// What a CS2-only variant answers instead of a CS:MC sample: the clip alias, the
    /// controller's untruncated time and whether it loops, which is all DrawCs2 reads
    /// from a pose before it samples the CS2 rig itself. Every binding lookup returns
    /// the identity, as on any pose missing a name.
    ///
    /// It exists because the draw hook treated a null pose as "nothing to draw" and
    /// handed the item back to Survivalcraft - the eight guns of 0.18.1 came out as a
    /// bare block at the bottom of the screen, with no arms and no animation.
    /// </summary>
    static KnifeRigPose Cs2OnlyPose(int variant, string clipAlias, float time, bool loop) {
        string name = Entry(variant).Name;
        string clip = Cs2Rig.ResolvedClip(name, clipAlias) ?? Cs2Rig.ResolvedClip(name, "idle") ?? "idle";
        float duration = Cs2Rig.Duration(name, clipAlias);
        float clamped = loop && duration > 0f ? time - duration * MathF.Floor(time / duration)
                                              : MathUtils.Clamp(time, 0f, Math.Max(0f, duration));
        return new KnifeRigPose(name, clipAlias, clip, duration, Entry(variant).SourceReferenceScale,
            s_noMatrices, s_noMatrices, s_noPoints, s_noMatrices, clamped, time, loop);
    }

    public static KnifeRigPose Sample(int variant, string clipAlias, float time, bool loop = false) {
        float requested = time;
        if (Entry(variant).Cs2Only) return Cs2OnlyPose(variant, clipAlias, time, loop);
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
        var frames = new Dictionary<string, Matrix>(StringComparer.Ordinal);
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
            // The bone's animated frame in mesh units: the absolute pose with the unit/axis
            // conversion but WITHOUT the binding's Right matrix. For the knife rigs Right is
            // the identity on the arm bones, so this equals the binding; the gun rigs carry
            // inverse-bind Right matrices there (skinning), and the binding of hand_r is
            // then near identity at idle instead of the hand's position. The muzzle and
            // both hands are read from this frame.
            // Left factor: only the unit part of N⁻¹. With the full N⁻¹ (translate(c)) the
            // frame's origin would be the mesh centre c carried through the bone, not the
            // bone's origin: 7 in along the bore on the AK-47, 16.7 in forward and 7.7 in up
            // on the AWP (hands and muzzle flash floating above the scope, 0.15.0).
            Matrix frame = Matrix.CreateScale(1f / asset.File.MeshNormalizationScale) * absolute[binding.BoneIndex] * ReadSourceMatrix(binding.LeftMatrix) * asset.Normalization;
            if (KnifeDiagnostics.IsFinite(frame)) frames[binding.Name] = frame;
            // CSMC/JOML: left * boneAbsolute * right.
            // Engine row-vector transpose: right^T * boneAbsolute^T * left^T.
            Matrix sourcePose = ReadSourceMatrix(binding.RightMatrix)
                * absolute[binding.BoneIndex]
                * ReadSourceMatrix(binding.LeftMatrix);
            Matrix normalizedPose = asset.InverseNormalization * sourcePose * asset.Normalization;
            if (KnifeDiagnostics.IsFinite(normalizedPose)) parts[binding.Name] = normalizedPose;
            else KnifeDiagnostics.WarnOnce($"rig-{asset.Name}-{binding.Name}-invalid", $"CSMC binding {asset.Name}/{binding.Name} produced a non-finite matrix.");
        }
        return new KnifeRigPose(asset.Name, clipAlias, clip.SourceName, clip.Duration, asset.File.SourceReferenceScale, parts, bones, attachments, frames, time, requested, loop);
    }

    /// <summary>
    /// The raw CSMC attachment pose per binding BEFORE mesh normalization:
    /// RightMatrix * boneAbsolute * LeftMatrix (Engine row-vector), the exact production
    /// rule Sample() uses. This is what CSMC 5.10's own Ӝ.þ(name) returns and what the
    /// round-5 controlled sampler exported, so it is the golden-baseline stage-A truth.
    /// Non-looping clamp, matching the offline sampler (loop=false).
    /// </summary>
    public static IReadOnlyDictionary<string, Matrix> SampleRawBindings(int variant, string clipAlias, float time) {
        if (Entry(variant).Cs2Only) return s_noMatrices;
        Asset asset = GetAsset(variant);
        if (!asset.File.Clips.TryGetValue(clipAlias, out Clip clip)) clip = asset.File.Clips["idle"];
        time = MathUtils.Clamp(time, 0f, Math.Max(0f, clip.Duration));
        Matrix[] absolute = CalculateAbsolute(asset, clip, time);
        var raw = new Dictionary<string, Matrix>(StringComparer.Ordinal);
        foreach (Binding binding in asset.File.Bindings) {
            if (binding.BoneIndex < 0 || binding.BoneIndex >= absolute.Length) continue;
            raw[binding.Name] = ReadSourceMatrix(binding.RightMatrix)
                * absolute[binding.BoneIndex]
                * ReadSourceMatrix(binding.LeftMatrix);
        }
        return raw;
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

    /// <summary>
    /// Bones whose clip curves are ignored: they stay at their rest pose relative to
    /// the parent. The butterfly's latch (v_weapon_lock) is keyed to swing 60-150
    /// degrees per frame through the flips, but CS:MC never shows it moving: in every
    /// frame of the MCCS video it lies along the handle, even with the handle pointing
    /// up, so it is not gravity either. Animated, it reads as a nail sticking out of
    /// the handle end.
    /// </summary>
    static readonly HashSet<string> s_staticBones = new(StringComparer.Ordinal) { "v_weapon_lock" };

    static Matrix SampleLocal(SkeletonBone bone, Clip clip, float time) {
        SourcePose rest = new(
            ReadVector(bone.Translation, Vector3.Zero),
            ReadQuaternion(bone.Rotation, Quaternion.Identity),
            ReadVector(bone.Scale, Vector3.One)
        );
        if (clip.Bones is null || s_staticBones.Contains(bone.Name) || !clip.Bones.TryGetValue(bone.Name, out BoneCurves curves))
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
        KnifeLog.Information(
            $"[ScCsgoKnives] exact CSMC rig {name}: format={file.Format}, parts=[{string.Join(',', file.MeshParts)}], "
            + $"bindings={file.Bindings.Count}, bones={file.Skeleton.Count}, clips=[{string.Join(',', file.Clips.Keys)}], "
            + $"normalizationCenter=({center.X:0.###},{center.Y:0.###},{center.Z:0.###}), normalizationScale={file.MeshNormalizationScale:0.######}."
        );
        return asset;
    }

    /// <summary>Length of a bone's rest translation (its parent-to-joint segment) in mesh units; 0 if unknown.</summary>
    public static float GetBoneRestLength(int variant, string bone) {
        Asset asset = TryGetAsset(variant);
        SkeletonBone b = asset?.File.Skeleton?.FirstOrDefault(x => x.Name == bone);
        if (b?.Translation is not { Length: >= 3 } t) return 0f;
        float len = MathF.Sqrt(t[0] * t[0] + t[1] * t[1] + t[2] * t[2]) * asset.File.MeshNormalizationScale;
        return float.IsFinite(len) ? len : 0f;
    }

    /// <summary>True when this variant has no CS:MC rig at all and is drawn from CS2.</summary>
    public static bool IsCs2Only(int variant) => Entry(variant).Cs2Only;

    static Asset GetAsset(int variant) {
        int index = Math.Clamp(variant, 0, s_assets.Length - 1);
        return s_assets[index] ??= Load(s_names[index]);
    }

    /// <summary>The CS:MC asset, or null for a CS2-only variant that has none.</summary>
    static Asset TryGetAsset(int variant) => Entry(variant).Cs2Only ? null : GetAsset(variant);

    static ManifestEntry[] LoadManifest() {
        Assembly assembly = typeof(CsmcKnifeRig).Assembly;
        ManifestEntry[] Read(string suffix, bool required) {
            string resource = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (resource is null) { if (required) throw new InvalidDataException($"Missing {suffix}."); return []; }
            using Stream stream = assembly.GetManifestResourceStream(resource);
            return JsonSerializer.Deserialize<ManifestEntry[]>(stream) ?? throw new InvalidDataException($"Empty {suffix}.");
        }
        ManifestEntry[] knives = Read("AnimationData.knives.json", true);
        ManifestEntry[] guns = Read("AnimationData.guns.json", false);
        foreach (ManifestEntry g in guns) g.IsGun = true;
        s_knifeCount = knives.Length;
        ManifestEntry[] entries = [.. knives, .. guns];
        KnifeLog.Information($"[ScCsgoKnives] rig manifest: {knives.Length} knives + {guns.Length} guns = [{string.Join(",", entries.Select(e => e.Name))}].");
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
    readonly IReadOnlyDictionary<string, Matrix> m_frames;

    public KnifeRigPose(string assetName, string clipAlias, string sourceClip, float duration, float sourceReferenceScale, IReadOnlyDictionary<string, Matrix> bindings, IReadOnlyDictionary<string, Matrix> bones, IReadOnlyDictionary<string, Vector3> attachments, IReadOnlyDictionary<string, Matrix> frames = null, float time = 0f, float requestedTime = 0f, bool looping = false) {
        Time = time;
        RequestedTime = requestedTime;
        Looping = looping;
        m_frames = frames ?? new Dictionary<string, Matrix>();
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
    /// <summary>Clip time this pose was sampled at, clamped or wrapped to the CS:MC clip.</summary>
    public float Time { get; }
    /// <summary>
    /// The time the caller asked for, before any clamping or wrapping. The cs2 profile
    /// needs it because its own clips are a different length; using <see cref="Time"/>
    /// would freeze a longer CS2 clip at the CS:MC clip's end.
    /// </summary>
    public float RequestedTime { get; }
    /// <summary>Whether the caller asked for a looping sample, so the cs2 profile can wrap at its own length.</summary>
    public bool Looping { get; }
    public float Duration { get; }
    public float SourceReferenceScale { get; }
    public IReadOnlyDictionary<string, Matrix> Bindings => m_bindings;
    public IReadOnlyDictionary<string, Matrix> Bones => m_bones;
    public IReadOnlyDictionary<string, Vector3> Attachments => m_attachments;

    /// <summary>A mesh part follows the bone named before its "__" suffix (chunked or duplicate records share one bone).</summary>
    public static string BoneOf(string part) { int i = part.IndexOf("__", StringComparison.Ordinal); return i > 0 ? part[..i] : part; }
    public Matrix GetBinding(string name) => m_bindings.TryGetValue(name, out Matrix value) || m_bindings.TryGetValue(BoneOf(name), out value) ? value : Matrix.Identity;
    public Matrix GetBone(string name) => m_bones.TryGetValue(name, out Matrix value) ? value : Matrix.Identity;
    /// <summary>The bone's animated frame in mesh units (absolute pose x unit conversion, no inverse bind); where a hand or the muzzle is.</summary>
    public Matrix GetBoneFrame(string name) => m_frames.TryGetValue(name, out Matrix value) ? value : GetBinding(name);
    public Vector3 GetBoneFrameOrigin(string name) { Matrix m = GetBoneFrame(name); return new Vector3(m.M41, m.M42, m.M43); }
    public Vector3 GetAttachment(string name) => m_attachments.TryGetValue(name, out Vector3 value) ? value : Vector3.Zero;
    public Vector3 GetBindingOrigin(string name) {
        Matrix value = GetBinding(name);
        return new Vector3(value.M41, value.M42, value.M43);
    }
}
