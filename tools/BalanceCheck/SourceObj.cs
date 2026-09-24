using Game;
// Same OBJ decoder as PackageCheck, reading source files without creating a package.
static class SourceObj {
 public static void Install(System.Reflection.Assembly mod) {
 var cache=new Dictionary<string,(float[],float[],int[])>();
 foreach(string path in Directory.EnumerateFiles("src/ScCsgoKnives/Assets/Models/ScCsgoKnives","*.obj")) {
            var positions = new List<float>(); var uvs = new List<float>(); var outPos = new List<float>(); var outUv = new List<float>(); var indices = new List<int>();
            var map = new Dictionary<(int, int), int>();
            using var reader = new StreamReader(File.OpenRead(path));
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
            cache[Path.GetFileNameWithoutExtension(path)] = (outPos.ToArray(), outUv.ToArray(), indices.ToArray());
        }
 Func<string,string,(float[],float[],int[])> provider=(asset,part)=>cache.TryGetValue($"{asset}_cs2_{part}",out var hit)?hit:([],[],[]);
 mod.GetType("Game.ScThirdPersonWeapon").GetField("ObjProvider").SetValue(null,provider);
 }
}
