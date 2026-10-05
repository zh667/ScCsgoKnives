"""Why the third-person gun is not posed in the user's game (2026-10-01, user: "第一人称枪械没问题，会跟着我的视角，枪械倾斜，但是
所有的第三人称都不会"; the user's Game.log: every third-person shot from DebugCamera / BumanCamera / OrbitCamera with
"tracer-from origin", i.e. ScThirdPerson had no posed weapon).

On the isolated 1.9.3.1 copy (M0_ENGINE=131) with every package of the user's Mods folder (read-only copies, the CS package
replaced by the one under test): an AK-47 in hand, third person, then the debug camera beside the player, then the orbit
camera. For each: the player model (component type, model, skin, bones), each early exit of ScThirdPerson.Pose in order,
whether the pose ran this frame, the third-person muzzle, and a frame.
Usage: sp_tp_user.py <label> <CS package | output-full | output-lite>
"""
import json, shutil, sys, time
from pathlib import Path

import m0
from m0 import RESULTS, RUNS, sha, to_menu
from mp_m1 import MAIN, KEEP_ACTIVE
import sp_camera_fire as cf
from sp_camera_fire import give, camera, look
from sp_smoke_view import snap

USER_MODS = Path(r"D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1/Mods")

WHY = MAIN + r'''
var human = pl.Entity.FindComponent<Game.ComponentHumanModel>();
var creature = pl.ComponentCreatureModel;
string Why() {
  if (human == null) return "no ComponentHumanModel (creature model " + creature?.GetType().FullName + ")";
  if (human.Model?.HasSkin == true) return "skinned model";
  if (human.m_componentMiner == null) return "no miner";
  if (human.m_hand1Bone == null) return "no hand1 bone";
  if (human.m_hand2Bone == null) return "no hand2 bone";
  if (human.m_bodyBone == null) return "no body bone";
  if (human.m_boneTransforms == null) return "no bone transforms";
  if (human.m_componentCreature?.ComponentHealth?.Health <= 0) return "dead";
  if (human.m_lieDownFactorModel > 0) return "lying down " + human.m_lieDownFactorModel;
  int value = Game.ScThirdPerson.PresentedValue(human, out var phase);
  string asset = Game.ScThirdPerson.AssetFor(value, out var stance);
  if (asset == null) return "no asset for value " + value;
  if (!human.m_boneTransforms[human.m_bodyBone.Index].HasValue) return "body bone not placed (asset " + asset + ")";
  return "ok (asset " + asset + ")";
}
var table = typeof(Game.ScThirdPerson).GetField("s_states", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
var args = new object[] { human, null }; bool found = human != null && (bool)table.GetType().GetMethod("TryGetValue").Invoke(table, args);
var st = args[1]; System.Func<string, object> F = n => st?.GetType().GetField(n)?.GetValue(st);
bool muzzle = Game.ScThirdPerson.TryGetMuzzleWorld(pl, "ak47", null, out var mw);
string bones = human?.Model == null ? "" : string.Join(",", human.Model.Bones.Select(b => b.Name).Take(16));
return System.Text.Json.JsonSerializer.Serialize(new {
  camera = pl.GameWidget.ActiveCamera.GetType().Name, creatureType = creature?.GetType().FullName, humanType = human?.GetType().FullName,
  skin = human?.Model?.HasSkin, boneCount = human?.Model?.Bones.Count, bones, hand1 = human?.m_hand1Bone?.Name, hand2 = human?.m_hand2Bone?.Name, body = human?.m_bodyBone?.Name,
  why = Why(), state = found, valid = F("Valid"), frame = F("Frame"), now = Engine.Time.FrameIndex, asset = F("Asset"), muzzle, look = pl.ComponentLocomotion.LookAngles.Y });
'''


def main(label, package):
    pkg = {"output-lite": m0.LITE, "output-full": m0.FULL}.get(package) or Path(package)
    user = sorted(p for p in USER_MODS.glob("*.scmod") if "CS武器" not in p.name)
    pkgs = user + [pkg]
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"tp-user-{label}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"tp-user-{label}", "packages": {p.name: sha(p) for p in pkgs}, "samples": []}
    g = None
    try:
        g = m0.game("server", case_dir, pkgs); R["engine"] = g.engine_info(); to_menu(g); m0.enter_world(g)
        g.func(KEEP_ACTIVE); x, y, z = [int(v) for v in cf.build_arena(g).split()]
        for _ in range(5):
            g.func(cf.teleport(x, y, z)); time.sleep(.7)
            if cf.near(g.func(cf.EYE), x, y, z): break
        g.func(cf.STAND); time.sleep(1.5); print("gun", g.func(give("ak47")), flush=True); time.sleep(2.5)
        g.func(look(0.0, 0.4)); time.sleep(.5)
        ex, ey, ez = [float(v) for v in g.func(cf.EYE).split()]
        for view in ("Tpp", "Debug", "Orbit"):
            if view == "Debug": g.func(camera("Fpp")); time.sleep(.5); print("debug", g.func(cf.debug_to(ex + 3, ey + .5, ez + 1, -3, -.5, -3)), flush=True)
            else: g.func(camera(view))
            time.sleep(1.5)
            s = json.loads(g.func(WHY, timeout=60)); s["view"] = view
            shot = snap(g, f"tp-user-{label}-{view}", ((0, 0, 0), (1, 1, 1)))[0]; time.sleep(.3)
            for f in Path(shot).parent.glob(Path(shot).name + "*"):
                (case_dir / "frames").mkdir(exist_ok=True); shutil.copy2(f, case_dir / "frames" / f.name); s["frame"] = str(case_dir / "frames" / f.name)
            R["samples"].append(s); print(json.dumps(s, ensure_ascii=False), flush=True)
        R["gameErrors"] = g.errors()[:40]
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None: g.close()
        m0.RUNTIME_OWNER.release()
    out = RESULTS / f"tp-user-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print(f"samples {len(R['samples'])}; errors {len(R.get('gameErrors', []))}; failure {R.get('failure')}; {out}", flush=True)
    return 1 if R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], sys.argv[2]))
