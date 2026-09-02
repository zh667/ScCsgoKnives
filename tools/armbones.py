"""Where the rig's own forearm bones land on screen, per knife, at idle.

The reference numbers this compares against were measured off CS:MC screenshots
with tools/armstat.py; the bone numbers come straight out of the animation the
mod ships, so a match means the arms can be driven by data instead of constants.
"""
import sys, os, json, math, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rigprobe as R

FX, FY = 0.6401, 1.1918          # logged in-game, 80 deg vertical
ANCHOR_SX, ANCHOR_SY, ANCHOR_D = 0.7021, 0.7595, 0.72
KNIFE_SCALE = 0.74
ASPECT = FY / FX

def lean_deg(hand, elbow):
    """Angle of the hand->elbow direction from straight down the screen, in the
    same aspect-corrected convention KnifeTuning.RightArmLean uses."""
    dx = (elbow[0] - hand[0]) * ASPECT
    dy = elbow[1] - hand[1]
    return math.degrees(math.atan2(dx, dy))

anchor = R.to_view(ANCHOR_SX, ANCHOR_SY, ANCHOR_D, FX, FY)
rows = []
for name in R.NAMES:
    rig = R.rig(name)
    place = R.placement(rig, KNIFE_SCALE, anchor)
    a = rig.absolute('idle', 0.0)
    out = {'name': name}
    for side in ('r', 'l'):
        hand_m = R.binding_matrix(rig, a, f'hand_{side}') @ place
        elbow_m = R.binding_matrix(rig, a, f'arm_lower_{side}') @ place
        h, e = hand_m[3, :3], elbow_m[3, :3]
        hs, es = R.to_screen(h, FX, FY), R.to_screen(e, FX, FY)
        out[side] = {
            'hand_view': h, 'elbow_view': e,
            'hand_screen': hs, 'elbow_screen': es,
            'forearm_len': float(np.linalg.norm(e - h)),
            'lean': lean_deg(hs, es) if hs and es else None,
            'hand_depth': -h[2], 'elbow_depth': -e[2],
        }
    rows.append(out)

MCCS = {'r': dict(lean=16.1, hand=(0.700, 0.709)), 'l': dict(lean=-58.1, hand=(0.392, 0.803))}
for side, label in (('r', 'RIGHT'), ('l', 'LEFT')):
    print(f'=== {label}  (CS:MC reference: lean {MCCS[side]["lean"]:+.1f}, hand {MCCS[side]["hand"]}) ===')
    print(f'{"knife":<12}{"hand sx":>9}{"hand sy":>9}{"elbow sx":>10}{"elbow sy":>10}{"lean":>8}{"forearm":>9}{"depth":>8}')
    for r in rows:
        d = r[side]
        hs, es = d['hand_screen'], d['elbow_screen']
        f = lambda v: f'{v:9.3f}' if v is not None else '      n/a'
        print(f'{r["name"]:<12}{f(hs[0] if hs else None)}{f(hs[1] if hs else None)}'
              f'{(f"{es[0]:10.3f}" if es else "       n/a")}{(f"{es[1]:10.3f}" if es else "       n/a")}'
              f'{(f"{d["lean"]:8.1f}" if d["lean"] is not None else "     n/a")}'
              f'{d["forearm_len"]:9.3f}{d["hand_depth"]:8.3f}')
    leans = [r[side]['lean'] for r in rows if r[side]['lean'] is not None]
    hx = [r[side]['hand_screen'][0] for r in rows if r[side]['hand_screen']]
    hy = [r[side]['hand_screen'][1] for r in rows if r[side]['hand_screen']]
    fl = [r[side]['forearm_len'] for r in rows]
    print(f'  mean lean {np.mean(leans):+.1f} (sd {np.std(leans):.1f})   '
          f'mean hand ({np.mean(hx):.3f}, {np.mean(hy):.3f}) sd ({np.std(hx):.3f}, {np.std(hy):.3f})   '
          f'mean forearm {np.mean(fl):.3f} (sd {np.std(fl):.3f})')
    print()
