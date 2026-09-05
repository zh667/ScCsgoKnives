"""Acceptance check of the exact chain against the CS:MC M9 video: blade tip and pommel
at idle, mid-inspect and the inspect hold, in pixels, versus the video's red-blade
measurements (1920x1084 frames at 5.0 s, 7.5 s and 8.2 s).

    python3 tools/videocheck.py [Key=Value overrides]
"""
import sys, os, json, math, numpy as np
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)
import holdcompare as HC, gripsolve as G
W, H = 1920, 1084
VIDEO = {  # (t into the inspect clip, video tip, video pommel, lean deg); video = clip + 6.70 s, crimson mask (skin excluded)
    'idle': (0.00, (740, 608), (1567, 1023), -63.2),
    'mid':  (0.80, (1423, 222), (1247, 1009), +9.7),
    'hold': (1.30, (1399, 204), (1265, 951), +7.2),
    'late': (1.90, (1421, 190), (1242, 969), +9.7),
}

def main():
    doc, tag = HC.run(); frames = doc['frames']   # HC filters 'search=1' out of the overrides? no: strip it here

    wfx, wfy = float(doc['weaponProjX']), float(doc['weaponProjY'])
    V = G.mesh_part('m9', 'weapon_hand_r')
    print(f"weapon projection fov {2*math.degrees(math.atan(1/wfy)):.1f} deg [{tag}]")
    worst = 0
    search = any(a == 'search=1' for a in sys.argv[1:])
    def landmarks(fr, vtip):
        M = np.array(fr['parts']['weapon_hand_r']).reshape(4, 4); P = (np.c_[V, np.ones(len(V))] @ M)[:, :3]
        z = -P[:, 2]; pts = np.c_[(0.5 + 0.5 * P[:, 0] * wfx / z) * W, (0.5 - 0.5 * P[:, 1] * wfy / z) * H]
        c = pts.mean(0); d = np.linalg.svd(pts - c, full_matrices=False)[2][0]
        proj = (pts - c) @ d; a, b = pts[np.argmax(proj)], pts[np.argmin(proj)]
        tip, pom = (a, b) if np.linalg.norm(a - vtip) < np.linalg.norm(b - vtip) else (b, a)
        dd = tip - pom; return tip, pom, math.degrees(math.atan2(dd[0], -dd[1]))
    for name, (t, vtip, vpom, vlean) in VIDEO.items():
        cands = [f for f in frames if abs(f['t'] - t) < (0.45 if search and t > 0 else 1e-6 + 1 / 60)]
        best = None
        for fr in cands:
            tip, pom, lean = landmarks(fr, np.array(vtip, float))
            et, ep = np.linalg.norm(tip - vtip), np.linalg.norm(pom - vpom)
            if best is None or et + ep < best[0]: best = (et + ep, fr, tip, pom, lean, et, ep)
        _, fr, tip, pom, lean, et, ep = best; worst = max(worst, et, ep)
        print(f"  {name:4s} t={fr['t']:.2f}: tip ({tip[0]:.0f},{tip[1]:.0f}) vs video {vtip} -> {et:5.1f} px | pommel ({pom[0]:.0f},{pom[1]:.0f}) vs {vpom} -> {ep:5.1f} px | lean {lean:+.1f} vs {vlean:+.1f}")
    print(f"  worst landmark error {worst:.1f} px")

if __name__ == '__main__':
    main()
