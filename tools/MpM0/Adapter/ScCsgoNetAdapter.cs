using System.Reflection;
using System.Text.Json;
using Engine;
using Game.Network;
namespace Game;

// current-direction-20260929 §6 M0: the separate network adapter layer. Compiled against the 1.9.3.2_MP engine and its
// CompatNet internal mod; shipped inside the package as Net/ScCsgoNet.bin and loaded only by ScNetProbe after the
// CompatNet contract was checked. It carries no gameplay yet: a handshake (CS network protocol, item layout, gun record
// schema and the exact core build must match on both ends) and one read-only, server-confirmed status request.
// Server state lives in memory only; nothing here writes to the world.
public sealed class ScCsgoNetAdapter : ModLoader, ICompatNetAdapter, IScNetBridge {
    /// <summary>"SC". CompatNet ids are wire protocol; the built-in adapters use 1-20.</summary>
    public const ushort Id = 0x5343;
    public const ushort OpHello = 1, OpHelloResult = 2, OpStatus = 3, OpStatusResult = 4;
    public const string Protocol = "zh667.ScCsgoKnives/net";
    public const int ProtocolVersion = 1;
    public const float HandshakeTimeout = 10;
    public const int RepliesKept = 32;

    public sealed record Hello(string Protocol, int Version, int Layout, int Schema, string ModVersion, string CoreBuild, int Request);
    public sealed record HelloResult(int Request, bool Accepted, string Reason, Hello Server);
    public sealed record StatusQuery(int Request);
    public sealed record StatusResult(int Request, bool Ok, string Reason, int PlayerIndex, string NetworkGuid, double GameTime, float Health, string Protection, int RemoteClients);

    sealed class Peer {
        public bool Accepted;
        public readonly Dictionary<int, byte[]> Replies = [];
        public readonly Queue<int> Order = new();
    }

    public ushort AdapterId => Id;
    public string AdapterName => "zh667.ScCsgoKnives network adapter (M0)";

    readonly Dictionary<ClientSession, Peer> m_peers = new(ReferenceEqualityComparer.Instance);
    ScNetHandshake m_handshake = ScNetHandshake.NotApplicable;
    string m_detail = "";
    bool m_helloDue;
    double m_sentAt;
    int m_nextRequest = 1;
    string m_lastStatus = "";

    public override void __ModInitialize() {
        CompatNetAdapterRegistry.Register(this);
        ModsManager.RegisterHook("OnNetworkPlayerStateChanged", this);
        ModsManager.RegisterHook("SubsystemUpdate", this);
        ModsManager.RegisterHook("OnProjectDisposed", this);
        ScNetProbe.Attach(this);
        Log.Information($"[ScCsgoNet] adapter {Id} registered with CompatNet");
    }

    // ---- IScNetBridge
    public ScNetRole Role => NetworkManager.IsServerRunning ? ScNetRole.Host : NetworkManager.IsClientRunning ? ScNetRole.Client : ScNetRole.Offline;
    public int RemoteClients => NetworkManager.IsServerRunning ? NetworkManager.ServerSessions.Count() : 0;
    public ScNetHandshake Handshake => Role == ScNetRole.Client ? m_handshake : ScNetHandshake.NotApplicable;
    public string HandshakeDetail => m_detail;
    public string LastStatus => m_lastStatus;
    public int RequestStatus() => Role == ScNetRole.Client && m_handshake == ScNetHandshake.Accepted ? RequestStatusForTest(m_nextRequest++) : -1;

    // ---- test hooks (M0 evidence only)
    /// <summary>Sends a status request with this id whatever the handshake state (repeats test de-duplication, a
    /// request without handshake tests the server's refusal).</summary>
    public int RequestStatusForTest(int request) => Send(OpStatus, new StatusQuery(request)) ? request : -1;
    /// <summary>Sends a hello, optionally claiming another item layout (a deliberately incompatible peer).</summary>
    public int SendHelloForTest(int layout) {
        Hello hello = Mine(m_nextRequest++);
        if (layout >= 0) hello = hello with { Layout = layout };
        m_handshake = ScNetHandshake.Pending; m_detail = "hello sent"; m_sentAt = Time.RealTime;
        return Send(OpHello, hello) ? hello.Request : -1;
    }

    // ---- identity
    static Assembly Core => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "ScCsgoKnives");
    static int CoreConstant(string type, string field) => Core?.GetType(type, false)?.GetField(field)?.GetRawConstantValue() is int v ? v : -1;
    static Hello Mine(int request) => new(Protocol, ProtocolVersion, CoreConstant("Game.ScGunEncoding", "Layout"), CoreConstant("Game.ScGunRegistry", "Schema"),
        ModsManager.ModList.FirstOrDefault(m => m.modInfo?.PackageName == "zh667.ScCsgoKnives")?.modInfo?.Version ?? "", Core?.ManifestModule.ModuleVersionId.ToString() ?? "", request);
    /// <summary>Why a peer's hello is not compatible with this server, or null.</summary>
    public static string Incompatibility(Hello peer, Hello server) =>
        peer is null ? "malformed hello"
        : peer.Protocol != server.Protocol || peer.Version != server.Version ? $"CS network protocol {peer.Protocol}/{peer.Version}, server {server.Protocol}/{server.Version}"
        : peer.Layout != server.Layout ? $"item layout {peer.Layout}, server {server.Layout}"
        : peer.Schema != server.Schema ? $"gun record schema {peer.Schema}, server {server.Schema}"
        : peer.ModVersion != server.ModVersion || peer.CoreBuild != server.CoreBuild ? $"core {peer.ModVersion} build {peer.CoreBuild}, server {server.ModVersion} build {server.CoreBuild}"
        : null;

    // ---- client
    static ComponentPlayer MainPlayer => GameManager.Project?.FindSubsystem<SubsystemPlayers>(false)?.MainPlayer;
    bool Send(ushort operation, object message) {
        ComponentPlayer player = MainPlayer;
        bool sent = player is not null && CompatNetCore.QueueRequest(player, Id, operation, JsonSerializer.SerializeToUtf8Bytes(message, message.GetType()));
        Log.Information($"[ScCsgoNet] client: op {operation} {JsonSerializer.Serialize(message, message.GetType())} {(sent ? "sent" : "NOT sent")}");
        return sent;
    }

    public override void OnNetworkPlayerStateChanged(NetworkState state) {
        if (state != NetworkState.ProjectLoaded || !NetworkManager.IsClientRunning || NetworkManager.IsServerRunning) return;
        m_helloDue = true; m_handshake = ScNetHandshake.Pending; m_detail = "waiting for the main player";
    }

    public override void SubsystemUpdate(SubsystemUpdate subsystemUpdate, float dt) {
        if (NetworkManager.IsServerRunning || !NetworkManager.IsClientRunning) return;
        if (m_helloDue && MainPlayer is not null) {
            m_helloDue = false; m_sentAt = Time.RealTime; m_detail = "hello sent";
            Send(OpHello, Mine(m_nextRequest++));
        }
        if (m_handshake == ScNetHandshake.Pending && !m_helloDue && Time.RealTime - m_sentAt > HandshakeTimeout) {
            m_handshake = ScNetHandshake.TimedOut;
            m_detail = $"no answer from the server within {HandshakeTimeout} s (server without the CS network adapter?): CS network features refused";
            Log.Warning("[ScCsgoNet] client: " + m_detail);
        }
    }

    public void HandleBroadcast(CompatNetPacket packet) {
        if (NetworkManager.IsServerRunning) return;
        switch (packet.Operation) {
            case OpHelloResult:
                HelloResult result = Read<HelloResult>(packet.Payload);
                if (result is null) return;
                m_handshake = result.Accepted ? ScNetHandshake.Accepted : ScNetHandshake.Rejected;
                m_detail = result.Accepted ? $"accepted by server core {result.Server?.ModVersion} build {result.Server?.CoreBuild}" : "rejected: " + result.Reason + ": CS network features refused";
                Log.Information($"[ScCsgoNet] client: hello {result.Request} {m_detail}");
                break;
            case OpStatusResult:
                m_lastStatus = System.Text.Encoding.UTF8.GetString(packet.Payload);
                Log.Information("[ScCsgoNet] client: status " + m_lastStatus);
                break;
        }
    }

    // ---- server
    public void HandleRequest(CompatNetPacket packet, PlayerData playerData) {
        if (!NetworkManager.IsServerRunning || packet.From is not { } from) return;
        if (!m_peers.TryGetValue(from, out Peer peer)) m_peers[from] = peer = new Peer();
        switch (packet.Operation) {
            case OpHello: {
                Hello hello = Read<Hello>(packet.Payload), server = Mine(hello?.Request ?? -1);
                string reason = Incompatibility(hello, server);
                peer.Accepted = reason is null;
                Log.Information($"[ScCsgoNet] server: hello {hello?.Request} from player {playerData.PlayerIndex} ({from.NetworkGuid}): {reason ?? "accepted"}");
                Reply(from, OpHelloResult, new HelloResult(hello?.Request ?? -1, reason is null, reason ?? "", server));
                break;
            }
            case OpStatus: {
                StatusQuery query = Read<StatusQuery>(packet.Payload);
                if (query is null) return;
                if (!peer.Accepted) {
                    Log.Information($"[ScCsgoNet] server: status {query.Request} from player {playerData.PlayerIndex} refused (no accepted handshake)");
                    Reply(from, OpStatusResult, new StatusResult(query.Request, false, "no accepted handshake", playerData.PlayerIndex, from.NetworkGuid.ToString(), 0, 0, "", 0));
                    return;
                }
                if (peer.Replies.TryGetValue(query.Request, out byte[] cached)) {
                    Log.Information($"[ScCsgoNet] server: status {query.Request} from player {playerData.PlayerIndex} is a repeat: cached answer resent, nothing re-evaluated");
                    CompatNetCore.QueueBroadcast(Id, OpStatusResult, cached, from, null);
                    return;
                }
                byte[] answer = JsonSerializer.SerializeToUtf8Bytes(Status(query.Request, playerData, from));
                peer.Replies[query.Request] = answer; peer.Order.Enqueue(query.Request);
                while (peer.Order.Count > RepliesKept) peer.Replies.Remove(peer.Order.Dequeue());
                Log.Information($"[ScCsgoNet] server: status {query.Request} from player {playerData.PlayerIndex} answered");
                CompatNetCore.QueueBroadcast(Id, OpStatusResult, answer, from, null);
                break;
            }
        }
    }

    /// <summary>Read-only: game time, the sender's identity and health, and its CS protection as stored.</summary>
    StatusResult Status(int request, PlayerData playerData, ClientSession from) {
        var project = GameManager.Project;
        var armor = project?.FindSubsystem<SubsystemScArmor>(false);
        string key = SubsystemScArmor.PlayerKey(playerData.PlayerIndex);
        return new StatusResult(request, true, "", playerData.PlayerIndex, from.NetworkGuid.ToString(),
            project?.FindSubsystem<SubsystemGameInfo>(false)?.TotalElapsedGameTime ?? 0,
            playerData.ComponentPlayer?.ComponentHealth?.Health ?? -1,
            armor is null ? "no protection store" : armor.Readable(key) ? armor.Get(key).Encode() : "unreadable",
            RemoteClients);
    }

    static void Reply(ClientSession to, ushort operation, object message) =>
        CompatNetCore.QueueBroadcast(Id, operation, JsonSerializer.SerializeToUtf8Bytes(message, message.GetType()), to, null);

    static T Read<T>(byte[] payload) where T : class {
        try { return payload is null ? null : JsonSerializer.Deserialize<T>(payload); }
        catch (JsonException) { return null; }
    }

    public override void OnProjectDisposed() {
        m_peers.Clear(); m_helloDue = false; m_handshake = ScNetHandshake.NotApplicable; m_detail = ""; m_lastStatus = "";
    }

    // ---- ICompatNetAdapter members this adapter does not use
    public override void OnPreloadAssemblies(IList<ModAssemblyImage> assemblies) { }
    public void OnServerUpdate(float dt) { }
    public bool ShouldSkipServerUpdate(IUpdateable updateable) => false;
    public bool ShouldSkipClientUpdate(IUpdateable updateable) => false;
}
