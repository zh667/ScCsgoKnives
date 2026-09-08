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
            int older = (int)registry.GetField("SchemaWithoutSkins").GetRawConstantValue();
            int layout = (int)spec.GetField("DataLayout").GetRawConstantValue();
            Check("schema-and-layout", schema == older + 1 && layout == 5,
                $"record schema {schema} (schema {older} had no finish); item layout stamp {layout} unchanged by finishes");
        }
        catch (Exception e) { Check("run", false, e.ToString()); }
        return results;
    }
}
