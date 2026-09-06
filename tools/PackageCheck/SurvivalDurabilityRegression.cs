using System.IO.Compression;
using System.Reflection;
using Game;
using TemplatesDatabase;

// Runs the engine's real wear and inventory save/load paths against packaged blocks.
static class SurvivalDurabilityRegression {
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod, string package) {
        List<Result> results = [];
        void Test(string name, Func<bool> test) {
            try { results.Add(new("survival-runtime/" + name, test(), name)); }
            catch (Exception e) { results.Add(new("survival-runtime/" + name, false, e.ToString())); }
        }
        string[] names = ["ScKnifeBlock", "ScGunBlock", "ScAmmoBlock", "ScWeaponMaterialBlock", "ScWeaponWorkbenchBlock", "ScGrenadeBlock"];
        var oldTypes = new Dictionary<Type, int>(BlocksManager.BlockTypeToIndex);
        var oldNames = new Dictionary<string, int>(BlocksManager.BlockNameToIndex);
        var previous = Enumerable.Range(700, names.Length).Select(i => BlocksManager.Blocks[i]).ToArray();
        var blocks = names.Select(n => (Block)Activator.CreateInstance(mod.GetType("Game." + n, true))).ToArray();
        object Call(string type, string method, params object[] args) => mod.GetType("Game." + type).GetMethod(method).Invoke(null, args);
        ComponentInventory Inventory(int value, int count = 1, int ammo = 0, int ammoCount = 0) {
            var inv = new ComponentInventory();
            for (int i = 0; i < 10; i++) inv.m_slots.Add(new());
            inv.AddSlotItems(0, value, count);
            if (ammoCount > 0) inv.AddSlotItems(1, ammo, ammoCount);
            return inv;
        }
        ComponentInventory Reload(ComponentInventory inv) {
            var values = new ValuesDictionary(); inv.Save(values, null);
            values.SetValue("SlotsCount", inv.SlotsCount);
            var copy = new ComponentInventory(); copy.Load(values, null); return copy;
        }
        bool Same(ComponentInventory a, ComponentInventory b) => a.ActiveSlotIndex == b.ActiveSlotIndex
            && Enumerable.Range(0, a.SlotsCount).All(i => a.GetSlotValue(i) == b.GetSlotValue(i) && a.GetSlotCount(i) == b.GetSlotCount(i));
        try {
            for (int i = 0; i < blocks.Length; i++) {
                var b = blocks[i]; b.BlockIndex = 700 + i;
                BlocksManager.BlockTypeToIndex[b.GetType()] = b.BlockIndex; BlocksManager.BlockNameToIndex[names[i]] = b.BlockIndex;
                BlocksManager.Blocks[b.BlockIndex] = b;
            }
            // Use shipped CSV metadata, including the old knife=1200 / gun=0 when testing a baseline.
            using (var zip = ZipFile.OpenRead(package)) {
                using var reader = new StreamReader(zip.GetEntry("Assets/ScCsgoKnivesBlocksData.csv").Open());
                var rows = reader.ReadToEnd().Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(r => r.Split(';')).ToArray();
                foreach (var row in rows.Skip(1)) {
                    var b = blocks.Single(b => b.GetType().Name == row[0]);
                    b.Durability = int.Parse(row[Array.IndexOf(rows[0], "Durability")]);
                    b.MaxStacking = int.Parse(row[Array.IndexOf(rows[0], "MaxStacking")]);
                }
            }
            foreach (var block in blocks) {
                Test("metadata/" + block.GetType().Name, () => block.Durability == -1);
                var values = block.GetCreativeValues().ToList();
                if (block == blocks[1]) {
                    foreach (int full in values.ToArray()) {
                        int data = Terrain.ExtractData(full);
                        foreach (int rounds in new[] { 0, 1 }) foreach (bool off in new[] { false, true }) {
                            int changed = (int)Call("GunSpec", "SetRounds", data, rounds);
                            changed = (int)Call("GunSpec", "SetSilencerOff", changed, off);
                            values.Add(Terrain.ReplaceData(full, changed));
                        }
                    }
                }
                foreach (int value in values.Distinct()) {
                    string id = block.GetType().Name + "/" + Terrain.ExtractData(value);
                    Test("wear/" + id, () => {
                        if (block.GetDurability(value) != -1 || block.GetDamage(value) != 0) return false;
                        foreach (int damage in new[] { 0, 1, 16, 1201, 4095, int.MaxValue })
                            if (BlocksManager.DamageItem(value, damage) != value || block.SetDamage(value, damage) != value) return false;
                        return true;
                    });
                    foreach (var mode in new[] { GameMode.Survival, GameMode.Harmless, GameMode.Challenging, GameMode.Cruel, GameMode.Creative }) {
                        Test("active-tool-and-save/" + mode + "/" + id, () => {
                            int count = Math.Min(3, block.GetMaxStacking(value));
                            var inv = Inventory(value, count);
                            // Palette localization is unrelated to wear and requires a graphical game host.
                            var settings = (WorldSettings)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(WorldSettings));
                            settings.GameMode = mode;
                            var miner = new ComponentMiner { Inventory = inv, m_subsystemGameInfo = new SubsystemGameInfo { WorldSettings = settings } };
                            for (int hit = 0; hit < 32; hit++) miner.DamageActiveTool(1);
                            return inv.GetSlotValue(0) == value && inv.GetSlotCount(0) == count && Same(inv, Reload(inv));
                        });
                    }
                }
            }
            for (int variant = 22; variant < 32; variant++) {
                int value = Terrain.MakeBlockValue(700, 0, variant);
                Test("unknown-knife/" + variant, () => !(bool)Call("ScKnifeBlock", "IsKnown", value)
                    && (int)Call("KnifeAnimationController", "ResolveVariant", value) == -1
                    && blocks[0].GetDisplayName(null, value).Contains("未知刀具")
                    && blocks[0].GetDescription(value).Contains("保留")
                    && Call("ScWeaponCrafting", "Find", value) is null
                    && Same(Inventory(value), Reload(Inventory(value))));
            }
            var spec = mod.GetType("Game.GunSpec");
            var guns = (Array)spec.GetField("All").GetValue(null);
            int magazine = Terrain.MakeBlockValue(702), shell = Terrain.MakeBlockValue(702, 0, 1);
            for (int v = 0; v < guns.Length; v++) {
                object gun = guns.GetValue(v); string name = (string)spec.GetField("Name").GetValue(gun);
                if (name == "taser") continue;
                int capacity = (int)spec.GetField("Magazine").GetValue(gun);
                int cost = (int)Call("ScReloadTransaction", "Required", gun);
                int ammo = (int)Call("ScReloadTransaction", "AmmoKind", gun) == 1 ? shell : magazine;
                int full = Terrain.MakeBlockValue(701, 0, (int)Call("GunSpec", "MakeData", v, capacity, false));
                int partial = Terrain.ReplaceData(full, (int)Call("GunSpec", "SetRounds", Terrain.ExtractData(full), capacity - 1));
                bool tube = (bool)Call("ScReloadTransaction", "IsTube", name);
                object Transaction(ComponentInventory inv) => Activator.CreateInstance(mod.GetType("Game.ScReloadTransaction"), inv, 0, inv.GetSlotValue(0), ammo, cost, capacity);
                bool Step(object t, string method) => (bool)t.GetType().GetMethod(method).Invoke(t, null);
                Test("shoot-reload-save/" + name, () => {
                    var inv = Inventory(full, 1, ammo, 10);
                    if (!(bool)Call("ScInventoryTransaction", "ReplaceWithCost", inv, 0, full, partial, 0, 0)) return false;
                    var copy = Reload(inv); if (!Same(inv, copy)) return false;
                    var t = Transaction(copy);
                    if (tube ? !Step(t, "InsertShell") : !Step(t, "Discard") || !Step(t, "InsertMagazine")) return false;
                    return copy.GetSlotValue(0) == full && copy.GetSlotCount(1) == 10 - (tube ? 1 : cost) && Same(copy, Reload(copy));
                });
                Test("interrupted-reload-save/" + name, () => {
                    var inv = Inventory(partial, 1, ammo, 10); var t = Transaction(inv);
                    if (tube ? !Step(t, "InsertShell") : !Step(t, "Discard")) return false;
                    t.GetType().GetMethod("Cancel").Invoke(t, null);
                    if (Step(t, tube ? "InsertShell" : "InsertMagazine")) return false;
                    var copy = Reload(inv);
                    return Same(inv, copy) && (int)Call("GunSpec", "GetRounds", Terrain.ExtractData(copy.GetSlotValue(0))) == (tube ? capacity : capacity - 1)
                        && copy.GetSlotCount(1) == (tube ? 9 : 10);
                });
                Test("insufficient-ammo/" + name, () => {
                    var inv = Inventory(partial); var t = Transaction(inv);
                    if (!tube && !Step(t, "Discard")) return false;
                    int expected = inv.GetSlotValue(0);
                    return !Step(t, tube ? "InsertShell" : "InsertMagazine") && inv.GetSlotValue(0) == expected && Same(inv, Reload(inv));
                });
            }
            for (int kind = 0; kind < 6; kind++) foreach (bool spawn in new[] { true, false }) {
                Test($"grenade-consumption-save/{kind}/{spawn}", () => {
                    int value = Terrain.MakeBlockValue(705, 0, kind); var inv = Inventory(value, 3);
                    var t = Activator.CreateInstance(mod.GetType("Game.ScThrowTransaction"), inv);
                    bool Commit() => (bool)t.GetType().GetMethod("Commit").Invoke(t, [false, (Func<bool>)(() => true), (Func<bool>)(() => spawn)]);
                    return Commit() == spawn && !Commit() && inv.GetSlotCount(0) == (spawn ? 2 : 3) && Same(inv, Reload(inv));
                });
            }
        } catch (Exception e) { results.Add(new("survival-runtime/setup", false, e.ToString())); }
        finally {
            for (int i = 0; i < blocks.Length; i++) BlocksManager.Blocks[700 + i] = previous[i];
            BlocksManager.BlockTypeToIndex.Clear(); foreach (var p in oldTypes) BlocksManager.BlockTypeToIndex[p.Key] = p.Value;
            BlocksManager.BlockNameToIndex.Clear(); foreach (var p in oldNames) BlocksManager.BlockNameToIndex[p.Key] = p.Value;
        }
        return results;
    }
}
