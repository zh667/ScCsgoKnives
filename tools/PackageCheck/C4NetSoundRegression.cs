using System.Reflection;
using System.Runtime.CompilerServices;
using Engine;
using Engine.Media;
using Game;
using GameEntitySystem;
using TemplatesDatabase;

/// <summary>mp-state-consistency-20261002 (OpenSpec world-audio A1/A2, case C13/C14): who hears a C4 plant.
/// The real SubsystemScC4 plant loop runs in a headless world (as C4Regression's live-subsystem checks do), once as the
/// host with a remote client planting, once as the host planting itself, once as the planting client. What is counted:
/// the sounds this process plays itself (its SubsystemAudio) and the sound messages the server sends, with the peer each
/// one leaves out. mpb's host played nothing for a client's plant (the user's report); the planter is still left out of
/// the server's messages, so its own client never plays a sound twice.
/// Not covered here: what any of it sounds like, distances, and a real connection (the user's two-device test).</summary>
static class C4NetSoundRegression {
    internal record Result(string Name, bool Ok, string Detail);
    sealed class FloorTerrain : SubsystemTerrain {
        public override TerrainRaycastResult? Raycast(Vector3 start, Vector3 end, bool interaction, bool air, Func<int, float, bool> action) =>
            new TerrainRaycastResult { Ray = new Ray3(start, -Vector3.UnitY), Distance = .25f, Value = 1, CellFace = new CellFace(0, 0, 0, 4) };
    }
    sealed class AudioProbe : SubsystemAudio {
        public readonly List<string> Sounds = [];
        public bool Broken;
        public override void PlaySound(string name, float volume, float pitch, Vector3 position, float distance, bool delay) {
            if (Broken) throw new InvalidOperationException("no audio output");
            Sounds.Add(name[(name.LastIndexOf('/') + 1)..]);
        }
    }
    static T Blank<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    const ushort OpSound = 64;

    internal static List<Result> Run(Assembly mod) {
        var results = new List<Result>();
        void Check(string name, bool ok, string detail = "") => results.Add(new("c4-net-sound/" + name, ok, detail));
        Type T(string n) => mod.GetType("Game." + n, false);
        var c4 = T("SubsystemScC4"); var net = T("ScNet");
        if (c4?.GetMethod("HeardHere") is not { } heardHere) { Check("the core has the host-side C4 sound correction", false, "SubsystemScC4.HeardHere missing: an older core"); return results; }
        bool Heard(bool owner, bool local, bool host) => (bool)heardHere.Invoke(null, [owner, local, host]);
        Check("who plays a plant sound: no owner always; the owner's own process; the host for anyone's plant; never a client for another's",
            Heard(false, false, false) && Heard(false, false, true) && Heard(true, true, false) && Heard(true, true, true) && Heard(true, false, true) && !Heard(true, false, false));

        var transportProperty = net.GetProperty("Transport", BindingFlags.Public | BindingFlags.Static);
        var engineHasNetwork = net.GetProperty("EngineHasNetwork", BindingFlags.Public | BindingFlags.Static);
        var clockField = net.GetField("Clock");
        var transportType = T("IScNetTransport"); var peerType = T("ScNetPeer"); var blockType = T("ScC4Block");
        object Enum(string type, string name) => System.Enum.Parse(T(type), name);
        object originalTransport = transportProperty.GetValue(null), originalClock = clockField.GetValue(null); bool originalEngine = (bool)engineHasNetwork.GetValue(null);
        var window = typeof(Window).GetField("m_state", BindingFlags.Static | BindingFlags.NonPublic);
        var oldWindow = window.GetValue(null); var oldScreen = ScreensManager.CurrentScreen; var oldRoot = ScreensManager.RootWidget; var oldAnimation = ScreensManager.m_animationData; var oldFont = LabelWidget.m_bitmapFont;
        var savedTypes = BlocksManager.BlockTypeToIndex.ToArray(); var savedBlocks = BlocksManager.Blocks.ToArray();
        try {
            window.SetValue(null, System.Enum.Parse(window.FieldType, "Active")); ScreensManager.CurrentScreen = null; ScreensManager.m_animationData = null; ScreensManager.RootWidget = new CanvasWidget();
            LabelWidget.BitmapFont = Blank<BitmapFont>();
            if (!BlocksManager.BlockTypeToIndex.ContainsKey(blockType)) { BlocksManager.BlockTypeToIndex[blockType] = 705; BlocksManager.Blocks[705] = (Block)Activator.CreateInstance(blockType); }
            int value = (int)blockType.GetProperty("Value").GetValue(null);
            double clock = 9000; clockField.SetValue(null, (Func<double>)(() => clock));

            // One world: the C4 subsystem, an audio probe, the planter with a C4 in hand.
            (Subsystem System, SubsystemTime Time, AudioProbe Audio, ComponentPlayer Player, ComponentInventory Inventory) World(int playerIndex) {
                var project = new Project(); var time = new SubsystemTime(); var audio = new AudioProbe(); var players = new SubsystemPlayers();
                var world = Blank<WorldSettings>(); world.GameMode = GameMode.Survival; var info = Blank<SubsystemGameInfo>(); info.WorldSettings = world;
                var system = (Subsystem)Activator.CreateInstance(c4);
                foreach (var s in new Subsystem[] { time, audio, players, info, new FloorTerrain(), new SubsystemBodies(), new SubsystemParticles(), system }) { s.m_project = project; project.m_subsystems.Add(s); }
                var player = Blank<ComponentPlayer>(); player.PlayerData = Blank<PlayerData>(); player.PlayerData.PlayerIndex = playerIndex;
                var widget = Blank<GameWidget>(); widget.GuiWidget = new CanvasWidget(); widget.WidgetsHierarchyInput = new WidgetInput(WidgetInputDevice.Keyboard); player.PlayerData.m_gameWidget = widget;
                player.ComponentGui = Blank<ComponentGui>(); player.ComponentGui.m_modalPanelContainerWidget = new CanvasWidget(); player.ComponentGui.ControlsContainerWidget = new CanvasWidget();
                player.ComponentHealth = new ComponentHealth { Health = 1 }; player.ComponentBody = new ComponentBody { StandingOnValue = 1, CanCrouch = true }; player.ComponentMiner = Blank<ComponentMiner>();
                var inv = new ComponentInventory(); for (int i = 0; i < 3; i++) inv.m_slots.Add(new()); inv.AddSlotItems(0, value, 1); player.ComponentMiner.Inventory = inv;
                var entity = Blank<Entity>(); player.m_entity = entity; entity.m_project = project; var model = Blank<ComponentFirstPersonModel>(); model.m_componentPlayer = player; entity.m_components = [model]; players.m_componentPlayers.Add(player);
                system.Load(new ValuesDictionary());
                return (system, time, audio, player, inv);
            }
            AimRayRegression.FakeTransport Transport(string role, string handshake, int localIndex, params int[] peers) {
                var t = (AimRayRegression.FakeTransport)DispatchProxy.Create(transportType, typeof(AimRayRegression.FakeTransport));
                t.Role = Enum("ScNetRole", role); t.Handshake = Enum("ScNetHandshake", handshake);
                var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(peerType));
                foreach (int index in peers) { var peer = Activator.CreateInstance(peerType); peerType.GetProperty("PlayerIndex").SetValue(peer, index); list.Add(peer); }
                t.Peers = list; t.Local = p => p.PlayerData?.PlayerIndex == localIndex;
                transportProperty.SetValue(null, t); engineHasNetwork.SetValue(null, true); return t;
            }
            object PeerOf(AimRayRegression.FakeTransport t, int index) => ((System.Collections.IEnumerable)t.Peers).Cast<object>().First(p => (int)peerType.GetProperty("PlayerIndex").GetValue(p) == index);
            // The server's copy of a remote client's state, as its messages set it (the wire itself is NetLoopCheck's case C10).
            void RemoteKey(ComponentPlayer player, bool held) {
                var input = T("ScNetGuns").GetMethod("RemoteInput").Invoke(null, [player]);
                input.GetType().GetField("Available").SetValue(input, true); input.GetType().GetField("Context").SetValue(input, true);
                var plant = T("ScNetC4").GetMethod("RemotePlant").Invoke(null, [player]);
                plant.GetType().GetField("Held").SetValue(plant, held); plant.GetType().GetField("Fuse").SetValue(plant, 30); plant.GetType().GetField("ReceivedAt").SetValue(plant, clock);
            }
            int Charges(Subsystem system) { var saved = new ValuesDictionary(); system.Save(saved); return saved.GetValue<ValuesDictionary>("Charges").Count; }
            bool Planting(Subsystem system, ComponentPlayer p) => (bool)c4.GetMethod("IsPlanting").Invoke(system, [p]);
            void Update(Subsystem system, SubsystemTime time, double t) { time.m_gameTime = t; ((IUpdateable)system).Update(.016f); }
            void Run(Subsystem system, SubsystemTime time, double from, double to) { for (double t = from; t <= to + 1e-9; t += .05) Update(system, time, t); }
            int Count(IEnumerable<string> sounds, string prefix) => sounds.Count(s => s.StartsWith(prefix, StringComparison.Ordinal));
            string Summary(IEnumerable<string> sounds) => $"initiate {Count(sounds, "c4_initiate")}, keys {Count(sounds, "c4_key_press")}, plant {Count(sounds, "c4_plant")}";
            List<(string Name, object Except)> Sent(AimRayRegression.FakeTransport t) => t.Broadcasts.Where(b => b.Op == OpSound).Select(b => {
                var reader = Activator.CreateInstance(T("ScNetReader"), [b.Payload]); string path = (string)reader.GetType().GetMethod("String").Invoke(reader, [256]);
                return (path[(path.LastIndexOf('/') + 1)..], b.Except);
            }).Where(s => s.Item1.StartsWith("c4_initiate") || s.Item1.StartsWith("c4_key_press") || s.Item1.StartsWith("c4_plant")).ToList();
            void Scenario(string name, Action body) { try { body(); } catch (Exception e) { Check(name, false, (e is TargetInvocationException t ? t.InnerException : e).ToString()); } }

            // ---- reference: single player (what the planter itself hears)
            int keys = 0;
            Scenario("single player reference", () => {
                transportProperty.SetValue(null, null); engineHasNetwork.SetValue(null, false);
                var w = World(1);
                c4.GetMethod("SetPlantButton").Invoke(w.System, [w.Player, true]);
                Run(w.System, w.Time, 0, 3.4);
                keys = Count(w.Audio.Sounds, "c4_key_press");
                Check("single player: one arming tone, the key presses and one plant sound", Count(w.Audio.Sounds, "c4_initiate") == 1 && keys > 0 && Count(w.Audio.Sounds, "c4_plant") == 1 && Charges(w.System) == 1, Summary(w.Audio.Sounds));
                w.System.Dispose();
            });

            // ---- the host, a remote client (player 1) plants; another client (player 2) is connected
            Scenario("client plants: host", () => {
                var t = Transport("Host", "NotApplicable", 0, 1, 2);
                var w = World(1);
                RemoteKey(w.Player, true);
                Run(w.System, w.Time, 0, 3.4);
                var sent = Sent(t); object planter = PeerOf(t, 1);
                Check("a client plants: the host itself plays the arming tone, every key press and the plant sound, once each", Count(w.Audio.Sounds, "c4_initiate") == 1 && Count(w.Audio.Sounds, "c4_key_press") == keys && Count(w.Audio.Sounds, "c4_plant") == 1 && Charges(w.System) == 1 && w.Inventory.GetSlotCount(0) == 0,
                    Summary(w.Audio.Sounds) + $"; charges {Charges(w.System)}");
                Check("a client plants: the server tells the other clients each sound once and leaves the planter out (it plays its own)", sent.Count == keys + 2 && sent.All(s => ReferenceEquals(s.Except, planter)) && Count(sent.Select(s => s.Name), "c4_initiate") == 1 && Count(sent.Select(s => s.Name), "c4_plant") == 1,
                    Summary(sent.Select(s => s.Name)) + $"; planter left out of {sent.Count(s => ReferenceEquals(s.Except, planter))}/{sent.Count}");
                w.System.Dispose();
            });

            // ---- the host plants itself
            Scenario("host plants", () => {
                var t = Transport("Host", "NotApplicable", 0, 1, 2);
                var w = World(0);
                c4.GetMethod("SetPlantButton").Invoke(w.System, [w.Player, true]);
                Run(w.System, w.Time, 0, 3.4);
                var sent = Sent(t);
                Check("the host plants: it plays each sound once and tells every client", Count(w.Audio.Sounds, "c4_initiate") == 1 && Count(w.Audio.Sounds, "c4_key_press") == keys && Count(w.Audio.Sounds, "c4_plant") == 1 && sent.Count == keys + 2 && sent.All(s => s.Except is null) && Charges(w.System) == 1,
                    Summary(w.Audio.Sounds) + "; sent " + Summary(sent.Select(s => s.Name)));
                w.System.Dispose();
            });

            // ---- the planting client's own process
            Scenario("client plants: its own process", () => {
                var t = Transport("Client", "Accepted", 1);
                var w = World(1);
                c4.GetMethod("SetPlantButton").Invoke(w.System, [w.Player, true]);
                Run(w.System, w.Time, 0, 3.4);
                Check("the planting client plays its own plant once and sends no sound; the charge and the item stay the server's", Count(w.Audio.Sounds, "c4_initiate") == 1 && Count(w.Audio.Sounds, "c4_key_press") == keys && Count(w.Audio.Sounds, "c4_plant") == 1 && t.Broadcasts.Count == 0
                    && Charges(w.System) == 0 && w.Inventory.GetSlotCount(0) == 1 && t.Sent.Any(m => m.Op == 70), Summary(w.Audio.Sounds) + $"; plant-key messages {t.Sent.Count(m => m.Op == 70)}");
                w.System.Dispose();
            });

            // ---- cancel and start again (host, remote planter)
            Scenario("cancel and restart", () => {
                var t = Transport("Host", "NotApplicable", 0, 1, 2);
                var w = World(1);
                RemoteKey(w.Player, true);
                Run(w.System, w.Time, 0, 1.5);
                int played = w.Audio.Sounds.Count, sent = Sent(t).Count; bool started = Planting(w.System, w.Player) && Count(w.Audio.Sounds, "c4_initiate") == 1;
                RemoteKey(w.Player, false);
                Run(w.System, w.Time, 1.55, 4.5);
                Check("a cancelled plant plays and sends nothing more, arms nothing and keeps the item", started && !Planting(w.System, w.Player) && w.Audio.Sounds.Count == played && Sent(t).Count == sent && Charges(w.System) == 0 && w.Inventory.GetSlotCount(0) == 1,
                    $"sounds {played}->{w.Audio.Sounds.Count}, messages {sent}->{Sent(t).Count}");
                RemoteKey(w.Player, true);
                Run(w.System, w.Time, 5, 8.4);
                Check("a new plant after it starts from its arming tone and completes", Count(w.Audio.Sounds, "c4_initiate") == 2 && Count(w.Audio.Sounds, "c4_plant") == 1 && Charges(w.System) == 1 && w.Inventory.GetSlotCount(0) == 0, Summary(w.Audio.Sounds));
                w.System.Dispose();
            });

            // ---- a client that goes silent with the plant key held
            Scenario("silent client", () => {
                Transport("Host", "NotApplicable", 0, 1, 2);
                var w = World(1);
                RemoteKey(w.Player, true);
                Run(w.System, w.Time, 0, 1.5);
                bool started = Planting(w.System, w.Player);
                clock += 2;                                                   // no plant-key message for two seconds of real time
                Run(w.System, w.Time, 1.55, 3.6);
                Check("a client that goes silent mid-plant: the plant is cancelled, nothing armed, the item kept", started && !Planting(w.System, w.Player) && Charges(w.System) == 0 && w.Inventory.GetSlotCount(0) == 1);
                w.System.Dispose();
            });

            // ---- this process cannot play any sound
            Scenario("no audio", () => {
                var t = Transport("Host", "NotApplicable", 0, 1, 2);
                var w = World(1); w.Audio.Broken = true;
                RemoteKey(w.Player, true);
                Run(w.System, w.Time, 0, 3.4);
                int charges = Charges(w.System); var sent = Sent(t);
                Run(w.System, w.Time, 3.45, 20);                              // the countdown's beeps fail to play as well
                var saved = new ValuesDictionary(); w.System.Save(saved);
                float remaining = Charges(w.System) == 1 ? saved.GetValue<ValuesDictionary>("Charges").GetValue<ValuesDictionary>("0").GetValue<float>("Remaining") : -1;
                // (The blast itself is not run here: its particle system needs the game's content. Its sound goes through the same guarded call.)
                Check("with no usable audio here the plant still completes, the other clients are still told every sound, and the charge keeps counting down",
                    charges == 1 && w.Inventory.GetSlotCount(0) == 0 && sent.Count == keys + 2 && remaining is > 5 and < 20, $"charges after plant {charges}, messages {sent.Count}, fuse 30 s: {remaining:0.0} s left at 20 s");
                w.System.Dispose();
            });
        }
        catch (Exception e) { Check("fixture", false, e.ToString()); }
        finally {
            transportProperty.SetValue(null, originalTransport); engineHasNetwork.SetValue(null, originalEngine); clockField.SetValue(null, originalClock);
            window.SetValue(null, oldWindow); ScreensManager.CurrentScreen = oldScreen; ScreensManager.RootWidget = oldRoot; ScreensManager.m_animationData = oldAnimation; LabelWidget.BitmapFont = oldFont;
            BlocksManager.BlockTypeToIndex.Clear(); foreach (var pair in savedTypes) BlocksManager.BlockTypeToIndex[pair.Key] = pair.Value;
            Array.Copy(savedBlocks, BlocksManager.Blocks, savedBlocks.Length);
        }
        return results;
    }
}
