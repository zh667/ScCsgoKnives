"""Calibrates the composition's free scalars against the reference silhouettes.

Only three numbers are still free once the arm direction comes from the rig:
the knife scale (which sets how far the whole composition spreads across the
frame) and the two arm widths. They are fitted here with the same silhouette
statistic the references were measured with, through verify_cs's transcription of
the shipped C#, so what is fitted is what ships.
"""
import sys, os, json, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import verify_cs as V
import rigprobe as R
from armstat import arm_stats

SPEC = V.SPEC
MODELS = os.path.join(V.ROOT if hasattr(V, 'ROOT') else
                      os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                      'src/ScCsgoKnives/Assets/Models/ScCsgoKnives')

# CS:MC's own M9, measured off Snipaste_2026-09-02_10-31-29.png: the red blade is
# the one thing in frame nothing else shares a colour with, and its visible extent
# is 0.366 screen widths (the handle carries on behind the arm).
REF_KNIFE_LEN = 0.366

def obj_bounds(name, part='weapon_hand_r'):
    """Bounding-box corners of a shipped mesh, in model units."""
    path = os.path.join(MODELS, f'{name}_{part}.obj')
    lo = np.array([1e9]*3); hi = np.array([-1e9]*3)
    with open(path) as fh:
        for line in fh:
            if line.startswith('v '):
                v = np.array([float(x) for x in line.split()[1:4]])
                lo = np.minimum(lo, v); hi = np.maximum(hi, v)
    return [np.array([x, y, z]) for x in (lo[0], hi[0])
            for y in (lo[1], hi[1]) for z in (lo[2], hi[2])]

BOUNDS = {}

def knife_length(name, place, binding):
    """Longest on-screen extent of the knife mesh, in screen widths."""
    if name not in BOUNDS:
        try: BOUNDS[name] = obj_bounds(name)
        except FileNotFoundError: BOUNDS[name] = None
    if BOUNDS[name] is None: return None
    pts = []
    for v in BOUNDS[name]:
        s = V.screen(R.xform(v, binding @ place))
        if s is None: return None
        pts.append(s)
    pts = np.array(pts)
    c = pts.mean(0)
    _, _, vt = np.linalg.svd(pts - c, full_matrices=False)
    proj = (pts - c) @ vt[0]
    return float(proj.max() - proj.min())

def measure(knife_scale, width_r, width_l):
    V.C['KnifeScale'] = knife_scale
    V.C['ArmScreenWidth'] = width_r
    V.C['LeftArmScreenWidth'] = width_l
    out = {}
    for side, key in (('r', 'right'), ('l', 'left')):
        rows, hands = [], []
        for name in R.NAMES:
            c = V.compose(name)
            grip, wrist, elbow = c[side]
            m = V.arm_world(grip, wrist, elbow, c['scale'], side == 'l')
            if m is None: continue
            msk = V.hull_mask(m)
            if msk is None: continue
            st = arm_stats(msk)
            if st and st.get('lean') is not None:
                rows.append(st)
                s = V.screen(grip)
                if s is not None: hands.append(s)
        if not rows: return None
        out[key] = dict(
            lean=float(np.median([x['lean'] for x in rows])),
            width=float(np.median([x['width_hand'] for x in rows])),
            hand=[float(np.median([h[0] for h in hands])), float(np.median([h[1] for h in hands]))])
    return out

def cost(m):
    if m is None: return 1e9
    e = 0.0
    for key in ('right', 'left'):
        s, g = SPEC[key], m[key]
        lean_sd = max(s.get('lean_std', 2.0), 2.0)
        e += ((g['lean'] - s['lean']) / lean_sd) ** 2
        e += ((g['width'] - s['width_hand']) / 0.010) ** 2
        e += ((g['hand'][0] - s['hand'][0]) / max(s['hand_std'][0], 0.02)) ** 2
        e += ((g['hand'][1] - s['hand'][1]) / max(s['hand_std'][1], 0.02)) ** 2
    return e

def sweep():
    best = None
    print(f'{"scale":>7}{"wR":>7}{"wL":>7}{"cost":>9}{"leanR":>8}{"leanL":>8}{"wR out":>8}{"wL out":>8}')
    for ks in np.arange(0.10, 1.35, 0.08):
        m = measure(ks, 0.083, 0.069)
        c = cost(m)
        if m: print(f'{ks:7.2f}{0.083:7.3f}{0.069:7.3f}{c:9.1f}{m["right"]["lean"]:8.1f}'
                    f'{m["left"]["lean"]:8.1f}{m["right"]["width"]:8.3f}{m["left"]["width"]:8.3f}')
        if best is None or c < best[0]: best = (c, ks, 0.083, 0.069)

    # widths are close to independent of scale: solve them by the measured ratio
    _, ks, _, _ = best
    m = measure(ks, 0.083, 0.069)
    wr = 0.083 * SPEC['right']['width_hand'] / m['right']['width']
    wl = 0.069 * SPEC['left']['width_hand'] / m['left']['width']
    for _ in range(6):
        m = measure(ks, wr, wl)
        wr *= SPEC['right']['width_hand'] / m['right']['width']
        wl *= SPEC['left']['width_hand'] / m['left']['width']
    print(f'\nwidth solve -> ArmScreenWidth {wr:.4f}, LeftArmScreenWidth {wl:.4f}')

    fine = None
    for ks in np.arange(max(0.06, ks-0.16), ks+0.16, 0.02):
        m = measure(ks, wr, wl); c = cost(m)
        if fine is None or c < fine[0]: fine = (c, ks, m)
    c, ks, m = fine
    print(f'\nbest: KnifeScale {ks:.3f}  ArmScreenWidth {wr:.4f}  LeftArmScreenWidth {wl:.4f}  cost {c:.1f}')
    for key in ('right', 'left'):
        s, g = SPEC[key], m[key]
        print(f'  {key:<6} lean {g["lean"]:+7.1f} vs {s["lean"]:+6.1f} (sd {s.get("lean_std",0):.1f})   '
              f'width {g["width"]:.3f} vs {s["width_hand"]:.3f}   '
              f'hand ({g["hand"][0]:.3f},{g["hand"][1]:.3f}) vs ({s["hand"][0]:.3f},{s["hand"][1]:.3f})')


if __name__ == '__main__':
    sweep()
