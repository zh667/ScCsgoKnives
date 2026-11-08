"""world-tree-gun-registry-20261107 (1.5.2) on the isolated standalone engine (M0_ENGINE=1951 by default here).

fixture    A COPY of the read-only fixture made by sp_portal_swap.py (the reproduced Portal swap: World1 #1 AWP/4, #2 M4A1-S/18,
           #3 AK-47/25; items AK-47 on #1 and AWP on #3; Space_0 with its own registry; a Ghoul world) is put into the runtime
           and loaded with the new build: the world loads, the AK-47 is #3 with 25 rounds and the AWP #1 with 4, the M4A1-S
           #2 with 18; Space_0 joins (one registry); Ghoul's travel with those guns loads its world; save and load again.
           The merge notice: not shown while Ghoul's difficulty dialog covers the game; shown once it is gone, and still
           on screen about ten seconds later (frames copied to .tmp/dev-temp/gun-tree/frames).
roundtrip  One fresh tree switched 1.5.1 -> new -> 1.5.1 -> new: each session loads without refusal; what the older build
           changed (a shot, a gun it made) is in the tree afterwards, what the new build made earlier is still there.
late       A Portal space that gets records of its own after the tree exists (decision 2026-11-08 cases 3 and 5): the new
           build founds the tree (World: AWP #1, AK-47 #2, M4A1-S #3 in slots 1, 2, 0); 1.5.1 carries them into Space_0
           (its arrival import numbers them M4 1, AWP 2, AK 3), drops the AK-47 there and throws the M4A1-S up, saves
           while it flies; the new build loads: the space merges late into the shared registry, the AWP in the inventory,
           the dropped AK-47 and the M4A1-S in flight all carry their tree numbers (#1, #2, #3) with matching records.
Usage: sp_tree_b.py fixture <label> <new cs package> <portal> <ghoul> <fixture dir>
       sp_tree_b.py roundtrip <label> <new cs package> <delivered 1.5.1 package>
       sp_tree_b.py late <label> <new cs package> <delivered 1.5.1 package> <portal>
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
from sp_tree import F
# The record notice on screen: the open dialogs (DialogsManager.m_dialogs) and the small messages the player sees.
NOTICE = MAIN + F + (
    'var dialogs = Game.DialogsManager.m_dialogs; var w = pl.ComponentGui.m_messageWidget; var list = w.GetType().GetField("m_messages").GetValue(w); '
    'int n = (int)list.GetType().GetProperty("Count").GetValue(list); var texts = new System.Collections.Generic.List<string>(); '
    'for (int i = 0; i < n; i++) { var m = list.GetType().GetProperty("Item").GetValue(list, new object[] { i }); var lw = m.GetType().GetField("LabelWidget").GetValue(m); texts.Add((string)lw.GetType().GetProperty("Text").GetValue(lw)); } '
    'return System.Text.Json.JsonSerializer.Serialize(new { dialogs = dialogs.Count, kinds = dialogs.Select(d => d.GetType().FullName).ToArray(), notice = texts.Any(t => t != null && t.StartsWith("CS：")), texts });')
# Closed as a player would: CS's own start-equipment dialog gets its first choice ("no equipment"; it reappears until a
# choice is made), every other dialog is closed.
HIDE_DIALOGS = MAIN + ('var all = Game.DialogsManager.m_dialogs.ToArray(); string chose = ""; '
                'if (all.Any(d => d is Game.ScStarterDialog)) { var se = pl.Entity.Project.FindSubsystem<Game.SubsystemScStarterEquipment>(false); '
                '  if (se != null) chose = " chose " + System.Enum.GetValues<Game.ScStarterPlan>()[0] + "=" + se.Choose(pl, System.Enum.GetValues<Game.ScStarterPlan>()[0]); } '
                'foreach (var d in all) Game.DialogsManager.HideDialog(d); '
                'return "hidden " + all.Length + ": " + string.Join(",", all.Select(d => d.GetType().FullName)) + chose;')
SETUP3 = MAIN + (   # World: #1 AWP, #2 AK-47, #3 M4A1-S; slots 0 M4A1-S, 1 AWP, 2 AK-47
    'var inv = pl.ComponentMiner.Inventory; for (int i = 0; i < inv.SlotsCount; i++) inv.RemoveSlotItems(i, inv.GetSlotCount(i)); '
    'int gb = Game.BlocksManager.GetBlockIndex<Game.ScGunBlock>(true); var reg = Game.ScGunRegistry.Current; int V(string n) => System.Array.FindIndex(Game.GunSpec.All, s => s.Name == n); '
    'int awp = V("awp"), ak = V("ak47"), m4 = V("m4a1s"); int a = reg.Allocate(awp, 4, false, Game.ScGunDurability.Full(awp)), b = reg.Allocate(ak, 25, false, Game.ScGunDurability.Full(ak)), c = reg.Allocate(m4, 18, false, Game.ScGunDurability.Full(m4)); '
    'inv.AddSlotItems(0, Game.Terrain.MakeBlockValue(gb, 0, Game.GunSpec.WithId(m4, c)), 1); inv.AddSlotItems(1, Game.Terrain.MakeBlockValue(gb, 0, Game.GunSpec.WithId(awp, a)), 1); '
    'inv.AddSlotItems(2, Game.Terrain.MakeBlockValue(gb, 0, Game.GunSpec.WithId(ak, b)), 1); inv.ActiveSlotIndex = 1; return a + "," + b + "," + c;')
DROP_AND_THROW = MAIN + (   # in the player's scene, 6 m away (out of pick-up reach): slot 2 dropped, slot 0 thrown straight up
    'var inv = pl.ComponentMiner.Inventory; var scene = pl.Entity.Project; int dropped = inv.GetSlotValue(2), thrown = inv.GetSlotValue(0); '
    'inv.RemoveSlotItems(2, 1); inv.RemoveSlotItems(0, 1); var at = pl.ComponentBody.Position + new Engine.Vector3(6f, 0.5f, 0f); '
    'var p = scene.FindSubsystem<Game.SubsystemPickables>(true).AddPickable(dropped, 1, at, Engine.Vector3.Zero, null); '
    'var f = scene.FindSubsystem<Game.SubsystemProjectiles>(true).FireProjectile(thrown, pl.ComponentBody.Position + new Engine.Vector3(0f, 3f, 6f), new Engine.Vector3(0f, 40f, 0f), Engine.Vector3.Zero, null); '
    'return (p != null) + "|" + (f != null) + "|" + dropped + "|" + thrown;')
# Gun items lying in or flying through the player's scene, and the inventory's slot 1, each with the model the shared
# registry has for its number.
WORLD_GUNS = MAIN + (
    'int gb = Game.BlocksManager.GetBlockIndex<Game.ScGunBlock>(true); var reg = Game.ScGunRegistry.Current; var scene = pl.Entity.Project; '
    'string D(int v) { int d = Game.Terrain.ExtractData(v); int id = Game.GunSpec.GetId(d); return Game.GunSpec.All[System.Math.Max(0, Game.GunSpec.GetVariant(d))].Name + "#" + id + "=" + (reg != null && reg.TryGetSnapshot(id, out var s) ? Game.GunSpec.All[s.Variant].Name : "-"); } '
    'var items = scene.FindSubsystem<Game.SubsystemPickables>(true).Pickables.Where(x => Game.Terrain.ExtractContents(x.Value) == gb).Select(x => "dropped:" + D(x.Value)) '
    '  .Concat(scene.FindSubsystem<Game.SubsystemProjectiles>(true).Projectiles.Where(x => Game.Terrain.ExtractContents(x.Value) == gb).Select(x => "flying:" + D(x.Value))) '
    '  .Concat(new[] { "slot1:" + D(pl.ComponentMiner.Inventory.GetSlotValue(1)) }); '
    'return scene.FindSubsystem<Game.SubsystemGameInfo>(true).DirectoryName + "|" + string.Join(";", items);')
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


FRAMES = m0.ROOT / ".tmp/dev-temp/gun-tree/frames"
def keep_frames(R):
    FRAMES.mkdir(parents=True, exist_ok=True); kept = []
    for f in R.get("frames", []):
        src = Path(f)
        if not src.is_file(): continue
        head = src.read_bytes()[:4]; dst = FRAMES / (src.name + (".png" if head.startswith(b"\x89PNG") else ".jpg"))
        shutil.copyfile(src, dst); kept.append(str(dst))
    R["framesKept"] = kept


def finish(R, g, out_name):
    if g is not None:
        try: g.close()
        except Exception: pass
    try: keep_frames(R)
    except Exception as e: R["framesKept"] = f"not copied: {e}"
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
        def frame(name):
            try: shot = snap(g, f"tree-b-fixture-{label}-{len(R['frames']):02d}-{name}", ((0, 0, 0), (1, 1, 1)))[0]; R["frames"].append(shot); step("frame " + name, shot)
            except Exception as e: step("frame failed " + name, str(e))
        # The notice waits while a dialog (Ghoul's difficulty choice) covers the game, then stays up long enough to read.
        time.sleep(4)
        covered = step("notice while the dialog is open", json.loads(g.func(NOTICE)))
        check("notice not shown while a dialog covers the game", covered["dialogs"] > 0 and not covered["notice"], covered)
        R["noticeWhileCovered"] = []
        frame("dialog-open")
        # Dialogs come one after another (the game's bulletin, Ghoul's options, then CS's own starter dialog): closed as a
        # player would, until none has appeared for two seconds. The notice must still be waiting until then.
        m = g.mark(); closed = []
        for _ in range(12):
            closed.append(g.func(HIDE_DIALOGS)); time.sleep(2)
            now = json.loads(g.func(NOTICE))
            if now["dialogs"] == 0: break
            R["noticeWhileCovered"].append({"dialogs": now["kinds"], "notice": now["notice"]})
        step("dialogs closed", closed)
        check("notice still waiting while later dialogs covered the game", not any(x["notice"] for x in R["noticeWhileCovered"]), R["noticeWhileCovered"])
        time.sleep(3)
        seen = step("notice after the dialogs closed", json.loads(g.func(NOTICE)))
        check("notice shown once nothing covers the game", seen["dialogs"] == 0 and seen["notice"], seen)
        frame("notice-shown")
        time.sleep(8)
        later = step("notice 8 s later", json.loads(g.func(NOTICE)))
        check("notice still on screen 8 s later", later["notice"], later)
        frame("notice-8s-later")
        try: g.wait(["record notice read"], 30, m); read = True
        except Exception: read = False
        check("notice counted as read (visible 10 s without interruption)", read, [l for l in g.lines[m:] if "record notice" in l])
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


def late(label, new_package, old_package, portal_package):
    case_dir, R, step, check = harness(f"tree-b-late-{label}")
    new, old, portal = Path(new_package), Path(old_package), Path(portal_package)
    R["packages"] = {"new": sha(new), "1.5.1": sha(old), "portal": sha(portal)}
    g = None
    def session(name, pkg):
        game = m0.game(name, case_dir, [pkg, portal]); R["sessions"].append({"name": name, **game.engine_info()}); to_menu(game); return game
    def move(game, name):
        result = step(name, game.func(MOVE)); game.func(KEEP_ACTIVE)
        if result.startswith(("unsupported", "no portal", "not allowed")): raise RuntimeError("portal switch: " + result)
        step("camera", poll(game, CAMERA_READY, lambda v: v == "ready", 120)); time.sleep(2); return result
    try:
        # 1  new build: a fresh world founds the tree
        g = session("s1-new", new); m0.enter_world(g); g.func(KEEP_ACTIVE); g.func(GUI_IN_SHOTS)
        step("camera", poll(g, CAMERA_READY, lambda v: v == "ready", 120)); time.sleep(2)
        directory, system_dir = step("world", g.func(WORLD)).split("|", 1)
        check("1 (new): records #1 AWP, #2 AK-47, #3 M4A1-S", step("records", g.func(SETUP3)) == "1,2,3")
        step("save", g.func(SAVE)); time.sleep(2); g.close(); g = None
        check("1 (new): the tree file exists", (Path(system_dir) / "ScCsgoGuns.xml").exists())
        # 2  1.5.1: into the own space (its arrival import gives the space numbers of its own), drop and throw, save in flight
        g = session("s2-151", old); m = g.mark(); step("load", g.func(load(directory))); hit = entered(g, step, m)
        check("2 (1.5.1): the world loads", hit == 'Entered screen "Game"', hit)
        # 1.5.1's arrival import in a Portal space reads the departure its root wrote on a save (as in the reproduced swap)
        step("save before the trip", g.func(SAVE)); time.sleep(2)
        m = g.mark(); move(g, "2: into the own space")
        try: g.wait(["[GUN_TRAVEL] arrival from"], 90, m)
        except Exception as e: step("no arrival line", str(e))
        time.sleep(3)
        before = step("2: guns in the space", g.func(WORLD_GUNS)); step("2: slots", g.func(SLOTS4))
        step("2: drop slot 2, throw slot 0", g.func(DROP_AND_THROW)); time.sleep(0.5)
        flying = step("2: in the space now", g.func(WORLD_GUNS))
        step("save", g.func(SAVE)); time.sleep(1)
        check("2 (1.5.1): the space numbered the carried guns itself (M4 1, AWP 2, AK 3)", "slot1:awp#2=awp" in before, before)
        check("2 (1.5.1): the space has a dropped and a flying gun at save", "dropped:" in flying and "flying:" in flying, flying)
        g.close(); g = None
        space_xml = Path(system_dir) / "Space_0" / "SpaceProject.xml"
        R["spaceSaved"] = {"exists": space_xml.exists(), "stamped": space_xml.exists() and "GunTreeId" in space_xml.read_text("utf-8", "replace")}
        check("2 (1.5.1): Space_0 saved with a registry of its own, unstamped", R["spaceSaved"] == {"exists": True, "stamped": False}, R["spaceSaved"])
        # 3  new build: the space merges late; every holder there carries its tree number
        g = session("s3-new", new); m = g.mark(); step("load", g.func(load(directory))); hit = entered(g, step, m)
        check("3 (new): the world loads", hit == 'Entered screen "Game"', hit)
        where = step("3: player's scene", g.func(WORLD_GUNS))
        if not where.split("|", 1)[0].endswith("/Space_0"): move(g, "3: into the own space"); where = step("3: player's scene", g.func(WORLD_GUNS))
        time.sleep(2)
        tree_lines = step("3: tree lines", [l for l in g.lines[m:] if "[GUN_TREE]" in l or "refused" in l or "型号冲突" in l])
        now = step("3: guns in the space", g.func(WORLD_GUNS))
        items = now.split("|", 1)[1].split(";") if "|" in now else []
        check("3 (new): the space merged late (at the root's load or its own), not refused",
              any("did not take in; merged now" in l or "never took in (Space_0)" in l for l in tree_lines) and not any("refused" in l for l in tree_lines), tree_lines)
        check("3 (new): the player's AWP saved in the space was moved in place", any("1 inventory item(s)" in l for l in tree_lines), tree_lines)
        check("3 (new): inventory AWP on #1 with the AWP record", "slot1:awp#1=awp" in items, items)
        check("3 (new): dropped AK-47 on #2 with the AK-47 record", "dropped:ak47#2=ak47" in items, items)
        check("3 (new): M4A1-S thrown before the save on #3 with the M4A1-S record (still flying, or landed)", any(i in items for i in ("flying:m4a1s#3=m4a1s", "dropped:m4a1s#3=m4a1s")), items)
        check("3 (new): the remap rewrote the dropped and the flying item in place", any("1 dropped item(s) and 1 item(s) in flight moved" in l for l in tree_lines), tree_lines)
        m = g.mark(); g.cmd(RELOAD); hit = entered(g, step, m)
        if not step("3: after reload", g.func(WORLD_GUNS)).split("|", 1)[0].endswith("/Space_0"): move(g, "3: into the own space again")
        again = step("3: guns after save and load", g.func(WORLD_GUNS)); items2 = again.split("|", 1)[1].split(";") if "|" in again else []
        check("3 (new): after save and load every gun matches its record, no conflict",
              all(i.split(":", 1)[1].split("#")[0] == i.split("=", 1)[1] for i in items2 if "#" in i) and not any("型号冲突" in l for l in g.lines[m:]), items2)
        R["gameErrors"] = g.errors()[:40]
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    return finish(R, g, f"tree-b-late-{label}.json")


if __name__ == "__main__":
    kind = sys.argv[1]
    sys.exit(fixture(*sys.argv[2:7]) if kind == "fixture" else roundtrip(*sys.argv[2:5]) if kind == "roundtrip" else late(*sys.argv[2:6]))
