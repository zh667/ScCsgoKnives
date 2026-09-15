using Engine.Input;
using System.Runtime.CompilerServices;
namespace Game;

/// <summary>Extra gamepad actions, independent of the native movement and inventory bindings.</summary>
public static class ScGamepadBindings {
    public static readonly Dictionary<string, string> Keys = new(StringComparer.Ordinal);
    public static float Threshold = .5f;
    public static string Default(string id) => ""; // Do not silently bind over another player's native inventory actions.
    public static string Get(string id) => Keys.GetValueOrDefault(id, Default(id));
    public static bool Valid(string key) => key == "" || key is "TriggerLeft" or "TriggerRight"
        || Enum.TryParse<GamePadButton>(key, out var b) && Enum.IsDefined(b) && b != GamePadButton.Null;
    public static string[] Options() => new[] { "", "TriggerLeft", "TriggerRight" }.Concat(Enum.GetValues<GamePadButton>()
        .Where(b => b != GamePadButton.Null).Select(b => b.ToString())).ToArray();
    public static string Label(string key) => key switch {
        "" => "未设置额外手柄键", "TriggerLeft" => "左扳机 LT", "TriggerRight" => "右扳机 RT",
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
    public static bool Down(ComponentPlayer p, string id, bool once) {
        string key = Get(id);
        if (!Valid(key) || key.Length == 0) return false;
        var input = p.GameWidget.Input;
        var states = States.GetOrCreateValue(p);
        if (!states.TryGetValue(key, out var state)) states[key] = state = new();
        if (state.Frame != Engine.Time.FrameIndex) {
            bool down;
            if (key is "TriggerLeft" or "TriggerRight") {
                float position = input.GetPadTriggerPosition(key == "TriggerLeft" ? GamePadTrigger.Left : GamePadTrigger.Right);
                down = position >= (state.Down ? Math.Max(.05f, Threshold - .1f) : Threshold);
            } else down = input.IsPadButtonDown(Enum.Parse<GamePadButton>(key));
            // Native shoulder DownOnce is a release event (modifier combos). CS actions require a press edge.
            state.Once = down && !state.Down; state.Down = down; state.Frame = Engine.Time.FrameIndex;
        }
        return once ? state.Once : state.Down;
    }
}
