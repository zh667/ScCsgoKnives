"""Import CS2's armour HUD badges for the CS armour HUD (deathmatch round 4, 2026-10-04; the user: "头盔图标在护甲图标上面，
数值一起显示，有头甲就显示，没头甲就不显示（护甲同理），全部仿照CS2即可，可以直接去看本地Cs2资源"), read-only.

Run on Windows: ./tools/dev.ps1 python tools/import_cs2_armor_hud.py
Source: the installed CS2 pak01 VPK through the Source2Viewer CLI the other imports use:
  panorama/images/hud/armor.vsvg_c          the body-armour shield CS2's hud-HA-armor panel shows without a helmet
  panorama/images/hud/armor_helmet.vsvg_c   the same shield with the helmet on it, shown with a helmet
  (panorama/layout/hud/hudhealthammocenter.vxml_c and styles/hud/hudhealthammocenter.vcss_c are exported beside them
  as the evidence of how CS2 uses the two: one badge, the armour value, the panel hidden without armour)
Outputs (members of both core packages; Full PNG, Lite WebP), in CS2's own colours (white rim, dark shield), 96x96:
  Assets/Textures/ScCsgoKnives/hud_cs2_armor.{png,webp}, hud_cs2_armor_helmet.{png,webp}
Record: docs/tasks/armor-hud-cs2-20261004-assets.json (source and output hashes, the VPK's identity).
"""
import hashlib, io, json, subprocess, sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / ".tmp/skin-bake-deps"))
import resvg_py
from PIL import Image

CS2 = Path(r"E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive")
VPK = CS2 / "game/csgo/pak01_dir.vpk"
CLI = next(p for p in [ROOT / ".tmp/vrf-cli/Source2Viewer-CLI.exe", Path(r"E:\CSMCReverse-Tools\ValveResourceFormat\CLI\bin\Release\Source2Viewer-CLI.exe")] if p.exists())
WORK = ROOT / ".tmp/cs2-armor-hud-20261004"
FULL = ROOT / "src/ScCsgoKnives/Assets"; LITE = ROOT / "src/ScCsgoKnives/AssetsLite"
RECORD = ROOT / "docs/tasks/armor-hud-cs2-20261004-assets.json"
def sha(p): return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def export(member):
    r = subprocess.run([str(CLI), "-i", str(VPK), "-o", str(WORK), "-d", "--vpk_filepath", member], capture_output=True, text=True, encoding="utf-8", errors="replace")
    assert r.returncode == 0, (member, r.stderr[-400:])
    print(member, "->", r.stdout.strip().splitlines()[-1:] if r.stdout.strip() else "(no output)", flush=True)

# the CLI writes a single export AS the output path when that folder does not exist yet: make the folder first
# (an earlier run of this script left such a file there)
if WORK.is_file(): WORK.unlink()
WORK.mkdir(parents=True, exist_ok=True); print("cli", CLI, flush=True)
for m in ["panorama/images/hud/armor.vsvg_c", "panorama/images/hud/armor_helmet.vsvg_c", "panorama/layout/hud/hudhealthammocenter.vxml_c", "panorama/styles/hud/hudhealthammocenter.vcss_c"]:
    export(m)
print(sorted(p.relative_to(WORK).as_posix() for p in WORK.rglob("*") if p.is_file()), flush=True)
css = (WORK / "panorama/styles/hud/hudhealthammocenter.css").read_text("utf-8")
# the rules this HUD follows, checked in CS2's own stylesheet: hidden without armour, the helmet badge only with a helmet
assert ".hud-HA-armor" in css and ".HUD--has-armor .hud-HA-armor" in css and ".hud-HA-main:not(.HUD--has-helmet) .hud-HA-icon--helmet" in css

records = []
def save(image, rel, name, svg):
    for edition, folder, ext in [("full", FULL, "png"), ("lite", LITE, "webp")]:
        target = folder / "Textures/ScCsgoKnives" / f"{rel}.{ext}"; target.parent.mkdir(parents=True, exist_ok=True)
        if ext == "png": image.save(target, optimize=True)
        else: image.save(target, "WEBP", lossless=True, method=6)
        records.append(dict(name=name, sources={svg.relative_to(ROOT).as_posix(): sha(svg)}, target=target.relative_to(ROOT).as_posix(),
                            member=f"Assets/Textures/ScCsgoKnives/{rel}.{ext}", sha256=sha(target), bytes=target.stat().st_size, edition=edition))

for name, svg in [("hud_cs2_armor", WORK / "panorama/images/hud/armor.svg"), ("hud_cs2_armor_helmet", WORK / "panorama/images/hud/armor_helmet.svg")]:
    raster = Image.open(io.BytesIO(resvg_py.svg_to_bytes(svg_path=str(svg), width=384, skip_system_fonts=True))).convert("RGBA")
    box = raster.getbbox(); raster = raster.crop(box) if box else raster
    raster.thumbnail((92, 92), Image.Resampling.LANCZOS)
    icon = Image.new("RGBA", (96, 96), (0, 0, 0, 0)); icon.alpha_composite(raster, ((96 - raster.width) // 2, (96 - raster.height) // 2))
    save(icon, name, name, svg)

identity = {"pak01_dir.vpk": sha(VPK), "steam.inf": (CS2 / "game/csgo/steam.inf").read_text("utf-8", errors="replace").splitlines()[:3]}
RECORD.write_text(json.dumps(records, ensure_ascii=False, indent=1), "utf-8")
(WORK / "identity.json").write_text(json.dumps(identity, ensure_ascii=False, indent=1), "utf-8")
print(json.dumps({"records": [(r["member"], r["bytes"], r["sha256"][:16]) for r in records], "identity": identity}, ensure_ascii=False, indent=1))
