#!/usr/bin/env python3
"""Install CS2's own hotbar icons: panorama/images/econ/weapons/base_weapons -> <name>_slot.png.

The mod's slot icons are 128x128 RGBA. CS:MC's are CS2's 512 x 384 econ renders
scaled by a quarter and centred (see SCALE below for the measurement), so that is
what this does to every one of them, and --check reports how close the result is
to what ships now.

Only weapons the mod ships are installed, by the table below; a CS2 icon with no
mod counterpart (grenades, C4, gloves) is left alone.

Usage:  python3 tools/install_gun_slot_icons_cs2.py [--only ak47 m9 ...] [--check] [--dry-run]
        (on Windows: python tools\\install_gun_slot_icons_cs2.py ...)
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
TEXTURES = ROOT / "src/ScCsgoKnives/Assets/Textures/ScCsgoKnives"
ICONS = (Path.home() / "workspaces/CSMCReverse/local_cs2_analysis/all_weapons/11_icons"
         / "panorama/images/econ/weapons/base_weapons")

SIZE = 128
# CS:MC's icons are CS2's 512 x 384 econ renders scaled by exactly a quarter into a
# 128 x 96 strip and centred in the 128 cell - no per-weapon crop, so a pistol stays
# smaller than a rifle, as in CS2's own buy menu. Measured: CS2's ak47 render has a
# 491 x 245 silhouette, CS:MC's ak47_slot.png 123 x 62; the M4A1-S 472 x 184 against
# 119 x 47; the AWP 479 x 184 against 121 x 47. --check reports the pixel agreement.
SCALE = 0.25

# mod asset -> CS2 econ image stem (without _png)
NAMES = {
    # guns
    "ak47": "weapon_ak47", "m4a1s": "weapon_m4a1_silencer", "awp": "weapon_awp",
    "deagle": "weapon_deagle", "glock18": "weapon_glock", "usp_silencer": "weapon_usp_silencer",
    "m4a4": "weapon_m4a1", "famas": "weapon_famas", "mp9": "weapon_mp9", "p90": "weapon_p90",
    "ssg08": "weapon_ssg08",
    "cz75a": "weapon_cz75a", "elite": "weapon_elite", "fiveseven": "weapon_fiveseven",
    "hkp2000": "weapon_hkp2000", "p250": "weapon_p250", "revolver": "weapon_revolver",
    "taser": "weapon_taser", "tec9": "weapon_tec9", "aug": "weapon_aug", "bizon": "weapon_bizon",
    "g3sg1": "weapon_g3sg1", "galilar": "weapon_galilar", "m249": "weapon_m249",
    "mac10": "weapon_mac10", "mag7": "weapon_mag7", "mp5sd": "weapon_mp5sd", "mp7": "weapon_mp7",
    "negev": "weapon_negev", "nova": "weapon_nova", "sawedoff": "weapon_sawedoff",
    "scar20": "weapon_scar20", "sg556": "weapon_sg556", "ump45": "weapon_ump45",
    "xm1014": "weapon_xm1014",
    # knives
    "bayonet": "weapon_bayonet", "bowie": "weapon_knife_survival_bowie",
    "butterfly": "weapon_knife_butterfly", "canis": "weapon_knife_canis",
    "cord": "weapon_knife_cord", "css": "weapon_knife_css", "default_ct": "weapon_knife",
    "default_t": "weapon_knife_t", "falchion": "weapon_knife_falchion",
    "flip": "weapon_knife_flip", "gut": "weapon_knife_gut", "karambit": "weapon_knife_karambit",
    "kukri": "weapon_knife_kukri", "m9": "weapon_knife_m9_bayonet",
    "navaja": "weapon_knife_gypsy_jackknife", "outdoor": "weapon_knife_outdoor",
    "push": "weapon_knife_push", "skeleton": "weapon_knife_skeleton",
    "stiletto": "weapon_knife_stiletto", "tactical": "weapon_knife_tactical",
    "talon": "weapon_knife_widowmaker", "ursus": "weapon_knife_ursus",
}


def fit(source: Image.Image, size: int = SIZE, scale: float = SCALE) -> Image.Image:
    im = source.convert("RGBA")
    w = max(1, round(im.width * scale))
    h = max(1, round(im.height * scale))
    small = im.resize((w, h), Image.LANCZOS)
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.paste(small, ((size - w) // 2, (size - h) // 2), small)
    return out


def difference(a: Image.Image, b: Image.Image) -> dict:
    """Mean absolute RGBA difference and the silhouette overlap, both at 128."""
    x = np.asarray(a.convert("RGBA")).astype(np.float32)
    y = np.asarray(b.convert("RGBA")).astype(np.float32)
    ma, mb = x[..., 3] > 8, y[..., 3] > 8
    both = ma | mb
    rgb = np.abs(x[..., :3] - y[..., :3])[both].mean() if both.any() else 0.0
    iou = float((ma & mb).sum()) / float(both.sum()) if both.any() else 1.0
    return {"rgb": round(float(rgb), 2), "alpha": round(float(np.abs(x[..., 3] - y[..., 3]).mean()), 2),
            "iou": round(iou, 4)}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", nargs="*")
    ap.add_argument("--check", action="store_true",
                    help="compare each result with the icon currently shipped and do not write")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()
    names = args.only or list(NAMES)
    missing = [n for n in names if n not in NAMES]
    if missing:
        raise SystemExit("no CS2 icon known for: %s" % ", ".join(missing))
    for name in names:
        src = ICONS / ("%s_png.png" % NAMES[name])
        if not src.exists():
            raise SystemExit("%s: %s is not in the export" % (name, src.name))
        icon = fit(Image.open(src))
        target = TEXTURES / ("%s_slot.png" % name)
        line = "%-13s %-34s -> %s" % (name, src.name, target.name)
        if target.exists():
            d = difference(icon, Image.open(target))
            line += "   vs shipped: rgb %5.2f alpha %5.2f silhouette IoU %.4f" % (d["rgb"], d["alpha"], d["iou"])
        if not args.check and not args.dry_run:
            icon.save(target, optimize=True)
            line += "   written %.1f KB" % (target.stat().st_size / 1024)
        print(line)


if __name__ == "__main__":
    main()
