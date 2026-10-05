#if DM_LOOP
using Engine;
using Game;

static partial class StateLoop {
    static partial void RunQuality(ref bool ran) {
        ran = true;
        double now = 5000; ScNet.Clock = () => now;
        s_adapterType.GetField("Now").SetValue(null, (Func<double>)(() => now));
        var server = Build("quality server"); var client = Build("quality client"); var observer = Build("quality observer"); Join(server, client, observer);
        DmNet.Register();
        var view = new SubsystemScDeathmatch { m_project = client.Project }; client.Project.m_subsystems.Add(view);
        var arena = new DmArenaDefinition().WithRegion(new Point3(0, 0, 0), new Point3(255, 100, 255)).WithLobby(new Vector3(0, 10, 0), 0);
        for (int i = 0; i < 128; i++) {
            arena = arena.AddSpawn(new Vector3(2 + i % 16 * 3, 10, 2 + i / 16 * 3), .51234567f, new string('中', 20));
            if (i + 1 is not (2 or 64 or 128)) continue;
            Enter(server, true); Take();
            ScNet.SendTo(ScNet.Peers.First(p => p.PlayerIndex == 1), DmNet.OpState, w => DmNet.WriteState(w, false, DmPhase.Editing, 1, 0, new DmRules(), arena, ""));
            int dropped = ScNet.Dropped; var packets = Take(); var encoded = packets.Select(Encode).ToArray();
            Deliver(packets, client, s_sessionA);
            Test("F2", "arena roundtrip " + (i + 1), packets.Count == 1 && ScNet.Dropped == dropped && view.View.Arena.Encode() == arena.Encode(), $"JSON {arena.Encode().Length} chars, platform packet {encoded.Sum(b => b.Length)} bytes");
        }
        // Packet-level refusal must leave the last successfully applied view intact.
        string before = view.View.Arena.Encode(); int startDropped = ScNet.Dropped;
        var over = arena with { Spawns = [.. arena.Spawns, new DmSpawnPoint(129, 1, 10, 1, 0)], NextSpawnId = 130 };
        Enter(server, true);
        ScNet.SendTo(ScNet.Peers.First(p => p.PlayerIndex == 1), DmNet.OpState, w => w.Bool(false).Byte(0).Int(2).Double(0).String(new DmRules().Encode()).String(over.Encode()).String("").String(DmWeapons.Fingerprint));
        Deliver(Take(), client, s_sessionA);
        Test("F2", "129 spawns refused without view mutation", ScNet.Dropped == startDropped + 1 && view.View.Arena.Encode() == before);
        var receive = typeof(DmNet).GetMethod("ReceiveState", All);
        bool lengthRefused = false;
        try { receive.Invoke(null, [new ScNetReader(new ScNetWriter().Bool(false).Byte(0).Int(1).Double(0).String(new DmRules().Encode()).String(new string(' ', 48 * 1024 + 1)).String("").String("").ToArray())]); }
        catch (System.Reflection.TargetInvocationException e) { lengthRefused = e.InnerException is InvalidDataException && e.InnerException.Message.Contains("long"); }
        Test("F2", "arena size refusal reason", lengthRefused && view.View.Arena.Encode() == before);
        string atLimit = arena.Encode().PadRight(48 * 1024);
        receive.Invoke(null, [new ScNetReader(new ScNetWriter().Bool(false).Byte(0).Int(1).Double(0).String(new DmRules().Encode()).String(atLimit).String("").String(DmWeapons.Fingerprint).ToArray())]);
        Test("F2", "exact arena byte budget accepted intact", view.View.Arena.Encode() == arena.Encode());
        bool senderRefused = false;
        try { DmNet.WriteState(new ScNetWriter(), false, DmPhase.Editing, 1, 0, new DmRules(), arena with { Spawns = [new DmSpawnPoint(1, 0, 0, 0, 0, new string('中', 48 * 1024))] }, ""); }
        catch (InvalidDataException e) { senderRefused = e.Message.Contains("long"); }
        Test("F2", "sender refuses over-budget labels", senderRefused);

        // Load the native saved domain fields for data legal in standalone (DeathmatchCheck performs the actual
        // 1.9.3.1 edit/save). Here the real 1.9.3.2_MP platform transports publication and a joining peer's state.
        Enter(server, true);
        var largeArena = new DmArenaDefinition().AddSpawn(new Vector3(1, 10, 1), 0, new string('a', 49 * 1024))
            .AddSpawn(new Vector3(2, 10, 2), 0, new string('a', 49 * 1024));
        var savedArena = new TemplatesDatabase.ValuesDictionary();
        savedArena.SetValue("Schema", DmIds.Schema); savedArena.SetValue("Enabled", true); savedArena.SetValue("Arena", largeArena.Encode());
        savedArena.SetValue("Armory", new DmArmory().Encode()); savedArena.SetValue("Match", new DmMatch().Encode(0));
        foreach (GameEntitySystem.Subsystem s in new GameEntitySystem.Subsystem[] { new SubsystemTime(), new SubsystemTerrain(), new SubsystemBodies(), new SubsystemGameInfo() }) {
            s.m_project = server.Project; server.Project.m_subsystems.Add(s);
        }
        var publication = new SubsystemScDeathmatch { m_project = server.Project }; server.Project.m_subsystems.Add(publication); publication.Load(savedArena);
        typeof(SubsystemScDeathmatch).GetProperty("Enabled").SetValue(view, true);
        typeof(SubsystemScDeathmatch).GetField("m_players", All).SetValue(view, client.Players);
        var publish = typeof(SubsystemScDeathmatch).GetMethod("Publish", All);
        Take(); int oldDrops = ScNet.Dropped;
        publish.Invoke(publication, [now]); var unavailable = Take(); Deliver(unavailable, client, s_sessionA);
        Test("R3", "saved large arena publishes bounded explicit unavailability on MP", ScNet.Dropped == oldDrops && unavailable.Count > 0
            && publication.View.Arena.Encode() == largeArena.Encode() && view.View.Phase == DmPhase.Editing && view.View.Reason == DmNet.ArenaNetworkProblem);
        Enter(server, true);
        typeof(SubsystemScDeathmatch).GetMethod("PeerAccepted", All).Invoke(publication, [ScNet.PeerOf(server.Player[1])]);
        publish.Invoke(publication, [now + .1]); var joinPackets = Take(); Deliver(joinPackets, client, s_sessionA);
        Test("R3", "joining peer gets recoverable map refusal", joinPackets.Count > 0 && ScNet.Dropped == oldDrops && view.View.Reason == DmNet.ArenaNetworkProblem);
        Enter(server, true);
        Test("R3", "first reducing edit survives while map remains oversized", publication.RemoveSpawn(1) is null && !DmNet.ArenaFitsNetwork(publication.Arena));
        publish.Invoke(publication, [now + .2]); Deliver(Take(), client, s_sessionA);
        Enter(server, true);
        Test("R3", "map can be repaired without weakening size budget", publication.RemoveSpawn(2) is null && publication.AddSpawn(new Vector3(3, 10, 3), 0, "修复点") is null);
        publish.Invoke(publication, [now + .3]); Deliver(Take(), client, s_sessionA);
        Test("R3", "repaired full arena arrives and error clears", ScNet.Dropped == oldDrops && view.View.Reason.Length == 0 && view.View.Arena.Encode() == publication.Arena.Encode());
        Enter(server, true); publication.Dispose(); server.Project.m_subsystems.Remove(publication);

        var tactical = s_modules.Select(a => a.GetType("Game.TacticalNet")).FirstOrDefault(t => t is not null);
        if (tactical is null) { Test("F3", "Tactical module supplied", false); return; }
        object Read() => tactical.GetMethod("Defuse").Invoke(null, [server.Player[1]]);
        bool Held() { var d = Read(); return d is not null && (bool)d.GetType().GetField("Held").GetValue(d); }
        var timerType = tactical.Assembly.GetType("Game.TacticalDefuseClock", true);
        object timer = Activator.CreateInstance(timerType, [true]);
        string Advance(float dt) => timerType.GetMethod("Advance").Invoke(timer, [40f, dt, Held()]).ToString();
        void Send(bool held) { Enter(client, false); ScNet.Send(84, w => w.Bool(held).Ray(Aim)); ToServer(server); }
        Send(true); Test("F3", "initial press", Held());
        bool continuous = true; string outcome = "Active";
        for (int i = 0; i < 24; i++) { now += .25; Send(true); continuous &= Held(); if (outcome == "Active") outcome = Advance(.25f); }
        Test("F3", "continuous six seconds", continuous);
        Test("F3", "normal held input completes real defuse clock", outcome == "Defused");
        timer = Activator.CreateInstance(timerType, [true]);
        now += ScNetGuns.InputLease + .01; Enter(server, true);
        Test("F3", "stopped packets expire", !Held());
        Test("F3", "expired input cancels real defuse clock", Advance(5.1f) == "Cancelled");
        Send(false); Test("F3", "late release remains released", !Held());
        Send(true); var oldPeer = ScNet.PeerOf(server.Player[1]);
        s_sessions.Remove(s_sessionA); s_adapter.SubsystemUpdate(null, .016f);
        Test("F3", "explicit disconnect clears held input", !Held() && ScNet.PeerOf(server.Player[1]) is null);
        s_sessionA = new ClientSession { PlayerIndex = 1, NetworkGuid = oldPeer.Guid, NetworkState = NetworkState.Playing }; s_sessions.Add(s_sessionA);
        Join(server, client, observer); Enter(server, true);
        Test("F3", "reconnect fixture uses a distinct connection", !ReferenceEquals(oldPeer, ScNet.PeerOf(server.Player[1])));
        Test("F3", "rejoin starts released", !Held());
        Send(true); Test("F3", "rejoin fresh input works", Held());
        ScNet.NotifyPeerLeft(oldPeer); Test("F3", "old disconnect cannot clear new session", Held());
        Send(false);
    }
}
#endif
