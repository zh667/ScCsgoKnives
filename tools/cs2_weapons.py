#!/usr/bin/env python3
"""CS2's gameplay numbers for the three guns -> AnimationData/cs2_weapons.json.

Straight out of `01_weapon_data/firearm_blocks/weapon_<gun>.vdata`. Fields whose
CS meaning is a formula rather than a value are converted here, and the two the
mod cannot take literally are flagged:

  spread     CS carries `m_flSpread` plus a state-dependent `m_flInaccuracy*`,
             both dimensionless direction perturbations. The mod fires one cone,
             so the standing first shot is atan(spread + inaccuracyStand) in
             degrees - a reading of the units, not of CS's bullet code.
  recoil     `m_flRecoilMagnitude` is in CS's own recoil units and its per-shot
             pattern comes from a seeded generator this port does not have. Only
             the RATIO between guns is used; the absolute scale stays the value
             the mod already had for the AK. Marked estimated until a wall-spray
             measurement settles it.

Two-element arrays in the vdata are [primary, alternate]: unsilenced/silenced for
the M4A1-S, unscoped/scoped for the AWP.

Usage:  python3 tools/cs2_weapons.py [--out FILE]
"""

from __future__ import annotations

import argparse
import json
import math
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "src/ScCsgoKnives/AnimationData"
VDATA = (Path.home() / "workspaces/CSMCReverse/local_cs2_analysis/all_weapons"
         / "01_weapon_data/firearm_blocks")

GUNS = {"ak47": "weapon_ak47", "m4a1s": "weapon_m4a1_silencer", "awp": "weapon_awp"}

SCALAR = ["m_nDamage", "m_iMaxClip1", "m_nPrimaryReserveAmmoMax", "m_flCycleTime",
          "m_flRange", "m_flRangeModifier", "m_flArmorRatio", "m_flHeadshotMultiplier",
          "m_flPenetration", "m_flRecoveryTimeStand", "m_flRecoveryTimeCrouch",
          "m_flRecoveryTimeStandFinal", "m_nRecoilSeed", "m_nZoomFOV1", "m_nZoomFOV2",
          "m_nZoomLevels", "m_flDeployDuration", "m_flKillAward"]
PAIRS = ["m_flSpread", "m_flInaccuracyStand", "m_flInaccuracyCrouch", "m_flInaccuracyMove",
         "m_flInaccuracyFire", "m_flInaccuracyJump", "m_flRecoilAngle",
         "m_flRecoilAngleVariance", "m_flRecoilMagnitude", "m_flRecoilMagnitudeVariance",
         "m_flMaxSpeed"]

# The AK's existing hand-fitted camera kick, kept as the anchor for the absolute
# recoil scale so only the ratios between guns change. ESTIMATED.
AK_KICK_PITCH_DEGREES = 1.6


def read(stem: str) -> dict:
    text = (VDATA / (stem + ".vdata")).read_text("utf-8", "replace")
    out = {}
    for name in SCALAR:
        m = re.search(r"%s\s*=\s*([\d.\-]+)" % re.escape(name), text)
        if m:
            out[name] = float(m.group(1))
    for name in PAIRS:
        m = re.search(r"%s\s*=\s*\[([^\]]*)\]" % re.escape(name), text)
        if m:
            out[name] = [float(x) for x in m.group(1).split(",")]
    m = re.search(r"m_bIsFullAuto\s*=\s*(true|false)", text)
    out["m_bIsFullAuto"] = m.group(1) == "true" if m else None
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", type=Path, default=DATA / "cs2_weapons.json")
    args = ap.parse_args()

    raw = {gun: read(stem) for gun, stem in GUNS.items()}
    ak_magnitude = raw["ak47"]["m_flRecoilMagnitude"][0]
    kick_per_unit = AK_KICK_PITCH_DEGREES / ak_magnitude

    guns = {}
    for gun, v in raw.items():
        spread, alt_spread = v["m_flSpread"][0], v["m_flSpread"][-1]
        stand, alt_stand = v["m_flInaccuracyStand"][0], v["m_flInaccuracyStand"][-1]
        magnitude, alt_magnitude = v["m_flRecoilMagnitude"][0], v["m_flRecoilMagnitude"][-1]
        guns[gun] = {
            "Damage": v["m_nDamage"],
            "HeadshotMultiplier": v["m_flHeadshotMultiplier"],
            "ArmorRatio": v["m_flArmorRatio"],
            "Penetration": v["m_flPenetration"],
            "Magazine": int(v["m_iMaxClip1"]),
            "ReserveClips": int(v.get("m_nPrimaryReserveAmmoMax", 0)),
            "CycleSeconds": v["m_flCycleTime"],
            "FullAuto": v["m_bIsFullAuto"],
            "RangeUnits": v["m_flRange"],
            "RangeModifier": v["m_flRangeModifier"],
            "MaxSpeed": v["m_flMaxSpeed"],
            "ZoomFov": [v.get("m_nZoomFOV1"), v.get("m_nZoomFOV2")],
            "RecoveryTimeStand": v["m_flRecoveryTimeStand"],
            "RecoilSeed": v.get("m_nRecoilSeed"),
            "RecoilAngleVariance": v["m_flRecoilAngleVariance"],
            "RecoilMagnitude": v["m_flRecoilMagnitude"],
            # Converted, with the reasoning in this file's docstring.
            "SpreadDegrees": round(math.degrees(math.atan(spread + stand)), 5),
            "SpreadDegreesAlternate": round(math.degrees(math.atan(alt_spread + alt_stand)), 5),
            "MoveSpreadDegrees": round(math.degrees(math.atan(spread + v["m_flInaccuracyMove"][0])), 5),
            "KickPitchDegrees": round(magnitude * kick_per_unit, 5),
            "KickPitchDegreesAlternate": round(alt_magnitude * kick_per_unit, 5),
            "KickYawDegrees": round(magnitude * kick_per_unit
                                    * math.sin(math.radians(v["m_flRecoilAngleVariance"][0] / 2)), 5),
            "KickRecoverPerSecond": round(1.0 / v["m_flRecoveryTimeStand"], 5),
            "Raw": v,
        }
        g = guns[gun]
        print("%-6s dmg %g x%g hs, armor %g, mag %d, cycle %gs, falloff %g/500u"
              % (gun, g["Damage"], g["HeadshotMultiplier"], g["ArmorRatio"],
                 g["Magazine"], g["CycleSeconds"], g["RangeModifier"]))
        print("       spread %.4f deg standing (%.4f alt), %.3f deg moving; kick %.3f deg (%.3f alt), "
              "yaw +-%.3f, recover %.2f/s"
              % (g["SpreadDegrees"], g["SpreadDegreesAlternate"], g["MoveSpreadDegrees"],
                 g["KickPitchDegrees"], g["KickPitchDegreesAlternate"], g["KickYawDegrees"],
                 g["KickRecoverPerSecond"]))

    doc = {
        "Format": "ScCsgoKnives.Cs2Weapons/1",
        "Notes": ("Values from weapon_<gun>.vdata. Damage falls off as "
                  "damage * RangeModifier^(units/500), the CS convention documented at "
                  "counterstrike.fandom.com/wiki/Damage_dropoff and "
                  "steamcommunity.com/sharedfiles/filedetails/?id=2599082552 - community "
                  "documentation of the formula, not the SDK source. Spread is "
                  "atan(m_flSpread + m_flInaccuracy*) in degrees, a reading of the units. "
                  "Recoil ratios are CS2's; the absolute degrees-per-recoil-unit scale is "
                  "anchored to the AK's previously hand-fitted 1.6 deg and is ESTIMATED."),
        "UnitsPerMetre": 1.0 / 0.0254,
        "FalloffUnits": 500.0,
        "KickDegreesPerRecoilUnit": round(kick_per_unit, 6),
        "Guns": guns,
    }
    args.out.write_text(json.dumps(doc, indent=1), "utf-8")
    print("wrote %s (%.1f KB)" % (args.out.name, args.out.stat().st_size / 1024))


if __name__ == "__main__":
    main()
