using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Engine;
namespace Game;

// current-direction-20260929 §6 M0: the core-side half of "shared core + separate network adapter". Compiled against the
// 1.9.3.1 API only (no multiplayer type is named at compile time), so the same package root DLL loads on the original
// single-player engine and on 1.9.3.2_MP. It decides once, at mod load, from what the running engine really offers:
//  - no Game.NetworkManager: standalone; nothing else happens (the MP adapter payload is never read).
//  - NetworkManager present: the CompatNet contract the adapter was compiled against is checked member by member; only
//    when it matches is Net/ScCsgoNet.bin loaded (never a root .dll: the loader resolves every root DLL's types eagerly and
//    aborts the whole package on a missing type). Any mismatch or failure refuses the network layer, loudly.
// Gameplay code would only ever see ScNetProbe.Bridge (null outside a verified multiplayer engine).

public enum ScNetEngine { Standalone, Multiplayer }
public enum ScNetRole { Standalone, Offline, Host, Client }
public enum ScNetHandshake { NotApplicable, Pending, Accepted, Rejected, TimedOut }

/// <summary>The narrow surface the network adapter implements for the core.</summary>
public interface IScNetBridge {
    ScNetRole Role { get; }
    int RemoteClients { get; }
    ScNetHandshake Handshake { get; }
    string HandshakeDetail { get; }
    /// <summary>Asks the server for a read-only status (M0's side-effect-free, server-confirmed request). Returns the
    /// request id, or -1 when refused locally (not a client, or no accepted handshake).</summary>
    int RequestStatus();
    string LastStatus { get; }
}

public sealed class ScNetProbeLoader : ModLoader {
    public override void __ModInitialize() => ScNetProbe.Initialize(Entity);
}

public static class ScNetProbe {
    public const string AdapterPayload = "Net/ScCsgoNet.bin";
    public const byte CompatNetProtocol = 2;
    public static ScNetEngine Engine { get; private set; }
    public static IScNetBridge Bridge { get; private set; }
    public static string Decision { get; private set; } = "not run";
    public static readonly List<string> Evidence = [];
    public static ScNetRole Role => Bridge?.Role ?? ScNetRole.Standalone;

    /// <summary>Called by the adapter's loader once it is registered with CompatNet.</summary>
    public static void Attach(IScNetBridge bridge) => Bridge = bridge;

    public static void Initialize(ModEntity entity) {
        try { Decide(entity); }
        catch (Exception e) { Bridge = null; Decision = $"network layer refused: {e.GetType().Name}: {e.Message}"; }
        foreach (string line in Evidence) Log.Information("[ScCsgoNet] " + line);
        Log.Information("[ScCsgoNet] decision: " + Decision);
    }

    static void Decide(ModEntity entity) {
        Type network = Type.GetType("Game.NetworkManager, Survivalcraft", false);
        Evidence.Add("Game.NetworkManager " + (network is null ? "absent" : "present"));
        if (network is null) {
            Engine = ScNetEngine.Standalone;
            Decision = "standalone engine: the network adapter is not loaded";
            return;
        }
        Engine = ScNetEngine.Multiplayer;
        Evidence.Add("mods: " + string.Join(", ", ModsManager.ModList.Select(m => m.modInfo?.PackageName + " " + m.modInfo?.Version)));
        List<string> missing = CheckContract(network);
        if (missing.Count > 0) {
            Decision = "multiplayer engine, but the CompatNet contract differs (" + string.Join("; ", missing) + "): network layer refused";
            return;
        }
        byte[] bytes = null;
        entity?.GetFile(AdapterPayload, stream => bytes = ModsManager.StreamToBytes(stream));
        if (bytes is null) { Decision = "multiplayer engine, but " + AdapterPayload + " is not in this package: network layer refused"; return; }
        Assembly assembly = Assembly.Load(bytes);
        assembly.GetTypes(); // resolve everything before publishing, as TacticalAppearanceIntegration does
        ModsManager.Dlls[assembly.FullName] = assembly;
        entity.HandleAssembly(assembly);
        Evidence.Add($"adapter {assembly.GetName().Name} {assembly.ManifestModule.ModuleVersionId} loaded from {AdapterPayload}");
        Decision = Bridge is null ? "multiplayer engine: adapter loaded but did not attach: network layer refused" : "multiplayer engine: CompatNet contract matched, adapter attached";
    }

    // Every engine member the adapter binds to, as "Type::Member(signature)". A difference means the adapter would fail
    // to bind at run time, so it is not loaded at all.
    static readonly (string Assembly, string Type, string[] Members)[] Contract = [
        ("Survivalcraft", "Game.NetworkManager", ["Boolean get_IsServerRunning()", "Boolean get_IsClientRunning()", "IEnumerable`1 get_ServerSessions()"]),
        ("Survivalcraft", "Game.ClientSession", ["Int32 get_PlayerIndex()", "Guid get_NetworkGuid()"]),
        ("Survivalcraft", "Game.ModLoader", ["Void OnNetworkPlayerStateChanged(NetworkState)", "Void SubsystemUpdate(SubsystemUpdate,Single)", "Void OnProjectDisposed()"]),
        ("Survivalcraft.CompatNet", "Game.ICompatNetAdapter", ["UInt16 get_AdapterId()", "String get_AdapterName()", "Void OnPreloadAssemblies(IList`1)",
            "Void HandleRequest(CompatNetPacket,PlayerData)", "Void HandleBroadcast(CompatNetPacket)", "Void OnServerUpdate(Single)",
            "Boolean ShouldSkipServerUpdate(IUpdateable)", "Boolean ShouldSkipClientUpdate(IUpdateable)"]),
        ("Survivalcraft.CompatNet", "Game.CompatNetAdapterRegistry", ["Void Register(ICompatNetAdapter)"]),
        ("Survivalcraft.CompatNet", "Game.CompatNetCore", ["Boolean QueueRequest(ComponentPlayer,UInt16,UInt16,Byte[])",
            "Boolean QueueBroadcast(UInt16,UInt16,Byte[],ClientSession,ClientSession)", "Boolean get_NetworkBridgeReady()"]),
        ("Survivalcraft.CompatNet", "Game.Network.CompatNetPacket", ["field UInt16 AdapterId", "field UInt16 Operation", "field Byte[] Payload"]),
    ];

    static List<string> CheckContract(Type network) {
        var missing = new List<string>();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies().GroupBy(a => a.GetName().Name).ToDictionary(g => g.Key, g => g.First());
        foreach (var (assemblyName, typeName, members) in Contract) {
            Type type = assemblyName == "Survivalcraft" ? network.Assembly.GetType(typeName, false) : assemblies.GetValueOrDefault(assemblyName)?.GetType(typeName, false);
            if (type is null) { missing.Add($"{assemblyName}:{typeName} absent"); continue; }
            var have = Describe(type);
            foreach (string member in members) if (!have.Contains(member)) missing.Add($"{typeName}::{member}");
            // A new abstract interface member would make the adapter class fail to load.
            if (type.IsInterface)
                foreach (MethodInfo m in type.GetMethods().Where(m => m.IsAbstract && !members.Contains(Describe(m))))
                    missing.Add($"{typeName} has new abstract member {Describe(m)}");
        }
        if (assemblies.GetValueOrDefault("Survivalcraft.CompatNet")?.GetType("Game.CompatNetCore", false)?.GetField("CurrentProtocolVersion") is { } protocol
            && protocol.GetRawConstantValue() is byte version && version != CompatNetProtocol)
            missing.Add($"CompatNet protocol {version} (adapter built for {CompatNetProtocol})");
        Evidence.Add($"CompatNet contract: {Contract.Sum(c => c.Members.Length)} members checked, {missing.Count} differences");
        return missing;
    }

    static HashSet<string> Describe(Type type) {
        const BindingFlags all = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        var set = new HashSet<string>(type.GetMethods(all).Select(Describe));
        foreach (FieldInfo f in type.GetFields(all)) set.Add($"field {f.FieldType.Name} {f.Name}");
        return set;
    }

    static string Describe(MethodInfo m) => $"{m.ReturnType.Name} {m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))})";
}
