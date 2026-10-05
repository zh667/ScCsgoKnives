"""deathmatch round 6 follow-up (2026-10-05, the user: "曳光弹倒是没看到你做"): are the tracers drawn, and how do they look?
The gun-visuals frames (a wall 9 m ahead, a quarter speed) show CS2's tracers as a bright orange dash on every shot and ours
not at all. Here: a wall further away (default 30 m), the game slowed further (default 0.05), the window's picture as fast as
PrintWindow gives it (JPEG at full chroma: a one-pixel line keeps its colour), and the gun subsystem's own tracer and wisp lists read while the trigger is held - so a missing tracer
and a tracer too thin or too brief to see are told apart.

Usage: sp_tracer_probe.py <label> <core package | stage-<tag>-full> [gun+gun] [factor] [wall metres]
Isolated 1.9.3.1 copy (M0_ENGINE=131), new creative world; the player's own Mods and worlds are never used.
"""
import json, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
import m0
from m0 import RESULTS, RUNS, sha, to_menu, poll
from mp_m1 import MAIN, KEEP_ACTIVE, build_arena, teleport
from sp_deathmatch import package, CREATIVE, STAND_HERE, FPP
from sp_camera_fire import give, look
from sp_gun_visuals import window_of, picture, time_factor, POS

def wall(distance): return MAIN + ('var t = project.FindSubsystem<Game.SubsystemTerrain>(true); var p = pl.ComponentBody.Position; int x = (int)System.MathF.Floor(p.X), y = (int)System.MathF.Floor(p.Y), z = (int)System.MathF.Floor(p.Z); '
    f'for (int dx = -8; dx <= 8; dx++) for (int dy = 0; dy <= 8; dy++) t.ChangeCell(x + dx, y + dy, z - {int(distance)}, Game.Terrain.MakeBlockValue(Game.GraniteBlock.Index)); return x + " " + y + " " + z;')
# the gun subsystem's tracer and wisp lists and its per-gun shot counts (what QueueTracer counted)
LISTS = MAIN + ('var sub = project.FindSubsystem<Game.SubsystemScGunBlockBehavior>(true); var f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance; '
    'var tr = (System.Collections.ICollection)typeof(Game.SubsystemScGunBlockBehavior).GetField("m_tracers", f).GetValue(sub); '
    'var wi = (System.Collections.ICollection)typeof(Game.SubsystemScGunBlockBehavior).GetField("m_wisps", f).GetValue(sub); '
    'var sc = (System.Collections.Generic.Dictionary<string, int>)typeof(Game.SubsystemScGunBlockBehavior).GetField("m_shotCounts", f).GetValue(sub); '
    'return "tracers " + tr.Count + " wisps " + wi.Count + " shots " + string.Join(",", sc.Select(k => k.Key + ":" + k.Value)) + " profile " + Game.KnifeTuning.GunProfile;')
AUTO = {"ak47", "m4a4", "m4a1s", "galilar", "famas", "aug", "sg556", "p90", "mp9", "mp7", "mac10", "ump45", "bizon", "negev", "m249"}
# one AK tracer put straight into the gun subsystem's list: "along" from in front of the camera (a little right and down, where a
# muzzle is) along the view for 30 m; "across" 8 m ahead from 4 m left to 4 m right of the view - whether the tracer pass draws
# anything at all, apart from when and where shots queue them
def inject(kind, gun="ak47", reach=30): return MAIN + ('var sub = project.FindSubsystem<Game.SubsystemScGunBlockBehavior>(true); var f = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance; '
    'var c = pl.GameWidget.ActiveCamera; var right = Engine.Vector3.Normalize(Engine.Vector3.Cross(c.ViewDirection, c.ViewUp)); '
    + ('var start = c.ViewPosition + c.ViewDirection * 0.6f + right * 0.18f - c.ViewUp * 0.14f; var end = c.ViewPosition + c.ViewDirection * ' + f"{float(reach)}" + 'f; ' if kind == "along" else
       'var start = c.ViewPosition + c.ViewDirection * 8f - right * 4f; var end = c.ViewPosition + c.ViewDirection * 8f + right * 4f; ')
    + 'var dir = Engine.Vector3.Normalize(end - start); float len = Engine.Vector3.Distance(start, end); '
    'var shotType = typeof(Game.SubsystemScGunBlockBehavior).GetNestedType("TracerShot", System.Reflection.BindingFlags.NonPublic); '
    'var time = project.FindSubsystem<Game.SubsystemTime>(true); '
    'var shot = System.Activator.CreateInstance(shotType, new object[] { start, dir, len, time.GameTime, Game.Cs2Effects.Get("' + gun + '").Tracer }); '
    'var list = (System.Collections.IList)typeof(Game.SubsystemScGunBlockBehavior).GetField("m_tracers", f).GetValue(sub); list.Add(shot); return "injected " + list.Count + " at " + time.GameTime + " camera " + c.GetType().Name;')


def main(label, core, guns="ak47+awp+p90", factor="0.05", distance="30"):
    pkgs = [package(core)]
    names = guns.replace("+", ",").split(",")
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"tracer-probe-{label}-{stamp}"; (case_dir / "frames").mkdir(parents=True)
    R = {"case": f"tracer-probe-{label}", "packages": {p.name: sha(p) for p in pkgs}, "guns": {}, "steps": [], "factor": float(factor), "wall": float(distance)}
    g = None
    def step(name, value=None):
        R["steps"].append({"step": name, "value": value}); print(f"{name}: {value}", flush=True); return value
    try:
        g = m0.game("server", case_dir, pkgs); R["engine"] = g.engine_info(); to_menu(g)
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Play"', 120, m)
        m = g.mark(); g.cmd("CLICK_WIDGET NewWorld"); g.wait('Entered screen "NewWorld"', 120, m)
        step("creative world", poll(g, CREATIVE, lambda v: v == "Creative", 20))
        m = g.mark(); g.cmd("CLICK_WIDGET Play"); g.wait('Entered screen "Player"', 600, m)
        m = g.mark(); g.cmd("CLICK_WIDGET PlayButton"); g.wait('Entered screen "Game"', 600, m)
        step("player", poll(g, m0.PLAYER_SPAWNED, lambda v: v == "True", 240)); g.func(KEEP_ACTIVE)
        x, y, z = [int(v) for v in step("arena", build_arena(g)).split()]
        g.func(teleport(x, y, z + 12)); g.func(STAND_HERE); time.sleep(1.5)
        step("wall", g.func(wall(distance))); g.func(FPP); time.sleep(.6)
        h = window_of(g.proc.pid); step("window", bool(h))
        cx, cy = [int(v) // 2 for v in g.func(MAIN + 'return Engine.Window.Size.X + " " + Engine.Window.Size.Y;').split()]
        # each gun's own tracer injected along the view, the game at 0.005 of its speed, so its flight lasts seconds on screen
        R["injected"] = {}
        for name in names:
            g.func(give(name)); time.sleep(1.2); g.func(FPP); g.func(look(0.0, 0.03)); g.func(time_factor(0.005)); time.sleep(.8)
            phase = f"inject-{name}"; d = case_dir / "frames" / phase; d.mkdir(exist_ok=True); lists = [step(phase, g.func(inject("along", name, float(distance) - .5)))]; t0 = time.time(); k = 0
            while time.time() - t0 < 7:
                im = picture(h); im.save(d / f"fp_{k:03d}.jpg", quality=95, subsampling=0); k += 1
                if k % 10 == 0: lists.append([round(time.time() - t0, 1), g.func(LISTS)])
            g.func(time_factor(1)); time.sleep(.5); R["injected"][phase] = {"frames": k, "lists": lists}; step(phase + " done", R["injected"][phase])
        slow = 1 / max(.01, float(factor))
        for name in names:
            d = case_dir / "frames" / name; d.mkdir(exist_ok=True)
            step(f"{name} given", g.func(give(name))); time.sleep(1.2)
            g.func(FPP); g.func(look(0.0, 0.03)); g.func(time_factor(float(factor))); time.sleep(.8)
            entry = {"before": g.func(LISTS), "during": []}
            g.cmd(f"MOUSEMOVE {cx} {cy}"); time.sleep(.1)
            saved = []; t0 = time.time(); held = True
            # pictures from the press on: a semi-automatic gun fires as the button goes down (an earlier version waited out
            # the press before the first picture and never saw the AWP's tracer)
            hold = (.45 if name in AUTO else .06) * slow
            g.cmd(f"MOUSEDOWN Left {cx} {cy}")
            while time.time() - t0 < .9 * slow:
                if held and time.time() - t0 > hold: g.cmd(f"MOUSEUP Left {cx} {cy}"); held = False
                im = picture(h); p = d / f"fp_{len(saved):03d}.jpg"; im.save(p, quality=95, subsampling=0); saved.append(p.name)
                if len(saved) % 6 == 0: entry["during"].append([round(time.time() - t0, 2), g.func(LISTS)])
            g.cmd(f"MOUSEUP Left {cx} {cy}")
            entry["after"] = g.func(LISTS); entry["frames"] = len(saved)
            g.func(time_factor(1)); R["guns"][name] = entry; step(f"{name} lists", {k: entry[k] for k in ["before", "after", "frames"]} | {"during": entry["during"][:12]})
        R["gameErrors"] = g.errors()[:40]
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None: g.close()
        m0.RUNTIME_OWNER.release()
    out = RESULTS / f"tracer-probe-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print(f"guns {len(R['guns'])}; failure {R.get('failure')}; {out}", flush=True)
    return 1 if R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:]))
