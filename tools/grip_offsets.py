"""Derive the per-knife grip point, expressed in the animated hand_r frame.

CS:MC ends the arm at `norm * bindingMatrix(hand_r) * asset.staticOffset`, not
at the binding origin (decompiled b$4ls).  That static offset is per weapon and
we cannot read it out of the obfuscated jar, so we recover the equivalent point
from our own meshes: the cluster of knife vertices closest to the wrist bone is
the grip.  Re-run this whenever a knife model is replaced.
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import json
from fpsim import *
from fprender import knife_models

SETBACK = 0.15   # push the grip back along the handle, away from the guard

NAMES = [k["name"] for k in json.load(open(os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))), ".tmp-csmc/installed.json")))]

for name in NAMES:
    rig = Rig(name)
    pose = rig.pose("idle", 0.0)
    hand = pose["bindings"]["hand_r"]
    inv = invert(hand)
    origin = (hand[12], hand[13], hand[14])
    local = []
    for binding, (verts, faces) in knife_models(name):
        m = mul(pose["bindings"][binding], inv)   # mesh -> hand_r local frame
        local.extend(xform(v, m) for v in verts)
    local.sort(key=lambda p: dot(p, p))
    cluster = local[:max(1, len(local)//50)]      # closest 2% of vertices
    n = len(cluster)
    grip = tuple(sum(p[i] for p in cluster)/n for i in range(3))
    # step back along the handle: away from the point of the blade
    world = [xform(p, hand) for p in local]
    origin = xform(grip, hand)
    tip = max(world, key=lambda p: length(vsub(p, origin)))
    back = norm(vsub(origin, tip))
    grip = xform(vadd(origin, vmul(back, SETBACK)), inv)
    print(f"        new({grip[0]:.4f}f, {grip[1]:.4f}f, {grip[2]:.4f}f),".ljust(44) + f"// {name}")
