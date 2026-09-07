using Engine;
using GameEntitySystem;
namespace Game;

/// <summary>Who holds which gun record right now: player inventories (creative open slots included), the inventories of
/// loaded block entities (vanilla chests and any container mod whose block entity carries an IInventory), and dropped
/// items. Read on the game thread. Used to split shared records; never to reclaim ids.</summary>
public static class ScGunHolders {
    public readonly record struct Holder(int Id, string Key, IInventory Inventory, int Slot);
    public static string PlayerKey(ComponentPlayer player, int slot) => $"player:{player.PlayerData.PlayerIndex}:{slot}";
    public static IEnumerable<Holder> Scan(Project project, int gunBlockIndex) {
        var players = project.FindSubsystem<SubsystemPlayers>(false);
        if (players is not null) foreach (var player in players.ComponentPlayers) {
            IInventory inventory = player.ComponentMiner?.Inventory;
            if (inventory is null) continue;
            int slots = inventory is ComponentCreativeInventory creative ? creative.OpenSlotsCount : inventory.SlotsCount;
            for (int slot = 0; slot < slots; slot++) foreach (var h in Of(inventory, slot, gunBlockIndex, PlayerKey(player, slot))) yield return h;
        }
        var blocks = project.FindSubsystem<SubsystemBlockEntities>(false);
        if (blocks is not null) foreach (var (point, entity) in blocks.m_blockEntities.ToArray()) {
            if (entity?.Entity is null) continue;
            foreach (var inventory in entity.Entity.FindComponents<IInventory>())
                for (int slot = 0; slot < inventory.SlotsCount; slot++) foreach (var h in Of(inventory, slot, gunBlockIndex, $"block:{point.X},{point.Y},{point.Z}:{slot}")) yield return h;
        }
        var pickables = project.FindSubsystem<SubsystemPickables>(false);
        if (pickables is not null) foreach (var pickable in pickables.Pickables.ToArray()) {
            if (Terrain.ExtractContents(pickable.Value) != gunBlockIndex) continue;
            int id = GunSpec.GetId(Terrain.ExtractData(pickable.Value));
            if (id >= GunSpec.FirstId && id <= GunSpec.LastId) yield return new Holder(id, "pickable", null, -1);
        }
    }
    static IEnumerable<Holder> Of(IInventory inventory, int slot, int gunBlockIndex, string key) {
        int value = inventory.GetSlotValue(slot);
        if (inventory.GetSlotCount(slot) == 0 || Terrain.ExtractContents(value) != gunBlockIndex) yield break;
        int id = GunSpec.GetId(Terrain.ExtractData(value));
        if (id >= GunSpec.FirstId && id <= GunSpec.LastId) yield return new Holder(id, key, inventory, slot);
    }
}
