"""Prototype: forearm driven by the rig's own arm_lower -> hand bones, drawn
with Survivalcraft's first-person hand model instead of a Minecraft box.

CS:MC stretches a box between a fixed screen point and the hand because
Minecraft's player arm has no elbow. Our rig is the CS:GO rig and *does* carry
arm_lower_r / arm_lower_l, so the forearm can follow the source animation
instead of rubber-banding.
"""
import sys, os, math, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fpsim import *
from fprender import knife_models, project_plane
import fpship

HAND_OBJ = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), '.tmp-sim/sc_hand.obj')
FINGER_Z = -0.23        # local Z of the gripping point (palm), after SC's 0.01 scale
GRIPS = fpship.GRIP

def left_grip(name):
    return json.load(open('.tmp-csmc/leftgrips.json')).get(name, [0, 0, 0])

def hand_matrix(grip, elbow, wrist, left, stretch=1.0):
    """Place the hand so its fingers sit on the grip and it runs to the elbow."""
    d = vsub(elbow, grip)
    if length(d) < 1e-4:
        return None
    z = norm(d)
    x = project_plane(xform_dir((1.0, 0.0, 0.0), wrist), z)
    y = norm(cross(z, x))
    sx = -1.0 if left else 1.0
    basis = [x[0]*sx, x[1]*sx, x[2]*sx, 0,
             y[0], y[1], y[2], 0,
             z[0]*stretch, z[1]*stretch, z[2]*stretch, 0,
             0, 0, 0, 1]
    return muls(trans((0, 0, -FINGER_Z)), basis, trans(grip))

def render(variant, clip, t, out, w=880, h=470, post=None):
    post = post or ident()
    rig = Rig(variant); pose = rig.pose(clip, t)
    place = fpship.placement(rig)
    fr = Frame(w, h, 80.0)
    hv, hf = load_obj(HAND_OBJ)
    parts = set(rig.f['MeshParts'])
    for side, offset in (('l', left_grip(variant)), ('r', GRIPS[variant])):
        wrist = mul(pose['bindings'][f'hand_{side}'], place)
        elbowM = mul(pose['bindings'][f'arm_lower_{side}'], place)
        grip = xform(offset, wrist)
        elbow = (elbowM[12], elbowM[13], elbowM[14])
        m = hand_matrix(grip, elbow, wrist, side == 'l')
        if m:
            fr.mesh(hv, hf, mul(m, post), (196, 150, 108) if side == 'r' else (168, 128, 92))
    for b, (vv, ff) in knife_models(variant):
        fr.mesh(vv, ff, mul(pose['bindings'][b], mul(place, post)), (170, 175, 185))
    fr.crosshair(); fr.save(out)
