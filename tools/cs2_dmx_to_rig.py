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
  * MeshParts and Bindings are empty. They describe the mesh, which stage 2
    replaces; writing CS:MC's values into a CS2 file would be a wrong source.

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
        "MeshParts": [],
        "Bindings": [],
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
