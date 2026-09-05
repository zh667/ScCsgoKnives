#!/usr/bin/env python3
"""Depth-buffered rasteriser for the cs2 profile: what is actually *visible*.

The acceptance masks have to be occluded masks. A left hand wrapped round a
handguard is mostly hidden by the weapon, and the grip ROI - where the hand test
is scored - is exactly where the occlusion is strongest, so an un-occluded
projection of the arm triangles compares a silhouette the screenshot cannot
contain and fails geometry that is correct.

So weapon and arms go into one z-buffer and the winner of each pixel decides
which mask it belongs to:

    weapon      the CS2 body_hd parts, placed by their bindings
    left_hand   arm triangles whose vertices are weighted to *_L joints
    right_hand  the same for *_R

Everything runs in the maths cs2_placement_selftest proves the shipped C# agrees
with (5.4e-06 m, 0.05 px), so a mask from here stands for what the mod draws.
"""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np

import cs2_arms_selftest as arms
import cs2_placement as place
import cs2_viewmodel as vm
from cs2_rig_selftest import GUNS

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "src/ScCsgoKnives/AnimationData"
MODELS = ROOT / "src/ScCsgoKnives/Assets/Models/ScCsgoKnives"

WEAPON, LEFT, RIGHT = 1, 2, 3


def read_obj_triangles(path: Path):
    """Vertices and triangle indices from one of the emitted OBJ parts."""
    verts = []
    faces = []
    for line in path.read_text("utf-8").splitlines():
        if line.startswith("v "):
            verts.append([float(x) for x in line.split()[1:4]])
        elif line.startswith("f "):
            faces.append([int(tok.split("/")[0]) - 1 for tok in line.split()[1:4]])
    return np.array(verts, float), np.array(faces, int)


def weapon_triangles(gun: str, clip_alias: str, t: float):
    """CS2 weapon geometry in rig inches, placed exactly as the renderer places it."""
    doc = json.loads((DATA / ("%s.cs2.animation.json" % gun)).read_text("utf-8"))
    cfg = GUNS[gun]
    stem = {v: k for k, v in cfg["clips"].items()}.get(clip_alias, clip_alias)
    clip = vm.load_clip(vm.CLIPS / cfg["folder"] / (stem + ".dmx"))
    absolute = clip.absolute(t)
    names = [b["Name"] for b in doc["Skeleton"]]
    points = []
    tris = []
    for binding in doc["Bindings"]:
        path = MODELS / ("%s_cs2_%s.obj" % (gun, binding["Name"]))
        if not path.exists():
            continue
        v, f = read_obj_triangles(path)
        if not len(v) or not len(f):
            continue
        right = np.array(binding["RightMatrix"], float).reshape(4, 4)
        bone = names[binding["BoneIndex"]]
        placed = (np.c_[v, np.ones(len(v))] @ right @ absolute[bone])[:, :3]
        tris.append(f + sum(len(p) for p in points))
        points.append(placed)
    if not points:
        return np.zeros((0, 3)), np.zeros((0, 3), int)
    return np.vstack(points), np.vstack(tris)


def arm_triangles(gun: str, clip_alias: str, t: float):
    """Skinned arms in rig inches, plus a per-triangle side label."""
    joints, ibm, pos, nor, w, j, mesh = arms.load_arms()
    cfg = GUNS[gun]
    stem = {v: k for k, v in cfg["clips"].items()}.get(clip_alias, clip_alias)
    clip = vm.load_clip(vm.CLIPS / cfg["folder"] / (stem + ".dmx"))
    skinned = arms.skin(joints, ibm, pos, w, j, clip.absolute(t), np.eye(4))

    side = np.zeros(len(pos), np.int8)
    for k in range(4):
        heavy = w[:, k] > 0.5
        side[heavy & np.array([joints[x].endswith("_L") for x in j[:, k]])] = LEFT
        side[heavy & np.array([joints[x].endswith("_R") for x in j[:, k]])] = RIGHT

    tris = np.vstack([p.indices.reshape(-1, 3) for p in mesh.primitives])
    labels = np.where((side[tris] == LEFT).all(1), LEFT,
                      np.where((side[tris] == RIGHT).all(1), RIGHT, 0)).astype(np.int8)
    return skinned, tris, labels


def rasterise(gun: str, clip_alias: str, t: float, cvars, width: int, height: int):
    """One z-buffer over weapon and arms. Returns the winner-id image and the depth."""
    placement = place.placement(cvars)
    fx, fy = place.projection_scales(cvars["viewmodel_fov"], width / height)

    wp, wt = weapon_triangles(gun, clip_alias, t)
    ap, at, al = arm_triangles(gun, clip_alias, t)

    def to_screen(points):
        view = (np.c_[points, np.ones(len(points))] @ placement)[:, :3]
        return place.to_screen(view, fx, fy, width, height)     # x, y, depth

    sw = to_screen(wp) if len(wp) else np.zeros((0, 3))
    sa = to_screen(ap) if len(ap) else np.zeros((0, 3))

    ids = np.zeros((height, width), np.int8)
    depth = np.full((height, width), np.inf)
    yy, xx = np.mgrid[0:height, 0:width]

    def draw(screen, tris, labels):
        for tri, label in zip(tris, labels):
            if label == 0:
                continue
            p = screen[tri]
            if (p[:, 2] <= 0.02).any():
                continue
            x0, y0 = np.floor(p[:, :2].min(0)).astype(int)
            x1, y1 = np.ceil(p[:, :2].max(0)).astype(int) + 1
            x0, y0 = max(x0, 0), max(y0, 0)
            x1, y1 = min(x1, width), min(y1, height)
            if x1 <= x0 or y1 <= y0:
                continue
            gx, gy = xx[y0:y1, x0:x1], yy[y0:y1, x0:x1]
            det = ((p[1, 1] - p[2, 1]) * (p[0, 0] - p[2, 0])
                   + (p[2, 0] - p[1, 0]) * (p[0, 1] - p[2, 1]))
            if abs(det) < 1e-9:
                continue
            a = ((p[1, 1] - p[2, 1]) * (gx - p[2, 0]) + (p[2, 0] - p[1, 0]) * (gy - p[2, 1])) / det
            b = ((p[2, 1] - p[0, 1]) * (gx - p[2, 0]) + (p[0, 0] - p[2, 0]) * (gy - p[2, 1])) / det
            c = 1.0 - a - b
            inside = (a >= 0) & (b >= 0) & (c >= 0)
            if not inside.any():
                continue
            # Perspective-correct depth: interpolate 1/z, which is linear in screen space.
            inv = a / p[0, 2] + b / p[1, 2] + c / p[2, 2]
            z = np.where(np.abs(inv) < 1e-12, np.inf, 1.0 / np.where(inv == 0, 1e-12, inv))
            here = inside & (z < depth[y0:y1, x0:x1])
            if not here.any():
                continue
            sub_d = depth[y0:y1, x0:x1]
            sub_i = ids[y0:y1, x0:x1]
            sub_d[here] = z[here]
            sub_i[here] = label
            depth[y0:y1, x0:x1] = sub_d
            ids[y0:y1, x0:x1] = sub_i

    if len(wt):
        draw(sw, wt, np.full(len(wt), WEAPON, np.int8))
    if len(at):
        draw(sa, at, al)
    return ids, depth


_CACHE = {}


def masks(gun: str, clip_alias: str, t: float, cvars, width: int, height: int):
    """Cached: PLACE and HAND ask for the same frame, and this is a Python rasteriser."""
    key = (gun, clip_alias, round(float(t), 6), width, height,
           tuple(sorted((k, float(v)) for k, v in cvars.items()
                        if k.startswith("viewmodel_"))))
    hit = _CACHE.get(key)
    if hit is None:
        ids, depth = rasterise(gun, clip_alias, t, cvars, width, height)
        hit = _CACHE[key] = {"weapon": ids == WEAPON, "left_hand": ids == LEFT,
                             "right_hand": ids == RIGHT, "ids": ids, "depth": depth}
    return hit


def contour(mask: np.ndarray) -> np.ndarray:
    """Boundary pixels of a binary mask (4-neighbour erosion difference)."""
    inner = mask.copy()
    inner[1:, :] &= mask[:-1, :]
    inner[:-1, :] &= mask[1:, :]
    inner[:, 1:] &= mask[:, :-1]
    inner[:, :-1] &= mask[:, 1:]
    return mask & ~inner


def chamfer(a: np.ndarray, b: np.ndarray):
    """Symmetric contour distance in pixels: (mean, p95, max). NaN if either is empty."""
    from scipy.ndimage import distance_transform_edt

    ca, cb = contour(a), contour(b)
    if not ca.any() or not cb.any():
        return float("nan"), float("nan"), float("nan")
    da = distance_transform_edt(~ca)
    db = distance_transform_edt(~cb)
    both = np.concatenate([db[ca], da[cb]])
    return float(both.mean()), float(np.percentile(both, 95)), float(both.max())


def centroid(mask: np.ndarray):
    ys, xs = np.nonzero(mask)
    if not len(xs):
        return None
    return np.array([xs.mean(), ys.mean()])


def iou(a: np.ndarray, b: np.ndarray) -> float:
    union = (a | b).sum()
    return float((a & b).sum() / union) if union else 0.0


if __name__ == "__main__":
    import argparse
    from PIL import Image

    ap = argparse.ArgumentParser()
    ap.add_argument("gun", choices=sorted(GUNS))
    ap.add_argument("--clip", default="idle")
    ap.add_argument("--t", type=float, default=0.0)
    ap.add_argument("--size", default="1400x1050")
    ap.add_argument("--out", type=Path)
    args = ap.parse_args()
    w, h = (int(v) for v in args.size.lower().split("x"))
    m = masks(args.gun, args.clip, args.t, place.CVARS, w, h)
    print("%s %s t=%.2f at %dx%d" % (args.gun, args.clip, args.t, w, h))
    for key in ("weapon", "left_hand", "right_hand"):
        c = centroid(m[key])
        print("   %-11s %7d px  centroid %s"
              % (key, m[key].sum(), "-" if c is None else np.round(c, 1)))
    if args.out:
        args.out.mkdir(parents=True, exist_ok=True)
        for key in ("weapon", "left_hand", "right_hand"):
            Image.fromarray((m[key] * 255).astype(np.uint8), "L").save(
                args.out / ("%s_%s.png" % (args.gun, key)))
        print("   wrote masks to %s" % args.out)
