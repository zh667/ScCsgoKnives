"""Install a gun's first-person material from the local CS2 export (the authoritative source
since 2026-09-04): the legacy `materials/models/weapons/v_models/<dir>/` set that CS:MC's
`source2_vmat/<short name>/` webps were converted from, here straight from the 2048x2048 PNGs.

    python3 tools/install_gun_textures_cs2.py ak47 m4a1s awp [--size=1024] [--awp-tint=r,g,b|none]

Packing follows each gun's legacy VMAT (csgo_weapon.vfx): TextureColor1 = *_color, AO = *_ao,
roughness = R of *_rough_psd_<hash>, metalness = G of the same packed texture (the VMAT binds
that texture as g_tMetalness; the M4A1-S binds a constant 0). Output: <gun>.png, <gun>_orm.png
(R = AO, G = roughness, B = metalness), <gun>_normal.png.

The AWP's legacy colour is the grey unpainted `substrate` (VMAT tint white); MCCS shows it
olive. The olive multiplier is the ratio of CS2's painted default colour to the substrate over
the painted pixels, in linear light (0.586, 0.642, 0.387) -- a derived value, kept until CS:MC's
own tint is read out. Pass --awp-tint=none to ship the grey substrate as is.
"""
import sys, os, glob
import numpy as np
from PIL import Image, ImageFilter

EXPORT = '/home/dev/workspaces/CSMCReverse/local_cs2_analysis/all_weapons/03_legacy_vmodels_materials/materials/models/weapons/v_models'
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'src/ScCsgoKnives/Assets/Textures/ScCsgoKnives')
DIRS = {'ak47': 'rif_ak47', 'm4a1s': 'rif_m4a1_s', 'awp': 'snip_awp'}
COLOR = {'ak47': 'ak47_color_psd_1f318532.png', 'm4a1s': 'rif_m4a1_s_color_psd_ebbb222d.png', 'awp': 'awp_substrate_color_psd_5f931239.png'}
AO = {'ak47': 'ak47_ao_psd_286fb1af.png', 'm4a1s': 'rif_m4a1_s_ao_psd_a4b42063.png', 'awp': 'awp_ao_psd_66d40823.png'}
ROUGH = {'ak47': 'ak47_rough_psd_73589151.png', 'm4a1s': 'rif_m4a1_s_rough_psd_856b2953.png', 'awp': 'awp_rough_psd_f317ee9.png'}
# The AK-47's legacy normal map is flat to within 3/255 but carries sparse decode outliers (0.06 % of pixels
# off by >50, seen as coloured speckles once lit), so all three ship a flat normal like their VMAT defaults.
NORMAL = {}
METAL_CONST = {'m4a1s': 0}                                   # VMAT: TextureMetalness1 = [0 0 0 0]
AWP_TINT = (0.586, 0.642, 0.387)

args = sys.argv[1:]
size = 1024
tint = AWP_TINT
for a in list(args):
    if a.startswith('--size='): size = int(a[7:]); args.remove(a)
    elif a.startswith('--awp-tint='):
        v = a[11:]; tint = None if v == 'none' else tuple(float(x) for x in v.split(',')); args.remove(a)

def load(gun, name, mode='RGB'):
    # Never resize or filter as RGBA: these PNGs carry a near-zero alpha (a paint/sticker helper), and Pillow
    # resamples RGBA premultiplied, which turns the colour into noise wherever alpha ~ 0. Colour and alpha
    # are handled as separate RGB / L images.
    src = Image.open(os.path.join(EXPORT, DIRS[gun], name))
    im = src.convert(mode)
    im = im.filter(ImageFilter.MedianFilter(3))               # the vtex decodes carry isolated wrong pixels (up to 0.2 %)
    return im if im.size == (size, size) else im.resize((size, size), Image.LANCZOS)

def load_alpha(gun, name):
    src = Image.open(os.path.join(EXPORT, DIRS[gun], name))
    a = src.split()[-1] if src.mode == 'RGBA' else Image.new('L', src.size, 255)
    return a if a.size == (size, size) else a.resize((size, size), Image.LANCZOS)

for gun in args:
    rgb = np.asarray(load(gun, COLOR[gun]), np.float32) / 255.0
    if gun == 'awp' and tint is not None:
        # The substrate's alpha marks the bare-metal parts (barrel, scope, bolt, bipod: alpha high) against the
        # painted body (stock, receiver: alpha 0), so the olive goes on the body only and the metal stays grey.
        paint = 1.0 - np.asarray(load_alpha(gun, COLOR[gun]), np.float32)[..., None] / 255.0
        mult = 1.0 + (np.array(tint, np.float32) - 1.0) * paint
        rgb = np.clip(rgb ** 2.2 * mult, 0, 1) ** (1 / 2.2)
    Image.fromarray((rgb * 255).astype(np.uint8), 'RGB').save(os.path.join(OUT, f'{gun}.png'), optimize=True)
    ao = np.asarray(load(gun, AO[gun]), np.uint8)[..., 0]
    rough = np.asarray(load(gun, ROUGH[gun]), np.uint8)
    metal = np.full_like(ao, METAL_CONST[gun]) if gun in METAL_CONST else rough[..., 1]
    Image.fromarray(np.stack([ao, rough[..., 0], metal], axis=-1), 'RGB').save(os.path.join(OUT, f'{gun}_orm.png'), optimize=True)
    if gun in NORMAL: normal = load(gun, NORMAL[gun], 'RGB')
    else: normal = Image.new('RGB', (size, size), (128, 128, 255))
    normal.save(os.path.join(OUT, f'{gun}_normal.png'), optimize=True)
    print(f'{gun}: {DIRS[gun]}/{COLOR[gun]} @ {size}  ORM means ao/rough/metal {int(ao.mean())}/{int(rough[..., 0].mean())}/{int(metal.mean())}  normal {"map" if gun in NORMAL else "flat"}  tint {tint if gun == "awp" else None}')
