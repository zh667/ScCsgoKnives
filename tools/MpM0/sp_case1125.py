"""Weapon case odds after the 2026-11-25 decision (gold = knives in their CS finish, CS guns weigh twice as much), in the
real game, isolated copy, muted:
  - the odds of each tier and of the CS rewards at levels 1, 8 and 20, from the loaded survivor case and the real roller
  - 4000 seeded rolls at level 20: every gold draw is a knife in its own finish
  - one opening screen that lands on a gold knife (the roll is built from the real case; the dialog is the real one), the
    claimed knife in the inventory keeps its finish
Usage: M0_ENGINE=131 python sp_case1125.py <label> <core package>[+<package>...]
The player's own Mods and worlds are never used; the window's sound and music are off for the whole run.
"""
import json, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import m0
from m0 import RESULTS, RUNS, sha, to_menu, poll
from mp_m1 import MAIN, KEEP_ACTIVE, SURVIVAL
from sp_gun_visuals import window_of, picture
from sp_engine_compat import STARTER_NONE, EMPTY, DURABLE, CLOSE_DIALOGS

m0.firewall_preflight = lambda: {"skipped": "standalone sp run; no MP role started", "problems": []}
if not m0.MPBIN.exists():
    m0.MPBIN = m0.M0 / "no-mp-build"; m0.MPBIN.mkdir(parents=True, exist_ok=True)

MUTE = 'Game.SettingsManager.SoundsVolume = 0f; Game.SettingsManager.MusicVolume = 0f; return "muted";'
SURV = ('Game.CaseRegistry.EnsureLoaded(); var def = Game.CaseRegistry.All.First(c => string.Equals(c.Id, Game.CsCaseRewards.TargetCase, System.StringComparison.OrdinalIgnoreCase)); '
        'int gunBlock = Game.BlocksManager.GetBlockIndex<Game.ScGunBlock>(true), knifeBlock = Game.BlocksManager.GetBlockIndex<Game.ScKnifeBlock>(true); ')


def odds(level): return SURV + (
    f'var ok = Game.CsCaseRewards.EligibleFor({level}); var w = Game.CaseRoller.EffectiveWeights(def, ok); double total = w.Values.Sum(); '
    'double P(Game.CaseRarity r, System.Func<Game.RewardDefinition, bool> pick) { if (!w.ContainsKey(r)) return 0; var rs = def.Rewards.Where(x => x.Rarity == r && ok(x)).ToArray(); '
    'return w[r] / total * rs.Where(pick).Sum(x => x.Weight) / rs.Sum(x => x.Weight) * 100; } '
    'double factory = P(Game.CaseRarity.Rare, x => x.Generated && Game.Terrain.ExtractContents(x.Value) == gunBlock); '
    'double templates = P(Game.CaseRarity.Epic, x => x.Generated); double knives = P(Game.CaseRarity.Legendary, x => x.Generated && Game.Terrain.ExtractContents(x.Value) == knifeBlock); '
    'double goldVanilla = P(Game.CaseRarity.Legendary, x => !x.Generated); double cs = 0; foreach (var r in w.Keys) cs += P(r, x => x.Generated); '
    'return System.FormattableString.Invariant($"tiers {string.Join(",", w.OrderBy(p => p.Key).Select(p => p.Key + "=" + (p.Value / total * 100).ToString("0.##")))} '
    'factory {factory:0.###} templates {templates:0.###} knives {knives:0.###} goldVanilla {goldVanilla:0.###} csAll {cs:0.##}");')

ROLLS = SURV + (
    'var ok = Game.CsCaseRewards.EligibleFor(20); var rnd = new System.Random(1125); int gold = 0, goldKnives = 0, goldOwnFinish = 0, goldSkinned = 0, guns = 0, n = 4000; '
    'var tiers = new System.Collections.Generic.Dictionary<Game.CaseRarity, int>(); '
    'for (int i = 0; i < n; i++) { var r = Game.CaseRoller.RollFor(def, ok, rnd).Reward; tiers[r.Rarity] = tiers.TryGetValue(r.Rarity, out var c) ? c + 1 : 1; '
    'if (r.Generated && (Game.Terrain.ExtractContents(r.Value) == gunBlock || r.Rarity == Game.CaseRarity.Epic)) guns++; '
    'if (r.Rarity != Game.CaseRarity.Legendary) continue; gold++; if (Game.Terrain.ExtractContents(r.Value) != knifeBlock) continue; goldKnives++; '
    'int skin = Game.ScKnifeBlock.SkinOf(r.Value); if (skin == Game.ScKnifeSkinCatalog.ForVariant(Game.ScKnifeBlock.GetVariant(r.Value))) goldOwnFinish++; if (skin != Game.ScKnifeSkinCatalog.None) goldSkinned++; } '
    'return $"n {n} tiers {string.Join(",", tiers.OrderBy(p => p.Key).Select(p => p.Key + "=" + p.Value))} csGuns {guns} gold {gold} knives {goldKnives} ownFinish {goldOwnFinish} skinned {goldSkinned}";')

# The real dialog on a roll from the real case whose winner is the first gold knife that has a finish; the claim puts
# the reward in slot 0.
GOLD_DIALOG = MAIN + SURV + (
    'var inv = pl.ComponentMiner.Inventory; for (int s = 0; s < inv.SlotsCount; s++) inv.RemoveSlotItems(s, inv.GetSlotCount(s)); '
    'var knife = def.Rewards.First(x => x.Rarity == Game.CaseRarity.Legendary && Game.ScKnifeBlock.SkinOf(x.Value) != Game.ScKnifeSkinCatalog.None); '
    'var baseRoll = Game.CaseRoller.RollFor(def, Game.CsCaseRewards.EligibleFor(20), new System.Random(7)); var reel = baseRoll.Reel.ToList(); reel[40] = knife; '
    'var roll = new Game.CaseRoll(knife, reel, 40); '
    'Game.DialogsManager.ShowDialog(null, new Game.CaseOpeningDialog(def, roll, r => inv.AddSlotItems(0, r.Value, r.Count))); '
    'return Game.ScKnifeSkinCatalog.Name(Game.ScKnifeBlock.SkinOf(knife.Value), Game.ScKnifeBlock.GetVariant(knife.Value)) + " | " + Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(knife.Value)].GetDisplayName(null, knife.Value);')
FINISH = ('var d = Game.DialogsManager.Dialogs.OfType<Game.CaseOpeningDialog>().LastOrDefault(); if (d == null) return "none"; '
          'typeof(Game.CaseOpeningDialog).GetMethod("FinishAnimation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(d, null); return "finished";')
CLAIM = MAIN + ('var d = Game.DialogsManager.Dialogs.OfType<Game.CaseOpeningDialog>().LastOrDefault(); if (d == null) return "none"; '
                'typeof(Game.CaseOpeningDialog).GetMethod("ClaimAndDismiss", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(d, null); '
                'var inv = pl.ComponentMiner.Inventory; int v = inv.GetSlotValue(0); if (inv.GetSlotCount(0) == 0) return "slot 0 empty"; '
                'return Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(v)].GetType().Name + " skin " + Game.ScKnifeBlock.SkinOf(v) + " own " + Game.ScKnifeSkinCatalog.ForVariant(Game.ScKnifeBlock.GetVariant(v)) '
                '+ " | " + Game.BlocksManager.Blocks[Game.Terrain.ExtractContents(v)].GetDisplayName(null, v);')


def main(label, packages):
    pkgs = [Path(p) for p in packages.split("+")]
    case_dir = RUNS / f"case1125-{label}-{time.strftime('%Y%m%d-%H%M%S')}"; (case_dir / "pictures").mkdir(parents=True)
    R = {"case": case_dir.name, "packages": {p.name: sha(p) for p in pkgs}, "checks": [], "pictures": [], "data": {}}
    def check(name, ok, detail=""):
        R["checks"].append({"check": name, "ok": bool(ok), "detail": detail})
        print(("PASS " if ok else "FAIL ") + name + " :: " + str(detail)[:600], flush=True)
    g = None; h = None
    def F(code, timeout=60): return g.func(code, timeout)
    def snap(name):
        try:
            im = picture(h); path = case_dir / "pictures" / f"{name}.jpg"; im.save(path, quality=88); R["pictures"].append(str(path)); return str(path)
        except Exception as e: R["pictures"].append(f"{name}: {e}"); return None
    try:
        g = m0.game("sp", case_dir, pkgs); R["engine"] = g.engine_info(); to_menu(g); F(MUTE)
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Play"', 120, m)
        m = g.mark(); g.cmd("CLICK_WIDGET NewWorld"); g.wait('Entered screen "NewWorld"', 120, m)
        poll(g, SURVIVAL, lambda v: v == "Survival", 20)
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Player"', 600, m)
        m = g.mark(); g.cmd("CLICK_WIDGET PlayButton"); g.wait('Entered screen "Game"', 600, m)
        poll(g, m0.PLAYER_SPAWNED, lambda v: v == "True", 240); F(KEEP_ACTIVE); F(MUTE); F(DURABLE); F(STARTER_NONE); F(CLOSE_DIALOGS); F(EMPTY)
        h = window_of(g.proc.pid)
        for level in (1, 8, 20):
            R["data"][f"odds{level}"] = v = F(odds(level)); print(f"odds Lv{level}: {v}", flush=True)
        o20 = R["data"]["odds20"]
        num = lambda key, s: float(s.split(key + " ")[1].split()[0])
        check("Lv20: gold 0.5 % and every bit of it knives", abs(num("knives", o20) - 0.5) < 1e-6 and num("goldVanilla", o20) == 0, o20)
        check("Lv20: factory guns about 3.05 %, painted and counter guns about 1.39 %", abs(num("factory", o20) - 3.055) < .01 and abs(num("templates", o20) - 1.389) < .01, o20)
        check("Lv1: no CS gun at all, knives still 0.5 %", num("factory", R["data"]["odds1"]) == 0 and num("templates", R["data"]["odds1"]) == 0 and abs(num("knives", R["data"]["odds1"]) - .5) < 1e-6, R["data"]["odds1"])
        rolls = R["data"]["rolls"] = F(ROLLS, 120); print("rolls:", rolls, flush=True)
        parts = rolls.split()
        val = lambda key: int(parts[parts.index(key) + 1])
        check("4000 rolls at Lv20: every gold draw is a knife in its own finish", val("gold") > 0 and val("gold") == val("knives") == val("ownFinish"), rolls)
        m = g.mark(); R["data"]["goldKnife"] = picked = F(GOLD_DIALOG); print("gold dialog:", picked, flush=True)
        time.sleep(1.2); snap("gold-rolling"); time.sleep(5.5); F(FINISH); time.sleep(.6); p = snap("gold-result")
        claimed = R["data"]["claimed"] = F(CLAIM); print("claimed:", claimed, flush=True)
        parts = claimed.split()
        check("the claimed gold knife keeps its finish in the inventory", parts[0] == "ScKnifeBlock" and parts[2] != "0" and parts[2] == parts[4], claimed)
    except Exception as e:
        check("run completed", False, f"{type(e).__name__}: {e}")
    finally:
        if g is not None: g.close()
        m0.RUNTIME_OWNER.release()
    failed = [c["check"] for c in R["checks"] if not c["ok"]]
    out = RESULTS / f"case1125-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print(f"SUMMARY {len(R['checks'])} checks, failed {failed}; {out}", flush=True)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2]))
