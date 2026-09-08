#!/usr/bin/env python3
"""Bake the audited CS2 weapon finishes into the mod's own gun texture space.

Inputs
------
* ``tools/gun_skins_catalog.json`` - the 11 approved skins, their gun, paint ID and bake mode.
* The audited export tree ``CSMCReverse/local_cs2_analysis/all_weapons`` for the paint recipes
  (``.vmat``), the pattern textures, the official inventory icons and each gun's current
  ``composite_inputs`` (the paint mask that says which surfaces a finish covers).
* The mod's shipped ``<gun>_hd`` / ``_hd_orm`` / ``_hd_normal`` set, which is the current CS2
  ``body_hd`` material at 1024 and is exactly what the runtime meshes are UV-mapped to.

Why the base is the mod's own texture and not a fresh composite: every skinned texture then
stays pixel-aligned with the mesh the mod already draws, so a skin can only change colour,
roughness and metalness - never placement.

Bake modes (measured, see docs/gun-skins-p0-0380.md)
---------------------------------------------------
``pattern``  The recipe is an anodized-multi style whose pattern is a *shared* texture that CS2
             applies to many different weapons, so it carries no weapon-specific UV layout. The
             four recipe colours are selected by the pattern's R/G/B channels exactly as the
             recipe declares, sampled through the recipe's own scale/offset/rotation.
``fade``     The recipe targets the current model and ships its own paint-by-number mask in that
             UV space, so the gradient is looked up through the real mask.
``palette``  The recipe's pattern is a weapon-specific texture authored in the *legacy* model's UV
             layout, which the current mesh does not share (measured: edge correlation +0.16..+0.21
             against the legacy AO, -0.05..-0.01 against the current AO). Pasting it would land the
             artwork on the wrong parts, so instead the skin's colour distribution is transferred by
             luminance percentile onto the gun's own shading. Colours are the skin's; placement is
             the gun's. This is an approximation and is labelled as one everywhere it is reported.

Outputs (all under ``src/ScCsgoKnives/Assets/Textures/ScCsgoKnives``)
    <gun>_hd__<skin>.png          colour
    <gun>_hd__<skin>_orm.png      occlusion / roughness / metalness
    <gun>_hd__<skin>_normal.png   normal (copied unless the recipe ships a usable one)
    <gun>_slot__<skin>.png        inventory icon, from CS2's official light-wear icon

Usage:  python3 tools/build_gun_skins.py [--only KEY ...] [--size 1024] [--manifest PATH]
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None

ROOT = Path(__file__).resolve().parent.parent
EXPORT = Path.home() / "workspaces/CSMCReverse/local_cs2_analysis/all_weapons"
PAINTS = EXPORT / "10_paints"
CURRENT = EXPORT / "04_current_weapon_materials/weapons/models"
TEX = ROOT / "src/ScCsgoKnives/Assets/Textures/ScCsgoKnives"
CATALOG = ROOT / "tools/gun_skins_catalog.json"


# ---------------------------------------------------------------- resource lookup


def find_one(basename: str, roots=(PAINTS,), suffix: str | None = None) -> Path | None:
    """The decoded file for a CS2 resource path, located by base name under the export tree.

    ``suffix`` pins the extension: a recipe and the material it points at share a stem, so a
    ``.vmat`` lookup must not come back with the ``.vcompmat`` that started it.
    """
    stem = Path(basename).stem
    want = suffix or (Path(basename).suffix if Path(basename).suffix in (".vmat", ".vcompmat") else None)
    for root in roots:
        hits = sorted(root.rglob(f"{stem}*{want}")) if want else (sorted(root.rglob(f"{stem}*.png")) or sorted(root.rglob(f"{stem}.*")))
        hits = [h for h in hits if "raw_compiled" not in h.parts]
        if hits:
            # Prefer an exact stem match, then the shortest path (the canonical copy).
            exact = [h for h in hits if h.stem == stem]
            return sorted(exact or hits, key=lambda p: (len(p.parts), len(p.name)))[0]
    return None


def parse_vmat(path: Path) -> dict:
    """The flat "key" "value" pairs of a decompiled Source 2 material, ignoring nested blocks."""
    out, depth = {}, 0
    for raw in path.read_text(encoding="utf-8", errors="replace").splitlines():
        line = raw.strip()
        depth += line.count("{") - line.count("}")
        if depth > 1:
            continue
        m = re.match(r'"([^"]+)"\s+"([^"]*)"$', line)
        if m:
            out[m.group(1)] = m.group(2)
    return out


def vec(value: str, fallback=(1.0, 1.0, 1.0)) -> np.ndarray:
    nums = [float(x) for x in re.findall(r"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?", value or "")]
    return np.array(nums[:3] if len(nums) >= 3 else fallback, float)


def num(params: dict, key: str, fallback: float) -> float:
    try:
        return float(params[key])
    except (KeyError, ValueError):
        return fallback


def recipe_material(recipe: Path) -> Path:
    """The paint .vmat a .vcompmat points at."""
    text = recipe.read_text(encoding="utf-8", errors="replace")
    m = re.search(r'm_strSpecificContainerMaterial\s*=\s*(?:resource_name:)?"([^"]+\.vmat)"', text)
    if not m:
        raise SystemExit(f"{recipe.name}: no specific container material")
    found = find_one(Path(m.group(1)).name, suffix=".vmat")
    if found is None:
        raise SystemExit(f"{recipe.name}: {m.group(1)} not in the export tree")
    return found


# ---------------------------------------------------------------- image helpers


def load_rgb(path: Path, size: int) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGB").resize((size, size), Image.LANCZOS), float) / 255.0


def load_gray(path: Path, size: int) -> np.ndarray:
    return np.asarray(Image.open(path).convert("L").resize((size, size), Image.LANCZOS), float) / 255.0


def save_rgb(a: np.ndarray, path: Path) -> None:
    Image.fromarray(np.clip(a * 255.0 + 0.5, 0, 255).astype(np.uint8), "RGB").save(path, optimize=True)


def luma(a: np.ndarray) -> np.ndarray:
    return a[..., 0] * 0.2126 + a[..., 1] * 0.7152 + a[..., 2] * 0.0722


def sample(pattern: np.ndarray, size: int, scale: float, offset: np.ndarray, rotation: float) -> np.ndarray:
    """The recipe's pattern transform: scale about the origin, rotate, offset, wrap."""
    v, u = np.meshgrid(np.linspace(0, 1, size, endpoint=False), np.linspace(0, 1, size, endpoint=False), indexing="ij")
    if rotation:
        c, s = np.cos(np.deg2rad(rotation)), np.sin(np.deg2rad(rotation))
        u, v = u * c - v * s, u * s + v * c
    u = (u * max(scale, 1e-6) + offset[0]) % 1.0
    v = (v * max(scale, 1e-6) + offset[1]) % 1.0
    h, w = pattern.shape[:2]
    return pattern[np.clip((v * h).astype(int), 0, h - 1), np.clip((u * w).astype(int), 0, w - 1)]


def shade(flat: np.ndarray, base: np.ndarray, mask: np.ndarray) -> np.ndarray:
    """Put the gun's own shading back over a flat paint colour.

    The finish supplies the colour, the model supplies where it is bright and where it is worn:
    the base luminance, normalised to the mean over the painted area, multiplies the paint.
    """
    weight = mask[..., None]
    painted = mask > 0.05
    if not painted.any():
        return flat
    ref = float(np.average(luma(base), weights=mask))
    gain = np.clip(luma(base) / max(ref, 1e-4), 0.35, 2.2)[..., None]
    return np.clip(flat * gain, 0, 1) * weight + base * (1 - weight)


def palette_source(icon: Path | None, pattern: np.ndarray) -> np.ndarray:
    """The colours a finish actually shows, preferring CS2's own render of the finished weapon.

    The official light-wear inventory icon is the finish applied to the weapon by CS2 itself, so its
    opaque pixels are the ground truth for which colours appear and in what proportion. The raw
    pattern is the fallback; it includes authoring background that never reaches the surface.
    """
    if icon is not None:
        a = np.asarray(Image.open(icon).convert("RGBA"), float) / 255.0
        opaque = a[..., 3] > 0.6
        if opaque.sum() > 2048:
            return a[..., :3][opaque]
    src = pattern.reshape(-1, 3)
    keep = luma(src) > 0.02  # drop the pattern's empty background, which is not a finish colour
    return src[keep] if keep.sum() > 512 else src


def palette_transfer(base: np.ndarray, src: np.ndarray, mask: np.ndarray) -> np.ndarray:
    """Map the gun's luminance onto the finish's colour distribution, percentile for percentile.

    Ranking by luminance keeps the model's light and shade while every colour the finish actually
    uses appears in the same proportion it has in the source.
    """
    order = np.argsort(luma(src))
    src = src[order]
    q = np.linspace(0, 1, 256)
    lut = src[np.clip((q * (len(src) - 1)).astype(int), 0, len(src) - 1)]
    lut = np.stack([np.convolve(lut[:, c], np.ones(9) / 9, mode="same") for c in range(3)], axis=1)
    lut[:4], lut[-4:] = lut[4], lut[-5]  # the convolution's ends borrow from outside the range
    b = luma(base)
    lo, hi = np.percentile(b[mask > 0.05], [2, 98]) if (mask > 0.05).any() else (0.0, 1.0)
    idx = np.clip((b - lo) / max(hi - lo, 1e-4), 0, 1)
    flat = np.clip(lut[np.clip((idx * 255).astype(int), 0, 255)], 0, 1)
    # The LUT already carries the finish's own light and shade, so only a light touch of the
    # model's detail is layered back; a full re-shade would darken it twice.
    weight = mask[..., None]
    ref = float(np.average(b, weights=mask)) if (mask > 0.05).any() else 0.5
    gain = np.clip(0.6 + 0.4 * (b / max(ref, 1e-4)), 0.6, 1.5)[..., None]
    out = np.clip(flat * gain, 0, 1)
    # Anchor the overall brightness to the finish itself: percentile mapping alone inherits the
    # base's exposure, which leaves a pale finish looking as dark as the factory gun.
    painted = mask > 0.05
    if painted.any():
        want, have = float(luma(src).mean()), float(np.average(luma(out), weights=mask))
        out = np.clip(out * np.clip(want / max(have, 1e-4), 0.5, 3.0), 0, 1)
    return out * weight + base * (1 - weight)


# ---------------------------------------------------------------- per-gun inputs


def gun_inputs(gun: dict, size: int) -> dict:
    """The mod's shipped maps plus the current model's paint mask, all at the working size."""
    stem, folder = gun["cs2Stem"], CURRENT / gun["cs2Dir"] / "materials/composite_inputs"
    masks = next(iter(sorted(folder.glob(f"{stem}_masks.png"))), None)
    if masks is None:
        raise SystemExit(f"{stem}: no composite_inputs masks")
    ao = next(iter(sorted(folder.glob(f"{stem}_cavity_*_ao.png"))), None)
    asset = gun["variantAsset"]
    return {
        "color": load_rgb(TEX / f"{asset}_hd.png", size),
        "orm": load_rgb(TEX / f"{asset}_hd_orm.png", size),
        "normal": TEX / f"{asset}_hd_normal.png",
        # R of the composite mask is the paint coverage: 1 where a finish covers the surface.
        "paint": np.asarray(Image.open(masks).convert("RGB").resize((size, size), Image.LANCZOS), float)[..., 0] / 255.0,
        "ao": load_gray(ao, size) if ao else None,
        "masksPath": masks,
    }


def icon_for(skin: dict, guns: dict) -> Path | None:
    cs2 = guns[skin["gun"]]["cs2Dir"]
    return find_one(f"weapon_{cs2}_{skin['key']}_light_png")


# ---------------------------------------------------------------- bake


def bake(skin: dict, guns: dict, size: int) -> dict:
    gun = guns[skin["gun"]]
    recipe = find_one(Path(skin["recipe"]).name, suffix=".vcompmat")
    if recipe is None:
        raise SystemExit(f"{skin['key']}: recipe {skin['recipe']} not found")
    vmat = recipe_material(recipe)
    p = parse_vmat(vmat)
    src = gun_inputs(gun, size)
    base, mask = src["color"], src["paint"]

    pattern_ref = p.get("TexturePattern", "")
    pattern_path = find_one(Path(pattern_ref).name) if pattern_ref else None
    if pattern_path is None:
        raise SystemExit(f"{skin['key']}: pattern {pattern_ref} not found")
    pattern = load_rgb(pattern_path, 2048 if Image.open(pattern_path).size[0] >= 1024 else 256)

    brightness = num(p, "g_flColorBrightness", 1.0)
    used = {"recipe": str(recipe), "material": str(vmat), "pattern": str(pattern_path),
            "paintMask": str(src["masksPath"]), "style": p.get("F_PAINT_STYLE", ""),
            "brightness": brightness, "paintRoughness": num(p, "g_flPaintRoughness", -1.0)}

    if skin["mode"] == "pattern":
        # Anodized multi: the pattern's R/G/B pick colours 1..3 over colour 0.
        colors = [vec(p.get(f"g_vColor{i}", ""), (0.5, 0.5, 0.5)) for i in range(4)]
        t = sample(pattern, size,
                   num(p, "g_flPatternTexCoordScale", 1.0),
                   vec(p.get("g_vPatternTexCoordOffset", ""), (0, 0, 0)),
                   num(p, "g_flPatternTexCoordRotation", 0.0))
        w = np.clip(t, 0, 1)
        rest = np.clip(1.0 - w.sum(axis=-1, keepdims=True), 0, 1)
        flat = (colors[0] * rest + colors[1] * w[..., 0:1] + colors[2] * w[..., 1:2] + colors[3] * w[..., 2:3])
        color = shade(np.clip(flat * brightness, 0, 1), base, mask)
        used["colors"] = [list(np.round(c, 4)) for c in colors]
    elif skin["mode"] == "fade":
        # The gradient is looked up through this weapon's own paint-by-number mask, which the
        # recipe authors in the current model's UV space.
        pbn = find_one(f"{skin['key']}_paint_by_number_masks")
        if pbn is None:
            raise SystemExit(f"{skin['key']}: paint-by-number mask not found")
        # R of the mask is the position along the weapon; the gradient varies along U only.
        g = np.asarray(Image.open(pbn).convert("RGB").resize((size, size), Image.LANCZOS), float)[..., 0] / 255.0
        w = pattern.shape[1]
        flat = pattern[0][np.clip((g * (w - 1)).astype(int), 0, w - 1)]
        color = shade(np.clip(flat * brightness, 0, 1), base, mask)
        used["paintByNumberMask"] = str(pbn)
    else:
        icon = icon_for(skin, guns)
        color = palette_transfer(base, palette_source(icon, np.clip(pattern * brightness, 0, 1)), mask)
        used["paletteSource"] = str(icon) if icon else str(pattern_path)

    # ORM: the finish sets roughness (and metalness when the recipe declares one) over painted area.
    orm = src["orm"].copy()
    rough = num(p, "g_flPaintRoughness", -1.0)
    if rough >= 0:
        orm[..., 1] = orm[..., 1] * (1 - mask) + rough * mask
    metal = num(p, "g_flPaintMetalness", -1.0)
    if metal >= 0:
        orm[..., 2] = orm[..., 2] * (1 - mask) + metal * mask

    asset, key = gun["variantAsset"], skin["key"]
    out = []
    save_rgb(color, TEX / f"{asset}_hd__{key}.png"); out.append(f"{asset}_hd__{key}.png")
    save_rgb(orm, TEX / f"{asset}_hd__{key}_orm.png"); out.append(f"{asset}_hd__{key}_orm.png")
    # The recipe's own normal, when it has one, is in the legacy UV layout; the gun's normal is the
    # one that matches this mesh, so it is reused rather than replaced.
    Image.open(src["normal"]).convert("RGB").resize((size, size), Image.LANCZOS).save(
        TEX / f"{asset}_hd__{key}_normal.png", optimize=True)
    out.append(f"{asset}_hd__{key}_normal.png")
    if p.get("TextureNormal"):
        used["recipeNormalIgnored"] = p["TextureNormal"]
    if p.get("TexturePearlescenceMask"):
        used["pearlescenceMaskIgnored"] = p["TexturePearlescenceMask"]
        used["pearlescentScale"] = num(p, "g_flPearlescentScale", 0.0)

    icon = icon_for(skin, guns)
    if icon is not None:
        im = Image.open(icon).convert("RGBA")
        side = max(im.size)
        square = Image.new("RGBA", (side, side), (0, 0, 0, 0))
        square.paste(im, ((side - im.size[0]) // 2, (side - im.size[1]) // 2))
        square.resize((128, 128), Image.LANCZOS).save(TEX / f"{asset}_slot__{key}.png", optimize=True)
        out.append(f"{asset}_slot__{key}.png")
        used["icon"] = str(icon)
    used["outputs"] = out
    return used


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    h.update(path.read_bytes())
    return h.hexdigest()


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", nargs="*", default=None)
    ap.add_argument("--size", type=int, default=1024)
    ap.add_argument("--manifest", default=str(ROOT / "docs/gun-skins-assets.json"))
    a = ap.parse_args()
    catalog = json.loads(CATALOG.read_text(encoding="utf-8"))
    guns = catalog["guns"]
    report = {"catalogVersion": catalog["version"], "size": a.size, "skins": {}}
    for skin in catalog["skins"]:
        if a.only and skin["key"] not in a.only:
            continue
        used = bake(skin, guns, a.size)
        used["mode"] = skin["mode"]
        used["files"] = {name: sha256(TEX / name) for name in used["outputs"]}
        report["skins"][skin["key"]] = used
        print(f"{skin['key']:30s} {skin['mode']:8s} -> {len(used['outputs'])} files")
    Path(a.manifest).write_text(json.dumps(report, indent=1, ensure_ascii=False, default=str) + "\n", encoding="utf-8")
    print("manifest:", a.manifest)
    return 0


if __name__ == "__main__":
    sys.exit(main())
