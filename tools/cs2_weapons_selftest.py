#!/usr/bin/env python3
"""Stage 6 acceptance: the gameplay numbers.

  A  source     every value in cs2_weapons.json is the vdata's, unchanged, and
                regenerating the file reproduces the shipped one byte for byte.
  B  derived    the three converted quantities recomputed independently here:
                spread = atan(m_flSpread + m_flInaccuracy*), the recoil ratios,
                and the recovery rate.
  C  falloff    the damage curve at a few distances, so the report carries
                numbers rather than a formula.
  D  shipped    the mod's own GunSpec fallbacks against CS2 - what changes when
                the cs2 profile is on.

Usage:  python3 tools/cs2_weapons_selftest.py [--json out.json]
"""

from __future__ import annotations

import argparse
import json
import math
import re
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import cs2_run

ROOT = Path(__file__).resolve().parent.parent
WEAPONS = ROOT / "src/ScCsgoKnives/AnimationData/cs2_weapons.json"
VDATA = (Path.home() / "workspaces/CSMCReverse/local_cs2_analysis/all_weapons"
         / "01_weapon_data/firearm_blocks")
STEMS = {"ak47": "weapon_ak47", "m4a1s": "weapon_m4a1_silencer", "awp": "weapon_awp"}

# What GunSpec.cs ships as the csmc-profile fallback, for the comparison in D.
SHIPPED = {"ak47": {"Damage": 36, "Magazine": 30, "SpreadDegrees": 0.35, "KickPitchDegrees": 1.6},
           "m4a1s": {"Damage": 38, "Magazine": 20, "SpreadDegrees": 0.30, "KickPitchDegrees": 1.15},
           "awp": {"Damage": 115, "Magazine": 5, "SpreadDegrees": 0.10, "KickPitchDegrees": 6.0}}


def vdata_value(stem, name, index=None):
    text = (VDATA / (stem + ".vdata")).read_text("utf-8", "replace")
    if index is None:
        m = re.search(r"%s\s*=\s*([\d.\-]+)" % re.escape(name), text)
        return float(m.group(1)) if m else None
    m = re.search(r"%s\s*=\s*\[([^\]]*)\]" % re.escape(name), text)
    return float(m.group(1).split(",")[index]) if m else None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--json", type=Path)
    args = ap.parse_args()

    doc = json.loads(WEAPONS.read_text("utf-8"))
    guns = doc["Guns"]

    print("A. Every literal value against the vdata")
    mismatches = []
    checks = [("Damage", "m_nDamage", None), ("Magazine", "m_iMaxClip1", None),
              ("CycleSeconds", "m_flCycleTime", None), ("ArmorRatio", "m_flArmorRatio", None),
              ("HeadshotMultiplier", "m_flHeadshotMultiplier", None),
              ("RangeModifier", "m_flRangeModifier", None), ("RangeUnits", "m_flRange", None),
              ("RecoveryTimeStand", "m_flRecoveryTimeStand", None)]
    total = 0
    for gun, stem in STEMS.items():
        for key, field, index in checks:
            total += 1
            want = vdata_value(stem, field, index)
            got = float(guns[gun][key])
            if want is None or abs(want - got) > 1e-6:
                mismatches.append("%s.%s: json %s vs vdata %s" % (gun, key, got, want))
    before = WEAPONS.read_bytes()
    cs2_run.run([sys.executable, ROOT / "tools/cs2_weapons.py"])
    stable = WEAPONS.read_bytes() == before
    print("   %d literal values checked, %d mismatched%s"
          % (total, len(mismatches), "" if not mismatches else ": " + "; ".join(mismatches)))
    print("   regenerating cs2_weapons.json reproduces the shipped file: %s" % stable)

    print("\nB. Derived quantities, recomputed here")
    derived = []
    for gun, stem in STEMS.items():
        spread = vdata_value(stem, "m_flSpread", 0)
        stand = vdata_value(stem, "m_flInaccuracyStand", 0)
        want = math.degrees(math.atan(spread + stand))
        got = guns[gun]["SpreadDegrees"]
        recover_want = 1.0 / vdata_value(stem, "m_flRecoveryTimeStand", None)
        magnitude = vdata_value(stem, "m_flRecoilMagnitude", 0)
        kick_want = magnitude * doc["KickDegreesPerRecoilUnit"]
        d = max(abs(want - got), abs(recover_want - guns[gun]["KickRecoverPerSecond"]),
                abs(kick_want - guns[gun]["KickPitchDegrees"]))
        derived.append({"gun": gun, "max_error": d})
        print("   %-6s spread %.5f deg, recover %.5f /s, kick %.5f deg  (max recompute error %.2e)"
              % (gun, got, guns[gun]["KickRecoverPerSecond"], guns[gun]["KickPitchDegrees"], d))

    print("\nC. Damage after CS falloff, damage * RangeModifier^(units/500)")
    falloff = []
    for gun in STEMS:
        g = guns[gun]
        row = {"gun": gun}
        parts = []
        for metres in (5, 20, 50, 100):
            units = metres * doc["UnitsPerMetre"]
            dmg = g["Damage"] * g["RangeModifier"] ** (units / doc["FalloffUnits"])
            row["%dm" % metres] = round(dmg, 2)
            parts.append("%3dm %6.2f" % (metres, dmg))
        falloff.append(row)
        print("   %-6s point-blank %5.1f | %s" % (gun, g["Damage"], "  ".join(parts)))

    print("\nD. What the cs2 profile changes against the shipped fallbacks")
    changes = []
    for gun, old in SHIPPED.items():
        g = guns[gun]
        for key in ("Damage", "Magazine", "SpreadDegrees", "KickPitchDegrees"):
            a, b = float(old[key]), float(g[key])
            if abs(a - b) > 1e-4:
                changes.append({"gun": gun, "field": key, "was": a, "now": b})
                print("   %-6s %-18s %8.4f -> %8.4f" % (gun, key, a, b))
    if not changes:
        print("   nothing")

    ok = not mismatches and stable and all(d["max_error"] < 1e-4 for d in derived)
    print("\nA/B/C/D %s" % ("PASS" if ok else "FAIL"))
    if args.json:
        args.json.write_text(json.dumps({"mismatches": mismatches, "regenerates_identically": stable,
                                         "derived": derived, "falloff": falloff,
                                         "changes": changes}, indent=2), "utf-8")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
