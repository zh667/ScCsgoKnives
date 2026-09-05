#!/usr/bin/env python3
"""Render a hotbar icon for a gun that has no CS:MC one.

The three guns that shipped first take their <gun>_slot.png from CS:MC's client jar
(tools/install_guns.py). The guns added from CS2 are not in that jar, so their slot
was missing and Survivalcraft drew its missing-texture placeholder - a slice of the
terrain atlas, which is what the 0.18.0 hotbar showed.

CS2 does have real icons, at panorama/images/econ/weapons/base_weapons/, 68 of them.
They are not in the local export yet. Until they are, this rasterises the gun's own
.cs2.parts against its own base colour: orthographic, z-buffered, one directional
light, cropped to the silhouette and padded.

Orientation is the rig's, not the bounding box's. Every CS2 gun's weapon_offset bind
frame maps Source forward (+X) to mesh +Z and Source up (+Z) to mesh +Y - checked
across all eleven .cs2.parts - so the barrel is mesh +Z and up is mesh +Y. The icon
shows the gun's left side with the muzzle to the left, which is how CS:MC's three
are drawn, and the camera angles (yaw, pitch, roll) come from
tools/fit_slot_icon_camera.py, which searches them against those three icons'
silhouettes. 0.18.1 measured the barrel as "the longest extent" and drew a flat
side view, and the hotbar showed two styles side by side.

Usage:  python3 tools/render_gun_slot_icons.py deagle glock18 [--size 128]
        (on Windows: python tools\\render_gun_slot_icons.py ...)
"""

from __future__ import annotations

import argparse
import json
import struct
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "src/ScCsgoKnives/AnimationData"
TEXTURES = ROOT / "src/ScCsgoKnives/Assets/Textures/ScCsgoKnives"
CAMERA = ROOT / "tools/reference/slot_icon_camera.json"


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


# Mesh (x left, y up, z forward) -> screen (x right, y up, z towards the viewer):
# the muzzle goes to screen -x and the gun's left side faces the viewer.
BASE = np.array([[0.0, 0.0, -1.0],
                 [0.0, 1.0, 0.0],
                 [1.0, 0.0, 0.0]], np.float32)


def view_matrix(yaw: float, pitch: float, roll: float) -> np.ndarray:
    """Rows are the screen axes in mesh space; screen = mesh @ M.T.

    yaw turns the gun about screen y (positive shows more of its front face),
    pitch tips it about screen x (positive looks down on it), roll tilts it in the
    image plane (positive lifts the muzzle). Degrees.
    """
    y, p, r = np.radians([yaw, pitch, roll])
    ry = np.array([[np.cos(y), 0, np.sin(y)], [0, 1, 0], [-np.sin(y), 0, np.cos(y)]], np.float32)
    rx = np.array([[1, 0, 0], [0, np.cos(p), -np.sin(p)], [0, np.sin(p), np.cos(p)]], np.float32)
    # A positive rotation about +z lowers a point on -x; the muzzle is on -x, so
    # the tilt that lifts it is the negative rotation.
    rz = np.array([[np.cos(-r), -np.sin(-r), 0], [np.sin(-r), np.cos(-r), 0], [0, 0, 1]], np.float32)
    return rz @ rx @ ry @ BASE


def camera() -> dict:
    if CAMERA.exists():
        return json.loads(CAMERA.read_text("utf-8"))
    return {"yaw": 0, "pitch": 0, "roll": 0}


def rasterize(gun: str, size: int, supersample: int = 4, parts_dir: Path = DATA,
              yaw: float = None, pitch: float = None, roll: float = None):
    """The icon before tone: per-pixel base colour, |n.l| and coverage at n = size*supersample."""
    cam = camera()
    yaw = cam["yaw"] if yaw is None else yaw
    pitch = cam["pitch"] if pitch is None else pitch
    roll = cam["roll"] if roll is None else roll
    pos, nor, uv, tris = read_parts(parts_dir / ("%s.cs2.parts" % gun))
    texture = None
    for name in ("%s_hd" % gun, gun):
        p = TEXTURES / ("%s.png" % name)
        if p.exists():
            texture = np.asarray(Image.open(p).convert("RGB"), np.float32) / 255.0
            break
    if texture is None:
        raise SystemExit("%s: no base colour texture" % gun)

    m = view_matrix(yaw, pitch, roll)
    view = pos @ m.T
    vnor = nor @ m.T
    n = size * supersample
    lo, hi = view[:, :2].min(0), view[:, :2].max(0)
    span = float(max(hi[0] - lo[0], hi[1] - lo[1]))
    if span <= 0:
        raise SystemExit("%s: degenerate bounds" % gun)
    centre = (lo + hi) * 0.5
    scale = (n * 0.92) / span
    screen = (view[:, :2] - centre) * np.float32([scale, -scale]) + n * 0.5
    depth = -view[:, 2]                              # smaller is nearer the viewer

    albedo = np.zeros((n, n, 3), np.float32)
    ndotl = np.zeros((n, n), np.float32)
    alpha = np.zeros((n, n), bool)
    zbuf = np.full((n, n), np.inf, np.float32)
    # From the upper left and in front, in screen space, as the CS:MC icons read.
    light = np.float32([-0.4, 0.6, 0.7])
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
        normal = (a[..., None] * vnor[tri[0]] + bl[..., None] * vnor[tri[1]] + c[..., None] * vnor[tri[2]])
        length = np.linalg.norm(normal, axis=-1, keepdims=True)
        normal = normal / np.where(length < 1e-6, 1.0, length)
        # One-sided: a face turned from the light goes to ambient. Two-sided |n.l|
        # never darkened anything and the tone fit could not reach the CS:MC icons'
        # tenth percentile (54 against 35-45) however hard it pushed diffuse.
        nl = np.clip((normal * light).sum(-1), 0.0, 1.0)
        rgb = texture[ty, tx]
        target = albedo[y0:y1, x0:x1]; target[win] = rgb[win]; albedo[y0:y1, x0:x1] = target
        lt = ndotl[y0:y1, x0:x1]; lt[win] = nl[win]; ndotl[y0:y1, x0:x1] = lt
        zt = zbuf[y0:y1, x0:x1]; zt[win] = z[win]; zbuf[y0:y1, x0:x1] = zt
        at = alpha[y0:y1, x0:x1]; at[win] = True; alpha[y0:y1, x0:x1] = at
    return albedo, ndotl, alpha


def shade(albedo, ndotl, alpha, size: int, ambient: float, diffuse: float, gamma: float) -> Image.Image:
    """Tone: albedo * (|n.l| * diffuse + ambient), then gamma; crop, pad, downsample."""
    lit = np.clip(albedo * (ndotl * diffuse + ambient)[..., None], 0, 1)
    rgb = np.power(lit, 1.0 / gamma)
    rgba = np.concatenate([rgb, alpha[..., None].astype(np.float32)], -1)
    im = Image.fromarray((rgba * 255).astype(np.uint8), "RGBA")
    box = im.getbbox()
    if box:
        im = im.crop(box)
        pad = max(im.size) // 12
        square = Image.new("RGBA", (max(im.size) + pad * 2,) * 2, (0, 0, 0, 0))
        square.paste(im, ((square.width - im.width) // 2, (square.height - im.height) // 2))
        im = square
    return im.resize((size, size), Image.LANCZOS)


def render(gun: str, size: int, supersample: int = 4, parts_dir: Path = DATA,
           yaw: float = None, pitch: float = None, roll: float = None,
           ambient: float = None, diffuse: float = None, gamma: float = None) -> Image.Image:
    cam = camera()
    ambient = cam.get("ambient", 0.35) if ambient is None else ambient
    diffuse = cam.get("diffuse", 0.75) if diffuse is None else diffuse
    gamma = cam.get("gamma", 1.0) if gamma is None else gamma
    albedo, ndotl, alpha = rasterize(gun, size, supersample, parts_dir, yaw, pitch, roll)
    return shade(albedo, ndotl, alpha, size, ambient, diffuse, gamma)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("guns", nargs="+")
    ap.add_argument("--size", type=int, default=128)
    ap.add_argument("--parts-dir", type=Path, default=DATA)
    ap.add_argument("--out-dir", type=Path, default=TEXTURES)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()
    cam = camera()
    print("camera yaw %s pitch %s roll %s (%s)" % (cam.get("yaw"), cam.get("pitch"), cam.get("roll"),
                                                  CAMERA.name if CAMERA.exists() else "no fit yet, flat"))
    for gun in args.guns:
        im = render(gun, args.size, parts_dir=args.parts_dir)
        out = args.out_dir / ("%s_slot.png" % gun)
        if not args.dry_run:
            im.save(out, optimize=True)
        covered = np.asarray(im)[..., 3].mean() / 255
        print("%-14s %dx%d, %4.1f%% of the icon covered -> %s (%.1f KB)"
              % (gun, args.size, args.size, 100 * covered, out.name,
                 out.stat().st_size / 1024 if out.exists() else 0))


if __name__ == "__main__":
    main()
