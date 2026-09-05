"""Arm silhouettes from the sky screenshots, with an overlay to check by eye.

The rule that works on both clients: an arm is dark and warm (r >= b, max < 150).
Sky and cloud are blue-dominant, the knife blade is bright.  Every measurement
here is drawn back onto the original so the segmentation can be verified rather
than trusted.
"""
import sys, os, glob, json, numpy as np
from PIL import Image, ImageDraw
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from armstat import arm_stats

HUD_TOP = 0.855

def arm_mask(rgb):
    r, b = rgb[..., 0], rgb[..., 2]
    m = (r >= b) & (rgb.max(axis=2) < 150)
    m[int(m.shape[0] * HUD_TOP):, :] = False
    return m

def label(mask):
    H, W = mask.shape
    lab = np.zeros((H, W), np.int32)
    parent = [0]
    def find(x):
        while parent[x] != x: x = parent[x]
        return x
    ys, xs = np.nonzero(mask)
    for y, x in zip(ys, xs):
        up = lab[y-1, x] if y else 0
        lf = lab[y, x-1] if x else 0
        if up and lf:
            lab[y, x] = min(up, lf)
            a, b_ = find(up), find(lf)
            if a != b_: parent[max(a, b_)] = min(a, b_)
        elif up or lf:
            lab[y, x] = up or lf
        else:
            parent.append(len(parent)); lab[y, x] = len(parent) - 1
    flat = np.array([find(i) for i in range(len(parent))], np.int32)
    return flat[lab]

def blobs(mask, min_frac=0.002):
    lab = label(mask)
    ids, counts = np.unique(lab[lab > 0], return_counts=True)
    H, W = mask.shape
    keep = [(int(i), int(c)) for i, c in zip(ids, counts) if c > min_frac * H * W]
    keep.sort(key=lambda t: -t[1])
    return lab, keep

def measure(path, outdir='.tmp-cmp'):
    im = Image.open(path).convert('RGB')
    rgb = np.asarray(im).astype(np.int16)
    H, W = rgb.shape[:2]
    m = arm_mask(rgb)
    lab, keep = blobs(m)
    vis = im.copy(); d = ImageDraw.Draw(vis)
    rows = []
    for n, (i, c) in enumerate(keep):
        bm = lab == i
        ys, xs = np.nonzero(bm)
        s = arm_stats(bm)
        top = ys.min()
        cx = xs[ys < top + 14].mean()
        rows.append({
            'blob': n, 'area_frac': round(c / float(W * H), 5),
            'bbox': [round(xs.min()/W, 3), round(top/H, 3), round(xs.max()/W, 3), round(ys.max()/H, 3)],
            'hand': [round(float(cx/W), 4), round(float(top/H), 4)],
            'lean': round(s['lean'], 1) if s.get('lean') is not None else None,
            'width_hand': round(s['width_hand'], 4) if s.get('width_hand') is not None else None,
            'reaches_hud': bool(ys.max() >= int(H * HUD_TOP) - 3),
        })
        d.rectangle([xs.min(), ys.min(), xs.max(), ys.max()], outline=(0, 255, 0), width=3)
        d.text((xs.min() + 5, ys.min() + 5), f'#{n}', fill=(0, 255, 0))
    vis.resize((W // 3, H // 3)).save(os.path.join(outdir, 'arms_' + os.path.basename(path)[-9:-4] + '_' + os.path.basename(os.path.dirname(path)) + '.png'))
    return {'file': os.path.basename(path), 'blobs': rows}

if __name__ == '__main__':
    for folder in sys.argv[1:] or ['photo', 'PHOTO2']:
        print(f'######## {folder}')
        for f in sorted(glob.glob(os.path.join(folder, '*.png'))):
            r = measure(f)
            print(r['file'])
            for b in r['blobs']:
                print('   ', json.dumps(b))
