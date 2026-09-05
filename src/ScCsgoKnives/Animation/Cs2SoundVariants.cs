using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game;

/// <summary>
/// How many numbered files each sound cue ships, e.g. ak47_fire_1..3.
///
/// Counted by tools/install_gun_sounds_cs2.py from the OGGs it installed and shipped
/// as data, because the count and the files have to agree and a hand-kept table does
/// not stay agreed: asking for ak47_fire_4 when three shipped plays nothing.
/// </summary>
public static class Cs2SoundVariants {
    const string Resource = "AnimationData.cs2_sound_variants.json";
    const string ExpectedFormat = "ScCsgoKnives.SoundVariants/1";

    sealed class File {
        [JsonPropertyName("Format")]
        public string Format { get; set; }
        [JsonPropertyName("Variants")]
        public Dictionary<string, int> Variants { get; set; }
    }

    /// <summary>Null when the file loaded; the reason otherwise.</summary>
    public static string LoadError { get; private set; } = "not loaded";

    public static readonly Dictionary<string, int> All = Load();

    static Dictionary<string, int> Load() {
        var loaded = new Dictionary<string, int>(StringComparer.Ordinal);
        try {
            Assembly assembly = typeof(Cs2SoundVariants).Assembly;
            string name = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(Resource, StringComparison.OrdinalIgnoreCase));
            if (name is null) {
                LoadError = $"no embedded {Resource}";
                KnifeDiagnostics.WarnOnce("cs2-variants-missing", $"No embedded {Resource}.");
                return loaded;
            }
            using Stream stream = assembly.GetManifestResourceStream(name);
            File file = JsonSerializer.Deserialize<File>(stream);
            if (file?.Format != ExpectedFormat || file.Variants is null) {
                LoadError = $"{Resource} is not {ExpectedFormat}";
                KnifeDiagnostics.WarnOnce("cs2-variants-format", $"{Resource} is not {ExpectedFormat}.");
                return loaded;
            }
            foreach ((string cue, int count) in file.Variants) loaded[cue] = count;
            LoadError = null;
            KnifeLog.Information($"[ScCsgoKnives] CS2 sound variants: {loaded.Count} cues.");
        }
        catch (Exception e) {
            LoadError = $"{e.GetType().Name}: {e.Message}";
            KnifeDiagnostics.WarnOnce("cs2-variants-load", $"Could not read {Resource}: {e.Message}");
        }
        return loaded;
    }
}
