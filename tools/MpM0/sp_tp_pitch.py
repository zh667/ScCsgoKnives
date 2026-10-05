"""Third-person gun pitch probe (2026-10-01 user report: "在第三视角下，我们人物模型拿枪的姿势一直都是水平的，我们的枪没有根据
当时的第一视角移动视角而变换角度，所以才导致看起来枪线和枪口对不齐").

On the isolated 1.9.3.1 copy (M0_ENGINE=131), one game: the player in third person with an AK-47, the look pitched level, up
and down. For each pitch: the player model (type, skinned or not), whether ScThirdPerson posed it this frame, the look angles,
the eye's forward, the third-person weapon's world axes and muzzle, the fist; then one shot (tracer start, impact, the
angle between the tracer and the barrel). Usage: sp_tp_pitch.py <label> <package> [<package> ...] ("providers" adds the
player's NekoMeko Model 1.1 and Neorxna 1.4 copies; output-full / output-lite / output-agents).
"""
import json, shutil, sys, time
from pathlib import Path

import m0
from m0 import RESULTS, RUNS, sha, to_menu
from mp_m1 import MAIN, KEEP_ACTIVE
from sp_agents_hint import PROVIDERS
import sp_camera_fire as cf
from sp_camera_fire import DEBUG_ON, FIRE_KEY, LAST, CLEAR_LAST, give, camera, look

STATE = MAIN + r'''
var human = pl.Entity.FindComponent<Game.ComponentHumanModel>();
var table = typeof(Game.ScThirdPerson).GetField("s_states", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
var args = new object[] { human, null }; bool found = human != null && (bool)table.GetType().GetMethod("TryGetValue").Invoke(table, args);
var st = args[1]; System.Func<string, object> F = n => st?.GetType().GetField(n)?.GetValue(st);
var eye = pl.ComponentCreatureModel.EyePosition; var look = Engine.Matrix.CreateFromQuaternion(pl.ComponentCreatureModel.EyeRotation).Forward;
string V(Engine.Vector3 v) => System.FormattableString.Invariant($"{v.X:0.000},{v.Y:0.000},{v.Z:0.000}");
float Pitch(Engine.Vector3 v) { v = Engine.Vector3.Normalize(v); return System.MathF.Asin(System.Math.Clamp(v.Y, -1f, 1f)); }
string world = "none", muzzle = "none", fist = "none", barrel = "none"; float barrelPitch = float.NaN;
if (found && F("World") is Engine.Matrix w && F("Weapon") is object weapon) {
  var grip = (Engine.Vector3)weapon.GetType().GetField("GripRight").GetValue(weapon); var mz = (Engine.Vector3)weapon.GetType().GetField("Muzzle").GetValue(weapon);
  var gw = Engine.Vector3.Transform(grip, w); var mw = Engine.Vector3.Transform(mz, w); var b = mw - gw;
  world = "fwd " + V(w.Forward) + " up " + V(w.Up) + " right " + V(w.Right); muzzle = V(mw); barrel = V(Engine.Vector3.Normalize(b)); barrelPitch = Pitch(b);
  fist = F("Fist") is Engine.Vector3 fv ? V(fv) : "none";
}
return System.Text.Json.JsonSerializer.Serialize(new {
  model = human?.Model?.Bones.Count, modelType = human?.GetType().FullName, skin = human?.Model?.HasSkin, posed = found, valid = F("Valid"), frame = F("Frame"), now = Engine.Time.FrameIndex,
  asset = F("Asset"), legacy = F("Legacy"), lookAngles = System.FormattableString.Invariant($"{pl.ComponentLocomotion.LookAngles.X:0.000},{pl.ComponentLocomotion.LookAngles.Y:0.000}"), lookPitch = Pitch(look), look = V(look), eye = V(eye),
  world, muzzle, barrel, barrelPitch, fist, camera = pl.GameWidget.ActiveCamera.GetType().Name });
'''


WISPS = MAIN + ('var guns = project.FindSubsystem<Game.SubsystemScGunBlockBehavior>(true); var F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance; '
    'var t = typeof(Game.SubsystemScGunBlockBehavior); var list = (System.Collections.IList)t.GetField("m_wisps", F).GetValue(guns); '
    'string Has(string n) => t.GetField(n, F).GetValue(guns) != null ? "yes" : "no"; '
    'return "wisps " + list.Count + " energy " + Has("m_wispEnergy") + " smoke " + Has("m_wispSmoke") + " addLut " + Has("m_tracerAddLut") + " blendLut " + Has("m_tracerBlendLut");')


def tracer_vs_barrel(s):
    """Degrees between the tracer (its start to the impact) and the third-person weapon's forward axis before the shot."""
    import math
    def field(k):
        try: return [float(v) for v in s["shot"].split(f" {k} ")[1].split()[0].split(",")]
        except Exception: return None
    t, i = field("tracer"), field("impact")
    try: fwd = [float(v) for v in s["world"].split("fwd ")[1].split()[0].split(",")]
    except Exception: fwd = None
    if not (t and i and fwd): return None
    d = [b - a for a, b in zip(t, i)]; n = math.sqrt(sum(x * x for x in d)) or 1
    return round(math.degrees(math.acos(max(-1, min(1, sum(x / n * f for x, f in zip(d, fwd)))))), 2)


def main(label, *packages):
    def resolve(p):
        if p == "providers": return [x for x in PROVIDERS if x.exists()]
        return [{"output-lite": m0.LITE, "output-full": m0.FULL, "output-agents": m0.LITE.with_name("[API1.9]CS武器1.4.0-探员包.scmod")}.get(p) or Path(p)]
    pkgs = [x for p in packages for x in resolve(p)]
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"tp-pitch-{label}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"tp-pitch-{label}", "packages": {p.name: sha(p) for p in pkgs}, "samples": []}
    g = None
    try:
        g = m0.game("server", case_dir, pkgs); R["engine"] = g.engine_info(); to_menu(g); m0.enter_world(g)
        g.func(KEEP_ACTIVE); x, y, z = [int(v) for v in cf.build_arena(g).split()]
        for _ in range(5):
            g.func(cf.teleport(x, y, z)); time.sleep(.7)
            if cf.near(g.func(cf.EYE), x, y, z): break
        g.func(cf.STAND); time.sleep(1.5); g.func(DEBUG_ON); g.func(FIRE_KEY); print("gun", g.func(give("ak47")), flush=True); time.sleep(2.5)
        print("camera", g.func(camera("Tpp")), flush=True); time.sleep(1)
        for pitch in (0.0, 0.5, -0.5, 0.9):
            g.func(look(0.0, pitch)); time.sleep(1.0)
            s = json.loads(g.func(STATE, timeout=60)); s["setPitch"] = pitch
            g.func(CLEAR_LAST); g.cmd("KEYDOWN F"); time.sleep(.05); g.cmd("KEYUP F"); time.sleep(.8); s["shot"] = g.func(LAST)
            s["tracerBarrelDeg"] = tracer_vs_barrel(s)
            R["samples"].append(s); print(json.dumps(s, ensure_ascii=False)[:900], flush=True)
        # Round 9 wisp: an AWP shot in third and in first person; the wisp list, its textures, and frames to look at.
        from sp_smoke_view import snap
        R["wisp"] = []
        print("awp", g.func(give("awp")), flush=True); time.sleep(3); g.func(look(0.0, 0.05)); time.sleep(.5)
        # In open air (25 blocks up, flying) so every run sees the line against the sky, not whatever the spawn put ahead.
        g.func(cf.teleport(x, y + 25, z)); time.sleep(1.5); g.func(look(0.0, 0.05)); time.sleep(.5)
        ex, ey, ez = [float(v) for v in g.func(cf.EYE).split()]
        for cam in ("Tpp", "Fpp", "Side"):
            if cam == "Side":   # a debug camera 6 m to the right and 8 m ahead, looking back across the shot line
                g.func(camera("Fpp")); time.sleep(.5); print("side", g.func(cf.debug_to(ex + 6, ey + 1, ez - 8, -6, -1, -6)), flush=True)
            else: g.func(camera(cam))
            time.sleep(1.5)
            g.cmd("KEYDOWN F"); time.sleep(.05); g.cmd("KEYUP F"); time.sleep(.12)
            entry = {"camera": cam, "state": g.func(WISPS), "frames": []}
            for at in (0.0, .6, 1.0):
                time.sleep(at)
                shot = snap(g, f"r9-wisp-{label}-{cam}-{len(entry['frames'])}", ((0, 0, 0), (1, 1, 1)))[0]
                time.sleep(.3)
                kept = []
                for f in Path(shot).parent.glob(Path(shot).name + "*"):   # the runtime's ScreenCapture is reset by the next run
                    (case_dir / "frames").mkdir(exist_ok=True); shutil.copy2(f, case_dir / "frames" / f.name); kept.append(str(case_dir / "frames" / f.name))
                entry["frames"].append(kept[0] if kept else shot)
            entry["after"] = g.func(WISPS); R["wisp"].append(entry); print("wisp", json.dumps(entry, ensure_ascii=False), flush=True)
            time.sleep(2.5)
        R["gameErrors"] = g.errors()
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None: g.close()
        m0.RUNTIME_OWNER.release()
    out = RESULTS / f"tp-pitch-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    R["maxTracerBarrelDeg"] = max((x["tracerBarrelDeg"] for x in R["samples"] if x.get("tracerBarrelDeg") is not None), default=None)
    print(f"max tracer/barrel angle {R['maxTracerBarrelDeg']} deg; samples {len(R['samples'])}; errors {len(R.get('gameErrors', []))}; failure {R.get('failure')}; {out}", flush=True)
    return 1 if R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], *sys.argv[2:]))
