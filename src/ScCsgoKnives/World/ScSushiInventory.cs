using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using GameEntitySystem;
namespace Game;

/// <summary>Optional, verified Sushi inventory integration; no dependency or third-party writes.</summary>
public static class ScSushiInventory {
    internal static IDictionary Channels(object subsystem) => subsystem?.GetType().FullName == "Sushi.SubsystemSushiSyncBox"
        ? subsystem.GetType().GetField("SushiSyncInventories")?.GetValue(subsystem) as IDictionary : null;

    // Channels persist even when no box currently selects them, and are not IInventory subsystems.
    public static IEnumerable<(int Channel, IInventory Inventory)> Stored(Project project) {
        if (project is null) yield break;
        foreach (var subsystem in project.Subsystems) {
            if (Channels(subsystem) is not { } channels) continue;
            foreach (DictionaryEntry entry in channels)
                if (entry.Key is int channel && channel >= 0 && entry.Value is IInventory inventory)
                    yield return (channel, inventory);
        }
    }

    sealed record Binding(int Channel, string Owner);
    static readonly ConditionalWeakTable<Project, ConditionalWeakTable<object, Binding>> Bindings = new();
    const string Prefix = "sushi-sync/";
    public static string RecoveryOwner(Project project, IInventory inventory) {
        foreach (var entry in Stored(project)) {
            if (!ReferenceEquals(entry.Inventory, inventory)) continue;
            var map = Bindings.GetOrCreateValue(project);
            if (!map.TryGetValue(inventory, out var binding)) {
                binding = new(entry.Channel, Prefix + entry.Channel.ToString(CultureInfo.InvariantCulture) + "/" + Guid.NewGuid().ToString("N"));
                map.Add(inventory, binding);
            }
            return binding.Channel == entry.Channel ? binding.Owner : null;
        }
        return null;
    }
    public static bool IsOwner(string owner) => owner?.StartsWith(Prefix, StringComparison.Ordinal) == true;
    public static bool IsLegacyProxyOwner(string owner) => owner?.EndsWith("/Sushi.ComponentSushiSyncBox", StringComparison.Ordinal) == true
        || owner?.EndsWith("/Sushi.ComponentSushiPersonBox", StringComparison.Ordinal) == true;
    public static IInventory Resolve(Project project, string owner) {
        // Sushi has no persisted channel UUID: channel numbers can be deleted and reused.
        // Only the same live backing object is positive evidence for automatic compensation.
        // After reload retain the saved debt for explicit recovery, never guess from channel number.
        if (project is null || !Bindings.TryGetValue(project, out var map)) return null;
        foreach (var entry in Stored(project))
            if (map.TryGetValue(entry.Inventory, out var binding) && binding.Channel == entry.Channel && binding.Owner == owner) return entry.Inventory;
        return null;
    }
    public static bool Blocks(string pending, string owner) {
        if (owner?.StartsWith("player/", StringComparison.Ordinal) == true
            && pending?.EndsWith("/Sushi.ComponentSushiPersonBox", StringComparison.Ordinal) == true) return true;
        if (!IsOwner(owner)) return false;
        if (IsLegacyProxyOwner(pending)) return true; // Old entity-only receipts did not capture a channel.
        if (!IsOwner(pending)) return false;
        string Channel(string s) => s.Split('/').ElementAtOrDefault(1);
        // An unresolved debt also fences a newly-created/reloaded inventory at the same channel.
        return Channel(pending) == Channel(owner);
    }
}
