#!/usr/bin/env python3
"""A CS2 gun's body_hd -> one binary of rigid parts, one bone each.

Measured before it was designed. Across the 32 guns' body_hd meshes, 850,590 of
850,629 vertices have a single influence at weight 1 - only the MAC-10's strap,
39 vertices, blends - and 145 triangles in all span two bones. The arms are 70%
blended. So a gun is not a skinned mesh: it is a set of rigid pieces, one bone
each, and drawing it as groups with a matrix apiece costs nothing per vertex.

That also makes it small. A rigid vertex needs no joint indices or weights, so
32 bytes instead of 52, and the whole set of 32 guns lands near 37 MB against the
96 MB the equivalent OBJ mesh parts would take.

The few triangles that do span bones are kept, not dropped and not reassigned:
they go into a blended residue with joints and weights, which the runtime skins
the way it skins the arms. g3sg1 has 8, mp9 17, mac10 120.

    SCK2PART                     magic
    u32  version = 1
    u16  jointCount              name + 16f inverse bind, each
    u32  rigidVertexCount        pos 3f, normal 3f, uv 2f   (32 bytes)
    u16  rigidPartCount          u16 joint, string material, u32 indexCount, indices
    u32  blendedVertexCount      pos, normal, uv, 4 joint bytes, 4 weights (52 bytes)
    u16  blendedPartCount        string material, u32 indexCount, indices

Usage:  python3 tools/cs2_glb_to_parts.py --source <glb> --out <file>
        (on Windows: python tools\\cs2_glb_to_parts.py ...)
"""

from __future__ import annotations

import argparse
import struct
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))

import cs2_glb
from cs2_glb_to_skinned import INCHES_PER_METRE, pick_mesh, write_string

ROOT = Path(__file__).resolve().parent.parent
MAGIC = b"SCK2PART"
VERSION = 1


def convert(path: Path, want_mesh: str = None):
    glb = cs2_glb.Glb(path)
    mesh, mesh_index = pick_mesh(glb, path, want_mesh)
    skin = glb.mesh_skin(mesh_index)
    if skin is None:
        raise SystemExit("%s: no skin" % path.name)
    joints = skin["joints"]
    if len(joints) > 65535:
        raise SystemExit("%s: %d joints" % (path.name, len(joints)))

    scale = np.diag([INCHES_PER_METRE] * 3 + [1.0])
    inverse_bind = [np.linalg.inv(scale) @ m @ scale for m in skin["inverse_bind"]]

    # One vertex block for the whole mesh, primitives concatenated with offsets.
    keys = ("POSITION", "NORMAL", "TEXCOORD_0", "JOINTS_0", "WEIGHTS_0")
    first = mesh.primitives[0]
    shared = all(np.array_equal(p.attributes[k], first.attributes[k])
                 for p in mesh.primitives[1:] for k in keys)
    blocks = [first] if shared else list(mesh.primitives)
    offsets, running = [], 0
    for p in mesh.primitives:
        offsets.append(0 if shared else running)
        if not shared:
            running += len(p.attributes["POSITION"])

    def stack(key, default=None):
        out = []
        for p in blocks:
            v = p.attributes.get(key)
            if v is None:
                if default is None:
                    raise SystemExit("%s: a primitive has no %s" % (path.name, key))
                v = np.tile(default, (len(p.attributes["POSITION"]), 1))
            out.append(v)
        return np.concatenate(out, 0)

    pos = stack("POSITION").astype(np.float32) * INCHES_PER_METRE
    nor = stack("NORMAL", np.float32([0, 0, 1])).astype(np.float32)
    uv = stack("TEXCOORD_0").astype(np.float32)
    w = stack("WEIGHTS_0").astype(np.float32)
    if w.max() > 1.5:
        w = w / np.float32(255.0)
    j = stack("JOINTS_0").astype(np.uint8)

    w = np.where(w < 1e-5, 0.0, w).astype(np.float32)
    total = w.sum(1, keepdims=True)
    w = np.divide(w, np.where(total < 1e-6, 1.0, total)).astype(np.float32)

    influences = (w > 1e-6).sum(1)
    dominant = j[np.arange(len(j)), w.argmax(1)]

    # A triangle is rigid when all three of its vertices sit on one bone with a
    # single influence. Anything else goes to the blended residue.
    rigid_tris, blended_tris = {}, []
    for prim, offset in zip(mesh.primitives, offsets):
        idx = prim.indices.astype(np.int64) + offset
        tri = idx.reshape(-1, 3)
        one = influences[tri].max(1) == 1
        same = (dominant[tri[:, 0]] == dominant[tri[:, 1]]) & (dominant[tri[:, 1]] == dominant[tri[:, 2]])
        keep = one & same
        for bone in np.unique(dominant[tri[keep][:, 0]]) if keep.any() else []:
            rows = tri[keep][dominant[tri[keep][:, 0]] == bone]
            rigid_tris.setdefault((int(bone), prim.material or ""), []).append(rows)
        if (~keep).any():
            blended_tris.append((prim.material or "", tri[~keep]))

    # Rigid block: only the vertices the rigid parts actually reference, renumbered.
    used = np.unique(np.concatenate([np.concatenate(v).reshape(-1)
                                     for v in rigid_tris.values()])) if rigid_tris else np.array([], np.int64)
    remap = np.full(len(pos), -1, np.int64)
    remap[used] = np.arange(len(used))

    body = MAGIC + struct.pack("<I", VERSION)
    body += struct.pack("<H", len(joints))
    for name, m in zip(joints, inverse_bind):
        body = write_string(body, name)
        body += struct.pack("<16f", *m.reshape(-1))

    body += struct.pack("<I", len(used))
    if len(used):
        rows = np.empty((len(used), 32), np.uint8)
        rows[:, 0:12] = pos[used].view(np.uint8).reshape(-1, 12)
        rows[:, 12:24] = nor[used].view(np.uint8).reshape(-1, 12)
        rows[:, 24:32] = uv[used].view(np.uint8).reshape(-1, 8)
        body += rows.tobytes()

    body += struct.pack("<H", len(rigid_tris))
    stats = []
    for (bone, material), rows in sorted(rigid_tris.items()):
        tri = remap[np.concatenate(rows)].astype(np.uint32)
        body += struct.pack("<H", bone)
        body = write_string(body, material)
        body += struct.pack("<I", tri.size)
        body += tri.reshape(-1).tobytes()
        stats.append((joints[bone], material, len(tri)))   # tri is (N, 3)

    # Blended residue, in the skinned format the arms already use.
    if blended_tris:
        bused = np.unique(np.concatenate([t.reshape(-1) for _, t in blended_tris]))
        bmap = np.full(len(pos), -1, np.int64)
        bmap[bused] = np.arange(len(bused))
        body += struct.pack("<I", len(bused))
        rows = np.empty((len(bused), 52), np.uint8)
        rows[:, 0:12] = pos[bused].view(np.uint8).reshape(-1, 12)
        rows[:, 12:24] = nor[bused].view(np.uint8).reshape(-1, 12)
        rows[:, 24:32] = uv[bused].view(np.uint8).reshape(-1, 8)
        rows[:, 32:36] = j[bused]
        rows[:, 36:52] = w[bused].view(np.uint8).reshape(-1, 16)
        body += rows.tobytes()
        body += struct.pack("<H", len(blended_tris))
        for material, tri in blended_tris:
            body = write_string(body, material)
            remapped = bmap[tri].astype(np.uint32)
            body += struct.pack("<I", remapped.size)
            body += remapped.reshape(-1).tobytes()
    else:
        body += struct.pack("<I", 0)
        body += struct.pack("<H", 0)

    # Nothing may be dropped: every source triangle is either in a rigid part or in
    # the blended residue. A silent loss here would show up as a hole in the gun.
    source_tris = sum(len(p.indices) // 3 for p in mesh.primitives)
    kept = sum(t for _, _, t in stats) + sum(len(t) for _, t in blended_tris)
    if kept != source_tris:
        raise SystemExit("%s: %d source triangles, %d kept" % (path.name, source_tris, kept))

    blended_count = sum(len(t) for _, t in blended_tris)
    return body, joints, stats, len(used), blended_count


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", type=Path, required=True)
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--mesh", help="mesh name substring; body_hd by default")
    args = ap.parse_args()

    blob, joints, stats, vertices, blended = convert(args.source, args.mesh)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_bytes(blob)
    print("%s: %d joints, %d rigid vertices, %d rigid parts, %d blended triangles -> %s (%.1f KB)"
          % (args.source.name, len(joints), vertices, len(stats), blended,
             args.out.name, len(blob) / 1024))
    for bone, material, tris in stats:
        print("   %-22s %-28s %6d tris" % (bone, material, tris))


if __name__ == "__main__":
    main()
