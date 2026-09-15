using System.Xml.Linq;
namespace Game;

/// <summary>The full-resolution resource pack must be installed, but any 1.x pack at or above the version this
/// core was built against is accepted: an icon or asset cleanup does not force every player onto one exact
/// resource version. The pack's own engine dependency already enforces the same floor.</summary>
public static class ScRequiredResources {
    public const string MinVersion = "1.10.2";
    public static void Validate() {
        // Validate before world parsing; do not let missing resources masquerade as missing skins.
        var marker = ContentManager.Get<XElement>("ScCsgoResources");
        bool supported = (string)marker.Attribute("Edition") == "Full"
            && (string)marker.Attribute("Format") == "1"
            && ScAnimationResources.Assembly.GetName().Version?.Major == 1
            && VersionAtLeast((string)marker.Attribute("Version"), MinVersion);
        if (!supported)
            throw new InvalidOperationException($"请安装主包配套的 CS 武器资源包：全量版 {MinVersion} 或更高，不要只安装主包。");
    }
    static bool VersionAtLeast(string value, string minimum) {
        if (!Version.TryParse(value, out var have) || !Version.TryParse(minimum, out var need)) return false;
        return have >= need;
    }
}
