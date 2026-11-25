"""1.6.0 level-up cases across versions, isolated 1.9.3.1 copy, muted:
  A  a world made by an older release (no record) at level 8 opened with the candidate: no cases on the first load, the
     record starts at 8, level 9 gives one;
  B  a candidate world at level 10 (record 10) opened with the older release, levelled to 12 there (no cases: it does not
     know them), back in the candidate: the record survived the trip and levels 11-12 give their 2 x 2 cases once.
Usage: M0_ENGINE=131 python sp_levelworld.py <label> <candidate core> <older core>
"""
import json, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import m0
from m0 import RESULTS, RUNS, sha, to_menu, poll
from mp_m1 import MAIN, KEEP_ACTIVE

m0.firewall_preflight = lambda: {"skipped": "standalone sp131 run; no MP role started", "problems": []}
if not m0.MPBIN.exists():
    m0.MPBIN = m0.M0 / "no-mp-build"; m0.MPBIN.mkdir(parents=True, exist_ok=True)
MUTE = 'Game.SettingsManager.SoundsVolume = 0f; Game.SettingsManager.MusicVolume = 0f; return "muted";'
WORLD = MAIN + 'return project.FindSubsystem<Game.SubsystemGameInfo>(true).DirectoryName;'
SAVE = 'Game.GameManager.SaveProject(true, true); return "saved";'
def set_level(level): return MAIN + f'pl.PlayerData.Level = {level}f; return "level {level}";'
# Cases counted by block name, so the same snippet runs on an older release that has no case block.
CASES = MAIN + ('var b = Game.BlocksManager.Blocks.FirstOrDefault(x => x != null && x.GetType().Name == "ScCaseBlock"); if (b == null) return "-1"; int c = Game.Terrain.MakeBlockValue(b.BlockIndex, 0, 0); '
                'return (Game.ScInventoryTransaction_Count).ToString();').replace("Game.ScInventoryTransaction_Count", "System.Linq.Enumerable.Range(0, pl.ComponentMiner.Inventory.SlotsCount).Where(i => pl.ComponentMiner.Inventory.GetSlotValue(i) == c).Sum(i => pl.ComponentMiner.Inventory.GetSlotCount(i))")
LEDGER = MAIN + ('var s = project.FindSubsystem<Game.SubsystemScFirstCraft>(true); var owner = Game.ScGunHolders.RecoveryOwner(project, pl.ComponentMiner.Inventory); '
                 'return owner + " " + (s.Ledger.TryGetRewardedLevel(owner, out int l) ? l.ToString() : "none");')
def load(directory): return m0.Call(
    'Game.WorldsManager.UpdateWorldsList(); var w = Game.WorldsManager.WorldInfos.FirstOrDefault(x => x.DirectoryName == $s0); '
    'if (w == null) return "no world " + $s0; Game.ScreensManager.SwitchScreen("GameLoading", w, null); return "loading";', directory)
CLOSE = 'foreach (var d in Game.DialogsManager.Dialogs.ToArray()) Game.DialogsManager.HideDialog(d); return "closed";'


def main(label, new_package, old_package):
    new, old = Path(new_package), Path(old_package)
    case_dir = RUNS / f"levelworld-{label}-{time.strftime('%Y%m%d-%H%M%S')}"; case_dir.mkdir(parents=True)
    R = {"case": case_dir.name, "packages": {p.name: sha(p) for p in [new, old]}, "steps": [], "checks": []}
    T0 = time.time(); g = None
    def step(name, value=None): R["steps"].append({"step": name, "value": value}); print(f"[{time.time() - T0:.0f}] {name}: {str(value)[:500]}", flush=True); return value
    def check(name, ok, detail=""): R["checks"].append({"check": name, "ok": bool(ok), "detail": detail}); print(("PASS " if ok else "FAIL ") + name + " :: " + str(detail)[:500], flush=True)
    def start(name, pkg): game = m0.game(name, case_dir, [pkg]); to_menu(game); game.func(MUTE); return game
    def enter(game, directory):
        m = game.mark(); game.func(load(directory)); game.wait('Entered screen "Game"', 600, m)
        poll(game, m0.PLAYER_SPAWNED, lambda v: v == "True", 240); game.func(KEEP_ACTIVE); game.func(MUTE); time.sleep(2); game.func(CLOSE)
    def done(game): game.func(SAVE); time.sleep(1.5); game.close()
    try:
        # A: older release makes the world at level 8
        g = start("a1-old", old); m0.enter_world(g); g.func(KEEP_ACTIVE); g.func(MUTE); time.sleep(1); g.func(CLOSE)
        da = step("A world", g.func(WORLD)); g.func(set_level(8)); time.sleep(1.5); done(g); g = None
        g = start("a2-new", new); enter(g, da); time.sleep(1.5)
        a_first = step("A first load: cases, record", (g.func(CASES), g.func(LEDGER)))
        g.func(set_level(9)); time.sleep(1.5); a_next = step("A level 9", (g.func(CASES), g.func(LEDGER))); done(g); g = None
        check("A: an older world at level 8 gives no cases on its first load and records 8; level 9 gives 1",
              a_first[0] == "0" and a_first[1].endswith(" 8") and a_next[0] == "1" and a_next[1].endswith(" 9"), {"first": a_first, "9": a_next})
        # B: candidate world at level 10, a trip through the older release levelled to 12, back
        g = start("b1-new", new); m0.enter_world(g); g.func(KEEP_ACTIVE); g.func(MUTE); time.sleep(1); g.func(CLOSE)
        db = step("B world", g.func(WORLD)); time.sleep(1.5); g.func(set_level(10)); time.sleep(1.5)
        b1 = step("B level 10", (g.func(CASES), g.func(LEDGER))); done(g); g = None
        g = start("b2-old", old); enter(g, db); g.func(set_level(12)); time.sleep(1.5); b2 = step("B older release at 12", g.func(CASES)); done(g); g = None
        g = start("b3-new", new); enter(g, db); time.sleep(1.5); b3 = step("B back in the candidate", (g.func(CASES), g.func(LEDGER)))
        g.func(set_level(12)); time.sleep(1.5); b4 = step("B again", (g.func(CASES), g.func(LEDGER))); done(g); g = None
        check("B: level 10 in the candidate gives 9 cases and records 10", b1[0] == "9" and b1[1].endswith(" 10"), b1)
        check("B: after the older release the record is still there and levels 11-12 give 2 x 2 cases once", b3[0] == str(9 + 4) and b3[1].endswith(" 12") and b4 == b3,
              {"old": b2, "back": b3, "again": b4})
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None:
            try: g.close()
            except Exception: pass
        m0.RUNTIME_OWNER.release()
    out = RESULTS / f"levelworld-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    failed = [c["check"] for c in R["checks"] if not c["ok"]]
    print(f"{len(R['checks'])} checks, {len(failed)} failed {failed}; failure {R.get('failure')}; {out}", flush=True)
    return 1 if failed or R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:]))
