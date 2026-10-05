using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

/// <summary>mp-state-consistency-20261002 (OpenSpec inventory-compatibility I1/I3, cases S01/S02/S05), against the user's
/// ACTUAL SushiBase / SushiTool assemblies (no stand-in Sushi classes): Sushi's stacking options, a stack of one gun
/// record, and Sushi's own bulk moves (store, same-items store, sort, spread) with CS guns.
/// What is real: SushiUtils.GetNewMaxStacking / SushiBaseFunction.ApplyMaxStackingTweaks, SushiSyncInventory (the
/// storage behind a sync box), SushiUtils.InventoryTransportItems / InventoryTransportSameItems / InventoryResort /
/// InventoryAverageSet, the engine's ComponentInventory, and the core's own transactions.
/// Not covered: Sushi's widgets and machines, pickables, a placed box in a world, and anything multiplayer (there is no
/// Sushi multiplayer package to test with; see docs/tasks/mp-state-consistency-plan-20261002.md, phase C).</summary>
static class SushiStackingRegression {
    internal record Result(string Name, bool Ok, string Detail);
    static T Blank<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    internal static List<Result> Run(Assembly mod, Assembly sushiBase, Assembly sushiTool) {
        var results = new List<Result>();
        void Check(string name, bool ok, string detail = "") => results.Add(new("sushi-stacking/" + name, ok, detail));
        void Test(string name, Action body) { try { body(); } catch (Exception e) { Check(name, false, (e is TargetInvocationException t ? t.InnerException : e).ToString()); } }
        Type T(string n) => mod.GetType("Game." + n, true);
        var gunType = T("ScGunBlock"); var ammoType = T("ScAmmoBlock"); var spec = T("GunSpec"); var registryType = T("ScGunRegistry"); var mutation = T("ScGunMutation");
        var transaction = T("ScInventoryTransaction"); var holders = T("ScGunHolders");
        if (gunType.GetMethod("GetMaxStacking", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) is null || transaction.GetMethod("IsGunStack") is null) {
            Check("the core limits its guns' stacking itself", false, "ScGunBlock.GetMaxStacking / ScInventoryTransaction.IsGunStack missing: an older core (its guns followed the stacking field Sushi rewrites)"); return results;
        }
        var current = registryType.GetField("Current"); object savedRegistry = current.GetValue(null);
        var locator = mutation.GetField("HolderLocator"); object savedLocator = locator.GetValue(null);
        var savedBlocks = BlocksManager.Blocks.ToArray(); var savedTypes = BlocksManager.BlockTypeToIndex.ToArray();
        var savedStacking = BlocksManager.Blocks.Where(b => b is not null).Select(b => (Block: b, b.MaxStacking)).ToArray();   // Sushi's tweak rewrites every block's field
        var utils = sushiBase.GetType("Sushi.SushiUtils", true); var settingsType = sushiBase.GetType("Sushi.SushiSettingsManager", true);
        var settings = (IDictionary)settingsType.GetField("m_values", Any).GetValue(null); var savedSettings = settings.Cast<DictionaryEntry>().ToArray();
        var source = sushiBase.GetType("Sushi.SushiSource", true);
        object savedInfo = source.GetProperty("SubsystemGameInfo").GetValue(null), savedPickables = source.GetProperty("SubsystemPickables").GetValue(null);
        try {
            // ---- blocks, as the game has them (Sushi's tweak walks the whole table)
            for (int i = 0; i < BlocksManager.Blocks.Length; i++) BlocksManager.Blocks[i] ??= new AirBlock { BlockIndex = i };
            int gun = 0, ammoIndex = 0;
            foreach (var (type, set) in new (Type, Action<int>)[] { (gunType, i => gun = i), (ammoType, i => ammoIndex = i) }) {
                int free = Enumerable.Range(700, 300).First(i => BlocksManager.Blocks[i] is AirBlock && !BlocksManager.BlockTypeToIndex.ContainsValue(i));
                var block = (Block)Activator.CreateInstance(type); block.BlockIndex = free; block.MaxStacking = type == gunType ? 1 : 40; block.IsPlaceable = false;
                BlocksManager.Blocks[free] = block; BlocksManager.BlockTypeToIndex[type] = free; set(free);
            }
            Block gunBlock = BlocksManager.Blocks[gun];
            int ak = Array.FindIndex(((Array)spec.GetField("All").GetValue(null)).Cast<object>().ToArray(), g => (string)spec.GetField("Name").GetValue(g) == "ak47");
            object akSpec = ((Array)spec.GetField("All").GetValue(null)).GetValue(ak);
            int WithId(int id) => Terrain.MakeBlockValue(gun, 0, (int)spec.GetMethod("WithId").Invoke(null, [ak, id]));
            int IdOf(int value) => (int)spec.GetMethod("GetId").Invoke(null, [Terrain.ExtractData(value)]);
            int Rounds(int value) => (int)spec.GetMethod("GetRounds").Invoke(null, [Terrain.ExtractData(value)]);
            int fresh = WithId(0);

            object registry = null;
            void NewRegistry() {
                registry = Activator.CreateInstance(registryType); current.SetValue(null, registry);
                registryType.GetField("RecoveryOwner").SetValue(registry, (Func<IInventory, string>)(_ => "player/test"));
            }
            int Allocate(int rounds, int durability = 1200) => (int)registryType.GetMethod("Allocate").Invoke(registry, [ak, rounds, false, durability, -1, 0]);
            int Next() => (int)registryType.GetProperty("Next").GetValue(registry);
            object Record(int id) => registryType.GetMethod("Get", Any).Invoke(registry, [id]);
            string Key(object inventory, int slot) => (string)holders.GetMethod("Key").Invoke(null, [inventory, slot]);
            (object Tx, string Why) Prepare(IInventory inventory, int slot) { object[] args = [inventory, slot, Key(inventory, slot), null]; return (mutation.GetMethod("Prepare").Invoke(null, args), args[3].ToString()); }
            string Commit(object tx) {
                var rt = T("ScGunRecord"); var p = System.Linq.Expressions.Expression.Parameter(rt);
                var nothing = System.Linq.Expressions.Expression.Lambda(typeof(Action<>).MakeGenericType(rt), System.Linq.Expressions.Expression.Empty(), p).Compile();
                return mutation.GetMethod("Commit").Invoke(tx, [nothing, 0, 0, null]).ToString();
            }
            ComponentInventory Player(int slots = 10) { var inv = new ComponentInventory(); for (int i = 0; i < slots; i++) inv.m_slots.Add(new()); return inv; }
            IInventory Box(int slots = 8) => (IInventory)Activator.CreateInstance(sushiTool.GetType("Sushi.SushiSyncInventory", true), [null, null, slots]);
            List<(int Value, int Count)> Items(IInventory inv) => Enumerable.Range(0, inv.SlotsCount).Where(i => inv.GetSlotCount(i) > 0).Select(i => (inv.GetSlotValue(i), inv.GetSlotCount(i))).OrderBy(x => x.Item1).ToList();
            int Guns(params IInventory[] inventories) => inventories.Sum(inv => Items(inv).Where(x => Terrain.ExtractContents(x.Value) == gun).Sum(x => x.Count));
            // Every live holder of a record across these inventories, as the gun subsystem's holder scan would report them.
            void Locate(params IInventory[] inventories) => locator.SetValue(null, (Func<int, string, IEnumerable<string>>)((id, except) => inventories
                .SelectMany(inv => Enumerable.Range(0, inv.SlotsCount).Where(i => inv.GetSlotCount(i) > 0 && Terrain.ExtractContents(inv.GetSlotValue(i)) == gun && IdOf(inv.GetSlotValue(i)) == id).Select(i => Key(inv, i)))
                .Where(k => k != except).ToArray()));
            object Sushi(string method, params object[] args) => utils.GetMethods(BindingFlags.Public | BindingFlags.Static).Single(m => m.Name == method && m.GetParameters().Length == args.Length).Invoke(null, args);

            // (Sushi's sort reads the game mode from its own static; no pickables subsystem unless a check sets one.)
            var world = Blank<WorldSettings>(); world.GameMode = GameMode.Survival; var info = Blank<SubsystemGameInfo>(); info.WorldSettings = world;
            source.GetProperty("SubsystemGameInfo").SetValue(null, info); source.GetProperty("SubsystemPickables").SetValue(null, null);

            // ---- S01: Sushi's stacking options
            int before = (int)utils.GetMethod("GetNewMaxStacking").Invoke(null, [gunBlock, null]);
            settings["AdjustMaxStacking"] = true; settings["AdjustNoPlaceableOneStacking"] = true; settings["MaxStackingNumberSmall"] = 1;
            sushiBase.GetType("Sushi.SushiBaseFunction", true).GetMethod("ApplyMaxStackingTweaks").Invoke(null, null);
            int raised = gunBlock.MaxStacking;
            Check("S01 the condition: with Sushi's two stacking options on, its load-time tweak rewrites the CS gun block's stacking field (1 -> 4)", before == 1 && raised == 4 && gunBlock.Durability < 0 && !gunBlock.IsPlaceable, $"options off {before}, field after the tweak {raised}");
            NewRegistry();
            int a = Allocate(7), instance = WithId(a);
            var player = Player();
            Check("S01 the CS gun block answers 1 whatever the field says, so the player's inventory, chests and crafting keep guns apart (an instance and a fresh gun alike)",
                gunBlock.GetMaxStacking(instance) == 1 && gunBlock.GetMaxStacking(fresh) == 1 && player.GetSlotCapacity(0, instance) == 1 && player.GetSlotCapacity(0, fresh) == 1,
                $"block {gunBlock.GetMaxStacking(instance)}/{gunBlock.GetMaxStacking(fresh)}, ComponentInventory capacity {player.GetSlotCapacity(0, instance)}/{player.GetSlotCapacity(0, fresh)}");
            var box = Box();
            int boxCapacity = box.GetSlotCapacity(0, instance);
            // Sushi's own containers compute their capacity after the block's answer (GetNewMaxStacking: an answer of 1 on a
            // non-placeable block without vanilla durability is raised again). This is Sushi's to change, not this mod's;
            // the check pins what the present Sushi build does, so a different build is noticed.
            Check("S01 KNOWN GAP (provider-controlled): Sushi's own storage still takes 4 identical CS guns in one slot", boxCapacity == 4, $"SushiSyncInventory.GetSlotCapacity = {boxCapacity}");
            var status = T("ScSushiCompatibility").GetMethod("Read").Invoke(null, null);
            string described = (string)T("ScSushiCompatibility").GetMethod("Describe").Invoke(null, [status]);
            Check("S01 the capability record says so: Sushi loaded, both mappings verified, stacking raised", (bool)status.GetType().GetProperty("Present").GetValue(status) && (bool)status.GetType().GetProperty("PersonBoxMapped").GetValue(status)
                && (bool)status.GetType().GetProperty("SyncBoxMapped").GetValue(status) && (bool)status.GetType().GetProperty("StackingRaised").GetValue(status), described);

            // ---- S02: a stack of one record inside Sushi's storage
            Test("S02 stack", () => {
                var record = Record(a); var rt = record.GetType();
                var killCount = rt.GetField("KillCount");
                rt.GetField("CounterInstalled").SetValue(record, true); killCount.SetValue(record, Convert.ChangeType(37, killCount.FieldType));
                box.AddSlotItems(0, instance, 1); box.AddSlotItems(0, instance, 1);          // two copies of one gun dropped onto each other
                Locate(box);
                var (tx, why) = Prepare(box, 0);
                string told = (string)mutation.GetMethod("Explain").Invoke(null, [System.Enum.Parse(T("ScGunResult"), "Stacked")]);
                Check("S02 a slot holding two guns of one record cannot be used and is left whole: refused as 'stacked', with a sentence that says what to do",
                    tx is null && why == "Stacked" && (bool)transaction.GetMethod("IsGunStack").Invoke(null, [box, 0]) && !(bool)transaction.GetMethod("IsWeaponSlot").Invoke(null, [box, 0])
                    && box.GetSlotCount(0) == 2 && box.GetSlotValue(0) == instance && Rounds(instance) == 7 && told.Contains("分到不同"), $"{why}; \"{told}\"");
                int next = Next();
                box.RemoveSlotItems(0, 1); box.AddSlotItems(1, instance, 1);                   // the player spreads them out
                (tx, why) = Prepare(box, 1);
                string result = tx is null ? why : Commit(tx);
                int separated = box.GetSlotValue(1);
                var copy = Record(IdOf(separated));
                Check("S02 spread out, the second copy gets its own record on first use; the original keeps its number, rounds and kills; nothing is lost", result == "Success" && IdOf(separated) == next && IdOf(box.GetSlotValue(0)) == a
                    && box.GetSlotCount(0) == 1 && box.GetSlotCount(1) == 1 && Rounds(box.GetSlotValue(0)) == 7 && Rounds(separated) == 7
                    && Convert.ToInt64(rt.GetField("KillCount").GetValue(record)) == 37 && Convert.ToInt64(rt.GetField("KillCount").GetValue(copy)) == 0,
                    $"{result}; ids {IdOf(box.GetSlotValue(0))}/{IdOf(separated)}, kills {rt.GetField("KillCount").GetValue(record)}/{rt.GetField("KillCount").GetValue(copy)}");
                // no number left in the world's table
                while (!(bool)registryType.GetProperty("IsFull").GetValue(registry)) Allocate(30);
                var full = Box(); full.AddSlotItems(0, instance, 1); full.AddSlotItems(0, instance, 1);
                Locate(full);
                var (stacked, stackedWhy) = Prepare(full, 0);
                full.RemoveSlotItems(0, 1); full.AddSlotItems(1, instance, 1);
                (tx, why) = Prepare(full, 1);
                result = tx is null ? why : Commit(tx);
                Check("S02 with no free record number the copies are kept as they are (refused, nothing truncated or overwritten)", stacked is null && stackedWhy == "Stacked" && result == "DuplicateUnresolved"
                    && full.GetSlotValue(0) == instance && full.GetSlotValue(1) == instance && full.GetSlotCount(0) == 1 && full.GetSlotCount(1) == 1 && Rounds(instance) == 7, result);
            });

            // ---- S02/S05: Sushi's bulk moves with two guns of the same model and name
            Test("S05 bulk moves", () => {
                NewRegistry();
                int one = WithId(Allocate(7, 900)), two = WithId(Allocate(21, 1100)), magazines = Terrain.MakeBlockValue(ammoIndex, 0, 0);
                var bag = Player(); var chest = Box();
                bag.AddSlotItems(0, one, 1); bag.AddSlotItems(3, two, 1); bag.AddSlotItems(5, magazines, 20);
                Locate(bag, chest);
                int next = Next(); var all = Items(bag);
                bool Intact(string step) {
                    var now = Items(bag).Concat(Items(chest)).OrderBy(x => x.Value).ToList();
                    bool same = now.SequenceEqual(all) && Next() == next && Rounds(one) == 7 && Rounds(two) == 21 && Guns(bag, chest) == 2;
                    // every record is held exactly once, so the next use of either gun separates nothing
                    foreach (var inv in new IInventory[] { bag, chest }) for (int i = 0; i < inv.SlotsCount; i++) {
                        if (inv.GetSlotCount(i) == 0 || Terrain.ExtractContents(inv.GetSlotValue(i)) != gun) continue;
                        var (tx, why) = Prepare(inv, i);
                        same &= tx is not null && Commit(tx) == "Success" && IdOf(inv.GetSlotValue(i)) is var id && (id == IdOf(one) || id == IdOf(two));
                    }
                    if (!same) throw new InvalidOperationException($"{step}: items {string.Join(",", now)} (expected {string.Join(",", all)}), next {Next()}/{next}, rounds {Rounds(one)}/{Rounds(two)}");
                    return same && Next() == next;
                }
                Sushi("InventoryTransportItems", bag, chest);
                bool stored = Intact("store all") && Items(bag).Count == 0;
                Sushi("InventoryResort", chest);
                bool sorted = Intact("sort");
                Sushi("InventoryTransportItems", chest, bag);
                bool taken = Intact("take all") && Items(chest).Count == 0;
                Check("S05 Sushi's store-all, sort and take-all move both same-named guns whole: each keeps its own number, rounds and wear, counts are conserved, no number is used", stored && sorted && taken,
                    $"store {stored}, sort {sorted}, take {taken}; next id {Next()}");
                // "same items" is by display name in Sushi: one gun in the box draws the other same-named gun in, as a separate item
                chest.AddSlotItems(0, one, 1); bag.RemoveSlotItems(Enumerable.Range(0, bag.SlotsCount).First(i => bag.GetSlotValue(i) == one && bag.GetSlotCount(i) > 0), 1);
                string sameItems;
                try { Sushi("InventoryTransportSameItems", bag, chest); sameItems = "ran"; } catch (Exception e) { sameItems = "threw " + (e.InnerException ?? e).GetType().Name + ": " + (e.InnerException ?? e).Message; }
                bool separate = Intact("same-items store");
                Check("S05 Sushi's same-items store never merges two guns by their name: they stay two items with their own records", separate && Items(chest).Concat(Items(bag)).Where(x => Terrain.ExtractContents(x.Value) == gun).All(x => x.Count == 1), $"{sameItems}; box holds {string.Join(",", Items(chest).Select(x => IdOf(x.Value) + "x" + x.Count))}");
                // spreading evenly needs one kind of item: two different guns are refused by Sushi before it touches anything
                var pair = Box(4); pair.AddSlotItems(0, one, 1); pair.AddSlotItems(1, two, 1); var pairBefore = Items(pair);
                try { Sushi("InventoryAverageSet", pair, 0); } catch (TargetInvocationException) { /* its message needs a player's GUI; the refusal comes first */ }
                Check("S05 Sushi's even spread leaves two different guns as they are", Items(pair).SequenceEqual(pairBefore));
            });

            // ---- a stack saved while guns still followed the field: sorting it with the lower capacity
            Test("S02 older stacks", () => {
                NewRegistry();
                var bag = Player(4); bag.m_slots[0].Value = fresh; bag.m_slots[0].Count = 3;      // three fresh guns in one slot, from before
                // (With a pickables subsystem Sushi's sort re-adds by the present capacity and would drop what does not fit at
                // the first player's feet; here everything fits. Without one it refuses to sort a slot above capacity.)
                source.GetProperty("SubsystemPickables").SetValue(null, Blank<SubsystemPickables>());
                Sushi("InventoryResort", bag);
                bool spread = Guns(bag) == 3 && Items(bag).All(x => x.Value == fresh && x.Count == 1);
                source.GetProperty("SubsystemPickables").SetValue(null, null);
                var tight = Player(2); tight.m_slots[0].Value = fresh; tight.m_slots[0].Count = 3;
                Sushi("InventoryResort", tight);
                Check("S02 a stack of fresh guns from before is not truncated: sorted into separate slots where there is room, left whole where there is not", spread && tight.GetSlotCount(0) == 3 && tight.GetSlotValue(0) == fresh && Guns(tight) == 3,
                    $"with room {string.Join(",", Items(bag).Select(x => x.Count))}; without {string.Join(",", Items(tight).Select(x => x.Count))}");
                var (tx, why) = Prepare(tight, 0);
                Check("S02 and that stack is refused as 'stacked' until spread out", tx is null && why == "Stacked");
            });

            // ---- S05: another item put into the hand while a reload is in progress
            Test("S05 reload", () => {
                NewRegistry();
                int one = WithId(Allocate(3)), two = WithId(Allocate(9)), magazines = Terrain.MakeBlockValue(ammoIndex, 0, 0);
                var bag = Player(); bag.AddSlotItems(0, one, 1); bag.AddSlotItems(1, magazines, 5); bag.ActiveSlotIndex = 0;
                Locate(bag);
                int cost = (int)T("ScReloadTransaction").GetMethod("Required").Invoke(null, [akSpec]), capacity = (int)spec.GetField("Magazine").GetValue(akSpec);
                var reload = Activator.CreateInstance(T("ScReloadTransaction"), [bag, 0, one, magazines, cost, capacity, Key(bag, 0)]);
                bool valid = (bool)reload.GetType().GetProperty("Valid").GetValue(reload);
                bag.RemoveSlotItems(0, 1); bag.AddSlotItems(0, two, 1);                           // an automatic refill replaces the gun in hand
                bool still = (bool)reload.GetType().GetProperty("Valid").GetValue(reload);
                bool discard = (bool)reload.GetType().GetMethod("Discard").Invoke(reload, null), insert = (bool)reload.GetType().GetMethod("InsertMagazine").Invoke(reload, null);
                Check("S05 a reload started for one gun does nothing to the gun that replaced it in the hand: no magazine taken, neither record changed", valid && !still && !discard && !insert && bag.GetSlotCount(1) == 5 && Rounds(two) == 9 && Rounds(one) == 3,
                    $"valid before {valid}, after {still}; discard {discard}, insert {insert}; magazines {bag.GetSlotCount(1)}, rounds {Rounds(one)}/{Rounds(two)}");
            });
        }
        catch (Exception e) { Check("fixture", false, (e is TargetInvocationException t ? t.InnerException : e).ToString()); }
        finally {
            current.SetValue(null, savedRegistry); locator.SetValue(null, savedLocator);
            settings.Clear(); foreach (var entry in savedSettings) settings[entry.Key] = entry.Value;
            source.GetProperty("SubsystemGameInfo").SetValue(null, savedInfo); source.GetProperty("SubsystemPickables").SetValue(null, savedPickables);
            BlocksManager.BlockTypeToIndex.Clear(); foreach (var pair in savedTypes) BlocksManager.BlockTypeToIndex[pair.Key] = pair.Value;
            Array.Copy(savedBlocks, BlocksManager.Blocks, savedBlocks.Length);
            foreach (var (block, stacking) in savedStacking) block.MaxStacking = stacking;
        }
        return results;
    }
}
