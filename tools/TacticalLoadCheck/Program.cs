using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Xml.Linq;
using Engine;
using Engine.Serialization;
using Game;
using TemplatesDatabase;

// Exercise the native archive scanner, loader registration and database type resolution.
// Each mode runs in its own process, without compile-time references to any tested mod.
if (args.Length != 5) throw new ArgumentException("TacticalLoadCheck <repo> <Content.zip> <tactical.scmod> <mode> <report>");
string root=Path.GetFullPath(args[0]), mode=args[3];
Dispatcher.Initialize();
AssemblyLoadContext.Default.Resolving += (context,name) => {
    string path=Path.Combine(AppContext.BaseDirectory,name.Name+".dll");
    return File.Exists(path)?context.LoadFromAssemblyPath(path):null;
};
AppDomain.CurrentDomain.AssemblyResolve += (_,a) => ModsManager.Dlls.GetValueOrDefault(a.Name);
var checks=new List<object>();
void Check(string name,bool ok){checks.Add(new{name,ok});if(!ok)throw new Exception(name);}
ModEntity Package(string path){var mod=new HeadlessMod{ModArchive=Game.ZipArchive.Open(File.OpenRead(path))};mod.InitResources();return mod;}
string Output(string name)=>Path.Combine(root,"output",name);
using var content=System.IO.Compression.ZipFile.OpenRead(args[1]);
int failed=0;
try {
    string coreVersion=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"src/ScCsgoKnives/modinfo.json"))).RootElement.GetProperty("Version").GetString();
    var core=Package(Output($"[API1.9]CS武器{coreVersion}-作者ZH667-全量版.scmod"));
    var tactical=Package(args[2]);
    var mods=new List<ModEntity>{core,tactical};
    bool complete=mode is "both" or "reversed" or "legacy" or "reload-disabled";
    bool nmmPresent=complete || mode is "nmm-only" or "disabled" or "outdated";
    bool neoPresent=complete || mode is "neo-only" or "disabled" or "outdated";
    ModEntity nmm=null,neo=null,legacy=null;
    if(nmmPresent){nmm=Package(Output("[API1.9]NekoMekoModel1.1-源码构建.scmod"));mods.Add(nmm);}
    if(neoPresent){
        string source=Path.Combine(root,".tmp/creature-audit-20260917/10");
        neo=new ModEntity{modInfo=ModsManager.DeserializeJson(File.ReadAllText(Path.Combine(source,"modinfo.json")))};
        var assembly=Assembly.Load(File.ReadAllBytes(Path.Combine(source,"neorxna.dll")));
        ModsManager.Dlls[assembly.FullName]=assembly;mods.Add(neo);
    }
    if(mode=="disabled")nmm.IsDisabled=true;
    if(mode=="outdated")neo.modInfo.Version="1.3";
    if(mode=="legacy"){
        legacy=Package(Path.Combine(root,"tools/fixtures/appearance-1.1.0.scmod"));mods.Add(legacy);
    }
    foreach(var mod in mods.Where(m=>!m.IsDisabled))ModsManager.ModList.Add(mod);
    var assemblies=new Dictionary<ModEntity,Assembly[]>();
    foreach(var mod in mods.Where(m=>!m.IsDisabled)){
        assemblies[mod]=mod.GetAssemblies();
        foreach(var a in assemblies[mod])ModsManager.Dlls[a.FullName]=a;
    }
    Check("native scanner sees only tactical root DLL",assemblies[tactical].Length==1&&assemblies[tactical][0].GetName().Name=="ScCsgoTactical");
    Check("tactical has no framework references",assemblies[tactical][0].GetReferencedAssemblies().All(a=>a.Name is not ("sc-nekomekomodel" or "neorxna" or "ScCsgoAppearance")));
    Check("native dependency declaration only requires core",tactical.modInfo.DependencyRanges.Count==1&&tactical.modInfo.DependencyRanges.ContainsKey(core.modInfo.PackageName));
    foreach(var mod in mods.Where(m=>!m.IsDisabled))mod.CombineContent();
    if(complete&&mode!="reversed")foreach(var a in assemblies[nmm])nmm.HandleAssembly(a);
    tactical.HandleAssembly(assemblies[tactical][0]);
    if(complete&&mode=="reversed")foreach(var a in assemblies[nmm])nmm.HandleAssembly(a);
    if(legacy!=null)foreach(var a in assemblies[legacy])legacy.HandleAssembly(a);
    var appearance=ModsManager.ModLoaders.Where(l=>l.GetType().FullName=="Game.AppearanceModLoader").ToArray();
    Check("appearance loader count",appearance.Length==(complete?1:0));
    Check("tactical gameplay loader survives",tactical.Loaders.Count(l=>l.GetType().Name=="TacticalModLoader")==1&&tactical.BlockTypes.Count>=4);
    var initialize=assemblies[tactical][0].GetType("Game.TacticalAppearanceIntegration").GetMethod("Initialize");
    initialize.Invoke(null,[tactical]);
    Check("initialization does not duplicate hooks",ModsManager.ModLoaders.Count(l=>l.GetType().FullName=="Game.AppearanceModLoader")==appearance.Length);
    if(complete){
        Check("appearance belongs to correct package",ReferenceEquals(appearance[0].Entity,legacy??tactical));
        Check("preview registered exactly once",ModsManager.m_tempModHooks["OnPlayerModelWidgetMeasureOverride"].UnorderedItems.Count(i=>i.Element.GetType().FullName=="Game.AppearanceModLoader")==1);
        var keys=ContentManager.List("NekoMekoRes/NekoResModel").Select(c=>{
            using var stream=c.Duplicate();using var json=JsonDocument.Parse(stream);return json.RootElement.GetProperty("Key").GetString();
        }).Where(k=>k.StartsWith("zh667.cs.")).ToArray();
        Check("merged resources keep unique saved model keys",keys.Order().SequenceEqual(new[]{"zh667.cs.ct","zh667.cs.t"}));
        using var stream=content.Entries.Single(e=>e.FullName.EndsWith("Database.xml")).Open();
        var db=XElement.Load(stream);
        foreach(var mod in new[]{core,nmm,tactical,legacy}.Where(m=>m!=null))mod.LoadXdb(ref db);
        DatabaseManager.LoadDataBaseFromXml(db);
        var player=DatabaseManager.FindEntityValuesDictionary("Player",true);
        foreach(var pair in new[]{("NekoMekoModel","Game.ComponentCsPlayerAppearance"),("NekoHUD","Game.ComponentCsAppearanceHud")}){
            string className=player.GetValue<ValuesDictionary>(pair.Item1).GetValue<string>("Class");
            Check("native database replacement "+pair.Item1,className==pair.Item2);
            Check("native type cache resolves "+pair.Item1,TypeCache.FindType(className,true,true).Assembly==appearance[0].GetType().Assembly);
        }
        if(mode=="reload-disabled"){
            nmm.IsDisabled=true;ModsManager.ModList.Remove(nmm);
            var next=Package(args[2]);next.HandleAssembly(assemblies[tactical][0]);
            Check("stale assemblies do not enable disabled integration",next.Loaders.All(l=>l.GetType().FullName!="Game.AppearanceModLoader"));
        }
    }else Check("optional payload not loaded",ModsManager.Dlls.Values.All(a=>a.GetName().Name!="ScCsgoAppearance"));
}catch(Exception e){failed=1;checks.Add(new{error=e.ToString()});Console.Error.WriteLine(e);}
File.WriteAllText(args[4],JsonSerializer.Serialize(new{mode,failed,checks},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"{mode}: {checks.Count} checks, {failed} failures");return failed;

sealed class HeadlessMod:ModEntity {
    // Icon upload requires a GPU; every archive/assembly/database operation remains native.
    public override void LoadIcon(Stream stream) { }
}
