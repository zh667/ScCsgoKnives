using System;
using Engine;

namespace Game;

/// <summary>Engine.Log in the game; the console when the maths runs headless (tools/ArmPreview).</summary>
public static class KnifeLog {
    public static bool ToConsole;

    // Release builds omit the call AND argument formatting. Enable only in a dedicated
    // diagnostic build; ordinary player key bindings cannot turn this on.
    [System.Diagnostics.Conditional("SC_CSGO_DIAGNOSTICS")]
    public static void Trace(string message) => Information(message);

    public static void Information(string message) {
        if (ToConsole) { Console.Error.WriteLine(message); return; }
        try { Log.Information(message); } catch { }
    }

    public static void Warning(string message) {
        if (ToConsole) { Console.Error.WriteLine("WARN " + message); return; }
        try { Log.Warning(message); } catch { }
    }

    public static void Error(string message) {
        if (ToConsole) { Console.Error.WriteLine("ERROR " + message); return; }
        try { Log.Error(message); } catch { }
    }
}
