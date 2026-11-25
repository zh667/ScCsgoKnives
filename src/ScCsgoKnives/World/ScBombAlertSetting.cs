using System.Runtime.CompilerServices;
namespace Game;

/// <summary>1.6.0 (user 2026-11-25): the conspicuous enemy-C4 alert (large message with direction and distance, the line
/// under the top of the screen and the marker drawn through terrain) is a switch on the game's own mod settings page
/// (modinfo "Settings": page <see cref="Page"/>, item <see cref="Item"/>; texts under ModSettings/zh667.ScCsgoKnives/... in
/// Assets/Lang). Off by default: a planted enemy bomb then gives what 1.5.0 gave (the plant sound, the bomb on the ground,
/// the beeps that speed up as the fuse runs and the defuse prompt next to it). Each device reads its own value, so a
/// multiplayer client follows its own setting; the server decides nothing here.</summary>
public static class ScBombAlertSetting {
    public const string Package = "zh667.ScCsgoKnives", Page = "Alerts", Item = "BombAlert";
    static int s_state = -1; // -1: read again; 0 off; 1 on
    /// <summary>Self-tests only: the value every caller sees while set.</summary>
    internal static bool? Forced;
    public static bool Enabled {
        get {
            if (Forced is bool forced) return forced;
            if (s_state < 0) { bool on; try { on = Read(); } catch (System.Exception) { on = false; } s_state = on ? 1 : 0; }
            return s_state == 1;
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool Read() => ModSettingsManager.TryGet(out bool value, Package, Page, Item) && value;
    /// <summary>The mod settings page changed a value of this package (ModLoader.OnModSettingChanged, idPath = [page, item]).</summary>
    public static void Changed(string[] idPath) { if (idPath is { Length: > 0 } && idPath[^1] == Item) s_state = -1; }
    /// <summary>Read the stored value again (a world loads).</summary>
    public static void Reload() => s_state = -1;
}
