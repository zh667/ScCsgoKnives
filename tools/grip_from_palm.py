"""Regenerates the per-knife grip point by snapping it onto the handle.

The old rule (tools/grip_offsets.py) took the knife vertices nearest the wrist and
then pushed the point back a hand-picked 0.15 along the handle. That constant is
not a measurement, and it walked the grip off the mesh on seventeen of the
twenty-two knives -- worst on the M9, where it ended up a tenth of the knife's
length below the blade, so the arm was drawn holding empty air.

Instead: seed from the palm, which the rig gives us for free (the four finger-base
bones), then take the centroid of the mesh inside a small ball around it. That
lands inside the handle's cross-section by construction, for every knife, with no
tunable in the loop. Knives whose finger bones are degenerate (the kukri) fall
back to their previous grip as the seed.
"""
import sys, os, json, numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rigprobe as R
from armplan import GRIPS

PALM = ['finger_index_0_r', 'finger_middle_0_r', 'finger_ring_0_r', 'finger_pinky_0_r']
BALL = 0.10          # of the knife's own length
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

def mesh(name):
    P = []
    with open(os.path.join(ROOT, f'src/ScCsgoKnives/Assets/Models/ScCsgoKnives/{name}_weapon_hand_r.obj')) as fh:
        for line in fh:
            if line.startswith('v '): P.append([float(x) for x in line.split()[1:4]])
    return np.array(P)

def solve(name):
    rig = R.rig(name); a = rig.absolute('idle', 0.0)
    hr = R.binding_matrix(rig, a, 'hand_r')
    wb = R.binding_matrix(rig, a, 'weapon_hand_r')
    V = mesh(name)
    P = (np.c_[V, np.ones(len(V))] @ wb)[:, :3]
    L = float(np.linalg.norm(P.max(0) - P.min(0)))
    palm = np.mean([R.binding_matrix(rig, a, f)[3, :3] for f in PALM], axis=0)
    old = R.xform(GRIPS[name], hr)
    seed, why = (palm, 'palm') if np.min(np.linalg.norm(P - palm, axis=1)) < 0.15 * L else (old, 'previous grip')
    r = BALL * L
    for _ in range(8):
        sel = np.linalg.norm(P - seed, axis=1) < r
        if sel.sum() >= 30: break
        r *= 1.5
    grip_world = P[sel].mean(0) if sel.sum() else seed
    local = R.xform(grip_world, np.linalg.inv(hr))
    moved = float(np.linalg.norm(grip_world - old)) / L
    return local, why, moved, float(np.min(np.linalg.norm(P - grip_world, axis=1))) / L

if __name__ == '__main__':
    print(f'{"knife":<12}{"seed":>14}{"moved":>8}{"dist to mesh":>14}')
    out = []
    for name in R.NAMES:
        g, why, moved, dist = solve(name)
        out.append((name, g))
        print(f'{name:<12}{why:>14}{moved:8.3f}{dist:14.4f}')
    print()
    for name, g in out:
        print(f'        new({g[0]:.4f}f, {g[1]:.4f}f, {g[2]:.4f}f),'.ljust(44) + f'// {name}')
