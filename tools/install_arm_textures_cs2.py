#!/usr/bin/env python3
"""Install CS2's bare-arm and glove textures for the cs2 profile's skinned arms.

    cs2_arm.png / cs2_arm_orm.png / cs2_arm_normal.png
    cs2_glove.png / cs2_glove_orm.png / cs2_glove_normal.png

Colour, ambient occlusion and normal come straight from the export's
`08_first_person/glb/weapons/models/shared/arms/`.

Roughness has no source. No VMAT for these materials was exported - the folder
holds the vmdl and the textures only - so the ORM's green channel is a constant,
marked ESTIMATED here and in the stage 4 report. It is baked into the texture
rather than exposed as a live tunable, because the shader has no per-material
roughness input; --arm-roughness / --glove-roughness change it and the mod has to
be repacked. Metalness is 0, which is not an estimate: skin and cloth are dielectric.

Usage:  python3 tools/install_arm_textures_cs2.py [--size 1024]
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

Image.MAX_IMAGE_PIXELS = None

ROOT = Path(__file__).resolve().parent.parent
SRC = (Path.home() / "workspaces/CSMCReverse/local_cs2_analysis/all_weapons"
       / "08_first_person/glb/weapons/models/shared/arms")
OUT = ROOT / "src/ScCsgoKnives/Assets/Textures/ScCsgoKnives"

# Estimated: no roughness map and no VMAT ship for these materials.
ARM_ROUGHNESS = 0.55
GLOVE_ROUGHNESS = 0.75

SETS = {
    "cs2_arm": {"color": "bare_arm_133_color_psd_5b76e0cf.png",
                "ao": "bare_arm_133_vmat_g_tambientocclusion_9782fd5c.png",
                "normal": "bare_arm_normal_tga_c9fefc2d.png",
                "roughness": ARM_ROUGHNESS},
    "cs2_glove": {"color": "glove_fingerless_color_psd_19aa3701.png",
                  "ao": "glove_fingerless_ao_psd_ce290e8d.png",
                  "normal": "glove_fingerless_normal_tga_5acd72d3.png",
                  "roughness": GLOVE_ROUGHNESS},
}


def load(name: str, size: int, mode="RGB"):
    im = Image.open(SRC / name).convert(mode)
    im = im.filter(ImageFilter.MedianFilter(3))
    return im if im.size == (size, size) else im.resize((size, size), Image.LANCZOS)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--size", type=int, default=1024)
    ap.add_argument("--arm-roughness", type=float, default=ARM_ROUGHNESS)
    ap.add_argument("--glove-roughness", type=float, default=GLOVE_ROUGHNESS)
    ap.add_argument("--json", type=Path, default=ROOT / "docs/cs2-stage4-materials.json")
    args = ap.parse_args()

    OUT.mkdir(parents=True, exist_ok=True)
    SETS["cs2_arm"]["roughness"] = args.arm_roughness
    SETS["cs2_glove"]["roughness"] = args.glove_roughness
    rows = []
    for name, spec in SETS.items():
        colour = load(spec["color"], args.size)
        ao = np.asarray(load(spec["ao"], args.size, "L"), np.uint8)
        normal = load(spec["normal"], args.size)
        rough = np.full_like(ao, int(round(spec["roughness"] * 255)))
        metal = np.zeros_like(ao)
        colour.save(OUT / f"{name}.png", optimize=True)
        Image.fromarray(np.stack([ao, rough, metal], -1), "RGB").save(OUT / f"{name}_orm.png", optimize=True)
        normal.save(OUT / f"{name}_normal.png", optimize=True)
        flat = float(np.abs(np.asarray(normal, int) - np.array([128, 128, 255])).mean())
        rows.append({"name": name, "size": args.size,
                     "sources": {k: v for k, v in spec.items() if k != "roughness"},
                     "roughness_estimated": spec["roughness"], "metalness": 0,
                     "ao_mean": round(float(ao.mean()), 2),
                     "normal_deviation_from_flat": round(flat, 3)})
        print("%-10s @%d  ao %.1f  roughness %.2f (ESTIMATED)  metal 0  normal deviation %.2f"
              % (name, args.size, ao.mean(), spec["roughness"], flat))
    args.json.write_text(json.dumps(rows, indent=1), "utf-8")


if __name__ == "__main__":
    main()
