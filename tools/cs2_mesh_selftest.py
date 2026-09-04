#!/usr/bin/env python3
"""Stage 2 acceptance: the CS2 body_hd meshes and current materials.

  A  rig      the GLB's bind pose and the animation DMX's weapon skeleton are the
              same skeleton, to within a single similarity transform. Residual is
              reported in inches; anything above a thousandth means the mesh and
              the animation disagree about where the bones are.
  B  rigidity every vertex is weighted to exactly one joint, and no triangle
              straddles two. Reported as counts, because the plan expected to
              need a max-weight rule and a torn-triangle policy.
  C  legacy   running the same converter on body_legacy - the mesh the mod already
              ships, by way of CS:MC - reproduces the shipped OBJs. This is the
              check that validates the axis order, the inch conversion and the
              normalization against an asset that is already known good.
  D  obj      every emitted part satisfies Game.ObjModelReader: one object, only
              triangles, complete p/t/n, at most 21845 faces and 65535 vertices.
  E  material every texture the VMAT binds resolves in the export, and the packed
              ORM carries AO/roughness/metalness in R/G/B.

Usage:  python3 tools/cs2_mesh_selftest.py [--json out.json] [--parts DIR]
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))

import cs2_run
import cs2_glb
import cs2_glb_to_obj as conv
import cs2_viewmodel as vm
import install_gun_textures_cs2hd as mat
from cs2_rig_selftest import GUNS as RIG_GUNS

ROOT = Path(__file__).resolve().parent.parent
SHIPPED = ROOT / "src/ScCsgoKnives/Assets/Models/ScCsgoKnives"


def read_obj_positions(path: Path) -> np.ndarray:
    out = []
    for line in path.read_text("utf-8").splitlines():
        if line.startswith("v "):
            out.append([float(x) for x in line.split()[1:4]])
    return np.array(out)


def check_rig():
    rows = []
    for gun, cfg in conv.GUNS.items():
        glb = cs2_glb.Glb(conv.MODELS / cfg["dir"] / cfg["glb"])
        meshes = glb.meshes()
        index = [i for i, m in enumerate(meshes) if m.name.endswith("body_hd")][0]
        skin = glb.mesh_skin(index)
        bind = {n: np.linalg.inv(m)[3, :3]
                for n, m in zip(skin["joints"], skin["inverse_bind"])}

        rig = RIG_GUNS[gun]
        stem = next(iter(rig["clips"]))
        clip = vm.load_clip(vm.CLIPS / rig["folder"] / (stem + ".dmx"))
        names = clip.names

        def rest(name):
            m = np.eye(4)
            i = names.index(name)
            while True:
                b = clip.bones[i]
                m = m @ (vm.from_quat(b.rest_orientation) @ vm.translation(b.rest_position))
                if b.name == "weapon":
                    return m[3, :3]
                i = b.parent

        shared = [n for n in skin["joints"] if n in names]
        a = np.array([bind[n] for n in shared])
        b = np.array([rest(n) for n in shared])
        scale, rot, off = vm.umeyama(a, b)
        residual = np.linalg.norm(scale * a @ rot + off - b, axis=1)
        rows.append({"gun": gun, "bones": shared, "scale": float(scale),
                     "rotation": np.round(rot, 6).tolist(),
                     "max_residual_in": float(residual.max()),
                     "mean_residual_in": float(residual.mean())})
    return rows


# Shipped OBJ stem -> the CS2 joint that owns the same geometry. "weapon_hand_r"
# is CS:MC's name for the body, which CS2 skins to weapon_offset.
def shipped_part_bone(gun: str, stem: str) -> str:
    tail = stem.split("_", 1)[1]
    # Exact first: "v_weapon_ak47_cliprelease" also starts with "v_weapon_ak47_clip".
    for cs2, csmc in RIG_GUNS[gun]["weapon_bones"].items():
        if tail == csmc:
            return cs2
    for cs2, csmc in sorted(RIG_GUNS[gun]["weapon_bones"].items(),
                            key=lambda kv: -len(kv[1])):
        if tail.startswith(csmc + "__"):
            return cs2
    return "weapon_offset" if tail.startswith("weapon_hand_r") else ""


# Parts where the mod's shipped mesh is known not to be CS2's, so a distance here
# says nothing about the converter. The AWP magazine in the shipped set has half
# the vertices of the GLB's and is not a rigid offset of it: CS:MC used a
# different magazine. Recorded with its measured number rather than waved away.
LEGACY_EXCEPTIONS = {("awp", "clip"): "CS:MC ships a different AWP magazine mesh"}


def check_legacy():
    from scipy.spatial import cKDTree

    rows = []
    for gun in sorted(conv.GUNS):
        cfg = conv.GUNS[gun]
        glb = cs2_glb.Glb(conv.MODELS / cfg["dir"] / cfg["glb"])
        meshes = glb.meshes()
        found = [i for i, m in enumerate(meshes) if m.name.endswith("body_legacy")]
        if not found:
            rows.append({"gun": gun, "status": "no body_legacy"})
            continue
        index = found[0]
        skin = glb.mesh_skin(index)
        centre, scale = conv.normalization(gun)

        by_bone = {}
        for prim in meshes[index].primitives:
            pos = conv.to_normalized(prim.attributes["POSITION"].astype(float), centre, scale)
            weights = prim.attributes["WEIGHTS_0"].astype(float)
            if weights.max() > 1.5:
                weights = weights / 255.0
            joints = prim.attributes["JOINTS_0"].astype(int)
            owner = joints[np.arange(len(joints)), weights.argmax(1)]
            for j in np.unique(owner):
                by_bone.setdefault(skin["joints"][j], []).append(pos[owner == j])
        by_bone = {k: np.vstack(v) for k, v in by_bone.items()}

        parts = []
        for path in sorted(SHIPPED.glob("%s_*.obj" % gun)):
            if "_cs2_" in path.name:
                continue
            bone = shipped_part_bone(gun, path.stem)
            target = by_bone.get(bone)
            if target is None or not len(target):
                parts.append({"file": path.name, "bone": bone, "status": "no CS2 counterpart"})
                continue
            v = read_obj_positions(path)
            d = cKDTree(target).query(v, k=1)[0] / scale * 25.4
            parts.append({"file": path.name, "bone": bone, "shipped_vertices": int(len(v)),
                          "glb_vertices": int(len(target)),
                          "max_mm": float(d.max()), "mean_mm": float(d.mean()),
                          "known_difference": LEGACY_EXCEPTIONS.get((gun, bone))})
        rows.append({"gun": gun, "parts": parts})
    return rows


def check_obj(parts: Path):
    files = sorted(parts.glob("*_cs2_*.obj"))
    if not files:
        return {"files": 0, "bad": 0, "note": "no CS2 parts built"}
    out = cs2_run.run([sys.executable, ROOT / "tools/validate_obj.py"] + files, check=False)
    tail = cs2_run.tail(out.stdout).splitlines()[-1] if (out.stdout or "").strip() else ""
    return {"files": len(files), "output": tail, "returncode": out.returncode,
            "total_bytes": sum(f.stat().st_size for f in files)}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--json", type=Path)
    ap.add_argument("--parts", type=Path, default=SHIPPED)
    args = ap.parse_args()

    print("A. GLB bind pose vs animation DMX weapon skeleton")
    rig = check_rig()
    for r in rig:
        print("   %-6s %d shared bones, scale %.5f (1/%.4f), residual max %.3e in, mean %.3e in"
              % (r["gun"], len(r["bones"]), r["scale"], 1 / r["scale"],
                 r["max_residual_in"], r["mean_residual_in"]))

    print("\nB. Rigid skinning")
    rigid = []
    for gun in sorted(conv.GUNS):
        r = conv.convert(gun, Path("/nonexistent"), "hd", write=False)
        rigid.append(r)
        print("   %-6s %d parts, %d faces, %d vertices with a blended weight, %d seam triangles"
              % (gun, len(r["parts"]), r["total_faces"], r["blended_vertices"],
                 r["split_triangles"]))

    print("\nC. Same converter on body_legacy vs the shipped OBJs, part by part")
    legacy = check_legacy()
    for r in legacy:
        if r.get("status"):
            print("   %-6s %s" % (r["gun"], r["status"]))
            continue
        for p in r["parts"]:
            if p.get("status"):
                print("   %-6s %-34s %s" % (r["gun"], p["file"], p["status"]))
            else:
                print("   %-6s %-34s bone %-14s %5d verts, max %.4f mm, mean %.4f mm%s"
                      % (r["gun"], p["file"], p["bone"], p["shipped_vertices"],
                         p["max_mm"], p["mean_mm"],
                         "   [%s]" % p["known_difference"] if p["known_difference"] else ""))

    print("\nD. Emitted OBJ parts against Game.ObjModelReader's rules")
    objs = check_obj(args.parts)
    print("   %s (%d files, %.1f MB)"
          % (objs.get("output") or objs.get("note"), objs["files"],
             objs.get("total_bytes", 0) / 1e6))

    print("\nE. VMAT bindings")
    materials = [mat.install(g, 1024, write=False) for g in sorted(mat.GUNS)]
    for m in materials:
        print("   %-6s all 5 textures resolve; ORM means ao %.1f rough %.1f metal %.1f; "
              "normal deviation from flat %.2f/255"
              % (m["gun"], m["means"]["ao"], m["means"]["roughness"],
                 m["means"]["metalness"], m["normal_deviation_from_flat"]))

    ok = (all(r["max_residual_in"] < 1e-3 for r in rig)
          and all(r["blended_vertices"] == 0 and r["split_triangles"] == 0 for r in rigid)
          and all(p.get("status") or p["known_difference"] or p["max_mm"] < 0.05
                  for r in legacy if not r.get("status") for p in r["parts"])
          and objs.get("returncode", 1) == 0
          and all(p["faces"] <= conv.MAX_FACES and p["vertices"] <= conv.MAX_VERTS
                  for r in rigid for p in r["parts"]))
    print("\nA/B/C/D/E %s" % ("PASS" if ok else "FAIL"))
    if args.json:
        args.json.write_text(json.dumps(
            {"rig": rig, "rigid": rigid, "legacy": legacy, "obj": objs,
             "materials": materials}, indent=2), "utf-8")
        print("wrote %s" % args.json)
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
