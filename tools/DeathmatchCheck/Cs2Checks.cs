// deathmatch round 5 (2026-10-05): CS2's spray patterns, hitboxes and hit groups, running speeds. The measured CS2 numbers
// below were copied from the CS2 1.41.8.8 session of 2026-10-04 (docs/tasks/deathmatch-cs2-measure-20261005/); the
// reference table values and the sweep sequence were computed by an independent Python implementation of the same
// published algorithms (the session's rec/valve.py and rec/sweep_sim.py), never by the code under test.
using Engine;
using Game;

static partial class Program {
    // (gun, alternate, rounds fired, [(shot index, up deg, left deg)]) - each round's measured angle in CS2, in order; a
    // few consecutive rounds that landed on one mark were measured once and are matched to one shot only
    static readonly (string Gun, bool Alternate, int Rounds, (int Shot, float Up, float Left)[] Shots)[] s_cs2Sprays = [
        ("ak47", false, 30, [(0, 0.02f, -0.02f), (1, 0.25f, -0.10f), (2, 1.32f, -0.03f), (3, 2.95f, -0.17f), (4, 4.72f, -0.18f), (5, 6.38f, 0.46f), (6, 7.83f, 0.94f), (7, 8.93f, 1.66f), (8, 9.73f, 0.84f), (9, 9.55f, -1.51f), (10, 9.79f, -2.70f), (11, 10.30f, -2.09f), (12, 10.36f, -2.85f), (13, 10.00f, -4.16f), (14, 10.27f, -4.41f), (15, 10.49f, -2.44f), (16, 10.80f, -1.24f), (17, 11.16f, -0.45f), (18, 11.17f, 0.93f), (19, 10.70f, 2.72f), (20, 10.72f, 1.78f), (21, 10.85f, 2.00f), (22, 11.11f, 1.62f), (23, 11.40f, 1.15f), (24, 11.21f, 2.13f), (25, 11.29f, 2.54f), (26, 11.42f, 1.46f), (27, 11.22f, -0.33f), (28, 10.39f, -2.77f), (29, 10.31f, -3.63f)]), // python reference rms 0.034
        ("m4a4", false, 30, [(0, -0.03f, 0.00f), (1, 0.23f, 0.04f), (2, 0.76f, 0.08f), (3, 1.73f, -0.21f), (4, 2.96f, 0.09f), (5, 4.28f, -0.29f), (6, 5.52f, -0.55f), (7, 6.42f, 0.30f), (8, 7.20f, 0.89f), (9, 7.37f, 2.05f), (10, 7.83f, 1.85f), (11, 8.22f, 0.87f), (12, 8.13f, -0.68f), (13, 7.96f, -2.11f), (14, 7.60f, -3.46f), (15, 7.86f, -3.50f), (16, 8.20f, -3.05f), (17, 8.08f, -3.50f), (19, 7.98f, -4.26f), (20, 8.28f, -2.50f), (21, 8.44f, -1.77f), (22, 8.52f, -0.41f), (23, 8.73f, 0.14f), (24, 8.88f, 0.78f), (25, 8.98f, 0.21f), (26, 9.06f, 0.45f), (28, 9.23f, 0.75f), (29, 9.35f, 1.10f)]), // python reference rms 0.048
        ("m4a1s", true, 20, [(0, -0.02f, 0.03f), (1, 0.16f, 0.05f), (2, 0.42f, 0.04f), (3, 1.11f, -0.08f), (4, 2.10f, 0.09f), (5, 3.17f, -0.24f), (6, 4.14f, -0.38f), (7, 4.85f, 0.25f), (8, 5.43f, 0.70f), (9, 5.55f, 1.62f), (10, 5.89f, 1.28f), (11, 6.16f, 0.49f), (12, 6.10f, -0.76f), (13, 5.92f, -1.71f), (14, 5.65f, -2.65f), (15, 5.90f, -2.55f), (16, 6.22f, -2.11f), (17, 6.11f, -2.56f), (18, 5.91f, -3.10f), (19, 6.10f, -2.95f)]), // python reference rms 0.041
        ("galilar", false, 35, [(1, 0.05f, 0.05f), (2, 0.28f, 0.07f), (3, 0.96f, 0.47f), (4, 1.84f, 1.11f), (5, 3.05f, 1.07f), (6, 4.28f, 1.13f), (7, 5.22f, 1.51f), (8, 5.85f, 2.09f), (9, 6.47f, 1.83f), (10, 6.88f, 0.68f), (11, 6.74f, -0.99f), (12, 6.25f, -2.61f), (13, 6.45f, -3.09f), (14, 6.48f, -3.63f), (15, 6.58f, -4.04f), (16, 6.76f, -4.21f), (17, 7.10f, -3.86f), (18, 7.35f, -2.47f), (19, 7.60f, -1.73f), (20, 7.55f, -0.42f), (21, 6.98f, 1.32f), (22, 7.02f, 1.62f), (23, 7.33f, 1.01f), (24, 7.32f, 1.64f), (25, 7.39f, 2.24f), (26, 7.11f, 3.14f), (27, 7.24f, 2.58f), (28, 6.99f, 0.92f), (29, 6.97f, -0.48f), (30, 7.13f, -1.29f), (31, 7.41f, -0.98f), (32, 7.36f, -1.67f), (33, 6.76f, -2.98f), (34, 6.52f, -3.71f)]), // python reference rms 0.031
        ("famas", false, 25, [(1, 0.08f, -0.06f), (2, 0.29f, -0.12f), (3, 0.92f, -0.52f), (4, 1.92f, -0.58f), (5, 3.09f, -0.51f), (6, 3.96f, 0.19f), (7, 4.68f, 1.09f), (8, 5.41f, 0.76f), (9, 5.75f, -0.29f), (10, 5.97f, -1.17f), (11, 6.10f, -1.89f), (12, 6.47f, -1.71f), (13, 6.61f, -0.41f), (14, 6.82f, 0.18f), (15, 6.72f, 1.23f), (16, 6.89f, 1.58f), (17, 6.79f, 2.39f), (18, 6.96f, 2.54f), (19, 7.20f, 2.31f), (20, 7.25f, 1.02f), (21, 7.25f, 0.82f), (22, 7.12f, 1.36f), (23, 6.90f, 2.18f), (24, 6.41f, 3.09f)]), // python reference rms 0.037
        ("mp9", false, 30, [(1, 0.08f, -0.02f), (2, 0.61f, -0.21f), (3, 1.40f, 0.02f), (4, 2.71f, -0.16f), (5, 4.00f, 0.47f), (6, 5.39f, 0.48f), (7, 6.67f, 0.07f), (8, 7.59f, 0.55f), (9, 8.18f, 1.77f), (10, 8.18f, 3.27f), (11, 7.82f, 4.77f), (12, 8.26f, 4.86f), (13, 8.87f, 4.89f), (14, 9.28f, 3.52f), (15, 9.85f, 2.70f), (16, 10.29f, 1.54f), (17, 10.62f, 0.74f), (18, 10.58f, -0.64f), (20, 10.06f, -2.32f), (21, 10.27f, -1.39f), (22, 10.34f, -0.02f), (23, 10.48f, 0.23f), (24, 10.39f, -0.83f), (25, 10.02f, -2.34f), (26, 10.09f, -2.84f), (27, 10.19f, -3.44f), (28, 10.44f, -2.96f), (29, 10.76f, -2.56f)]), // python reference rms 0.053
        ("ump45", false, 25, [(0, -0.02f, -0.06f), (1, 0.21f, -0.04f), (2, 0.71f, -0.18f), (3, 1.77f, -0.31f), (4, 3.02f, -0.63f), (5, 4.27f, -1.13f), (6, 5.53f, -1.19f), (7, 6.53f, -0.64f), (8, 7.24f, -0.81f), (9, 7.85f, -0.36f), (10, 8.18f, 0.62f), (11, 8.46f, 1.36f), (12, 8.77f, 1.40f), (14, 9.12f, 1.63f), (15, 9.11f, 2.10f), (16, 9.13f, 2.50f), (17, 9.24f, 1.73f), (18, 9.27f, 0.75f), (20, 9.06f, 1.45f), (21, 8.77f, 2.34f), (22, 8.84f, 2.14f), (23, 8.87f, 1.04f), (24, 8.94f, 0.73f)]), // python reference rms 0.039
        ("p90", false, 50, [(2, 0.10f, -0.07f), (3, 0.58f, -0.08f), (4, 1.28f, -0.21f), (5, 2.04f, -0.75f), (6, 2.73f, -1.45f), (7, 3.67f, -1.98f), (8, 4.52f, -1.56f), (9, 5.22f, -1.04f), (10, 5.83f, -0.88f), (11, 6.48f, -0.66f), (12, 6.89f, -0.87f), (13, 6.90f, -1.50f), (14, 7.03f, -1.83f), (15, 7.22f, -2.23f), (16, 7.21f, -2.80f), (17, 7.28f, -2.18f), (18, 7.38f, -1.24f), (19, 7.39f, -0.73f), (20, 7.42f, -0.14f), (21, 7.43f, 0.57f), (22, 7.23f, 1.38f), (25, 7.32f, 0.87f), (26, 7.28f, 0.01f), (28, 7.01f, -1.21f), (29, 7.20f, -0.92f), (30, 7.34f, -1.09f), (31, 7.46f, -1.40f), (32, 7.65f, -1.14f), (33, 7.61f, -0.28f), (35, 7.66f, 0.04f), (37, 7.60f, -0.76f), (38, 7.57f, -1.36f), (40, 7.69f, -0.75f), (41, 7.44f, 0.38f), (42, 7.35f, 0.92f), (43, 7.33f, 1.56f), (44, 7.22f, 2.34f), (45, 7.19f, 2.82f), (46, 7.08f, 3.38f), (48, 7.35f, 2.70f), (49, 7.18f, 2.84f)]), // python reference rms 0.048
        ("mac10", false, 30, [(1, 0.04f, -0.07f), (2, 0.22f, -0.19f), (3, 0.56f, -0.12f), (4, 1.38f, 0.07f), (5, 2.37f, 0.66f), (6, 3.52f, 1.18f), (7, 4.67f, 1.67f), (8, 5.69f, 1.42f), (9, 6.31f, 1.90f), (10, 6.82f, 2.09f), (11, 7.27f, 2.29f), (12, 7.68f, 2.17f), (13, 8.01f, 2.11f), (14, 8.26f, 1.64f), (15, 8.15f, 0.36f), (16, 7.72f, -1.05f), (17, 7.71f, -1.27f), (18, 7.61f, -1.78f), (19, 7.68f, -1.54f), (20, 7.67f, -1.86f), (21, 7.48f, -2.50f), (22, 7.48f, -3.01f), (23, 7.57f, -2.21f), (24, 7.73f, -1.46f), (25, 7.86f, -0.68f), (27, 7.73f, 0.66f), (28, 7.55f, -0.37f)]), // python reference rms 0.037
    ];

    static void Cs2Round5() {
        // ---------------------------------------------------------------- the pattern table (Valve's stream, the vdata)
        var ak = DmRecoilModel.TableOf("ak47", false);
        bool Near((float A, float M) e, float a, float m) => MathF.Abs(e.A - a) < 2e-3f && MathF.Abs(e.M - m) < 2e-3f;
        // python reference: ak47 [(27.3303, 22.5), (-17.8304, 21.6328), (5.8008, 22.9554), (1.6354, 25.1531)] #63 (20.377, 30.0)
        Test("K01", "AK-47 pattern table from seed 223: Valve's stream, 0.55 blend, first four shots suppressed from 0.75",
            Near(ak[0], 27.3303f, 22.5f) && Near(ak[1], -17.8304f, 21.6328f) && Near(ak[2], 5.8008f, 22.9554f) && Near(ak[3], 1.6354f, 25.1531f) && Near(ak[63], 20.377f, 30f),
            string.Join(" ", ak.Take(4).Select(e => $"({e.Angle:0.####},{e.Magnitude:0.####})")) + $" #63 ({ak[63].Angle:0.####},{ak[63].Magnitude:0.####})");
        // python reference: glock18 [(3.6468, 18.0), (-11.5065, 18.0), (-19.1463, 18.0), (8.8945, 18.0)]; m4a1s alternate [(-11.4441, 15.75), (-0.523, 15.143), ...]
        var glock = DmRecoilModel.TableOf("glock18", false); var m4s = DmRecoilModel.TableOf("m4a1s", true);
        Test("K02", "a semi-automatic table is neither blended nor suppressed (Glock-18); the alternate pair has its own (M4A1-S silenced)",
            Near(glock[0], 3.6468f, 18f) && Near(glock[1], -11.5065f, 18f) && Near(glock[3], 8.8945f, 18f) && Near(m4s[0], -11.4441f, 15.75f) && Near(m4s[1], -.523f, 15.143f),
            $"glock ({glock[0].Angle:0.####},{glock[0].Magnitude}) m4a1s ({m4s[0].Angle:0.####},{m4s[0].Magnitude:0.####})");

        // ---------------------------------------------------------------- the patterns against CS2
        var worst = new List<string>(); bool all = true;
        foreach (var (gun, alternate, rounds, shots) in s_cs2Sprays) {
            var state = new DmRecoilState(); float cycle = DmWeapons.Raw(gun, "m_flCycleTime");
            var predicted = new (float Up, float Left)[rounds];
            for (int k = 0; k < rounds; k++) {
                double t = 10 + k * (double)cycle; state.Advance(t);
                var (bullet, _) = state.Angles(); predicted[k] = (bullet.X, bullet.Y);
                state.Fire(gun, alternate, t);
            }
            double sum = 0; float max = 0;
            foreach (var (shot, up, left) in shots) {
                float du = predicted[shot].Up - up, dl = predicted[shot].Left - left;
                sum += du * du + dl * dl; max = Math.Max(max, Math.Max(MathF.Abs(du), MathF.Abs(dl)));
            }
            double rms = Math.Sqrt(sum / (2 * shots.Length));
            bool ok = rms < .1 && max < .3; all &= ok;
            worst.Add($"{gun}{(alternate ? " (alt)" : "")} {shots.Length}/{rounds} rms {rms:0.000} max {max:0.000}");
        }
        Test("K03", "every measured CS2 spray (AK-47, M4A4, M4A1-S silenced, Galil AR, FAMAS, MP9, UMP-45, P90, MAC-10) is reproduced within 0.1 deg RMS, 0.3 deg per round",
            all, string.Join("; ", worst));
        {
            var state = new DmRecoilState();
            for (int k = 0; k < 10; k++) state.Fire("ak47", false, 10 + k * .1);
            float held = state.Index; state.Advance(10.9 + .1); float atCycle = state.Index;
            state.Advance(10.9 + 1); float rested = state.Index;
            var (bullet, view) = state.Angles();
            Test("K04", "a held trigger keeps the index; a second's rest returns the pattern to its first shot (CS:GO SDK decay, not measured)",
                held == 10 && atCycle == 10 && rested < 1, $"held {held} next cycle {atCycle} after 1 s {rested:0.###}");
            state.Fire("ak47", false, 12); state.Advance(12.05); (bullet, view) = state.Angles();
            Test("K05", "the view shows CS2's measured share of the round's offset (pitch 0.602, yaw 0.558)",
                bullet.X > 0 && MathF.Abs(view.X - .602f * bullet.X) < 1e-5f && MathF.Abs(view.Y - .558f * bullet.Y) < 1e-5f, $"bullet {bullet} view {view}");
            var other = new DmRecoilState(); other.Fire("ak47", false, 0); other.Fire("ak47", false, .1); other.Fire("m4a4", false, .2);
            Test("K06", "another gun starts its own pattern at its first shot", other.Gun == "m4a4" && other.Index == 1, $"{other.Gun} {other.Index}");
        }
        {
            Vector3 d = ScGunHandling.Turned(new Vector3(0, 0, -1), 10, 0), l = ScGunHandling.Turned(new Vector3(0, 0, -1), 0, 90);
            Test("K07", "a round turns up and left as view angles add (forward -Z: up +Y, left -X)",
                MathF.Abs(d.Y - MathF.Sin(MathUtils.DegToRad(10))) < 1e-5f && MathF.Abs(d.Z + MathF.Cos(MathUtils.DegToRad(10))) < 1e-5f && MathF.Abs(l.X + 1) < 1e-5f, $"{d} {l}");
        }

        // ---------------------------------------------------------------- the capsules
        Test("K10", "CS2's hitbox table loaded: 19 capsules - 1 head, 1 neck, 3 chest, 2 stomach, 6 arm, 6 leg",
            DmHitboxes.Ready && DmHitboxes.Standing.Count == 19 && DmHitboxes.Standing.Count(c => c.Part == ScHitPart.Head) == 1 && DmHitboxes.Standing.Count(c => c.Part == ScHitPart.Neck) == 1
            && DmHitboxes.Standing.Count(c => c.Part == ScHitPart.Body) == 3 && DmHitboxes.Standing.Count(c => c.Part == ScHitPart.Stomach) == 2
            && DmHitboxes.Standing.Count(c => c.Part == ScHitPart.Arm) == 6 && DmHitboxes.Standing.Count(c => c.Part == ScHitPart.Leg) == 6,
            DmHitboxes.LoadError ?? string.Join(",", DmHitboxes.Standing.GroupBy(c => c.Part).Select(g => $"{g.Key}{g.Count()}")));
        {
            var cap = new ScHitCapsule(Vector3.Zero, new Vector3(0, 10, 0), 1, ScHitPart.Body);
            float? through = cap.Intersect(new Vector3(5, 5, 0), new Vector3(-1, 0, 0)), over = cap.Intersect(new Vector3(5, 12, 0), new Vector3(-1, 0, 0));
            float? end = cap.Intersect(new Vector3(5, 10.5f, 0), new Vector3(-1, 0, 0)), inside = cap.Intersect(new Vector3(0, 3, .5f), new Vector3(1, 0, 0)), behind = cap.Intersect(new Vector3(5, 5, 0), new Vector3(1, 0, 0));
            Test("K11", "capsule entry: side 4, end cap 5-sqrt(0.75), above it nothing, from inside 0, pointing away nothing",
                through is float a && MathF.Abs(a - 4) < 1e-4f && over is null && end is float b && MathF.Abs(b - (5 - MathF.Sqrt(.75f))) < 1e-4f && inside == 0 && behind is null,
                $"{through} {over} {end} {inside} {behind}");
        }
        // CS2's sweep (2026-10-04, aim_map): a bot facing the shooter at 174 units, the shooter's eye 60.71 units above its feet, single
        // nospread AK-47 rounds stepped down; the server's log named each hit group (null: no hit). Rows above the neck missed in CS2
        // because the bot's aiming pose turned its head off the centre line - the capsules keep the head centred (not compared).
        (float Pitch, ScHitPart? Cs2)[] sweep = [(-3, null), (-2, null), (-1.5f, null), (-1, null), (-.5f, null), (0, ScHitPart.Neck), (.25f, ScHitPart.Neck),
            (.5f, ScHitPart.Body), (.75f, ScHitPart.Body), (1, ScHitPart.Body), (1.25f, ScHitPart.Body), (1.5f, ScHitPart.Body), (1.75f, ScHitPart.Body), (2, ScHitPart.Body),
            (2.25f, ScHitPart.Body), (2.5f, ScHitPart.Body), (3, ScHitPart.Body), (3.5f, ScHitPart.Body), (4, ScHitPart.Body), (5, ScHitPart.Body), (6, ScHitPart.Stomach),
            (7, ScHitPart.Stomach), (8, null), (9, null), (10, null), (11, null), (12, null), (14, null), (16, null), (18, null), (20, null)];
        // python reference over the same capsules: head x5, neck, chest x14, stomach x3, nothing x8
        ScHitPart?[] reference = [ScHitPart.Head, ScHitPart.Head, ScHitPart.Head, ScHitPart.Head, ScHitPart.Head, ScHitPart.Neck, .. Enumerable.Repeat<ScHitPart?>(ScHitPart.Body, 14),
            ScHitPart.Stomach, ScHitPart.Stomach, ScHitPart.Stomach, .. Enumerable.Repeat<ScHitPart?>(null, 8)];
        // in CS2 units: a body whose standing eye is CS2's (height 64.0626 / 0.875) puts every capsule at its table place
        float height = DmHitboxes.EyeStanding / DmHitboxes.StandingEyeShare;
        var placed = DmHitboxes.Place(Vector3.Zero, new Vector3(1, 0, 0), height, DmHitboxes.EyeStanding);
        var got = sweep.Select(row => {
            float p = MathUtils.DegToRad(row.Pitch); var hit = ScHitCapsule.Resolve(placed, new Vector3(174, 60.71f, 0), new Vector3(-MathF.Cos(p), -MathF.Sin(p), 0), 400);
            return hit.Part == ScHitPart.Unknown ? (ScHitPart?)null : hit.Part;
        }).ToArray();
        int body = 0, agree = 0;
        for (int i = 0; i < sweep.Length; i++) if (sweep[i].Pitch >= 0) { body++; if (got[i] == sweep[i].Cs2) agree++; }
        Test("K12", "the capsules repeat the python reference sweep exactly", got.SequenceEqual(reference), string.Join(",", got.Select(g => g?.ToString() ?? "-")));
        Test("K13", "against CS2's own sweep: at least 24 of the 26 body rows give CS2's hit group (neck, chest, stomach, the gap between the legs)",
            agree >= 24, $"{agree}/{body}: " + string.Join(" ", sweep.Select((r, i) => $"{r.Pitch}:{got[i]?.ToString() ?? "-"}{(r.Pitch >= 0 && got[i] != r.Cs2 ? "!" : "")}")));
        {
            // a crouched body (this game: eye at 0.45 of standing) still has its head where its eye is; lying down has none
            var crouched = DmHitboxes.Place(Vector3.Zero, new Vector3(1, 0, 0), height, .45f * DmHitboxes.EyeStanding);
            var head = ScHitCapsule.Resolve(crouched, new Vector3(100, 67.5348f * .45f, 0), new Vector3(-1, 0, 0), 400);
            var standingThere = ScHitCapsule.Resolve(placed, new Vector3(100, 67.5348f * .45f, 0), new Vector3(-1, 0, 0), 400);
            Test("K14", "crouched: the head at 45% of its standing height (a standing body has no head there); lying down: no capsules",
                head.Part == ScHitPart.Head && standingThere.Part != ScHitPart.Head && DmHitboxes.Place(Vector3.Zero, new Vector3(1, 0, 0), height, .2f * height) is null,
                $"crouched {head.Part}, standing {standingThere.Part}");
            // the body's yaw: facing +Z instead, a ray along -Z at the same place meets the same head
            var turned = DmHitboxes.Place(Vector3.Zero, new Vector3(0, 0, 1), height, DmHitboxes.EyeStanding);
            var front = ScHitCapsule.Resolve(turned, new Vector3(0, 67.5f, 100), new Vector3(0, 0, -1), 400);
            Test("K15", "the capsules turn with the body", front.Part == ScHitPart.Head, front.Part.ToString());
        }

        // ---------------------------------------------------------------- hit groups and armour (CS2 1.41.8.8, 2026-10-04)
        {
            float far = 36 * MathF.Pow(.98f, 1012f / 500);   // AK-47 m_nDamage 36, m_flRangeModifier 0.98, 1012 units: 34.556
            var chest = DmCombat.Settle(100, 100, far * DmCombat.RegionMultiplier(ScHitPart.Body, 4), 1.55f, DmCombat.Covered(ScHitPart.Body, false));
            var leg = DmCombat.Settle(100, 100, far * DmCombat.RegionMultiplier(ScHitPart.Leg, 4), 1.55f, DmCombat.Covered(ScHitPart.Leg, false));
            int head = (int)(far * DmCombat.RegionMultiplier(ScHitPart.Head, 4));
            Test("K20", "CS2 at 1012 units with armour, no helmet: chest 26 health + 3 armour, leg 25 and no armour, head 138",
                chest.HealthLost == 26 && chest.ArmourLost == 3 && leg.HealthLost == 25 && leg.ArmourLost == 0 && head == 138, $"chest {chest} leg {leg} head {head}");
            float near = 36 * MathF.Pow(.98f, 174f / 500);    // 174 units: 35.75
            var stomach = DmCombat.Settle(100, 0, near * DmCombat.RegionMultiplier(ScHitPart.Stomach, 4), 1.55f, true);
            var neck = DmCombat.Settle(100, 0, near * DmCombat.RegionMultiplier(ScHitPart.Neck, 4), 1.55f, true);
            var torso = DmCombat.Settle(100, 0, near * DmCombat.RegionMultiplier(ScHitPart.Body, 4), 1.55f, true);
            Test("K21", "CS2 at 174 units without armour: stomach 44 (x1.25), neck 35 and chest 35 (x1)",
                stomach.HealthLost == 44 && neck.HealthLost == 35 && torso.HealthLost == 35, $"stomach {stomach.HealthLost} neck {neck.HealthLost} chest {torso.HealthLost}");
            var hits = new ScShotHits(); hits.Add(ScHitPart.Neck, 10, Vector3.Zero, Vector3.UnitX); hits.Add(ScHitPart.Stomach, 20, Vector3.Zero, Vector3.UnitX); hits.Add(ScHitPart.Body, 5, Vector3.Zero, Vector3.UnitX);
            Test("K22", "a shot's neck and stomach power are kept apart from the chest", hits.Neck == 10 && hits.Stomach == 20 && hits.Body == 5 && hits.Total == 35
                && hits.Regions().Count() == 3, hits.ToString());
            Test("K23", "the vest covers neck and stomach in the core's protection too", ScArmorRules.Covers(ScArmorRules.Vest, ScHitPart.Neck) && ScArmorRules.Covers(ScArmorRules.Vest, ScHitPart.Stomach) && !ScArmorRules.Covers(ScArmorRules.Helmet, ScHitPart.Neck));
        }

        // ---------------------------------------------------------------- running speed (the user: knife = this game's walk, others by CS2's ratio)
        float Speed(float units) => 3.1f * units / DmMovement.ReferenceUnits;
        Test("K30", "knife 250 (3.1 m/s), AK-47 215 (2.666), AWP 200 / scoped 100 (1.24), Negev 150, Zeus 230, grenades 245",
            DmWeapons.RawEquipment("weapon_knife", "m_flMaxSpeed") == 250 && DmMovement.UnitsForGun("ak47", false) == 215 && DmMovement.UnitsForGun("awp", false) == 200
            && DmMovement.UnitsForGun("awp", true) == 100 && DmMovement.UnitsForGun("negev", false) == 150 && DmMovement.UnitsForGun("taser", false) == 230
            && DmWeapons.RawEquipment("weapon_hegrenade", "m_flMaxSpeed") == 245 && MathF.Abs(Speed(215) - 2.666f) < 1e-3f && MathF.Abs(Speed(100) - 1.24f) < 1e-3f,
            $"ak {Speed(DmMovement.UnitsForGun("ak47", false)):0.###} awp scoped {Speed(DmMovement.UnitsForGun("awp", true)):0.###}");
        Test("K31", "every gun of the profile has a running speed in CS2's range", GunSpec.All.All(g => DmMovement.UnitsForGun(g.Name, false) is >= 100 and <= 250),
            string.Join(" ", GunSpec.All.Where(g => DmMovement.UnitsForGun(g.Name, false) is < 100 or > 250).Select(g => g.Name)));
        Test("K32", "both ends compare the round-5 rules: the fingerprint carries the recoil model and the hitbox table", DmWeapons.Ready && DmHitboxes.Fingerprint != "none",
            $"{DmWeapons.Fingerprint} hit {DmHitboxes.Fingerprint}");
        Cs2Round6();
    }

    // round 6 (2026-10-05): CS2's penetration. Measured in CS2 1.41.8.8 on 2026-10-04 (aim_map, an AK-47 through the edge of a
    // Wood_Crate about 150 units away, sv_showimpacts_penetration): thickness (cm) -> damage lost, read off the overlay; the
    // second reading's digits overlapped (8.5 or 8.0)
    static void Cs2Round6() {
        float ak = 36 * MathF.Pow(.98f, 150f / 500);   // 35.78
        (float Cm, float Lost, float Tolerance)[] crate = [(5.5f, 7.58f, .3f), (13.09f, 8.5f, .6f), (28.03f, 9.53f, .4f), (42.02f, 11.4f, .15f)];
        var got = crate.Select(c => DmPenetrationRules.Cs2Loss(2, "Wood_Crate", "Wood_Crate", c.Cm / 2.54f, ak)).ToArray();
        Test("K40", "CS2's penetration formula repeats CS2's measured losses through a wooden crate (AK-47: 5.5 / 13.1 / 28.0 / 42.0 cm)",
            DmPenetrationRules.Ready && got.Zip(crate).All(p => p.First is float l && MathF.Abs(l - p.Second.Lost) <= p.Second.Tolerance),
            DmPenetrationRules.LoadError ?? string.Join(" ", got.Zip(crate).Select(p => $"{p.Second.Cm}cm {p.First:0.00}/{p.Second.Lost}")));
        // the user: wood easy (a crossing entered and left at wood counts a tenth of its thickness); the rest by real thickness
        float? woodBlock = DmPenetrationRules.Loss("ak47", "Wood", "Wood", 1f, ak), glockWood = DmPenetrationRules.Loss("glock18", "Wood", "Wood", 1f, 30);
        float? stone = DmPenetrationRules.Loss("ak47", "concrete", "concrete", 1f, ak), stoneSlab = DmPenetrationRules.Loss("ak47", "concrete", "concrete", .5f, ak);
        float? glass = DmPenetrationRules.Loss("ak47", "glass", "glass", 1f, ak), ironDoor = DmPenetrationRules.Loss("ak47", "metal", "metal", .1875f, ak);
        float? ironBlock = DmPenetrationRules.Loss("ak47", "solidmetal", "solidmetal", 1f, ak), earth = DmPenetrationRules.Loss("awp", "dirt", "dirt", 1f, 115);
        // by hand: wood 35.78*.16 + 1.5*1.25*3*(1/3) + (1/3)*3.937^2/24 = 5.72 + 1.875 + 0.215 = 7.81; glass 35.78*.05 + 1.875 + (1/3)*39.37^2/24 = 1.79 + 1.875 + 21.53 = 25.19
        Test("K41", "wood crosses easily (a whole block: AK-47 loses 7.8, Glock-18 8.8), glass 25.2; a stone, iron or earth block and a stone slab stop the round; an iron door does not",
            woodBlock is float w && MathF.Abs(w - 7.81f) < .05f && glockWood is float gw && gw < 9 && glass is float gl && MathF.Abs(gl - 25.19f) < .05f
            && stone is null && stoneSlab is null && ironBlock is null && earth is null && ironDoor is float d && d < ak - 1,
            $"wood {woodBlock:0.00} glock {glockWood:0.00} glass {glass:0.00} stone {stone} slab {stoneSlab} iron door {ironDoor:0.00} iron block {ironBlock} earth (AWP) {earth}");
        float? body = DmPenetrationRules.Loss("ak47", "flesh", "flesh", .3f, ak);
        Test("K42", "a round goes on through a player (CS2's flesh: an AK-47 through 0.3 m keeps about half); the Zeus never crosses; nothing crosses more than 90 units",
            body is float b && b > 15 && b < 22 && DmPenetrationRules.Loss("taser", "Wood", "Wood", .05f, 500) is null && DmPenetrationRules.Cs2Loss(2.5f, "Wood", "Wood", 91, 115) is null && DmPenetrationRules.MaxCrossings == 4,
            $"flesh 0.3 m: {body:0.00}");
        (string Class, string Sound, string Want)[] map = [("PlanksBlock", "Wood", "Wood"), ("OakWoodBlock", "Wood", "Wood"), ("WoodenDoorBlock", "Wood", "Wood"), ("GraniteBlock", "Stone", "concrete"),
            ("BrickBlock", "Stone", "brick"), ("IceBlock", "Stone", "ice"), ("IronBlock", "Metal", "solidmetal"), ("IronDoorBlock", "Metal", "metal"), ("GlassBlock", "Glass", "glass"),
            ("WindowBlock", "Glass", "glass"), ("DirtBlock", "Dirt", "dirt"), ("SandBlock", "Sand", "sand"), ("GravelBlock", "Sand", "gravel"), ("SnowBlock", "Snow", "snow"),
            ("CarpetBlock", "Soft", "carpet"), ("ClayBlock", "Soft", "clay"), ("FurnitureBlock", "", "default"), ("SomeModBlock", null, "default")];
        var wrong = map.Where(m => DmPenetrationRules.SurfaceFor(m.Class, m.Sound) != m.Want).Select(m => $"{m.Class}->{DmPenetrationRules.SurfaceFor(m.Class, m.Sound)}").ToList();
        Test("K43", "every vanilla material family has its CS2 surface", wrong.Count == 0, string.Join(" ", wrong));
        var torso = new[] { new ScHitCapsule(new Vector3(0, -.5f, 0), new Vector3(0, .5f, 0), .15f, ScHitPart.Body) };
        float exit = ScBulletPenetration.ExitCapsules(torso, new Vector3(0, 0, 1), new Vector3(0, 0, -1), .85f);
        Test("K44", "a round leaves a body where its capsules end (0.3 m through a 0.15 m capsule)", MathF.Abs(exit - 1.15f) < .005f, $"{exit:0.0000}");
        Test("K45", "both ends compare the round-6 rules: the fingerprint carries CS2's surfaces and the wood rule", DmPenetrationRules.Fingerprint != "none" && DmWeapons.Ready,
            $"{DmWeapons.Fingerprint} pen {DmPenetrationRules.Fingerprint}");
    }
}
