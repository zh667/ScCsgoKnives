// deathmatch-addon offline checks. Usage: DeathmatchCheck <out.json>
// Every expected number in the combat and profile sections was computed or copied by hand (the comment beside it says
// from what); none is produced by the code under test.
using System.Text.Json;
using Engine;
using Game;

static partial class Program {
    sealed record Check(string Id, string Name, bool Ok, string Detail);
    static readonly List<Check> s_checks = [];
    static void Test(string id, string name, bool ok, string detail = "") {
        s_checks.Add(new(id, name, ok, detail));
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")} [{id}] {name}{(detail.Length > 0 ? " :: " + detail : "")}");
    }
    static void Section(string id, Action body) {
        try { body(); } catch (Exception e) { Test(id, "section completed", false, e.ToString()); }
    }
    static int Main(string[] args) {
        Section("A", Arena); Section("S", Spawns); Section("R", Armoury); Section("C", Combat); Section("W", Weapons); Section("M", Match); Section("V", View); Section("U", Ui); Section("K", Cs2Round5);
        Section("F2-SP", () => {
            ScNet.Attach(null);
            var arena = new SubsystemScDeathmatch();
            typeof(SubsystemScDeathmatch).GetProperty("Enabled").SetValue(arena, true);
            typeof(SubsystemScDeathmatch).GetProperty("Match").SetValue(arena, new DmMatch());
            string label = new string('中', 48 * 1024);
            Test("F2-SP", "network budget does not restrict standalone map data", arena.AddSpawn(new Vector3(1, 10, 1), 0, label) is null && arena.Arena.Spawns.Single().Label == label);
        });
        Section("R3", PublicationRecovery);
        int failed = s_checks.Count(c => !c.Ok);
        if (args.Length > 0) File.WriteAllText(args[0], JsonSerializer.Serialize(new { failed, total = s_checks.Count, profile = new { DmWeapons.Cs2Version, DmWeapons.Fingerprint, DmWeapons.LoadError },
            checks = s_checks, scope = "offline rules and standalone map editing; no loaded engine world, no network, nothing seen or heard" }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{s_checks.Count} checks, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- a small world
    sealed class Probe : IDmWorldProbe {
        public int Floor = 63; public (int, int, int, int) LoadedBox = (-64, -64, 64, 64);
        public readonly HashSet<Point3> Blocks = [], Fire = [], Water = [], Holes = [];
        public bool Loaded(Point3 c) => c.X >= LoadedBox.Item1 && c.Z >= LoadedBox.Item2 && c.X <= LoadedBox.Item3 && c.Z <= LoadedBox.Item4;
        public bool Solid(Point3 c) => Blocks.Contains(c) || c.Y <= Floor && !Holes.Contains(c);
        public bool Hazard(Point3 c) => Fire.Contains(c);
        public bool Fluid(Point3 c) => Water.Contains(c);
    }
    static DmArenaDefinition Box() => new DmArenaDefinition().WithRegion(new Point3(-20, 60, -20), new Point3(20, 80, 20)).WithLobby(new Vector3(30.5f, 64, 30.5f), 0);
    static Vector3 At(float x, float z) => new(x + .5f, 64, z + .5f);

    static void Arena() {
        var probe = new Probe();
        var empty = new DmArenaDefinition();
        Test("A01", "an arena without a region cannot start", !DmArenaRules.CanStart(DmArenaRules.Validate(empty, probe)) && DmArenaRules.Validate(empty, probe)[0].Code == DmArenaIssueCode.NoRegion);
        var arena = Box().AddSpawn(At(-10, -10), 0, "西北").AddSpawn(At(10, 10), 1.5f);
        var issues = DmArenaRules.Validate(arena, probe);
        Test("A02", "region, preparation place and two separate points: nothing blocks", DmArenaRules.CanStart(issues), string.Join("; ", issues.Select(i => i.Message)));
        var one = Box().AddSpawn(At(0, 0), 0);
        Test("A03", "one point is not enough for a match", DmArenaRules.Validate(one, probe).Any(i => i.Code == DmArenaIssueCode.TooFewSpawns && i.Blocks));
        probe.Blocks.Add(new Point3(10, 65, 10));
        issues = DmArenaRules.Validate(arena, probe);
        Test("A04", "a point with a block at head height is reported and not counted", issues.Any(i => i.Code == DmArenaIssueCode.SpawnBlocked && i.SpawnId == 2) && issues.Any(i => i.Code == DmArenaIssueCode.TooFewSpawns), string.Join("; ", issues.Select(i => i.Code)));
        probe.Blocks.Clear(); probe.Holes.Add(new Point3(10, 63, 10));
        Test("A05", "a point over a hole has no ground", DmArenaRules.Validate(arena, probe).Any(i => i.Code == DmArenaIssueCode.SpawnNoGround && i.SpawnId == 2));
        probe.Holes.Clear(); probe.Fire.Add(new Point3(10, 64, 10));
        Test("A06", "a point in fire is a hazard", DmArenaRules.Validate(arena, probe).Any(i => i.Code == DmArenaIssueCode.SpawnHazard && i.SpawnId == 2));
        probe.Fire.Clear(); probe.Water.Add(new Point3(10, 65, 10));
        Test("A07", "a point under water is a hazard", DmArenaRules.Validate(arena, probe).Any(i => i.Code == DmArenaIssueCode.SpawnHazard && i.SpawnId == 2));
        probe.Water.Clear(); probe.LoadedBox = (-64, -64, 5, 64);
        issues = DmArenaRules.Validate(arena, probe);
        Test("A08", "a point on terrain that is not loaded is never called valid", issues.Any(i => i.Code == DmArenaIssueCode.SpawnUnloaded && i.SpawnId == 2) && !DmArenaRules.CanStart(issues), string.Join("; ", issues.Select(i => i.Code)));
        probe.LoadedBox = (-64, -64, 64, 64);
        var outside = arena.AddSpawn(At(40, 0), 0);
        Test("A09", "a point outside the region is reported, the others still serve", DmArenaRules.Validate(outside, probe).Any(i => i.Code == DmArenaIssueCode.SpawnOutside && i.SpawnId == 3) && DmArenaRules.CanStart(DmArenaRules.Validate(outside, probe)));
        var twins = Box().AddSpawn(At(0, 0), 0).AddSpawn(new Vector3(.9f, 64, .9f), 0);
        Test("A10", "two points in the same place count once", DmArenaRules.Validate(twins, probe).Any(i => i.Code == DmArenaIssueCode.SpawnDuplicate) && !DmArenaRules.CanStart(DmArenaRules.Validate(twins, probe)));
        var close = Box().AddSpawn(At(0, 0), 0).AddSpawn(At(3, 0), 0);
        Test("A11", "points all in one spot: advice, not a refusal", DmArenaRules.Validate(close, probe).Any(i => i.Code == DmArenaIssueCode.SpawnsClustered && !i.Blocks) && DmArenaRules.CanStart(DmArenaRules.Validate(close, probe)));
        var huge = new DmArenaDefinition().WithRegion(new Point3(0, 0, 0), new Point3(300, 80, 20)).WithLobby(At(30, 30), 0);
        Test("A12", "a region edge over the limit blocks", DmArenaRules.Validate(huge, probe).Any(i => i.Code == DmArenaIssueCode.RegionTooLarge && i.Blocks));
        bool decoded = DmArenaDefinition.TryDecode(arena.Encode(), out var back);
        Test("A13", "the arena survives its own text", decoded && back.Revision == arena.Revision && back.Spawns.Count == 2 && back.Spawns[0].Label == "西北" && back.Spawns[1].Yaw == 1.5f && back.MinX == -20 && back.MaxY == 80 && back.HasLobby && back.NextSpawnId == 3);
        Test("A14", "text that is not an arena is refused, never guessed", !DmArenaDefinition.TryDecode("{\"Spawns\":[{\"Id\":0,\"X\":1}]}", out _) && !DmArenaDefinition.TryDecode("not json", out _) && !DmArenaDefinition.TryDecode("{\"Spawns\":null}", out _));
        var removed = arena.RemoveSpawn(1).AddSpawn(At(5, 5), 0);
        Test("A15", "a removed point's number is not given to a later point", removed.Spawns.Select(s => s.Id).SequenceEqual([2, 3]));
        Test("A16", "the box is inclusive of both corner cells and nothing beyond", arena.Contains(new Vector3(20.99f, 80.99f, -20f)) && !arena.Contains(new Vector3(21f, 70, 0)) && !arena.Contains(new Vector3(0, 59.99f, 0)));
        Test("A17", "rules: the match length stays within 1-60 minutes", !new DmRules { Minutes = 0 }.Valid && !new DmRules { Minutes = 61 }.Valid && new DmRules { Minutes = 99 }.Normalize().Minutes == 60 && new DmRules().Minutes == 10 && !new DmRules().Grenades);
    }

    static void Spawns() {
        var probe = new Probe();
        var arena = Box().AddSpawn(At(-15, -15), 0).AddSpawn(At(15, 15), 0).AddSpawn(At(0, 15), 0).AddSpawn(At(15, -15), 0);
        DmSpawnSelector.Context Ctx(Action<DmSpawnSelector.Context> with = null) { var c = new DmSpawnSelector.Context { Arena = arena, World = probe, Sees = (_, _) => false }; with?.Invoke(c); return c; }
        Test("S01", "an arena without points gives no point (never a made-up place)", DmSpawnSelector.Choose(new DmSpawnSelector.Context { Arena = Box(), World = probe }) is null);
        var enemyNear1 = new DmEnemy(At(-14, -14), At(-14, -14) + new Vector3(0, 1.6f, 0));
        var chosen = Enumerable.Range(0, 60).Select(i => DmSpawnSelector.Choose(Ctx(c => { c.Enemies = [enemyNear1]; c.Random = () => i / 60f; })).Id).Distinct().OrderBy(i => i).ToArray();
        Test("S02", "a point beside an enemy is not chosen while hidden far points exist", chosen.SequenceEqual([2, 3, 4]), string.Join(",", chosen));
        // every point is seen; the enemy stands at point 1: the farthest (point 2, the opposite corner) is the one
        var seen = DmSpawnSelector.Choose(Ctx(c => { c.Enemies = [enemyNear1]; c.Sees = (_, _) => true; }));
        Test("S03", "when every point is in sight the one farthest from the nearest enemy is taken", seen?.Id == 2, $"{seen?.Id}");
        var unseenOnly = DmSpawnSelector.Choose(Ctx(c => { c.Enemies = [enemyNear1]; c.Sees = (_, to) => Vector3.Distance(to, At(15, 15) + new Vector3(0, 1.6f, 0)) > 1; }));
        Test("S04", "an unseen point is preferred to seen ones", unseenOnly?.Id == 2, $"{unseenOnly?.Id}");
        var reserved = Enumerable.Range(0, 40).Select(i => DmSpawnSelector.Choose(Ctx(c => { c.Reserved = new HashSet<int> { 2, 3 }; c.Random = () => i / 40f; })).Id).Distinct().OrderBy(i => i).ToArray();
        Test("S05", "points promised to other pending lives are not handed out twice", reserved.SequenceEqual([1, 4]), string.Join(",", reserved));
        var occupied = Enumerable.Range(0, 40).Select(i => DmSpawnSelector.Choose(Ctx(c => { c.Occupied = [At(15, 15)]; c.Random = () => i / 40f; })).Id).Distinct().ToArray();
        Test("S06", "a point somebody stands on is not used", !occupied.Contains(2), string.Join(",", occupied));
        var danger = Enumerable.Range(0, 40).Select(i => DmSpawnSelector.Choose(Ctx(c => { c.Danger = p => p.X > 10; c.Random = () => i / 40f; })).Id).Distinct().OrderBy(i => i).ToArray();
        Test("S07", "a point in present danger (fire) is not used", danger.SequenceEqual([1, 3]), string.Join(",", danger));
        var recent = Enumerable.Range(0, 40).Select(i => DmSpawnSelector.Choose(Ctx(c => { c.Now = 100; c.LastUsed = new Dictionary<int, double> { [1] = 98, [2] = 99, [3] = 50 }; c.Random = () => i / 40f; })).Id).Distinct().OrderBy(i => i).ToArray();
        Test("S08", "points used a moment ago are passed over while others of the same grade exist", recent.SequenceEqual([3, 4]), string.Join(",", recent));
        var onlyRecent = DmSpawnSelector.Choose(Ctx(c => { c.Now = 100; c.LastUsed = new Dictionary<int, double> { [1] = 99, [2] = 99, [3] = 99, [4] = 99 }; }));
        Test("S09", "when every point was just used one is still given", onlyRecent is not null);
        var spread = Enumerable.Range(0, 200).Select(i => DmSpawnSelector.Choose(Ctx(c => c.Random = () => i / 200f)).Id).GroupBy(i => i).ToDictionary(g => g.Key, g => g.Count());
        Test("S10", "equal points are all used, none every time", spread.Count == 4 && spread.Values.All(n => n >= 30), string.Join(" ", spread.Select(p => $"{p.Key}:{p.Value}")));
        probe.Blocks.Add(new Point3(-15, 64, -15)); probe.Blocks.Add(new Point3(15, 65, 15)); probe.Fire.Add(new Point3(0, 64, 15)); probe.Holes.Add(new Point3(15, 63, -15));
        Test("S11", "every point blocked, burning or without ground: no point, the player waits", DmSpawnSelector.Choose(Ctx()) is null);
        probe.Blocks.Clear(); probe.Fire.Clear(); probe.Holes.Clear();
        var withOutside = arena.AddSpawn(At(50, 50), 0);
        bool never = Enumerable.Range(0, 100).All(i => DmSpawnSelector.Choose(new DmSpawnSelector.Context { Arena = withOutside, World = probe, Sees = (_, _) => false, Random = () => i / 100f }).Id != 5);
        Test("S12", "a point outside the arena is never chosen", never);
    }

    static void Armoury() {
        int next = 5000; var made = new List<(int Id, int Variant)>();
        int Allocate(int variant) { made.Add((next, variant)); return next++; }
        var armory = new DmArmory();
        bool first = armory.TryLease(3, "a", 1, 1, Allocate, out int id1, out bool reused1);
        Test("R01", "the first loan of a model makes one record", first && !reused1 && armory.Count == 1 && armory.VariantOf(id1) == 3 && made.Count == 1);
        armory.Return(id1);
        bool again = armory.TryLease(3, "b", 1, 1, Allocate, out int id2, out _);
        Test("R02", "a returned record is lent again: no new record, the same number, the same model", again && id2 == id1 && made.Count == 1 && armory.VariantOf(id2) == 3);
        bool second = armory.TryLease(3, "a", 1, 2, Allocate, out int id3, out _);
        Test("R03", "a second player of the model at the same time gets another record", second && id3 != id2 && armory.Count == 2);
        bool same = armory.TryLease(3, "a", 1, 3, Allocate, out int id4, out bool reused4);
        Test("R04", "the player who already holds the model keeps that record", same && reused4 && id4 == id3 && armory.Count == 2);
        int generationB = armory.LeaseOf(id2).Generation;
        armory.Return(id2); armory.TryLease(3, "c", 1, 1, Allocate, out int id5, out _);
        Test("R05", "a loan to the next player is a new generation: the former holder's is not current", id5 == id2 && !armory.Current(id2, "b", generationB) && armory.Current(id2, "c", armory.LeaseOf(id2).Generation) && armory.LeaseOf(id2).Generation == generationB + 1);
        Test("R06", "a refused allocation changes nothing", !armory.TryLease(7, "a", 1, 1, _ => -1, out _, out _) && armory.Count == 2 && armory.LeasedTo("a").Count() == 1);

        // the bound: every one of 16 players takes every model at once
        var full = new DmArmory(); int allocations = 0;
        bool all = true;
        for (int player = 0; player < DmFixed.MaxPlayers; player++) for (int variant = 0; variant < GunSpec.All.Length; variant++) all &= full.TryLease(variant, "p" + player, 1, 1, v => { allocations++; return 10000 + allocations; }, out _, out _);
        Test("R07", "16 players holding all 35 models: exactly 560 records, the stated bound", all && full.Count == DmArmory.Limit && DmArmory.Limit == 560 && allocations == 560, $"{full.Count}/{DmArmory.Limit}");
        Test("R08", "a seventeenth holder of a model is refused and nothing is made", !full.TryLease(0, "extra", 1, 1, v => { allocations++; return 20000; }, out _, out _) && allocations == 560 && full.Count == 560);

        // long run: lives come and go, models change, visitors differ - the pool follows the peak of simultaneous use
        var pool = new DmArmory(); int allocated = 0; var random = new System.Random(20261003);
        var holding = new Dictionary<string, List<int>>(); int peak = 0;
        for (int life = 0; life < 20000; life++) {
            string player = "visitor" + random.Next(400);                // 400 different players over time, at most 16 at once
            if (!holding.ContainsKey(player) && holding.Count >= DmFixed.MaxPlayers) { string leaving = holding.Keys.ElementAt(random.Next(holding.Count)); pool.ReturnAll(leaving); holding.Remove(leaving); }
            pool.ReturnAll(player);
            var ids = new List<int>();
            foreach (int variant in new[] { random.Next(GunSpec.All.Length), random.Next(GunSpec.All.Length) }.Distinct())
                if (pool.TryLease(variant, player, 1 + life / 500, life, v => 30000 + allocated++, out int id, out _)) ids.Add(id);
            holding[player] = ids; peak = Math.Max(peak, holding.Values.Sum(l => l.Count));
        }
        Test("R09", "20000 lives of 400 visitors: the pool never exceeds the bound and stays far below the number of lives", pool.Count <= DmArmory.Limit && pool.Count == allocated && pool.Count < 600, $"records {pool.Count}, allocations {allocated}, peak in use {peak}");
        var perModel = Enumerable.Range(0, GunSpec.All.Length).Max(pool.CountOf);
        Test("R10", "no model ever has more records than players", perModel <= DmFixed.MaxPlayers, $"{perModel}");
        Test("R11", "every record still has the model it was made with", pool.Ids.All(id => pool.VariantOf(id) >= 0) && pool.Ids.Distinct().Count() == pool.Count);

        string text = pool.Encode(); var decodedPool = DmArmory.Decode(text);
        Test("R12", "the ledger survives its own text: records, models, loans, generations", decodedPool.Unreadable is null && decodedPool.Count == pool.Count && decodedPool.Ids.All(id => decodedPool.VariantOf(id) == pool.VariantOf(id) && decodedPool.LeaseOf(id)?.PlayerKey == pool.LeaseOf(id)?.PlayerKey
            && (pool.LeaseOf(id) is null || decodedPool.LeaseOf(id).Generation == pool.LeaseOf(id).Generation)) && decodedPool.Encode() == text);
        decodedPool.ReturnEverything();
        Test("R13", "a loaded world's loans are all parked again (no life survives a load)", decodedPool.Parked.Count() == decodedPool.Count);
        var garbage = DmArmory.Decode("1|5000:999:0||");
        Test("R14", "a ledger that cannot be read is kept as it was and holds nothing", garbage.Unreadable == "1|5000:999:0||" && garbage.Count == 0 && garbage.Encode() == "1|5000:999:0||");
        var future = DmArmory.Decode("2|whatever");
        Test("R15", "a ledger of a later layout is kept as it was", future.Unreadable == "2|whatever" && future.Encode() == "2|whatever");
        var q = new DmArmory(); q.TryLease(1, "a", 1, 1, _ => 7000, out int qid, out _); q.Quarantine(qid);
        Test("R16", "a record taken out of the pool is neither lent nor forgotten", !q.Owns(qid) && q.Count == 0 && q.Quarantined.Count == 1 && DmArmory.Decode(q.Encode()).Quarantined.SequenceEqual([(7000, 1)]));
        var keyed = new DmArmory(); keyed.TryLease(1, "local:0", 1, 1, _ => 7100, out _, out _); keyed.TryLease(2, "a|b,c:d", 1, 1, _ => 7101, out _, out _);
        var keyedBack = DmArmory.Decode(keyed.Encode());
        Test("R17", "player keys with separators survive the text", keyedBack.Unreadable is null && keyedBack.LeasedTo("a|b,c:d").SequenceEqual([7101]) && keyedBack.LeasedTo("local:0").SequenceEqual([7100]));
    }

    static void Combat() {
        // Hand-computed with Counter-Strike's published armour arithmetic: through armour, damage * (ratio / 2) reaches
        // health; half of the rest wears the armour; health and armour lose whole points (truncated).
        var ak = DmCombat.Settle(100, 100, 36, 1.55f, true);                       // 36 * .775 = 27.9 -> 27 ; (36 - 27.9) * .5 = 4.05 -> 4
        Test("C01", "AK-47 body shot on 100/100: 27 health, 4 armour", ak is { Health: 73, Armour: 96, HealthLost: 27, ArmourLost: 4, Lethal: false }, ak.ToString());
        var akHead = DmCombat.Settle(100, 100, 36 * 4, 1.55f, true);                // 144 * .775 = 111.6 -> lethal
        Test("C02", "AK-47 head shot through a helmet kills", akHead is { Health: 0, Lethal: true }, akHead.ToString());
        var m4Head = DmCombat.Settle(100, 100, 38 * 3.475f, 1.4f, true);            // 132.05 * .7 = 92.435 -> 92 ; (132.05 - 92.435) * .5 = 19.8 -> 19
        Test("C03", "M4A1-S head shot through a helmet leaves 8 health, wears 19 armour", m4Head is { Health: 8, Armour: 81, Lethal: false }, m4Head.ToString());
        var leg = DmCombat.SettleRegions(100, 100, true, [(ScHitPart.Leg, 36f)], 4, 1.55f, out _);   // legs: * .75, no armour -> 27
        Test("C04", "a leg shot: three quarters of the damage, armour neither protects nor wears", leg is { Health: 73, Armour: 100 }, leg.ToString());
        var thin = DmCombat.Settle(100, 2, 100, 1.0f, true);                        // worn would be 25 > 2: armour takes 2 * 2 = 4, 96 reach health
        Test("C05", "armour that runs out lets the remainder through", thin is { Health: 4, Armour: 0, ArmourLost: 2 }, thin.ToString());
        var bare = DmCombat.Settle(100, 0, 36, 1.55f, true);
        Test("C06", "no armour: the whole damage", bare is { Health: 64, Armour: 0 }, bare.ToString());
        var glock = DmCombat.Settle(100, 100, 30, .94f, true);                      // 30 * .47 = 14.1 -> 14 ; 15.9 * .5 = 7.95 -> 7
        Test("C07", "Glock body shot on 100/100: 14 health, 7 armour", glock is { Health: 86, Armour: 93 }, glock.ToString());
        var awp = DmCombat.Settle(100, 100, 115, 1.95f, true);                      // 115 * .975 = 112.1 -> lethal
        Test("C08", "AWP body shot on 100/100 kills", awp.Lethal, awp.ToString());
        var noHelmet = DmCombat.SettleRegions(100, 100, false, [(ScHitPart.Head, 30f)], 1, .94f, out var part);   // already multiplied by the caller: 30, no helmet -> 30
        Test("C09", "without a helmet the head takes the damage unreduced", noHelmet is { Health: 70, Armour: 100 }, noHelmet.ToString());
        Test("C10", "nothing, a negative or a NaN damage changes nothing", DmCombat.Settle(50, 50, 0, 1, true) is { Health: 50, Armour: 50, Lethal: false } && DmCombat.Settle(50, 50, -5, 1, true).Health == 50 && DmCombat.Settle(50, 50, float.NaN, 1, true).Health == 50);
        Test("C11", "a life that is over is not settled again", DmCombat.Settle(0, 50, 30, 1, true) is { Health: 0, HealthLost: 0, Lethal: false });
        // a shotgun's pellets on two regions of a bare target: head 20 first (100 -> 80), then body 90 kills: the body ended it
        var mixed = DmCombat.SettleRegions(100, 0, false, [(ScHitPart.Body, 90f), (ScHitPart.Head, 20f)], 1, 1, out var lethalPart);
        Test("C12", "pellets on head and body: the head's share is settled first and the region that ends the life is the body", mixed.Lethal && lethalPart == ScHitPart.Body);
        var headKills = DmCombat.SettleRegions(30, 0, false, [(ScHitPart.Body, 90f), (ScHitPart.Head, 40f)], 1, 1, out lethalPart);
        Test("C13", "when the head's share alone ends the life it is a head kill", headKills.Lethal && lethalPart == ScHitPart.Head);
        // AK at 500 units (12.7 blocks at 39.37 units a metre): 36 * 0.98 = 35.28 -> * .775 = 27.342 -> 27
        float far = DmCombat.AtDistance(36, .98f, 500f / 39.37f, 39.37f, 500);
        Test("C14", "distance: the range modifier applies once per 500 units", MathF.Abs(far - 35.28f) < .01f, $"{far}");
        Test("C15", "distance: no modifier, no loss; nothing is gained at point blank", DmCombat.AtDistance(36, 1, 50, 39.37f, 500) == 36 && DmCombat.AtDistance(36, .98f, 0, 39.37f, 500) == 36 && DmCombat.AtDistance(36, .98f, -3, 39.37f, 500) == 36);
        int health = 100, armour = 100, shots = 0;                               // AK body shots until dead: 27,27,27 -> 19 left, the fourth kills
        while (health > 0 && shots < 10) { var s = DmCombat.Settle(health, armour, 36, 1.55f, true); health = s.Health; armour = s.Armour; shots++; }
        Test("C16", "four AK body shots end a 100/100 life", shots == 4 && armour == 84, $"shots {shots}, armour {armour}");
        Test("C17", "the starting figures are 100 health and 100 armour, protection 3 seconds", DmFixed.Health == 100 && DmFixed.Armour == 100 && DmFixed.ProtectionSeconds == 3);
    }

    static void Weapons() {
        Test("W01", "the CS2 profile is loaded and names its build", DmWeapons.Ready && DmWeapons.Cs2Version == "1.41.8.8" && DmWeapons.Fingerprint.Length == 16, $"{DmWeapons.LoadError} {DmWeapons.Cs2Version} {DmWeapons.Fingerprint}");
        if (!DmWeapons.Ready) return;
        GunSpec Spec(string name) => GunSpec.All.First(g => g.Name == name);
        bool Stats(string name, out EffectiveGunStats s, bool alternate = false) => DmWeapons.TryStats(Spec(name), alternate, out s);
        Test("W02", "every one of the 35 guns has competitive numbers", GunSpec.All.Length == 35 && GunSpec.All.All(g => DmWeapons.TryStats(g, false, out var s) && s.Power > 0 && s.CycleSeconds > 0 && s.Capacity > 0), string.Join(",", GunSpec.All.Where(g => !DmWeapons.TryStats(g, false, out var s) || !(s.Power > 0)).Select(g => g.Name)));
        // copied by hand from weapons.vdata 1.41.8.8 (m_nDamage, m_nNumBullets, m_flHeadshotMultiplier, m_flArmorRatio)
        var expected = new (string Gun, float Damage, int Pellets, float Head, float Armour)[] {
            ("ak47", 36, 1, 4, 1.55f), ("awp", 115, 1, 4, 1.95f), ("glock18", 30, 1, 4, .94f), ("nova", 26, 9, 4, 1.0f), ("m4a1s", 38, 1, 3.475f, 1.4f), ("negev", 35, 1, 4, 1.42f), ("taser", 500, 1, 4, 2.0f) };
        foreach (var e in expected) {
            bool ok = Stats(e.Gun, out var s);
            Test("W03", $"{e.Gun}: damage, pellets, head multiplier and armour ratio are the vdata's", ok && s.Power == e.Damage * e.Pellets && s.Pellets == e.Pellets && s.HeadMultiplier == e.Head && DmWeapons.ArmourRatio(e.Gun) == e.Armour,
                $"power {s.Power} pellets {s.Pellets} head {s.HeadMultiplier} armour {DmWeapons.ArmourRatio(e.Gun)}");
        }
        var clips = GunSpec.All.Where(g => DmWeapons.TryStats(g, false, out var s) && s.Capacity != (int)DmWeapons.Raw(g.Name, "m_iMaxClip1")).Select(g => $"{g.Name} {ScGunGrowth.Capacity(Array.IndexOf(GunSpec.All, g), 0)}!={DmWeapons.Raw(g.Name, "m_iMaxClip1")}").ToArray();
        Test("W04", "every magazine is CS2's m_iMaxClip1", clips.Length == 0, string.Join("; ", clips));
        Stats("ak47", out var akStats); Stats("glock18", out var glockStats);
        Test("W05", "falloff: AK 0.98 and Glock 0.85 per 500 units (12.7 blocks)", MathF.Abs(akStats.Falloff(Spec("ak47"), 12.7f) - .98f) < .001f && MathF.Abs(glockStats.Falloff(Spec("glock18"), 12.7f) - .85f) < .001f && akStats.Falloff(Spec("ak47"), 0) == 1,
            $"{akStats.Falloff(Spec("ak47"), 12.7f)} {glockStats.Falloff(Spec("glock18"), 12.7f)}");
        Stats("awp", out var awpStats); Stats("negev", out var negev);
        Test("W06", "cycle times are the vdata's (AK 0.1 s, AWP 1.455 s, Negev 0.075 s)", MathF.Abs(akStats.CycleSeconds - .1f) < 1e-4f && MathF.Abs(awpStats.CycleSeconds - 1.455f) < 1e-4f && MathF.Abs(negev.CycleSeconds - .075f) < 1e-4f,
            $"{akStats.CycleSeconds} {awpStats.CycleSeconds} {negev.CycleSeconds}");
        var badHandling = GunSpec.All.Where(g => { var m = DmWeapons.Handling(g, false); return !float.IsFinite(m.BaseCone + m.MovingExtra + m.CrouchingCone + m.JumpExtra + m.BloomPerShot + m.BloomMax + m.BloomRecoverySeconds + m.KickPitch + m.KickYaw)
            || m.CrouchingCone > m.BaseCone + 1e-5f || m.MovingExtra < 0 || m.JumpExtra < 0 || m.BloomMax + 1e-5f < m.BloomPerShot || m.BloomRecoverySeconds <= 0; }).Select(g => g.Name).ToArray();
        Test("W07", "every gun's handling is finite and ordered (crouch <= stand, moving and jumping add, bloom has a ceiling)", badHandling.Length == 0, string.Join(",", badHandling));
        // AK standing: atan(0.0006 + 0.00641) = 0.40164 degrees ; crouching atan(0.0006 + 0.00481) = 0.30997 degrees (by hand)
        var akMode = DmWeapons.Handling(Spec("ak47"), false);
        Test("W08", "AK standing and crouching cones are atan(spread + inaccuracy) of the vdata", MathF.Abs(akMode.BaseCone - .40164f) < .0005f && MathF.Abs(akMode.CrouchingCone - .30997f) < .0005f, $"{akMode.BaseCone} {akMode.CrouchingCone}");
        // AWP: unscoped stand 0.0808 -> atan(0.081) = 4.6309 deg ; scoped 0.002 -> atan(0.0022) = 0.12605 deg
        var hip = DmWeapons.Handling(Spec("awp"), false); var scoped = DmWeapons.Handling(Spec("awp"), true);
        Test("W09", "AWP: the unscoped cone is wide, the scoped one narrow, both from the vdata", MathF.Abs(hip.BaseCone - 4.6309f) < .005f && MathF.Abs(scoped.BaseCone - .12605f) < .0005f, $"{hip.BaseCone} {scoped.BaseCone}");
        Test("W10", "the Zeus reaches 120 units (3.05 blocks) and no farther", Stats("taser", out var zeus) && MathF.Abs(zeus.Range - 120f / 39.37f) < .01f, $"{zeus.Range}");
        Test("W11", "a rifle's traced range is capped at 128 blocks", akStats.Range == DmWeapons.MaxRangeBlocks);
        Test("W12", "knife: 40 / 90 from behind, heavy 65 / 180 from behind; armour ratio 1.7 from the vdata", DmWeapons.KnifeDamage(false, false) == 40 && DmWeapons.KnifeDamage(false, true) == 90 && DmWeapons.KnifeDamage(true, false) == 65 && DmWeapons.KnifeDamage(true, true) == 180 && DmWeapons.ArmourRatio("knife_karambit") == 1.7f);
        Test("W13", "HE grenade: 99 at the centre, 350 units, armour ratio 1.2 (vdata)", DmWeapons.HeDamage == 99 && MathF.Abs(DmWeapons.HeRadiusBlocks - 350f / 39.37f) < .01f && DmWeapons.ArmourRatio("grenade_hegrenade") == 1.2f);
        Test("W14", "fire: 40 a second for both bottles (vdata m_nDamage)", DmWeapons.FirePerSecond("grenade_molotov") == 40 && DmWeapons.FirePerSecond("grenade_incendiary") == 40);
        Test("W15", "an unknown weapon's armour ratio leaves the damage as it is", DmWeapons.ArmourRatio("whatever") == 2 && DmWeapons.ArmourRatio(null) == 2);
        var disagree = GunSpec.All.Where(g => Cs2Weapons.Get(g.Name) is { } c && (c.Damage != DmWeapons.Raw(g.Name, "m_nDamage") || c.ArmorRatio != DmWeapons.Raw(g.Name, "m_flArmorRatio") || c.RangeModifier != DmWeapons.Raw(g.Name, "m_flRangeModifier") || c.HeadshotMultiplier != DmWeapons.Raw(g.Name, "m_flHeadshotMultiplier"))).Select(g => g.Name).ToArray();
        Test("W16", "the core's own CS2 table (an earlier extraction) and this profile agree on damage, armour ratio, range modifier and head multiplier", Cs2Weapons.Get("ak47") is not null && disagree.Length == 0, Cs2Weapons.Get("ak47") is null ? "core table not loaded: " + Cs2Weapons.LoadError : string.Join(",", disagree));
        Test("W17", "units: the core table's 39.37 units a metre and 500-unit falloff step", MathF.Abs(Cs2Weapons.UnitsPerMetre - 39.37f) < .01f && Cs2Weapons.FalloffUnits == 500, $"{Cs2Weapons.UnitsPerMetre} {Cs2Weapons.FalloffUnits}");
        // one statement of what a competitive AK body shot does end to end at 12.7 blocks: 36 * .98 = 35.28 ; * .775 = 27.342 -> 27 ;
        // armour (35.28 - 27.342) * .5 = 3.969 -> 3
        float power = akStats.PelletPower(Spec("ak47"), 12.7f);
        var settled = DmCombat.SettleRegions(100, 100, true, [(ScHitPart.Body, power)], 1, DmWeapons.ArmourRatio("ak47"), out _);
        Test("W18", "end to end: an AK body shot at 12.7 blocks takes 27 health and 3 armour", settled is { Health: 73, Armour: 97 }, $"{power} {settled}");
        float headPower = akStats.PelletPower(Spec("ak47"), 0) * akStats.HeadMultiplier;
        Test("W19", "end to end: an AK head shot kills through the helmet", DmCombat.SettleRegions(100, 100, true, [(ScHitPart.Head, headPower)], 1, DmWeapons.ArmourRatio("ak47"), out var where).Lethal && where == ScHitPart.Head);
    }
}
