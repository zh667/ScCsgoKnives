"""Camera and character apart (post-mp-bugs-20260930 review B / round 3 item 1), single player, real game frames.

Run (Windows): ./tools/dev.ps1 python tools/MpM0/sp_camera_fire.py <package path | output-lite | output-full> <label>
One game in the fixed runtime role "server" (single player), TestAutomation and one CS package. In a cleared arena the
player stands at its origin with a target 7 m ahead. Every shot is read back from SubsystemScGunBlockBehavior.LastShotDebug
(camera, eye, shot ray, tracer start, hit) and the gun's record (rounds), the target from its health. Checked:
  first person: the mod's fire button and its key binding both fire from the eye and hit;
  a free-flight camera (FlyCamera, IsEntityControlEnabled=false) 10 m to the side, looking at the target: the button, the
    key and even a hit/dig ray injected past the engine's input gate fire nothing, spend nothing, hurt nothing;
  the R8's hammer drawn under the button, the camera switched to free flight and back: no shot is delivered;
  a Glock burst started, the camera switched to free flight on the next frame: the rest of the burst is dropped;
  third person with a two-block wall between the character and the target, which the camera above sees over: the shot
    leaves the eye along the character's look (the first-person crosshair's line) and stops at the wall; with the wall gone
    it hits;
  the orbit camera (a free camera like the debug and perspective-view cameras): fires from the eye, along the character's own look;
  (first-person-eye-shot-20261001: every view shoots the first-person crosshair's line; the round-4 musket origin is history)
  creative flight in first person: fires;
  the Dual Berettas in third person: the tracer starts at the drawn gun (near the body, not the eye), left and right alternating.
"""
import json, math, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import m0
from m0 import Game, MODS, RESULTS, RUNS, TA_BUILT, sha, to_menu, poll
from mp_m1 import PROJECT, MAIN, SURVIVAL, KEEP_ACTIVE, STAND, build_arena, teleport, EYE, near, aim_at, spawn_target, health
from mp_m2b import snap, rounds

def mod_type(name): return f'System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("{name}")).FirstOrDefault(x => x != null)'
GUNS = 'var guns = project.FindSubsystem<Game.SubsystemScGunBlockBehavior>(true); '
DEBUG_ON = PROJECT + 'Game.SubsystemScGunBlockBehavior.DebugShots = true; Game.SubsystemScGunBlockBehavior.LastShotDebug = ""; return "on";'
LAST = 'return Game.SubsystemScGunBlockBehavior.LastShotDebug;'
CLEAR_LAST = 'Game.SubsystemScGunBlockBehavior.LastShotDebug = ""; return "ok";'
def button(down): return MAIN + GUNS + f'guns.SetFireButton(pl, {"true" if down else "false"}); return "ok";'
# The on-screen fire button held for <frames> engine frames (released by a Window.Frame handler): a real touch holds it for
# whole frames, while two separate FUNC calls ~50 ms apart can straddle no gun update when a Roslyn compile blocks the main
# thread (camera-fire r4x #4: both first-person button presses produced no shot while the key presses right after them did).
def button_frames(frames): return MAIN + GUNS + ('guns.SetFireButton(pl, true); int start = Engine.Time.FrameIndex, left = ' + str(frames) + '; System.Action h = null; '
    'h = () => { if (--left <= 0) { guns.SetFireButton(pl, false); Engine.Window.Frame -= h; } }; Engine.Window.Frame += h; return start.ToString();')
def give(name): return MAIN + (
    f'int v = System.Array.FindIndex(Game.GunSpec.All, s => s.Name == "{name}"); var spec = Game.GunSpec.All[v]; '
    'int id = Game.ScGunRegistry.Current.Allocate(v, spec.Magazine, false, Game.ScGunDurability.Full(v)); '
    'var inv = pl.ComponentMiner.Inventory; inv.RemoveSlotItems(0, inv.GetSlotCount(0)); '
    'inv.AddSlotItems(0, Game.Terrain.MakeBlockValue(Game.BlocksManager.GetBlockIndex<Game.ScGunBlock>(true), 0, Game.GunSpec.WithId(v, id)), 1); inv.ActiveSlotIndex = 0; return id + " " + spec.Magazine;')
CAMERA = MAIN + 'var c = pl.GameWidget.ActiveCamera; return c.GetType().Name + " " + c.IsEntityControlEnabled + " " + System.FormattableString.Invariant($"{c.ViewPosition.X:0.00} {c.ViewPosition.Y:0.00} {c.ViewPosition.Z:0.00}");'
def camera(kind):  # Fpp, Tpp, Orbit, Fly
    add = 'if (w.FindCamera<Game.FlyCamera>(false) == null) w.AddCamera(new Game.FlyCamera(w), g => true); ' if kind == "Fly" else ''
    return MAIN + f'var w = pl.GameWidget; {add}w.ActiveCamera = w.FindCamera<Game.{kind}Camera>(); return w.ActiveCamera.GetType().Name + " " + w.ActiveCamera.IsEntityControlEnabled;'
def fly_to(x, y, z, tx, ty, tz): return MAIN + (f'var w = pl.GameWidget; var f = w.FindCamera<Game.FlyCamera>(); f.m_position = new Engine.Vector3({x}f, {y}f, {z}f); '
    f'f.m_direction = Engine.Vector3.Normalize(new Engine.Vector3({tx}f, {ty}f, {tz}f) - f.m_position); f.m_velocity = Engine.Vector3.Zero; f.SetupPerspectiveCamera(f.m_position, f.m_direction, Engine.Vector3.UnitY); return "ok";')
FIRE_KEY = PROJECT + 'Game.ScGunBindings.Keys[Game.ScGunFunctions.Fire] = "F"; return Game.ScGunBindings.Get(Game.ScGunFunctions.Fire);'
def wall(x, y, z, on):
    v = "Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index)" if on else "0"
    return PROJECT + f'var t = project.FindSubsystem<Game.SubsystemTerrain>(true); for (int dx = -3; dx <= 3; dx++) for (int dy = 0; dy <= 1; dy++) t.ChangeCell({x} + dx, {y} + dy, {z} + 2, {v}); return "ok";'
CREATIVE_FLY = MAIN + 'pl.ComponentLocomotion.IsCreativeFlyEnabled = true; pl.ComponentBody.IsGravityEnabled = false; return pl.ComponentLocomotion.IsCreativeFlyEnabled.ToString();'
BURST_ON = MAIN + GUNS + 'var f = typeof(Game.SubsystemScGunBlockBehavior).GetField("m_states", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); var states = (System.Collections.IDictionary)f.GetValue(guns); var st = states[pl]; st.GetType().GetField("BurstMode").SetValue(st, true); return "burst";'
# 1.9.3.1: the body's rotation carries the yaw (LookAngles.X is only the head's turn relative to it, auto-levelled back to 0);
# EyeRotation = body rotation × (−LookAngles.X, LookAngles.Y).
def look(yaw, pitch): return MAIN + (f'pl.ComponentBody.Rotation = Engine.Quaternion.CreateFromAxisAngle(Engine.Vector3.UnitY, {yaw}f); '
    f'pl.ComponentLocomotion.LookAngles = new Engine.Vector2(0f, {pitch}f); return "ok";')
TRACE = ('var terrain = project.FindSubsystem<Game.SubsystemTerrain>(true); var bodies = project.FindSubsystem<Game.SubsystemBodies>(true); float dist = 256f; '
    'var cell = Game.ScGunRange.TraceBullet(terrain, s, d, 256f); if (cell.HasValue && cell.Value.Distance < dist) dist = cell.Value.Distance; '
    'var body = bodies.Raycast(s, s + d * dist, 0.35f, (b, q) => b.Entity != pl.Entity); '
    'if (body.HasValue && body.Value.Distance < dist) return "body " + body.Value.ComponentBody.Entity.Id; return cell.HasValue ? "terrain" : "none";')
# What the active camera's centre ray first meets, i.e. the aim point ScAimRay takes: "body <id>", "terrain" or "none".
# The aim look_at() starts from: the gun's line must pass through the target's centre AFTER the character has turned to face
# it, and the gun origin (eye + Right*0.3 - Up*0.2) depends on that facing; so the origin is predicted for the yaw that faces
# the target (look() sets body.Rotation = yaw about Y), not read from the body's current, still unturned matrix.
# Every view shoots from the eye along the character's look (first-person-eye-shot-20261001): the eye is aimed at the centre.
def aim_facing(tid): return MAIN + (f'var t = project.Entities.FirstOrDefault(e => e.Id == {tid})?.FindComponent<Game.ComponentBody>(); if (t == null) return "no target"; '
    'var eye = pl.ComponentCreatureModel.EyePosition; var bb = t.BoundingBox; var c = (bb.Min + bb.Max) * 0.5f; var d0 = Engine.Vector3.Normalize(c - eye); '
    'float h = System.MathF.Atan2(-d0.X, -d0.Z); var fm = Engine.Matrix.CreateFromQuaternion(Engine.Quaternion.CreateFromAxisAngle(Engine.Vector3.UnitY, h)); '
    'var d = Engine.Vector3.Normalize(c - eye); '
    'return System.FormattableString.Invariant($"{eye.X} {eye.Y} {eye.Z} {d.X} {d.Y} {d.Z}");')
CROSSHAIR = MAIN + 'var cam = pl.GameWidget.ActiveCamera; var s = cam.ViewPosition; var d = Engine.Vector3.Normalize(cam.ViewDirection); ' + TRACE
# What the shot's line first meets in every view: the eye along the character's own look (the first-person crosshair's line).
LOOKLINE = MAIN + 'var s = pl.ComponentCreatureModel.EyePosition; var d = Engine.Vector3.Normalize(Engine.Matrix.CreateFromQuaternion(pl.ComponentCreatureModel.EyeRotation).Forward); ' + TRACE
# A hit ray from the camera's position at the target's centre (what a camera that sees the target over a wall would aim).
# The third-party "survival perspective view" mod that provides Game.BumanCamera (the user's BUMANCAMERA); copied into the test
# runtime's Mods beside the CS package when present on this machine. Its camera is found by type name and driven by reflection.
BUMAN = Path(r"D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1/Mods/[API1.8]生存开透视角.scmod")
BUMAN_INFO = MAIN + ('var w = pl.GameWidget; var c = w.Cameras.FirstOrDefault(q => q.GetType().Name == "BumanCamera"); if (c == null) return "no BumanCamera: " + string.Join(",", w.Cameras.Select(q => q.GetType().Name)); '
    'var t = c.GetType(); return t.FullName + " : " + t.BaseType.Name + " control " + c.IsEntityControlEnabled + " movement " + c.UsesMovementControls + " fields " + string.Join(",", t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Select(f => f.Name));')
def buman_to(x, y, z, dx, dy, dz): return MAIN + (f'var w = pl.GameWidget; var c = w.Cameras.FirstOrDefault(q => q.GetType().Name == "BumanCamera"); if (c == null) return "no BumanCamera"; var t = c.GetType(); '
    f'var fp = t.GetField("m_position", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic); var fd = t.GetField("m_direction", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic); '
    f'if (fp == null || fd == null) return "no position/direction fields"; w.ActiveCamera = c; var pos = new Engine.Vector3({x}f, {y}f, {z}f); var dir = Engine.Vector3.Normalize(new Engine.Vector3({dx}f, {dy}f, {dz}f)); fp.SetValue(c, pos); fd.SetValue(c, dir); '
    'var setup = t.GetMethod("SetupPerspectiveCamera", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(Engine.Vector3), typeof(Engine.Vector3), typeof(Engine.Vector3) }, null); if (setup != null) setup.Invoke(c, new object[] { pos, dir, Engine.Vector3.UnitY }); '
    'return c.GetType().Name + " " + c.IsEntityControlEnabled + " " + System.FormattableString.Invariant($"{c.ViewPosition.X:0.00} {c.ViewPosition.Y:0.00} {c.ViewPosition.Z:0.00} | {c.ViewDirection.X:0.000} {c.ViewDirection.Y:0.000} {c.ViewDirection.Z:0.000}");')
ORIGIN_RULE = MAIN + ('var eye = pl.ComponentCreatureModel.EyePosition; var m = pl.ComponentBody.Matrix; var o = eye + m.Right * 0.3f - m.Up * 0.2f; '
    'return System.FormattableString.Invariant($"{o.X:0.00} {o.Y:0.00} {o.Z:0.00}");')
def debug_to(x, y, z, dx, dy, dz): return MAIN + (f'var w = pl.GameWidget; var d = w.FindCamera<Game.DebugCamera>(false); if (d == null) return "no DebugCamera"; w.ActiveCamera = d; '
    f'd.m_position = new Engine.Vector3({x}f, {y}f, {z}f); d.m_direction = Engine.Vector3.Normalize(new Engine.Vector3({dx}f, {dy}f, {dz}f)); d.SetupPerspectiveCamera(d.m_position, d.m_direction, Engine.Vector3.UnitY); '
    'return w.ActiveCamera.GetType().Name + " " + w.ActiveCamera.IsEntityControlEnabled + " " + System.FormattableString.Invariant($"{d.ViewPosition.X:0.00} {d.ViewPosition.Y:0.00} {d.ViewPosition.Z:0.00} | {d.ViewDirection.X:0.000} {d.ViewDirection.Y:0.000} {d.ViewDirection.Z:0.000}");')
CAM_DIR = MAIN + 'var c = pl.GameWidget.ActiveCamera; return System.FormattableString.Invariant($"{c.ViewPosition.X:0.00} {c.ViewPosition.Y:0.00} {c.ViewPosition.Z:0.00} {c.ViewDirection.X:0.000} {c.ViewDirection.Y:0.000} {c.ViewDirection.Z:0.000}");'
# The vanilla musket, loaded and cocked, in slot 0; fired through the engine's own aim completion from the active camera's ray.
GIVE_MUSKET = MAIN + ('int data = Game.MusketBlock.SetHammerState(Game.MusketBlock.SetLoadState(Game.MusketBlock.SetBulletType(0, Game.BulletBlock.BulletType.MusketBall), Game.MusketBlock.LoadState.Loaded), true); '
    'var inv = pl.ComponentMiner.Inventory; inv.RemoveSlotItems(0, inv.GetSlotCount(0)); inv.AddSlotItems(0, Game.Terrain.MakeBlockValue(Game.MusketBlock.Index, 0, data), 1); inv.ActiveSlotIndex = 0; return "musket";')
FIRE_MUSKET = MAIN + ('var c = pl.GameWidget.ActiveCamera; var sub = project.FindSubsystem<Game.SubsystemMusketBlockBehavior>(true); var ps = project.FindSubsystem<Game.SubsystemProjectiles>(true); int before = ps.m_projectiles.Count; '
    'sub.OnAim(new Engine.Ray3(c.ViewPosition, c.ViewDirection), pl.ComponentMiner, Game.AimState.Completed); var p = ps.m_projectiles.Count > before ? ps.m_projectiles[ps.m_projectiles.Count - 1] : null; if (p == null) return "no projectile"; '
    'var v = p.Velocity - pl.ComponentBody.Velocity; var d = Engine.Vector3.Normalize(v); return System.FormattableString.Invariant($"{p.Position.X:0.00} {p.Position.Y:0.00} {p.Position.Z:0.00} {d.X:0.000} {d.Y:0.000} {d.Z:0.000}");')
def cam_ray_to(tid): return MAIN + (f'var t = project.Entities.FirstOrDefault(e => e.Id == {tid})?.FindComponent<Game.ComponentBody>(); if (t == null) return "no target"; '
    'var cam = pl.GameWidget.ActiveCamera; var s = cam.ViewPosition; var bb = t.BoundingBox; var c = (bb.Min + bb.Max) * 0.5f; var d = Engine.Vector3.Normalize(c - s); '
    'return System.FormattableString.Invariant($"{s.X} {s.Y} {s.Z} {d.X} {d.Y} {d.Z}");')


def main(package, label):
    pkg = {"output-lite": m0.LITE, "output-full": m0.FULL}.get(package) or Path(package)
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"camera-fire-{label}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"camera-fire-{label}", "package": {pkg.name: sha(pkg)}, "steps": [], "checks": []}
    T0 = time.time()
    def step(name, value):
        R["steps"].append({"t": round(time.time() - T0, 1), "step": name, "value": value}); print(f"[{round(time.time() - T0, 1)}] {name}: {value}", flush=True); return value
    def check(name, ok, detail=""):
        R["checks"].append({"check": name, "ok": bool(ok), "detail": detail}); print(("PASS " if ok else "FAIL ") + name + " :: " + str(detail), flush=True)
    g = None
    try:
        g = m0.game("server", case_dir, [pkg] + ([BUMAN] if BUMAN.exists() else [])); R["engine"] = g.engine_info(); to_menu(g)
        m0.enter_world(g)
        step("window kept active", g.func(KEEP_ACTIVE))
        x, y, z = [int(v) for v in step("arena", build_arena(g)).split()]
        for attempt in range(10):
            g.func(teleport(x, y, z)); time.sleep(.7)
            if near(g.func(EYE), x, y, z): break
        g.func(STAND); time.sleep(1.5)
        step("shot debug", g.func(DEBUG_ON))
        eye = step("eye", g.func(EYE))
        def target(distance=7):
            eid, _ = g.func(spawn_target(0, 0, 0).replace("new Engine.Vector3(0f + 0.5f, 0f, 0f + 9.5f)", f"new Engine.Vector3({x + .5}f, {y}f, {z + .5 + distance}f)")).split(); time.sleep(1.5); return eid
        def remove(eid): g.func(PROJECT + f'var e = project.Entities.FirstOrDefault(q => q.Id == {eid}); if (e != null) project.RemoveEntity(e, true); return "ok";'); time.sleep(.3)
        def shot(): return g.func(LAST)
        def field(line, key):
            try: return line.split(f" {key} ")[1].split()[0]
            except IndexError: return ""
        def vec(s): return tuple(float(v) for v in s.split(",")) if s else (math.nan,) * 3   # no shot: every comparison fails, the run goes on
        def nums(parts): return tuple(float(v) for v in parts)   # space-separated FUNC output already split
        SPREAD = .05   # the gun's own random spread per shot (~0.3 deg for a standing AK), far below the 20-degree turn tested
        def on_line(point, origin, direction, ahead=1.5):
            # the engine starts a fired projectile a little ahead of its origin along its line (CanFireProjectile's fire position)
            d = tuple(a - b for a, b in zip(point, origin)); t = sum(a * b for a, b in zip(d, direction))
            perp = math.sqrt(max(0.0, sum(a * a for a in d) - t * t)); return -.05 < t < ahead and perp < .12
        def dist(a, b): return sum((p - q) ** 2 for p, q in zip(a, b)) ** .5
        def off_line(point, origin, direction):
            # distance of a point from a shot's line (the impact of a later shot along the same line lands on it, wherever it stops;
            # the gun's spread moves it by up to SPREAD per metre travelled)
            d = tuple(a - b for a, b in zip(point, origin)); t = sum(a * b for a, b in zip(d, direction))
            return math.sqrt(max(0.0, sum(a * a for a in d) - t * t))
        def fire_button(frames=3):
            g.func(CLEAR_LAST); g.func(button_frames(frames)); time.sleep(.8)
        def fire_key():
            g.func(CLEAR_LAST); g.cmd("KEYDOWN F"); time.sleep(.05); g.cmd("KEYUP F"); time.sleep(.6)
        def look_at(eid, pitches=None, line=LOOKLINE):
            """Turn the character until the shot's line (the eye along the character's look, in every view) meets the target: the
            eye's own look (yaw/pitch to the target's centre, sign conventions tried), or the pitches given. Returns (yaw, pitch)
            or None."""
            ex, ey, ez, dx, dy, dz = [float(v) for v in g.func(aim_facing(eid)).split()]
            yaw0, pitch0 = math.atan2(-dx, -dz), math.asin(max(-1.0, min(1.0, dy)))
            hits = []
            for yaw, pitch in pitches or [(sy * yaw0, sp * pitch0) for sy in (1, -1) for sp in (1, -1)]:
                g.func(look(yaw, pitch)); time.sleep(.4)
                if g.func(line) == f"body {eid}":   # the bullet's line
                    hits.append((yaw, pitch))
                    if not pitches: break
                elif hits: break   # past the far edge of the target: the run of hits is complete
            if not hits: step("crosshair never on the target", g.func(CROSSHAIR)); return None
            # The middle of the run (a sweep's first hit grazes the target's edge; recoil recovery would then miss).
            yaw, pitch = hits[len(hits) // 2]; g.func(look(yaw, pitch)); time.sleep(.4)
            return (round(yaw, 4), round(pitch, 4))

        # 1. First person: button and key binding.
        step("camera", g.func(camera("Fpp"))); step("fire key", g.func(FIRE_KEY))
        cow = target(); gid, mag = step("ak47", g.func(give("ak47"))).split(); time.sleep(2.5)
        aim = step("look at the target", look_at(cow)); yaw = aim[0] if aim else 0.0
        h0 = float(g.func(health(cow))); fire_button(); s1 = shot(); r1 = rounds(g, gid)
        check("first person: the button fires from the eye along the crosshair and hits", r1 == int(mag) - 1 and field(s1, "hit") == cow and dist(vec(field(s1, "ray")), vec(field(s1, "eye"))) < .05 and dist(vec(field(s1, "camera")), vec(field(s1, "eye"))) < .05, s1)
        fire_key(); s2 = shot(); r2 = rounds(g, gid)
        check("first person: the key binding fires from the eye and hits", r2 == r1 - 1 and field(s2, "hit") == cow, s2)
        check("the target was hurt twice", float(g.func(health(cow))) < h0, f"{h0} -> {g.func(health(cow))}")

        # 2. Free-flight camera: nothing fires by any input path.
        step("camera", g.func(camera("Fly"))); g.func(fly_to(x + 10.5, y + 3, z + 3.5, x + .5, y + 1, z + 7.5)); time.sleep(.5)
        cam = step("camera now", g.func(CAMERA)); h1 = float(g.func(health(cow))); r_before = rounds(g, gid)
        fire_button(); fire_key()
        ray = g.func(aim_at(cow)); g.func(CLEAR_LAST); g.cmd(f"HIT_RAY {ray}"); g.cmd(f"DIG_RAY {ray} 3"); time.sleep(.8)
        check("free-flight camera: the button, the key and an injected hit/dig ray fire nothing, spend nothing, hurt nothing",
              cam.startswith("FlyCamera False") and rounds(g, gid) == r_before and shot() == "" and float(g.func(health(cow))) == h1, f"{cam} rounds {r_before}->{rounds(g, gid)} shot '{shot()}' health {h1}->{g.func(health(cow))}")
        step("camera", g.func(camera("Fpp"))); time.sleep(.5)

        # 3. R8: hammer drawn under the button, camera to free flight and back before the release.
        remove(cow); cow = target(); gid, mag = step("revolver", g.func(give("revolver"))).split(); time.sleep(2.5)
        g.func(CLEAR_LAST); g.func(button(True)); time.sleep(.2); g.func(camera("Fly")); time.sleep(1.0); g.func(camera("Fpp")); time.sleep(.6); g.func(button(False)); time.sleep(.6)
        check("R8: a hammer drawn before a switch to free flight delivers no shot on the way back", rounds(g, gid) == int(mag) and shot() == "", f"rounds {rounds(g, gid)} of {mag}, shot '{shot()}'")

        # 4. Glock burst cut by the camera switch.
        remove(cow); cow = target(); gid, mag = step("glock18", g.func(give("glock18"))).split(); time.sleep(2.5); step("burst mode", g.func(BURST_ON))
        g.func(CLEAR_LAST); g.func(button(True)); g.func(camera("Fly")); g.func(button(False)); time.sleep(1.0); g.func(camera("Fpp")); time.sleep(.8)
        fired = int(mag) - rounds(g, gid)
        check("Glock burst: the camera switched on the next frame drops the rest of the burst", fired <= 1, f"{fired} round(s) fired of a 3-round burst; last shot '{shot()}'")

        # 5. Third person. The vanilla camera looks through the head, so its centre ray is the eye's line beyond the head:
        # the pitch is searched until the crosshair meets the target, then a two-block wall goes up between them.
        remove(cow); cow = target(); gid, mag = step("ak47", g.func(give("ak47"))).split(); time.sleep(2.5)
        step("camera", g.func(camera("Tpp"))); time.sleep(.5)
        aim = step("third-person pitch with the crosshair on the target", look_at(cow, [(yaw, s * math.radians(deg)) for s in (1, -1) for deg in range(3, 50, 3)]))
        g.func(wall(x, y, z, True)); time.sleep(.8); cam = step("camera now", g.func(CAMERA)); h2 = float(g.func(health(cow))); step("crosshair", g.func(CROSSHAIR))
        fire_button(); s5 = shot()
        check("third person: the shot leaves the eye along the character's look and stops at the wall between the character and the target",
              cam.startswith("TppCamera True") and rounds(g, gid) == int(mag) - 1 and field(s5, "hit") == "terrain" and dist(vec(field(s5, "ray")), vec(field(s5, "eye"))) < .05
              and field(s5, "aim") == "character-look" and float(g.func(health(cow))) == h2, s5)
        g.func(wall(x, y, z, False)); time.sleep(.8)
        if g.func(LOOKLINE) != f"body {cow}": step("look moved by the recoil: re-aimed", look_at(cow, [(yaw, s * math.radians(deg)) for s in (1, -1) for deg in range(3, 50, 3)]))
        fire_button(); s6 = shot()
        check("third person, wall gone: the same shot hits the target", field(s6, "hit") == cow and dist(vec(field(s6, "ray")), vec(field(s6, "eye"))) < .05, s6)
        check("third person: the tracer leaves the body's weapon, not the eye and not a stale first-person frame", .15 < dist(vec(field(s6, "tracer")), vec(field(s6, "eye"))) < 1.6, s6)
        # 5b. The camera raised over the wall (look pitched the other way), a hit ray injected from the camera at the target it
        # sees over the wall: the aim point is the target, the shot still leaves the eye and stops at the wall.
        g.func(look(yaw, -math.copysign(math.radians(25), aim[1] if aim else 1.0))); time.sleep(.5); g.func(wall(x, y, z, True)); time.sleep(.8)
        cam = step("camera now", g.func(CAMERA)); h3 = float(g.func(health(cow))); ray = step("camera ray at the target", g.func(cam_ray_to(cow)))
        g.func(CLEAR_LAST); g.cmd(f"HIT_RAY {ray}"); g.cmd(f"DIG_RAY {ray} 3"); time.sleep(.8); s5b = shot()
        rd = nums(ray.split()[3:6])
        check("third person: a hit ray injected from the camera over the wall moves nothing; the shot leaves the eye along the character's look (not the camera's ray) and the target is untouched",
              s5b != "" and dist(vec(field(s5b, "ray")), vec(field(s5b, "eye"))) < .05 and dist(vec(field(s5b, "dir")), vec(field(s5b, "look"))) < SPREAD and dist(vec(field(s5b, "dir")), rd) > .1
              and float(g.func(health(cow))) == h3, f"{s5b} camera {cam} injected {ray}")
        g.func(wall(x, y, z, False)); time.sleep(.8); ray = g.func(cam_ray_to(cow)); rd = nums(ray.split()[3:6]); g.func(CLEAR_LAST); g.cmd(f"HIT_RAY {ray}"); g.cmd(f"DIG_RAY {ray} 3"); time.sleep(.8); s5c = shot()
        check("third person, wall gone: the injected camera ray still changes nothing (the eye along the character's look)",
              s5c != "" and dist(vec(field(s5c, "ray")), vec(field(s5c, "eye"))) < .05 and dist(vec(field(s5c, "dir")), vec(field(s5c, "look"))) < SPREAD, s5c)

        # 6. Orbit camera: control enabled, camera away from the body.
        step("camera", g.func(camera("Orbit"))); time.sleep(.6); cam = step("camera now", g.func(CAMERA)); fire_button(); s7 = shot()
        check("orbit camera (free camera: the engine freezes the character's look): fires from the eye along the character's own look, not the orbit camera's direction",
              cam.startswith("OrbitCamera True") and s7 != "" and field(s7, "aim") == "character-look" and dist(vec(field(s7, "ray")), vec(field(s7, "eye"))) < .05
              and dist(vec(field(s7, "dir")), vec(field(s7, "look"))) < SPREAD and dist(vec(field(s7, "dir")), vec(field(s7, "camera-dir"))) > .2, s7)

        # 7. Creative flight, first person.
        step("camera", g.func(camera("Fpp"))); step("creative fly", g.func(CREATIVE_FLY)); time.sleep(.5); fire_button(); s8 = shot()
        check("creative flight in first person is not a free camera: fires", s8 != "" and field(s8, "camera-class") == "FppCamera", s8)
        g.func(STAND); time.sleep(.5)

        # 8. Dual Berettas, third person: left and right.
        remove(cow); cow = target(); gid, mag = step("elite", g.func(give("elite"))).split(); time.sleep(2.5); step("camera", g.func(camera("Tpp"))); time.sleep(.6)
        fire_button(); s9 = shot(); time.sleep(.3); fire_button(); s10 = shot()
        t9, t10 = vec(field(s9, "tracer")), vec(field(s10, "tracer")); e = vec(field(s9, "eye"))
        check("Dual Berettas in third person: two shots trace from two different gun muzzles near the body", field(s9, "bone") != field(s10, "bone") and dist(t9, t10) > .08 and .15 < dist(t9, e) < 1.6 and .15 < dist(t10, e) < 1.6, f"{s9} || {s10}")
        step("camera", g.func(camera("Fpp")))

        # 9. Debug camera (IsEntityControlEnabled = true): the character stands still and looks at the target; the camera is placed
        # elsewhere with the same direction, then translated only, then turned. The shot is the first-person crosshair's line
        # (the eye along the character's look) whatever the camera does.
        remove(cow); cow = target(); gid, mag = step("ak47", g.func(give("ak47"))).split(); time.sleep(2.5)
        aim = step("look at the target", look_at(cow)); base_dir = nums(g.func(CAM_DIR).split()[3:6]); origin_rule = nums(g.func(ORIGIN_RULE).split())
        eyev = nums(g.func(EYE).split())
        placed = step("debug camera 4 m to the side, same direction", g.func(debug_to(eyev[0] + 4, eyev[1] + 1, eyev[2], *base_dir))); time.sleep(.5)
        if placed.startswith("no DebugCamera"):
            check("DebugCamera available in this build", False, placed)
        else:
            fire_button(); d1 = shot()
            check("debug camera beside the character: the shot leaves the eye along the character's own look (not the camera's position or direction)",
                  d1 != "" and field(d1, "camera-class") == "DebugCamera" and field(d1, "aim") == "character-look" and dist(vec(field(d1, "ray")), eyev) < .05
                  and dist(vec(field(d1, "dir")), vec(field(d1, "look"))) < SPREAD, f"{d1} rule-origin {origin_rule}")
            step("debug camera translated 4 m up and 3 m back, same direction", g.func(debug_to(eyev[0] + 4, eyev[1] + 5, eyev[2] + 3, *base_dir))); time.sleep(.5)
            fire_button(); d2 = shot()
            check("translation only: origin and direction identical to the previous shot", d2 != "" and dist(vec(field(d2, "ray")), vec(field(d1, "ray"))) < .02 and dist(vec(field(d2, "dir")), vec(field(d1, "dir"))) < SPREAD, f"{d1} || {d2}")
            turned = (base_dir[0] * math.cos(.35) + base_dir[2] * math.sin(.35), base_dir[1], -base_dir[0] * math.sin(.35) + base_dir[2] * math.cos(.35))
            step("debug camera turned 20 degrees", g.func(debug_to(eyev[0] + 4, eyev[1] + 5, eyev[2] + 3, *turned))); time.sleep(.5)
            fire_button(); d3 = shot()
            check("turn only (the user's report: tracer and impact followed the camera's turns): origin and direction as before the turn (the character's look, not the turned camera's), the impact on the same line (the target killed by the earlier shots may no longer stop it)",
                  d3 != "" and dist(vec(field(d3, "ray")), eyev) < .05 and dist(vec(field(d3, "dir")), vec(field(d1, "dir"))) < SPREAD and dist(vec(field(d3, "dir")), turned) > .2
                  and field(d3, "impact") != "" and off_line(vec(field(d3, "impact")), vec(field(d1, "ray")), vec(field(d1, "dir"))) < .1 + SPREAD * dist(vec(field(d3, "impact")), vec(field(d1, "ray"))), f"{d1} || {d3} turned {turned}")
            check("debug camera: every tracer starts at the character (body muzzle or gun origin), never at the camera",
                  all(x != "" and dist(vec(field(x, "tracer")), vec(field(x, "eye"))) < 1.8 for x in (d1, d2, d3)), " || ".join(f"{field(x, 'tracer')} from {field(x, 'tracer-from')}" for x in (d1, d2, d3)))
            cam_now = nums(g.func(CAM_DIR).split())
            g.func(CLEAR_LAST); g.cmd("DIG_RAY " + " ".join(f"{v:.5f}" for v in cam_now[:3] + cam_now[3:6]) + " 6"); time.sleep(.8); d5 = shot()
            if d5: check("a native dig ray from the turned debug camera (mouse fire) still shoots along the character's own look", dist(vec(field(d5, "dir")), vec(field(d1, "dir"))) < SPREAD and field(d5, "input") == "dig", d5)
            else: step("native dig ray from the debug camera", "no shot (native gun fire not taken from dig on this platform)")
            # 10. The vanilla musket in the same setups: where its ball starts and heads, to compare with the CS shots above.
            step("musket", g.func(GIVE_MUSKET)); time.sleep(.5)
            origin_rule = nums(g.func(ORIGIN_RULE).split()); m1 = step("musket from the turned debug camera", g.func(FIRE_MUSKET))
            if m1.startswith("no projectile"): check("vanilla musket fired for comparison", False, m1)
            else:
                mo, md = nums(m1.split()[0:3]), nums(m1.split()[3:6])
                # Reference only: vanilla's own musket in a free camera heads along the camera (its aim ray); the CS guns no longer do.
                check("vanilla musket (reference): its ball starts on the line from eye + right 0.3 − up 0.2, heading along the debug camera's direction", on_line(mo, origin_rule, turned) and dist(md, turned) < SPREAD, f"musket {m1} rule-origin {origin_rule} camera-dir {turned}")
                check("CS shot leaves the eye, beside the vanilla ball's origin (CS: the first-person crosshair's line in every view; vanilla: musket origin along the camera; a deliberate difference)",
                      dist(vec(field(d3, "ray")), eyev) < .05 and not on_line(mo, vec(field(d3, "ray")), md), f"cs {field(d3, 'ray')} {field(d3, 'dir')} musket {m1}")
            step("camera", g.func(camera("Fpp"))); time.sleep(.4)
            time.sleep(1.0); fpp_dir = nums(g.func(CAM_DIR).split()[3:6]); origin_rule = nums(g.func(ORIGIN_RULE).split()); step("musket", g.func(GIVE_MUSKET)); time.sleep(.3)
            m2 = step("musket in first person", g.func(FIRE_MUSKET))
            if not m2.startswith("no projectile"):
                check("vanilla musket in first person: start on the same origin line, direction = the camera's", on_line(nums(m2.split()[0:3]), origin_rule, fpp_dir) and dist(nums(m2.split()[3:6]), fpp_dir) < SPREAD, f"musket {m2} rule-origin {origin_rule} camera-dir {fpp_dir}")
            gid, mag = step("ak47", g.func(give("ak47"))).split(); time.sleep(1.5); origin_rule = nums(g.func(ORIGIN_RULE).split()); fpp_dir = nums(g.func(CAM_DIR).split()[3:6]); fire_button(); d4 = shot()
            check("CS in first person: origin = the eye (the crosshair's line, user decision 2026-10-01), direction = the camera's; the tracer still leaves the drawn muzzle",
                  d4 != "" and dist(vec(field(d4, "ray")), vec(field(d4, "eye"))) < .05 and dist(vec(field(d4, "dir")), fpp_dir) < SPREAD and field(d4, "tracer-from") == "viewmodel", f"{d4} rule-origin {origin_rule}")

        # 11. BUMANCAMERA (third-party mod camera), when the mod is present: the same translation/turn protocol as the debug camera.
        info = step("BumanCamera", g.func(BUMAN_INFO))
        if info.startswith("no BumanCamera") or info.startswith("no main"):
            check("BumanCamera present in this runtime (mod copied into Mods)", False, info + (" | mod file missing on this machine" if not BUMAN.exists() else ""))
        else:
            gid, mag = step("ak47", g.func(give("ak47"))).split(); time.sleep(1.5)
            step("look at the target", look_at(cow)); base_dir = nums(g.func(CAM_DIR).split()[3:6]); origin_rule = nums(g.func(ORIGIN_RULE).split()); eyev = nums(g.func(EYE).split())
            placed = step("BumanCamera 4 m to the side, same direction", g.func(buman_to(eyev[0] + 4, eyev[1] + 1, eyev[2], *base_dir))); time.sleep(.5)
            controlled = placed.split()[1] == "True" if len(placed.split()) > 1 else None
            fire_button(); b1 = shot()
            if controlled:
                check("BumanCamera beside the character: the shot leaves the eye along the character's own look", b1 != "" and field(b1, "aim") == "character-look" and dist(vec(field(b1, "ray")), eyev) < .05 and dist(vec(field(b1, "dir")), vec(field(b1, "look"))) < SPREAD, f"{b1} rule-origin {origin_rule} camera {placed}")
                step("BumanCamera translated 4 m up and 3 m back, same direction", g.func(buman_to(eyev[0] + 4, eyev[1] + 5, eyev[2] + 3, *base_dir))); time.sleep(.5); fire_button(); b2 = shot()
                check("BumanCamera translation only: origin and direction identical", b2 != "" and dist(vec(field(b2, "ray")), vec(field(b1, "ray"))) < .02 and dist(vec(field(b2, "dir")), vec(field(b1, "dir"))) < SPREAD, f"{b1} || {b2}")
                turned = (base_dir[0] * math.cos(.35) + base_dir[2] * math.sin(.35), base_dir[1], -base_dir[0] * math.sin(.35) + base_dir[2] * math.cos(.35))
                step("BumanCamera turned 20 degrees", g.func(buman_to(eyev[0] + 4, eyev[1] + 5, eyev[2] + 3, *turned))); time.sleep(.5); fire_button(); b3 = shot()
                check("BumanCamera turn only: nothing changes (origin, direction and impact as before the turn; not the turned camera's direction)",
                      b3 != "" and dist(vec(field(b3, "ray")), eyev) < .05 and dist(vec(field(b3, "dir")), vec(field(b1, "dir"))) < SPREAD and dist(vec(field(b3, "dir")), turned) > .2
                      and off_line(vec(field(b3, "impact")), vec(field(b1, "ray")), vec(field(b1, "dir"))) < .1 + SPREAD * dist(vec(field(b3, "impact")), vec(field(b1, "ray"))), f"{b1} || {b3} turned {turned}")
                check("BumanCamera: every tracer starts at the character, never at the camera", all(x != "" and dist(vec(field(x, "tracer")), vec(field(x, "eye"))) < 1.8 for x in (b1, b2, b3)),
                      " || ".join(f"{field(x, 'tracer')} from {field(x, 'tracer-from')}" for x in (b1, b2, b3)))
            else:
                check("BumanCamera reports IsEntityControlEnabled=false: no shot by the engine's own control rule (same as FlyCamera)", b1 == "" and rounds(g, gid) == int(mag), f"camera {placed} shot '{b1}' rounds {rounds(g, gid)}/{mag}")
            step("camera", g.func(camera("Fpp")))
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None: R["gameErrors"] = g.errors(); g.close()
        m0.RUNTIME_OWNER.release()
    RESULTS.mkdir(parents=True, exist_ok=True)
    out = RESULTS / f"camera-fire-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    failed = [c for c in R["checks"] if not c["ok"]]
    print(f"{len(R['checks'])} checks, {len(failed)} failed" + (f"; failure: {R['failure']}" if "failure" in R else "") + f"; errors {len(R.get('gameErrors', []))}")
    return 0 if not failed and "failure" not in R else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2]))
