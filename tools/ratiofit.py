"""Fits the one parameter that actually controls the composition's perspective.

Pinning the grip to a screen anchor at depth D and scaling the rig by S is
degenerate in (S, D) -- doubling both reproduces the same image exactly.  Only
S/D matters, and it sets how much of the frame the rig spreads across: how far
the forearms lean, where the left hand lands, how long the knife looks.  So it
is one number, fitted against four measurements taken off the CS:MC references.
"""
import sys, os, math, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rigprobe as R
from armplan import GRIPS, LEFT_GRIPS

FX, FY = 0.6401, 1.1918                     # in-game, logged
ASPECT = FY / FX
ANCHOR_SX, ANCHOR_SY = 0.7021, 0.7595
DEPTH = 0.72                                # held fixed; only scale/depth matters
REF_R_LEAN, REF_L_LEAN = 16.1, -58.1
REF_L_HAND = (0.392, 0.803)
REFERENCE_SOURCE_SCALE = 13.618

def screen(p):
    d = -p[2]
    return None if d <= 1e-4 else np.array([p[0]*FX/d*0.5+0.5, 0.5-p[1]*FY/d*0.5])

anchor = R.to_view(ANCHOR_SX, ANCHOR_SY, DEPTH, FX, FY)

CACHE = {}
for name in R.NAMES:
    rig = R.rig(name)
    a = rig.absolute('idle', 0.0)
    CACHE[name] = (rig.ref_scale,
                   R.binding_matrix(rig, a, 'hand_r'), R.binding_matrix(rig, a, 'arm_lower_r'),
                   R.binding_matrix(rig, a, 'hand_l'), R.binding_matrix(rig, a, 'arm_lower_l'))

def compose(name, knife_scale):
    ref, hr, ar, hl, al = CACHE[name]
    s = knife_scale * ref / REFERENCE_SOURCE_SCALE
    orientation = R.scale([s]*3) @ R.rot_z(270) @ R.rot_y(180) @ R.rot_x(90)
    idle_grip = R.xform(GRIPS[name], hr @ orientation)
    place = orientation @ R.translation(anchor - idle_grip)
    out = {}
    for side, g, hm, am in (('r', GRIPS[name], hr, ar), ('l', LEFT_GRIPS.get(name, (0,0,0)), hl, al)):
        grip = R.xform(g, hm @ place)
        elbow = (am @ place)[3, :3]
        out[side] = (grip, elbow)
    return out

def lean_of(grip, elbow):
    a, b = screen(grip), screen(elbow)
    if a is None: return None
    if b is None:                                   # elbow past the eye: use direction
        d = elbow - grip
        eps = grip + d * (0.2 * (-grip[2]) / max(1e-3, abs(d[2])) if d[2] > 0 else 0.3)
        b = screen(eps)
        if b is None: return None
    return math.degrees(math.atan2((b[0]-a[0])*ASPECT, b[1]-a[1]))

def score(ks):
    e, n = 0.0, 0
    for name in R.NAMES:
        if name == 'kukri': continue                # known-degenerate bones
        c = compose(name, ks)
        rl = lean_of(*c['r']); ll = lean_of(*c['l']); lh = screen(c['l'][0])
        if rl is None or ll is None or lh is None: e += 10; n += 1; continue
        e += ((rl - REF_R_LEAN) / 20.0) ** 2
        e += ((ll - REF_L_LEAN) / 20.0) ** 2
        e += ((lh[0] - REF_L_HAND[0]) / 0.06) ** 2 + ((lh[1] - REF_L_HAND[1]) / 0.06) ** 2
        n += 1
    return e / max(n, 1)

print(f'{"knifeScale":>11}{"score":>9}{"r lean":>9}{"l lean":>9}{"l hand x":>10}{"l hand y":>10}')
best = None
for ks in np.linspace(0.02, 1.6, 80):
    s = score(ks)
    rl, ll, lx, ly = [], [], [], []
    for name in R.NAMES:
        if name == 'kukri': continue
        c = compose(name, ks)
        a, b, h = lean_of(*c['r']), lean_of(*c['l']), screen(c['l'][0])
        if a is not None: rl.append(a)
        if b is not None: ll.append(b)
        if h is not None: lx.append(h[0]); ly.append(h[1])
    if best is None or s < best[0]: best = (s, ks)
    if abs(ks*100 % 10) < 2.1 or ks < 0.06:
        print(f'{ks:11.3f}{s:9.2f}{np.mean(rl):9.1f}{np.mean(ll):9.1f}{np.mean(lx):10.3f}{np.mean(ly):10.3f}')
print(f'\nbest knifeScale = {best[1]:.3f} (score {best[0]:.2f});  current shipped = 0.740')
print(f'target: r lean {REF_R_LEAN:+.1f}, l lean {REF_L_LEAN:+.1f}, l hand {REF_L_HAND}')
