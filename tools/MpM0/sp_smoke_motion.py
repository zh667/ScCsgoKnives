"""Smoke seen by a moving viewer, single player, real game frames captured continuously (round 3 item 2).

Run (Windows): ./tools/dev.ps1 python tools/MpM0/sp_smoke_motion.py <package path | output-lite | output-full> <label>
Like sp_smoke_view.py: a cleared arena, one smoke at a fixed point with its age held every frame, a diamond column 9 m
behind it. The player (first person) is moved every frame along paths - strafe past, orbit, rise, approach through the
old 18/40 m distance bands and back, a fast pass, and in-place yaw/pitch turns - looking at the target, and every frame
is captured through the engine's screenshot path with the smoke and, for the same viewer state, without it and with
neither. Recorded per frame (UTC and frame index): the viewer position, the target's projected box and "seen" (the
fraction of the target's pixels the smoke left unchanged). Output: frames-*.gif per path (with smoke, 480 px wide, the
frame time stamped in), a contact sheet per path, and motion-<label>.json with the per-frame series. A stable cloud gives
a "seen" series that changes smoothly with the viewer's position and never with the frame alone; the reviewer looks at
the GIF for puffs turning or re-sizing. Pictures stay on Windows.
"""
import json, math, os, sys, time
from datetime import datetime, timezone
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import m0
from m0 import Game, MODS, RESULTS, RUNS, TA_BUILT, sha, to_menu, poll
from mp_m1 import PROJECT, MAIN, SURVIVAL, KEEP_ACTIVE
from sp_smoke_view import ARENA, smoke, smoke_state, camera, capture, snap

WEATHER_OFF = PROJECT + ('var w = project.FindSubsystem<Game.SubsystemWeather>(true); project.FindSubsystem<Game.SubsystemGameInfo>(true).WorldSettings.AreWeatherEffectsEnabled = false; '
    'w.ManualPrecipitationEnd(); w.PrecipitationIntensity = 0f; return "ok";')


# Round 4 (2026-10-01): the puffs face the viewing player's character, not the camera. Probes and free-camera helpers.
FLY_ADD = MAIN + 'var w = pl.GameWidget; if (w.FindCamera<Game.FlyCamera>(false) == null) w.AddCamera(new Game.FlyCamera(w), g => true); w.ActiveCamera = w.FindCamera<Game.FlyCamera>(); return w.ActiveCamera.GetType().Name;'
def fly_to(x, y, z, tx, ty, tz): return m0.Call(MAIN + ('var w = pl.GameWidget; var f = w.FindCamera<Game.FlyCamera>(); w.ActiveCamera = f; f.m_position = new Engine.Vector3($f0, $f1, $f2); '
    'f.m_direction = Engine.Vector3.Normalize(new Engine.Vector3($f3, $f4, $f5) - f.m_position); f.m_velocity = Engine.Vector3.Zero; f.SetupPerspectiveCamera(f.m_position, f.m_direction, Engine.Vector3.UnitY); return "ok";'), x, y, z, tx, ty, tz)
# The facing normal of one puff (the middle one of the cloud's list) as the renderer would draw it for this view: anchor = the
# view's character eye; camera position/direction recorded beside it.
FACING = MAIN + ('var s = (Game.ScGrenadeState)System.AppDomain.CurrentDomain.GetData("sc.smoke"); var cam = pl.GameWidget.ActiveCamera; var anchor = pl.ComponentCreatureModel.EyePosition; '
    'var sprites = Game.ScGrenadeVisuals.Smoke(s, 8f); var sp = sprites[sprites.Count / 2]; var q = Game.ScGrenadeVisuals.SmokeQuads(sp, s, anchor, cam.ViewRight); '
    'var wq = Game.ScGrenadeVisuals.SmokeQuadWeights(sp, s, anchor, cam.ViewRight, cam.ViewPosition); '
    'var n = Engine.Vector3.Normalize(Engine.Vector3.Cross(q[0].Item1, q[0].Item2)); var c = cam.ViewPosition; var d = cam.ViewDirection; '
    'return System.FormattableString.Invariant($"{anchor.X:0.00} {anchor.Y:0.00} {anchor.Z:0.00} | {c.X:0.00} {c.Y:0.00} {c.Z:0.00} | {d.X:0.000} {d.Y:0.000} {d.Z:0.000} | {sp.Position.X:0.00} {sp.Position.Y:0.00} {sp.Position.Z:0.00} | {n.X:0.0000} {n.Y:0.0000} {n.Z:0.0000} | {wq[0]:0.000} {wq[1]:0.000} {wq[2]:0.000}");')
def stand(x, y, z, tx, ty, tz): return m0.Call(MAIN + ('var body = pl.ComponentBody; pl.ComponentLocomotion.IsCreativeFlyEnabled = true; body.IsGravityEnabled = false; body.Velocity = Engine.Vector3.Zero; '
    'pl.ComponentCreatureModel.Update(0f); var off = pl.ComponentCreatureModel.EyePosition - body.Position; body.Position = new Engine.Vector3($f0, $f1, $f2) - off; '
    'var d = new Engine.Vector3($f3 - $f0, 0f, $f5 - $f2); body.Rotation = Engine.Quaternion.CreateFromAxisAngle(Engine.Vector3.UnitY, System.MathF.Atan2(-d.X, -d.Z)); pl.ComponentLocomotion.LookAngles = Engine.Vector2.Zero; '
    'pl.ComponentCreatureModel.Update(0f); var eye = pl.ComponentCreatureModel.EyePosition; return System.FormattableString.Invariant($"{eye.X:0.00} {eye.Y:0.00} {eye.Z:0.00}");'), x, y, z, tx, ty, tz)

# M0_SMOKE_MODE: "facing" (the fast acceptance run, ~5 min: only the camera-only / character-only / third-person facing paths,
# one capture per frame for the sheets and GIFs, facing normals and quad weights measured) or "full" (default: also the
# round-3 viewer paths and the with/without/neither captures behind the "seen" occlusion metric, ~27 min; final evidence).
MODE = os.environ.get("M0_SMOKE_MODE", "full")
DAY_LOCK = PROJECT + 'project.FindSubsystem<Game.SubsystemGameInfo>(true).WorldSettings.TimeOfDayMode = Game.TimeOfDayMode.Day; return "day";'  # night frames made the seen metric meaningless (r4a run)


def paths(C, y):
    """(name, [(eye, turn)]) with eye world positions and (yaw, pitch) turns; the target is looked at otherwise."""
    cx, cz = C[0], C[2]; e = y + 1.62
    out = []
    out.append(("strafe-8m", [((cx - 8 + 16 * i / 40, e, cz - 8), (0, 0)) for i in range(41)]))
    out.append(("orbit-7m", [((cx + 7 * math.cos(2 * math.pi * i / 48), e, cz + 7 * math.sin(2 * math.pi * i / 48)), (0, 0)) for i in range(49)]))
    out.append(("rise-0-9m", [((cx, y + .9 + 8.1 * i / 30, cz - 8), (0, 0)) for i in range(31)]))
    out.append(("approach-45-4m-and-back", [((cx, e, cz - (45 - 41 * i / 40)), (0, 0)) for i in range(41)] + [((cx, e, cz - (4 + 41 * i / 40)), (0, 0)) for i in range(1, 41)]))
    out.append(("fast-pass", [((cx - 30 + 60 * i / 24, y + 3, cz - 5), (0, 0)) for i in range(25)]))
    out.append(("turn-in-place-8m", [((cx, e, cz - 8), (.9 * math.sin(2 * math.pi * i / 30), .5 * math.sin(4 * math.pi * i / 30))) for i in range(31)]))
    return out


def main(package, label):
    pkg = {"output-lite": m0.LITE, "output-full": m0.FULL}.get(package) or Path(package)
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"smoke-motion-{label}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"smoke-motion-{label}", "package": {pkg.name: sha(pkg)}, "paths": [], "steps": []}
    T0 = time.time()
    def step(name, value):
        R["steps"].append({"t": round(time.time() - T0, 1), "step": name, "value": value}); print(f"[{round(time.time() - T0, 1)}] {name}: {value}", flush=True); return value
    g = None
    try:
        g = m0.game("server", case_dir, [pkg]); R["engine"] = g.engine_info(); to_menu(g)
        g.cmd('EXEC SettingsManager.ShowGuiInScreenshots = false; SettingsManager.ShowLogoInScreenshots = false; SettingsManager.SaveSettings();')
        m0.enter_world(g)
        step("window kept active", g.func(KEEP_ACTIVE)); step("weather off", g.func(WEATHER_OFF)); step("day locked", g.func(DAY_LOCK))
        g.func(MAIN + 'pl.ComponentLocomotion.IsCreativeFlyEnabled = true; pl.ComponentBody.IsGravityEnabled = false; var p = players.GlobalSpawnPosition + new Engine.Vector3(20f, 12f, 22f); pl.ComponentBody.Position = p; return "ok";'); time.sleep(4)
        t0 = time.time(); a = g.func(ARENA)
        while a.startswith("wait") and time.time() - t0 < 90: time.sleep(1); a = g.func(ARENA)
        x, y, z = [int(v) for v in step("arena", a).split()]
        step("smoke placed", g.func(smoke(x, y, z))); time.sleep(1)
        # The column stands 9 m behind the smoke (sp_smoke_view's 7 m one is moved): the 7 m orbit passes it without walking through it.
        g.func(PROJECT + f'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); for (int dy = 0; dy <= 2; dy++) {{ t.ChangeCell({x}, {y} + dy, {z} + 7, 0); t.ChangeCell({x}, {y} + dy, {z} + 9, Game.Terrain.MakeBlockValue(Game.DiamondBlock.Index)); }} return "ok";'); time.sleep(1)
        C = (x + .5, y, z + .5); target = (x + .5, y + 1.5, z + 9.5); box = ((x, y, z + 9), (x + 1, y + 3, z + 10))
        g.func(smoke_state(2.0, 12.0, True, False))
        n = 0; R["mode"] = MODE
        for name, points in (paths(C, y) if MODE == "full" else []):
            frames = []
            for eye, (yaw, pitch) in points:
                got = g.func(camera(eye, target, yaw, pitch, 1.0, False)); time.sleep(.6)   # the viewmodel's sway settles: the three captures must agree outside the smoke
                n += 1; stamp_utc = datetime.now(timezone.utc).strftime("%H:%M:%S.%f")[:-3]
                g.func(smoke_state(2.0, 12.0, True, False)); time.sleep(.2); a_path, a_box = snap(g, f"sm-{label}-{n:04d}-a.png", box)
                g.func(smoke_state(2.0, 12.0, False, False)); time.sleep(.2); b_path, _ = snap(g, f"sm-{label}-{n:04d}-b.png", box)
                # Block changes reach the drawn chunk mesh a few frames later: wait before the capture and before the next frame.
                g.func(PROJECT + f'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); for (int dy = 0; dy <= 2; dy++) t.ChangeCell({x}, {y} + dy, {z} + 9, 0); return "ok";'); time.sleep(.5)
                c_path, _ = snap(g, f"sm-{label}-{n:04d}-c.png", box)
                g.func(PROJECT + f'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); for (int dy = 0; dy <= 2; dy++) t.ChangeCell({x}, {y} + dy, {z} + 9, Game.Terrain.MakeBlockValue(Game.DiamondBlock.Index)); return "ok";'); time.sleep(.5)
                frames.append({"n": n, "utc": stamp_utc, "eye": list(eye), "turn": [yaw, pitch], "got": got, "with": a_path, "without": b_path, "neither": c_path, "targetBox": a_box})
            R["paths"].append({"path": name, "frames": frames}); step(f"path {name}", len(frames))
        # Round 4 — A: the character stands still 8 m from the smoke, only a free camera moves (orbit, rise, strafe): the puffs'
        # facing normal must not change. B: the camera stands still, the character moves: the normal follows the character.
        # C: third person with the character turning in place and strafing: what the third-person camera sees stays continuous.
        eye0 = (C[0], y + 1.62, C[2] - 8)
        def facing_frame(name, n, eye, turn):
            stamp_utc = datetime.now(timezone.utc).strftime("%H:%M:%S.%f")[:-3]
            fac = g.func(FACING)
            g.func(smoke_state(2.0, 12.0, True, False)); time.sleep(.2); a_path, a_box = snap(g, f"sm-{label}-{n:04d}-a.png", box)
            if MODE != "full": return {"n": n, "utc": stamp_utc, "eye": list(eye), "turn": list(turn), "facing": fac, "with": a_path, "without": "", "neither": "", "targetBox": a_box}
            g.func(smoke_state(2.0, 12.0, False, False)); time.sleep(.2); b_path, _ = snap(g, f"sm-{label}-{n:04d}-b.png", box)
            g.func(PROJECT + f'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); for (int dy = 0; dy <= 2; dy++) t.ChangeCell({x}, {y} + dy, {z} + 9, 0); return "ok";'); time.sleep(.5)
            c_path, _ = snap(g, f"sm-{label}-{n:04d}-c.png", box)
            g.func(PROJECT + f'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); for (int dy = 0; dy <= 2; dy++) t.ChangeCell({x}, {y} + dy, {z} + 9, Game.Terrain.MakeBlockValue(Game.DiamondBlock.Index)); return "ok";'); time.sleep(.5)
            return {"n": n, "utc": stamp_utc, "eye": list(eye), "turn": list(turn), "facing": fac, "with": a_path, "without": b_path, "neither": c_path, "targetBox": a_box}
        step("character at 8 m", g.func(stand(eye0[0], eye0[1], eye0[2], C[0], y + 1, C[2]))); step("free camera", g.func(FLY_ADD))
        cam_paths = [("cam-orbit-10m", [(C[0] + 10 * math.cos(2 * math.pi * i / 16), y + 1.62, C[2] + 10 * math.sin(2 * math.pi * i / 16)) for i in range(17)]),
                     ("cam-rise-1-12m", [(C[0] + 6, y + 1 + 11 * i / 12, C[2] - 6) for i in range(13)]),
                     ("cam-strafe-12m", [(C[0] - 6 + i, y + 1.62, C[2] - 10) for i in range(13)])]
        for name, points in cam_paths:
            frames = []
            for cpos in points:
                g.func(fly_to(cpos[0], cpos[1], cpos[2], C[0], y + 1.5, C[2])); time.sleep(.5); n += 1
                frames.append(facing_frame(name, n, cpos, (0, 0)))
            R["paths"].append({"path": name, "frames": frames, "mode": "camera-only"}); step(f"path {name}", len(frames))
        frames = []
        g.func(fly_to(C[0] - 8, y + 3, C[2] - 8, C[0], y + 1.5, C[2])); time.sleep(.3)
        for i in range(13):
            e = (C[0] - 6 + i, y + 1.62, C[2] - 8); g.func(stand(e[0], e[1], e[2], C[0], y + 1, C[2])); g.func(fly_to(C[0] - 8, y + 3, C[2] - 8, C[0], y + 1.5, C[2])); time.sleep(.5); n += 1
            frames.append(facing_frame("char-strafe-12m", n, e, (0, 0)))
        R["paths"].append({"path": "char-strafe-12m", "frames": frames, "mode": "character-only"}); step("path char-strafe-12m", len(frames))
        for name, points in [("tpp-turn-in-place-8m", [(eye0, (.9 * math.sin(2 * math.pi * i / 30), .5 * math.sin(4 * math.pi * i / 30))) for i in range(31)]),
                             ("tpp-strafe-8m", [((C[0] - 8 + 16 * i / 40, y + 1.62, C[2] - 8), (0, 0)) for i in range(41)])]:
            frames = []
            for eye, (yaw, pitch) in points:
                g.func(camera(eye, target, yaw, pitch, 1.0, True)); time.sleep(.6); n += 1
                frames.append(facing_frame(name, n, eye, (yaw, pitch)))
            R["paths"].append({"path": name, "frames": frames, "mode": "third-person"}); step(f"path {name}", len(frames))
        g.func(smoke_state(2.0, 12.0, True, False))
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None: R["gameErrors"] = g.errors(); g.close()
    # The frames are read from the runtime's ScreenCapture folder: the runtime is released only after the analysis
    # (the next run archives that folder as soon as it owns the runtime).
    try: R["analysis"] = analyse(case_dir, R, label)
    except Exception as e: R["analysis"] = {"failure": f"{type(e).__name__}: {e}"}
    RESULTS.mkdir(parents=True, exist_ok=True)
    m0.RUNTIME_OWNER.release()
    out = RESULTS / f"smoke-motion-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print(json.dumps({"case": R["case"], "failure": R.get("failure"), "errors": (R.get("gameErrors") or [])[:3], "analysis": R["analysis"]}, ensure_ascii=False, indent=1)[:12000])
    print("report", out, "case", case_dir)
    return 0 if "failure" not in R else 1


def analyse(case_dir, R, label):
    import numpy as np
    from PIL import Image, ImageDraw
    summary = []
    for p in R["paths"]:
        seen = []; thumbs = []
        for f in p["frames"]:
            a, b, c = Path(f["with"]), Path(f["without"] or "-"), Path(f["neither"] or "-")
            if not a.exists(): f["seen"] = None; continue
            full = b.exists() and c.exists()
            A = np.asarray(Image.open(a).convert("RGB"), dtype=np.int16)
            B = np.asarray(Image.open(b).convert("RGB"), dtype=np.int16) if full else None; Cn = np.asarray(Image.open(c).convert("RGB"), dtype=np.int16) if full else None
            x0, y0, x1, y1, w, h, behind = f["targetBox"].split()
            x0, y0, x1, y1 = max(0, int(float(x0))), max(0, int(float(y0))), min(A.shape[1], int(float(x1))), min(A.shape[0], int(float(y1)))
            if not full or behind == "True" or x1 - x0 < 3 or y1 - y0 < 3: f["seen"] = None
            else:
                mask = np.abs(B[y0:y1, x0:x1] - Cn[y0:y1, x0:x1]).max(axis=2) > 20
                f["seen"] = None if mask.sum() < 20 else round(float((np.abs(A[y0:y1, x0:x1] - B[y0:y1, x0:x1]).max(axis=2)[mask] < 12).mean()), 3)
            if f["seen"] is not None: seen.append(f["seen"])
            im = Image.open(a).convert("RGB"); d = ImageDraw.Draw(im)
            if f["seen"] is not None: d.rectangle([x0, y0, x1, y1], outline=(255, 0, 0), width=3)
            im = im.resize((480, int(480 * im.height / im.width))); d = ImageDraw.Draw(im); d.rectangle([0, 0, 480, 16], fill=(0, 0, 0))
            wq = f.get("facing", "").split("|")[-1].strip() if f.get("facing", "").count("|") >= 5 else ""
            d.text((4, 2), f'{p["path"]} #{f["n"]} {f["utc"]} eye {f["eye"][0]:.1f},{f["eye"][1]:.1f},{f["eye"][2]:.1f} seen {f["seen"]}' + (f" w {wq}" if wq else ""), fill=(255, 255, 0))
            thumbs.append(im)
            for q in (a, b, c):
                if q.exists(): q.replace(case_dir / q.name)
        if thumbs:
            thumbs[0].save(case_dir / f"frames-{p['path']}.gif", save_all=True, append_images=thumbs[1:], duration=120, loop=0)
            cols = 6; rows = (len(thumbs) + cols - 1) // cols; th = thumbs[0].height
            sheet = Image.new("RGB", (cols * 480, rows * th), "black")
            for i, t in enumerate(thumbs): sheet.paste(t, ((i % cols) * 480, (i // cols) * th))
            if sheet.width * sheet.height > 4_000_000: sheet = sheet.resize((sheet.width // 2, sheet.height // 2))
            sheet.save(case_dir / f"sheet-{p['path']}.jpg", quality=78)
        # Frame-to-frame change of "seen" per metre moved: a jump without movement is the cloud changing on its own.
        jumps = []
        for i in range(1, len(p["frames"])):
            f0, f1 = p["frames"][i - 1], p["frames"][i]
            if f0.get("seen") is None or f1.get("seen") is None: continue
            moved = math.dist(f0["eye"], f1["eye"]); turned = abs(f1["turn"][0] - f0["turn"][0]) + abs(f1["turn"][1] - f0["turn"][1])
            jumps.append({"n": f1["n"], "dSeen": round(f1["seen"] - f0["seen"], 3), "moved": round(moved, 2), "turned": round(turned, 3)})
        big = [j for j in jumps if abs(j["dSeen"]) > .15]
        # Round 4: the facing normal of the probed puff per frame; how much it turned between frames, and how far it is from
        # the direction to the character's eye (the anchor). Camera-only paths must show ~0 turning.
        def parse_facing(f):
            try:
                parts = [tuple(float(v) for v in seg.split()) for seg in f["facing"].split("|")]
                # the probe prints the normal to 4 decimals: normalise again, or acos(n.n) of a rounded unit vector reads ~0.5 degrees
                l = math.sqrt(sum(v * v for v in parts[4])) or 1; return parts[0], parts[3], tuple(v / l for v in parts[4])
            except Exception: return None
        turns = []; offs = []
        for i, f in enumerate(p["frames"]):
            pf = parse_facing(f)
            if pf is None: continue
            anchor, puff, normal = pf
            to = tuple(a - b for a, b in zip(anchor, puff)); l = math.sqrt(sum(v * v for v in to)) or 1
            offs.append(math.degrees(math.acos(max(-1, min(1, sum(n * t / l for n, t in zip(normal, to)))))))
            if i > 0 and (prev := parse_facing(p["frames"][i - 1])) is not None:
                turns.append(math.degrees(math.acos(max(-1, min(1, sum(a * b for a, b in zip(normal, prev[2])))))))
        summary.append({"path": p["path"], "mode": p.get("mode", "first-person"), "frames": len(p["frames"]), "seenMin": min(seen) if seen else None, "seenMax": max(seen) if seen else None,
                        "facingMaxTurnDeg": round(max(turns), 3) if turns else None, "facingVsAnchorMaxDeg": round(max(offs), 3) if offs else None,
                        "largestFrameToFrameChange": max((abs(j["dSeen"]) for j in jumps), default=0), "changesOver0.15": big[:12],
                        "gif": str(case_dir / f"frames-{p['path']}.gif"), "sheet": str(case_dir / f"sheet-{p['path']}.jpg")})
    return {"summary": summary}


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2]))
