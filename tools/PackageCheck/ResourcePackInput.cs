using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Game;

/// <summary>Validate actual separately shipped archives, then expose their disjoint union to
/// existing package tests. The union is temporary, not a deliverable and never hides collisions.</summary>
static class ResourcePackInput {
    /// <summary>True when the core's dependency admits this resource version: exact `[x]`, bare `x` (minimum) or a
    /// NuGet interval `[lo,hi)`. Keeps an asset-cleanup release from pinning every player to one exact pack.</summary>
    static bool DependencyAccepts(string spec, string version) {
        spec=(spec??"").Trim();
        var have=ParseVersion(version);
        if(spec.Length==0)return true;
        if(spec[0] is '[' or '(') {
            bool incLo=spec[0]=='[',incHi=spec[^1]==']';
            var inner=spec.Trim('[',']','(',')').Split(',');
            if(inner.Length>0&&inner[0].Length>0) { var lo=ParseVersion(inner[0]); if(have<lo||(have==lo&&!incLo))return false; }
            if(inner.Length>1&&inner[1].Length>0) { var hi=ParseVersion(inner[1]); if(have>hi||(have==hi&&!incHi))return false; }
            return true;
        }
        return have>=ParseVersion(spec);
    }
    static System.Version ParseVersion(string text) => System.Version.TryParse(text,out var v) ? v : new System.Version(0,0);
    internal static Assembly Animations(Assembly core) =>
        (Assembly)core.GetType("Game.ScAnimationResources")?.GetProperty("Assembly")?.GetValue(null) ?? core;
    internal static string Merge(string corePath,string resourcePath,string directory) {
        using var core=ZipFile.OpenRead(corePath);
        using var infoStream=core.GetEntry("modinfo.json").Open();
        using var info=JsonDocument.Parse(infoStream);
        bool required=info.RootElement.TryGetProperty("Dependencies",out var deps)&&deps.ValueKind==JsonValueKind.Object&&deps.TryGetProperty("zh667.ScCsgoResources",out _);
        if(resourcePath is null) {
            if(required)throw new InvalidDataException("This core requires --resource-pack <matching ScCsgoResources.scmod>; core alone is not a valid installation.");
            return corePath;
        }
        if(!required)throw new InvalidDataException("Resource pack supplied for a monolithic/unsupported core.");
        using var resources=ZipFile.OpenRead(resourcePath);
        using var metaStream=resources.GetEntry("modinfo.json").Open();
        using var meta=JsonDocument.Parse(metaStream);
        string version=meta.RootElement.GetProperty("Version").GetString();
        if(meta.RootElement.GetProperty("PackageName").GetString()!="zh667.ScCsgoResources"
            || !DependencyAccepts(deps.GetProperty("zh667.ScCsgoResources").GetString(), version))
            throw new InvalidDataException("Resource pack version/name mismatch.");
        bool Asset(string n)=>n.StartsWith("Assets/Textures/")||n.StartsWith("Assets/Models/")||n.StartsWith("Assets/Audio/");
        if(core.Entries.Any(e=>Asset(e.FullName)||e.Name=="ScCsgoResources.dll"))throw new InvalidDataException("Core still contains split assets.");
        if(resources.Entries.Any(e=>!Asset(e.FullName)&&e.FullName is not ("modinfo.json" or "ScCsgoResources.dll" or "LICENSE" or "ASSET_SOURCES.md" or "THIRD_PARTY_NOTICES.md" or "Assets/ScCsgoResources.xml")))
            throw new InvalidDataException("Unexpected gameplay/config files in resource-only pack.");
        using var markerStream=resources.GetEntry("Assets/ScCsgoResources.xml").Open();var marker=XElement.Load(markerStream);
        if((string)marker.Attribute("Version")!=version||((version,(string)marker.Attribute("Edition")) is not ("1.0.0","Full") and not ("1.1.0","Optimized512") and not ("1.2.0","Full") and not ("1.3.0","Optimized512") and not ("1.4.0","Full") and not ("1.5.0","Optimized512") and not ("1.6.0","Full") and not ("1.7.0","Optimized512") and not ("1.8.0","Full") and not ("1.9.0","Optimized512") and not ("1.10.0","Full") and not ("1.10.1","Full") and not ("1.10.2","Full") and not ("1.10.3","Full") and not ("1.10.4","Full")))throw new InvalidDataException("Wrong resource marker.");
        var listed=marker.Elements("File").ToArray();
        if(listed.Length!=resources.Entries.Count(e=>Asset(e.FullName)))throw new InvalidDataException("Resource manifest coverage mismatch.");
        foreach(var f in listed) {
            using var data=(resources.GetEntry((string)f.Attribute("Path"))??throw new InvalidDataException("Missing resource")).Open();
            if(!Convert.ToHexString(SHA256.HashData(data)).Equals((string)f.Attribute("Sha256"),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Corrupt resource: "+(string)f.Attribute("Path"));
        }
        string path=Path.Combine(directory,"effective-installation.scmod");
        using(var output=ZipFile.Open(path,ZipArchiveMode.Create)) {
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var archive in new[]{core,resources})foreach(var entry in archive.Entries) {
                if(ReferenceEquals(archive,resources)&&entry.FullName is "modinfo.json" or "LICENSE" or "ASSET_SOURCES.md" or "THIRD_PARTY_NOTICES.md")continue;
                if(!seen.Add(entry.FullName))throw new InvalidDataException("Duplicate installation path: "+entry.FullName);
                using var input=entry.Open();using var target=output.CreateEntry(entry.FullName,CompressionLevel.NoCompression).Open();input.CopyTo(target);
            }
        }
        Console.Error.WriteLine($"Validated split resource pack: {Path.GetFullPath(resourcePath)}; assets={listed.Length}");
        return path;
    }
}
