#!/usr/bin/env python3
"""Install the 22 knives' CS2 materials, the same way the guns' are installed.

The knives' meshes now come from CS2 (tools/cs2_knife_rig.py), so their UVs are
CS2's and the CS:MC textures no longer apply. This packs what CS2 binds today,
from the knife export's own `weapon_knife_<x>.vmat` (shader csgo_weapon.vfx):

    TextureColor1           -> <knife>_cs2.png            (RGB)
    TextureAmbientOcclusion -> <knife>_cs2_orm.png  R
    TextureRoughness1       -> <knife>_cs2_orm.png  G
    TextureMetalness1       -> <knife>_cs2_orm.png  B
    TextureNormal           -> <knife>_cs2_normal.png

It reuses install_gun_textures_cs2hd's readers rather than copying them, so the
de-speckle and the never-load-as-RGBA rule stay in one place - those PNGs carry a
mostly-zero alpha that Pillow premultiplies into noise on resize.

The `_cs2` suffix keeps these beside the CS:MC textures instead of replacing them:
KnifeTuning.KnifeProfile switches between the two routes at runtime, so both sets
have to ship.

Usage:  python3 tools/install_knife_textures_cs2.py [knife ...] [--size 1024]
        (on Windows: python tools\\install_knife_textures_cs2.py)
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))

import install_gun_textures_cs2hd as guns

ROOT = Path(__file__).resolve().parent.parent
KNIVES = (Path.home() / "workspaces/CSMCReverse/local_cs2_analysis/all_weapons"
          / "09_knives/decompiled/weapons/models/knife")
OUT = ROOT / "src/ScCsgoKnives/Assets/Textures/ScCsgoKnives"
DATA = ROOT / "src/ScCsgoKnives/AnimationData"

BINDINGS = guns.BINDINGS


def knife_names() -> list:
    return [k["Name"] for k in json.loads((DATA / "knives.json").read_text("utf-8"))]


def folder(knife: str) -> str:
    return "knife_%s" % ("default_ct" if knife == "default_ct" else knife)


# The GLB export saves a texture as "<stem>_<ext>_<hash>.png" beside the model,
# while the decompiled tree saves it as "<stem>.png" next to the VMAT. A binding
# may name either, and one of them - materials/default/default_normal.tga - is
# not under the knife's directory at all.
GLB = (Path.home() / "workspaces/CSMCReverse/local_cs2_analysis/all_weapons"
       / "09_knives/glb/weapons/models/knife")


def resolve(knife: str, vmat_dir: Path, value: str) -> Path:
    """Find the texture a VMAT binding names, in either export tree."""
    tail = Path(value).name
    stem, ext = tail.rsplit(".", 1) if "." in tail else (tail, "")
    roots = [vmat_dir, vmat_dir / "composite_inputs", GLB / folder(knife)]
    for base in roots:
        direct = base / tail
        if direct.exists():
            return direct
        as_png = base / (stem + ".png")
        if as_png.exists():
            return as_png
        # "<stem>_<ext>_<hash>.png", the GLB exporter's name
        hashed = sorted(base.glob("%s_%s_*.png" % (stem, ext))) if ext else []
        if hashed:
            return hashed[0]
    return vmat_dir / tail


def install(knife: str, size: int, write=True) -> dict:
    d = KNIVES / folder(knife) / "materials"
    vmat_path = d / ("weapon_%s.vmat" % folder(knife))
    if not vmat_path.exists():
        raise SystemExit("%s: no %s" % (knife, vmat_path))
    vmat = guns.read_vmat(vmat_path)
    missing = [b for b in BINDINGS if b not in vmat]
    if missing:
        raise SystemExit("%s: %s binds no %s" % (knife, vmat_path.name, ", ".join(missing)))

    sources = {b: resolve(knife, d, vmat[b]) for b in BINDINGS}
    absent = {b: p for b, p in sources.items() if not p.exists()}
    if absent:
        raise SystemExit("%s: not in the export: %s"
                         % (knife, ", ".join("%s -> %s" % (b, p) for b, p in absent.items())))

    colour = guns.load(sources["TextureColor1"], size, "RGB")
    ao = np.asarray(guns.load(sources["TextureAmbientOcclusion"], size, "L"), np.uint8)
    rough = np.asarray(guns.load(sources["TextureRoughness1"], size, "L"), np.uint8)
    metal = np.asarray(guns.load(sources["TextureMetalness1"], size, "L"), np.uint8)
    normal = guns.load(sources["TextureNormal"], size, "RGB")

    if write:
        OUT.mkdir(parents=True, exist_ok=True)
        colour.save(OUT / ("%s_cs2.png" % knife), optimize=True)
        Image.fromarray(np.stack([ao, rough, metal], -1), "RGB").save(
            OUT / ("%s_cs2_orm.png" % knife), optimize=True)
        normal.save(OUT / ("%s_cs2_normal.png" % knife), optimize=True)

    flat = np.abs(np.asarray(normal, int) - np.array([128, 128, 255])).mean()
    return {
        "knife": knife, "size": size,
        # CS2 binds materials/default/default_normal.tga - a single [128,128,255]
        # texel - for most knives: the detail is in the mesh, which is why they are
        # 2000-18000 triangles. A flat normal here is the source, not a fallback.
        "flatNormalFromDefault": "materials/default/" in vmat["TextureNormal"],
        "vmat": vmat_path.name,
        "sources": {b: p.name for b, p in sources.items()},
        "means": {"colour": [round(float(x), 2) for x in np.asarray(colour, float).reshape(-1, 3).mean(0)],
                  "ao": round(float(ao.mean()), 2), "roughness": round(float(rough.mean()), 2),
                  "metalness": round(float(metal.mean()), 2)},
        "normal_deviation_from_flat": round(float(flat), 3),
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("knives", nargs="*")
    ap.add_argument("--size", type=int, default=1024)
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--json", type=Path, default=ROOT / "docs/cs2-knife-materials.json")
    args = ap.parse_args()

    names = args.knives or knife_names()
    report = []
    for knife in names:
        info = install(knife, args.size, write=not args.dry_run)
        report.append(info)
        print("%-11s colour %s  ao %5.1f  rough %5.1f  metal %5.1f  normal dev %6.2f"
              % (knife, info["means"]["colour"], info["means"]["ao"],
                 info["means"]["roughness"], info["means"]["metalness"],
                 info["normal_deviation_from_flat"]))
    if not args.dry_run:
        args.json.write_text(json.dumps(report, indent=1), "utf-8")
        print("\nwrote %s" % args.json.name)


if __name__ == "__main__":
    main()
