#!/usr/bin/env python3
"""Install the three guns' CURRENT CS2 materials (the body_hd set).

Where install_gun_textures_cs2.py packs the legacy v_models textures that CS:MC
was built on, this packs what CS2 draws today:
``04_current_weapon_materials/weapons/models/<dir>/materials/``, bound exactly as
each gun's ``weapon_*.vmat`` (shader csgo_weapon.vfx) binds them:

    TextureColor1          -> <gun>_hd.png            (RGB)
    TextureAmbientOcclusion-> <gun>_hd_orm.png  R
    TextureRoughness1      -> <gun>_hd_orm.png  G
    TextureMetalness1      -> <gun>_hd_orm.png  B
    TextureNormal          -> <gun>_hd_normal.png

Two things this set fixes over the legacy one, both by having a real source
rather than a derived value:

  * the AWP needs no tint. The legacy colour was the unpainted grey substrate and
    the mod multiplied it by a fitted olive (0.586, 0.642, 0.387); the current
    set's *_default_color is the finished default finish, so the multiplier is
    gone and with it an estimated number.
  * the M4A1-S gets real metalness. Its legacy VMAT bound a constant 0.

Not implemented: g_flMetalnessTransitionBias (AK 2.0, M4A1-S 2.407, AWP 2.0) and
g_vMetalnessRemapRange [0,1]. csgo_weapon.vfx has not been disassembled, so the
formula that consumes them is unknown; the values are recorded in the sidecar
JSON and the shader ignores them rather than guessing a curve.

Usage:  python3 tools/install_gun_textures_cs2hd.py ak47 m4a1s awp [--size=1024]
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

Image.MAX_IMAGE_PIXELS = None

ROOT = Path(__file__).resolve().parent.parent
EXPORT = (Path.home() / "workspaces/CSMCReverse/local_cs2_analysis/all_weapons"
          / "04_current_weapon_materials/weapons/models")
OUT = ROOT / "src/ScCsgoKnives/Assets/Textures/ScCsgoKnives"

GUNS = {"ak47": ("ak47", "weapon_rif_ak47.vmat"),
        "m4a1s": ("m4a1_silencer", "weapon_rif_m4a1_silencer.vmat"),
        "awp": ("awp", "weapon_snip_awp.vmat")}

BINDINGS = ["TextureColor1", "TextureAmbientOcclusion", "TextureRoughness1",
            "TextureMetalness1", "TextureNormal"]


def read_vmat(path: Path) -> dict:
    text = path.read_text("utf-8", "replace")
    out = {}
    for key, value in re.findall(r'"([A-Za-z_0-9]+)"\s+"([^"]*)"', text):
        out.setdefault(key, value)
    return out


def load(path: Path, size: int, mode="RGB"):
    """Load, de-speckle, then resize - and never as RGBA.

    These PNGs carry a mostly-zero alpha (a paint compositor helper); Pillow
    resamples RGBA premultiplied, which turns colour into noise wherever alpha
    is 0. The median pass removes the vtex decode outliers (0.03-0.12 % of
    pixels off by more than 50) before they get averaged into the downscale.
    """
    im = Image.open(path).convert(mode)
    im = im.filter(ImageFilter.MedianFilter(3))
    return im if im.size == (size, size) else im.resize((size, size), Image.LANCZOS)


def resolve(vmat_dir: Path, value: str) -> Path:
    """A VMAT texture path is game-root relative; the export keeps the tail."""
    candidate = EXPORT.parent.parent / value
    if candidate.exists():
        return candidate
    return vmat_dir / Path(value).name


def install(gun: str, size: int, write=True) -> dict:
    directory, vmat_name = GUNS[gun]
    vmat_dir = EXPORT / directory / "materials"
    vmat = read_vmat(vmat_dir / vmat_name)
    missing = [b for b in BINDINGS if b not in vmat]
    if missing:
        raise SystemExit("%s: %s binds no %s" % (gun, vmat_name, ", ".join(missing)))

    sources = {b: resolve(vmat_dir, vmat[b]) for b in BINDINGS}
    for b, p in sources.items():
        if not p.exists():
            raise SystemExit("%s: %s -> %s not in the export" % (gun, b, p))

    colour = load(sources["TextureColor1"], size, "RGB")
    ao = np.asarray(load(sources["TextureAmbientOcclusion"], size, "L"), np.uint8)
    rough = np.asarray(load(sources["TextureRoughness1"], size, "L"), np.uint8)
    metal = np.asarray(load(sources["TextureMetalness1"], size, "L"), np.uint8)
    normal = load(sources["TextureNormal"], size, "RGB")

    if write:
        OUT.mkdir(parents=True, exist_ok=True)
        colour.save(OUT / ("%s_hd.png" % gun), optimize=True)
        Image.fromarray(np.stack([ao, rough, metal], -1), "RGB").save(
            OUT / ("%s_hd_orm.png" % gun), optimize=True)
        normal.save(OUT / ("%s_hd_normal.png" % gun), optimize=True)

    flat = np.abs(np.asarray(normal, int) - np.array([128, 128, 255])).mean()
    return {
        "gun": gun, "size": size, "vmat": str((vmat_dir / vmat_name).relative_to(EXPORT.parent.parent)),
        "sources": {b: str(p.relative_to(EXPORT.parent.parent)) for b, p in sources.items()},
        "means": {"colour": [round(float(x), 2) for x in np.asarray(colour, float).reshape(-1, 3).mean(0)],
                  "ao": round(float(ao.mean()), 2), "roughness": round(float(rough.mean()), 2),
                  "metalness": round(float(metal.mean()), 2)},
        "normal_deviation_from_flat": round(float(flat), 3),
        "recorded_but_not_implemented": {
            "g_flMetalnessTransitionBias": vmat.get("g_flMetalnessTransitionBias"),
            "g_vMetalnessRemapRange": vmat.get("g_vMetalnessRemapRange"),
            "g_vColorTint": vmat.get("g_vColorTint"),
            "g_flModelTintAmount": vmat.get("g_flModelTintAmount"),
        },
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("guns", nargs="*", default=sorted(GUNS))
    ap.add_argument("--size", type=int, default=1024)
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--json", type=Path, default=ROOT / "docs/cs2-stage2-materials.json")
    args = ap.parse_args()

    rows = []
    for gun in args.guns or sorted(GUNS):
        row = install(gun, args.size, write=not args.dry_run)
        rows.append(row)
        m = row["means"]
        print("%-6s @%d  colour %s  ao %.1f rough %.1f metal %.1f  normal deviation %.2f  bias %s"
              % (gun, args.size, m["colour"], m["ao"], m["roughness"], m["metalness"],
                 row["normal_deviation_from_flat"],
                 row["recorded_but_not_implemented"]["g_flMetalnessTransitionBias"]))
    if args.json and not args.dry_run:
        args.json.write_text(json.dumps(rows, indent=1), "utf-8")
        print("wrote %s" % args.json.name)


if __name__ == "__main__":
    main()
