using Engine;
using GameEntitySystem;
using System.Reflection;
using System.Runtime.CompilerServices;
using TemplatesDatabase;
namespace Game;

/// <summary>Regression cases for the five 0.36.1 review failures, using the shipped transaction/recovery
/// implementations. Faults may happen both before and after the inventory changes.</summary>
public static class ScGunRecoverySelfTest {
    sealed class Inventory : IInventory {
        public Project Project => null;
        public int SlotsCount => 8;
        public int VisibleSlotsCount { get; set; } = 8;
        public int ActiveSlotIndex { get; set; }
        public int[] Values = new int[8], Counts = new int[8];
        public int ThrowRemove = -1, ThrowAdd = -1, PartialAdd = -1;
        public bool RemoveThenThrow, AddThenThrow, RefuseAll;
        public Action OnAdd;
        public int GetSlotValue(int i) => Values[i];
        public int GetSlotCount(int i) => Counts[i];
        public int GetSlotCapacity(int i, int value) => i == 0 ? 1 : 40;
        public int GetSlotProcessCapacity(int i, int v) => 0;
        public void AddSlotItems(int i, int value, int count) {
            if (i == ThrowAdd && !AddThenThrow) throw new InvalidOperationException("injected Add before mutation");
            if (RefuseAll) return;
            if (Counts[i] > 0 && Values[i] != value) throw new InvalidOperationException("mixed slot");
            Values[i] = value; Counts[i] += i == PartialAdd ? Math.Min(count, 1) : count;
            OnAdd?.Invoke();
            if (i == ThrowAdd && AddThenThrow) throw new InvalidOperationException("injected Add after mutation");
        }
        public int RemoveSlotItems(int i, int count) {
            if (i == ThrowRemove && !RemoveThenThrow) throw new InvalidOperationException("injected Remove before mutation");
            int taken = Math.Min(count, Counts[i]); Counts[i] -= taken;
            if (i == ThrowRemove) throw new InvalidOperationException("injected Remove after mutation");
            return taken;
        }
        public void ProcessSlotItems(int i, int v, int count, int process, out int result, out int resultCount) { result = v; resultCount = 0; }
        public void DropAllItems(Vector3 p) => Array.Clear(Counts);
        public int Total(int value) => Enumerable.Range(0, SlotsCount).Where(i => Values[i] == value).Sum(i => Counts[i]);
    }
    static T Blank<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    public static void Run(Action<string, bool, string> check) {
        var saved = ScGunRegistry.Current; var locator = ScGunMutation.HolderLocator;
        void Test(string name, Func<bool> run) {
            ScGunRegistry.Current = new(); ScGunMutation.HolderLocator = null;
            try { bool ok = run(); check("m4-recovery/" + name, ok, name); }
            catch (Exception e) { check("m4-recovery/" + name, false, e.ToString()); }
        }
        Inventory Fresh(int rounds = 0) { var inv = new Inventory(); inv.AddSlotItems(0, Terrain.MakeBlockValue(512, 0, GunSpec.MakeData(0, rounds)), 1); inv.AddSlotItems(1, 900, 2); return inv; }
        ScGunMutation Prepare(Inventory inv) => ScGunMutation.Prepare(inv, 0, "player/0", out _) ?? throw new InvalidOperationException("prepare refused");
        try {
            foreach (bool after in new[] { false, true }) foreach (bool add in new[] { false, true }) {
                bool a = after, insertion = add;
                Test($"ammo-and-gun-{(add ? "Add" : "Remove")}-{(after ? "after" : "before")}-throw", () => {
                    var inv = Fresh(); int gun = inv.Values[0];
                    if (insertion) { inv.ThrowAdd = 0; inv.AddThenThrow = a; }
                    else { inv.ThrowRemove = 0; inv.RemoveThenThrow = a; }
                    var outcome = Prepare(inv).Commit(r => r.Rounds = 30, 900, 1);
                    return outcome == ScGunResult.InventoryRejected && inv.Total(gun) == 1 && inv.Total(900) == 2
                        && ScGunRegistry.Current.Count == 0 && ScGunRegistry.Current.Recovery.Count == 0;
                });
            }
            Test("partial-return-no-duplication", () => {
                var inv = Fresh(); inv.AddSlotItems(2, 950, 2); inv.AddSlotItems(3, 951, 1); inv.PartialAdd = 2; inv.ThrowRemove = 3;
                var result = Prepare(inv).Commit(r => r.Rounds = 30, materials: new Dictionary<int, int> { [950] = 2, [951] = 1 });
                return result == ScGunResult.InventoryRejected && inv.Total(950) == 2 && inv.Total(951) == 1 && ScGunRegistry.Current.Recovery.Count == 0;
            });
            Test("compensation-save-reload-partial-retry-once", () => {
                var registry = ScGunRegistry.Current; var inv = Fresh(); inv.AddSlotItems(2, 950, 2); inv.AddSlotItems(3, 951, 1);
                inv.RefuseAll = true; inv.ThrowRemove = 3;
                if (Prepare(inv).Commit(r => r.Rounds = 30, materials: new Dictionary<int, int> { [950] = 2, [951] = 1 }) != ScGunResult.RecoveryPending) return false;
                if (registry.Recovery.Count != 1 || inv.Total(950) != 0) return false;
                var snapshot = registry.Save(0);
                var restored = ScGunRegistry.Load(snapshot, 0);
                // A real reopen has different inventory object identity but the same stable owner.
                var target = new Inventory { Values = (int[])inv.Values.Clone(), Counts = (int[])inv.Counts.Clone() };
                restored.Recovery.Retry(_ => null); if (restored.Recovery.Count != 1) return false;
                int completed = restored.Recovery.Retry(owner => owner == "player/0" ? target : null);
                int again = restored.Recovery.Retry(_ => target);
                var roundTrip = ScGunRegistry.Load(restored.Save(0), 0);
                return completed == 1 && again == 0 && target.Total(950) == 2 && target.Total(951) == 1 && roundTrip.Recovery.Count == 0
                    && ScGunRegistry.Load(snapshot, 0).Recovery.Count == 1; // immutable prior snapshot
            });
            Test("compensation-partial-progress-persists", () => {
                var recovery = ScGunRegistry.Current.Recovery;
                recovery.Enqueue("player/0", [new ScGunUndo { Slot = 2, Value = 950, Count = 3 }]);
                var inv = new Inventory { PartialAdd = 2 };
                // Occupy every fallback slot so only one item can arrive this attempt.
                for (int i = 0; i < 8; i++) if (i != 2) inv.AddSlotItems(i, 901, 1);
                if (recovery.Retry(_ => inv) != 0 || inv.Total(950) != 1 || recovery.Batches.Single().Steps[0].Count != 2) return false;
                var loaded = ScGunRegistry.Load(ScGunRegistry.Current.Save(0), 0);
                inv.PartialAdd = -1;
                return loaded.Recovery.Retry(_ => inv) == 1 && inv.Total(950) == 3 && loaded.Recovery.Retry(_ => inv) == 0;
            });
            Test("compensation-inserted-replacement-first", () => {
                var inv = Fresh(); int gun = inv.Values[0], attempted = Terrain.ReplaceData(gun, GunSpec.WithId(0, 1));
                inv.OnAdd = () => { inv.ThrowRemove = 0; throw new InvalidOperationException("after insertion"); };
                var result = Prepare(inv).Commit(r => r.Rounds = 30, 900, 1);
                if (result != ScGunResult.RecoveryPending || inv.Total(attempted) != 1 || inv.Total(gun) != 0) return false;
                inv.OnAdd = null; inv.ThrowRemove = -1;
                return ScGunRegistry.Current.Recovery.Retry(_ => inv) == 1 && inv.Total(attempted) == 0 && inv.Total(gun) == 1 && inv.Total(900) == 2;
            });
            Test("world-switch-refuses-prepared-transaction", () => {
                var inv = Fresh(); var tx = Prepare(inv); ScGunRegistry.Current = new();
                return tx.Commit(r => r.Rounds = 30, 900, 1) == ScGunResult.Foreign && inv.Total(900) == 2 && ScGunRegistry.Current.Count == 0;
            });
            Test("reject-save-inside-commit", () => {
                var inv = Fresh(); bool refused = false;
                inv.OnAdd = () => { try { ScGunRegistry.Current.Save(0); } catch (InvalidOperationException) { refused = true; } };
                return Prepare(inv).Commit(r => r.Rounds = 30) == ScGunResult.Success && refused && ScGunRegistry.Current.Save(0) is not null;
            });
            Test("existing-record-no-remove-add", () => {
                var inv = Fresh(3); int gun = inv.Values[0]; inv.ThrowRemove = 0; inv.ThrowAdd = 0;
                return Prepare(inv).Commit(r => r.Rounds = 30, 900, 1) == ScGunResult.Success && inv.Values[0] == gun && inv.Total(900) == 1 && GunSpec.GetRounds(Terrain.ExtractData(gun)) == 30;
            });
            Test("runtime-engine-scan-same-frame-copy", () => {
                var project = Blank<Project>(); project.m_subsystems = []; project.m_entities = [];
                var scanner = new SubsystemItemsScanner { m_project = project };
                project.m_subsystems.AddRange([scanner, new SubsystemPickables(), new SubsystemProjectiles(), new SubsystemMovingBlocks()]);
                var a = new ComponentInventory(); var b = new ComponentInventory();
                for (int i = 0; i < 2; i++) { a.m_slots.Add(new()); b.m_slots.Add(new()); }
                int id = ScGunRegistry.Current.Allocate(0, 29, false, 1499), value = Terrain.MakeBlockValue(512, 0, GunSpec.WithId(0, id));
                a.m_slots[0].Value = value; a.m_slots[0].Count = 1;
                var e1 = Blank<Entity>(); e1.Id = 10; e1.m_project = project; e1.m_components = [a];
                var e2 = Blank<Entity>(); e2.Id = 11; e2.m_project = project; e2.m_components = [b];
                project.m_entities[e1] = true; project.m_entities[e2] = true;
                var subsystem = new SubsystemScGunBlockBehavior { m_project = project };
                typeof(SubsystemScGunBlockBehavior).GetField("m_registry", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(subsystem, ScGunRegistry.Current);
                var method = typeof(SubsystemScGunBlockBehavior).GetMethod("Holders", BindingFlags.Instance | BindingFlags.NonPublic);
                var gunType = typeof(ScGunBlock); bool had = BlocksManager.BlockTypeToIndex.TryGetValue(gunType, out int old); var oldBlock = BlocksManager.Blocks[512];
                try {
                    BlocksManager.BlockTypeToIndex[gunType] = 512; BlocksManager.Blocks[512] = new ScGunBlock { BlockIndex = 512, MaxStacking = 1 };
                    List<ScGunHolders.Holder> Scan() => (List<ScGunHolders.Holder>)method.Invoke(subsystem, null);
                    if (Scan().Count != 1) return false;
                    b.m_slots[0].Value = value; b.m_slots[0].Count = 1; ScInventoryTransaction.Changed(b);
                    if (Scan().Count != 2) return false; // same Time.FrameIndex, no newly published id
                    ScGunMutation.HolderLocator = (rid, except) => Scan().Where(h => h.Id == rid && h.Key != except).Select(h => h.Key).ToArray();
                    var tx = ScGunMutation.Prepare(a, 0, ScGunHolders.Key(a, 0), out _);
                    if (tx.Commit(r => { r.Rounds--; r.Durability--; }) != ScGunResult.Success) return false;
                    string owner = ScGunHolders.RecoveryOwner(project, b);
                    return GunSpec.GetId(Terrain.ExtractData(a.GetSlotValue(0))) != id && GunSpec.GetDurability(Terrain.ExtractData(b.GetSlotValue(0))) == 1499
                        && ReferenceEquals(ScGunHolders.ResolveRecoveryOwner(project, owner), b);
                }
                finally { if (had) BlocksManager.BlockTypeToIndex[gunType] = old; else BlocksManager.BlockTypeToIndex.Remove(gunType); BlocksManager.Blocks[512] = oldBlock; }
            });
            Test("holder-identities-independent-of-hash-collision", () => {
                var byHash = new Dictionary<int, object>(); var keys = new HashSet<string>(); bool sawCollision = false;
                for (int i = 0; i < 100000; i++) {
                    var obj = new object(); int hash = RuntimeHelpers.GetHashCode(obj); string key = ScGunHolders.Key(obj, 0);
                    if (!keys.Add(key) || key != ScGunHolders.Key(obj, 0) || key == ScGunHolders.Key(obj, 1)) return false;
                    if (byHash.TryGetValue(hash, out var other)) {
                        sawCollision = true;
                        if (ScGunHolders.Key(obj, 0) == ScGunHolders.Key(other, 0)) return false;
                        break;
                    }
                    byHash[hash] = obj;
                }
                // Hash collisions are runtime-dependent; uniqueness is checked for every live object either way.
                return sawCollision || keys.Count == 100000;
            });
        }
        finally { ScGunRegistry.Current = saved; ScGunMutation.HolderLocator = locator; }
    }
}
