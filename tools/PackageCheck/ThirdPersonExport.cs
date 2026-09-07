using System.IO.Compression;
using System.Reflection;
using Engine.Media;

/// <summary>Writes the packaged mod's own third-person pose (vanilla human + baked weapon, world triangles) for
/// tools/third_person_render.py, so arm and grip placement can be looked at without a device.</summary>
static class ThirdPersonExport {
    /// <summary>The OBJ pieces of the three binding-driven guns, read from the package for the headless bake.</summary>
    internal static void ProvideObj(Assembly mod, string package) {
        using var zip = ZipFile.OpenRead(package);
        var cache = new Dictionary<string, (float[], float[], int[])>();
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".obj", StringComparison.OrdinalIgnoreCase) && e.FullName.Contains("Models/ScCsgoKnives/"))) {
            var positions = new List<float>(); var uvs = new List<float>(); var outPos = new List<float>(); var outUv = new List<float>(); var indices = new List<int>();
            var map = new Dictionary<(int, int), int>();
            using var reader = new StreamReader(entry.Open());
            string line;
            while ((line = reader.ReadLine()) is not null) {
                var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (f.Length == 0) continue;
                if (f[0] == "v") { positions.Add(float.Parse(f[1], System.Globalization.CultureInfo.InvariantCulture)); positions.Add(float.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture)); positions.Add(float.Parse(f[3], System.Globalization.CultureInfo.InvariantCulture)); }
                else if (f[0] == "vt") { uvs.Add(float.Parse(f[1], System.Globalization.CultureInfo.InvariantCulture)); uvs.Add(f.Length > 2 ? float.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture) : 0); }
                else if (f[0] == "f") {
                    var corners = new List<int>();
                    for (int i = 1; i < f.Length; i++) {
                        var c = f[i].Split('/'); int v = int.Parse(c[0]) - 1, vt = c.Length > 1 && c[1].Length > 0 ? int.Parse(c[1]) - 1 : -1;
                        if (!map.TryGetValue((v, vt), out int index)) {
                            index = outPos.Count / 3; map[(v, vt)] = index;
                            outPos.Add(positions[v * 3]); outPos.Add(positions[v * 3 + 1]); outPos.Add(positions[v * 3 + 2]);
                            outUv.Add(vt >= 0 ? uvs[vt * 2] : 0); outUv.Add(vt >= 0 ? uvs[vt * 2 + 1] : 0);
                        }
                        corners.Add(index);
                    }
                    for (int i = 1; i + 1 < corners.Count; i++) { indices.Add(corners[0]); indices.Add(corners[i]); indices.Add(corners[i + 1]); }
                }
            }
            cache[Path.GetFileNameWithoutExtension(entry.FullName)] = (outPos.ToArray(), outUv.ToArray(), indices.ToArray());
        }
        Func<string, string, (float[], float[], int[])> provider = (asset, part) => cache.TryGetValue($"{asset}_cs2_{part}", out var hit) ? hit : ([], [], []);
        mod.GetType("Game.ScThirdPersonWeapon").GetField("ObjProvider").SetValue(null, provider);
    }
    internal static void Write(Assembly mod, string vanillaContent, string directory) {
        Directory.CreateDirectory(directory);
        using var zip = ZipFile.OpenRead(vanillaContent);
        using var stream = zip.GetEntry("Assets/Models/HumanMale.dae").Open(); using var ms = new MemoryStream(); stream.CopyTo(ms); ms.Position = 0;
        ModelData human = Collada.Load(ms);
        var preview = mod.GetType("Game.ScThirdPerson").GetMethod("PreviewJson");
        string[] assets = ["ak47", "awp", "glock18", "elite", "taser", "m249", "nova", "karambit", "grenade_hegrenade", "grenade_molotov", "mp9", "deagle"];
        foreach (string asset in assets) foreach ((string name, float pitch) in new[] { ("level", 0f), ("up", .6f), ("down", -.6f) }) {
            try {
                string json = (string)preview.Invoke(null, [human, asset, 0f, pitch]);
                File.WriteAllText(Path.Combine(directory, $"{asset}_{name}.json"), json);
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(directory, $"{asset}_{name}.error.txt"), e.ToString()); }
        }
    }
}
