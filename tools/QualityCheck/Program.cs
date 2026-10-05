using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Game;

if (args.Length is < 1 or > 2 || args.Length == 2 && args[1] is not ("--workbench-only" or "--inventory-only")) { Console.Error.WriteLine("QualityCheck <report.json> [--workbench-only|--inventory-only]"); return 2; }
Engine.Dispatcher.Initialize();
var cases = new List<CheckResult>();
void Test(string name, Action action) {
    try { action(); cases.Add(new(name, true, "")); }
    catch (Exception e) { cases.Add(new(name, false, e.GetBaseException().ToString())); }
}
bool focused = args.Length == 2;
var notExecuted = new List<string> { "real game/provider callbacks", "historical player snapshot (not requested)" };
if (!focused) {
TravelChecks.Run(Test);
ArrivalRecoveryChecks.Run(Test);
LifecycleChecks.Run(Test);
foreach (var c in ItemTravelRegression.Run(typeof(ScNet).Assembly)) {
    if (c.Detail.StartsWith("NOT RUN:")) { notExecuted.Add(c.Name + ": " + c.Detail); continue; }
    Test(c.Name, () => TravelChecks.Require(c.Ok, c.Detail));
}
foreach (var c in GenericTravelRegression.Run(typeof(ScNet).Assembly, null)) Test(c.Name, () => TravelChecks.Require(c.Ok, c.Detail));
}
if (!focused || args[1] == "--inventory-only") InventoryBoundaryChecks.Run(Test);
if (!focused || args[1] == "--workbench-only") WorkbenchChecks.Run(Test);
var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic &&
    (a.GetName().Name.StartsWith("ScCsgo") || a.GetName().Name is "Survivalcraft" or "Engine" or "GameEntitySystem" or "TemplatesDatabase"))
    .Select(a => new { name = a.GetName().Name, version = a.GetName().Version.ToString(), path = a.Location,
        sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a.Location))) }).ToArray();
var report = new { scope = "API 1.9.3.1 offline production calls with fault-injected inventories; no worlds or installs", assemblies,
    count = cases.Count, passed = cases.Count(c => c.Ok), failed = cases.Count(c => !c.Ok), cases, notExecuted, trace = WorkbenchChecks.Trace, inventoryTrace = InventoryBoundaryChecks.Trace };
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));
File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"QualityCheck: {report.passed}/{report.count}; failures={report.failed}");
foreach (var c in cases.Where(c => !c.Ok)) Console.WriteLine($"FAIL {c.Name}: {c.Detail}");
return report.failed == 0 ? 0 : 1;

record CheckResult(string Name, bool Ok, string Detail);
