using Engine;

namespace Game;

public static class KnifeDiagnostics {
    static readonly HashSet<string> s_warnings = new(StringComparer.Ordinal);

    public static bool IsFinite(Matrix matrix) =>
        float.IsFinite(matrix.M11) && float.IsFinite(matrix.M12) && float.IsFinite(matrix.M13) && float.IsFinite(matrix.M14)
        && float.IsFinite(matrix.M21) && float.IsFinite(matrix.M22) && float.IsFinite(matrix.M23) && float.IsFinite(matrix.M24)
        && float.IsFinite(matrix.M31) && float.IsFinite(matrix.M32) && float.IsFinite(matrix.M33) && float.IsFinite(matrix.M34)
        && float.IsFinite(matrix.M41) && float.IsFinite(matrix.M42) && float.IsFinite(matrix.M43) && float.IsFinite(matrix.M44);

    public static string MatrixSummary(Matrix matrix) =>
        $"T=({matrix.M41:0.###},{matrix.M42:0.###},{matrix.M43:0.###}), "
        + $"X=({matrix.M11:0.###},{matrix.M12:0.###},{matrix.M13:0.###}), "
        + $"Y=({matrix.M21:0.###},{matrix.M22:0.###},{matrix.M23:0.###}), "
        + $"Z=({matrix.M31:0.###},{matrix.M32:0.###},{matrix.M33:0.###})";

    public static void WarnOnce(string key, string message) {
        if (s_warnings.Add(key)) Log.Warning($"[ScCsgoKnives] {message}");
    }
}
