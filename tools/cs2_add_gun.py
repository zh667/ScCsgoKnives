#!/usr/bin/env python3
"""Run the asset pipeline for a gun, and print the code it still needs.

One gun is eight things, and the first eight were done by hand in the order
below with a few mistakes each time. This runs the file-producing steps and
prints, for the two edits that live in code, exactly the text to paste:

  1  <gun>.cs2.animation.json      tools/cs2_gun_rig.py
  2  <gun>.cs2.parts               tools/cs2_glb_to_parts.py on the body GLB
  3  <gun>_hd.png + PBR maps       tools/install_gun_textures_cs2hd.py
  4  <gun>_fire_*.ogg, zoom        tools/install_gun_sounds_cs2.py
  5  <gun>_slot.png                tools/install_gun_slot_icons_cs2.py (CS2's own icon)
  6  cs2_sounds.json + cues        tools/cs2_sound_timings.py, install_gun_sounds_cs2.py --cues
  7  cs2_effects.json              tools/cs2_effects.py (its GUNS table names the flash)
  8  code: GunSpec.All entry, guns.json entry, Lang names   <- printed, then applied
     with --apply

Every number in the GunSpec entry is CS2's own (cs2_weapons.json, from the
.vdata), formatted the way the existing entries are. The display names are the
one thing typed here.

Usage:  python3 tools/cs2_add_gun.py fiveseven hkp2000 [--apply] [--skip textures,sounds]
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import cs2_run  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
TOOLS = ROOT / "tools"
DATA = ROOT / "src/ScCsgoKnives/AnimationData"
TEXTURES = ROOT / "src/ScCsgoKnives/Assets/Textures/ScCsgoKnives"
AUDIO = ROOT / "src/ScCsgoKnives/Assets/Audio/ScCsgoKnives"
LANG = ROOT / "src/ScCsgoKnives/Assets/Lang"
GUNSPEC = ROOT / "src/ScCsgoKnives/Rendering/GunSpec.cs"
MODELS = (Path.home() / "workspaces/CSMCReverse/local_cs2_analysis/all_weapons"
          / "02_models/glb_with_animations/weapons/models")

# mod name -> export directory (the GLB and the materials live under it)
DIRS = {
    "ak47": "ak47", "m4a1s": "m4a1_silencer", "awp": "awp",
    "cz75a": "cz75a", "deagle": "deagle", "elite": "elite", "fiveseven": "fiveseven",
    "glock18": "glock18", "hkp2000": "hkp2000", "p250": "p250", "revolver": "revolver",
    "taser": "taser", "tec9": "tec9", "usp_silencer": "usp_silencer",
    "aug": "aug", "famas": "famas", "galilar": "galilar", "m4a4": "m4a4", "sg556": "sg556",
    "bizon": "bizon", "mac10": "mac10", "mp5sd": "mp5sd", "mp7": "mp7", "mp9": "mp9",
    "p90": "p90", "ump45": "ump45",
    "mag7": "mag7", "nova": "nova", "sawedoff": "sawedoff", "xm1014": "xm1014",
    "m249": "m249", "negev": "negev",
    "g3sg1": "g3sg1", "scar20": "scar20", "ssg08": "ssg08",
}

# Display names. zh, en, and the description's control line is composed below
# from the spec (burst / scope / silencer), so only the weapon itself is written.
NAMES = {
    "cz75a": ("CZ75 自动", "CZ75-Auto", "9 毫米手枪", "A 9 mm pistol"),
    "elite": ("双持贝瑞塔", "Dual Berettas", "9 毫米双持手枪，左右轮流击发", "Two 9 mm pistols, fired in turn"),
    "fiveseven": ("FN57", "Five-SeveN", "5.7 毫米手枪", "A 5.7 mm pistol"),
    "hkp2000": ("P2000", "P2000", "9 毫米手枪", "A 9 mm pistol"),
    "p250": ("P250", "P250", "0.357 英寸手枪", "A .357 pistol"),
    "revolver": ("R8 左轮", "R8 Revolver", "8 发左轮手枪，左键扣扳机击发，右键快速扇射", "An 8-round revolver; left click cocks and fires, right click fans the hammer"),
    "taser": ("电击枪", "Zeus x27", "单发电击枪，近距离一击致命", "A single-shot stun gun, lethal up close"),
    "tec9": ("Tec-9", "Tec-9", "9 毫米手枪", "A 9 mm pistol"),
    "aug": ("AUG", "AUG", "5.56 毫米突击步枪，带瞄准镜", "A 5.56 mm assault rifle with a scope"),
    "bizon": ("PP-野牛", "PP-Bizon", "9 毫米冲锋枪，64 发螺旋弹鼓", "A 9 mm SMG with a 64-round helical magazine"),
    "g3sg1": ("G3SG1", "G3SG1", "7.62 毫米自动狙击步枪", "A 7.62 mm auto sniper"),
    "galilar": ("加利尔 AR", "Galil AR", "5.56 毫米突击步枪", "A 5.56 mm assault rifle"),
    "m249": ("M249", "M249", "5.56 毫米轻机枪，100 发弹链", "A 5.56 mm light machine gun, 100-round belt"),
    "mac10": ("MAC-10", "MAC-10", "0.45 英寸冲锋枪", "A .45 SMG"),
    "mag7": ("MAG-7", "MAG-7", "弹匣供弹霰弹枪", "A magazine-fed shotgun"),
    "mp5sd": ("MP5-SD", "MP5-SD", "9 毫米冲锋枪，自带消音器", "A 9 mm SMG with an integral suppressor"),
    "mp7": ("MP7", "MP7", "4.6 毫米冲锋枪", "A 4.6 mm SMG"),
    "negev": ("内格夫", "Negev", "5.56 毫米轻机枪，150 发弹链", "A 5.56 mm light machine gun, 150-round belt"),
    "nova": ("新星", "Nova", "泵动霰弹枪", "A pump-action shotgun"),
    "sawedoff": ("短管霰弹枪", "Sawed-Off", "短管泵动霰弹枪，近距离威力大", "A sawed-off pump shotgun, devastating up close"),
    "scar20": ("SCAR-20", "SCAR-20", "7.62 毫米自动狙击步枪", "A 7.62 mm auto sniper"),
    "sg556": ("SG 553", "SG 553", "5.56 毫米突击步枪，带瞄准镜", "A 5.56 mm assault rifle with a scope"),
    "ump45": ("UMP-45", "UMP-45", "0.45 英寸冲锋枪", "A .45 SMG"),
    "xm1014": ("XM1014", "XM1014", "半自动霰弹枪", "A semi-automatic shotgun"),
}


def body_glb(gun: str) -> Path:
    d = MODELS / DIRS[gun]
    hits = [p for p in d.glob("weapon_*.glb")
            if "physics" not in p.name and "holster" not in p.name and not p.stem.endswith("_mag")]
    if not hits:
        raise SystemExit("%s: no body GLB in %s" % (gun, d))
    return min(hits, key=lambda p: len(p.name))


def run(cmd, **kw):
    print("   $", " ".join(str(c) for c in cmd))
    out = cs2_run.run([str(c) for c in cmd], **kw)
    if out.returncode != 0:
        print("     (exit %d)" % out.returncode)
        for line in (out.stdout or "").splitlines()[-8:]:
            print("     | " + line)
        for line in (out.stderr or "").splitlines()[-8:]:
            print("     ! " + line)
    return out


def assets(gun: str, skip: set):
    if "rig" not in skip and not (DATA / ("%s.cs2.animation.json" % gun)).exists():
        run([sys.executable, TOOLS / "cs2_gun_rig.py", "--gun", gun])
    if "parts" not in skip and not (DATA / ("%s.cs2.parts" % gun)).exists():
        run([sys.executable, TOOLS / "cs2_glb_to_parts.py", "--source", body_glb(gun),
             "--out", DATA / ("%s.cs2.parts" % gun)])
    if "textures" not in skip and not (TEXTURES / ("%s_hd.png" % gun)).exists():
        run([sys.executable, TOOLS / "install_gun_textures_cs2hd.py", gun, "--size", "1024"])
    if "sounds" not in skip and not list(AUDIO.glob("%s_fire_*.ogg" % gun)):
        run([sys.executable, TOOLS / "install_gun_sounds_cs2.py", gun])
    if "icon" not in skip and not (TEXTURES / ("%s_slot.png" % gun)).exists():
        run([sys.executable, TOOLS / "install_gun_slot_icons_cs2.py", "--only", gun])


def cues(guns):
    run([sys.executable, TOOLS / "cs2_sound_timings.py"], check=False)
    out = run([sys.executable, TOOLS / "install_gun_sounds_cs2.py", "--cues", *guns], check=False)
    # Say what the installer did: three batches in a row came out short here and
    # the reason never reached the log.
    lines = (out.stdout or "").splitlines()
    print("     cues installed: %d, without a source: %d"
          % (sum(1 for l in lines if " -> " in l), sum(1 for l in lines if l.startswith("!!"))))
    for l in lines:
        if l.startswith("!!"):
            print("     " + l)
    run([sys.executable, TOOLS / "cs2_sound_timings.py"])


def spec_entry(gun: str) -> str:
    w = json.loads((DATA / "cs2_weapons.json").read_text("utf-8"))["Guns"][gun]
    lines = []
    notes = []
    if w["HasBurstMode"]:
        notes.append("Burst: m_flCycleTimeWhenInBurstMode %s, m_flTimeBetweenBurstShots %s."
                     % (w["BurstCycleSeconds"], w["BurstShotSeconds"]))
    zoom = []
    if w["ZoomLevels"] > 0:
        fovs = w["ZoomFov"][:w["ZoomLevels"]]
        zoom = [round(90.0 / f, 4) for f in fovs]
        notes.append("Scope: %s against CS2's 90, i.e. %s.%s"
                     % (" and ".join("m_nZoomFOV%d %g" % (i + 1, f) for i, f in enumerate(fovs)),
                        " and ".join("%g" % z for z in zoom),
                        "" if w.get("HideViewModelWhenZoomed", True)
                        else " m_bHideViewModelWhenZoomed false: the gun stays drawn and aims down its own scope."))
    if w["Pellets"] > 1:
        notes.append("m_nNumBullets %d." % w["Pellets"])
    if w["SilencerType"] == "WEAPONSILENCER_INTEGRATED":
        notes.append("Integral suppressor: WEAPONSILENCER_INTEGRATED, the single shot sound is the suppressed one.")
    for n in notes:
        lines.append("            // " + n)
    lines.append('            Name = "%s", Magazine = %d, CycleSeconds = %gf, Automatic = %s, AttackPower = %gf,'
                 % (gun, w["Magazine"], w["CycleSeconds"], "true" if w["FullAuto"] else "false", w["Damage"]))
    lines.append("            KickPitchDegrees = %.3ff, KickYawDegrees = %.3ff, KickRecoverPerSecond = %.2ff,"
                 % (w["KickPitchDegrees"], w["KickYawDegrees"], w["KickRecoverPerSecond"]))
    extras = ["SpreadDegrees = %.4ff" % w["SpreadDegrees"]]
    if w["SilencerType"] == "WEAPONSILENCER_DETACHABLE":
        extras.append("HasSilencer = true")
    if w["SilencerType"] == "WEAPONSILENCER_INTEGRATED":
        extras.append("SilencedAlways = true")
    if w["Pellets"] > 1:
        extras.append("Pellets = %d" % w["Pellets"])
    if zoom:
        extras.append("ZoomLevels = [%s]" % ", ".join("%gf" % z for z in zoom))
        if not w.get("HideViewModelWhenZoomed", True):
            extras.append("ScopeHidesWeapon = false")
    lines.append("            " + ", ".join(extras) + ",")
    if w["HasBurstMode"]:
        lines.append("            HasBurstMode = true, BurstCycleSeconds = %gf, BurstShotSeconds = %gf,"
                     % (w["BurstCycleSeconds"], w["BurstShotSeconds"]))
    # The specials, each from a file: the alternate cycle from the vdata pair, the
    # muzzle bones from the rig's skeleton, the flash from the effects table, the
    # range from the vdata where it is short, the recharge for a gun with no reload.
    rig = json.loads((DATA / ("%s.cs2.animation.json" % gun)).read_text("utf-8"))
    bones = {b["Name"] for b in rig["Skeleton"]}
    aliases = {c.get("Alias") for c in rig["Clips"].values()}
    special = []
    # The vdata pair's second value is an alternate fire only where the rig has a
    # clip for it (shoot_alt1_*: the R8's fanning); the pistols' [0.15, 0.3] carry a
    # second slot CS2 never fires.
    if w.get("CycleSecondsAlternate") and "shootAlt" in aliases:
        lines.append("            // m_flCycleTime [%g, %g]: the second is the fanned shot on the aim key." % (w["CycleSeconds"], w["CycleSecondsAlternate"]))
        special.append("CycleSecondsAlternate = %gf" % w["CycleSecondsAlternate"])
    if "muzzle_l" in bones and "muzzle_r" in bones:
        special.append('MuzzleBone = "muzzle_r", LeftMuzzleBone = "muzzle_l"')
    effects = json.loads((DATA / "cs2_effects.json").read_text("utf-8"))["Guns"].get(gun) or {}
    if not effects.get("Flash"):
        special.append("MuzzleEffects = false")
    if w["RangeUnits"] < 1000:
        lines.append("            // m_flRange %g in, i.e. %.2f m; the rifles' 4096 stay on the default." % (w["RangeUnits"], w["RangeUnits"] * 0.0254))
        special.append("RangeBlocks = %.2ff" % (w["RangeUnits"] * 0.0254))
    if w["Magazine"] == 1 and "reload" not in aliases:
        lines.append("            // No reload clip and one round: recharges. 30 s is CS2's Zeus timing, not in the vdata - assumed.")
        special.append("RechargeSeconds = 30f")
    if special:
        lines.append("            " + ", ".join(special) + ",")
    return "        new() {\n" + "\n".join(lines) + "\n        },"


def lang_entries(gun: str, variant: int, w: dict) -> tuple:
    zh, en, zh_desc, en_desc = NAMES[gun]
    mag = w["Magazine"]
    mode_zh = "全自动" if w["FullAuto"] else "半自动"
    mode_en = "automatic" if w["FullAuto"] else "semi-automatic"
    ctrl_zh, ctrl_en = "左键开火", "Left click fires"
    if w["HasBurstMode"]:
        ctrl_zh, ctrl_en = "右键在%s与三连发之间切换" % mode_zh, "Right click switches between %s and three-round burst" % mode_en
    elif w["ZoomLevels"] >= 2:
        ctrl_zh, ctrl_en = "右键开镜（再按一次放大，第三次退出）", "Right click scopes (again to magnify, a third time to leave)"
    elif w["ZoomLevels"] == 1:
        ctrl_zh, ctrl_en = "右键开镜", "Right click scopes"
    elif w["SilencerType"] == "WEAPONSILENCER_DETACHABLE":
        ctrl_zh, ctrl_en = "右键装卸消音器", "Right click fits or removes the silencer"
    reload_zh = "，R 换弹。" if mag > 1 else "。"
    reload_en = ". R reloads." if mag > 1 else "."
    zh_full = "%s，%d 发%s，%s。%s%s" % (zh_desc, mag, "弹匣" if mag > 1 else "", mode_zh, ctrl_zh, reload_zh)
    en_full = "%s, %d round%s, %s. %s%s" % (en_desc, mag, "s" if mag != 1 else "", mode_en, ctrl_en, reload_en)
    key = "ScGunBlock:%d" % variant
    return key, {"DisplayName": zh, "Description": zh_full}, {"DisplayName": en, "Description": en_full}


def apply(guns):
    """Append the GunSpec entries, the guns.json entries and the Lang names."""
    spec = GUNSPEC.read_text("utf-8")
    manifest = json.loads((DATA / "guns.json").read_text("utf-8"))
    have = {e["Name"] for e in manifest}
    zh = json.loads((LANG / "zh-CN.json").read_text("utf-8"))
    en = json.loads((LANG / "en-US.json").read_text("utf-8"))
    w_all = json.loads((DATA / "cs2_weapons.json").read_text("utf-8"))["Guns"]
    block = []
    for gun in guns:
        if 'Name = "%s"' % gun in spec:
            print("   GunSpec already has %s" % gun)
            continue
        block.append(spec_entry(gun))
    if block:
        marker = "    ];\n\n    public static GunSpec ForAsset"
        assert marker in spec, "GunSpec.All's end not found"
        spec = spec.replace(marker, "\n".join(block) + "\n" + marker, 1)
        GUNSPEC.write_text(spec, "utf-8")
    for gun in guns:
        if gun not in have:
            manifest.append({"Name": gun, "MeshParts": [], "SourceReferenceScale": 0.0,
                             "Table": gun, "Cs2Only": True})
    (DATA / "guns.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=1) + "\n", "utf-8")
    # Variant = index in GunSpec.All, i.e. the order of guns.json's entries.
    order = [e["Name"] for e in manifest]
    section = "Blocks"
    for gun in guns:
        key, z, e = lang_entries(gun, order.index(gun), w_all[gun])
        zh[section][key] = z
        en[section][key] = e
    (LANG / "zh-CN.json").write_text(json.dumps(zh, ensure_ascii=False, indent=1) + "\n", "utf-8")
    (LANG / "en-US.json").write_text(json.dumps(en, ensure_ascii=False, indent=1) + "\n", "utf-8")
    print("   applied: GunSpec %d entries, guns.json %d entries, Lang %d names"
          % (len(block), len(manifest), len(guns)))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("guns", nargs="+")
    ap.add_argument("--skip", default="", help="comma list of rig,parts,textures,sounds,icon,cues,effects")
    ap.add_argument("--apply", action="store_true", help="write the GunSpec, manifest and Lang entries")
    args = ap.parse_args()
    skip = {s for s in args.skip.split(",") if s}
    unknown = [g for g in args.guns if g not in DIRS or g not in NAMES]
    if unknown:
        raise SystemExit("no table entry for: %s" % ", ".join(unknown))
    for gun in args.guns:
        print("== %s" % gun)
        assets(gun, skip)
    # The manifest before the cues: cs2_sound_timings.py walks guns.json, so a gun
    # not yet in it has no clips in the table and nothing for --cues to install.
    # Three batches went out with their reload cues missing this way.
    if args.apply:
        apply(args.guns)
    if "cues" not in skip:
        if not args.apply:
            print("   (no --apply: the cues of a gun not yet in guns.json cannot be installed)")
        cues(args.guns)
    if "effects" not in skip:
        run([sys.executable, TOOLS / "cs2_effects.py"])
    print("\n== GunSpec entries")
    for gun in args.guns:
        print(spec_entry(gun))


if __name__ == "__main__":
    main()
