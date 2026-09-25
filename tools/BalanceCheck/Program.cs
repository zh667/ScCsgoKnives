using System.Text.Json;
using Game;

if(args.Length==3&&args[0]=="--ui")return BalanceUiCheck.Run(args[1],args[2]);
Engine.Dispatcher.Initialize();
SourceObj.Install(typeof(GunSpec).Assembly);
var checks = new List<Result>();
void Check(string name, bool ok, string detail) => checks.Add(new(name, ok, detail));
try {
    using var full=JsonDocument.Parse(Cs2SelfTest.RunJson());
    foreach(var c in full.RootElement.GetProperty("checks").EnumerateArray())Check(c.GetProperty("name").GetString(),c.GetProperty("ok").GetBoolean(),c.GetProperty("detail").GetString());
}catch(Exception e){Check("Cs2SelfTest/exception",false,e.ToString());}
if (args.Length >= 2) MigrationCheck.Run(args[1], Check);
BalanceRegression.Run(Check);
LoadIntegrityCheck.Run(Check);
string output = args.Length > 0 ? args[0] : "balance-check.json";
var report = new { coreSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(GunSpec).Assembly.Location))).ToLowerInvariant(), count=checks.Count, passed=checks.Count(c=>c.Ok), failed=checks.Count(c=>!c.Ok), checks };
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented=true }));
Console.WriteLine($"BalanceCheck: {report.passed}/{report.count}; failures={report.failed}");
foreach(var c in checks.Where(c=>!c.Ok)) Console.WriteLine($"FAIL {c.Name}: {c.Detail}");
return report.failed == 0 ? 0 : 1;
record Result(string Name, bool Ok, string Detail);
