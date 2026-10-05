"""Smoke seen from one spot while the camera turns, single player, real game frames (post-mp-bugs-20260930 §2).

Run (Windows): ./tools/dev.ps1 python tools/MpM0/sp_smoke_view.py <package path | output-lite | output-full> <label>
One game in the fixed runtime role "server" (single player), TestAutomation and one CS package. A cleared flat arena,
one smoke placed at a fixed point with its age held every frame (the same cloud in every picture), and a diamond-block
column 7 m behind it. For every scene (full, growing, fading, an HE opening, a wall through the cloud), eye position
(standing 8 m, near 5 m, high 9 m up, raised 5 m up, crouching 17 m away, offset sideways, inside, third person) and view
(target in the centre, near the left/right edge, above/below the centre; one or two fields of view) the engine's own
screenshot path (ScreenCaptureManager.Capture) renders three frames: with the smoke, without it, and with neither smoke nor
target. Target pixels are those the column changes in the smoke-free frame; "seen" is the fraction of them the smoke left
unchanged (0 = hidden; none when something else, e.g. the wall, hides the target anyway). Contact sheets
of every scene are written for visual review; pictures stay on Windows.
"""
import json, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import m0
from m0 import Game, MODS, RESULTS, RUNS, TA_BUILT, sha, to_menu, poll
from mp_m1 import PROJECT, MAIN, SURVIVAL, KEEP_ACTIVE

ARENA = PROJECT + ('var p = players.GlobalSpawnPosition + new Engine.Vector3(20f, 8f, 30f); int x = (int)System.MathF.Floor(p.X), y = (int)System.MathF.Floor(p.Y), z = (int)System.MathF.Floor(p.Z); '
    'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); '
    'for (int dx = -18; dx <= 18; dx++) for (int dz = -18; dz <= 18; dz++) { var ch = t.Terrain.GetChunkAtCell(x + dx, z + dz); if (ch == null || ch.State <= Game.TerrainChunkState.InvalidContents4) return "wait chunk"; } '
    'for (int dx = -18; dx <= 18; dx++) for (int dz = -18; dz <= 18; dz++) { for (int dy = 0; dy <= 14; dy++) t.ChangeCell(x + dx, y + dy, z + dz, 0); t.ChangeCell(x + dx, y - 1, z + dz, Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index)); } '
    'for (int dy = 0; dy <= 2; dy++) t.ChangeCell(x, y + dy, z + 7, Game.Terrain.MakeBlockValue(Game.DiamondBlock.Index)); '
    'return x + " " + y + " " + z;')
BF = "System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance"
def smoke(x, y, z): return PROJECT + (
    f'var sub = project.FindSubsystem<Game.SubsystemScGrenades>(true); var active = (System.Collections.Generic.List<Game.ScGrenadeState>)typeof(Game.SubsystemScGrenades).GetField("m_active", {BF}).GetValue(sub); '
    f'var dist = (System.Collections.Generic.List<Game.ScSmokeDisturbance>)typeof(Game.SubsystemScGrenades).GetField("m_disturbances", {BF}).GetValue(sub); '
    f'var s = new Game.ScGrenadeState {{ Kind = 2, Effect = true, Grounded = true, Id = 900001, Position = new Engine.Vector3({x}f + .5f, {y}f, {z}f + .5f), Age = 2f, Remaining = 12f }}; active.Add(s); '
    'var d = System.AppDomain.CurrentDomain; d.SetData("sc.smoke", s); d.SetData("sc.smoke.age", 2f); d.SetData("sc.smoke.remaining", 12f); '
    'var he = new Game.ScSmokeDisturbance { Center = s.Position + new Engine.Vector3(0, 1f, -2.2f) }; he.SmokeIds.Add(s.Id); d.SetData("sc.smoke.he", he); '
    'System.Action hold = () => { s.Age = (float)d.GetData("sc.smoke.age"); s.Remaining = (float)d.GetData("sc.smoke.remaining"); he.Remaining = Game.ScSmokeDisturbance.Total; }; '
    'Engine.Window.Frame += hold; return "smoke " + s.Position;')
def smoke_state(age, remaining, present, opening):
    return PROJECT + (
        f'var sub = project.FindSubsystem<Game.SubsystemScGrenades>(true); var active = (System.Collections.Generic.List<Game.ScGrenadeState>)typeof(Game.SubsystemScGrenades).GetField("m_active", {BF}).GetValue(sub); '
        f'var dist = (System.Collections.Generic.List<Game.ScSmokeDisturbance>)typeof(Game.SubsystemScGrenades).GetField("m_disturbances", {BF}).GetValue(sub); '
        'var d = System.AppDomain.CurrentDomain; var s = (Game.ScGrenadeState)d.GetData("sc.smoke"); var he = (Game.ScSmokeDisturbance)d.GetData("sc.smoke.he"); '
        f'd.SetData("sc.smoke.age", {age}f); d.SetData("sc.smoke.remaining", {remaining}f); s.Age = {age}f; s.Remaining = {remaining}f; '
        f'active.Remove(s); if ({"true" if present else "false"}) active.Add(s); dist.Remove(he); if ({"true" if opening else "false"}) dist.Add(he); return active.Count + " " + dist.Count;')
def column(x, y, z, on):
    v = "Game.Terrain.MakeBlockValue(Game.DiamondBlock.Index)" if on else "0"
    return PROJECT + f'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); for (int dy = 0; dy <= 2; dy++) t.ChangeCell({x}, {y} + dy, {z} + 7, {v}); return "ok";'
def wall(x, y, z, on):
    v = "Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index)" if on else "0"
    return PROJECT + (f'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); for (int dz = -5; dz <= 5; dz++) for (int dy = 0; dy <= 5; dy++) t.ChangeCell({x} + 2, {y} + dy, {z} + dz, {v}); return "ok";')

# Places the player's eye at (ex, ey, ez), looks at (tx, ty, tz) turned by (yaw, pitch) radians, in first or third person with
# the given field-of-view setting; returns the eye it got.
def camera(eye, target, yaw, pitch, fov, third):
    # A Call (m0): the values are FUNCA arguments on the 1.9.3.1 copy, so this per-frame snippet compiles once.
    return m0.Call(MAIN + (
        'var body = pl.ComponentBody; pl.ComponentLocomotion.IsCreativeFlyEnabled = true; body.IsGravityEnabled = false; body.Velocity = Engine.Vector3.Zero; '
        f'Game.SettingsManager.ViewAngle = $f0; var w = pl.GameWidget; w.ActiveCamera = {"w.FindCamera<Game.TppCamera>()" if third else "w.FindCamera<Game.FppCamera>()"}; '
        'pl.ComponentCreatureModel.Update(0f); var off = pl.ComponentCreatureModel.EyePosition - body.Position; body.Position = new Engine.Vector3($f1, $f2, $f3) - off; '
        'pl.ComponentCreatureModel.Update(0f); var eye = pl.ComponentCreatureModel.EyePosition; '
        'var dir = Engine.Vector3.Normalize(new Engine.Vector3($f4, $f5, $f6) - eye); '
        'float heading = System.MathF.Atan2(-dir.X, -dir.Z) + $f7, elevation = System.Math.Clamp(System.MathF.Asin(dir.Y) + $f8, -1.4f, 1.4f); '
        'var want = new Engine.Vector3(-System.MathF.Sin(heading) * System.MathF.Cos(elevation), System.MathF.Sin(elevation), -System.MathF.Cos(heading) * System.MathF.Cos(elevation)); float best = -2f; string got = ""; '
        'foreach (float ps in new[] { 1f, -1f }) { body.Rotation = Engine.Quaternion.CreateFromAxisAngle(Engine.Vector3.UnitY, heading); pl.ComponentLocomotion.LookAngles = new Engine.Vector2(0f, ps * elevation); pl.ComponentCreatureModel.Update(0f); '
        '  float dot = Engine.Vector3.Dot(Engine.Matrix.CreateFromQuaternion(pl.ComponentCreatureModel.EyeRotation).Forward, want); if (dot > 0.9995f) { best = dot; break; } best = System.Math.Max(best, dot); } '
        'return System.FormattableString.Invariant($"{eye.X:0.00} {eye.Y:0.00} {eye.Z:0.00} {best:0.00000}");'), fov, *eye, *target, yaw, pitch)
# Renders the frame through the engine's screenshot path and projects the target column (world box) with the camera used.
SETTLED = MAIN + 'var c = pl.GameWidget.ActiveCamera; return System.FormattableString.Invariant($"{c.ViewPosition.X:0.000} {c.ViewPosition.Y:0.000} {c.ViewPosition.Z:0.000} {c.ViewDirection.X:0.0000} {c.ViewDirection.Y:0.0000} {c.ViewDirection.Z:0.0000}");'
def settle(g, timeout=3.0):
    """Waits until the active camera stops moving (the third-person camera eases towards its place over several frames)."""
    end = time.time() + timeout; last = g.func(SETTLED)
    while time.time() < end:
        time.sleep(.15); now = g.func(SETTLED)
        if now == last: return True
        last = now
    return False


def snap(g, name, box, tries=8):
    """capture() with a retry: a minimized window answers "minimized" (and is restored); the frame is taken once it is back."""
    for i in range(tries):
        r = g.func(capture(name, box))
        if r != "minimized": return r.split("|")
        time.sleep(.5)
    raise RuntimeError(f"window stayed minimized: {name}")


def capture(name, box):
    (x0, y0, z0), (x1, y1, z1) = box
    return m0.Call(MAIN + (
        'var size = Engine.Window.Size; if (size.X == 0 || size.Y == 0) { var view = typeof(Engine.Window).GetField("m_view", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null); var wsp = view?.GetType().GetProperty("WindowState"); if (wsp != null) wsp.SetValue(view, System.Enum.Parse(wsp.PropertyType, "Normal")); return "minimized"; } '
        'var name = $s0; Game.ScreenCaptureManager.Capture(size.X, size.Y, name); '
        'var path = Engine.Storage.GetSystemPath(Engine.Storage.CombinePaths(Game.ScreenCaptureManager.ScreenshotDir, name)); '
        'var cam = pl.GameWidget.ActiveCamera; var vp = cam.ViewProjectionMatrix; float minX = 1e9f, minY = 1e9f, maxX = -1e9f, maxY = -1e9f; bool behind = false; '
        f'foreach (var c in new[] {{ new Engine.Vector3({x0}f, {y0}f, {z0}f), new Engine.Vector3({x1}f, {y0}f, {z0}f), new Engine.Vector3({x0}f, {y1}f, {z0}f), new Engine.Vector3({x1}f, {y1}f, {z0}f), '
        f'  new Engine.Vector3({x0}f, {y0}f, {z1}f), new Engine.Vector3({x1}f, {y0}f, {z1}f), new Engine.Vector3({x0}f, {y1}f, {z1}f), new Engine.Vector3({x1}f, {y1}f, {z1}f) }}) {{ '
        '  float hx = c.X * vp.M11 + c.Y * vp.M21 + c.Z * vp.M31 + vp.M41, hy = c.X * vp.M12 + c.Y * vp.M22 + c.Z * vp.M32 + vp.M42, hw = c.X * vp.M14 + c.Y * vp.M24 + c.Z * vp.M34 + vp.M44; '
        '  if (hw <= 0) { behind = true; continue; } float px = (hx / hw + 1) * .5f * size.X, py = (1 - hy / hw) * .5f * size.Y; '
        '  minX = System.Math.Min(minX, px); maxX = System.Math.Max(maxX, px); minY = System.Math.Min(minY, py); maxY = System.Math.Max(maxY, py); } '
        'return path + "|" + System.FormattableString.Invariant($"{minX:0} {minY:0} {maxX:0} {maxY:0} {size.X} {size.Y} {behind}");'), name)

SCENES = [("full", 2.0, 12.0, False, False), ("growing", .45, 17.0, False, False), ("fading", 3.0, .6, False, False),
          ("he-opening", 2.0, 12.0, True, False), ("wall", 2.0, 12.0, False, True)]
VIEWS = [(0, 0), (.8, 0), (-.8, 0), (0, .6), (0, -.6)]


def main(package, label):
    import math
    pkg = {"output-lite": m0.LITE, "output-full": m0.FULL}.get(package) or Path(package)
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"smoke-view-{label}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"smoke-view-{label}", "package": {pkg.name: sha(pkg)}, "views": [], "steps": []}
    T0 = time.time()
    def step(name, value):
        R["steps"].append({"t": round(time.time() - T0, 1), "step": name, "value": value}); print(f"[{round(time.time() - T0, 1)}] {name}: {value}", flush=True); return value
    g = None
    try:
        g = m0.game("server", case_dir, [pkg]); R["engine"] = g.engine_info(); to_menu(g)
        g.cmd('EXEC SettingsManager.ShowGuiInScreenshots = false; SettingsManager.ShowLogoInScreenshots = false; SettingsManager.SaveSettings();')
        m0.enter_world(g)
        step("window kept active", g.func(KEEP_ACTIVE))
        # No rain streaks or rain sound in the frames: the same clear weather for every package compared.
        step("day locked", g.func(PROJECT + 'project.FindSubsystem<Game.SubsystemGameInfo>(true).WorldSettings.TimeOfDayMode = Game.TimeOfDayMode.Day; return "day";'))
        step("weather off", g.func(PROJECT + 'var w = project.FindSubsystem<Game.SubsystemWeather>(true); project.FindSubsystem<Game.SubsystemGameInfo>(true).WorldSettings.AreWeatherEffectsEnabled = false; '
            'w.ManualPrecipitationEnd(); w.PrecipitationIntensity = 0f; return "precipitation " + w.IsPrecipitationStarted + " intensity " + w.PrecipitationIntensity;'))
        g.func(MAIN + 'pl.ComponentLocomotion.IsCreativeFlyEnabled = true; pl.ComponentBody.IsGravityEnabled = false; var p = players.GlobalSpawnPosition + new Engine.Vector3(20f, 12f, 22f); pl.ComponentBody.Position = p; return "ok";')
        time.sleep(4)
        t0 = time.time(); a = g.func(ARENA)
        while a.startswith("wait") and time.time() - t0 < 90: time.sleep(1); a = g.func(ARENA)
        x, y, z = [int(v) for v in step("arena (smoke at x+.5, y, z+.5; target column at z+7)", a).split()]
        g.func(MAIN + 'var tod = project.FindSubsystem<Game.SubsystemTimeOfDay>(true); return "ok";')
        step("smoke placed", g.func(smoke(x, y, z)))
        C = (x + .5, y, z + .5); target = (x + .5, y + 1.5, z + 7.5)
        box = ((x, y, z + 7), (x + 1, y + 3, z + 8))
        positions = [("standing-8m", (C[0], y + 1.62, C[2] - 8), False, [.8, 1.2]),
                     ("near-5m", (C[0], y + 1.62, C[2] - 5), False, [1.0]),
                     ("high-9m-up", (C[0], y + 9, C[2] - 8), False, [.8, 1.2]),
                     ("raised-5m-up", (C[0], y + 5, C[2] - 8), False, [.8, 1.2]),
                     ("far-crouch-17m", (C[0], y + .9, C[2] - 17), False, [1.0]),
                     ("offset-sideways", (C[0] + 3, y + 1.62, C[2] - 8), False, [1.0]),
                     ("inside", (C[0], y + 1.62, C[2] - 1.5), False, [1.0]),
                     ("third-person-8m", (C[0], y + 1.62, C[2] - 8), True, [1.0])]
        n = 0
        for scene, age, remaining, opening, walled in SCENES:
            g.func(wall(x, y, z, walled)); time.sleep(.5)
            for pos, eye, third, fovs in positions:
                for fov in fovs:
                    half = math.radians(80 * fov / 2); half_h = math.atan(math.tan(half) * 16 / 9)
                    for fy, fp in VIEWS:
                        got = g.func(camera(eye, target, fy * half_h * .9, fp * half * .9, fov, third)); time.sleep(.35); settle(g)
                        g.func(smoke_state(age, remaining, True, opening)); time.sleep(.15)
                        n += 1; a_path, a_box = snap(g, f"sv-{label}-{n:04d}-a.png", box)
                        g.func(smoke_state(age, remaining, False, False)); time.sleep(.15)
                        b_path, b_box = snap(g, f"sv-{label}-{n:04d}-b.png", box)
                        g.func(column(x, y, z, False)); time.sleep(.15)
                        c_path, _ = snap(g, f"sv-{label}-{n:04d}-c.png", box)
                        g.func(column(x, y, z, True))
                        R["views"].append({"n": n, "scene": scene, "position": pos, "thirdPerson": third, "viewAngle": fov, "turn": [fy, fp], "eye": got,
                                           "with": a_path, "without": b_path, "neither": c_path, "targetBox": a_box, "targetBoxWithout": b_box})
                step(f"{scene} {pos}", len(R["views"]))
            g.func(wall(x, y, z, False))
        g.func(smoke_state(2, 12, True, False))
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None: R["gameErrors"] = g.errors(); g.close()
    # The frames are read from the runtime's ScreenCapture folder: the runtime is released only after the analysis
    # (the next run archives that folder as soon as it owns the runtime).
    try: R["analysis"] = analyse(case_dir, R)
    except Exception as e: R["analysis"] = {"failure": f"{type(e).__name__}: {e}"}
    RESULTS.mkdir(parents=True, exist_ok=True)
    m0.RUNTIME_OWNER.release()
    out = RESULTS / f"smoke-view-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print(json.dumps({"case": R["case"], "failure": R.get("failure"), "errors": R.get("gameErrors", [])[:5], "analysis": R["analysis"]}, ensure_ascii=False, indent=1)[:14000])
    print("report", out, "case", case_dir)
    return 0 if "failure" not in R else 1


def analyse(case_dir, R):
    import numpy as np
    from PIL import Image, ImageDraw
    rows = {}; sheet = {}
    def keep(*frames):
        for p in frames:
            if p.exists() and p.parent != case_dir: p.replace(case_dir / p.name)
    for v in R["views"]:
        a, b, c = Path(v["with"]), Path(v["without"]), Path(v["neither"])
        if not a.exists() or not b.exists() or not c.exists(): v["seen"] = None; continue
        A = np.asarray(Image.open(a).convert("RGB"), dtype=np.int16); B = np.asarray(Image.open(b).convert("RGB"), dtype=np.int16)
        Cn = np.asarray(Image.open(c).convert("RGB"), dtype=np.int16)
        x0, y0, x1, y1, w, h, behind = v["targetBox"].split()
        x0, y0, x1, y1 = max(0, int(x0)), max(0, int(y0)), min(A.shape[1], int(x1)), min(A.shape[0], int(y1))
        if behind == "True" or x1 - x0 < 3 or y1 - y0 < 3: v["seen"] = None; v["note"] = "target off screen"; continue
        # The with/without frames must show the same view: the third-person camera eases towards its place, and a box that moved
        # between the captures compares two different views (r5a view 55: y -141..30 with smoke, -242..-37 without).
        if v.get("targetBoxWithout"):
            wb = [float(q) for q in v["targetBoxWithout"].split()[:4]]; ab = [float(q) for q in v["targetBox"].split()[:4]]
            if max(abs(p - q) for p, q in zip(ab, wb)) > 6: v["seen"] = None; v["note"] = "camera moved between the captures"; continue
        # Target pixels: where the column changes the smoke-free frame. None: something else (a wall) already hides it.
        mask = np.abs(B[y0:y1, x0:x1] - Cn[y0:y1, x0:x1]).max(axis=2) > 20
        v["targetPixels"] = int(mask.sum())
        if mask.sum() < 20: v["seen"] = None; v["note"] = "target not visible even without smoke"; keep(a, b, c); continue
        diff = np.abs(A[y0:y1, x0:x1] - B[y0:y1, x0:x1]).max(axis=2)
        v["seen"] = round(float((diff[mask] < 12).mean()), 3)
        key = (v["scene"], v["position"]); rows.setdefault(key, []).append(v["seen"])
        # Contact sheet thumbnail with the target box and the measure.
        im = Image.open(a).convert("RGB"); d = ImageDraw.Draw(im); d.rectangle([x0, y0, x1, y1], outline=(255, 0, 0), width=3)
        d.text((8, 8), f'{v["scene"]} {v["position"]} fov {v["viewAngle"]} turn {v["turn"]} seen {v["seen"]}', fill=(255, 255, 0))
        sheet.setdefault(v["scene"], []).append(im.resize((320, int(320 * im.height / im.width))))
        keep(a, b, c)
    summary = [{"scene": s, "position": p, "views": len(vals), "seenMax": max(vals), "seenMin": min(vals), "seenValues": vals} for (s, p), vals in rows.items()]
    for v in R["views"]:
        if v.get("seen") is None: summary.append({"scene": v["scene"], "position": v["position"], "view": v["n"], "note": v.get("note", "no picture")})
    sheets = []
    for scene, thumbs in sheet.items():
        cols = 6; rows_n = (len(thumbs) + cols - 1) // cols; th = thumbs[0].height
        img = Image.new("RGB", (cols * 320, rows_n * th), "black")
        for i, t in enumerate(thumbs): img.paste(t, ((i % cols) * 320, (i // cols) * th))
        path = case_dir / f"sheet-{scene}.jpg"
        if img.width * img.height > 3_000_000: img = img.resize((img.width // 2, img.height // 2))
        img.save(path, quality=80); sheets.append(str(path))
    return {"summary": summary, "sheets": sheets}


def reanalyse(label):
    """Recomputes the analysis of a finished run (results/smoke-view-<label>.json, frames already in its run folder)."""
    out = RESULTS / f"smoke-view-{label}.json"; R = json.loads(out.read_text("utf-8"))
    case_dir = sorted(RUNS.glob(f"smoke-view-{label}-*"))[-1]
    for v in R["views"]:
        for k in ("with", "without", "neither"):
            if v.get(k): v[k] = str(case_dir / Path(v[k]).name)
        v.pop("seen", None); v.pop("note", None)
    R["analysis"] = analyse(case_dir, R); R["reanalysed"] = time.strftime("%Y-%m-%d %H:%M:%S")
    out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print(json.dumps({"case": R["case"], "analysis": R["analysis"]}, ensure_ascii=False, indent=1)[:14000])
    return 0


if __name__ == "__main__":
    if sys.argv[1] == "reanalyse": sys.exit(reanalyse(sys.argv[2]))
    sys.exit(main(sys.argv[1], sys.argv[2]))
