#!/usr/bin/env python3
"""The AUG / SG 553 zoomed HUD, from CS2's scope_filter.

    materials/models/weapons/shared/scope/scope_filter.vmat   shader ui.vfx
    materials/models/weapons/shared/scope/scope_filter.png    256 x 256

Measured here so the renderer's reading of it is on record: a circle of radius
0.64 of the image's half-size, tinted (0,9,8) at alpha 120 inside, feathering to
opaque black by 0.76 and black beyond. It is the one ui.vfx material in the
shared scope folder, i.e. the HUD image of the scope those two guns aim down
(the sniper HUD is panorama/images/hud/scope/scope_circle + scope_line_blur).
Its on-screen size is not in the export; the renderer draws it as a
screen-height square, which is recorded as an assumption there.

    python3 tools/cs2_scope_filter_texture.py [--check]
"""
import argparse, json, os, sys
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.expanduser("~/workspaces/CSMCReverse/local_cs2_analysis/all_weapons/07_scope/materials/models/weapons/shared/scope")
DEST = os.path.join(ROOT, "src/ScCsgoKnives/Assets/Textures/ScCsgoKnives/cs2_scope_filter.png")


def profile(a):
    h, w = a.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    r = np.sqrt((xx - w / 2 + 0.5) ** 2 + (yy - h / 2 + 0.5) ** 2) / (w / 2)
    out = {}
    for r0 in (0.3, 0.6, 0.64, 0.7, 0.76, 0.9):
        m = (r >= r0 - 0.01) & (r < r0 + 0.01)
        out[str(r0)] = {"rgb": [int(x) for x in a[m][:, :3].mean(axis=0).round()], "alpha": int(a[m][:, 3].mean().round())}
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()
    vmat = open(os.path.join(SRC, "scope_filter.vmat"), encoding="utf-8").read()
    if '"ui.vfx"' not in vmat:
        sys.exit("scope_filter.vmat is no longer ui.vfx")
    img = Image.open(os.path.join(SRC, "scope_filter.png")).convert("RGBA")
    a = np.asarray(img, np.float32)
    prof = profile(a)
    inside, edge, outside = prof["0.3"], prof["0.7"], prof["0.9"]
    ok = inside["alpha"] < 140 and outside["alpha"] >= 250 and inside["alpha"] < edge["alpha"] < outside["alpha"]
    print(json.dumps({"source": "07_scope/materials/models/weapons/shared/scope/scope_filter.png",
                      "size": list(img.size), "profile": prof,
                      "reading": "window radius 0.64 of the half-size, feathered to 0.76, tint inside, black outside",
                      "ok": ok}, indent=1))
    if not ok:
        sys.exit("scope_filter.png does not look like a tinted window in black")
    if not args.check:
        img.save(DEST)
        print("wrote", DEST, os.path.getsize(DEST), "bytes")
    return 0


if __name__ == "__main__":
    sys.exit(main())
