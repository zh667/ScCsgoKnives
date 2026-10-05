using Engine;
using Engine.Graphics;
using GameEntitySystem;
namespace Game;

/// <summary>The package's own presentation resources (tools/import_cs2_dm_assets.py: CS2's kill-method icons, its respawn
/// and match-end sounds). Every use tolerates a missing resource: an icon falls back to its word, a sound to silence -
/// a rule never depends on either.</summary>
public static class DmArt {
    static readonly Dictionary<string, Texture2D> s_icons = new(StringComparer.Ordinal);
    static readonly HashSet<string> s_missing = new(StringComparer.Ordinal);
    /// <summary>A kill-method icon ("headshot", "noscope", "smoke", "penetrate", "blind", "suicide"), or null.</summary>
    public static Texture2D Icon(string name) {
        if (s_icons.TryGetValue(name, out var texture)) return texture;
        if (s_missing.Contains(name)) return null;
        try { texture = ContentManager.Get<Texture2D>("Textures/ScCsgoDeathmatch/kill_" + name); s_icons[name] = texture; return texture; }
        catch (Exception e) { s_missing.Add(name); KnifeDiagnostics.WarnOnce("dm-icon-" + name, $"[CS_DM] icon kill_{name} unavailable ({e.GetType().Name}); its word is shown instead"); return null; }
    }
    /// <summary>Plays one of the package's sounds once: at a place in the world, or (null) for this listener only.</summary>
    public static void Play(Project project, string name, Vector3? position, float volume = 1f) {
        if (s_missing.Contains("sound:" + name) || project?.FindSubsystem<SubsystemAudio>(false) is not { } audio) return;
        try {
            if (position is { } at) audio.PlaySound("Audio/ScCsgoDeathmatch/" + name, volume, 0f, at, 6f, true);
            else audio.PlaySound("Audio/ScCsgoDeathmatch/" + name, volume, 0f, 0f, 0f);
        }
        catch (Exception e) { s_missing.Add("sound:" + name); KnifeDiagnostics.WarnOnce("dm-sound-" + name, $"[CS_DM] sound {name} unavailable ({e.GetType().Name}: {e.Message}); playing nothing"); }
    }
    /// <summary>The marks of a kill, in the order they are shown, each with its icon's name and its word.</summary>
    public static IEnumerable<(string Icon, string Word)> Marks(DmKill kill) {
        if (kill.AttackerBlind) yield return ("blind", "致盲中");
        if (kill.ThroughSmoke) yield return ("smoke", "穿烟");
        if (kill.NoScope) yield return ("noscope", "盲狙");
        if (kill.Penetration) yield return ("penetrate", "穿透");
        if (kill.Headshot) yield return ("headshot", "爆头");
    }
}
