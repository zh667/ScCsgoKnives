using System.Reflection;
namespace Game;

/// <summary>Verified proxy adapters only. Equal item contents or a GUID alone never imply aliasing.</summary>
public static class ScInventoryIdentity {
    /// <summary>Pin verified forwarding inventories, including their creative-slot semantics.
    /// An unresolved known proxy is not safe to scan or mutate as an independent container.</summary>
    public static IInventory Inventory(IInventory inventory) {
        object storage = Storage(inventory);
        if (ReferenceEquals(storage, inventory) && inventory?.GetType().FullName is "Sushi.ComponentSushiPersonBox" or "Sushi.ComponentSushiSyncBox") return null;
        return storage as IInventory ?? inventory;
    }
    public static string DurableVault(object inventory) {
        if (inventory?.GetType().FullName != "Logistics.ComponentStorageUnit" || ReferenceEquals(Storage(inventory), inventory)) return null;
        return inventory.GetType().GetProperty("VaultGuid")?.GetValue(inventory) is Guid id && id != Guid.Empty ? "logistics-vault/" + id.ToString("N") : null;
    }
    static readonly Dictionary<Type, MethodInfo> Resolvers = new();
    public static object Storage(object inventory) {
        if (inventory is null) return null;
        Type type = inventory.GetType();
        // SushiTouch exposes several inventory proxies. PersonBox forwards every
        // slot to the player's real inventory; treating each proxy as a holder
        // creates the exact 7-way duplicate/ID exhaustion seen in the field.
        if (type.FullName is "Sushi.ComponentSushiPersonBox") {
            try {
                object total = type.GetField("m_SubsystemSushiTotal", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(inventory);
                object miner = total?.GetType().GetField("ComponentMiner", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(total);
                // Inventory is an engine property, not a field. Read it on every resolution:
                // Sushi follows the active miner inventory when the player/game mode changes.
                object real = (miner as ComponentMiner)?.Inventory;
                if (real is not null) { KnifeDiagnostics.WarnOnce("inventory-alias-sushi-person", "[GUN_STORAGE] mapped Sushi.ComponentSushiPersonBox to the player's underlying inventory"); return real; }
                KnifeDiagnostics.WarnOnce("inventory-alias-sushi-person-unresolved", "[GUN_STORAGE] Sushi person-box mapping unavailable: SubsystemSushiTotal.ComponentMiner.Inventory is null or the verified member contract changed");
            } catch (Exception e) { KnifeDiagnostics.WarnOnce("inventory-alias-sushi-person-error", "[GUN_STORAGE] Sushi person-box mapping failed: " + e.GetBaseException().Message); }
        }
        if (type.FullName is "Sushi.ComponentSushiSyncBox") {
            try {
                object sync = type.GetField("m_subsystemSushiSyncBox", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(inventory);
                object channel = type.GetField("channelIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(inventory);
                // Verified SushiTool DLL uses Dictionary<int,SushiSyncInventory>, not IList.
                // Channel keys can be sparse; never substitute channel zero or bound by Count.
                if (channel is int key && ScSushiInventory.Channels(sync) is { } channels
                    && channels.Contains(key) && channels[key] is IInventory real) return real;
                KnifeDiagnostics.WarnOnce("inventory-alias-sushi-sync-unresolved", "[GUN_STORAGE] Sushi sync-box channel unavailable or member contract changed; no guessed alias");
            } catch (Exception e) { KnifeDiagnostics.WarnOnce("inventory-alias-sushi-sync-error", "[GUN_STORAGE] Sushi sync-box mapping failed: " + e.GetBaseException().Message); }
        }
        if (!Resolvers.TryGetValue(type, out var resolve)) {
            // Logistics 1.0.0.1 forwards all slot operations without index translation to this object.
            resolve = type.FullName == "Logistics.ComponentStorageUnit"
                ? type.GetMethods(BindingFlags.Instance | BindingFlags.Public).SingleOrDefault(m =>
                    m.Name == "TryResolveVault" && m.ReturnType == typeof(bool)
                    && m.GetParameters() is { Length: 1 } p && p[0].IsOut
                    && p[0].ParameterType.GetElementType()?.FullName == "Logistics.StorageVault") : null;
            Resolvers[type] = resolve;
        }
        if (resolve is null) return inventory;
        try {
            object[] args = { null };
            if (resolve.Invoke(inventory, args) is true && args[0] is not null) return args[0];
        } catch (Exception e) {
            KnifeDiagnostics.WarnOnce("inventory-alias-" + type.FullName, "[GUN_STORAGE] shared inventory resolution failed; no guessed alias: " + e.GetBaseException().Message);
        }
        return inventory;
    }
}
