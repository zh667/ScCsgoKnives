"""Diagnosis of the intermittent camera-fire button failures (2026-10-01, r7a run 1: every button-fired check of one session
failed, the key binding fired; the next two sessions passed 27/27).

Hypothesis: sp_camera_fire's button_frames sets SubsystemScGunBlockBehavior.SetFireButton directly, below the product's
input layer, while SubsystemScKnifeBlockBehavior writes SetFireButton(player, <touch panel Fire pressed>) on every update.
Both subsystems have UpdateOrder.Default and the engine breaks that tie with u1.GetHashCode() - u2.GetHashCode()
(SubsystemUpdate.Comparer, decompiled 1.9.3.1), so their order is random per session: when the knife subsystem updates first
it overwrites the injected press before the gun reads it, for the whole session. A real touch keeps the panel pressed.

Check: one session; the two subsystems are put in each order in the engine's sorted update list (the order is read back
before every shot; the list is only re-sorted when updateables are added or removed); a direct button press and a key
press are fired in each order. Expected: gun first → both fire; knife first → the direct button does not fire, the key does.
Usage: sp_button_order.py <package> <label>
"""
import json, sys, time
from pathlib import Path

import m0
from m0 import RESULTS, RUNS, sha, to_menu
from mp_m1 import MAIN, KEEP_ACTIVE, PROJECT
import sp_camera_fire as cf
from sp_camera_fire import DEBUG_ON, LAST, CLEAR_LAST, button_frames, give, FIRE_KEY, camera

ORDER = PROJECT + ('var su = project.FindSubsystem<Game.SubsystemUpdate>(true); var l = su.m_sortedUpdateables; '
                   'int g = l.FindIndex(u => u is Game.SubsystemScGunBlockBehavior), k = l.FindIndex(u => u is Game.SubsystemScKnifeBlockBehavior); return g + " " + k;')
def set_order(gun_first): return PROJECT + (
    'var su = project.FindSubsystem<Game.SubsystemUpdate>(true); var l = su.m_sortedUpdateables; '
    'int g = l.FindIndex(u => u is Game.SubsystemScGunBlockBehavior), k = l.FindIndex(u => u is Game.SubsystemScKnifeBlockBehavior); '
    f'if ((g < k) != {"true" if gun_first else "false"}) {{ var t = l[g]; l[g] = l[k]; l[k] = t; }} '
    'g = l.FindIndex(u => u is Game.SubsystemScGunBlockBehavior); k = l.FindIndex(u => u is Game.SubsystemScKnifeBlockBehavior); return g + " " + k;')


def main(package, label):
    pkg = {"output-lite": m0.LITE, "output-full": m0.FULL}.get(package) or Path(package)
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"button-order-{label}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"button-order-{label}", "package": {pkg.name: sha(pkg)}, "steps": [], "checks": []}
    def step(name, value): R["steps"].append({"step": name, "value": value}); print(f"{name}: {value}", flush=True); return value
    def check(name, ok, detail=""): R["checks"].append({"check": name, "ok": bool(ok), "detail": detail}); print(("PASS " if ok else "FAIL ") + name + " :: " + str(detail)[:300], flush=True)
    g = None
    try:
        g = m0.game("server", case_dir, [pkg]); R["engine"] = g.engine_info(); to_menu(g); m0.enter_world(g)
        # camera-fire's own setup (arena, standing in it, first person), so the shots start from its verified state.
        g.func(KEEP_ACTIVE); x, y, z = [int(v) for v in step("arena", cf.build_arena(g)).split()]
        for _ in range(5):
            g.func(cf.teleport(x, y, z)); time.sleep(.7)
            if cf.near(g.func(cf.EYE), x, y, z): break
        g.func(cf.STAND); time.sleep(1.5); g.func(DEBUG_ON)
        step("camera", g.func(camera("Fpp"))); step("fire key", g.func(FIRE_KEY)); step("ak47", g.func(give("ak47"))); time.sleep(2.5)
        g.func(CLEAR_LAST); g.cmd("KEYDOWN F"); time.sleep(.05); g.cmd("KEYUP F"); time.sleep(.6)
        if not g.func(LAST): raise RuntimeError("prerequisite: the key binding does not fire after camera-fire's setup")
        step("session's own order (gun index, knife index)", g.func(ORDER))
        for gun_first in (True, False, True, False):
            name = "gun first" if gun_first else "knife first"
            step(f"{name}: order set", g.func(set_order(gun_first))); time.sleep(.5)
            before = g.func(ORDER)
            g.func(CLEAR_LAST); g.func(button_frames(3)); time.sleep(.8); button = g.func(LAST)
            after = g.func(ORDER)
            g.func(CLEAR_LAST); g.cmd("KEYDOWN F"); time.sleep(.05); g.cmd("KEYUP F"); time.sleep(.6); key = g.func(LAST)
            gi, ki = (int(x) for x in before.split())
            check(f"{name}: order held during the shot ({before} -> {after}); direct button {'fires' if gun_first else 'does not fire'}, key fires",
                  before == after and (gi < ki) == gun_first and (bool(button) == gun_first) and bool(key),
                  f"button {'shot' if button else 'none'}, key {'shot' if key else 'none'}")
        R["gameErrors"] = g.errors()
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None: R.setdefault("gameErrors", g.errors()); g.close()
        m0.RUNTIME_OWNER.release()
    failed = [c for c in R["checks"] if not c["ok"]]
    out = RESULTS / f"button-order-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print(f"{len(R['checks'])} checks, {len(failed)} failed; errors {len(R.get('gameErrors', []))}; failure {R.get('failure')}; {out}", flush=True)
    return 1 if failed or R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2]))
