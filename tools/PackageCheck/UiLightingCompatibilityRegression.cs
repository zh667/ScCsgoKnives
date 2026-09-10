using System.Reflection;
using Game;

static class UiLightingCompatibilityRegression {
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod, string enchantmentPath) {
        List<Result> results = [];
        void Check(string n, bool ok, string d) => results.Add(new("ui-light-compat/" + n, ok, d));
        var renderer = mod.GetType("Game.KnifePbrRenderer"); var tuning = mod.GetType("Game.KnifeTuning");
        float baseFactor = (float)tuning.GetField("PbrGunEnvIntensity").GetValue(null);
        var rig = mod.GetType("Game.CsmcKnifeRig"); int count = (int)rig.GetProperty("KnifeCount").GetValue(null);
        float F(int variant, string material, float scene) => (float)renderer.GetMethod("SceneEnvFactor").Invoke(null, [variant, material, scene]);
        try {
            foreach (string material in new[] { "cs2_arm", "cs2_glove" }) foreach (float intensity in new[] { 0f, .05f, .15f, .5f, 1f }) {
                var samples = new[] { F(0, material, intensity), F(count, material, intensity), F(count + 1, material, intensity), F(count + 35, material, intensity) };
                Check($"arms-independent-of-held-item/{material}/{intensity}", samples.All(x => Math.Abs(x - baseFactor) < .0001f), "same arms/gloves environment factor with knife, AK, M4 and grenade");
            }
            Check("night-studio-factor-bounded", F(0, null, .05f) <= baseFactor && F(count + 35, "grenade_hegrenade_cs2", .05f) <= baseFactor,
                "knife and grenade no longer get full studio IBL in dark scenes");
            Check("daytime-m4-studio-boost-removed", F(0, null, 1f) == 1 && F(count + 1, "m4a1s_hd__cu_m4a1s_csgo2048", 1f) == baseFactor,
                "M4 skins match factory environmental intensity; knives retain their separate calibration");
            if (enchantmentPath is null) return results;
            // No Harmony PatchAll or mod loader initialization: inspect its actual bit reader/classifier and
            // use its existing exclusion set. The rest of this process must not gain foreign gameplay patches.
            var enchantment = new PackageContext("enchantment135").LoadFromAssemblyPath(Path.GetFullPath(enchantmentPath));
            var manager = enchantment.GetType("Enchantment.EnchantmentManager", true);
            var blackField = manager.GetField("s_blacklistedAssemblies", BindingFlags.Static | BindingFlags.NonPublic);
            var blacklist = (HashSet<Assembly>)blackField.GetValue(null); var original = blacklist.ToArray();
            try {
                bool fire = (bool)manager.GetMethod("WeaponHasFireAspect").Invoke(null, [67785]);
                Check("p90-model-bits-look-like-fire", fire, "actual Enchantment.WeaponHasFireAspect(67785 from user's 0.28.2 P90 log) is true, despite no enchantment");
                bool registered = (bool)mod.GetType("Game.ScEnchantmentCompatibility").GetMethod("Register").Invoke(null, [enchantment]);
                foreach (string name in new[] { "ScGunBlock", "ScKnifeBlock", "ScGrenadeBlock", "ScGunSkinTemplateBlock", "ScGunCounterTemplateBlock" }) {
                    var block = (Block)Activator.CreateInstance(mod.GetType("Game." + name));
                    block.DefaultMeleePower = 10; // deliberate false-positive bait, matching the classifier's fallback
                    Check("enchantment-exclusion/" + name, registered && !(bool)manager.GetMethod("IsWeaponBlock").Invoke(null, [block]),
                        "actual third-party weapon predicate refuses our bit-packed items; FireAspect postfix exits before SetOnFire");
                }
                Check("vanilla-enchantments-retained", (bool)manager.GetMethod("IsWeaponBlock").Invoke(null, [new IronMacheteBlock()])
                    && (bool)manager.GetMethod("WeaponHasFireAspect").Invoke(null, [8]), "vanilla weapon classifier/fire bit unchanged");
            } finally { blacklist.Clear(); foreach (var a in original) blacklist.Add(a); }
        } catch (Exception e) { Check("setup", false, e.ToString()); }
        return results;
    }
}
