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
    public static string KeyLabel(string text) {
        if(string.IsNullOrEmpty(text))return "未设置额外键盘键";
        if(!Enum.TryParse<Key>(text,out var key) || !Enum.IsDefined(key))return "未知按键";
        if(key>=Key.A && key<=Key.Z)return "字母 "+key;
        if(key>=Key.F1 && key<=Key.F12)return "功能键 "+key;
        if(key>=Key.Number0 && key<=Key.Number9)return "数字 "+((int)key-(int)Key.Number0);
        return key switch {
            Key.Null=>"无",Key.Back=>"返回键",Key.Shift=>"上档键（Shift）",Key.Control=>"控制键（Ctrl）",Key.Alt=>"换档键（Alt）",
            Key.LeftArrow=>"左方向键 ←",Key.RightArrow=>"右方向键 →",Key.UpArrow=>"上方向键 ↑",Key.DownArrow=>"下方向键 ↓",
            Key.Enter=>"回车键",Key.Escape=>"退出键",Key.Space=>"空格键",Key.Tab=>"制表键（Tab）",Key.BackSpace=>"退格键",
            Key.Insert=>"插入键",Key.Delete=>"删除键",Key.PageUp=>"向上翻页键",Key.PageDown=>"向下翻页键",
            Key.Home=>"行首键",Key.End=>"行尾键",Key.CapsLock=>"大写锁定键",
            Key.Tilde=>"反引号／波浪号键 ` ~",Key.Minus=>"减号键 -",Key.Plus=>"等号／加号键 = +",
            Key.LeftBracket=>"左方括号键 [",Key.RightBracket=>"右方括号键 ]",Key.Semicolon=>"分号键 ;",
            Key.Quote=>"引号键",Key.Comma=>"逗号键 ,",Key.Period=>"句号键 .",Key.Slash=>"斜杠键 /",Key.BackSlash=>"反斜杠键 \\",
            _=>"未知按键"
        };
    }
    public static string NativeBinding(string id) {
        if(SettingsManager.KeyboardMappingSettings is null)return ""; // before native settings initialization
        string[] names = id switch {
            ScGunFunctions.Fire or ScGunFunctions.ThrowStrong => ["Hit","Dig"],
            ScGunFunctions.Scope or ScGunFunctions.Silencer or ScGunFunctions.Burst or ScGunFunctions.RevolverAlt
                or ScGunFunctions.KnifeHeavy or ScGunFunctions.ThrowWeak => ["Aim"],
            _ => []
        };
        string Describe(object input)=>input switch {
            MouseButton.Left=>"鼠标左键",MouseButton.Right=>"鼠标右键",MouseButton.Middle=>"鼠标中键",
            MouseButton.Ext1=>"鼠标侧键一",MouseButton.Ext2=>"鼠标侧键二",
            Key k when k!=Key.Null=>KeyLabel(k.ToString()),_=>null
        };
        return string.Join("／",names.Select(n=>Describe(SettingsManager.GetKeyboardMapping(n,false))).Where(s=>s is not null).Distinct());
    }
    public static string BindingSummary(string id,string extraKey) {
        string native=NativeBinding(id);
        string extra=string.IsNullOrEmpty(extraKey)?"":KeyLabel(extraKey);
        return Label(id)+"："+(native.Length>0 ? native+(extra.Length>0?" ＋ "+extra:"") : extra.Length>0?extra:"点击添加键盘键");
    }
}
