// quick-throw-20261002: a throw on both ends. Letting go throws - at once, wherever the pull has got to; held, the whole
// preparation stays.
//
// The client end and the server end each run SubsystemScGrenades.Update - the method the game calls every frame - on
// their own frame clocks (GunCadence.cs: any pair of frame rates, frame times that vary, a host that stalls, a link whose
// delay is fixed, zero or bunches in order). The client's player is driven through the on-screen throw button; the
// server's copy of that player only by what arrived: the start, the word that the client's throw has begun, the view.
//
// The server never tells a tap from a hold: it has no clock for the press at all. So what is asked here is the
// correspondence, per throw: the client's throw begins in the frame its button goes up (no wait for the pull); the server
// makes exactly one grenade, no earlier than that word reaches it and no later than the throw's own release moment after
// it (plus the server's frame); a hold is never thrown early, however the messages bunch; a start and its release in one
// server frame still make one grenade; repeated or stray messages make none; and what the host and the other clients
// are shown lets go when the grenade does.
//
// Not covered: a real link (the platform's channel is reliable and ordered; this delivers in order), survival stacks
// (the players here are in creative mode: the count taken per throw is PackageCheck's QuickThrowRegression), anything
// seen or heard, the Android runtime. Written against members the delivered mpe1 build has as well, so the same run
// against mpe1 shows a tap waiting out the whole pull.
using System.Reflection;
using Engine;
using Game;
using GameEntitySystem;

static partial class StateLoop {
    static partial class GunLoop {
        sealed class OpenTerrain : SubsystemTerrain { public override TerrainRaycastResult? Raycast(Vector3 a, Vector3 b, bool i, bool s, Func<int, float, bool> f) => null; }
        static SubsystemScGrenades s_serverThrows, s_clientThrows;
        // The engine's real time (a stopwatch) paces what is sent "20 times a second" or "10 times a second": it is moved
        // to the shared clock at every step, so those rates are measured on the simulated time line.
        static readonly FieldInfo StartTicks = typeof(Time).GetField("m_startTicks", All);
        static void RealTimeIs(double t) => StartTicks.SetValue(null, (long?)(System.Diagnostics.Stopwatch.GetTimestamp() - (long)(t * System.Diagnostics.Stopwatch.Frequency)));

        static SubsystemScGrenades ArmThrows(End e) {
            var project = e.W.Project; var grenades = new SubsystemScGrenades(); grenades.m_project = project; project.m_subsystems.Add(grenades);
            var terrain = new OpenTerrain { Terrain = new Terrain() };
            for (int x = -4; x <= 4; x++) for (int z = -4; z <= 4; z++) terrain.Terrain.AllocateChunk(x, z).State = TerrainChunkState.Valid;
            terrain.m_project = project;
            Set(grenades, "m_time", e.Time); Set(grenades, "m_terrain", terrain); Set(grenades, "m_bodies", project.FindSubsystem<SubsystemBodies>(true));
            Set(grenades, "m_players", e.W.Players); Set(grenades, "m_info", project.FindSubsystem<SubsystemGameInfo>(true));
            return grenades;
        }
        static int MadeOnServer => (int)Get(s_serverThrows, "m_nextGrenadeId");
        /// <summary>Whether the client shows a grenade made after <paramref name="next"/> was the server's next id (older
        /// ones - an explosion or a smoke still running from an earlier case - are not this throw's).</summary>
        static bool ShownOnClient(int next) => ((IEnumerable<ScGrenadeState>)Get(s_clientThrows, "m_active")).Any(s => s.Id >= next);

        /// <summary>One throw as each end went through it (shared time; -1: never).</summary>
        sealed class Toss {
            public double Start, Down = -1, Up = -1, ClientThrow = -1, ClientReleased = -1, ClientEnd = -1, WordArrives = -1;
            public double Prepared = -1, ServerThrow = -1, Made = -1, HostReleased = -1, ToldReleased = -1, ToldEnded = -1;
            public int MadeCount, ClientMade; public bool ClientReady, ServerReady; public double ServerFrame, ClientFrame;
            string At(double t) => t < 0 ? "never" : (t - Start).ToString("0.000");
            public string Line => $"button {At(Down)}-{At(Up)}; client: throw begins {At(ClientThrow)}{(ClientReady ? " (held ready first)" : "")}, lets go {At(ClientReleased)}, ends {At(ClientEnd)}; "
                + $"server: prepared {At(Prepared)}, the client's word arrives {At(WordArrives)}, throw begins {At(ServerThrow)}{(ServerReady ? " (held ready first)" : "")}, grenade {At(Made)} x{MadeCount}; others shown the empty hand from {At(ToldReleased)}";
        }
        /// <summary>The on-screen throw button down from 0.1 s for <paramref name="hold"/> seconds, then <paramref name="settle"/> seconds.</summary>
        static Toss Throw(bool low, double hold, double settle = 2.6, Action<double, Toss> client = null, Action<double, Toss> server = null, Func<double, bool> held = null) {
            var x = new Toss { Start = s_clock }; double start = s_clock; int made = MadeOnServer;
            held ??= t => t >= .1 - 1e-9 && t < .1 + hold - 1e-9;
            Advance(start + .1 + hold + settle, _ => false, client: t => {
                RealTimeIs(t);
                bool pressed = held(t - start);
                s_clientThrows.SetThrowButton(A.P, low, pressed, false, false, 2);
                double arrives = Math.Max(s_upLast, t + s_pace.Up(t));               // when what this frame sends reaches the server
                ((IUpdateable)s_clientThrows).Update(A.Time.m_gameTimeDelta);
                x.ClientFrame = Math.Max(x.ClientFrame, A.Time.m_gameTimeDelta);
                var phase = s_clientThrows.ThrowPhase(A.P);
                if (pressed && x.Down < 0) x.Down = t;
                if (!pressed && x.Down >= 0 && x.Up < 0) x.Up = t;
                if (phase.Active && phase.Stage == 1) x.ClientReady = true;
                if (phase.Active && phase.Stage == 2 && x.ClientThrow < 0) { x.ClientThrow = t; x.WordArrives = arrives; }
                if (phase.Active && phase.Released && x.ClientReleased < 0) x.ClientReleased = t;
                if (x.ClientReleased >= 0 && !phase.Active && x.ClientEnd < 0) x.ClientEnd = t;
                client?.Invoke(t, x);
            }, server: t => {
                RealTimeIs(t);
                int before = MadeOnServer;
                ((IUpdateable)s_serverThrows).Update(S.Time.m_gameTimeDelta);
                if (x.Made < 0) x.ServerFrame = Math.Max(x.ServerFrame, S.Time.m_gameTimeDelta);
                var phase = s_serverThrows.ThrowPhase(S.P);
                if (phase.Active && x.Prepared < 0) x.Prepared = t;
                if (phase.Active && phase.Stage == 1) x.ServerReady = true;
                if (phase.Active && phase.Stage == 2 && x.ServerThrow < 0) x.ServerThrow = t;
                if (MadeOnServer > before && x.Made < 0) x.Made = t;
                if (phase.Active && phase.Released && x.HostReleased < 0) x.HostReleased = t;
                // what the server relays to the other clients about that player (its own process's word)
                var told = ScNetPresentation.RemoteOf(1)?.Throw ?? default;
                if (told.Active && told.Released && x.ToldReleased < 0) x.ToldReleased = t;
                if (x.ToldReleased >= 0 && !told.Active && x.ToldEnded < 0) x.ToldEnded = t;
                server?.Invoke(t, x);
            });
            x.MadeCount = MadeOnServer - made;
            return x;
        }
        static float ReleaseMoment(int kind, bool low) => Cs2Rig.GrenadeReleaseTime(ScGrenadeBlock.Assets[kind], low ? "throwLow" : "throwHigh");
        /// <summary>Exactly one grenade, made no earlier than the client's word reaches the server and no later than the throw's
        /// own release moment after it (the server acts in its first frame at or after each of the two).</summary>
        static bool Corresponds(Toss x, float release) => x.MadeCount == 1 && x.ClientThrow >= 0 && x.Made >= x.WordArrives - 1e-9 && x.Made <= x.WordArrives + release + 2 * x.ServerFrame + 1e-6
            && x.ClientReleased >= 0 && x.ClientEnd >= 0;
        /// <summary>The client's throw begins in the frame its button is seen up, and its hand lets go at the release moment.</summary>
        static bool AtOnce(Toss x, float release) => x.Up >= 0 && Math.Abs(x.ClientThrow - x.Up) < 1e-9 && x.ClientReleased - x.Up <= release + x.ClientFrame + 1e-6;

        /// <summary>A stack of one throwable in slot <paramref name="slot"/> of player 1 on every end, in hand and drawn.</summary>
        static void HoldGrenade(int slot, int kind) {
            int value = ScGrenadeBlock.Value(kind);
            foreach (var w in new[] { S.W, A.W, B }) { w.Inventory(1).m_slots[slot] = value; w.Inventory(1).ActiveSlotIndex = slot; }
            Frames(100);                                                      // the draw finishes
        }

        static void Throws() {
            s_up = s_down = 3; s_serverEvery = 1;
            s_serverThrows = ArmThrows(S); s_clientThrows = ArmThrows(A);
            object ticks = StartTicks.GetValue(null);
            try { T01(); T02(); T03(); T04(); T05(); T06(); T07(); }
            finally { StartTicks.SetValue(null, ticks); }
        }

        // ================================================================ T01 a tap, on every pairing of clocks and link
        static void T01() {
            HoldGrenade(3, 0);
            foreach (var pace in Paces) {
                Begin(pace); Advance(s_clock + .3, _ => false);
                var lines = new List<string>(); bool ok = true;
                foreach (var (low, hold) in new[] { (false, .05), (true, .03), (false, .15), (true, .4) }) {
                    float release = ReleaseMoment(0, low);
                    var x = Throw(low, hold);
                    bool good = AtOnce(x, release) && Corresponds(x, release) && !x.ClientReady && !x.ServerReady;
                    ok &= good;
                    lines.Add($"{(low ? "weak" : "strong")} {hold * 1000:0} ms: up>{(x.ClientReleased - x.Up) * 1000:0} ms client, word>{(x.Made - x.WordArrives) * 1000:0} ms server, x{x.MadeCount}" + (good ? "" : " [" + x.Line + "]"));
                }
                Test("T01", $"{pace.Name}: a throw let go before the pull is through (30-400 ms, strong and weak): the client's throw begins in the frame the button goes up; the server makes exactly one grenade, when the client's word arrives plus the throw's own release moment, never held ready first",
                    ok, string.Join("; ", lines));
            }
        }

        // ================================================================ T02 a hold is never thrown early
        static void T02() {
            HoldGrenade(3, 1);
            foreach (var pace in new[] { Paces[0], Paces[2], Paces[6], Paces[11], Paces[12] }) {
                Begin(pace); Advance(s_clock + .3, _ => false);
                var lines = new List<string>(); bool ok = true;
                foreach (bool low in new[] { false, true }) {
                    float release = ReleaseMoment(1, low); double hold = 1.7;
                    var x = Throw(low, hold);
                    // nothing leaves either hand while the button is down, whatever the spacing of what the server receives
                    bool good = x.ClientReady && x.ServerReady && x.ClientThrow >= x.Up - 1e-9 && x.ServerThrow >= x.WordArrives - 1e-9 && AtOnce(x, release) && Corresponds(x, release);
                    ok &= good;
                    lines.Add($"{(low ? "weak" : "strong")}: held {(x.Up - x.Down):0.000} s, both ends held ready, server throw begins {(x.ServerThrow - x.WordArrives) * 1000:0} ms after the word, x{x.MadeCount}" + (good ? "" : " [" + x.Line + "]"));
                }
                Test("T02", $"{pace.Name}: held 1.7 s: both ends pull and hold ready; the server throws nothing until the client's word that its throw has begun, then exactly one", ok, string.Join("; ", lines));
            }
        }

        // ================================================================ T03 the start and the release in one server frame
        static void T03() {
            HoldGrenade(3, 0);
            foreach (var pace in new[] { Paces[12], Paces[2] }) {
                Begin(pace); Advance(s_clock + .3, _ => false);
                int together = 0, good = 0, n = 0; var bad = new List<string>();
                for (int i = 0; i < 12; i++) {
                    bool low = i % 2 == 1; float release = ReleaseMoment(0, low);
                    var x = Throw(low, .02 + .003 * i, 2.3 + .037 * i);
                    n++;
                    if (x.Prepared >= 0 && Math.Abs(x.Prepared - x.ServerThrow) < 1e-9) together++;
                    if (AtOnce(x, release) && Corresponds(x, release)) good++; else bad.Add(x.Line);
                }
                Test("T03", $"{pace.Name}: twelve taps of 20-55 ms: when the start and the client's word are taken in the same server frame the throw still begins there and makes one grenade", good == n && together >= 1,
                    $"{good}/{n} correspond; start and word in one server frame: {together}" + (bad.Count > 0 ? " :: " + string.Join(" | ", bad.Take(2)) : ""));
            }
        }

        // ================================================================ T04 taps in a row
        static void T04() {
            HoldGrenade(3, 1);
            foreach (var pace in new[] { Paces[0], Paces[11] }) {
                Begin(pace); Advance(s_clock + .3, _ => false);
                int made = MadeOnServer, released = 0; bool was = false; double start = s_clock; var times = new List<double>();
                // a press every 0.3 s for 9 s: most fall inside a running throw or before the next grenade is in hand
                var x = Throw(false, 0, 9.5, held: t => t >= .1 && t < 9.1 && (t - .1) % .3 < .06, client: (t, _) => {
                    var phase = s_clientThrows.ThrowPhase(A.P); bool now = phase.Active && phase.Released;
                    if (now && !was) { released++; times.Add(t - start); }
                    was = now;
                });
                int server = MadeOnServer - made;
                Test("T04", $"{pace.Name}: a press every 0.3 s for 9 s: every throw the client's hand lets go is one grenade on the server, none more, none refused; presses during a throw or its recovery throw nothing",
                    released >= 3 && server == released, $"client let go {released} times (at {string.Join(" ", times.Select(t => t.ToString("0.00")))}), server made {server}");
            }
        }

        // ================================================================ T05 repeated and stray messages
        static void T05() {
            HoldGrenade(3, 0);
            Begin(Paces[0]); Advance(s_clock + .3, _ => false);
            var view = (new Vector3(3, 79.5f, 0), new Vector3(3, 79.5f, 0), Vector3.UnitY);
            // nothing in hand being thrown: a release, a cancel and a held word on their own
            int before = MadeOnServer;
            Advance(s_clock + .5, _ => false, client: t => { RealTimeIs(t); if (s_clientFrames % 7 == 3) { ScNetGrenades.SendHeld(s_clientFrames % 2 == 0, view); ScNetGrenades.SendCancel(); } },
                server: t => { RealTimeIs(t); ((IUpdateable)s_serverThrows).Update(S.Time.m_gameTimeDelta); });
            bool strayNothing = MadeOnServer == before && !s_serverThrows.ThrowPhase(S.P).Active;
            // a tap whose start and release are each sent a second time: one frame later, and again 0.3 s later
            int repeats = 0;
            var x = Throw(false, .05, client: (t, toss) => {
                if (toss.ClientThrow < 0) return;
                double since = t - toss.ClientThrow;
                if (repeats == 0 && since > 0 || repeats == 1 && since >= .3) { repeats++; ScNetGrenades.SendStart(false, view); ScNetGrenades.SendHeld(false, view); }
            });
            float release = ReleaseMoment(0, false);
            Test("T05", "a release, a cancel and a held word with no throw in hand do nothing; a tap whose start and release are sent again (a frame later, and 0.3 s later, inside its follow-through) is still one grenade",
                strayNothing && repeats == 2 && Corresponds(x, release), $"stray messages: {(strayNothing ? "nothing made, nothing prepared" : "something happened")}; repeated {repeats} times: {x.Line}");
        }

        // ================================================================ T06 cancelled before it is committed
        static void T06() {
            HoldGrenade(3, 0);
            foreach (var w in new[] { S.W, A.W, B }) w.Inventory(1).m_slots[4] = S.W.Slot(1, 0);   // (whatever slot 0 holds: not a throwable in hand)
            foreach (var (name, low, hold, after) in new[] { ("during the pull, the button still down", false, 1.2, -.7), ("after a tap of the weak throw, before its release moment", true, .05, .05) }) {
                Begin(Paces[0]); Advance(s_clock + .3, _ => false);
                foreach (var w in new[] { S.W, A.W, B }) w.Inventory(1).ActiveSlotIndex = 3;
                Advance(s_clock + 1.4, _ => false, client: t => { RealTimeIs(t); s_clientThrows.SetThrowButton(A.P, low, false, false, false, 2); ((IUpdateable)s_clientThrows).Update(A.Time.m_gameTimeDelta); },
                    server: t => { RealTimeIs(t); ((IUpdateable)s_serverThrows).Update(S.Time.m_gameTimeDelta); });
                double switchAt = -1, arrives = -1; bool clientThrew = false; int next = MadeOnServer;
                var x = Throw(low, hold, 2.0, client: (t, toss) => {
                    double mark = after < 0 ? toss.Down - after : toss.Up + after;
                    if (switchAt < 0 && (after < 0 ? toss.Down >= 0 : toss.Up >= 0) && t >= mark - 1e-9) { switchAt = t; arrives = Math.Max(s_upLast, t + s_pace.Up(t)); A.W.Inventory(1).ActiveSlotIndex = 4; B.Inventory(1).ActiveSlotIndex = 4; }
                    clientThrew |= ShownOnClient(next);
                }, server: (t, _) => { if (arrives >= 0 && t >= arrives && S.W.Inventory(1).ActiveSlotIndex != 4) S.W.Inventory(1).ActiveSlotIndex = 4; });
                Enter(S.W, true); bool serverIdle = !s_serverThrows.ThrowPhase(S.P).Active; Enter(A.W, false); bool clientIdle = !s_clientThrows.ThrowPhase(A.P).Active;
                Test("T06", $"switching to another slot {name}: the throw is taken back on both ends; no grenade is made, none is shown", switchAt >= 0 && x.MadeCount == 0 && x.ClientReleased < 0 && serverIdle && clientIdle && !clientThrew,
                    $"switched {(switchAt - x.Start):0.000}; grenades {x.MadeCount}; afterwards the server {(serverIdle ? "has no throw" : "still has a throw")}, the client {(clientIdle ? "has none" : "still has one")}{(clientThrew ? ", the client showed a grenade" : "")}; {x.Line}");
            }
            foreach (var w in new[] { S.W, A.W, B }) w.Inventory(1).ActiveSlotIndex = 3;
        }

        // ================================================================ T07 what the host and the other clients are shown
        static void T07() {
            HoldGrenade(3, 2);
            foreach (var pace in new[] { Paces[0], Paces[5] }) {
                // (T06 left another slot as "the one held before": a finished throw returns to it, so the grenade is taken out anew)
                foreach (var w in new[] { S.W, A.W, B }) w.Inventory(1).ActiveSlotIndex = 3;
                Begin(pace); Advance(s_clock + 1.4, _ => false, client: t => { RealTimeIs(t); s_clientThrows.SetThrowButton(A.P, false, false, false, false, 2); ((IUpdateable)s_clientThrows).Update(A.Time.m_gameTimeDelta); },
                    server: t => { RealTimeIs(t); ((IUpdateable)s_serverThrows).Update(S.Time.m_gameTimeDelta); });
                float release = ReleaseMoment(2, false); double delay = pace.Up(0);
                var x = Throw(false, .05);
                // The host shows that player from its own run of the throw; other clients from the thrower's word, relayed
                // (20 times a second). Neither keeps the grenade in the hand once it has left.
                bool host = x.HostReleased >= 0 && Math.Abs(x.HostReleased - x.Made) < 1e-9;
                bool told = x.ToldReleased >= 0 && x.ToldReleased - x.ClientReleased <= delay + ScNetPresentation.PhaseInterval + x.ServerFrame + x.ClientFrame + 1e-6 && x.ToldEnded >= 0;
                // and the finished throw returns each end to the slot held before the grenade
                bool back = A.W.Inventory(1).ActiveSlotIndex == 4 && S.W.Inventory(1).ActiveSlotIndex == 4;
                Test("T07", $"{pace.Name}: a tap as others see it: the host's copy of that player lets go in the frame the grenade is made; the word relayed to the other clients says the hand is empty within the link's delay and one refresh of the client letting go, and ends with the throw; both ends then hold the slot held before the grenade",
                    Corresponds(x, release) && host && told && back, $"client lets go {(x.ClientReleased - x.Start):0.000}; grenade and host's view {(x.Made - x.Start):0.000}/{(x.HostReleased - x.Start):0.000}; relayed word: empty hand {(x.ToldReleased - x.Start):0.000}, ended {(x.ToldEnded < 0 ? "never" : (x.ToldEnded - x.Start).ToString("0.000"))} :: {x.Line}");
            }
        }
    }
}
