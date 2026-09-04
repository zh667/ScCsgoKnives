#!/usr/bin/env python3
"""Turn CS2's sound-event frames into seconds -> AnimationData/cs2_sounds.json.

The frame numbers come from ``08_first_person/sound-event-timings.csv`` (read
out of each clip's CNmClipDocEvent_Sound track); the frame rate comes from the
matching DMX, so nothing here assumes 30 fps. Event names resolve to CS2's own
WAVs through ``05_audio/weapon-soundevent-mapping.json``, and each is matched
against the OGGs the mod already ships so the JSON says which cues can play
today and which still need installing.

Usage:  python3 tools/cs2_sound_timings.py [--all] [--out FILE]
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
from cs2_rig_selftest import GUNS

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "src/ScCsgoKnives/AnimationData"
AUDIO = ROOT / "src/ScCsgoKnives/Assets/Audio"
ANALYSIS = vm.ANALYSIS
MAPPING = ANALYSIS.parent / "05_audio/weapon-soundevent-mapping.json"

# CS2 clip stem -> the clip key the mod's animation controller uses.
CLIP_KEYS = {
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
    return out


def norm(name: str) -> str:
    """Fold a name to letters and digits so ak47_boltpull == AK47.BoltPull."""
    return re.sub(r"[^a-z0-9]", "", name.lower())


def guess_asset(event: str, wavs, shipped: set):
    """Map a CS2 event to a shipped OGG, by the WAV's own name then by shape.

    Returns (asset, why). ``asset`` is None when the mod has no file for it yet.
    Matching folds away separators because the mod's OGG names were typed by
    hand (m4a1s_silencer_screw_1) while CS2 spells them SilencerScrew1.
    """
    folded = {norm(s): s for s in shipped}
    stems = [Path(w).stem for w in wavs]
    for stem in stems:
        if stem in shipped:
            return stem, "wav name"
        trimmed = re.sub(r"_\d+$", "", stem)
        if trimmed in shipped:
            return trimmed, "wav name without variant index"
    # Weapon_AK47.BoltPull_Q -> ak47boltpull; _Q is CS2's quiet variant.
    m = re.match(r"Weapon_([A-Za-z0-9]+)\.(\w+)$", event)
    if m:
        gun = m.group(1).lower()
        gun = {"m4a1": "m4a1s"}.get(gun, gun)
        cue = re.sub(r"_q$", "", m.group(2).lower())
        hit = folded.get(norm(gun + cue))
        if hit:
            return hit, "gun and cue name"
        for stem in stems:
            hit = folded.get(norm(re.sub(r"_\d+$", "", stem)))
            if hit:
                return hit, "folded wav name"
    return None, "not shipped"


def build(all_weapons=False):
    shipped = shipped_assets()
    files = event_files()
    by_clip = defaultdict(list)
    with (ANALYSIS / "sound-event-timings.csv").open(encoding="utf-8-sig") as fh:
        for row in csv.DictReader(fh):
            if row["class"] != "CNmClipDocEvent_Sound":
                continue
            by_clip[row["clip"]].append(row)

    wanted = {}
    for gun, cfg in GUNS.items():
        folder = vm.CLIPS / cfg["folder"]
        for dmx in sorted(folder.glob("*.dmx")):
            wanted["decompiled/animation/anims/viewmodel/%s/%s.vnmclip"
                   % (cfg["folder"], dmx.stem)] = (gun, dmx)

    clips = {}
    missing = []
    for clip_path, rows in sorted(by_clip.items()):
        entry = wanted.get(clip_path)
        if entry is None:
            if not all_weapons:
                continue
            gun, dmx = "", ANALYSIS / clip_path
        gun, dmx = entry
        clip = vm.load_clip(dmx)
        rate = clip.frame_rate if clip.frame_rate > 1.5 else 30.0
        cues = []
        for row in rows:
            event = row["name"]
            asset, why = guess_asset(event, files.get(event, []), shipped)
            if asset is None:
                missing.append((gun, dmx.stem, event))
            cues.append({
                "At": round(float(row["start_frame"]) / rate, 6),
                "Frame": float(row["start_frame"]),
                "Duration": round(float(row["duration_frames"]) / rate, 6),
                "Event": event, "Asset": asset, "Match": why,
                "Relevance": row.get("relevance", ""),
                "Wav": [Path(w).name for w in files.get(event, [])],
            })
        cues.sort(key=lambda c: (c["At"], c["Event"]))
        clips["%s:%s" % (gun, CLIP_KEYS.get(dmx.stem, dmx.stem))] = {
            "SourceClip": dmx.stem, "FrameRate": round(rate, 6), "Cues": cues,
        }
    return {
        "Format": "ScCsgoKnives.Cs2Sounds/1",
        "Notes": ("Seconds are the CS2 clip's own CNmClipDocEvent_Sound frame divided "
                  "by that clip's DMX frame rate. Static clips carry a degenerate "
                  "frameRate of 1.0 in the DMX and are read at 30 fps instead - the "
                  "rate every animated viewmodel clip in the export uses. Cues with "
                  "Asset null have no OGG in the mod yet."),
        "Source": {
            "timings": "08_first_person/sound-event-timings.csv",
            "event_to_file": "05_audio/weapon-soundevent-mapping.json",
        },
        "Clips": clips,
    }, missing


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--all", action="store_true", help="every weapon, not just the three")
    ap.add_argument("--out", type=Path, default=DATA / "cs2_sounds.json")
    args = ap.parse_args()

    doc, missing = build(args.all)
    args.out.write_text(json.dumps(doc, ensure_ascii=False, indent=1), "utf-8")
    total = sum(len(c["Cues"]) for c in doc["Clips"].values())
    playable = sum(1 for c in doc["Clips"].values() for q in c["Cues"] if q["Asset"])
    print("%d clips, %d cues, %d resolve to a shipped OGG -> %s (%.1f KB)"
          % (len(doc["Clips"]), total, playable, args.out.name,
             args.out.stat().st_size / 1024))
    for gun, clip, event in missing:
        print("   no asset: %-6s %-22s %s" % (gun, clip, event))


if __name__ == "__main__":
    main()
