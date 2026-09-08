#!/usr/bin/env python3
"""Render the mod's own gun meshes with a baked skin, offline.

The three skinnable guns ship as normalised OBJ pieces (``<gun>_cs2_<part>.obj``) carrying the
same UVs the runtime samples, so drawing those pieces with ``<gun>_hd__<skin>.png`` shows exactly
what the shader will sample. Textured, z-buffered, flat-lit; no animation pose, the pieces are
already in one assembled space.

This is a texture check, not a game screenshot: it has none of the runtime's PBR, environment
lighting or scope work.

    python3 tools/gun_skin_preview.py [--gun ak47 ...] [--out DIR] [--size 520]
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
MODELS = ROOT / "src/ScCsgoKnives/Assets/Models/ScCsgoKnives"
TEX = ROOT / "src/ScCsgoKnives/Assets/Textures/ScCsgoKnives"
CATALOG = json.loads((ROOT / "tools/gun_skins_catalog.json").read_text(encoding="utf-8"))


def load_obj(path: Path):
    pos, uv, tri = [], [], []
    for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
        f = line.split()
        if not f:
            continue
        if f[0] == "v":
            pos.append([float(f[1]), float(f[2]), float(f[3])])
        elif f[0] == "vt":
            uv.append([float(f[1]), float(f[2]) if len(f) > 2 else 0.0])
        elif f[0] == "f":
            corners = []
            for c in f[1:]:
                p = c.split("/")
                corners.append((int(p[0]) - 1, int(p[1]) - 1 if len(p) > 1 and p[1] else -1))
            for i in range(1, len(corners) - 1):
                tri.append((corners[0], corners[i], corners[i + 1]))
    return np.array(pos, float), np.array(uv, float) if uv else np.zeros((1, 2)), tri


def gun_mesh(gun: str, silencer=False, legacy=False):
    verts, uvs, tris = [], [], []
    prefix = gun + ("_legacy" if legacy else "")
    for obj in sorted(MODELS.glob(f"{prefix}_cs2_*.obj")):
        if obj.stem.endswith("_silencer") and not silencer:
            continue  # the detachable silencer is drawn only when the record says it is on
        p, t, f = load_obj(obj)
        base_v, base_t = len(verts), len(uvs)
        verts.extend(p.tolist())
        lens = legacy and gun == "awp" and "__p2" in obj.stem
        uvs.extend(np.column_stack([t, np.full(len(t), int(lens))]).tolist())
        for a, b, c in f:
            tris.append(((a[0] + base_v, a[1] + base_t if a[1] >= 0 else -1),
                         (b[0] + base_v, b[1] + base_t if b[1] >= 0 else -1),
                         (c[0] + base_v, c[1] + base_t if c[1] >= 0 else -1)))
    return np.array(verts, float), np.array(uvs, float), tris


def render(verts, uvs, tris, texture: np.ndarray, size: int, yaw: float = 0.2, pitch: float = 0.1, shader=None):
    lo, hi = verts.min(0), verts.max(0)
    centre, extent = (lo + hi) / 2, float((hi - lo).max())
    cy, sy = np.cos(yaw), np.sin(yaw)
    cp, sp = np.cos(pitch), np.sin(pitch)
    R = np.array([[cy, 0, -sy], [sp * sy, cp, sp * cy], [cp * sy, -sp, cp * cy]])
    # Runtime OBJ coordinates are Z-up, not Y-up. The old preview looked down
    # the top edge and hid most of the painted side surfaces.
    p = (verts - centre)[:, [0, 2, 1]] @ R.T
    scale = size * 0.86 / max(extent, 1e-6)
    xs = size / 2 + p[:, 0] * scale
    ys = size / 2 - p[:, 1] * scale
    zs = p[:, 2]
    img = np.full((size, size, 3), 0.94, float)
    zbuf = np.full((size, size), -np.inf)  # +Z is toward the viewer after the rotation, so nearer is greater
    th, tw = texture.shape[:2]
    light = np.array([0.35, 0.75, 0.55])
    light /= np.linalg.norm(light)
    for a, b, c in tris:
        ia, ib, ic = a[0], b[0], c[0]
        x = np.array([xs[ia], xs[ib], xs[ic]])
        y = np.array([ys[ia], ys[ib], ys[ic]])
        z = np.array([zs[ia], zs[ib], zs[ic]])
        x0, x1 = int(max(0, np.floor(x.min()))), int(min(size - 1, np.ceil(x.max())))
        y0, y1 = int(max(0, np.floor(y.min()))), int(min(size - 1, np.ceil(y.max())))
        if x1 < x0 or y1 < y0:
            continue
        det = (x[1] - x[0]) * (y[2] - y[0]) - (x[2] - x[0]) * (y[1] - y[0])
        if abs(det) < 1e-9:
            continue
        n = np.cross(p[ib] - p[ia], p[ic] - p[ia])
        ln = np.linalg.norm(n)
        if ln < 1e-12:
            continue
        shade = 0.45 + 0.55 * abs(float(n @ light) / ln)
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
        w0 = ((x[1] - gx) * (y[2] - gy) - (x[2] - gx) * (y[1] - gy)) / det
        w1 = ((x[2] - gx) * (y[0] - gy) - (x[0] - gx) * (y[2] - gy)) / det
        w2 = 1 - w0 - w1
        inside = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
        if not inside.any():
            continue
        depth = w0 * z[0] + w1 * z[1] + w2 * z[2]
        sub = zbuf[y0:y1 + 1, x0:x1 + 1]
        hit = inside & (depth > sub)  # +Z is toward the viewer after the rotation
        sub[hit] = depth[hit]
        if not hit.any():
            continue
        if a[1] >= 0 and b[1] >= 0 and c[1] >= 0:
            u = w0 * uvs[a[1], 0] + w1 * uvs[b[1], 0] + w2 * uvs[c[1], 0]
            v = w0 * uvs[a[1], 1] + w1 * uvs[b[1], 1] + w2 * uvs[c[1], 1]
            tx = np.clip((u % 1.0 * tw).astype(int), 0, tw - 1)
            ty = np.clip((v % 1.0 * th).astype(int), 0, th - 1)
            col = texture[ty, tx]
            if uvs.shape[1] > 2 and uvs[a[1],2] == 1:
                col = np.full(col.shape, .290196)  # shared_scope.vmat, opaque hardware not painted body
        else:
            col = np.full(gx.shape + (3,), 0.5)
        if shader is not None:
            img[y0:y1 + 1, x0:x1 + 1][hit] = shader(col[hit], np.stack([u[hit],v[hit]],-1), n/ln)
        else:
            img[y0:y1 + 1, x0:x1 + 1][hit] = np.clip(col[hit] * shade, 0, 1)
    return Image.fromarray((img * 255).astype(np.uint8), "RGB")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--gun", nargs="*", default=None)
    ap.add_argument("--out", default=str(ROOT / "docs/gun-skins-preview.png"))
    ap.add_argument("--size", type=int, default=420)
    ap.add_argument("--textures", type=Path, default=TEX)
    ap.add_argument("--compare", type=Path, help="baseline texture directory; one before/after row per skin")
    ap.add_argument("--silencer", action="store_true")
    ap.add_argument("--reverse", action="store_true", help="inspect the opposite side")
    a = ap.parse_args()
    guns = CATALOG["guns"]
    rows = []
    for gun_key, gun in guns.items():
        if a.gun and gun_key not in a.gun:
            continue
        verts, uvs, tris = gun_mesh(gun["variantAsset"], a.silencer)
        if a.reverse:
            verts = verts * [-1, -1, 1]
        def shot(path, legacy=False):
            verts, uvs, tris = gun_mesh(gun["variantAsset"], a.silencer, legacy)
            if a.reverse:
                verts = verts * [-1,-1,1]
            texture = np.asarray(Image.open(path).convert("RGB"), float) / 255
            im = render(verts, uvs, tris, texture, a.size)
            return im.crop((0, int(a.size*.23), a.size, int(a.size*.77)))
        skins = [s for s in CATALOG["skins"] if s["gun"] == gun_key]
        if a.compare:
            for s in skins:
                name = f"{gun['variantAsset']}_hd__{s['key']}.png"
                before, after = a.compare/name, a.textures/name
                if not after.exists():
                    continue
                old, new = shot(before, s.get("legacyModel", False)), shot(after, s.get("displayBody", "legacy") == "legacy")
                row = Image.new("RGB", (a.size*2, new.height+30), "white")
                row.paste(old, (0,30))
                row.paste(new, (a.size,30))
                d = ImageDraw.Draw(row)
                d.text((8,8), f"{gun_key} / {s['nameEn']} - BEFORE", fill="black")
                d.text((a.size+8,8), "AFTER - offline texture check, not in-game PBR", fill="black")
                rows.append(row)
            continue
        cells = []
        cells.append(("Original (unchanged)", shot(TEX / f"{gun['variantAsset']}_hd.png")))
        for s in skins:
            p = a.textures / f"{gun['variantAsset']}_hd__{s['key']}.png"
            if not p.exists():
                continue
            display = s.get("displayBody", "legacy" if s.get("legacyModel") else "hd")
            cells.append((f"{s['nameEn']} [{display} UV]", shot(p, display == "legacy")))
        row = Image.new("RGB", (a.size * len(cells), cells[0][1].height + 20), (255, 255, 255))
        d = ImageDraw.Draw(row)
        for i, (label, im) in enumerate(cells):
            row.paste(im, (i * a.size, 20))
            d.text((i * a.size + 4, 5), f"{gun_key}  {label}", fill=(0, 0, 0))
        rows.append(row)
    width = max(r.width for r in rows)
    sheet = Image.new("RGB", (width, sum(r.height for r in rows)), (255, 255, 255))
    y = 0
    for r in rows:
        sheet.paste(r, (0, y))
        y += r.height
    sheet.save(a.out)
    print(a.out, sheet.size)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
