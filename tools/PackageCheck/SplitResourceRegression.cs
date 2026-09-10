using System.IO.Compression;
using ZipArchive = System.IO.Compression.ZipArchive;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Engine;
using Game;

static class SplitResourceRegression {
    internal record Result(string Name,bool Ok,string Detail);
    internal static List<Result> Run(Assembly mod,string corePath,string resourcePath,string baseline,string sushiMod) {
        List<Result> results=[];
        void Check(string n,bool ok,string detail="")=>results.Add(new("split-resources/"+n,ok,detail));
        string Read(ZipArchive zip,string name){using var r=new StreamReader(zip.GetEntry(name).Open());return r.ReadToEnd();}
        try {
            if(sushiMod is not null) {
                using var original=File.OpenRead(sushiMod);using var decoded=ModsManager.GetDecipherStream(original);
                using var sushi=new ZipArchive(decoded,ZipArchiveMode.Read);using var labels=JsonDocument.Parse(Read(sushi,"Assets/Lang/zh-CN.json"));
                var expected=labels.RootElement.GetProperty("SushiButtonConfigWidget");
                var bindings=mod.GetType("Game.ScGunBindings");
                // 0.41.12 uses our own Chinese captions. Optional reference audit covers IDs only;
                // no longer require or claim that every third-party display caption matches.
                foreach(var key in Enum.GetValues<Engine.Input.Key>())
                    Check("reference-key-id/"+key,expected.TryGetProperty(key.ToString(),out _));
                var old=SettingsManager.KeyboardMappingSettings;
                try {
                    SettingsManager.InitializeKeyboardMappingSettings();
                    foreach(var key in Enum.GetValues<Engine.Input.MouseButton>()) {
                        SettingsManager.KeyboardMappingSettings.SetValue("Aim",key);
                        Check("sushi-exact-mouse/"+key,(string)bindings.GetMethod("NativeBinding").Invoke(null,["scope"])==expected.GetProperty(key.ToString()).GetString());
                    }
                }finally{SettingsManager.KeyboardMappingSettings=old;}
            }
            if(resourcePath is null)return results;
            using var core=ZipFile.OpenRead(corePath);using var resources=ZipFile.OpenRead(resourcePath);
            var coreInfo=ModsManager.DeserializeJson(Read(core,"modinfo.json"));var resourceInfo=ModsManager.DeserializeJson(Read(resources,"modinfo.json"));
            Check("native-dependency-parser",coreInfo.PackageName=="zh667.ScCsgoKnives"&&coreInfo.DependencyRanges.TryGetValue(resourceInfo.PackageName,out var range)&&range.Satisfies(resourceInfo.NuGetVersion));
            var savedAll=ModsManager.ModListAll;var savedList=ModsManager.ModList;var savedMap=ModsManager.PackageNameToModEntity;
            try {
                ModsManager.ModListAll=[];ModsManager.ModList=[];ModsManager.PackageNameToModEntity=[];
                var missing=new ModEntity{modInfo=coreInfo};missing.CheckDependencies();Check("native-missing-dependency-disabled",missing.IsDisabled);
                var provider=new ModEntity{modInfo=resourceInfo};var consumer=new ModEntity{modInfo=coreInfo};
                ModsManager.ModListAll=[consumer,provider];ModsManager.ModList=[];ModsManager.PackageNameToModEntity=[];
                consumer.CheckDependencies();Check("native-resource-loaded-before-core",!consumer.IsDisabled&&ModsManager.ModList.IndexOf(provider)<ModsManager.ModList.IndexOf(consumer));
            }finally{ModsManager.ModListAll=savedAll;ModsManager.ModList=savedList;ModsManager.PackageNameToModEntity=savedMap;}
            var assets=ResourcePackInput.Animations(mod);
            Check("resource-assembly-has-no-gameplay",assets.GetTypes().All(t=>!t.IsSubclassOf(typeof(ModLoader))&&!t.IsSubclassOf(typeof(Block))));
            Check("large-curves-only-in-resource-assembly",assets.GetManifestResourceNames().Count(n=>n.EndsWith(".cs2.animation.json"))==63
                &&!mod.GetManifestResourceNames().Any(n=>n.EndsWith(".cs2.animation.json")||n.EndsWith(".skin")||n.EndsWith(".parts")));
            var cache=(IDictionary<string,List<object>>)typeof(ContentManager).GetField("Caches",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
            cache.TryGetValue("ScCsgoResources",out var oldMarker);
            try {
                cache["ScCsgoResources"]=[XElement.Parse(Read(resources,"Assets/ScCsgoResources.xml"))];
                mod.GetType("Game.ScRequiredResources").GetMethod("Validate").Invoke(null,null);Check("runtime-marker-accepted",true);
                cache["ScCsgoResources"]=[new XElement("Resources",new XAttribute("Version","99"))];
                bool refused=false;try{mod.GetType("Game.ScRequiredResources").GetMethod("Validate").Invoke(null,null);}catch(TargetInvocationException){refused=true;}
                Check("runtime-wrong-marker-refused",refused);
                var source=new XElement("Project",new XElement("Subsystems",new XElement("Values",new XAttribute("Name","ScGunBlockBehavior"))));
                var loader=Activator.CreateInstance(mod.GetType("Game.ScCsgoKnivesModLoader"));
                mod.GetType("Game.ScCsgoKnivesModLoader").GetMethod("ProjectXmlLoad",[typeof(XElement),typeof(WorldInfo),typeof(ContainerWidget)]).Invoke(loader,[source,null,null]);
                Check("wrong-resource-marks-load-refused",source.Descendants("Value").Any(v=>(string)v.Attribute("Name")=="GunFormatLoadError"));
            }finally{if(oldMarker is null)cache.Remove("ScCsgoResources");else cache["ScCsgoResources"]=oldMarker;}
            if(baseline is not null) {
                using var old=ZipFile.OpenRead(baseline);
                foreach(var oldEntry in old.Entries.Where(e=>e.FullName.StartsWith("Assets/Textures/")||e.FullName.StartsWith("Assets/Audio/")||e.FullName.StartsWith("Assets/Models/"))) {
                    var entry=resources.GetEntry(oldEntry.FullName)??throw new InvalidDataException("Missing prior resource "+oldEntry.FullName);
                    using var now=entry.Open();using var before=oldEntry.Open();
                    Check("asset-byte-preserved/"+entry.FullName,SHA256.HashData(now).AsSpan().SequenceEqual(SHA256.HashData(before)));
                }
                using var oldDll=old.GetEntry("ScCsgoKnives.dll").Open();using var bytes=new MemoryStream();oldDll.CopyTo(bytes);bytes.Position=0;
                var original=new PackageContext("resource-monolithic-baseline").LoadFromStream(bytes);
                foreach(var n in assets.GetManifestResourceNames()) {
                    using var before=original.GetManifestResourceStream(n);using var after=assets.GetManifestResourceStream(n);
                    Check("embedded-byte-preserved/"+n,before is not null&&SHA256.HashData(before).AsSpan().SequenceEqual(SHA256.HashData(after)));
                }
            }
            var skins=(Array)mod.GetType("Game.ScGunSkinCatalog").GetField("All").GetValue(null);
            var specType=mod.GetType("Game.GunSpec");var specs=(Array)specType.GetField("All").GetValue(null);
            var renderer=mod.GetType("Game.KnifePbrRenderer");
            foreach(var skin in skins)if((string)skin.GetType().GetProperty("Gun").GetValue(skin)=="m4a1s") {
                var material=(string)skin.GetType().GetProperty("Material").GetValue(skin);
                int index=Array.FindIndex(specs.Cast<object>().ToArray(),s=>(string)specType.GetField("Name").GetValue(s)=="m4a1s");
                int variant=(int)mod.GetType("Game.ScGunBlock").GetMethod("AssetIndex").Invoke(null,[index]);
                foreach(float light in new[]{0f,.05f,.15f,.5f,.8f,1f}) {
                    float Factor(string m)=>(float)renderer.GetMethod("SceneEnvFactor").Invoke(null,[variant,m,light]);
                    Check($"m4-scene-consistent/{material}/{light}",Factor(material)==Factor("m4a1s_hd"));
                }
            }
        }catch(Exception e){Check("failure",false,e.ToString());}
        return results;
    }
}
