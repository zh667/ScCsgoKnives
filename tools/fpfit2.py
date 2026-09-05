"""Fit the fixed-direction arm model to the CS:MC spec.

The four controls per arm are near enough independent that a fixed-point loop
converges: the hand position follows the anchor, the measured lean follows the
set screen angle, the taper follows the depth ratio, and the width follows the
thickness.  The silhouette is the convex hull of the box's eight projected
corners, filled with numpy, so a fit iteration costs a fraction of a second.
"""
import sys, os, math, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
from fpsim import *
from armstat import arm_stats, AR
import fpship, fparm2

W, H = 960, 540
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SPEC = json.load(open(os.path.join(ROOT, '.tmp-csmc/mccs_spec3.json')))
BOXV, _ = load_obj(fparm2.BOX)

def hull_mask(m):
    pts = []
    for v in BOXV:
        p = xform(v, m); s = fparm2.screen(p)
        if s is None: return None
        pts.append((s[0]*W, s[1]*H))
    pts = np.array(pts)
    # convex hull (gift wrap is fine for 8 points)
    o = pts[np.lexsort((pts[:, 1], pts[:, 0]))]
    def cross2(a, b):
        return a[0]*b[1]-a[1]*b[0]          # numpy 2 dropped the 2-D cross product
    def half(ps):
        st = []
        for p in ps:
            while len(st) > 1 and cross2(st[-1]-st[-2], p-st[-2]) <= 0: st.pop()
            st.append(p)
        return st[:-1]
    hull = np.array(half(o)+half(o[::-1]))
    if len(hull) < 3: return None
    y0, y1 = max(0, int(hull[:, 1].min())), min(H-1, int(hull[:, 1].max())+1)
    if y1 <= y0: return None
    msk = np.zeros((H, W), bool)
    ys = np.arange(y0, y1)+0.5
    n = len(hull)
    xs_hit = [[] for _ in ys]
    for i in range(n):
        a, b = hull[i], hull[(i+1) % n]
        if a[1] == b[1]: continue
        lo, hi = min(a[1], b[1]), max(a[1], b[1])
        sel = (ys >= lo) & (ys < hi)
        if not sel.any(): continue
        xx = a[0]+(ys[sel]-a[1])*(b[0]-a[0])/(b[1]-a[1])
        for j, x in zip(np.nonzero(sel)[0], xx): xs_hit[j].append(x)
    for j, row in enumerate(xs_hit):
        if len(row) < 2: continue
        row.sort()
        x0, x1 = int(max(0, row[0])), int(min(W-1, row[-1]))
        if x1 >= x0: msk[y0+j, x0:x1+1] = True
    return msk

def measure(side):
    out = []
    for name in sorted(fpship.GRIP):
        try: rig = Rig(name); pose = rig.pose('idle', 0.0); place = fpship.placement(rig)
        except Exception: continue
        grip, wrist = fparm2.grip_point(rig, pose, place, side, name)
        m = fparm2.arm_matrix(grip, wrist, side == 'l')
        if not m: continue
        msk = hull_mask(m)
        if msk is None: continue
        s = arm_stats(msk)
        if s: out.append(s)
    if len(out) < 4: return None
    L = np.array([x['lean'] for x in out])
    return dict(lean=float(np.median(L)), lean_std=float(L.std()),
                width_hand=float(np.median([x['width_hand'] for x in out])),
                taper=float(np.median([x['taper'] for x in out])),
                hand=[float(np.median([x['hand'][0] for x in out])),
                      float(np.median([x['hand'][1] for x in out]))],
                hand_std=[float(np.std([x['hand'][0] for x in out])),
                          float(np.std([x['hand'][1] for x in out]))], n=len(out))

def fit(iters=25):
    for it in range(iters):
        mr, ml = measure('r'), measure('l')
        if mr is None or ml is None: print('arm missing'); return
        err = max(abs(mr['hand'][0]-SPEC['right']['hand'][0]), abs(mr['hand'][1]-SPEC['right']['hand'][1]),
                  abs(ml['hand'][0]-SPEC['left']['hand'][0]), abs(ml['hand'][1]-SPEC['left']['hand'][1]),
                  abs(mr['lean']-SPEC['right']['lean'])/100, abs(ml['lean']-SPEC['left']['lean'])/100,
                  abs(mr['width_hand']-SPEC['right']['width_hand'])*3,
                  abs(ml['width_hand']-SPEC['left']['width_hand'])*3)
        if it % 4 == 0 or err < 1.5e-3:
            print(f"it{it:>2} err {err:.4f} | R lean {mr['lean']:+6.1f} w {mr['width_hand']:.3f} taper {mr['taper']:.2f} hand ({mr['hand'][0]:.3f},{mr['hand'][1]:.3f})"
                  f" | L lean {ml['lean']:+6.1f} w {ml['width_hand']:.3f} taper {ml['taper']:.2f} hand ({ml['hand'][0]:.3f},{ml['hand'][1]:.3f})")
        if err < 1.5e-3: break
        a = fparm2.screen(fpship.HAND_ANCHOR)
        fpship.HAND_ANCHOR = fparm2.to_view(a[0]+(SPEC['right']['hand'][0]-mr['hand'][0])*0.9,
                                            a[1]+(SPEC['right']['hand'][1]-mr['hand'][1])*0.9, 0.72)
        zl = []
        for n in sorted(fpship.GRIP):
            try: rig = Rig(n); zl.append(mul(rig.pose('idle',0.0)['bindings']['hand_l'], fpship.placement(rig))[14])
            except Exception: pass
        dl = fparm2.to_view(0.5+(SPEC['left']['hand'][0]-ml['hand'][0])*0.9,
                            0.5+(SPEC['left']['hand'][1]-ml['hand'][1])*0.9, -float(np.median(zl)))
        fparm2.LEFT_SHIFT = (fparm2.LEFT_SHIFT[0]+dl[0], fparm2.LEFT_SHIFT[1]+dl[1], fparm2.LEFT_SHIFT[2])
        fparm2.RIGHT_LEAN += (SPEC['right']['lean']-mr['lean'])*0.8
        fparm2.LEFT_LEAN  += (SPEC['left']['lean']-ml['lean'])*0.8
        fparm2.RIGHT_NEAR = min(2.2, max(1.0, fparm2.RIGHT_NEAR*(SPEC['right']['taper']/mr['taper'])**0.4))
        fparm2.LEFT_NEAR  = min(2.2, max(1.0, fparm2.LEFT_NEAR*(SPEC['left']['taper']/ml['taper'])**0.4))
        fparm2.ARM_THICKNESS  *= (SPEC['right']['width_hand']/mr['width_hand'])**0.8
        fparm2.LEFT_THICKNESS *= (SPEC['left']['width_hand']/ml['width_hand'])**0.8
    return measure('r'), measure('l')

if __name__ == '__main__':
    mr, ml = fit()
    print(f"\n{'':8}{'lean':>9}{'lean sd':>9}{'width':>8}{'taper':>8}{'hand x':>9}{'hand y':>9}")
    for k, m in (('right', mr), ('left', ml)):
        s = SPEC[k]
        print(f"  {k:<6}{s['lean']:>9.1f}{s['lean_std']:>9.1f}{s['width_hand']:>8.3f}{s['taper']:>8.2f}{s['hand'][0]:>9.3f}{s['hand'][1]:>9.3f}   MCCS")
        print(f"  {'':<6}{m['lean']:>9.1f}{m['lean_std']:>9.1f}{m['width_hand']:>8.3f}{m['taper']:>8.2f}{m['hand'][0]:>9.3f}{m['hand'][1]:>9.3f}   ours")
        print(f"  {'':<6}{m['lean']-s['lean']:>+9.1f}{'':>9}{m['width_hand']-s['width_hand']:>+8.3f}"
              f"{m['taper']-s['taper']:>+8.2f}{m['hand'][0]-s['hand'][0]:>+9.3f}{m['hand'][1]-s['hand'][1]:>+9.3f}   diff")
    r = dict(anchor=[round(v,4) for v in fpship.HAND_ANCHOR], left_shift=[round(v,4) for v in fparm2.LEFT_SHIFT],
             right_lean=round(fparm2.RIGHT_LEAN,3), left_lean=round(fparm2.LEFT_LEAN,3),
             right_near=round(fparm2.RIGHT_NEAR,4), left_near=round(fparm2.LEFT_NEAR,4),
             thickness=round(fparm2.ARM_THICKNESS,4), left_thickness=round(fparm2.LEFT_THICKNESS,4))
    print('\n'+json.dumps(r, indent=1))
    json.dump(r, open(os.path.join(ROOT, '.tmp-csmc/fit2.json'), 'w'), indent=1)
