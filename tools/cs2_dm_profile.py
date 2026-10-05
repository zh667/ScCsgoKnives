#!/usr/bin/env python3
"""CS2's weapon numbers for the deathmatch package -> src/ScCsgoDeathmatch/Data/dm_cs2_profile.json.

deathmatch-addon task 4.1. Runs where the CS2 files are (Windows); only this small JSON and the source identities
leave that machine. Every value is the vdata's own number, unconverted, under the vdata's own field name, so the
C# side (DmWeapons) holds every conversion and the evidence table can name the field a number came from:

  guns        the 35 firearm blocks (the same extraction as tools/cs2_weapons.py, which the core's table is made with)
  equipment   weapon_hegrenade / weapon_molotov / weapon_incgrenade / weapon_flashbang / weapon_smokegrenade /
              weapon_decoy / weapon_knife / weapon_taser blocks of the source weapons.vdata, as far as they carry numbers
  source      sha256 of each file read, steam.inf's version lines

What a number MEANS in CS2's code (the armour formula, the recoil pattern generator, the inaccuracy recovery curve,
knife damage, the HE falloff) is not in the vdata; nothing here claims it.

Usage:  python tools/cs2_dm_profile.py --vdata-root <.../01_weapon_data> --cs2 <CS2 install dir> [--out FILE]
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path

import cs2_weapons

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "src/ScCsgoDeathmatch/Data/dm_cs2_profile.json"
GUN_FIELDS = ["m_nDamage", "m_flHeadshotMultiplier", "m_flArmorRatio", "m_flPenetration", "m_flRange", "m_flRangeModifier",
              "m_iMaxClip1", "m_flCycleTime", "m_flCycleTime_second", "m_nNumBullets", "m_bIsFullAuto", "m_bHasBurstMode",
              "m_flCycleTimeWhenInBurstMode", "m_flTimeBetweenBurstShots", "m_flSpread", "m_flInaccuracyStand",
              "m_flInaccuracyCrouch", "m_flInaccuracyMove", "m_flInaccuracyFire", "m_flInaccuracyJump",
              "m_flRecoveryTimeStand", "m_flRecoveryTimeCrouch", "m_flRecoveryTimeStandFinal", "m_nRecoilSeed",
              "m_flRecoilAngle", "m_flRecoilAngleVariance", "m_flRecoilMagnitude", "m_flRecoilMagnitudeVariance",
              "m_flMaxSpeed", "m_nZoomLevels", "m_bUnzoomsAfterShot", "m_flDeployDuration"]
EQUIPMENT = ["weapon_hegrenade", "weapon_molotov", "weapon_incgrenade", "weapon_flashbang", "weapon_smokegrenade",
             "weapon_decoy", "weapon_knife", "weapon_taser"]
EQUIPMENT_FIELDS = ["m_nDamage", "m_flRange", "m_flRangeModifier", "m_flArmorRatio", "m_flHeadshotMultiplier",
                    "m_flPenetration", "m_flCycleTime", "m_iMaxClip1", "m_flMaxSpeed", "m_flThrowVelocity"]


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def numbers(text: str, fields: list[str]) -> dict:
    out = {}
    for name in fields:
        m = re.search(r"%s\s*=\s*\[([^\]]*)\]" % re.escape(name), text)
        if m:
            out[name] = [float(x) for x in m.group(1).split(",") if x.strip()]
            continue
        m = re.search(r"%s\s*=\s*(-?[\d.]+)" % re.escape(name), text)
        if m:
            out[name] = float(m.group(1))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--vdata-root", type=Path, required=True)
    ap.add_argument("--cs2", type=Path, required=True)
    ap.add_argument("--out", type=Path, default=OUT)
    args = ap.parse_args()

    cs2_weapons.VDATA = args.vdata_root / "firearm_blocks"
    files = {}
    guns = {}
    for gun, stem in cs2_weapons.GUNS.items():
        path = cs2_weapons.VDATA / (stem + ".vdata")
        files["firearm_blocks/" + path.name] = sha(path)
        raw = cs2_weapons.read(stem)
        guns[gun] = {"block": stem, **{k: raw[k] for k in GUN_FIELDS if raw.get(k) is not None}}
    source = args.vdata_root / "source/weapons.vdata"
    equipment = {}
    if source.exists():
        files["source/weapons.vdata"] = sha(source)
        for block in EQUIPMENT:
            text = cs2_weapons.prefab_text(block)
            base = re.search(r'_base\s*=\s*"([^"]+)"', text)
            found = numbers(text, EQUIPMENT_FIELDS)
            if base:
                for k, v in numbers(cs2_weapons.prefab_text(base.group(1)), EQUIPMENT_FIELDS).items():
                    found.setdefault(k, v)
            equipment[block] = {"present": bool(text), "base": base.group(1) if base else None, **found}
    steam = {}
    inf = args.cs2 / "game/csgo/steam.inf"
    if inf.exists():
        files["steam.inf"] = sha(inf)
        for line in inf.read_text("utf-8", "replace").splitlines():
            if "=" in line:
                k, v = line.split("=", 1)
                if k.strip() in ("ClientVersion", "ServerVersion", "PatchVersion", "VersionDate", "VersionTime"):
                    steam[k.strip()] = v.strip()
    pak = args.cs2 / "game/csgo/pak01_dir.vpk"
    out = {"Format": "ScCsgoDeathmatch.Cs2Profile/1",
           "Source": {"steam": steam, "files": dict(sorted(files.items())),
                      "pak01_dir": {"size": pak.stat().st_size, "sha256": sha(pak)} if pak.exists() else None,
                      "note": "raw vdata values; conversions and their status are in DmWeapons.cs and the evidence table"},
           "Guns": guns, "Equipment": equipment}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(out, indent=1, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({"out": str(args.out), "guns": len(guns), "equipment": {k: sorted(v) for k, v in equipment.items()},
                      "steam": steam, "files": len(files), "sha256": sha(args.out)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
