#!/usr/bin/env python3
"""Install the CS2 cues the mod was dropping for want of an OGG.

cs2_sounds.json carried 69 cues, 34 of which resolved to no shipped file and were
discarded at load. Those 34 need only six source files:

    movement1.wav          AWP/M4A1 WeaponMove1, M4A1 SilencerWeaponMove1      4 cues
    movement2.wav          AK47/AWP/M4A1 WeaponMove2                           8 cues
    movement3.wav          AK47 WeaponMove1 and 3, AWP/M4A1 3, Silencer 3     17 cues
    ak47_addammo_02.wav    AK47 AddAmmo                                        1 cue
    ak47_inspect_f245.wav  AK47 Inspect_F245                                   3 cues
    m4a1_addammo_01.mp3    M4A1 AddAmmo                                        1 cue

The last one is why the export looked short: the whole m4a1 folder decoded to MP3,
not WAV, so a .wav-only filter reported "no source". There is a source.

Output names are chosen so cs2_sound_timings.py's existing resolver finds them with
no new special-casing: the movement files keep their own stem, ak47_addammo drops the
variant index, and m4a1s_addammo matches on gun-plus-cue.

Everything is written mono - the engine's OGG length only comes out right for one
channel - at libsndfile's default Vorbis quality.

Usage:  python3 tools/install_cs2_sounds.py [--dry-run]
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
import soundfile as sf

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "src/ScCsgoKnives/Assets/Audio/ScCsgoKnives"
SRC = (Path.home() / "workspaces/CSMCReverse/local_cs2_analysis/all_weapons"
       / "05_audio/decoded/sounds/weapons")

INSTALL = [
    ("movement1.wav", "movement1.ogg"),
    ("movement2.wav", "movement2.ogg"),
    ("movement3.wav", "movement3.ogg"),
    ("ak47/ak47_addammo_02.wav", "ak47_addammo.ogg"),
    ("ak47/ak47_inspect_f245.wav", "ak47_inspect_f245.ogg"),
    ("m4a1/m4a1_addammo_01.mp3", "m4a1s_addammo.ogg"),
]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    OUT.mkdir(parents=True, exist_ok=True)
    for source, target in INSTALL:
        path = SRC / source
        if not path.exists():
            raise SystemExit("missing source: %s" % path)
        data, rate = sf.read(path, always_2d=True, dtype="float32")
        mono = data.mean(axis=1)
        peak = float(np.abs(mono).max())
        # MP3 decoding overshoots unity (m4a1_addammo_01 peaks at 1.077); writing that
        # straight to Vorbis clips on playback. Scale only when it actually exceeds.
        scaled = peak > 1.0
        if scaled:
            mono = mono * (0.99 / peak)
        out = OUT / target
        if not args.dry_run:
            sf.write(out, mono, rate, format="OGG", subtype="VORBIS")
        size = out.stat().st_size if out.exists() else 0
        print("%-28s %5d Hz  %2d ch -> mono  %6.3f s  peak %.3f%s  ->  %-24s %6.1f KB"
              % (source, rate, data.shape[1], len(mono) / rate, peak,
                 " (scaled to 0.99)" if scaled else "", target, size / 1024))


if __name__ == "__main__":
    main()
