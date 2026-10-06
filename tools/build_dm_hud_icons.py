"""CS2's own equipment icons for the deathmatch buy wheel (2026-10-06; the user: the wheel's pixel icons looked CSMC-like,
"可以用cs2自己的", and "刀具现在只有刺刀有HUD").

Run on Windows: ./tools/dev.ps1 python tools/build_dm_hud_icons.py
Source (read only, extracted from the installed CS2 pak01 VPK by an earlier recorded import):
  .tmp/cs2-hud-weapons-20260919/panorama/images/icons/equipment/*.svg   CS2's HUD silhouettes of guns, knives, grenades, taser
Each SVG is rasterised smooth (anti-aliased alpha, white), cropped to its shape and scaled to a fixed height: the icon CS2
itself draws in its HUD and buy menu, not the pixel reduction of tools/build_dm_pixel_icons.py (which the kill feed and the
equipment rows keep). Output: src/ScCsgoDeathmatch/Assets/Textures/ScCsgoDeathmatch/hud/<name>.png and the provenance
record docs/tasks/deathmatch-addon-hud-icons-20261006.json (source hash -> icon hash), read by tools/deathmatch_140.py.
Only the icons the wheel can show are built: every gun, every knife of the core, the throwables and the taser.
"""
import hashlib, io, json, sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / ".tmp/skin-bake-deps"))
import resvg_py
from PIL import Image

EQUIPMENT = ROOT / ".tmp/cs2-hud-weapons-20260919/panorama/images/icons/equipment"
OUT = ROOT / "src/ScCsgoDeathmatch/Assets/Textures/ScCsgoDeathmatch/hud"
RECORD = ROOT / "docs/tasks/deathmatch-addon-hud-icons-20261006.json"
HEIGHT = 64   # drawn 32 GUI units high in the wheel: twice that keeps it sharp on a scaled HUD
GUNS = ["ak47", "aug", "awp", "bizon", "cz75a", "deagle", "elite", "famas", "fiveseven", "g3sg1", "galilar", "glock", "m249", "m4a1", "m4a1_silencer",
        "mac10", "mag7", "mp5sd", "mp7", "mp9", "negev", "nova", "p2000", "p250", "p90", "revolver", "sawedoff", "scar20", "sg556", "ssg08", "taser", "tec9",
        "ump45", "usp_silencer", "xm1014"]
# the core's knives (CsmcKnifeRig.FrozenKnifeOrder) as CS2 names them; the same table as DmPx.s_knifeSprite
KNIVES = ["bayonet", "knife", "knife_t", "knife_karambit", "knife_m9_bayonet", "knife_butterfly", "knife_survival_bowie", "knife_canis", "knife_cord", "knife_css",
          "knife_falchion", "knife_flip", "knife_gut", "knife_kukri", "knife_gypsy_jackknife", "knife_outdoor", "knife_push", "knife_skeleton", "knife_stiletto",
          "knife_tactical", "knife_widowmaker", "knife_ursus"]
GRENADES = ["hegrenade", "flashbang", "smokegrenade", "molotov", "incgrenade", "decoy"]

def sha(b): return hashlib.sha256(b).hexdigest()
def smooth(svg: Path, height: int) -> Image.Image:
    # rendered at 4x and reduced with an area filter: the edge alpha stays soft, the shape stays exact
    big = Image.open(io.BytesIO(bytes(resvg_py.svg_to_bytes(svg_path=str(svg), height=height * 4, skip_system_fonts=True)))).convert("RGBA")
    box = big.getbbox()
    if box: big = big.crop(box)
    w = max(1, round(big.width * height / big.height))
    alpha = big.getchannel("A").resize((w, height), Image.BOX)
    img = Image.new("RGBA", (w, height), (255, 255, 255, 0)); img.putalpha(alpha)
    return img

OUT.mkdir(parents=True, exist_ok=True)
records = []
for kind, names in [("gun", GUNS), ("knife", KNIVES), ("grenade", GRENADES)]:
    for name in names:
        svg = EQUIPMENT / f"{name}.svg"; assert svg.exists(), svg
        img = smooth(svg, HEIGHT); dst = OUT / f"{name}.png"; img.save(dst, optimize=True)
        records.append(dict(kind=kind, name=name, source=svg.relative_to(ROOT).as_posix(), sourceSha256=sha(svg.read_bytes()), size=list(img.size),
                            target=dst.relative_to(ROOT).as_posix(), sha256=sha(dst.read_bytes())))
stale = sorted(p.name for p in OUT.glob("*.png") if p.name not in {Path(r["target"]).name for r in records})
assert not stale, f"unrecorded icons in {OUT}: {stale}"
# a contact sheet on a dark panel for looking at the result
cols, cell = 8, (220, HEIGHT + 12)
sheet = Image.new("RGBA", (cols * cell[0], ((len(records) + cols - 1) // cols) * cell[1]), (28, 30, 34, 255))
for i, r in enumerate(records):
    im = Image.open(ROOT / r["target"]); sheet.alpha_composite(im.crop((0, 0, min(im.width, cell[0] - 8), im.height)), ((i % cols) * cell[0] + 4, (i // cols) * cell[1] + 6))
preview = ROOT / ".tmp/dev-temp/dm-hud-icons-20261006/sheet.png"; preview.parent.mkdir(parents=True, exist_ok=True); sheet.convert("RGB").save(preview)
RECORD.write_text(json.dumps(dict(method=f"rasterised at 4x, area-reduced to {HEIGHT} px high; white with anti-aliased alpha", records=records), ensure_ascii=False, indent=1) + "\n", "utf8")
print(len(records), "icons;", sum((ROOT / r["target"]).stat().st_size for r in records), "bytes; sheet", preview)
