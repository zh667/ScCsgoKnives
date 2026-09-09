using Engine;
namespace Game;

/// <summary>Android app: is the APK AssetManager, not a writable directory. Use the same document
/// root as the game's own Settings.xml. Desktop keeps its existing filenames and user configuration.</summary>
public static class ScLocalSettings {
    public static string PathFor(string filename) => Resolve(filename, OperatingSystem.IsAndroid(), ModsManager.DocPath);
    internal static string Resolve(string filename, bool android, string documentRoot) {
        if (string.IsNullOrWhiteSpace(filename) || filename.IndexOfAny(['/', '\\', ':']) >= 0)
            throw new ArgumentException("Expected a local settings filename", nameof(filename));
        if (!android) return "app:/" + filename;
        if (string.IsNullOrWhiteSpace(documentRoot) || documentRoot.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Android document root must not point into APK assets");
        return Storage.CombinePaths(documentRoot, filename);
    }
}
