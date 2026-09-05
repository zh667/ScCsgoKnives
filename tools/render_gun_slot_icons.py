#!/usr/bin/env python3
"""Render a hotbar icon for a gun that has no CS:MC one.

The three guns that shipped first take their <gun>_slot.png from CS:MC's client jar
(tools/install_guns.py). The guns added from CS2 are not in that jar, so their slot
was missing and Survivalcraft drew its missing-texture placeholder - a slice of the
terrain atlas, which is what the 0.18.0 hotbar showed.

CS2 does have real icons, at panorama/images/econ/weapons/base_weapons/, 68 of them.
They are not in the local export yet. Until they are, this rasterises the gun's own
.cs2.parts against its own base colour: orthographic, z-buffered, lit by a single
headlamp, cropped to the silhouette and padded, which is close enough to read in a
hotbar and is the gun's own geometry rather than a drawing.

Usage:  python3 tools/render_gun_slot_icons.py deagle glock18 [--size 128]
        (on Windows: python tools\\render_gun_slot_icons.py ...)
"""

from __future__ import annotations

import argparse
import struct
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "src/ScCsgoKnives/AnimationData"
TEXTURES = ROOT / "src/ScCsgoKnives/Assets/Textures/ScCsgoKnives"


def read_parts(path: Path):
    b = path.read_bytes()
    off = 8
    (version,) = struct.unpack_from("<I", b, off); off += 4
    if version != 1:
        raise SystemExit("%s: unsupported version %d" % (path.name, version))
    (joints,) = struct.unpack_from("<H", b, off); off += 2
    for _ in range(joints):
        (n,) = struct.unpack_from("<H", b, off); off += 2 + n + 64
    (count,) = struct.unpack_from("<I", b, off); off += 4
    rows = np.frombuffer(b, np.uint8, count * 32, off).reshape(count, 32); off += count * 32
    pos = rows[:, 0:12].copy().view(np.float32).reshape(count, 3)
    nor = rows[:, 12:24].copy().view(np.float32).reshape(count, 3)
    uv = rows[:, 24:32].copy().view(np.float32).reshape(count, 2)
    (parts,) = struct.unpack_from("<H", b, off); off += 2
    tris = []
    for _ in range(parts):
        off += 2                                     # joint
        (n,) = struct.unpack_from("<H", b, off); off += 2 + n
        (indices,) = struct.unpack_from("<I", b, off); off += 4
        idx = np.frombuffer(b, np.uint32, indices, off); off += indices * 4
        tris.append(idx.reshape(-1, 3))
    return pos, nor, uv, np.concatenate(tris) if tris else np.zeros((0, 3), np.uint32)


def render(gun: str, size: int, supersample: int = 4) -> Image.Image:
    pos, nor, uv, tris = read_parts(DATA / ("%s.cs2.parts" % gun))
    texture = None
    for name in ("%s_hd" % gun, gun):
        p = TEXTURES / ("%s.png" % name)
        if p.exists():
            texture = np.asarray(Image.open(p).convert("RGB"), np.float32) / 255.0
            break
    if texture is None:
        raise SystemExit("%s: no base colour texture" % gun)

    # Which axis the barrel lies along is not assumed - the GLB and the rig do not
    # agree on a convention and the first attempt drew every gun end-on, 12 pixels
    # wide. It is measured: the longest extent is the barrel and goes across the icon,
    # the shortest is the depth, and what is left is up. That is how CS:MC's own icons
    # are laid out, a gun lying flat, e.g. ak47_slot is 123 x 62.
    extent = pos.max(0) - pos.min(0)
    order = np.argsort(extent)                 # shortest, middle, longest
    across, up, into = int(order[2]), int(order[1]), int(order[0])
    view = np.stack([pos[:, across], pos[:, up], pos[:, into]], 1)
    n = size * supersample
    lo, hi = view[:, :2].min(0), view[:, :2].max(0)
    span = float(max(hi[0] - lo[0], hi[1] - lo[1]))
    if span <= 0:
        raise SystemExit("%s: degenerate bounds" % gun)
    centre = (lo + hi) * 0.5
    scale = (n * 0.92) / span
    screen = (view[:, :2] - centre) * np.float32([scale, -scale]) + n * 0.5
    depth = view[:, 2]

    colour = np.zeros((n, n, 3), np.float32)
    alpha = np.zeros((n, n), bool)
    zbuf = np.full((n, n), np.inf, np.float32)
    light = np.float32([0.35, 0.6, -0.72])
    light /= np.linalg.norm(light)

    yy, xx = np.mgrid[0:n, 0:n]
    th, tw = texture.shape[:2]
    for tri in tris:
        p = screen[tri]
        x0, y0 = np.maximum(np.floor(p.min(0)).astype(int), 0)
        x1, y1 = np.minimum(np.ceil(p.max(0)).astype(int) + 1, n)
        if x1 <= x0 or y1 <= y0:
            continue
        det = ((p[1, 1] - p[2, 1]) * (p[0, 0] - p[2, 0]) + (p[2, 0] - p[1, 0]) * (p[0, 1] - p[2, 1]))
        if abs(det) < 1e-9:
            continue
        gx, gy = xx[y0:y1, x0:x1], yy[y0:y1, x0:x1]
        a = ((p[1, 1] - p[2, 1]) * (gx - p[2, 0]) + (p[2, 0] - p[1, 0]) * (gy - p[2, 1])) / det
        bl = ((p[2, 1] - p[0, 1]) * (gx - p[2, 0]) + (p[0, 0] - p[2, 0]) * (gy - p[2, 1])) / det
        c = 1.0 - a - bl
        inside = (a >= 0) & (bl >= 0) & (c >= 0)
        if not inside.any():
            continue
        z = a * depth[tri[0]] + bl * depth[tri[1]] + c * depth[tri[2]]
        win = inside & (z < zbuf[y0:y1, x0:x1])
        if not win.any():
            continue
        u = a * uv[tri[0], 0] + bl * uv[tri[1], 0] + c * uv[tri[2], 0]
        v = a * uv[tri[0], 1] + bl * uv[tri[1], 1] + c * uv[tri[2], 1]
        tx = np.clip((u % 1.0) * (tw - 1), 0, tw - 1).astype(int)
        ty = np.clip((v % 1.0) * (th - 1), 0, th - 1).astype(int)
        normal = (a[..., None] * nor[tri[0]] + bl[..., None] * nor[tri[1]] + c[..., None] * nor[tri[2]])
        normal = np.stack([normal[..., across], normal[..., up], normal[..., into]], -1)
        length = np.linalg.norm(normal, axis=-1, keepdims=True)
        normal = normal / np.where(length < 1e-6, 1.0, length)
        shade = np.clip(np.abs((normal * light).sum(-1)), 0.0, 1.0) * 0.75 + 0.35
        rgb = texture[ty, tx] * shade[..., None]
        target = colour[y0:y1, x0:x1]
        target[win] = np.clip(rgb[win], 0, 1)
        colour[y0:y1, x0:x1] = target
        zt = zbuf[y0:y1, x0:x1]; zt[win] = z[win]; zbuf[y0:y1, x0:x1] = zt
        at = alpha[y0:y1, x0:x1]; at[win] = True; alpha[y0:y1, x0:x1] = at

    rgba = np.concatenate([colour, alpha[..., None].astype(np.float32)], -1)
    im = Image.fromarray((rgba * 255).astype(np.uint8), "RGBA")
    # Crop to the silhouette, pad a little, then down to the icon size.
    box = im.getbbox()
    if box:
        im = im.crop(box)
        pad = max(im.size) // 12
        square = Image.new("RGBA", (max(im.size) + pad * 2,) * 2, (0, 0, 0, 0))
        square.paste(im, ((square.width - im.width) // 2, (square.height - im.height) // 2))
        im = square
    return im.resize((size, size), Image.LANCZOS)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("guns", nargs="+")
    ap.add_argument("--size", type=int, default=128)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()
    for gun in args.guns:
        im = render(gun, args.size)
        out = TEXTURES / ("%s_slot.png" % gun)
        if not args.dry_run:
            im.save(out, optimize=True)
        covered = np.asarray(im)[..., 3].mean() / 255
        print("%-14s %dx%d, %4.1f%% of the icon covered -> %s (%.1f KB)"
              % (gun, args.size, args.size, 100 * covered, out.name,
                 out.stat().st_size / 1024 if out.exists() else 0))


if __name__ == "__main__":
    main()
