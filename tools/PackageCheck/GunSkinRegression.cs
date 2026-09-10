using System.Collections;
using System.IO.Compression;
using System.Reflection;

/// <summary>Every finish the packaged catalogue offers must ship the textures the packaged renderer will ask
/// for, and no finish may claim a gun that has no model. This is the drift guard between
/// tools/gun_skins_catalog.json (which bakes the assets) and ScGunSkinCatalog (which names them at runtime):
/// they are written separately, so the package is where they have to agree.</summary>
static class GunSkinRegression {
    internal record Result(string Name, bool Ok, string Detail);

    internal static List<Result> Run(Assembly mod, string package) {
        var results = new List<Result>();
        void Check(string name, bool ok, string detail) => results.Add(new("gun-skin/" + name, ok, detail));
        try {
            var catalog = mod.GetType("Game.ScGunSkinCatalog");
            var skins = (Array)catalog.GetField("All").GetValue(null);
            var spec = mod.GetType("Game.GunSpec");
            var guns = (Array)spec.GetField("All").GetValue(null);
            var names = guns.Cast<object>().Select(g => (string)spec.GetField("Name").GetValue(g)).ToArray();
            using var zip = ZipFile.OpenRead(package);
            var entries = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool Has(string texture) => entries.Contains($"Assets/Textures/ScCsgoKnives/{texture}.png");

            Check("catalogue-not-empty", skins.Length > 0, $"{skins.Length} finishes");
            var native = mod.GetType("Game.ScGunNativeMesh");
            Check("world-uv-repeat", ReferenceEquals(native.GetProperty("WorldSampler").GetValue(null), Engine.Graphics.SamplerState.LinearWrap), "native world views use repeat sampling instead of BlocksManager PointClamp");
            var partsMethod = native.GetMethod("Parts");
            using var addedStream=mod.GetManifestResourceStream("Game.AnimationData.gun_additional_skins.json");
            using var added=System.Text.Json.JsonDocument.Parse(addedStream);
            var expectedLegacy=added.RootElement.EnumerateArray().Where(x=>x.GetProperty("legacy").GetBoolean()).Select(x=>x.GetProperty("gun").GetString()).ToHashSet();
            var newIds=added.RootElement.EnumerateArray().Select(x=>x.GetProperty("paintId").GetInt32()).ToArray();
            Check("new33-exact-catalogue",newIds.Length==33&&newIds.Distinct().Count()==33&&newIds.Contains(1119)
                &&!newIds.Any(x=>x is >=1120 and <=1123),"33 new finishes; Glock Emerald only");
            foreach (string asset in new[] { "ak47", "awp", "m4a1s" }.Concat(expectedLegacy)) {
                var parts = (Array)partsMethod.Invoke(null, [asset]);
                var rig = mod.GetType("Game.Cs2Rig");
                bool present = parts.Length > 0, bound = true, scope = false;
                foreach (var part in parts) {
                    var t = part.GetType();
                    string path = (string)native.GetMethod("ModelPath").Invoke(null, [asset, part]);
                    present &= entries.Contains("Assets/" + path + ".obj");
                    if (t.GetProperty("Material").GetValue(part) is string special) {
                        scope = true;
                        present &= Has(special) && Has(special + "_orm") && Has(special + "_normal");
                    }
                    foreach (string clip in new[] { "idle", "deploy", "inspect", "reload" }) foreach (float time in new[] { 0f, .4f, 1f }) {
                        var pose = rig.GetMethod("Sample").Invoke(null, [asset, clip, time]);
                        var matrix = (Engine.Matrix)t.GetMethod("World").Invoke(part, [pose]);
                        bound &= float.IsFinite(matrix.M11) && float.IsFinite(matrix.M41) && Math.Abs(matrix.M11)+Math.Abs(matrix.M12)+Math.Abs(matrix.M13) > 1;
                    }
                }
                Check("native-model/" + asset, present && bound && (asset != "awp" || scope), $"{parts.Length} native parts, finite deploy/inspect/reload bindings, separate scope material={scope}");
                var counterRenderer=mod.GetType("Game.ScStatTrakRenderer");
                object[] frameArgs=[asset,true,null];
                bool counterFrame=(bool)counterRenderer.GetMethod("ItemMatrix").Invoke(null,frameArgs);
                Check("native-counter-frame/"+asset,counterFrame,"official legacy attachment resolves in the actual native body frame");
                foreach(string clip in new[]{"idle","deploy","inspect","reload"}) {
                    var pose=rig.GetMethod("Sample").Invoke(null,[asset,clip,.4f]);
                    object[] args=[asset,true,pose,null];
                    Check("native-counter-pose/"+asset+"/"+clip,(bool)counterRenderer.GetMethod("AttachmentWorld").Invoke(null,args),"official attachment bone exists in animated pose");
                }
                if(asset is "aug" or "sg556") {
                    var materials=parts.Cast<object>().Select(p=>(string)p.GetType().GetProperty("Material").GetValue(p)).ToArray();
                    Check("native-optical-material/"+asset,materials.Contains("cs2_scope_lens")
                        &&!materials.Any(x=>x?.EndsWith("_shared_scope_lens")==true),"same dedicated dark lens material as the factory gun");
                    if(asset=="aug")Check("aug-internal-scope-not-body-atlas",materials.Contains("cs2_legacy_scope"),"internal optical surface has its own material");
                }
                if(asset is "ssg08" or "scar20")Check("sniper-independent-scope/"+asset,
                    parts.Cast<object>().Any(p=>(string)p.GetType().GetProperty("Material").GetValue(p)=="cs2_legacy_scope"),
                    "scope interior uses official shared optical material, not the skin atlas");
                var weaponType = mod.GetType("Game.ScThirdPersonWeapon");
                var weapon = weaponType.GetMethod("For").Invoke(null, [asset, true]);
                var groups = weapon is null ? Array.Empty<object>() : ((Array)weaponType.GetField("Groups").GetValue(weapon)).Cast<object>().ToArray();
                Check("native-third-person/" + asset, weapon is not null && (int)weaponType.GetField("Vertices").GetValue(weapon) > 1000
                    && (asset != "awp" || groups.Any(g => (string)g.GetType().GetProperty("Texture").GetValue(g) == "cs2_legacy_scope")),
                    $"packaged OBJ geometry baked into {groups.Length} independent material/silencer groups");
            }
            var visibility=mod.GetType("Game.ScGunPartVisibility").GetMethod("Visible");
            foreach(string clip in new[]{"idle","deploy","draw","draw2","inspect","shoot","reload"}) {
                bool shown=(bool)visibility.Invoke(null,["revolver","loader_holder",clip,.4f,false]);
                bool cylinder=(bool)visibility.Invoke(null,["revolver","cylinder",clip,.4f,false]);
                Check("r8-loader-visibility/"+clip,shown==(clip=="reload")&&cylinder,"reload prop only; actual cylinder remains visible");
            }
            var aperture=mod.GetType("Game.KnifePbrRenderer").GetMethod("TryDrawPart").GetParameters().Last();
            Check("native-optical-aperture-input",aperture.Name=="scopeAperture"&&aperture.IsOptional,"OBJ skin path supports the same ADS cutout as rigid factory geometry");
            var template = (Game.Block)Activator.CreateInstance(mod.GetType("Game.ScGunSkinTemplateBlock"));
            Check("template-icon-scale", Math.Abs(template.DefaultIconViewScale - .8f) < .001f, "same .8 icon view scale as original gun CSV");
            var seenId = new HashSet<int>();
            var seenKey = new HashSet<string>(StringComparer.Ordinal);
            foreach (object skin in skins) {
                var t = skin.GetType();
                int paintId = (int)t.GetProperty("PaintId").GetValue(skin);
                string key = (string)t.GetProperty("Key").GetValue(skin);
                string gun = (string)t.GetProperty("Gun").GetValue(skin);
                string material = (string)t.GetProperty("Material").GetValue(skin);
                string icon = (string)t.GetProperty("Icon").GetValue(skin);
                var missing = new List<string>();
                foreach (string texture in new[] { material, material + "_orm", material + "_normal", icon })
                    if (!Has(texture)) missing.Add(texture);
                bool model = Array.IndexOf(names, gun) >= 0;
                bool unique = seenId.Add(paintId) & seenKey.Add(key);
                bool legacy = (bool)native.GetMethod("UsesLegacy").Invoke(null, [gun, material]);
                bool expectedBody = newIds.Contains(paintId) ? expectedLegacy.Contains(gun) : gun != "ak47" && paintId != 1177;
                Check($"native-routing/{key}", legacy == expectedBody, legacy ? "body_legacy" : "body_hd");
                var factor = mod.GetType("Game.KnifePbrRenderer").GetMethod("GunEnvFactor");
                int variant = (int)mod.GetType("Game.ScGunBlock").GetMethod("AssetIndex").Invoke(null, [Array.IndexOf(names, gun)]);
                float light = (float)factor.Invoke(null, [variant, material]);
                float original = (float)factor.Invoke(null, [variant, gun + "_hd"]);
                Check($"finish-lighting/{key}", light == original,
                    $"environment factor finish={light}, original={original}; scene light still multiplies both");
                Check($"assets/{key}", missing.Count == 0 && model && unique && paintId > 0,
                    missing.Count > 0 ? "missing " + string.Join(", ", missing)
                    : !model ? $"{gun} is not a gun variant"
                    : !unique ? "duplicate paint ID or key"
                    : paintId <= 0 ? "paint ID 0 is reserved for the factory look"
                    : $"paint {paintId} on {gun}, 4 textures present");
            }

            // The runtime resolver must fall back to the factory material for anything it cannot place, and
            // must never hand a gun another gun's finish.
            var material2 = catalog.GetMethod("Material");
            var icon2 = catalog.GetMethod("Icon");
            object first = skins.GetValue(0);
            string firstGun = (string)first.GetType().GetProperty("Gun").GetValue(first);
            int firstId = (int)first.GetType().GetProperty("PaintId").GetValue(first);
            string otherGun = names.First(n => n != firstGun);
            Check("resolver-falls-back",
                (string)material2.Invoke(null, [firstGun, 0]) == $"{firstGun}_hd"
                && (string)material2.Invoke(null, [firstGun, 999999]) == $"{firstGun}_hd"
                && (string)material2.Invoke(null, [otherGun, firstId]) == $"{otherGun}_hd"
                && (string)icon2.Invoke(null, [otherGun, firstId]) == $"{otherGun}_slot",
                "unknown, absent and foreign finishes all resolve to the factory set");
            Check("factory-geometry-fallback", !(bool)native.GetMethod("UsesLegacy").Invoke(null, [firstGun, firstGun + "_hd"]), "factory material never selects legacy UV geometry");

            // Costs live in one table and every tier must charge something.
            var cost = (IDictionary)catalog.GetField("Cost").GetValue(null);
            bool priced = cost.Count > 0;
            foreach (DictionaryEntry e in cost) {
                var v = e.Value.GetType();
                int blank = (int)v.GetField("Item1").GetValue(e.Value), mech = (int)v.GetField("Item2").GetValue(e.Value), paint = (int)v.GetField("Item3").GetValue(e.Value);
                priced &= blank > 0 && mech > 0 && paint > 0;
            }
            Check("tiers-priced", priced, $"{cost.Count} tiers, every one charging blanks, mechanisms and paint");

            // The record schema that carries a finish must be the one the build writes.
            var registry = mod.GetType("Game.ScGunRegistry");
            int schema = (int)registry.GetField("Schema").GetRawConstantValue();
            int noSkins = (int)registry.GetField("SchemaWithoutSkins").GetRawConstantValue();
            int noGrowth = (int)registry.GetField("SchemaWithoutGrowth").GetRawConstantValue();
            int tenLevels = (int)registry.GetField("SchemaTenLevels").GetRawConstantValue();
            int layout = (int)spec.GetField("DataLayout").GetRawConstantValue();
            var known = registry.GetMethod("IsKnownSchema");
            bool converts = (bool)known.Invoke(null, [noSkins]) && (bool)known.Invoke(null, [noGrowth]) && (bool)known.Invoke(null, [tenLevels]) && (bool)known.Invoke(null, [schema])
                && !(bool)known.Invoke(null, [schema + 1]) && !(bool)known.Invoke(null, [0]);
            Check("schema-and-layout", schema == tenLevels + 1 && tenLevels == noGrowth + 1 && noGrowth == noSkins + 1 && layout == 5 && converts,
                $"record schema {schema} (schema {noSkins} had no finish, {noGrowth} no counter); every earlier schema still converts, a later one is refused; item layout stamp {layout} unchanged");
        }
        catch (Exception e) { Check("run", false, e.ToString()); }
        return results;
    }
}
