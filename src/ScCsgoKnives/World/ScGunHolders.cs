using System.Runtime.CompilerServices;
using System.Threading;
using Engine;
using GameEntitySystem;
namespace Game;

/// <summary>Who holds which gun record right now. Uses the engine's own SubsystemItemsScanner, which walks every
/// subsystem and entity inventory (players, creative open slots, vanilla chests, Stash chests and any other
/// ComponentInventoryBase), dropped items, projectiles and moving blocks. A holder key is the container object's
/// identity plus the slot, so two chests at different places, two pickables or two components on one block entity
/// never collide. Read on the game thread; used to split shared records, never to reclaim ids.</summary>
public static class ScGunHolders {
    public readonly record struct Holder(int Id, string Key, IInventory Inventory, int Slot);
    sealed class Identity { public readonly long Number = Interlocked.Increment(ref s_nextIdentity); }
    static long s_nextIdentity;
    static readonly ConditionalWeakTable<object, Identity> s_identities = new();
    /// <summary>Unique within the running world: object identity of the container, then the slot/index.</summary>
    public static string Key(object container, int index) => container is null ? $"none:{index}" : $"holder:{s_identities.GetValue(ScInventoryIdentity.Storage(container), _ => new Identity()).Number}:{index}";
    public static string PlayerKey(ComponentPlayer player, int slot) => Key(player?.ComponentMiner?.Inventory, slot);

    // These keys survive world saves. Entity.Id is persisted by EntityData/Project in SCAPI 1.9.2.1.
    // Unlike holder keys they identify a recovery destination, not an individual slot or gun.
    public static string RecoveryOwner(Project project, IInventory inventory) {
        if (project is null || inventory is null) return null;
        string vault = ScInventoryIdentity.DurableVault(inventory);
        if (vault is not null) return vault;
        object storage = ScInventoryIdentity.Storage(inventory);
        string sync = ScSushiInventory.RecoveryOwner(project, storage as IInventory);
        if (sync is not null) return sync;
        if (ScInventoryIdentity.Inventory(inventory) is null) return null;
        var players = project.FindSubsystem<SubsystemPlayers>(false);
        if (players is not null) foreach (var p in players.ComponentPlayers)
            if (ReferenceEquals(p.ComponentMiner?.Inventory, storage)) return $"player/{p.PlayerData.PlayerIndex}";
        foreach (var entity in project.Entities) {
            int index = 0;
            foreach (var component in entity.Components) {
                if (ReferenceEquals(component, inventory)) return $"entity/{entity.Id}/{index}/{component.GetType().FullName}";
                index++;
            }
        }
        int subsystemIndex = 0;
        foreach (var subsystem in project.Subsystems) {
            if (ReferenceEquals(subsystem, inventory)) return $"subsystem/{subsystemIndex}/{subsystem.GetType().FullName}";
            subsystemIndex++;
        }
        return null; // no durable owner: refuse a transaction before taking any items
    }
    public static IInventory ResolveRecoveryOwner(Project project, string owner) {
        if (project is null || owner is null) return null;
        if (ScSushiInventory.IsOwner(owner)) return ScSushiInventory.Resolve(project, owner);
        // Old mutable-proxy receipts lack the original player/channel. Do not refund into
        // whichever destination that box happens to select now; keep the saved claim intact.
        if (ScSushiInventory.IsLegacyProxyOwner(owner)) return null;
        if (owner.StartsWith("logistics-vault/", StringComparison.Ordinal))
            return project.Entities.SelectMany(e => e.Components.OfType<IInventory>()).FirstOrDefault(i => ScInventoryIdentity.DurableVault(i) == owner);
        var players = project.FindSubsystem<SubsystemPlayers>(false);
        if (players is not null) foreach (var p in players.ComponentPlayers)
            if (owner == $"player/{p.PlayerData.PlayerIndex}") return p.ComponentMiner?.Inventory;
        foreach (var entity in project.Entities) {
            int index = 0;
            foreach (var component in entity.Components) {
                if (component is IInventory inventory && owner == $"entity/{entity.Id}/{index}/{component.GetType().FullName}") return inventory;
                index++;
            }
        }
        int subsystemIndex = 0;
        foreach (var subsystem in project.Subsystems) {
            if (subsystem is IInventory inventory && owner == $"subsystem/{subsystemIndex}/{subsystem.GetType().FullName}") return inventory;
            subsystemIndex++;
        }
        return null;
    }
    public static IEnumerable<Holder> Scan(Project project, int gunBlockIndex) {
        var seen = new HashSet<string>();
        // Same coverage as API 1.9.3.1 SubsystemItemsScanner, without allocating every unrelated item
        // or enumerating a shared 20k-slot vault seven times. Only proven storage aliases are skipped.
        var storageSeen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var inventories = project.Subsystems.OfType<IInventory>().Concat(project.Entities.SelectMany(e => e.Components.OfType<IInventory>()))
            .Concat(ScSushiInventory.Stored(project).Select(e => e.Inventory));
        foreach (var source in inventories) {
            var inventory = ScInventoryIdentity.Inventory(source);
            if (inventory is null) continue;
            if (IsDormantPlayerInventory(inventory) || !storageSeen.Add(ScInventoryIdentity.Storage(inventory))) continue;
            int slots = inventory is ComponentCreativeInventory creative ? creative.OpenSlotsCount : inventory.SlotsCount;
            for (int slot = 0; slot < slots; slot++) foreach (var h in Of(inventory, slot, gunBlockIndex, seen)) yield return h;
        }
        var pickables = project.FindSubsystem<SubsystemPickables>(false);
        if (pickables is not null) foreach (var pickable in pickables.Pickables.ToArray()) {
            if (pickable.Count <= 0 || Terrain.ExtractContents(pickable.Value) != gunBlockIndex || !MatchesRecord(pickable.Value)) continue;
            int id = GunSpec.GetId(Terrain.ExtractData(pickable.Value));
            if (id >= GunSpec.FirstId && id <= GunSpec.LastId && seen.Add(Key(pickable, 0))) yield return new Holder(id, Key(pickable, 0), null, -1);
        }
        var projectiles = project.FindSubsystem<SubsystemProjectiles>(false);
        if (projectiles is not null) foreach (var projectile in projectiles.Projectiles.ToArray()) {
            if (Terrain.ExtractContents(projectile.Value) != gunBlockIndex || !MatchesRecord(projectile.Value)) continue;
            int id = GunSpec.GetId(Terrain.ExtractData(projectile.Value));
            if (id >= GunSpec.FirstId && id <= GunSpec.LastId) yield return new Holder(id, Key(projectile, 0), null, -1);
        }
        var moving = project.FindSubsystem<SubsystemMovingBlocks>(false);
        if (moving is not null) foreach (var set in moving.MovingBlockSetEnumerable) for (int i = 0; i < set.Blocks.Count; i++) {
            int value = set.Blocks[i].Value;
            if (Terrain.ExtractContents(value) != gunBlockIndex || !MatchesRecord(value)) continue;
            int id = GunSpec.GetId(Terrain.ExtractData(value));
            if (id >= GunSpec.FirstId && id <= GunSpec.LastId) yield return new Holder(id, Key(set, i), null, -1);
        }
    }
    /// <summary>The API saves both player inventories, but Miner selects only one for the current mode.
    /// An inactive inventory is not a second live copy during chest transfers after switching mode.
    /// Other accessible inventories (crafting, Stash, chests) must still participate in duplication checks.</summary>
    public static bool IsDormantPlayerInventory(IInventory inventory) {
        if (inventory is not ComponentCreativeInventory && inventory is not ComponentInventory) return false;
        var component = (GameEntitySystem.Component)inventory;
        var player = component.Entity?.FindComponent<ComponentPlayer>();
        var active = player?.ComponentMiner?.Inventory;
        return active is not null && !ReferenceEquals(active, inventory);
    }
    // Positive evidence only: re-read both live slots and their current backing stores.
    // A vanished/remapped witness must never be interpreted as proof of uniqueness.
    internal static bool StillDuplicates(Holder acting, Holder witness, int gunIndex) {
        bool Live(Holder h) => h.Inventory is not null && !IsDormantPlayerInventory(h.Inventory)
            && h.Slot >= 0 && h.Slot < h.Inventory.SlotsCount && Key(h.Inventory, h.Slot) == h.Key
            && h.Inventory.GetSlotCount(h.Slot) > 0 && Terrain.ExtractContents(h.Inventory.GetSlotValue(h.Slot)) == gunIndex
            && MatchesRecord(h.Inventory.GetSlotValue(h.Slot))
            && GunSpec.GetId(Terrain.ExtractData(h.Inventory.GetSlotValue(h.Slot))) == h.Id;
        return acting.Id == witness.Id && acting.Key != witness.Key && Live(acting) && Live(witness);
    }
    static IEnumerable<Holder> Of(IInventory inventory, int slot, int gunBlockIndex, HashSet<string> seen) {
        int value = inventory.GetSlotValue(slot);
        if (inventory.GetSlotCount(slot) == 0 || Terrain.ExtractContents(value) != gunBlockIndex || !MatchesRecord(value)) yield break;
        int id = GunSpec.GetId(Terrain.ExtractData(value));
        string key = Key(inventory, slot);
        if (id >= GunSpec.FirstId && id <= GunSpec.LastId && seen.Add(key))
            yield return inventory is ComponentScCompatibilityArchive ? new Holder(id,key,null,-1) : new Holder(id, key, inventory, slot);
    }
    // Invalid references are reserved at XML load, but cannot witness a live duplicate: doing so
    // would strip growth from a healthy gun merely because an unrelated broken item shares its number.
    public static bool MatchesRecord(int value) => ScGunRegistry.Current is null
        || GunSpec.TryGetSnapshot(Terrain.ExtractData(value), out _);
}
