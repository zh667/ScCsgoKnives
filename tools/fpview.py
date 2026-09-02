"""Render the fitted composition so it can be eyeballed against PHOTO2."""
import sys, os, math, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fpsim import *
from fprender import knife_models
import fpship, fparm2

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
F = json.load(open(os.path.join(ROOT, '.tmp-csmc/fit2.json')))
fpship.HAND_ANCHOR = tuple(F['anchor'])
fparm2.LEFT_SHIFT = tuple(F['left_shift'])
fparm2.RIGHT_LEAN, fparm2.LEFT_LEAN = F['right_lean'], F['left_lean']
fparm2.RIGHT_NEAR, fparm2.LEFT_NEAR = F['right_near'], F['left_near']
fparm2.ARM_THICKNESS, fparm2.LEFT_THICKNESS = F['thickness'], F['left_thickness']

def render(name, out, clip='idle', t=0.0, w=640, h=360):
    rig = Rig(name); pose = rig.pose(clip, t); place = fpship.placement(rig)
    fr = Frame(w, h, 80.0)
    bv, bf = load_obj(fparm2.BOX)
    for side, col in (('l', (196, 150, 156)), ('r', (214, 166, 172))):
        grip, wrist = fparm2.grip_point(rig, pose, place, side, name)
        m = fparm2.arm_matrix(grip, wrist, side == 'l')
        if m: fr.mesh(bv, bf, m, col)
    for b, (vv, ff) in knife_models(name):
        fr.mesh(vv, ff, mul(pose['bindings'][b], place), (170, 175, 185))
    fr.crosshair(); fr.save(out)
    return out

if __name__ == '__main__':
    os.makedirs(os.path.join(ROOT, '.tmp-sim/fit'), exist_ok=True)
    for n in sys.argv[1:] or ['karambit', 'bayonet', 'butterfly', 'm9_bayonet']:
        try:
            print(render(n, os.path.join(ROOT, f'.tmp-sim/fit/{n}.png')))
        except Exception as e:
            print(f"{n}: {e}")
