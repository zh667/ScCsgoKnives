using System.Security.Cryptography;
using System.Text.Json;
using Engine;
using Game;

Dispatcher.Initialize();
var results=SushiInventoryRegression.Run(typeof(ScGunRegistry).Assembly,args[1]);
var checks=results.Select(r=>new{name=r.Name,ok=r.Ok,detail=r.Detail}).ToArray();
int failed=checks.Count(c=>!c.ok);
File.WriteAllText(args[2],JsonSerializer.Serialize(new{failed,packageSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0]))).ToLowerInvariant(),checks},new JsonSerializerOptions{WriteIndented=true}));
foreach(var c in checks.Where(c=>!c.ok))Console.WriteLine(c.name+": "+c.detail);
Console.WriteLine($"Sushi native checks {checks.Length}, failed {failed}");return failed==0?0:1;
