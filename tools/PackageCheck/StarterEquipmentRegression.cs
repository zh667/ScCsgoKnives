using System.Reflection;
using Engine;
using Game;
using TemplatesDatabase;

static class StarterEquipmentRegression {
    internal record Result(string Name, bool Ok, string Detail);
    internal static List<Result> Run(Assembly mod) {
        List<Result> results = [];
        void Test(string name, Func<bool> test) { try { results.Add(new("starter/" + name, test(), name)); } catch (Exception e) { results.Add(new("starter/" + name, false, e.ToString())); } }
        var type = mod.GetType("Game.SubsystemScStarterEquipment"); var spec = mod.GetType("Game.GunSpec");
        var savedTypes = new Dictionary<Type, int>(BlocksManager.BlockTypeToIndex); var savedNames = new Dictionary<string, int>(BlocksManager.BlockNameToIndex);
        Block[] previous = [BlocksManager.Blocks[700], BlocksManager.Blocks[701], BlocksManager.Blocks[702]];
        try {
            string[] names = ["ScKnifeBlock", "ScGunBlock", "ScAmmoBlock"];
            for (int i = 0; i < 3; i++) {
                var blockType = mod.GetType("Game." + names[i]); BlocksManager.BlockTypeToIndex[blockType] = 700 + i; BlocksManager.BlockNameToIndex[names[i]] = 700 + i;
                var block = (Block)Activator.CreateInstance(blockType); if (i < 2) block.MaxStacking = 1; BlocksManager.Blocks[700 + i] = block;
            }
            var items = ((Array)type.GetMethod("Items").Invoke(null, null)).Cast<object>()
                .Select(item => (Value: (int)item.GetType().GetField("Item1").GetValue(item), Count: (int)item.GetType().GetField("Item2").GetValue(item))).ToArray();
            Test("ct-knife-full-usp-three-magazines", () => {
                int data = Terrain.ExtractData(items[1].Value);
                int pistol = (int)mod.GetType("Game.ScGunBlock").GetMethod("GetVariant").Invoke(null, [items[1].Value]);
                var guns = (Array)spec.GetField("All").GetValue(null); var gun = guns.GetValue(pistol);
                return items.Length == 3 && items[0].Count == 1 && items[1].Count == 1 && items[2].Count == 3
                    && (string)mod.GetType("Game.CsmcKnifeRig").GetMethod("GetAssetName").Invoke(null, [Terrain.ExtractData(items[0].Value)]) == "default_ct"
                    && (string)spec.GetField("Name").GetValue(gun) == "usp_silencer"
                    && (int)spec.GetMethod("GetRounds").Invoke(null, [data]) == 12
                    && !(bool)spec.GetMethod("GetSilencerOff").Invoke(null, [data])
                    && Terrain.ExtractContents(items[2].Value) == 702 && Terrain.ExtractData(items[2].Value) == 0;
            });
            foreach (var mode in Enum.GetValues<GameMode>()) foreach (var spawn in Enum.GetValues<PlayerData.SpawnMode>()) foreach (int count in new[] { 0, 1, 2 }) {
                Test($"eligibility/{mode}/{spawn}/{count}", () => (bool)type.GetMethod("Eligible").Invoke(null, [mode, spawn, count])
                    == (mode != GameMode.Creative && mode != GameMode.Adventure && spawn != PlayerData.SpawnMode.Respawn && count == 1));
            }
            ComponentInventory Inventory(int slots = 5) {
                var inv = new ComponentInventory();
                for (int i = 0; i < slots; i++) inv.m_slots.Add(new ComponentInventoryBase.Slot());
                return inv;
            }
            bool Grant(object system, int player, int count, IInventory inventory, Action<int, int> overflow = null, GameMode mode = GameMode.Survival, PlayerData.SpawnMode spawn = PlayerData.SpawnMode.InitialIntro) =>
                (bool)type.GetMethod("TryGrant").Invoke(system, [mode, spawn, player, count, inventory, overflow ?? ((_, _) => throw new Exception("Unexpected overflow"))]);
            int Count(IInventory inv, int value) => Enumerable.Range(0, inv.SlotsCount).Where(i => inv.GetSlotValue(i) == value).Sum(inv.GetSlotCount);
            foreach (var mode in new[] { GameMode.Harmless, GameMode.Survival, GameMode.Challenging, GameMode.Cruel }) foreach (var spawn in new[] { PlayerData.SpawnMode.InitialIntro, PlayerData.SpawnMode.InitialNoIntro }) {
                Test($"first-spawn-keeps-existing-items/{mode}/{spawn}", () => {
                    var inv = Inventory(); inv.m_slots[1].Value = 123; inv.m_slots[1].Count = 2; inv.ActiveSlotIndex = 1;
                    var system = Activator.CreateInstance(type);
                    return Grant(system, 1, 1, inv, mode: mode, spawn: spawn) && inv.GetSlotValue(1) == 123 && inv.GetSlotCount(1) == 2
                        && inv.ActiveSlotIndex == 1 && items.All(item => Count(inv, item.Value) == item.Count)
                        && !Grant(system, 1, 1, inv) && items.All(item => Count(inv, item.Value) == item.Count);
                });
            }
            Test("saved-claim-is-per-player", () => {
                var system = Activator.CreateInstance(type); var inv = Inventory(); if (!Grant(system, 4, 1, inv)) return false;
                var saved = new ValuesDictionary(); type.GetMethod("Save").Invoke(system, [saved]);
                var loaded = Activator.CreateInstance(type); type.GetMethod("Load").Invoke(loaded, [saved]);
                var other = Inventory();
                return !Grant(loaded, 4, 1, inv) && Grant(loaded, 5, 1, other) && items.All(item => Count(other, item.Value) == item.Count);
            });
            Test("old-world-and-respawn-do-not-grant", () => {
                var system = Activator.CreateInstance(type); type.GetMethod("Load").Invoke(system, [new ValuesDictionary()]);
                var inv = Inventory();
                return !Grant(system, 1, 2, inv) && !Grant(system, 1, 1, inv, spawn: PlayerData.SpawnMode.Respawn)
                    && inv.m_slots.All(s => s.Count == 0);
            });
            Test("full-backpack-drops-complete-kit-without-overwriting", () => {
                var inv = Inventory(1); inv.m_slots[0].Value = 123; inv.m_slots[0].Count = 2;
                var dropped = new Dictionary<int, int>(); var system = Activator.CreateInstance(type);
                return Grant(system, 1, 1, inv, (v, n) => dropped[v] = dropped.GetValueOrDefault(v) + n)
                    && items.All(item => dropped.GetValueOrDefault(item.Value) == item.Count)
                    && inv.GetSlotValue(0) == 123 && inv.GetSlotCount(0) == 2;
            });
            Test("partial-backpack-keeps-overflow-quantities", () => {
                var inv = Inventory(2); var dropped = new Dictionary<int, int>(); var system = Activator.CreateInstance(type);
                return Grant(system, 1, 1, inv, (v, n) => dropped[v] = dropped.GetValueOrDefault(v) + n)
                    && items.All(item => Count(inv, item.Value) + dropped.GetValueOrDefault(item.Value) == item.Count)
                    && dropped.GetValueOrDefault(items[2].Value) == 3;
            });
            var checkpoint = type.GetMethod("TryCheckpoint", BindingFlags.Instance | BindingFlags.NonPublic);
            bool Checkpoint(object system, bool current, int[] ready, Action save) => (bool)checkpoint.Invoke(system, [current, ready, save]);
            Test("checkpoint-deferred-until-ready-current-project-once", () => {
                var system = Activator.CreateInstance(type); int saves = 0; Action save = () => saves++;
                if (!Grant(system, 4, 1, Inventory()) || saves != 0) return false;
                return !Checkpoint(system, false, [4], save) && !Checkpoint(system, true, [], save)
                    && !Checkpoint(system, true, [5], save) && saves == 0
                    && Checkpoint(system, true, [4], save) && saves == 1 && !Checkpoint(system, true, [4], save);
            });
            Test("checkpoint-failure-remains-pending", () => {
                var system = Activator.CreateInstance(type); Grant(system, 4, 1, Inventory());
                bool failed = false;
                try { Checkpoint(system, true, [4], () => throw new InvalidOperationException("simulated snapshot failure")); }
                catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { failed = true; }
                int saves = 0;
                return failed && Checkpoint(system, true, [4], () => saves++) && saves == 1;
            });
            Test("checkpoint-per-player-and-loaded-claim", () => {
                var system = Activator.CreateInstance(type); Grant(system, 4, 1, Inventory()); Grant(system, 5, 1, Inventory());
                int saves = 0; Action save = () => saves++;
                if (!Checkpoint(system, true, [4], save) || !Checkpoint(system, true, [5], save) || saves != 2) return false;
                var saved = new ValuesDictionary(); type.GetMethod("Save").Invoke(system, [saved]);
                var loaded = Activator.CreateInstance(type); type.GetMethod("Load").Invoke(loaded, [saved]);
                return !Checkpoint(loaded, true, [4, 5], save) && !Grant(loaded, 4, 1, Inventory()) && saves == 2;
            });
            Test("creative-inventory-rejected-even-if-mode-mismatches", () => !Grant(Activator.CreateInstance(type), 1, 1, new ComponentCreativeInventory()));
        } finally {
            for (int i = 0; i < 3; i++) BlocksManager.Blocks[700 + i] = previous[i];
            BlocksManager.BlockTypeToIndex.Clear(); foreach (var p in savedTypes) BlocksManager.BlockTypeToIndex[p.Key] = p.Value;
            BlocksManager.BlockNameToIndex.Clear(); foreach (var p in savedNames) BlocksManager.BlockNameToIndex[p.Key] = p.Value;
        }
        return results;
    }
}
