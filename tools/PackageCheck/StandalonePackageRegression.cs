using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;
using Game;

static class StandalonePackageRegression {
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod, string path) {
        List<Result> checks = [];
        void Check(string n, bool ok, string detail = "") => checks.Add(new("standalone/" + n, ok, detail));
        using var zip = ZipFile.OpenRead(path);
        using var metaReader = new StreamReader(zip.GetEntry("modinfo.json").Open());
        var info = ModsManager.DeserializeJson(metaReader.ReadToEnd());
        if (info.DependencyRanges.Count != 0 || info.Version != "1.1.0") return checks;
        try {
            Check("resource-assembly-bundled", zip.GetEntry("ScCsgoResources.dll") is not null
                && ResourcePackInput.Animations(mod).GetManifestResourceNames().Any(n => n.EndsWith("c4.cs2.animation.json")));
            using var input = zip.GetEntry("Assets/ScCsgoResources.xml").Open();
            var marker = XElement.Load(input);
            var validate = mod.GetType("Game.ScRequiredResources").GetMethod("ValidateMarker");
            validate.Invoke(null, [marker]);
            Check("bundled-marker-accepted", true, marker.Attribute("Edition")?.Value);
            foreach (string attribute in new[] { "Edition", "Format", "Version" }) {
                var bad = new XElement(marker); bad.SetAttributeValue(attribute, "invalid");
                bool refused = false;
                try { validate.Invoke(null, [bad]); } catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { refused = true; }
                Check("invalid-marker-refused/" + attribute, refused);
            }
            var listed = marker.Elements("File").ToArray();
            bool Asset(string n) => n.StartsWith("Assets/Textures/") || n.StartsWith("Assets/Models/") || n.StartsWith("Assets/Audio/");
            Check("resource-manifest-complete", listed.Select(e => (string)e.Attribute("Path")).Order()
                .SequenceEqual(zip.Entries.Where(e => Asset(e.FullName)).Select(e => e.FullName).Order()));
            foreach (var file in listed) {
                string name = (string)file.Attribute("Path");
                using var data = zip.GetEntry(name).Open();
                Check("resource-hash/" + name, Convert.ToHexString(SHA256.HashData(data))
                    .Equals((string)file.Attribute("Sha256"), StringComparison.OrdinalIgnoreCase));
            }
            Check("native-metadata-self-contained", info.PackageName == "zh667.ScCsgoKnives" && info.DependencyRanges.Count == 0);
        } catch (Exception e) { Check("failure", false, e.ToString()); }
        return checks;
    }
}
