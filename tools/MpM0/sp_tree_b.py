"""world-tree-gun-registry-20261107 phase B on the isolated standalone engine (M0_ENGINE=1951 by default here).

fixture    A COPY of the read-only fixture made by sp_portal_swap.py (the reproduced Portal swap: World1 #1 AWP/4, #2 M4A1-S/18,
           #3 AK-47/25; items AK-47 on #1 and AWP on #3; Space_0 with its own registry; a Ghoul world) is put into the runtime
           and loaded with the new build: the world loads, the AK-47 is #3 with 25 rounds and the AWP #1 with 4, the M4A1-S
           #2 with 18; Space_0 joins (one registry); Ghoul's travel with those guns loads its world; save and load again.
roundtrip  One fresh tree switched 1.5.1 -> new -> 1.5.1 -> new: each session loads without refusal; what the older build
           changed (a shot, a gun it made) is in the tree afterwards, what the new build made earlier is still there.
Usage: sp_tree_b.py fixture <label> <new cs package> <portal> <ghoul> <fixture dir>
       sp_tree_b.py roundtrip <label> <new cs package> <delivered 1.5.1 package>
"""
import json, os, shutil, stat, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))

import sp_tree   # noqa: F401  (sp_uninstall's standalone/worktree harness adaptations)
import m0
from m0 import RESULTS, RUNS, sha, to_menu, poll
from mp_m1 import MAIN, KEEP_ACTIVE
from sp_deathmatch import GUI_IN_SHOTS
from sp_tree import MOVE, CAMERA_READY
from sp_subworld import GHOUL_TRAVEL
from sp_uninstall import load, WORLD, SAVE
from sp_portal_swap import STATE, RAY0
from sp_smoke_view import snap

RELOAD = 'EXEC var info = Game.GameManager.WorldInfo; Game.GameManager.SaveProject(true, true); Game.GameManager.DisposeProject(); Game.ScreensManager.SwitchScreen("GameLoading", info, null);'
ADD = lambda slot, name, rounds: MAIN + (   # a new numbered gun made by whichever build is running, into a slot
    f'var inv = pl.ComponentMiner.Inventory; int gb = Game.BlocksManager.GetBlockIndex<Game.ScGunBlock>(true); int v = System.Array.FindIndex(Game.GunSpec.All, s => s.Name == "{name}"); '
    f'int id = Game.ScGunRegistry.Current.Allocate(v, {rounds}, false, Game.ScGunDurability.Full(v)); inv.RemoveSlotItems({slot}, inv.GetSlotCount({slot})); '
    f'inv.AddSlotItems({slot}, Game.Terrain.MakeBlockValue(gb, 0, Game.GunSpec.WithId(v, id)), 1); return id.ToString();')
SLOTS4 = MAIN + ('var inv = pl.ComponentMiner.Inventory; var reg = Game.ScGunRegistry.Current; '
    'return string.Join(";", System.Linq.Enumerable.Range(0, 4).Select(i => { int v = inv.GetSlotValue(i); int d = Game.Terrain.ExtractData(v); int id = Game.GunSpec.GetId(d); '
    '  string rec = reg != null && reg.TryGetSnapshot(id, out var s) ? Game.GunSpec.All[s.Variant].Name + "/" + s.Rounds : "-"; '
    '  return inv.GetSlotCount(i) == 0 ? i + ":empty" : i + ":" + Game.GunSpec.All[System.Math.Max(0, Game.GunSpec.GetVariant(d))].Name + "#" + id + "=" + rec; }));')


def harness(case):
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"{case}-{stamp}"; (case_dir / "frames").mkdir(parents=True)
    R = {"case": case, "engine": m0.SP_VERSION, "steps": [], "checks": [], "frames": [], "sessions": []}
    T0 = time.time()
    def step(name, value=None):
        R["steps"].append({"t": round(time.time() - T0, 1), "step": name, "value": value}); print(f"[{round(time.time() - T0, 1)}] {name}: {str(value)[:900]}", flush=True); return value
    def check(name, ok, detail=""):
        R["checks"].append({"check": name, "ok": bool(ok), "detail": detail}); print(("PASS " if ok else "FAIL ") + name + " :: " + str(detail)[:900], flush=True); return ok
    return case_dir, R, step, check


def finish(R, g, out_name):
    if g is not None:
        try: g.close()
        except Exception: pass
    m0.RUNTIME_OWNER.release()
    RESULTS.mkdir(parents=True, exist_ok=True)
    out = RESULTS / out_name; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    failed = [c["check"] for c in R["checks"] if not c["ok"]]
    print(f"engine {R['engine']}; checks {len(R['checks']) - len(failed)}/{len(R['checks'])}; failure {R.get('failure')}; failed {failed}; {out}", flush=True)
    return 1 if R.get("failure") or failed else 0


def entered(game, step, m):
    hit = game.wait(['Entered screen "Game"', "Loading World Failed", "gun load refused", "gun format load refused"], 900, m); step("entered", hit)
    if hit == 'Entered screen "Game"':
        poll(game, m0.PLAYER_SPAWNED, lambda v: v == "True", 240); game.func(KEEP_ACTIVE); game.func(GUI_IN_SHOTS)
        step("camera with the player", poll(game, CAMERA_READY, lambda v: v == "ready", 120)); time.sleep(2)
    return hit


def fixture(label, cs_package, portal_package, ghoul_package, fixture_dir):
    case_dir, R, step, check = harness(f"tree-b-fixture-{label}")
    pkgs = [Path(cs_package), Path(portal_package), Path(ghoul_package)]; R["packages"] = {p.name: sha(p) for p in pkgs}
    source = Path(fixture_dir) / "World"; g = None
    keys = ("[GUN_TREE]", "[GUN_INTEGRITY]", "型号冲突", "gun load refused", "Loading World Failed", "[GUN_MIRROR]", "gun registry:", "跨世界")
    def state(name): return step(name, json.loads(g.func(STATE)))
    def lines(since): return [l for l in g.lines[since:] if any(k in l for k in keys)]
    try:
        manifest = json.loads((Path(fixture_dir) / "manifest.json").read_text("utf-8"))
        before = next(s["value"] for s in manifest["steps"] if s["step"].startswith("1: World1, before the trip"))
        R["fixtureBefore"] = before; R["fixtureManifest"] = str(Path(fixture_dir) / "manifest.json")
        g = m0.game("fixture", case_dir, pkgs); R["engine_info"] = g.engine_info(); to_menu(g)
        target = Path(g.dir) / "doc" / "Worlds" / "World"
        if target.exists(): raise RuntimeError(f"{target} already exists in the runtime")
        shutil.copytree(source, target, copy_function=shutil.copyfile)          # a copy; the fixture itself is never opened for writing
        for p in target.rglob("*"):
            if p.is_file(): os.chmod(p, stat.S_IREAD | stat.S_IWRITE)
        step("fixture copied", {"from": str(source), "to": str(target), "files": sum(1 for p in target.rglob("*") if p.is_file())})
        m = g.mark(); step("load", g.func(load("app:/doc/Worlds/World"))); hit = entered(g, step, m)
        loaded = state("loaded with the new build"); merge = step("merge lines", lines(m))
        check("the world loads", hit == 'Entered screen "Game"', hit)
        slots = loaded["slots"]
        check("AK-47 back on its own record #3 with 25 rounds", slots[0].startswith("0:ak47#3 rounds=25") and "current[3]=ak47/25" in slots[0], slots[0])
        check("AWP back on its own record #1 with 4 rounds", slots[2].startswith("2:awp#1 rounds=4") and "current[1]=awp/4" in slots[2], slots[2])
        check("M4A1-S unchanged on #2 with 18 rounds", slots[1].startswith("1:m4a1s#2 rounds=18"), slots[1])
        check("no model conflict on the load", not any("型号冲突" in l for l in merge), merge)
        check("the merge is logged with the corrected guns", any("merged the registries of" in l for l in merge), merge)
        try: s = snap(g, f"tree-b-fixture-{label}-00-loaded", ((0, 0, 0), (1, 1, 1)))[0]; R["frames"].append(s); step("frame loaded", s)
        except Exception as e: step("frame failed loaded", str(e))
        # Space_0 joins: one registry for both projects, the same guns
        m = g.mark(); step("into Space_0", g.func(MOVE)); g.func(KEEP_ACTIVE); step("camera", poll(g, CAMERA_READY, lambda v: v == "ready", 120)); time.sleep(3)
        space = state("in Space_0"); step("space lines", lines(m))
        check("Space_0 joined: one registry, the same guns and rounds", space["scene"].endswith("/Space_0") and space["sceneRegistry"] == "same as root" and space["slots"] == loaded["slots"], space)
        step("home", g.func(MOVE)); g.func(KEEP_ACTIVE); poll(g, CAMERA_READY, lambda v: v == "ready", 120); time.sleep(2)
        # Ghoul with those guns
        step("save", g.func(SAVE)); time.sleep(2)
        # Ghoul's TransPortal toggles by its own saved state: the fixture's run was refused on the way in, so its state may
        # still say "inside" and the first call may lead home. Travel until the player is in Tartareosity, then home.
        def ghoul(n):
            m = g.mark(); step(f"Ghoul TransPortal {n}", g.func(GHOUL_TRAVEL)); hit = entered(g, step, m)
            st = state(f"after Ghoul TransPortal {n}"); st["lines"] = lines(m); return hit, st
        trips = [ghoul(1)]
        if not trips[-1][1]["scene"].endswith("/Tartareosity"): trips.append(ghoul(2))
        hit, inside = trips[-1]
        check("Ghoul's world loads with the guns", hit == 'Entered screen "Game"' and inside["scene"].endswith("/Tartareosity") and not any("refused" in l for l in inside["lines"]),
              [(h, t["scene"]) for h, t in trips])
        check("in the Ghoul world: same numbers and rounds", all(t["slots"] == loaded["slots"] for _, t in trips), [t["slots"] for _, t in trips])
        hit, home = ghoul(len(trips) + 1)
        check("home again: same numbers and rounds", hit == 'Entered screen "Game"' and home["scene"] == loaded["scene"] and home["slots"] == loaded["slots"], {"scene": home["scene"], "slots": home["slots"]})
        m = g.mark(); g.cmd(RELOAD); hit = entered(g, step, m)
        again = state("after save and load")
        check("after save and load: same numbers and rounds, no conflict", hit == 'Entered screen "Game"' and again["slots"] == loaded["slots"] and not any("型号冲突" in l for l in lines(m)), again["slots"])
        R["allLines"] = [l for l in g.lines if any(k in l for k in keys)][:200]; R["gameErrors"] = g.errors()[:40]
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    try:   # the fixture itself: every file as its manifest recorded it, still read-only
        import hashlib
        files = manifest["files"]; bad = [n for n, v in files.items() if hashlib.sha256((Path(fixture_dir) / n).read_bytes()).hexdigest() != v["sha256"]]
        writable = [n for n in files if os.access(Path(fixture_dir) / n, os.W_OK)]
        check("the fixture is unchanged and still read-only", not bad and not writable, {"changed": bad, "writable": writable, "files": len(files)})
    except Exception as e: check("the fixture is unchanged and still read-only", False, str(e))
    return finish(R, g, f"tree-b-fixture-{label}.json")


def roundtrip(label, new_package, old_package):
    case_dir, R, step, check = harness(f"tree-b-roundtrip-{label}")
    new, old = Path(new_package), Path(old_package); R["packages"] = {"new": sha(new), "1.5.1": sha(old)}
    g = None; directory = None
    def session(name, pkg):
        game = m0.game(name, case_dir, [pkg]); R["sessions"].append({"name": name, **game.engine_info()}); to_menu(game); return game
    def shoot(game, name):
        before = game.func(SLOTS4); ray = game.func(RAY0); game.cmd(f"DIG_RAY {ray} 6"); time.sleep(2.5); after = game.func(SLOTS4)
        step(f"{name}: shot", {"before": before, "after": after}); return after
    def close(game):
        lines = list(game.lines); game.close(); return lines
    try:
        # 1  1.5.1: a fresh world, AK-47 #1 (30) and AWP #2 (5)
        g = session("s1-151", old); m0.enter_world(g); g.func(KEEP_ACTIVE); g.func(GUI_IN_SHOTS)
        step("camera", poll(g, CAMERA_READY, lambda v: v == "ready", 120)); time.sleep(2)
        directory, system_dir = step("world", g.func(WORLD)).split("|", 1)
        step("1.5.1 makes AK-47", g.func(ADD(0, "ak47", 30))); step("1.5.1 makes AWP", g.func(ADD(1, "awp", 5)))
        s1 = step("s1 slots", g.func(SLOTS4)); step("save", g.func(SAVE)); time.sleep(2); close(g); g = None
        # 2  new: the tree is founded from it; a shot; the new build makes an M4A1-S
        g = session("s2-new", new); m = g.mark(); step("load", g.func(load(directory))); hit = entered(g, step, m)
        check("2 (new): the 1.5.1 world loads", hit == 'Entered screen "Game"', hit)
        s2 = shoot(g, "2 (new)"); step("new makes M4A1-S", g.func(ADD(2, "m4a1s", 20))); s2 = step("s2 slots", g.func(SLOTS4))
        step("save", g.func(SAVE)); time.sleep(2); l2 = close(g); g = None
        tree_file = Path(system_dir) / "ScCsgoGuns.xml"
        check("2 (new): the tree file exists", tree_file.exists(), str(tree_file))
        # 3  1.5.1 again: reads the full copy in Project.xml; a shot; it makes a SG 553
        g = session("s3-151", old); m = g.mark(); step("load", g.func(load(directory))); hit = entered(g, step, m)
        check("3 (1.5.1): the world saved by the new build loads", hit == 'Entered screen "Game"', hit)
        s3a = step("s3 slots on load", g.func(SLOTS4))
        check("3 (1.5.1): it sees the new build's state", s3a == s2, {"s2": s2, "s3": s3a})
        s3 = shoot(g, "3 (1.5.1)"); step("1.5.1 makes SG 553", g.func(ADD(3, "sg556", 30))); s3 = step("s3 slots", g.func(SLOTS4))
        step("save", g.func(SAVE)); time.sleep(2); close(g); g = None
        # 4  new again: reconciled by receipt, nothing lost
        g = session("s4-new", new); m = g.mark(); step("load", g.func(load(directory))); hit = entered(g, step, m)
        lines4 = step("s4 tree lines", [l for l in g.lines[m:] if "[GUN_TREE]" in l or "refused" in l or "型号冲突" in l])
        s4 = step("s4 slots", g.func(SLOTS4))
        check("4 (new): the world 1.5.1 saved loads", hit == 'Entered screen "Game"', hit)
        check("4 (new): every gun and round as 1.5.1 left them", s4 == s3, {"s3": s3, "s4": s4})
        check("4 (new): reconciled by its receipt", any("older build" in l for l in lines4), lines4)
        R["gameErrors"] = g.errors()[:40]
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    return finish(R, g, f"tree-b-roundtrip-{label}.json")


if __name__ == "__main__":
    kind = sys.argv[1]
    sys.exit(fixture(*sys.argv[2:7]) if kind == "fixture" else roundtrip(*sys.argv[2:5]))
