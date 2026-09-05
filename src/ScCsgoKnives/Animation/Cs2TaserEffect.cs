using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Engine;

namespace Game;

/// <summary>
/// The Zeus x27's shot as CS2's particle files describe it, read from
/// AnimationData/cs2_taser_effect.json (tools/cs2_taser_effect.py). The vdata names
/// weapon_tracers_taser as the tracer; its wires (wire1a/1b) have no renderer, the
/// arc laid over them (wire2) does - two rope passes, 0.4 s, light blue - and the
/// muzzle plays weapon_muzzle_flash_taser: a blue glow, a flare and sparks. The
/// impact end gets glow_05 sprites and its own sparks.
///
/// What the file cannot say and the renderer assumes: the ropes' texture scroll rate
/// is noise-driven (a constant is used), the flare's MOD2X blend has no equivalent in
/// the engine (drawn additive, dimmed and capped), and a spark trail's length is the
/// velocity over a fixed slice of time.
/// </summary>
public static class Cs2TaserEffect {
    public sealed class Fade {
        [JsonPropertyName("Min")] public float? Min { get; set; }
        [JsonPropertyName("Max")] public float? Max { get; set; }
        [JsonPropertyName("Proportional")] public bool Proportional { get; set; }
        public float Seconds => ((Min ?? 0.25f) + (Max ?? 0.25f)) * 0.5f;
    }

    public sealed class RadiusRamp {
        [JsonPropertyName("StartScale")] public float StartScale { get; set; } = 1f;
        [JsonPropertyName("EndScale")] public float EndScale { get; set; } = 1f;
        [JsonPropertyName("Bias")] public float Bias { get; set; } = 0.5f;

        /// <summary>Source's biased interpolation: t / ((1/b - 2)(1 - t) + 1).</summary>
        public float At(float t) {
            t = MathUtils.Saturate(t);
            float b = MathUtils.Clamp(Bias, 0.01f, 0.99f);
            float biased = t / ((1f / b - 2f) * (1f - t) + 1f);
            return MathUtils.Lerp(StartScale, EndScale, biased);
        }
    }

    public sealed class Movement {
        [JsonPropertyName("Gravity")] public float[] Gravity { get; set; }
        [JsonPropertyName("Drag")] public float Drag { get; set; }
        /// <summary>CS2's gravity is on Z, inches/s^2; Survivalcraft's up is Y, metres.</summary>
        public float GravityMetres => Gravity is { Length: >= 3 } ? Gravity[2] * Cs2Placement.InchesToEngine : 0f;
    }

    public sealed class RopePass {
        [JsonPropertyName("Textures")] public string[] Textures { get; set; }
        [JsonPropertyName("RadiusScale")] public float? RadiusScale { get; set; }
        [JsonPropertyName("Overbright")] public float? Overbright { get; set; }
    }

    public sealed class Arc {
        [JsonPropertyName("StartSeconds")] public float StartSeconds { get; set; }
        [JsonPropertyName("Points")] public float Points { get; set; }
        [JsonPropertyName("LifeSeconds")] public float[] LifeSeconds { get; set; }
        [JsonPropertyName("RadiusCurveDomainMax")] public float[] RadiusCurveDomainMax { get; set; }
        [JsonPropertyName("Radius")] public RadiusRamp Radius { get; set; }
        [JsonPropertyName("FadeOut")] public Fade FadeOut { get; set; }
        [JsonPropertyName("Movement")] public Movement Movement { get; set; }
        [JsonPropertyName("DampenRangeInches")] public float DampenRangeInches { get; set; }
        [JsonPropertyName("ColorMin")] public int[] ColorMin { get; set; }
        [JsonPropertyName("ColorMax")] public int[] ColorMax { get; set; }
        [JsonPropertyName("Passes")] public RopePass[] Passes { get; set; }
        public float Life => LifeSeconds is { Length: >= 1 } ? LifeSeconds[0] : 0f;
        /// <summary>The radius curve runs the particle index 0..domain[0] onto 0..domain[1] inches.</summary>
        public float RadiusInchesAt(int index) =>
            RadiusCurveDomainMax is { Length: >= 2 } && RadiusCurveDomainMax[0] > 0f
                ? RadiusCurveDomainMax[1] * index / RadiusCurveDomainMax[0] : 1f;
    }

    public sealed class Wire {
        [JsonPropertyName("Points")] public float Points { get; set; }
        [JsonPropertyName("LifeSeconds")] public float[] LifeSeconds { get; set; }
        [JsonPropertyName("Bulge")] public float Bulge { get; set; }
        [JsonPropertyName("Color")] public int[] Color { get; set; }
        [JsonPropertyName("Rendered")] public bool Rendered { get; set; }
    }

    public sealed class Sprites {
        [JsonPropertyName("Count")] public float Count { get; set; }
        [JsonPropertyName("LifeSeconds")] public float[] LifeSeconds { get; set; }
        [JsonPropertyName("RadiusInches")] public float[] RadiusInches { get; set; }
        [JsonPropertyName("SphereInches")] public float SphereInches { get; set; }
        [JsonPropertyName("Radius")] public RadiusRamp Radius { get; set; }
        [JsonPropertyName("FadeOut")] public Fade FadeOut { get; set; }
        [JsonPropertyName("ColorMin")] public int[] ColorMin { get; set; }
        [JsonPropertyName("ColorMax")] public int[] ColorMax { get; set; }
        [JsonPropertyName("Overbright")] public float? Overbright { get; set; }
        [JsonPropertyName("Blend")] public string Blend { get; set; }
        [JsonPropertyName("Texture")] public string Texture { get; set; }
        public bool Mod2x => Blend is not null && Blend.EndsWith("MOD2X", StringComparison.Ordinal);
    }

    public sealed class Sparks {
        [JsonPropertyName("MaxParticles")] public int MaxParticles { get; set; }
        [JsonPropertyName("EmissionSeconds")] public float EmissionSeconds { get; set; }
        [JsonPropertyName("EmitRate")] public float? EmitRate { get; set; }
        [JsonPropertyName("LifeSeconds")] public float[] LifeSeconds { get; set; }
        [JsonPropertyName("RadiusInches")] public float[] RadiusInches { get; set; }
        [JsonPropertyName("RadiusScale")] public float? RadiusScale { get; set; }
        [JsonPropertyName("MaxLengthInches")] public float? MaxLengthInches { get; set; }
        [JsonPropertyName("SpeedMin")] public float[] SpeedMin { get; set; }
        [JsonPropertyName("SpeedMax")] public float[] SpeedMax { get; set; }
        [JsonPropertyName("Movement")] public Movement Movement { get; set; }
        [JsonPropertyName("FadeIn")] public Fade FadeIn { get; set; }
        [JsonPropertyName("FadeOut")] public Fade FadeOut { get; set; }
        [JsonPropertyName("Color")] public int[] Color { get; set; }
        [JsonPropertyName("Texture")] public string Texture { get; set; }
        /// <summary>How many CS2 emits: the rate over the emission window, else the system's cap.</summary>
        public int Count => EmitRate is > 0f ? (int)MathF.Round(EmitRate.Value * EmissionSeconds) : MaxParticles;
    }

    public sealed class Bake {
        [JsonPropertyName("source")] public string Source { get; set; }
        [JsonPropertyName("output_size")] public int[] OutputSize { get; set; }
    }

    public sealed class File {
        [JsonPropertyName("Format")] public string Format { get; set; }
        [JsonPropertyName("Gun")] public string Gun { get; set; }
        [JsonPropertyName("Wire")] public Wire Wire { get; set; }
        [JsonPropertyName("Arc")] public Arc Arc { get; set; }
        [JsonPropertyName("MuzzleGlow")] public Sprites MuzzleGlow { get; set; }
        [JsonPropertyName("MuzzleFlash")] public Sprites MuzzleFlash { get; set; }
        [JsonPropertyName("MuzzleSparks")] public Sparks MuzzleSparks { get; set; }
        [JsonPropertyName("ImpactGlow")] public Sprites ImpactGlow { get; set; }
        [JsonPropertyName("ImpactSparks")] public Sparks ImpactSparks { get; set; }
        [JsonPropertyName("Baked")] public Dictionary<string, Bake> Baked { get; set; }
    }

    const string Resource = "AnimationData.cs2_taser_effect.json";
    const string ExpectedFormat = "ScCsgoKnives.Cs2TaserEffect/1";

    /// <summary>Null when the file loaded; the self-test fails on anything else.</summary>
    public static string LoadError { get; private set; } = "not loaded";

    static readonly File s_file = Load();

    public static File Data => s_file;

    /// <summary>The gun this effect belongs to (the Zeus).</summary>
    public static bool Applies(string gun) => s_file?.Gun is not null && gun == s_file.Gun;

    /// <summary>The baked texture a CS2 material name maps to, or null.</summary>
    public static string BakedTexture(string cs2Texture) {
        if (cs2Texture is null || s_file?.Baked is null) return null;
        // materials/effects/spark is already shipped as the tracer's additive texture.
        if (cs2Texture == "effects/spark") return "cs2_tracer_add";
        foreach ((string stem, Bake bake) in s_file.Baked)
            if (bake.Source is not null && bake.Source.StartsWith(cs2Texture, StringComparison.Ordinal)) return stem;
        return null;
    }

    static File Load() {
        try {
            Assembly assembly = typeof(Cs2TaserEffect).Assembly;
            string name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(Resource, StringComparison.OrdinalIgnoreCase));
            if (name is null) {
                LoadError = $"no embedded {Resource}";
                KnifeDiagnostics.WarnOnce("cs2-taser-missing", $"No embedded {Resource}.");
                return null;
            }
            using Stream stream = assembly.GetManifestResourceStream(name);
            File file = JsonSerializer.Deserialize<File>(stream);
            if (file?.Format != ExpectedFormat || file.Arc is null || file.Gun is null) {
                LoadError = $"{Resource} is not {ExpectedFormat}";
                KnifeDiagnostics.WarnOnce("cs2-taser-format", LoadError);
                return null;
            }
            LoadError = null;
            KnifeLog.Information($"[ScCsgoKnives] CS2 Zeus effect: arc {file.Arc.Life:0.##} s over {file.Arc.Points:0} points, "
                + $"{file.MuzzleGlow?.Count:0} glow + {file.MuzzleFlash?.Count:0} flare + {file.MuzzleSparks?.Count} sparks at the muzzle, "
                + $"{file.ImpactGlow?.Count:0} glow + {file.ImpactSparks?.Count} sparks at the impact.");
            return file;
        }
        catch (Exception e) {
            LoadError = e.Message;
            KnifeDiagnostics.WarnOnce("cs2-taser-load", $"Could not read {Resource}: {e.Message}");
            return null;
        }
    }
}
