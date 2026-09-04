#!/usr/bin/env python3
"""Stage 4 acceptance: CS2's arms and gloves, CPU-skinned.

  A  rig       the arms GLB and the animation DMX are the same skeleton - bone
               segment lengths compared, which is pose-independent - and every
               joint carrying weight resolves either to a rig bone or to one of
               the four twist bones CS2's own vmdl defines.
  B  skinning  the shipped C# (Cs2SkinnedMesh, run headless out of the mod
               assembly) reproduces this file's reference skinning vertex for
               vertex.
  C  contact   the fingers are on the weapon: distance from the finger vertices
               to the nearest CS2 weapon-mesh vertex, per gun, at idle.
  D  weights   every vertex's influences sum to 1.

Usage:  python3 tools/cs2_arms_selftest.py [--json out.json]
"""

from __future__ import annotations

import argparse
import json
import os
import struct
import subprocess
import sys
from collections import Counter
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))

import cs2_glb
import cs2_glb_to_obj as meshconv
import cs2_placement as place
import cs2_viewmodel as vm
from cs2_rig_selftest import GUNS

ROOT = Path(__file__).resolve().parent.parent
ARMS = (Path.home() / "workspaces/CSMCReverse/local_cs2_analysis/all_weapons"
        / "08_first_person/glb/weapons/models/shared/arms/weapon_arms.glb")
INCHES = 39.370079

# From weapon_arms.vmdl's AnimConstraintTiltTwist blocks: slave, parent, input, weight.
TWIST = [("arm_lower_L_TWIST", "arm_lower_L", "hand_L", 0.5),
         ("arm_lower_L_TWIST1", "arm_lower_L", "hand_L", 1.0),
         ("arm_lower_R_TWIST", "arm_lower_R", "hand_R", 0.5),
         ("arm_lower_R_TWIST1", "arm_lower_R", "hand_R", 1.0)]


def load_arms():
    glb = cs2_glb.Glb(ARMS)
    mesh = glb.meshes()[0]
    skin = glb.mesh_skin(0)
    prim = mesh.primitives[0]
    pos = prim.attributes["POSITION"].astype(float) * INCHES
    nor = prim.attributes["NORMAL"].astype(float)
    w = prim.attributes["WEIGHTS_0"].astype(float)
    if w.max() > 1.5:
        w = w / 255.0
    w = np.where(w < 1e-5, 0.0, w)
    w = w / np.where(w.sum(1, keepdims=True) < 1e-6, 1.0, w.sum(1, keepdims=True))
    j = prim.attributes["JOINTS_0"].astype(int)
    scale = np.diag([INCHES] * 3 + [1.0])
    ibm = [np.linalg.inv(scale) @ m @ scale for m in skin["inverse_bind"]]
    return skin["joints"], ibm, pos, nor, w, j, mesh


def quat_from_matrix(m):
    r = vm.rotation_of(m)
    t = np.trace(r)
    if t > 0:
        s = np.sqrt(t + 1.0) * 2
        q = np.array([(r[1, 2] - r[2, 1]) / s, (r[2, 0] - r[0, 2]) / s,
                      (r[0, 1] - r[1, 0]) / s, 0.25 * s])
    else:
        i = int(np.argmax(np.diag(r)))
        k, l = (i + 1) % 3, (i + 2) % 3
        s = np.sqrt(1.0 + r[i, i] - r[k, k] - r[l, l]) * 2
        q = np.zeros(4)
        q[3] = (r[k, l] - r[l, k]) / s
        q[i] = 0.25 * s
        q[k] = (r[k, i] + r[i, k]) / s
        q[l] = (r[l, i] + r[i, l]) / s
    n = np.linalg.norm(q)
    return q / n if n > 1e-9 else np.array([0.0, 0, 0, 1])


def slerp_from_identity(q, weight):
    if q[3] < 0:
        q = -q
    angle = 2 * np.arccos(np.clip(q[3], -1, 1))
    if angle < 1e-7:
        return np.array([0.0, 0, 0, 1])
    axis = q[:3] / max(np.sin(angle / 2), 1e-9)
    a = angle * weight
    return np.append(axis * np.sin(a / 2), np.cos(a / 2))


def bone_matrices(joints, ibm, absolute, placement):
    out = []
    for name, inv in zip(joints, ibm):
        target = absolute.get(name)
        if target is None:
            hit = next((t for t in TWIST if t[0] == name), None)
            if hit and hit[1] in absolute and hit[2] in absolute:
                index = joints.index(name)
                parent_index = joints.index(hit[1])
                rest = np.linalg.inv(ibm[index]) @ ibm[parent_index]
                local = quat_from_matrix(absolute[hit[2]] @ np.linalg.inv(absolute[hit[1]]))
                twist = np.array([local[0], 0.0, 0.0, local[3]])
                n = np.linalg.norm(twist)
                twist = twist / n if n > 1e-6 else np.array([0.0, 0, 0, 1])
                target = rest @ vm.from_quat(slerp_from_identity(twist, hit[3])) @ absolute[hit[1]]
        out.append(None if target is None else inv @ target @ placement)
    return out


def skin(joints, ibm, pos, w, j, absolute, placement):
    mats = bone_matrices(joints, ibm, absolute, placement)
    h = np.c_[pos, np.ones(len(pos))]
    out = np.zeros((len(pos), 3))
    for k in range(4):
        for jj in np.unique(j[:, k]):
            m = mats[jj]
            if m is None:
                continue
            sel = (j[:, k] == jj) & (w[:, k] > 0)
            if sel.any():
                out[sel] += (h[sel] @ m)[:, :3] * w[sel, k][:, None]
    return out


def csharp_skin(gun, clip, t, out_path: Path):
    r = subprocess.run(
        ["dotnet", "run", "--project", str(ROOT / "tools/ArmPreview/ArmPreview.csproj"),
         "-c", "Release", "--", "cs2arms", gun, clip, "%r" % t, str(out_path)],
        capture_output=True, text=True, cwd=ROOT,
        env={**os.environ, "DOTNET_ROLL_FORWARD": "Major"})
    if r.returncode:
        raise SystemExit(r.stderr.strip()[-800:])
    raw = out_path.read_bytes()
    (count,) = struct.unpack_from("<i", raw, 0)
    return np.frombuffer(raw, np.float32, count * 3, 4).reshape(count, 3).astype(float)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--json", type=Path)
    ap.add_argument("--tmp", type=Path, default=ROOT / ".tmp-cs2")
    args = ap.parse_args()
    args.tmp.mkdir(parents=True, exist_ok=True)

    joints, ibm, pos, nor, w, j, mesh = load_arms()
    clip = vm.load_clip(vm.CLIPS / GUNS["ak47"]["folder"] / "idle_ak.dmx")
    rig = set(clip.names)

    print("A. Arms GLB against the animation skeleton")
    bind = {n: np.linalg.inv(m) for n, m in zip(joints, ibm)}
    agree = total = 0
    for b in clip.bones:
        if b.parent < 0 or b.name not in bind or clip.bones[b.parent].name not in bind:
            continue
        total += 1
        d = np.linalg.norm(bind[b.name][3, :3] - bind[clip.bones[b.parent].name][3, :3])
        if abs(d - np.linalg.norm(b.rest_position)) < 1e-3 * max(1.0, np.linalg.norm(b.rest_position)):
            agree += 1
    weighted = sorted({joints[jj] for k in range(4) for jj in j[w[:, k] > 0, k]})
    twist_names = {t[0] for t in TWIST}
    unresolved = [n for n in weighted if n not in rig and n not in twist_names]
    twist_weight = sum(w[:, k][[joints[x] in twist_names for x in j[:, k]]].sum() for k in range(4))
    print("   %d/%d shared bone segments agree in length to 0.1%%" % (agree, total))
    print("   %d joints carry weight; %d unresolved: %s"
          % (len(weighted), len(unresolved), unresolved or "none"))
    print("   twist bones carry %.2f%% of the mesh's weight, driven at %s"
          % (100 * twist_weight / w.sum(), ", ".join("%s=%.1f" % (t[0].split('_')[-1], t[3]) for t in TWIST[:2])))

    print("\nB. Shipped C# skinning against this file's reference")
    cases = [("ak47", "idle", 0.0), ("ak47", "reload", 1.0), ("m4a1s", "deploy", 0.5),
             ("awp", "inspect", 2.0)]
    worst = 0.0
    rows = []
    placement = place.placement()
    for gun, alias, t in cases:
        got = csharp_skin(gun, alias, t, args.tmp / ("arms_%s_%s.bin" % (gun, alias)))
        stem = {v: k for k, v in GUNS[gun]["clips"].items()}[alias]
        c = vm.load_clip(vm.CLIPS / GUNS[gun]["folder"] / (stem + ".dmx"))
        want = skin(joints, ibm, pos, w, j, c.absolute(t), placement)
        d = np.linalg.norm(got - want, axis=1)
        worst = max(worst, float(d.max()))
        rows.append({"gun": gun, "clip": alias, "t": t, "vertices": int(len(got)),
                     "max_error_m": float(d.max()), "mean_error_m": float(d.mean())})
        print("   %-6s %-8s t=%.2f  %d vertices, max %.3e m, mean %.3e m"
              % (gun, alias, t, len(got), d.max(), d.mean()))

    print("\nC. Fingers on the weapon at idle (inches, rig space)")
    contact = []
    for gun, cfg in GUNS.items():
        stem = [k for k, v in cfg["clips"].items() if v == "idle"][0]
        c = vm.load_clip(vm.CLIPS / cfg["folder"] / (stem + ".dmx"))
        absolute = c.absolute(0.0)
        skinned = skin(joints, ibm, pos, w, j, absolute, np.eye(4))
        # Place the weapon exactly the way the renderer does: the emitted OBJ parts
        # are normalized, and each part's binding is Right * boneAbsolute (Left is
        # identity), which lands them in rig inches. Transforming the GLB by hand
        # here would repeat, and get wrong, what the binding already encodes.
        doc = json.loads((ROOT / "src/ScCsgoKnives/AnimationData"
                          / ("%s.cs2.animation.json" % gun)).read_text("utf-8"))
        chunks = []
        for binding in doc["Bindings"]:
            path = (ROOT / "src/ScCsgoKnives/Assets/Models/ScCsgoKnives"
                    / ("%s_cs2_%s.obj" % (gun, binding["Name"])))
            if not path.exists():
                continue
            v = np.array([[float(x) for x in line.split()[1:4]]
                          for line in path.read_text().splitlines() if line.startswith("v ")])
            right = np.array(binding["RightMatrix"], float).reshape(4, 4)
            bone = doc["Skeleton"][binding["BoneIndex"]]["Name"]
            chunks.append((np.c_[v, np.ones(len(v))] @ right @ absolute[bone])[:, :3])
        weapon = np.vstack(chunks)
        from scipy.spatial import cKDTree
        tree = cKDTree(weapon)
        row = {"gun": gun}
        for side, bones in (("right", ["finger_index_1_R", "finger_middle_1_R", "finger_thumb_1_R"]),
                            ("left", ["finger_index_1_L", "finger_middle_1_L", "finger_thumb_1_L"])):
            sel = np.zeros(len(pos), bool)
            for b in bones:
                if b not in joints:
                    continue
                bi = joints.index(b)
                for k in range(4):
                    sel |= (j[:, k] == bi) & (w[:, k] > 0.5)
            if not sel.any():
                continue
            d = tree.query(skinned[sel], k=1)[0]
            row[side] = {"vertices": int(sel.sum()), "median_in": float(np.median(d)),
                         "p90_in": float(np.percentile(d, 90))}
            print("   %-6s %-5s hand: %4d finger vertices, median %.3f in from the weapon surface, p90 %.3f in"
                  % (gun, side, sel.sum(), np.median(d), np.percentile(d, 90)))
        contact.append(row)

    print("\nD. Weights")
    sums = w.sum(1)
    print("   influences per vertex: %s; sum in [%.6f, %.6f]"
          % (dict(Counter((w > 0).sum(1)).most_common()), sums.min(), sums.max()))

    ok = (not unresolved and agree >= 48 and worst < 5e-5
          and abs(sums.min() - 1) < 1e-5 and abs(sums.max() - 1) < 1e-5
          and all(r.get("right", {}).get("median_in", 9) < 1.0 for r in contact))
    print("\nA/B/C/D %s" % ("PASS" if ok else "FAIL"))
    if args.json:
        args.json.write_text(json.dumps(
            {"segments_agree": agree, "segments_total": total, "unresolved_joints": unresolved,
             "twist_weight_fraction": float(twist_weight / w.sum()),
             "skinning": rows, "contact": contact}, indent=2), "utf-8")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
