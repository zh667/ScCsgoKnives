using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Game;
using NuGet.Versioning;

// Real engine metadata parser and version-range semantics; no rendering/game or package execution.
var results=new List<object>();int failed=0;
foreach(string path in args.Skip(1)){
    using var archive=ZipFile.OpenRead(path);
    foreach(var entry in archive.Entries.Where(e=>e.FullName=="modinfo.json"||e.FullName.EndsWith(".modinfo.json"))){
        bool ok=false;string error=null;
        try{
            using var reader=new StreamReader(entry.Open());var text=reader.ReadToEnd();
            var json=JsonDocument.Parse(text).RootElement;var native=ModsManager.DeserializeJson(text);
            if(native.Version!="1.5.0"||json.GetProperty("Author").GetString()!="zh667"||json.GetProperty("ApiVersion").GetString()!="1.9.3.1")throw new Exception("Wrong version, author or API");
            string name=json.GetProperty("Name").GetString(),description=json.GetProperty("Description").GetString();
            if(name.Contains("1.4.0")||description.Contains("CS武器1.4.0")||description.Contains("CS武器1.7.1"))throw new Exception("Stale player-facing metadata");
            if(native.PackageName!="zh667.ScCsgoKnives"){
                if(!native.DependencyRanges.TryGetValue("zh667.ScCsgoKnives",out var range)||!range.Satisfies(NuGetVersion.Parse("1.5.0"))||range.Satisfies(NuGetVersion.Parse("1.4.0"))||range.Satisfies(NuGetVersion.Parse("1.4.9")))throw new Exception("Wrong native core dependency");
            }else if(native.DependencyRanges.Count!=0)throw new Exception("Core gained dependencies");
            ok=true;
        }catch(Exception e){failed++;error=e.ToString();}
        results.Add(new{package=Path.GetFileName(path),member=entry.FullName,ok,error});
    }
}
var hashes=args.Skip(1).ToDictionary(Path.GetFileName,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant());
File.WriteAllText(args[0],JsonSerializer.Serialize(new{failed,engine="SurvivalcraftAPI 1.9.3.1",packages=hashes,checks=results},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Native metadata: {results.Count-failed}/{results.Count} passed");return failed==0?0:1;
