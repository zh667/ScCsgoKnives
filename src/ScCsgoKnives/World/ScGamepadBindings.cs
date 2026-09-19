using Engine.Input;
using System.Runtime.CompilerServices;
namespace Game;

/// <summary>Extra gamepad actions, independent of the native movement and inventory bindings.</summary>
public static class ScGamepadBindings {
    public static readonly Dictionary<string, string> Keys = new(StringComparer.Ordinal);
    public static float Threshold = .5f;
    public static string Default(string id) => id switch {
        ScGunFunctions.Fire or ScGunFunctions.ThrowStrong or ScGunFunctions.Plant => "TriggerRight",
        ScGunFunctions.Scope or ScGunFunctions.Silencer or ScGunFunctions.Burst or ScGunFunctions.RevolverAlt or ScGunFunctions.ThrowWeak => "TriggerLeft",
        ScGunFunctions.Reload or ScGunFunctions.C4Timer => "LeftShoulder+X",
        ScGunFunctions.Inspect => "LeftShoulder+Y",
        ScGunFunctions.KnifeHeavy => "RightThumb", _=>""
    };
    public static string Get(string id) => Keys.GetValueOrDefault(id, Default(id));
    public static bool Valid(string key) => key == "" || key is "TriggerLeft" or "TriggerRight" or "LeftShoulder+X" or "LeftShoulder+Y"
        || Enum.TryParse<GamePadButton>(key, out var b) && Enum.IsDefined(b) && b != GamePadButton.Null;
    public static string[] Options() => new[] { "", "TriggerLeft", "TriggerRight", "LeftShoulder+X", "LeftShoulder+Y" }.Concat(Enum.GetValues<GamePadButton>()
        .Where(b => b != GamePadButton.Null).Select(b => b.ToString())).ToArray();
    public static string Label(string key) => key switch {
        "" => "未设置额外手柄键", "TriggerLeft" => "左扳机 LT", "TriggerRight" => "右扳机 RT",
        "LeftShoulder+X" => "按住 LB ＋ X", "LeftShoulder+Y" => "按住 LB ＋ Y",
        "LeftShoulder" => "左肩键 LB", "RightShoulder" => "右肩键 RB", "LeftThumb" => "左摇杆按下",
        "RightThumb" => "右摇杆按下", "DPadLeft" => "方向左", "DPadRight" => "方向右",
        "DPadUp" => "方向上", "DPadDown" => "方向下", "Start" => "菜单键", "Back" => "选择键",
        _ => "按键 " + key
    };
    sealed class TriggerState { public bool Down; public long Frame = -1; public bool Once; }
    static readonly ConditionalWeakTable<ComponentPlayer, Dictionary<string, TriggerState>> States = new();
    public static int ConnectedMask(ComponentPlayer p) {
        int mask = 0;
        for (int i = 0; i < 4; i++) if (((int)p.GameWidget.Input.Devices & (2048 << i)) != 0 && GamePad.IsConnected(i)) mask |= 1 << i;
        return mask;
    }
    public static bool NativeAimBinding(string id) => SettingsManager.GamepadMappingSettings is not null &&
        (Get(id) is "TriggerLeft" && SettingsManager.GetGamepadMapping("Aim",false) is GamePadTrigger.Left
        || Get(id) is "TriggerRight" && SettingsManager.GetGamepadMapping("Aim",false) is GamePadTrigger.Right);
    public static bool HasNativeConflict(string key) {
        if(SettingsManager.GamepadMappingSettings is null)return false;
        foreach(var pair in SettingsManager.GamepadMappingSettings) {
            if(pair.Value is TemplatesDatabase.ValuesDictionary combo && key.Contains('+')) {
                var parts=key.Split('+');
                if(combo.GetValue<object>("ModifierKey",null)?.ToString()==parts[0] && combo.GetValue<object>("ActionKey",null)?.ToString()==parts[1])return true;
            }
            else if(pair.Value is GamePadButton b && b!=GamePadButton.Null && b.ToString()==key)return true;
        }
        return false;
    }
    public static bool Down(ComponentPlayer p, string id, bool once) {
        string key = Get(id);
        if (!Valid(key) || key.Length == 0) return false;
        // An existing customized vanilla action takes priority over an implicit default.
        if(key==Default(id) && HasNativeConflict(key))return false;
        var input = p.GameWidget.Input;
        var states = States.GetOrCreateValue(p);
        if (!states.TryGetValue(key, out var state)) states[key] = state = new();
        if (state.Frame != Engine.Time.FrameIndex) {
            bool down;
            if (key is "TriggerLeft" or "TriggerRight") {
                float position = input.GetPadTriggerPosition(key == "TriggerLeft" ? GamePadTrigger.Left : GamePadTrigger.Right);
                down = position >= (state.Down ? Math.Max(.05f, Threshold - .1f) : Threshold);
            } else if(key.Contains('+')) {
                var parts=key.Split('+');var modifier=Enum.Parse<GamePadButton>(parts[0]);var action=Enum.Parse<GamePadButton>(parts[1]);
                down=false;
                if(!input.m_isCleared)for(int i=0;i<4;i++)if(((int)input.Devices&(2048<<i))!=0 && GamePad.IsButtonDown(i,modifier)&&GamePad.IsButtonDown(i,action)) {
                    down=true;
                    // Native shoulders fire on RELEASE. Register use of this chord so releasing LB
                    // does not also edit/open the block in front of the player.
                    GamePad.SetModifierKeyOfCurrentCombo(i,modifier);
                }
            } else down = input.IsPadButtonDown(Enum.Parse<GamePadButton>(key));
            // Native shoulder DownOnce is a release event (modifier combos). CS actions require a press edge.
            state.Once = down && !state.Down; state.Down = down; state.Frame = Engine.Time.FrameIndex;
        }
        return once ? state.Once : state.Down;
    }
}
