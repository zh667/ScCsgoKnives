#!/usr/bin/env python3
"""CS2 body_hd weapon meshes -> Survivalcraft OBJ parts, one per weapon bone.

The GLB carries two bodies: ``body_legacy`` (what the mod ships today, by way of
CS:MC) and ``body_hd`` (CS2's current mesh, roughly twice the triangles). This
takes body_hd, splits it into rigid parts by the weapon skeleton joint each
vertex is weighted to, and writes each part as an OBJ the engine's strict
ObjModelReader accepts.

Geometry lands in exactly the normalized space the mod already uses, which was
recovered from the shipped assets rather than assumed:

    normalized = (glb_position[[2, 0, 1]] * 39.37009 - MeshCenter) * MeshNormalizationScale

Running that on ``body_legacy`` reproduces the shipped ``ak47_weapon_hand_r.obj``
to 0.017 mm mean / 0.045 mm max, and its UVs match with no V flip - which is why
MeshCenter and MeshNormalizationScale are taken unchanged from each gun's
existing *.csmc.animation.json instead of being recomputed. The CS2 mesh
therefore drops into the current placement chain in place of the legacy one.

Usage:  python3 tools/cs2_glb_to_obj.py [--gun ak47] [--out DIR] [--body hd|legacy]
"""

from __future__ import annotations

import argparse
import json
import sys
from collections import Counter, defaultdict
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))

import cs2_glb
import cs2_viewmodel as vm

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "src/ScCsgoKnives/AnimationData"
MODELS = (Path.home() / "workspaces/CSMCReverse/local_cs2_analysis/all_weapons"
          / "02_models/glb_with_animations/weapons/models")

# Survivalcraft indexes each OBJ object with ushort indices.
MAX_FACES = 21845
MAX_VERTS = 65535

# metres -> Source inches, and the axis order that takes glTF to the weapon space
# the mod's OBJs live in. Both were measured against the shipped assets, see above.
INCHES_PER_METRE = 39.370079
AXIS_ORDER = [2, 0, 1]

GUNS = {
    "ak47": {"dir": "ak47", "glb": "weapon_rif_ak47.glb"},
    "m4a1s": {"dir": "m4a1_silencer", "glb": "weapon_rif_m4a1_silencer.glb"},
    "awp": {"dir": "awp", "glb": "weapon_snip_awp.glb"},
}


def normalization(gun: str):
    doc = json.loads((DATA / ("%s.csmc.animation.json" % gun)).read_text("utf-8"))
    return np.array(doc["MeshCenter"], float), float(doc["MeshNormalizationScale"])


def to_normalized(positions, centre, scale):
    return (positions[:, AXIS_ORDER] * INCHES_PER_METRE - centre) * scale


def to_normalized_dir(vectors):
    """Directions (normals) take the axis order only - no scale, no offset."""
    out = vectors[:, AXIS_ORDER]
    n = np.linalg.norm(out, axis=1, keepdims=True)
    return out / np.where(n < 1e-12, 1.0, n)


def dedupe(values, decimals):
    """Round, then map each row to an index into the unique set, order preserved."""
    rounded = np.round(np.asarray(values, float), decimals)
    seen = {}
    index = np.empty(len(rounded), int)
    unique = []
    for i, row in enumerate(map(tuple, rounded)):
        hit = seen.get(row)
        if hit is None:
            hit = seen[row] = len(unique)
            unique.append(row)
        index[i] = hit
    return np.array(unique, float), index


def fmt(x, decimals):
    text = ("%." + str(decimals) + "f") % x
    text = text.rstrip("0").rstrip(".")
    return text if text not in ("", "-0") else "0"


def write_obj(path: Path, name: str, positions, uvs, normals, faces):
    """One OBJ object: shared v/vt/vn blocks then triangles, as the engine wants."""
    v, vi = dedupe(positions, 5)
    t, ti = dedupe(uvs, 6)
    n, ni = dedupe(normals, 4)
    lines = ["# CS2 body_hd, tools/cs2_glb_to_obj.py"]
    lines += ["v %s %s %s" % tuple(fmt(c, 5) for c in row) for row in v]
    lines += ["vt %s %s" % tuple(fmt(c, 6) for c in row) for row in t]
    lines += ["vn %s %s %s" % tuple(fmt(c, 4) for c in row) for row in n]
    lines.append("o %s" % name)
    for tri in faces:
        lines.append("f " + " ".join("%d/%d/%d" % (vi[k] + 1, ti[k] + 1, ni[k] + 1)
                                     for k in tri))
    path.write_text("\n".join(lines) + "\n", "utf-8")
    return len(v), len(t), len(n), len(faces)


def convert(gun: str, out_dir: Path, body: str = "hd", write: bool = True):
    cfg = GUNS[gun]
    glb = cs2_glb.Glb(MODELS / cfg["dir"] / cfg["glb"])
    meshes = glb.meshes()
    picked = [(i, m) for i, m in enumerate(meshes) if m.name.endswith("body_" + body)]
    if not picked:
        raise SystemExit("%s: no body_%s mesh" % (gun, body))
    mesh_index, mesh = picked[0]
    skin = glb.mesh_skin(mesh_index)
    centre, scale = normalization(gun)

    report = {"gun": gun, "body": body, "joints": skin["joints"], "parts": [],
              "split_triangles": 0, "blended_vertices": 0, "total_faces": 0}

    groups = defaultdict(list)   # (joint index, primitive index) -> triangles
    store = {}
    for pi, prim in enumerate(mesh.primitives):
        pos = prim.attributes["POSITION"].astype(float)
        uv = prim.attributes["TEXCOORD_0"].astype(float)
        nor = prim.attributes.get("NORMAL")
        nor = (nor.astype(float) if nor is not None
               else np.tile([0.0, 0.0, 1.0], (len(pos), 1)))
        weights = prim.attributes["WEIGHTS_0"].astype(float)
        if weights.max() > 1.5:
            weights = weights / 255.0
        joints = prim.attributes["JOINTS_0"].astype(int)
        owner = joints[np.arange(len(joints)), weights.argmax(1)]
        report["blended_vertices"] += int((weights.max(1) <= 0.999).sum())
        store[pi] = (to_normalized(pos, centre, scale), uv, to_normalized_dir(nor), owner)

        tris = prim.indices.reshape(-1, 3)
        for tri in tris:
            owners = owner[tri]
            if owners[0] == owners[1] == owners[2]:
                pick = owners[0]
            else:
                # A triangle straddling two bones goes to the majority owner; it
                # would tear either way, and these are seam triangles only.
                pick = Counter(owners).most_common(1)[0][0]
                report["split_triangles"] += 1
            groups[(int(pick), pi)].append(tri)

    if write:
        out_dir.mkdir(parents=True, exist_ok=True)
    for (joint, pi), tris in sorted(groups.items()):
        bone = skin["joints"][joint]
        pos, uv, nor, _ = store[pi]
        base = bone if len(mesh.primitives) == 1 or pi == 0 else "%s__p%d" % (bone, pi + 1)
        chunks = [tris[i:i + MAX_FACES] for i in range(0, len(tris), MAX_FACES)] or [[]]
        for c, chunk in enumerate(chunks):
            name = base if len(chunks) == 1 else "%s__c%d" % (base, c + 1)
            flat = np.array(chunk).reshape(-1)
            remap = {v: k for k, v in enumerate(dict.fromkeys(flat.tolist()))}
            local = np.array([remap[v] for v in flat.tolist()]).reshape(-1, 3)
            keep = np.array(list(remap.keys()))
            path = out_dir / ("%s_cs2_%s.obj" % (gun, name))
            counts = ((len(keep), 0, 0, len(chunk)) if not write else
                      write_obj(path, name, pos[keep], uv[keep], nor[keep], local))
            report["parts"].append({"name": name, "bone": bone, "primitive": pi,
                                    "faces": len(chunk), "vertices": counts[0],
                                    "file": path.name,
                                    "over_ushort": counts[0] > MAX_VERTS})
            report["total_faces"] += len(chunk)
    return report


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--gun", action="append", choices=sorted(GUNS))
    ap.add_argument("--body", default="hd", choices=["hd", "legacy"])
    ap.add_argument("--out", type=Path,
                    default=ROOT / "src/ScCsgoKnives/Assets/Models/ScCsgoKnives")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--json", type=Path)
    args = ap.parse_args()

    reports = []
    for gun in args.gun or sorted(GUNS):
        r = convert(gun, args.out, args.body, write=not args.dry_run)
        reports.append(r)
        print("%-6s body_%s: %d parts, %d faces, %d blended verts, %d seam triangles"
              % (gun, args.body, len(r["parts"]), r["total_faces"],
                 r["blended_vertices"], r["split_triangles"]))
        for p in r["parts"]:
            print("    %-26s bone %-14s %6d faces %6d verts%s"
                  % (p["name"], p["bone"], p["faces"], p["vertices"],
                     "  OVER USHORT" if p["over_ushort"] else ""))
    if args.json:
        args.json.write_text(json.dumps(reports, indent=1), "utf-8")


if __name__ == "__main__":
    main()
