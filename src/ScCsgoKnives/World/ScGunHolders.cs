using System.Runtime.CompilerServices;
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
    /// <summary>Unique within the running world: object identity of the container, then the slot/index.</summary>
    public static string Key(object container, int index) => container is null ? $"none:{index}" : $"{container.GetType().Name}#{RuntimeHelpers.GetHashCode(container)}:{index}";
    public static string PlayerKey(ComponentPlayer player, int slot) => Key(player?.ComponentMiner?.Inventory, slot);
    public static IEnumerable<Holder> Scan(Project project, int gunBlockIndex) {
        var scanner = project.FindSubsystem<SubsystemItemsScanner>(false);
        var seen = new HashSet<string>();
        if (scanner is not null) {
            foreach (var item in scanner.ScanItems().ToArray()) {
                if (item.Count <= 0 || Terrain.ExtractContents(item.Value) != gunBlockIndex) continue;
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
    static IEnumerable<Holder> Of(IInventory inventory, int slot, int gunBlockIndex, HashSet<string> seen) {
        int value = inventory.GetSlotValue(slot);
        if (inventory.GetSlotCount(slot) == 0 || Terrain.ExtractContents(value) != gunBlockIndex) yield break;
        int id = GunSpec.GetId(Terrain.ExtractData(value));
        string key = Key(inventory, slot);
        if (id >= GunSpec.FirstId && id <= GunSpec.LastId && seen.Add(key)) yield return new Holder(id, key, inventory, slot);
    }
}
