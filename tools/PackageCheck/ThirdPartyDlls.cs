using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using Game;

/// <summary>Read-only DLL inputs from an explicit game Mods directory. No third-party files are rewritten.</summary>
sealed class ThirdPartyDlls : IDisposable {
    readonly Dictionary<string, byte[]> bytes = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Assembly> loaded = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, byte[]> RecipaediaAssets = new();
    public ThirdPartyDlls(string directory) {
        foreach (string path in Directory.EnumerateFiles(directory, "*.scmod")) {
            System.IO.Compression.ZipArchive zip;
            try { zip = ZipFile.OpenRead(path); }
            catch (InvalidDataException) { zip = new(ModsManager.GetDecipherStream(File.OpenRead(path)), ZipArchiveMode.Read); }
            using (zip) {
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("Assets/RecipaediaEX/") && e.FullName.EndsWith(".xml"))) {
                using var stream = entry.Open(); using var data = new MemoryStream(); stream.CopyTo(data); RecipaediaAssets[entry.FullName[7..^4]] = data.ToArray();
            }
            foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))) {
                string name = Path.GetFileNameWithoutExtension(entry.FullName);
                if (name.StartsWith("ScCsgo", StringComparison.Ordinal)) continue;
                using var stream = entry.Open(); using var data = new MemoryStream(); stream.CopyTo(data);
                if (bytes.TryGetValue(name, out var previous) && !previous.SequenceEqual(data.ToArray()))
                    throw new InvalidDataException("Conflicting third-party DLLs: " + name);
                bytes[name] = data.ToArray();
            }
            }
        }
        AssemblyLoadContext.Default.Resolving += Resolve;
    }
    Assembly Resolve(AssemblyLoadContext context, AssemblyName name) => bytes.ContainsKey(name.Name) ? Load(name.Name) : null;
    public Assembly Load(string name) {
        if (loaded.TryGetValue(name, out var result)) return result;
        var existing = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
        if (existing != null) return loaded[name] = existing;
        using var stream = new MemoryStream(bytes[name]);
        return loaded[name] = AssemblyLoadContext.Default.LoadFromStream(stream);
    }
    public string Hash(string name) => Convert.ToHexString(SHA256.HashData(bytes[name])).ToLowerInvariant();
    public void Dispose() => AssemblyLoadContext.Default.Resolving -= Resolve;
}
