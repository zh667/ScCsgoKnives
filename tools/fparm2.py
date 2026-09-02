"""The arm model the PHOTO2 references actually support.

Measured across 20 static CS:MC shots: each arm holds a *fixed screen direction*
(right +15.4 deg +/-1.4, left -56.7 deg +/-0.5) while the hand swings freely with
the knife (x varies by a quarter of the screen).  Our old model had it backwards --
a fixed entrance point with the direction falling out of wherever the hand went --
which is why the right arm swept 65 degrees across the frame instead of standing
nearly upright.

The arm also widens toward the bottom of the frame (right 1.27x, left 1.19x), so
the far end sits nearer the eye than the hand does.  Two numbers per arm, both
measured: a screen angle and a depth ratio.
"""
import sys, os, math, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fpsim import *
import fpship

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BOX = os.path.join(ROOT, '.tmp-sim/sc_handbox.obj')
BOX_LENGTH = 0.27559
AR = 16/9
FY = 1.0/math.tan(math.radians(40.0)); FX = FY/AR

RIGHT_LEAN = 15.4          # degrees from straight down, going down the arm
LEFT_LEAN = -56.7
RIGHT_NEAR = 1.27          # bottom end is this many times nearer than the hand
LEFT_NEAR = 1.19
ARM_THICKNESS = 2.29
LEFT_THICKNESS = 2.29
LEFT_SHIFT = (0.0, 0.0, 0.0)
LEFT_GRIPS = json.load(open(os.path.join(ROOT, '.tmp-csmc/leftgrips.json')))
EXIT_Y = 1.30              # run the arm to here so it always leaves the frame

def screen(p):
    d = -p[2]
    if d < 1e-4: return None
    return (p[0]*FX/d*0.5+0.5, 0.5-p[1]*FY/d*0.5)

def to_view(sx, sy, d):
    return ((sx-0.5)*2.0*d/FX, (0.5-sy)*2.0*d/FY, -d)

def arm_matrix(grip, wrist, left):
    """A box from the hand, down a fixed screen direction, out of the frame."""
    s = screen(grip)
    if s is None: return None
    d = -grip[2]
    lean = math.radians(LEFT_LEAN if left else RIGHT_LEAN)
    near = LEFT_NEAR if left else RIGHT_NEAR
    dirs = (math.sin(lean)/AR, math.cos(lean))          # screen-space, aspect corrected
    t = (EXIT_Y - s[1])/dirs[1] if dirs[1] > 1e-6 else 2.0
    far = to_view(s[0]+dirs[0]*t, s[1]+dirs[1]*t, d/near)
    span = vsub(far, grip)
    reach = length(span)
    if not math.isfinite(reach) or reach < 1e-4: return None
    axis = vmul(span, 1.0/reach)                        # local +Z runs down the arm
    ref = xform_dir((1.0, 0.0, 0.0), wrist)
    side = project_plane(ref, axis)
    if length(side) < 0.15: side = project_plane((0.0, 1.0, 0.0), axis)
    side = norm(side)
    up = norm(cross(side, axis))
    k = reach/BOX_LENGTH
    th = LEFT_THICKNESS if left else ARM_THICKNESS
    stretch = [th*(-1.0 if left else 1.0),0,0,0, 0,th,0,0, 0,0,k,0, 0,0,0,1]
    frame = [side[0],side[1],side[2],0, up[0],up[1],up[2],0, axis[0],axis[1],axis[2],0,
             grip[0],grip[1],grip[2],1]
    return muls(stretch, trans((0, 0, BOX_LENGTH*k)), frame)

def project_plane(v, axis):
    return vsub(v, vmul(axis, dot(v, axis)))

def grip_point(rig, pose, place, side, name):
    wrist = mul(pose['bindings'][f'hand_{side}'], place)
    off = LEFT_GRIPS.get(name, [0,0,0]) if side == 'l' else fpship.GRIP[name]
    p = xform(off, wrist)
    if side == 'l': p = vadd(p, LEFT_SHIFT)
    return p, wrist
