"""Iterate the composition parameters until our render matches the CS:MC spec.

Each arm has two controls with a clean geometric meaning: the anchor (or the
left-hand shift) decides where the hand sits, and the entrance decides the arm's
direction and how far it runs off the bottom of the frame.  Both are corrected
against the same silhouette statistics measured off the PHOTO2 references.
"""
import sys, os, math, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
from fpsim import vadd, xform, mul, Rig
import fpship, fparm, fpfit
from fpfit import SPEC, to_view, stats, arm_masks, FX, FY

AR = 16/9
def scr(p):
    d = -p[2]; return (p[0]*FX/d*0.5+0.5, 0.5-p[1]*FY/d*0.5)
def ar_vec(a, b):           # aspect-corrected screen vector a -> b
    return ((b[0]-a[0])*AR, (b[1]-a[1]))
def lean_of(v):
    return math.degrees(math.atan2(abs(v[0]), abs(v[1])))

D_RE, D_LE, D_ANCHOR = 0.78, 0.95, 0.72
fparm.RIGHT_ENTRANCE = to_view(*SPEC['right']['entrance'], D_RE)
fparm.LEFT_ENTRANCE  = to_view(*SPEC['left']['entrance'],  D_LE)

def left_hand_depth():
    zs = []
    for n in sorted(fpship.GRIP):
        try: rig = Rig(n); pl = fpship.placement(rig); p = rig.pose('idle', 0.0)
        except Exception: continue
        b = mul(p['bindings']['hand_l'], pl); zs.append(b[14])
    return -float(np.median(zs))

for it in range(9):
    mr, ml = stats(arm_masks('r')), stats(arm_masks('l'))
    err = max(abs(mr['hand'][0]-SPEC['right']['hand'][0]), abs(mr['hand'][1]-SPEC['right']['hand'][1]),
              abs(ml['hand'][0]-SPEC['left']['hand'][0]),  abs(ml['hand'][1]-SPEC['left']['hand'][1]),
              abs(mr['lean']-SPEC['right']['lean'])/200, abs(ml['lean']-SPEC['left']['lean'])/200)
    print(f"it{it}: R lean {mr['lean']:5.1f} w {mr['width']:.3f} hand ({mr['hand'][0]:.3f},{mr['hand'][1]:.3f}) ent ({mr['entrance'][0]:.3f},{mr['entrance'][1]:.3f})"
          f" | L lean {ml['lean']:5.1f} w {ml['width']:.3f} hand ({ml['hand'][0]:.3f},{ml['hand'][1]:.3f}) ent ({ml['entrance'][0]:.3f},{ml['entrance'][1]:.3f})  err {err:.4f}")
    if err < 3e-3: break

    # 1. hands: anchor drives the right, a global shift drives the left
    a = scr(fpship.HAND_ANCHOR)
    fpship.HAND_ANCHOR = to_view(a[0]+(SPEC['right']['hand'][0]-mr['hand'][0]),
                                 a[1]+(SPEC['right']['hand'][1]-mr['hand'][1]), D_ANCHOR)
    dz = left_hand_depth()
    dl = to_view(0.5+(SPEC['left']['hand'][0]-ml['hand'][0]), 0.5+(SPEC['left']['hand'][1]-ml['hand'][1]), dz)
    fparm.LEFT_SHIFT = (fparm.LEFT_SHIFT[0]+dl[0]*0.9, fparm.LEFT_SHIFT[1]+dl[1]*0.9, fparm.LEFT_SHIFT[2])

    # 2. entrances: rotate to fix the lean, scale the run to fix the measured entrance
    for key, m, ent, dep in (('right', mr, 'RIGHT_ENTRANCE', D_RE), ('left', ml, 'LEFT_ENTRANCE', D_LE)):
        e = scr(getattr(fparm, ent)); h = m['hand']; s = SPEC[key]
        v = ar_vec(h, e)
        rot = math.radians(s['lean']-m['lean']) * (1 if v[0]*v[1] > 0 else -1) * 0.8
        c, sn = math.cos(rot), math.sin(rot)
        v = (v[0]*c - v[1]*sn, v[0]*sn + v[1]*c)
        run = math.hypot(*ar_vec(s['hand'], s['entrance'])) / max(1e-6, math.hypot(*ar_vec(m['hand'], m['entrance'])))
        v = (v[0]*(1+(run-1)*0.6), v[1]*(1+(run-1)*0.6))
        setattr(fparm, ent, to_view(h[0]+v[0]/AR, h[1]+v[1], dep))

    # 3. thickness from the right arm's width
    fparm.ARM_THICKNESS *= (SPEC['right']['width']/mr['width'])**0.7

mr, ml = stats(arm_masks('r')), stats(arm_masks('l'))
print(f"\n{'':8}{'enter x':>9}{'enter y':>9}{'lean':>8}{'width':>8}{'hand x':>9}{'hand y':>9}")
for key, m in (('right', mr), ('left', ml)):
    s = SPEC[key]
    print(f"  {key:<6}{s['entrance'][0]:>9.3f}{s['entrance'][1]:>9.3f}{s['lean']:>8.1f}{s['width']:>8.3f}{s['hand'][0]:>9.3f}{s['hand'][1]:>9.3f}   MCCS")
    print(f"  {'':<6}{m['entrance'][0]:>9.3f}{m['entrance'][1]:>9.3f}{m['lean']:>8.1f}{m['width']:>8.3f}{m['hand'][0]:>9.3f}{m['hand'][1]:>9.3f}   ours")
    print(f"  {'':<6}{m['entrance'][0]-s['entrance'][0]:>+9.3f}{m['entrance'][1]-s['entrance'][1]:>+9.3f}"
          f"{m['lean']-s['lean']:>+8.1f}{m['width']-s['width']:>+8.3f}{m['hand'][0]-s['hand'][0]:>+9.3f}{m['hand'][1]-s['hand'][1]:>+9.3f}   diff")
r = dict(anchor=[round(v,4) for v in fpship.HAND_ANCHOR], right=[round(v,4) for v in fparm.RIGHT_ENTRANCE],
         left=[round(v,4) for v in fparm.LEFT_ENTRANCE], shift=[round(v,4) for v in fparm.LEFT_SHIFT],
         thickness=round(fparm.ARM_THICKNESS,4))
print(); print(json.dumps(r, indent=1))
json.dump(r, open('.tmp-csmc/fit.json','w'), indent=1)
