"""Solve the two things the reverse-engineered chain leaves open -- the placement's
uniform scale s and view-space translation t -- from the CS:MC video.

The chain's rotations are settled (our idle lean matches the video to a degree).
Everything else is a similarity: a mesh point p (from the headless sweep run with
scale 1 and no hand translate) lands at s*p + t in view space. Three observed
points (idle tip, hold tip, hold pommel) give six equations for four unknowns;
Gauss-Newton solves them. The fitted t is then compared with the candidates:
Minecraft's hand translate (0.56, -0.52, -0.72), or nothing.

    python3 tools/chainfit.py [Key=Value overrides for the sweep]
"""
import sys, os, json, subprocess, math, numpy as np
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)
import gripsolve as G

FOV = float(next((a.split('=')[1] for a in sys.argv[1:] if a.startswith('fov=')), '48'))  # CS:MC draws the weapon at its own FOV (48 for knives)
W, H = 1920, 1084          # the CS:MC video frame
FY = 1.0 / math.tan(math.radians(FOV / 2)); FX = FY / (W / H)
OBS = {  # measured on MCCS_VIDEO/M9.mp4 frames (idle 5.0 s, hold 8.2 s) by red-pixel segmentation
    'idle_tip': (742, 611), 'idle_pommel': (1559, 1020), 'hold_tip': (1399, 204), 'hold_pommel': (1215, 940),
}
HOLD_T = 1.33
REAL = any(a == 'real=1' for a in sys.argv[1:])   # real=1: sweep at the chain's own scale and fit only the translation
BASE = ['ExactChain=1', 'ExactScaleOverride=' + ('0' if REAL else '1'), 'ExactHandX=0', 'ExactHandY=0', 'ExactHandZ=0', 'ExactMirrorX=0']


def sweep(overrides):
    env = dict(os.environ, DOTNET_ROLL_FORWARD='Major')
    dll = os.path.join(HERE, 'ArmPreview', 'bin', 'Release', 'net10.0', 'ArmPreview.dll')
    path = os.path.join(ROOT, '.tmp-fist', 'chainfit_m9.json')
    r = subprocess.run(['dotnet', dll, 'm9', 'inspect', '30', path, str(FX), str(FY)] + overrides, capture_output=True, text=True, env=env, cwd=ROOT)
    if r.returncode: print(r.stderr); raise SystemExit(1)
    return json.load(open(path))


def knife_points(frame):
    """3D view-space points of the whole mesh at this frame, plus tip/pommel picks."""
    pts = []
    for part, m in frame['parts'].items():
        V = G.mesh_part('m9', part); M = np.array(m).reshape(4, 4)
        pts.append((np.c_[V, np.ones(len(V))] @ M)[:, :3])
    P = np.vstack(pts)
    c = P.mean(0); axis = np.linalg.svd(P - c, full_matrices=False)[2][0]
    proj = (P - c) @ axis
    tip, pommel = P[np.argmax(proj)], P[np.argmin(proj)]
    # the tip is the end nearer the top of the screen at idle (blade up-left)
    if tip[1] < pommel[1]: tip, pommel = pommel, tip
    return P, tip, pommel


def project(p):
    x, y, z = p
    return np.array([(0.5 + 0.5 * x * FX / -z) * W, (0.5 - 0.5 * y * FY / -z) * H])


def fit(points, obs, tz=None):
    """Grid over scale s (and depth tz unless given); for each, tx/ty are linear in the
    projection (the depth does not depend on them) and solved exactly. Returns the best."""
    best = None
    tzs = [tz] if tz is not None else np.linspace(-1.5, 1.0, 126)
    fixed = next((float(a.split('=')[1]) for a in sys.argv[1:] if a.startswith('s=')), 1.0 if REAL else None)
    for sv in ([fixed] if fixed is not None else np.geomspace(0.03, 4.0, 240)):
        for tzv in tzs:
            A = []; b = []
            ok = True
            for p, o in zip(points, obs):
                z = sv * p[2] + tzv
                if z >= -0.02: ok = False; break
                # x_px = (0.5 + 0.5*(s*px+tx)*FX/(-z))*W  -> linear in tx; same for ty
                kx = 0.5 * FX / -z * W; ky = 0.5 * FY / -z * H
                A.append([kx, 0]); b.append(o[0] - (0.5 + 0.5 * sv * p[0] * FX / -z) * W)
                A.append([0, -ky]); b.append(o[1] - (0.5 - 0.5 * sv * p[1] * FY / -z) * H)
            if not ok: continue
            A = np.array(A); b = np.array(b)
            sol, *_ = np.linalg.lstsq(A, b, rcond=None)
            r = A @ sol - b
            rms = np.sqrt((r ** 2).mean())
            if best is None or rms < best[0]:
                best = (rms, sv, np.array([sol[0], sol[1], tzv]), -r)
    rms, sv, t, r = best
    return np.concatenate([[sv], t]), r


def main():
    extra = [a for a in sys.argv[1:] if '=' in a and not a.startswith(('tz=', 'rot=', 'fov=', 's=')) and a != 'real=1' and a != 'mode=idle']
    doc = sweep(BASE + extra); frames = doc['frames']
    idle = frames[0]; hold = min(frames, key=lambda f: abs(f['t'] - HOLD_T))
    _, tip0, pom0 = knife_points(idle); _, tip1, pom1 = knife_points(hold)
    print(f"sweep scale=1, no hand translate, {' '.join(extra) or 'defaults'}")
    print(f"  idle tip {np.round(tip0,3)} pommel {np.round(pom0,3)} | hold tip {np.round(tip1,3)} pommel {np.round(pom1,3)}")
    pts = [tip0, pom0, tip1, pom1]; obs = [np.array(OBS[k]) for k in ('idle_tip', 'idle_pommel', 'hold_tip', 'hold_pommel')]
    tz = next((float(a.split('=')[1]) for a in sys.argv[1:] if a.startswith('tz=')), None)
    # Optional extra rotation of the whole chain about a view axis, swept: does a
    # missing pitch/yaw/roll explain the residual?
    rot = next((a.split('=')[1] for a in sys.argv[1:] if a.startswith('rot=')), None)
    if rot:
        axis = {'pitch': 0, 'yaw': 1, 'roll': 2}[rot]
        results = []
        for deg in range(-45, 46, 3):
            a = math.radians(deg); c, si = math.cos(a), math.sin(a)
            if axis == 0: R = np.array([[1,0,0],[0,c,-si],[0,si,c]])
            elif axis == 1: R = np.array([[c,0,si],[0,1,0],[-si,0,c]])
            else: R = np.array([[c,-si,0],[si,c,0],[0,0,1]])
            xr, rr = fit([R @ q for q in pts], obs, tz)
            results.append((np.sqrt((rr**2).mean()), deg, xr))
        results.sort()
        print(f"  {rot} sweep (best 5): " + '; '.join(f"{d:+d}deg rms {e:.1f} s={xx[0]:.3f} tz={xx[3]:.2f}" for e, d, xx in results[:5]))
        return
    if any(a == 'mode=idle' for a in sys.argv[1:]):
        # idle only: tip and the guard (56% of the way from tip to pommel on the M9)
        guard0 = tip0 + 0.56 * (pom0 - tip0)
        pts = [tip0, guard0]; obs = [np.array(OBS['idle_tip']), np.array((1245, 837))]
        for tzv in ([tz] if tz is not None else [-0.9, -0.72, -0.6, -0.5, -0.4, -0.3, -0.2]):
            xr, rr = fit(pts, obs, tzv)
            print(f"  idle-only tz={tzv:+.2f}: s={xr[0]:.3f} t=({xr[1]:+.3f},{xr[2]:+.3f},{xr[3]:+.2f}) rms {np.sqrt((rr**2).mean()):.1f}px  whole knife {np.linalg.norm(tip0-pom0)*xr[0]:.3f} units")
        return
    x, r = fit(pts, obs, tz)
    s, t = x[0], x[1:]
    print(f"  FIT: scale s = {s:.4f}   translation t = ({t[0]:+.4f}, {t[1]:+.4f}, {t[2]:+.4f})")
    print(f"  residuals (px): idle tip {np.round(r[0:2],1)} idle pommel {np.round(r[2:4],1)}  hold tip {np.round(r[4:6],1)}  hold pommel {np.round(r[6:8],1)}  rms {np.sqrt((r**2).mean()):.1f}")
    for name, cand in [('none', (0, 0, 0)), ('MC hand (0.56,-0.52,-0.72)', (0.56, -0.52, -0.72))]:
        print(f"  vs {name}: |t - cand| = {np.linalg.norm(t - np.array(cand)):.3f}")
    # what the fit implies at idle for the pommel (not observed: hidden in the fist)
    print(f"  implied idle pommel px {np.round(project(s*pom0+t),0)}  (video fist centre ~ (1363,910))")
    # knife length check
    L = np.linalg.norm(tip1 - pom1) * s
    print(f"  implied knife length in view units: {L:.3f}  (CS:MC mesh in metres would be 0.389 x f)")


if __name__ == '__main__':
    main()
