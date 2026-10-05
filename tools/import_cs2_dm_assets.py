"""CS2 presentation resources for the deathmatch package (deathmatch-addon tasks 4.8 / 7.7).

Run on Windows: ./tools/dev.ps1 python tools/import_cs2_dm_assets.py
Reads the installed CS2 pak01 VPK read-only through the Source2Viewer CLI already used by the other imports, into
.tmp/dev-temp/deathmatch-20261003/cs2 (never the install, never the repository), and derives:

  kill-method icons   panorama/images/hud/deathnotice/{icon_headshot,noscope,smoke_kill,penetrate,blind_kill,icon_suicide}
                      -> Assets/Textures/ScCsgoDeathmatch/kill_*.png (64 px, the SVG's own shape, white)
  respawn sound       sounds/player/pl_respawn      -> Assets/Audio/ScCsgoDeathmatch/respawn.ogg (mono: positioned in the world)
  match end sound     sounds/ui/deathmatch_end      -> Assets/Audio/ScCsgoDeathmatch/match_end.ogg

and the provenance record docs/tasks/deathmatch-addon-assets-20261003.json (VPK identity, each member's decoded hash and
the derived file's hash). NOT found in the VPK as a resource and therefore not imported: the white spawn-protection look
(CS2 renders it in code); the package draws its own marker and says so.
"""
import hashlib, io, json, subprocess, sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / ".tmp/skin-bake-deps"))
import numpy as np, soundfile as sf, resvg_py
from PIL import Image

CS2 = Path(r"E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive")
VPK = CS2 / "game/csgo/pak01_dir.vpk"
CLI = next(p for p in [ROOT / ".tmp/vrf-cli/Source2Viewer-CLI.exe", Path(r"E:\CSMCReverse-Tools\ValveResourceFormat\CLI\bin\Release\Source2Viewer-CLI.exe")] if p.exists())
WORK = ROOT / ".tmp/dev-temp/deathmatch-20261003/cs2"
ASSETS = ROOT / "src/ScCsgoDeathmatch/Assets"
ICONS = {"headshot": "icon_headshot", "noscope": "noscope", "smoke": "smoke_kill", "penetrate": "penetrate", "blind": "blind_kill", "suicide": "icon_suicide"}
SOUNDS = {"respawn": "sounds/player/pl_respawn", "match_end": "sounds/ui/deathmatch_end"}

def sha(b): return hashlib.sha256(b).hexdigest()
def export(path):
    r = subprocess.run([str(CLI), "-i", str(VPK), "-o", str(WORK), "-d", "--vpk_filepath", path], capture_output=True, text=True, encoding="utf-8", errors="replace")
    if r.returncode != 0: raise SystemExit(f"export of {path} failed: {r.stdout[-400:]} {r.stderr[-400:]}")

WORK.mkdir(parents=True, exist_ok=True)
export("panorama/images/hud/deathnotice")
for path in SOUNDS.values(): export(path + ".vsnd_c")
records = []
textures = ASSETS / "Textures/ScCsgoDeathmatch"; textures.mkdir(parents=True, exist_ok=True)
for name, stem in ICONS.items():
    src = next((WORK / "panorama/images/hud/deathnotice").glob(stem + ".svg"))
    raster = Image.open(io.BytesIO(bytes(resvg_py.svg_to_bytes(svg_path=str(src), width=256, skip_system_fonts=True)))).convert("RGBA")
    box = raster.getbbox(); raster = raster.crop(box) if box else raster
    raster.thumbnail((60, 60), Image.Resampling.LANCZOS)
    # the HUD tints the icon itself: keep the shape (alpha), make the colour white
    alpha = raster.getchannel("A"); white = Image.new("RGBA", raster.size, (255, 255, 255, 0)); white.putalpha(alpha)
    target = Image.new("RGBA", (64, 64)); target.alpha_composite(white, ((64 - white.width) // 2, (64 - white.height) // 2))
    dst = textures / f"kill_{name}.png"; target.save(dst)
    records.append(dict(kind="icon", name=name, source=f"panorama/images/hud/deathnotice/{stem}.vsvg_c", decodedSha256=sha(src.read_bytes()), target=dst.relative_to(ROOT).as_posix(),
                        member=f"Assets/Textures/ScCsgoDeathmatch/kill_{name}.png", sha256=sha(dst.read_bytes()), bytes=dst.stat().st_size, opaquePixels=int(np.count_nonzero(np.asarray(target)[:, :, 3] > 32))))
audio = ASSETS / "Audio/ScCsgoDeathmatch"; audio.mkdir(parents=True, exist_ok=True)
for name, path in SOUNDS.items():
    src = next(p for p in [WORK / (path + ".wav"), WORK / (path + ".mp3")] if p.exists())
    data, rate = sf.read(src, always_2d=True)
    corr = float(np.corrcoef(data[:, 0], data[:, 1])[0, 1]) if data.shape[1] > 1 else 1.0
    mono = data.mean(axis=1) if corr >= .5 else data[:, int(np.argmax((data ** 2).mean(axis=0)))]
    dst = audio / f"{name}.ogg"; sf.write(dst, mono, rate, format="OGG", subtype="VORBIS")
    back, back_rate = sf.read(dst); assert back_rate == rate and abs(len(back) - len(mono)) <= rate // 100
    records.append(dict(kind="sound", name=name, source=path + ".vsnd_c", decoded=src.name, decodedSha256=sha(src.read_bytes()), sourceChannels=int(data.shape[1]), rate=int(rate), seconds=round(len(mono) / rate, 3),
                        peak=round(float(np.abs(mono).max()), 4), target=dst.relative_to(ROOT).as_posix(), member=f"Assets/Audio/ScCsgoDeathmatch/{name}.ogg", sha256=sha(dst.read_bytes()), bytes=dst.stat().st_size))
steam = dict(l.split("=", 1) for l in (CS2 / "game/csgo/steam.inf").read_text("utf8", "replace").splitlines() if "=" in l)
out = dict(cs2=dict(PatchVersion=steam.get("PatchVersion"), ClientVersion=steam.get("ClientVersion"), pak01_dir=dict(bytes=VPK.stat().st_size, sha256=sha(VPK.read_bytes()))), tool=CLI.name, records=records,
           notImported=["the white spawn-protection look: no resource of that name in the VPK (rendered by CS2's code); the package draws its own marker",
                        "buy menu / radial menu / scoreboard panorama layouts: Source 2 UI code that does not run here; the wheel and the board are this package's own widgets"])
(ROOT / "docs/tasks/deathmatch-addon-assets-20261003.json").write_text(json.dumps(out, indent=1, ensure_ascii=False) + "\n", "utf8")
print(json.dumps(out, indent=1, ensure_ascii=False))
