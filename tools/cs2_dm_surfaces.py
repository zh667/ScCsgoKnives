"""CS2's surface penetration modifiers for the deathmatch package (round 6, 2026-10-05; the user chose "按方块材质+厚度",
then "木制的都设为容易穿吧"), read-only on the CS2 install.

Run on Windows: ./tools/dev.ps1 python tools/cs2_dm_surfaces.py
Source: the installed CS2 pak01 VPK through the Source2Viewer CLI the other imports use:
  scripts/surfaceproperties_game.txt   every surface's bulletPenetrationDistanceModifier / bulletPenetrationDamageModifier
                                       (a surface that names neither keeps "default"'s, as the file's first entry is the base)
Output: src/ScCsgoDeathmatch/Data/dm_cs2_surfaces.json (the names and both modifiers, as read)
Record: docs/tasks/deathmatch-cs2-surfaces-20261005-assets.json (VPK identity, source and output hashes)
"""
import hashlib, json, re, subprocess
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
CS2 = Path(r"E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive")
VPK = CS2 / "game/csgo/pak01_dir.vpk"
CLI = next(p for p in [ROOT / ".tmp/vrf-cli/Source2Viewer-CLI.exe", Path(r"E:\CSMCReverse-Tools\ValveResourceFormat\CLI\bin\Release\Source2Viewer-CLI.exe")] if p.exists())
WORK = ROOT / ".tmp/dev-temp/cs2-surface-20261005"
MEMBER = "scripts/surfaceproperties_game.txt"
OUT = ROOT / "src/ScCsgoDeathmatch/Data/dm_cs2_surfaces.json"
RECORD = ROOT / "docs/tasks/deathmatch-cs2-surfaces-20261005-assets.json"
def sha(p): return hashlib.sha256(Path(p).read_bytes()).hexdigest()

if __name__ == "__main__":
    WORK.mkdir(parents=True, exist_ok=True)
    r = subprocess.run([str(CLI), "-i", str(VPK), "-o", str(WORK), "-d", "--vpk_filepath", MEMBER], capture_output=True, text=True, encoding="utf-8", errors="replace")
    assert r.returncode == 0, r.stderr[-400:]
    src = WORK / MEMBER; text = src.read_text("utf-8", errors="replace")
    blocks = re.findall(r"\{([^{}]*)\}", text[text.index("SurfacePropertiesList"):])
    surfaces = {}
    for b in blocks:
        name = re.search(r'surfacePropertyName\s*=\s*"([^"]+)"', b)
        if not name: continue
        d = re.search(r"bulletPenetrationDistanceModifier\s*=\s*(-?[\d.]+)", b); m = re.search(r"bulletPenetrationDamageModifier\s*=\s*(-?[\d.]+)", b)
        surfaces[name.group(1)] = {"distance": float(d.group(1)) if d else None, "damage": float(m.group(1)) if m else None}
    base = surfaces["default"]; assert base["distance"] is not None and base["damage"] is not None
    for s in surfaces.values():
        s["distance"] = base["distance"] if s["distance"] is None else s["distance"]; s["damage"] = base["damage"] if s["damage"] is None else s["damage"]
    for need in ["default", "Wood", "glass", "concrete", "brick", "rock", "metal", "solidmetal", "dirt", "sand", "gravel", "snow", "ice", "clay", "carpet", "flesh", "foliage", "watermelon", "cardboard"]:
        assert need in surfaces, need
    result = {"Format": "ScCsgoDeathmatch.Cs2Surfaces/1", "Source": {"vpk": "game/csgo/pak01_dir.vpk", "vpkSha256": sha(VPK), "member": MEMBER, "memberSha256": sha(src),
              "steam": (CS2 / "game/csgo/steam.inf").read_text("utf-8", errors="replace").splitlines()[:3]},
              "Note": "a surface that names neither modifier keeps default's (the file's first entry)", "Surfaces": surfaces}
    OUT.write_text(json.dumps(result, ensure_ascii=False, indent=1), "utf-8")
    RECORD.write_text(json.dumps({"output": OUT.relative_to(ROOT).as_posix(), "sha256": sha(OUT), "bytes": OUT.stat().st_size, "source": result["Source"]}, ensure_ascii=False, indent=1), "utf-8")
    print(len(surfaces), "surfaces ->", OUT, sha(OUT)[:16]); print({k: surfaces[k] for k in ["Wood", "glass", "concrete", "metal", "solidmetal", "flesh"]})
