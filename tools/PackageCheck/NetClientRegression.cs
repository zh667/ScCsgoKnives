using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Game;

/// <summary>mp-user-logs-20261002 (Android host + Windows client): what the core does on a multiplayer client, checked
/// through the real message code with a transport that has no network.
/// - A gun whose record the server has not sent is drawn as its model (not its flat inventory icon) and stays unusable;
///   outside a client session nothing changes (a missing record is still not guessed).
/// - What other players see a player's weapon doing (action, throw, plant) travels owner → server → the other clients, is
///   bounded on read, and never replaces the state a process simulates itself.
/// - A client whose CS network layer is not accepted sends nothing and is told why.</summary>
static class NetClientRegression {
    internal record Result(string Name, bool Ok, string Detail);
    static T Blank<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    internal static List<Result> Run(Assembly mod) {
        var results = new List<Result>();
        void Check(string name, bool ok, string detail = "") => results.Add(new("net-client/" + name, ok, detail));
        void Test(string name, Action body) { try { body(); } catch (Exception e) { Check(name, false, (e is TargetInvocationException t ? t.InnerException : e).ToString()); } }
        Type T(string n) => mod.GetType("Game." + n, false);
        var net = T("ScNet"); var presentation = T("ScNetPresentation");
        if (presentation is null || T("ScGunBlock")?.GetMethod("IsShown") is null) { Check("the core has the client-side multiplayer corrections", false, "an older core: ScNetPresentation / ScGunBlock.IsShown missing"); return results; }
        var transportProperty = net.GetProperty("Transport", BindingFlags.Public | BindingFlags.Static);
        var engineHasNetwork = net.GetProperty("EngineHasNetwork", BindingFlags.Public | BindingFlags.Static);
        var transportType = T("IScNetTransport"); var peerType = T("ScNetPeer");
        object Enum(string type, string name) => System.Enum.Parse(T(type), name);
        var registryType = T("ScGunRegistry"); var current = registryType.GetField("Current");
        object originalTransport = transportProperty.GetValue(null), originalRegistry = current.GetValue(null); bool originalEngine = (bool)engineHasNetwork.GetValue(null);
        AimRayRegression.FakeTransport Transport(string role, string handshake, params int[] peers) {
            var t = (AimRayRegression.FakeTransport)DispatchProxy.Create(transportType, typeof(AimRayRegression.FakeTransport));
            t.Role = Enum("ScNetRole", role); t.Handshake = Enum("ScNetHandshake", handshake);
            var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(peerType));
            foreach (int index in peers) { var peer = Activator.CreateInstance(peerType); peerType.GetProperty("PlayerIndex").SetValue(peer, index); list.Add(peer); }
            t.Peers = list; transportProperty.SetValue(null, t); return t;
        }
        void Standalone() { transportProperty.SetValue(null, null); engineHasNetwork.SetValue(null, false); }
        // The CS blocks, when this runs before the game registered any (the checks only need their indices).
        var savedTypes = BlocksManager.BlockTypeToIndex.ToArray(); var savedBlocks = BlocksManager.Blocks.ToArray();
        int free = 700;
        foreach (string name in new[] { "ScKnifeBlock", "ScGunBlock", "ScGrenadeBlock", "ScC4Block" }) {
            Type type = T(name);
            if (type is null || BlocksManager.BlockTypeToIndex.ContainsKey(type)) continue;
            while (BlocksManager.Blocks[free] is not null && BlocksManager.Blocks[free] is not AirBlock) free++;
            var block = (Block)Activator.CreateInstance(type); block.BlockIndex = free; BlocksManager.Blocks[free] = block; BlocksManager.BlockTypeToIndex[type] = free++;
        }
        try {
            presentation.GetMethod("Register").Invoke(null, null); // the mod loader does this in the game
            // ---------------------------------------------------------------- a gun waiting for the server's record
            Test("gun record pending", () => {
                var gunBlock = T("ScGunBlock"); var spec = T("GunSpec");
                bool Bool(string method, int value) => (bool)gunBlock.GetMethod(method).Invoke(null, [value]);
                int gunIndex = BlocksManager.BlockTypeToIndex[gunBlock];
                var registry = Activator.CreateInstance(registryType); current.SetValue(null, registry);
                int variant = 6; // m4a4
                int unknown = Terrain.MakeBlockValue(gunIndex, 0, (int)spec.GetMethod("WithId").Invoke(null, [variant, 37]));     // id 37: no record here
                int fresh = Terrain.MakeBlockValue(gunIndex, 0, (int)spec.GetMethod("WithId").Invoke(null, [variant, 0]));
                int extended = Terrain.MakeBlockValue(gunIndex, 0, (int)spec.GetMethod("WithId").Invoke(null, [variant, 1500])); // model only in the record
                var resolve = T("KnifeAnimationController").GetMethod("ResolveVariant");
                int Resolve(int value) => (int)resolve.Invoke(null, [value]);
                int asset = (int)gunBlock.GetMethod("AssetIndex").Invoke(null, [variant]);

                Standalone();
                Check("single player: a gun without its record is not usable, not shown as a model, not awaiting anything", !Bool("IsKnown", unknown) && !Bool("IsShown", unknown) && !Bool("AwaitsRecord", unknown) && Resolve(unknown) == -1);
                Check("single player: a fresh gun is unchanged", Bool("IsKnown", fresh) && Bool("IsShown", fresh) && Resolve(fresh) == asset);
                Transport("Host", "NotApplicable", 1);
                Check("server: a gun without its record is still refused (records are the server's own)", !Bool("IsShown", unknown) && Resolve(unknown) == -1);
                Transport("Client", "TimedOut");
                Check("client, handshake failed: the gun is shown as its model and stays unusable", !Bool("IsKnown", unknown) && Bool("AwaitsRecord", unknown) && Bool("IsShown", unknown) && Resolve(unknown) == asset,
                    $"known {Bool("IsKnown", unknown)} awaits {Bool("AwaitsRecord", unknown)} variant {Resolve(unknown)} (asset {asset})");
                Check("client: the finish of a pending gun is the default one (nothing is guessed)", (int)gunBlock.GetMethod("SkinOf").Invoke(null, [unknown]) == 0);
                Check("client: an id whose model is only in the record cannot be shown either", !Bool("IsShown", extended) && Resolve(extended) == -1);
                string name = ((Block)BlocksManager.Blocks[gunIndex]).GetDisplayName(null, unknown);
                Check("client: its name says it waits for the server, not that the save is damaged", name.Contains("等待服务器同步") && !name.Contains("异常") && !name.Contains("待恢复"), name);
                Transport("Client", "Accepted");
                Check("client, accepted: the same until the server's row arrives", Bool("IsShown", unknown) && !Bool("IsKnown", unknown));
                registryType.GetMethod("Allocate").Invoke(registry, [variant, 12, false, 100, -1, 0]);
                int recorded = Terrain.MakeBlockValue(gunIndex, 0, (int)spec.GetMethod("WithId").Invoke(null, [variant, 1]));
                Check("client: once the record exists the gun is usable and no longer awaiting", Bool("IsKnown", recorded) && !Bool("AwaitsRecord", recorded) && Resolve(recorded) == asset);
            });

            // ---------------------------------------------------------------- a client's shown reload and the gun's first record
            // mp-state-consistency-20261002: the server gives a fresh gun its record at the reload's insert; the slot's value
            // then changes on the client under the same selection, and the reload it shows must go on (never for another gun).
            Test("client reload follows the gun's first record", () => {
                var reloadType = T("ScReloadTransaction"); var spec = T("GunSpec");
                if (reloadType.GetMethod("FollowAllocation") is not { } follow) { Check("a client's shown reload goes on when the server gives the fresh gun its record", false, "ScReloadTransaction.FollowAllocation missing: an older core"); return; }
                int gunIndex = BlocksManager.BlockTypeToIndex[T("ScGunBlock")];
                int Value(int variant, int id) => Terrain.MakeBlockValue(gunIndex, 0, (int)spec.GetMethod("WithId").Invoke(null, [variant, id]));
                current.SetValue(null, Activator.CreateInstance(registryType));
                Transport("Client", "Accepted");
                int freshEmpty = Value(6, 1023), instance = Value(6, 5), otherModel = Value(7, 5), second = Value(6, 9);
                ComponentInventory Bag(int value) { var inv = new ComponentInventory(); for (int i = 0; i < 4; i++) inv.m_slots.Add(new()); inv.m_slots[0].Value = value; inv.m_slots[0].Count = 1; inv.ActiveSlotIndex = 0; return inv; }
                bool Valid(object reload) => (bool)reloadType.GetProperty("Valid").GetValue(reload);
                var bag = Bag(freshEmpty);
                var reload = Activator.CreateInstance(reloadType, [bag, 0, freshEmpty, 0, 0, 30]);
                bool before = Valid(reload);
                bag.m_slots[0].Value = instance;
                bool cut = !Valid(reload), followed = (bool)follow.Invoke(reload, [instance]), after = Valid(reload);
                Check("a client's shown reload goes on when the server gives the fresh gun its record", before && cut && followed && after, $"valid {before} -> {!cut} -> followed {followed} -> {after}");
                bag.m_slots[0].Value = second;
                Check("but not when the slot then holds another gun (an instance replaced by an instance)", !Valid(reload) && !(bool)follow.Invoke(reload, [second]) && !Valid(reload));
                var other = Bag(freshEmpty); var otherReload = Activator.CreateInstance(reloadType, [other, 0, freshEmpty, 0, 0, 30]);
                other.m_slots[0].Value = otherModel;
                Check("nor for another model in that slot", !(bool)follow.Invoke(otherReload, [otherModel]) && !Valid(otherReload));
            });

            // ---------------------------------------------------------------- blocked client: nothing leaves, the player is told
            Test("blocked client", () => {
                var send = net.GetMethod("Send");
                var blocked = Transport("Client", "TimedOut");
                Check("a client without an accepted handshake sends no game message", !(bool)send.Invoke(null, [(ushort)48, null]) && blocked.Sent.Count == 0 && (bool)net.GetProperty("ClientBlocked").GetValue(null));
                var knife = T("ScNetGuns").GetMethod("SendKnife"); var start = T("ScNetGrenades").GetMethod("SendStart");
                bool knifeSent = (bool)knife.Invoke(null, [false, true, new Ray3(Vector3.Zero, Vector3.UnitZ)]);
                bool throwSent = (bool)start.Invoke(null, [false, (Vector3.Zero, Vector3.Zero, Vector3.UnitZ)]);
                Check("a blocked client's knife swing and throw report that nothing was sent", !knifeSent && !throwSent && blocked.Sent.Count == 0, $"knife {knifeSent} throw {throwSent}");
                var accepted = Transport("Client", "Accepted");
                Check("an accepted client's knife swing and throw are sent", (bool)knife.Invoke(null, [true, true, new Ray3(Vector3.Zero, Vector3.UnitZ)]) && (bool)start.Invoke(null, [true, (Vector3.Zero, Vector3.Zero, Vector3.UnitZ)]) && accepted.Sent.Count == 2);
                Transport("Client", "TimedOut");
                string message = (string)net.GetProperty("BlockedMessage").GetValue(null);
                Check("the blocked message is a sentence for the player", message.StartsWith("联机："), message);
            });

            // ---------------------------------------------------------------- presentation: owner → server → observers
            Test("presentation", () => {
                var actionType = T("ScWeaponAction"); var kind = T("ScWeaponActionKind"); var throwType = T("ScThrowPhase"); var plantType = T("ScPlantPhase");
                var writerType = T("ScNetWriter"); var readerType = T("ScNetReader");
                object Action(string asset, string kindName, string clip, long sequence, float elapsed, float duration, bool looped = false) =>
                    Activator.CreateInstance(actionType, [asset, System.Enum.Parse(kind, kindName), clip, sequence, elapsed, duration, elapsed, looped]);
                dynamic Get(object o, string member) => o.GetType().GetProperty(member) is { } property ? property.GetValue(o) : o.GetType().GetField(member).GetValue(o);
                object Reader(byte[] bytes) => Activator.CreateInstance(readerType, [bytes]);
                byte[] Written(Action<object> write) { var w = Activator.CreateInstance(writerType); write(w); return (byte[])writerType.GetMethod("ToArray").Invoke(w, null); }
                object M(string name, params object[] args) => presentation.GetMethod(name).Invoke(null, args);

                // wire round trips, and what a hostile peer may not do
                object reload = Action("ak47", "Reload", "reload", 12, .25f, 2.4f, true);
                object back = M("ReadAction", Reader(Written(w => M("WriteAction", w, reload))));
                Check("an action survives the wire", (string)Get(back, "Asset") == "ak47" && Get(back, "Kind").ToString() == "Reload" && (string)Get(back, "Clip") == "reload" && (long)Get(back, "Sequence") == 12
                    && Math.Abs((float)Get(back, "Elapsed") - .25f) < 1e-6f && Math.Abs((float)Get(back, "Duration") - 2.4f) < 1e-6f && (bool)Get(back, "LoopedReload"), back.ToString());
                object wild = M("ReadAction", Reader(Written(w => { dynamic d = w; d.String("ak47").Byte((byte)250).String("x").Long(long.MaxValue).Float(1e9f).Float(1e9f).Bool(false); })));
                Check("an out-of-range action is clamped on read", Get(wild, "Kind").ToString() == "Grenade" && (float)Get(wild, "Elapsed") <= 600 && (float)Get(wild, "Duration") <= 60 && (long)Get(wild, "Sequence") < (1L << 40), wild.ToString());
                int smoke = (int)T("ScGrenadeBlock").GetMethod("Value").Invoke(null, [2]);
                object thrown = Activator.CreateInstance(throwType, [smoke, "grenade_smokegrenade", 2, true, false, 1f, 0f, .5f, 0f]);
                object thrownBack = M("ReadThrow", Reader(Written(w => M("WriteThrow", w, thrown))));
                Check("a throw phase survives the wire", (bool)Get(thrownBack, "Active") && (int)Get(thrownBack, "Value") == smoke && (int)Get(thrownBack, "Stage") == 2 && (bool)Get(thrownBack, "Low") && Math.Abs((float)Get(thrownBack, "Wind") - .5f) < 1e-6f, thrownBack.ToString());
                object fake = M("ReadThrow", Reader(Written(w => { dynamic d = w; d.Bool(true).Int(123456).String("not_a_grenade").Byte((byte)2).Bool(false).Bool(false).Float(1f).Float(0f).Float(1f).Float(0f); })));
                Check("a throw of something that is no throwable is not shown", !(bool)Get(fake, "Active"));
                object plant = Activator.CreateInstance(plantType, [1, false, 1.5f, 7L, new Vector3(3, 64, -2)]);
                object plantBack = M("ReadPlant", Reader(Written(w => M("WritePlant", w, plant))));
                Check("a plant phase survives the wire", (bool)Get(plantBack, "Active") && !(bool)Get(plantBack, "Placed") && Math.Abs((float)Get(plantBack, "Seconds") - 1.5f) < 1e-6f && (Vector3)Get(plantBack, "Position") == new Vector3(3, 64, -2), plantBack.ToString());
                bool truncated = false;
                try { M("ReadAction", Reader([3, 0x61])); } catch (TargetInvocationException e) when (e.InnerException is System.IO.InvalidDataException) { truncated = true; }
                Check("a truncated message is refused", truncated);

                // owner on a client: sends its action once per change, phases while they run
                M("Clear");
                var client = Transport("Client", "Accepted");
                var me = Blank<ComponentPlayer>(); me.PlayerData = Blank<PlayerData>(); me.PlayerData.PlayerIndex = 1;
                object noThrow = Activator.CreateInstance(throwType), noPlant = Activator.CreateInstance(plantType);
                var tick = presentation.GetMethod("Tick");
                object inspect = Action("ak47", "Inspect", "inspect", 5, 0, 4f);
                tick.Invoke(null, [me, inspect, noThrow, noPlant]); tick.Invoke(null, [me, Action("ak47", "Inspect", "inspect", 5, .3f, 4f), noThrow, noPlant]);
                ushort opPresent = (ushort)presentation.GetField("OpPresent").GetValue(null), opPlayer = (ushort)presentation.GetField("OpPlayerPresent").GetValue(null);
                Check("the owner sends an action once, when it changes", client.Sent.Count(m => m.Op == opPresent) == 1, $"{client.Sent.Count} messages");
                tick.Invoke(null, [me, Action("ak47", "Slash", "slash1", 6, 0, .5f), noThrow, noPlant]);
                Check("a new action is sent", client.Sent.Count(m => m.Op == opPresent) == 2);
                var blockedClient = Transport("Client", "TimedOut");
                tick.Invoke(null, [me, Action("ak47", "Reload", "reload", 7, 0, 2f), noThrow, noPlant]);
                Check("a blocked client sends no presentation", blockedClient.Sent.Count == 0);
                // the host is an owner too: its own player's action goes to every accepted client, named by player index
                M("Clear");
                var hosting = Transport("Host", "NotApplicable", 1, 3); hosting.Local = p => p.PlayerData?.PlayerIndex == 0;
                var hostPlayer = Blank<ComponentPlayer>(); hostPlayer.PlayerData = Blank<PlayerData>(); hostPlayer.PlayerData.PlayerIndex = 0;
                tick.Invoke(null, [hostPlayer, Action("awp", "Reload", "reload", 3, 0, 3.6f), noThrow, noPlant]);
                var hostMessage = hosting.Broadcasts.LastOrDefault();
                bool named = false;
                if (hostMessage.Payload is not null) { dynamic r = Reader(hostMessage.Payload); named = (int)r.Int() == 0 && (byte)r.Byte() == 1 && (string)Get(M("ReadAction", (object)r), "Asset") == "awp"; }
                Check("the host's own action is broadcast to the accepted clients", hosting.Broadcasts.Count == 1 && hostMessage.Op == (ushort)presentation.GetField("OpPlayerPresent").GetValue(null) && hostMessage.Except is null && named, $"{hosting.Broadcasts.Count} broadcasts");
                var alone = Transport("Host", "NotApplicable"); alone.Local = p => true;
                tick.Invoke(null, [hostPlayer, Action("awp", "Inspect", "inspect", 4, 0, 5f), noThrow, noPlant]);
                Check("a host without clients sends nothing", alone.Broadcasts.Count == 0);
                // phases: sent while they run and once more when they end
                M("Clear");
                var phased = Transport("Client", "Accepted");
                object idle = Action("grenade_smokegrenade", "Idle", "idle", 9, 0, 0);
                tick.Invoke(null, [me, idle, thrown, noPlant]); int withPhase = phased.Sent.Count;
                tick.Invoke(null, [me, idle, noThrow, noPlant]); int afterEnd = phased.Sent.Count;
                tick.Invoke(null, [me, idle, noThrow, noPlant]);
                Check("a throw phase is sent while it runs and once when it ends", withPhase == 1 && afterEnd == 2 && phased.Sent.Count == 2, $"{withPhase}/{afterEnd}/{phased.Sent.Count}");

                // observer: applies the server's message for another player and merges it with what it simulates itself
                byte[] ownerMessage = client.Sent.First(m => m.Op == opPresent).Payload;
                var observer = Transport("Client", "Accepted"); observer.Local = p => p.PlayerData?.PlayerIndex == 2;
                byte[] relayed = Written(w => { dynamic d = w; d.Int(1).Raw(ownerMessage); });
                net.GetMethod("ReceiveOnClient").Invoke(null, [opPlayer, relayed]);
                object remote = M("RemoteOf", 1);
                Check("an observer keeps the other player's action", remote is not null && (long)Get(remote, "Messages") == 1 && (int)presentation.GetField("Applied").GetValue(null) >= 1);
                var other = Blank<ComponentPlayer>(); other.PlayerData = Blank<PlayerData>(); other.PlayerData.PlayerIndex = 1;
                var entity = Blank<GameEntitySystem.Entity>(); entity.m_components = [other]; other.m_entity = entity;
                object seen = M("ActionOf", entity);
                Check("third-person poses read that action for the observed player", (string)Get(seen, "Asset") == "ak47" && Get(seen, "Kind").ToString() == "Inspect" && (bool)Get(seen, "Active") && (long)Get(seen, "Sequence") >= (1L << 40), seen.ToString());
                var mine = Blank<ComponentPlayer>(); mine.PlayerData = Blank<PlayerData>(); mine.PlayerData.PlayerIndex = 2;
                var myEntity = Blank<GameEntitySystem.Entity>(); myEntity.m_components = [mine]; mine.m_entity = myEntity;
                net.GetMethod("ReceiveOnClient").Invoke(null, [opPlayer, Written(w => { dynamic d = w; d.Int(2).Raw(ownerMessage); })]);
                Check("a message about the player this process owns never replaces its own action", !(bool)Get(M("ActionOf", myEntity), "Active"));
                // a throw phase shown for the observed player, and dropped when stale
                byte[] phase = Written(w => { dynamic d = w; d.Int(1).Byte((byte)6); M("WriteThrow", w, thrown); M("WritePlant", w, plant); });
                net.GetMethod("ReceiveOnClient").Invoke(null, [opPlayer, phase]);
                Check("an observer shows the other player's throw and plant phases", (bool)Get(M("ThrowOf", other), "Active") && (bool)Get(M("PlantOf", other), "Active") && !(bool)Get(M("ThrowOf", mine), "Active"));
                remote.GetType().GetField("ThrowAt").SetValue(remote, Time.RealTime - 5d); remote.GetType().GetField("PlantAt").SetValue(remote, Time.RealTime - 5d);
                Check("a phase that is no longer refreshed is dropped", !(bool)Get(M("ThrowOf", other), "Active") && !(bool)Get(M("PlantOf", other), "Active"));
                Standalone();
                Check("single player: no session, nothing is read from the network state", !(bool)Get(M("ThrowOf", other), "Active") && !(bool)Get(M("ActionOf", entity), "Active"));

                // server: relays a client's presentation to the other accepted clients only after it parsed, rate-limited
                M("Clear");
                var server = Transport("Host", "NotApplicable", 1, 3); server.Local = p => p.PlayerData?.PlayerIndex == 0;
                var peer = ((System.Collections.IList)server.Peers)[0];
                void RelayedExceptSender() => Check("the relay leaves out the client the action came from", server.Broadcasts.Count > 0 && server.Broadcasts.All(b => ReferenceEquals(b.Except, peer)), $"{server.Broadcasts.Count} broadcasts");
                var health = Blank<ComponentHealth>(); other.ComponentHealth = health;
                var players = new SubsystemPlayers(); var project = new GameEntitySystem.Project(); project.m_subsystems.Add(players);
                other.PlayerData.ComponentPlayer = other; players.m_playersData.Add(other.PlayerData);
                var savedProject = GameManager.m_project; GameManager.m_project = project;
                try {
                    int relayedBefore = (int)presentation.GetField("Relayed").GetValue(null);
                    net.GetMethod("ReceiveOnServer").Invoke(null, [peer, opPresent, ownerMessage]);
                    Check("the server keeps a client's action for its own view and relays it", (int)presentation.GetField("Relayed").GetValue(null) == relayedBefore + 1 && M("RemoteOf", 1) is not null
                        && (bool)Get(M("ActionOf", entity), "Active"));
                    RelayedExceptSender();
                    net.GetMethod("ReceiveOnServer").Invoke(null, [peer, opPresent, new byte[] { 1, 0x61 }]);
                    Check("a malformed presentation is dropped, not relayed", (int)presentation.GetField("Relayed").GetValue(null) == relayedBefore + 1);
                    int max = (int)presentation.GetField("MaxPerSecond").GetRawConstantValue();
                    for (int i = 0; i < max * 3; i++) net.GetMethod("ReceiveOnServer").Invoke(null, [peer, opPresent, ownerMessage]);
                    int relayedNow = (int)presentation.GetField("Relayed").GetValue(null) - relayedBefore;
                    Check("a client cannot flood the relay", relayedNow <= max * 2 + 2 && (int)presentation.GetField("Dropped").GetValue(null) > 0, $"{relayedNow} relayed of {max * 3 + 1}");
                }
                finally { GameManager.m_project = savedProject; }
            });
        }
        finally {
            transportProperty.SetValue(null, originalTransport); engineHasNetwork.SetValue(null, originalEngine); current.SetValue(null, originalRegistry); T("ScNetPresentation")?.GetMethod("Clear")?.Invoke(null, null);
            Array.Copy(savedBlocks, BlocksManager.Blocks, savedBlocks.Length); BlocksManager.BlockTypeToIndex.Clear(); foreach (var pair in savedTypes) BlocksManager.BlockTypeToIndex[pair.Key] = pair.Value;
        }
        return results;
    }
}
