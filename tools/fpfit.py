"""Fit the first-person composition to CS:MC, measured rather than eyeballed.

The 20 static shots in PHOTO2/ give a screen-space spec for both arms: where each
enters the frame, how far it leans, how wide it is, and where the hand ends up.
This renders our own composition across the whole knife fleet and measures it with
exactly the same statistics, so the comparison is like for like.
"""
import sys, os, math, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
from fpsim import *
import fpship, fparm

W, H = 480, 270
FY = 1.0/math.tan(math.radians(40.0)); FX = FY/(W/H)
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SPEC = json.load(open(os.path.join(ROOT, '.tmp-csmc/mccs_solved.json')))
BG = (30, 34, 40)

def to_view(sx, sy, d):
    """Screen fraction + depth -> SC view space."""
    return ((sx-0.5)*2.0*d/FX, (0.5-sy)*2.0*d/FY, -d)

def stats(masks):
    """The PHOTO2 statistics: entrance by convergence of the arm axes."""
    lines, widths, tops = [], [], []
    for m in masks:
        ys, xs = np.nonzero(m)
        if len(xs) < 200: continue
        pts = np.stack([xs, ys], 1).astype(float); c = pts.mean(0)
        _, _, vt = np.linalg.svd(pts-c, full_matrices=False); ax = vt[0]
        if ax[1] < 0: ax = -ax
        w = (pts-c)@vt[1]
        wd = np.percentile(w, 97)-np.percentile(w, 3)
        if wd < 4: continue
        t = (pts-c)@ax
        lines.append((c, ax)); widths.append(wd/W); tops.append((c+ax*t.min())/[W, H])
    if len(lines) < 4: return None
    A = np.zeros((2, 2)); b = np.zeros(2)
    for c, a in lines:
        P = np.eye(2)-np.outer(a, a); A += P; b += P@c
    e = np.linalg.solve(A, b)
    ang = [math.degrees(math.atan2(abs(a[0]), abs(a[1]))) for _, a in lines]
    tops = np.array(tops)
    return dict(entrance=[e[0]/W, e[1]/H], lean=float(np.median(ang)),
                width=float(np.median(widths)),
                hand=[float(np.median(tops[:, 0])), float(np.median(tops[:, 1]))], n=len(lines))

def arm_masks(side, clip='idle', t=0.0):
    """One silhouette per knife for a single arm, rendered alone."""
    out = []
    bv, bf = load_obj(fparm.BOX)
    ent = fparm.LEFT_ENTRANCE if side == 'l' else fparm.RIGHT_ENTRANCE
    for name in sorted(fpship.GRIP):
        try:
            rig = Rig(name); pose = rig.pose(clip, t); place = fpship.placement(rig)
        except Exception:
            continue
        off = fparm.LEFT_GRIPS.get(name, [0, 0, 0]) if side == 'l' else fpship.GRIP[name]
        wrist = mul(pose['bindings'][f'hand_{side}'], place)
        m = fparm.arm_matrix(ent, xform(off, wrist), wrist, side == 'l')
        if not m: continue
        fr = Frame(W, H, 80.0)
        fr.mesh(bv, bf, m, (200, 200, 200))
        a = np.array(fr.color, dtype=np.int16).reshape(H, W, 3)
        out.append(np.abs(a-np.array(BG)).sum(2) > 12)
    return out

def report(tag=''):
    rows = {}
    print(f"{tag}\n{'':8}{'enter x':>9}{'enter y':>9}{'lean':>8}{'width':>8}{'hand x':>9}{'hand y':>9}")
    for side, key in (('r', 'right'), ('l', 'left')):
        s = SPEC[key]; m = stats(arm_masks(side))
        print(f"  {key:<6}{s['entrance'][0]:>9.3f}{s['entrance'][1]:>9.3f}{s['lean']:>8.1f}"
              f"{s['width']:>8.3f}{s['hand'][0]:>9.3f}{s['hand'][1]:>9.3f}   MCCS")
        if m is None:
            print(f"  {'':<6}{'arm not drawn':>9}"); continue
        d = [m['entrance'][0]-s['entrance'][0], m['entrance'][1]-s['entrance'][1],
             m['lean']-s['lean'], m['width']-s['width'],
             m['hand'][0]-s['hand'][0], m['hand'][1]-s['hand'][1]]
        print(f"  {'':<6}{m['entrance'][0]:>9.3f}{m['entrance'][1]:>9.3f}{m['lean']:>8.1f}"
              f"{m['width']:>8.3f}{m['hand'][0]:>9.3f}{m['hand'][1]:>9.3f}   ours (n={m['n']})")
        print(f"  {'':<6}{d[0]:>+9.3f}{d[1]:>+9.3f}{d[2]:>+8.1f}{d[3]:>+8.3f}{d[4]:>+9.3f}{d[5]:>+9.3f}   diff")
        rows[key] = m
    return rows

if __name__ == '__main__':
    report('current values')
