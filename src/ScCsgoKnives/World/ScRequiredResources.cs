using System.Xml.Linq;
namespace Game;

/// <summary>Both standalone editions carry a checked resource marker and their resource assembly.</summary>
public static class ScRequiredResources {
    public const string MinVersion = "1.10.2";
    public static void Validate() {
        // Validate before world parsing; do not let missing resources masquerade as missing skins.
        ValidateMarker(ContentManager.Get<XElement>("ScCsgoResources"));
    }
    public static void ValidateMarker(XElement marker) {
        bool supported = (string)marker.Attribute("Edition") is "Full" or "Optimized512"
            && (string)marker.Attribute("Format") == "1"
            && ScAnimationResources.Assembly.GetName().Version?.Major == 1
            && VersionAtLeast((string)marker.Attribute("Version"), MinVersion);
        if (!supported)
            throw new InvalidOperationException("CS 武器内置资源缺失或不兼容，请重新安装完整的全量版或 512 轻量版文件。");
    }
    static bool VersionAtLeast(string value, string minimum) {
        if (!Version.TryParse(value, out var have) || !Version.TryParse(minimum, out var need)) return false;
        return have >= need;
    }
}
