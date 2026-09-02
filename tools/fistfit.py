"""Fit the fist boxes to the CS:MC arm silhouettes.

Right arm per knife: the box's axis runs from the grip G (measured, fixed) down
the screen at `lean`, reaching the frame edge nearer the eye by `near`; the cap
sits `overshoot` (a fraction of the box width) beyond G; the box is face-on to
the camera.  Free: lean, near, overshoot, width, depth ratio.  Score: IoU with
the photo's right-arm mask.
Left arm: same box, but its cap centre is free too (there is no knife to pin it).
"""
import sys, os, json, math, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import verify_cs as V
import mccs_masks as M
from fistsolve import REF, frac, W, H, DEPTH

PX, PY = V.PX, V.PY
ARM_EXIT_Y = V.C['ArmExitY']
w2, h2 = W // 2, H // 2

def to_view(sx, sy, d): return np.array([(sx - 0.5) * 2 * d / PX, (0.5 - sy) * 2 * d / PY, -d])
def to_screen(p):
    d = -p[2]
    return None if d <= 1e-4 else np.array([p[0] * PX / d * 0.5 + 0.5, 0.5 - p[1] * PY / d * 0.5])

def far_point(grip, lean_deg, near):
    depth = -grip[2]
    sx, sy = grip[0] * PX / depth * 0.5 + 0.5, 0.5 - grip[1] * PY / depth * 0.5
    lean = math.radians(lean_deg)
    stepx, stepy = math.sin(lean) / (PY / PX), math.cos(lean)
    run = min(4.0, max(0.05, (ARM_EXIT_Y - sy) / max(stepy, 0.05)))
    return to_view(sx + stepx * run, sy + stepy * run, depth / max(near, 0.1))

def box_corners(grip, lean, near, width_frac, overshoot_ratio, depth_ratio):
    depth = -grip[2]
    width = width_frac * 2 * depth / PX          # view units at the grip's depth
    far = far_point(grip, lean, near)
    axis = far - grip; L = np.linalg.norm(axis); axis /= L
    los = grip / np.linalg.norm(grip)             # line of sight to the grip
    side = los - axis * np.dot(los, axis); side /= np.linalg.norm(side)   # face-on
    up = np.cross(side, axis); up /= np.linalg.norm(up)
    seat = grip - axis * (overshoot_ratio * width)
    thick = width * depth_ratio
    cs = []
    for a in (0.0, L + overshoot_ratio * width):
        for su in (-0.5, 0.5):
            for ss in (-0.5, 0.5):
                cs.append(seat + axis * a + up * (su * width) + side * (ss * thick))
    return np.array(cs), dict(axis=axis, side=side, up=up, seat=seat, width=width)

def hull_mask(corners):
    pts = []
    for c in corners:
        s = to_screen(c)
        if s is None: return None
        pts.append([s[0] * w2, s[1] * h2])
    pts = np.array(pts)
    o = pts[np.lexsort((pts[:, 1], pts[:, 0]))]
    cross2 = lambda a, b: a[0] * b[1] - a[1] * b[0]
    def half(ps):
        st = []
        for p in ps:
            while len(st) > 1 and cross2(st[-1] - st[-2], p - st[-2]) <= 0: st.pop()
            st.append(p)
        return st[:-1]
    hull = np.array(half(o) + half(o[::-1]))
    if len(hull) < 3: return None
    y0, y1 = max(0, int(hull[:, 1].min())), min(h2 - 1, int(hull[:, 1].max()) + 1)
    if y1 <= y0: return None
    msk = np.zeros((h2, w2), bool)
    for y in range(y0, y1):
        yc = y + 0.5; xs = []
        for j in range(len(hull)):
            a, b = hull[j], hull[(j + 1) % len(hull)]
            if (a[1] <= yc < b[1]) or (b[1] <= yc < a[1]):
                xs.append(a[0] + (yc - a[1]) * (b[0] - a[0]) / (b[1] - a[1]))
        if len(xs) < 2: continue
        x0, x1 = max(0, int(min(xs))), min(w2, int(max(xs)) + 1)
        if x1 > x0: msk[y, x0:x1] = True
    return msk

def iou(a, b, region):
    a, b = a & region, b & region
    u = (a | b).sum()
    return (a & b).sum() / u if u else 0.0

def fit_box(grip, target, region, init, bounds, fixed=None):
    """Coordinate descent on (lean, near, width, overshoot, depth_ratio)."""
    p = dict(init)
    def score(q):
        c, _ = box_corners(grip, q['lean'], q['near'], q['width'], q['overshoot'], q['depth'])
        m = hull_mask(c)
        return -1.0 if m is None else iou(m, target, region)
    best = score(p)
    steps = dict(lean=4.0, near=0.2, width=0.008, overshoot=0.15, depth=0.25)
    for _ in range(4):
        improved = True
        while improved:
            improved = False
            for key in steps:
                if fixed and key in fixed: continue
                for sgn in (1, -1):
                    q = dict(p); q[key] = p[key] + sgn * steps[key]
                    lo, hi = bounds[key]
                    if not (lo <= q[key] <= hi): continue
                    s = score(q)
                    if s > best + 1e-5: best, p, improved = s, q, True
        for key in steps: steps[key] *= 0.5
    return p, best

BOUNDS = dict(lean=(-70, 70), near=(0.9, 3.0), width=(0.03, 0.16), overshoot=(0.0, 1.6), depth=(0.5, 1.6))

if __name__ == '__main__':
    out = {}
    for name in REF:
        im = M.load(name); sky, arm, hud, knife = M.masks(im)
        right = M.right_arm_region(arm)[::2, ::2]
        r = REF[name]
        grip = to_view(*frac(r['G']), DEPTH)
        region = np.zeros((h2, w2), bool); region[(r['F'][1] - 40) // 2:, 450:] = True
        init = dict(lean=r['lean0'], near=1.5, width=r['w'] / W, overshoot=0.8, depth=1.0)
        p, s = fit_box(grip, right, region, init, BOUNDS)
        c, fr = box_corners(grip, p['lean'], p['near'], p['width'], p['overshoot'], p['depth'])
        cap = to_screen(fr['seat']) * [W, H]
        print(f"{name:<10} right: lean {p['lean']:+.1f} near {p['near']:.2f} width {p['width']:.4f} "
              f"overshoot {p['overshoot']:.2f}w depth {p['depth']:.2f}  IoU {s:.3f}  cap->({cap[0]:.0f},{cap[1]:.0f}) meas F {r['F']}")
        out[name] = dict(right=p, right_iou=s)
        # left arm: cap centre free.  Seed from the mask's top rows.
        left = (arm & (np.arange(W)[None, :] < 900))[::2, ::2]
        rows = np.nonzero(left[350:].any(1))[0] + 350
        top = rows.min()
        xs = np.nonzero(left[top + 8])[0]
        cap0 = ((xs.min() + xs.max()) / 2 / w2, (top + 8) / h2)
        regionL = np.zeros((h2, w2), bool); regionL[top - 15:, :450] = True
        bestL = None
        for dx in np.linspace(-0.05, 0.05, 5):
            for dy in np.linspace(-0.02, 0.06, 5):
                gL = to_view(cap0[0] + dx, cap0[1] + dy, 0.55)
                pL, sL = fit_box(gL, left, regionL, dict(lean=-50, near=1.4, width=0.07, overshoot=0.0, depth=1.0),
                                 BOUNDS, fixed={'overshoot'})
                if bestL is None or sL > bestL[0]: bestL = (sL, pL, (cap0[0] + dx, cap0[1] + dy))
        sL, pL, gL = bestL
        print(f"{name:<10} left:  lean {pL['lean']:+.1f} near {pL['near']:.2f} width {pL['width']:.4f} depth {pL['depth']:.2f}"
              f"  cap=({gL[0]:.3f},{gL[1]:.3f}) IoU {sL:.3f}")
        out[name]['left'] = dict(pL, capx=gL[0], capy=gL[1], iou=sL)
    json.dump(out, open('.tmp-fist/fistfit.json', 'w'), indent=1)
