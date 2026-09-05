#!/usr/bin/env python3
"""The AUG / SG 553 scope lens textures, from CS2's shared_scope_lens.vmat.

    weapons/models/shared/materials/scope/shared_scope_lens.vmat
        shader csgo_weapon.vfx, F_TRANSLUCENT 1
        TextureColor1 / TextureAmbientOcclusion = shared_scope_lens_mask.png
        g_vColorTint [0.156863 0.156863 0.156863]
        TextureRoughness1 [0.078431 ...], TextureMetalness1 [1 ...], TextureNormal flat
        g_flOpacityScale = pow(1 - $ent_ironsight, .5)   (clear when aiming down it)

Bakes three PNGs in the mod's PBR layout (install_gun_textures_cs2hd.py: ORM =
AO in R, roughness in G, metalness in B):

    cs2_scope_lens.png         mask x 0.156863, alpha 255
    cs2_scope_lens_orm.png     R = mask, G = 20 (0.078431 x 255), B = 255
    cs2_scope_lens_normal.png  128,128,255

    python3 tools/cs2_scope_lens_texture.py [--check]
"""
import argparse, json, os, sys
import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.expanduser("~/workspaces/CSMCReverse/local_cs2_analysis/all_weapons/07_scope/weapons/models/shared/materials/scope")
DEST = os.path.join(ROOT, "src/ScCsgoKnives/Assets/Textures/ScCsgoKnives")
TINT = 0.156863
ROUGHNESS = 0.078431
METALNESS = 1.0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()
    vmat = open(os.path.join(SRC, "shared_scope_lens.vmat"), encoding="utf-8").read()
    for needle in ('"F_TRANSLUCENT"\t"1"', "0.156863", "0.078431", 'pow(1-$ent_ironsight,.5)'):
        if needle not in vmat:
            sys.exit("shared_scope_lens.vmat no longer says %r" % needle)
    mask = np.asarray(Image.open(os.path.join(SRC, "shared_scope_lens_mask.png")).convert("RGBA"), np.float32)
    lum = mask[..., :3].mean(axis=2)
    h, w = lum.shape
    base = np.zeros((h, w, 4), np.uint8)
    base[..., :3] = np.clip(lum * TINT, 0, 255).astype(np.uint8)[..., None]
    base[..., 3] = 255
    orm = np.zeros((h, w, 3), np.uint8)
    orm[..., 0] = lum.astype(np.uint8)
    orm[..., 1] = int(round(ROUGHNESS * 255))
    orm[..., 2] = int(round(METALNESS * 255))
    normal = np.zeros((h, w, 3), np.uint8)
    normal[...] = (128, 128, 255)
    report = {
        "source": "07_scope/weapons/models/shared/materials/scope/shared_scope_lens.vmat",
        "mask_size": [w, h], "mask_mean": round(float(lum.mean()), 2),
        "base_mean": round(float(base[..., :3].mean()), 2),
        "orm": {"ao": "mask", "roughness": int(round(ROUGHNESS * 255)), "metalness": int(round(METALNESS * 255))},
        "opacity_when_aiming": "pow(1-1,.5) = 0: the lens is not drawn while zoomed",
    }
    print(json.dumps(report, indent=1))
    if args.check:
        return 0
    os.makedirs(DEST, exist_ok=True)
    Image.fromarray(base, "RGBA").save(os.path.join(DEST, "cs2_scope_lens.png"))
    Image.fromarray(orm, "RGB").save(os.path.join(DEST, "cs2_scope_lens_orm.png"))
    Image.fromarray(normal, "RGB").save(os.path.join(DEST, "cs2_scope_lens_normal.png"))
    for n in ("cs2_scope_lens.png", "cs2_scope_lens_orm.png", "cs2_scope_lens_normal.png"):
        print("wrote", n, os.path.getsize(os.path.join(DEST, n)), "bytes")
    return 0


if __name__ == "__main__":
    sys.exit(main())
