"""Segment the four CS:MC reference photos into sky / arm / knife / HUD.

The arm is a dark maroon box (77,55,57); the sky is (130,174,255) with light
clouds absent in these shots.  Everything else that is not HUD is knife.
"""
import numpy as np
from PIL import Image

PHOTOS = {
    'm9':        'photo/Snipaste_2026-09-02_10-31-29.png',
    'karambit':  'photo/Snipaste_2026-09-02_10-31-37.png',
    'butterfly': 'photo/Snipaste_2026-09-02_10-31-46.png',
    'tactical':  'photo/Snipaste_2026-09-02_10-31-54.png',
}
# x0,y0,x1,y1 boxes that are HUD in every photo
HUD = [(0, 0, 300, 330), (1640, 370, 1920, 620), (1680, 0, 1920, 80),
       (670, 955, 1250, 1080), (0, 920, 500, 980), (935, 515, 985, 565)]

def load(name):
    return np.asarray(Image.open(PHOTOS[name]).convert('RGB')).astype(int)

def masks(im):
    r, g, b = im[..., 0], im[..., 1], im[..., 2]
    sky = (b > 200) & (g > 130) & (r < 180) & (b - r > 60)
    # arm faces: dark maroon; the cap is a touch lighter (~90,65,68)
    arm = (r > 55) & (r < 125) & (g > 35) & (g < 95) & (b > 35) & (b < 100) & (r - g > 12) & (r - g < 40) & (abs(g - b) < 12)
    hud = np.zeros_like(sky)
    for x0, y0, x1, y1 in HUD: hud[y0:y1, x0:x1] = True
    knife = ~sky & ~arm & ~hud
    return sky, arm, hud, knife

def clean(mask, min_size=60):
    """Drop connected components smaller than min_size (4-connectivity, via label)."""
    H, W = mask.shape
    lab = np.zeros((H, W), np.int32); n = 0
    ys, xs = np.nonzero(mask)
    todo = set(zip(ys.tolist(), xs.tolist()))
    comps = []
    while todo:
        seed = todo.pop(); stack = [seed]; comp = [seed]; n += 1; lab[seed] = n
        while stack:
            y, x = stack.pop()
            for dy, dx in ((1,0),(-1,0),(0,1),(0,-1)):
                q = (y+dy, x+dx)
                if q in todo:
                    todo.discard(q); lab[q] = n; stack.append(q); comp.append(q)
        comps.append(comp)
    out = np.zeros_like(mask)
    for comp in comps:
        if len(comp) >= min_size:
            for y, x in comp: out[y, x] = True
    return out

def right_arm_region(arm):
    """The right-arm component: the arm pixels right of the knife's midline."""
    return arm & (np.arange(arm.shape[1])[None, :] > 900)

if __name__ == '__main__':
    for name in PHOTOS:
        im = load(name)
        sky, arm, hud, knife = masks(im)
        # small-component cleanup only on the knife mask (the arm is one blob)
        knife_c = clean(knife[::2, ::2], 20)
        H, W = sky.shape
        vis = np.zeros((H//2, W//2, 3), np.uint8)
        vis[sky[::2, ::2]] = (40, 60, 120)
        vis[arm[::2, ::2]] = (200, 80, 80)
        vis[knife_c] = (80, 255, 80)
        vis[hud[::2, ::2]] = (60, 60, 60)
        Image.fromarray(vis).save(f'.tmp-fist/mask_{name}.png')
        ra = right_arm_region(arm)
        rows = np.nonzero(ra.any(1))[0]
        print(f'== {name}: right arm rows {rows.min()}..{rows.max()}, knife px {knife_c.sum()*4}')
        for y in list(range(rows.min(), rows.min()+60, 10)) + list(range(rows.min()+60, 1080, 60)):
            xs = np.nonzero(ra[y])[0]
            if len(xs) == 0: continue
            runs = np.split(xs, np.nonzero(np.diff(xs) > 4)[0] + 1)
            best = max(runs, key=len)
            print(f'   y={y:4d}: x {best[0]:4d}..{best[-1]:4d}  w={best[-1]-best[0]:3d}')
