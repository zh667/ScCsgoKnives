using Engine;
using TemplatesDatabase;
using GameEntitySystem;
namespace Game;

/// <summary>feedback-1124-170 (1.6.0): component economy, shotgun range/falloff/knockback, tube reload batches, shield
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
        Test("economy/blank-recipe-6-iron", () => Iron(ScWeaponMaterialBlock.Blank) == 6
            && ScComponentCrafting.All[0].Ingredients.SequenceEqual(new[] { ("ironingot", 6), ("coalchunk", 2) }));
        Test("economy/mechanism-no-blank", () => ScComponentCrafting.All[1].Ingredients.All(i => !i.Id.StartsWith("sccsgomaterial"))
            && ScComponentCrafting.All[1].Ingredients.SequenceEqual(new[] { ("ironingot", 5), ("copperingot", 4), ("germaniumchunk", 2) }));
        Test("economy/gun-iron-totals-30-percent-less", () => {
            // Before 1.6.0 a metal part took 8 iron and a mechanical part a metal part (8 more): 2496 for the 35 guns, AK 88.
            var guns = ScWeaponCrafting.All.Where(x => !x.Knife).ToArray();
            int Total(ScWeaponCrafting.Entry e) => e.B * Iron(0) + e.M * Iron(1) + e.H * Iron(2) + e.O * Iron(3);
            int all = guns.Sum(Total), before = guns.Sum(e => (e.B + e.M) * 8);
            var ak = guns.Single(x => x.Name == "ak47");
            detail = $"35 guns {before} -> {all} ({100.0 * (all - before) / before:0.0} %), AK {(ak.B + ak.M) * 8} -> {Total(ak)}, guns {guns.Length}";
            return guns.Length == 35 && before == 2496 && all == 1751 && Total(ak) == 61;
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
                    && ScShotgunTuning.Impulse(nova, false, true, 10, 75) == .6f && ScShotgunTuning.Impulse(nova, false, true, 5, 350) == 1.5f && ScShotgunTuning.Impulse(nova, false, true, 5, 1500) == .6f
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
        // --- D16-D18: cases ---------------------------------------------------------------------------------------
        RewardDefinition Reward(CaseRarity rarity, string tag, int level = 0) => new() { Rarity = rarity, Block = tag, Value = tag.GetHashCode(), Count = 1, Weight = 1, MinPlayerLevel = level };
        Test("cases/survivor-weights", () => { var w = new RarityWeights(); return w.Common == 70 && w.Uncommon == 20 && w.Rare == 7 && w.Epic == 2.5 && w.Legendary == .5; });
        Test("cases/legend-level-filter", () => {
            var d = new CaseDefinition { Id = "t", Rewards = [Reward(CaseRarity.Common, "coal"), Reward(CaseRarity.Epic, "glock18", 2), Reward(CaseRarity.Epic, "ak47", 14), Reward(CaseRarity.Epic, "awp", 18)] };
            var random = new System.Random(1124); var low = new HashSet<string>(); var high = new HashSet<string>();
            for (int i = 0; i < 1000; i++) { low.Add(CaseRoller.RollFor(d, CsCaseRewards.EligibleFor(1), random).Reward.Block); high.Add(CaseRoller.RollFor(d, CsCaseRewards.EligibleFor(18), random).Reward.Block); }
            var reels = CaseRoller.RollFor(d, CsCaseRewards.EligibleFor(1), random).Reel.Select(r => r.Block).ToHashSet();
            detail = $"Lv1 {string.Join(",", low)}; Lv18 {string.Join(",", high)}";
            return low.SetEquals(new[] { "coal" }) && high.Contains("awp") && high.Contains("ak47") && !reels.Contains("awp") && !reels.Contains("glock18");
        });
        Test("cases/empty-tier-falls-back", () => {
            var d = new CaseDefinition { Id = "t", Rewards = [Reward(CaseRarity.Common, "coal"), Reward(CaseRarity.Uncommon, "grip"), Reward(CaseRarity.Epic, "awp", 18), Reward(CaseRarity.Legendary, "knife")] };
            var w = CaseRoller.EffectiveWeights(d, CsCaseRewards.EligibleFor(1));
            detail = string.Join(", ", w.Select(p => $"{p.Key} {p.Value}"));
            // Epic (2.5) and the empty Rare (7) fall to Uncommon; Legendary keeps its own 0.5.
            return w.Count == 3 && w[CaseRarity.Common] == 70 && Math.Abs(w[CaseRarity.Uncommon] - 29.5) < 1e-9 && w[CaseRarity.Legendary] == .5
                && Math.Abs(CaseRoller.EffectiveWeights(d, CsCaseRewards.EligibleFor(18)).Values.Sum() - 100) < 1e-9;
        });
        // 2026-11-25 (user decision): gold is only knives, each in its CS finish; the CS guns weigh twice as much.
        Test("cases/gold-tier-is-skinned-knives", () => {
            CaseRegistry.EnsureLoaded();
            var survivor = CaseRegistry.All.FirstOrDefault(c => string.Equals(c.Id, CsCaseRewards.TargetCase, StringComparison.OrdinalIgnoreCase));
            if (survivor is null) { detail = "no survivor case"; return false; }
            int knifeBlock = BlocksManager.GetBlockIndex<ScKnifeBlock>(true);
            var gold = survivor.Rewards.Where(r => r.Rarity == CaseRarity.Legendary).ToArray();
            int skinned = gold.Count(r => ScKnifeBlock.SkinOf(r.Value) != ScKnifeSkinCatalog.None);
            bool allKnives = gold.Length > 0 && gold.All(r => r.Generated && Terrain.ExtractContents(r.Value) == knifeBlock);
            bool ownFinish = gold.All(r => ScKnifeBlock.SkinOf(r.Value) == ScKnifeSkinCatalog.ForVariant(ScKnifeBlock.GetVariant(r.Value)));
            detail = $"gold {gold.Length}, skinned {skinned}, names {string.Join(" | ", gold.Take(4).Select(r => ScKnifeSkinCatalog.Name(ScKnifeBlock.SkinOf(r.Value), ScKnifeBlock.GetVariant(r.Value))))}";
            return allKnives && ownFinish && (ScMinimalEdition.Enabled || skinned > 0);
        });
        Test("cases/cs-gun-weights", () => {
            CaseRegistry.EnsureLoaded();
            var survivor = CaseRegistry.All.First(c => string.Equals(c.Id, CsCaseRewards.TargetCase, StringComparison.OrdinalIgnoreCase));
            double Sum(CaseRarity rarity, System.Func<RewardDefinition, bool> pick) => survivor.Rewards.Where(r => r.Rarity == rarity && r.Generated && pick(r)).Sum(r => r.Weight);
            int gunBlock = BlocksManager.GetBlockIndex<ScGunBlock>(true);
            double guns = Sum(CaseRarity.Rare, r => Terrain.ExtractContents(r.Value) == gunBlock), templates = Sum(CaseRarity.Epic, _ => true);
            double vanillaEpic = survivor.Rewards.Where(r => r.Rarity == CaseRarity.Epic && !r.Generated).Sum(r => r.Weight);
            detail = $"factory guns {guns}, painted+counter {templates}, vanilla epic {vanillaEpic}";
            return Math.Abs(guns - CsCaseRewards.GunsWeight) < 1e-9 && Math.Abs(templates - (CsCaseRewards.PaintedGunsWeight + CsCaseRewards.CounterGunsWeight)) < 1e-9
                && Math.Abs(vanillaEpic - 7.2) < 1e-9;
        });
        Test("cases/drop-rates", () => {
            var survivor = new CaseDefinition { CreatureDropChance = .03 };
            return CaseAcquisition.DropChance(true, survivor) == .15 && CaseAcquisition.DropChance(false, survivor) == .03;
        });
        Test("cases/level-rewards-1-2-3-per-level", () => {
            int[] want = { 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 3, 3 };
            return Enumerable.Range(0, want.Length).All(l => CaseAcquisition.CasesForLevel(l) == want[l]) && CaseAcquisition.CasesForLevels(1, 30) == 9 + 20 + 30
                && CaseAcquisition.CasesForLevels(9, 12) == 1 + 2 + 2 && CaseAcquisition.CasesForLevels(20, 21) == 3;
        });
        Test("cases/level-rewards-once-per-level-persisted", () => {
            var ledger = new ScFirstCraftLedger(); var got = new List<int>();
            int R(int level, bool creative = false, string who = "player/1") { int n = CaseAcquisition.LevelReward(ledger, who, level, creative); got.Add(n); return n; }
            R(7);            // first sight of a world from before the rule: recorded, nothing granted
            R(8); R(8);      // one level, once
            R(12);           // a jump: 9, 10 (1 each), 11, 12 (2 each)
            R(10); R(12);    // died and lost levels, regained: nothing
            for (int i = 0; i < 2; i++) { var v = new ValuesDictionary(); ledger.Save(v); ledger = ScFirstCraftLedger.Load(Round(v)); }
            R(12); R(13);    // after two saves: still nothing for 12, 2 for 13
            R(20, true);     // creative: recorded, nothing granted
            R(21);           // 3
            R(5, false, "player/2"); R(6, false, "player/2"); // another player counts on its own
            detail = string.Join(",", got);
            return got.SequenceEqual(new[] { 0, 1, 0, 6, 0, 0, 0, 2, 0, 3, 0, 1 });
        });

        // --- D22-D28: first-craft reward -------------------------------------------------------------------------
        ScWeaponCrafting.Entry Gun(string name) => ScWeaponCrafting.All.First(e => !e.Knife && e.Name == name);
        var anyKnife = ScWeaponCrafting.All.First(e => e.Knife);
        ValuesDictionary Round(ValuesDictionary v) { var x = new System.Xml.Linq.XElement("Values"); v.Save(x); var r = new ValuesDictionary(); r.ApplyOverrides(System.Xml.Linq.XElement.Parse(x.ToString())); return r; }
        Test("first-craft/once-per-gun-and-player", () => {
            var ledger = new ScFirstCraftLedger(); var granted = new List<string>();
            bool Make(string owner, string gun) => ledger.CraftAndReward(() => true, owner, Gun(gun), false, granted.Add);
            Make("player:1", "ak47"); Make("player:1", "ak47"); Make("player:1", "m4a4"); Make("player:2", "ak47");
            detail = string.Join(",", granted);
            return granted.SequenceEqual(new[] { "ak47", "m4a4", "ak47" }) && ledger.Has("player:2", "ak47") && !ledger.Has("player:2", "m4a4");
        });
        Test("first-craft/save-reload-no-repeat", () => {
            var ledger = new ScFirstCraftLedger(); ledger.TryMark("player:1", "ak47"); ledger.TryMark("player:2", "awp"); ledger.TryMark("player:1", "future_gun_from_a_newer_catalogue");
            for (int i = 0; i < 2; i++) { var v = new ValuesDictionary(); ledger.Save(v); ledger = ScFirstCraftLedger.Load(Round(v)); }
            int grants = 0; ledger.CraftAndReward(() => true, "player:1", Gun("ak47"), false, _ => grants++);
            return grants == 0 && ledger.Has("player:2", "awp") && ledger.Has("player:1", "future_gun_from_a_newer_catalogue") && !ledger.Has("player:2", "ak47");
        });
        Test("first-craft/cancel-fail-creative-knife-none", () => {
            var ledger = new ScFirstCraftLedger(); int grants = 0;
            ledger.CraftAndReward(() => false, "p", Gun("ak47"), false, _ => grants++);
            bool threw = false; try { ledger.CraftAndReward(() => throw new InvalidOperationException("injected"), "p", Gun("ak47"), false, _ => grants++); } catch (InvalidOperationException) { threw = true; }
            ledger.CraftAndReward(() => true, "p", Gun("ak47"), true, _ => grants++);
            ledger.CraftAndReward(() => true, "p", anyKnife, false, _ => grants++);
            ledger.CraftAndReward(() => true, null, Gun("ak47"), false, _ => grants++);
            return threw && grants == 0 && !ledger.Has("p", "ak47") && ledger.CraftAndReward(() => true, "p", Gun("ak47"), false, _ => grants++) && grants == 1;
        });
        Test("first-craft/unknown-schema-kept-read-only", () => {
            var v = new ValuesDictionary(); v.SetValue("Schema", 2); v.SetValue("Future", "x");
            var ledger = ScFirstCraftLedger.Load(Round(v)); var back = new ValuesDictionary(); ledger.Save(back);
            return !ledger.TryMark("p", "ak47") && back.GetValue("Schema", 0) == 2 && back.GetValue("Future", "") == "x";
        });
        Test("first-craft/world-copy-restores-only-a-lost-group-of-this-world", () => {
            System.Xml.Linq.XElement Project(string seed, bool withLedger) {
                var subs = new System.Xml.Linq.XElement("Subsystems", new System.Xml.Linq.XElement("Values", new System.Xml.Linq.XAttribute("Name", "GameInfo"),
                    new System.Xml.Linq.XElement("Value", new System.Xml.Linq.XAttribute("Name", "WorldSeed"), new System.Xml.Linq.XAttribute("Type", "string"), new System.Xml.Linq.XAttribute("Value", seed))));
                if (withLedger) { var v = new ValuesDictionary(); var l = new ScFirstCraftLedger(); l.TryMark("player:1", "ak47"); l.Save(v);
                    var x = new System.Xml.Linq.XElement("Values", new System.Xml.Linq.XAttribute("Name", SubsystemScFirstCraft.GroupName)); v.Save(x); subs.Add(x); }
                return new System.Xml.Linq.XElement("Project", subs);
            }
            var saved = Project("123", true); var copy = ScFirstCraftMirror.Document(saved);
            var lost = Project("123", false); bool restored = ScFirstCraftMirror.Restore(lost, copy);
            var group = lost.Element("Subsystems").Elements("Values").Single(e => (string)e.Attribute("Name") == SubsystemScFirstCraft.GroupName);
            var v2 = new ValuesDictionary(); v2.ApplyOverrides(group); var back = ScFirstCraftLedger.Load(v2);
            return copy is not null && restored && back.Has("player:1", "ak47")
                && !ScFirstCraftMirror.Restore(Project("999", false), copy)           // another world's copy
                && !ScFirstCraftMirror.Restore(Project("123", true), copy)            // Project.xml keeps its own group
                && ScFirstCraftMirror.Document(Project("123", false)) is null;
        });
        Test("reload/mag7-is-not-a-tube", () => !ScReloadTransaction.IsTube("mag7") && ScReloadTransaction.IsTube("nova") && ScReloadTransaction.IsTube("xm1014") && ScReloadTransaction.IsTube("sawedoff"));
        // 1.6.0 (user 2026-11-25): the conspicuous enemy-C4 alert is a mod-settings switch, off unless the stored value says on.
        Test("bomb-alert/off-unless-stored-on", () => {
            var forced = ScBombAlertSetting.Forced; ScBombAlertSetting.Forced = null;
            try {
                bool stored = ModSettingsManager.TryGet(out bool v, ScBombAlertSetting.Package, ScBombAlertSetting.Page, ScBombAlertSetting.Item) && v;
                ScBombAlertSetting.Reload(); bool now = ScBombAlertSetting.Enabled;
                ScBombAlertSetting.Changed(["Weapons", "Editor"]); bool other = ScBombAlertSetting.Enabled;
                ScBombAlertSetting.Changed(["Alerts", "BombAlert"]); bool again = ScBombAlertSetting.Enabled;
                detail = $"stored {stored} enabled {now}";
                return now == stored && other == now && again == stored;
            } finally { ScBombAlertSetting.Forced = forced; ScBombAlertSetting.Reload(); }
        });
    }
}
