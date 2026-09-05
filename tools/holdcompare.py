"""Render our M9 inspect hold (from the shipped C# maths, headless) as a 1920x1080
point cloud so it can be laid over the CS:MC video's hold frame, and print the
on-screen blade tip / guard / grip so the two can be compared in pixels.

    python3 tools/holdcompare.py [knife=m9] [clip=inspect] [Key=Value ...]

Key=Value overrides go to ArmPreview (KnifeTuning), e.g. InspectTravelScale=1.
The hold is the first frame where the rigid wrist roll reaches its first peak.
"""
import sys, os, json, subprocess, numpy as np
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)
from PIL import Image, ImageDraw
import verify_cs as V, gripsolve as G, fleet_qa as F

OVERRIDES = [a for a in sys.argv[1:] if '=' in a and not a.startswith(('hold=', 'search='))]
POS = [a for a in sys.argv[1:] if '=' not in a]
knife = POS[0] if POS else 'm9'; clip = POS[1] if len(POS) > 1 else 'inspect'
FPS = 30
FX, FY = 0.6704, 1.1918          # the game's projection (Game.log: vertical fov 80deg, aspect 1.778)
W, H = 1920, 1080


WFX, WFY = FX, FY                # the weapon projection (CS:MC draws the knife at its own FOV, 48 for knives)


def screen(p, weapon=False):
    x, y, z = p
    if z >= -0.01: return None
    fx, fy = (WFX, WFY) if weapon else (FX, FY)
    return ((0.5 + 0.5 * x * fx / -z) * W, (0.5 - 0.5 * y * fy / -z) * H)


def run():
    env = dict(os.environ, DOTNET_ROLL_FORWARD='Major')
    dll = os.path.join(HERE, 'ArmPreview', 'bin', 'Release', 'net10.0', 'ArmPreview.dll')
    proj = os.path.join(HERE, 'ArmPreview', 'ArmPreview.csproj')
    src = os.path.join(ROOT, 'src', 'ScCsgoKnives')
    newest = max(os.path.getmtime(os.path.join(d, f)) for d, _, fs in os.walk(src) for f in fs if f.endswith('.cs') and 'bin' not in d and 'obj' not in d)
    if not os.path.exists(dll) or os.path.getmtime(dll) < newest:
        subprocess.run(['dotnet', 'build', proj, '-c', 'Release', '-nologo', '-v', 'q'], check=True, env=env, cwd=ROOT)
    tag = '_'.join(o.replace('=', '') for o in OVERRIDES) or 'default'
    path = os.path.join(ROOT, '.tmp-fist', f'hold_{knife}_{clip}_{tag}.json')
    out = subprocess.run(['dotnet', dll, knife, clip, str(FPS), path, str(FX), str(FY)] + OVERRIDES, capture_output=True, text=True, encoding="utf-8", errors="replace", env=env, cwd=ROOT)
    if out.returncode != 0:
        print(out.stderr); raise SystemExit(out.returncode)
    return json.load(open(path)), tag


def project_parts(fr):
    pts = []
    for part, m in fr['parts'].items():
        Vm = G.mesh_part(knife, part); M = np.array(m).reshape(4, 4)
        world = (np.c_[Vm, np.ones(len(Vm))] @ M)[:, :3]
        for p in world:
            s = screen(p, weapon=True)
            if s is not None: pts.append(s)
    return np.array(pts)


def main():
    global FX, FY, WFX, WFY
    doc, tag = run(); frames = doc['frames']
    FX, FY = float(doc['projX']), float(doc['projY'])      # the hand-pass projection the sweep used (arms)
    WFX, WFY = float(doc.get('weaponProjX', FX)), float(doc.get('weaponProjY', FY))   # the knife's own projection
    import math as _m
    print(f"hand projection fov {2*_m.degrees(_m.atan(1/FY)):.1f} deg, weapon projection fov {2*_m.degrees(_m.atan(1/WFY)):.1f} deg")
    # The M9's first inspect hold rests at 1.33 s; other knives can pass hold=<seconds>.
    hold_t = float(next((a.split('=')[1] for a in sys.argv[1:] if a.startswith('hold=')), '1.33'))
    hold = min(range(len(frames)), key=lambda i: abs(frames[i]['t'] - hold_t))
    def metrics(fr):
        pts = project_parts(fr)
        tip = pts[np.argmin(pts[:, 1])]
        grip = np.array(screen(np.array(fr['right']['grip'])))
        c = pts.mean(0); u, sv, vt = np.linalg.svd(pts - c, full_matrices=False); d = vt[0]
        if d[1] > 0: d = -d
        lean = np.degrees(np.arctan2(d[0], -d[1]))
        length = np.ptp(pts @ d)                          # extent along the knife's own axis, px
        return pts, tip, grip, lean, length
    pts0, tip0, grip0, lean0, len0 = metrics(frames[0])
    print(f"  idle  : tip=({tip0[0]:.0f},{tip0[1]:.0f}) grip=({grip0[0]:.0f},{grip0[1]:.0f}) lean={lean0:+.1f}deg length={len0:.0f}px")
    fr = frames[hold]
    pts, tip, grip, lean, length = metrics(fr)
    print(f"  hold t={fr['t']:.2f}: tip=({tip[0]:.0f},{tip[1]:.0f}) grip=({grip[0]:.0f},{grip[1]:.0f}) lean={lean:+.1f}deg length={length:.0f}px  hold/idle={length/len0:.2f}")
    print(f"{knife} {clip} [{tag}]")
    def box(fr, d2):
        s = fr['right']; seat = np.array(s['seat']); axis = np.array(s['axis']); side = np.array(s['side']); up = np.array(s['up'])
        w = s['width']; L = s['reach'] + s['overshoot']
        cs = [seat + axis * a + up * (su * w) + side * (ss * w) for a in (0.0, L) for su in (-0.5, 0.5) for ss in (-0.5, 0.5)]
        sc = [screen(c) for c in cs]
        for i, j in [(0,1),(1,3),(3,2),(2,0),(4,5),(5,7),(7,6),(6,4),(0,4),(1,5),(2,6),(3,7)]:
            if sc[i] and sc[j]: d2.line([sc[i], sc[j]], fill=(255, 255, 0), width=2)
    def render(fr, pts):
        canvas = Image.new('RGB', (W, H), (120, 160, 240)); d2 = ImageDraw.Draw(canvas)
        if fr['right']: box(fr, d2)
        for x, y in pts:
            if 0 <= x < W and 0 <= y < H: d2.point((x, y), fill=(200, 30, 30))
        return canvas
    ours_idle = render(frames[0], pts0); ours_hold = render(fr, pts)
    # side by side with the CS:MC video frames (idle at 5.0 s, hold at 8.2 s), each half size
    S = '/tmp/claude-1000/-home-dev/584a761a-993d-4689-a30e-a9182a581a55/scratchpad/vid'
    def half(im): return im.resize((960, 540))
    sheet = Image.new('RGB', (1920, 1080), (0, 0, 0))
    try:
        sheet.paste(half(Image.open(f'{S}/mccs_5.0.png').convert('RGB')), (0, 0)); sheet.paste(half(Image.open(f'{S}/mccs_8.2.png').convert('RGB')), (960, 0))
    except Exception as e: print('no mccs frames', e)
    sheet.paste(half(ours_idle), (0, 540)); sheet.paste(half(ours_hold), (960, 540))
    out = os.path.join(ROOT, '.tmp-fist', f'hold_{knife}_{clip}_{tag}.png'); sheet.save(out); print('wrote', out)


if __name__ == '__main__':
    main()
