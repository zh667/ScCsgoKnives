using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Game;

/// <summary>
/// CS2's own sound trigger times for the gun clips, from AnimationData/cs2_sounds.json
/// (tools/cs2_sound_timings.py).
///
/// Where the shipped table in SubsystemScGunBlockBehavior was timed by watching the
/// magazine and bolt bones move, these are the CNmClipDocEvent_Sound frames written
/// into CS2's own .vnmclip tracks, divided by that clip's DMX frame rate. The two
/// disagree by up to 580 ms, so this is a switch rather than a silent replacement:
/// KnifeTuning.GunSoundProfile picks which one plays, and the old table stays the
/// default until the timings are checked against a CS2 recording.
///
/// Cues CS2 fires but the mod has no OGG for (WeaponMove1/2/3, AddAmmo,
/// Inspect_F245) carry a null Asset in the JSON and are dropped on load.
/// </summary>
public static class Cs2Sounds {
    sealed class Cue {
        public float At { get; set; }
        public float Frame { get; set; }
        public string Event { get; set; }
        public string Asset { get; set; }
    }

    sealed class ClipSounds {
        public string SourceClip { get; set; }
        public float FrameRate { get; set; }
        public List<Cue> Cues { get; set; }
    }

    sealed class SoundFile {
        public string Format { get; set; }
        public Dictionary<string, ClipSounds> Clips { get; set; }
    }

    const string Resource = "AnimationData.cs2_sounds.json";
    const string ExpectedFormat = "ScCsgoKnives.Cs2Sounds/1";

    /// <summary>Null when the file loaded; the reason otherwise. See Cs2Effects.LoadError.</summary>
    public static string LoadError { get; private set; } = "not loaded";

    static readonly Dictionary<string, (float At, string Name)[]> s_clips = Load();

    /// <summary>Cues for a "<spec>:<clip>" key, in CS2's order. False when CS2 has none.</summary>
    public static bool TryGet(string key, out (float At, string Name)[] cues) => s_clips.TryGetValue(key, out cues);

    public static int ClipCount => s_clips.Count;

    static Dictionary<string, (float At, string Name)[]> Load() {
        var loaded = new Dictionary<string, (float At, string Name)[]>(StringComparer.Ordinal);
        try {
            Assembly assembly = typeof(Cs2Sounds).Assembly;
            string name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(Resource, StringComparison.OrdinalIgnoreCase));
            if (name is null) {
                LoadError = $"no embedded {Resource}";
                KnifeDiagnostics.WarnOnce("cs2-sounds-missing", $"No embedded {Resource}; CS2 sound timings unavailable.");
                return loaded;
            }
            using Stream stream = assembly.GetManifestResourceStream(name);
            SoundFile file = JsonSerializer.Deserialize<SoundFile>(stream);
            if (file?.Format != ExpectedFormat || file.Clips is null) {
                LoadError = $"{Resource} is not {ExpectedFormat}";
                KnifeDiagnostics.WarnOnce("cs2-sounds-format", $"{Resource} is not {ExpectedFormat}; CS2 sound timings unavailable.");
                return loaded;
            }
            int dropped = 0;
            foreach ((string key, ClipSounds clip) in file.Clips) {
                var cues = new List<(float At, string Name)>();
                foreach (Cue cue in clip.Cues ?? []) {
                    if (string.IsNullOrEmpty(cue.Asset)) { dropped++; continue; }
                    cues.Add((cue.At, cue.Asset));
                }
                if (cues.Count != 0) loaded[key] = cues.ToArray();
            }
            LoadError = null;
            KnifeLog.Information(
                $"[ScCsgoKnives] CS2 sound timings: {loaded.Count} clips playable, "
                + $"{loaded.Values.Sum(c => c.Length)} cues, {dropped} cues have no shipped audio."
            );
        }
        catch (Exception e) {
            LoadError = $"{e.GetType().Name}: {e.Message}";
            KnifeDiagnostics.WarnOnce("cs2-sounds-load", $"Could not read {Resource}: {e.Message}");
        }
        return loaded;
    }
}
