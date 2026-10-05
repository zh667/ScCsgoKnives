using System;
using Engine;

namespace Game;

/// <summary>Engine.Log in the game; the console when the maths runs headless (tools/ArmPreview).</summary>
public static class KnifeLog {
    public static bool ToConsole;

    /// <summary>Detail for the test tools: periodic memory, performance and spawn status, resource timings, network
    /// operations. Off in players' games; on when the process runs with SCCS_DIAGNOSTICS=1 (the test harnesses) or a
    /// regression sets it. Release 1.4.0 (2026-10-01 user: "日志相关代码只保留必要的"): an ordinary Game.log keeps
    /// warnings, errors and one-line records of loading, compatibility decisions and save-data changes only.</summary>
    public static bool Diagnostics = Environment.GetEnvironmentVariable("SCCS_DIAGNOSTICS") == "1";

    /// <summary>Writes only while <see cref="Diagnostics"/> is on. Callers that build costly text check the flag first.</summary>
    public static void Diagnostic(string message) { if (Diagnostics) Information(message); }

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
