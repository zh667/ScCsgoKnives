"""Smoke look, before/after (2026-10-01 user feedback: the round-4 smoke changed shape and fill; the earlier one looked good).

The same scene and the same six views for each package given, one game session per package, on the engine M0_ENGINE
selects (standalone: 131): first person 8 m and 5 m off-axis, third person 8 m, and a debug camera beside, above and at 45°
while the character stands 8 m from the cloud. Writes one labelled side-by-side sheet (row = view, column = package) and
the raw frames into the run folder. A short run (~2 min a package): for looking, not for occlusion numbers (sp_smoke_view).

Usage: sp_smoke_look.py <label> <package path | output-lite | output-full> [<package> ...]
"""
import json, math, sys, time
from pathlib import Path

import m0
from m0 import RESULTS, RUNS, sha, to_menu
from mp_m1 import PROJECT, MAIN, KEEP_ACTIVE
from sp_smoke_view import ARENA, smoke, smoke_state, camera, snap
from sp_smoke_motion import stand, DAY_LOCK

def debug_view(x, y, z, tx, ty, tz): return m0.Call(MAIN + (
    'var w = pl.GameWidget; var d = w.FindCamera<Game.DebugCamera>(false); if (d == null) return "no DebugCamera"; w.ActiveCamera = d; '
    'd.m_position = new Engine.Vector3($f0, $f1, $f2); d.m_direction = Engine.Vector3.Normalize(new Engine.Vector3($f3, $f4, $f5) - d.m_position); '
    'd.SetupPerspectiveCamera(d.m_position, d.m_direction, Engine.Vector3.UnitY); return w.ActiveCamera.GetType().Name;'), x, y, z, tx, ty, tz)
WEATHER_OFF = PROJECT + ('var w = project.FindSubsystem<Game.SubsystemWeather>(true); project.FindSubsystem<Game.SubsystemGameInfo>(true).WorldSettings.AreWeatherEffectsEnabled = false; '
    'w.ManualPrecipitationEnd(); w.PrecipitationIntensity = 0f; return "clear";')


def session(pkg, label, case_dir, R):
    shots = []
    def step(name, value):
        R["steps"].append({"package": pkg.name, "step": name, "value": value}); print(f"[{pkg.name}] {name}: {value}", flush=True); return value
    g = m0.game("server", case_dir, [pkg]); info = g.engine_info()
    try:
        to_menu(g)
        g.cmd('EXEC SettingsManager.ShowGuiInScreenshots = false; SettingsManager.ShowLogoInScreenshots = false; SettingsManager.SaveSettings();')
        m0.enter_world(g)
        step("window kept active", g.func(KEEP_ACTIVE)); step("day locked", g.func(DAY_LOCK)); step("weather off", g.func(WEATHER_OFF))
        g.func(MAIN + 'pl.ComponentLocomotion.IsCreativeFlyEnabled = true; pl.ComponentBody.IsGravityEnabled = false; var p = players.GlobalSpawnPosition + new Engine.Vector3(20f, 12f, 22f); pl.ComponentBody.Position = p; return "ok";')
        time.sleep(4)
        t0 = time.time(); a = g.func(ARENA)
        while a.startswith("wait") and time.time() - t0 < 90: time.sleep(1); a = g.func(ARENA)
        x, y, z = [int(v) for v in step("arena", a).split()]
        step("smoke placed", g.func(smoke(x, y, z))); g.func(smoke_state(2.0, 12.0, True, False))
        C = (x + .5, y, z + .5); aim = (C[0], y + 1.5, C[2]); box = ((x, y, z + 7), (x + 1, y + 3, z + 8)); e = y + 1.62
        views = [("first person 8 m", lambda: g.func(camera((C[0], e, C[2] - 8), aim, 0, 0, 1.0, False))),
                 ("first person 5 m, off-axis", lambda: g.func(camera((C[0] + 3, e, C[2] - 4), aim, 0, 0, 1.0, False))),
                 ("third person 8 m", lambda: g.func(camera((C[0], e, C[2] - 8), aim, 0, 0, 1.0, True)))]
        for i, (name, place) in enumerate(views):
            place(); time.sleep(.8); path, _ = snap(g, f"look-{label}-{len(R['sessions'])}-{i}.png", box); shots.append((name, path))
        step("character 8 m from the cloud", g.func(stand(C[0], e, C[2] - 8, C[0], y + 1, C[2])))
        for i, (name, cam) in enumerate([("debug camera beside (90°)", (C[0] + 9, e + .5, C[2])),
                                         ("debug camera at 45°, raised", (C[0] + 6, y + 5, C[2] - 6)),
                                         ("debug camera above", (C[0] + .3, y + 12, C[2] - .3))], start=len(views)):
            step(name, g.func(debug_view(*cam, *aim))); time.sleep(.8); path, _ = snap(g, f"look-{label}-{len(R['sessions'])}-{i}.png", box); shots.append((name, path))
        R["gameErrors"] = R.get("gameErrors", []) + g.errors()
    finally:
        g.close()
    return {"package": pkg.name, "sha256": sha(pkg), "engine": info, "shots": shots}


def main(label, *packages):
    pkgs = [{"output-lite": m0.LITE, "output-full": m0.FULL}.get(p) or Path(p) for p in packages]
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"smoke-look-{label}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"smoke-look-{label}", "steps": [], "sessions": []}
    try:
        for pkg in pkgs: R["sessions"].append(session(pkg, label, case_dir, R))
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    try:
        from PIL import Image, ImageDraw
        cols = len(R["sessions"]); rows = max((len(s["shots"]) for s in R["sessions"]), default=0); W = 640
        cells = {}
        for c, s in enumerate(R["sessions"]):
            for r, (name, path) in enumerate(s["shots"]):
                src = Path(path)
                if not src.exists(): continue
                im = Image.open(src).convert("RGB"); src.replace(case_dir / src.name); R.setdefault("frames", []).append(str(case_dir / src.name))
                im = im.resize((W, int(W * im.height / im.width))); d = ImageDraw.Draw(im); d.rectangle([0, 0, W, 18], fill=(0, 0, 0))
                d.text((4, 3), f"{name} | {s['package'][:48]} {s['sha256'][:8]}", fill=(255, 255, 0)); cells[(r, c)] = im
        if cells:
            h = next(iter(cells.values())).height; sheet = Image.new("RGB", (cols * W, rows * h), "black")
            for (r, c), im in cells.items(): sheet.paste(im, (c * W, r * h))
            sheet.save(case_dir / "look-sheet.jpg", quality=85); R["sheet"] = str(case_dir / "look-sheet.jpg")
    except Exception as e:
        R["sheetFailure"] = f"{type(e).__name__}: {e}"
    m0.RUNTIME_OWNER.release()
    out = RESULTS / f"smoke-look-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    ok = not R.get("failure") and "sheet" in R
    print(json.dumps({"case": R["case"], "failure": R.get("failure"), "sheet": R.get("sheet"), "errors": R.get("gameErrors", [])[:5]}, ensure_ascii=False, indent=1))
    print(f"report {out} case {case_dir}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], *sys.argv[2:]))
