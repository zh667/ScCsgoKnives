"""Solve each knife's grip position directly against the CS:MC screenshots.

The one number a screenshot gives per knife with no ambiguity is the distance
from the (known, pinned) hand position to the blade tip.  Sliding the grip along
the knife's own axis changes that distance on screen; so does the global scale.
Solve the scale on the M9's independently-measured blade length, then slide each
knife's grip until its hand-to-tip distance matches its own reference photo.
The grip is kept on the knife by snapping it to the centroid of the mesh slab at
that axial position, so the fist always closes on the handle itself.
"""
import sys, os, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rigprobe as R
import verify_cs as V

HAND = np.array([0.700, 0.709])
ASPECT_Y = 1080.0 / 1920.0            # converts y-fractions to width units

def wdist(a, b):
    return float(np.hypot(a[0] - b[0], (a[1] - b[1]) * ASPECT_Y))

import json
MANIFEST = {e['Name']: e['MeshParts'] for e in json.load(open('src/ScCsgoKnives/AnimationData/knives.json'))}

def mesh_part(name, part):
    P = []
    with open(f'src/ScCsgoKnives/Assets/Models/ScCsgoKnives/{name}_{part}.obj') as fh:
        for line in fh:
            if line.startswith('v '): P.append([float(x) for x in line.split()[1:4]])
    return np.array(P)

class Knife:
    def __init__(self, name):
        self.name = name
        self.rig = R.rig(name); self.a = self.rig.absolute('idle', 0.0)
        self.hr = R.binding_matrix(self.rig, self.a, 'hand_r')
        # The butterfly's blade is its own mesh part with its own binding; a grip
        # solved against one handle alone thinks the knife has no blade at all.
        parts = []
        for part in MANIFEST[name]:
            wb = R.binding_matrix(self.rig, self.a, part)
            Vm = mesh_part(name, part)
            parts.append((np.c_[Vm[::4], np.ones(len(Vm[::4]))] @ wb)[:, :3])   # subsampled; the extremes survive 1-in-4
        self.P = np.concatenate(parts)                              # rig-pose space
        c = self.P.mean(0)
        _, _, vt = np.linalg.svd(self.P - c, full_matrices=False)
        ax = vt[0]
        t = (self.P - c) @ ax
        lo, hi = t.min(), t.max()
        # orient the axis so t=0 is the butt (the end nearer the wrist at rest)
        wrist = self.hr[3, :3]
        if np.linalg.norm(c + ax * hi - wrist) < np.linalg.norm(c + ax * lo - wrist):
            ax, t, lo, hi = -ax, -t, -hi, -lo
        self.c, self.ax, self.t, self.lo, self.hi = c, ax, t, lo, hi

    def grip_at(self, f):
        """Mesh-slab centroid at axial fraction f (0 = butt, 1 = tip), hand-local."""
        target = self.lo + f * (self.hi - self.lo)
        half = 0.04 * (self.hi - self.lo)
        for _ in range(8):
            sel = np.abs(self.t - target) < half
            if sel.sum() >= 25: break
            half *= 1.6
        world = self.P[sel].mean(0)
        return R.xform(world, np.linalg.inv(self.hr)), world

    def screen_geom(self, f, k, pitch=0.0, yaw=0.0):
        V.C['KnifeScale'] = k
        grip_local, _ = self.grip_at(f)
        s = k * self.rig.ref_scale / V.C['ReferenceSourceScale']
        o = (R.scale([s]*3) @ R.rot_z(270) @ R.rot_y(180) @ R.rot_x(90)
             @ R.rot_x(pitch) @ R.rot_y(yaw))        # view-space, about the grip once pinned
        place = o @ R.translation(V.ANCHOR - R.xform(grip_local, self.hr @ o))
        pts = [V.screen(p) for p in (np.c_[self.P, np.ones(len(self.P))] @ place)[:, :3]]
        good = [p for p in pts if p is not None]
        if len(good) < len(pts) * 0.9:                 # rotated behind the eye: reject
            return dict(tip=np.array([9.0, 9.0]), d_tip=9.0, extent=9.0)
        S = np.array(good)
        d = np.array([wdist(p, HAND) for p in S])
        j = int(np.argmax(d))
        # blade length: extent from the guardless measure -- distance from the
        # on-screen grip to the tip minus nothing; report tip info and extent
        c = S.mean(0)
        _, _, vt = np.linalg.svd(S - c, full_matrices=False)
        proj = (S - c) @ vt[0]
        return dict(tip=S[j], d_tip=float(d[j]), extent=float(proj.max() - proj.min()))

V.PX, V.PY = 0.6704, 1.1918
V.ANCHOR = V.to_view(V.C['AnchorScreenX'], V.C['AnchorScreenY'], V.C['AnchorDepth'])

def solve(pitch, yaw, refs, knives, verbose=False):
    """k, per-knife f at a given global rotation; returns tip errors too."""
    m9 = knives['m9']
    k, f = 0.6, 0.3
    for _ in range(20):
        g = m9.screen_geom(f, k, pitch, yaw)
        k *= refs['m9']['extent'] / max(g['extent'], 1e-6)
        g = m9.screen_geom(f, k, pitch, yaw)
        err = refs['m9']['d_tip'] - g['d_tip']
        f = min(0.9, max(0.02, f - err / (m9.hi - m9.lo) * 0.5))
    fs = {'m9': f}
    for name in ('karambit', 'butterfly', 'tactical'):
        kn = knives[name]
        lo_f, hi_f = 0.02, 0.9
        for _ in range(28):
            mid = 0.5 * (lo_f + hi_f)
            if kn.screen_geom(mid, k, pitch, yaw)['d_tip'] > refs[name]['d_tip']: lo_f = mid
            else: hi_f = mid
        fs[name] = 0.5 * (lo_f + hi_f)
    err = 0.0
    for name, f_n in fs.items():
        g = knives[name].screen_geom(f_n, k, pitch, yaw)
        e = wdist(g['tip'], refs[name]['tip'])
        err += e * e
        if verbose:
            print(f"  {name:<10} f={f_n:.3f}  d_tip {g['d_tip']:.3f}/{refs[name]['d_tip']:.3f}  "
                  f"tip ({g['tip'][0]:.3f},{g['tip'][1]:.3f}) ref ({refs[name]['tip'][0]:.3f},{refs[name]['tip'][1]:.3f})  err {e:.3f}")
    return k, fs, err

if __name__ == '__main__':
    refs = eval(open('.tmp-cmp/refs.py').read())        # measured from the photos
    knives = {n: Knife(n) for n in ('m9', 'karambit', 'butterfly', 'tactical')}
    best = None
    for pitch in range(-70, 21, 10):
        for yaw in range(-40, 41, 10):
            k, fs, err = solve(float(pitch), float(yaw), refs, knives)
            if best is None or err < best[2]: best = (float(pitch), float(yaw), err)
    pitch, yaw = best[0], best[1]
    print(f'coarse best: pitch {pitch:+.0f} yaw {yaw:+.0f} err {best[2]:.4f}')
    for step in (5.0, 2.0, 1.0):
        cands = [(pitch+dp, yaw+dy) for dp in (-step,0,step) for dy in (-step,0,step)]
        scored = [(solve(p_, y_, refs, knives)[2], p_, y_) for p_, y_ in cands]
        _, pitch, yaw = min(scored)
    k, fs, err = solve(pitch, yaw, refs, knives, verbose=True)
    print(f'\nKnifePitch = {pitch:.1f}  KnifeYaw = {yaw:.1f}  KnifeScale = {k:.3f}  sum tip err {err:.4f}')
    for name, f_n in fs.items():
        g, _ = knives[name].grip_at(f_n)
        print(f'        new({g[0]:.4f}f, {g[1]:.4f}f, {g[2]:.4f}f),'.ljust(44) + f'// {name}  (f={f_n:.2f})')
