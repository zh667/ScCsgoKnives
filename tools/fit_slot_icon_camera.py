#!/usr/bin/env python3
"""Measure the camera CS:MC's hotbar icons were drawn with.

The three guns that shipped first carry CS:MC's own icons. The guns added from CS2
have icons rendered from their meshes (tools/render_gun_slot_icons.py), and the
first version of those was a flat side view, which the device hotbar showed as a
different style next to the CS:MC three. Rather than guess a "nicer" angle, this
renders the same three guns from their own CS2 meshes under a candidate camera and
scores the silhouette against the CS:MC icon (intersection over union after
fitting the bounding boxes), then searches yaw, pitch and roll for the best score.
The winner is what render_gun_slot_icons.py uses for every gun.

Mesh axes are the rig's: every CS2 gun's weapon_offset bind frame maps Source
forward (+X) to mesh +Z and Source up (+Z) to mesh +Y (checked across all eleven
.cs2.parts files), so "the barrel" is +Z and "up" is +Y and nothing is measured
from the bounding box.

Usage:  python3 tools/fit_slot_icon_camera.py --parts-dir DIR [--json out.json]
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parent))
from render_gun_slot_icons import read_parts, view_matrix  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
TEXTURES = ROOT / "src/ScCsgoKnives/Assets/Textures/ScCsgoKnives"
REFERENCE = ["ak47", "m4a1s", "awp"]


def silhouette(pos: np.ndarray, tris: np.ndarray, yaw: float, pitch: float, roll: float, n: int = 160) -> np.ndarray:
    m = view_matrix(yaw, pitch, roll)
    view = pos @ m.T
    xy = view[:, :2]
    lo, hi = xy.min(0), xy.max(0)
    span = float(max(hi[0] - lo[0], hi[1] - lo[1]))
    scale = (n * 0.9) / span
    screen = (xy - (lo + hi) * 0.5) * np.float32([scale, -scale]) + n * 0.5
    im = Image.new("L", (n, n), 0)
    draw = ImageDraw.Draw(im)
    for tri in tris:
        p = screen[tri]
        draw.polygon([tuple(q) for q in p], fill=255)
    return np.asarray(im) > 0


def fit_boxes(mask: np.ndarray, target_shape: tuple) -> np.ndarray:
    """Crop to the silhouette and scale uniformly so it fills the target's box."""
    ys, xs = np.nonzero(mask)
    crop = mask[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    th, tw = target_shape
    s = min(tw / crop.shape[1], th / crop.shape[0])
    im = Image.fromarray(crop.astype(np.uint8) * 255).resize(
        (max(1, round(crop.shape[1] * s)), max(1, round(crop.shape[0] * s))), Image.BILINEAR)
    out = np.zeros((th, tw), bool)
    a = np.asarray(im) > 127
    y0 = (th - a.shape[0]) // 2
    x0 = (tw - a.shape[1]) // 2
    out[y0:y0 + a.shape[0], x0:x0 + a.shape[1]] = a
    return out


def iou(a: np.ndarray, b: np.ndarray) -> float:
    inter = np.logical_and(a, b).sum()
    union = np.logical_or(a, b).sum()
    return float(inter) / float(union) if union else 0.0


def reference_mask(gun: str) -> np.ndarray:
    im = Image.open(TEXTURES / ("%s_slot.png" % gun)).convert("RGBA")
    a = np.asarray(im)[..., 3] > 8
    ys, xs = np.nonzero(a)
    return a[ys.min():ys.max() + 1, xs.min():xs.max() + 1]


def score(meshes, refs, yaw, pitch, roll, n=160) -> float:
    total = 0.0
    for gun, (pos, tris) in meshes.items():
        ref = refs[gun]
        sil = silhouette(pos, tris, yaw, pitch, roll, n)
        total += iou(fit_boxes(sil, ref.shape), ref)
    return total / len(meshes)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--parts-dir", type=Path, required=True)
    ap.add_argument("--json", type=Path)
    ap.add_argument("--stride", type=int, default=3, help="use every n-th triangle for the search")
    args = ap.parse_args()

    meshes = {}
    for gun in REFERENCE:
        pos, _nor, _uv, tris = read_parts(args.parts_dir / ("%s.cs2.parts" % gun))
        meshes[gun] = (pos, tris[::args.stride])
    refs = {gun: reference_mask(gun) for gun in REFERENCE}
    for gun, ref in refs.items():
        print("%-6s CS:MC icon silhouette %dx%d" % (gun, ref.shape[1], ref.shape[0]))

    # Coarse grid, then refine around the best cell. Roll is the in-plane tilt
    # (positive lifts the muzzle), yaw turns the gun to show its front or back,
    # pitch looks down on it.
    best = (-1.0, None)
    for yaw in range(-40, 41, 10):
        for pitch in range(-30, 31, 10):
            for roll in range(-30, 31, 10):
                s = score(meshes, refs, yaw, pitch, roll)
                if s > best[0]:
                    best = (s, (yaw, pitch, roll))
    print("coarse best %.4f at yaw %d pitch %d roll %d" % (best[0], *best[1]))
    y0, p0, r0 = best[1]
    for yaw in range(y0 - 8, y0 + 9, 2):
        for pitch in range(p0 - 8, p0 + 9, 2):
            for roll in range(r0 - 8, r0 + 9, 2):
                s = score(meshes, refs, yaw, pitch, roll)
                if s > best[0]:
                    best = (s, (yaw, pitch, roll))
    print("fine best   %.4f at yaw %d pitch %d roll %d" % (best[0], *best[1]))
    yaw, pitch, roll = best[1]
    per_gun = {}
    for gun, (pos, tris) in meshes.items():
        sil = silhouette(pos, tris, yaw, pitch, roll, 256)
        per_gun[gun] = round(iou(fit_boxes(sil, refs[gun].shape), refs[gun]), 4)
        print("   %-6s IoU %.4f" % (gun, per_gun[gun]))
    flat = score(meshes, refs, 0, 0, 0)
    print("   (flat side view, the 0.18.1 render, scores %.4f)" % flat)
    result = {"yaw": yaw, "pitch": pitch, "roll": roll, "meanIoU": round(best[0], 4),
              "perGun": per_gun, "flatSideViewIoU": round(flat, 4),
              "method": "silhouette IoU against the CS:MC icon after box fit, mean of ak47/m4a1s/awp"}
    if args.json:
        args.json.write_text(json.dumps(result, indent=2), "utf-8")
    return result


if __name__ == "__main__":
    main()
