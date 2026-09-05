"""Render the current (or a candidate) first-person composition offline."""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fpsim import *

import json as _json
_CACHE = {}

def mesh_parts(name):
    if name not in _CACHE:
        with open(os.path.join(ANIM, f"{name}.csmc.animation.json")) as f:
            _CACHE[name] = _json.load(f)["MeshParts"]
    return _CACHE[name]

def knife_models(name):
    return [(b, load_obj(os.path.join(MODELS, f"{name}_{b}.obj"))) for b in mesh_parts(name)]

def root_matrix(root_scale=1.60, placement=(1.37, -0.82, 0.0)):
    r = muls(scale(root_scale), rotz(math.radians(270)), roty(math.radians(180)),
             rotx(math.radians(90)), trans((-0.22, 0.42, -0.18)))
    return mul(r, trans(placement))

def project_plane(v, n):
    p = vsub(v, vmul(n, dot(v, n)))
    l = length(p)
    if not math.isfinite(l) or l < 1e-5:
        p = cross(n, (0, 1, 0))
        l = length(p)
        if l < 1e-5:
            return (1.0, 0.0, 0.0)
    return vmul(p, 1.0/l)

# ------------------------------------------------- current implementation
def arm_matrix_current(start, end, wrist, side):
    d = vsub(end, start)
    dist = length(d)
    if dist < 1e-4:
        return trans(start)
    y = vmul(d, 1.0/dist)
    ref = (1.0, 0.0, 0.0) if abs(y[0]) < 0.85 else (0.0, 0.0, 1.0)
    x = project_plane(ref, y)
    ax = project_plane(xform_dir(x, wrist), y)
    roll = 0.0
    if length(ax) > 1e-3:
        ax = norm(ax)
        roll = math.atan2(dot(cross(x, ax), y), max(-1.0, min(1.0, dot(x, ax))))
    final = roll + math.radians(45 + (90 if side else -90))
    z = norm(cross(x, y))
    rx = vadd(vmul(x, math.cos(final)), vmul(z, math.sin(final)))
    rz = norm(cross(rx, y))
    ls = max(0.65, min(4.8, dist/0.625))
    tv = 0.82*1.45*(-1 if side else 1)
    lz = ls*(0.625/0.7)
    return [rx[0]*tv, rx[1]*tv, rx[2]*tv, 0,
            rz[0]*abs(tv), rz[1]*abs(tv), rz[2]*abs(tv), 0,
            -y[0]*lz, -y[1]*lz, -y[2]*lz, 0,
            start[0], start[1], start[2], 1]

def render(variant, clip="idle", t=0.0, out="out.png", w=768, h=413, mode="current",
           root_scale=1.60, placement=(1.37, -0.82, 0.0)):
    rig = Rig(variant)
    pose = rig.pose(clip, t)
    root = root_matrix(root_scale, placement)
    fr = Frame(w, h, 80.0)
    for binding, (verts, faces) in knife_models(variant):
        fr.mesh(verts, faces, mul(pose["bindings"][binding], root), (170, 175, 185))
    inv = invert(root)
    arm_v, arm_f = load_obj(os.path.join(MODELS, "player_arm.obj"))
    right_start = xform((0.58, -0.78, -0.70), inv)
    left_start = xform((-0.70, -0.82, -0.72), inv)
    b = pose["bindings"]
    right_end = (b["hand_r"][12], b["hand_r"][13], b["hand_r"][14])
    left_end = pose["attachments"]["hand_l"]
    if mode == "current":
        fr.mesh(arm_v, arm_f, mul(arm_matrix_current(left_start, left_end, b["hand_l"], True), root), (150, 110, 70))
        fr.mesh(arm_v, arm_f, mul(arm_matrix_current(right_start, right_end, b["hand_r"], False), root), (170, 125, 80))
    fr.crosshair()
    fr.save(out)
    def s(v): return "(%.3f,%.3f,%.3f)" % v
    print(f"{variant}/{clip}@{t}: rightStart(local)={s(right_start)} rightEnd={s(right_end)} "
          f"len={length(vsub(right_end,right_start)):.3f}")
    print(f"   leftStart(local)={s(left_start)} leftEnd={s(left_end)} "
          f"len={length(vsub(left_end,left_start)):.3f}")
    print(f"   view: rightStart={s(xform(right_start,root))} rightEnd={s(xform(right_end,root))} "
          f"leftEnd={s(xform(left_end,root))}")
    kb = mul(pose["bindings"][VARIANTS[variant][0]], root)
    print(f"   knife origin(view)={s((kb[12],kb[13],kb[14]))}")

if __name__ == "__main__":
    import argparse
    ap = argparse.ArgumentParser()
    ap.add_argument("variant"); ap.add_argument("--clip", default="idle")
    ap.add_argument("--t", type=float, default=0.0); ap.add_argument("--out", default="out.png")
    ap.add_argument("--mode", default="current")
    a = ap.parse_args()
    render(a.variant, a.clip, a.t, a.out, mode=a.mode)
