using System.IO;
using System.Reflection;
using System.Text.Json;
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
    public sealed class Flash {
        public float[] Lifetime { get; set; }        // literal or [min, max]
        public float Particles { get; set; }
        public int SequenceFrames { get; set; }
        public int[] ColorMin { get; set; }
        public int[] ColorMax { get; set; }
        public float[] Alpha { get; set; }
        public float? FadeOut { get; set; }
        public string[] Textures { get; set; }
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

    public sealed class Tracer {
        public int[] ColorMin { get; set; }
        public int[] ColorMax { get; set; }
        public float? Speed { get; set; }          // inches per second
        public float? MaxLength { get; set; }      // inches
        public float? LengthFadeIn { get; set; }   // seconds
        public float[] Alpha { get; set; }
        public string Source { get; set; }

        /// <summary>Travel speed in engine units per second (inches -> metres).</summary>
        public float MetresPerSecond => (Speed ?? 20500f) * Cs2Placement.InchesToEngine;
        /// <summary>Trail length in engine units.</summary>
        public float TrailMetres => (MaxLength ?? 1200f) * Cs2Placement.InchesToEngine;
        public float FadeInSeconds => LengthFadeIn ?? 0.095f;

        /// <summary>
        /// Mid colour. The assault-rifle tracer carries no C_INIT_RandomColor - its
        /// colour comes from materials/effects/spark - so it falls back to white.
        /// </summary>
        public Color Tint {
            get {
                if (ColorMin is not { Length: >= 3 } || ColorMax is not { Length: >= 3 }) return Color.White;
                return new Color((byte)((ColorMin[0] + ColorMax[0]) / 2), (byte)((ColorMin[1] + ColorMax[1]) / 2),
                                 (byte)((ColorMin[2] + ColorMax[2]) / 2), (byte)255);
            }
        }
        public float AlphaMid => Alpha is { Length: >= 2 } ? (Alpha[0] + Alpha[1]) * 0.5f : 0.75f;
    }

    public sealed class Gun {
        public float[] MuzzlePos0 { get; set; }
        public float[] MuzzlePos1 { get; set; }
        public string TracerParticle { get; set; }
        public float? TracerFrequency { get; set; }
        public Dictionary<string, Flash> Flash { get; set; }
        public Tracer Tracer { get; set; }
    }

    sealed class EffectsFile {
        public string Format { get; set; }
        public Dictionary<string, Gun> Guns { get; set; }
    }

    const string Resource = "AnimationData.cs2_effects.json";
    const string ExpectedFormat = "ScCsgoKnives.Cs2Effects/1";
    static readonly Dictionary<string, Gun> s_guns = Load();

    public static Gun Get(string gun) => gun is not null && s_guns.TryGetValue(gun, out Gun g) ? g : null;

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
                KnifeDiagnostics.WarnOnce("cs2-effects-missing", $"No embedded {Resource}.");
                return loaded;
            }
            using Stream stream = assembly.GetManifestResourceStream(name);
            EffectsFile file = JsonSerializer.Deserialize<EffectsFile>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (file?.Format != ExpectedFormat || file.Guns is null) {
                KnifeDiagnostics.WarnOnce("cs2-effects-format", $"{Resource} is not {ExpectedFormat}.");
                return loaded;
            }
            foreach ((string gun, Gun g) in file.Guns) loaded[gun] = g;
            KnifeLog.Information(
                $"[ScCsgoKnives] CS2 effects: " + string.Join("; ", loaded.Select(kv =>
                    $"{kv.Key} muzzle0=({kv.Value.MuzzlePos0?[0]:0.###},{kv.Value.MuzzlePos0?[1]:0.###},{kv.Value.MuzzlePos0?[2]:0.###})"
                    + $" flash={kv.Value.Flash?.Count ?? 0} tracer every {TracerFrequency(kv.Key)}"))
            );
        }
        catch (Exception e) {
            KnifeDiagnostics.WarnOnce("cs2-effects-load", $"Could not read {Resource}: {e.Message}");
        }
        return loaded;
    }
}
