using System.Collections;
using System.Reflection;
using System.Text.Json;
using Engine;
using Game;

static class ResourceRegression {
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod, string package) {
        List<Result> results = [];
        void Test(string name, Func<bool> test) { try { results.Add(new("resources/" + name, test(), name)); } catch (Exception e) { results.Add(new("resources/" + name, false, e.ToString())); } }
        object Call(string type, string method, params object[] args) => mod.GetType("Game." + type).GetMethod(method).Invoke(null, args);
        void Clear() => Call("ScResourceCaches", "ClearAll");
        Dictionary<string, int> Counts() => (Dictionary<string, int>)Call("ScResourceCaches", "Counts");
        string[] names = mod.GetManifestResourceNames().Where(n => n.EndsWith(".cs2.animation.json"))
            .Select(n => n.Split("AnimationData.")[1].Replace(".cs2.animation.json", "")).ToArray();
        try {
            Clear();
            Test("startup-no-model-or-animation-load", () => {
                foreach (string name in new[] { "ScGunBlock", "ScKnifeBlock", "ScGrenadeBlock" }) {
                    var b = (Block)Activator.CreateInstance(mod.GetType("Game." + name)); b.Initialize();
                }
                return Counts().Values.All(n => n == 0);
            });
            foreach (string name in names) Test("metadata-only/" + name, () => {
                string resource = mod.GetManifestResourceNames().Single(n => n.EndsWith("." + name + ".cs2.animation.json"));
                using var stream = mod.GetManifestResourceStream(resource); using var document = JsonDocument.Parse(stream);
                var root = document.RootElement;
                string Value(string key) => root.TryGetProperty(key, out var p) && p.ValueKind != JsonValueKind.Null ? p.GetString() : null;
                return (bool)Call("Cs2Rig", "Has", name)
                    && (string)Call("Cs2Rig", "SkinnedResource", name) == Value("Skinned")
                    && (string)Call("Cs2Rig", "PartsResource", name) == Value("Parts")
                    && Counts().Values.All(n => n == 0);
            });
            var cacheType = mod.GetType("Game.ScResourceCache`2").MakeGenericType(typeof(string), typeof(object));
            object Cache(long idle, Func<long> clock) => Activator.CreateInstance(cacheType, "test-lru", 2, idle, clock);
            void Put(object c, string key, object value) => cacheType.GetProperty("Item").SetValue(c, value, [key]);
            object Get(object c, string key) => cacheType.GetProperty("Item").GetValue(c, [key]);
            int Count(object c) => (int)cacheType.GetProperty("Count").GetValue(c);
            bool Has(object c, string key) => (bool)cacheType.GetMethod("TryGetValue").Invoke(c, [key, null]);
            Test("lru-evicts-least-recent-and-keeps-live-reference", () => {
                long now = 0; var c = Cache(0, () => now++); object a = new(), b = new(), d = new();
                Put(c, "a", a); Put(c, "b", b); if (!ReferenceEquals(Get(c, "a"), a)) return false;
                Put(c, "c", d); return Count(c) == 2 && !Has(c, "b") && ReferenceEquals(Get(c, "a"), a) && b is not null;
            });
            Test("visible-models-protected-then-idle-trimmed", () => {
                long now = 0; var c = Cache(2000, () => now); Put(c, "a", new()); Put(c, "b", new()); Put(c, "c", new());
                if (Count(c) != 3) return false; now = 3000;
                Get(c, "a"); return Count(c) == 2 && Has(c, "a") && !Has(c, "b");
            });
            Test("null-results-cached-and-clear-releases-entries", () => {
                var c = Cache(0, () => 1); Put(c, "missing", null);
                bool cached = Has(c, "missing") && Get(c, "missing") is null;
                cacheType.GetMethod("Clear").Invoke(c, null); return cached && Count(c) == 0;
            });
            // Traverse every full rig and mesh, exceeding each cache limit. Compare a
            // real sample before and after eviction so reload cannot change animation data.
            string Signature(object pose) {
                var bones = (Dictionary<string, Matrix>)pose.GetType().GetField("Bones").GetValue(pose);
                return string.Join("|", bones.OrderBy(p => p.Key).Select(p => p.Key + ":" + p.Value.ToString()));
            }
            string expected = Signature(Call("Cs2Rig", "Sample", "butterfly", "idle", .125f));
            foreach (string name in names) {
                Test("load-with-cache-limits/" + name, () => {
                    if (Call("Cs2Rig", "Sample", name, "idle", .125f) is null) return false;
                    Call("Cs2SkinnedMesh", "Weapon", name); Call("Cs2RigidMesh", "For", name);
                    var counts = Counts(); return counts.GetValueOrDefault("animations") <= 12
                        && counts.GetValueOrDefault("skinned-weapons") <= 8 && counts.GetValueOrDefault("rigid-weapons") <= 8;
                });
            }
            Test("evicted-animation-reloads-identically", () => Signature(Call("Cs2Rig", "Sample", "butterfly", "idle", .125f)) == expected);
            foreach (string blockName in new[] { "ScKnifeBlock", "ScGunBlock", "ScGrenadeBlock" }) {
                var blockType = mod.GetType("Game." + blockName); var block = Activator.CreateInstance(blockType);
                var model = blockType.GetMethod("Model", BindingFlags.Instance | BindingFlags.NonPublic);
                int count = blockName == "ScKnifeBlock" ? 22 : blockName == "ScGunBlock" ? 35 : 6;
                for (int i = 0; i < count; i++) {
                    int variant = i;
                    if (blockName == "ScGunBlock") {
                        var spec = mod.GetType("Game.GunSpec"); object gun = ((Array)spec.GetField("All").GetValue(null)).GetValue(i);
                        string asset = (string)spec.GetField("Name").GetValue(gun);
                        if (((IEnumerable)Call("Cs2Rig", "GetMeshParts", asset)).Cast<object>().Any()) continue; // 3 legacy OBJ GPU loaders
                    }
                    foreach (bool flight in blockName == "ScGrenadeBlock" ? new[] { false, true } : new[] { false }) {
                        Test($"item-model-rebuild/{blockName}/{i}/{flight}", () => {
                            object[] args = blockName == "ScGrenadeBlock" ? [variant, flight] : [variant];
                            string Meshes(object value) {
                                IEnumerable<BlockMesh> meshes = value is BlockMesh mesh ? [mesh] : value is IEnumerable list
                                    ? list.Cast<object>().Select(x => (BlockMesh)x.GetType().GetField("Item1").GetValue(x))
                                    : [(BlockMesh)value.GetType().GetProperty("Mesh").GetValue(value)];
                                return string.Join("|", meshes.Select(m => $"{m.Vertices.Count}:{m.Indices.Count}:{m.Vertices[0].Position}:{m.Vertices[m.Vertices.Count-1].Position}"));
                            }
                            object first = model.Invoke(block, args); string a = Meshes(first);
                            Clear(); return Meshes(model.Invoke(block, args)) == a;
                        });
                    }
                }
            }
            Test("world-disposal-clears-managed-resources", () => {
                var loader = (ModLoader)Activator.CreateInstance(mod.GetType("Game.ScCsgoKnivesModLoader"));
                loader.OnProjectDisposed(); return Counts().Values.All(n => n == 0);
            });
            var policy = mod.GetType("Game.ScResourcePolicy"); var configure = policy.GetMethod("Configure", BindingFlags.Static | BindingFlags.NonPublic);
            void Lite(bool lite) => configure.Invoke(null, [lite]);
            try {
                Test("packaged-edition-loads-through-content-manager", () => {
                    using var zip = System.IO.Compression.ZipFile.OpenRead(package);
                    using var stream = zip.GetEntry("Assets/ScCsgoKnivesEdition.xml").Open();
                    var xml = System.Xml.Linq.XElement.Load(stream);
                    var caches = (IDictionary<string, List<object>>)typeof(ContentManager).GetField("Caches", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                    string key = "ScCsgoKnivesEdition"; bool had = caches.TryGetValue(key, out var previous);
                    try {
                        caches[key] = [xml]; policy.GetMethod("LoadEdition").Invoke(null, null);
                        return (bool)policy.GetProperty("Lite").GetValue(null) == ((string)xml.Attribute("Name") == "Lite");
                    } finally { if (had) caches[key] = previous; else caches.Remove(key); Lite(false); }
                });
                Test("lite-keeps-smoke-and-flash-coverage", () => {
                    var st = mod.GetType("Game.ScGrenadeState"); var smoke = Activator.CreateInstance(st);
                    st.GetField("Age").SetValue(smoke, 2f); st.GetField("Remaining").SetValue(smoke, 10f); st.GetField("Effect").SetValue(smoke, true);
                    string Sprites(object list) => string.Join("|", ((IEnumerable)list).Cast<object>().Select(o => o.ToString()));
                    Lite(false); string a = Sprites(Call("ScGrenadeVisuals", "Smoke", smoke, 5f));
                    string flash = Sprites(Call("ScGrenadeVisuals", "Burst", Vector3.Zero, .1f, true, false, 5f));
                    Lite(true); return a == Sprites(Call("ScGrenadeVisuals", "Smoke", smoke, 5f))
                        && flash == Sprites(Call("ScGrenadeVisuals", "Burst", Vector3.Zero, .1f, true, false, 5f));
                });
                Test("lite-reduces-decorative-blast-and-fire-only", () => {
                    int CountList(object value) => ((IEnumerable)value).Cast<object>().Count();
                    var st = mod.GetType("Game.ScGrenadeState"); var fire = Activator.CreateInstance(st);
                    st.GetField("Age").SetValue(fire, 2f); st.GetField("Remaining").SetValue(fire, 5f);
                    var points = Enumerable.Range(0, 20).Select(i => new Vector3(i, 0, 0)).ToArray();
                    Lite(false); int blast = CountList(Call("ScGrenadeVisuals", "Burst", Vector3.Zero, .1f, false, false, 5f));
                    int full = CountList(Call("ScGrenadeVisuals", "Fire", fire, points, 5f));
                    Lite(true); int lite = CountList(Call("ScGrenadeVisuals", "Fire", fire, points, 5f));
                    return CountList(Call("ScGrenadeVisuals", "Burst", Vector3.Zero, .1f, false, false, 5f)) == blast - 9 && lite >= points.Length && lite < full;
                });
            } finally { Lite(false); }
        } catch (Exception e) { results.Add(new("resources/setup", false, e.ToString())); }
        finally { if (mod.GetType("Game.ScResourceCaches") is not null) Clear(); }
        return results;
    }
}
