using System.Reflection;
using System.Text.Json;
using Engine;
using Game.Network;
namespace Game;

// current-direction-20260929 §6: the separate network adapter layer. Compiled against the 1.9.3.2 multiplayer engine and
// its Multiplayer internal mod; shipped inside the package as Net/ScCsgoNet.bin and loaded only by ScNet after the engine
// contract was checked. It is the transport for the core (IScNetTransport): a handshake (CS network protocol, item
// layout, gun record schema, the gameplay build and the agents must match: ScNetIdentity, so Full and split Lite of one
// release play together), the accepted peers, and requests/broadcasts carrying the core's messages. It holds no
// gameplay; the server keeps its peer list in memory only.
//
// mp-user-logs-20261002: the wire is this adapter's own packet (ScCsgoPacket) registered on the Multiplayer mod's public
// packet table. It no longer rides the CompatNet internal mod's packet 98: the platform's Android build ships Multiplayer
// only, so an Android host could not load the former adapter at all and every client's handshake timed out. The sender of
// a request is the connection the platform delivered it on (Packet.From), never an index the client wrote.
public sealed class ScCsgoNetAdapter : ModLoader, IScNetTransport, IScNetRewind, IScNetRewindInfo, IScNetInventorySync {
    /// <summary>"SC": kept as the adapter's name in logs (the former CompatNet adapter id).</summary>
    public const ushort Id = 0x5343;
    public const ushort OpHello = 1, OpHelloResult = 2, OpStatus = 3, OpStatusResult = 4, OpTerrainVerify = 5, OpTerrainReport = 6;
    public const string Protocol = "zh667.ScCsgoKnives/net";
    /// <summary>3 (2026-10-01): the gameplay build replaces the exact module; agents and edition added.
    /// 4 (2026-10-02): own packet on the Multiplayer packet table instead of CompatNet's; presentation messages.
    /// 5 (2026-10-02, mp-state-consistency): gun input carries its sequence and the selection it is for; shot
    /// confirmations (OpShotAck) and record requests (OpWant) added. A peer of protocol 4 is refused with that reason.
    /// 6 (2026-10-02, mpc3 feedback): protocol 5 gave the shot confirmation the hit feedback's number (42), so it never
    /// arrived and drew hit markers instead. The confirmation is now part of the record message, shots are settled by
    /// number (fired / skipped) instead of by time, the gun input carries the client's shot count and its reload request,
    /// and the server answers reloads (OpReload 37). A peer of protocol 5 or earlier is refused with that reason.
    /// 7 (2026-10-03, deathmatch-addon): the hello names the mode the loaded world runs (id, package build, rules
    /// fingerprint); a server whose world runs a mode refuses a peer that does not have exactly that. Ordinary worlds
    /// compare nothing new. A peer of protocol 6 or earlier is refused with that reason.</summary>
    public const int ProtocolVersion = 7;
    /// <summary>A hello is sent again every <see cref="HelloRetry"/> s until answered; after <see cref="HandshakeTimeout"/> s
    /// the client is told (CS weapons off) and keeps asking slowly (<see cref="SlowRetry"/> s, <see cref="SlowRetries"/>
    /// times): a server that answers late is still accepted.</summary>
    public const float HandshakeTimeout = 12, HelloRetry = 3, SlowRetry = 15;
    public const int SlowRetries = 8;
    public const int RepliesKept = 32;

    public sealed record Hello(string Protocol, int Version, int Layout, int Schema, string ModVersion, string CoreBuild, int Request, string Agents = "", string Edition = "", string Mode = "");
    public sealed record HelloResult(int Request, bool Accepted, string Reason, Hello Server);
    public sealed record StatusQuery(int Request);
    public sealed record StatusResult(int Request, bool Ok, string Reason, int PlayerIndex, string NetworkGuid, double GameTime, float Health, string Protection, int RemoteClients);

    sealed class Session {
        public ScNetPeer Peer;
        public readonly Dictionary<int, byte[]> Replies = [];
        public readonly Queue<int> Order = new();
    }

    public static ScCsgoNetAdapter Instance { get; private set; }
    /// <summary>The handshake's clock (tools/NetLoopCheck replaces it to walk through the retries and the timeout).</summary>
    public static Func<double> Now = () => Time.RealTime;
    /// <summary>Hellos this client sent in the current session (test diagnostics).</summary>
    public int HelloAttempts => m_helloAttempts >= int.MaxValue / 2 ? 1 : m_helloAttempts + m_slowAttempts;

    readonly Dictionary<ClientSession, Session> m_sessions = new(ReferenceEqualityComparer.Instance);
    readonly List<ScNetPeer> m_peers = [];
    ScNetHandshake m_handshake = ScNetHandshake.NotApplicable;
    string m_detail = "";
    bool m_helloDue;
    double m_sentAt, m_firstSentAt;
    int m_helloAttempts, m_slowAttempts;
    int m_nextRequest = 1;
    string m_lastStatus = "";
    /// <summary>The packet is on the platform's table (null until tried; a text when it could not be registered).</summary>
    public string PacketStatus { get; private set; }
    public bool PacketRegistered => PacketStatus == "registered";

    public override void __ModInitialize() {
        Instance = this;
        ModsManager.RegisterHook("OnNetworkPlayerStateChanged", this);
        ModsManager.RegisterHook("SubsystemUpdate", this);
        ModsManager.RegisterHook("OnProjectDisposed", this);
        ModsManager.RegisterHook("OnNetworkTerrainSyncChunkList", this);
        ModsManager.RegisterHook("OnLoadingFinished", this);
        ScNet.Attach(this);
        Log.Information($"[ScCsgoNet] adapter attached (packet {ScCsgoPacket.PacketId}, protocol {ProtocolVersion})");
        try { ScPlatformCompat.Install(this); }
        catch (Exception e) { Log.Warning("[ScCsgoNet] platform corrections not installed: " + e); }
    }

    /// <summary>The Multiplayer mod rebuilds its packet table in its own initialisation; every mod is initialised by now.</summary>
    public override void OnLoadingFinished(List<Action> actions) => EnsurePacket();

    /// <summary>Registers the packet once; checked again before the first use of a session (a table rebuilt later would
    /// have dropped it). A taken id is reported, never overwritten.</summary>
    public bool EnsurePacket() {
        try {
            if (PacketStatus == "registered" && ScCsgoPacket.OnTable() != false) return true;
            if (ScCsgoPacket.OnTable() == true) { PacketStatus = "registered"; return true; }
            PacketManager.RegisterPacket<ScCsgoPacket>();
            PacketStatus = "registered";
            Log.Information($"[ScCsgoNet] packet {ScCsgoPacket.PacketId} registered on the Multiplayer packet table");
            return true;
        }
        catch (Exception e) {
            string status = $"packet {ScCsgoPacket.PacketId} not registered: {e.GetType().Name}: {e.Message}";
            if (PacketStatus != status) Log.Warning("[ScCsgoNet] " + status);
            PacketStatus = status; return false;
        }
    }

    // ---- platform: late-join terrain reconciliation (server observes every chunk list the platform sends)
    public readonly ScTerrainReconcile Terrain = new();
    public string PlatformSummary => ScPlatformCompat.Summary;
    public override void OnNetworkTerrainSyncChunkList(TerrainUpdater updater, int playerIndex, List<TerrainChunk> chunks) {
        if (ScPlatformCompat.TerrainInstalled && NetworkManager.IsServerRunning && chunks is not null) Terrain.Sent(playerIndex, chunks);
    }
    static ClientSession SessionOf(int playerIndex) => NetworkManager.ServerSessions.FirstOrDefault(s => s.PlayerIndex == playerIndex);
    bool m_noticeDue;

    // ---- IScNetTransport
    public ScNetRole Role => NetworkManager.IsServerRunning ? ScNetRole.Host : NetworkManager.IsClientRunning ? ScNetRole.Client : ScNetRole.Offline;
    public ScNetHandshake Handshake => Role == ScNetRole.Client ? m_handshake : ScNetHandshake.NotApplicable;
    public string HandshakeDetail => m_detail;
    public IReadOnlyList<ScNetPeer> Peers => m_peers;
    public bool IsLocal(ComponentPlayer player) => player?.PlayerData?.IsMainPlayer == true;

    // ---- lag compensation for hit-scan shots (the engine compensates only its own projectiles)
    /// <summary>Where <paramref name="body"/> may have stood on <paramref name="shooter"/>'s screen: the engine's server-side
    /// body history sampled across the same window its own projectile compensation unions (RewindWindowStart..End, i.e.
    /// RemoteBodyDelay plus up to one snapshot interval), each moved back by the shot's own trip to the server. None for
    /// the shooter itself or a body without history (it is then tested where it is).</summary>
    int IScNetRewind.TryRewound(ComponentBody body, ComponentPlayer shooter, Vector3[] positions) {
        if (s_rewindBroken || positions is null || positions.Length == 0 || body is null || shooter?.ComponentBody is null || body == shooter.ComponentBody || !NetworkManager.IsServerRunning) return 0;
        // The body history is not part of the transport's contract: a platform build without it (the members are bound
        // when Rewound is first compiled) loses the compensation, never the network layer.
        try { return Rewound(body, shooter, positions); }
        catch (Exception e) { s_rewindBroken = true; Log.Warning("[ScCsgoNet] body history unavailable, shots are not compensated: " + e.Message); return 0; }
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int Rewound(ComponentBody body, ComponentPlayer shooter, Vector3[] positions) {
        double now = Time.RealTime - OneWaySeconds(shooter);
        int n = 0;
        for (int i = 0; i < positions.Length; i++) {
            double f = positions.Length == 1 ? .5 : (double)i / (positions.Length - 1);
            double at = now - (NetworkBodyHistory.RewindWindowStart + (NetworkBodyHistory.RewindWindowEnd - NetworkBodyHistory.RewindWindowStart) * f);
            if (!NetworkBodyHistory.TryGetBoxAt(body, at, out BoundingBox box)) return 0;
            positions[n++] = new Vector3((box.Min.X + box.Max.X) / 2, box.Min.Y, (box.Min.Z + box.Max.Z) / 2);
        }
        return n;
    }
    static bool s_rewindBroken;
    string IScNetRewindInfo.Describe(ComponentPlayer shooter) {
        string ping = "?";
        try { ping = PingText(shooter); }
        catch (Exception e) { ping = "ping unreadable: " + e.GetType().Name; }
        return $"window {NetworkBodyHistory.RewindWindowStart:0.000}-{NetworkBodyHistory.RewindWindowEnd:0.000} oneway {OneWaySeconds(shooter):0.000} {ping}";
    }
    // The direct server's connections carry a ping; a relay host's sessions do not (0: the shot's own trip is not added).
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static string PingText(ComponentPlayer shooter) {
        if (NetworkManager.NetworkServer is NetworkServer server)
            foreach (var pair in server.ClientSessions)
                if (pair.Value?.PlayerIndex == shooter?.PlayerData.PlayerIndex) return $"ping {pair.Key.Ping} rtt {pair.Key.RoundTripTime}";
        return "no direct connection (relay or none)";
    }
    static bool s_pingBroken;
    static double OneWaySeconds(ComponentPlayer shooter) {
        if (s_pingBroken) return 0;
        try { return DirectOneWay(shooter); }
        catch (Exception) { s_pingBroken = true; return 0; }
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static double DirectOneWay(ComponentPlayer shooter) {
        if (NetworkManager.NetworkServer is not NetworkServer server) return 0;
        int index = shooter.PlayerData.PlayerIndex;
        foreach (var pair in server.ClientSessions) if (pair.Value?.PlayerIndex == index) return Math.Clamp(pair.Key.Ping / 1000.0, 0, 1);
        return 0;
    }

    // ---- IScNetInventorySync: the platform's own inventory replication, for slot writes the engine does not announce
    // (a creative inventory's AddSlotItems, a direct slot write). The same packet the platform sends after a drag
    // between inventories: the whole inventory, addressed by its entity and component type, to every client. Queued by
    // ScNetSlots.EndOfFrame right after the records the slots name. Not part of the transport's contract: a platform
    // build without these members loses this replication (said once, loudly), never the network layer.
    static bool s_inventoryBroken;
    /// <summary>"available" or why not (test diagnostics, written to the log once at the first use).</summary>
    public static string InventorySyncStatus { get; private set; } = "not used yet";
    bool IScNetInventorySync.Publish(IInventory inventory) {
        // Only an entity's component is addressed exactly on the other side (entity id + component type). A provider's
        // own storage object is its provider's bridge's to replicate.
        if (s_inventoryBroken || inventory is not GameEntitySystem.Component { Entity: not null } || !NetworkManager.IsServerRunning) return false;
        try { SyncTo(inventory, null); InventorySyncStatus = "available"; return true; }
        catch (Exception e) { InventoryBroken(e); return false; }
    }
    bool IScNetInventorySync.Correct(IInventory inventory, ScNetPeer peer) {
        if (s_inventoryBroken || inventory is not GameEntitySystem.Component { Entity: not null } || peer?.Session is not ClientSession session || !m_peers.Contains(peer) || !NetworkManager.IsServerRunning) return false;
        try { SyncTo(inventory, session); InventorySyncStatus = "available"; return true; }
        catch (Exception e) { InventoryBroken(e); return false; }
    }
    bool IScNetInventorySync.AnnounceActiveSlot(IInventory inventory, ScNetPeer except) {
        if (s_inventoryBroken || inventory is null || !NetworkManager.IsServerRunning) return false;
        try { ActiveSlotTo(inventory, except?.Session as ClientSession); return true; }
        catch (Exception e) { InventoryBroken(e); return false; }
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void ActiveSlotTo(IInventory inventory, ClientSession except) => NetworkManager.Queue(new ActiveSlotChangePacket(inventory) { Except = except });
    static void InventoryBroken(Exception e) {
        s_inventoryBroken = true; InventorySyncStatus = $"unavailable: {e.GetType().Name}: {e.Message}";
        Log.Error("[ScCsgoNet] the platform's inventory replication is unavailable; a gun slot the server rewrites in a creative inventory will not reach the clients: " + e.Message);
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void SyncTo(IInventory inventory, ClientSession session) => InventoryPacketHelpers.QueueInventorySync(inventory, session);

    // ---- the wire: one packet type, two directions
    static bool QueueRequest(ushort operation, byte[] payload) {
        if (Instance?.PacketRegistered != true || !NetworkManager.IsClientRunning || NetworkManager.IsServerRunning || payload is null || payload.Length > ScCsgoPacket.MaximumPayloadBytes) return false;
        NetworkManager.Queue(new ScCsgoPacket(ScCsgoPacket.Request, operation, payload));
        return true;
    }
    static bool QueueBroadcast(ushort operation, byte[] payload, ClientSession to) {
        if (Instance?.PacketRegistered != true || !NetworkManager.IsServerRunning || to is null || payload is null || payload.Length > ScCsgoPacket.MaximumPayloadBytes) return false;
        NetworkManager.Queue(new ScCsgoPacket(ScCsgoPacket.Broadcast, operation, payload) { To = to });
        return true;
    }
    /// <summary>Called by the platform's receive loop (main thread) through <see cref="ScCsgoPacket.Handle"/>. A malformed
    /// packet or a failing handler never escapes into the platform's network callback.</summary>
    internal void Receive(ScCsgoPacket packet, bool isServer) {
        try {
            if (packet.Wire != ScCsgoPacket.WireVersion) return;
            if (packet.Kind == ScCsgoPacket.Request) {
                // The sender is the connection the platform received this on; its player is that connection's player.
                if (!isServer || !NetworkManager.IsServerRunning || packet.From is not { PlayerIndex: >= 0 } from) return;
                PlayerData playerData = GameManager.Project?.FindSubsystem<SubsystemPlayers>(false)?.PlayersData.FirstOrDefault(d => d.PlayerIndex == from.PlayerIndex);
                if (playerData is not null) HandleRequest(packet.Operation, packet.Payload ?? [], from, playerData);
            }
            else if (packet.Kind == ScCsgoPacket.Broadcast && !isServer) HandleBroadcast(packet.Operation, packet.Payload ?? []);
        }
        catch (Exception e) { Log.Error($"[ScCsgoNet] packet op {packet.Operation} dropped: {e}"); }
    }

    public bool SendToServer(ushort op, byte[] payload) =>
        op >= ScNet.FirstGameOp && Role == ScNetRole.Client && m_handshake == ScNetHandshake.Accepted && MainPlayer is not null
        && QueueRequest(op, payload);
    public bool SendTo(ScNetPeer peer, ushort op, byte[] payload) =>
        op >= ScNet.FirstGameOp && peer?.Session is ClientSession session && m_peers.Contains(peer) && QueueBroadcast(op, payload, session);
    public void Broadcast(ushort op, byte[] payload, ScNetPeer except) {
        if (op < ScNet.FirstGameOp) return;
        // Only accepted peers get game messages: one packet per peer (never the engine-wide broadcast, which also reaches
        // clients that were not accepted, or have no CS mod and would log an unknown packet).
        foreach (var peer in m_peers) if (!ReferenceEquals(peer, except) && peer.Session is ClientSession session) QueueBroadcast(op, payload, session);
    }

    // ---- diagnostics used by the two-process tests
    public int RemoteClients => NetworkManager.IsServerRunning ? NetworkManager.ServerSessions.Count() : 0;
    public string LastStatus => m_lastStatus;
    public int RequestStatus() => Role == ScNetRole.Client && m_handshake == ScNetHandshake.Accepted ? RequestStatusForTest(m_nextRequest++) : -1;
    /// <summary>Sends a status request with this id whatever the handshake state (repeats test de-duplication, a
    /// request without handshake tests the server's refusal).</summary>
    public int RequestStatusForTest(int request) => SendControl(OpStatus, new StatusQuery(request)) ? request : -1;
    /// <summary>Sends a hello claiming another gameplay build and/or agents (null keeps this process's own): a peer of another
    /// release, or with the agents on one side only.</summary>
    public int SendHelloIdentityForTest(string build, string agents) {
        Hello hello = Mine(m_nextRequest++);
        hello = hello with { CoreBuild = build ?? hello.CoreBuild, Agents = agents ?? hello.Agents };
        m_handshake = ScNetHandshake.Pending; m_detail = "hello sent"; m_sentAt = m_firstSentAt = Now(); m_helloAttempts = int.MaxValue / 2;
        return SendControl(OpHello, hello) ? hello.Request : -1;
    }
    /// <summary>Sends a hello claiming another CS network protocol version (a peer of an earlier release).</summary>
    public int SendHelloProtocolForTest(int version) {
        Hello hello = Mine(m_nextRequest++) with { Version = version };
        m_handshake = ScNetHandshake.Pending; m_detail = "hello sent"; m_sentAt = m_firstSentAt = Now(); m_helloAttempts = int.MaxValue / 2;
        return SendControl(OpHello, hello) ? hello.Request : -1;
    }
    /// <summary>Sends a hello, optionally claiming another item layout (a deliberately incompatible peer).</summary>
    public int SendHelloForTest(int layout) {
        Hello hello = Mine(m_nextRequest++);
        if (layout >= 0) hello = hello with { Layout = layout };
        m_handshake = ScNetHandshake.Pending; m_detail = "hello sent"; m_sentAt = m_firstSentAt = Now(); m_helloAttempts = int.MaxValue / 2;
        return SendControl(OpHello, hello) ? hello.Request : -1;
    }

    // ---- identity
    static Assembly Core => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "ScCsgoKnives");
    static int CoreConstant(string type, string field) => Core?.GetType(type, false)?.GetField(field)?.GetRawConstantValue() is int v ? v : -1;
    static Hello Mine(int request) => new(Protocol, ProtocolVersion, CoreConstant("Game.ScGunEncoding", "Layout"), CoreConstant("Game.ScGunRegistry", "Schema"),
        ModsManager.ModList.FirstOrDefault(m => m.modInfo?.PackageName == "zh667.ScCsgoKnives")?.modInfo?.Version ?? "", ScNetIdentity.Build, request,
        ScNetIdentity.Agents, ScNetIdentity.Edition, ScNetIdentity.Mode(GameManager.Project));
    /// <summary>Why a peer's hello is not compatible with this server, or null.</summary>
    public static string Incompatibility(Hello peer, Hello server) =>
        peer is null ? "malformed hello"
        : peer.Protocol != server.Protocol || peer.Version != server.Version ? $"CS 网络协议 {peer.Protocol}/{peer.Version} 与服务器 {server.Protocol}/{server.Version} 不同"
        : peer.Layout != server.Layout ? $"物品布局 {peer.Layout} 与服务器 {server.Layout} 不同"
        : peer.Schema != server.Schema ? $"枪械记录格式 {peer.Schema} 与服务器 {server.Schema} 不同"
        : peer.ModVersion != server.ModVersion ? $"CS 武器 {peer.ModVersion} 与服务器 {server.ModVersion} 不是同一版本"
        : ScNetIdentity.Incompatibility(peer.CoreBuild, peer.Agents, server.CoreBuild, server.Agents)
          ?? ScNetIdentity.ModeIncompatibility(peer.Mode, server.Mode);

    // ---- client
    static ComponentPlayer MainPlayer => GameManager.Project?.FindSubsystem<SubsystemPlayers>(false)?.MainPlayer;
    bool SendControl(ushort operation, object message) {
        bool sent = MainPlayer is not null && QueueRequest(operation, JsonSerializer.SerializeToUtf8Bytes(message, message.GetType()));
        KnifeLog.Diagnostic($"[ScCsgoNet] client: op {operation} {JsonSerializer.Serialize(message, message.GetType())} {(sent ? "sent" : "NOT sent")}");
        return sent;
    }

    public override void OnNetworkPlayerStateChanged(NetworkState state) {
        if (state != NetworkState.ProjectLoaded || !NetworkManager.IsClientRunning || NetworkManager.IsServerRunning) return;
        ScNetWorkbench.SessionClosed(this);
        m_helloDue = true; m_noticeDue = true; m_handshake = ScNetHandshake.Pending; m_detail = "waiting for the main player";
        m_helloAttempts = 0; m_slowAttempts = 0;
    }

    public override void SubsystemUpdate(SubsystemUpdate subsystemUpdate, float dt) {
        ScNetWorkbench.Tick();
        if (NetworkManager.IsServerRunning) {
            if (!m_serverChecked) { m_serverChecked = true; EnsurePacket(); }
            ForgetDepartedSessions();
            // Every subsystem has updated: the slots CS transactions rewrote this frame go out, after their records.
            ScNetSlots.EndOfFrame();
            if (ScPlatformCompat.TerrainInstalled) Terrain.ServerTick(SessionOf, (session, payload) => QueueBroadcast(OpTerrainVerify, payload, session));
            return;
        }
        m_serverChecked = false;
        if (!NetworkManager.IsClientRunning) return;
        if (m_noticeDue && MainPlayer is { ComponentGui: { } gui }) {
            m_noticeDue = false;
            if (ScPlatformCompat.PlayerNotice() is { } notice) { try { gui.DisplaySmallMessage(notice, Color.White, true, false); } catch (Exception e) { Log.Warning("[ScCsgoNet] notice: " + e.Message); } }
        }
        if (m_helloDue && MainPlayer is not null) {
            m_helloDue = false; m_detail = "hello sent";
            if (!EnsurePacket()) {
                // This process cannot speak at all: say so at once instead of waiting for a timeout that blames the server.
                m_handshake = ScNetHandshake.TimedOut; m_helloAttempts = int.MaxValue / 2;
                m_detail = $"本机无法注册 CS 联机数据包（{PacketStatus}），CS 武器已停用";
                Log.Warning("[ScCsgoNet] client: " + m_detail);
                return;
            }
            m_sentAt = m_firstSentAt = Now(); m_helloAttempts = 1;
            SendControl(OpHello, Mine(m_nextRequest++));
        }
        if (m_helloDue || m_handshake is not (ScNetHandshake.Pending or ScNetHandshake.TimedOut)) return;
        double nowReal = Now();
        bool retries = m_helloAttempts < int.MaxValue / 2; // a test's own hello is never repeated
        if (m_handshake == ScNetHandshake.Pending) {
            if (nowReal - m_firstSentAt > HandshakeTimeout) {
                m_handshake = ScNetHandshake.TimedOut;
                // Nothing came back: no CS mod on the server, a CS build before this transport (it listens on the
                // platform's CompatNet packet), or a server whose own CS network layer was refused (it is told so itself).
                m_detail = $"服务器 {HandshakeTimeout:0} 秒内没有回应 CS 握手（已发送 {(retries ? m_helloAttempts : 1)} 次）：服务器没有安装 CS 武器、版本早于本机，或其联机组件未加载；请双方使用同一版 CS 武器。CS 武器已停用";
                Log.Warning("[ScCsgoNet] client: " + m_detail);
            }
            else if (retries && nowReal - m_sentAt > HelloRetry) { m_sentAt = nowReal; m_helloAttempts++; SendControl(OpHello, Mine(m_nextRequest++)); }
        }
        else if (retries && m_slowAttempts < SlowRetries && nowReal - m_sentAt > SlowRetry) { m_sentAt = nowReal; m_slowAttempts++; SendControl(OpHello, Mine(m_nextRequest++)); }
    }
    bool m_serverChecked;

    void HandleBroadcast(ushort operation, byte[] payload) {
        if (NetworkManager.IsServerRunning) return;
        if (operation >= ScNet.FirstGameOp) {
            if (m_handshake == ScNetHandshake.Accepted) ScNet.ReceiveOnClient(operation, payload);
            return;
        }
        switch (operation) {
            case OpHelloResult:
                HelloResult result = Read<HelloResult>(payload);
                if (result is null) return;
                bool wasAccepted = m_handshake == ScNetHandshake.Accepted;
                // An answer to an earlier hello of this session never undoes an acceptance (retries cross in flight).
                if (wasAccepted && !result.Accepted && result.Request != m_nextRequest - 1) return;
                m_handshake = result.Accepted ? ScNetHandshake.Accepted : ScNetHandshake.Rejected;
                if (!result.Accepted) m_helloAttempts = int.MaxValue / 2;
                m_detail = result.Accepted ? $"accepted by server core {result.Server?.ModVersion} build {result.Server?.CoreBuild} ({result.Server?.Edition}{(string.IsNullOrEmpty(result.Server?.Agents) ? "" : " + agents")})" : "服务器拒绝：" + result.Reason + "，CS 武器已停用";
                Log.Information($"[ScCsgoNet] client: hello {result.Request} {m_detail}");
                if (result.Accepted && !wasAccepted) ScNet.NotifyClientAccepted();
                break;
            case OpStatusResult:
                m_lastStatus = System.Text.Encoding.UTF8.GetString(payload);
                KnifeLog.Diagnostic("[ScCsgoNet] client: status " + m_lastStatus);
                break;
            case OpTerrainVerify:
                // Platform terrain reconciliation: answer with what this client really holds (settled chunks only).
                if (Read<ScTerrainReconcile.Verify>(payload) is { } verify && MainPlayer is not null)
                    QueueRequest(OpTerrainReport, JsonSerializer.SerializeToUtf8Bytes(ScTerrainReconcile.Answer(verify)));
                break;
        }
    }

    // ---- server
    void HandleRequest(ushort operation, byte[] payload, ClientSession from, PlayerData playerData) {
        if (!m_sessions.TryGetValue(from, out Session session)) m_sessions[from] = session = new Session();
        if (operation >= ScNet.FirstGameOp) {
            // Game messages only from accepted peers, and only for the player of the connection they arrived on.
            if (session.Peer is { } peer && peer.PlayerIndex == playerData.PlayerIndex) ScNet.ReceiveOnServer(peer, operation, payload);
            return;
        }
        switch (operation) {
            case OpTerrainReport:
                if (ScPlatformCompat.TerrainInstalled) Terrain.OnReport(playerData.PlayerIndex, Read<ScTerrainReconcile.Report>(payload), from);
                return;
            case OpHello: {
                Hello hello = Read<Hello>(payload), server = Mine(hello?.Request ?? -1);
                string reason = Incompatibility(hello, server);
                Log.Information($"[ScCsgoNet] server: hello {hello?.Request} from player {playerData.PlayerIndex} ({from.NetworkGuid}, {hello?.Edition}{(string.IsNullOrEmpty(hello?.Agents) ? "" : " + agents")} on a {server.Edition}{(string.IsNullOrEmpty(server.Agents) ? "" : " + agents")} server): {reason ?? "accepted"}");
                if (reason is null) Accept(session, from, playerData);
                else Drop(session, "rejected: " + reason);
                ReplyControl(from, OpHelloResult, new HelloResult(hello?.Request ?? -1, reason is null, reason ?? "", server));
                break;
            }
            case OpStatus: {
                StatusQuery query = Read<StatusQuery>(payload);
                if (query is null) return;
                if (session.Peer is null) {
                    Log.Information($"[ScCsgoNet] server: status {query.Request} from player {playerData.PlayerIndex} refused (no accepted handshake)");
                    ReplyControl(from, OpStatusResult, new StatusResult(query.Request, false, "no accepted handshake", playerData.PlayerIndex, from.NetworkGuid.ToString(), 0, 0, "", 0));
                    return;
                }
                if (session.Replies.TryGetValue(query.Request, out byte[] cached)) {
                    KnifeLog.Diagnostic($"[ScCsgoNet] server: status {query.Request} from player {playerData.PlayerIndex} is a repeat: cached answer resent, nothing re-evaluated");
                    QueueBroadcast(OpStatusResult, cached, from);
                    return;
                }
                byte[] answer = JsonSerializer.SerializeToUtf8Bytes(Status(query.Request, playerData, from));
                session.Replies[query.Request] = answer; session.Order.Enqueue(query.Request);
                while (session.Order.Count > RepliesKept) session.Replies.Remove(session.Order.Dequeue());
                KnifeLog.Diagnostic($"[ScCsgoNet] server: status {query.Request} from player {playerData.PlayerIndex} answered");
                QueueBroadcast(OpStatusResult, answer, from);
                break;
            }
        }
    }

    void Accept(Session session, ClientSession from, PlayerData playerData) {
        if (session.Peer is { } existing && existing.PlayerIndex == playerData.PlayerIndex) return;
        if (session.Peer is not null) Drop(session, "player changed");
        var peer = new ScNetPeer { PlayerIndex = playerData.PlayerIndex, Guid = from.NetworkGuid, Session = from };
        session.Peer = peer; m_peers.Add(peer);
        ScNet.NotifyPeerAccepted(peer);
    }

    void Drop(Session session, string why) {
        if (session.Peer is not { } peer) return;
        Terrain.Forget(peer.PlayerIndex);
        session.Peer = null; m_peers.Remove(peer);
        Log.Information($"[ScCsgoNet] server: {peer} left ({why})");
        ScNet.NotifyPeerLeft(peer);
    }

    void ForgetDepartedSessions() {
        if (m_sessions.Count == 0) return;
        var live = new HashSet<ClientSession>(NetworkManager.ServerSessions, ReferenceEqualityComparer.Instance);
        foreach (var gone in m_sessions.Keys.Where(s => !live.Contains(s)).ToList()) { Drop(m_sessions[gone], "disconnected"); m_sessions.Remove(gone); }
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

    static void ReplyControl(ClientSession to, ushort operation, object message) =>
        QueueBroadcast(operation, JsonSerializer.SerializeToUtf8Bytes(message, message.GetType()), to);

    static T Read<T>(byte[] payload) where T : class {
        try { return payload is null ? null : JsonSerializer.Deserialize<T>(payload); }
        catch (JsonException) { return null; }
    }

    public override void OnProjectDisposed() {
        ScNetWorkbench.SessionClosed(this);
        foreach (var session in m_sessions.Values) Drop(session, "world closed");
        m_sessions.Clear(); m_peers.Clear(); Terrain.Clear(); m_noticeDue = false;
        m_helloDue = false; m_handshake = ScNetHandshake.NotApplicable; m_detail = ""; m_lastStatus = ""; m_helloAttempts = m_slowAttempts = 0; m_serverChecked = false;
    }
}

/// <summary>The adapter's one packet on the Multiplayer mod's packet table. Requests travel client → server, broadcasts
/// server → one client (<see cref="Packet.To"/>). The id lies above the platform's own range (its last is 98).</summary>
public sealed class ScCsgoPacket : Packet {
    public const byte PacketId = 0xC5;
    public const byte WireVersion = 1, Request = 0, Broadcast = 1;
    public const int MaximumPayloadBytes = 64 * 1024;

    public byte Wire = WireVersion, Kind;
    public ushort Operation;
    public byte[] Payload = [];

    public ScCsgoPacket() { }
    public ScCsgoPacket(byte kind, ushort operation, byte[] payload) { Kind = kind; Operation = operation; Payload = payload ?? []; }

    public override byte ID => PacketId;
    /// <summary>As the platform's own player packets: only between peers that have loaded the project.</summary>
    public override NetworkState MinNeedState => NetworkState.ProjectLoaded;
    public override void Serialize(PacketSerializer archive) {
        archive.Value(ref Wire);
        archive.Value(ref Kind);
        archive.Value(ref Operation);
        archive.Buffer(ref Payload, MaximumPayloadBytes);
    }
    public override void Handle(bool isServer) => ScCsgoNetAdapter.Instance?.Receive(this, isServer);

    /// <summary>Whether the platform's table holds a factory for this id (null when the table cannot be read).</summary>
    public static bool? OnTable() {
        try {
            return typeof(PacketManager).GetField("m_factories", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) is Func<Packet>[] table
                ? table[PacketId] is { } factory && factory() is ScCsgoPacket : null;
        }
        catch (Exception) { return null; }
    }
}
