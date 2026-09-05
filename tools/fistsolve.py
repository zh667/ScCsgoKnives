"""Fit each knife's on-screen pose to its CS:MC photo by silhouette, then fit the fist.

Unlike gripsolve (tip distance only), this matches the whole visible knife
silhouette -- a symmetric Chamfer distance between the projected mesh points and
the photo's knife pixels -- so the handle's foreshortening is constrained too,
not just where the tip lands.  The grip G (where the handle crosses the fist's
axis) is measured off the photo per knife and held fixed; free per knife are the
scale k and the axial fraction f of the mesh that sits at G; shared are the
composition pitch and yaw.
"""
import sys, os, json, math, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rigprobe as R
import verify_cs as V
import gripsolve as G
import mccs_masks as M

W, H = 1920, 1080
# Measured off the photos (px): G = handle line x fist axis; F = fist cap centre;
# w = cap width; lean0 = rough axis lean (deg, + = right going down)
REF = {
    'm9':        dict(G=(1363, 910), F=(1342, 747), w=178, lean0=8),
    'karambit':  dict(G=(1237, 877), F=(1147, 745), w=170, lean0=33),
    'butterfly': dict(G=(1389, 871), F=(1375, 778), w=180, lean0=8),
    'tactical':  dict(G=(1387, 910), F=(1370, 778), w=177, lean0=8),
}
DEPTH = V.C['AnchorDepth']

def frac(p): return (p[0] / W, p[1] / H)

class Target:
    def __init__(self, name):
        im = M.load(name)
        sky, arm, hud, knife = M.masks(im)
        self.arm = arm
        kc = M.clean(knife[::2, ::2], 20)
        ys, xs = np.nonzero(kc)
        pts = np.c_[xs * 2.0, ys * 2.0]
        rng = np.random.default_rng(0)
        self.mask_pts = pts[rng.choice(len(pts), min(350, len(pts)), replace=False)]
        self.right_arm = M.right_arm_region(arm)

class Fit:
    def __init__(self, name):
        self.name = name
        self.k = G.Knife(name)
        self.t = Target(name)
        self.anchor = V.to_view(*frac(REF[name]['G']), DEPTH)
        self.P = self.k.P[::2]
        self.Ph = np.c_[self.P, np.ones(len(self.P))]

    def project(self, f, k, pitch, yaw):
        kn = self.k
        grip_local, _ = kn.grip_at(f)
        s = k * kn.rig.ref_scale / V.C['ReferenceSourceScale']
        o = (R.scale([s]*3) @ R.rot_z(270) @ R.rot_y(180) @ R.rot_x(90) @ R.rot_x(pitch) @ R.rot_y(yaw))
        place = o @ R.translation(self.anchor - R.xform(grip_local, kn.hr @ o))
        pv = (self.Ph @ place)[:, :3]
        d = -pv[:, 2]
        ok = d > 1e-3
        sx = (pv[:, 0] * V.PX / np.where(ok, d, 1) * 0.5 + 0.5) * W
        sy = (0.5 - pv[:, 1] * V.PY / np.where(ok, d, 1) * 0.5) * H
        return np.c_[sx, sy], ok

    def cost(self, f, k, pitch, yaw):
        S, ok = self.project(f, k, pitch, yaw)
        if ok.mean() < 0.9: return 1e6
        S = S[ok]
        ix = np.clip(S[:, 0].astype(int), 0, W-1); iy = np.clip(S[:, 1].astype(int), 0, H-1)
        onscreen = (S[:, 0] >= 0) & (S[:, 0] < W) & (S[:, 1] >= 0) & (S[:, 1] < H)
        hidden = self.t.arm[iy, ix] | ~onscreen
        Sv = S[~hidden]
        if len(Sv) < 50: return 1e6
        D = np.sqrt(((Sv[:, None, :] - self.t.mask_pts[None, :, :]) ** 2).sum(-1))
        fwd = D.min(1).mean()                # rendered visible -> photo
        Dall = np.sqrt(((S[:, None, :] - self.t.mask_pts[None, :, :]) ** 2).sum(-1))
        back = Dall.min(0).mean()            # photo -> rendered (any, incl. behind the fist)
        return fwd + back

    def best_kf(self, pitch, yaw, k0=None, f0=None):
        ks = np.linspace(0.4, 1.3, 7) if k0 is None else k0 * np.linspace(0.85, 1.15, 5)
        fs = np.linspace(0.05, 0.55, 6) if f0 is None else np.clip(f0 + np.linspace(-0.08, 0.08, 5), 0.02, 0.9)
        best = (1e9, None, None)
        for k in ks:
            for f in fs:
                c = self.cost(f, k, pitch, yaw)
                if c < best[0]: best = (c, k, f)
        return best

def refine(fit, pitch, yaw, k, f, steps=((0.05, 0.03), (0.02, 0.012), (0.008, 0.005))):
    best = (fit.cost(f, k, pitch, yaw), k, f)
    for dk, df in steps:
        improved = True
        while improved:
            improved = False
            for kk, ff in ((best[1]+dk, best[2]), (best[1]-dk, best[2]), (best[1], best[2]+df), (best[1], best[2]-df)):
                if kk <= 0.1 or not (0.02 <= ff <= 0.92): continue
                c = fit.cost(ff, kk, pitch, yaw)
                if c < best[0] - 1e-6: best, improved = (c, kk, ff), True
    return best

if __name__ == '__main__':
    names = ['m9', 'karambit', 'butterfly', 'tactical']
    fits = {n: Fit(n) for n in names}
    # coarse: shared pitch/yaw grid, per knife best (k,f)
    results = []
    for pitch in range(-55, 6, 15):
        for yaw in range(-30, 61, 15):
            tot = 0.0; per = {}
            for n in names:
                c, k, f = fits[n].best_kf(float(pitch), float(yaw))
                tot += c; per[n] = (c, k, f)
            results.append((tot, pitch, yaw, per))
            print(f'pitch {pitch:+d} yaw {yaw:+d}: total {tot:7.2f}  ' + '  '.join(f"{n}={per[n][0]:.1f}" for n in names), flush=True)
    results.sort(key=lambda r: r[0])
    tot, pitch, yaw, per = results[0]
    print(f'\ncoarse best pitch {pitch:+d} yaw {yaw:+d} total {tot:.2f}')
    # refine pitch/yaw at 5 then 2 degrees with per-knife local (k,f)
    state = {n: (per[n][1], per[n][2]) for n in names}
    for step in (5.0, 2.0, 1.0):
        improved = True
        while improved:
            improved = False
            for dp, dy in ((step,0),(-step,0),(0,step),(0,-step)):
                p2, y2 = pitch+dp, yaw+dy
                t2 = 0.0; s2 = {}
                for n in names:
                    c, k, f = refine(fits[n], p2, y2, *state[n], steps=((0.03, 0.02), (0.01, 0.007)))
                    t2 += c; s2[n] = (k, f)
                if t2 < tot - 1e-4:
                    tot, pitch, yaw, state, improved = t2, p2, y2, s2, True
                    print(f'  refine -> pitch {pitch:+.0f} yaw {yaw:+.0f} total {tot:.2f}', flush=True)
    final = {}
    for n in names:
        c, k, f = refine(fits[n], pitch, yaw, *state[n])
        final[n] = dict(cost=c, k=k, f=f)
        print(f'{n:<10} k={k:.3f} f={f:.3f} chamfer={c:.2f}px')
    out = dict(pitch=pitch, yaw=yaw, knives=final, ref={n: REF[n] for n in names})
    json.dump(out, open('.tmp-fist/knifefit.json', 'w'), indent=1)
    print(f'\nKnifePitch {pitch:+.0f} KnifeYaw {yaw:+.0f}; written .tmp-fist/knifefit.json')
