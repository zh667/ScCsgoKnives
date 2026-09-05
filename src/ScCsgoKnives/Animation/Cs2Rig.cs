using System.IO;
using System.Reflection;
using System.Text.Json;
using Engine;

namespace Game;

/// <summary>
/// CS2's own viewmodel rig, read from AnimationData/&lt;gun&gt;.cs2.animation.json
/// (tools/cs2_dmx_to_rig.py, out of CS2's binary DMX clips).
///
/// Same maths as <see cref="CsmcKnifeRig"/> - row vectors, local = R*T, absolute =
/// local * parent, mesh parts placed by Right * boneAbsolute * Left - but a
/// different skeleton: 64 bones with CS2's own names (hand_R, finger_index_meta_R,
/// wpn, weapon_offset), lengths in Source inches, and no root matrix baked in. The
/// two rigs are not interchangeable; AnimationData/cs2_bone_map.json records which
/// names correspond.
///
/// Positions are in the CS2 viewmodel's own space, which is the camera's: see
/// Cs2Placement.
/// </summary>
public static class Cs2Rig {
    sealed class Curve {
        public string Interpolation { get; set; }
        public float[] Times { get; set; }
        public float[][] Values { get; set; }
    }

    sealed class BoneCurves {
        public Curve Rotation { get; set; }
        public Curve Translation { get; set; }
    }

    sealed class Clip {
        public string SourceName { get; set; }
        /// <summary>
        /// The mod alias this clip answers to, written by the generator. The three
        /// guns keep their table below because it maps several aliases onto one clip
        /// (shoot1/2/3, inspectStart/Loop/End); the 22 knives carry the alias in the
        /// file instead, so adding a knife needs no C# change.
        /// </summary>
        public string Alias { get; set; }
        public float FrameRate { get; set; }
        public int FrameCount { get; set; }
        public float Duration { get; set; }
        public Dictionary<string, BoneCurves> Bones { get; set; }
        /// <summary>CS2's own clip events (CNmClipDocEvent_*), as the generator read them.</summary>
        public List<ClipEvent> Events { get; set; }
    }

    public sealed class ClipEvent {
        public string Class { get; set; }
        public string Name { get; set; }
        public float At { get; set; }
        public float Duration { get; set; }
    }

    /// <summary>
    /// A shotgun's reload as CS2 plays it: one clip whose WPN_RELOAD_INTRO, _LOOP
    /// and _OUTRO events mark three sections, the loop run once per shell. The Nova
    /// loads a shell in 0.433 s of a 1.633 s clip; a full reload from empty is the
    /// intro, eight loops and the outro. Null for a gun whose reload is one pass.
    /// </summary>
    public sealed class ReloadSections {
        public float LoopStart;      // WPN_RELOAD_LOOP.At: the intro ends here
        public float LoopLength;     // WPN_RELOAD_LOOP.Duration: one shell
        public float OutroStart;     // WPN_RELOAD_OUTRO.At
        public float End;            // the clip's length
        public float AddAmmoInLoop;  // WPN_RELOAD_ADD_AMMO.At - LoopStart: when the shell counts

        public float Duration(int loops) => LoopStart + Math.Max(0, loops) * LoopLength + (End - OutroStart);

        /// <summary>The clip time for a reload of `loops` shells at `elapsed` seconds in.</summary>
        public float ClipTime(int loops, float elapsed) {
            if (elapsed < LoopStart) return Math.Max(0f, elapsed);
            float inLoops = elapsed - LoopStart;
            if (LoopLength > 0f && inLoops < loops * LoopLength) return LoopStart + inLoops % LoopLength;
            float outro = OutroStart + (inLoops - Math.Max(0, loops) * LoopLength);
            return Math.Min(outro, End);
        }
    }

    static readonly Dictionary<string, ReloadSections> s_reloadSections = new(StringComparer.Ordinal);

    public static ReloadSections GetReloadSections(string gun) {
        if (gun is null) return null;
        if (s_reloadSections.TryGetValue(gun, out ReloadSections hit)) return hit;
        ReloadSections sections = null;
        Clip clip = Resolve(Get(gun), "reload");
        if (clip?.Events is not null) {
            ClipEvent loop = clip.Events.FirstOrDefault(e => e.Class == "CNmClipDocEvent_ID" && e.Name == "WPN_RELOAD_LOOP");
            ClipEvent outro = clip.Events.FirstOrDefault(e => e.Class == "CNmClipDocEvent_ID" && e.Name == "WPN_RELOAD_OUTRO");
            ClipEvent add = clip.Events.FirstOrDefault(e => e.Class == "CNmClipDocEvent_ID" && e.Name == "WPN_RELOAD_ADD_AMMO");
            if (loop is not null && loop.Duration > 0f) {
                sections = new ReloadSections {
                    LoopStart = loop.At, LoopLength = loop.Duration,
                    OutroStart = outro?.At ?? loop.At + loop.Duration, End = clip.Duration,
                    AddAmmoInLoop = add is not null ? Math.Clamp(add.At - loop.At, 0f, loop.Duration) : loop.Duration * 0.5f,
                };
            }
        }
        s_reloadSections[gun] = sections;
        return sections;
    }

    sealed class SkeletonBone {
        public int Index { get; set; }
        public string Name { get; set; }
        public int Parent { get; set; }
        public float[] Translation { get; set; }
        public float[] Rotation { get; set; }
        public float[] Scale { get; set; }
    }

    sealed class Binding {
        public string Name { get; set; }
        public string Bone { get; set; }
        public int BoneIndex { get; set; }
        public float[] RightMatrix { get; set; }
        public float[] LeftMatrix { get; set; }
    }

    sealed class RigFile {
        public string Format { get; set; }
        /// <summary>The .cs2.skin beside this rig, for a weapon that is one skinned mesh.</summary>
        public string Skinned { get; set; }
        /// <summary>The .cs2.parts beside this rig, for a weapon that is rigid pieces.</summary>
        public string Parts { get; set; }
        public string Units { get; set; }
        public float[] MeshCenter { get; set; }
        public float MeshNormalizationScale { get; set; }
        public string[] MeshParts { get; set; }
        public List<Binding> Bindings { get; set; }
        public List<SkeletonBone> Skeleton { get; set; }
        public Dictionary<string, Clip> Clips { get; set; }
    }

    sealed class Asset {
        public string Name;
        public RigFile File;
        public Dictionary<string, Binding> Bindings;
        public Dictionary<string, Clip> ByAlias;
        public Matrix Normalization;
        public Matrix InverseNormalization;
    }

    /// <summary>One sampled frame: mesh part matrices and bone frames, both in rig space.</summary>
    public sealed class Pose {
        public string Gun;
        public string Clip;
        public float Time;
        public Dictionary<string, Matrix> Parts;
        public Dictionary<string, Matrix> Bones;

        public Matrix GetPart(string name) => Parts.TryGetValue(name, out Matrix m) ? m : Matrix.Identity;
        public Vector3 GetBoneOrigin(string name) => Bones.TryGetValue(name, out Matrix m) ? m.Translation : Vector3.Zero;
        public bool HasBone(string name) => Bones.ContainsKey(name);
    }

    const string ExpectedFormat = "ScCsgoKnives.Cs2Animation/1";

    /// <summary>The mod's clip aliases mapped onto the CS2 clip file each gun uses.</summary>
    static readonly Dictionary<string, Dictionary<string, string>> s_clipAliases = new(StringComparer.Ordinal) {
        ["ak47"] = new(StringComparer.Ordinal) {
            ["idle"] = "idle_ak", ["deploy"] = "draw_ak", ["shoot1"] = "shoot1_ak",
            ["shoot2"] = "shoot1_ak", ["shoot3"] = "shoot1_ak", ["reload"] = "reload_ak",
            ["inspect"] = "lookat01_ak", ["inspectStart"] = "lookat01_ak",
            ["inspectLoop"] = "lookat01_ak", ["inspectEnd"] = "lookat01_ak",
        },
        ["m4a1s"] = new(StringComparer.Ordinal) {
            ["idle"] = "idle_rifle", ["deploy"] = "draw_rifle",
            ["shootSilenced"] = "shoot1_rifle", ["shootUnsilenced"] = "shoot1_rifle",
            ["reload"] = "reload_rifle", ["inspect"] = "lookat01_rifle",
            ["inspectStart"] = "lookat01_rifle", ["inspectLoop"] = "lookat01_rifle",
            ["inspectEnd"] = "lookat01_rifle",
            ["attach"] = "silencer_attach_rifle", ["detach"] = "silencer_detach_rifle",
        },
        ["awp"] = new(StringComparer.Ordinal) {
            ["idle"] = "idle_awp", ["deploy"] = "draw_awp", ["shoot1"] = "shoot1_awp",
            ["reload"] = "reload_awp", ["inspect"] = "lookat01_awp",
            ["inspectStart"] = "lookat01_awp", ["inspectLoop"] = "lookat01_awp",
            ["inspectEnd"] = "lookat01_awp",
        },
    };

    static readonly Dictionary<string, Asset> s_assets = new(StringComparer.Ordinal);

    /// <summary>
    /// True when a CS2 rig exists for this asset. The three guns are listed in
    /// s_clipAliases; the knives are not, so this loads (and caches) instead of
    /// consulting a table, which is what lets a knife be added by shipping its
    /// .cs2.animation.json alone.
    /// </summary>
    public static bool Has(string gun) => s_clipAliases.ContainsKey(gun) || Get(gun) is not null;

    /// <summary>The .cs2.skin this asset's mesh lives in, or null when it has none.</summary>
    public static string SkinnedResource(string gun) => Get(gun)?.File.Skinned;

    /// <summary>The .cs2.parts this asset's mesh lives in, or null when it has none.</summary>
    public static string PartsResource(string gun) => Get(gun)?.File.Parts;

    public static IReadOnlyList<string> GetMeshParts(string gun) => Get(gun)?.File.MeshParts ?? [];

    /// <summary>
    /// Length in seconds of the clip that would actually play for this alias, or 0
    /// when the asset is unknown.
    ///
    /// It resolves exactly as Sample does, idle fallback included. They used to
    /// differ: Sample fell back to idle and Duration returned 0, which for the three
    /// knives CS2 gives no second idle - bowie, falchion, push - would have had the
    /// controller time a zero-length idle2 while the renderer drew idle.
    /// </summary>
    public static float Duration(string gun, string clipAlias) {
        Asset asset = Get(gun);
        Clip clip = ResolveOrIdle(asset, clipAlias);
        return clip?.Duration ?? 0f;
    }

    /// <summary>
    /// True when this asset has a clip for the alias, without the idle fallback.
    /// The controller picks from these, so it can no longer choose an alias the
    /// CS2 file cannot answer - which is what made the butterfly's second draw and
    /// most second inspects come out as the finished idle pose in 0.17.0.
    /// </summary>
    public static bool HasAlias(string gun, string clipAlias) =>
        Resolve(Get(gun), clipAlias) is not null;

    /// <summary>
    /// The CS2 clip an alias resolves to, or null. Two aliases that resolve to the
    /// same clip are indistinguishable on screen - which is the whole bug: 0.17.0's
    /// deploy2 resolved to idle, so a second draw looked like no draw at all. Their
    /// durations are no test, deploy and deploy2 both run 1.0000 s on six knives.
    /// </summary>
    public static string ResolvedClip(string gun, string clipAlias) =>
        Resolve(Get(gun), clipAlias)?.SourceName;

    /// <summary>
    /// What Sample will draw for this alias: the clip, else idle.
    ///
    /// The fallback stays, because drawing idle beats drawing nothing, but it is no
    /// longer silent. Falling back on anything except idle2 means something asked for
    /// a clip this rig has not got, and in 0.17.0 that happened 67 times in one
    /// five-minute session without a word in the log. idle2 is exempt: CS2 and CS:MC
    /// agree that bowie, falchion and push have a single idle.
    /// </summary>
    static Clip ResolveOrIdle(Asset asset, string clipAlias) {
        Clip clip = Resolve(asset, clipAlias);
        if (clip is not null || asset is null) return clip;
        if (clipAlias is not ("idle" or "idle2"))
            KnifeDiagnostics.WarnOnce($"cs2-alias-{asset.Name}-{clipAlias}",
                $"{asset.Name} has no CS2 clip for '{clipAlias}'; drawing idle instead. "
                + $"It has [{(asset.ByAlias is null ? "" : string.Join(Char.Parse(","), asset.ByAlias.Keys))}].");
        return Resolve(asset, "idle");
    }

    public static Pose Sample(string gun, string clipAlias, float time) {
        Asset asset = Get(gun);
        if (asset is null) return null;
        Clip clip = ResolveOrIdle(asset, clipAlias);
        if (clip is null) return null;
        time = MathUtils.Clamp(time, 0f, Math.Max(0f, clip.Duration));

        List<SkeletonBone> skeleton = asset.File.Skeleton;
        Matrix[] absolute = new Matrix[skeleton.Count];
        Matrix[] local = new Matrix[skeleton.Count];
        bool[] done = new bool[skeleton.Count];
        for (int i = 0; i < skeleton.Count; i++) local[i] = SampleLocal(skeleton[i], clip, time);
        for (int i = 0; i < skeleton.Count; i++) Calculate(i);

        var bones = new Dictionary<string, Matrix>(skeleton.Count, StringComparer.Ordinal);
        for (int i = 0; i < skeleton.Count; i++) bones[skeleton[i].Name] = absolute[i];

        var parts = new Dictionary<string, Matrix>(StringComparer.Ordinal);
        foreach (Binding binding in asset.File.Bindings) {
            if (binding.BoneIndex < 0 || binding.BoneIndex >= absolute.Length) continue;
            parts[binding.Name] = ReadMatrix(binding.RightMatrix)
                * absolute[binding.BoneIndex]
                * ReadMatrix(binding.LeftMatrix);
        }
        return new Pose { Gun = gun, Clip = clip.SourceName, Time = time, Parts = parts, Bones = bones };

        Matrix Calculate(int index) {
            if (done[index]) return absolute[index];
            done[index] = true;                       // the emitted skeleton is a tree; guard anyway
            int parent = skeleton[index].Parent;
            absolute[index] = parent >= 0 && parent < skeleton.Count && parent != index
                ? local[index] * Calculate(parent)
                : local[index];
            return absolute[index];
        }
    }

    static Clip Resolve(Asset asset, string clipAlias) {
        if (asset is null || clipAlias is null) return null;
        if (s_clipAliases.TryGetValue(asset.Name, out var aliases)
            && aliases.TryGetValue(clipAlias, out string stem)
            && asset.File.Clips.TryGetValue(stem, out Clip byAlias)) return byAlias;
        // The knives declare their alias in the file. bowie, falchion and push have
        // no second idle in CS2 - and none in CS:MC either - so idle2 falls through
        // to idle rather than resolving to nothing.
        if (asset.ByAlias is not null && asset.ByAlias.TryGetValue(clipAlias, out Clip declared))
            return declared;
        return asset.File.Clips.TryGetValue(clipAlias, out Clip direct) ? direct : null;
    }

    static Matrix SampleLocal(SkeletonBone bone, Clip clip, float time) {
        Vector3 restT = ReadVector(bone.Translation, Vector3.Zero);
        Quaternion restR = ReadQuaternion(bone.Rotation, Quaternion.Identity);
        Vector3 translation = restT;
        Quaternion rotation = restR;
        if (clip.Bones is not null && clip.Bones.TryGetValue(bone.Name, out BoneCurves curves)) {
            translation = SampleVector(curves.Translation, time, restT);
            rotation = SampleQuaternion(curves.Rotation, time, restR);
        }
        return Matrix.CreateFromQuaternion(rotation) * Matrix.CreateTranslation(translation);
    }

    static Vector3 SampleVector(Curve curve, float time, Vector3 fallback) {
        if (curve?.Times is not { Length: > 0 } || curve.Values is not { Length: > 0 }) return fallback;
        (int lo, int hi, float f) = FindKeys(curve.Times, time);
        Vector3 a = ReadVector(curve.Values[lo], fallback);
        return lo == hi ? a : Vector3.Lerp(a, ReadVector(curve.Values[hi], fallback), f);
    }

    static Quaternion SampleQuaternion(Curve curve, float time, Quaternion fallback) {
        if (curve?.Times is not { Length: > 0 } || curve.Values is not { Length: > 0 }) return fallback;
        (int lo, int hi, float f) = FindKeys(curve.Times, time);
        Quaternion a = ReadQuaternion(curve.Values[lo], fallback);
        return lo == hi ? a : Quaternion.Normalize(Quaternion.Slerp(a, ReadQuaternion(curve.Values[hi], fallback), f));
    }

    static (int Lo, int Hi, float Factor) FindKeys(float[] times, float time) {
        if (times.Length == 1 || time <= times[0]) return (0, 0, 0f);
        int last = times.Length - 1;
        if (time >= times[last]) return (last, last, 0f);
        int hi = Array.BinarySearch(times, time);
        if (hi >= 0) return (hi, hi, 0f);
        hi = ~hi;
        int lo = hi - 1;
        return (lo, hi, (time - times[lo]) / Math.Max(0.000001f, times[hi] - times[lo]));
    }

    static Vector3 ReadVector(float[] v, Vector3 fallback) =>
        v is { Length: >= 3 } ? new Vector3(v[0], v[1], v[2]) : fallback;

    static Quaternion ReadQuaternion(float[] v, Quaternion fallback) =>
        v is { Length: >= 4 } ? new Quaternion(v[0], v[1], v[2], v[3]) : fallback;

    static Matrix ReadMatrix(float[] v) {
        if (v is not { Length: >= 16 }) return Matrix.Identity;
        return new Matrix {
            M11 = v[0], M12 = v[1], M13 = v[2], M14 = v[3],
            M21 = v[4], M22 = v[5], M23 = v[6], M24 = v[7],
            M31 = v[8], M32 = v[9], M33 = v[10], M34 = v[11],
            M41 = v[12], M42 = v[13], M43 = v[14], M44 = v[15]
        };
    }

    static Asset Get(string gun) {
        if (gun is null) return null;
        if (s_assets.TryGetValue(gun, out Asset hit)) return hit;
        Asset asset = null;
        try {
            asset = Load(gun);
        }
        catch (Exception e) {
            KnifeDiagnostics.WarnOnce($"cs2-rig-{gun}", $"Could not read the CS2 rig for {gun}: {e.Message}");
        }
        s_assets[gun] = asset;
        return asset;
    }

    static Asset Load(string gun) {
        Assembly assembly = typeof(Cs2Rig).Assembly;
        string suffix = $"AnimationData.{gun}.cs2.animation.json";
        string resource = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Missing embedded {suffix}.");
        using Stream stream = assembly.GetManifestResourceStream(resource);
        RigFile file = JsonSerializer.Deserialize<RigFile>(stream);
        if (file?.Format != ExpectedFormat || file.Skeleton is null || file.Clips is null || file.Bindings is null)
            throw new InvalidDataException($"{suffix} is not {ExpectedFormat}.");
        Vector3 centre = ReadVector(file.MeshCenter, Vector3.Zero);
        Matrix normalization = Matrix.CreateTranslation(-centre) * Matrix.CreateScale(file.MeshNormalizationScale);
        var asset = new Asset {
            Name = gun, File = file,
            Bindings = file.Bindings.ToDictionary(b => b.Name, StringComparer.Ordinal),
            ByAlias = file.Clips.Values.Where(c => !string.IsNullOrEmpty(c.Alias))
                          .GroupBy(c => c.Alias, StringComparer.Ordinal)
                          .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal),
            Normalization = normalization,
            InverseNormalization = Matrix.Invert(normalization)
        };
        KnifeLog.Information(
            $"[ScCsgoKnives] CS2 rig {gun}: bones={file.Skeleton.Count}, clips=[{string.Join(',', file.Clips.Keys)}], "
            + (file.MeshParts is { Length: > 0 }
                ? $"parts=[{string.Join(',', file.MeshParts)}]"
                : $"skinned={file.Skinned ?? "none"}")
            + $", units={file.Units}."
        );
        return asset;
    }
}
