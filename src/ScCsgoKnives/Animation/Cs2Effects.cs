using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Engine;

namespace Game;

/// <summary>
/// CS2's muzzle flash and tracer numbers, from AnimationData/cs2_effects.json
/// (tools/cs2_effects.py). Muzzle positions and the tracer reference come from each
/// gun's .vdata, the flash envelope from the .vpcf CS2 plays.
///
/// The mod draws a sprite, not a particle system, so this carries what a sprite can
/// honour - lifetime, sequence length, colour range, alpha range, fade - and the
/// generator records in the JSON what it deliberately does not model.
///
/// A cross-check worth keeping: the AK's m_vecMuzzlePos0 is
/// [37.422, -4.938, -3.394], which is the `muzzle` bone of its idle clip to three
/// decimals. The vdata and the animation agree about where the barrel ends.
/// </summary>
public static class Cs2Effects {
    /// <summary>
    /// Accepts a JSON number or an array of numbers as float[].
    ///
    /// 0.16.4 shipped a cs2_effects.json whose AK lifetime was the scalar 0.05 while the
    /// M4A1-S suppressed one was [0.3, 0.85]; float[] threw on the scalar, the whole file
    /// failed to load, and every tracer and the CS2 flash envelope were silently inactive.
    /// The generator now always writes an array, and this accepts either shape so the
    /// contract cannot break that way again.
    /// </summary>
    sealed class ScalarOrArray : JsonConverter<float[]> {
        public override float[] Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) {
            if (reader.TokenType == JsonTokenType.Number) return [reader.GetSingle()];
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException($"expected a number or an array, got {reader.TokenType}");
            var values = new List<float>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray) {
                if (reader.TokenType == JsonTokenType.Null) continue;
                values.Add(reader.GetSingle());
            }
            return [.. values];
        }

        public override void Write(Utf8JsonWriter writer, float[] value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, options);
    }

    public sealed class Flash {
        [JsonPropertyName("Lifetime")]
        [JsonConverter(typeof(ScalarOrArray))]
        public float[] Lifetime { get; set; }
        /// <summary>Null when the emitter's count is a curve rather than a literal (the M249's flash).</summary>
        [JsonPropertyName("Particles")]
        public float? Particles { get; set; }
        [JsonPropertyName("SequenceFrames")]
        public int SequenceFrames { get; set; }
        [JsonPropertyName("ColorMin")]
        public int[] ColorMin { get; set; }
        [JsonPropertyName("ColorMax")]
        public int[] ColorMax { get; set; }
        [JsonPropertyName("Alpha")]
        [JsonConverter(typeof(ScalarOrArray))]
        public float[] Alpha { get; set; }
        [JsonPropertyName("FadeOut")]
        public float? FadeOut { get; set; }
        [JsonPropertyName("Textures")]
        public string[] Textures { get; set; }
        [JsonPropertyName("Unmodelled")]
        public string[] Unmodelled { get; set; }

        public float Seconds => Lifetime is { Length: >= 2 } ? (Lifetime[0] + Lifetime[1]) * 0.5f
            : Lifetime is { Length: 1 } ? Lifetime[0] : 0.05f;
        public float AlphaMid => Alpha is { Length: >= 2 } ? (Alpha[0] + Alpha[1]) * 0.5f
            : Alpha is { Length: 1 } ? Alpha[0] : 1f;
        public Color Tint {
            get {
                if (ColorMin is not { Length: >= 3 } || ColorMax is not { Length: >= 3 }) return Color.White;
                return new Color((byte)((ColorMin[0] + ColorMax[0]) / 2), (byte)((ColorMin[1] + ColorMax[1]) / 2),
                                 (byte)((ColorMin[2] + ColorMax[2]) / 2), (byte)255);
            }
        }
    }

    /// <summary>
    /// One C_OP_RenderTrails pass. CS2 draws each tracer trail twice - an additive
    /// pass on materials/effects/spark and a blend-add pass on frame 4 of
    /// materials/particle/sparks - with different radius scales and length fade-ins.
    /// </summary>
    public sealed class TracerPass {
        [JsonPropertyName("Texture")]
        public string Texture { get; set; }            // the baked PNG's name, or null
        [JsonPropertyName("SourceTexture")]
        public string SourceTexture { get; set; }      // the vtex the vpcf names
        [JsonPropertyName("Blend")]
        public string Blend { get; set; }
        [JsonPropertyName("RadiusScale")]
        public float? RadiusScale { get; set; }
        [JsonPropertyName("LengthFadeIn")]
        public float? LengthFadeIn { get; set; }       // seconds
        /// <summary>
        /// The trail's on-screen half-width is clamped to this fraction of the
        /// viewport height. Without it a 0.5-inch trail two feet from the eye covers
        /// most of the screen: 0.16.5 drew a fixed 0.012-unit half-width and looked
        /// like a white plank at close range.
        /// </summary>
        [JsonPropertyName("MinSize")]
        public float MinSize { get; set; }
        [JsonPropertyName("MaxSize")]
        public float MaxSize { get; set; }
        /// <summary>The AWP's trail fades out entirely once it is this big on screen.</summary>
        /// <summary>"C_OP_RenderRopes" for the SMG's rope, drawn by the same ribbon; null for a trail.</summary>
        [JsonPropertyName("Renderer")]
        public string Renderer { get; set; }
        public bool IsRope => Renderer == "C_OP_RenderRopes";
        [JsonPropertyName("StartFadeSize")]
        public float StartFadeSize { get; set; }
        [JsonPropertyName("EndFadeSize")]
        public float EndFadeSize { get; set; }

        /// <summary>
        /// Both of CS2's tracer blend modes land on the engine's additive state.
        /// PARTICLE_OUTPUT_BLEND_MODE_BLEND_ADD is dst + src.rgb * src.a, which is what
        /// BlendState.Additive (SourceAlpha, One) does. PARTICLE_OUTPUT_BLEND_MODE_ADD
        /// is dst + src.rgb, and the texture it is used with - materials/effects/spark -
        /// is opaque everywhere, so its alpha is 1 and the two agree there as well.
        /// Anything else would have to be looked at rather than assumed, so it says so.
        /// </summary>
        public bool Additive => Blend is null || Blend.EndsWith("ADD", StringComparison.Ordinal);
        public bool BlendUnderstood => Blend is null
            || Blend is "PARTICLE_OUTPUT_BLEND_MODE_ADD" or "PARTICLE_OUTPUT_BLEND_MODE_BLEND_ADD";
    }

    /// <summary>
    /// A CS2 tracer system, read whole rather than reduced to a speed and a length.
    ///
    /// CS2 emits one particle that runs from the muzzle control point to the impact
    /// one: C_INIT_MoveBetweenPoints gives it the speed, C_INIT_DistanceToCPInit
    /// scales its lifetime by the shot distance, so it lives exactly
    /// distance / speed and dies on impact. C_OP_FadeAndKillForTracers ramps alpha
    /// against that normalised life, C_OP_DistanceToTransform lengthens the trail
    /// with distance from the viewer, and each C_OP_RenderTrails pass clamps the
    /// drawn width in screen space.
    /// </summary>
    public sealed class Tracer {
        [JsonPropertyName("ColorMin")]
        public int[] ColorMin { get; set; }
        [JsonPropertyName("ColorMax")]
        public int[] ColorMax { get; set; }
        [JsonPropertyName("ColorFromTexture")]
        public bool ColorFromTexture { get; set; }
        [JsonPropertyName("Speed")]
        public float? Speed { get; set; }              // inches per second
        [JsonPropertyName("MaxLength")]
        public float? MaxLength { get; set; }          // inches
        [JsonPropertyName("Radius")]
        [JsonConverter(typeof(ScalarOrArray))]
        public float[] Radius { get; set; }            // inches
        [JsonPropertyName("TrailSeconds")]
        [JsonConverter(typeof(ScalarOrArray))]
        public float[] TrailSeconds { get; set; }      // seconds of travel drawn behind the head
        [JsonPropertyName("Alpha")]
        [JsonConverter(typeof(ScalarOrArray))]
        public float[] Alpha { get; set; }
        [JsonPropertyName("FadeInStart")]
        public float FadeInStart { get; set; }
        [JsonPropertyName("FadeInEnd")]
        public float FadeInEnd { get; set; }
        [JsonPropertyName("FadeOutStart")]
        public float FadeOutStart { get; set; } = 1f;
        [JsonPropertyName("FadeOutEnd")]
        public float FadeOutEnd { get; set; } = 1f;
        [JsonPropertyName("StartAlpha")]
        public float StartAlpha { get; set; }
        [JsonPropertyName("EndAlpha")]
        public float EndAlpha { get; set; }
        [JsonPropertyName("LengthScaleInput")]
        public float[] LengthScaleInput { get; set; }
        [JsonPropertyName("LengthScaleOutput")]
        public float[] LengthScaleOutput { get; set; }
        [JsonPropertyName("Passes")]
        public TracerPass[] Passes { get; set; }
        [JsonPropertyName("Source")]
        public string Source { get; set; }
        [JsonPropertyName("Unmodelled")]
        public string[] Unmodelled { get; set; }

        /// <summary>Travel speed in engine units per second (inches -> metres).</summary>
        public float MetresPerSecond => (Speed ?? 20500f) * Cs2Placement.InchesToEngine;
        /// <summary>The trail length cap in engine units.</summary>
        public float TrailMetres => (MaxLength ?? 1200f) * Cs2Placement.InchesToEngine;
        /// <summary>Half-width in engine units before the screen-space clamp, for one pass.</summary>
        public float HalfWidthMetres(TracerPass pass) =>
            Mid(Radius, 1f) * (pass?.RadiusScale ?? 1f) * Cs2Placement.InchesToEngine;
        /// <summary>How many seconds of travel the trail spans, before the distance scale.</summary>
        public float TrailSecondsMid => Mid(TrailSeconds, 0.0925f);
        public float AlphaMid => Mid(Alpha, 0.75f);

        static float Mid(float[] v, float fallback) =>
            v is { Length: >= 2 } ? (v[0] + v[1]) * 0.5f : v is { Length: 1 } ? v[0] : fallback;

        /// <summary>
        /// Alpha against the fraction of the shot line already flown, from
        /// C_OP_FadeAndKillForTracers: StartAlpha until FadeInStart, full by FadeInEnd,
        /// back to EndAlpha between FadeOutStart and FadeOutEnd.
        ///
        /// The fraction is of the path, not of the lifetime. The systems set no
        /// lifespan, so Source's default second applies, scaled by
        /// C_INIT_DistanceToCPInit over 0..180 inches - at 20500 in/s a 30 m shot is
        /// over in 58 ms, so against a lifetime the trail would still be at StartAlpha
        /// (0) when it hit. C_INIT_MoveBetweenPoints runs the particle from the muzzle
        /// control point to the impact one and this operator kills it there, which is
        /// the "ForTracers" part; 0.2/0.3/0.95 are fractions of that trip. It is why a
        /// CS2 tracer is not visible right at the muzzle.
        /// </summary>
        public float PathAlpha(float u) {
            if (u <= FadeInStart) return StartAlpha;
            if (u < FadeInEnd)
                return MathUtils.Lerp(StartAlpha, 1f, (u - FadeInStart) / MathF.Max(FadeInEnd - FadeInStart, 1e-4f));
            if (u <= FadeOutStart) return 1f;
            if (u < FadeOutEnd)
                return MathUtils.Lerp(1f, EndAlpha, (u - FadeOutStart) / MathF.Max(FadeOutEnd - FadeOutStart, 1e-4f));
            return EndAlpha;
        }

        /// <summary>
        /// C_OP_DistanceToTransform on the trail length: near the viewer the trail is
        /// drawn shorter, far away longer. Distance is in inches, as the vpcf writes it.
        /// </summary>
        public float LengthScale(float metresFromViewer) {
            if (LengthScaleOutput is not { Length: >= 2 }) return 1f;
            float lo = LengthScaleInput is { Length: >= 2 } ? LengthScaleInput[0] : 0f;
            float hi = LengthScaleInput is { Length: >= 2 } ? LengthScaleInput[1] : 0f;
            if (hi <= lo) return LengthScaleOutput[1];
            float inches = metresFromViewer / Cs2Placement.InchesToEngine;
            return MathUtils.Lerp(LengthScaleOutput[0], LengthScaleOutput[1],
                                  MathUtils.Saturate((inches - lo) / (hi - lo)));
        }

        /// <summary>
        /// Mid colour. The assault-rifle tracer's C_INIT_RandomColor carries no bounds,
        /// which is Source's white - its colour is in materials/effects/spark, and the
        /// generator records that as ColorFromTexture rather than leaving it unset.
        /// </summary>
        public Color Tint {
            get {
                if (ColorMin is not { Length: >= 3 } || ColorMax is not { Length: >= 3 }) return Color.White;
                return new Color((byte)((ColorMin[0] + ColorMax[0]) / 2), (byte)((ColorMin[1] + ColorMax[1]) / 2),
                                 (byte)((ColorMin[2] + ColorMax[2]) / 2), (byte)255);
            }
        }
    }

    public sealed class Gun {
        [JsonPropertyName("MuzzlePos0")]
        public float[] MuzzlePos0 { get; set; }
        [JsonPropertyName("MuzzlePos1")]
        public float[] MuzzlePos1 { get; set; }
        [JsonPropertyName("TracerParticle")]
        public string TracerParticle { get; set; }
        [JsonPropertyName("TracerFrequency")]
        public float? TracerFrequency { get; set; }
        [JsonPropertyName("Flash")]
        public Dictionary<string, Flash> Flash { get; set; }
        [JsonPropertyName("Tracer")]
        public Tracer Tracer { get; set; }
    }

    sealed class EffectsFile {
        [JsonPropertyName("Format")]
        public string Format { get; set; }
        [JsonPropertyName("Guns")]
        public Dictionary<string, Gun> Guns { get; set; }
    }

    const string Resource = "AnimationData.cs2_effects.json";
    const string ExpectedFormat = "ScCsgoKnives.Cs2Effects/3";
    /// <summary>
    /// Null when the file loaded. Anything else is the reason it did not, and the
    /// self-test fails on it: 0.16.5 loaded its values correctly *and* reported a
    /// failure, because the success log called back into TracerFrequency while the
    /// static field it reads was still null. Values alone are not evidence of a
    /// healthy load, so the status is published rather than inferred.
    /// </summary>
    public static string LoadError { get; private set; } = "not loaded";

    static readonly Dictionary<string, Gun> s_guns = Load();

    public static Gun Get(string gun) =>
        gun is not null && s_guns is not null && s_guns.TryGetValue(gun, out Gun g) ? g : null;

    /// <summary>The flash for a gun in its current state, or null when CS2 has none.</summary>
    public static Flash GetFlash(string gun, bool silenced) {
        Gun g = Get(gun);
        if (g?.Flash is null) return null;
        if (silenced && g.Flash.TryGetValue("silenced", out Flash s)) return s;
        return g.Flash.TryGetValue("default", out Flash d) ? d : null;
    }

    /// <summary>Muzzle in rig inches: pos1 when suppressed and the gun declares one.</summary>
    public static Vector3? MuzzlePosition(string gun, bool silenced) {
        Gun g = Get(gun);
        float[] v = silenced && g?.MuzzlePos1 is { Length: >= 3 } ? g.MuzzlePos1 : g?.MuzzlePos0;
        return v is { Length: >= 3 } ? new Vector3(v[0], v[1], v[2]) : null;
    }

    /// <summary>How often CS2 draws a tracer for this gun; 1 means every shot.</summary>
    public static int TracerFrequency(string gun) {
        float? f = Get(gun)?.TracerFrequency;
        return f is null || f < 1f ? 0 : (int)MathF.Round(f.Value);
    }

    static Dictionary<string, Gun> Load() {
        var loaded = new Dictionary<string, Gun>(StringComparer.Ordinal);
        try {
            Assembly assembly = typeof(Cs2Effects).Assembly;
            string name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(Resource, StringComparison.OrdinalIgnoreCase));
            if (name is null) {
                LoadError = $"no embedded {Resource}";
                KnifeDiagnostics.WarnOnce("cs2-effects-missing", $"No embedded {Resource}.");
                return loaded;
            }
            using Stream stream = assembly.GetManifestResourceStream(name);
            // Names are pinned by JsonPropertyName rather than inferred:
            // PropertyNameCaseInsensitive ignores case, not separators, so a snake_case
            // key silently left its property at the default in 0.16.4.
            EffectsFile file = JsonSerializer.Deserialize<EffectsFile>(stream);
            if (file?.Format != ExpectedFormat || file.Guns is null) {
                LoadError = $"{Resource} is not {ExpectedFormat}";
                KnifeDiagnostics.WarnOnce("cs2-effects-format", $"{Resource} is not {ExpectedFormat}.");
                return loaded;
            }
            foreach ((string gun, Gun g) in file.Guns) loaded[gun] = g;
            LoadError = null;
            KnifeLog.Information(
                $"[ScCsgoKnives] CS2 effects: " + string.Join("; ", loaded.Select(kv =>
                    $"{kv.Key} muzzle0=({kv.Value.MuzzlePos0?[0]:0.###},{kv.Value.MuzzlePos0?[1]:0.###},{kv.Value.MuzzlePos0?[2]:0.###})"
                    + $" flash={kv.Value.Flash?.Count ?? 0}"
                    + $" tracer every {(kv.Value.TracerFrequency is > 0f ? (int)MathF.Round(kv.Value.TracerFrequency.Value) : 0)}"))
            );
        }
        catch (Exception e) {
            LoadError = $"{e.GetType().Name}: {e.Message}";
            KnifeDiagnostics.WarnOnce("cs2-effects-load", $"Could not read {Resource}: {e.Message}");
        }
        return loaded;
    }
}
