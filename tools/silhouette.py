"""Break the depth/FOV degeneracy of the chain fit with the whole blade silhouette.

For each weapon-FOV hypothesis, fit the residual eye-space translation on the four
tip/pommel observations (chainfit), then project every blade vertex of the idle and
hold frames and measure the overlap (IoU) with the red blade mask of the CS:MC video.

    python3 tools/silhouette.py [fov list, default 35 38 42 48 55 62] [Key=Value sweep overrides]
"""
import sys, os, json, math, numpy as np
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)
from PIL import Image
import chainfit as C, gripsolve as G
S = '/tmp/claude-1000/-home-dev/584a761a-993d-4689-a30e-a9182a581a55/scratchpad/vid'
W, H = 1920, 1084

def red_mask(name):
    im = np.array(Image.open(f'{S}/{name}').convert('RGB')).astype(int); r, g, b = im[..., 0], im[..., 1], im[..., 2]
    m = (r > 50) & (r > g + 35) & (r > b + 25)          # the crimson skin is dark: blade pixels are about (105, 2, 18)
    m[:320, :300] = False; m[900:, :1240] = False; m[380:620, 1640:] = False
    return m

def project(P, fov, t):
    fy = 1 / math.tan(math.radians(fov / 2)); fx = fy / (W / H)
    Q = P + t; z = -Q[:, 2]; ok = z > 0.01
    x = (0.5 + 0.5 * Q[:, 0] * fx / z) * W; y = (0.5 - 0.5 * Q[:, 1] * fy / z) * H
    return np.c_[x, y][ok]

def raster(pts, dil=3):
    m = np.zeros((H, W), bool)
    xi = np.clip(pts[:, 0].astype(int), 0, W - 1); yi = np.clip(pts[:, 1].astype(int), 0, H - 1)
    m[yi, xi] = True
    from scipy.ndimage import binary_dilation, binary_closing
    return binary_closing(binary_dilation(m, iterations=dil), iterations=2)

def main():
    fovs = [float(a) for a in sys.argv[1:] if '=' not in a] or [35, 38, 42, 48, 55, 62]
    extra = [a for a in sys.argv[1:] if '=' in a]
    C.REAL = True
    doc = C.sweep(['ExactChain=1', 'ExactScaleOverride=0', 'ExactHandX=0', 'ExactHandY=0', 'ExactHandZ=0', 'ExactMirrorX=0'] + extra)
    frames = doc['frames']; idle = frames[0]; hold = min(frames, key=lambda f: abs(f['t'] - C.HOLD_T))
    V = G.mesh_part('m9', 'weapon_hand_r')                     # normalized mesh vertices
    def world(fr): M = np.array(fr['parts']['weapon_hand_r']).reshape(4, 4); return (np.c_[V, np.ones(len(V))] @ M)[:, :3]
    P0, P1 = world(idle), world(hold)
    masks = {'idle': red_mask('mccs_5.0.png'), 'hold': red_mask('mccs_8.2.png')}
    _, t0, p0 = C.knife_points(idle); _, t1, p1 = C.knife_points(hold)
    pts = [t0, p0, t1, p1]; obs = [np.array(C.OBS[k], float) for k in ('idle_tip', 'idle_pommel', 'hold_tip', 'hold_pommel')]
    # blade = the tip half of the knife along its own axis in the normalized mesh (z is the long axis)
    axis_sign = 1 if np.linalg.norm(V[np.argmax(V[:, 2])] - V.mean(0)) > 0 else 1
    zc = V[:, 2]
    for fov in fovs:
        C.FOV = fov; C.FY = 1 / math.tan(math.radians(fov / 2)); C.FX = C.FY / (W / H)
        x, r = C.fit(pts, obs, None); t = x[1:]
        rms = math.sqrt((r ** 2).mean())
        best = None
        for side in (+1, -1):                                  # which end of the mesh is the tip
            sel = side * zc > 0.02
            ious = []
            for name, P, m in (('idle', P0, masks['idle']), ('hold', P1, masks['hold'])):
                ours = raster(project(P[sel], fov, t))
                inter = (ours & m).sum(); union = (ours | m).sum()
                ious.append(inter / max(union, 1))
            if best is None or sum(ious) > sum(best[1]): best = (side, ious)
        print(f"fov {fov:5.1f}: t=({t[0]:+.3f},{t[1]:+.3f},{t[2]:+.3f}) point-rms {rms:5.1f}px | blade IoU idle {best[1][0]:.3f} hold {best[1][1]:.3f} mean {np.mean(best[1]):.3f} (tip side z{'+' if best[0] > 0 else '-'})")

if __name__ == '__main__':
    main()
