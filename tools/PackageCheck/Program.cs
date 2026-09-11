// PackageCheck --scmod <path> [--sha256 <hex>] [--json <out>]
//
//   Runs the CS2 acceptance against the DLL *inside* a packaged .scmod, which is the
//   artifact that reaches the device, instead of against a working-tree rebuild.
//
//   0.16.5 was accepted on a working-tree DLL whose SHA-256
//   (72f814bf...) did not match the one in the package that shipped
//   (02b70383...). Nothing in that run had opened the package. This host verifies the
//   package hash first, extracts ScCsgoKnives.dll from it, loads that assembly in its
//   own context and calls Game.Cs2SelfTest.RunJson by reflection, so the assertions
//   are the mod's own code running out of the shipped bytes. It then reads the sound
//   table from that same assembly's embedded resources and checks every cue against
//   the OGGs in the same zip - the table and the audio have to come from one package
//   or the coverage number means nothing.
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

// A local-engine compatibility probe may carry additional engine dependencies that are not in
// this host's NuGet deps.json (e.g. OpenXR). Resolve only DLLs already alongside this executable.
AssemblyLoadContext.Default.Resolving += (context, name) => {
    string path = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
    return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
};

static string Sha256(string path) {
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

string scmod = null, expected = null, jsonOut = null, vanillaContent = null, framesOut = null, polishOut = null, resourceAudit = null, thirdPersonOut = null, weaponStats = null, stattrakOut = null;
string published0282 = null, snapshot0282 = null;
string enchantmentAssembly = null;
string previousGrowthPackage = null, ghoulAssembly = null;
string travelSourceSnapshot = null;
string resourcePack = null;
string resourceBaseline = null, sushiPackage = null;
bool attributeBenchmark = false;
for (int i = 0; i < args.Length; i++) {
    switch (args[i]) {
        case "--resource-baseline": resourceBaseline = args[++i]; break;
        case "--sushi-mod": sushiPackage = args[++i]; break;
        case "--resource-pack": resourcePack = args[++i]; break;
        case "--travel-source-snapshot": travelSourceSnapshot = args[++i]; break;
        case "--previous-growth-package": previousGrowthPackage = args[++i]; break;
        case "--ghoul-assembly": ghoulAssembly = args[++i]; break;
        case "--attribute-benchmark": attributeBenchmark = true; break;
        case "--enchantment-assembly": enchantmentAssembly = args[++i]; break;
        case "--published-0282": published0282 = args[++i]; break;
        case "--snapshot-0282": snapshot0282 = args[++i]; break;
        case "--resource-audit": resourceAudit = args[++i]; break;
        case "--weapon-stats": weaponStats = args[++i]; break;
        case "--stattrak-out": stattrakOut = args[++i]; break;
        case "--scmod": scmod = args[++i]; break;
        case "--sha256": expected = args[++i]; break;
        case "--json": jsonOut = args[++i]; break;
        case "--vanilla-content": vanillaContent = args[++i]; break;
        case "--frames-out": framesOut = args[++i]; break;
        case "--polish-out": polishOut = args[++i]; break;
        case "--third-person-out": thirdPersonOut = args[++i]; break;
        default: Console.Error.WriteLine($"unknown argument '{args[i]}'"); return 2;
    }
}
if (scmod is null) { Console.Error.WriteLine("usage: PackageCheck --scmod <path> [--sha256 <hex>] [--json <out>]"); return 2; }
// Headless hosts (the VPS) have no game window, so nothing ever calls Engine.Dispatcher.Initialize().
// Engine objects the regressions create through GetUninitializedObject still run GraphicsResource's
// finalizer, which posts to the Dispatcher and throws "Dispatcher is not initialized" on the finalizer
// thread, killing the process mid-run. Initializing it here makes those posts inert queue entries.
Engine.Dispatcher.Initialize();
if (!File.Exists(scmod)) { Console.Error.WriteLine($"no such package: {scmod}"); return 2; }

string digest = Sha256(scmod);
if (expected is not null && !string.Equals(digest, expected.Trim().ToLowerInvariant(), StringComparison.Ordinal)) {
    Console.Error.WriteLine($"package sha256 mismatch\n  file     {digest}\n  expected {expected.Trim().ToLowerInvariant()}");
    return 3;
}

using var testWorkspace = new TestWorkspace();
string deliveredCore=Path.GetFullPath(scmod);
long deliveredCoreBytes=new FileInfo(scmod).Length;
int deliveredCoreEntries;
using(var archive=ZipFile.OpenRead(scmod))deliveredCoreEntries=archive.Entries.Count;
try { scmod=ResourcePackInput.Merge(scmod,resourcePack,testWorkspace.DirectoryPath); }
catch(Exception e){Console.Error.WriteLine("Invalid resource installation: "+e.Message);return 3;}
string temp = Path.Combine(testWorkspace.DirectoryPath, "packagecheck-" + digest[..16]);
Directory.CreateDirectory(temp);
string dllPath, dllDigest;
var oggs = new List<string>();
long packageBytes = new FileInfo(scmod).Length;
int entryCount;
using (ZipArchive zip = ZipFile.OpenRead(scmod)) {
    entryCount = zip.Entries.Count;
    ZipArchiveEntry entry = zip.Entries.FirstOrDefault(e =>
        e.FullName.EndsWith("ScCsgoKnives.dll", StringComparison.OrdinalIgnoreCase));
    if (entry is null) { Console.Error.WriteLine("the package contains no ScCsgoKnives.dll"); return 3; }
    dllPath = Path.Combine(temp, "ScCsgoKnives.dll");
    entry.ExtractToFile(dllPath, overwrite: true);
    dllDigest = Sha256(dllPath);
    foreach (ZipArchiveEntry e in zip.Entries)
        if (e.FullName.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
            oggs.Add(Path.GetFileNameWithoutExtension(e.FullName));
}

var context = new PackageContext("scmod");
if(resourcePack is not null) {
    using var resources=ZipFile.OpenRead(resourcePack);
    using var input=resources.GetEntry("ScCsgoResources.dll").Open();using var bytes=new MemoryStream();input.CopyTo(bytes);bytes.Position=0;
    context.ResourceAssembly=context.LoadFromStream(bytes);
}
else {
    // A monolithic release carries the resource assembly beside the core DLL.
    using var package=ZipFile.OpenRead(scmod);
    var resource=package.Entries.FirstOrDefault(e=>e.FullName.EndsWith("ScCsgoResources.dll",StringComparison.OrdinalIgnoreCase));
    if(resource is not null) { using var input=resource.Open(); using var bytes=new MemoryStream(); input.CopyTo(bytes); bytes.Position=0; context.ResourceAssembly=context.LoadFromStream(bytes); }
}
// Do not leave Windows holding the extracted DLL open until process exit: tests can
// release their temporary files deterministically after loading the identical bytes.
Assembly mod;
using (var dllStream = File.OpenRead(dllPath)) mod = context.LoadFromStream(dllStream);
Type selfTest = mod.GetType("Game.Cs2SelfTest");
if (selfTest is null) { Console.Error.WriteLine("the packaged assembly has no Game.Cs2SelfTest"); return 3; }

// The mod logs through Engine's Log; send it to stderr so stdout stays one JSON blob.
Type knifeLog = mod.GetType("Game.KnifeLog");
knifeLog?.GetProperty("ToConsole", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, true);

if (resourceAudit is not null) { ResourceAudit.Write(mod, resourceAudit, digest); return 0; }
if (weaponStats is not null) { WeaponStatsExport.Write(mod, scmod, weaponStats, digest); return 0; }

if (attributeBenchmark) {
    Console.WriteLine(JsonSerializer.Serialize(AttributeColdRegression.Run(mod, false)));
    return 0; // Measurement mode also accepts the historical baseline with eager loads.
}
var attributeColdChecks = AttributeColdRegression.Run(mod);

ThirdPersonExport.ProvideObj(mod, scmod); // the self-test bakes the OBJ-piece guns for third person from the package's own files
// The gun state table a world would own (layout v5): partial magazines and durability live in records, not in the item value.
var registryType = mod.GetType("Game.ScGunRegistry");
void FreshRegistry() => registryType?.GetField("Current").SetValue(null, Activator.CreateInstance(registryType)); // each check set gets an empty table: 1022 records is a world's budget, not the suite's
FreshRegistry();
string runJson;
try {
    runJson = (string)selfTest.GetMethod("RunJson", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
}
catch (TargetInvocationException e) {
    Console.Error.WriteLine($"Cs2SelfTest.RunJson threw inside the packaged assembly: {e.InnerException}");
    return 4;
}
JsonNode result = JsonNode.Parse(runJson);

// The sound table has to be the one this DLL carries, not the repository's.
string table = (string)selfTest.GetMethod("SoundTableJson", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
var soundChecks = new List<object>();
int cues = 0, unnamed = 0, absent = 0;
if (table is null) {
    soundChecks.Add(new { name = "sound/table", ok = false, detail = "the packaged assembly embeds no cs2_sounds.json" });
}
else {
    var missing = new List<string>();
    JsonNode clips = JsonNode.Parse(table)?["Clips"];
    foreach (var clip in clips?.AsObject() ?? []) {
        foreach (JsonNode cue in clip.Value?["Cues"]?.AsArray() ?? []) {
            cues++;
            string asset = cue?["Asset"]?.GetValue<string>();
            if (string.IsNullOrEmpty(asset)) { unnamed++; missing.Add($"{clip.Key}: cue with no asset"); continue; }
            if (!oggs.Contains(asset, StringComparer.OrdinalIgnoreCase)) {
                absent++;
                missing.Add($"{clip.Key}: {asset}.ogg not in the package");
            }
        }
    }
    soundChecks.Add(new {
        name = "sound/coverage", ok = unnamed == 0 && absent == 0,
        detail = unnamed == 0 && absent == 0
            ? $"{cues} of {cues} cues resolve to an OGG in this package ({oggs.Count} OGGs), table read from the packaged DLL"
            : $"{unnamed} cues unnamed and {absent} naming a file the package lacks: {string.Join("; ", missing.Take(8))}",
    });
}

var checks = result["checks"].AsArray().Select(c => new {
    name = c["name"].GetValue<string>(), ok = c["ok"].GetValue<bool>(), detail = c["detail"].GetValue<string>(),
}).Cast<object>().ToList();
checks.AddRange(soundChecks);
if (vanillaContent is not null) {
    FreshRegistry();
    foreach (var c in SurvivalPackageIntegration.Run(mod,scmod,vanillaContent))
        checks.Add(new { name=c.Name,ok=c.Ok,detail=c.Detail });
    foreach (var c in HeadshotRegression.Run(mod,vanillaContent))
        checks.Add(new { name=c.Name,ok=c.Ok,detail=c.Detail });
}
FreshRegistry(); foreach(var c in CreativeRuntimeRegression.Run(mod)) checks.Add(new { name=c.Name,ok=c.Ok,detail=c.Detail });
FreshRegistry(); foreach(var c in InteractionRegression.Run(mod)) checks.Add(new { name=c.Name,ok=c.Ok,detail=c.Detail });
FreshRegistry(); foreach(var c in SwitchAnimationRegression.Run(mod)) checks.Add(new { name=c.Name,ok=c.Ok,detail=c.Detail });
FreshRegistry(); foreach(var c in MobileRegression.Run(mod)) checks.Add(new { name=c.Name,ok=c.Ok,detail=c.Detail });
FreshRegistry(); foreach(var c in StarterEquipmentRegression.Run(mod)) checks.Add(new { name=c.Name,ok=c.Ok,detail=c.Detail });
FreshRegistry(); foreach(var c in SurvivalDurabilityRegression.Run(mod,scmod)) checks.Add(new { name=c.Name,ok=c.Ok,detail=c.Detail });
FreshRegistry(); foreach(var c in AmmoHudRegression.Run(mod,scmod)) checks.Add(new { name=c.Name,ok=c.Ok,detail=c.Detail });
FreshRegistry(); foreach(var c in ResourceRegression.Run(mod,scmod)) checks.Add(new { name=c.Name,ok=c.Ok,detail=c.Detail });
FreshRegistry(); FreshRegistry(); foreach(var c in GunSkinRegression.Run(mod,scmod)) checks.Add(new { name=c.Name,ok=c.Ok,detail=c.Detail });
foreach(var c in CombatRegression.Run(mod,scmod)) checks.Add(new { name=c.Name,ok=c.Ok,detail=c.Detail });
foreach(var c in GunHandlingRegression.Run(mod)) checks.Add(new { name=c.Name,ok=c.Ok,detail=c.Detail });
foreach(var c in GunDiagnosticsRegression.Run(mod)) checks.Add(new { name=c.Name,ok=c.Ok,detail=c.Detail });
foreach(var c in attributeColdChecks) checks.Add(new { name=c.Name,ok=c.Ok,detail=c.Detail });
foreach(var c in TravelRegression.Run(mod)) checks.Add(new { name=c.Name,ok=c.Ok,detail=c.Detail });
foreach(var c in ControlsScopeRegression.Run(mod)) checks.Add(new { name=c.Name,ok=c.Ok,detail=c.Detail });
foreach(var c in Growth30BoundaryRegression.Run(mod,previousGrowthPackage,ghoulAssembly)) checks.Add(new {name=c.Name,ok=c.Ok,detail=c.Detail});
foreach(var c in LiveBackupRegression.Run(mod)) checks.Add(new {name=c.Name,ok=c.Ok,detail=c.Detail});
foreach(var c in GenericTravelRegression.Run(mod,travelSourceSnapshot)) checks.Add(new {name=c.Name,ok=c.Ok,detail=c.Detail});
foreach(var c in SplitResourceRegression.Run(mod,deliveredCore,resourcePack,resourceBaseline,sushiPackage)) checks.Add(new {name=c.Name,ok=c.Ok,detail=c.Detail});
if (vanillaContent is not null) foreach(var c in WeaponHelpLayoutRegression.Run(mod,vanillaContent)) checks.Add(new { name=c.Name,ok=c.Ok,detail=c.Detail });
if (published0282 is not null) foreach(var c in Published0282Regression.Run(mod,published0282,snapshot0282)) checks.Add(new { name=c.Name,ok=c.Ok,detail=c.Detail });
foreach(var c in UiLightingCompatibilityRegression.Run(mod,enchantmentAssembly)) checks.Add(new { name=c.Name,ok=c.Ok,detail=c.Detail });
foreach(var c in GunWorldEffectsRegression.Run(mod)) checks.Add(new { name=c.Name,ok=c.Ok,detail=c.Detail });
int failed = checks.Count(c => !(bool)c.GetType().GetProperty("ok").GetValue(c));

string output = JsonSerializer.Serialize(new {
    package = deliveredCore,
    packageSha256 = digest,
    packageBytes = deliveredCoreBytes,
    entries = deliveredCoreEntries,
    resourcePackage = resourcePack is null ? null : Path.GetFullPath(resourcePack),
    resourcePackageSha256 = resourcePack is null ? null : Sha256(resourcePack),
    effectiveInstallationEntries = entryCount,
    dllSha256 = dllDigest,
    dllFromPackage = true,
    assemblyVersion = mod.GetName().Version?.ToString(),
    oggsInPackage = oggs.Count,
    cues,
    failed,
    checks,
}, new JsonSerializerOptions { WriteIndented = false });
Console.WriteLine(output);
if (jsonOut is not null) File.WriteAllText(jsonOut, output);
if (framesOut is not null && failed==0) FrameExport.Write(mod,framesOut);
if (polishOut is not null && failed==0) PolishExport.Write(mod,polishOut);
if (thirdPersonOut is not null && vanillaContent is not null) ThirdPersonExport.Write(mod,vanillaContent,thirdPersonOut);
if (stattrakOut is not null) File.WriteAllText(stattrakOut,
    (string)mod.GetType("Game.ScStatTrakRenderer").GetMethod("PreviewJson").Invoke(null,null));
return failed == 0 ? 0 : 1;

/// <summary>Loads the mod from the package; everything else falls through to the host.</summary>
sealed class PackageContext(string name) : AssemblyLoadContext(name, isCollectible: false) {
    internal Assembly ResourceAssembly;
    protected override Assembly Load(AssemblyName assemblyName) => assemblyName.Name==ResourceAssembly?.GetName().Name ? ResourceAssembly : null;
}
