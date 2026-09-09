using Engine;
using Engine.Input;
namespace Game;

/// <summary>Device-local keyboard bindings. Touch mappers send the same keys on Android.
/// Independent of the mod's touch-button enable switch; never changes vanilla bindings.</summary>
public static class ScGunBindings {
    public static readonly Dictionary<string, string> Keys = new(StringComparer.Ordinal);
    public static string Default(string id) => id switch { ScGunFunctions.Reload => "R", ScGunFunctions.Inspect => "G", _ => "" };
    public static void Reset() { Keys.Clear(); foreach (string id in ScGunFunctions.All) Keys[id] = Default(id); }
    public static string Get(string id) => Keys.GetValueOrDefault(id, Default(id));
    public static bool Valid(string text) => text == "" || Enum.TryParse<Key>(text, out var key) && Enum.IsDefined(key) && key is not (Key.Null or Key.Escape or Key.Back);
    public static bool Available(ComponentPlayer p) => Window.IsActive && !ScWeaponTouchPanel.MenuActive
        && (ScreensManager.CurrentScreen is null || ReferenceEquals(ScreensManager.CurrentScreen, ScreensManager.FindScreen<Screen>("Game")))
        && !ScreensManager.IsAnimating && p.ComponentHealth.Health > 0 && p.ComponentGui.ModalPanelWidget is null
        && !DialogsManager.HasDialogs(p.GuiWidget) && !DialogsManager.HasDialogs(ScreensManager.RootWidget);
    public static bool Down(ComponentPlayer p, string id, bool once = false) {
        if (!Available(p) || !Enum.TryParse<Key>(Get(id), out var key) || key == Key.Null) return false;
        return once ? p.GameWidget.Input.IsKeyDownOnce(key) : p.GameWidget.Input.IsKeyDown(key);
    }
    // Secondary actions are mutually exclusive across guns. Other shared keys would trigger two actions.
    public static bool Conflict(string a, string b) {
        bool Secondary(string s) => s is ScGunFunctions.Scope or ScGunFunctions.Silencer or ScGunFunctions.Burst or ScGunFunctions.RevolverAlt;
        if (Secondary(a) && Secondary(b)) return false;
        bool KnifeOnly(string s) => s == ScGunFunctions.KnifeHeavy;
        bool GrenadeOnly(string s) => s is ScGunFunctions.ThrowWeak or ScGunFunctions.ThrowStrong;
        if (a == ScGunFunctions.Inspect || b == ScGunFunctions.Inspect) return true;
        if (KnifeOnly(a) || KnifeOnly(b)) return a == ScGunFunctions.Fire || b == ScGunFunctions.Fire || KnifeOnly(a) && KnifeOnly(b);
        if (GrenadeOnly(a) || GrenadeOnly(b)) return GrenadeOnly(a) && GrenadeOnly(b);
        return true;
    }
    public static string Label(string id) => id == ScGunFunctions.Fire ? "开火／轻刀" : ScGunFunctions.Label(id);
}
