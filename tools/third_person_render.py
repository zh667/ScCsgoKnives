#!/usr/bin/env python3
"""Rasterise PackageCheck's --third-person-out JSON (vanilla human + mod weapon, world triangles) into PNG
contact sheets: front, left side, three-quarter. Flat shading, z-buffer, pure numpy.

    python3 tools/third_person_render.py <dir> [--out sheet.png] [--size 420]
"""
import argparse, glob, json, math, os
import numpy as np
from PIL import Image, ImageDraw

COLOURS = {"Body": (150, 110, 90), "Head": (220, 190, 160), "Hand1": (200, 160, 130), "Hand2": (200, 160, 130), "Leg1": (70, 90, 150), "Leg2": (70, 90, 150)}

def load(path):
    d = json.load(open(path))
    tris = []
    for m in d["meshes"]:
        p = np.array(m["positions"], float).reshape(-1, 3)
        idx = np.array(m["indices"], int).reshape(-1, 3)
        colour = COLOURS.get(m["name"], (60, 60, 60) if m["name"].startswith("weapon") else (120, 120, 120))
        tris.append((p[idx], colour, m["name"]))
    return d, tris

def render(tris, points, view, size):
    # view: (eye, target); orthographic-ish perspective camera, up = +Y
    eye, target = np.array(view[0], float), np.array(view[1], float)
    f = target - eye; f /= np.linalg.norm(f)
    r = np.cross(f, [0, 1, 0]); r /= np.linalg.norm(r); u = np.cross(r, f)
    img = np.full((size, size, 3), 245, np.uint8); z = np.full((size, size), np.inf)
    focal = size * 1.6
    def project(p):
        v = p - eye
        x, y, d = v @ r, v @ u, v @ f
        d = np.maximum(d, 1e-3)
        return np.stack([size / 2 + focal * x / d, size / 2 - focal * y / d, d], -1)
    light = np.array([0.3, 0.8, 0.5]); light /= np.linalg.norm(light)
    for verts, colour, name in tris:
        n = np.cross(verts[:, 1] - verts[:, 0], verts[:, 2] - verts[:, 0])
        ln = np.linalg.norm(n, axis=1); keep = ln > 1e-12
        n = n[keep] / ln[keep][:, None]; verts = verts[keep]
        shade = 0.45 + 0.55 * np.abs(n @ light)
        pv = project(verts.reshape(-1, 3)).reshape(-1, 3, 3)
        for t, s in zip(pv, shade):
            x0, y0 = int(max(0, t[:, 0].min())), int(max(0, t[:, 1].min()))
            x1, y1 = int(min(size - 1, t[:, 0].max())), int(min(size - 1, t[:, 1].max()))
            if x1 < x0 or y1 < y0: continue
            xs, ys = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
            (ax, ay), (bx, by), (cx, cy) = t[0, :2], t[1, :2], t[2, :2]
            det = (bx - ax) * (cy - ay) - (cx - ax) * (by - ay)
            if abs(det) < 1e-9: continue
            w0 = ((bx - xs) * (cy - ys) - (cx - xs) * (by - ys)) / det
            w1 = ((cx - xs) * (ay - ys) - (ax - xs) * (cy - ys)) / det
            w2 = 1 - w0 - w1
            inside = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
            if not inside.any(): continue
            depth = w0 * t[0, 2] + w1 * t[1, 2] + w2 * t[2, 2]
            sub = z[y0:y1 + 1, x0:x1 + 1]
            mask = inside & (depth < sub)
            sub[mask] = depth[mask]
            col = (np.array(colour) * s).clip(0, 255).astype(np.uint8)
            img[y0:y1 + 1, x0:x1 + 1][mask] = col
    out = Image.fromarray(img); draw = ImageDraw.Draw(out)
    for name, p in points.items():
        q = project(np.array([p], float))[0]
        colour = {"fist": (255, 0, 0), "gripLeft": (0, 160, 0), "muzzle": (0, 0, 255)}.get(name, (255, 140, 0))
        if 0 <= q[0] < size and 0 <= q[1] < size: draw.ellipse([q[0] - 3, q[1] - 3, q[0] + 3, q[1] + 3], fill=colour)
    return out

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("dir"); ap.add_argument("--out", default=None); ap.add_argument("--size", type=int, default=420)
    ap.add_argument("--only", default=None)
    a = ap.parse_args()
    files = sorted(glob.glob(os.path.join(a.dir, "*.json")))
    if a.only: files = [f for f in files if a.only in os.path.basename(f)]
    views = [("front", ([0, 1.1, 3.2], [0, 1.0, 0])), ("left", ([-3.2, 1.1, 0], [0, 1.0, 0])), ("3/4", ([-2.3, 1.6, 2.3], [0, 0.95, 0]))]
    rows = []
    for f in files:
        d, tris = load(f)
        cells = [render(tris, d["points"], v, a.size) for _, v in views]
        row = Image.new("RGB", (a.size * len(cells), a.size + 18), (255, 255, 255))
        for i, c in enumerate(cells): row.paste(c, (i * a.size, 18))
        ImageDraw.Draw(row).text((4, 2), "%s  right=(%.2f,%.2f) left=(%.2f,%.2f) two=%s" % (os.path.basename(f)[:-5], d["right"][0], d["right"][1], d["left"][0], d["left"][1], d["twoHanded"]), fill=(0, 0, 0))
        rows.append(row)
    sheet = Image.new("RGB", (rows[0].width, sum(r.height for r in rows)), (255, 255, 255))
    y = 0
    for r in rows: sheet.paste(r, (0, y)); y += r.height
    out = a.out or os.path.join(a.dir, "sheet.png"); sheet.save(out); print(out, sheet.size)

if __name__ == "__main__":
    main()
