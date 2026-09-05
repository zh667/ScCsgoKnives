#!/usr/bin/env python3
"""Measure the tone CS:MC's hotbar icons carry, so rendered ones sit beside them.

With the camera fixed (tools/fit_slot_icon_camera.py), the remaining difference
between a rendered icon and a CS:MC one is exposure: the three CS:MC icons read at
a mean luminance near 83 with a tenth percentile in the mid thirties, while the
0.18.1 renders ranged from 58 (P90) to 129 (Desert Eagle). This renders the same
three guns once, keeps their base colour and |n.l| per pixel, and searches
ambient, diffuse and gamma for the closest match to the CS:MC icons' luminance
mean and tenth/ninetieth percentiles. Those three numbers go into the camera
file next to the angles.

Usage:  python3 tools/fit_slot_icon_tone.py --parts-dir DIR [--json out.json]
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
from render_gun_slot_icons import rasterize, shade  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
TEXTURES = ROOT / "src/ScCsgoKnives/Assets/Textures/ScCsgoKnives"
REFERENCE = ["ak47", "m4a1s", "awp"]


def stats(im: Image.Image):
    a = np.asarray(im.convert("RGBA")).astype(np.float32)
    mask = a[..., 3] > 8
    rgb = a[..., :3][mask]
    lum = 0.299 * rgb[:, 0] + 0.587 * rgb[:, 1] + 0.114 * rgb[:, 2]
    return np.array([lum.mean(), np.percentile(lum, 10), np.percentile(lum, 90)])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--parts-dir", type=Path, required=True)
    ap.add_argument("--json", type=Path)
    ap.add_argument("--size", type=int, default=128)
    args = ap.parse_args()

    refs = {g: stats(Image.open(TEXTURES / ("%s_slot.png" % g))) for g in REFERENCE}
    rasters = {g: rasterize(g, args.size, 2, args.parts_dir) for g in REFERENCE}
    for g in REFERENCE:
        print("%-6s CS:MC icon luminance mean %.1f p10 %.1f p90 %.1f" % (g, *refs[g]))

    best = (1e9, None)
    for ambient in np.arange(0.05, 0.81, 0.05):
        for diffuse in np.arange(0.3, 2.01, 0.1):
            for gamma in (0.8, 0.9, 1.0, 1.1, 1.2, 1.3, 1.4, 1.6, 1.8):
                err = 0.0
                for g in REFERENCE:
                    im = shade(*rasters[g], args.size, float(ambient), float(diffuse), float(gamma))
                    err += float(np.abs(stats(im) - refs[g]).sum())
                if err < best[0]:
                    best = (err, (round(float(ambient), 2), round(float(diffuse), 2), gamma))
    ambient, diffuse, gamma = best[1]
    print("best ambient %.2f diffuse %.2f gamma %.2f (summed |mean,p10,p90| error %.1f over three icons)"
          % (ambient, diffuse, gamma, best[0]))
    per_gun = {}
    for g in REFERENCE:
        im = shade(*rasters[g], args.size, ambient, diffuse, gamma)
        s = stats(im)
        per_gun[g] = {"render": [round(float(x), 1) for x in s], "csmc": [round(float(x), 1) for x in refs[g]]}
        print("   %-6s render mean %.1f p10 %.1f p90 %.1f   csmc %.1f %.1f %.1f" % (g, *s, *refs[g]))
    result = {"ambient": ambient, "diffuse": diffuse, "gamma": gamma, "error": round(best[0], 1),
              "perGun": per_gun,
              "method": "grid search on luminance mean/p10/p90 against the CS:MC icons of ak47/m4a1s/awp"}
    if args.json:
        args.json.write_text(json.dumps(result, indent=2), "utf-8")


if __name__ == "__main__":
    main()
