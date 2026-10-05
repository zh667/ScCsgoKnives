"""Import CS2 sources for the protection feedback of current-direction-20260929 §2/§4, read-only.

Run on Windows: ./tools/dev.ps1 python tools/import_cs2_armor_feedback.py
Sources (local CS2 extractions, never modified):
  .tmp/hud-armor-mp-20260929/cs2/sounds/player/headshot_armor_e1.wav   the helmet "dink" CS2's DamageHeadShotArmor events use
  .tmp/hud-armor-mp-20260929/cs2/panorama/images/hud/armor.svg, .../icons/equipment/helmet.svg   HUD protection icons
  E:/projects/CSMCReverse/local_cs2_analysis/all_weapons/06_particles/textures/materials/particle/sparks/sparks_seq{0,3}.png
  E:/projects/CSMCReverse/local_cs2_analysis/all_weapons/06_particles/textures/materials/effects/yellowflare.png
    (the textures impact_helmet_headshot*.vpcf reference: sparks.vtex sequences 0-1 / 3-4, yellowflare.vtex)
Outputs (members of both core packages; Full PNG, Lite WebP for textures):
  Assets/Audio/ScCsgoKnives/Hits/helmet_dink.ogg            mono Ogg Vorbis, level unchanged (the game sets volumes)
  Assets/Textures/ScCsgoKnives/Hits/helmet_spark.{png,webp} 128x128 2x2 atlas: streak, flare, streak, small flare
  Assets/Textures/ScCsgoKnives/hud_armor.{png,webp}, hud_helmet.{png,webp}  96x96 white silhouettes (tinted in game)
Record: docs/tasks/current-direction-20260929-armor-feedback-assets.json (source and output hashes). The earlier
headshot_armor.ogg (from headshot_armor_01, another CS2 event layer) is retired from the packages by the pipeline.
"""
import hashlib, io, json, sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / ".tmp/skin-bake-deps"))
import numpy as np, soundfile as sf
import resvg_py
from PIL import Image

CS2 = ROOT / ".tmp/hud-armor-mp-20260929/cs2"
PARTICLES = Path(r"E:/projects/CSMCReverse/local_cs2_analysis/all_weapons/06_particles/textures/materials")
FULL = ROOT / "src/ScCsgoKnives/Assets"; LITE = ROOT / "src/ScCsgoKnives/AssetsLite"
def sha(p): return hashlib.sha256(Path(p).read_bytes()).hexdigest()
records = []
def record(name, sources, target, member, edition):
    records.append(dict(name=name, sources={Path(s).as_posix(): sha(s) for s in sources}, target=target.relative_to(ROOT).as_posix(),
                        member=member, sha256=sha(target), bytes=target.stat().st_size, edition=edition))

# 1. The dink (mono: average when the channels agree, else the louder channel, as the earlier hit sounds).
src = CS2 / "sounds/player/headshot_armor_e1.wav"
data, rate = sf.read(src, always_2d=True)
corr = float(np.corrcoef(data[:, 0], data[:, 1])[0, 1]) if data.shape[1] > 1 else 1.0
mono = data.mean(axis=1) if corr >= .5 else data[:, int(np.argmax((data ** 2).mean(axis=0)))]
dst = FULL / "Audio/ScCsgoKnives/Hits/helmet_dink.ogg"; dst.parent.mkdir(parents=True, exist_ok=True)
sf.write(dst, mono, rate, format="OGG", subtype="VORBIS")
back, back_rate = sf.read(dst); assert back_rate == rate and abs(len(back) - len(mono)) <= rate // 100
record("helmet_dink", [src], dst, "Assets/Audio/ScCsgoKnives/Hits/helmet_dink.ogg", "both")
records[-1].update(channels=int(data.shape[1]), channelCorrelation=round(corr, 4), rate=int(rate), seconds=round(len(mono) / rate, 4),
                   peak=round(float(np.abs(mono).max()), 4), rms=round(float(np.sqrt((mono ** 2).mean())), 4))

def save(image, rel, name, sources):
    full = FULL / "Textures/ScCsgoKnives" / (rel + ".png"); full.parent.mkdir(parents=True, exist_ok=True); image.save(full, optimize=True)
    record(name, sources, full, "Assets/Textures/ScCsgoKnives/" + rel + ".png", "full")
    lite = LITE / "Textures/ScCsgoKnives" / (rel + ".webp"); lite.parent.mkdir(parents=True, exist_ok=True); image.save(lite, "WEBP", lossless=True, method=6)
    record(name, sources, lite, "Assets/Textures/ScCsgoKnives/" + rel + ".webp", "lite")

# 2. The spark atlas: premultiplied onto black for additive blending (black adds nothing).
def cell(path, size, keep_aspect=True):
    im = Image.open(path).convert("RGBA")
    if keep_aspect: im.thumbnail((size, size), Image.Resampling.LANCZOS)
    else: im = im.resize((size, size), Image.Resampling.LANCZOS)
    rgb = np.asarray(im).astype(np.float32); a = rgb[..., 3:4] / 255
    out = np.zeros((size, size, 4), np.float32)
    ox, oy = (size - im.width) // 2, (size - im.height) // 2
    out[oy:oy + im.height, ox:ox + im.width, :3] = rgb[..., :3] * a
    out[..., 3] = 255
    return Image.fromarray(out.clip(0, 255).astype(np.uint8), "RGBA")
streak0, streak3, flare = PARTICLES / "particle/sparks/sparks_seq0.png", PARTICLES / "particle/sparks/sparks_seq3.png", PARTICLES / "effects/yellowflare.png"
atlas = Image.new("RGBA", (128, 128), (0, 0, 0, 255))
# Streaks keep their narrow aspect (sparks_seq0 is 12x58, seq3 23x49): the game's quad width covers the whole cell.
atlas.paste(cell(streak0, 64), (0, 0)); atlas.paste(cell(flare, 64), (64, 0)); atlas.paste(cell(streak3, 64), (0, 64)); atlas.paste(cell(flare, 40), (76, 76))
save(atlas, "Hits/helmet_spark", "helmet_spark", [streak0, streak3, flare])

# 3. HUD icons: the CS2 silhouettes in white on transparent, fitted to 96x96 without distortion: the HUD body-armour
#    shield for the body protection and the equipment helmet for the head protection (armor_helmet.svg is CS2's combined
#    "armour with helmet" badge, not a head-only mark).
for f in [FULL / "Textures/ScCsgoKnives/hud_armor_helmet.png", LITE / "Textures/ScCsgoKnives/hud_armor_helmet.webp"]:
    if f.exists(): f.unlink()  # an earlier run's combined badge
for name, svg in [("armor", CS2 / "panorama/images/hud/armor.svg"), ("helmet", CS2 / "panorama/images/icons/equipment/helmet.svg")]:
    raster = Image.open(io.BytesIO(resvg_py.svg_to_bytes(svg_path=str(svg), width=384, skip_system_fonts=True))).convert("RGBA")
    box = raster.getbbox(); raster = raster.crop(box) if box else raster
    raster.thumbnail((88, 88), Image.Resampling.LANCZOS)
    alpha = raster.split()[3]; white = Image.new("RGBA", raster.size, (255, 255, 255, 0)); white.putalpha(alpha)
    icon = Image.new("RGBA", (96, 96), (255, 255, 255, 0)); icon.alpha_composite(white, ((96 - raster.width) // 2, (96 - raster.height) // 2))
    save(icon, "hud_" + name, "hud_" + name, [svg])

out = ROOT / "docs/tasks/current-direction-20260929-armor-feedback-assets.json"
out.write_text(json.dumps(records, indent=1, ensure_ascii=False) + "\n", "utf8")
print(json.dumps(records, indent=1, ensure_ascii=False))
