#!/usr/bin/env python3
"""Bake clean gun finishes into the unchanged runtime HD UV layout.

Custom/gunsmith RGB artwork is transferred from the embedded body_legacy mesh,
not reduced to an icon palette. Shared patterns keep recipe colours. Painted
albedo never uses the scratched factory colour as a luminance multiplier.
No synthetic wear/grunge is added; this is NOT Valve's Factory New compositor.
Original textures and inventory icons are read-only unless --icons is supplied.

Install tools/requirements-gun-skins.txt, then run with --export-root PATH.
Use --out DIR --manifest PATH to inspect a staged bake before installing it.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
from pathlib import Path

import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt

import cs2_kv3
from cs2_glb import Glb
from gun_skin_reproject import body, correspondence, lookup, raster_surface

Image.MAX_IMAGE_PIXELS = None
ROOT = Path(__file__).resolve().parent.parent
TEX = ROOT / "src/ScCsgoKnives/Assets/Textures/ScCsgoKnives"
CATALOG = ROOT / "tools/gun_skins_catalog.json"


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def find_one(root, reference):
    stem = Path(reference).stem
    suffix = Path(reference).suffix
    suffix = suffix if suffix in (".vmat", ".vcompmat") else ".png"
    hits = [p for p in root.rglob(f"{stem}*{suffix}") if "raw_compiled" not in p.parts]
    exact = [p for p in hits if p.stem == stem]
    if not hits:
        raise FileNotFoundError(f"{reference} not found in {root}")
    return min(exact or hits, key=lambda p: (len(p.parts), len(p.name), str(p)))


def parse_vmat(path):
    out, depth = {}, 0
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        depth += line.count("{") - line.count("}")
        match = re.match(r'"([^"]+)"\s+"([^"]*)"$', line)
        if depth <= 1 and match:
            out[match[1]] = match[2]
    return out


def recipe_parameters(path, paints):
    doc = cs2_kv3.load(path)
    material = next(n["m_strSpecificContainerMaterial"] for n in cs2_kv3.walk(doc)
                    if "m_strSpecificContainerMaterial" in n)
    material = find_one(paints, material)
    params = parse_vmat(material)
    # New-style recipes override template values through structured loose variables.
    aliases = {"g_tPattern": "TexturePattern", "g_tPaintByNumberMasks": "TextureMasks1"}
    for node in cs2_kv3.walk(doc):
        if node.get("m_strAlias") not in ("econ_instance", "exposed_params"):
            continue
        for value in node.get("m_vecLooseVariables", []):
            key = aliases.get(value["m_strName"], value["m_strName"])
            kind = value.get("m_nVariableType", "")
            if kind == "LOOSE_VARIABLE_TYPE_RESOURCE_TEXTURE":
                params[key] = value["m_strTextureContentAssetPath"]
            elif kind == "LOOSE_VARIABLE_TYPE_COLOR4":
                params[key] = np.array(value["m_cValueColor4"], float) / 255
            elif kind == "LOOSE_VARIABLE_TYPE_FLOAT2":
                params[key] = np.array([value.get("m_flValueFloatX", 0), value.get("m_flValueFloatY", 0), 0])
            elif kind == "LOOSE_VARIABLE_TYPE_FLOAT1":
                params[key] = value["m_flValueFloatX"]
    return params, material


def vec(value, fallback=(0, 0, 0)):
    if isinstance(value, np.ndarray):
        return value[:3]
    nums = [float(x) for x in re.findall(r"[-+]?\d*\.?\d+(?:[eE][-+]?\d+)?", str(value or ""))]
    return np.array(nums[:3] if len(nums) >= 3 else fallback)


def load_rgb(path, size=None):
    # Alpha in source paint patterns is packed material data, NOT transparency.
    im = Image.open(path).convert("RGB")
    if size is not None:
        im = im.resize((size, size), Image.Resampling.LANCZOS)
    return np.asarray(im, np.float32) / 255


def save_rgb(a, path):
    Image.fromarray(np.uint8(np.clip(a*255+0.5, 0, 255))).save(path, optimize=True)


def clean_coat(flat, base, mask):
    """Keep unpainted parts, but never inherit default paint chips in coated areas."""
    return np.clip(flat, 0, 1)*mask[..., None] + base*(1-mask[..., None])


def pattern_colors(pattern, u, v, p):
    rotation = np.deg2rad(float(p.get("g_flPatternTexCoordRotation", 0)))
    scale = float(p.get("g_flPatternTexCoordScale", 1))
    offset = vec(p.get("g_vPatternTexCoordOffset"))
    x = (u*np.cos(rotation)-v*np.sin(rotation))*scale + offset[0]
    y = (u*np.sin(rotation)+v*np.cos(rotation))*scale + offset[1]
    w = lookup(pattern, np.stack([x % 1, y % 1], -1))
    colors = [vec(p.get(f"g_vColor{i}"), (.5, .5, .5)) for i in range(4)]
    # Layered colour masks remain bounded even at overlapping RGB transitions.
    flat = np.broadcast_to(colors[0], w.shape).copy()
    for channel in range(3):
        flat = flat*(1-w[..., channel:channel+1]) + colors[channel+1]*w[..., channel:channel+1]
    return flat


def gun_inputs(gun, export, size):
    stem = gun["cs2Stem"]
    folder = export / "04_current_weapon_materials/weapons/models" / gun["cs2Dir"] / "materials/composite_inputs"
    masks = folder / f"{stem}_masks.png"
    ao = next(folder.glob(f"{stem}_cavity_*_ao.png"))
    glb = export / "02_models/glb_with_animations/weapons/models" / gun["cs2Dir"] / f"{stem}.glb"
    asset = gun["variantAsset"]
    return {"color": load_rgb(TEX/f"{asset}_hd.png", size), "orm": load_rgb(TEX/f"{asset}_hd_orm.png", size),
            "normal": TEX/f"{asset}_hd_normal.png", "mask": load_rgb(masks, size)[..., 0],
            "ao": load_rgb(ao, size)[..., 0], "glb": glb, "maskPath": masks, "aoPath": ao}


def bake(skin, gun, src, args):
    paints = args.export_root / "10_paints"
    recipe = find_one(paints, skin["recipe"])
    p, material = recipe_parameters(recipe, paints)
    pattern_path = find_one(paints, p["TexturePattern"])
    pattern = load_rgb(pattern_path)
    mask, base = src["mask"], src["color"]
    used = {"recipe": str(recipe), "material": str(material), "pattern": str(pattern_path),
            "mode": skin["mode"], "wearPolicy": "no-added-wear; not official Factory New",
            "sourceSha256": {str(f): sha256(f) for f in (recipe, material, pattern_path, src["glb"], src["maskPath"], src["aoPath"])}}
    brightness = float(p.get("g_flColorBrightness", 1))
    if skin["mode"] == "reproject":
        mapping = correspondence(src["glb"], args.size, args.cache)
        # Preserve authored RGB details, including intentionally unpainted hardware.
        color = lookup(pattern, mapping["uv"])
        mask = src["mask"] if skin.get("coverage") == "hd-metal-mask" else np.ones_like(mask)
        color = clean_coat(color, base, mask)
        used["projectionDistanceMmPercentiles"] = dict(zip(("50", "90", "99", "100"),
            (np.percentile(mapping["distance"][mapping["valid"]], [50,90,99,100])*1000).round(4).tolist()))
        used["brightnessRecordedNotApplied"] = brightness
    elif skin["mode"] == "pattern":
        v, u = np.mgrid[:args.size, :args.size] / args.size
        flat = pattern_colors(pattern, u, v, p)
        color = clean_coat(flat * brightness**(1/2.2), base, mask)
        used["placement"] = "shared pattern in HD UV; not Valve randomized legacy placement"
    elif skin["mode"] == "fade":
        mask_path = find_one(paints, p["TextureMasks1"])
        mask = load_rgb(mask_path, args.size)[..., 0]
        mesh = body(Glb(src["glb"]), "hd")
        positions, ids = raster_surface(mesh, args.size)
        valid = ids >= 0
        near = distance_transform_edt(~valid, return_distances=False, return_indices=True)
        positions = positions[tuple(near)]
        # A continuous longitudinal gradient, not a UV-island-dependent recolouring.
        # The PBN R channel is COVERAGE, not a gradient coordinate.
        angle = np.deg2rad(float(p.get("g_flPatternTexCoordRotation", 0)))
        long = positions[..., 2]*np.cos(angle) + positions[..., 1]*np.sin(angle)
        lo, hi = np.percentile(long[(mask > .5) & valid], [1, 99])
        t = np.clip((long-lo)/max(hi-lo, 1e-6), 0, 1)
        colors = np.array([vec(p[f"g_vColor{i}"]) for i in (3,2,1)])
        flat = np.stack([np.interp(t, [0,.5,1], colors[:, c]) for c in range(3)], -1)
        color = clean_coat(flat, base, mask)
        used["paintByNumberMask"] = str(mask_path)
        used["sourceSha256"][str(mask_path)] = sha256(mask_path)
        used["placement"] = "recipe colours/rotation, fixed planar gradient; not Valve seed/fade percentage"
    else:
        raise ValueError(f"Unsupported bake mode: {skin['mode']}")

    orm = src["orm"].copy()
    orm[..., 0] = src["ao"]  # geometric AO stays in ORM, not scratched colour-derived shading
    rough = float(p.get("g_flPaintRoughness", .4))
    orm[..., 1] = orm[..., 1]*(1-mask) + rough*mask
    # Conservative PBR approximation; gunsmith's packed per-pixel metal routing is
    # not reproduced. Keep that limitation explicit rather than inventing alpha wear.
    style = int(p.get("F_PAINT_STYLE", 0))
    metal = 1.0 if style in (3,4) or skin["mode"] == "fade" else (0.65 if style == 8 else 0.0)
    orm[..., 2] = orm[..., 2]*(1-mask) + metal*mask
    asset, key = gun["variantAsset"], skin["key"]
    names = [f"{asset}_hd__{key}{suffix}.png" for suffix in ("", "_orm", "_normal")]
    save_rgb(color, args.out/names[0])
    save_rgb(orm, args.out/names[1])
    normal = Image.open(src["normal"]).convert("RGB").resize((args.size,args.size), Image.Resampling.LANCZOS)
    existing = TEX/names[2]
    if existing.exists() and np.array_equal(np.asarray(Image.open(existing).convert("RGB")), np.asarray(normal)):
        # Preserve byte-identical existing normals instead of PNG encoder churn.
        if existing.resolve() != (args.out/names[2]).resolve():
            shutil.copyfile(existing, args.out/names[2])
    else:
        normal.save(args.out/names[2], optimize=True)
    used["materialLimits"] = "HD normals retained; uniform paint roughness/metalness; no custom normal or pearlescence compositor"
    retained_icon = TEX/f"{asset}_slot__{key}.png"
    used["unchangedInventoryIcon"] = {"file": retained_icon.name, "sha256": sha256(retained_icon)}
    if args.icons:
        icon = find_one(paints, f"weapon_{gun['cs2Dir']}_{key}_light_png.png")
        im = Image.open(icon).convert("RGBA")
        side = max(im.size)
        square = Image.new("RGBA", (side,side))
        square.paste(im, ((side-im.width)//2, (side-im.height)//2))
        name = f"{asset}_slot__{key}.png"
        square.resize((128,128), Image.Resampling.LANCZOS).save(args.out/name, optimize=True)
        names.append(name)
    used["outputs"] = names
    used["files"] = {name: sha256(args.out/name) for name in names}
    return used


def main():
    ap = argparse.ArgumentParser()
    local_export = ROOT.parent / "CSMCReverse/local_cs2_analysis/all_weapons"
    ap.add_argument("--export-root", type=Path, default=local_export if local_export.exists() else Path.home()/"workspaces/CSMCReverse/local_cs2_analysis/all_weapons")
    ap.add_argument("--out", type=Path, default=TEX)
    ap.add_argument("--cache", type=Path, default=ROOT/".tmp/skin-uv-cache")
    ap.add_argument("--manifest", type=Path, default=ROOT/"docs/gun-skins-assets.json")
    ap.add_argument("--only", nargs="*")
    ap.add_argument("--size", type=int, default=1024)
    ap.add_argument("--icons", action="store_true")
    args = ap.parse_args()
    if not 64 <= args.size <= 4096:
        ap.error("size must be between 64 and 4096")
    catalog = json.loads(CATALOG.read_text(encoding="utf-8"))
    if args.only and set(args.only)-{s["key"] for s in catalog["skins"]}:
        ap.error("unknown skin key")
    args.out.mkdir(parents=True, exist_ok=True)
    report = {"catalogVersion": catalog["version"], "size": args.size, "skins": {}}
    inputs = {}
    for skin in catalog["skins"]:
        if args.only and skin["key"] not in args.only:
            continue
        gun = catalog["guns"][skin["gun"]]
        if skin["gun"] not in inputs:
            inputs[skin["gun"]] = gun_inputs(gun, args.export_root, args.size)
        report["skins"][skin["key"]] = bake(skin, gun, inputs[skin["gun"]], args)
        print(f"{skin['key']}: {skin['mode']}", flush=True)
    args.manifest.parent.mkdir(parents=True, exist_ok=True)
    args.manifest.write_text(json.dumps(report, ensure_ascii=False, indent=1)+"\n", encoding="utf-8")


if __name__ == "__main__":
    main()
