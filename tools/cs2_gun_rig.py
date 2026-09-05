#!/usr/bin/env python3
"""CS2's own viewmodel clips for every gun -> <gun>.cs2.animation.json.

The three guns shipped so far went through cs2_dmx_to_rig.py, which solves a
binding per rigid mesh part because that was how their meshes were exported. The
gun GLBs turn out to be skinned exactly like the knives - the AK weights bolt,
clip, cliprelease and trigger; the revolver seventeen joints including
cylbullet1..; the M249 twenty-one including bullet01.. - so every gun can take the
knives' path: one skinned mesh, no per-part solve, and cs2_glb_to_skinned.py reads
it unchanged.

Clip names. There is no single suffix rule across CS2's folders - pistol_glock18
holds draw_glock, pistol_hkp2000 holds draw_hkp, rifle_ssg08 holds both draw_ssg08
and draw_ssg08_lgcy - so the suffix is derived per folder from its own draw clip
and every other action is matched as <action>_<suffix>. That is checked, not
assumed: a gun whose core clips do not all resolve is reported.

Usage:  python3 tools/cs2_gun_rig.py [--gun deagle] [--out DIR] [--dry-run]
        (on Windows: python tools\\cs2_gun_rig.py)
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

# The three that shipped before this generator existed. Their rigs come from
# cs2_dmx_to_rig.py and carry MeshParts and Bindings for CS2's rigid pieces; a rig
# written here would have neither and would switch them to the skinned path, which
# is a change worth making on its own and with its own acceptance, not as a side
# effect of adding other guns. Writing over them needs --replace-shipped.
SHIPPED = {"ak47", "m4a1s", "awp"}

# Every folder CS2 ships, so --all can enumerate what is portable.
ALL_FOLDERS = {
    # pistols
    "usp_silencer": "pistol/_default_pistol", "cz75a": "pistol/pistol_cz75a",
    "deagle": "pistol/pistol_deagle", "elite": "pistol/pistol_elite",
    "fiveseven": "pistol/pistol_fiveseven", "glock18": "pistol/pistol_glock18",
    "hkp2000": "pistol/pistol_hkp2000", "p250": "pistol/pistol_p250",
    "revolver": "pistol/pistol_revolver", "taser": "pistol/pistol_taser",
    "tec9": "pistol/pistol_tec9",
    # rifles, SMGs, shotguns, LMGs, snipers - CS2 files them all under rifle/
    "m4a1s": "rifle/_default_rifle", "ak47": "rifle/rifle_ak", "aug": "rifle/rifle_aug",
    "awp": "rifle/rifle_awp", "bizon": "rifle/rifle_bizon", "famas": "rifle/rifle_famas",
    "g3sg1": "rifle/rifle_g3sg1", "galilar": "rifle/rifle_galilar",
    "m249": "rifle/rifle_m249", "m4a4": "rifle/rifle_m4a4", "mac10": "rifle/rifle_mac10",
    "mag7": "rifle/rifle_mag7", "mp5sd": "rifle/rifle_mp5sd", "mp7": "rifle/rifle_mp7",
    "mp9": "rifle/rifle_mp9", "negev": "rifle/rifle_negev", "nova": "rifle/rifle_nova",
    "p90": "rifle/rifle_p90", "sawedoff": "rifle/rifle_sawedoff",
    "scar20": "rifle/rifle_scar20", "sg556": "rifle/rifle_sg556",
    "ssg08": "rifle/rifle_ssg08", "ump45": "rifle/rifle_ump45",
    "xm1014": "rifle/rifle_xm1014",
}

# mod alias -> the clip patterns to try, in order. {s} is the folder's own suffix.
ALIASES = [
    ("deploy",          ["draw_{s}"]),
    ("deploy2",         ["draw2_{s}", "draw_2_{s}"]),
    ("deploySilenced",  ["draw_silenced_{s}"]),
    ("idle",            ["idle_{s}", "idle1_{s}"]),
    ("idleEmpty",       ["idle_slide_back_{s}"]),
    ("shoot1",          ["shoot1_{s}", "shoot_right1_{s}"]),
    ("shootEmpty",      ["shoot_empty_{s}"]),
    ("reload",          ["reload_{s}", "empty_reload_{s}"]),
    ("reloadEmpty",     ["reload_empty_{s}", "reload2_empty_{s}"]),
    ("inspect",         ["lookat01_{s}"]),
    ("inspect2",        ["lookat02_{s}"]),
    ("inspect3",        ["lookat03_{s}"]),
    ("attach",          ["silencer_attach_{s}"]),
    ("detach",          ["silencer_detach_{s}"]),
]

# The clips a gun must have for the mod to draw it at all.
CORE = ["deploy", "idle", "shoot1", "inspect"]


def suffix(folder: str, stems) -> str:
    """The folder's own clip suffix, from its draw clip.

    Not the folder name: pistol_glock18 uses `glock`, pistol_hkp2000 uses `hkp`.
    The shortest draw wins, so rifle_ssg08 picks ssg08 over ssg08_lgcy.
    """
    draws = [s for s in stems
             if s.startswith("draw_")
             and not s.startswith("draw_2_") and not s.startswith("draw_silenced_")]
    if not draws:
        raise SystemExit("%s: no draw clip to take the suffix from (%s)" % (folder, stems))
    return min(draws, key=len)[len("draw_"):]


def config(gun: str, folder: str) -> dict:
    stems = vm.clip_stems(folder)
    if not stems:
        raise SystemExit("no CS2 clips for %s (looked in %s)" % (gun, folder))
    s = suffix(folder, stems)
    available = set(stems)
    clips = {}
    for alias, patterns in ALIASES:
        hit = next((p.format(s=s) for p in patterns if p.format(s=s) in available), None)
        if hit and hit not in clips:
            clips[hit] = alias
    missing_core = [a for a in CORE if a not in clips.values()]
    return {"folder": folder, "suffix": s, "clips": clips,
            "missingCore": missing_core,
            "unused": sorted(x for x in available if x not in clips)}


def convert(gun: str, folder: str) -> dict:
    cfg = config(gun, folder)
    if cfg["missingCore"]:
        raise SystemExit("%s: no clip for %s" % (gun, ", ".join(cfg["missingCore"])))
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
            raise SystemExit("%s/%s: bone list differs from %s" % (gun, stem, reference.name))
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
        "Notes": ("CS2's own viewmodel animation. The mesh is not described here: the "
                  "gun is one skinned mesh whose moving parts ride bones in this same "
                  "skeleton, so it ships through cs2_glb_to_skinned.py and "
                  "MeshParts/Bindings are deliberately empty."),
        "Source": {
            "analysis": "local_cs2_analysis/all_weapons/08_first_person",
            "folder": cfg["folder"], "clipSuffix": cfg["suffix"],
            "clipsNotUsed": cfg["unused"],
        },
        "MeshParts": [],
        "Bindings": [],
        "Skinned": "%s.cs2.skin" % gun,
        "Skeleton": skeleton,
        "Clips": clips,
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--gun", action="append")
    ap.add_argument("--all", action="store_true", help="every gun CS2 ships")
    ap.add_argument("--replace-shipped", action="store_true",
                    help="also rewrite ak47/m4a1s/awp, whose rigs carry mesh parts")
    ap.add_argument("--dry-run", action="store_true", help="report the alias map only")
    ap.add_argument("--out", type=Path, default=DATA)
    args = ap.parse_args()

    names = args.gun or sorted(ALL_FOLDERS)
    unknown = [n for n in names if n not in ALL_FOLDERS]
    if unknown:
        raise SystemExit("unknown gun(s): %s" % ", ".join(unknown))
    if not args.replace_shipped and not args.dry_run:
        held = [n for n in names if n in SHIPPED]
        names = [n for n in names if n not in SHIPPED]
        if held:
            print("skipping %s (already shipped with mesh parts; --replace-shipped to rewrite)"
                  % ", ".join(held))

    bad = []
    for gun in names:
        folder = ALL_FOLDERS[gun]
        cfg = config(gun, folder)
        if args.dry_run:
            print("%-14s %-22s suffix=%-12s %2d aliases: %s%s"
                  % (gun, folder, cfg["suffix"], len(cfg["clips"]),
                     ",".join(sorted(set(cfg["clips"].values()))),
                     "   MISSING " + ",".join(cfg["missingCore"]) if cfg["missingCore"] else ""))
            if cfg["missingCore"]:
                bad.append(gun)
            continue
        doc = convert(gun, folder)
        args.out.mkdir(parents=True, exist_ok=True)
        path = args.out / ("%s.cs2.animation.json" % gun)
        path.write_text(json.dumps(doc, ensure_ascii=False, separators=(",", ":")), "utf-8")
        frames = sum(c["FrameCount"] for c in doc["Clips"].values())
        print("%-14s %2d clips, %4d frames, %2d bones -> %s (%.1f MB)"
              % (gun, len(doc["Clips"]), frames, len(doc["Skeleton"]),
                 path.name, path.stat().st_size / 1e6))
    return 1 if bad else 0


if __name__ == "__main__":
    raise SystemExit(main())
