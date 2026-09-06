using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

static class ResourceAudit {
    internal static void Write(Assembly mod, string output, string packageHash) {
        object Invoke(string type, string method, params object[] args) => mod.GetType("Game." + type).GetMethod(method).Invoke(null, args);
        object Cache(string type, string field) => mod.GetType("Game." + type).GetField(field, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        object[] caches = [Cache("Cs2Rig", "s_assets"), Cache("Cs2SkinnedMesh", "s_weapons"), Cache("Cs2RigidMesh", "s_cache")];
        foreach (var cache in caches) cache.GetType().GetMethod("Clear").Invoke(cache, null);
        long before = GC.GetTotalMemory(true);
        var watch = Stopwatch.StartNew();
        string[] assets = mod.GetManifestResourceNames().Where(n => n.EndsWith(".cs2.animation.json"))
            .Select(n => n.Split("AnimationData.")[1].Replace(".cs2.animation.json", "")).ToArray();
        foreach (string asset in assets) {
            Invoke("Cs2Rig", "Sample", asset, "idle", .125f);
            Invoke("Cs2SkinnedMesh", "Weapon", asset);
            Invoke("Cs2RigidMesh", "For", asset);
        }
        watch.Stop();
        long after = GC.GetTotalMemory(true);
        var result = new { packageSha256 = packageHash, assets = assets.Length, elapsedMs = watch.ElapsedMilliseconds,
            managedBeforeBytes = before, managedAfterBytes = after, managedGrowthBytes = after - before,
            cacheEntries = caches.Select(c => (int)c.GetType().GetProperty("Count").GetValue(c)).ToArray(),
            scope = "Fresh headless host, visit all 63 animations/meshes then force GC. Managed heap only; excludes GPU textures, game world, native memory and process working set. Timing is not an FPS benchmark." };
        File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(result));
    }
}
