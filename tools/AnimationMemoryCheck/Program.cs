// Retained managed animation-cache bytes on this .NET runtime; not Android/process peak RAM.
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Collections;
using System.Runtime.CompilerServices;
if(args.Length!=3)throw new ArgumentException("AnimationMemoryCheck <core.dll> <label> <report.json>");
Engine.Dispatcher.Initialize();
string path=Path.GetFullPath(args[0]);var context=new AssemblyLoadContext(args[1]);
context.LoadFromAssemblyPath(Path.Combine(Path.GetDirectoryName(path),"ScCsgoResources.dll"));
var core=context.LoadFromAssemblyPath(path);
var rig=core.GetType("Game.CsmcKnifeRig");int count=(int)rig.GetProperty("AssetCount").GetValue(null);
var assets=Enumerable.Range(0,count).Select(i=>(string)rig.GetMethod("GetAssetName").Invoke(null,[i])).ToArray();
var sample=core.GetType("Game.Cs2Rig").GetMethod("Sample");
// Resolve metadata/JIT without retaining any full animation file.
core.GetType("Game.Cs2Rig").GetMethod("Duration").Invoke(null,[assets[0],"idle"]);
GC.Collect();GC.WaitForPendingFinalizers();long before=GC.GetTotalMemory(true);var watch=Stopwatch.StartNew();
foreach(string asset in assets)if(sample.Invoke(null,[asset,"idle",0f])==null)throw new Exception(asset+" no pose");
watch.Stop();GC.Collect();GC.WaitForPendingFinalizers();long after=GC.GetTotalMemory(true);
int cleared=RemoveInspect(core);
GC.Collect();GC.WaitForPendingFinalizers();long withoutInspect=GC.GetTotalMemory(true);
var counts=core.GetType("Game.ScResourceCaches").GetMethod("Counts").Invoke(null,null);
var report=new{label=args[1],assetsSampled=count,cacheCounts=counts,coreSha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),managedBefore=before,managedAfter=after,retainedAnimationDelta=after-before,inspectClipsCleared=cleared,inspectRetainedDelta=after-withoutInspect,loadMilliseconds=watch.ElapsedMilliseconds,
    scope="All 64 rigs sampled once through normal bounded cache; retained managed bytes after full GC, not all rigs retained. No geometry/textures/audio. Windows .NET only, not Android or peak/process RAM."};
File.WriteAllText(args[2],JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(report));

[MethodImpl(MethodImplOptions.NoInlining)]
static int RemoveInspect(Assembly core){
    var cache=core.GetType("Game.Cs2Rig").GetField("s_assets",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
    var entries=(IDictionary)cache.GetType().GetField("m_entries",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(cache);
    int cleared=0;
    foreach(object node in entries.Values){
        object entry=node.GetType().GetProperty("Value").GetValue(node);
        object asset=entry.GetType().GetProperty("Value").GetValue(entry);
        object file=asset.GetType().GetField("File").GetValue(asset);
        foreach(DictionaryEntry pair in (IDictionary)file.GetType().GetProperty("Clips").GetValue(file)){
            object clip=pair.Value;string name=pair.Key+" "+clip.GetType().GetProperty("Alias").GetValue(clip);
            if(name.Contains("inspect",StringComparison.OrdinalIgnoreCase)||name.Contains("lookat",StringComparison.OrdinalIgnoreCase)){
                var bones=(IDictionary)clip.GetType().GetProperty("Bones").GetValue(clip);
                if(bones.Count>0){cleared++;bones.Clear();}
            }
        }
    }
    return cleared;
}
