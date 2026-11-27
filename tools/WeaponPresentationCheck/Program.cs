using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Engine;
using Game;

// Issue #9: category identity and localized item names. Rendering checks (counter parents, materials) are not part
// of this runner; they live with the separate third-person work.
if(args.Length!=3)throw new ArgumentException("WeaponPresentationCheck <repo> <report directory> <Full.scmod>");
string root=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
var checks=new List<string>();var errors=new List<string>();
void Check(string name,bool pass){if(!pass)throw new Exception(name);checks.Add(name);}
Dispatcher.Initialize();
using var package=ZipFile.OpenRead(args[2]);
bool done=false;
Window.Frame+=()=>{if(done)return;done=true;try{
    foreach(string locale in new[]{"zh-CN","en-US"}){
        using var source=package.GetEntry("Assets/Lang/"+locale+".json").Open();using var copy=new MemoryStream();source.CopyTo(copy);
        Check("package/locale/"+locale,copy.ToArray().SequenceEqual(File.ReadAllBytes(Path.Combine(root,"src/ScCsgoKnives/Assets/Lang/"+locale+".json"))));
    }
    {
        using var source=package.GetEntry("Assets/ScCsgoKnivesBlocksData.csv").Open();using var reader=new StreamReader(source);
        var rows=reader.ReadToEnd().Split('\n').Skip(1).Where(l=>l.Trim().Length>0).Select(l=>l.Split(';')).ToArray();
        Check("package/csv-category",rows.Length>0&&rows.All(r=>r[2]==ScWeaponText.Category));
    }
    LocaleChecks.Run(root,Check);
    ScCreativeSkinsSelfTest.Run((name,ok,detail)=>{if(ok)checks.Add(name);else errors.Add(name+": "+detail);});
    ScCreativeCountersSelfTest.Run((name,ok,detail)=>{if(ok)checks.Add(name);else errors.Add(name+": "+detail);});
}catch(Exception e){errors.Add(e.ToString());Console.Error.WriteLine(e);}finally{Window.Close();}};
Window.Run(320,240,WindowMode.Fixed,"Weapon localization regression");
string Sha(string path)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new{
    engine=typeof(BlocksManager).Assembly.FullName,engineApiVersion=ModsManager.APINuGetVersion.ToString(),engineSha256=Sha(typeof(BlocksManager).Assembly.Location),
    coreSha256=Sha(typeof(ScGunBlock).Assembly.Location),tacticalSha256=Sha(typeof(ScTacticalShieldBlock).Assembly.Location),packageSha256=Sha(args[2]),
    runnerSha256=Sha(Assembly.GetExecutingAssembly().Location),
    checks,errors},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"{checks.Count} passed; {errors.Count} failed");foreach(var e in errors)Console.WriteLine("FAIL "+e);return errors.Count==0?0:1;
