using System.Xml.Linq;
namespace Game;

public static class ScResourcePolicy {
    public static bool Lite { get; private set; }
    public static void LoadEdition() {
        var edition = ContentManager.Get<XElement>("ScCsgoKnivesEdition");
        Configure(string.Equals((string)edition.Attribute("Name"), "Lite", StringComparison.Ordinal));
    }
    internal static void Configure(bool lite) => Lite = lite;
}
