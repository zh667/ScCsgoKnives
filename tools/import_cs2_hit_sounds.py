"""Import CS2's bullet-hit feedback sounds (headshot-armor-balance-20260929 H4) from the local read-only VPK export.

Run on Windows: ./tools/dev.ps1 python tools/import_cs2_hit_sounds.py
Source: .tmp/armor-plan-20260929/cs2-audio/sounds/player/{headshot_noarmor_01,headshot_armor_01,kevlar1}.wav, decoded
read-only from sounds/player/*.vsnd_c of the local CS2 install during the planning pass (the install is untouched).
Output: mono Ogg Vorbis (the engine positions only mono sounds) in src/ScCsgoKnives/Assets/Audio/ScCsgoKnives/Hits and
the provenance record docs/tasks/headshot-armor-balance-20260929-sounds.json. Levels are not changed here; the game
sets the playback volume. Uses .tmp/skin-bake-deps (numpy, soundfile), as the other CS2 audio imports do.
"""
import hashlib, json, sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / ".tmp/skin-bake-deps"))
import numpy as np, soundfile as sf

SOURCE = ROOT / ".tmp/armor-plan-20260929/cs2-audio/sounds/player"
TARGET = ROOT / "src/ScCsgoKnives/Assets/Audio/ScCsgoKnives/Hits"
SOUNDS = {"headshot_noarmor": "headshot_noarmor_01.wav", "headshot_armor": "headshot_armor_01.wav", "kevlar": "kevlar1.wav"}

def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()

TARGET.mkdir(parents=True, exist_ok=True); records = []
for name, file in SOUNDS.items():
    src = SOURCE / file
    data, rate = sf.read(src, always_2d=True)
    # Averaging cancels weakly or anti-correlated stereo (headshot_armor_01: -0.18, -40% RMS); keep the louder channel then.
    corr = float(np.corrcoef(data[:, 0], data[:, 1])[0, 1]) if data.shape[1] > 1 else 1.0
    mono = data.mean(axis=1) if corr >= .5 else data[:, int(np.argmax((data ** 2).mean(axis=0)))]
    dst = TARGET / (name + ".ogg")
    sf.write(dst, mono, rate, format="OGG", subtype="VORBIS")
    back, back_rate = sf.read(dst)
    assert back_rate == rate and abs(len(back) - len(mono)) <= rate // 100, (name, len(back), len(mono))
    records.append(dict(name=name, source=src.relative_to(ROOT).as_posix(), sourceSha256=sha(src), sourceChannels=int(data.shape[1]), channelCorrelation=round(corr, 4),
                        rate=int(rate), seconds=round(len(mono) / rate, 4), peak=round(float(np.abs(mono).max()), 4),
                        target=dst.relative_to(ROOT).as_posix(), member="Assets/Audio/ScCsgoKnives/Hits/" + name + ".ogg",
                        sha256=sha(dst), bytes=dst.stat().st_size, decodedSeconds=round(len(back) / back_rate, 4)))
out = ROOT / "docs/tasks/headshot-armor-balance-20260929-sounds.json"
out.write_text(json.dumps(records, indent=1, ensure_ascii=False), "utf8")
print(json.dumps(records, indent=1, ensure_ascii=False))
