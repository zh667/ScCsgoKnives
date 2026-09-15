#!/usr/bin/env python3
"""Rebuild inventory icons so they stay sharp and lose the CS2 white rim.

The previous pass only recoloured the 1px alpha boundary and then Lanczos-upscaled
the 128 icons to 256. That left the baked studio outline intact and made it thicker,
which is why the creative tab still looked haloed and soft.

This rebuilds from CS2's 512x384 econ renders when they are on disk, replaces only
the bright studio stroke (thin dark parts such as sights stay), bleeds RGB into the
transparent margin so Survivalcraft's linear filter cannot pick up a white halo,
and writes a uniform 256x192 4:3 sheet. Skin icons without a 512 source are cleaned
in place and fitted onto the same 4:3 sheet.

    python tools/optimize_slot_icons.py [--check] [--preview DIR] [--size 256]
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
TEXTURES = ROOT / "src/ScCsgoKnives/Assets/Textures/ScCsgoKnives"
CS2_BASE = ROOT.parent / "CSMCReverse/local_cs2_analysis/all_weapons/11_icons/panorama/images/econ/weapons/base_weapons"
EXPORT = ROOT.parent / "CSMCReverse/local_cs2_analysis/all_weapons"

# mod asset -> CS2 econ image stem (without _png), same table as install_gun_slot_icons_cs2.py
BASE_NAMES = {
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
    "grenade_hegrenade": "weapon_hegrenade", "grenade_flashbang": "weapon_flashbang",
    "grenade_smokegrenade": "weapon_smokegrenade", "grenade_molotov": "weapon_molotov",
    "grenade_incendiary": "weapon_incgrenade", "grenade_decoy": "weapon_decoy",
    "c4": "weapon_c4",
}

INVENTORY_GRAY = (106, 99, 88, 255)


def dilate(mask: np.ndarray) -> np.ndarray:
    out = mask.copy()
    out[1:] |= mask[:-1]
    out[:-1] |= mask[1:]
    out[:, 1:] |= mask[:, :-1]
    out[:, :-1] |= mask[:, 1:]
    return out


def distance_inside(solid: np.ndarray, limit: int) -> np.ndarray:
    """4-connected distance from outside. 0 = outside / unreached."""
    dist = np.zeros(solid.shape, np.int16)
    frontier = ~solid
    for d in range(1, limit + 1):
        nxt = dilate(frontier)
        ring = nxt & ~frontier & solid
        if not ring.any():
            break
        dist[ring] = d
        frontier = nxt
    dist[solid & (dist == 0)] = limit
    return dist


def spread_color(rgb: np.ndarray, known: np.ndarray, steps: int) -> tuple[np.ndarray, np.ndarray]:
    """Copy known RGB onto neighbouring unknown pixels, inside-out."""
    color = rgb.astype(np.float32).copy()
    mask = known.copy()
    for _ in range(steps):
        acc = np.zeros_like(color)
        wt = np.zeros(mask.shape, np.float32)

        def add(sy, sx, dy, dx):
            m = mask[sy, sx].astype(np.float32)
            acc[dy, dx] += color[sy, sx] * m[..., None]
            wt[dy, dx] += m

        add(slice(None, -1), slice(None), slice(1, None), slice(None))
        add(slice(1, None), slice(None), slice(None, -1), slice(None))
        add(slice(None), slice(None, -1), slice(None), slice(1, None))
        add(slice(None), slice(1, None), slice(None), slice(None, -1))
        fill = (~mask) & (wt > 0)
        if not fill.any():
            break
        color[fill] = acc[fill] / wt[fill, None]
        mask |= fill
    return color, mask


def harden_alpha(rgba: np.ndarray) -> np.ndarray:
    """Bleed interior RGB into the transparent margin and drop dust. No silhouette erosion."""
    alpha = rgba[..., 3].astype(np.float32)
    rgb = rgba[..., :3].astype(np.float32)
    opaque = alpha >= 32
    if not opaque.any():
        out = rgba.copy()
        out[..., 3] = 0
        return out
    bled, _ = spread_color(rgb, opaque, 3)
    rgb = np.where(opaque[..., None], rgb, bled)
    alpha_out = np.where(alpha < 32, 0, alpha)
    out = np.empty_like(rgba)
    out[..., :3] = np.clip(rgb, 0, 255)
    out[..., 3] = np.clip(alpha_out, 0, 255)
    return out


def strip_bright_rim(rgba: np.ndarray) -> np.ndarray:
    """Replace only the light studio stroke. Thin dark parts (sights, rags) stay."""
    alpha = rgba[..., 3].astype(np.float32)
    rgb = rgba[..., :3].astype(np.float32)
    solid = alpha >= 32
    if not solid.any():
        return rgba
    rim = max(2, round(min(rgba.shape[:2]) / 128))  # 4px at 512, 2px at 256
    interior = rim + 3
    dist = distance_inside(solid, interior + 6)
    known = (dist >= interior) & (alpha >= 200)
    if not known.any():
        return rgba
    filled, reached = spread_color(rgb, known, interior + 4)
    luma = rgb @ np.array([0.2126, 0.7152, 0.0722], np.float32)
    filled_luma = filled @ np.array([0.2126, 0.7152, 0.0722], np.float32)
    bright_rim = reached & (dist > 0) & (dist <= rim) & (alpha > 0) & (luma > filled_luma + 22)
    rgb = np.where(bright_rim[..., None], filled, rgb)
    out = rgba.copy()
    out[..., :3] = np.clip(rgb, 0, 255)
    return out


def as_43(im: Image.Image) -> Image.Image:
    """Keep CS2's 4:3 canvas (rifles stay larger than pistols). Square sheets are a centred 4:3 strip."""
    w, h = im.size
    if w * 3 == h * 4:
        return im
    if w == h:
        top = (h - h * 3 // 4) // 2
        return im.crop((0, top, w, top + h * 3 // 4))
    side = max(w, h)
    canvas = Image.new("RGBA", (side, side * 3 // 4), (0, 0, 0, 0))
    canvas.paste(im, ((canvas.width - w) // 2, (canvas.height - h) // 2), im)
    return canvas


def fit_sheet(im: Image.Image, width: int) -> Image.Image:
    sheet = as_43(im)
    height = width * 3 // 4
    if sheet.size != (width, height):
        sheet = sheet.resize((width, height), Image.LANCZOS)
    return sheet


def load_hires(stem: str) -> Image.Image | None:
    """Prefer CS2's 512x384 file over the already-downscaled slot PNG."""
    direct = CS2_BASE / f"{stem}_png.png"
    if direct.is_file():
        return Image.open(direct).convert("RGBA")
    matches = list(EXPORT.glob(f"**/default_generated/{stem}_light_png.png"))
    if matches:
        return Image.open(max(matches, key=lambda p: p.stat().st_size)).convert("RGBA")
    return None


def skin_hires(asset: str, key: str) -> Image.Image | None:
    cs2 = BASE_NAMES.get(asset, f"weapon_{asset}")
    matches = list(EXPORT.glob(f"**/default_generated/{cs2}_{key}_light_png.png"))
    if matches:
        return Image.open(max(matches, key=lambda p: p.stat().st_size)).convert("RGBA")
    return None


def process_image(src: Image.Image, width: int) -> Image.Image:
    arr = np.asarray(src.convert("RGBA")).astype(np.float32)
    native = Image.fromarray(harden_alpha(strip_bright_rim(arr)).astype(np.uint8), "RGBA")
    sheet = fit_sheet(native, width)
    # After Lanczos, only re-bleed. A second rim-strip would eat iron sights.
    return Image.fromarray(harden_alpha(np.asarray(sheet).astype(np.float32)).astype(np.uint8), "RGBA")


def preview_on_slot(im: Image.Image, scale: int = 2) -> Image.Image:
    big = im.resize((im.width * scale, im.height * scale), Image.NEAREST)
    bg = Image.new("RGBA", big.size, INVENTORY_GRAY)
    return Image.alpha_composite(bg, big)


def targets(only: set[str] | None = None) -> list[tuple[Path, Image.Image, str]]:
    rows = []
    for path in sorted(TEXTURES.glob("*_slot*.png")):
        name = path.name
        if only and name not in only:
            continue
        src = None
        origin = "slot"
        if "_slot__" in name:
            asset, key = name[:-4].split("_slot__", 1)
            src = skin_hires(asset, key)
            origin = "cs2-skin" if src else "slot"
        else:
            asset = name[:-4].removesuffix("_slot")
            stem = BASE_NAMES.get(asset)
            if stem:
                src = load_hires(stem)
                origin = "cs2-base" if src else "slot"
        if src is None:
            src = Image.open(path).convert("RGBA")
        rows.append((path, src, origin))
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--preview", type=Path, help="write before/after sheets here, do not save textures")
    ap.add_argument("--only", nargs="*", help="basename filters, e.g. ak47_slot.png")
    ap.add_argument("--size", type=int, default=256, help="output width; height is 3/4 of this")
    args = ap.parse_args()
    rows = targets(set(args.only) if args.only else None)
    changed = 0
    sources = {"cs2-base": 0, "cs2-skin": 0, "slot": 0}
    preview_dir = args.preview
    if preview_dir:
        preview_dir.mkdir(parents=True, exist_ok=True)
    for path, src, origin in rows:
        sources[origin] += 1
        out = process_image(src, args.size)
        before = Image.open(path).convert("RGBA")
        same = before.size == out.size and np.array_equal(np.asarray(before), np.asarray(out))
        note = origin if not same else "ok"
        if same:
            continue
        changed += 1
        print(f"{path.name:<52} {before.size[0]}x{before.size[1]} -> {out.size[0]}x{out.size[1]}  {note}")
        if preview_dir:
            preview_on_slot(fit_sheet(src, args.size)).save(preview_dir / f"before_{path.name}")
            preview_on_slot(out).save(preview_dir / f"after_{path.name}")
            out.save(preview_dir / f"raw_{path.name}")
        if not args.check and not preview_dir:
            out.save(path, optimize=True)
    print(f"{changed}/{len(rows)} icons {'would change' if args.check or preview_dir else 'updated'}; "
          f"sources base={sources['cs2-base']} skin={sources['cs2-skin']} fallback={sources['slot']}")


if __name__ == "__main__":
    main()
