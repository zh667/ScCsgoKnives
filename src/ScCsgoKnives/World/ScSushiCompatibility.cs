using System.Reflection;
namespace Game;

/// <summary>mp-state-consistency-20261002 (I5): what of the Sushi packages (玲兰辅助 SushiBase, 玲兰科技 SushiTool) this
/// process can and cannot rely on, read from what is actually loaded: never from a version text, never assumed. One line
/// in the log per world when Sushi is present; the offline checks read the same record. Nothing of Sushi is changed.
/// Each capability is separate, because they fail separately: the inventory mapping this core verified offline against
/// SushiBase 3.0 / SushiTool 3.0, the stacking Sushi's own containers allow, and the multiplayer bridge, which is the
/// platform's (its CompatNet internal mod carries one for Sushi on the Windows build; the Android build has none) and not
/// this mod's: the CS network layer replicates CS state and ordinary entity inventories, not Sushi's shared channels,
/// its machines or who a personal box belongs to.</summary>
public static class ScSushiCompatibility {
    public sealed record Status(bool Present, string Assemblies, bool PersonBoxMapped, bool SyncBoxMapped, int GunStackField, bool PlatformBridge) {
        /// <summary>Sushi rewrote the CS gun block's stacking field (its stacking options are on).</summary>
        public bool StackingRaised => GunStackField > 1;
    }
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static Status Read() {
        Assembly Find(string name) => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
        Assembly sushiBase = Find("SushiBase"), sushiTool = Find("SushiTool");
        if (sushiBase is null && sushiTool is null) return new(false, "", false, false, 1, false);
        string Identity(Assembly a) => a is null ? "" : $"{a.GetName().Name} {a.GetName().Version} {a.ManifestModule.ModuleVersionId:N}";
        // The members ScInventoryIdentity and ScSushiInventory bind to; a build without them is "not mapped", never guessed.
        bool person = sushiTool?.GetType("Sushi.ComponentSushiPersonBox", false)?.GetField("m_SubsystemSushiTotal", Fields) is { } total
            && total.FieldType.GetField("ComponentMiner", Fields)?.FieldType == typeof(ComponentMiner);
        Type box = sushiTool?.GetType("Sushi.ComponentSushiSyncBox", false);
        bool sync = box?.GetField("m_subsystemSushiSyncBox", Fields) is { } store && box.GetField("channelIndex", Fields)?.FieldType == typeof(int)
            && typeof(System.Collections.IDictionary).IsAssignableFrom(store.FieldType.GetField("SushiSyncInventories")?.FieldType);
        int stack = BlocksManager.BlockTypeToIndex.TryGetValue(typeof(ScGunBlock), out int gun) && BlocksManager.Blocks[gun] is { } block ? block.MaxStacking : 1;
        bool bridge = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Survivalcraft.CompatNet" && a.GetType("Game.SushiToolCompatNetRuntime", false) is not null);
        return new(true, string.Join("; ", new[] { Identity(sushiBase), Identity(sushiTool) }.Where(s => s.Length > 0)), person, sync, stack, bridge);
    }

    public static string Describe(Status s) {
        if (!s.Present) return "Sushi not loaded";
        string stacking = s.StackingRaised
            ? $"Sushi's stacking options are on (the gun block's stacking field reads {s.GunStackField}): CS guns still stack 1 wherever the block is asked (player, chests, crafting); Sushi's own boxes, shared channels and machines compute their capacity themselves and take up to {s.GunStackField} identical guns in a slot - such a stack is kept whole and cannot be used until it is spread out"
            : "Sushi's stacking options leave CS guns at 1";
        string multiplayer = ScNet.EngineHasNetwork
            ? s.PlatformBridge ? "multiplayer: the platform's own Sushi bridge (CompatNet) is present; shared channels, machines and personal-box ownership are that bridge's, not verified by CS weapons with this build pair"
                : "multiplayer: this platform build has no Sushi bridge; Sushi's shared channels, machines and personal boxes are not replicated between players (CS weapons' own state is)"
            : "single player";
        return $"{s.Assemblies}; personal box -> the player's own inventory: {(s.PersonBoxMapped ? "mapped" : "NOT mapped (members differ), treated as unknown")}; "
            + $"sync box -> its shared channel: {(s.SyncBoxMapped ? "mapped" : "NOT mapped (members differ), treated as unknown")}; {stacking}; {multiplayer}";
    }

    static object s_logged;
    /// <summary>One line per world, only when Sushi is loaded.</summary>
    public static void LogOnce(object world) {
        if (ReferenceEquals(s_logged, world)) return;
        s_logged = world;
        try { var status = Read(); if (status.Present) KnifeLog.Information("[CS_SUSHI] " + Describe(status)); }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("sushi-capabilities", "[CS_SUSHI] capability check failed: " + e.Message); }
    }
}
