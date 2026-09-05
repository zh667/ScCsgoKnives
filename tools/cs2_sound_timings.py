#!/usr/bin/env python3
"""Turn CS2's sound-event frames into seconds -> AnimationData/cs2_sounds.json.

The frame numbers come from ``08_first_person/sound-event-timings.csv`` (read
out of each clip's CNmClipDocEvent_Sound track); the frame rate comes from the
matching clip in the gun's shipped .cs2.animation.json, so nothing here assumes
30 fps. Event names resolve to CS2's own WAVs through
``05_audio/weapon-soundevent-mapping.json``, and each is matched against the
OGGs the mod already ships so the JSON says which cues can play today and which
still need installing (tools/install_gun_sounds_cs2.py --cues installs them).

Which guns and which clips: every gun in AnimationData/guns.json, and for each
the clips its rig file carries, keyed by that clip's own alias (deploy, reload,
inspect, attach ...). The first version of this file listed three guns and their
clips by hand; the eight guns added in 0.18.0 were not in the list, so their
reloads had no cues at all and the device played nothing.

Usage:  python3 tools/cs2_sound_timings.py [--out FILE]
"""

from __future__ import annotations

import argparse
import csv
import json
import re
import sys
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import cs2_viewmodel as vm

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "src/ScCsgoKnives/AnimationData"
AUDIO = ROOT / "src/ScCsgoKnives/Assets/Audio"
ANALYSIS = vm.ANALYSIS
MAPPING = ANALYSIS.parent / "05_audio/weapon-soundevent-mapping.json"

# The three guns whose rigs predate the alias field: their clip stems map to the
# controller's keys here. Everything newer declares its alias in the rig file.
LEGACY_CLIP_KEYS = {
    "draw_ak": "deploy", "idle_ak": "idle", "shoot1_ak": "shoot1",
    "reload_ak": "reload", "lookat01_ak": "inspect",
    "lookat01_draw_ak": "inspectDraw", "lookat01_transfix_ak": "inspectTransfix",
    "lookat03_ak": "inspect3", "lookat03_draw_ak": "inspect3Draw",
    "lookat03_transfix_ak": "inspect3Transfix", "lookat_draw_ak": "inspectShort",
    "draw_rifle": "deploy", "idle_rifle": "idle", "shoot1_rifle": "shootSilenced",
    "reload_rifle": "reload", "lookat01_rifle": "inspect",
    "silencer_attach_rifle": "attach", "silencer_detach_rifle": "detach",
    "draw_awp": "deploy", "idle_awp": "idle", "shoot1_awp": "shoot1",
    "reload_awp": "reload", "lookat01_awp": "inspect",
}
LEGACY_FOLDERS = {"ak47": "rifle/rifle_ak", "m4a1s": "rifle/_default_rifle", "awp": "rifle/rifle_awp"}


def shipped_assets() -> set:
    return {p.stem for p in AUDIO.rglob("*.ogg")}


def event_files() -> dict:
    """Event -> its decoded audio files.

    WAV first, then MP3: the whole m4a1 folder decoded to MP3 only, so a wav-only
    filter reported Weapon_M4A1.AddAmmo as having no source when it has one.
    """
    rows = json.loads(MAPPING.read_text("utf-8"))
    out = {}
    for row in rows:
        decoded = row.get("decoded_files") or []
        out[row["event"]] = ([f for f in decoded if f.endswith(".wav")]
                             or [f for f in decoded if f.endswith(".mp3")])
    return CaseFold(out)


class CaseFold(dict):
    """Soundevent names are case-insensitive in CS2 and the clips spell them
    freely: reload_p250 cues Weapon_p250.Clipin, the soundevents file defines
    Weapon_P250.Clipin. 24 cues across MAG-7, P250, SCAR-20, M249, Nova, Sawed-Off
    and SG 553 resolved to nothing on an exact match."""
    def __init__(self, base):
        super().__init__(base)
        self._fold = {k.lower(): k for k in base}
    def get(self, key, default=None):
        hit = self._fold.get(key.lower()) if isinstance(key, str) else None
        return super().get(hit, default) if hit is not None else default
    def __contains__(self, key):
        return isinstance(key, str) and key.lower() in self._fold


def norm(name: str) -> str:
    """Fold a name to letters and digits so ak47_boltpull == AK47.BoltPull."""
    return re.sub(r"[^a-z0-9]", "", name.lower())


def cue_name(event: str) -> str:
    """Weapon_DEagle.Clipin -> clipin; Weapon_MP9.Clip.Slide -> clip_slide; _Q dropped."""
    tail = event.split(".", 1)[1] if "." in event else event
    tail = re.sub(r"_Q$", "", tail)
    return re.sub(r"[^a-z0-9]+", "_", tail.lower()).strip("_")


def guess_asset(event: str, wavs, shipped: set, gun: str):
    """Map a CS2 event to a shipped OGG, by the WAV's own name then by shape.

    Returns (asset, why). ``asset`` is None when the mod has no file for it yet.
    Matching folds away separators because the mod's OGG names were typed by
    hand (m4a1s_silencer_screw_1) while CS2 spells them SilencerScrew1. The mod's
    own gun name goes first: the USP-S is usp_silencer here and USP in the event.
    """
    folded = {norm(s): s for s in shipped}
    stems = [Path(w).stem for w in wavs]
    hit = folded.get(norm(gun + cue_name(event)))
    if hit:
        return hit, "mod gun and cue name"
    for stem in stems:
        if stem in shipped:
            return stem, "wav name"
        trimmed = re.sub(r"_\d+$", "", stem)
        if trimmed in shipped:
            return trimmed, "wav name without variant index"
    # Weapon_AK47.BoltPull_Q -> ak47boltpull; _Q is CS2's quiet variant.
    m = re.match(r"Weapon_([A-Za-z0-9]+)\.(\w[\w.]*)$", event)
    if m:
        token = m.group(1).lower()
        token = {"m4a1": "m4a1s"}.get(token, token)
        hit = folded.get(norm(token + cue_name(event)))
        if hit:
            return hit, "event gun and cue name"
        for stem in stems:
            hit = folded.get(norm(re.sub(r"_\d+$", "", stem)))
            if hit:
                return hit, "folded wav name"
    return None, "not shipped"


def gun_clips():
    """(gun, csv clip path, clip key, frame rate) for every clip of every gun."""
    manifest = json.loads((DATA / "guns.json").read_text("utf-8"))
    out = []
    for entry in manifest:
        gun = entry["Name"]
        rig = json.loads((DATA / ("%s.cs2.animation.json" % gun)).read_text("utf-8"))
        source = rig.get("Source") or {}
        folder = source.get("folder") or source.get("clip_folder") or LEGACY_FOLDERS.get(gun)
        if folder is None:
            raise SystemExit("%s: rig file names no clip folder" % gun)
        for stem, clip in rig["Clips"].items():
            key = clip.get("Alias") or LEGACY_CLIP_KEYS.get(stem)
            if not key:
                raise SystemExit("%s/%s: no alias" % (gun, stem))
            rate = float(clip.get("FrameRate") or 0.0)
            out.append((gun, "decompiled/animation/anims/viewmodel/%s/%s.vnmclip" % (folder, stem),
                        key, stem, rate if rate > 1.5 else 30.0))
    return out


def build():
    shipped = shipped_assets()
    files = event_files()
    by_clip = defaultdict(list)
    with (ANALYSIS / "sound-event-timings.csv").open(encoding="utf-8-sig") as fh:
        for row in csv.DictReader(fh):
            if row["class"] != "CNmClipDocEvent_Sound":
                continue
            by_clip[row["clip"]].append(row)

    clips = {}
    missing = []
    for gun, clip_path, key, stem, rate in gun_clips():
        rows = by_clip.get(clip_path)
        if not rows:
            continue
        cues = []
        for row in rows:
            event = row["name"]
            asset, why = guess_asset(event, files.get(event, []), shipped, gun)
            if asset is None:
                missing.append((gun, stem, event))
            cues.append({
                "At": round(float(row["start_frame"]) / rate, 6),
                "Frame": float(row["start_frame"]),
                "Duration": round(float(row["duration_frames"]) / rate, 6),
                "Event": event, "Asset": asset, "Match": why,
                "Relevance": row.get("relevance", ""),
                "Wav": [Path(w).name for w in files.get(event, [])],
            })
        cues.sort(key=lambda c: (c["At"], c["Event"]))
        clips["%s:%s" % (gun, key)] = {
            "SourceClip": stem, "FrameRate": round(rate, 6), "Cues": cues,
        }
    return {
        "Format": "ScCsgoKnives.Cs2Sounds/1",
        "Notes": ("Seconds are the CS2 clip's own CNmClipDocEvent_Sound frame divided "
                  "by that clip's frame rate as the shipped rig file carries it. Static "
                  "clips carry a degenerate frameRate of 1.0 and are read at 30 fps "
                  "instead - the rate every animated viewmodel clip in the export uses. "
                  "Cues with Asset null have no OGG in the mod yet."),
        "Source": {
            "timings": "08_first_person/sound-event-timings.csv",
            "event_to_file": "05_audio/weapon-soundevent-mapping.json",
            "guns": "AnimationData/guns.json and each gun's .cs2.animation.json aliases",
        },
        "Clips": clips,
    }, missing


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", type=Path, default=DATA / "cs2_sounds.json")
    args = ap.parse_args()

    doc, missing = build()
    args.out.write_text(json.dumps(doc, ensure_ascii=False, indent=1), "utf-8")
    total = sum(len(c["Cues"]) for c in doc["Clips"].values())
    playable = sum(1 for c in doc["Clips"].values() for q in c["Cues"] if q["Asset"])
    print("%d clips, %d cues, %d resolve to a shipped OGG -> %s (%.1f KB)"
          % (len(doc["Clips"]), total, playable, args.out.name,
             args.out.stat().st_size / 1024))
    for gun, clip, event in missing:
        print("   no asset: %-13s %-24s %s" % (gun, clip, event))
    return 1 if missing else 0


if __name__ == "__main__":
    raise SystemExit(main())
