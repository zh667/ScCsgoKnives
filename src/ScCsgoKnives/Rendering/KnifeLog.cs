using System;
using Engine;

namespace Game;

/// <summary>Engine.Log in the game; the console when the maths runs headless (tools/ArmPreview).</summary>
public static class KnifeLog {
    public static bool ToConsole;

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
