using Engine.Audio;
namespace Game;

public static class ScCombatAudio {
    public const string KillSound = "Audio/ScCsgoKnives/bf1_kill_ding";
    public static bool PlayKill() {
        float volume = Math.Clamp(SettingsManager.SoundsVolume, 0, 1) * .9f;
        if (volume <= AudioManager.MinAudibleVolume) return false;
        try {
            // A centred UI confirmation, with no world-distance or simulation-speed filtering.
            new Sound(ContentManager.Get<SoundBuffer>(KillSound), volume, 1f, 0f, false, true).Play();
            return true;
        }
        catch (Exception e) {
            KnifeDiagnostics.WarnOnce("combat-kill-sound", $"Kill sound could not play: {e.Message}");
            return false;
        }
    }
}
