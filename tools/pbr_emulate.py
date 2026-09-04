"""Offline emulation of Shaders/KnifePbr.psh: rasterise a sweep frame's parts (view space)
and shade them with the shipped base/ORM/normal maps, env atlas and BRDF LUT, so the
first-person look can be examined without a GPU.
    python3 tools/pbr_emulate.py <asset> <sweep.json> <out.png> [W H] [normal=0/1] [light=1.0] [metal=x] [rough=x]
"""
import sys, json, math, numpy as np
from PIL import Image
sys.path.insert(0, 'tools'); import gripsolve as G
asset, sweep, out = sys.argv[1:4]
W, H = (int(sys.argv[4]), int(sys.argv[5])) if len(sys.argv) > 5 else (640, 361)
kv = dict(a.split('=') for a in sys.argv[6:] if '=' in a)
USE_NORMAL = kv.get('normal', '1') == '1'; LIGHT = float(kv.get('light', '1.0'))
FORCE_METAL = kv.get('metal'); FORCE_ROUGH = kv.get('rough'); FRAME = int(kv.get('frame', '0'))
FLIPV = kv.get('flipv', '0') == '1'; MIN_ROUGH = float(kv.get('minrough', '0')); PITCH = math.radians(float(kv.get('pitch', '0')))   # camera pitch up (deg): rotates the world-aligned env
ENV_RANGE, ENV_INT, EXPOSURE, DIRECT, SAT, RBIAS = 6.0, float(kv.get('env', '1')) * LIGHT, 1.0, 0.5 * float(kv.get('direct', '1')) * LIGHT, 0.25, float(kv.get('rbias', '0'))
TEX = 'src/ScCsgoKnives/Assets/Textures/ScCsgoKnives/'
TEXSCALE = int(kv.get('texscale', '0'))   # downsample the material maps to this size first (mipmap stand-in); 0 = as shipped
def load(name, mode='RGB', scale=True):
    im = Image.open(TEX + name).convert(mode)
    if scale and TEXSCALE > 0 and im.size[0] > TEXSCALE: im = im.resize((TEXSCALE, TEXSCALE), Image.BOX)
    return np.asarray(im, np.float32) / 255.0
base_t = load(f'{asset}.png'); orm_t = load(f'{asset}_orm.png'); nrm_t = load(f'{asset}_normal.png')
env_t = load('env_specular_rgbm.png', 'RGBA', scale=False); brdf_t = load('env_brdf.png', scale=False)
def sample(tex, u, v, wrap=True):
    if FLIPV: v = 1.0 - v
    h, w = tex.shape[:2]
    if wrap: u = np.mod(u, 1.0); v = np.mod(v, 1.0)
    x = np.clip((u * (w - 1)).astype(int), 0, w - 1); y = np.clip(((1 - v) * (h - 1)).astype(int), 0, h - 1)   # OBJ v up
    return tex[y, x]
def sample_env_level(d, row):
    a = np.abs(d); face = np.zeros(len(d)); uvx = np.zeros(len(d)); uvy = np.zeros(len(d)); m = np.zeros(len(d))
    cx = (a[:, 0] >= a[:, 1]) & (a[:, 0] >= a[:, 2]); cy = ~cx & (a[:, 1] >= a[:, 2]); cz = ~cx & ~cy
    px = cx & (d[:, 0] > 0); nx = cx & ~(d[:, 0] > 0); py = cy & (d[:, 1] > 0); ny = cy & ~(d[:, 1] > 0); pz = cz & (d[:, 2] > 0); nz = cz & ~(d[:, 2] > 0)
    for mask, f, ux, uy, mm in ((px, 0, -d[:, 2], -d[:, 1], a[:, 0]), (nx, 1, d[:, 2], -d[:, 1], a[:, 0]), (py, 2, d[:, 0], d[:, 2], a[:, 1]), (ny, 3, d[:, 0], -d[:, 2], a[:, 1]), (pz, 4, d[:, 0], -d[:, 1], a[:, 2]), (nz, 5, -d[:, 0], -d[:, 1], a[:, 2])):
        face[mask] = f; uvx[mask] = ux[mask]; uvy[mask] = uy[mask]; m[mask] = mm[mask]
    uvx = (uvx / m + 1) * 0.5; uvy = (uvy / m + 1) * 0.5; pad = 0.5 / 128
    uvx = np.clip(uvx, pad, 1 - pad); uvy = np.clip(uvy, pad, 1 - pad)
    ax = (face + uvx) / 6.0; ay = (row + uvy) / 6.0
    h, w = env_t.shape[:2]; t = env_t[np.clip((ay * (h - 1)).astype(int), 0, h - 1), np.clip((ax * (w - 1)).astype(int), 0, w - 1)]   # texture(): v=0 at top
    c = t[:, :3] * t[:, 3:4] * ENV_RANGE; l = c @ np.array([0.2126, 0.7152, 0.0722])
    return l[:, None] + (c - l[:, None]) * SAT
def sample_env(d, rough):
    level = np.clip(rough, 0, 1) * 5.0; r0 = np.floor(level); r1 = np.minimum(r0 + 1, 5); f = (level - r0)[:, None]
    return sample_env_level(d, r0) * (1 - f) + sample_env_level(d, r1) * f
doc = json.load(open(sweep)); fr = doc['frames'][FRAME]; fx, fy = doc['weaponProjX'], doc['weaponProjY']
pos = np.zeros((H, W, 3), np.float32); nrm = np.zeros((H, W, 3), np.float32); uvb = np.zeros((H, W, 2), np.float32); zb = np.full((H, W), np.inf); tan = np.zeros((H, W, 3), np.float32); bit = np.zeros((H, W, 3), np.float32)
def load_obj(path):
    V = []; T = []; F = []
    for l in open(path):
        if l.startswith('v '): V.append([float(x) for x in l.split()[1:4]])
        elif l.startswith('vt '): T.append([float(x) for x in l.split()[1:3]])
        elif l.startswith('f '): F.append([tuple(int(i) - 1 for i in (w.split('/') + ['0', '0'])[:2]) for w in l.split()[1:]])
    return np.array(V), np.array(T), F
for part, m in fr['parts'].items():
    V, T, F = load_obj(f'src/ScCsgoKnives/Assets/Models/ScCsgoKnives/{asset}_{part}.obj'); M = np.array(m).reshape(4, 4)
    P = (np.c_[V, np.ones(len(V))] @ M)[:, :3]
    for f in F:
        ids = [i[0] for i in f]; tids = [i[1] for i in f]; p = P[ids]; z = -p[:, 2]
        if (z <= 0.05).any(): continue
        sx = (0.5 + 0.5 * p[:, 0] * fx / z) * W; sy = (0.5 - 0.5 * p[:, 1] * fy / z) * H
        x0, x1 = int(max(0, sx.min())), int(min(W - 1, sx.max())); y0, y1 = int(max(0, sy.min())), int(min(H - 1, sy.max()))
        if x1 < x0 or y1 < y0: continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
        d = (sx[1] - sx[0]) * (sy[2] - sy[0]) - (sx[2] - sx[0]) * (sy[1] - sy[0])
        if abs(d) < 1e-9: continue
        l1 = ((sx[1] - xs) * (sy[2] - ys) - (sx[2] - xs) * (sy[1] - ys)) / d; l2 = ((sx[2] - xs) * (sy[0] - ys) - (sx[0] - xs) * (sy[2] - ys)) / d; l3 = 1 - l1 - l2
        mask = (l1 >= 0) & (l2 >= 0) & (l3 >= 0)
        if not mask.any(): continue
        # perspective-correct interpolation
        iw = l1 / z[0] + l2 / z[1] + l3 / z[2]; zz = 1 / iw
        sub = zb[y0:y1 + 1, x0:x1 + 1]; mask &= zz < sub
        if not mask.any(): continue
        wts = np.stack([l1 / z[0], l2 / z[1], l3 / z[2]], -1) / iw[..., None]
        pp = wts @ p; uv = np.array([T[t] for t in tids]); uu = wts @ uv
        n = np.cross(p[1] - p[0], p[2] - p[0]); n /= np.linalg.norm(n) + 1e-12
        e1 = p[1] - p[0]; e2 = p[2] - p[0]; du1 = uv[1] - uv[0]; du2 = uv[2] - uv[0]
        det = du1[0] * du2[1] - du2[0] * du1[1]
        if abs(det) > 1e-12:
            tt = (e1 * du2[1] - e2 * du1[1]) / det; bb = (e2 * du1[0] - e1 * du2[0]) / det
        else: tt = np.zeros(3); bb = np.zeros(3)
        sub[mask] = zz[mask]; pos[y0:y1 + 1, x0:x1 + 1][mask] = pp[mask]; uvb[y0:y1 + 1, x0:x1 + 1][mask] = uu[mask]
        nrm[y0:y1 + 1, x0:x1 + 1][mask] = n; tan[y0:y1 + 1, x0:x1 + 1][mask] = tt; bit[y0:y1 + 1, x0:x1 + 1][mask] = bb
hit = np.isfinite(zb); P = pos[hit]; Ng = nrm[hit]; UV = uvb[hit]; Tt = tan[hit]; Bt = bit[hit]
base = sample(base_t, UV[:, 0], UV[:, 1]) ** 2.2; orm = sample(orm_t, UV[:, 0], UV[:, 1])
ao = orm[:, 0]; rough = np.clip(orm[:, 1] + RBIAS, 0.04, 1); metal = orm[:, 2]
if FORCE_METAL is not None: metal = np.full_like(metal, float(FORCE_METAL))
if FORCE_ROUGH is not None: rough = np.full_like(rough, float(FORCE_ROUGH))
rough = np.maximum(rough, MIN_ROUGH)
V = -P / np.linalg.norm(P, axis=1, keepdims=True)
flip = (Ng * V).sum(1) < 0; Ng[flip] = -Ng[flip]
if USE_NORMAL:
    tn = sample(nrm_t, UV[:, 0], UV[:, 1]) * 2 - 1
    # Schueler frame: T,B from the triangle's derivatives, normalised by the larger one (as the shader does)
    Tp = Tt - Ng * (Tt * Ng).sum(1, keepdims=True); Bp = Bt - Ng * (Bt * Ng).sum(1, keepdims=True)
    invmax = 1 / np.sqrt(np.maximum((Tp * Tp).sum(1), (Bp * Bp).sum(1)) + 1e-12)
    Tp *= invmax[:, None]; Bp *= invmax[:, None]
    N = Tp * tn[:, :1] + Bp * tn[:, 1:2] + Ng * tn[:, 2:3]; N /= np.linalg.norm(N, axis=1, keepdims=True) + 1e-12
else: N = Ng
f0 = 0.04 * (1 - metal[:, None]) + base * metal[:, None]; diffuse = base * (1 - metal[:, None])
a = rough * rough; a2 = a * a; NoV = np.maximum((N * V).sum(1), 1e-4)
R = 2 * (N * V).sum(1, keepdims=True) * N - V     # reflect(-V, N)
brdf = brdf_t[np.clip(((1 - rough) * 255).astype(int), 0, 255), np.clip((NoV * 255).astype(int), 0, 255)][:, :2]   # texture(u_brdf, (NoV, rough)): v = rough, v=0 at top? (shader samples with rough as y)
cp, sp = math.cos(PITCH), math.sin(PITCH); Rx = np.array([[1,0,0],[0,cp,-sp],[0,sp,cp]])   # view->world for a camera pitched up
Nw = N @ Rx.T; Rw = R @ Rx.T
irr = sample_env(Nw, np.ones(len(N))); pre = sample_env(Rw, rough)
ibl = (irr * diffuse + pre * (f0 * brdf[:, :1] + brdf[:, 1:2])) * ao[:, None] * ENV_INT
def direct(Ldir):
    L = np.array(Ldir, np.float32); L /= np.linalg.norm(L); NoL = np.maximum(N @ L, 0)
    Hh = L + V; Hh /= np.linalg.norm(Hh, axis=1, keepdims=True) + 1e-12
    NoH = np.maximum((N * Hh).sum(1), 0); VoH = np.maximum((V * Hh).sum(1), 0)
    F = f0 + (1 - f0) * ((1 - VoH) ** 5)[:, None]
    dd = NoH * NoH * (a2 - 1) + 1; D = a2 / (math.pi * dd * dd + 1e-7)
    gv = NoL * np.sqrt(NoV * NoV * (1 - a2) + a2); gl = NoV * np.sqrt(NoL * NoL * (1 - a2) + a2); Vis = 0.5 / (gv + gl + 1e-7)
    spec = F * (D * Vis)[:, None]; diff = (1 - F) * diffuse / math.pi
    return (diff + spec) * DIRECT * NoL[:, None]
# SC's two lights in view space for a camera looking down -z with world up +y (yaw irrelevant for a rough estimate)
col = (ibl + direct([0.12, 0.25, -0.34]) + direct([-0.12, 0.25, 0.34])) * EXPOSURE
tm = np.clip((col * (2.51 * col + 0.03)) / (col * (2.43 * col + 0.59) + 0.14), 0, 1) ** (1 / 2.2)
img = np.full((H, W, 3), 0.45, np.float32); img[hit] = tm
Image.fromarray((img * 255).astype(np.uint8)).save(out)
if 'dump' in kv: np.savez(kv['dump'], hit=hit, tm=tm, base=base ** (1 / 2.2), rough=rough, metal=metal, ao=ao)
print(f"{asset}: pixels {hit.sum()}  mean srgb {tm.mean(0).round(3)}  metal mean {metal.mean():.2f} rough mean {rough.mean():.2f} ao mean {ao.mean():.2f} base(lin) mean {base.mean():.3f}")
