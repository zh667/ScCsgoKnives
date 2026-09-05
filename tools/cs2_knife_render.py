#!/usr/bin/env python3
"""Draw a CS2-route knife (or gun) frame offline, every triangle coloured by the
joint it rides, so a stray piece of geometry can be traced to its bone without a
device. Pure Python: the rig JSON is sampled here, the .cs2.skin skinned here,
and the placement / projection come from cs2_placement.py.

    python3 tools/cs2_knife_render.py butterfly idle 0.0 [--out PNG] [--size 960x540]
    python3 tools/cs2_knife_render.py butterfly inspect 4.05 --zoom 3 --centre 0.62,0.72
"""
import argparse, json, math, os, struct, sys
import numpy as np
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import cs2_placement as P

DATA = os.path.join(os.path.dirname(HERE), "src/ScCsgoKnives/AnimationData")


def quat_to_matrix(q):
    x, y, z, w = q
    return np.array([[1 - 2 * (y * y + z * z), 2 * (x * y + z * w), 2 * (x * z - y * w)],
                     [2 * (x * y - z * w), 1 - 2 * (x * x + z * z), 2 * (y * z + x * w)],
                     [2 * (x * z + y * w), 2 * (y * z - x * w), 1 - 2 * (x * x + y * y)]])


def key(curve, t):
    ts, vs = curve["Times"], curve["Values"]
    if len(ts) == 1 or t <= ts[0]:
        return np.array(vs[0], float)
    if t >= ts[-1]:
        return np.array(vs[-1], float)
    hi = np.searchsorted(ts, t)
    lo = hi - 1
    f = (t - ts[lo]) / max(1e-6, ts[hi] - ts[lo])
    a, b = np.array(vs[lo], float), np.array(vs[hi], float)
    if len(a) == 4:
        if np.dot(a, b) < 0:
            b = -b
        q = a + (b - a) * f
        return q / np.linalg.norm(q)
    return a + (b - a) * f


def sample(rig, clip_name, t):
    """Absolute bone matrices (row-vector convention: p @ M) in rig inches."""
    sk = rig["Skeleton"]
    clip = rig["Clips"][clip_name]
    local = []
    for b in sk:
        T, R = np.array(b["Translation"], float), np.array(b["Rotation"], float)
        cur = clip["Bones"].get(b["Name"])
        if cur:
            if cur.get("Translation", {}).get("Values"):
                T = key(cur["Translation"], t)
            if cur.get("Rotation", {}).get("Values"):
                R = key(cur["Rotation"], t)
        M = np.eye(4)
        M[:3, :3] = quat_to_matrix(R)
        M[3, :3] = T
        local.append(M)
    absolute = [None] * len(sk)

    def calc(i):
        if absolute[i] is not None:
            return absolute[i]
        p = sk[i]["Parent"]
        absolute[i] = local[i] @ calc(p) if p >= 0 else local[i]
        return absolute[i]

    for i in range(len(sk)):
        calc(i)
    return {b["Name"]: absolute[i] for i, b in enumerate(sk)}


def read_skin(path):
    with open(path, "rb") as f:
        assert f.read(8) == b"SCK2SKIN"
        ver, = struct.unpack("<I", f.read(4))
        assert ver == 2
        nj, = struct.unpack("<H", f.read(2))

        def rs():
            n, = struct.unpack("<H", f.read(2))
            return f.read(n).decode()
        joints, inverse = [], []
        for _ in range(nj):
            joints.append(rs())
            inverse.append(np.array(struct.unpack("<16f", f.read(64))).reshape(4, 4))
        nv, = struct.unpack("<i", f.read(4))
        raw = np.frombuffer(f.read(nv * (12 + 12 + 8 + 4 + 16)), dtype=np.uint8).reshape(nv, 52)
        pos = raw[:, 0:12].copy().view("<f4").reshape(nv, 3)
        bones = raw[:, 32:36].copy()
        wts = raw[:, 36:52].copy().view("<f4").reshape(nv, 4)
        npr, = struct.unpack("<H", f.read(2))
        prims = []
        for _ in range(npr):
            mat = rs()
            n, = struct.unpack("<i", f.read(4))
            prims.append((mat, np.frombuffer(f.read(4 * n), dtype="<u4").astype(int)))
    return joints, inverse, pos, bones, wts, prims


def resolve_clip(rig, alias):
    for stem, c in rig["Clips"].items():
        if c.get("Alias") == alias or stem == alias:
            return stem
    raise SystemExit("no clip for %s; have %s" % (alias, sorted(c.get("Alias") for c in rig["Clips"].values())))


PALETTE = [(230, 80, 80), (80, 200, 80), (80, 120, 240), (240, 200, 60), (200, 80, 220), (60, 210, 210), (240, 140, 40), (160, 160, 160)]


def render(asset, alias, t, size, zoom, centre, out):
    rig = json.load(open(os.path.join(DATA, "%s.cs2.animation.json" % asset)))
    joints, inverse, pos, bones, wts, prims = read_skin(os.path.join(DATA, "%s.cs2.skin" % asset))
    stem = resolve_clip(rig, alias)
    absolute = sample(rig, stem, t)
    place = P.placement()
    skinning = []
    for j, name in enumerate(joints):
        if name in absolute:
            skinning.append(inverse[j] @ absolute[name] @ place)
        else:
            skinning.append(None)
    hom = np.concatenate([pos, np.ones((len(pos), 1))], 1)
    view = np.zeros((len(pos), 3))
    dominant = np.zeros(len(pos), int)
    for v in range(len(pos)):
        acc = np.zeros(4)
        best, bestw = -1, 0.0
        for k in range(4):
            w = wts[v, k]
            if w <= 0:
                continue
            m = skinning[bones[v, k]]
            if m is None:
                continue
            acc += w * (hom[v] @ m)
            if w > bestw:
                best, bestw = bones[v, k], w
        view[v] = acc[:3]
        dominant[v] = best
    W, H = size
    fx, fy = P.projection_scales(P.CVARS["viewmodel_fov"], W / H)
    screen = P.to_screen(view, fx, fy, W, H)
    # zoom about a screen-fraction centre
    cx, cy = centre[0] * W, centre[1] * H
    sx = (screen[:, 0] - cx) * zoom + W / 2
    sy = (screen[:, 1] - cy) * zoom + H / 2
    depth = screen[:, 2]
    img = Image.new("RGB", (W, H), (40, 44, 52))
    draw = ImageDraw.Draw(img)
    tris = []
    for mat, idx in prims:
        for i in range(0, len(idx), 3):
            a, b, c = idx[i], idx[i + 1], idx[i + 2]
            if view[a, 2] > -0.01 or view[b, 2] > -0.01 or view[c, 2] > -0.01:
                continue
            tris.append((-(depth[a] + depth[b] + depth[c]) / 3, a, b, c))
    tris.sort()   # painter's order, far first (depth is -z, so more negative = farther)
    counts = {}
    for _, a, b, c in tris:
        j = dominant[a]
        col = PALETTE[j % len(PALETTE)]
        # cheap lighting from the winding
        draw.polygon([(sx[a], sy[a]), (sx[b], sy[b]), (sx[c], sy[c])], fill=col)
        counts[joints[j]] = counts.get(joints[j], 0) + 1
    legend = ", ".join("%s=%s" % (joints[j], PALETTE[j % len(PALETTE)]) for j in range(len(joints)) if joints[j] in counts)
    draw.rectangle((0, 0, W, 14), fill=(0, 0, 0))
    draw.text((2, 1), "%s %s@%.2fs  %s" % (asset, stem, t, legend), fill=(255, 255, 0))
    img.save(out)
    print("wrote", out, "triangles per joint:", counts)
    # where each joint's geometry lands on screen (unzoomed)
    for j, name in enumerate(joints):
        m = dominant == j
        if m.any() and (wts[m] > 0).any():
            print("  %-14s screen x %.0f..%.0f  y %.0f..%.0f  depth %.3f..%.3f" % (
                name, screen[m, 0].min(), screen[m, 0].max(), screen[m, 1].min(), screen[m, 1].max(),
                view[m, 2].min(), view[m, 2].max()))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("asset")
    ap.add_argument("clip")
    ap.add_argument("t", type=float)
    ap.add_argument("--out")
    ap.add_argument("--size", default="960x540")
    ap.add_argument("--zoom", type=float, default=1.0)
    ap.add_argument("--centre", default="0.5,0.5")
    a = ap.parse_args()
    W, H = (int(x) for x in a.size.split("x"))
    centre = tuple(float(x) for x in a.centre.split(","))
    out = a.out or os.path.join("/tmp", "%s_%s_%.2f.png" % (a.asset, a.clip, a.t))
    render(a.asset, a.clip, a.t, (W, H), a.zoom, centre, out)


if __name__ == "__main__":
    main()
