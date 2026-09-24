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
if(args.Length==4&&args[0]=="--archive-identical"){
    // Verify every recompressed member using the game's own ZIP implementation,
    // against the previously accepted package, not merely a second desktop unzip.
    using var original=System.IO.Compression.ZipFile.OpenRead(args[1]);
    using var native=Game.ZipArchive.Open(File.OpenRead(args[2]));
    var files=native.ReadCentralDir();
    if(!files.Select(f=>f.FilenameInZip).Order().SequenceEqual(original.Entries.Select(e=>e.FullName).Order()))
        throw new Exception("Archive member names differ");
    foreach(var file in files){
        using var data=new MemoryStream();native.ExtractFile(file,data);
        using var reference=original.GetEntry(file.FilenameInZip).Open();
        data.Position=0;
        if(!System.Security.Cryptography.SHA256.HashData(data).SequenceEqual(System.Security.Cryptography.SHA256.HashData(reference)))
            throw new Exception("Native extracted bytes differ: "+file.FilenameInZip);
    }
    File.WriteAllText(args[3],JsonSerializer.Serialize(new{count=files.Count,failed=0,nativeReader="Game.ZipArchive"}));
    Console.WriteLine($"Native ZIP: {files.Count} identical members");return 0;
}
if (args.Length is not (5 or 6)) throw new ArgumentException("TacticalLoadCheck <repo> <Content.zip> <tactical.scmod> <mode> <report> [core.scmod]");
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
    var core=Package(args.Length==6?args[5]:Output($"[API1.9]CS武器{coreVersion}-作者ZH667-全量版.scmod"));
    bool merged=core.ModFiles.ContainsKey("ScCsgoBundle.dll");
    var tactical=merged?core:Package(args[2]);
    var mods=merged?new List<ModEntity>{core}:new List<ModEntity>{core,tactical};
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
    var tacticalAssembly=assemblies[tactical].Single(a=>a.GetName().Name=="ScCsgoTactical");
    Check("native scanner sees expected root DLLs",merged?
        assemblies[core].Select(a=>a.GetName().Name).Order().SequenceEqual(new[]{"ScCsgoBundle","ScCsgoKnives","ScCsgoResources","ScCsgoTactical"}):assemblies[tactical].Length==1);
    Check("tactical has no framework references",tacticalAssembly.GetReferencedAssemblies().All(a=>a.Name is not ("sc-nekomekomodel" or "neorxna" or "ScCsgoAppearance")));
    Check("native dependency declaration",merged?core.modInfo.DependencyRanges.Count==0:tactical.modInfo.DependencyRanges.Count==1&&tactical.modInfo.DependencyRanges.ContainsKey(core.modInfo.PackageName));
    foreach(var mod in mods.Where(m=>!m.IsDisabled))mod.CombineContent();
    if(complete&&mode!="reversed")foreach(var a in assemblies[nmm])nmm.HandleAssembly(a);
    if(merged)foreach(var a in assemblies[core])core.HandleAssembly(a);
    else tactical.HandleAssembly(tacticalAssembly);
    if(complete&&mode=="reversed")foreach(var a in assemblies[nmm])nmm.HandleAssembly(a);
    if(legacy!=null)foreach(var a in assemblies[legacy])legacy.HandleAssembly(a);
    var appearance=ModsManager.ModLoaders.Where(l=>l.GetType().FullName=="Game.AppearanceModLoader").ToArray();
    Check("appearance loader count",appearance.Length==(complete?1:0));
    Check("tactical gameplay loader survives",tactical.Loaders.Count(l=>l.GetType().Name=="TacticalModLoader")==1&&tactical.BlockTypes.Count>=4);
    if(merged){
        var loader=core.Loaders.Single(l=>l.GetType().Name=="BundleModLoader");
        Check("alias absent until native assembly loop completes",!ModsManager.GetModEntity("zh667.ScCsgoTactical",out _));
        var actions=new List<Action>();loader.OnLoadingFinished(actions);foreach(var action in actions)action();foreach(var action in actions)action();
        Check("core remains primary loader",core.Loader.GetType().Name=="ScCsgoKnivesModLoader");
        Check("legacy tactical identity resolves once",ModsManager.GetModEntity("zh667.ScCsgoTactical",out var identity)&&ModsManager.ModList.Count(m=>m.modInfo.PackageName=="zh667.ScCsgoTactical")==1);
        Check("identity never duplicates gameplay or resources",identity.ModArchive==null&&identity.Loaders.Count==0&&identity.BlockTypes.Count==0&&identity.ModFiles.Count==0);
        var used=new SubsystemUsedMods();var saved=new ValuesDictionary();used.Save(saved);
        for(int round=0;round<2;round++){
            // In deliberately incomplete framework modes, the host has scanned an
            // NMM DLL whose dependency is absent (the real engine disables it).
            // Do not make the global converter scan this invalid fixture assembly.
            bool xmlRoundtrip=complete||mode=="none";
            if(xmlRoundtrip){var xml=new XElement("Values");saved.Save(xml);saved=new ValuesDictionary();saved.ApplyOverrides(XElement.Parse(xml.ToString()));}
            else used.Save(saved);
            var entries=saved.GetValue<ValuesDictionary>("Mods");var identities=Enumerable.Range(0,saved.GetValue<int>("ModsCount")).Select(i=>entries.GetValue<ValuesDictionary>(i.ToString())).ToArray();
            Check("native UsedMods "+(xmlRoundtrip?"XML roundtrip ":"identity save ")+round,identities.Count(v=>v.GetValue<string>("PackageName")=="zh667.ScCsgoKnives")==1&&identities.Count(v=>v.GetValue<string>("PackageName")=="zh667.ScCsgoTactical"&&v.GetValue<string>("Version")=="1.4.0")==1);
        }
        ModsManager.ModList.Remove(identity);var duplicate=new ModEntity{modInfo=identity.modInfo};ModsManager.ModList.Add(duplicate);
        bool refused=false;try{loader.__ModInitialize();}catch(InvalidOperationException){refused=true;}finally{ModsManager.ModList.Remove(duplicate);ModsManager.ModList.Add(identity);}
        Check("duplicate tactical identity refuses loading",refused);
    }
    var initialize=tacticalAssembly.GetType("Game.TacticalAppearanceIntegration").GetMethod("Initialize");
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
        // Match native default load ordering; the merged core now also owns appearance.
        foreach(var mod in new[]{core,nmm,tactical,legacy}.Where(m=>m!=null).Distinct().OrderBy(m=>m.modInfo.LoadOrder))mod.LoadXdb(ref db);
        DatabaseManager.LoadDataBaseFromXml(db);
        var player=DatabaseManager.FindEntityValuesDictionary("Player",true);
        foreach(var pair in new[]{("NekoMekoModel","Game.ComponentCsPlayerAppearance"),("NekoHUD","Game.ComponentCsAppearanceHud")}){
            string className=player.GetValue<ValuesDictionary>(pair.Item1).GetValue<string>("Class");
            Check("native database replacement "+pair.Item1,className==pair.Item2);
            Check("native type cache resolves "+pair.Item1,TypeCache.FindType(className,true,true).Assembly==appearance[0].GetType().Assembly);
        }
        if(mode=="reload-disabled"){
            nmm.IsDisabled=true;ModsManager.ModList.Remove(nmm);
            var next=Package(args[2]);next.HandleAssembly(tacticalAssembly);
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
