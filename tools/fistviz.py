"""Overlay the fitted composition (knife mesh points + fist boxes) on the CS:MC photos."""
import sys, os, json, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from PIL import Image, ImageDraw
import fistsolve as FS, fistfit as FF, mccs_masks as M

def box_edges(corners):
    idx = lambda a, u, s: a * 4 + u * 2 + s
    edges = []
    for a in (0, 1):
        for u in (0, 1):
            edges.append((idx(a, u, 0), idx(a, u, 1)))
        for s in (0, 1):
            edges.append((idx(a, 0, s), idx(a, 1, s)))
    for u in (0, 1):
        for s in (0, 1):
            edges.append((idx(0, u, s), idx(1, u, s)))
    return edges

def draw(name, kf, ff, out, crop=None):
    im = Image.open(M.PHOTOS[name]).convert('RGB')
    d = ImageDraw.Draw(im)
    fit = FS.Fit(name)
    kk = kf['knives'][name]
    S, ok = fit.project(kk['f'], kk['k'], kf['pitch'], kf['yaw'])
    for (x, y), o in zip(S, ok):
        if o and 0 <= x < FS.W and 0 <= y < FS.H: d.point((x, y), fill=(0, 255, 0))
    r = FS.REF[name]
    grip = FF.to_view(*FS.frac(r['G']), FS.DEPTH)
    p = ff[name]['right']
    c, fr = FF.box_corners(grip, p['lean'], p['near'], p['width'], p['overshoot'], p['depth'])
    pts = [FF.to_screen(v) * [FS.W, FS.H] for v in c]
    for a, b in box_edges(c):
        d.line([tuple(pts[a]), tuple(pts[b])], fill=(255, 255, 0), width=2)
    pl = ff[name]['left']
    gl = FF.to_view(pl['capx'], pl['capy'], 0.55)
    c, _ = FF.box_corners(gl, pl['lean'], pl['near'], pl['width'], 0.0, pl['depth'])
    pts = [FF.to_screen(v) * [FS.W, FS.H] for v in c]
    for a, b in box_edges(c):
        d.line([tuple(pts[a]), tuple(pts[b])], fill=(255, 160, 0), width=2)
    d.ellipse([r['G'][0]-6, r['G'][1]-6, r['G'][0]+6, r['G'][1]+6], outline=(255, 0, 255), width=3)
    if crop: im = im.crop(crop)
    im.save(out)

if __name__ == '__main__':
    kf = json.load(open('.tmp-fist/knifefit.json'))
    ff = json.load(open('.tmp-fist/fistfit.json'))
    tiles = []
    for name in ('m9', 'karambit', 'butterfly', 'tactical'):
        draw(name, kf, ff, f'.tmp-fist/viz_{name}.png', crop=(300, 450, 1750, 1080))
        tiles.append(Image.open(f'.tmp-fist/viz_{name}.png'))
    w, h = tiles[0].size
    sheet = Image.new('RGB', (w * 2, h * 2))
    for i, t in enumerate(tiles): sheet.paste(t, ((i % 2) * w, (i // 2) * h))
    sheet.save('.tmp-fist/viz_sheet.png')
    print('ok')

def draw_cs(name, out, crop=None, dot=1):
    """Overlay the composition the shipped C# builds (verify_cs) on the CS:MC photo."""
    import verify_cs as V
    im = Image.open(M.PHOTOS[name]).convert('RGB')
    d = ImageDraw.Draw(im)
    comp = V.compose(name)
    fit = FS.Fit(name)
    P = fit.k.P
    pts = (np.c_[P, np.ones(len(P))] @ comp['place'])[:, :3]
    for p in pts:
        s = V.screen(p)
        if s is None: continue
        x, y = s[0] * FS.W, s[1] * FS.H
        d.rectangle([x - dot, y - dot, x + dot, y + dot], fill=(0, 255, 0))
    for side, colour in (('r', (255, 255, 0)), ('l', (255, 160, 0))):
        if side == 'l' and not comp['left_usable']: continue
        c = V.box_corners(comp[side])
        pts2 = [V.screen(v) for v in c]
        if any(p is None for p in pts2): continue
        pts2 = [p * [FS.W, FS.H] for p in pts2]
        for a, b in box_edges(c):
            d.line([tuple(pts2[a]), tuple(pts2[b])], fill=colour, width=2)
        g = V.screen(comp[side]['grip']) * [FS.W, FS.H]
        d.ellipse([g[0] - 6, g[1] - 6, g[0] + 6, g[1] + 6], outline=(255, 0, 255), width=3)
    if crop: im = im.crop(crop)
    im.save(out)
    return im

def cs_sheet(out):
    tiles = [draw_cs(n, f'.tmp-fist/cs_{n}.png', crop=(300, 450, 1750, 1080)) for n in ('m9', 'karambit', 'butterfly', 'tactical')]
    w, h = tiles[0].size
    sheet = Image.new('RGB', (w * 2, h * 2))
    for i, t in enumerate(tiles): sheet.paste(t, ((i % 2) * w, (i // 2) * h))
    sheet.save(out)
