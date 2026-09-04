"""Install the CS:MC `source2_vmat` texture set for a gun (the clean look MCCS renders),
replacing the `native` set (CS2's weathered default material, which reads as dirt and rust).

    python3 tools/install_gun_textures_s2.py ak47 awp

source2_vmat/<gun>/: *_color (or *_substrate_color), *_ao, *_rough (packed: R = roughness,
G = metalness, B = 0, the CS2 packing), *_normal (or default_normal 1x1). Output: the mod's
<gun>.png (base colour), <gun>_orm.png (R = AO, G = roughness, B = metalness), <gun>_normal.png.
Verified 2026-09-04 by rendering the AK-47 meshbin with both sets: source2_vmat gives the MCCS
look (clean wood, grey steel), native gives the yellow patch and rust the user called 花.
"""
import sys, os, io, zipfile, glob
import numpy as np
from PIL import Image
ZIP = '/home/dev/workspaces/reference/CSMCClient20260822.zip'
PRE = 'overrides/gec_texture_stream/tex/source2_vmat/'
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), 'src/ScCsgoKnives/Assets/Textures/ScCsgoKnives')
SIZE = 1024
z = zipfile.ZipFile(ZIP)
names = z.namelist()
def pick(gun, *keys):
    hits = [n for n in names if n.startswith(f'{PRE}{gun}/') and any(k in n.rsplit('/', 1)[1] for k in keys) and not n.endswith('/')]
    if not hits: raise SystemExit(f'{gun}: no file matching {keys}')
    return hits[0]
def img(name, mode='RGB'):
    im = Image.open(io.BytesIO(z.read(name))).convert(mode)
    return im if im.size == (SIZE, SIZE) else im.resize((SIZE, SIZE), Image.LANCZOS if im.size[0] > SIZE else Image.NEAREST)
NATIVE = os.path.expanduser('~/csmc-guns/tex')
def install_native(gun, record):
    """The CS:MC `native` set (CS2's default material): used where MCCS shows it, e.g. the AWP's olive paint
    (the source2_vmat `substrate` colour is the unpainted grey base) and the M4A1-S (no source2_vmat default)."""
    tex = os.path.join(NATIVE, f'{record}_native')
    def grey(pattern):
        return np.asarray(Image.open(glob.glob(os.path.join(tex, pattern))[0]).convert('L').resize((SIZE, SIZE), Image.LANCZOS), np.uint8)
    base = Image.open(glob.glob(os.path.join(tex, '*_basecolor.webp'))[0]).convert('RGBA').resize((SIZE, SIZE), Image.LANCZOS)
    orm = np.stack([grey('*_ambient_occlusion.webp'), grey('*_roughness.webp'), grey('*_metalness.webp')], axis=-1)
    normal = Image.open(glob.glob(os.path.join(tex, '*_normal.webp'))[0]).convert('RGB').resize((SIZE, SIZE), Image.LANCZOS)
    base.save(os.path.join(OUT, f'{gun}.png'), optimize=True); Image.fromarray(orm, 'RGB').save(os.path.join(OUT, f'{gun}_orm.png'), optimize=True); normal.save(os.path.join(OUT, f'{gun}_normal.png'), optimize=True)
    print(f'{gun}: native set from {tex}  ORM means {tuple(int(v) for v in orm.reshape(-1,3).mean(0))}')

args = sys.argv[1:]
if args[:1] == ['--native']:
    for gun in args[1:]: install_native(gun, {'awp': 'awp', 'ak47': 'ak47', 'm4a1s': 'm4a1_silencer'}[gun])
    raise SystemExit
# gun -> source2_vmat directory holding CS:MC's clean vanilla set (the vmat-named dirs are CS2's weathered defaults)
S2DIR = {'ak47': 'ak47', 'awp': 'awp', 'm4a1s': 'rif_m4a1_s'}
TINT = {}   # gun -> (r, g, b) multiplier in linear light on the colour map (AWP: substrate grey -> MCCS olive)
for a in list(args):
    if a.startswith('--tint='):
        g, v = a[7:].split(':'); TINT[g] = tuple(float(x) for x in v.split(',')); args.remove(a)
for gun in args:
    d = S2DIR.get(gun, gun)
    color = img(pick(d, '_color'), 'RGBA')
    if gun in TINT:
        arr = np.asarray(color, np.float32) / 255.0
        lin = arr[..., :3] ** 2.2 * np.array(TINT[gun], np.float32)
        arr[..., :3] = np.clip(lin, 0, 1) ** (1 / 2.2)
        color = Image.fromarray((arr * 255).astype(np.uint8), 'RGBA')
    ao = np.asarray(img(pick(d, '_ao')), np.uint8)[..., 0]
    rough = np.asarray(img(pick(d, '_rough')), np.uint8)
    normal = img(pick(d, '_normal', 'default_normal'))
    orm = np.stack([ao, rough[..., 0], rough[..., 1]], axis=-1)
    color.save(os.path.join(OUT, f'{gun}.png'), optimize=True)
    Image.fromarray(orm, 'RGB').save(os.path.join(OUT, f'{gun}_orm.png'), optimize=True)
    normal.save(os.path.join(OUT, f'{gun}_normal.png'), optimize=True)
    print(f'{gun}: [{d}] color {pick(d, "_color").rsplit("/",1)[1]} tint {TINT.get(gun)}  ORM ao/rough/metal means {int(ao.mean())}/{int(rough[...,0].mean())}/{int(rough[...,1].mean())}  normal {pick(d, "_normal", "default_normal").rsplit("/",1)[1]} {normal.size}')
