using Engine;
using GameEntitySystem;
namespace Game;

/// <summary>feedback-1124-170 (1.7.0): component economy, shotgun range/falloff/knockback, tube reload batches, shield
/// wear, enemy C4 blasts, cases and first-craft rewards. Headless; each test restores what it changes.</summary>
public static class ScFeedback1124SelfTest {
    sealed class Inventory : IInventory {
        public Project Project => null;
        public int SlotsCount => 8;
        public int VisibleSlotsCount { get; set; } = 8;
        public int ActiveSlotIndex { get; set; }
        public readonly int[] Values = new int[8], Counts = new int[8];
        public int GetSlotValue(int i) => Values[i];
        public int GetSlotCount(int i) => Counts[i];
        public int GetSlotCapacity(int i, int v) => i == 0 ? 1 : 40;
        public int GetSlotProcessCapacity(int i, int v) => 0;
        public void AddSlotItems(int i, int v, int n) { if (n == 0) return; if (Counts[i] > 0 && Values[i] != v) throw new InvalidOperationException("mixed slot"); Values[i] = v; Counts[i] += n; }
        public int RemoveSlotItems(int i, int n) { n = Math.Min(n, Counts[i]); Counts[i] -= n; return n; }
        public void ProcessSlotItems(int i, int v, int count, int process, out int result, out int resultCount) { result = v; resultCount = 0; }
        public void DropAllItems(Vector3 position) => Array.Clear(Counts);
    }
    static bool Near(float a, float b, float e = 1e-4f) => MathF.Abs(a - b) < e;

    public static void Run(Action<string, bool, string> check) {
        string detail = "";
        void Test(string name, Func<bool> test) { detail = ""; try { check("feedback-1124/" + name, test(), detail.Length > 0 ? detail : name); } catch (Exception e) { check("feedback-1124/" + name, false, e.ToString()); } }

        // --- D11/D12: iron per component and per gun ------------------------------------------------------------
        int Iron(int kind) => ScComponentCrafting.All[kind].Ingredients.Where(i => i.Id == "ironingot").Sum(i => i.Count);
        Test("economy/blank-recipe-4-iron", () => Iron(ScWeaponMaterialBlock.Blank) == 4
            && ScComponentCrafting.All[0].Ingredients.SequenceEqual(new[] { ("ironingot", 4), ("coalchunk", 2) }));
        Test("economy/mechanism-no-blank", () => ScComponentCrafting.All[1].Ingredients.All(i => !i.Id.StartsWith("sccsgomaterial"))
            && ScComponentCrafting.All[1].Ingredients.SequenceEqual(new[] { ("ironingot", 2), ("copperingot", 4), ("germaniumchunk", 2) }));
        Test("economy/gun-iron-totals", () => {
            var want = new Dictionary<string, int> { ["glock18"] = 14, ["nova"] = 22, ["ak47"] = 34, ["awp"] = 42, ["m249"] = 48 };
            var parts = new List<string>(); bool ok = true;
            foreach (var (gun, iron) in want) {
                var e = ScWeaponCrafting.All.Single(x => !x.Knife && x.Name == gun);
                int total = e.B * Iron(0) + e.M * Iron(1) + e.H * Iron(2) + e.O * Iron(3);
                parts.Add($"{gun} {total}"); ok &= total == iron;
            }
            detail = string.Join(", ", parts); return ok;
        });
        Test("economy/enemy-material-drop-split", () => {
            int[] count = new int[4]; const int n = 1000;
            for (int i = 0; i < n; i++) { float roll = (i + .5f) / n; count[ScComponentCrafting.EnemyDropKind(roll, false)]++; count[ScComponentCrafting.EnemyDropKind(roll, true)]++; }
            detail = string.Join("/", count);
            return count[0] == 1000 && count[1] == 600 && count[2] == 200 && count[3] == 200 && ScComponentCrafting.EnemyDropChance == .5f;
        });

        // --- D1–D4: shotguns -----------------------------------------------------------------------------------
        int Variant(string name) => Array.FindIndex(GunSpec.All, g => g.Name == name);
        int Template(int variant) => Terrain.MakeBlockValue(512, 0, GunSpec.WithId(variant, GunSpec.FreshFull));
        var shotguns = new (string Name, float Range, float NewRange)[] { ("nova", 24, 36), ("xm1014", 22, 33), ("mag7", 20, 30), ("sawedoff", 18, 27) };
        bool gunplay = ScGunplaySettings.Enabled;
        try {
            ScGunplaySettings.Enabled = true;
            Test("shotgun/range-x1.5", () => {
                var parts = new List<string>(); bool ok = true;
                foreach (var (name, range, wanted) in shotguns) {
                    var spec = GunSpec.All[Variant(name)];
                    var s = EffectiveGunStats.ResolveLevel(spec, Template(Variant(name)), false, 0);
                    parts.Add($"{name} {s.Range}/{s.NpcRange(spec)}");
                    ok &= Near(s.Range, wanted) && Near(s.NpcRange(spec), range) && Near(ScGunHandling.For(name).Range, range);
                }
                var ak = GunSpec.All[Variant("ak47")]; var a = EffectiveGunStats.ResolveLevel(ak, Template(Variant("ak47")), false, 0);
                ok &= Near(a.Range, ScGunHandling.For("ak47").Range) && ScShotgunTuning.Survival(ak) == ScGunHandling.For("ak47");
                detail = string.Join(", ", parts); return ok;
            });
            Test("shotgun/floor-30-both-curves", () => shotguns.All(g => {
                var spec = GunSpec.All[Variant(g.Name)]; var t = ScShotgunTuning.Survival(spec);
                return Near(ScGunHandling.Falloff(t, t.Range), .3f) && Near(ScGunHandling.Falloff(t, t.FalloffStart), 1) && ScGunHandling.Falloff(t, t.Range + .01f) == 0
                    && Near(t.FalloffStart, ScGunHandling.For(g.Name).FalloffStart * 1.5f) && Near(t.MidDistance ?? 0, (ScGunHandling.For(g.Name).MidDistance ?? 0) * 1.5f);
            }) && Near(ScSurvivalBalance.Falloff(GunSpec.All[Variant("nova")], 37.5f), .3f) && Near(ScSurvivalBalance.Falloff(GunSpec.All[Variant("nova")], 7.5f), 1)
               && Near(ScSurvivalBalance.Falloff(GunSpec.All[Variant("nova")], 22.5f), .4f) && Near(ScSurvivalBalance.Falloff(GunSpec.All[Variant("nova")], 80), .3f));
            Test("shotgun/total-power-unchanged", () => shotguns.All(g => {
                var spec = GunSpec.All[Variant(g.Name)]; var s = EffectiveGunStats.ResolveLevel(spec, Template(Variant(g.Name)), false, 0);
                return spec.Pellets > 1 && Near(s.PelletPower(spec, 0) * spec.Pellets, s.Power) && Near(s.Power, ScSurvivalBalance.PowerAtLevel(g.Name, 0));
            }));
            Test("shotgun/npc-unchanged", () => {
                var spec = GunSpec.All[Variant("nova")]; var s = EffectiveGunStats.ResolveLevel(spec, Template(Variant("nova")), false, 0);
                float old = MathUtils.Lerp(.4f, .15f, (20f - 15) / (24 - 15));
                detail = $"npc {s.NpcFalloff(spec, 20)} want {old}, player {s.Falloff(spec, 20)}";
                return Near(s.NpcFalloff(spec, 20), old) && s.Falloff(spec, 20) > old && Near(ScSurvivalBalance.FalloffBase(spec, 25), .15f);
            });
            Test("shotgun/knockback-close-only", () => {
                var nova = GunSpec.All[Variant("nova")]; var ak = GunSpec.All[Variant("ak47")];
                return ScShotgunTuning.Impulse(nova, false, true, 5, 75) == 1.5f && ScShotgunTuning.Impulse(nova, false, true, 6, 75) == 1.5f
                    && ScShotgunTuning.Impulse(nova, false, true, 10, 75) == .6f && ScShotgunTuning.Impulse(nova, false, true, 5, 500) == .6f
                    && ScShotgunTuning.Impulse(ak, false, true, 5, 75) == .6f && ScShotgunTuning.Impulse(nova, false, false, 5, 75) == 0
                    && ScShotgunTuning.Impulse(null, true, true, 1, 75) == 1.5f && ScShotgunTuning.Impulse(nova, false, true, float.NaN, 75) == .6f;
            });
        } finally { ScGunplaySettings.Enabled = gunplay; }

        // --- D5: a grown tube loads several shells per loop ------------------------------------------------------
        Test("reload/tube-per-loop", () => {
            var parts = new List<string>(); bool ok = true;
            foreach (string name in new[] { "nova", "xm1014", "sawedoff" }) {
                var spec = GunSpec.All[Variant(name)];
                for (int level = 0; level <= ScGunGrowth.MaxLevel; level++) {
                    int capacity = ScGunGrowth.Capacity(Variant(name), level), per = ScReloadTransaction.ShellsPerLoop(spec, capacity);
                    var batches = ScReloadTransaction.ShellBatches(capacity, per).ToArray();
                    ok &= batches.Sum() == capacity && batches.All(b => b >= 1 && b <= per) && batches.Length <= spec.Magazine + 1;
                    if (level is 0 or 10 or 50) parts.Add($"{name} Lv{level} {capacity}: {per}/loop x{batches.Length}");
                }
            }
            var nova = GunSpec.All[Variant("nova")];
            ok &= ScReloadTransaction.ShellsPerLoop(nova, ScGunGrowth.Capacity(Variant("nova"), 0)) == 1
                && ScGunGrowth.Capacity(Variant("nova"), 10) == 12 && ScReloadTransaction.ShellsPerLoop(nova, 12) == 2
                && ScGunGrowth.Capacity(Variant("nova"), 50) == 52 && ScReloadTransaction.ShellsPerLoop(nova, 52) == 7
                && ScReloadTransaction.ShellBatches(52, 7).Count() == 8 && ScReloadTransaction.ShellBatches(52, 7).Last() == 3
                && ScReloadTransaction.ShellBatches(5, 7).SequenceEqual(new[] { 5 }) && !ScReloadTransaction.ShellBatches(0, 7).Any();
            if (Cs2Rig.GetReloadSections("nova") is { } sections)
                parts.Add($"nova empty fill Lv0 {sections.Duration(8):0.00}s Lv50 {sections.Duration(ScReloadTransaction.ShellBatches(52, 7).Count()):0.00}s (was {sections.Duration(52):0.00}s)");
            detail = string.Join("; ", parts); return ok;
        });
        const int shellItem = 901;
        // A grown tube is a world record at that growth level (a Lv0 record refuses rounds above the base tube).
        int TubeGun(int rounds, int level = 50) {
            var registry = ScGunRegistry.Current ??= new ScGunRegistry();
            int v = Variant("nova"), max = ScGunGrowth.MaxDurability(v, level);
            int id = registry.Allocate(v, rounds, false, max, max);
            var records = (Dictionary<int, ScGunRecord>)typeof(ScGunRegistry).GetField("m_records", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(registry);
            var r = records[id]; r.CounterInstalled = true; r.AppliedGrowthLevel = level; r.GrowthRulesVersion = ScGunGrowth.RulesVersion; // test setup only
            return Terrain.MakeBlockValue(512, 0, GunSpec.WithId(v, id));
        }
        Test("reload/interrupt-keeps-loaded", () => {
            var i = new Inventory(); i.AddSlotItems(0, TubeGun(0), 1); i.AddSlotItems(1, shellItem, 30);
            var t = new ScReloadTransaction(i, 0, i.GetSlotValue(0), shellItem, 1, 52);
            int a = t.InsertShells(7), b = t.InsertShells(7), c = t.InsertShells(7);
            t.Cancel();   // fire pressed after the third loop: the shells already in stay, nothing more is charged
            int rounds = GunSpec.GetRounds(Terrain.ExtractData(i.GetSlotValue(0)));
            detail = $"{a}+{b}+{c} rounds {rounds} shells left {i.Counts[1]}";
            return a == 7 && b == 7 && c == 7 && rounds == 21 && i.Counts[1] == 9 && t.InsertShells(7) == 0 && i.Counts[1] == 9;
        });
        Test("reload/ammo-short", () => {
            var i = new Inventory(); i.AddSlotItems(0, TubeGun(0), 1); i.AddSlotItems(1, shellItem, 5);
            var t = new ScReloadTransaction(i, 0, i.GetSlotValue(0), shellItem, 1, 52);
            int done = t.InsertShells(7); int rounds = GunSpec.GetRounds(Terrain.ExtractData(i.GetSlotValue(0)));
            detail = $"loaded {done}, rounds {rounds}, shells {i.Counts[1]}, result {t.LastResult}";
            return done == 5 && rounds == 5 && i.Counts[1] == 0 && t.LastResult != ScGunResult.Success;
        });
        Test("reload/full-stops-without-charge", () => {
            var i = new Inventory(); i.AddSlotItems(0, TubeGun(10, 10), 1); i.AddSlotItems(1, shellItem, 10);
            var t = new ScReloadTransaction(i, 0, i.GetSlotValue(0), shellItem, 1, 12);
            int done = t.InsertShells(7);
            return done == 2 && GunSpec.GetRounds(Terrain.ExtractData(i.GetSlotValue(0))) == 12 && i.Counts[1] == 8;
        });
        Test("reload/mag7-is-not-a-tube", () => !ScReloadTransaction.IsTube("mag7") && ScReloadTransaction.IsTube("nova") && ScReloadTransaction.IsTube("xm1014") && ScReloadTransaction.IsTube("sawedoff"));
    }
}
