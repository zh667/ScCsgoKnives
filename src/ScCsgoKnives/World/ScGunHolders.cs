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
    public static string Key(object container, int index) => container is null ? $"none:{index}" : $"holder:{s_identities.GetValue(container, _ => new Identity()).Number}:{index}";
    public static string PlayerKey(ComponentPlayer player, int slot) => Key(player?.ComponentMiner?.Inventory, slot);

    // These keys survive world saves. Entity.Id is persisted by EntityData/Project in SCAPI 1.9.2.1.
    // Unlike holder keys they identify a recovery destination, not an individual slot or gun.
    public static string RecoveryOwner(Project project, IInventory inventory) {
        if (project is null || inventory is null) return null;
        var players = project.FindSubsystem<SubsystemPlayers>(false);
        if (players is not null) foreach (var p in players.ComponentPlayers)
            if (ReferenceEquals(p.ComponentMiner?.Inventory, inventory)) return $"player/{p.PlayerData.PlayerIndex}";
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
        var scanner = project.FindSubsystem<SubsystemItemsScanner>(false);
        var seen = new HashSet<string>();
        if (scanner is not null) {
            foreach (var item in scanner.ScanItems().ToArray()) {
                if (item.Count <= 0 || Terrain.ExtractContents(item.Value) != gunBlockIndex) continue;
                if (IsDormantPlayerInventory(item.Container as IInventory)) continue;
                int id = GunSpec.GetId(Terrain.ExtractData(item.Value));
                if (id < GunSpec.FirstId || id > GunSpec.LastId) continue;
                if (item.Container is ComponentCreativeInventory creative && item.IndexInContainer >= creative.OpenSlotsCount) continue; // the catalogue, not a held gun
                string key = Key(item.Container, item.IndexInContainer);
                if (!seen.Add(key)) continue;
                yield return new Holder(id, key, item.Container as IInventory, item.IndexInContainer);
            }
            yield break;
        }
        // No scanner subsystem (unexpected): players, loaded block entities and pickables by hand.
        var players = project.FindSubsystem<SubsystemPlayers>(false);
        if (players is not null) foreach (var player in players.ComponentPlayers) {
            IInventory inventory = player.ComponentMiner?.Inventory;
            if (inventory is null) continue;
            int slots = inventory is ComponentCreativeInventory creative ? creative.OpenSlotsCount : inventory.SlotsCount;
            for (int slot = 0; slot < slots; slot++) foreach (var h in Of(inventory, slot, gunBlockIndex, seen)) yield return h;
        }
        var blocks = project.FindSubsystem<SubsystemBlockEntities>(false);
        if (blocks is not null) foreach (var entity in blocks.m_blockEntities.Values.ToArray()) {
            if (entity?.Entity is null) continue;
            foreach (var inventory in entity.Entity.FindComponents<IInventory>())
                for (int slot = 0; slot < inventory.SlotsCount; slot++) foreach (var h in Of(inventory, slot, gunBlockIndex, seen)) yield return h;
        }
        var pickables = project.FindSubsystem<SubsystemPickables>(false);
        if (pickables is not null) foreach (var pickable in pickables.Pickables.ToArray()) {
            if (Terrain.ExtractContents(pickable.Value) != gunBlockIndex) continue;
            int id = GunSpec.GetId(Terrain.ExtractData(pickable.Value));
            if (id >= GunSpec.FirstId && id <= GunSpec.LastId && seen.Add(Key(pickable, 0))) yield return new Holder(id, Key(pickable, 0), null, -1);
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
    static IEnumerable<Holder> Of(IInventory inventory, int slot, int gunBlockIndex, HashSet<string> seen) {
        int value = inventory.GetSlotValue(slot);
        if (inventory.GetSlotCount(slot) == 0 || Terrain.ExtractContents(value) != gunBlockIndex) yield break;
        int id = GunSpec.GetId(Terrain.ExtractData(value));
        string key = Key(inventory, slot);
        if (id >= GunSpec.FirstId && id <= GunSpec.LastId && seen.Add(key)) yield return new Holder(id, key, inventory, slot);
    }
}
