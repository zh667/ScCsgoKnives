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

# mod name -> the vdata block. All 35 CS2 ships, so a gun's numbers are ready before
# the port reaches it; the mod only draws the ones GunSpec lists.
GUNS = {
    "ak47": "weapon_ak47", "m4a1s": "weapon_m4a1_silencer", "awp": "weapon_awp",
    "aug": "weapon_aug", "bizon": "weapon_bizon", "cz75a": "weapon_cz75a",
    "deagle": "weapon_deagle", "elite": "weapon_elite", "famas": "weapon_famas",
    "fiveseven": "weapon_fiveseven", "g3sg1": "weapon_g3sg1", "galilar": "weapon_galilar",
    "glock18": "weapon_glock", "hkp2000": "weapon_hkp2000", "m249": "weapon_m249",
    "m4a4": "weapon_m4a1", "mac10": "weapon_mac10", "mag7": "weapon_mag7",
    "mp5sd": "weapon_mp5sd", "mp7": "weapon_mp7", "mp9": "weapon_mp9",
    "negev": "weapon_negev", "nova": "weapon_nova", "p250": "weapon_p250",
    "p90": "weapon_p90", "revolver": "weapon_revolver", "sawedoff": "weapon_sawedoff",
    "scar20": "weapon_scar20", "sg556": "weapon_sg556", "ssg08": "weapon_ssg08",
    "taser": "weapon_taser", "tec9": "weapon_tec9", "ump45": "weapon_ump45",
    "usp_silencer": "weapon_usp_silencer", "xm1014": "weapon_xm1014",
}

SCALAR = ["m_nDamage", "m_iMaxClip1", "m_nPrimaryReserveAmmoMax", "m_flCycleTime",
          "m_flRange", "m_flRangeModifier", "m_flArmorRatio", "m_flHeadshotMultiplier",
          "m_flPenetration", "m_flRecoveryTimeStand", "m_flRecoveryTimeCrouch",
          "m_flRecoveryTimeStandFinal", "m_nRecoilSeed", "m_nZoomFOV1", "m_nZoomFOV2",
          "m_nZoomLevels", "m_flDeployDuration", "m_flKillAward",
          "m_flZoomTime0", "m_flZoomTime1", "m_flZoomTime2",
          "m_nNumBullets", "m_flCycleTimeWhenInBurstMode", "m_flTimeBetweenBurstShots"]
PAIRS = ["m_flSpread", "m_flInaccuracyStand", "m_flInaccuracyCrouch", "m_flInaccuracyMove",
         "m_flInaccuracyFire", "m_flInaccuracyJump", "m_flRecoilAngle",
         "m_flRecoilAngleVariance", "m_flRecoilMagnitude", "m_flRecoilMagnitudeVariance",
         "m_flMaxSpeed"]

# The AK's existing hand-fitted camera kick, kept as the anchor for the absolute
# recoil scale so only the ratios between guns change. ESTIMATED.
AK_KICK_PITCH_DEGREES = 1.6


def prefab_text(name: str) -> str:
    """One `<name> = { ... }` block out of the source weapons.vdata."""
    source = VDATA.parent / "source/weapons.vdata"
    if not source.exists():
        return ""
    whole = source.read_text("utf-8", "replace")
    start = whole.find("\n%s = " % name)
    if start < 0:
        start = whole.find("%s = " % name)
        if start < 0:
            return ""
    depth = 0
    for i in range(start, len(whole)):
        if whole[i] == "{":
            depth += 1
        elif whole[i] == "}":
            depth -= 1
            if depth == 0:
                return whole[start:i + 1]
    return ""


def read(stem: str) -> dict:
    text = (VDATA / (stem + ".vdata")).read_text("utf-8", "replace")
    # Five guns - Five-SeveN, Glock-18, P250, Revolver, Taser - leave m_flCycleTime and
    # sometimes more to their `_base` prefab, so the prefab is appended and the firearm
    # block searched first. Every regex below takes the first match.
    base = re.search(r'_base\s*=\s*"([^"]+)"', text)
    if base:
        text = text + "\n" + prefab_text(base.group(1))
    out = {}
    for name in SCALAR:
        # Some of these are written as a pair - the Glock's cycle time is
        # [ 0.15, 0.3 ], primary then burst-mode secondary - so a bare number is tried
        # first and the first element of a list second. Five guns need it:
        # Five-SeveN, Glock-18, P250, Revolver and the Taser.
        m = re.search(r"%s\s*=\s*([\d.\-]+)" % re.escape(name), text)
        if m:
            out[name] = float(m.group(1))
            continue
        m = re.search(r"%s\s*=\s*\[\s*([\d.\-]+)" % re.escape(name), text)
        if m:
            out[name] = float(m.group(1))
            out[name + "_isPair"] = True
    for name in PAIRS:
        m = re.search(r"%s\s*=\s*\[([^\]]*)\]" % re.escape(name), text)
        if m:
            out[name] = [float(x) for x in m.group(1).split(",")]
    m = re.search(r"m_bIsFullAuto\s*=\s*(true|false)", text)
    out["m_bIsFullAuto"] = m.group(1) == "true" if m else None
    m = re.search(r"m_bHideViewModelWhenZoomed\s*=\s*(true|false)", text)
    out["m_bHideViewModelWhenZoomed"] = m.group(1) == "true" if m else None
    m = re.search(r"m_bHasBurstMode\s*=\s*(true|false)", text)
    out["m_bHasBurstMode"] = m.group(1) == "true" if m else None
    m = re.search(r'm_eSilencerType\s*=\s*"(\w+)"', text)
    out["m_eSilencerType"] = m.group(1) if m else None
    # The sound events CS2 plays. SPECIAL1 is the silenced shot on the two detachable
    # silencers and on the integrated MP5-SD, where it repeats the unsilenced entry.
    block = re.search(r"m_aShootSounds\s*=\s*\{(.*?)\n\t*\}", text, re.S)
    sounds = {}
    if block:
        for key, value in re.findall(r'(WEAPON_SOUND_\w+)\s*=\s*soundevent:"([^"]+)"', block.group(1)):
            sounds.setdefault(key, value)
    out["m_aShootSounds"] = sounds
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
        # The Taser is not a firearm and CS2 gives it no spread, inaccuracy, recoil or
        # move speed. Those read as zero rather than being invented, and the derived
        # degrees below come out zero with them.
        pair = lambda k: v.get(k) or [0.0, 0.0]
        spread, alt_spread = pair("m_flSpread")[0], pair("m_flSpread")[-1]
        stand, alt_stand = pair("m_flInaccuracyStand")[0], pair("m_flInaccuracyStand")[-1]
        magnitude, alt_magnitude = pair("m_flRecoilMagnitude")[0], pair("m_flRecoilMagnitude")[-1]
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
            "MaxSpeed": v.get("m_flMaxSpeed") or [0.0],
            # Pellets per shot: 1 for everything but the shotguns (Nova 9, MAG-7 and
            # Sawed-Off 8, XM1014 6).
            "Pellets": int(v.get("m_nNumBullets") or 1),
            # Burst mode exists on exactly two guns. CS2 carries the burst's cycle time
            # and the gap between its shots but not how many shots it fires; three is
            # Counter-Strike's long-standing burst and is marked as the assumption.
            "HasBurstMode": bool(v.get("m_bHasBurstMode")),
            "BurstCycleSeconds": v.get("m_flCycleTimeWhenInBurstMode"),
            "BurstShotSeconds": v.get("m_flTimeBetweenBurstShots"),
            "BurstShotsAssumed": 3 if v.get("m_bHasBurstMode") else 0,
            # WEAPONSILENCER_NONE / _DETACHABLE (M4A1-S, USP-S) / _INTEGRATED (MP5-SD).
            "SilencerType": v.get("m_eSilencerType"),
            "ShootSounds": v.get("m_aShootSounds") or {},
            "ZoomFov": [v.get("m_nZoomFOV1"), v.get("m_nZoomFOV2")],
            "ZoomLevels": int(v.get("m_nZoomLevels", 0)),
            # How long CS2 takes to interpolate to each zoomed FOV. The AWP's is 0.05
            # for all three, three frames at 60 fps, which is why its scope reads as
            # instant. 0.17.1 gated the lens overlay on a 0.25 s aim blend while the
            # world FOV changed immediately, so a quarter second of the shot was
            # magnified with no scope drawn.
            "ZoomSeconds": [v.get("m_flZoomTime0"), v.get("m_flZoomTime1"), v.get("m_flZoomTime2")],
            "HideViewModelWhenZoomed": v.get("m_bHideViewModelWhenZoomed"),
            "RecoveryTimeStand": v.get("m_flRecoveryTimeStand", 0.0),
            "RecoilSeed": v.get("m_nRecoilSeed"),
            "RecoilAngleVariance": pair("m_flRecoilAngleVariance"),
            "RecoilMagnitude": pair("m_flRecoilMagnitude"),
            # Converted, with the reasoning in this file's docstring.
            "SpreadDegrees": round(math.degrees(math.atan(spread + stand)), 5),
            "SpreadDegreesAlternate": round(math.degrees(math.atan(alt_spread + alt_stand)), 5),
            "MoveSpreadDegrees": round(math.degrees(math.atan(spread + pair("m_flInaccuracyMove")[0])), 5),
            "KickPitchDegrees": round(magnitude * kick_per_unit, 5),
            "KickPitchDegreesAlternate": round(alt_magnitude * kick_per_unit, 5),
            "KickYawDegrees": round(magnitude * kick_per_unit
                                    * math.sin(math.radians(pair("m_flRecoilAngleVariance")[0] / 2)), 5),
            "KickRecoverPerSecond": round(1.0 / v["m_flRecoveryTimeStand"], 5) if v.get("m_flRecoveryTimeStand") else 0.0,
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
