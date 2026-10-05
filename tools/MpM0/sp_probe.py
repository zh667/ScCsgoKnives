"""Harness probe on the selected engine (M0_ENGINE: mp / 131): the cheapest end-to-end check that the automation mod, its
log and command ports, world entry, FUNC against the loaded packages, input injection and the screenshot path work on
this engine, run BEFORE the long single-player scenarios (a failed prerequisite stops them instead of wasting an hour).

Usage: sp_probe.py <package path | output-lite | output-full> <label>
"""
import json, shutil, sys, time
from pathlib import Path

import m0
from m0 import RESULTS, RUNS, sha, to_menu
from mp_m1 import MAIN, KEEP_ACTIVE, EYE

ENGINE_INFO = ('return System.Reflection.Assembly.GetAssembly(typeof(Game.GameManager)).GetName().Version.ToString() + " | " + '
               'string.Join(", ", ModsManager.ModList.Select(m => m.modInfo.PackageName + " " + m.modInfo.Version));')
KEY_F = 'return Engine.Input.Keyboard.IsKeyDown(Engine.Input.Key.F).ToString();'
SHOT = MAIN + ('var size = Engine.Window.Size; Game.ScreenCaptureManager.Capture(size.X, size.Y, "probe.png"); '
               'return Engine.Storage.GetSystemPath(Engine.Storage.CombinePaths(Game.ScreenCaptureManager.ScreenshotDir, "probe.png"));')


def main(package, label):
    pkg = {"output-lite": m0.LITE, "output-full": m0.FULL}.get(package) or Path(package)
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"probe-{label}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"probe-{label}", "package": {pkg.name: sha(pkg)}, "steps": [], "checks": []}
    T0 = time.time()
    def step(name, value):
        R["steps"].append({"t": round(time.time() - T0, 1), "step": name, "value": value}); print(f"[{round(time.time() - T0, 1)}] {name}: {value}", flush=True); return value
    def check(name, ok, detail=""):
        R["checks"].append({"check": name, "ok": bool(ok), "detail": detail}); print(("PASS " if ok else "FAIL ") + name + " :: " + str(detail), flush=True)
    g = None
    try:
        g = m0.game("server", case_dir, [pkg]); R["engine"] = g.engine_info(); step("engine folder", R["engine"]["folder"]); to_menu(g)
        info = step("engine and mods (FUNC)", g.func(ENGINE_INFO))
        check("FUNC answers and the package under test is loaded", "zh667.ScCsgoKnives" in info, info)
        m0.enter_world(g)
        step("window kept active", g.func(KEEP_ACTIVE))
        eye = step("eye", g.func(EYE))
        def floats(v):
            try: return len([float(x) for x in v.split()]) == 3
            except ValueError: return False
        check("the player is in the world (eye position readable)", floats(eye), eye)
        g.cmd("KEYDOWN F"); time.sleep(.1); down = g.func(KEY_F); g.cmd("KEYUP F"); time.sleep(.1); up = g.func(KEY_F)
        check("key injection reaches the engine (down, then up)", down == "True" and up == "False", f"{down}/{up}")
        path = step("screenshot", g.func(SHOT)); time.sleep(1.5); p = Path(path)
        check("screenshot written", p.exists() and p.stat().st_size > 1000, f"{path} {p.stat().st_size if p.exists() else 'missing'} bytes")
        if p.exists(): shutil.copy2(p, case_dir / "probe.png")
        R["gameErrors"] = g.errors()
        check("no engine errors during the probe", not R["gameErrors"], R["gameErrors"][:3])
        # Packages with the 2026-10-01 resource residency: its install lines on this engine (no package kept as loaded).
        with g.lock: mem = [l for l in g.lines if "[CS_MEM] residency" in l]
        if mem:
            R["residency"] = mem
            check("resource residency installed on this engine (members read from the package file, none kept as loaded)",
                  any("now read from" in l for l in mem) and not any("kept as loaded" in l for l in mem), [l[-200:] for l in mem])
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None:
            R.setdefault("gameErrors", g.errors()); g.close()
        m0.RUNTIME_OWNER.release()
    failed = [c["check"] for c in R["checks"] if not c["ok"]]
    out = RESULTS / f"probe-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print(f"{len(R['checks'])} checks, {len(failed)} failed; errors {len(R.get('gameErrors', []))}; engine {R.get('engine', {}).get('engine')}; {out}", flush=True)
    return 1 if failed or R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2]))
