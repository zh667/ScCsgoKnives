// mpc3-feedback-subworld-20261002 (OpenSpec weapon-state-consistency R07-R10): the gun state machine itself, on both ends.
//
// StateCases.cs lets a few calls stand in for the gun update. Here nothing does: the client end and the server end each
// run SubsystemScGunBlockBehavior.Update - the method the game calls every frame - for a player holding a real gun, with
// the real animation data (deploy, cadence, reload milestones). The client's player is driven through the same inputs a
// player uses (the on-screen fire button or the mouse rays, the reload request); the server's copy of that player is
// driven only by what arrived over the wire. Between them: the production registration of every handler (StateLoop.cs),
// the CS adapter's packet, the platform's writer, decoder and inventory packets, and a delay in each direction, so that
// answers arrive some frames after the shots they are about, as they do in play.
//
// Each frame the trace records what the client's ammo readout is computed from (the server's record as mirrored, the
// shots not settled yet), the server's own rounds, and whether each end is reloading. The checks read that trace.
//
// What is still not covered: real network timing and loss recovery (the platform's channel is reliable and ordered; this
// delivers in order with a fixed delay), a survival reload's ammunition (the players are in creative mode here), anything
// seen or heard, the Android runtime. Those stay with the user's two-device test.
//
// Written against members the delivered mpc3 build has as well (later ones are read by name), so the same run against
// mpc3's core and adapter shows the reported faults: see completion_140.py netloop, "gunloop-delivered".
using System.Collections;
using System.Reflection;
using Engine;
using Game;
using Game.Network;
using GameEntitySystem;

static partial class StateLoop {
    static partial class GunLoop {
        sealed class Audio : SubsystemAudio {
            public override void PlaySound(string name, float volume, float pitch, Vector3 position, float minDistance, bool autoDelay) { }
            public override void PlayRandomSound(string name, float volume, float pitch, Vector3 position, float minDistance, bool autoDelay) { }
        }
        sealed class Eye : Camera {
            public Eye() : base(null) { }
            public override Vector3 ViewPosition => new(0, 80, 0); public override Vector3 ViewDirection => Vector3.UnitY; public override Vector3 ViewUp => Vector3.UnitZ; public override Vector3 ViewRight => Vector3.UnitX;
            public override Matrix ViewMatrix => Matrix.Identity; public override Matrix InvertedViewMatrix => Matrix.Identity; public override Matrix ProjectionMatrix => Matrix.Identity; public override Matrix ScreenProjectionMatrix => Matrix.Identity;
            public override Matrix InvertedProjectionMatrix => Matrix.Identity; public override Matrix ViewProjectionMatrix => Matrix.Identity; public override Vector2 ViewportSize => new(1280, 720); public override Matrix ViewportMatrix => Matrix.Identity;
            public override BoundingFrustum ViewFrustum => new(Matrix.Identity); public override bool UsesMovementControls => false; public override bool IsEntityControlEnabled => true; public override void Update(float dt) { }
        }
        sealed class End { public World W; public SubsystemScGunBlockBehavior Guns; public SubsystemTime Time; public ComponentPlayer P; }

        const float Dt = 1 / 60f;
        static double s_clock = 2000;
        static int s_tick, s_up = 3, s_down = 3, s_serverEvery = 1;
        static End S, A; static World B;
        static readonly Queue<(int At, List<Packet> Batch)> s_toServer = new(), s_toClients = new();
        static readonly Type Behavior = typeof(SubsystemScGunBlockBehavior);
        static readonly FieldInfo KnifeNow = typeof(ScNet).Assembly.GetType("Game.KnifeClock", true).GetField("VirtualNow");
        static readonly PropertyInfo FrameIndex = typeof(Time).GetProperty("FrameIndex");
        static object Get(object o, string name) => o.GetType().GetField(name, All).GetValue(o);
        static void Set(object o, string name, object value) => o.GetType().GetField(name, All).SetValue(o, value);

        // ---------------------------------------------------------------- the two ends
        static End Arm(World w) {
            var project = w.Project;
            var time = new SubsystemTime { m_gameTime = s_clock }; var info = new SubsystemGameInfo { WorldSettings = Blank<WorldSettings>() }; info.WorldSettings.GameMode = GameMode.Creative;
            var terrain = new SubsystemTerrain { Terrain = new Terrain() }; var bodies = new SubsystemBodies(); var audio = new Audio();
            foreach (Subsystem s in new Subsystem[] { time, info, terrain, bodies, audio }) { s.m_project = project; project.m_subsystems.Add(s); }
            var guns = new SubsystemScGunBlockBehavior(); guns.m_project = project; project.m_subsystems.Add(guns);
            Set(guns, "m_time", time); Set(guns, "m_registry", w.Registry); Set(guns, "m_terrain", terrain); Set(guns, "m_bodies", bodies); Set(guns, "m_audio", audio); Set(guns, "m_players", w.Players);
            foreach (int index in new[] { 0, 1, 2 }) {
                var player = w.Player[index]; var entity = player.Entity;
                player.ComponentHealth = new ComponentHealth { Health = 1 };
                player.ComponentInput = Blank<ComponentInput>(); player.ComponentInput.m_playerInput = new PlayerInput();
                player.ComponentGui = Blank<ComponentGui>(); player.ComponentGui.m_modalPanelContainerWidget = new CanvasWidget(); player.ComponentGui.ControlsContainerWidget = new CanvasWidget { IsVisible = false };
                var body = new ComponentBody { Position = new Vector3(index * 3, 78, 0), BoxSize = new Vector3(.65f, 1.8f, .65f) }; body.m_entity = entity; player.ComponentBody = body;
                player.ComponentLocomotion = new ComponentLocomotion();
                var widget = Blank<GameWidget>(); widget.GuiWidget = new CanvasWidget(); widget.m_activeCamera = new Eye(); player.PlayerData.m_gameWidget = widget; player.PlayerData.GameWidget = widget;
                var components = new List<Component> { player, (Component)player.ComponentMiner.Inventory, player.ComponentMiner, body };
                if (index == 1) { var model = Blank<ComponentFirstPersonModel>(); model.m_componentPlayer = player; model.m_entity = entity; components.Add(model); }
                entity.m_components = components;
                w.Players.m_componentPlayers.Add(player);
                ((IDictionary)Get(guns, "m_brokenNoticeAt"))[player] = 1e9;   // (the notice needs a real GUI)
            }
            return new End { W = w, Guns = guns, Time = time, P = w.Player[1] };
        }
        static object State(End e) => ((IDictionary)Get(e.Guns, "m_states")) is { } states && states.Contains(e.P) ? states[e.P] : null;
        static bool Reloading(End e) => State(e) is { } state && Get(state, "Reload") is not null;
        static bool Waiting(End e) => State(e) is { } state && state.GetType().GetField("ServerReloading", All)?.GetValue(state) is true;

        // ---------------------------------------------------------------- frames
        sealed record Sample(int Tick, int Hud, int Mirror, int Pending, int Server, bool ClientReload, bool ServerReload, int Hits);
        static readonly List<Sample> s_trace = [];
        static int s_gunId, s_variant;
        static int Hud() { Enter(A.W, false); int value = A.P.ComponentMiner.ActiveBlockValue; return IsGun(value) ? ScNetGuns.ShownRounds(A.P, GunSpec.GetRounds(Terrain.ExtractData(value))) : -1; }
        static int Pending() { Enter(A.W, false); return ScNetGuns.PendingShots(A.P); }
        /// <summary>The rounds of the item this end's player holds, read from this end's own table (-1: no gun in hand).</summary>
        static int HeldRounds(End e, bool server) { Enter(e.W, server); int value = e.P.ComponentMiner.ActiveBlockValue; return IsGun(value) ? GunSpec.GetRounds(Terrain.ExtractData(value)) : -1; }
        static int Mirror() => HeldRounds(A, false);
        static int Server() => HeldRounds(S, true);
        static int Stat(Type type, string field) => type.GetField(field, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is int n ? n : 0;
        static int GiveUps => Stat(typeof(ScNetGuns), "GiveUps");
        static int Dropped => Stat(typeof(ScNet), "Dropped");
        /// <summary>What the server said about this client's reloads ("#id Phase"), oldest first; empty on a build that says nothing.</summary>
        static List<string> ReloadWords() {
            var words = new List<string>();
            if (typeof(ScNetGuns).GetField("ReloadLog", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is IEnumerable log)
                foreach (object entry in log) words.Add($"#{entry.GetType().GetField("Item1").GetValue(entry)} {entry.GetType().GetField("Item2").GetValue(entry)}");
            return words;
        }
        static void ClearReloadWords() => (typeof(ScNetGuns).GetField("ReloadLog", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as IList)?.Clear();

        /// <summary>One frame of the client, then one of the server; what each sent arrives s_up / s_down frames later.</summary>
        /// <param name="fire">the fire button is down this frame</param>
        /// <param name="mouse">the mouse (the engine's dig/hit rays) instead of the on-screen button</param>
        static void Frame(bool fire = false, bool mouse = false, Action client = null, Action server = null) {
            s_tick++; s_clock += Dt; KnifeNow.SetValue(null, s_clock);
            // ---- the client
            while (s_toClients.Count > 0 && s_toClients.Peek().At <= s_tick) { var batch = s_toClients.Dequeue().Batch; Deliver(batch, A.W, s_sessionA); Deliver(batch, B, s_sessionB); }
            Enter(A.W, false); FrameIndex.SetValue(null, 200000 + s_tick * 2);
            A.Time.m_gameTime = s_clock; A.Time.m_gameTimeDelta = Dt;
            var ray = new Ray3(new Vector3(3, 80, 0), Vector3.UnitY);
            A.P.ComponentInput.m_playerInput = mouse ? new PlayerInput { Dig = fire ? ray : null, Hit = fire && !s_was ? ray : null } : new PlayerInput();
            A.Guns.SetFireButton(A.P, fire && !mouse); s_was = fire;
            client?.Invoke();
            A.Guns.Update(Dt);
            s_toServer.Enqueue((s_tick + s_up, Take()));
            // ---- the server (every s_serverEvery-th client frame: a host that runs at a lower frame rate)
            if (s_tick % s_serverEvery != 0 && server is null) { s_trace.Add(new(s_tick, Hud(), Mirror(), Pending(), Server(), Reloading(A), Reloading(S), HitsReceived())); return; }
            Enter(S.W, true);
            while (s_toServer.Count > 0 && s_toServer.Peek().At <= s_tick) ToServer(S.W, s_toServer.Dequeue().Batch);
            Enter(S.W, true); FrameIndex.SetValue(null, 200001 + s_tick * 2);
            S.Time.m_gameTime = s_clock; S.Time.m_gameTimeDelta = Dt * s_serverEvery;
            server?.Invoke();
            // One process plays both ends, and the gun update's client tick drops this process's predictions when it runs
            // as a server (rightly: a process is one or the other). The client end's are put back after the server's frame.
            var kept = ClientStatics.Select(f => f.GetValue(null)).ToArray();
            S.Guns.Update(Dt * s_serverEvery);
            for (int i = 0; i < kept.Length; i++) ClientStatics[i].SetValue(null, kept[i]);
            EndServerFrame(S.W);
            s_toClients.Enqueue((s_tick + s_down, Take()));
            s_trace.Add(new(s_tick, Hud(), Mirror(), Pending(), Server(), Reloading(A), Reloading(S), HitsReceived()));
        }
        static readonly FieldInfo[] ClientStatics = new[] { "s_prediction", "s_deferredAck" }.Select(n => typeof(ScNetGuns).GetField(n, All)).Where(f => f is not null).ToArray();
        static bool s_was;
        static readonly Sample None = new(0, -1, -1, 0, -1, false, false, 0);
        static Sample Last => s_trace.Count > 0 ? s_trace[^1] : None;
        static void Frames(int n, bool fire = false, bool mouse = false) { for (int i = 0; i < n; i++) Frame(fire, mouse); }
        /// <summary>Frames until the condition holds (at most <paramref name="limit"/>); returns whether it did.</summary>
        static bool Until(Func<bool> done, int limit, bool fire = false) { for (int i = 0; i < limit; i++) { if (done()) return true; Frame(fire); } return done(); }
        /// <summary>A gun with its own record in slot <paramref name="slot"/> of player 1 on every end, in hand, drawn and idle.</summary>
        static void Hold(int slot, int variant, int rounds) {
            Enter(S.W, true);
            s_variant = variant; s_gunId = S.W.Registry.Allocate(variant, rounds, false, ScGunDurability.Full(variant));
            int value = Instance(variant, s_gunId);
            foreach (var w in new[] { S.W, A.W, B }) { w.Inventory(1).m_slots[slot] = value; w.Inventory(1).ActiveSlotIndex = slot; }
            Frames(150);                                                      // the record arrives; the draw finishes on both ends
            s_trace.Clear(); ClearReloadWords();
        }
        /// <summary>A fresh gun (the item template, no record yet) in that slot on every end, in hand, drawn and idle.</summary>
        static void HoldFresh(int slot, int variant) {
            s_variant = variant; s_gunId = -1; int value = Fresh(variant);
            foreach (var w in new[] { S.W, A.W, B }) { w.Inventory(1).m_slots[slot] = value; w.Inventory(1).ActiveSlotIndex = slot; }
            Frames(150); s_trace.Clear(); ClearReloadWords();
        }
        static string Readings(IEnumerable<Sample> samples, Func<Sample, object> pick) {
            // run-length: "30x12 29x6 ..."
            var parts = new List<string>(); object last = null; int n = 0;
            foreach (var sample in samples) { object v = pick(sample); if (n > 0 && Equals(v, last)) { n++; continue; } if (n > 0) parts.Add($"{last}x{n}"); last = v; n = 1; }
            if (n > 0) parts.Add($"{last}x{n}");
            return string.Join(" ", parts);
        }

        public static void Run() {
            ScNet.Clock = () => s_clock;
            s_adapterType.GetField("Now").SetValue(null, (Func<double>)(() => s_clock));
            typeof(ScNet).Assembly.GetType("Game.KnifeClock", true).GetField("Virtual").SetValue(null, true); KnifeNow.SetValue(null, s_clock);
            SettingsManager.SoundsVolume = 0;
            var window = typeof(Window).GetField("m_state", BindingFlags.Static | BindingFlags.NonPublic); window.SetValue(null, Enum.Parse(window.FieldType, "Active"));
            ScreensManager.CurrentScreen = null; ScreensManager.m_animationData = null; ScreensManager.RootWidget = new CanvasWidget();
            int free = 720;
            foreach (string name in new[] { "ScKnifeBlock", "ScGrenadeBlock", "ScAmmoBlock" }) { var type = typeof(ScNet).Assembly.GetType("Game." + name, true); BlocksManager.BlockTypeToIndex[type] = free; BlocksManager.BlockNameToIndex[name] = free++; }
            var server = Build("server"); var a = Build("client A"); B = Build("client B");
            S = Arm(server); A = Arm(a);
            Join(server, a, B);
            Test("setup", "A and B are accepted; the handlers are those of the game's own registration (core, then the optional packages given)", ScNet.Peers.Count == 2 && s_overwrites.Count == 0 && Conflicts().Count == 0 && HandlerTable().Count >= 27,
                $"{ScNet.Peers.Count} peers; {HandlerTable().Count} handlers; packages: {string.Join(",", s_modules.Select(m => m.GetName().Name).DefaultIfEmpty("none"))}; replaced [{string.Join("; ", s_overwrites)}] refused [{string.Join("; ", Conflicts())}]");
            G01(); G02(); G03(); G04(); G05(); G06(); G07(); G08(); G09();
            Cadence();   // GunCadence.cs: G10-G16, two independent frame clocks
            Throws();    // ThrowLoop.cs: T01-T07, the grenade throw on the same two clocks
        }

        static bool NeverUp(IEnumerable<Sample> samples) { int last = int.MaxValue; foreach (var s in samples) { if (s.Hud > last) return false; last = s.Hud; } return true; }
        static bool Consistent(IEnumerable<Sample> samples) => samples.All(s => s.Hud == Math.Max(0, s.Mirror - s.Pending) && s.Pending >= 0);

        // ================================================================ G01 (R07/R08) a long press at nothing
        static void G01() {
            foreach (bool mouse in new[] { false, true }) {
                string input = mouse ? "mouse" : "fire button";
                Hold(mouse ? 1 : 0, s_ak, 30);
                int hits = HitsReceived(), giveUps = GiveUps, dropped = Dropped;
                Frames(72, fire: true, mouse: mouse);                          // 1.2 s of automatic fire
                var burst = s_trace.ToList();
                Frames(30);                                                     // released; the last answers arrive
                var last = Last; int fired = 30 - last.Server;
                var feedback = A.Guns.FeedbackOf(A.P);
                Test("G01", $"{input}: 1.2 s of automatic fire at nothing: the server fires what the client showed, and both end on the same rounds with nothing pending", fired is >= 11 and <= 13 && last.Hud == last.Server && last.Mirror == last.Server && last.Pending == 0,
                    $"server fired {fired}; client readout {last.Hud}, its record {last.Mirror}, pending {last.Pending}");
                Test("G01", $"{input}: not one hit is shown for shots at nothing: no hit message, no marker, no kill line", last.Hits == hits && feedback.LastKind == 0 && feedback.Kills.Count == 0,
                    $"hit messages {last.Hits - hits}, marker kind {feedback.LastKind}, kill lines {feedback.Kills.Count}");
                Test("G01", $"{input}: while firing, the readout only ever counts down, one shot at a time, and always equals the server's record less the client's own unsettled shots", NeverUp(s_trace) && Consistent(s_trace)
                    && burst.Zip(burst.Skip(1), (x, y) => x.Hud - y.Hud).All(d => d is 0 or 1), "readout: " + Readings(s_trace, s => s.Hud) + " | pending: " + Readings(s_trace, s => s.Pending));
                Test("G01", $"{input}: every shot was settled by the server's word: none by a time-out, no message dropped, at most the shots in flight pending", GiveUps == giveUps && Dropped == dropped && s_trace.Max(s => s.Pending) <= 3,
                    $"give-ups {GiveUps - giveUps}, dropped {Dropped - dropped}, most pending {s_trace.Max(s => s.Pending)} (delay {s_up}+{s_down} frames)");
            }
        }

        // ================================================================ G02 (R08) taps
        static void G02() {
            Hold(2, s_ak, 30);
            int giveUps = GiveUps, hits = HitsReceived();
            for (int tap = 0; tap < 6; tap++) { Frames(2, fire: true); Frames(13); }
            Frames(20);
            var last = Last;
            Test("G02", "six taps: six shots on the server, the readout goes down by one at each tap and never back up; nothing pending, no time-out, no hit shown", last.Server == 24 && last.Hud == 24 && last.Mirror == 24 && last.Pending == 0 && NeverUp(s_trace) && Consistent(s_trace)
                && GiveUps == giveUps && last.Hits == hits, $"server {last.Server}; readout: {Readings(s_trace, s => s.Hud)}");
        }

        // ================================================================ G03 (R07) real results still arrive
        static void G03() {
            Hold(3, s_ak, 30);
            var feedback = A.Guns.FeedbackOf(A.P); int hits = HitsReceived();
            var seen = new List<string>();
            foreach (var (outcome, name) in new[] { (1, "hit"), (3, "head hit"), (2, "kill") }) {
                Frame(server: () => ScNetFeedback.Hit(S.P, outcome, "匪徒", "AK-47", 14f));
                Frames(s_down + 2);
                seen.Add($"{name}: marker {feedback.LastKind}, kill lines {feedback.Kills.Count}");
                Test("G03", $"a {name} the server reports reaches the shooter's screen as that", feedback.LastKind == outcome && feedback.Kills.Count == (outcome == 2 ? 1 : 0), seen[^1]);
                Frames(20);
            }
            Test("G03", "three results, three messages", HitsReceived() == hits + 3, $"{HitsReceived() - hits}");
        }

        // ================================================================ G04 (R09/R10) the magazine runs dry under a held trigger
        static void G04() {
            // (delay to the server, delay back, the server's frames per client frame)
            foreach (var (up, down, every, name) in new[] { (3, 3, 1, "50 ms each way"), (15, 3, 1, "250 ms to the server"), (12, 12, 1, "200 ms each way"), (3, 3, 3, "a host at a third of the client's frame rate"), (9, 6, 4, "a host at a quarter of the frame rate, 150/100 ms") }) {
                s_up = up; s_down = down; s_serverEvery = every;
                Hold(4, s_ak, 8);
                int giveUps = GiveUps, capacity = 30, hits = HitsReceived();
                // held until the client has shown its last round and started its reload, and on through the reload
                bool emptied = Until(() => Last.Hud == 0 && Last.ClientReload, 300, fire: true);
                int atEmpty = s_trace.Count, serverThen = Last.Server, pendingThen = Last.Pending;
                bool reloaded = Until(() => Last.Server == capacity, 500, fire: true);
                int atServerFull = s_trace.Count;
                bool seenFull = Until(() => Last.Mirror >= capacity - 1 && !Last.ClientReload, 300, fire: true);
                Until(() => !Reloading(S) && !Waiting(A), 300, fire: true);
                var words = ReloadWords();
                var own = words.Where(w => !w.StartsWith("#0 ")).Select(w => w.Split(' ')[0]).Distinct().ToList();
                var between = s_trace.Skip(atEmpty - 1).TakeWhile(s => s.Mirror < capacity - 2).ToList();
                Test("G04", $"{name}: the client shows its last round and starts reloading while the server still holds {serverThen}; the server fires every shot that client showed, and only then reloads", emptied && reloaded && s_trace.Take(atServerFull).Min(s => s.Server) == 0 && pendingThen >= serverThen,
                    $"server had {serverThen} left (client: {pendingThen} shots not settled) when the client showed 0; server rounds: {Readings(s_trace.Take(atServerFull + 1), s => s.Server)}");
                Test("G04", $"{name}: from the moment it shows 0 until the server's full magazine arrives the readout stays 0: it never jumps back to the rounds the server had left", between.Count > 0 && between.All(s => s.Hud == 0), "readout: " + Readings(between, s => s.Hud) + " | record: " + Readings(between, s => s.Mirror));
                Test("G04", $"{name}: the client asked once; the server says what became of it - accepted, then completed - and refuses or cancels nothing", own.Count == 1 && words.Contains(own[0] + " Accepted") && words[^1] == own[0] + " Completed" && !words.Any(w => w.EndsWith("Refused") || w.EndsWith("Cancelled")),
                    string.Join(", ", words.DefaultIfEmpty("the server said nothing")));
                Test("G04", $"{name}: the reload ends with the server's full magazine on both ends (and the held trigger fires on from it), nothing settled by a time-out, nothing shown as a hit", seenFull && Last.Server >= capacity - 12 && GiveUps == giveUps && Consistent(s_trace) && Last.Hits == hits
                    && s_trace.Skip(atServerFull).All(s => s.Server >= capacity - 12), $"server {Last.Server}, client record {Last.Mirror}, readout {Last.Hud}; give-ups {GiveUps - giveUps}");
                Frames(60);
            }
            s_up = s_down = 3; s_serverEvery = 1;
            // the trigger is let go as the last round is shown, with a slow host: its trigger goes up with rounds left
            s_up = 6; s_serverEvery = 4;
            Hold(5, s_ak, 8);
            bool shownEmpty = Until(() => Last.Hud == 0 && Last.ClientReload, 300, fire: true);
            int left = Last.Server;
            bool full = Until(() => Last.Server == 30 && Last.Mirror == 30 && !Last.ClientReload && !Reloading(S) && !Waiting(A), 600);
            var said = ReloadWords();
            Test("G04", "the trigger let go as the client shows 0 while a slow host still has rounds (the reported \"server 2, client 0\"): the reload the client shows is performed by the server and ends with a full magazine on both ends, never with the rounds that were left", shownEmpty && left > 0 && full
                && said.Count >= 2 && said[^1].EndsWith("Completed") && Last.Hud == 30, $"server had {left} when the client showed 0; server said: {string.Join(", ", said.DefaultIfEmpty("nothing"))}; end: server {Last.Server}, readout {Last.Hud}; server rounds: {Readings(s_trace, s => s.Server)}; readout: {Readings(s_trace, s => s.Hud)}");
            s_up = 3; s_serverEvery = 1;
        }

        // ================================================================ G05 (R10) the reload key
        static void G05() {
            Hold(6, s_ak, 30);
            Frames(30, fire: true); Frames(20);
            int before = Last.Server;
            Frame(client: () => A.Guns.RequestReload(A.P));
            bool shown = Reloading(A);
            bool full = Until(() => Last.Server == 30 && Last.Mirror == 30 && !Reloading(A) && !Waiting(A), 400);
            var words = ReloadWords();
            Test("G05", "a reload by the key with rounds left: shown at once on the client, performed by the server (accepted, completed), full on both ends", before is > 0 and < 30 && shown && full && Last.Hud == 30 && words.Count == 2 && words[0].EndsWith("Accepted") && words[1].EndsWith("Completed"),
                $"{before} rounds before; server said: {string.Join(", ", words.DefaultIfEmpty("nothing"))}; end: server {Last.Server}, readout {Last.Hud}");
            Test("G05", "during it the readout is the server's rounds, then the full magazine: nothing else", s_trace.SkipWhile(s => !s.ClientReload).All(s => s.Hud == before || s.Hud == 30) && Consistent(s_trace), "readout: " + Readings(s_trace.SkipWhile(s => !s.ClientReload), s => s.Hud));
        }

        // ================================================================ G06 (R10) a reload the server does not perform
        static void G06() {
            Hold(7, s_ak, 30);
            Frames(30, fire: true); Frames(20);
            int before = Last.Server;
            // the server holds another slot for that player when the request arrives (a change of its own still on its way)
            int asked = s_trace.Count;
            Frame(client: () => A.Guns.RequestReload(A.P), server: () => S.W.Inventory(1).ActiveSlotIndex = 9);
            bool shown = Reloading(A);
            bool takenBack = Until(() => !Reloading(A), 60);
            int afterRefusal = s_trace.Count - asked;
            var words = ReloadWords();
            Test("G06", "a reload the server refuses (it holds another slot for that player): the client's reload is taken back when the word arrives, long before its animation would end; the rounds stay the server's", shown && takenBack && afterRefusal <= s_up + s_down + 4 && words.Count == 1 && words[0].EndsWith("Refused")
                && Last.Hud == before && Rounds(S.W, s_gunId, s_variant) == before && !Waiting(A), $"taken back after {afterRefusal} frames; server said: {string.Join(", ", words.DefaultIfEmpty("nothing"))}; readout {Last.Hud}, the server's record {Rounds(S.W, s_gunId, s_variant)}");
            // the server's active slot follows the client's after half a second; the next request is performed
            Frames(60); ClearReloadWords();
            Frame(client: () => A.Guns.RequestReload(A.P));
            bool full = Until(() => Last.Server == 30 && Last.Mirror == 30 && !Reloading(A) && !Waiting(A), 400);
            Test("G06", "and once the server holds that gun again, the next reload is performed and completes", full && ReloadWords().LastOrDefault()?.EndsWith("Completed") == true, string.Join(", ", ReloadWords().DefaultIfEmpty("nothing")));
        }

        // ================================================================ G07 (R10) switching away during a reload
        static void G07() {
            Hold(8, s_ak, 30);
            Frames(30, fire: true); Frames(20);
            int before = Last.Server;
            Frame(client: () => A.Guns.RequestReload(A.P));
            Frames(20);                                                         // both ends are reloading; the magazine is not in yet
            bool both = Reloading(A) && Reloading(S);
            foreach (var w in new[] { S.W, A.W, B }) w.Inventory(1).ActiveSlotIndex = 9;   // the player switches away (the platform's active-slot message)
            Frames(20);
            bool cancelled = !Reloading(A) && !Reloading(S);
            foreach (var w in new[] { S.W, A.W, B }) w.Inventory(1).ActiveSlotIndex = 8;
            Frames(150);
            var words = ReloadWords();
            Test("G07", "switching away during a reload cancels it on both ends: the server says so, no round is added, and the gun reads the server's rounds when taken out again", both && cancelled && words.Any(w => w.EndsWith("Cancelled")) && !words.Any(w => w.EndsWith("Completed"))
                && Last.Server == before && Last.Hud == before && Last.Mirror == before, $"server said: {string.Join(", ", words.DefaultIfEmpty("nothing"))}; rounds before {before}, server {Last.Server}, readout {Last.Hud}");
        }

        // ================================================================ G08 (R10) a reload the server begins by itself
        static void G08() {
            Hold(0, s_ak, 30);
            // The server's magazine is emptied under the client (what a provider's direct write or a shot only the server
            // fired amounts to) while the trigger is held: the server reloads by itself; the client had no reason to.
            Frames(10, fire: true);
            Frame(fire: true, server: () => Shoot(S.W, 1, out _, r => r.Rounds = 0));
            bool began = Until(() => Reloading(S), 60, fire: true);
            bool shown = Until(() => Reloading(A), 60, fire: true);
            bool full = Until(() => Last.Server >= 27 && Last.Mirror >= 27 && !Reloading(A), 500, fire: true);
            var words = ReloadWords();
            Test("G08", "a reload the server begins by itself is shown on the client and ends with the server's magazine there too", began && shown && full && words.Any(w => w.EndsWith("Accepted")) && words.Any(w => w.EndsWith("Completed")) && Consistent(s_trace.TakeLast(30)),
                $"server said: {string.Join(", ", words.DefaultIfEmpty("nothing"))}; end: server {Last.Server}, readout {Last.Hud}");
            Frames(30);
        }

        // ================================================================ G09 (R08-R10) a fresh gun: its first shots, its first reload
        static void G09() {
            foreach (var (up, down, every, name) in new[] { (3, 3, 1, "50 ms each way"), (9, 6, 3, "a slow host, 150/100 ms") }) {
                s_up = up; s_down = down; s_serverEvery = every;
                HoldFresh(every == 1 ? 1 : 2, s_ak);
                int giveUps = GiveUps, hits = HitsReceived(), next = S.W.Registry.Next, slot = A.W.Inventory(1).ActiveSlotIndex;
                // three taps: the first gives the gun its record on the server, the slot follows on the client
                for (int tap = 0; tap < 3; tap++) { Frames(2, fire: true); Frames(16); }
                Frames(30);
                int id = IdOf(S.W.Slot(1, slot));
                Test("G09", $"{name}: a fresh gun's first three shots: one record on the server, the client's slot follows it, the readout goes 30, 29, 28, 27 and never back", S.W.Registry.Next == next + 1 && id == next && A.W.Slot(1, slot) == S.W.Slot(1, slot) && Last.Server == 27 && Last.Hud == 27 && Last.Pending == 0
                    && NeverUp(s_trace) && Consistent(s_trace) && GiveUps == giveUps && Last.Hits == hits, $"record #{id}; server {Last.Server}; readout: {Readings(s_trace, s => s.Hud)}");
                // the rest of the magazine under a held trigger, and the first reload
                s_trace.Clear();
                bool emptied = Until(() => Last.Hud == 0 && Last.ClientReload, 400, fire: true);
                int atEmpty = s_trace.Count;
                bool full = Until(() => Last.Server == 30, 500, fire: true);
                Until(() => !Reloading(S) && !Reloading(A) && !Waiting(A), 300);
                Frames(40);
                var words = ReloadWords(); var between = s_trace.Skip(atEmpty - 1).TakeWhile(s => s.Mirror < 28).ToList();
                Test("G09", $"{name}: emptied under a held trigger, its first reload is performed by the server and ends with a full magazine on both ends; the readout stays 0 until it arrives; nothing by a time-out, no hit shown", emptied && full && Last.Server == 30 && Last.Hud == 30 && Last.Mirror == 30 && Last.Pending == 0
                    && between.Count > 0 && between.All(s => s.Hud == 0) && words.Any(w => w.EndsWith("Completed")) && !words.Any(w => w.EndsWith("Refused") || w.EndsWith("Cancelled")) && Consistent(s_trace) && GiveUps == giveUps && Last.Hits == hits && S.W.Registry.Next == next + 1,
                    $"server said: {string.Join(", ", words.DefaultIfEmpty("nothing"))}; end: server {Last.Server}, readout {Last.Hud}; readout through the reload: {Readings(between, s => s.Hud)}");
            }
            s_up = s_down = 3; s_serverEvery = 1;
        }
    }
}
