using System.Reflection;
namespace Game;

/// <summary>Use Enchantment 1.3.5's existing exclusion sets for our own assembly only.
/// Our item data is a model/instance key, NOT its bit-packed enchantments. No foreign file patches.</summary>
public static class ScEnchantmentCompatibility {
    public static bool Register(Assembly enchantment) {
        var manager = enchantment?.GetType("Enchantment.EnchantmentManager", false);
        if (manager is null) return false;
        var field = manager.GetField("s_blacklistedAssemblies", BindingFlags.Static | BindingFlags.NonPublic);
        if (field?.GetValue(null) is not HashSet<Assembly> excluded) return false;
        var ours = typeof(ScGunBlock).Assembly;
        excluded.Add(ours);
        var patches = enchantment.GetType("Enchantment.Patches.EnchantmentPatches", false);
        if (patches?.GetField("s_pbrAssemblies", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) is HashSet<Assembly> drawExcluded)
            drawExcluded.Add(ours);
        return true;
    }
    public static void Initialize() {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            if (assembly.GetType("Enchantment.EnchantmentManager", false) is null) continue;
            try {
                if (Register(assembly)) KnifeLog.Information("[COMPAT] Enchantment: CS weapon assembly excluded from bit-packed enchantments; weapon IDs and counters unchanged.");
                else KnifeLog.Warning("[COMPAT] Enchantment present but its exclusion API differs; no foreign patches applied.");
            } catch (Exception e) { KnifeLog.Warning("[COMPAT] Enchantment exclusion failed: " + e.Message); }
        }
    }
}
