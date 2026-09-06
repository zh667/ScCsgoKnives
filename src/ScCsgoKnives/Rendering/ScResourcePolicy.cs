using System.Xml.Linq;
namespace Game;

public static class ScResourcePolicy {
    /// <summary>Reduced decorative particles: the Lite edition and the smaller Mini edition alike.</summary>
    public static bool Lite { get; private set; }
    /// <summary>The edition name from the package's ScCsgoKnivesEdition.xml (Full, Lite or Mini).</summary>
    public static string Edition { get; private set; } = "Full";
    public static void LoadEdition() {
        var edition = ContentManager.Get<XElement>("ScCsgoKnivesEdition");
        ConfigureEdition((string)edition.Attribute("Name") ?? "Full");
    }
    // One Configure overload only: the package self-tests find it by name through reflection.
    internal static void Configure(bool lite) => ConfigureEdition(lite ? "Lite" : "Full");
    internal static void ConfigureEdition(string edition) {
        Edition = edition;
        Lite = edition is "Lite" or "Mini";
    }
}
