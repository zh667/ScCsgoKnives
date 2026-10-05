// mpd2-ammo-jitter-20261002 (OpenSpec weapon-state-consistency tasks 12, verification 11): a short burst, then the readout
// rose by one after the trigger went up.
//
// GunLoop.cs steps the client and the server on one tick, the server every n-th client frame. That cannot show what the
// user's two devices do: each end has its own frame clock, and a shot is taken in the first frame at or after its time.
// Here the two ends run on independent clocks over one time line: any pair of frame rates (100/50, 60/50, 144/50 ...),
// frame durations that vary, a host that stalls now and then, and a link whose delay is fixed, zero, or varies while
// keeping the order (so messages bunch and arrive several to a server frame).
//
// What is recorded is not only the end state. For every burst: each shot the client showed (when), each report of it the
// server took (when), each shot the server executed (when), what the server marked as not fired, and every rise of the
// readout. The checks then ask for the correspondence itself: the server executed exactly the shots the client showed,
// in the frame their report arrived (or within the stated bound when the link bunches), never after that, never faster
// than the gun's cadence allows, and nothing was "settled as skipped" to make the numbers meet.
//
// Still not covered: a real link (loss and retransmission, clock drift between devices), the Android runtime, anything
// seen or heard. Written against members the delivered mpd2 build has as well, so the same run against mpd2 shows the fault.
using System.Reflection;
using Engine;
using Game;
using Game.Network;

static partial class StateLoop {
    static partial class GunLoop {
        /// <summary>How the two ends and the link behave: the k-th frame's duration at each end, the delay of a message
        /// sent at time t in each direction (order is kept: a message never overtakes an earlier one).</summary>
        sealed class Pace {
            public string Name; public Func<int, double> Client, Server; public Func<double, double> Up, Down; public double Phase;
            /// <summary>The link neither bunches nor stalls: a shot must be executed in the server frame its report arrives.</summary>
            public bool Steady = true;
        }
        static Func<int, double> Fps(double fps) { float dt = (float)(1 / fps); return _ => dt; }            // (a float, as the engine's frame time is)
        /// <summary>Frame durations spread ±<paramref name="spread"/> around the rate, repeatable.</summary>
        static Func<int, double> Uneven(double fps, double spread, uint seed) => k => (1 / fps) * (1 + spread * (2 * Noise(seed, k) - 1));
        /// <summary>A host that stalls: every <paramref name="every"/>-th frame lasts <paramref name="stall"/> seconds.</summary>
        static Func<int, double> Stalling(double fps, int every, double stall) => k => k % every == every - 1 ? stall : (float)(1 / fps);
        static double Noise(uint seed, int k) { uint x = seed ^ (uint)(k * 0x9E3779B1); x ^= x >> 16; x *= 0x7FEB352D; x ^= x >> 15; x *= 0x846CA68B; x ^= x >> 16; return x / 4294967296.0; }
        static Func<double, double> Fixed(double seconds) => _ => seconds;
        /// <summary>The delay rises by <paramref name="extra"/> for <paramref name="window"/> seconds out of every <paramref name="period"/>.</summary>
        static Func<double, double> Periodic(double seconds, double period, double window, double extra) => t => seconds + (t % period < window ? extra : 0);
        static Func<double, double> Random(double from, double to, uint seed) => t => from + (to - from) * Noise(seed, (int)(t * 97));

        sealed class Burst {
            public double Hold, Start, ReleaseAt = -1;
            public readonly List<(int N, double T)> Shown = [], Reported = [], Fired = [];
            public readonly List<(double T, int N)> Skipped = [];
            public readonly List<(double T, int From, int To)> Rises = [];
            public int HudStart, HudEnd, ServerStart, ServerEnd, PendingEnd, MirrorEnd, Held;
            public int SkippedCount => Skipped.Sum(s => s.N);
            /// <summary>For each executed shot, how long after its report was taken (the i-th executed against the i-th reported).</summary>
            public IEnumerable<double> Lags => Fired.Zip(Reported, (f, r) => f.T - r.T);
            public string Line => $"hold {Hold:0.000}: shown {Shown.Count}, executed {Fired.Count}, skipped {SkippedCount}" + (Rises.Count > 0 ? $", readout rose {string.Join(",", Rises.Select(r => $"{r.From}->{r.To} at {r.T - Start:0.000}"))}" : "");
            public string Times => $"shown at [{string.Join(" ", Shown.Select(s => (s.T - Start).ToString("0.000")))}] executed at [{string.Join(" ", Fired.Select(s => (s.T - Start).ToString("0.000")))}]"
                + (Skipped.Count > 0 ? $" skipped at [{string.Join(" ", Skipped.Select(s => $"{s.T - Start:0.000}x{s.N}"))}]" : "");
        }

        // ---------------------------------------------------------------- two clocks
        static Pace s_pace; static double s_nextClient, s_nextServer, s_prevClient, s_prevServer, s_upLast, s_downLast;
        static int s_clientFrames, s_serverFrames, s_frameIndex;
        static readonly Queue<(double At, List<Packet> Batch)> s_upQueue = new(), s_downQueue = new();
        static readonly FieldInfo PredictionField = typeof(ScNetGuns).GetField("s_prediction", All);
        /// <summary>Shots the client has shown for the gun in hand (its own count, under the present selection).</summary>
        static int ShownShots() => PredictionField.GetValue(null) is { } p && p.GetType().GetField("Shots", All).GetValue(p) is int n ? n : 0;
        static ScRemoteGunInput Remote() { Enter(S.W, true); return ScNetGuns.RemoteInput(S.P); }

        /// <summary>Everything still on the tick-driven queues is delivered, and the two clocks start from now.</summary>
        static void Begin(Pace pace) {
            while (s_toServer.Count > 0) ToServer(S.W, s_toServer.Dequeue().Batch);
            Enter(S.W, true); EndServerFrame(S.W); s_toClients.Enqueue((0, Take()));
            while (s_toClients.Count > 0) { var batch = s_toClients.Dequeue().Batch; Deliver(batch, A.W, s_sessionA); Deliver(batch, B, s_sessionB); }
            s_pace = pace; s_prevClient = s_prevServer = s_upLast = s_downLast = s_clock; s_clientFrames = s_serverFrames = 0;
            s_nextClient = s_clock + pace.Client(0); s_nextServer = s_clock + pace.Phase + pace.Server(0);
            s_upQueue.Clear(); s_downQueue.Clear();
        }
        /// <summary>Runs both ends until <paramref name="until"/> (shared time); <paramref name="trigger"/> says whether the
        /// client's fire input is down at a client frame's time.</summary>
        static void Advance(double until, Func<double, bool> trigger, Burst burst = null, bool mouse = false, Action<double> client = null, Action<double> server = null) {
            while (true) {
                bool clientTurn = s_nextClient <= s_nextServer; double t = clientTurn ? s_nextClient : s_nextServer;
                if (t >= until) break;
                s_clock = t; KnifeNow.SetValue(null, t);
                if (clientTurn) { ClientStep(t, trigger(t), burst, mouse, client); s_nextClient = t + s_pace.Client(++s_clientFrames); }
                else { ServerStep(t, burst, server); s_nextServer = t + s_pace.Server(++s_serverFrames); }
            }
        }
        static void ClientStep(double t, bool fire, Burst burst, bool mouse, Action<double> hook) {
            double dt = t - s_prevClient; s_prevClient = t;
            while (s_downQueue.Count > 0 && s_downQueue.Peek().At <= t) { var batch = s_downQueue.Dequeue().Batch; Deliver(batch, A.W, s_sessionA); Deliver(batch, B, s_sessionB); }
            Enter(A.W, false); FrameIndex.SetValue(null, 400000 + ++s_frameIndex);
            A.Time.m_gameTime = t; A.Time.m_gameTimeDelta = (float)dt;
            var ray = new Ray3(new Vector3(3, 80, 0), Vector3.UnitY);
            A.P.ComponentInput.m_playerInput = mouse ? new PlayerInput { Dig = fire ? ray : null, Hit = fire && !s_was ? ray : null } : new PlayerInput();
            A.Guns.SetFireButton(A.P, fire && !mouse); s_was = fire;
            hook?.Invoke(t);
            int before = ShownShots();
            A.Guns.Update((float)dt);
            int after = ShownShots();
            double at = Math.Max(s_upLast, t + s_pace.Up(t)); s_upLast = at; s_upQueue.Enqueue((at, Take()));
            if (burst is null) return;
            for (int n = before + 1; n <= after; n++) burst.Shown.Add((n, t));
            int hud = Hud(), held = A.P.ComponentMiner.ActiveBlockValue;
            // (a rise is a rise of the same gun's readout: another gun taken in hand, or a reload, is not one)
            if (held == burst.Held && burst.HudEnd >= 0 && hud > burst.HudEnd && !Reloading(A)) burst.Rises.Add((t, burst.HudEnd, hud));
            burst.HudEnd = hud; burst.Held = held;
        }
        static int s_reported, s_fired, s_skipped; static bool s_held;
        static void ServerStep(double t, Burst burst, Action<double> hook) {
            double dt = t - s_prevServer; s_prevServer = t;
            Enter(S.W, true);
            while (s_upQueue.Count > 0 && s_upQueue.Peek().At <= t) ToServer(S.W, s_upQueue.Dequeue().Batch);
            Enter(S.W, true); FrameIndex.SetValue(null, 400000 + ++s_frameIndex);
            S.Time.m_gameTime = t; S.Time.m_gameTimeDelta = (float)dt;
            var remote = ScNetGuns.RemoteInput(S.P) ?? new ScRemoteGunInput();                                  // (null once the connection is gone)
            if (remote.ClientShots < s_reported || remote.Fired < s_fired) { s_reported = remote.ClientShots; s_fired = remote.Fired; s_skipped = remote.Skipped; }   // another selection: counts start over
            if (burst is not null) {
                for (int n = s_reported + 1; n <= remote.ClientShots; n++) burst.Reported.Add((n, t));
                bool held = remote.Dig || remote.Custom;
                if (s_held && !held && burst.ReleaseAt < 0) burst.ReleaseAt = t;
                s_held = held;
            }
            s_reported = remote.ClientShots;
            hook?.Invoke(t);
            var kept = ClientStatics.Select(f => f.GetValue(null)).ToArray();
            S.Guns.Update((float)dt);
            for (int i = 0; i < kept.Length; i++) ClientStatics[i].SetValue(null, kept[i]);
            if (burst is not null) {
                for (int n = s_fired + 1; n <= remote.Fired; n++) burst.Fired.Add((n, t));
                if (remote.Skipped > s_skipped) burst.Skipped.Add((t, remote.Skipped - s_skipped));
            }
            s_fired = remote.Fired; s_skipped = remote.Skipped;
            EndServerFrame(S.W);
            double at = Math.Max(s_downLast, t + s_pace.Down(t)); s_downLast = at; s_downQueue.Enqueue((at, Take()));
        }
        /// <summary>One press held for <paramref name="hold"/> seconds, then <paramref name="settle"/> seconds with the trigger up.</summary>
        static Burst Fire(double hold, double settle = .7, bool mouse = false, Action<double> client = null, Action<double> server = null, Func<double, bool> trigger = null) {
            var remote = Remote() ?? new ScRemoteGunInput(); s_reported = remote.ClientShots; s_fired = remote.Fired; s_skipped = remote.Skipped; s_held = false;
            var burst = new Burst { Hold = hold, Start = s_clock, HudStart = Hud(), ServerStart = Server(), Held = A.P.ComponentMiner.ActiveBlockValue }; burst.HudEnd = burst.HudStart;
            double start = s_clock;
            Advance(start + hold + settle, t => trigger is null ? t - start < hold : trigger(t - start), burst, mouse, client, server);
            burst.ServerEnd = Server(); burst.PendingEnd = Pending(); burst.MirrorEnd = Mirror();
            return burst;
        }
        static double Interval(int variant) => ScGunGrowth.ShotInterval(variant, GunSpec.All[variant].CycleSeconds, 0);
        /// <summary>The bound the server allows a client's shot around its own schedule (ScNetGuns.ShotLead; 0 on a build without it).</summary>
        static double Lead => typeof(ScNetGuns).GetField("ShotLead", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() is double d ? d : 0;

        static bool Matched(Burst b) => b.Shown.Count == b.Fired.Count && b.SkippedCount == 0 && b.Rises.Count == 0 && b.HudEnd == b.ServerEnd && b.MirrorEnd == b.ServerEnd && b.PendingEnd == 0
            && b.ServerStart - b.ServerEnd == b.Fired.Count && b.HudStart - b.HudEnd == b.Shown.Count;
        /// <summary>No two executed shots closer than the cadence allows: j-i intervals less the stated lead, for every pair.</summary>
        static bool WithinRate(Burst b, double interval, double lead) {
            for (int i = 0; i < b.Fired.Count; i++) for (int j = i + 1; j < b.Fired.Count; j++) if (b.Fired[j].T - b.Fired[i].T < (j - i) * interval - lead - 1e-6) return false;
            return true;
        }
        static string Worst(IEnumerable<Burst> bursts) => string.Join(" | ", bursts.Where(b => !Matched(b)).Take(3).Select(b => b.Line + " " + b.Times));

        static readonly Pace[] Paces = [
            new() { Name = "client 100 fps, host 50 fps, 50 ms each way", Client = Fps(100), Server = Fps(50), Up = Fixed(.05), Down = Fixed(.05) },
            new() { Name = "client 100 fps, host 50 fps, no transit delay", Client = Fps(100), Server = Fps(50), Up = Fixed(0), Down = Fixed(0) },
            new() { Name = "client 100 fps, host 50 fps, delay rising by 90 ms in ordered bunches", Client = Fps(100), Server = Fps(50), Up = Periodic(.05, .37, .09, .09), Down = Periodic(.05, .43, .11, .07), Steady = false },
            new() { Name = "client 60 fps, host 50 fps, 50 ms each way", Client = Fps(60), Server = Fps(50), Up = Fixed(.05), Down = Fixed(.05), Phase = .003 },
            new() { Name = "client 60 fps, host 60 fps, 50 ms each way", Client = Fps(60), Server = Fps(60), Up = Fixed(.05), Down = Fixed(.05), Phase = .007 },
            new() { Name = "client 60 fps, host 60 fps, 250 ms each way", Client = Fps(60), Server = Fps(60), Up = Fixed(.25), Down = Fixed(.25), Phase = .004 },
            new() { Name = "client 60 fps, host 60 fps, delay rising by 150 ms in ordered bunches", Client = Fps(60), Server = Fps(60), Up = Periodic(.05, .62, .15, .15), Down = Periodic(.05, .72, .18, .12), Steady = false },
            new() { Name = "client 60 fps, host 30 fps, 50 ms each way", Client = Fps(60), Server = Fps(30), Up = Fixed(.05), Down = Fixed(.05) },
            new() { Name = "client 144 fps, host 50 fps, 30 ms each way", Client = Fps(144), Server = Fps(50), Up = Fixed(.03), Down = Fixed(.03), Phase = .001 },
            new() { Name = "client 50 fps, host 100 fps, 50 ms each way", Client = Fps(50), Server = Fps(100), Up = Fixed(.05), Down = Fixed(.05), Phase = .002 },
            new() { Name = "client ~100 fps, host ~50 fps, frame times varying by 35%, 50 ms each way", Client = Uneven(100, .35, 11), Server = Uneven(50, .35, 12), Up = Fixed(.05), Down = Fixed(.05) },
            new() { Name = "client ~60 fps, host ~50 fps, frame times varying by 35%, delay 20-120 ms in order", Client = Uneven(60, .35, 21), Server = Uneven(50, .35, 22), Up = Random(.02, .12, 23), Down = Random(.02, .12, 24), Steady = false },
            new() { Name = "client 100 fps, host 50 fps stalling 120 ms every 23rd frame (inputs arrive in batches), 50 ms each way", Client = Fps(100), Server = Stalling(50, 23, .12), Up = Fixed(.05), Down = Fixed(.05), Steady = false },
        ];

        static void Cadence() {
            s_up = s_down = 3; s_serverEvery = 1;
            G10(); G11(); G12(); G13(); G14(); G15(); G16();
        }

        // ================================================================ G10 short bursts on two clocks, every release phase
        static void G10() {
            double interval = Interval(s_ak), lead = Lead;
            int slot = 0;
            foreach (var pace in Paces) {
                var bursts = new List<Burst>();
                // the user's three bursts (0.6, 0.6, 0.5 s), then releases at twelve phases across one shot interval
                var holds = new List<double> { .60, .60, .50 }; for (int i = 0; i < 12; i++) holds.Add(.33 + interval * i / 12 + .0007);
                int giveUps = GiveUps, dropped = Dropped;
                for (int i = 0; i < holds.Count; i++) {
                    if (i % 4 == 0) { Hold(slot = (slot + 1) % 9, s_ak, 30); Begin(pace); Advance(s_clock + .2, _ => false); }
                    bursts.Add(Fire(holds[i]));
                }
                int shown = bursts.Sum(b => b.Shown.Count), executed = bursts.Sum(b => b.Fired.Count), skipped = bursts.Sum(b => b.SkippedCount), rises = bursts.Sum(b => b.Rises.Count);
                Test("G10", $"{pace.Name}: {holds.Count} short bursts, released at every phase of the shot interval: the server executes exactly the shots the client showed - none marked as not fired, the readout never rises after the trigger goes up",
                    bursts.All(Matched) && shown > 40 && GiveUps == giveUps && Dropped == dropped,
                    $"shown {shown}, executed {executed}, skipped {skipped}, readout rises {rises} in {bursts.Count(b => b.Rises.Count > 0)} of {bursts.Count} bursts" + (bursts.All(Matched) ? "" : " :: " + Worst(bursts)) + " :: first three: " + string.Join("; ", bursts.Take(3).Select(b => b.Line)));
                double worstLag = bursts.SelectMany(b => b.Lags).DefaultIfEmpty(0).Max(), frame = bursts.Count > 0 ? 0 : 0;
                double afterRelease = bursts.Where(b => b.ReleaseAt >= 0 && b.Fired.Count > 0).Select(b => b.Fired[^1].T - b.ReleaseAt).DefaultIfEmpty(0).Max();
                Test("G10", $"{pace.Name}: each shot is executed {(pace.Steady ? "in the server frame its report arrives" : $"within {lead * 1000:0} ms of its report")}, none later than that after the release was taken; and never faster than the gun's cadence",
                    bursts.All(b => b.Lags.All(l => l <= (pace.Steady ? 1e-9 : lead + 1e-6))) && afterRelease <= (pace.Steady ? 1e-9 : lead + 1e-6) && bursts.All(b => WithinRate(b, interval, lead)),
                    $"longest wait after a report {worstLag * 1000:0.0} ms; latest execution after the release was taken {afterRelease * 1000:0.0} ms; smallest gap between executed shots {bursts.SelectMany(b => b.Fired.Zip(b.Fired.Skip(1), (x, y) => y.T - x.T)).DefaultIfEmpty(0).Min() * 1000:0.0} ms (cadence {interval * 1000:0.0} ms, allowance {lead * 1000:0} ms)");
            }
        }

        // ================================================================ G11 what the server does not take on the client's word
        static void G11() {
            double interval = Interval(s_ak), lead = Lead;
            var pace = Paces[0];
            // five shots claimed in one frame, no trigger: at most what the cadence allows at once is executed, the rest is said not to have happened
            Hold(1, s_ak, 30); Begin(pace); Advance(s_clock + .2, _ => false);
            bool claimed = false;
            var burst = Fire(0, 1.0, client: t => { if (!claimed) { claimed = true; Enter(A.W, false); for (int i = 0; i < 5; i++) ScNetGuns.PredictShot(A.P); } });
            // (n shots take at least n-1 intervals less the allowance, and nothing runs later than the allowance after its report)
            int allowed = 1 + (int)Math.Floor(2 * lead / interval + 1e-9);
            Test("G11", "five shots claimed at once without the time for them: the server executes only what the gun's cadence and its stated allowance permit, says the rest did not happen, and the client's readout comes back to the server's rounds",
                burst.Fired.Count >= 1 && burst.Fired.Count <= allowed && burst.Fired.Count + burst.SkippedCount == 5 && burst.ServerStart - burst.ServerEnd == burst.Fired.Count && burst.HudEnd == burst.ServerEnd && burst.PendingEnd == 0 && WithinRate(burst, interval, lead),
                $"claimed 5, executed {burst.Fired.Count} (at most {allowed}), skipped {burst.SkippedCount}; server rounds {burst.ServerStart}->{burst.ServerEnd}, readout ends {burst.HudEnd}");
            // a held trigger with twice as many shots claimed as shown by the cadence
            Hold(2, s_ak, 30); Begin(pace); Advance(s_clock + .2, _ => false);
            int last = 0;
            burst = Fire(1.0, 1.0, client: t => { Enter(A.W, false); int n = ShownShots(); if (n > last) { ScNetGuns.PredictShot(A.P); last = ShownShots(); } });
            int most = (int)Math.Floor((1.0 + lead) / interval + 1e-9) + 2;
            Test("G11", "a held trigger claiming two shots for every one the gun can fire: the server fires at the gun's cadence, the excess is said not to have happened, nothing extra is spent",
                burst.Fired.Count <= most && burst.SkippedCount >= 8 && WithinRate(burst, interval, lead) && burst.ServerStart - burst.ServerEnd == burst.Fired.Count && burst.HudEnd == burst.ServerEnd && burst.PendingEnd == 0,
                $"claimed {burst.Fired.Count + burst.SkippedCount}, executed {burst.Fired.Count} in 1.0 s (at most {most}), skipped {burst.SkippedCount}; readout ends {burst.HudEnd}, server {burst.ServerEnd}");
        }

        // ================================================================ G12 the R8's cocked shot, released around the moment it fires
        static void G12() {
            int r8 = Array.FindIndex(GunSpec.All, g => g.Name == "revolver"); double cock = Interval(r8);
            foreach (var pace in new[] { Paces[0], Paces[3], Paces[6], Paces[10] }) {
                Hold(3, r8, GunSpec.All[r8].Magazine); Begin(pace); Advance(s_clock + .2, _ => false);
                var bursts = new List<Burst>(); int rounds = GunSpec.All[r8].Magazine;
                // released before the hammer falls, at it, just after it; then long enough for two shots
                foreach (double hold in new[] { cock - .08, cock - .02, cock + .005, cock + .012, cock + .03, cock + .08 }) {
                    if (rounds < 2) { Hold(4, r8, GunSpec.All[r8].Magazine); Begin(pace); Advance(s_clock + .2, _ => false); rounds = GunSpec.All[r8].Magazine; }
                    var b = Fire(hold, 1.0); bursts.Add(b); rounds -= b.Fired.Count;
                }
                Hold(4, r8, GunSpec.All[r8].Magazine); Begin(pace); Advance(s_clock + .2, _ => false);
                var two = Fire(2 * cock + .2, 1.0); bursts.Add(two);
                Test("G12", $"R8, {pace.Name}: the trigger let go before, at and just after the hammer falls, and held for two shots: the server commits the cocked shot exactly when the client showed it, never one the client did not show",
                    bursts.All(Matched) && bursts.Take(2).All(b => b.Shown.Count == 0) && bursts.Skip(4).Take(2).All(b => b.Shown.Count == 1) && two.Shown.Count == 2,
                    string.Join("; ", bursts.Select(b => b.Line)) + (bursts.All(Matched) ? "" : " :: " + Worst(bursts)));
            }
        }

        // ================================================================ G13 three-round bursts
        static void G13() {
            int famas = Array.FindIndex(GunSpec.All, g => g.Name == "famas"), glock = s_glock;
            foreach (var (variant, name) in new[] { (famas, "FAMAS"), (glock, "Glock-18") }) foreach (var pace in new[] { Paces[0], Paces[3], Paces[11] }) {
                Hold(5, variant, GunSpec.All[variant].Magazine); Begin(pace); Advance(s_clock + .2, _ => false);
                foreach (var end in new[] { A, S }) { object state = State(end); state.GetType().GetField("BurstMode", All).SetValue(state, true); }
                var bursts = new List<Burst>();
                // taps of different lengths: shorter than the burst, as long as it, held into the next one
                foreach (double hold in new[] { .03, .09, .16, .30 }) bursts.Add(Fire(hold, 1.1));
                Test("G13", $"{name} in burst mode, {pace.Name}: each burst is three rounds on both ends, whatever the length of the press", bursts.All(Matched) && bursts.All(b => b.Shown.Count % 3 == 0 && b.Shown.Count >= 3),
                    string.Join("; ", bursts.Select(b => b.Line)) + (bursts.All(Matched) ? "" : " :: " + Worst(bursts)));
            }
        }

        // ================================================================ G14 single shots pressed at about the gun's own cadence
        static void G14() {
            int deagle = Array.FindIndex(GunSpec.All, g => g.Name == "deagle"); double interval = Interval(deagle), lead = Lead;
            foreach (var pace in new[] { Paces[0], Paces[3], Paces[6], Paces[11] }) {
                var bursts = new List<Burst>();
                foreach (double every in new[] { interval - .02, interval + .004, interval + .03 }) {
                    Hold(6, deagle, GunSpec.All[deagle].Magazine); Begin(pace); Advance(s_clock + .2, _ => false);
                    // presses of 40 ms, one every `every` seconds, for 1.5 s (faster than the cadence: some presses fire nothing on the client)
                    bursts.Add(Fire(1.5, 1.0, mouse: true, trigger: t => t < 1.5 && t % every < .04));
                }
                Test("G14", $"Desert Eagle tapped slightly faster than, at and slightly slower than its cadence, {pace.Name}: every shot the client showed is the server's, none more, none fewer, and none faster than the cadence", bursts.All(Matched) && bursts.All(b => b.Shown.Count >= 4) && bursts.All(b => WithinRate(b, interval, lead)),
                    string.Join("; ", bursts.Select(b => b.Line)) + (bursts.All(Matched) ? "" : " :: " + Worst(bursts)));
            }
        }

        // ================================================================ G15 switching away and a menu, mid-burst
        static void G15() {
            foreach (var pace in new[] { Paces[0], Paces[3], Paces[11] }) {
                Hold(7, s_ak, 30); int ak = S.W.Slot(1, 7);
                Enter(S.W, true); int other = Instance(s_ak, S.W.Registry.Allocate(s_ak, 30, false, ScGunDurability.Full(s_ak)));
                foreach (var w in new[] { S.W, A.W, B }) w.Inventory(1).m_slots[8] = other;
                Frames(20); Begin(pace); Advance(s_clock + .2, _ => false);
                // the player switches to slot 8 while the trigger is down; the server's copy of the active slot follows when the
                // platform's message arrives (after everything the client sent before it, as on the ordered channel)
                double switchAt = -1, arrives = -1; int shownAk = 0;
                var burst = Fire(2.4, .9, client: t => { if (switchAt < 0 && ShownShots() >= 4) { shownAk = ShownShots(); switchAt = t; arrives = Math.Max(s_upLast, t + pace.Up(t)); A.W.Inventory(1).ActiveSlotIndex = 8; B.Inventory(1).ActiveSlotIndex = 8; } },
                    server: t => { if (arrives >= 0 && t >= arrives && S.W.Inventory(1).ActiveSlotIndex != 8) S.W.Inventory(1).ActiveSlotIndex = 8; });
                int akSpent = 30 - Rounds(S.W, IdOf(ak), s_ak), otherSpent = 30 - Rounds(S.W, IdOf(other), s_ak), shownOther = burst.Shown.Count - shownAk;
                // (One case is not made to correspond: a shot shown in the client's last frame before the switch whose report
                // reaches the server in the same frame as the switch itself. The gun is then no longer in the server's hand;
                // the shot is refused, never executed from the other gun. It cannot happen on a link with a steady delay.)
                bool same = akSpent == shownAk && burst.SkippedCount == 0, refusedOne = akSpent == shownAk - 1 && burst.SkippedCount <= 1;
                Test("G15", $"{pace.Name}: switching to another gun with the trigger down: the shots shown with the first gun before the switch are the rounds the server took from it{(pace.Steady ? "" : " (or all but the one reported in the very frame of the switch)")}, the shots shown with the second are the rounds taken from that one, and no shot of one gun is executed from the other; neither readout rises",
                    switchAt >= 0 && shownAk >= 4 && (pace.Steady ? same : same || refusedOne) && otherSpent == shownOther && shownOther >= 2 && burst.Rises.Count == 0 && burst.PendingEnd == 0 && burst.HudEnd == burst.ServerEnd,
                    $"first gun: shown {shownAk}, rounds taken on the server {akSpent}; second gun: shown {shownOther}, taken {otherSpent}; skipped {burst.SkippedCount}; readout of the gun in hand ends {burst.HudEnd}, server {burst.ServerEnd}");
                // a menu opens on the client mid-burst: it stops showing shots, the server stops with it
                Hold(7, s_ak, 30); Begin(pace); Advance(s_clock + .2, _ => false);
                var modal = new CanvasWidget(); var panel = A.P.ComponentGui.m_modalPanelContainerWidget;
                burst = Fire(.9, .9, client: t => { if (ShownShots() >= 4 && panel.Children.Count == 0) panel.Children.Add(modal); });
                panel.Children.Remove(modal);
                Test("G15", $"{pace.Name}: a menu opened with the trigger down: the shots shown before it are the server's, none after", Matched(burst) && burst.Shown.Count is >= 4 and <= 5, burst.Line + (Matched(burst) ? "" : " :: " + burst.Times));
            }
        }

        // ================================================================ G16 the connection goes away mid-burst
        static void G16() {
            var pace = Paces[0];
            Hold(0, s_ak, 30); Begin(pace); Advance(s_clock + .2, _ => false);
            double goneAt = -1; int firedThen = -1;
            var burst = Fire(1.0, 1.0, server: t => {
                if (goneAt < 0 && Remote().Fired >= 4) { goneAt = t; firedThen = Remote().Fired; s_sessions.Remove(s_sessionA); s_upQueue.Clear(); }
                else if (goneAt >= 0) s_upQueue.Clear();                           // nothing of that client reaches the server any more
            });
            int after = burst.Fired.Count(f => f.T > goneAt + 1e-9);
            Enter(S.W, true);
            Test("G16", "the client's connection goes away while it fires: the server fires nothing more for that player, from the frame it sees the connection gone", goneAt >= 0 && after == 0 && 30 - burst.ServerEnd == burst.Fired.Count && ScNet.Peers.Count == 1,
                $"executed before {firedThen}, after {after}; server rounds {burst.ServerEnd}; peers {ScNet.Peers.Count}");
            // back in: the session is accepted again, a new burst corresponds
            s_sessions.Insert(0, s_sessionA);
            Enter(A.W, false); Call("SendHelloForTest", -1); ToServer(S.W); Deliver(Take(), A.W, s_sessionA);
            Hold(1, s_ak, 30); Begin(pace); Advance(s_clock + .2, _ => false);
            var again = Fire(.55);
            Test("G16", "accepted again, the next burst corresponds shot for shot", ScNet.Peers.Count == 2 && Matched(again) && again.Shown.Count >= 5, again.Line + (Matched(again) ? "" : " :: " + again.Times));
        }
    }
}
