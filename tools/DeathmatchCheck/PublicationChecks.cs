using System.Reflection;
using Engine;
using Game;
using TemplatesDatabase;

static partial class Program {
    sealed class PublicationTransport : IScNetTransport {
        public ScNetRole Role => ScNetRole.Host;
        public ScNetHandshake Handshake => ScNetHandshake.Accepted;
        public string HandshakeDetail => "offline publication fixture";
        public IReadOnlyList<ScNetPeer> Peers { get; } = [new ScNetPeer { PlayerIndex = 1, Guid = Guid.NewGuid() }];
        public List<(ushort Op, byte[] Data)> Packets = [];
        public bool FailNextState;
        public bool IsLocal(ComponentPlayer p) => true;
        public bool SendToServer(ushort op, byte[] payload) => false;
        public bool SendTo(ScNetPeer peer, ushort op, byte[] payload) { Packets.Add((op, payload)); return true; }
        public void Broadcast(ushort op, byte[] payload, ScNetPeer except) {
            if (op == DmNet.OpState && FailNextState) { FailNextState = false; throw new IOException("injected publication failure"); }
            Packets.Add((op, payload));
        }
    }
    static void PublicationRecovery() {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        ScNet.Attach(null);
        var dm = new SubsystemScDeathmatch();
        typeof(SubsystemScDeathmatch).GetProperty("Enabled").SetValue(dm, true);
        typeof(SubsystemScDeathmatch).GetProperty("Match").SetValue(dm, new DmMatch());
        typeof(SubsystemScDeathmatch).GetField("m_players", flags).SetValue(dm, new SubsystemPlayers());
        string label = new string('a', 49 * 1024);
        Test("R3-01", "standalone accepts large labels", dm.AddSpawn(new Vector3(1, 10, 1), 0, label) is null && dm.AddSpawn(new Vector3(2, 10, 2), 0, label) is null);
        var saved = new ValuesDictionary(); dm.Save(saved);
        bool decoded = DmArenaDefinition.TryDecode(saved.GetValue<string>("Arena"), out var arena);
        Test("R3-02", "saved map remains intact", decoded && arena.Spawns.All(s => s.Label == label));
        typeof(SubsystemScDeathmatch).GetProperty("Arena").SetValue(dm, arena);
        var transport = new PublicationTransport(); ScNet.Attach(transport);
        try {
            var publish = typeof(SubsystemScDeathmatch).GetMethod("Publish", flags);
            publish.Invoke(dm, [6d]); publish.Invoke(dm, [12d]);
            Test("R3-03", "host updates local view and sends bounded unavailability state", dm.View.Arena.Encode() == arena.Encode() && dm.View.Reason.Length > 0
                && transport.Packets.Any(p => p.Op == DmNet.OpState) && transport.Packets.All(p => p.Data.Length <= 64 * 1024));
            Test("R3-04", "unsupported online map cannot open lobby", dm.OpenLobby() is not null && dm.Match.Phase == DmPhase.Editing);
            Test("R3-05", "incremental repair accepted while still over budget", dm.RemoveSpawn(1) is null && !DmNet.ArenaFitsNetwork(dm.Arena));
            publish.Invoke(dm, [13d]);
            Test("R3-06", "final repair and normal full publication", dm.RemoveSpawn(2) is null && dm.AddSpawn(new Vector3(3, 10, 3), 0, "正常") is null);
            publish.Invoke(dm, [14d]);
            var packet = transport.Packets.Last(p => p.Op == DmNet.OpState).Data;
            var reader = new ScNetReader(packet); reader.Bool(); reader.Byte(); reader.Int(); reader.Double(); reader.String(); string sentArena = reader.String(DmArenaRules.MaxNetworkBytes); string reason = reader.String();
            Test("R3-07", "repaired state contains full map and clears error", sentArena == dm.Arena.Encode() && reason.Length == 0 && dm.View.Reason.Length == 0);
            dm.AddSpawn(new Vector3(4, 10, 4), 0, "第二点"); transport.FailNextState = true;
            publish.Invoke(dm, [15d]);
            Test("R3-08", "failed publication retains dirty state and updates local view", (bool)typeof(SubsystemScDeathmatch).GetField("m_stateDirty", flags).GetValue(dm)
                && dm.View.Arena.Encode() == dm.Arena.Encode());
            publish.Invoke(dm, [15.1d]);
            Test("R3-09", "next publication retries before periodic deadline", !(bool)typeof(SubsystemScDeathmatch).GetField("m_stateDirty", flags).GetValue(dm));
        } finally { ScNet.Attach(null); }
    }
}
