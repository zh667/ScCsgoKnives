"""Mirror of the shipped CsmcFirstPersonRenderer maths, for offline checking."""
import sys, os, math, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fpsim import *
from fprender import knife_models, project_plane

KNIFE_SCALE = 0.74
REFERENCE_SOURCE_SCALE = 13.618
HAND_ANCHOR = (0.34, -0.34, -0.72)
RIGHT_ENTRANCE = (0.72, -0.95, -0.78)
LEFT_ENTRANCE = (-0.84, -0.98, -0.78)
ARM_TRANSVERSE = 0.60
ARM_UNIT = 0.625
ARM_GRIP_WRAP = 0.35
GRIP = json.load(open(os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                                   ".tmp-csmc/grips.json")))
INSPECT_TRAVEL = 0.55

def placement(rig):
    k = KNIFE_SCALE * rig.f["SourceReferenceScale"] / REFERENCE_SOURCE_SCALE
    base = muls(scale(k), rotz(math.radians(270)), roty(math.radians(180)),
                rotx(math.radians(90)))
    idle = rig.pose("idle", 0.0)["bindings"]["hand_r"]
    grip = xform(GRIP[rig.name], mul(idle, base))
    return mul(base, trans(vsub(HAND_ANCHOR, grip)))

def arm_matrix(start, end, wrist, left, wrap, post):
    d = vsub(end, start)
    reach = length(d)
    if reach < 1e-4:
        return None
    axis = vmul(d, 1.0 / reach)
    distance = reach + wrap
    across = project_plane(xform_dir((1.0, 0.0, 0.0), wrist), axis)
    twist = math.radians(45.0 + (-90.0 if left else 90.0))
    sideways = vadd(vmul(across, math.cos(twist)), vmul(cross(axis, across), math.sin(twist)))
    depth = norm(cross(sideways, axis))
    ls = max(0.65, min(4.8, distance / ARM_UNIT))
    tv = -ARM_TRANSVERSE if left else ARM_TRANSVERSE
    m = [sideways[0]*tv, sideways[1]*tv, sideways[2]*tv, 0,
         axis[0]*ls, axis[1]*ls, axis[2]*ls, 0,
         depth[0]*ARM_TRANSVERSE, depth[1]*ARM_TRANSVERSE, depth[2]*ARM_TRANSVERSE, 0,
         start[0], start[1], start[2], 1]
    return mul(m, post)

def render(variant, clip, t, out, w=1024, h=551, post=None):
    post = post or ident()
    rig = Rig(variant)
    pose = rig.pose(clip, t)
    place = placement(rig)
    if clip.startswith("inspect"):
        idle = xform(GRIP[variant], rig.pose("idle", 0.0)["bindings"]["hand_r"])
        now = xform(GRIP[variant], pose["bindings"]["hand_r"])
        place = mul(trans(vmul(vsub(idle, now), 1.0-INSPECT_TRAVEL)), place)
    root = mul(place, post)
    fr = Frame(w, h, 80.0)
    arm_v, arm_f = load_obj(os.path.join(MODELS, "player_arm.obj"))
    rw = mul(pose["bindings"]["hand_r"], place)
    lw = mul(pose["bindings"]["hand_l"], place)
    grip = xform(GRIP[variant], rw)
    wrap = ARM_GRIP_WRAP * length(vsub(grip, (rw[12], rw[13], rw[14])))
    for st, en, wr, left, wp, col in (
            (LEFT_ENTRANCE, xform(pose["attachments"]["hand_l"], place), lw, True, 0.0, (150, 116, 84)),
            (RIGHT_ENTRANCE, grip, rw, False, wrap, (196, 150, 108))):
        m = arm_matrix(st, en, wr, left, wp, post)
        if m:
            fr.mesh(arm_v, arm_f, m, col)
    for binding, (verts, faces) in knife_models(variant):
        fr.mesh(verts, faces, mul(pose["bindings"][binding], root), (170, 175, 185))
    fr.crosshair()
    fr.save(out)

def arm_axes(variant, clip, samples=61):
    """Return the per-frame arm basis so discontinuities show up as spikes."""
    rig = Rig(variant)
    dur = rig.f["Clips"].get(clip, rig.f["Clips"]["idle"])["Duration"] or 1.0
    place = placement(rig)
    out = []
    for i in range(samples):
        pose = rig.pose(clip, dur * i / (samples - 1))
        rw = mul(pose["bindings"]["hand_r"], place)
        grip = xform(GRIP[variant], rw)
        m = arm_matrix(RIGHT_ENTRANCE, grip, rw, False, 0.0, ident())
        out.append((m[0], m[1], m[2]))
    return out
