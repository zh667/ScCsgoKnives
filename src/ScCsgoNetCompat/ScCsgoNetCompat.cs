using System.Reflection;
using Engine;
using Game.Network;
namespace Game;

// Round 3 (post-mp-bugs-20260930 item 4), split out 2026-10-02 (mp-user-logs-20261002): the one correction that concerns
// the platform's CompatNet internal mod. Without it this payload is not loaded at all (ScNet.Decide).
//
// CommandCompatNet's freeze. The platform's command-block compatibility adapter (id 2, for the mod "zh.command") is
// registered even when that mod is absent. Its server tick broadcasts every player's state every 0.01 s, and each
// snapshot re-arms a 2 s "skip the local player's updates" on the client, so a client never walks, digs, hits, uses
// or changes slots (evidence: tools/MpM0/mp_vanilla.py). The registry has no replacement call, but it is rebuilt by
// the CompatNet loader once, during assembly preload, and this payload is loaded by the core afterwards: it re-registers
// every adapter, with id 2 wrapped (ScCommandCompatGate). Without zh.command the wrapper lets nothing through: there
// are no commands to sync (and a client without CompatNet, e.g. on the platform's Android build, is not sent a packet it
// cannot read a hundred times a second). With zh.command every request goes to the platform's adapter unchanged, and its
// server tick runs for CommandWindow seconds after a command request, so a real command's teleport/health/attribute
// correction is broadcast and applied once, and the client's freeze ends with it instead of being renewed forever.
// No platform file is rewritten, no third-party package is touched, nothing is patched in memory.
public sealed class ScCsgoNetCompatLoader : ModLoader {
    public const double CommandWindow = 2.5;
    /// <summary>Upstream CompatNet builds (MVID) where the freeze is fixed: no gate there.</summary>
    public static readonly string[] FixedIn = [];

    public override void __ModInitialize() {
        ModsManager.RegisterHook("OnProjectDisposed", this);
        try { ScNet.CompatStatus = InstallCommandGate(); }
        catch (Exception e) { ScNet.CompatStatus = $"command gate failed: {e.GetType().Name}: {e.Message}"; }
        ScNet.CommandGateInstalled = ScNet.CompatStatus == "command gate: installed";
        Log.Information("[ScCsgoNet] CompatNet corrections: " + ScNet.CompatStatus);
    }

    static string InstallCommandGate() {
        if (FixedIn.Contains(typeof(CompatNetAdapterRegistry).Assembly.ManifestModule.ModuleVersionId.ToString())) return "upstream fixed: gate off";
        if (CompatNetAdapterRegistry.TryGet(ScCommandCompatGate.CommandAdapterId, out var present) && present is ScCommandCompatGate) return "command gate: installed";
        string why = CommandContract(present);
        if (why is not null) return "contract differs, gate off: " + why;
        // Rebuild the registry with id 2 wrapped: the only registry operations are Reset and Register.
        var adapters = CompatNetAdapterRegistry.Adapters.ToList();
        CompatNetAdapterRegistry.Reset();
        foreach (var a in adapters) CompatNetAdapterRegistry.Register(ReferenceEquals(a, present) ? new ScCommandCompatGate(present) : a);
        return CompatNetAdapterRegistry.TryGet(ScCommandCompatGate.CommandAdapterId, out var now) && now is ScCommandCompatGate ? "command gate: installed" : "registry rebuild did not take";
    }

    /// <summary>The platform pieces the gate relies on: the built-in adapter 2 for zh.command and the runtime whose server
    /// tick broadcasts player state. Null when they are as expected.</summary>
    static string CommandContract(ICompatNetAdapter present) {
        if (present is null) return "no adapter 2 registered";
        Type t = present.GetType();
        if (t.Name != "CommandCompatNetAdapter") return $"adapter 2 is {t.Name}";
        if (t.GetField("TargetPackageName", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string != ScCommandCompatGate.CommandPackage) return "adapter 2 targets another mod";
        Type runtime = t.Assembly.GetType("Game.CommandCompatNetRuntime", false) ?? t.Assembly.GetTypes().FirstOrDefault(x => x.Name == "CommandCompatNetRuntime");
        if (runtime is null) return "no CommandCompatNetRuntime";
        if (runtime.GetMethod("SendPlayerStateSnapshot", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes) is null) return "runtime no longer broadcasts player state this way";
        if (runtime.GetMethod("ShouldSkipClientUpdate", BindingFlags.Public | BindingFlags.Static, [typeof(IUpdateable)]) is null) return "runtime no longer skips client updates this way";
        return null;
    }

    public override void OnProjectDisposed() {
        if (CompatNetAdapterRegistry.TryGet(ScCommandCompatGate.CommandAdapterId, out var gate) && gate is ScCommandCompatGate g) g.Reset();
    }
}

/// <summary>Adapter 2 (zh.command) behind a gate: nothing without the command mod; with it, the platform adapter's server
/// tick runs only for a window after a command request, so a command's correction is one correction.</summary>
public sealed class ScCommandCompatGate : ICompatNetAdapter {
    public const ushort CommandAdapterId = 2;
    public const string CommandPackage = "zh.command";
    const ushort EditBlock = 1, ExecuteCommand = 2;
    readonly ICompatNetAdapter m_inner;
    double m_windowUntil;
    public int Requests { get; private set; }
    public ScCommandCompatGate(ICompatNetAdapter inner) { m_inner = inner; }
    public static bool CommandModLoaded => ModsManager.ModList.Any(m => string.Equals(m.modInfo?.PackageName, CommandPackage, StringComparison.OrdinalIgnoreCase));
    public ushort AdapterId => m_inner.AdapterId;
    public string AdapterName => m_inner.AdapterName + " (gated by ScCsgoNet)";
    public void OnPreloadAssemblies(IList<ModAssemblyImage> assemblies) => m_inner.OnPreloadAssemblies(assemblies);
    public void HandleRequest(CompatNetPacket packet, PlayerData playerData) {
        if (packet.Operation is ExecuteCommand or EditBlock) { m_windowUntil = Time.RealTime + ScCsgoNetCompatLoader.CommandWindow; Requests++; }
        m_inner.HandleRequest(packet, playerData);
    }
    public void HandleBroadcast(CompatNetPacket packet) => m_inner.HandleBroadcast(packet);
    public void OnServerUpdate(float dt) { if (CommandModLoaded && Time.RealTime < m_windowUntil) m_inner.OnServerUpdate(dt); }
    public bool ShouldSkipServerUpdate(IUpdateable updateable) => CommandModLoaded && m_inner.ShouldSkipServerUpdate(updateable);
    public bool ShouldSkipClientUpdate(IUpdateable updateable) => CommandModLoaded && m_inner.ShouldSkipClientUpdate(updateable);
    public void Reset() => m_windowUntil = 0;
}

