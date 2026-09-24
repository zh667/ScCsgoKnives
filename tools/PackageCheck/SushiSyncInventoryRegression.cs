using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

// Actual installed third-party types only. Sparse keys, real backing storage and real engine scanner.
static class SushiSyncInventoryRegression {
    internal static List<SushiInventoryRegression.Result> Run(Assembly mod, Assembly sushiBase, Assembly sushiTool) {
        List<SushiInventoryRegression.Result> results = [];
        void Check(string name, Func<bool> test) {
            try { results.Add(new("sushi-sync/" + name, test(), "")); }
            catch (Exception e) { results.Add(new("sushi-sync/" + name, false, e.GetBaseException().ToString())); }
        }
        Type T(string n) => mod.GetType("Game." + n, true);
        object Call(string t, string m, params object[] a) => T(t).GetMethod(m).Invoke(null, a);
        var registryType = T("ScGunRegistry"); var current = registryType.GetField("Current");
        var mutation = T("ScGunMutation"); var locator = mutation.GetField("HolderLocator");
        object saved = current.GetValue(null), oldLocator = locator.GetValue(null);
        var oldTypes = BlocksManager.BlockTypeToIndex.ToArray(); var oldGun = BlocksManager.Blocks[512]; var oldAmmo = BlocksManager.Blocks[900];
        object Blank(Type t) => RuntimeHelpers.GetUninitializedObject(t);
        var syncType = sushiTool.GetType("Sushi.SubsystemSushiSyncBox", true);
        var boxType = sushiTool.GetType("Sushi.ComponentSushiSyncBox", true);
        var inventoryType = sushiTool.GetType("Sushi.SushiSyncInventory", true);
        Project Project() { var p = (Project)Blank(typeof(Project)); p.m_subsystems = []; p.m_entities = []; return p; }
        var project = Project();
        var subsystem = (Subsystem)Blank(syncType); subsystem.m_project = project; project.m_subsystems.Add(subsystem);
        var channelsField = syncType.GetField("SushiSyncInventories");
        var channels = (IDictionary)Activator.CreateInstance(channelsField.FieldType); channelsField.SetValue(subsystem, channels);
        IInventory Native(ValuesDictionary data = null) => (IInventory)Activator.CreateInstance(inventoryType, [project, data, 30]);
        IInventory Box(int channel, int entityId) {
            var box = (Component)Blank(boxType); boxType.GetField("m_subsystemSushiSyncBox").SetValue(box, subsystem); boxType.GetField("channelIndex").SetValue(box, channel);
            var entity = (Entity)Blank(typeof(Entity)); entity.Id = entityId; entity.m_project = project; entity.m_components = [box]; box.m_entity = entity; project.m_entities[entity] = true;
            return (IInventory)box;
        }
        var shared = Native(); var other = Native(); channels.Add(0, shared); channels.Add(7, other);
        var a = Box(0, 10); var b = Box(0, 11); var c = Box(7, 12);
        string Key(IInventory inv, int slot = 0) => (string)Call("ScGunHolders", "Key", inv, slot);
        string Owner(IInventory inv) => (string)Call("ScGunHolders", "RecoveryOwner", project, inv);
        IInventory Resolve(string owner) => (IInventory)Call("ScGunHolders", "ResolveRecoveryOwner", project, owner);
        object[] Scan() => ((IEnumerable)Call("ScGunHolders", "Scan", project, 512)).Cast<object>().ToArray();
        int HolderId(object h) => (int)h.GetType().GetProperty("Id").GetValue(h);
        string HolderKey(object h) => (string)h.GetType().GetProperty("Key").GetValue(h);
        object registry = null;
        int Next() => (int)registryType.GetProperty("Next").GetValue(registry);
        void NewRegistry() {
            registry = Activator.CreateInstance(registryType); current.SetValue(null, registry);
            registryType.GetField("RecoveryOwner").SetValue(registry, (Func<IInventory, string>)Owner);
            locator.SetValue(null, (Func<int, string, IEnumerable<string>>)((id, except) => Scan().Where(h => HolderId(h) == id && HolderKey(h) != except).Select(HolderKey).ToArray()));
        }
        int Allocate() => (int)registryType.GetMethod("Allocate").Invoke(registry, [0, 19, false, 700, 2250, 0]);
        int Value(int id) => Terrain.MakeBlockValue(512, 0, (int)Call("GunSpec", "WithId", 0, id));
        object Record(int id) => registryType.GetMethod("Get", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(registry, [id]);
        object Prepare(IInventory inv, out string why) {
            object[] args = [inv, 0, Key(inv), null]; var tx = mutation.GetMethod("Prepare").Invoke(null, args); why = args[3].ToString(); return tx;
        }
        var rt = T("ScGunRecord"); var p = Expression.Parameter(rt);
        var noop = Expression.Lambda(typeof(Action<>).MakeGenericType(rt), Expression.Empty(), p).Compile();
        string Commit(object tx) => mutation.GetMethod("Commit").Invoke(tx, [noop, 0, 0, null]).ToString();
        void Put(IInventory inv, int value, int slot = 0) { inv.RemoveSlotItems(slot, int.MaxValue); inv.AddSlotItems(slot, value, 1); }
        try {
            var gun = (Block)Activator.CreateInstance(T("ScGunBlock")); gun.BlockIndex = 512; gun.MaxStacking = 1;
            BlocksManager.BlockTypeToIndex[T("ScGunBlock")] = 512; BlocksManager.Blocks[512] = gun;
            BlocksManager.Blocks[900] = new DirtBlock { MaxStacking = 100 };
            NewRegistry(); int id = Allocate(); Put(shared, Value(id));
            Check("same-channel-actual-backing-and-holder", () => ReferenceEquals(Call("ScInventoryIdentity", "Storage", a), shared) && Key(a) == Key(b));
            Check("sparse-channel-seven-not-list-index", () => ReferenceEquals(Call("ScInventoryIdentity", "Storage", c), other) && Key(c) != Key(a));
            Check("shared-slot-write-visible-not-two-guns", () => { a.RemoveSlotItems(0, 1); a.AddSlotItems(0, Value(id), 1); return b.GetSlotValue(0) == Value(id) && shared.GetSlotCount(0) == 1; });
            Check("same-storage-shares-transaction-revision", () => { Call("ScInventoryTransaction", "Changed", a); return Equals(Call("ScInventoryTransaction", "Revision", a), Call("ScInventoryTransaction", "Revision", b)) && Equals(Call("ScInventoryTransaction", "Revision", b), Call("ScInventoryTransaction", "Revision", shared)); });
            Check("scanner-one-holder-for-two-proxies", () => Scan().Length == 1);
            Check("scanner-includes-channel-without-any-box", () => { var hidden = Native(); channels.Add(12, hidden); Put(hidden, Value(id)); bool ok = Scan().Any(h => ReferenceEquals(h.GetType().GetProperty("Inventory").GetValue(h), hidden)); channels.Remove(12); return ok; });
            Check("scanner-no-box-entities-still-finds-stored-gun", () => {
                var entities = project.m_entities.ToArray(); project.m_entities.Clear();
                try { return Scan().Length == 1; } finally { foreach (var entry in entities) project.m_entities.Add(entry.Key, entry.Value); }
            });
            Check("recovery-owner-shared-and-resolves-to-backing", () => Owner(a) != null && Owner(a) == Owner(b) && Owner(b) == Owner(shared) && ReferenceEquals(Resolve(Owner(a)), shared));
            Check("210-real-proxy-commits-no-id-or-growth-loss", () => {
                NewRegistry(); int rid = Allocate(); Put(shared, Value(rid)); var r = Record(rid);
                rt.GetField("CounterInstalled").SetValue(r, true); rt.GetField("KillCount").SetValue(r, 19L);
                for (int i = 0; i < 210; i++) { var tx = Prepare(i % 2 == 0 ? a : b, out _); if (tx == null || Commit(tx) != "Success") return false; }
                return Next() == 2 && shared.GetSlotValue(0) == Value(rid) && (long)rt.GetField("KillCount").GetValue(r) == 19L && (int)rt.GetField("Durability").GetValue(r) == 700;
            });
            Check("retune-before-commit-rejected", () => {
                var tx = Prepare(a, out _); Put(other, shared.GetSlotValue(0)); boxType.GetField("channelIndex").SetValue(a, 7);
                try { return tx != null && Commit(tx) == "StateChanged"; } finally { boxType.GetField("channelIndex").SetValue(a, 0); other.RemoveSlotItems(0, 1); }
            });
            Check("retune-during-commit-rolls-back-original-record", () => {
                var tx = Prepare(a, out _); int rid = (int)Call("GunSpec", "GetId", Terrain.ExtractData(shared.GetSlotValue(0))); var r = Record(rid); int revision = (int)rt.GetField("Revision").GetValue(r);
                mutation.GetField("AfterRecordWrite", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(tx, (Action)(() => boxType.GetField("channelIndex").SetValue(a, 7)));
                try { return Commit(tx) == "InventoryRejected" && (int)rt.GetField("Revision").GetValue(r) == revision && other.GetSlotCount(0) == 0; }
                finally { boxType.GetField("channelIndex").SetValue(a, 0); }
            });
            Check("full-table-sole-old-gun-remains-usable", () => {
                while (Next() <= 1022) Allocate(); var tx = Prepare(a, out _); return tx != null && Commit(tx) == "Success" && Next() == 1023;
            });
            Check("full-table-real-independent-copy-refused", () => {
                Put(other, shared.GetSlotValue(0)); try { var tx = Prepare(a, out _); return tx != null && Commit(tx) == "DuplicateUnresolved" && Next() == 1023; }
                finally { other.RemoveSlotItems(0, 1); }
            });
            Check("full-table-fresh-gun-refused-without-item-loss", () => {
                int original = shared.GetSlotValue(0); Put(shared, Value(0)); try { var tx = Prepare(a, out _); return tx != null && Commit(tx) == "RegistryFull" && shared.GetSlotValue(0) == Value(0) && shared.GetSlotCount(0) == 1; }
                finally { Put(shared, original); }
            });
            for (int round = 0; round < 2; round++) Check("native-inventory-and-registry-xml-roundtrip-" + round, () => {
                ValuesDictionary Round(ValuesDictionary input) { var xml = new XElement("Values"); input.Save(xml); var output = new ValuesDictionary(); output.ApplyOverrides(XElement.Parse(xml.ToString())); return output; }
                var invData = (ValuesDictionary)inventoryType.GetMethod("Save").Invoke(shared, null);
                var newInv = Native(Round(invData)); int value = shared.GetSlotValue(0);
                var data = (ValuesDictionary)registryType.GetMethod("Save").Invoke(registry, [0d]);
                registry = registryType.GetMethod("Load").Invoke(null, [Round(data), 0d]); current.SetValue(null, registry); registryType.GetField("RecoveryOwner").SetValue(registry, (Func<IInventory, string>)Owner);
                shared = newInv; channels[0] = shared; int rid = (int)Call("GunSpec", "GetId", Terrain.ExtractData(value)); var r = Record(rid);
                return a.GetSlotValue(0) == value && b.GetSlotValue(0) == value && Next() == 1023 && Scan().Length == 1 && (long)rt.GetField("KillCount").GetValue(r) == 19 && (int)rt.GetField("Rounds").GetValue(r) == 19;
            });
            Check("refund-never-follows-box-channel-switch", () => {
                string owner = Owner(a); boxType.GetField("channelIndex").SetValue(a, 7);
                try { return ReferenceEquals(Resolve(owner), shared); } finally { boxType.GetField("channelIndex").SetValue(a, 0); }
            });
            Check("recreated-channel-does-not-inherit-old-refund", () => {
                string owner = Owner(a); channels[0] = Native(); try { return Resolve(owner) == null; } finally { channels[0] = shared; }
            });
            Check("same-session-refund-retries-original-storage-not-retuned-box", () => {
                string owner = Owner(a); var recoveryType = T("ScGunRecovery"); var recovery = Activator.CreateInstance(recoveryType);
                var undoType = T("ScGunUndo"); var step = Activator.CreateInstance(undoType); undoType.GetField("Slot").SetValue(step, 3); undoType.GetField("Value").SetValue(step, 900); undoType.GetField("Count").SetValue(step, 2);
                var steps = Array.CreateInstance(undoType, 1); steps.SetValue(step, 0); recoveryType.GetMethod("Enqueue").Invoke(recovery, [owner, steps]);
                boxType.GetField("channelIndex").SetValue(a, 7);
                try { return (int)recoveryType.GetMethod("Retry").Invoke(recovery, [(Func<string, IInventory>)Resolve]) == 1 && shared.GetSlotCount(3) == 2 && other.GetSlotCount(3) == 0; }
                finally { boxType.GetField("channelIndex").SetValue(a, 0); shared.RemoveSlotItems(3, int.MaxValue); other.RemoveSlotItems(3, int.MaxValue); }
            });
            Check("channel-removed-during-deduction-keeps-refund-not-detached-items", () => {
                var recovery = registryType.GetProperty("Recovery").GetValue(registry);
                int originalCount = (int)recovery.GetType().GetProperty("Count").GetValue(recovery);
                shared.AddSlotItems(2, 900, 2); var tx = Prepare(a, out _);
                mutation.GetField("AfterRecordWrite", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(tx, (Action)(() => channels.Remove(0)));
                try {
                    string result = mutation.GetMethod("Commit").Invoke(tx, [noop, 900, 1, null]).ToString();
                    return result == "RecoveryPending" && shared.GetSlotCount(2) == 1 && (int)recovery.GetType().GetProperty("Count").GetValue(recovery) == originalCount + 1;
                } finally {
                    channels[0] = shared;
                    recovery.GetType().GetMethod("Retry").Invoke(recovery, [(Func<string, IInventory>)Resolve]);
                    shared.RemoveSlotItems(2, int.MaxValue);
                }
            });
            Check("unresolved-refund-survives-two-saves-and-fences-reused-channel", () => {
                string owner = Owner(a); var recovery = registryType.GetProperty("Recovery").GetValue(registry); var recoveryType = recovery.GetType();
                var undoType = T("ScGunUndo"); var step = Activator.CreateInstance(undoType); undoType.GetField("Slot").SetValue(step, 3); undoType.GetField("Value").SetValue(step, 900); undoType.GetField("Count").SetValue(step, 2);
                var steps = Array.CreateInstance(undoType, 1); steps.SetValue(step, 0); recoveryType.GetMethod("Enqueue").Invoke(recovery, [owner, steps]);
                channels[0] = Native();
                try {
                    for (int r = 0; r < 2; r++) { var xml = new XElement("Values"); ((ValuesDictionary)recoveryType.GetMethod("Save").Invoke(recovery, null)).Save(xml); var read = new ValuesDictionary(); read.ApplyOverrides(XElement.Parse(xml.ToString())); recovery = recoveryType.GetMethod("Load").Invoke(null, [read]); }
                    return Resolve(owner) == null && (bool)recoveryType.GetMethod("HasPending").Invoke(recovery, [Owner(a)]) && (int)recoveryType.GetProperty("Count").GetValue(recovery) == 1;
                } finally { channels[0] = shared; }
            });
            Check("legacy-proxy-receipt-is-not-guessed", () => Resolve("entity/10/0/Sushi.ComponentSushiSyncBox") == null);
            Check("missing-channel-refuses-prepare-without-throw", () => {
                boxType.GetField("channelIndex").SetValue(a, 62);
                try { return Prepare(a, out _) == null; } finally { boxType.GetField("channelIndex").SetValue(a, 0); }
            });
            Check("ordinary-sushi-box-and-machines-remain-independent", () => {
                foreach (var type in sushiTool.GetTypes().Where(t => !t.IsAbstract && typeof(IInventory).IsAssignableFrom(t) && t != boxType && t.Name != "ComponentSushiPersonBox")) {
                    var first = (IInventory)Blank(type); var second = (IInventory)Blank(type);
                    if (!ReferenceEquals(Call("ScInventoryIdentity", "Storage", first), first) || Key(first) == Key(second)) return false;
                }
                return true;
            });
            Check("personal-box-creative-hotbar-not-catalogue", () => {
                NewRegistry();
                var creative = new ComponentCreativeInventory { OpenSlotsCount = 10 }; for (int i = 0; i < 12; i++) creative.m_slots.Add(0);
                creative.m_slots[0] = Value(0); creative.m_slots[10] = Value(0);
                var total = Blank(sushiBase.GetType("Sushi.SubsystemSushiTotal", true)); var miner = new ComponentMiner { Inventory = creative }; total.GetType().GetField("ComponentMiner").SetValue(total, miner);
                var personType = sushiTool.GetType("Sushi.ComponentSushiPersonBox", true); var proxy = (IInventory)Blank(personType); personType.GetField("m_SubsystemSushiTotal").SetValue(proxy, total);
                registryType.GetField("RecoveryOwner").SetValue(registry, (Func<IInventory, string>)(_ => "player/test")); locator.SetValue(null, null);
                var tx = Prepare(proxy, out _);
                return (bool)Call("ScInventoryTransaction", "IsWeaponSlot", proxy, 0) && !(bool)Call("ScInventoryTransaction", "IsWeaponSlot", proxy, 10)
                    && tx != null && Commit(tx) == "Success" && creative.GetSlotValue(0) != Value(0) && creative.GetSlotValue(10) == Value(0) && Next() == 2;
            });
            Check("creative-person-proxy-scan-and-ui-exclude-catalogue", () => {
                var creative = new ComponentCreativeInventory { OpenSlotsCount = 10 }; for (int i = 0; i < 12; i++) creative.m_slots.Add(0);
                creative.m_slots[0] = Value(1); creative.m_slots[10] = Value(1);
                var total = Blank(sushiBase.GetType("Sushi.SubsystemSushiTotal", true)); var miner = new ComponentMiner { Inventory = creative }; total.GetType().GetField("ComponentMiner").SetValue(total, miner);
                var personType = sushiTool.GetType("Sushi.ComponentSushiPersonBox", true); var proxy = (IInventory)Blank(personType); personType.GetField("m_SubsystemSushiTotal").SetValue(proxy, total);
                var isolated = Project(); var entity = (Entity)Blank(typeof(Entity)); entity.m_project = isolated; entity.m_components = [(Component)proxy]; ((Component)proxy).m_entity = entity; isolated.m_entities[entity] = true;
                int holders = ((IEnumerable)Call("ScGunHolders", "Scan", isolated, 512)).Cast<object>().Count();
                return holders == 1 && ((IEnumerable)Call("ScOwnedGunAttributes", "Candidates", proxy)).Cast<object>().Count() == 1
                    && ((IEnumerable)Call("ScGunCounter", "Candidates", proxy, 512)).Cast<object>().Count() == 1
                    && ((IEnumerable)Call("ScWeaponSkinning", "Candidates", proxy, 512)).Cast<object>().Count() == 1;
            });
        } finally {
            current.SetValue(null, saved); locator.SetValue(null, oldLocator); BlocksManager.Blocks[512] = oldGun; BlocksManager.Blocks[900] = oldAmmo;
            BlocksManager.BlockTypeToIndex.Clear(); foreach (var pair in oldTypes) BlocksManager.BlockTypeToIndex[pair.Key] = pair.Value;
        }
        return results;
    }
}
