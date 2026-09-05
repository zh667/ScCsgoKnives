"""Measures arms and knife off the sky-background screenshots.

Sky shots make both segmentable with a colour rule instead of a background
difference: the arm is the only warm (R>B) thing in frame and the knife is the
only desaturated thing.  The HUD band is cut off and treated as a border so
armstat drops the bins it truncates instead of counting them as narrowing.
"""
import sys, os, glob, json, numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from armstat import arm_stats

HUD_TOP = 0.84          # everything below this is hearts/hotbar, not the scene

def load(path):
    return np.asarray(Image.open(path).convert('RGB')).astype(np.int16)

def masks(rgb):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    mx = rgb.max(axis=2)
    mn = rgb.min(axis=2)
    sat = mx - mn
    arm = (r > b + 25) & (mx > 40)                       # warm: skin or sleeve
    sky = (b > r + 15)                                   # blue sky and cloud shade
    knife = (~arm) & (~sky) & (sat < 45) & (mx > 45)     # grey metal
    cut = int(rgb.shape[0] * HUD_TOP)
    for m in (arm, sky, knife):
        m[cut:, :] = False
    return arm, knife

def biggest_blobs(mask, n=2):
    """Flood fill without scipy: iterative label by scanline union-find."""
    H, W = mask.shape
    lab = np.zeros((H, W), np.int32)
    nxt = 1
    parent = {0: 0}
    def find(x):
        while parent[x] != x: x = parent[x]
        return x
    def union(a, b):
        ra, rb = find(a), find(b)
        if ra != rb: parent[max(ra, rb)] = min(ra, rb)
    ys, xs = np.nonzero(mask)
    for y, x in zip(ys, xs):
        up = lab[y-1, x] if y else 0
        left = lab[y, x-1] if x else 0
        if up and left:
            lab[y, x] = min(up, left); union(up, left)
        elif up or left:
            lab[y, x] = up or left
        else:
            lab[y, x] = nxt; parent[nxt] = nxt; nxt += 1
    flat = np.zeros(nxt, np.int32)
    for i in range(1, nxt): flat[i] = find(i)
    lab = flat[lab]
    ids, counts = np.unique(lab[lab > 0], return_counts=True)
    order = np.argsort(-counts)[:n]
    return [(lab == ids[i], int(counts[i])) for i in order]

def report(path):
    rgb = load(path)
    H, W = rgb.shape[:2]
    arm, knife = masks(rgb)
    out = {'file': os.path.basename(path), 'w': W, 'h': H}
    blobs = biggest_blobs(arm, 2)
    sides = []
    for m, area in blobs:
        ys, xs = np.nonzero(m)
        sides.append((xs.mean(), m, area))
    sides.sort()                                        # left blob first
    labels = ['left', 'right'][:len(sides)]
    for label, (_, m, area) in zip(labels, sides):
        s = arm_stats(m)
        ys, xs = np.nonzero(m)
        top = ys.min()
        cx = xs[ys < top + 12].mean()
        out[label] = {
            'area_frac': area / float(W * H),
            'lean': s.get('lean'),
            'width_hand': s.get('width_hand'),
            'hand': [round(cx / W, 4), round(top / float(H), 4)],
        }
    kb = biggest_blobs(knife, 1)
    if kb:
        m, area = kb[0]
        ys, xs = np.nonzero(m)
        pts = np.stack([xs / W, ys / H], 1)
        c = pts.mean(0)
        u, sv, vt = np.linalg.svd(pts - c, full_matrices=False)
        proj = (pts - c) @ vt[0]
        out['knife'] = {
            'area_frac': area / float(W * H),
            'length_frac': float(proj.max() - proj.min()),
            'centre': [round(float(c[0]), 4), round(float(c[1]), 4)],
            'axis_deg': round(float(np.degrees(np.arctan2(-vt[0][1], vt[0][0]))), 1),
        }
    return out, arm, knife

if __name__ == '__main__':
    for folder in ('photo', 'PHOTO2'):
        print(f'######## {folder}')
        for f in sorted(glob.glob(os.path.join(folder, '*.png'))):
            o, arm, knife = report(f)
            print(json.dumps(o, ensure_ascii=False))
            vis = np.zeros((*arm.shape, 3), np.uint8)
            vis[arm] = (255, 120, 0); vis[knife] = (0, 200, 255)
            Image.fromarray(vis).resize((480, 270)).save(
                f'.tmp-cmp/mask_{folder}_{os.path.basename(f)[-9:-4]}.png')
