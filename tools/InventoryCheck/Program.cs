using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

// Source-DLL regression runner: deliberately neither packages nor installs anything.
if (args.Length != 3) { Console.Error.WriteLine("InventoryCheck <core.dll> <Mods directory> <report.json>"); return 2; }
Engine.Dispatcher.Initialize();
string dll = Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving += (context, name) => {
    string dependency = Path.Combine(Path.GetDirectoryName(dll), name.Name + ".dll");
    return File.Exists(dependency) ? context.LoadFromAssemblyPath(dependency) : null;
};
var mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
SourceObj.Install(mod);
var cases = SushiInventoryRegression.Run(mod, Path.GetFullPath(args[1]));
foreach (string name in new[] { "ScGunRecoverySelfTest", "SurvivalSelfTest", "ScCreativeCountersSelfTest", "ScCreativeSkinsSelfTest", "ScGunGrowthSelfTest", "ScGunSkinGrowthSelfTest", "ScGunSaveGuardSelfTest", "ScGun0282MigrationSelfTest" }) {
    try {
        Action<string, bool, string> check = (n, ok, detail) => cases.Add(new(n, ok, detail));
        mod.GetType("Game." + name, true).GetMethod("Run").Invoke(null, [check]);
    } catch (Exception e) { cases.Add(new(name + "/exception", false, e.GetBaseException().ToString())); }
}
var report = new { dll, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))), count = cases.Count, passed = cases.Count(c => c.Ok), failed = cases.Count(c => !c.Ok), cases };
File.WriteAllText(Path.GetFullPath(args[2]), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"InventoryCheck: {report.passed}/{report.count}, failures={report.failed}");
foreach (var c in cases.Where(c => !c.Ok)) Console.WriteLine($"FAIL {c.Name}: {c.Detail}");
return report.failed == 0 ? 0 : 1;
