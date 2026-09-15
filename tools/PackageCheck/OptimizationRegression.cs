using System.IO.Compression;
using System.Reflection;
using System.Xml.Linq;
using Engine.Media;

static class OptimizationRegression {
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod, string package) {
        var results = new List<Result>();
        void Check(string name, bool ok, string detail="") => results.Add(new("optimization/"+name,ok,detail));
        using var zip = ZipFile.OpenRead(package);
        var markerEntry = zip.GetEntry("Assets/ScCsgoKnivesEdition.xml");
        using var markerStream = markerEntry.Open();
        bool optimized = (string)XElement.Load(markerStream).Attribute("Name") == "Optimized512";
        if (optimized) {
            var images = zip.Entries.Where(e => e.FullName.StartsWith("Assets/Textures/")).ToArray();
            // 555 includes the deterministic Gamma Doppler knife finish maps and
            // the C4 visual set added alongside the mobile resource pass.
            Check("texture-count-and-format",images.Length==555 && images.All(e=>e.Name.EndsWith(".webp")));
            foreach (var entry in images) {
                try {
                    using var stream=entry.Open(); using var buffer=new MemoryStream(); stream.CopyTo(buffer); buffer.Position=0;
                    var decoded=Image.Load(buffer);
                    bool special=entry.Name is "weapon_c4_digits.webp" or "env_specular_rgbm.webp" or "muzzle_fire.webp" or "muzzle_smoke.webp";
                    Check("engine-image/"+entry.Name, decoded.Width>0&&decoded.Height>0&&(special||Math.Max(decoded.Width,decoded.Height)<=512),$"{decoded.Width}x{decoded.Height}; real Engine.Media.Image decoder, no GPU");
                    decoded.Dispose();
                }catch(Exception e){Check("engine-image/"+entry.Name,false,e.ToString());}
            }
        }
        var ui=mod.GetType("Game.ScUiSettings");var policy=mod.GetType("Game.ScResourcePolicy");
        string savedEdition=(string)policy.GetProperty("Edition").GetValue(null);
        bool savedSimple=(bool)ui.GetField("SimpleMaterials").GetValue(null);
        try {
            var configure=policy.GetMethod("ConfigureEdition",BindingFlags.Static|BindingFlags.NonPublic);
            foreach(string edition in new[]{"Full","Optimized512"}) {
                configure.Invoke(null,[edition]);ui.GetMethod("ResetAll").Invoke(null,null);
                Check("default/"+edition,(bool)ui.GetField("SimpleMaterials").GetValue(null)==(edition=="Optimized512"));
            }
            var file=ui.GetNestedType("File",BindingFlags.NonPublic);
            var legacy=System.Text.Json.JsonSerializer.Deserialize("{\"Version\":1}",file);
            Check("legacy-settings-defer-to-edition",file.GetProperty("SimpleMaterials").GetValue(legacy) is null);
            foreach(bool simple in new[]{true,false}) {
                object value=System.Text.Json.JsonSerializer.Deserialize("{\"Version\":1,\"SimpleMaterials\":"+simple.ToString().ToLowerInvariant()+"}",file);
                for(int i=0;i<2;i++) value=System.Text.Json.JsonSerializer.Deserialize(System.Text.Json.JsonSerializer.Serialize(value,file),file);
                Check("settings-roundtrip/"+simple,(bool)file.GetProperty("SimpleMaterials").GetValue(value)==simple);
            }
        }finally {
            policy.GetMethod("ConfigureEdition",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,[savedEdition]);
            ui.GetField("SimpleMaterials").SetValue(null,savedSimple);
        }
        return results;
    }
}
