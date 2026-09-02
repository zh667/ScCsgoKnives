"""Blade length on screen, measured the same way on both clients.

Blade only, not the whole knife: on the reference the handle is black and merges
with the arm, so a whole-knife measure there is really a blade measure. Comparing
our whole knife against it is what made 0.9.0's knife too small.
"""
import sys, os, glob, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from PIL import Image, ImageDraw
from armshot import blobs

def blade_mask(rgb, mccs):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx, mn = rgb.max(axis=2), rgb.min(axis=2)
    if mccs:
        # Saturated paint against a plain blue sky; the arms are dark and warm.
        m = (mx - mn > 55) & (mx > 110) & ~((b > r + 20) & (b > 190))
    else:
        # Bare steel: grey, and unlike the cloud it has no blue cast.
        m = (b - r < 18) & (mx - mn < 38) & (mx > 95)
    m[int(m.shape[0] * 0.84):, :] = False
    m[:, :int(m.shape[1] * 0.05)] = False
    return m

def measure(path, mccs, outdir='.tmp-cmp'):
    im = Image.open(path).convert('RGB')
    rgb = np.asarray(im).astype(np.int16)
    H, W = rgb.shape[:2]
    lab, keep = blobs(blade_mask(rgb, mccs), min_frac=0.002)
    if not keep: return None
    bm = lab == keep[0][0]
    ys, xs = np.nonzero(bm)
    p = np.stack([xs / W, ys / H], 1)
    c = p.mean(0)
    _, _, vt = np.linalg.svd(p - c, full_matrices=False)
    proj = (p - c) @ vt[0]
    length = float(proj.max() - proj.min())
    vis = im.copy(); d = ImageDraw.Draw(vis)
    d.rectangle([xs.min(), ys.min(), xs.max(), ys.max()], outline=(0, 255, 0), width=4)
    a = (c + vt[0] * proj.min()) * [W, H]
    z = (c + vt[0] * proj.max()) * [W, H]
    d.line([tuple(a), tuple(z)], fill=(255, 0, 255), width=4)
    vis.resize((W // 3, H // 3)).save(os.path.join(outdir, 'blade_' + os.path.basename(path)[-9:-4] + ('_mccs' if mccs else '_ours') + '.png'))
    return dict(f=os.path.basename(path)[-9:-4], length=length, area=keep[0][1] / float(W * H))

if __name__ == '__main__':
    for folder, mccs in (('photo', True), ('PHOTO2', False)):
        print(f'######## {folder}')
        vals = []
        for f in sorted(glob.glob(os.path.join(folder, '*.png'))):
            r = measure(f, mccs)
            if r: print(f"  {r['f']}  blade {r['length']:.3f} screen widths   area {r['area']:.4f}"); vals.append(r['length'])
        if vals: print(f'  MEDIAN {np.median(vals):.3f}')
