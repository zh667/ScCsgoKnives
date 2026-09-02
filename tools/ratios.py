"""Scale-free ratios measured off both clients' screenshots, no simulator involved.

Comparing absolute screen fractions between two games means trusting that both
captures are the same field of view. These ratios do not: knife length against
hand separation is a property of the rig alone, so if it differs the knife mesh
really is the wrong size relative to the skeleton, whatever the camera is doing.
"""
import sys, os, glob, json, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from PIL import Image
from armshot import arm_mask, blobs, label

def knife_mask(rgb, mccs):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx, mn = rgb.max(axis=2), rgb.min(axis=2)
    if mccs:
        # Every reference knife is a saturated colour against a plain blue sky;
        # the arms are dark and warm and the sky is the only other blue thing.
        m = (mx - mn > 55) & (mx > 110) & ~((b > r + 20) & (b > 200))
    else:
        # Ours are bare steel: brighter than the overcast sky and unsaturated.
        m = (mx - mn < 40) & (mx > 150)
    m[int(m.shape[0] * 0.84):, :] = False
    return m

def principal_length(mask, W, H):
    ys, xs = np.nonzero(mask)
    p = np.stack([xs / W, ys / H], 1)
    c = p.mean(0)
    _, _, vt = np.linalg.svd(p - c, full_matrices=False)
    proj = (p - c) @ vt[0]
    return float(proj.max() - proj.min()), c

def arms(path):
    rgb = np.asarray(Image.open(path).convert('RGB')).astype(np.int16)
    H, W = rgb.shape[:2]
    lab, keep = blobs(arm_mask(rgb))
    out = []
    for i, c in keep:
        bm = lab == i
        ys, xs = np.nonzero(bm)
        # The HUD panels sit in a top corner and never touch the HUD line.
        if ys.max() < int(H * 0.855) - 3: continue
        top = ys.min()
        out.append(dict(x=float(xs[ys < top + 14].mean() / W), y=float(top / H),
                        area=c / float(W * H), lo=float(xs.min()/W), hi=float(xs.max()/W)))
    out.sort(key=lambda d: d['x'])
    return out, (W, H)

def report(folder, mccs):
    print(f'######## {folder}')
    rows = []
    for f in sorted(glob.glob(os.path.join(folder, '*.png'))):
        a, (W, H) = arms(f)
        rgb = np.asarray(Image.open(f).convert('RGB')).astype(np.int16)
        km = knife_mask(rgb, mccs)
        lab, keep = blobs(km, min_frac=0.003)
        klen, kc = (None, None)
        if keep:
            klen, kc = principal_length(lab == keep[0][0], W, H)
        if len(a) < 2 or klen is None:
            print(f'  {os.path.basename(f)}: arms={len(a)} knife={klen}'); continue
        left, right = a[0], a[-1]
        sep = float(np.hypot(right['x'] - left['x'], right['y'] - left['y']))
        rows.append(dict(f=os.path.basename(f)[-9:-4], sep=sep, klen=klen,
                         L=(left['x'], left['y']), Rt=(right['x'], right['y']),
                         ratio=klen / sep))
        print(f"  {rows[-1]['f']}  left=({left['x']:.3f},{left['y']:.3f})  right=({right['x']:.3f},{right['y']:.3f})"
              f"  handSep={sep:.3f}  knifeLen={klen:.3f}  knife/sep={klen/sep:.2f}")
    if rows:
        print(f"  MEDIAN handSep {np.median([r['sep'] for r in rows]):.3f}   "
              f"knifeLen {np.median([r['klen'] for r in rows]):.3f}   "
              f"knife/sep {np.median([r['ratio'] for r in rows]):.2f}")
    return rows

if __name__ == '__main__':
    m = report('photo', True)
    o = report('PHOTO2', False)
    if m and o:
        rm = np.median([r['ratio'] for r in m]); ro = np.median([r['ratio'] for r in o])
        print(f'\nknife length / hand separation:  CS:MC {rm:.2f}   ours {ro:.2f}   '
              f'ours is {ro/rm:.2f}x')
