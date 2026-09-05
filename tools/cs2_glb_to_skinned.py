#!/usr/bin/env python3
"""CS2's arms and gloves -> a skinned mesh the mod can CPU-skin each frame.

Survivalcraft has no GPU skinning, so the arms are transformed on the CPU and
handed to Display.DrawUserIndexed. This writes the one asset that needs:
positions, normals, UVs, four joint indices and four weights per vertex, the
joint list, and each joint's inverse bind matrix already scaled to the rig's
inches, so the runtime does

    view = sum_j w_j * (vertex * inverseBind_j * boneAbsolute_j)

Why that is exactly right, measured rather than assumed: 48 of the 55 bone
segments the arms GLB and the animation DMX share agree to 0.1 % in length, so
the two are the same skeleton and the joint frames coincide. The seven that do
not are wpn/wpnEnd/wpnTip (collapsed at bind) and the shoulders (arms spread in
the bind pose) - pose differences, which the inverse bind absorbs.

Four weighted joints - arm_lower_{L,R}_TWIST and _TWIST1, carrying 15.6 % of the
mesh's weight - are not animated bones. CS2 drives them with AnimConstraintTiltTwist
from weapon_arms.vmdl: slave weight 0.5 for _TWIST, 1.0 for _TWIST1, input the
matching hand, input_axis 0 and slave_axis 0. Those weights are read from the
file; the runtime synthesises the bones with them (Cs2Rig.TwistBones).

Usage:  python3 tools/cs2_glb_to_skinned.py [--out FILE] [--glove glove_fingerless]
"""

from __future__ import annotations

import argparse
import struct
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))

import cs2_glb

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "src/ScCsgoKnives/AnimationData"
GLB = (Path.home() / "workspaces/CSMCReverse/local_cs2_analysis/all_weapons"
       / "08_first_person/glb")
ARMS = GLB / "weapons/models/shared/arms/weapon_arms.glb"

MAGIC = b"SCK2SKIN"
VERSION = 2
INCHES_PER_METRE = 39.370079


def write_string(out, text: str):
    raw = text.encode("utf-8")
    out += struct.pack("<H", len(raw))
    out += raw
    return out


def pick_mesh(glb, path: Path, want: str = None):
    """Which mesh to convert, and its index.

    The arms GLB has one. A gun GLB has two - body_legacy, which CS:MC was built on,
    and body_hd, which CS2 draws today and which the mod's textures already come from
    - and the Dual Berettas add an eholster. So body_hd wins by default, and --mesh
    picks by name substring when that is not what is wanted.
    """
    meshes = glb.meshes()
    if not meshes:
        raise SystemExit("%s: no mesh" % path.name)
    names = [getattr(m, "name", "") or "" for m in meshes]
    if want:
        hits = [i for i, n in enumerate(names) if want in n]
        if len(hits) != 1:
            raise SystemExit("%s: --mesh %r matches %d of %s" % (path.name, want, len(hits), names))
        return meshes[hits[0]], hits[0]
    if len(meshes) == 1:
        return meshes[0], 0
    hd = [i for i, n in enumerate(names) if n.endswith(".body_hd")]
    if len(hd) == 1:
        return meshes[hd[0]], hd[0]
    raise SystemExit("%s: %d meshes and no single body_hd: %s" % (path.name, len(meshes), names))


def convert(path: Path, want_mesh: str = None) -> bytes:
    glb = cs2_glb.Glb(path)
    mesh, mesh_index = pick_mesh(glb, path, want_mesh)
    skin = glb.mesh_skin(mesh_index)
    if skin is None:
        raise SystemExit("%s: no skin" % path.name)

    joints = skin["joints"]
    if len(joints) > 255:
        raise SystemExit("%s: %d joints, more than a byte index holds" % (path.name, len(joints)))

    # Inverse bind, adjusted so a vertex in inches maps to joint-local in inches:
    # scale the translation row only, since the input is scaled too.
    scale = np.diag([INCHES_PER_METRE] * 3 + [1.0])
    inverse_bind = [np.linalg.inv(scale) @ m @ scale for m in skin["inverse_bind"]]

    body = b""
    body += MAGIC + struct.pack("<I", VERSION)
    body += struct.pack("<H", len(joints))
    for name, m in zip(joints, inverse_bind):
        body = write_string(body, name)
        body += struct.pack("<16f", *m.reshape(-1))

    # The arms' two primitives index the same vertex accessors - only the index lists
    # and the material differ - so the block is written once and skinned once; writing
    # it per primitive would double the per-frame CPU skinning for identical data.
    # Guns do not share: the AK's body_hd has 21414 vertices in one primitive and 36 in
    # the other. Those are concatenated and the indices offset, which the format
    # already allows. The shared case is kept exactly as it was so cs2_arms.skin does
    # not change.
    keys = ("POSITION", "NORMAL", "TEXCOORD_0", "JOINTS_0", "WEIGHTS_0")
    first = mesh.primitives[0]
    shared = all(np.array_equal(prim.attributes[k], first.attributes[k])
                 for prim in mesh.primitives[1:] for k in keys)

    if shared:
        blocks = [first]
        offsets = [0] * len(mesh.primitives)
    else:
        blocks = list(mesh.primitives)
        offsets, running = [], 0
        for prim in mesh.primitives:
            offsets.append(running)
            running += len(prim.attributes["POSITION"])

    def stack(key, default=None):
        out = []
        for prim in blocks:
            v = prim.attributes.get(key)
            if v is None:
                if default is None:
                    raise SystemExit("%s: a primitive has no %s" % (path.name, key))
                v = np.tile(default, (len(prim.attributes["POSITION"]), 1))
            out.append(v)
        return np.concatenate(out, 0)

    pos = stack("POSITION").astype(np.float32) * INCHES_PER_METRE
    nor = stack("NORMAL", np.float32([0, 0, 1])).astype(np.float32)
    uv = stack("TEXCOORD_0").astype(np.float32)
    w = stack("WEIGHTS_0").astype(np.float32)
    if w.max() > 1.5:
        w = w / np.float32(255.0)
    j = stack("JOINTS_0").astype(np.uint8)

    # Drop influences below a rounding threshold and renormalise: a stray 1e-7 on an
    # unmapped joint would otherwise pull a vertex toward the origin.
    w = np.where(w < 1e-5, 0.0, w).astype(np.float32)
    total = w.sum(1, keepdims=True)
    w = np.divide(w, np.where(total < 1e-6, 1.0, total)).astype(np.float32)

    body += struct.pack("<I", len(pos))
    rows = np.empty((len(pos), 52), np.uint8)
    rows[:, 0:12] = pos.view(np.uint8).reshape(-1, 12)
    rows[:, 12:24] = nor.view(np.uint8).reshape(-1, 12)
    rows[:, 24:32] = uv.view(np.uint8).reshape(-1, 8)
    rows[:, 32:36] = j
    rows[:, 36:52] = w.view(np.uint8).reshape(-1, 16)
    body += rows.tobytes()

    body += struct.pack("<H", len(mesh.primitives))
    stats = []
    for prim, offset in zip(mesh.primitives, offsets):
        idx = (prim.indices.astype(np.uint32) + np.uint32(offset))
        if idx.max(initial=0) >= len(pos):
            raise SystemExit("%s: an index points past the vertex block" % path.name)
        body = write_string(body, prim.material or "")
        body += struct.pack("<I", len(idx))
        body += idx.tobytes()
        stats.append((prim.material, len(pos), len(idx) // 3))

    return body, joints, stats


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", type=Path, default=ARMS)
    ap.add_argument("--out", type=Path, default=DATA / "cs2_arms.skin")
    ap.add_argument("--mesh", help="mesh name substring; body_hd is preferred by default")
    args = ap.parse_args()

    blob, joints, stats = convert(args.source, args.mesh)
    args.out.write_bytes(blob)
    print("%s: %d joints, %d shared vertices, %d primitives -> %s (%.1f KB)"
          % (args.source.name, len(joints), stats[0][1], len(stats), args.out.name, len(blob) / 1024))
    for material, verts, tris in stats:
        print("   %-20s %6d tris" % (material, tris))


if __name__ == "__main__":
    main()
