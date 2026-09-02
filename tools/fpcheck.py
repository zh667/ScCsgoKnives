"""Measure our render with exactly the statistics used on the PHOTO2 references."""
import sys, os, math, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
from fpsim import *
import fpship, fparm2

W, H = 480, 270; AR = 16/9; BG = (30, 34, 40)
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SPEC = json.load(open(os.path.join(ROOT, '.tmp-csmc/mccs_spec2.json')))['spec']

def measure(side, clip='idle', t=0.0):
    bv, bf = load_obj(fparm2.BOX)
    rows = []
    for name in sorted(fpship.GRIP):
        try: rig = Rig(name); pose = rig.pose(clip, t); place = fpship.placement(rig)
        except Exception: continue
        grip, wrist = fparm2.grip_point(rig, pose, place, side, name)
        m = fparm2.arm_matrix(grip, wrist, side == 'l')
        if not m: continue
        fr = Frame(W, H, 80.0); fr.mesh(bv, bf, m, (200, 200, 200))
        a = np.array(fr.color, dtype=np.int16).reshape(H, W, 3)
        msk = np.abs(a-np.array(BG)).sum(2) > 12
        ys, xs = np.nonzero(msk)
        if len(xs) < 200: continue
        pts = np.stack([xs/W*AR, ys/H], 1); c = pts.mean(0)
        _, _, vt = np.linalg.svd(pts-c, full_matrices=False); ax, pp = vt[0], vt[1]
        if ax[1] < 0: ax = -ax
        tt = (pts-c)@ax; w = (pts-c)@pp
        prof = []
        t0, t1 = np.percentile(tt, 2), np.percentile(tt, 98)
        for k in range(8):
            b = w[(tt >= t0+(t1-t0)*k/8) & (tt < t0+(t1-t0)*(k+1)/8)]
            prof.append((np.percentile(b,98)-np.percentile(b,2))/AR if len(b) > 20 else np.nan)
        hand = c+ax*tt.min()
        rows.append(dict(name=name, signed=math.degrees(math.atan2(ax[0], ax[1])),
                         hand=[hand[0]/AR, hand[1]], prof=prof))
    return rows

def report(tag):
    print(tag)
    print(f"{'':8}{'lean':>9}{'lean sd':>9}{'width':>8}{'taper':>8}{'hand x':>9}{'hand y':>9}{'hand sd x':>11}")
    out = {}
    for side, key in (('r', 'right'), ('l', 'left')):
        r = measure(side); s = SPEC[key]
        if not r: print(f"  {key}: no arm"); continue
        sg = np.array([x['signed'] for x in r])
        q1, q3 = np.percentile(sg, 25), np.percentile(sg, 75)
        k = sg[(sg > q1-1.5*(q3-q1)-1e-9) & (sg < q3+1.5*(q3-q1)+1e-9)]
        pr = np.nanmedian(np.array([x['prof'] for x in r]), 0)
        hx = np.array([x['hand'][0] for x in r]); hy = np.array([x['hand'][1] for x in r])
        print(f"  {key:<6}{s['signed_lean']:>9.1f}{s['lean_std']:>9.1f}{s['width']:>8.3f}"
              f"{'1.27' if key=='right' else '1.19':>8}{s['hand'][0]:>9.3f}{s['hand'][1]:>9.3f}{s['hand_std'][0]:>11.3f}   MCCS")
        print(f"  {'':<6}{np.median(k):>9.1f}{k.std():>9.1f}{np.nanmedian(pr):>8.3f}"
              f"{pr[-1]/pr[0]:>8.2f}{np.median(hx):>9.3f}{np.median(hy):>9.3f}{hx.std():>11.3f}   ours (n={len(r)})")
        out[key] = dict(lean=float(np.median(k)), width=float(np.nanmedian(pr)),
                        hand=[float(np.median(hx)), float(np.median(hy))],
                        prof=[float(v) for v in pr])
    return out

if __name__ == '__main__':
    report('fixed-direction model, before fitting')
