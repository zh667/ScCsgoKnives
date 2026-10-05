"""Pixel-art icons for the deathmatch package (deathmatch-addon round 2, R2-6: "像素风的CS2风格，类似于CSMC那种").

Run on Windows: ./tools/dev.ps1 python tools/build_dm_pixel_icons.py
Sources (read only, already extracted from the installed CS2 pak01 VPK by earlier recorded imports):
  .tmp/cs2-hud-weapons-20260919/panorama/images/icons/equipment/*.svg   CS2's own equipment silhouettes (guns, knives,
                                                                        grenades, taser, armour)
  .tmp/dev-temp/deathmatch-20261003/cs2/panorama/images/hud/deathnotice/*.svg   CS2's kill-method icons
Each SVG is rasterised large, cropped to its shape, reduced to a small pixel height with an area filter and then cut at
half coverage, so every pixel is fully on or off: the silhouette becomes a crisp pixel sprite (white, alpha only) that the
game scales by whole pixels with point sampling. Output: src/ScCsgoDeathmatch/Assets/Textures/ScCsgoDeathmatch/px/*.png
and the provenance record docs/tasks/deathmatch-addon-pixel-icons-20261003.json (source hash -> sprite hash).
"""
import hashlib, io, json, sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / ".tmp/skin-bake-deps"))
import numpy as np, resvg_py
from PIL import Image

EQUIPMENT = ROOT / ".tmp/cs2-hud-weapons-20260919/panorama/images/icons/equipment"
DEATHNOTICE = ROOT / ".tmp/dev-temp/deathmatch-20261003/cs2/panorama/images/hud/deathnotice"
OUT = ROOT / "src/ScCsgoDeathmatch/Assets/Textures/ScCsgoDeathmatch/px"
MARKS = {"headshot": "icon_headshot", "noscope": "noscope", "smoke": "smoke_kill", "penetrate": "penetrate", "blind": "blind_kill", "suicide": "icon_suicide"}
# equipment icons are drawn this many pixels high; kill marks a little smaller (they sit beside a weapon in the feed)
WEAPON_H, MARK_H, GEAR_H = 14, 12, 14

def sha(b): return hashlib.sha256(b).hexdigest()
def pixel(svg: Path, height: int) -> Image.Image:
    big = Image.open(io.BytesIO(bytes(resvg_py.svg_to_bytes(svg_path=str(svg), height=height * 16, skip_system_fonts=True)))).convert("RGBA")
    box = big.getbbox()
    if box: big = big.crop(box)
    w = max(1, round(big.width * height / big.height))
    a = np.asarray(big.getchannel("A").resize((w, height), Image.BOX), dtype=np.float32) / 255.0
    on = (a >= .45).astype(np.uint8) * 255
    img = Image.new("RGBA", (w, height), (255, 255, 255, 0)); img.putalpha(Image.fromarray(on, "L"))
    return img

OUT.mkdir(parents=True, exist_ok=True)
records = []
for svg in sorted(EQUIPMENT.glob("*.svg")):
    stem = svg.stem
    h = GEAR_H if any(k in stem for k in ("armor", "helmet", "kevlar", "defuser", "assaultsuit")) else WEAPON_H
    img = pixel(svg, h); dst = OUT / f"{stem}.png"; img.save(dst)
    records.append(dict(kind="equipment", name=stem, source=svg.relative_to(ROOT).as_posix(), sourceSha256=sha(svg.read_bytes()), size=list(img.size), target=dst.relative_to(ROOT).as_posix(), sha256=sha(dst.read_bytes())))
for name, stem in MARKS.items():
    svg = DEATHNOTICE / f"{stem}.svg"; img = pixel(svg, MARK_H); dst = OUT / f"kill_{name}.png"; img.save(dst)
    records.append(dict(kind="kill-mark", name=name, source=svg.relative_to(ROOT).as_posix(), sourceSha256=sha(svg.read_bytes()), size=list(img.size), target=dst.relative_to(ROOT).as_posix(), sha256=sha(dst.read_bytes())))
# a contact sheet at 4x (point upscale) for looking at the result
cols, cell = 8, (90 * 4, 18 * 4 + 8)
sheet = Image.new("RGBA", (cols * cell[0], ((len(records) + cols - 1) // cols) * cell[1]), (28, 30, 34, 255))
for i, r in enumerate(records):
    im = Image.open(ROOT / r["target"]); im = im.resize((im.width * 4, im.height * 4), Image.NEAREST)
    sheet.alpha_composite(im.crop((0, 0, min(im.width, cell[0] - 4), im.height)), ((i % cols) * cell[0] + 2, (i // cols) * cell[1] + 2))
preview = ROOT / ".tmp/dev-temp/deathmatch-round2-20261003/pixel-icons-sheet.png"; preview.parent.mkdir(parents=True, exist_ok=True); sheet.convert("RGB").save(preview)
(ROOT / "docs/tasks/deathmatch-addon-pixel-icons-20261003.json").write_text(json.dumps(dict(method="rasterised at 16x, area-reduced to the target height, cut at 45% coverage; white with binary alpha", records=records), ensure_ascii=False, indent=1) + "\n", "utf8")
print(len(records), "sprites;", sorted(r["name"] for r in records if r["kind"] == "equipment"))
print("sheet", preview)
