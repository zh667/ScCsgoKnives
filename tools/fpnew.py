"""Candidate first-person composition: anchor the rig on the right hand, keep
the arm in view space so its thickness no longer rides on the knife scale."""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fpsim import *
from fprender import VARIANTS, knife_models, project_plane

# ---- tunables ------------------------------------------------------------
KNIFE_SCALE   = 0.50                     # view units per normalised mesh unit
HAND_ANCHOR   = (0.40, -0.26, -0.72)     # idle hand_r position in view space
RIGHT_ENTRANCE= (0.72, -0.95, -0.78)   # off-screen lower-right arm entrance
ARM_THICKNESS = 0.82
ARM_UNIT      = 0.625                    # CSMC natural arm length

def orient():
    return muls(rotz(math.radians(270)), roty(math.radians(180)), rotx(math.radians(90)))

GRIP = {"karambit": (-0.0012, 0.0342, -0.0086),
        "m9":       ( 0.1358, 0.0089, -0.0456),
        "butterfly":( 0.1932, 0.0605, -0.0820)}
LEFT_ENTRANCE = (-0.84, -0.98, -0.78)

def place_matrix(rig, knife_scale=KNIFE_SCALE, anchor=HAND_ANCHOR):
    base = mul(scale(knife_scale), orient())
    idle = rig.pose("idle", 0.0)["bindings"]["hand_r"]
    grip = xform(GRIP[rig.name], mul(idle, base))
    return mul(base, trans(vsub(anchor, grip)))

def rotation_to(a, b):
    """Shortest-arc rotation matrix (row-vector) taking unit a to unit b."""
    d = dot(a, b)
    if d > 0.999999:
        return ident()
    if d < -0.999999:
        axis = cross(a, (1.0, 0.0, 0.0))
        if length(axis) < 1e-6:
            axis = cross(a, (0.0, 0.0, 1.0))
        axis = norm(axis)
        return quat_matrix((axis[0], axis[1], axis[2], 0.0))
    axis = cross(a, b)
    s = math.sqrt((1.0+d)*2.0)
    q = (axis[0]/s, axis[1]/s, axis[2]/s, s*0.5)
    n = math.sqrt(sum(v*v for v in q))
    return quat_matrix(tuple(v/n for v in q))

def arm_matrix(start, end, wrist, side=False, thickness=ARM_THICKNESS):
    d = vsub(end, start)
    dist = length(d)
    if dist < 1e-4:
        return None
    y = vmul(d, 1.0/dist)
    ref = (1.0, 0.0, 0.0) if abs(y[0]) < 0.85 else (0.0, 0.0, 1.0)
    x = project_plane(ref, y)
    ax = project_plane(xform_dir(x, wrist), y)
    roll = 0.0
    if length(ax) > 1e-3:
        ax = norm(ax)
        roll = math.atan2(dot(cross(x, ax), y), max(-1.0, min(1.0, dot(x, ax))))
    theta = math.pi + roll + math.radians(45.0 + (90.0 if side else -90.0))
    ls = max(0.65, min(4.8, dist/ARM_UNIT))
    return muls(scale((-thickness if side else thickness, ls, thickness)), roty(theta),
                rotation_to((0, 1, 0), y), trans(start))

def render(variant, clip="idle", t=0.0, out="out.png", w=768, h=413, **kw):
    rig = Rig(variant)
    pose = rig.pose(clip, t)
    place = place_matrix(rig, kw.get("knife_scale", KNIFE_SCALE), kw.get("anchor", HAND_ANCHOR))
    fr = Frame(w, h, 80.0)
    arm_v, arm_f = load_obj(os.path.join(MODELS, "player_arm.obj"))
    hb = mul(pose["bindings"]["hand_r"], place)
    lb = mul(pose["bindings"]["hand_l"], place)
    end = xform(GRIP[variant], hb)
    start = kw.get("entrance", RIGHT_ENTRANCE)
    lend = xform(pose["attachments"]["hand_l"], place)
    for st, en, wr, side, col in ((LEFT_ENTRANCE, lend, lb, True, (150, 116, 84)),
                                  (start, end, hb, False, (196, 150, 108))):
        am = arm_matrix(st, en, wr, side, kw.get("thickness", ARM_THICKNESS))
        if am:
            fr.mesh(arm_v, arm_f, am, col)
    for binding, (verts, faces) in knife_models(variant):
        fr.mesh(verts, faces, mul(pose["bindings"][binding], place), (170, 175, 185))
    fr.crosshair()
    fr.save(out)
    print(f"{variant}/{clip}@{t}: hand_view=({end[0]:.3f},{end[1]:.3f},{end[2]:.3f}) "
          f"armLen={length(vsub(end,start)):.3f}")

if __name__ == "__main__":
    import argparse
    ap = argparse.ArgumentParser()
    ap.add_argument("variant"); ap.add_argument("--clip", default="idle")
    ap.add_argument("--t", type=float, default=0.0); ap.add_argument("--out", default="out.png")
    ap.add_argument("--scale", type=float, default=KNIFE_SCALE)
    ap.add_argument("--thickness", type=float, default=ARM_THICKNESS)
    a = ap.parse_args()
    render(a.variant, a.clip, a.t, a.out, knife_scale=a.scale, thickness=a.thickness)
