using System.Reflection;
using Engine;
using GameEntitySystem;
namespace Game;

// current-direction-20260929 §6: the core half of "shared core + separate network adapter". Everything here compiles
// against the 1.9.3.1 API only and names no multiplayer type, so the same package loads on the original engine, where
// it decides "standalone" once and every call below is a no-op. On the 1.9.3.2 multiplayer engine the adapter
// (Net/ScCsgoNet.bin, built against that engine and its Multiplayer internal mod) is loaded after its engine contract was
// checked member by member; it implements IScNetTransport and hands peers' messages to the handlers registered here.
//
// mp-user-logs-20261002: the transport is the adapter's own packet on the Multiplayer mod's public packet table. The
// CompatNet internal mod is NOT required (the Android build of the platform ships Multiplayer only; a host there refused
// the whole network layer and every client timed out). Where CompatNet exists (Windows), a second, optional payload
// (Net/ScCsgoNetCompat.bin) carries the one correction that concerns it (the command-adapter freeze gate).
//
// Authority rule used by the gameplay code: IsAuthority (single player, MP host/server, MP engine with no session) runs
// the real game; a remote client (IsRemoteClient) never commits gun records, inventories, damage or world changes, it
// sends its input/requests and shows the server's results. A client whose handshake failed (ClientBlocked) refuses the
// CS actions instead of computing anything a second time.

public enum ScNetRole { Standalone, Offline, Host, Client }
public enum ScNetHandshake { NotApplicable, Pending, Accepted, Rejected, TimedOut }

/// <summary>A remote client as the server sees it, once its handshake was accepted.</summary>
public sealed class ScNetPeer {
    public int PlayerIndex { get; set; }
    public Guid Guid { get; set; }
    /// <summary>The adapter's session object (opaque to the core).</summary>
    public object Session { get; set; }
    public ComponentPlayer Player(Project project) =>
        project?.FindSubsystem<SubsystemPlayers>(false)?.PlayersData.FirstOrDefault(d => d.PlayerIndex == PlayerIndex)?.ComponentPlayer;
    public override string ToString() => $"player {PlayerIndex} ({Guid})";
}

/// <summary>What the network adapter provides to the core.</summary>
public interface IScNetTransport {
    ScNetRole Role { get; }
    ScNetHandshake Handshake { get; }
    string HandshakeDetail { get; }
    /// <summary>Server: remote clients with an accepted handshake.</summary>
    IReadOnlyList<ScNetPeer> Peers { get; }
    /// <summary>This process reads this player's devices (the one main player of an MP process).</summary>
    bool IsLocal(ComponentPlayer player);
    bool SendToServer(ushort op, byte[] payload);
    bool SendTo(ScNetPeer peer, ushort op, byte[] payload);
    void Broadcast(ushort op, byte[] payload, ScNetPeer except);
}

/// <summary>Optional transport service: where a body may have stood on a remote client's screen when it shot (the engine
/// keeps each creature body's recent positions on the server; a client draws other bodies RemoteBodyDelay plus up to one
/// snapshot interval late). Fills <paramref name="positions"/> with samples across that window, returns how many.</summary>
public interface IScNetRewind {
    int TryRewound(ComponentBody body, ComponentPlayer shooter, Vector3[] positions);
}
/// <summary>Test diagnostics of the rewind (the window and the shooter's latency estimate it used).</summary>
public interface IScNetRewindInfo {
    string Describe(ComponentPlayer shooter);
}

public static class ScNet {
    public const string AdapterPayload = "Net/ScCsgoNet.bin";
    /// <summary>Optional: the corrections that need the platform's CompatNet internal mod; loaded only where it exists.</summary>
    public const string CompatPayload = "Net/ScCsgoNetCompat.bin";
    /// <summary>Ops below this belong to the adapter itself (handshake, diagnostics).</summary>
    public const ushort FirstGameOp = 32;
    public const byte CompatNetProtocol = 2;
    /// <summary>What the optional CompatNet part reported ("absent", "command gate: installed", "contract differs: ...").</summary>
    public static string CompatStatus { get; set; } = "not run";
    /// <summary>Set by the optional CompatNet part: the command-adapter gate is in place (test diagnostics, player notice).</summary>
    public static bool CommandGateInstalled { get; set; }

    public static IScNetTransport Transport { get; private set; }
    /// <summary>The clock of network leases, retries and prediction time-outs (real time; the offline checks replace it to
    /// walk through them).</summary>
    public static Func<double> Clock = () => Time.RealTime;
    public static double Now => Clock();
    /// <summary>The running engine has a network layer (1.9.3.2_MP), whether or not ours could attach to it.</summary>
    public static bool EngineHasNetwork { get; private set; }
    public static string Decision { get; private set; } = "not run";
    public static readonly List<string> Evidence = [];
    // ---- a short in-memory trace of multiplayer gameplay decisions (last 64), read by the two-process tests
    static readonly Queue<string> s_trace = new();
    static string s_lastTrace;
    /// <summary>Records one multiplayer decision (only while a session exists; single player records nothing). A repeat of
    /// the previous entry (a per-frame condition) is recorded once.</summary>
    public static void Trace(string text) {
        if (Role is not (ScNetRole.Host or ScNetRole.Client)) return;
        lock (s_trace) {
            if (text == s_lastTrace) return;
            s_lastTrace = text; s_trace.Enqueue($"{Time.RealTime:0.00} {text}"); while (s_trace.Count > 64) s_trace.Dequeue();
        }
    }
    public static string TraceText { get { lock (s_trace) return string.Join(" | ", s_trace); } }

    static Func<bool> s_serverRunning, s_clientRunning;
    public static ScNetRole Role {
        get {
            if (Transport is not null) return Transport.Role;
            if (!EngineHasNetwork) return ScNetRole.Standalone;
            // Refused network layer: the role still has to be known so a client does not run the game on its own.
            return s_serverRunning?.Invoke() == true ? ScNetRole.Host : s_clientRunning?.Invoke() == true ? ScNetRole.Client : ScNetRole.Offline;
        }
    }
    /// <summary>This process runs the real game (single player, MP host, MP engine without a session).</summary>
    public static bool IsAuthority => Role != ScNetRole.Client;
    public static bool IsRemoteClient => Role == ScNetRole.Client;
    public static bool IsHost => Role == ScNetRole.Host;
    /// <summary>A remote client that may not play CS weapons: no network layer, or its handshake was not accepted.</summary>
    public static bool ClientBlocked => IsRemoteClient && Transport?.Handshake != ScNetHandshake.Accepted;
    public static IReadOnlyList<ScNetPeer> Peers => Transport?.Peers ?? [];
    /// <summary>Whether this process reads this player's own devices. Always true outside multiplayer.</summary>
    public static bool IsLocal(ComponentPlayer player) => player is not null && (Transport is null ? !EngineHasNetwork || IsMainPlayerByReflection(player) : Transport.IsLocal(player));

    // ---------------------------------------------------------------- loading
    public static void Initialize(ModEntity entity) {
        Evidence.Clear(); Transport = null;
        try { Decide(entity); }
        catch (Exception e) { Transport = null; Decision = $"network layer refused: {e.GetType().Name}: {e.Message}"; }
        foreach (string line in Evidence) KnifeLog.Diagnostic("[ScCsgoNet] " + line);
        Log.Information("[ScCsgoNet] decision: " + Decision);
    }

    /// <summary>Called by the adapter once it is registered with the engine.</summary>
    public static void Attach(IScNetTransport transport) => Transport = transport;

    /// <summary>Why this process has no CS network layer although the engine has multiplayer (null when it has one, or on
    /// the standalone engine): shown to a host once, and to a blocked client.</summary>
    public static string RefusedReason { get; private set; }

    static void Decide(ModEntity entity) {
        RefusedReason = null; CompatStatus = "not run"; CommandGateInstalled = false;
        Type network = Type.GetType("Game.NetworkManager, Survivalcraft", false);
        Evidence.Add("Game.NetworkManager " + (network is null ? "absent" : "present"));
        if (network is null) { EngineHasNetwork = false; Decision = "standalone engine: the network adapter is not loaded"; return; }
        EngineHasNetwork = true;
        s_serverRunning = Getter(network, "IsServerRunning"); s_clientRunning = Getter(network, "IsClientRunning");
        Evidence.Add("mods: " + string.Join(", ", ModsManager.ModList.Select(m => m.modInfo?.PackageName + " " + m.modInfo?.Version)));
        var assemblies = AppDomain.CurrentDomain.GetAssemblies().GroupBy(a => a.GetName().Name).ToDictionary(g => g.Key, g => g.First());
        List<string> missing = CheckContract(network, assemblies, Contract, "transport");
        if (missing.Count > 0) { Refuse("multiplayer engine, but its Multiplayer contract differs (" + string.Join("; ", missing) + ")", "联机平台的 Multiplayer 组件与本模组不匹配"); return; }
        if (LoadPayload(entity, AdapterPayload) is not { } adapter) { Refuse("multiplayer engine, but " + AdapterPayload + " is not in this package", "安装包缺少联机组件"); return; }
        if (Transport is null) { Refuse("multiplayer engine: adapter loaded but did not attach", "联机组件未能挂接"); return; }
        Evidence.Add($"adapter {adapter.GetName().Name} {adapter.ManifestModule.ModuleVersionId} loaded from {AdapterPayload}");
        // The optional CompatNet part: never a reason to refuse the transport.
        if (!assemblies.ContainsKey("Survivalcraft.CompatNet")) CompatStatus = "absent";
        else {
            List<string> compatMissing = CheckContract(network, assemblies, CompatContract, "CompatNet");
            if (assemblies["Survivalcraft.CompatNet"].GetType("Game.CompatNetCore", false)?.GetField("CurrentProtocolVersion") is { } protocol
                && protocol.GetRawConstantValue() is byte version && version != CompatNetProtocol)
                compatMissing.Add($"CompatNet protocol {version} (corrections built for {CompatNetProtocol})");
            if (compatMissing.Count > 0) CompatStatus = "contract differs, corrections off: " + string.Join("; ", compatMissing);
            else {
                try {
                    CompatStatus = "loaded";
                    if (LoadPayload(entity, CompatPayload) is null) CompatStatus = CompatPayload + " is not in this package, corrections off";
                }
                catch (Exception e) { CompatStatus = $"corrections failed to load: {e.GetType().Name}: {e.Message}"; }
            }
        }
        Decision = "multiplayer engine: transport contract matched, adapter attached; CompatNet " + CompatStatus;
    }
    static void Refuse(string decision, string player) { Decision = decision + ": network layer refused"; RefusedReason = player; }
    static Assembly LoadPayload(ModEntity entity, string member) {
        byte[] bytes = null;
        entity?.GetFile(member, stream => bytes = ModsManager.StreamToBytes(stream));
        if (bytes is null) return null;
        Assembly assembly = Assembly.Load(bytes);
        assembly.GetTypes(); // resolve everything before publishing, as TacticalAppearanceIntegration does
        ModsManager.Dlls[assembly.FullName] = assembly;
        entity.HandleAssembly(assembly);
        return assembly;
    }

    static Func<bool> Getter(Type type, string property) {
        var p = type.GetProperty(property, BindingFlags.Public | BindingFlags.Static);
        return p is null ? null : () => { try { return p.GetValue(null) is true; } catch { return false; } };
    }
    static bool IsMainPlayerByReflection(ComponentPlayer player) =>
        player.PlayerData?.GetType().GetProperty("IsMainPlayer")?.GetValue(player.PlayerData) is true;

    // Every engine member the adapter binds to, as "Type::Member(signature)". A difference means the adapter would fail
    // to bind at run time, so it is not loaded at all. The transport needs the engine and its Multiplayer internal mod only.
    static readonly (string Assembly, string Type, string[] Members)[] Contract = [
        ("Survivalcraft", "Game.NetworkManager", ["Boolean get_IsServerRunning()", "Boolean get_IsClientRunning()", "IEnumerable`1 get_ServerSessions()", "Void Queue(Object)", "Object get_NetworkServer()"]),
        ("Survivalcraft", "Game.ClientSession", ["Int32 get_PlayerIndex()", "Guid get_NetworkGuid()", "NetworkState get_NetworkState()"]),
        ("Survivalcraft", "Game.PlayerData", ["Boolean get_IsMainPlayer()"]),
        ("Survivalcraft", "Game.ModLoader", ["Void OnNetworkPlayerStateChanged(NetworkState)", "Void SubsystemUpdate(SubsystemUpdate,Single)", "Void OnProjectDisposed()", "Void OnLoadingFinished(List`1)"]),
        ("Survivalcraft", "Game.ModLoader", ["Void OnNetworkTerrainSyncChunkList(TerrainUpdater,Int32,List`1)"]),
        ("Survivalcraft", "Game.TerrainChunk", ["field Int32[] Cells", "field TerrainChunkState ThreadState", "field TerrainChunkState State", "field Point2 Coords", "field Point2 Origin"]),
        ("Survivalcraft.Multiplayer", "Game.Network.Packet", ["Byte get_ID()", "Void Handle(Boolean)", "Void Serialize(PacketSerializer)", "Byte get_Channel()", "NetworkState get_MinNeedState()",
            "ClientSession get_From()", "Void set_To(ClientSession)", "Void set_Except(ClientSession)"]),
        ("Survivalcraft.Multiplayer", "Game.Network.PacketManager", ["Void RegisterPacket()"]),
        ("Survivalcraft.Multiplayer", "Game.Network.PacketSerializer", ["Void Value(Byte&)", "Void Value(UInt16&)", "Void Buffer(Byte[]&,Int32)"]),
        ("Survivalcraft.Multiplayer", "Game.Network.TerrainSyncChunkListPacket", []),
        ("Survivalcraft.Multiplayer", "Game.Network.TerrainChangeCellListPacket", []),
        ("Survivalcraft.Multiplayer", "Game.Network.CellChange", ["field Int32 X", "field Int32 Y", "field Int32 Z", "field Int32 Value"]),
    ];
    // What the optional corrections for the CompatNet internal mod bind to.
    static readonly (string Assembly, string Type, string[] Members)[] CompatContract = [
        ("Survivalcraft.CompatNet", "Game.ICompatNetAdapter", ["UInt16 get_AdapterId()", "String get_AdapterName()", "Void OnPreloadAssemblies(IList`1)",
            "Void HandleRequest(CompatNetPacket,PlayerData)", "Void HandleBroadcast(CompatNetPacket)", "Void OnServerUpdate(Single)",
            "Boolean ShouldSkipServerUpdate(IUpdateable)", "Boolean ShouldSkipClientUpdate(IUpdateable)"]),
        ("Survivalcraft.CompatNet", "Game.CompatNetAdapterRegistry", ["Void Register(ICompatNetAdapter)", "Void Reset()", "IReadOnlyCollection`1 get_Adapters()", "Boolean TryGet(UInt16,ICompatNetAdapter&)"]),
        ("Survivalcraft.CompatNet", "Game.Network.CompatNetPacket", ["field UInt16 AdapterId", "field UInt16 Operation", "field Byte[] Payload"]),
    ];

    static List<string> CheckContract(Type network, Dictionary<string, Assembly> assemblies, (string Assembly, string Type, string[] Members)[] contract, string name) {
        var missing = new List<string>();
        foreach (var (assemblyName, typeName, members) in contract) {
            Type type = assemblyName == "Survivalcraft" ? network.Assembly.GetType(typeName, false) : assemblies.GetValueOrDefault(assemblyName)?.GetType(typeName, false);
            if (type is null) { missing.Add($"{assemblyName}:{typeName} absent"); continue; }
            var have = DescribeMembers(type);
            foreach (string member in members) if (!have.Contains(member)) missing.Add($"{typeName}::{member}");
            // A new abstract member would make the adapter's class (an interface implementation or a packet) fail to load.
            if (type.IsInterface || type.IsAbstract && !type.IsSealed)
                foreach (MethodInfo m in type.GetMethods().Where(m => m.IsAbstract && !members.Contains(DescribeMethod(m))))
                    missing.Add($"{typeName} has new abstract member {DescribeMethod(m)}");
        }
        Evidence.Add($"{name} contract: {contract.Sum(c => c.Members.Length)} members checked, {missing.Count} differences");
        return missing;
    }

    static HashSet<string> DescribeMembers(Type type) {
        const BindingFlags all = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        var set = new HashSet<string>(type.GetMethods(all).Select(DescribeMethod));
        foreach (FieldInfo f in type.GetFields(all)) set.Add($"field {f.FieldType.Name} {f.Name}");
        return set;
    }
    static string DescribeMethod(MethodInfo m) => $"{m.ReturnType.Name} {m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))})";

    // ---------------------------------------------------------------- messages
    public delegate void ServerHandler(ScNetPeer from, ComponentPlayer player, ScNetReader message);
    public delegate void ClientHandler(ScNetReader message);
    static readonly Dictionary<ushort, ServerHandler> s_onServer = [];
    static readonly Dictionary<ushort, ClientHandler> s_onClient = [];
    // mpc3 feedback (2026-10-02): the shot confirmation was given op 42, which the hit feedback already used. The later
    // registration replaced the earlier one without a word, so every confirmation was read as a hit (a white hit marker
    // for shots at nothing) and no shot was ever confirmed. One number now has one meaning: a second, different handler
    // for a number is refused and recorded (the first stands), in either direction; registering the same handler again
    // (a second initialisation) is the same registration. The offline checks run the production registration of every
    // module and require this list to be empty.
    /// <summary>Registrations that were refused because the number already belongs to another handler.</summary>
    public static readonly List<string> Conflicts = [];
    static string Name(Delegate handler) => $"{handler.Method.DeclaringType?.FullName}.{handler.Method.Name}";
    static bool Claim(ushort op, Delegate handler, string direction) {
        if (op < FirstGameOp) throw new ArgumentOutOfRangeException(nameof(op));
        Delegate held = s_onServer.TryGetValue(op, out var a) ? a : s_onClient.TryGetValue(op, out var b) ? b : null;
        if (held is null || held.Method == handler.Method) return true;
        string conflict = $"op {op} ({direction}): {Name(handler)} refused, the number belongs to {Name(held)}";
        Conflicts.Add(conflict);
        Log.Error("[ScCsgoNet] message number conflict: " + conflict);
        return false;
    }
    /// <summary>Handles a message a remote client sent (server only; the sender's player is resolved and alive-checked by the caller).</summary>
    public static void OnServer(ushort op, ServerHandler handler) { if (Claim(op, handler, "client to server")) s_onServer[op] = handler; }
    /// <summary>Handles a message the server sent (remote clients only).</summary>
    public static void OnClient(ushort op, ClientHandler handler) { if (Claim(op, handler, "server to client")) s_onClient[op] = handler; }
    /// <summary>Every registered number and its handler, both directions (test diagnostics).</summary>
    public static IEnumerable<(ushort Op, string Direction, string Handler)> Ops =>
        s_onServer.Select(p => (p.Key, "client to server", Name(p.Value))).Concat(s_onClient.Select(p => (p.Key, "server to client", Name(p.Value)))).OrderBy(o => o.Key);
    /// <summary>The core's own handlers, in the one order the mod loader registers them (the offline checks call the same).</summary>
    public static void RegisterCore() {
        if (s_coreRegistered) return;                                  // a second initialisation registers (and subscribes) nothing twice
        s_coreRegistered = true;
        ScNetMirror.Register();
        ScNetGuns.Register();
        ScNetFeedback.Register();
        ScNetWorkbench.Register();
        ScNetGrenades.Register();
        ScNetC4.Register();
        ScNetPresentation.Register();
    }
    static bool s_coreRegistered;
    /// <summary>Messages dropped by a handler (malformed) and messages a handler did not read to the end (test diagnostics).</summary>
    public static int Dropped, Unread;

    /// <summary>Client → server. False when this is not an accepted remote client.</summary>
    public static bool Send(ushort op, Action<ScNetWriter> write) {
        if (!IsRemoteClient || ClientBlocked) return false;
        var w = new ScNetWriter(); write?.Invoke(w);
        return Transport.SendToServer(op, w.ToArray());
    }
    /// <summary>Server → one remote client.</summary>
    public static void SendTo(ScNetPeer peer, ushort op, Action<ScNetWriter> write) {
        if (!IsHost || peer is null || Transport is null) return;
        var w = new ScNetWriter(); write?.Invoke(w);
        Transport.SendTo(peer, op, w.ToArray());
    }
    /// <summary>Server → one remote client; false when the message could not be queued (it is the caller's to send again).</summary>
    public static bool TrySendTo(ScNetPeer peer, ushort op, Action<ScNetWriter> write) {
        if (!IsHost || peer is null || Transport is null) return false;
        var w = new ScNetWriter(); write?.Invoke(w);
        return Transport.SendTo(peer, op, w.ToArray());
    }
    /// <summary>Server → every accepted remote client; false when any of them could not be queued (true with no client).</summary>
    public static bool TryBroadcast(ushort op, Action<ScNetWriter> write) {
        if (!IsHost || Transport is null) return false;
        var peers = Transport.Peers;
        if (peers.Count == 0) return true;
        var w = new ScNetWriter(); write?.Invoke(w);
        byte[] payload = w.ToArray(); bool all = true;
        for (int i = 0; i < peers.Count; i++) all &= Transport.SendTo(peers[i], op, payload);
        return all;
    }
    /// <summary>Server → every accepted remote client (optionally but one).</summary>
    public static void Broadcast(ushort op, Action<ScNetWriter> write, ScNetPeer except = null) {
        if (!IsHost || Transport is null || Transport.Peers.Count == 0 || Transport.Peers.Count == 1 && ReferenceEquals(Transport.Peers[0], except)) return;
        var w = new ScNetWriter(); write?.Invoke(w);
        Transport.Broadcast(op, w.ToArray(), except);
    }
    /// <summary>The accepted peer that controls this player (server), or null for the host's own player.</summary>
    public static ScNetPeer PeerOf(ComponentPlayer player) {
        if (player?.PlayerData is null || Transport is null) return null;
        int index = player.PlayerData.PlayerIndex;
        foreach (var peer in Transport.Peers) if (peer.PlayerIndex == index) return peer;
        return null;
    }
    /// <summary>A player whose actions arrive over the network (server side): an accepted peer's player.</summary>
    public static bool IsRemoteDriven(ComponentPlayer player) => IsHost && PeerOf(player) is not null;

    // Called by the adapter.
    public static void ReceiveOnServer(ScNetPeer from, ushort op, byte[] payload) {
        if (!s_onServer.TryGetValue(op, out var handler)) { KnifeDiagnostics.WarnOnce("scnet-server-op-" + op, $"[ScCsgoNet] no server handler for op {op}"); return; }
        var player = from.Player(GameManager.Project);
        if (player is null || player.ComponentHealth is null) return;
        var reader = new ScNetReader(payload);
        try { handler(from, player, reader); }
        catch (Exception e) { Dropped++; KnifeDiagnostics.WarnOnce("scnet-server-fail-" + op, $"[ScCsgoNet] message {op} from {from} dropped: {e.GetType().Name}: {e.Message}"); return; }
        if (!reader.End) { Unread++; KnifeDiagnostics.WarnOnce("scnet-server-unread-" + op, $"[ScCsgoNet] message {op} from {from} has bytes its handler did not read"); }
    }
    public static void ReceiveOnClient(ushort op, byte[] payload) {
        if (!s_onClient.TryGetValue(op, out var handler)) { KnifeDiagnostics.WarnOnce("scnet-client-op-" + op, $"[ScCsgoNet] no client handler for op {op}"); return; }
        var reader = new ScNetReader(payload);
        try { handler(reader); }
        catch (Exception e) { Dropped++; KnifeDiagnostics.WarnOnce("scnet-client-fail-" + op, $"[ScCsgoNet] message {op} dropped: {e.GetType().Name}: {e.Message}"); return; }
        if (!reader.End) { Unread++; KnifeDiagnostics.WarnOnce("scnet-client-unread-" + op, $"[ScCsgoNet] message {op} has bytes its handler did not read"); }
    }

    /// <summary>Server: a remote client's handshake was accepted (systems send it their full state).</summary>
    public static event Action<ScNetPeer> PeerAccepted;
    /// <summary>Server: an accepted peer disconnected or was rejected.</summary>
    public static event Action<ScNetPeer> PeerLeft;
    /// <summary>Client: this client's handshake was accepted.</summary>
    public static event Action ClientAccepted;
    public static void NotifyPeerAccepted(ScNetPeer peer) => Raise(() => PeerAccepted?.Invoke(peer), "peer-accepted");
    public static void NotifyPeerLeft(ScNetPeer peer) => Raise(() => PeerLeft?.Invoke(peer), "peer-left");
    public static void NotifyClientAccepted() => Raise(() => ClientAccepted?.Invoke(), "client-accepted");
    static void Raise(Action action, string what) {
        try { action(); }
        catch (Exception e) { KnifeDiagnostics.WarnOnce("scnet-event-" + what, $"[ScCsgoNet] {what} handler failed: {e}"); }
    }

    /// <summary>The one line a blocked client shows when a CS action is refused.</summary>
    public static string BlockedMessage => Transport is null
        ? $"联机：本机的 CS 武器联机组件不可用（{RefusedReason ?? "未加载"}），CS 武器在此服务器上已停用。"
        : Transport.Handshake == ScNetHandshake.Pending ? "联机：正在与服务器核对 CS 武器版本……"
        : "联机：" + Transport.HandshakeDetail;

    /// <summary>Tells a blocked client's own player why a CS action did nothing, at most once every few seconds.</summary>
    public static void TellBlocked(ComponentPlayer player) {
        if (player?.ComponentGui is null || !IsLocal(player) || Time.RealTime < s_blockedToldAt + 4) return;
        s_blockedToldAt = Time.RealTime;
        try { player.ComponentGui.DisplaySmallMessage(BlockedMessage, Color.Red, true, false); } catch { }
    }
    static double s_blockedToldAt = double.NegativeInfinity;

    /// <summary>A host whose CS network layer was refused runs the game alone: every client's CS weapons stay switched
    /// off. The host is told once per world, when the first other player is present (mp-user-logs-20261002: an Android host
    /// refused the layer silently and both clients only saw a timeout).</summary>
    public static void HostTick(Project project) {
        if (Transport is not null || !EngineHasNetwork || RefusedReason is null || s_serverRunning?.Invoke() != true) { if (s_serverRunning?.Invoke() != true) s_hostTold = null; return; }
        if (ReferenceEquals(s_hostTold, project)) return;
        var players = project?.FindSubsystem<SubsystemPlayers>(false);
        if (players is null || players.ComponentPlayers.Count < 2 || players.ComponentPlayers.FirstOrDefault(IsMainPlayerByReflection) is not { ComponentGui: { } gui }) return;
        s_hostTold = project;
        string text = $"联机：本机（房主）的 CS 武器联机组件未加载（{RefusedReason}），其他玩家的 CS 武器无法使用。";
        Log.Warning("[ScCsgoNet] host: " + text + " " + Decision);
        try { gui.DisplaySmallMessage(text, Color.Red, true, false); } catch { }
    }
    static object s_hostTold;
}
