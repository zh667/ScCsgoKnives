"""CT/T headshot diagnosis (2026-10-01 user report: "CT和T的头部击打音效和伤害，准星颜色，好像没了").

On the isolated 1.9.3.1 copy (M0_ENGINE=131): a CT and a T in front of an invulnerable player; for each actor the state
the precise hit test depends on (skinned model, IScLogicalPose provider, ScSkinnedHitRegions.Known, TryLogicalPose,
native animation flag), its armour, the head joint's world position, what ScGunHitTest.Raycast reports for a ray from the
player's eye to that head (Head expected), then a real shot through the gun's own path aimed at the head
(ScCombatFeedback.LastKind: 3 = non-lethal head hit, the yellow crosshair; health before/after).
Usage: sp_agent_headshot.py <label> <package> [<package> ...]   (paths, output-*/stage-* tokens, or "providers" to add
the player's NekoMeko Model 1.1 and Neorxna 1.4 copies)
"""
import json, sys, time
from pathlib import Path

import m0
from m0 import RESULTS, RUNS, sha, to_menu
from mp_m1 import MAIN, KEEP_ACTIVE, PROJECT
from sp_memory import AGENTS
from sp_camera_fire import DEBUG_ON, FIRE_KEY, give
from sp_agents_hint import PROVIDERS
import sp_camera_fire as cf
from mp_m3t import make_enemy

INSPECT = PROJECT + MAIN.replace(PROJECT, '') + r'''
var F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
var e = project.Entities.FirstOrDefault(x => x.Id == $i0); if (e == null) return "{\"error\":\"no entity\"}";
var model = e.FindComponent<Game.ComponentCreatureModel>(); var body = e.FindComponent<Game.ComponentBody>(); var health = e.FindComponent<Game.ComponentHealth>();
var provider = e.FindComponent<Game.IScLogicalPose>();
bool known = model?.Model != null && Game.ScSkinnedHitRegions.Known(model.Model);
var local = new Engine.Matrix?[model?.Model?.Bones.Count ?? 0]; bool pose = provider != null && provider.TryLogicalPose(local);
object native = model?.GetType().GetField("nativeAnimationRequired", F)?.GetValue(model);
int layers = model?.AnimationController?.Layers?.Length ?? -1;
string headInfo = "none"; string hit = "none"; string partAt = "none"; float hx = 0, hy = 0, hz = 0;
if (pose) {
  var absolute = new Engine.Matrix[local.Length]; var original = model.m_boneTransforms;
  try { model.m_boneTransforms = local; model.ProcessBoneHierarchy(model.Model.RootBone, Engine.Matrix.Identity, absolute); } finally { model.m_boneTransforms = original; }
  var head = model.Model.Bones.FirstOrDefault(b => b.Name != null && b.Name.StartsWith("head_"));
  if (head != null) {
    var hp = absolute[head.Index].Translation; hx = hp.X; hy = hp.Y; hz = hp.Z; headInfo = head.Name;
    var eye = pl.ComponentCreatureModel.EyePosition; var dir = Engine.Vector3.Normalize(hp - eye);
    var bodies = project.FindSubsystem<Game.SubsystemBodies>(true).Bodies;
    var h = Game.ScGunHitTest.Raycast(bodies, pl.ComponentBody, eye, dir, 60f);
    hit = h.HasValue ? (h.Value.Body.Entity.Id == e.Id ? "" : "other-") + h.Value.Part + " " + h.Value.Reason : "miss";
    partAt = Game.ScGunHitTest.PartAt(body, hp, dir).ToString();
    var shotRay = Game.ScAimRay.Resolve(pl, new Engine.Ray3(eye, dir)); var sh = Game.ScGunHitTest.Raycast(bodies, pl.ComponentBody, shotRay.Position, shotRay.Direction, 60f);
    var closest = shotRay.Position + shotRay.Direction * Engine.Vector3.Dot(hp - shotRay.Position, shotRay.Direction);
    hit += " | gun ray from " + shotRay.Position + ": " + (sh.HasValue ? sh.Value.Part + " at " + sh.Value.Distance.ToString("0.00") : "miss") + ", passes the head joint at " + (closest - hp).Length().ToString("0.00") + " m (offset " + (closest - hp) + ")";
  }
}
string armor = "?"; try { var a = project.FindSubsystem<Game.SubsystemScArmor>(false); var en = e.FindComponent<Game.ComponentTacticalEnemy>(); var co = e.FindComponent<Game.ComponentTacticalCompanion>();
  string key = en?.ArmorKey ?? co?.ArmorKey; armor = a == null ? "no armor subsystem" : key == null ? "no key" : key + " " + a.Get(key).Encode(); } catch (System.Exception ex) { armor = "err " + ex.Message; }
return System.Text.Json.JsonSerializer.Serialize(new { template = e.ValuesDictionary?.DatabaseObject?.Name, modelType = model?.GetType().Name, route = model?.ModelRoute, skin = model?.Model?.HasSkin,
  provider = provider != null, sameModel = provider != null && object.ReferenceEquals(provider.LogicalModel, model?.Model), known, pose, native = System.Convert.ToString(native), layers,
  health = health?.Health, armor, head = headInfo, headPos = new[] { hx, hy, hz }, hit, partAt,
  gunplay = Game.ScGunplaySettings.Enabled });
'''
def aim_and_fire(eid): return MAIN + (
    f'var e = project.Entities.FirstOrDefault(x => x.Id == {eid}); var model = e.FindComponent<Game.ComponentCreatureModel>(); var provider = e.FindComponent<Game.IScLogicalPose>(); '
    'var local = new Engine.Matrix?[model.Model.Bones.Count]; if (!provider.TryLogicalPose(local)) return "no pose"; var absolute = new Engine.Matrix[local.Length]; var original = model.m_boneTransforms; '
    'try { model.m_boneTransforms = local; model.ProcessBoneHierarchy(model.Model.RootBone, Engine.Matrix.Identity, absolute); } finally { model.m_boneTransforms = original; } '
    'var hp = absolute[model.Model.Bones.First(b => b.Name != null && b.Name.StartsWith("head_")).Index].Translation; var eye = pl.ComponentCreatureModel.EyePosition; var dir = Engine.Vector3.Normalize(hp - eye); '
    'float yaw = System.MathF.Atan2(-dir.X, -dir.Z), pitch = System.MathF.Asin(System.Math.Clamp(dir.Y, -1f, 1f)); '
    'pl.ComponentBody.Rotation = Engine.Quaternion.CreateFromAxisAngle(Engine.Vector3.UnitY, yaw); pl.ComponentLocomotion.LookAngles = new Engine.Vector2(0f, pitch); '
    'var hh = e.FindComponent<Game.ComponentHealth>(); var fb = project.FindSubsystem<Game.SubsystemScGunBlockBehavior>(true).FeedbackOf(pl); System.AppDomain.CurrentDomain.SetData("hs.before", hh.Health); System.AppDomain.CurrentDomain.SetData("hs.at", fb.LastAt); return "aimed " + hp;')
AFTER = (MAIN + 'var e = project.Entities.FirstOrDefault(x => x.Id == $i0); var hh = e?.FindComponent<Game.ComponentHealth>(); var fb = project.FindSubsystem<Game.SubsystemScGunBlockBehavior>(true).FeedbackOf(pl); '
         'return System.Text.Json.JsonSerializer.Serialize(new { before = (float)System.AppDomain.CurrentDomain.GetData("hs.before"), after = hh?.Health, lastKind = fb.LastKind, updated = fb.LastAt != (double)System.AppDomain.CurrentDomain.GetData("hs.at"), shot = Game.SubsystemScGunBlockBehavior.LastShotDebug.Length > 0 });')
SAFE = MAIN + ('pl.ComponentHealth.IsInvulnerable = true; pl.ComponentLocomotion.IsCreativeFlyEnabled = false; pl.ComponentBody.IsGravityEnabled = true; '
               'project.FindSubsystem<Game.SubsystemGameInfo>(true).WorldSettings.TimeOfDayMode = Game.TimeOfDayMode.Day; return "safe";')


def main(label, *packages):
    def resolve(p):
        if p.startswith(("stage-lite:", "stage-agents:", "stage-full:")):
            kind, tag = p.split(":", 1); suffix = {"stage-lite": "轻量包.scmod", "stage-agents": "探员包.scmod", "stage-full": "全量包.scmod"}[kind]
            return [sorted((m0.ROOT / ".tmp/completion-140-20260929" / tag / "candidate").glob("*" + suffix))[0]]
        if p == "providers": return [x for x in PROVIDERS if x.exists()]
        return [{"output-lite": m0.LITE, "output-full": m0.FULL, "output-agents": m0.LITE.with_name("[API1.9]CS武器1.4.0-探员包.scmod")}.get(p) or Path(p)]
    pkgs = [x for p in packages for x in resolve(p)]
    stamp = time.strftime("%Y%m%d-%H%M%S"); case_dir = RUNS / f"agent-headshot-{label}-{stamp}"; case_dir.mkdir(parents=True)
    R = {"case": f"agent-headshot-{label}", "packages": {p.name: sha(p) for p in pkgs}, "actors": [], "steps": []}
    g = None
    try:
        g = m0.game("server", case_dir, pkgs); R["engine"] = g.engine_info(); to_menu(g); m0.enter_world(g)
        g.func(KEEP_ACTIVE); print(g.func(SAFE)); g.func(DEBUG_ON); g.func(FIRE_KEY); print("gun", g.func(give("ak47"))); time.sleep(2)
        import os
        if os.environ.get("HS_MODE") == "enemy" or "enemy" in label:
            x, y, z = [int(v) for v in cf.build_arena(g).split()]
            for _ in range(5):
                g.func(cf.teleport(x, y, z)); time.sleep(.7)
                if cf.near(g.func(cf.EYE), x, y, z): break
            g.func(cf.STAND); time.sleep(1.5); print(g.func(cf.camera("Fpp"))); print(g.func(SAFE)); time.sleep(1)
            ids = []
            for k, dx in enumerate((-1.5, 1.5)):
                eid = g.func(make_enemy([x + .5 + dx, float(y), z + 9.5])); print("enemy", eid, flush=True); ids.append(int(eid))
            time.sleep(2)
            for t in range(6):
                for eid in ids: print(f"t{t} {eid}", g.func(m0.Call(INSPECT, eid), timeout=60)[:420], flush=True)
                time.sleep(1)
        else:
            made = g.func(AGENTS); print("agents:", made, flush=True); time.sleep(3)
            ids = [int(x.split("#")[1]) for x in made.split(", ") if "#" in x]
        for eid in ids:
            info = json.loads(g.func(m0.Call(INSPECT, eid), timeout=60)); print(json.dumps(info, ensure_ascii=False), flush=True)
            shots = []
            for _ in range(2):
                print("  ", g.func(aim_and_fire(eid))); time.sleep(.6)
                g.cmd("KEYDOWN F"); time.sleep(.05); g.cmd("KEYUP F"); time.sleep(.8)
                shots.append(json.loads(g.func(m0.Call(AFTER, eid)))); print("   shot:", shots[-1], flush=True)
            R["actors"].append({"id": eid, "inspect": info, "shots": shots})
        with g.lock: R["lines"] = [l[:300] for l in g.lines if "CS_" in l and ("HEAD" in l.upper() or "ARMOR" in l.upper() or "hit" in l)][:40]
        R["gameErrors"] = g.errors()
    except Exception as e:
        R["failure"] = f"{type(e).__name__}: {e}"; print("FAILURE", R["failure"], flush=True)
    finally:
        if g is not None: g.close()
        m0.RUNTIME_OWNER.release()
    out = RESULTS / f"agent-headshot-{label}.json"; out.write_text(json.dumps(R, ensure_ascii=False, indent=1), "utf-8")
    print(f"actors {len(R['actors'])}; errors {len(R.get('gameErrors', []))}; failure {R.get('failure')}; {out}", flush=True)
    return 1 if R.get("failure") else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1], *sys.argv[2:]))
