// mp-user-logs-20261002: the CS multiplayer transport, end to end, on the real platform assemblies without starting a game.
// One process plays both ends: the platform's NetworkManager delegates say which role is "running", every packet the
// adapter queues is serialised with the platform's own PacketStreamWriter/PacketSerializer, decoded by the platform's own
// PacketManager and handed to Packet.Handle with the connection it "arrived" on, as the platform's receive loop does.
// What this proves: the core loads the adapter by its contract on a platform WITHOUT the CompatNet internal mod (the
// Android build's situation: usage without --with-compatnet never loads that assembly), the packet registers and survives
// the platform's wire format, the handshake, sender authentication, retries, timeout, late acceptance and refusals.
// What it does not prove: a real session between two devices (relay, timing, the Android runtime itself).
// Usage: NetLoopCheck <refs dir> <ScCsgoNet.dll> <ScCsgoNetCompat.dll | -> <out.json> [--with-compatnet]
//        NetLoopCheck <refs dir> <ScCsgoNet.dll> - <out.json> --state | --baseline | --gunloop [--modules=<dll>;<dll>]
//        NetLoopCheck <refs dir> <ScCsgoNet.dll> - <out.json> --dmloop --modules=<ScCsgoDeathmatch.dll>[;<dll>]
//            (deathmatch-addon: the deathmatch package on a server and two clients, DmLoop.cs)
//            (gun state cases: StateLoop.cs, StateCases.cs; the real gun state machine on both ends: GunLoop.cs;
//             --modules: the optional packages' assemblies, registered as their own loaders register them)
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;

static class Program {
    static string s_refs; static bool s_withCompat; static readonly List<string> s_asked = [];
    static int Main(string[] args) {
        if (args.Length < 4) { Console.Error.WriteLine("NetLoopCheck <refs dir> <ScCsgoNet.dll> <ScCsgoNetCompat.dll | -> <out.json> [--with-compatnet]"); return 2; }
        s_refs = Path.GetFullPath(args[0]); s_withCompat = args.Contains("--with-compatnet");
        AssemblyLoadContext.Default.Resolving += (context, name) => {
            lock (s_asked) s_asked.Add(name.Name);
            if (name.Name == "Survivalcraft.CompatNet" && !s_withCompat) return null; // the transport must never need it
            string file = Path.Combine(s_refs, name.Name + ".dll");
            return File.Exists(file) ? context.LoadFromAssemblyPath(file) : null;
        };
        if (args.Contains("--state") || args.Contains("--baseline") || args.Contains("--gunloop") || args.Contains("--dmloop")) return RunState(args);
        return Loop.Run(args[1], args[2] == "-" ? null : args[2], args[3], s_withCompat, s_refs, s_asked);
    }
    // (Its own method: the state cases bind to core members the transport check never touches.)
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int RunState(string[] args) {
        string modules = args.FirstOrDefault(a => a.StartsWith("--modules="))?["--modules=".Length..] ?? "";
        return StateLoop.Run(args[1], args[2] == "-" ? null : args[2], args[3], s_refs, args.Contains("--baseline") ? "baseline" : args.Contains("--gunloop") ? "gunloop" : args.Contains("--dmloop") ? "dmloop" : "state",
            modules.Split(';', StringSplitOptions.RemoveEmptyEntries));
    }
}

static class Loop {
    sealed record Check(string Name, bool Ok, string Detail);
    static readonly List<Check> s_checks = [];
    static void Test(string name, bool ok, string detail = "") { s_checks.Add(new(name, ok, detail)); Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? " :: " + detail : "")}"); }

    sealed class Entity(byte[] adapter, byte[] compat) : Game.ModEntity {
        public override bool GetFile(string filename, Action<Stream> action) {
            byte[] bytes = filename == Game.ScNet.AdapterPayload ? adapter : filename == Game.ScNet.CompatPayload ? compat : null;
            if (bytes is null) return false;
            action(new MemoryStream(bytes)); return true;
        }
    }
    static T Blank<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run(string adapterPath, string compatPath, string output, bool withCompat, string refs, List<string> asked) {
        try { Body(adapterPath, compatPath, withCompat, refs, asked); }
        catch (Exception e) { Test("harness completed", false, e.ToString()); }
        int failed = s_checks.Count(c => !c.Ok);
        File.WriteAllText(output, JsonSerializer.Serialize(new { withCompatNet = withCompat, failed, checks = s_checks,
            platform = new[] { "Survivalcraft", "Survivalcraft.Multiplayer", "Survivalcraft.CompatNet" }.ToDictionary(n => n, n => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == n)?.ManifestModule.ModuleVersionId.ToString() ?? "not loaded"),
            scope = "offline loopback on the real platform assemblies; not a game session" }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{s_checks.Count} checks, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    static void Body(string adapterPath, string compatPath, bool withCompat, string refs, List<string> asked) {
        // ---- the platform, as far as a headless process needs it: the Multiplayer mod's own packet table and delegates
        bool server = false, client = false;
        var queued = new List<Game.Network.Packet>();
        var mySession = new Game.ClientSession { PlayerIndex = 1, NetworkGuid = Guid.NewGuid(), NetworkState = Game.NetworkState.Playing };
        var sessions = new List<Game.ClientSession>();
        Game.NetworkManager.IsServerRunningFunc = () => server; Game.NetworkManager.IsClientRunningFunc = () => client;
        Game.NetworkManager.MySessionFunc = () => client ? mySession : null; Game.NetworkManager.ServerSessionsFunc = () => sessions;
        Game.NetworkManager.QueueAction = o => { if (o is Game.Network.Packet p) queued.Add(p); };
        Game.Network.PacketManager.Initialize();
        void AsServer() { server = true; client = false; }
        void AsClient() { server = false; client = true; }
        void AsNobody() { server = client = false; }
        // The platform's wire: writer + serializer out, PacketManager in.
        byte[] Encode(Game.Network.Packet p) { var w = new Game.Network.PacketStreamWriter(); w.Write((byte)0x88); w.Write(p.ID); p.Serialize(new Game.Network.PacketSerializer(w)); return w.Data(); }
        List<Game.Network.Packet> Wire(Game.Network.Packet p) => Game.Network.PacketManager.DecodePackets(Encode(p));
        /// Delivers everything queued so far to the other end; returns how many packets went over.
        int DeliverToServer(Game.ClientSession from) { var batch = queued.ToList(); queued.Clear(); AsServer(); int n = 0; foreach (var p in batch) foreach (var d in Wire(p)) { d.From = from; d.Handle(true); n++; } return n; }
        int DeliverToClient(Game.ClientSession to) { var batch = queued.ToList(); queued.Clear(); AsClient(); int n = 0; foreach (var p in batch) { if (p.To is not null && !ReferenceEquals(p.To, to)) continue; foreach (var d in Wire(p)) { d.Handle(false); n++; } } return n; }

        // ---- a world with two players: the host's (index 0) and the client's (index 1)
        var project = new GameEntitySystem.Project();
        var players = new Game.SubsystemPlayers();
        project.m_subsystems.Add(players);
        Game.ComponentPlayer Player(int index) {
            var data = Blank<Game.PlayerData>(); data.PlayerIndex = index;
            var player = Blank<Game.ComponentPlayer>(); player.PlayerData = data; player.ComponentHealth = Blank<Game.ComponentHealth>(); data.ComponentPlayer = player;
            players.m_playersData.Add(data); return player;
        }
        var hostPlayer = Player(0); var clientPlayer = Player(1); var otherPlayer = Player(2);
        Game.GameManager.m_project = project;

        // ---- loading, through the core's own decision (ScNet.Initialize), from bytes as in the game
        if (withCompat) {
            var compatNet = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(refs, "Survivalcraft.CompatNet.dll"));
            // The platform's CompatNet loader registers its built-in adapters (id 2: the command adapter) before mods load.
            var registry = compatNet.GetType("Game.CompatNetAdapterRegistry", true);
            var command = Activator.CreateInstance(compatNet.GetType("Game.CommandCompatNetAdapter", true));
            registry.GetMethod("Register").Invoke(null, [command]);
        }
        var entity = new Entity(File.ReadAllBytes(adapterPath), compatPath is null ? null : File.ReadAllBytes(compatPath)) { modInfo = new Game.ModInfo { Name = "CS", PackageName = "zh667.ScCsgoKnives", Version = "1.4.0" } };
        ModsManager.ModList.Add(entity);
        Game.ScNet.Initialize(entity);
        Console.WriteLine("decision: " + Game.ScNet.Decision);
        foreach (string line in Game.ScNet.Evidence) Console.WriteLine("evidence: " + line);
        var transport = Game.ScNet.Transport;
        Test("core loads the adapter by its Multiplayer contract", Game.ScNet.EngineHasNetwork && transport is not null && Game.ScNet.Decision.Contains("transport contract matched, adapter attached") && Game.ScNet.RefusedReason is null, Game.ScNet.Decision);
        if (transport is null) return;
        bool compatLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Survivalcraft.CompatNet");
        if (!withCompat) {
            Test("no CompatNet in the process and none asked for (the Android build's situation)", !compatLoaded && !asked.Contains("Survivalcraft.CompatNet") && Game.ScNet.CompatStatus == "absent" && !Game.ScNet.CommandGateInstalled,
                $"loaded {compatLoaded}, asked [{string.Join(",", asked.Distinct())}], status {Game.ScNet.CompatStatus}");
        }
        else {
            var registry = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Survivalcraft.CompatNet").GetType("Game.CompatNetAdapterRegistry", true);
            object[] lookup = [(ushort)2, null]; bool found = (bool)registry.GetMethod("TryGet").Invoke(null, lookup);
            Test("with CompatNet the optional payload installs the command gate", Game.ScNet.CompatStatus == "command gate: installed" && Game.ScNet.CommandGateInstalled && found && lookup[1].GetType().Name == "ScCommandCompatGate",
                $"status {Game.ScNet.CompatStatus}, adapter 2 is {lookup[1]?.GetType().Name}");
        }
        var adapter = (Game.ModLoader)transport; Type adapterType = adapter.GetType();
        Test("the transport assembly references no CompatNet", !adapterType.Assembly.GetReferencedAssemblies().Any(a => a.Name == "Survivalcraft.CompatNet"), string.Join(",", adapterType.Assembly.GetReferencedAssemblies().Select(a => a.Name)));
        T Get<T>(string property) => (T)adapterType.GetProperty(property).GetValue(adapter);
        object Call(string method, params object[] a) => adapterType.GetMethod(method).Invoke(adapter, a);
        double clock = 1000; adapterType.GetField("Now").SetValue(null, (Func<double>)(() => clock));
        float timeout = (float)adapterType.GetField("HandshakeTimeout").GetRawConstantValue(), retry = (float)adapterType.GetField("HelloRetry").GetRawConstantValue(), slow = (float)adapterType.GetField("SlowRetry").GetRawConstantValue();
        byte packetId = (byte)adapter.GetType().Assembly.GetType("Game.ScCsgoPacket", true).GetField("PacketId").GetRawConstantValue();

        // ---- the packet on the platform's table
        adapter.OnLoadingFinished([]);
        Test("packet registered on the Multiplayer packet table above the platform's ids", Get<bool>("PacketRegistered") && packetId > (byte)Game.Network.PacketType.ModPacket, $"id {packetId}, status {Get<string>("PacketStatus")}");
        Test("a second registration is not attempted (no duplicate-id failure)", (bool)Call("EnsurePacket") && Get<bool>("PacketRegistered"), Get<string>("PacketStatus"));

        int peerAccepted = 0, peerLeft = 0, clientAccepted = 0;
        Game.ScNet.PeerAccepted += _ => peerAccepted++; Game.ScNet.PeerLeft += _ => peerLeft++; Game.ScNet.ClientAccepted += () => clientAccepted++;
        const ushort OpUp = 200, OpDown = 201;
        var serverGot = new List<(int Peer, Game.ComponentPlayer Player, int A, string B, Engine.Vector3 C)>(); var clientGot = new List<(int A, string B)>();
        Game.ScNet.OnServer(OpUp, (from, player, r) => serverGot.Add((from.PlayerIndex, player, r.Int(), r.String(64), r.Vector3())));
        Game.ScNet.OnClient(OpDown, r => clientGot.Add((r.Int(), r.String(64))));
        var clientSession = new Game.ClientSession { PlayerIndex = 1, NetworkGuid = mySession.NetworkGuid, NetworkState = Game.NetworkState.Playing };
        var otherSession = new Game.ClientSession { PlayerIndex = 2, NetworkGuid = Guid.NewGuid(), NetworkState = Game.NetworkState.Playing };
        sessions.Add(clientSession); sessions.Add(otherSession);

        // ---- before the handshake nothing of the game goes out
        AsClient();
        Test("a client without a handshake sends no game message", !Game.ScNet.Send(OpUp, w => w.Int(1).String("x").Vector3(default)) && queued.Count == 0);
        adapter.OnNetworkPlayerStateChanged(Game.NetworkState.ProjectLoaded);
        Test("joining a world starts the handshake (pending, CS actions blocked meanwhile)", transport.Handshake == Game.ScNetHandshake.Pending && Game.ScNet.ClientBlocked, transport.Handshake.ToString());
        adapter.SubsystemUpdate(null, .016f);
        Test("the hello is one packet of the adapter's own type", queued.Count == 1 && queued[0].ID == packetId && queued[0].GetType().Name == "ScCsgoPacket", $"{queued.Count} queued");
        byte[] helloBytes = Encode(queued[0]);

        // ---- a server WITHOUT CS weapons (or with a refused layer): the platform drops the unknown packet, nothing answers
        Game.Network.PacketManager.UnRegisterPacket(packetId);
        int dropped; try { dropped = Game.Network.PacketManager.DecodePackets(helloBytes).Count; } catch (Exception e) { dropped = -1; Console.WriteLine(e); }
        Test("a platform without the CS packet drops it without failing", dropped == 0, $"decoded {dropped}");
        queued.Clear();
        int attemptsBefore = Get<int>("HelloAttempts");
        for (double t = 0; t < timeout - .01; t += .5) { clock = 1000 + t; adapter.SubsystemUpdate(null, .5f); }
        int resends = queued.Count;
        Test("an unanswered hello is repeated, bounded", attemptsBefore == 1 && resends == (int)((timeout - .01) / retry) && transport.Handshake == Game.ScNetHandshake.Pending, $"{resends} resends in {timeout} s (every {retry} s)");
        clock = 1000 + timeout + .1; adapter.SubsystemUpdate(null, .1f);
        Test("then the client is told the server did not answer (not a version verdict)", transport.Handshake == Game.ScNetHandshake.TimedOut && Game.ScNet.ClientBlocked && Game.ScNet.BlockedMessage.Contains("没有回应") && !Game.ScNet.Send(OpUp, w => w.Int(1)),
            Game.ScNet.BlockedMessage);
        queued.Clear();
        clock += slow + .1; adapter.SubsystemUpdate(null, .1f);
        Test("and keeps asking slowly", queued.Count == 1 && transport.Handshake == Game.ScNetHandshake.TimedOut, $"{queued.Count} queued");

        // ---- the server gets CS weapons' packet back (same process: the adapter registers again) and answers late
        Test("the adapter notices its packet is gone and registers again", (bool)Call("EnsurePacket") && Game.Network.PacketManager.DecodePackets(helloBytes).Count == 1, Get<string>("PacketStatus"));
        int toServer = DeliverToServer(clientSession);
        Test("server accepts the hello of the connection it arrived on", toServer == 1 && peerAccepted == 1 && Game.ScNet.Peers.Count == 1 && Game.ScNet.Peers[0].PlayerIndex == 1 && queued.Count == 1 && ReferenceEquals(queued[0].To, clientSession),
            $"delivered {toServer}, accepted {peerAccepted}, peers {Game.ScNet.Peers.Count}, replies {queued.Count}");
        int toClient = DeliverToClient(clientSession);
        Test("a late answer is still accepted by the client", toClient == 1 && transport.Handshake == Game.ScNetHandshake.Accepted && clientAccepted == 1 && !Game.ScNet.ClientBlocked, $"{transport.Handshake} {transport.HandshakeDetail}");

        // ---- game messages, both directions, through the platform's wire
        AsClient();
        bool sent = Game.ScNet.Send(OpUp, w => w.Int(42).String("刀 knife").Vector3(new Engine.Vector3(1.5f, -2, 3)));
        DeliverToServer(clientSession);
        Test("client → server message arrives intact for that connection's player", sent && serverGot.Count == 1 && serverGot[0] == (1, clientPlayer, 42, "刀 knife", new Engine.Vector3(1.5f, -2, 3)), serverGot.Count == 0 ? "nothing" : serverGot[0].ToString());
        AsServer();
        Game.ScNet.Broadcast(OpDown, w => w.Int(7).String("ok"));
        Test("server → clients goes to accepted peers only", queued.Count == 1 && ReferenceEquals(queued[0].To, clientSession), $"{queued.Count} packets");
        DeliverToClient(clientSession);
        Test("server → client message arrives intact", clientGot.Count == 1 && clientGot[0] == (7, "ok"), clientGot.Count == 0 ? "nothing" : clientGot[0].ToString());

        // ---- what others see a player's weapon doing: owner → server → the OTHER accepted client, over the same wire
        Game.ScNetPresentation.Register();
        AsClient(); Call("SendHelloForTest", -1);
        var secondHello = queued.ToList(); queued.Clear(); AsServer();
        foreach (var p in secondHello) foreach (var d in Wire(p)) { d.From = otherSession; d.Handle(true); }   // a second client joins
        queued.Clear();
        Test("a second client is accepted on its own connection", Game.ScNet.Peers.Count == 2 && Game.ScNet.Peers.Any(p => p.PlayerIndex == 2), $"peers {string.Join(",", Game.ScNet.Peers.Select(p => p.PlayerIndex))}");
        // (The single client-side state of this process was put back to Pending by its test hello: accept it again.)
        AsClient(); Call("SendHelloForTest", -1); DeliverToServer(clientSession); DeliverToClient(clientSession);
        var inspect = new Game.ScWeaponAction("ak47", Game.ScWeaponActionKind.Inspect, "inspect", 5, .1f, 4f, .1f);
        AsClient();
        bool presented = Game.ScNet.Send(Game.ScNetPresentation.OpPresent, w => { w.Byte(1); Game.ScNetPresentation.WriteAction(w, inspect); });
        DeliverToServer(clientSession);
        Test("the server relays a client's weapon action to the other accepted client only", presented && queued.Count == 1 && ReferenceEquals(queued[0].To, otherSession) && Game.ScNetPresentation.RemoteOf(1) is not null,
            $"sent {presented}, relayed packets {queued.Count}, to other {(queued.Count > 0 && ReferenceEquals(queued[0].To, otherSession))}");
        Game.ScNetPresentation.Clear();
        DeliverToClient(otherSession);
        var shown = Game.ScNetPresentation.RemoteOf(1)?.Timeline.Read(Game.KnifeClock.Now);
        Test("the other client shows that action for that player", shown is { Asset: "ak47", Kind: Game.ScWeaponActionKind.Inspect, Active: true }, shown?.ToString() ?? "nothing");
        Game.ScNetPresentation.Clear();
        sessions.Remove(otherSession); AsServer(); adapter.SubsystemUpdate(null, .016f); int leftAfterSecond = peerLeft;

        // ---- sender authentication: the connection decides whose message it is
        AsClient(); Game.ScNet.Send(OpUp, w => w.Int(99).String("forged").Vector3(default));
        var forged = queued.ToList(); queued.Clear(); AsServer();
        var stranger = new Game.ClientSession { PlayerIndex = 2, NetworkGuid = Guid.NewGuid(), NetworkState = Game.NetworkState.Playing }; sessions.Add(stranger);
        foreach (var p in forged) foreach (var d in Wire(p)) { d.From = stranger; d.Handle(true); }       // arrives on a connection that never shook hands
        foreach (var p in forged) foreach (var d in Wire(p)) { d.From = null; d.Handle(true); }           // no connection at all
        foreach (var p in forged) foreach (var d in Wire(p)) { d.From = new Game.ClientSession { PlayerIndex = 5 }; d.Handle(true); } // a connection without a player here
        Test("a game message from an unaccepted or unknown connection is ignored", serverGot.Count == 1, $"{serverGot.Count} handled");
        foreach (var p in forged) foreach (var d in Wire(p)) { d.From = clientSession; d.Handle(false); } // a request handled as if by a client
        Test("a request is never handled on the client side", serverGot.Count == 1 && clientGot.Count == 1);
        // A malformed payload must not escape into the platform's receive loop.
        bool threw = false;
        try { var bad = Wire(forged[0])[0]; bad.GetType().GetField("Payload").SetValue(bad, new byte[] { 1 }); bad.From = clientSession; AsServer(); bad.Handle(true); }
        catch (Exception) { threw = true; }
        Test("a malformed message is dropped inside the adapter", !threw && serverGot.Count == 1);

        // ---- an incompatible peer is refused with the reason, and the acceptance of the other connection stands
        AsClient(); Call("SendHelloForTest", 5);
        DeliverToServer(clientSession); int afterRefusal = Game.ScNet.Peers.Count; DeliverToClient(clientSession);
        Test("a hello claiming another item layout is refused with that reason", transport.Handshake == Game.ScNetHandshake.Rejected && transport.HandshakeDetail.Contains("物品布局") && afterRefusal == 0 && peerLeft == leftAfterSecond + 1 && Game.ScNet.ClientBlocked,
            $"{transport.Handshake} {transport.HandshakeDetail}; peers {afterRefusal}");
        AsClient(); Call("SendHelloForTest", -1); DeliverToServer(clientSession); DeliverToClient(clientSession);
        Test("its own hello is accepted again", transport.Handshake == Game.ScNetHandshake.Accepted && Game.ScNet.Peers.Count == 1 && peerAccepted == 3, $"{transport.Handshake}; peers {Game.ScNet.Peers.Count}; accepted {peerAccepted}");

        // ---- leaving
        sessions.Remove(clientSession); AsServer(); adapter.SubsystemUpdate(null, .016f);
        Game.ScNet.Broadcast(OpDown, w => w.Int(8).String("gone"));
        Test("a departed connection is forgotten and gets nothing more", Game.ScNet.Peers.Count == 0 && peerLeft == leftAfterSecond + 2 && queued.Count == 0, $"peers {Game.ScNet.Peers.Count}, left {peerLeft}, queued {queued.Count}");
        AsNobody(); adapter.OnProjectDisposed();
        Test("after the world closes the role is offline and nothing is blocked", Game.ScNet.Role == Game.ScNetRole.Offline && Game.ScNet.IsAuthority && !Game.ScNet.ClientBlocked, Game.ScNet.Role.ToString());
    }
}
