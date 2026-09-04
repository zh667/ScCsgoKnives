#!/usr/bin/env python3
"""CS2 viewmodel placement: rig space -> the engine's view space, and to screen.

Unlike the CS:MC chain this replaces, almost nothing here is fitted. Measured
from the clips themselves (tools/cs2_placement.py --measure):

    root_motion is the eye. wpnEnd, the weapon's back end, sits at x = 0.4 in
    (AK), -0.02 (M4A1-S), 0.7 (AWP); the muzzle is at x = +37.4 / +39.6 / +55.0
    with y ~ -5 and z ~ -3. trigger->muzzle points along +x to within 0.05.

That is standard Source view space - x forward, y left, z up, origin at the eye -
so the CS2 rig needs no placement solve at all: it is already posed in the
camera's frame. What the chain does is only

    1. axis change, Source (x fwd, y left, z up) -> Engine (x right, y up, z back)
    2. inches -> engine units (0.0254)
    3. the player's viewmodel_offset_x/y/z, in inches, as right / forward / up
    4. a projection built from viewmodel_fov

Step 4 is the one assumption. Source treats `fov` as a horizontal angle defined
at 4:3 and keeps the vertical fixed as the aspect widens (Hor+):

    fov_y = 2 * atan(tan(fov_x / 2) / (4 / 3))

That is Source 1's documented behaviour, applied to CS2 without a local
measurement to confirm it; the numbers it produces are marked ASSUMED in the
report until the CS2 recording settles them. Everything else above is read.

Local cvars (D:\\steam\\userdata\\1415980225, "name" "zh667"):
viewmodel_fov 68, offset_x 2.5, offset_y 0, offset_z -1.5.
"""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))

import cs2_viewmodel as vm
from cs2_rig_selftest import GUNS

INCHES_TO_ENGINE = 0.0254

# Source view space -> Engine view space, row-vector: engine = source @ AXIS.
# x_fwd -> -z, y_left -> -x, z_up -> +y.
AXIS = np.array([[0.0, 0.0, -1.0, 0.0],
                 [-1.0, 0.0, 0.0, 0.0],
                 [0.0, 1.0, 0.0, 0.0],
                 [0.0, 0.0, 0.0, 1.0]])

# Read from the user's own CS2 config, not defaults.
CVARS = {"viewmodel_fov": 68.0, "viewmodel_offset_x": 2.5,
         "viewmodel_offset_y": 0.0, "viewmodel_offset_z": -1.5}
CVAR_SOURCE = ("D:/steam/userdata/1415980225/730/local/cfg/cs2_user_convars_0_slot0.vcfg"
               ' ("name" "zh667")')


def fov_y_degrees(viewmodel_fov: float) -> float:
    """Source's Hor+ rule: `fov` is horizontal at 4:3, the vertical is fixed."""
    return math.degrees(2 * math.atan(math.tan(math.radians(viewmodel_fov) / 2) / (4 / 3)))


def placement(cvars=None) -> np.ndarray:
    """Rig inches (Source view space) -> engine view space, row-vector."""
    c = dict(CVARS, **(cvars or {}))
    m = np.diag([INCHES_TO_ENGINE] * 3 + [1.0]) @ AXIS
    offset = vm.translation(np.array([c["viewmodel_offset_x"],
                                      c["viewmodel_offset_z"],
                                      -c["viewmodel_offset_y"]]) * INCHES_TO_ENGINE)
    return m @ offset


def projection_scales(viewmodel_fov: float, aspect: float):
    """(fx, fy) such that sx = (0.5 + 0.5*x*fx/z)*W with z = -view.z."""
    fy = 1.0 / math.tan(math.radians(fov_y_degrees(viewmodel_fov)) / 2)
    return fy / aspect, fy


def to_screen(points, fx, fy, width, height):
    p = np.atleast_2d(np.asarray(points, float))
    z = -p[:, 2]
    sx = (0.5 + 0.5 * p[:, 0] * fx / np.where(np.abs(z) < 1e-6, 1e-6, z)) * width
    sy = (0.5 - 0.5 * p[:, 1] * fy / np.where(np.abs(z) < 1e-6, 1e-6, z)) * height
    return np.stack([sx, sy, z], -1)


def landmarks(gun: str, clip_stem: str = None, t: float = 0.0, cvars=None,
              width=1920, height=1080):
    cfg = GUNS[gun]
    stem = clip_stem or [k for k, v in cfg["clips"].items() if v == "idle"][0]
    clip = vm.load_clip(vm.CLIPS / cfg["folder"] / (stem + ".dmx"))
    absolute = clip.absolute(t)
    m = placement(cvars)
    fx, fy = projection_scales((cvars or CVARS).get("viewmodel_fov",
                                                    CVARS["viewmodel_fov"]),
                               width / height)
    out = {}
    for name in ("muzzle", "wpnEnd", "wpnTip", "hand_R", "hand_L", "trigger",
                 "clip", "finger_index_1_R", "root_motion"):
        if name not in absolute:
            continue
        view = (np.append(absolute[name][3, :3], 1.0) @ m)[:3]
        s = to_screen(view, fx, fy, width, height)[0]
        out[name] = {"view": [round(float(x), 5) for x in view],
                     "screen": [round(float(s[0]), 1), round(float(s[1]), 1)],
                     "depth": round(float(s[2]), 4)}
    return {"gun": gun, "clip": stem, "t": t,
            "fov_y_deg": round(fov_y_degrees((cvars or CVARS).get("viewmodel_fov", CVARS["viewmodel_fov"])), 4),
            "landmarks": out}


def measure():
    print("CS2 viewmodel rig, idle, positions relative to root_motion (inches, Source axes)")
    print("   %-6s %-24s %-24s %-24s" % ("gun", "wpnEnd", "muzzle", "trigger->muzzle dir"))
    for gun, cfg in GUNS.items():
        stem = [k for k, v in cfg["clips"].items() if v == "idle"][0]
        clip = vm.load_clip(vm.CLIPS / cfg["folder"] / (stem + ".dmx"))
        a = clip.absolute(0.0)
        d = a["muzzle"][3, :3] - a["trigger"][3, :3]
        d = d / np.linalg.norm(d)
        print("   %-6s %-24s %-24s %-24s"
              % (gun, np.round(a["wpnEnd"][3, :3], 3), np.round(a["muzzle"][3, :3], 3),
                 np.round(d, 3)))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--measure", action="store_true")
    ap.add_argument("--gun", action="append", choices=sorted(GUNS))
    ap.add_argument("--clip")
    ap.add_argument("--t", type=float, default=0.0)
    ap.add_argument("--json", type=Path)
    args = ap.parse_args()

    if args.measure:
        measure()
        return

    print("viewmodel cvars from %s" % CVAR_SOURCE)
    print("   " + ", ".join("%s=%g" % kv for kv in CVARS.items()))
    print("   fov_y = %.4f deg (Source Hor+ from viewmodel_fov %g at 4:3) -- ASSUMED rule"
          % (fov_y_degrees(CVARS["viewmodel_fov"]), CVARS["viewmodel_fov"]))
    rows = []
    for gun in args.gun or sorted(GUNS):
        row = landmarks(gun, args.clip, args.t)
        rows.append(row)
        print("\n%s %s t=%.3f  (screen at 1920x1080)" % (gun, row["clip"], row["t"]))
        for name, v in row["landmarks"].items():
            print("   %-18s view %-30s screen (%7.1f,%7.1f)  depth %.4f m"
                  % (name, str(v["view"]), v["screen"][0], v["screen"][1], v["depth"]))
    if args.json:
        args.json.write_text(json.dumps(rows, indent=1), "utf-8")


if __name__ == "__main__":
    main()
