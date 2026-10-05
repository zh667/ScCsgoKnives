using Engine;
namespace Game;

/// <summary>The CS T/CT player appearance in multiplayer (mp-user-logs-20261002). NekoMeko Model keeps a player's model
/// choice in that player's entity data and nothing sends it when it changes: the platform delivers entity data to a
/// joiner once, from the server's own instance of each player. So a client's choice never reached the server (everyone
/// else, and that client itself after rejoining, saw the class default), and a host's later change reached nobody.
///
/// This syncs the choices that involve our own two models (zh667.cs.ct / zh667.cs.t): the process that owns a player
/// tells the server when that player takes or leaves one; the server applies the same NMM selection call to its instance
/// (so its entity data, what joiners get and what it saves, carries the key) and tells the other accepted clients, who do
/// the same. Leaving a CS model sends the model it was left for, so observers do not keep the agent. Choices between
/// other NMM models are not ours to carry and stay as NMM and the platform handle them. Presentation only.</summary>
public static class ScNetAppearance {
    /// <summary>Client → server: the sender's own model and skin keys. Server → clients: the same for one player.</summary>
    public const ushort OpSelect = 90, OpPlayerModel = 91;
    const int MaxKey = 96;
    /// <summary>A selection for a player whose entity has not arrived yet waits this long for it.</summary>
    const double PendingSeconds = 20;
    sealed class Pending { public string Model, Skin; public double Until; }
    static readonly Dictionary<int, Pending> s_pending = [];
    // The local player's last announced keys (null: nothing announced in this session).
    static string s_sentModel, s_sentSkin; static bool s_sentCs; static double s_checkAt;
    /// <summary>Counters read by tests.</summary>
    public static int Sent, Applied, Relayed;

    public static void Register() {
        ScNet.OnServer(OpSelect, (from, player, r) => {
            string model = r.String(MaxKey), skin = r.String(MaxKey);
            if (player.Entity.FindComponent<ComponentCsPlayerAppearance>() is not { } component) return;
            Set(component, model, skin);
            // What the server's instance really holds is what the others are told (an unknown key changed nothing).
            ScNet.Broadcast(OpPlayerModel, w => w.Int(from.PlayerIndex).String(component.ModelKey).String(component.SkinKey), from);
            Relayed++;
        });
        ScNet.OnClient(OpPlayerModel, r => {
            int index = r.Int(); string model = r.String(MaxKey), skin = r.String(MaxKey);
            s_pending[index] = new Pending { Model = model, Skin = skin, Until = Time.RealTime + PendingSeconds };
        });
        ScNet.ClientAccepted += () => { s_sentModel = s_sentSkin = null; s_sentCs = false; };
        ScNet.PeerAccepted += peer => {
            // A client that joins is told the CS choices the server knows (its own included: the server's instance is what
            // every other observer shows for it).
            var players = GameManager.Project?.FindSubsystem<SubsystemPlayers>(false);
            if (players is null) return;
            foreach (var p in players.ComponentPlayers)
                if (p.Entity.FindComponent<ComponentCsPlayerAppearance>() is { IsCs: true } c && p.PlayerData.PlayerIndex != peer.PlayerIndex)
                    ScNet.SendTo(peer, OpPlayerModel, w => w.Int(p.PlayerData.PlayerIndex).String(c.ModelKey).String(c.SkinKey));
        };
    }
    public static void Clear() { s_pending.Clear(); s_sentModel = s_sentSkin = null; s_sentCs = false; }

    /// <summary>The same calls NMM's own selection dialog makes; a key this process has no resource for changes nothing.</summary>
    static void Set(ComponentCsPlayerAppearance component, string model, string skin) {
        if (!string.IsNullOrEmpty(model) && model != component.ModelKey) component.SetResModel(model);
        if (!string.IsNullOrEmpty(skin) && skin != component.SkinKey) component.SetResSkin(skin);
        Applied++;
    }

    /// <summary>From every player appearance component's update.</summary>
    public static void Tick(ComponentCsPlayerAppearance component) {
        if (ScNet.Role is not (ScNetRole.Host or ScNetRole.Client) || component.ComponentPlayer?.PlayerData is not { } data) return;
        if (!ScNet.IsLocal(component.ComponentPlayer)) {
            // An observed player: the selection its owner (through the server) announced.
            if (ScNet.IsRemoteClient && s_pending.TryGetValue(data.PlayerIndex, out var pending)) {
                if (Time.RealTime > pending.Until || pending.Model == component.ModelKey && pending.Skin == component.SkinKey) { s_pending.Remove(data.PlayerIndex); return; }
                if (string.IsNullOrEmpty(component.ModelKey)) return; // NMM has not initialised this component yet
                Set(component, pending.Model, pending.Skin); s_pending.Remove(data.PlayerIndex);
            }
            return;
        }
        // This process's own player: announce when a CS model is taken, changed or left.
        if (Time.RealTime < s_checkAt) return;
        s_checkAt = Time.RealTime + .5;
        bool client = ScNet.IsRemoteClient && !ScNet.ClientBlocked, host = ScNet.IsHost && ScNet.Peers.Count > 0;
        if (!client && !host) { if (!ScNet.IsRemoteClient) { s_sentModel = s_sentSkin = null; s_sentCs = false; } return; }
        string model = component.ModelKey, skin = component.SkinKey;
        if (string.IsNullOrEmpty(model) || model == s_sentModel && skin == s_sentSkin) return;
        if (!component.IsCs && !s_sentCs) { s_sentModel = model; s_sentSkin = skin; return; } // not ours, and no agent to take off
        if (client) { if (!ScNet.Send(OpSelect, w => w.String(model).String(skin))) return; }
        else ScNet.Broadcast(OpPlayerModel, w => w.Int(data.PlayerIndex).String(model).String(skin));
        s_sentModel = model; s_sentSkin = skin; s_sentCs = component.IsCs; Sent++;
    }
}
