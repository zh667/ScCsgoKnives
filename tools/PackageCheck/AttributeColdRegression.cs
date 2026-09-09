using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;

/// <summary>Must run before any animation self-test has warmed the DLL. No graphics or player world.</summary>
static class AttributeColdRegression {
    internal record Result(string Name,bool Ok,string Detail);
    internal static List<Result> Run(Assembly mod, bool verifyMetadata = true) {
        var checks=new List<Result>();
        void Check(string name,bool ok,string detail)=>checks.Add(new("attributes-cold/"+name,ok,detail));
        try {
            var attrs=mod.GetType("Game.ScGunAttributes",true);
            var specs=(Array)mod.GetType("Game.GunSpec",true).GetField("All").GetValue(null);
            var caches=mod.GetType("Game.ScResourceCaches",true);
            Dictionary<string,int> Counts()=>(Dictionary<string,int>)caches.GetMethod("Counts").Invoke(null,null);
            var before=Counts();long allocated=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();
            _=attrs.GetProperty("Ranges").GetValue(null);
            double coldMs=watch.Elapsed.TotalMilliseconds;
            foreach(int v in Enumerable.Range(0,specs.Length)) {
                int value=(int)attrs.GetMethod("TemplateValue").Invoke(null,[v]);
                foreach(int level in new[]{0,10})_ = attrs.GetMethod("Rows").Invoke(null,[specs.GetValue(v),value,level]);
            }
            var after=Counts();
            string metric=$"cold ranges {coldMs:0.00} ms, all 35 Lv0/Lv10 rows {watch.Elapsed.TotalMilliseconds:0.00} ms, allocated {(GC.GetAllocatedBytesForCurrentThread()-allocated)/1048576.0:0.00} MiB; animations before={before.GetValueOrDefault("animations")} after={after.GetValueOrDefault("animations")}";
            Check("no-skeletal-load-for-35-guns",after.GetValueOrDefault("animations")==0,metric);
            Console.Error.WriteLine("[ATTRIBUTE_BENCH] "+metric);
            if (!verifyMetadata) return checks;
            string resource=mod.GetManifestResourceNames().Single(n=>n.EndsWith(".cs2_catalog.json"));
            using var stream=mod.GetManifestResourceStream(resource);var metadata=JsonNode.Parse(stream).AsObject();
            foreach(var (name,entry) in metadata) {
                using var source=mod.GetManifestResourceStream(mod.GetManifestResourceNames().Single(n=>n.EndsWith($"AnimationData.{name}.cs2.animation.json")));
                var full=JsonNode.Parse(source)["Clips"].AsObject();var light=entry["Clips"]?.AsObject();
                bool same=light?.Count==full.Count;
                foreach(var (clipName,clip) in full) {
                    var projected=new JsonObject();
                    foreach(string field in new[]{"SourceName","Alias","Duration","Events","Additive","AdditiveBase","AdditiveOver"})
                        if(clip.AsObject().TryGetPropertyValue(field,out var val))projected[field]=val?.DeepClone();
                    same &= light is not null && light.TryGetPropertyValue(clipName,out var actual) && JsonNode.DeepEquals(projected,actual);
                }
                Check("source-metadata-identical/"+name,same,"every clip alias, duration and event matches packaged original; no bone curves in metadata");
            }
        }catch(Exception e){Check("setup",false,e.ToString());}
        return checks;
    }
}
