#!/usr/bin/env python3
"""Convert CS2 viewmodel DMX clips into <gun>.cs2.animation.json + bone_map.json.

Output mirrors the shipped *.csmc.animation.json schema (Skeleton / Clips /
Bones / Rotation+Translation curves) so CsmcKnifeRig can read it once the cs2
profile is wired up, with three deliberate differences, all recorded in the
file's own Notes field:

  * bone names stay as CS2 spells them (hand_R, finger_index_meta_R, ...);
    bone_map.json carries the CS2 <-> CS:MC correspondence.
  * lengths are Source inches, straight from the DMX. The CS:MC files bake an
    inch->metre root matrix; here that conversion belongs to stage 3's
    placement chain, so nothing is scaled on the way out.
  * MeshParts and Bindings describe the CS2 body_hd parts written by
    cs2_glb_to_obj, bound to CS2's own weapon bones (stage 2). Each binding is
    Right * boneAbsolute * Left, the production rule CsmcKnifeRig already uses,
    built so that at the bone's rest pose the product is the identity - the
    geometry ships already positioned, and the matrix only carries the bone's
    departure from rest.

Usage:  python3 tools/cs2_dmx_to_rig.py [--gun ak47] [--out DIR]
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))

import cs2_glb
import cs2_glb_to_obj as mesh
import cs2_viewmodel as vm
from cs2_rig_selftest import ARM_MAP, GUNS

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "src/ScCsgoKnives/AnimationData"
CLIPS = vm.CLIPS

# Every clip in the gun's own folder, not only the ones CS:MC had a twin for.
EXTRA_CLIPS = {
    "ak47": ["lookat01_draw_ak", "lookat01_transfix_ak", "lookat03_ak",
             "lookat03_draw_ak", "lookat03_transfix_ak", "lookat_draw_ak"],
    "m4a1s": ["silencer_attach_rifle", "silencer_detach_rifle"],
    "awp": [],
}


def r6(x):
    return round(float(x), 6)


def curve(times, values, kind):
    """One DmeLogLayer as a CsmcKnifeRig curve.

    Samples before t=0 are export pre-roll and are dropped: the sampler clamps
    below the first key, so keeping them would make t=0 read the pre-roll value.
    A curve whose samples are all bit-identical collapses to a single key.
    """
    keep = [i for i, t in enumerate(times) if t >= -1e-9]
    if not keep:
        keep = [len(times) - 1]
    ts = [times[i] for i in keep]
    vs = [list(values[i]) for i in keep]
    if len(vs) > 1 and all(v == vs[0] for v in vs[1:]):
        ts, vs = ts[:1], vs[:1]
    return {"Interpolation": "LINEAR",
            "Times": [r6(t) for t in ts],
            "Values": [[r6(c) for c in v] for v in vs]}


_EVENT_BLOCK = re.compile(r'\{\s*_class\s*=\s*"(CNmClipDocEvent_[^"]+)"')


def read_events(vnmclip: Path, frame_rate: float):
    """CNmClipDocEvent_* entries from the KV3 sidecar, frames turned into seconds."""
    if not vnmclip.exists():
        return []
    text = vnmclip.read_text("utf-8", "replace")
    out = []
    for m in _EVENT_BLOCK.finditer(text):
        depth = 0
        for i in range(m.start(), len(text)):
            if text[i] == "{":
                depth += 1
            elif text[i] == "}":
                depth -= 1
                if depth == 0:
                    block = text[m.start():i + 1]
                    break
        else:
            continue

        def field(name):
            f = re.search(r'(?m)^\s*%s\s*=\s*(?:"([^"]*)"|([^\r\n]+))' % re.escape(name), block)
            if not f:
                return None
            return (f.group(1) if f.group(1) is not None else f.group(2)).strip()

        start = float(field("m_flStartTime") or 0.0)
        dur = float(field("m_flDuration") or 0.0)
        out.append({
            "Class": m.group(1),
            "StartFrame": start, "DurationFrames": dur,
            "At": r6(start / frame_rate), "Duration": r6(dur / frame_rate),
            "Name": field("m_name") or field("m_ID") or "",
            "SyncID": field("m_syncID") or "",
            "Relevance": field("m_relevance") or "",
        })
    return sorted(out, key=lambda e: (e["At"], e["Name"]))


def mesh_bindings(gun: str, skeleton: list, reference) -> tuple:
    """MeshParts and Bindings for the CS2 body_hd parts, in the mod's normalized space.

    Geometry from cs2_glb_to_obj sits in ``(glb[[2,0,1]] * 39.37009 - MeshCenter) * s``.
    The animation rig measures its bones in the viewmodel's own space, which is the
    camera's, and differs from the mesh's by the constant translation the GLB-to-DMX
    fit returns. With N the normalization, P that translation, and D_rest the bone's
    rest pose relative to the weapon root,

        Right = N^-1 * P^-1 * D_rest^-1        Left = identity

    so ``vertex * Right * boneAbsolute`` lands in rig inches - which is what
    Cs2Placement consumes - and at the rest pose Right * D_rest collapses to
    N^-1 * P^-1, the plain normalized-to-rig-inches map, checked below.

    Note boneAbsolute is the FULL rig absolute (relative to root_motion), not the
    weapon-relative one: a part that does not move relative to the weapon then
    simply rides the weapon bone, and bolt/clip/trigger add their own departure.
    """
    cfg = mesh.GUNS[gun]
    glb = cs2_glb.Glb(mesh.MODELS / cfg["dir"] / cfg["glb"])
    meshes = glb.meshes()
    index = [i for i, m in enumerate(meshes) if m.name.endswith("body_hd")][0]
    skin = glb.mesh_skin(index)
    centre, scale = mesh.normalization(gun)

    names = [b["Name"] for b in skeleton]

    def rest(name):
        m = np.eye(4)
        i = names.index(name)
        while True:
            b = skeleton[i]
            m = m @ (vm.from_quat(b["Rotation"]) @ vm.translation(b["Translation"]))
            if b["Name"] == "weapon":
                return m
            i = b["Parent"]

    shared = [n for n in skin["joints"] if n in names]
    bind = np.array([np.linalg.inv(m)[3, :3]
                     for n, m in zip(skin["joints"], skin["inverse_bind"]) if n in shared])
    target = np.array([rest(n)[3, :3] for n in shared])
    fit_scale, fit_rot, offset = vm.umeyama(bind, target)
    residual = float(np.linalg.norm(fit_scale * bind @ fit_rot + offset - target, axis=1).max())
    if residual > 1e-3:
        raise SystemExit("%s: GLB bind pose and DMX weapon skeleton differ by %.4f in"
                         % (gun, residual))

    n_matrix = vm.translation(-centre) @ np.diag([scale, scale, scale, 1.0])
    p_matrix = vm.translation(-offset)
    inv_n = np.linalg.inv(n_matrix)
    inv_p = np.linalg.inv(p_matrix)

    report = mesh.convert(gun, Path("/nonexistent"), "hd", write=False)
    parts = []
    bindings = []
    for part in report["parts"]:
        bone = part["bone"]
        d_rest = rest(bone)
        right = inv_n @ inv_p @ np.linalg.inv(d_rest)
        left = np.eye(4)
        # At rest the binding must be exactly the normalized -> rig-inches map, i.e.
        # the mesh sits where the GLB puts it before any animation moves it.
        error = float(np.abs(right @ d_rest - inv_n @ inv_p).max())
        if error > 1e-8:
            raise SystemExit("%s/%s: rest binding is off by %.2e"
                             % (gun, part["name"], error))
        parts.append(part["name"])
        bindings.append({
            "Name": part["name"], "Bone": bone, "BoneIndex": names.index(bone),
            "Faces": part["faces"], "Vertices": part["vertices"],
            "RightMatrix": [r6(x) for x in right.reshape(-1)],
            "ReferenceMatrix": [r6(x) for x in d_rest.reshape(-1)],
            "LeftMatrix": [r6(x) for x in left.reshape(-1)],
            "RestMapError": error,
        })
    return parts, bindings, centre, scale, residual


def convert(gun: str) -> dict:
    cfg = GUNS[gun]
    folder = CLIPS / cfg["folder"]
    stems = list(cfg["clips"]) + EXTRA_CLIPS.get(gun, [])
    reference = vm.load_clip(folder / (stems[0] + ".dmx"))

    skeleton = []
    for i, b in enumerate(reference.bones):
        skeleton.append({
            "Index": i, "Name": b.name, "Parent": b.parent,
            "Children": [j for j, c in enumerate(reference.bones) if c.parent == i],
            "Translation": [r6(x) for x in b.rest_position],
            "Rotation": [r6(x) for x in b.rest_orientation],
            "Scale": [1, 1, 1],
        })

    clips = {}
    for stem in stems:
        path = folder / (stem + ".dmx")
        if not path.exists():
            continue
        clip = vm.load_clip(path)
        if clip.names != reference.names:
            raise SystemExit("%s: bone list differs from %s" % (stem, reference.name))
        bones = {}
        for b in clip.bones:
            entry = {}
            if b.orientation:
                entry["Rotation"] = curve(*b.orientation, "q")
            if b.position:
                entry["Translation"] = curve(*b.position, "v")
            if entry:
                bones[b.name] = entry
        clips[stem] = {
            "SourceName": stem,
            "SourceFile": str(path.relative_to(vm.ANALYSIS)),
            "FrameRate": r6(clip.frame_rate),
            "FrameCount": clip.frame_count,
            "Duration": r6(clip.duration),
            "Events": read_events(folder / (stem + ".vnmclip"), clip.frame_rate),
            "Bones": bones,
        }

    parts, bindings, centre, scale, residual = mesh_bindings(gun, skeleton, reference)

    return {
        "Format": "ScCsgoKnives.Cs2Animation/1",
        "Units": "inch",
        "Notes": ("Bone names are CS2's own; see bone_map.json. Lengths are Source "
                  "inches with no root matrix applied - the inch-to-block conversion "
                  "belongs to the stage 3 placement chain. MeshParts and Bindings are "
                  "empty until stage 2 replaces the mesh."),
        "Source": {
            "analysis": "local_cs2_analysis/all_weapons/08_first_person",
            "clip_folder": cfg["folder"],
            "viewmodel_skeleton": "animation/skeletons/characters/viewmodel.vnmskel",
            "weapon_skeleton": "animation/skeletons/weapons/%s.vnmskel" % (
                {"ak47": "ak47", "m4a1s": "m4a1_silencer", "awp": "awp"}[gun]),
        },
        "WeaponRoot": reference.weapon_root,
        "AttachBone": reference.attach_bone,
        "MeshCenter": [r6(x) for x in centre],
        "MeshNormalizationScale": r6(scale),
        "MeshSource": "02_models/glb_with_animations/.../%s body_hd" % mesh.GUNS[gun]["glb"],
        "MeshBindResidualInches": r6(residual),
        "MeshParts": parts,
        "Bindings": bindings,
        "Skeleton": skeleton,
        "Clips": clips,
    }


def bone_map() -> dict:
    known = vm.attach_bones()
    out = {
        "Notes": ("CS2 viewmodel bone -> the name the CS:MC animbin uses. The two "
                  "rigs are not the same skeleton: CS2 carries finger_*_meta_*, "
                  "armUpperShoulder_*, armUpperStraighten_0_*, arm_upper_*, wpn*, "
                  "attachHand_* and econ, which CS:MC has no bone for, and CS:MC's "
                  "arm_lower_* absorbs the shoulder CS2 keeps separate. Entries below "
                  "are name correspondences only, not claims that the bones coincide."),
        "AttachBone": vm.DEFAULT_ATTACH_BONE,
        "AttachBoneDeclaredIn_vnmskel": known,
        "Arms": dict(sorted(ARM_MAP.items())),
        "Weapon": {g: dict(sorted(c["weapon_bones"].items())) for g, c in GUNS.items()},
    }
    reference = vm.load_clip(CLIPS / GUNS["ak47"]["folder"] / "draw_ak.dmx")
    mapped = set(ARM_MAP) | set(GUNS["ak47"]["weapon_bones"])
    out["UnmappedCs2Bones"] = sorted(n for n in reference.names if n not in mapped)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--gun", action="append", choices=sorted(GUNS))
    ap.add_argument("--out", type=Path, default=DATA)
    args = ap.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)

    for gun in args.gun or sorted(GUNS):
        doc = convert(gun)
        path = args.out / ("%s.cs2.animation.json" % gun)
        path.write_text(json.dumps(doc, ensure_ascii=False, separators=(",", ":")), "utf-8")
        frames = sum(c["FrameCount"] for c in doc["Clips"].values())
        events = sum(len(c["Events"]) for c in doc["Clips"].values())
        print("%-6s %2d clips, %4d frames, %2d events, %d bones -> %s (%.1f KB)"
              % (gun, len(doc["Clips"]), frames, events, len(doc["Skeleton"]),
                 path.name, path.stat().st_size / 1024))

    path = args.out / "cs2_bone_map.json"
    path.write_text(json.dumps(bone_map(), ensure_ascii=False, indent=1), "utf-8")
    print("wrote %s" % path.name)


if __name__ == "__main__":
    main()
