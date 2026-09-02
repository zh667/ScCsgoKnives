"""Predicts the arm the rebuilt DrawArm would draw, and scores it against CS:MC.

Direction comes from the rig's own arm_lower -> hand bones (per knife, per frame,
which is what CS:MC does) instead of a single fitted lean.  Width and overshoot
come from CS:MC's own arm box: assets/csmcmod/geckolib/models/source2_arms.geo.json
is a 3 x 12 x 4 pixel cube pivoted at [+-6, 12, 0], so the box is a quarter of its
length wide, a third deep, and reaches 2/12 of its length past the attachment point.
"""
import sys, os, math, json, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rigprobe as R

FX, FY = 0.6401, 1.1918
ASPECT = FY / FX
ANCHOR = R.to_view(0.7021, 0.7595, 0.72, FX, FY)
KNIFE_SCALE = 0.74

# source2_arms.geo.json, RightArm: pivot [-6,12,0], cube origin [-8,2,-2] size [3,12,4]
ARM_W_OVER_L = 3.0 / 12.0
ARM_D_OVER_L = 4.0 / 12.0
ARM_OVERSHOOT = 2.0 / 12.0          # box top sits 2px above the pivot
EXIT_Y = 1.15                       # march until the arm has left the frame

GRIPS = {   # kept in step with s_gripOffsets in CsmcFirstPersonRenderer.cs
 'karambit':(0.0471,0.0544,-0.0904),
 'm9':(0.2157,0.0669,-0.3250),
 'butterfly':(0.1953,0.0845,-0.2164),
 'bayonet':(0.2615,0.0698,-0.2780),
 'bowie':(0.1736,0.0795,-0.2732),
 'canis':(0.1873,0.0590,-0.2479),
 'cord':(0.2282,0.0698,-0.3027),
 'css':(0.2721,0.0610,-0.1989),
 'default_ct':(0.1994,0.0710,-0.2525),
 'default_t':(0.2508,0.0725,-0.1787),
 'falchion':(0.1979,0.1020,-0.2768),
 'flip':(0.2424,0.0811,-0.2460),
 'gut':(0.1466,0.0717,-0.2382),
 'kukri':(0.6512,0.5096,-0.9796),
 'navaja':(0.2191,0.1114,-0.2745),
 'outdoor':(0.1805,0.0797,-0.2563),
 'push':(0.7868,0.1805,-0.0681),
 'skeleton':(0.2553,0.0797,-0.2585),
 'stiletto':(0.2253,0.0935,-0.2796),
 'tactical':(0.1502,0.0630,-0.2564),
 'talon':(0.0364,0.0450,-0.2201),
 'ursus':(0.2011,0.0731,-0.2593)}
LEFT_GRIPS = {'push': (0.5970, 0.1456, -0.2224)}

def screen(p):
    d = -p[2]
    return None if d <= 1e-4 else np.array([p[0]*FX/d*0.5+0.5, 0.5-p[1]*FY/d*0.5])

MIN_DEPTH = 0.12

def off_frame(p):
    """True once the point is past the bottom edge or too close to the eye."""
    d = -p[2]
    if d <= MIN_DEPTH: return True
    s = screen(p)
    return s is None or s[1] >= EXIT_Y

def march_to_exit(grip, direction, start_len):
    """Longest run down the forearm that is still in front of the eye and on frame.

    A real forearm carries on past the eye towards the shoulder, so the far end
    being behind the camera is normal, not a failure -- the arm just has to be
    cut where it leaves the view instead of being abandoned."""
    lo, hi = 0.0, max(start_len, 0.05)
    if not off_frame(grip + direction * hi):
        for _ in range(40):                     # still on frame: push further out
            lo, hi = hi, hi * 1.5 + 0.05
            if off_frame(grip + direction * hi): break
        else:
            return hi
    for _ in range(50):
        mid = 0.5 * (lo + hi)
        if off_frame(grip + direction * mid): hi = mid
        else: lo = mid
    return lo

def report():
    rows = []
    for name in R.NAMES:
        rig = R.rig(name)
        place = R.placement(rig, KNIFE_SCALE, ANCHOR, GRIPS[name])
        a = rig.absolute('idle', 0.0)
        row = {'name': name}
        for side, key in (('r', GRIPS[name]), ('l', LEFT_GRIPS.get(name, (0, 0, 0)))):
            wrist = R.binding_matrix(rig, a, f'hand_{side}') @ place
            elbow_m = R.binding_matrix(rig, a, f'arm_lower_{side}') @ place
            grip = R.xform(key, wrist)
            elbow = elbow_m[3, :3]
            span = elbow - grip
            forearm = float(np.linalg.norm(span))
            if forearm < 0.05:
                row[side] = {'degenerate': True, 'forearm': forearm}
                continue
            d = span / forearm
            reach = march_to_exit(grip, d, forearm)
            hs, es = screen(grip), screen(grip + d * reach)
            if hs is None or es is None:
                row[side] = {"degenerate": True, "forearm": forearm, "why": "behind eye"}
                continue
            lean = math.degrees(math.atan2((es[0]-hs[0])*ASPECT, es[1]-hs[1]))
            # on-screen width of the box at the hand end
            width_view = forearm * ARM_W_OVER_L
            depth = -grip[2]
            row[side] = {'forearm': round(forearm, 3), 'reach': round(reach, 3),
                         'hand': [round(float(hs[0]), 3), round(float(hs[1]), 3)],
                         'exit': [round(float(es[0]), 3), round(float(es[1]), 3)],
                         'lean': round(lean, 1),
                         'width_screen': round(width_view * FX / depth, 4),
                         'depth': round(depth, 3)}
        rows.append(row)

    MCCS = {'r': (16.1, (0.700, 0.709), 0.0828), 'l': (-58.1, (0.392, 0.803), 0.0694)}
    for side, label in (('r', 'RIGHT'), ('l', 'LEFT')):
        ref_lean, ref_hand, ref_w = MCCS[side]
        print(f'=== {label}  CS:MC: lean {ref_lean:+.1f}  hand {ref_hand}  width {ref_w:.3f} ===')
        print(f'{"knife":<12}{"hand":>16}{"exit":>16}{"lean":>8}{"width":>8}{"forearm":>9}{"reach":>8}')
        ok = [r for r in rows if not r[side].get('degenerate')]
        for r in rows:
            d = r[side]
            if d.get('degenerate'):
                print(f'{r["name"]:<12}  DEGENERATE forearm={d["forearm"]:.3f} {d.get("why","")}'); continue
            print(f'{r["name"]:<12}{str(d["hand"]):>16}{str(d["exit"]):>16}{d["lean"]:8.1f}'
                  f'{d["width_screen"]:8.3f}{d["forearm"]:9.3f}{d["reach"]:8.2f}')
        L = [r[side]['lean'] for r in ok]
        HX = [r[side]['hand'][0] for r in ok]; HY = [r[side]['hand'][1] for r in ok]
        W = [r[side]['width_screen'] for r in ok]
        print(f'  mean lean {np.mean(L):+.1f} (sd {np.std(L):.1f}) vs {ref_lean:+.1f}   '
              f'hand ({np.mean(HX):.3f},{np.mean(HY):.3f}) vs {ref_hand}   '
              f'width {np.mean(W):.3f} vs {ref_w:.3f}\n')


if __name__ == '__main__':
    report()
