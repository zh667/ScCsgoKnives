"""Export the PBR texture set the first-person knife shader needs.

Per knife (factory finish, same source as the existing basecolor):
  {name}_orm.png     R = ambient occlusion, G = roughness, B = metalness
  {name}_normal.png  tangent-space normal map (a 4x4 flat one when CS ships none)

Shared:
  env_specular_rgbm.png  CS:MC's prefiltered specular environment (6 roughness rows x 6
                         cube faces, 128px each, RGBM-encoded) -- the light the knives
                         reflect in MCCS, reused so the metal reads the same way here.
  env_brdf.png           split-sum BRDF lookup, computed here (importance-sampled GGX),
                         u = N.V, v = roughness, R = scale, G = bias.

CS packs the "rough" map as R = roughness, G = metalness (measured: G is binary, blade 254
/ handle 1). AO is grey. Most knives ship the shared 1x1 flat default normal.

    python3 tools/export_pbr_textures.py
"""
import io, os, sys, zipfile
import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from install_knives import KNIVES, CLIENT, TEX, TEXTURE_SIZE

ENV_SRC = "/home/dev/workspaces/CSMCReverse/work/csmc_shaders/assets/csmcmod/textures/source2_environment/studio_specular_rgbm.png"
VMAT = "overrides/gec_texture_stream/tex/source2_vmat/{}/"
LUT_SIZE = 256
LUT_SAMPLES = 256


def find(z, folder, tag):
    hits = [n for n in z.namelist() if n.startswith(folder) and tag in n.rsplit("/", 1)[1] and n.endswith(".webp")]
    return hits[0] if hits else None


def load(z, name):
    return Image.open(io.BytesIO(z.read(name)))


def grey(im, size):
    return np.asarray(im.convert("L").resize((size, size), Image.LANCZOS))


def export_knives(z):
    rows = []
    for record, name, _icon in KNIVES:
        folder = VMAT.format("weapon_" + record)
        rough_n = find(z, folder, "_rough_")
        ao_n = find(z, folder, "_ao_")
        normal_n = find(z, folder, "_normal_")
        if rough_n is None:
            raise SystemExit(f"{name}: no roughness map under {folder}")
        rough = load(z, rough_n).convert("RGB")
        rough = np.asarray(rough.resize((TEXTURE_SIZE, TEXTURE_SIZE), Image.LANCZOS))
        if ao_n is not None and load(z, ao_n).size[0] > 1:
            ao = grey(load(z, ao_n), TEXTURE_SIZE)
        else:
            ao = np.full((TEXTURE_SIZE, TEXTURE_SIZE), 255, np.uint8)
        orm = np.stack([ao, rough[..., 0], rough[..., 1]], axis=-1)
        Image.fromarray(orm, "RGB").save(os.path.join(TEX, f"{name}_orm.png"), optimize=True)

        flat = True
        if normal_n is not None:
            nim = load(z, normal_n).convert("RGB")
            if nim.size[0] > 1:
                flat = False
                nim.resize((TEXTURE_SIZE, TEXTURE_SIZE), Image.LANCZOS).save(
                    os.path.join(TEX, f"{name}_normal.png"), optimize=True)
        if flat:
            Image.fromarray(np.full((4, 4, 3), (128, 128, 255), np.uint8), "RGB").save(
                os.path.join(TEX, f"{name}_normal.png"), optimize=True)
        rows.append((name, int(orm[..., 1].mean()), round(float((orm[..., 2] > 127).mean()), 2), "flat" if flat else "map"))
    return rows


def brdf_lut(size, samples):
    """Karis 2013 split-sum environment BRDF (scale, bias) by importance-sampled GGX."""
    nov = np.clip((np.arange(size) + 0.5) / size, 1e-3, 1.0)          # columns: N.V
    rough = np.clip((np.arange(size) + 0.5) / size, 0.02, 1.0)         # rows: roughness
    NoV = nov[None, :, None]
    R = rough[:, None, None]
    a = R * R
    i = np.arange(samples, dtype=np.float64)
    # Hammersley sequence
    bits = i.astype(np.uint32)
    bits = ((bits << 16) | (bits >> 16)) & 0xFFFFFFFF
    bits = ((bits & 0x55555555) << 1) | ((bits & 0xAAAAAAAA) >> 1)
    bits = ((bits & 0x33333333) << 2) | ((bits & 0xCCCCCCCC) >> 2)
    bits = ((bits & 0x0F0F0F0F) << 4) | ((bits & 0xF0F0F0F0) >> 4)
    bits = ((bits & 0x00FF00FF) << 8) | ((bits & 0xFF00FF00) >> 8)
    xi1 = (i + 0.5) / samples
    xi2 = bits.astype(np.float64) / 4294967296.0
    xi1 = xi1[None, None, :]
    xi2 = xi2[None, None, :]
    phi = 2.0 * np.pi * xi1
    cos_t = np.sqrt((1.0 - xi2) / (1.0 + (a * a - 1.0) * xi2))
    sin_t = np.sqrt(np.maximum(0.0, 1.0 - cos_t * cos_t))
    Hx, Hy, Hz = sin_t * np.cos(phi), sin_t * np.sin(phi), cos_t
    Vx, Vz = np.sqrt(np.maximum(0.0, 1.0 - NoV * NoV)), NoV
    VoH = Vx * Hx + Vz * Hz
    Lz = 2.0 * VoH * Hz - Vz
    NoL = np.maximum(Lz, 0.0)
    NoH = np.maximum(Hz, 0.0)
    VoH = np.maximum(VoH, 0.0)
    k = a * a / 2.0
    G = (NoL / (NoL * (1 - k) + k)) * (NoV / (NoV * (1 - k) + k))
    G_vis = np.where(NoL > 0, G * VoH / np.maximum(NoH * NoV, 1e-6), 0.0)
    Fc = (1.0 - VoH) ** 5
    A = ((1.0 - Fc) * G_vis).mean(axis=-1)
    B = (Fc * G_vis).mean(axis=-1)
    lut = np.zeros((size, size, 3), np.uint8)
    lut[..., 0] = np.clip(A * 255 + 0.5, 0, 255)
    lut[..., 1] = np.clip(B * 255 + 0.5, 0, 255)
    return lut


def main():
    os.makedirs(TEX, exist_ok=True)
    z = zipfile.ZipFile(CLIENT)
    rows = export_knives(z)
    Image.open(ENV_SRC).convert("RGBA").save(os.path.join(TEX, "env_specular_rgbm.png"), optimize=True)
    lut = brdf_lut(LUT_SIZE, LUT_SAMPLES)
    Image.fromarray(lut, "RGB").save(os.path.join(TEX, "env_brdf.png"), optimize=True)
    print(f"{'knife':12}{'rough(mean)':>12}{'metal(frac)':>12}  normal")
    for name, r, m, n in rows:
        print(f"{name:12}{r:12d}{m:12.2f}  {n}")
    # LUT sanity: smooth + grazing should be near (1, 0); rough + facing well below 1.
    print("lut[rough=0,NoV=1] =", lut[0, -1, :2], " lut[rough=1,NoV=1] =", lut[-1, -1, :2],
          " lut[rough=0.5,NoV=0.5] =", lut[LUT_SIZE // 2, LUT_SIZE // 2, :2])
    total = sum(os.path.getsize(os.path.join(TEX, f)) for f in os.listdir(TEX) if f.endswith(("_orm.png", "_normal.png", "env_specular_rgbm.png", "env_brdf.png")))
    print(f"PBR textures total {total / 1e6:.1f} MB")


if __name__ == "__main__":
    main()
