using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Game;

static class KnifeFinishRegression {
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod, string package) {
        List<Result> results = [];
        void Check(string name, bool ok, string detail = "") => results.Add(new("knife-finish/" + name, ok, detail));
        object Call(string type, string method, params object[] args) => mod.GetType("Game." + type).GetMethod(method).Invoke(null, args);
        try {
            using var zip = ZipFile.OpenRead(package);
            using var source = zip.GetEntry("Assets/ScKnifeFinishes.json").Open();
            using var manifest = JsonDocument.Parse(source);
            var rows = manifest.RootElement.GetProperty("rows").EnumerateArray().ToArray();
            Check("20-official-pairs", rows.Length == 20);
            Check("bayonet-ruby", (string)Call("ScKnifeSkinCatalog", "Finish", 3) == "am_ruby_marbleized"
                && ((string)Call("ScKnifeSkinCatalog", "Name", 1, 3)).Contains("红宝石")
                && rows.Single(r => r.GetProperty("asset").GetString() == "bayonet").GetProperty("paintId").GetInt32() == 415);
            foreach (var row in rows) {
                int variant = row.GetProperty("variant").GetInt32(); string asset = row.GetProperty("asset").GetString(), finish = row.GetProperty("finish").GetString();
                int skin = (int)Call("ScKnifeSkinCatalog", "ForVariant", variant);
                int value = Terrain.MakeBlockValue(700, 0, (int)Call("ScKnifeSkinCatalog", "With", variant, skin));
                string material = (string)Call("ScKnifeSkinCatalog", "MaterialForRender", asset, variant, value);
                string icon = (string)Call("ScKnifeSkinCatalog", "Icon", asset, skin, variant);
                Check(asset + "/selected-body-material", material == $"{asset}_finish__{finish}");
                Check(asset + "/factory-remains-factory", (string)Call("ScKnifeSkinCatalog", "MaterialForRender", asset, variant, Terrain.MakeBlockValue(700, 0, variant)) == asset + "_cs2");
                Check(asset + "/old-preview-value-retained", (string)Call("ScKnifeSkinCatalog", "MaterialForRender", asset, variant, Terrain.MakeBlockValue(700, 0, variant | 32)) == material);
                foreach (string map in new[] { material, material + "_normal", material + "_orm", icon })
                    Check(asset + "/map/" + map, zip.GetEntry("Assets/Textures/ScCsgoKnives/" + map + ".png") is not null);
                using var stream = zip.GetEntry("Assets/Textures/ScCsgoKnives/" + icon + ".png").Open();
                string hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                var sources = row.GetProperty("sourceHashes").EnumerateObject().Where(p => p.Name.Contains("default_generated")).ToArray();
                Check(asset + "/official-icon-source", sources.Length == 1 && sources[0].Value.GetString()?.Length == 64, sources.Length == 0 ? "missing" : sources[0].Value.GetString());
                Check(asset + "/display-icon", hash.Length == 64, hash);
            }
            foreach (int v in new[] { 8, 9 }) Check("no-fabricated-default-finish/" + v, (int)Call("ScKnifeSkinCatalog", "ForVariant", v) == 0);
            var blockType = mod.GetType("Game.ScKnifeBlock"); var block = Activator.CreateInstance(blockType);
            var values = ((IEnumerable)blockType.GetMethod("GetCreativeValues").Invoke(block, null)).Cast<int>().ToArray();
            Check("22-factory-plus-20-finishes", values.Length == 42 && values.Distinct().Count() == 42
                && values.Count(v => (int)Call("ScKnifeSkinCatalog", "Get", v) == 0) == 22);
            var renderer = mod.GetType("Game.CsmcFirstPersonRenderer");
            var skinned = renderer.GetMethod("DrawCs2SkinnedWeapon", BindingFlags.NonPublic | BindingFlags.Static);
            Check("actual-skinned-draw-resolves-finish", CombatRegression.Calls(skinned).Any(c => c.DeclaringType.Name == "ScKnifeSkinCatalog" && c.Name == "MaterialForRender"));
            Check("exact-drawn-item-is-an-argument", renderer.GetMethod("Draw").GetParameters().Last().Name == "itemValue"
                && skinned.GetParameters().Any(p => p.Name == "itemValue"));
            Check("third-person-resolves-finish", CombatRegression.Calls(mod.GetType("Game.ScThirdPerson").GetMethod("Draw"))
                .Any(c => c.DeclaringType.Name == "ScKnifeSkinCatalog" && c.Name == "Texture"));
        } catch (Exception e) { Check("exception", false, e.ToString()); }

        var saved = BlocksManager.m_categories.ToArray();
        try {
            var loader = (ModLoader)Activator.CreateInstance(mod.GetType("Game.ScCsgoKnivesModLoader"));
            foreach (string weapons in new[] { "Weapons", "武器" }) foreach (bool first in new[] { false, true }) {
                var names = new List<string> { "Terrain", weapons, "Clothes", "Spawner Eggs", "Fireworks", "CS武器", "Furniture", "第三方武器" };
                if (first) { names.Remove("CS武器"); names.Insert(0, "CS武器"); }
                BlocksManager.m_categories.Clear(); BlocksManager.m_categories.AddRange(names);
                // A real API widget with detached state: no window/GPU/world is needed.
                var widget = (CreativeInventoryWidget)RuntimeHelpers.GetUninitializedObject(typeof(CreativeInventoryWidget));
                typeof(ContainerWidget).GetField("Children").SetValue(widget, new WidgetsList(widget));
                widget.m_categories = names.Select(n => new CreativeInventoryWidget.Category { Name = n }).ToList();
                widget.m_componentCreativeInventory = new ComponentCreativeInventory { CategoryIndex = names.IndexOf("Fireworks") };
                widget.m_activeCategoryIndex = names.IndexOf("Fireworks");
                Call("ScCreativeCategoryOrder", "Global");
                loader.BeforeWidgetUpdate(widget);
                Check($"native-widget-order/{weapons}/{first}", widget.m_categories.Select(c => c.Name).SequenceEqual(BlocksManager.m_categories)
                    && widget.m_categories.FindIndex(c => c.Name == "CS武器") == widget.m_categories.FindIndex(c => c.Name == weapons) + 1);
                Check($"native-selection-preserved/{weapons}/{first}", widget.GetCategoryName(widget.m_componentCreativeInventory.CategoryIndex) == "Fireworks");
                var frozen = widget.m_categories.ToArray(); for (int frame = 0; frame < 30; frame++) loader.BeforeWidgetUpdate(widget);
                Check($"native-order-stable/{weapons}/{first}", frozen.SequenceEqual(widget.m_categories));
            }
        } catch (Exception e) { Check("category-exception", false, e.ToString()); }
        finally { BlocksManager.m_categories.Clear(); BlocksManager.m_categories.AddRange(saved); }
        return results;
    }
}
