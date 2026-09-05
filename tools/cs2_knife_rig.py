#!/usr/bin/env python3
"""CS2's own knife viewmodel clips -> <knife>.cs2.animation.json.

The guns needed a mesh-part solve because CS2 ships them as several rigid pieces
bound to weapon bones. The knives do not: every one of the 22 is a single skinned
mesh, and the parts that move ride bones inside it - the butterfly weights blade,
lock and rear, the folders weight blade, the push dagger weight weapon_l and
weapon_r. So this writes the animation only, and the mesh goes through
cs2_glb_to_skinned.py, the same path the arms already take.

What made this possible, measured rather than assumed (2026-09-05):

  * each knife clip's DMX carries the merged tree, exactly as the guns' do:
    knife_m9/idle1_m9 has 59 bones of which 44 are arm bones, against the AK's
    64 of which the same 44.
  * across all 22 knives the arm-bone set differs from the AK's by zero;
    knife_push is a superset, adding weapon_hand_l/r for the dual wield.
  * 44 of the arm mesh's 48 weighted joints are hit directly. The four that are
    not - arm_lower_{L,R}_TWIST and _TWIST1 - are absent from the AK's DMX too,
    because CS2 drives them with AnimConstraintTiltTwist and Cs2SkinnedMesh.Twist
    already synthesises them.

So the CS:MC skeleton is not involved and nothing is retargeted.

Clip aliases are the ones the mod already plays. CS2 and CS:MC agree on which
knives have a second idle: bowie, falchion and push have one idle in both, so
those three map idle2 to idle rather than inventing a clip.

Usage:  python3 tools/cs2_knife_rig.py [--knife m9] [--out DIR]
        (on Windows: python tools\\cs2_knife_rig.py ...)
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import cs2_viewmodel as vm
from cs2_dmx_to_rig import curve, r6, read_events

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "src/ScCsgoKnives/AnimationData"
KNIVES_JSON = DATA / "knives.json"

# mod alias -> the CS2 clip stem to try, in order. "%s" is the knife's own stem.
ALIASES = [
    ("deploy",  ["draw_%s"]),
    ("idle",    ["idle1_%s", "idle_%s"]),
    ("idle2",   ["idle2_%s"]),
    ("inspect", ["lookat01_%s"]),
    ("slash1",  ["light_miss1_%s"]),
    ("slash2",  ["light_miss2_%s"]),
]


def knife_names() -> list:
    return [k["Name"] for k in json.loads(KNIVES_JSON.read_text("utf-8"))]


def config(knife: str) -> dict:
    """Folder and alias->stem for one knife, resolved against what CS2 ships."""
    # The CT default knife has no folder of its own; it drives _default_knife,
    # which is also why its graph is viewmodel_knife.vnmgraph+default_ct.
    if knife == "default_ct":
        folder, stem = "knife/_default_knife", "knife"
    else:
        folder, stem = "knife/knife_%s" % knife, knife
    available = set(vm.clip_stems(folder))
    if not available:
        raise SystemExit("no CS2 clips for %s (looked in %s)" % (knife, folder))
    clips, missing = {}, []
    for alias, patterns in ALIASES:
        hit = next((p % stem for p in patterns if (p % stem) in available), None)
        if hit:
            clips[hit] = alias
        else:
            missing.append(alias)
    return {"folder": folder, "stem": stem, "clips": clips,
            "missing": missing, "available": sorted(available)}


def convert(knife: str) -> dict:
    cfg = config(knife)
    stems = list(cfg["clips"])
    reference = vm.load_clip(vm.clip_path(cfg["folder"], stems[0]))

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
        path = vm.clip_path(cfg["folder"], stem)
        clip = vm.load_clip(path)
        if clip.names != reference.names:
            raise SystemExit("%s/%s: bone list differs from %s"
                             % (knife, stem, reference.name))
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
            "Alias": cfg["clips"][stem],
            "SourceFile": vm.relative_to_root(path),
            "FrameRate": r6(clip.frame_rate),
            "FrameCount": clip.frame_count,
            "Duration": r6(clip.duration),
            "Events": read_events(path.with_suffix(".vnmclip"), clip.frame_rate),
            "Bones": bones,
        }

    return {
        "Format": "ScCsgoKnives.Cs2Animation/1",
        "Units": "inch",
        "Notes": ("CS2's own knife viewmodel animation. Bone names are CS2's; the "
                  "CS:MC rig is not involved and nothing is retargeted. The mesh is "
                  "not described here: every knife is one skinned mesh whose moving "
                  "parts ride bones in this same skeleton, so it ships through "
                  "cs2_glb_to_skinned.py like the arms, and MeshParts/Bindings - "
                  "which exist for the guns' rigid pieces - are deliberately empty."),
        "Source": {
            "analysis": "local_cs2_analysis/all_weapons/09_knives",
            "folder": cfg["folder"],
            "aliasesMissing": cfg["missing"],
            "clipsNotUsed": [s for s in cfg["available"] if s not in clips],
        },
        "MeshParts": [],
        "Bindings": [],
        "Skinned": "%s.cs2.skin" % knife,
        "Skeleton": skeleton,
        "Clips": clips,
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--knife", action="append")
    ap.add_argument("--out", type=Path, default=DATA)
    args = ap.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)

    names = args.knife or knife_names()
    unknown = [n for n in names if n not in knife_names()]
    if unknown:
        raise SystemExit("unknown knife(s): %s" % ", ".join(unknown))

    total_missing = []
    for knife in names:
        doc = convert(knife)
        path = args.out / ("%s.cs2.animation.json" % knife)
        path.write_text(json.dumps(doc, ensure_ascii=False, separators=(",", ":")), "utf-8")
        frames = sum(c["FrameCount"] for c in doc["Clips"].values())
        events = sum(len(c["Events"]) for c in doc["Clips"].values())
        miss = doc["Source"]["aliasesMissing"]
        if miss:
            total_missing.append((knife, miss))
        print("%-11s %d clips, %4d frames, %2d events, %2d bones -> %s (%.1f KB)%s"
              % (knife, len(doc["Clips"]), frames, events, len(doc["Skeleton"]),
                 path.name, path.stat().st_size / 1024,
                 "  no " + ",".join(miss) if miss else ""))
    if total_missing:
        print("\nCS2 ships no second idle for: %s"
              % ", ".join(k for k, _ in total_missing)
              + " - CS:MC agrees, so the mod falls back to idle for them.")


if __name__ == "__main__":
    main()
