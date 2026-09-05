#!/usr/bin/env python3
"""Install a gun's CS2 firing sounds, from the soundevents CS2 itself names.

Nothing here is matched on a file name. Each gun's vdata lists its shoot sounds by
event - WEAPON_SOUND_SINGLE, WEAPON_SOUND_SPECIAL1 for a silenced shot,
WEAPON_SOUND_ZOOM_IN and _OUT for a scope - and 05_audio/shoot-event-mapping.json
resolves an event to the WAVs it actually plays. So the USP-S ends up with
usp_unsilenced_01..03 for its unsilenced shot and usp_01..03 for its silenced one,
which is the way round CS2 has it and the opposite of what the file names suggest.

Output names are the ones the mod already plays:

    <gun>_fire_1..n.ogg            WEAPON_SOUND_SINGLE
    <gun>_fire_silenced_1..n.ogg   WEAPON_SOUND_SPECIAL1, only where the gun has a silencer
    <gun>_zoom.ogg / _zoom_out.ogg WEAPON_SOUND_ZOOM_IN / _OUT

Everything is written mono: the engine's OGG length is only right for one channel.

With --cues it installs, for the named guns, every sound their CS2 clips cue and
the mod has no OGG for yet - the reload's clipout/clipin/addammo/bolt, the draw,
the silencer screws - named <gun>_<cue>.ogg after the event's own tail
(Weapon_DEagle.Clipin -> deagle_clipin, Weapon_MP9.Clip.Slide -> mp9_clip_slide).
Which events those are comes from AnimationData/cs2_sounds.json, so run
tools/cs2_sound_timings.py first and again afterwards to record the new files.
0.18.1 shipped the eight new guns with their firing sounds only, and the device
reloaded them in silence.

Usage:  python3 tools/install_gun_sounds_cs2.py deagle glock18 [--dry-run]
        python3 tools/install_gun_sounds_cs2.py --cues deagle glock18 [--dry-run]
        (on Windows: python tools\\install_gun_sounds_cs2.py ...)
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import sys

import numpy as np
import soundfile as sf

sys.path.insert(0, str(Path(__file__).resolve().parent))

ROOT = Path(__file__).resolve().parent.parent
AUDIO = (Path.home() / "workspaces/CSMCReverse/local_cs2_analysis/all_weapons/05_audio")
OUT = ROOT / "src/ScCsgoKnives/Assets/Audio/ScCsgoKnives"
WEAPONS = ROOT / "src/ScCsgoKnives/AnimationData/cs2_weapons.json"

# WEAPON_SOUND_* -> the mod's file stem. A gun without the silencer or the scope
# simply has no such event and nothing is written for it.
TARGETS = {
    "WEAPON_SOUND_SINGLE": "{gun}_fire",
    "WEAPON_SOUND_SPECIAL1": "{gun}_fire_silenced",
    "WEAPON_SOUND_ZOOM_IN": "{gun}_zoom",
    "WEAPON_SOUND_ZOOM_OUT": "{gun}_zoom_out",
}
SINGLE_FILE = {"WEAPON_SOUND_ZOOM_IN", "WEAPON_SOUND_ZOOM_OUT"}


def events() -> dict:
    path = AUDIO / "shoot-event-mapping.json"
    return {e["event"]: e for e in json.loads(path.read_text("utf-8"))}


def all_events() -> dict:
    """Every weapon soundevent CS2 names, not only the shoot ones.

    Case-insensitive, as soundevents are in CS2: the P250's clips cue
    Weapon_p250.Clipin and the soundevents file defines Weapon_P250.Clipin.
    """
    import cs2_sound_timings as timings
    path = AUDIO / "weapon-soundevent-mapping.json"
    return timings.CaseFold({e["event"]: e for e in json.loads(path.read_text("utf-8"))})


def install_cues(guns: list, dry: bool) -> int:
    """Install the OGGs the guns' CS2 clips cue and the table still marks Asset null."""
    import cs2_sound_timings as timings
    table = json.loads((ROOT / "src/ScCsgoKnives/AnimationData/cs2_sounds.json").read_text("utf-8"))
    mapping = all_events()
    missing = []
    written = set()
    for key, clip in table["Clips"].items():
        gun = key.split(":", 1)[0]
        if gun not in guns:
            continue
        for cue in clip["Cues"]:
            if cue.get("Asset"):
                continue
            event = cue["Event"]
            entry = mapping.get(event) or {}
            decoded = entry.get("decoded_files") or []
            files = [AUDIO.parent / f for f in decoded if f.endswith(".wav")] \
                or [AUDIO.parent / f for f in decoded if f.endswith(".mp3")]
            files = [f for f in files if f.exists()]
            if not files:
                missing.append((gun, key, event))
                continue
            name = "%s_%s" % (gun, timings.cue_name(event))
            target = OUT / ("%s.ogg" % name)
            if name in written or target.exists():
                continue
            written.add(name)
            source = sorted(files)[0]
            # One undecodable file must not stop the rest of the batch: it is reported
            # with the others at the end and the cue stays without an asset.
            try:
                rate, ch, seconds, peak = write_mono(source, target, dry)
            except Exception as e:
                missing.append((gun, key, "%s (%s: %s)" % (event, source.name, e)))
                continue
            size = target.stat().st_size if target.exists() else 0
            print("%-13s %-30s %-28s %5d Hz %d ch %6.3f s -> %-30s %6.1f KB"
                  % (gun, event, source.name, rate, ch, seconds, target.name, size / 1024))
    for gun, key, event in missing:
        print("!! %-13s %-24s %s has no decoded file" % (gun, key, event))
    if not dry:
        write_variants()
    return 1 if missing else 0


def write_mono(source: Path, target: Path, dry: bool) -> tuple:
    data, rate = sf.read(source, always_2d=True, dtype="float32")
    mono = data.mean(axis=1)
    peak = float(np.abs(mono).max())
    # An MP3-decoded source can overshoot unity; writing that straight to Vorbis
    # clips on playback.
    if peak > 1.0:
        mono = mono * (0.99 / peak)
    if not dry:
        OUT.mkdir(parents=True, exist_ok=True)
        sf.write(target, mono, rate, format="OGG", subtype="VORBIS")
    return rate, data.shape[1], len(mono) / rate, peak


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("guns", nargs="+")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--cues", action="store_true",
                    help="install the clip-cued sounds (reload, draw, silencer) instead of the shots")
    args = ap.parse_args()
    if args.cues:
        return install_cues(args.guns, args.dry_run)

    guns = json.loads(WEAPONS.read_text("utf-8"))["Guns"]
    mapping = events()
    missing = []
    for gun in args.guns:
        spec = guns.get(gun)
        if spec is None:
            raise SystemExit("no CS2 weapon data for %s" % gun)
        silenced_ok = spec["SilencerType"] != "WEAPONSILENCER_NONE"
        for key, stem in TARGETS.items():
            event = (spec.get("ShootSounds") or {}).get(key)
            if event is None:
                continue
            # The M4A4 inherits the M4A1 prefab's silenced entry and never uses it.
            if key == "WEAPON_SOUND_SPECIAL1" and not silenced_ok:
                continue
            entry = mapping.get(event)
            # decoded_files are written relative to all_weapons/, e.g.
            # "05_audio/decoded/sounds/weapons/ak47/ak47_01.wav".
            files = [AUDIO.parent / f for f in (entry or {}).get("decoded_files", [])]
            files = [f for f in files if f.exists()]
            if not files:
                missing.append((gun, key, event))
                continue
            for i, source in enumerate(sorted(files), 1):
                name = stem.format(gun=gun)
                target = OUT / ("%s.ogg" % name if key in SINGLE_FILE else "%s_%d.ogg" % (name, i))
                rate, ch, seconds, peak = write_mono(source, target, args.dry_run)
                size = target.stat().st_size if target.exists() else 0
                print("%-13s %-24s %-28s %5d Hz %d ch %6.3f s -> %-26s %6.1f KB"
                      % (gun, event, source.name, rate, ch, seconds, target.name, size / 1024))
                if key in SINGLE_FILE:
                    break
    if not args.dry_run:
        write_variants()
    for gun, key, event in missing:
        print("!! %-13s %-24s %s has no decoded file" % (gun, key, event))
    return 1 if missing else 0


def write_variants():
    """Count the numbered files each cue ships, from the OGGs on disk.

    This used to be a table in SubsystemScGunBlockBehavior, so installing a gun's
    sounds needed a code edit too, and a count that disagreed with the files asked
    for one that is not there.
    """
    import collections
    import re
    counts = collections.Counter()
    for f in OUT.glob("*.ogg"):
        m = re.match(r"(.+)_(\d+)\.ogg$", f.name)
        if m:
            counts[m.group(1)] = max(counts[m.group(1)], int(m.group(2)))
    target = ROOT / "src/ScCsgoKnives/AnimationData/cs2_sound_variants.json"
    target.write_text(json.dumps({
        "Format": "ScCsgoKnives.SoundVariants/1",
        "Notes": ("How many numbered files each cue ships, counted from the installed "
                  "OGGs rather than kept in a table."),
        "Variants": dict(sorted(counts.items())),
    }, ensure_ascii=False, indent=1), "utf-8")
    print("\nwrote cs2_sound_variants.json: %d cues" % len(counts))


if __name__ == "__main__":
    raise SystemExit(main())
