using System.Text.Json;
namespace Game;

/// <summary>Small resource manifest. Finding a model must not deserialize its complete animations.</summary>
public static class Cs2Catalog {
    public sealed class Entry {
        public string Skinned { get; set; }
        public string Parts { get; set; }
        public string[] MeshParts { get; set; }
    }
    static readonly Dictionary<string, Entry> Entries = Load();
    static Dictionary<string, Entry> Load() {
        var assembly = typeof(Cs2Catalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith(".cs2_catalog.json")));
        return JsonSerializer.Deserialize<Dictionary<string, Entry>>(stream);
    }
    public static Entry Get(string name) => name is not null && Entries.TryGetValue(name, out var entry) ? entry : null;
}
