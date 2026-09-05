"""Candidate 0.5.2: MCCS-style stretched arm, but with Survivalcraft's own
FirstPersonHand box and a grip offset on BOTH hands.

0.5.0 drove the arm off the rig's forearm bones. That kept the motion honest but
left the arm floating: SC's hand box is only 0.276 long and the forearm spans
0.6-1.3, so three quarters of the arm was missing. MCCS stretches its box from a
fixed screen entrance to the hand precisely so the arm always reaches the frame
edge -- that part of its design is worth keeping.
"""
import sys, os, math, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fpsim import *
from fprender import knife_models, project_plane
import fpship

BOX = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), '.tmp-sim/sc_handbox.obj')
BOX_LENGTH = 0.27559          # SC's hand box, after the 0.01 scale
ARM_THICKNESS = 2.60          # -> 0.205 across, the width MCCS's arm subtends
MIN_ARM_LENGTH = 0.75         # never let the arm shrink to a stub
MAX_ARM_REACH = 2.00          # beyond this the hand is not plausibly reachable
RIGHT_ENTRANCE = (0.72, -0.95, -0.78)
LEFT_ENTRANCE = (-0.84, -0.98, -0.78)
LEFT_SHIFT = (0.0, 0.0, 0.0)   # corrective shift so the fleet's left hand lands where MCCS's does
LEFT_GRIPS = json.load(open(os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), '.tmp-csmc/leftgrips.json')))

def arm_matrix(entrance, grip, wrist, left):
    d = vsub(entrance, grip)
    reach = length(d)
    if not math.isfinite(reach) or reach < 1e-4 or reach > MAX_ARM_REACH:
        return None
    axis = vmul(d, 1.0/reach)                      # grip -> entrance, i.e. local +Z
    across = project_plane(xform_dir((1.0, 0.0, 0.0), wrist), axis)
    twist = math.radians(45.0 + (-90.0 if left else 90.0))
    side = vadd(vmul(across, math.cos(twist)), vmul(cross(axis, across), math.sin(twist)))
    up = norm(cross(side, axis))
    k = max(reach, MIN_ARM_LENGTH) / BOX_LENGTH
    t = ARM_THICKNESS * (-1.0 if left else 1.0)
    stretch = [t,0,0,0, 0,ARM_THICKNESS,0,0, 0,0,k,0, 0,0,0,1]
    frame = [side[0],side[1],side[2],0, up[0],up[1],up[2],0, axis[0],axis[1],axis[2],0, grip[0],grip[1],grip[2],1]
    return muls(stretch, trans((0, 0, BOX_LENGTH*k)), frame)

def render(variant, clip, t, out, w=440, h=236, post=None):
    post = post or ident()
    rig = Rig(variant); pose = rig.pose(clip, t)
    place = fpship.placement(rig)
    fr = Frame(w, h, 80.0)
    bv, bf = load_obj(BOX)
    for side, entrance, offset, col in (
            ('l', LEFT_ENTRANCE, LEFT_GRIPS.get(variant, [0,0,0]), (150,116,84)),
            ('r', RIGHT_ENTRANCE, fpship.GRIP[variant], (196,150,108))):
        wrist = mul(pose['bindings'][f'hand_{side}'], place)
        m = arm_matrix(entrance, xform(offset, wrist), wrist, side == 'l')
        if m: fr.mesh(bv, bf, mul(m, post), col)
    for b, (vv, ff) in knife_models(variant):
        fr.mesh(vv, ff, mul(pose['bindings'][b], mul(place, post)), (170,175,185))
    fr.crosshair(); fr.save(out)
