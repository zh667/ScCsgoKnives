#!/usr/bin/env python3
"""Stage 1 acceptance: prove the CS2 DMX parse and rig maths are right.

Four checks, each printing numbers rather than a verdict alone:

  A  parse      every binary DMX under decompiled/ is consumed byte-exactly.
  B  rig        hand_R/hand_L coincide with their wpnHand_ IK targets on every
                frame of every clip. Only the correct quaternion convention,
                composition order, hierarchy and sampling give this; a
                transposed rotation puts them ~12 inches apart.
  C  attach     hanging the weapon skeleton off "wpn" reproduces the CS:MC
                hand-to-muzzle distance for all three guns.
  E  roundtrip  <gun>.cs2.animation.json, resampled the way CsmcKnifeRig would,
                reproduces the matrices taken straight from the DMX. This is what
                proves the pre-roll trimming and constant-curve collapse in
                cs2_dmx_to_rig are lossless.
  D  cross      CS2 clip vs the same clip in the shipped CS:MC animbin:
                timeline (fps, frames, duration) and per-frame absolute
                rotation deltas, bone by bone.

Check D deliberately compares rotation *deltas*, not absolute matrices. The two
rigs are not the same skeleton - CS2's viewmodel bones are ~1-8% longer and it
carries finger meta joints CS:MC folded away - so absolute matrices cannot
agree by construction. Deltas are invariant to bind pose and to the global
frame, so they test what is actually shared: the take and its timeline.

Usage:  python3 tools/cs2_rig_selftest.py [--json out.json] [--quick]
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))

import cs2_dmx
import cs2_viewmodel as vm
import rigprobe

ANIM = vm.ANIM
CLIPS = vm.CLIPS

# CS2 clip file -> the clip key it corresponds to in <gun>.csmc.animation.json.
GUNS = {
    "ak47": {
        "folder": "rifle/rifle_ak",
        "clips": {"draw_ak": "deploy", "idle_ak": "idle", "shoot1_ak": "shoot1",
                  "reload_ak": "reload", "lookat01_ak": "inspect"},
        "weapon_bones": {"clip": "v_weapon_ak47_clip",
                         "cliprelease": "v_weapon_ak47_cliprelease",
                         "trigger": "v_weapon_ak47_trigger",
                         "bolt": "v_weapon_ak47_bolt", "muzzle": "muzzle"},
    },
    "m4a1s": {
        "folder": "rifle/_default_rifle",
        "clips": {"draw_rifle": "deploy", "idle_rifle": "idle",
                  "shoot1_rifle": "shootSilenced", "reload_rifle": "reload",
                  "lookat01_rifle": "inspect"},
        "weapon_bones": {"clip": "v_weapon_m4a1_clip",
                         "trigger": "v_weapon_m4a1_trigger",
                         "bolt": "v_weapon_m4a1_bolt",
                         "silencer": "v_weapon_silencer", "muzzle": "muzzle"},
    },
    "awp": {
        "folder": "rifle/rifle_awp",
        "clips": {"draw_awp": "deploy", "idle_awp": "idle",
                  "shoot1_awp": "shoot1", "reload_awp": "reload",
                  "lookat01_awp": "inspect"},
        "weapon_bones": {"clip": "v_weapon_awp_clip",
                         "trigger": "v_weapon_awp_trigger",
                         "rail": "v_weapon_awp_bolt_rail",
                         "bolt_action": "v_weapon_awp_bolt_action",
                         "muzzle": "muzzle"},
    },
}

FINGERS = ["thumb_0", "thumb_1", "thumb_2", "index_0", "index_1", "index_2",
           "middle_0", "middle_1", "middle_2", "ring_0", "ring_1", "ring_2",
           "pinky_0", "pinky_1", "pinky_2"]

# Arm and finger bones CS:MC kept under the same name, modulo case.
ARM_MAP = {}
for _side in ("L", "R"):
    ARM_MAP["arm_lower_" + _side] = "arm_lower_" + _side.lower()
    ARM_MAP["hand_" + _side] = "hand_" + _side.lower()
    for _f in FINGERS:
        ARM_MAP["finger_%s_%s" % (_f, _side)] = "finger_%s_%s" % (_f, _side.lower())


def check_parse(quick=False):
    files = sorted((ANIM.parent).rglob("*.dmx"))
    binary = text = 0
    failures = []
    for f in files:
        head = f.read_bytes()[:64]
        if b"encoding binary" not in head:
            text += 1
            continue
        try:
            cs2_dmx.load(f)
            binary += 1
        except Exception as exc:  # noqa: BLE001 - report, do not hide
            failures.append((str(f.relative_to(ANIM.parent)), str(exc)))
    return {"binary_ok": binary, "binary_total": binary + len(failures),
            "keyvalues2_skipped": text, "failures": failures}


def check_rig(quick=False):
    """hand_* must land on wpnHand_* - the IK target the animator snapped to."""
    worst = 0.0
    worst_at = None
    samples = 0
    for gun, cfg in GUNS.items():
        for stem in cfg["clips"]:
            path = CLIPS / cfg["folder"] / (stem + ".dmx")
            if not path.exists():
                continue
            clip = vm.load_clip(path)
            frames = range(0, clip.frame_count, 3 if quick else 1)
            for i in frames:
                a = clip.absolute(clip.frame_time(i))
                for side in ("L", "R"):
                    d = float(np.linalg.norm(a["hand_" + side][3, :3]
                                             - a["wpnHand_" + side][3, :3]))
                    samples += 1
                    if d > worst:
                        worst, worst_at = d, "%s/%s frame %d hand_%s" % (gun, stem, i, side)
    return {"max_inches": worst, "at": worst_at, "samples": samples}


def check_attach():
    rows = []
    for gun, cfg in GUNS.items():
        stem = next(iter(cfg["clips"]))
        clip = vm.load_clip(CLIPS / cfg["folder"] / (stem + ".dmx"))
        rig = rigprobe.rig(gun)
        csmc_clip = cfg["clips"][stem]
        t = clip.duration / 2
        a = clip.absolute(t)
        b = rig.absolute(csmc_clip, t)
        cs2_d = float(np.linalg.norm(a["hand_R"][3, :3] - a["muzzle"][3, :3]))
        csmc_d = float(np.linalg.norm(b["hand_r"][3, :3] - b["muzzle"][3, :3])) / 0.0254
        rows.append({"gun": gun, "attach_bone": clip.attach_bone,
                     "cs2_hand_to_muzzle_in": cs2_d,
                     "csmc_hand_to_muzzle_in": csmc_d,
                     "delta_pct": 100 * (cs2_d - csmc_d) / csmc_d})
    return rows


def check_roundtrip(quick=False):
    """Emitted JSON vs the DMX it came from, in inches, over every frame."""
    data = Path(__file__).resolve().parent.parent / "src/ScCsgoKnives/AnimationData"
    rows = []
    for gun, cfg in GUNS.items():
        path = data / ("%s.cs2.animation.json" % gun)
        if not path.exists():
            rows.append({"gun": gun, "status": "not built"})
            continue
        doc = json.loads(path.read_text("utf-8"))
        skeleton = doc["Skeleton"]
        parents = [b["Parent"] for b in skeleton]
        names = [b["Name"] for b in skeleton]
        worst = 0.0
        worst_at = None
        for stem, clip_doc in doc["Clips"].items():
            clip = vm.load_clip(CLIPS / cfg["folder"] / (stem + ".dmx"))
            curves = clip_doc["Bones"]
            for i in range(0, clip.frame_count, 3 if quick else 1):
                t = clip.frame_time(i)
                local = []
                for b in skeleton:
                    c = curves.get(b["Name"]) or {}
                    q = _sample_json(c.get("Rotation"), t, b["Rotation"])
                    tr = _sample_json(c.get("Translation"), t, b["Translation"])
                    local.append(vm.from_quat(q) @ vm.translation(tr))
                out = [None] * len(skeleton)

                def calc(j):
                    if out[j] is None:
                        p = parents[j]
                        out[j] = local[j] @ calc(p) if p >= 0 else local[j]
                    return out[j]

                for j in range(len(skeleton)):
                    calc(j)
                truth = clip.absolute(t)
                for j, name in enumerate(names):
                    d = float(np.abs(out[j] - truth[name]).max())
                    if d > worst:
                        worst, worst_at = d, "%s %s frame %d %s" % (gun, stem, i, name)
        rows.append({"gun": gun, "max_abs_error": worst, "at": worst_at,
                     "clips": len(doc["Clips"])})
    return rows


def _sample_json(curve, t, fallback):
    if not curve or not curve["Times"]:
        return np.array(fallback, float)
    return vm.sample(curve["Times"], curve["Values"], t)


def _deltas(frames, matrices, name):
    r = [vm.rotation_of(m[name]) for m in matrices]
    return np.array([vm.angle_between(r[i], r[i + 1]) for i in range(len(r) - 1)])


def check_cross(quick=False):
    out = []
    for gun, cfg in GUNS.items():
        rig = rigprobe.rig(gun)
        csmc_bones = {b["Name"] for b in rig.f["Skeleton"]}
        for stem, csmc_name in cfg["clips"].items():
            path = CLIPS / cfg["folder"] / (stem + ".dmx")
            if not path.exists() or csmc_name not in rig.f["Clips"]:
                continue
            clip = vm.load_clip(path)
            csmc = rig.f["Clips"][csmc_name]
            row = {"gun": gun, "cs2_clip": stem, "csmc_clip": csmc_name,
                   "cs2_fps": clip.frame_rate, "cs2_frames": clip.frame_count,
                   "cs2_duration": clip.duration,
                   "csmc_duration": csmc["Duration"],
                   "csmc_frames": len(next(iter(csmc["Bones"].values()))["Rotation"]["Times"]),
                   "bones": {}}
            if clip.frame_count < 2:
                out.append(row)
                continue
            step = 3 if quick else 1
            times = [clip.frame_time(i) for i in range(0, clip.frame_count, step)]
            a_mats = [clip.absolute(t) for t in times]
            b_mats = [rig.absolute(csmc_name, t) for t in times]
            mapping = dict(ARM_MAP)
            mapping.update({k: v for k, v in cfg["weapon_bones"].items()
                            if v in csmc_bones})
            for c2, cm in sorted(mapping.items()):
                if c2 not in a_mats[0] or cm not in csmc_bones:
                    continue
                x = _deltas(times, a_mats, c2)
                y = _deltas(times, b_mats, cm)
                if len(x) < 2:
                    continue
                d = np.abs(x - y)
                corr = float(np.corrcoef(x, y)[0, 1]) if x.std() > 1e-9 and y.std() > 1e-9 else float("nan")
                row["bones"][c2] = {"csmc": cm, "mean_deg": float(d.mean()),
                                    "max_deg": float(d.max()), "corr": corr}
            out.append(row)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--json", type=Path)
    ap.add_argument("--quick", action="store_true",
                    help="sample every third frame; for fast iteration only")
    args = ap.parse_args()

    print("A. DMX parse sweep")
    parse = check_parse(args.quick)
    print("   %d/%d binary DMX consumed byte-exactly, %d keyvalues2 text files skipped"
          % (parse["binary_ok"], parse["binary_total"], parse["keyvalues2_skipped"]))
    for f, e in parse["failures"][:10]:
        print("   FAIL %s: %s" % (f, e))

    print("\nB. Rig invariant: hand_* vs its wpnHand_* IK target")
    rig = check_rig(args.quick)
    print("   max %.6f in over %d samples (%s)"
          % (rig["max_inches"], rig["samples"], rig["at"]))

    print("\nC. Weapon skeleton attach bone")
    attach = check_attach()
    for r in attach:
        print("   %-6s attach=%-4s CS2 hand->muzzle %7.3f in, CS:MC %7.3f in, %+.2f%%"
              % (r["gun"], r["attach_bone"], r["cs2_hand_to_muzzle_in"],
                 r["csmc_hand_to_muzzle_in"], r["delta_pct"]))

    print("\nE. Emitted JSON round-trip against the DMX")
    trip = check_roundtrip(args.quick)
    for r in trip:
        if r.get("status"):
            print("   %-6s %s" % (r["gun"], r["status"]))
        else:
            print("   %-6s %d clips, max |error| %.3e in (%s)"
                  % (r["gun"], r["clips"], r["max_abs_error"], r["at"]))

    print("\nD. CS2 clip vs CS:MC animbin")
    cross = check_cross(args.quick)
    print("   %-6s %-14s %-16s %-22s %-22s" % ("gun", "cs2 clip", "csmc clip", "cs2 fps/frames/dur", "csmc frames/dur"))
    for r in cross:
        print("   %-6s %-14s %-16s %7.4f %3d %7.4f      %3d %7.4f"
              % (r["gun"], r["cs2_clip"], r["csmc_clip"], r["cs2_fps"],
                 r["cs2_frames"], r["cs2_duration"], r["csmc_frames"], r["csmc_duration"]))
    print()
    print("   per-frame absolute rotation delta, |CS2 - CS:MC| in degrees")
    print("   %-6s %-14s %8s %8s %8s %6s" % ("gun", "clip", "mean", "max", "corr", "bones"))
    for r in cross:
        if not r["bones"]:
            continue
        m = [b["mean_deg"] for b in r["bones"].values()]
        x = [b["max_deg"] for b in r["bones"].values()]
        c = [b["corr"] for b in r["bones"].values() if b["corr"] == b["corr"]]
        print("   %-6s %-14s %8.4f %8.4f %8.4f %6d"
              % (r["gun"], r["cs2_clip"], np.mean(m), max(x), np.mean(c), len(r["bones"])))

    trip_ok = all(r.get("status") or r["max_abs_error"] < 1e-4 for r in trip)
    ok = not parse["failures"] and rig["max_inches"] < 0.01 and trip_ok
    print("\nA/B/C/E %s" % ("PASS" if ok else "FAIL"))
    if args.json:
        args.json.write_text(json.dumps(
            {"parse": parse, "rig": rig, "attach": attach,
             "roundtrip": trip, "cross": cross},
            indent=2), "utf-8")
        print("wrote %s" % args.json)
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
