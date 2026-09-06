using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Engine.Media;
using Game;

static class AmmoHudRegression {
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod, string package) {
        List<Result> results = [];
        void Test(string name, Func<bool> test) { try { results.Add(new("ammo-hud/" + name, test(), name)); } catch (Exception e) { results.Add(new("ammo-hud/" + name, false, e.ToString())); } }
        var readout = mod.GetType("Game.ScAmmoReadout"); var spec = mod.GetType("Game.GunSpec");
        var ammoType = mod.GetType("Game.ScAmmoBlock"); var gunType = mod.GetType("Game.ScGunBlock");
        var oldTypes = new Dictionary<Type, int>(BlocksManager.BlockTypeToIndex);
        var previous = new[] { BlocksManager.Blocks[700], BlocksManager.Blocks[701] };
        object Static(string type, string method, params object[] args) => mod.GetType("Game." + type).GetMethod(method).Invoke(null, args);
        string Text(object r, string property) => (string)readout.GetProperty(property).GetValue(r);
        bool Flag(object r, string property) => (bool)readout.GetProperty(property).GetValue(r);
        int Value(object gun, int rounds) {
            int variant = Array.IndexOf((Array)spec.GetField("All").GetValue(null), gun);
            return Terrain.MakeBlockValue(701, 0, (int)Static("GunSpec", "MakeData", variant, rounds, false));
        }
        ComponentInventory Inventory(int value, int ammo, int count) {
            var inv = new ComponentInventory(); for (int i = 0; i < 10; i++) inv.m_slots.Add(new());
            inv.AddSlotItems(0, value, 1); inv.AddSlotItems(1, ammo, count); return inv;
        }
        try {
            BlocksManager.BlockTypeToIndex[ammoType] = 700; BlocksManager.BlockTypeToIndex[gunType] = 701;
            BlocksManager.Blocks[700] = (Block)Activator.CreateInstance(ammoType);
            BlocksManager.Blocks[701] = (Block)Activator.CreateInstance(gunType); BlocksManager.Blocks[701].MaxStacking = 1;
            using var zip = ZipFile.OpenRead(package);
            foreach (string lang in new[] { "zh-CN", "en-US" }) {
                using var reader = new StreamReader(zip.GetEntry("Assets/Lang/" + lang + ".json").Open());
                var dict = JsonDocument.Parse(reader.ReadToEnd()).RootElement.GetProperty("ScCsgoKnives").GetProperty("AmmoHud").Clone();
                Func<string, string> localize = key => dict.GetProperty(key).GetString();
                object Read(object gun, ComponentInventory inv, bool creative = false, double seconds = -1, bool loading = false) =>
                    readout.GetMethod("Read").Invoke(null, [gun, inv.GetSlotValue(0), inv, creative, seconds, loading, localize]);
                foreach (object gun in (Array)spec.GetField("All").GetValue(null)) {
                    string name = (string)spec.GetField("Name").GetValue(gun);
                    int capacity = (int)spec.GetField("Magazine").GetValue(gun);
                    if (name == "taser") {
                        Test(lang + "/taser-ready", () => {
                            var r = Read(gun, Inventory(Value(gun, 1), 700, 3));
                            return Text(r, "Main") == localize("Ready") && !Flag(r, "Charging") && !Text(r, "Main").Contains('∞');
                        });
                        foreach (bool creative in new[] { false, true }) foreach (double seconds in new[] { 10, 9.91, .01, 0, -1, double.NaN, 100 }) {
                            Test($"{lang}/taser-countdown/{creative}/{seconds}", () => {
                                var r = Read(gun, Inventory(Value(gun, 0), 700, 3), creative, seconds);
                                string expected = seconds is >= 10 or < 0 || double.IsNaN(seconds) ? "10.0" : seconds > 9 ? "10.0" : seconds > 0 ? "0.1" : "0.0";
                                return Text(r, "Main").Contains(expected) && Flag(r, "Charging") && !Flag(r, "Insufficient")
                                    && Text(r, "Detail") == localize("AutoCharge") && !Text(r, "Main").Contains('∞');
                            });
                        }
                        continue;
                    }
                    bool shells = (int)Static("ScReloadTransaction", "AmmoKind", gun) == 1;
                    int ammo = Terrain.MakeBlockValue(700, 0, shells ? 1 : 0);
                    int cost = (int)Static("ScReloadTransaction", "Required", gun);
                    foreach (bool creative in new[] { false, true }) foreach (int count in new[] { 0, Math.Max(0, cost - 1), cost, 7 }.Distinct()) {
                        Test($"{lang}/{name}/{creative}/reserve-{count}", () => {
                            var inv = Inventory(Value(gun, Math.Min(3, capacity)), ammo, count);
                            // Unrelated ammunition must not contribute to the reserve count.
                            inv.AddSlotItems(2, Terrain.MakeBlockValue(700, 0, shells ? 0 : 1), 20);
                            var r = Read(gun, inv, creative);
                            return Text(r, "Main").Contains($"/ {capacity}") && Text(r, "Main").EndsWith("×" + (creative ? "∞" : count))
                                && Flag(r, "Insufficient") == (!creative && count < cost)
                                && Text(r, "Main").Contains(localize(shells ? "Shells" : "Magazines").Split('·')[1].Trim().Split('×')[0].Trim());
                        });
                    }
                    Test($"{lang}/{name}/split-stacks-live-change", () => {
                        var inv = Inventory(Value(gun, 0), ammo, 2); inv.AddSlotItems(3, ammo, 3);
                        if (!Text(Read(gun, inv), "Main").EndsWith("×5")) return false;
                        inv.RemoveSlotItems(3, 2);
                        return Text(Read(gun, inv), "Main").EndsWith("×3") && Flag(Read(gun, inv), "Empty");
                    });
                }
                foreach (string name in new[] { "usp_silencer", "nova", "mag7", "negev" }) {
                    Test(lang + "/reload-events/" + name, () => {
                        object gun = Static("GunSpec", "ForAsset", name); bool tube = name == "nova";
                        int capacity = (int)spec.GetField("Magazine").GetValue(gun), cost = (int)Static("ScReloadTransaction", "Required", gun);
                        int ammo = Terrain.MakeBlockValue(700, 0, name is "nova" or "mag7" ? 1 : 0);
                        var inv = Inventory(Value(gun, 3), ammo, 10);
                        var t = Activator.CreateInstance(mod.GetType("Game.ScReloadTransaction"), inv, 0, inv.GetSlotValue(0), ammo, cost, capacity);
                        bool Step(string method) => (bool)t.GetType().GetMethod(method).Invoke(t, null);
                        if (tube) {
                            return Step("InsertShell") && Text(Read(gun, inv, loading: true), "Main").StartsWith("4 / ")
                                && Text(Read(gun, inv), "Main").EndsWith("×9") && Text(Read(gun, inv), "Detail") == localize("Tube");
                        }
                        if (!Step("Discard") || !Text(Read(gun, inv, loading: true), "Main").StartsWith("3 / ")
                            || !Text(Read(gun, inv), "Main").EndsWith("×10")) return false;
                        return Step("InsertMagazine") && Text(Read(gun, inv), "Main").StartsWith(capacity + " / ")
                            && Text(Read(gun, inv), "Main").EndsWith("×" + (10 - cost));
                    });
                }
            }
            Test("widget-passive-show-hide-dispose", () => {
                var oldFont = LabelWidget.m_bitmapFont;
                try {
                    LabelWidget.BitmapFont = (BitmapFont)RuntimeHelpers.GetUninitializedObject(typeof(BitmapFont));
                    var hudType = mod.GetType("Game.ScAmmoHud"); var hud = Activator.CreateInstance(hudType);
                    var panel = (StackPanelWidget)hudType.GetField("Panel").GetValue(hud); var host = new StackPanelWidget(); host.Children.Add(panel);
                    var r = Activator.CreateInstance(readout, "3 / 12", "Mags ×3", false, false, false);
                    hudType.GetMethod("Show").Invoke(hud, [r]);
                    bool passive = panel.IsVisible && !panel.IsHitTestVisible && panel.Children.All(w => !w.IsHitTestVisible);
                    hudType.GetMethod("Hide").Invoke(hud, null); bool hidden = !panel.IsVisible;
                    ((IDisposable)hud).Dispose(); return passive && hidden && host.Children.Count == 0;
                } finally { LabelWidget.BitmapFont = oldFont; }
            });
        } catch (Exception e) { results.Add(new("ammo-hud/setup", false, e.ToString())); }
        finally {
            BlocksManager.Blocks[700] = previous[0]; BlocksManager.Blocks[701] = previous[1];
            BlocksManager.BlockTypeToIndex.Clear(); foreach (var p in oldTypes) BlocksManager.BlockTypeToIndex[p.Key] = p.Value;
        }
        return results;
    }
}
