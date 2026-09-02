"""Tests CS:MC's own first-person transform against the reference screenshots.

b$4lx (decompiled), in JOML column order:
    translate(-0.22, 0.42, -0.18); rotateX(90); rotateY(180); rotateZ(270); scale(f)
transposes to the row-vector order the mod already uses -- except the mod throws
the translation away and substitutes a screen anchor.  Because the scale is
uniform it factors out: p_view = f * (p_rig . R) + T, so one rig sample per knife
is enough to sweep f.
"""
import sys, os, math, json, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rigprobe as R
from armplan import GRIPS, LEFT_GRIPS

CSMC_T = np.array([-0.22, 0.42, -0.18])
ROT = R.rot_z(270) @ R.rot_y(180) @ R.rot_x(90)
REF = {'r': (0.700, 0.709), 'l': (0.392, 0.803)}

def proj(fov_deg, aspect=1920/1080.0):
    fy = 1.0 / math.tan(math.radians(fov_deg) / 2.0)
    return fy / aspect, fy

def rig_points():
    """Grip and forearm-root of both hands, in rig space, rotated but unscaled."""
    pts = {}
    for name in R.NAMES:
        rig = R.rig(name)
        a = rig.absolute('idle', 0.0)
        d = {}
        for side, g in (('r', GRIPS[name]), ('l', LEFT_GRIPS.get(name, (0, 0, 0)))):
            wrist = R.binding_matrix(rig, a, f'hand_{side}')
            d[side] = (R.xform(R.xform(g, wrist), ROT), R.xform(rig.binding_matrix_origin(a, f'arm_lower_{side}') if False
                                                               else R.binding_matrix(rig, a, f'arm_lower_{side}')[3, :3], ROT))
        pts[name] = d
    return pts

def screen(p, fx, fy):
    d = -p[2]
    return None if d <= 1e-4 else (p[0]*fx/d*0.5+0.5, 0.5-p[1]*fy/d*0.5)

PTS = rig_points()

def score(f, fx, fy):
    err, n = 0.0, 0
    for name in R.NAMES:
        for side in ('r', 'l'):
            p = f * PTS[name][side][0] + CSMC_T
            s = screen(p, fx, fy)
            if s is None: err += 4.0
            else: err += (s[0]-REF[side][0])**2 + (s[1]-REF[side][1])**2
            n += 1
    return err / n

print(f'{"fov":>6}{"best f":>10}{"rms":>9}')
best = None
grid = np.concatenate([np.linspace(0.005, 2.0, 400), np.linspace(2.0, 60.0, 600)])
for fov in (60, 65, 70, 75, 80, 90, 100, 110):
    fx, fy = proj(fov)
    e, f = min((score(f, fx, fy), f) for f in grid)
    print(f'{fov:6}{f:10.4f}{math.sqrt(e):9.4f}')
    if best is None or e < best[0]: best = (e, f, fov)

e, f, fov = best
fx, fy = proj(fov)
print(f'\nbest: fov={fov}  f={f:.4f}  rms={math.sqrt(e):.4f}')
print(f'{"knife":<12}{"right hand":>17}{"left hand":>17}{"r depth":>9}{"l depth":>9}{"r lean":>8}{"l lean":>8}')
aspect = fy / fx
for name in R.NAMES:
    row = f'{name:<12}'
    leans = []
    for side in ('r', 'l'):
        g, el = PTS[name][side]
        pg, pe = f * g + CSMC_T, f * el + CSMC_T
        s, se = screen(pg, fx, fy), screen(pe, fx, fy)
        row += (f'({s[0]:.3f},{s[1]:.3f})'.rjust(17) if s else '     behind eye'.rjust(17))
        leans.append(math.degrees(math.atan2((se[0]-s[0])*aspect, se[1]-s[1])) if s and se else float('nan'))
    for side in ('r', 'l'):
        row += f'{-(f * PTS[name][side][0] + CSMC_T)[2]:9.3f}'
    row += f'{leans[0]:8.1f}{leans[1]:8.1f}'
    print(row)
print(f'{"CS:MC ref":<12}{"(0.700,0.709)":>17}{"(0.392,0.803)":>17}{"":>18}{16.1:8.1f}{-58.1:8.1f}')
