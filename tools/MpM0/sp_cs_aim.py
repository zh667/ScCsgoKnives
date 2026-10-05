"""The CS player appearance aiming with the look (round 11, 2026-10-01): numbers only, on the isolated 1.9.3.1 copy.

The player model is switched to the CS CT appearance (NekoMeko model key zh667.cs.ct, found by reflection), an AK-47 in
hand, third person; for look pitch 0 / +0.5 / -0.5 / +1.0: the drawn weapon's barrel direction (CsPlayerItems'
RootWorld × inverse view, forward axis) against the look, the reported muzzle, one shot's tracer start; and a frame each.
Usage: sp_cs_aim.py <label> <package> [<package> ...]   ("providers" adds the user's NekoMeko Model 1.1 and Neorxna 1.4)
"""
import json, shutil, sys, time
from pathlib import Path

import m0
from m0 import RESULTS, RUNS, sha, to_menu
from mp_m1 import MAIN, KEEP_ACTIVE
from sp_agents_hint import PROVIDERS
import sp_camera_fire as cf
from sp_camera_fire import DEBUG_ON, FIRE_KEY, LAST, CLEAR_LAST, give, camera, look
from sp_smoke_view import snap

MEMBERS = MAIN + r'''
object c = pl.Entity.Components.FirstOrDefault(x => x.GetType().Name == "ComponentCsPlayerAppearance");
if (c == null) return "no ComponentCsPlayerAppearance: " + string.Join(",", pl.Entity.Components.Select(x => x.GetType().Name));
var F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.FlattenHierarchy;
var t = c.GetType().BaseType;
object P(string n) => c.GetType().GetProperty(n)?.GetValue(c);
string Members(System.Type ty) => string.Join(" ", ty.GetMembers(F).Where(m => m.Name.IndexOf("Key", System.StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("SetRes", System.StringComparison.OrdinalIgnoreCase) >= 0 || m.Name.IndexOf("SetModel", System.StringComparison.OrdinalIgnoreCase) >= 0).Select(m => m.MemberType + ":" + m.Name));
return "key=" + P("ModelKey") + " isCs=" + P("IsCs") + " | " + Members(t);
'''
SET_CS = MAIN + r'''
object c = pl.Entity.Components.FirstOrDefault(x => x.GetType().Name == "ComponentCsPlayerAppearance");
if (c == null) return "no ComponentCsPlayerAppearance";
var F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
object P(string n) => c.GetType().GetProperty(n)?.GetValue(c);
string done = "none";
for (var ty = c.GetType(); ty != null && done == "none"; ty = ty.BaseType) {
  var m = ty.GetMethods(F).FirstOrDefault(x => x.Name == "SetResModel" && x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == typeof(string));
  if (m != null) { m.Invoke(c, new object[] { "zh667.cs.ct" }); done = "SetResModel"; break; }
  var p = ty.GetProperty("ModelKey", F);
  if (p != null && p.CanWrite) { p.SetValue(c, "zh667.cs.ct"); done = "ModelKey property"; break; }
  var f = ty.GetFields(F).FirstOrDefault(x => x.FieldType == typeof(string) && x.Name.IndexOf("key", System.StringComparison.OrdinalIgnoreCase) >= 0);
  if (f != null) { f.SetValue(c, "zh667.cs.ct"); done = "field " + f.Name; break; }
}
return done + " -> key=" + P("ModelKey") + " isCs=" + P("IsCs");
'''
STATE = MAIN + r'''
var human = pl.Entity.FindComponent<Game.ComponentHumanModel>();
object c = pl.Entity.Components.FirstOrDefault(x => x.GetType().Name == "ComponentCsPlayerAppearance");
object P(object o, string n) => o?.GetType().GetProperty(n)?.GetValue(o);
var cam = pl.GameWidget.ActiveCamera;
string V(Engine.Vector3 v) => System.FormattableString.Invariant($"{v.X:0.000},{v.Y:0.000},{v.Z:0.000}");
float Pitch(Engine.Vector3 v) { v = Engine.Vector3.Normalize(v); return System.MathF.Asin(System.Math.Clamp(v.Y, -1f, 1f)); }
var weapon = Game.ScThirdPersonWeapon.For("ak47", Game.ScGunNativeMesh.Resolve("ak47", 0, out _, out _) != null);
var actions = P(P(c, "Pose"), "Actions");
var W = (Engine.Matrix)actions.GetType().GetMethod("RootWorld").Invoke(actions, new object[] { weapon, human.AbsoluteBoneTransformsForCamera }) * cam.InvertedViewMatrix;
var look = Engine.Matrix.CreateFromQuaternion(pl.ComponentCreatureModel.EyeRotation).Forward;
bool muzzle = Game.ScThirdPerson.TryGetMuzzleWorld(pl, "ak47", null, out var mw);
return System.Text.Json.JsonSerializer.Serialize(new { isCs = P(c, "IsCs"), skin = human.Model.HasSkin, camera = cam.GetType().Name, lookPitch = Pitch(look), barrel = V(W.Forward), barrelPitch = Pitch(W.Forward),
  barrelYawDot = Engine.Vector3.Dot(Engine.Vector3.Normalize(new Engine.Vector3(W.Forward.X, 0, W.Forward.Z)), Engine.Vector3.Normalize(new Engine.Vector3(look.X, 0, look.Z))),
  side = V(W.Right), muzzle, muzzleAt = V(mw), eye = V(pl.ComponentCreatureModel.EyePosition) });
'''


def main(label, *packages):
    def resolve(p):
        if p == "providers": return [x for x in PROVIDERS if x.exists()]
        return [{"output-lite": m0.LITE, "output-full": m0.FULL}.get(p) or Path(p)]
    pkgs = [x for p in packages for x in resolve(p)]
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"cs-aim-{label}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"cs-aim-{label}", "packages": {p.name: sha(p) for p in pkgs}, "samples": []}
    g = None
    try:
        g = m0.game("server", case_dir, pkgs); R["engine"] = g.engine_info(); to_menu(g); m0.enter_world(g)
        g.func(KEEP_ACTIVE); x, y, z = [int(v) for v in cf.build_arena(g).split()]
        for _ in range(5):
            g.func(cf.teleport(x, y, z)); time.sleep(.7)
            if cf.near(g.func(cf.EYE), x, y, z): break
        g.func(cf.STAND); time.sleep(1.5); g.func(DEBUG_ON); g.func(FIRE_KEY)
        R["members"] = g.func(MEMBERS, timeout=60); print("members", R["members"], flush=True)
        R["setCs"] = g.func(SET_CS, timeout=60); print("set", R["setCs"], flush=True); time.sleep(2)
        print("gun", g.func(give("ak47")), flush=True); time.sleep(2.5)
        g.func(camera("Tpp")); time.sleep(1)
        for pitch in (0.0, 0.5, -0.5, 1.0):
            g.func(look(0.0, pitch)); time.sleep(1.2)
            s = json.loads(g.func(STATE, timeout=60)); s["setPitch"] = pitch
            g.func(CLEAR_LAST); g.cmd("KEYDOWN F"); time.sleep(.05); g.cmd("KEYUP F"); time.sleep(.8)
            shot = g.func(LAST); s["tracerFrom"] = shot.split(" tracer-from ")[1].split()[0] if " tracer-from " in shot else ""
            frame = snap(g, f"cs-aim-{label}-{pitch}", ((0, 0, 0), (1, 1, 1)))[0]; time.sleep(.3)
            for f in Path(frame).parent.glob(Path(frame).name + "*"):
                (case_dir / "frames").mkdir(exist_ok=True); shutil.copy2(f, case_dir / "frames" / f.name); s["frame"] = str(case_dir / "frames" / f.name)
            R["samples"].append(s); print(json.dumps(s, ensure_ascii=False), flush=True)
        base = next((s["barrelPitch"] for s in R["samples"] if s["setPitch"] == 0.0), None)
        if base is not None:
            for s in R["samples"]: s["barrelFollows"] = round(s["barrelPitch"] - base, 3)
        R["gameErrors"] = g.errors()[:30]
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None: g.close()
        m0.RUNTIME_OWNER.release()
    out = RESULTS / f"cs-aim-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print("follows:", [(s["setPitch"], s.get("barrelFollows"), s.get("tracerFrom")) for s in R["samples"]], f"errors {len(R.get('gameErrors', []))}; failure {R.get('failure')}; {out}", flush=True)
    return 1 if R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], *sys.argv[2:]))
