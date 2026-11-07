"""Reproduces the player's 2026-10-31 swap (D:/下载/Game (4).log) with the DELIVERED 1.5.1 Full package, Portal 1.5.0
and Ghoul 2.0.1 on the isolated standalone engine (M0_ENGINE=1951, the player's 1.9.5.1; 131 if that cannot drive it).

What the log shows: World1 held numbered guns; Portal created Space_0 and ran it BESIDE World1; CS's arrival check in
Space_0 imported the carried guns with new local numbers ("3 carried gun(s) imported (3 new number(s)) ... 2 slot(s)
given local numbers"); the player walked back to World1 without a reload, the items kept Space_0's numbers, and from the
next load World1 reported "#1 型号冲突：物品为AK-47，记录为AWP" and "#3 型号冲突：物品为AWP，记录为AK-47". Travelling with
Ghoul while carrying such a gun then failed the destination ("跨世界携带快照不兼容或未就绪：携带枪械 #3 的记录缺…").

Arrangement: World1's records #1 AWP, #2 M4A1-S, #3 AK-47 (allocated in that order); slots 0 AK-47 (#3), 1 M4A1-S (#2),
2 AWP (#1). The arrival import numbers the carried guns in slot order 1, 2, 3, so AK-47 -> 1 and AWP -> 3 (M4A1-S keeps 2:
two slots rewritten) - back in World1, #1 and #3 point at each other's model.
Steps: 1 new survival world, the three guns, one shot, save. 2 into the own space (Portal's MoveSpacePlayer), wait for the
arrival import, record slots and both registries; back to World1 live; save, leave, load again; capture GUN_INTEGRITY.
3 Ghoul's TransPortal while carrying the swapped guns; capture the refusal. Then the world folder (World, Space_0,
Tartareosity) is copied once into <fixtures> with a manifest; the copy is made read-only.
Usage: sp_portal_swap.py <label> <cs package> <portal package> <ghoul package> <fixture dir>   (ASCII paths)
"""
import hashlib, json, os, shutil, stat, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))

import sp_tree   # noqa: F401  (also applies sp_uninstall's standalone/worktree harness adaptations)
import m0
from m0 import RESULTS, RUNS, sha, to_menu, poll
from mp_m1 import MAIN, KEEP_ACTIVE
from sp_deathmatch import GUI_IN_SHOTS
from sp_tree import MOVE, CAMERA_READY, F
from sp_subworld import GHOUL_TRAVEL
from sp_smoke_view import snap

SETUP = MAIN + (
    'var inv = pl.ComponentMiner.Inventory; for (int i = 0; i < inv.SlotsCount; i++) inv.RemoveSlotItems(i, inv.GetSlotCount(i)); '
    'int gb = Game.BlocksManager.GetBlockIndex<Game.ScGunBlock>(true); var reg = Game.ScGunRegistry.Current; '
    'int V(string n) => System.Array.FindIndex(Game.GunSpec.All, s => s.Name == n); '
    'int awp = V("awp"), m4 = V("m4a1s"), ak = V("ak47"); '
    'int a = reg.Allocate(awp, 4, false, Game.ScGunDurability.Full(awp)); int b = reg.Allocate(m4, 18, false, Game.ScGunDurability.Full(m4)); int c = reg.Allocate(ak, 25, false, Game.ScGunDurability.Full(ak)); '
    'inv.AddSlotItems(0, Game.Terrain.MakeBlockValue(gb, 0, Game.GunSpec.WithId(ak, c)), 1); inv.AddSlotItems(1, Game.Terrain.MakeBlockValue(gb, 0, Game.GunSpec.WithId(m4, b)), 1); '
    'inv.AddSlotItems(2, Game.Terrain.MakeBlockValue(gb, 0, Game.GunSpec.WithId(awp, a)), 1); inv.ActiveSlotIndex = 0; '
    'return a + "," + b + "," + c;')
# Slots 0..2 (item model, number, rounds read through the current registry, the model that registry has for that number)
# and records 1..3 of the root's and the scene's own gun subsystem registries.
STATE = MAIN + F + (
    'var inv = pl.ComponentMiner.Inventory; var cur = Game.ScGunRegistry.Current; '
    'string Name(int v) => v >= 0 && v < Game.GunSpec.All.Length ? Game.GunSpec.All[v].Name : "?"; '
    'string Rec(object r, int n) { if (r == null) return "-"; var args = new object[] { n, null }; return (bool)r.GetType().GetMethod("TryGetSnapshot").Invoke(r, args) '
    '  ? Name((int)args[1].GetType().GetProperty("Variant").GetValue(args[1])) + "/" + args[1].GetType().GetProperty("Rounds").GetValue(args[1]) : "-"; } '
    'System.Func<GameEntitySystem.Project, object> Reg = p => p?.FindSubsystem<Game.SubsystemScGunBlockBehavior>(false) is { } g ? typeof(Game.SubsystemScGunBlockBehavior).GetField("m_registry", f).GetValue(g) : null; '
    'var scene = pl.Entity.Project; var rootReg = Reg(project); var sceneReg = Reg(scene); '
    'var slots = System.Linq.Enumerable.Range(0, 3).Select(i => { int v = inv.GetSlotValue(i); int d = Game.Terrain.ExtractData(v); int id = Game.GunSpec.GetId(d); '
    '  return i + ":" + (inv.GetSlotCount(i) == 0 ? "empty" : Name(Game.GunSpec.GetVariant(d)) + "#" + id + " rounds=" + Game.GunSpec.GetRounds(d) + " usable=" + Game.ScGunBlock.IsKnown(v) + " current[" + id + "]=" + Rec(cur, id)); }).ToArray(); '
    'return System.Text.Json.JsonSerializer.Serialize(new { scene = scene.FindSubsystem<Game.SubsystemGameInfo>(true).DirectoryName, slots, '
    '  root = string.Join(" ", System.Linq.Enumerable.Range(1, 3).Select(n => "#" + n + "=" + Rec(rootReg, n))), '
    '  sceneRegistry = object.ReferenceEquals(sceneReg, rootReg) ? "same as root" : string.Join(" ", System.Linq.Enumerable.Range(1, 3).Select(n => "#" + n + "=" + Rec(sceneReg, n))), '
    '  current = object.ReferenceEquals(cur, rootReg) ? "root" : object.ReferenceEquals(cur, sceneReg) ? "scene" : "other" });')
RAY0 = MAIN + ('pl.ComponentMiner.Inventory.ActiveSlotIndex = 0; var c = pl.GameWidget.ActiveCamera; var p = c.ViewPosition; var d = Engine.Vector3.Normalize(c.ViewDirection + new Engine.Vector3(0, 0.3f, 0)); '
               'return System.FormattableString.Invariant($"{p.X} {p.Y} {p.Z} {d.X} {d.Y} {d.Z}");')
WORLD = MAIN + 'var dir = project.FindSubsystem<Game.SubsystemGameInfo>(true).DirectoryName; return dir + "|" + Engine.Storage.GetSystemPath(dir);'
SAVE = 'Game.GameManager.SaveProject(true, true); return "saved";'
RELOAD = 'EXEC var info = Game.GameManager.WorldInfo; Game.GameManager.SaveProject(true, true); Game.GameManager.DisposeProject(); Game.ScreensManager.SwitchScreen("GameLoading", info, null);'


def main(label, cs_package, portal_package, ghoul_package, fixture_dir):
    pkgs = [Path(cs_package), Path(portal_package), Path(ghoul_package)]
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"portal-swap-{label}-{stamp}"; (case_dir / "frames").mkdir(parents=True)
    R = {"case": f"portal-swap-{label}", "engine": m0.SP_VERSION, "packages": {p.name: sha(p) for p in pkgs}, "steps": [], "checks": [], "frames": []}
    T0 = time.time(); g = None; system_dir = None
    def step(name, value=None):
        R["steps"].append({"t": round(time.time() - T0, 1), "step": name, "value": value}); print(f"[{round(time.time() - T0, 1)}] {name}: {str(value)[:900]}", flush=True); return value
    def check(name, ok, detail=""):
        R["checks"].append({"check": name, "ok": bool(ok), "detail": detail}); print(("PASS " if ok else "FAIL ") + name + " :: " + str(detail)[:900], flush=True); return ok
    def state(name): return step(name, json.loads(g.func(STATE)))
    def lines(since, *keys): return [l for l in g.lines[since:] if any(k in l for k in keys)]
    def frame(name):
        try: shot = snap(g, f"portal-swap-{label}-{len(R['frames']):02d}-{name}", ((0, 0, 0), (1, 1, 1)))[0]; R["frames"].append(shot); step("frame " + name, shot)
        except Exception as e: step("frame failed " + name, str(e))
    def move(name):
        result = step(name, g.func(MOVE)); g.func(KEEP_ACTIVE)
        if result.startswith(("unsupported", "no portal", "not allowed")): raise RuntimeError("portal switch: " + result)
        step("camera with the player", poll(g, CAMERA_READY, lambda v: v == "ready", 120)); time.sleep(2); return result
    keys = ("[GUN_TRAVEL]", "[GUN_INTEGRITY]", "型号冲突", "gun load refused", "gun format load refused", "Loading World Failed", "unusable gun data", "gun registry:", "跨世界")
    try:
        g = m0.game("swap", case_dir, pkgs); R["engine_info"] = g.engine_info(); to_menu(g)
        # ---- 1: World1 with three numbered guns arranged to collide with the arrival's renumbering
        m0.enter_world(g); g.func(KEEP_ACTIVE); g.func(GUI_IN_SHOTS); time.sleep(3)
        directory, system_dir = step("world", g.func(WORLD)).split("|", 1)
        ids = step("records #AWP,#M4A1-S,#AK-47", g.func(SETUP))
        check("1: World1 records #1 AWP, #2 M4A1-S, #3 AK-47", ids == "1,2,3", ids)
        before = state("1: World1, before the trip")
        ray = g.func(RAY0); g.cmd(f"DIG_RAY {ray} 6"); time.sleep(2.5)
        fired = state("1: after one shot of the AK-47 in slot 0")
        step("save", g.func(SAVE)); time.sleep(2)

        # ---- 2: into Space_0 through Portal, wait for CS's arrival import, back to World1 live
        m = g.mark(); move("2: into the own space")
        try: g.wait(["[GUN_TRAVEL] arrival from"], 90, m)
        except Exception as e: step("no arrival line within 90 s", str(e))
        time.sleep(3)
        space = state("2: in Space_0 after the arrival check")
        step("2: CS lines in the space", lines(m, *keys))
        m = g.mark(); move("2: back to World1, no reload")
        home = state("2: World1 again (live)")
        frame("world1-live")
        step("save", g.func(SAVE)); time.sleep(2)
        m = g.mark(); g.cmd(RELOAD)
        hit = g.wait(['Entered screen "Game"', "Loading World Failed", "gun load refused"], 600, m); step("2: reload", hit)
        poll(g, m0.PLAYER_SPAWNED, lambda v: v == "True", 120); g.func(KEEP_ACTIVE); time.sleep(4)
        reloaded = state("2: after save, leave and load")
        integrity = step("2: GUN_INTEGRITY lines on the load", lines(m, "[GUN_INTEGRITY]", "型号冲突"))
        R["afterReloadLines"] = lines(m, *keys)
        swapped = [l for l in integrity if "型号冲突" in l]
        check("2: the load reports model conflicts (the player's pattern)", bool(swapped), integrity)
        frame("world1-after-reload")

        # ---- 3: Ghoul with the swapped guns carried
        if reloaded["scene"] != directory: step("back to World1 for Ghoul", (move("3: back to World1"), state("3: World1"))[1])
        step("save", g.func(SAVE)); time.sleep(2)
        m = g.mark(); step("3: Ghoul TransPortal", g.func(GHOUL_TRAVEL))
        hit = g.wait(['Entered screen "Game"', "Loading World Failed", "gun load refused"], 600, m); step("3: Ghoul destination", hit)
        time.sleep(3)
        ghoul = step("3: refusal lines", lines(m, "gun load refused", "Loading World Failed", "跨世界", "[GUN_TRAVEL]", "携带"))
        check("3: the Ghoul destination is refused for the carried snapshot", any("跨世界携带快照不兼容或未就绪" in l for l in ghoul), ghoul)
        frame("ghoul-refusal")
        R["gameErrors"] = g.errors()[:40]
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None:
            try: R["allCsLines"] = [l for l in g.lines if any(k in l for k in keys)][:200]; g.close()
            except Exception: pass
        # ---- the world folder as the run left it: an immutable fixture for phase B
        if system_dir and Path(system_dir).is_dir():
            dest = Path(fixture_dir); source = Path(system_dir)
            if dest.exists(): R["fixture"] = f"{dest} already exists; not overwritten"
            else:
                shutil.copytree(source, dest / source.name)
                files = sorted(p for p in dest.rglob("*") if p.is_file())
                manifest = {"source": str(source), "engine": R.get("engine_info", {}), "packages": R["packages"], "created": time.strftime("%Y-%m-%d %H:%M:%S"),
                            "files": {str(p.relative_to(dest)).replace("\\", "/"): {"bytes": p.stat().st_size, "sha256": hashlib.sha256(p.read_bytes()).hexdigest()} for p in files},
                            "steps": R["steps"], "checks": R["checks"]}
                (dest / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=1), "utf-8")
                for p in list(dest.rglob("*")) + [dest]:
                    if p.is_file(): os.chmod(p, stat.S_IREAD)
                R["fixture"] = {"path": str(dest), "files": len(files), "manifest": str(dest / "manifest.json")}
            step("fixture", R["fixture"])
        m0.RUNTIME_OWNER.release()
    RESULTS.mkdir(parents=True, exist_ok=True)
    out = RESULTS / f"portal-swap-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    failed = [c["check"] for c in R["checks"] if not c["ok"]]
    print(f"engine {R['engine']}; checks {len(R['checks']) - len(failed)}/{len(R['checks'])}; failure {R.get('failure')}; failed {failed}; {out}", flush=True)
    return 1 if R.get("failure") or failed else 0


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:6]))
