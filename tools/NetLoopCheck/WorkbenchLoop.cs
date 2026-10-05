using Engine;
using Game;
using Game.Network;

static partial class StateLoop {
    static readonly List<string> s_workbenchWire = [];
    static void RunWorkbench() {
        double now = 5000; ScNet.Clock = () => now;
        s_adapterType.GetField("Now").SetValue(null, (Func<double>)(() => now));
        var server = Build("workbench server"); var client = Build("workbench client"); var other = Build("workbench observer"); Join(server, client, other);
        var terrain = new SubsystemTerrain { Terrain = new Terrain(), m_project = server.Project }; server.Project.m_subsystems.Add(terrain);
        foreach (var p in server.Player.Values) p.ComponentHealth.Health = 1;
        int calls = 0, local = 0, code = 0; string detail = null;
        var operation = new ScWorkbenchOp(ScWorkbenchOpKind.Repair, new Point3(1000, 60, 1000), 9876, 2, 4, 1234, 8, 9, "目标", "quoted-state");
        List<Packet> Packets() {
            var batch = Take();
            foreach (var p in batch.Where(p => IsCs(p) && Op(p) is ScNetWorkbench.OpRequest or ScNetWorkbench.OpResult)) {
                byte[] payload = (byte[])p.GetType().GetField("Payload").GetValue(p);
                s_workbenchWire.Add($"{Op(p)}:{Convert.ToBase64String(payload)}");
                var decoded = PacketManager.DecodePackets(Encode(p)).Single();
                Test("WB-wire", "platform packet preserves workbench payload", Op(decoded) == Op(p) && payload.SequenceEqual((byte[])decoded.GetType().GetField("Payload").GetValue(decoded)));
            }
            return batch;
        }
        void Request() => ScNetWorkbench.Run(operation, () => { local++; return new(99); }, r => { calls++; code = r.Code; detail = r.Detail; });
        Enter(client, false); Take(); Request(); var request = Packets();
        var first = request.Single(p => IsCs(p) && Op(p) == ScNetWorkbench.OpRequest);
        int originalId = new ScNetReader((byte[])first.GetType().GetField("Payload").GetValue(first)).Int();
        Test("WB-wire", "one request registered before platform send", request.Count == 1 && WorkbenchChecks.Pending() == 1 && local == 0);
        ToServer(server, request); var reply = Packets(); Deliver(reply, client, s_sessionA); Deliver(reply, client, s_sessionA);
        Test("WB-wire", "real server validation result completes once", calls == 1 && code == -1 && detail.Contains("装配台") && WorkbenchChecks.Pending() == 0 && local == 0);
        Enter(client, false); Request(); Packets();
        now += ScNetWorkbench.RequestTimeout; s_adapter.SubsystemUpdate(null, .016f); Take();
        Test("WB-wire", "adapter Tick expires at original deadline", calls == 2 && code == ScWorkbenchResult.Unknown && WorkbenchChecks.Pending() == 0);
        Enter(server, true); ScNet.SendTo(ScNet.Peers.First(p => p.PlayerIndex == 1), ScNetWorkbench.OpResult, w => w.Int(originalId).Int(1).String("late"));
        Deliver(Packets(), client, s_sessionA); Test("WB-wire", "late platform answer cannot complete again", calls == 2);
        Enter(client, false); Request(); Packets();
        s_adapter.OnNetworkPlayerStateChanged(NetworkState.ProjectLoaded); Take();
        Test("WB-wire", "adapter new-session entry cancels existing request as unknown", calls == 3 && code == -2 && WorkbenchChecks.Pending() == 0);
        Join(server, client, other); Enter(client, false); Take(); Request(); var fresh = Packets();
        int freshId = new ScNetReader((byte[])fresh.Single().GetType().GetField("Payload").GetValue(fresh.Single())).Int();
        Enter(server, true); ScNet.SendTo(ScNet.Peers.First(p => p.PlayerIndex == 1), ScNetWorkbench.OpResult, w => w.Int(freshId).Int(1).String("accepted"));
        Deliver(Packets(), client, s_sessionA);
        Test("WB-wire", "new session uses a larger id and receives success", freshId > originalId && calls == 4 && code == 1 && local == 0);
        ScNetWorkbench.WorldClosed(client.Project);
        WorkbenchChecks.Run((name, action) => {
            try { action(); Test("WB-contract", name, true); }
            catch (Exception e) { Test("WB-contract", name, false, e.ToString()); }
        });
    }
}
