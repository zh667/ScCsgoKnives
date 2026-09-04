#!/usr/bin/env python3
"""Stage 3 acceptance: the shipped C# reproduces the offline CS2 placement.

Same discipline as rigprobe.py against CsmcKnifeRig: tools/cs2_placement.py is
the reference, ArmPreview's "cs2" mode runs Cs2Rig + Cs2Placement out of the mod
assembly itself, and every landmark is compared in view space and on screen.

This does not check that the placement matches CS2 - that is the overlay against
a recording, tools/cs2_videocheck.py. It checks that what ships is what was
derived.

Usage:  python3 tools/cs2_placement_selftest.py [--json out.json]
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))

import cs2_placement as ref
from cs2_rig_selftest import GUNS

ROOT = Path(__file__).resolve().parent.parent
CASES = [("ak47", "idle", 0.0), ("ak47", "deploy", 0.0), ("ak47", "deploy", 0.5),
         ("ak47", "reload", 1.0), ("ak47", "inspect", 2.0), ("ak47", "shoot1", 0.1),
         ("m4a1s", "idle", 0.0), ("m4a1s", "deploy", 0.4), ("m4a1s", "reload", 1.5),
         ("m4a1s", "attach", 2.0),
         ("awp", "idle", 0.0), ("awp", "deploy", 0.6), ("awp", "reload", 2.0),
         ("awp", "shoot1", 0.7)]

ALIAS = {gun: {v: k for k, v in cfg["clips"].items()} for gun, cfg in GUNS.items()}
EXTRA = {("m4a1s", "attach"): "silencer_attach_rifle"}


def csharp(gun, clip, t):
    out = subprocess.run(
        ["dotnet", "run", "--project", str(ROOT / "tools/ArmPreview/ArmPreview.csproj"),
         "-c", "Release", "--", "cs2", gun, clip, "%r" % t],
        capture_output=True, text=True, cwd=ROOT,
        env={**os.environ, "DOTNET_ROLL_FORWARD": "Major"})
    if out.returncode:
        raise SystemExit(out.stderr.strip()[-800:])
    return json.loads(out.stdout.strip().splitlines()[-1])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--json", type=Path)
    args = ap.parse_args()

    rows = []
    worst_view = worst_screen = 0.0
    worst_at = None
    print("%-6s %-14s %6s  %-18s %10s %10s" % ("gun", "clip", "t", "landmarks", "max |dview|", "max |dpx|"))
    for gun, clip, t in CASES:
        got = csharp(gun, clip, t)
        stem = EXTRA.get((gun, clip)) or ALIAS[gun].get(clip)
        want = ref.landmarks(gun, stem, t)
        if abs(want["fov_y_deg"] - got["fovY"]) > 1e-3:
            raise SystemExit("fovY differs: python %.5f vs C# %.5f" % (want["fov_y_deg"], got["fovY"]))
        dv = dp = 0.0
        shared = 0
        for name, a in want["landmarks"].items():
            b = got["lm"].get(name)
            if b is None:
                continue
            shared += 1
            dv = max(dv, float(np.abs(np.array(a["view"]) - np.array(b["view"])).max()))
            # A landmark behind or at the eye projects to nonsense in both; skip it.
            if a["depth"] > 0.05:
                dp = max(dp, float(np.abs(np.array(a["screen"]) - np.array(b["screen"])).max()))
        rows.append({"gun": gun, "clip": clip, "t": t, "cs2_clip": got["clip"],
                     "landmarks": shared, "max_view_error_m": dv, "max_screen_error_px": dp})
        print("%-6s %-14s %6.2f  %-18d %10.2e %10.4f" % (gun, clip, t, shared, dv, dp))
        if dv > worst_view:
            worst_view, worst_at = dv, "%s/%s@%.2f" % (gun, clip, t)
        worst_screen = max(worst_screen, dp)

    print("\nworst: %.3e m in view space, %.4f px on a 1920x1080 frame (%s)"
          % (worst_view, worst_screen, worst_at))
    # The bound is float32 round-off, not a tolerance for disagreement: the C#
    # chain runs in single precision and the reference in double, and a ~1 m
    # coordinate carries about 5e-06 m of that through the matrix product. Set
    # well below the stage 3 target of 10 px so a real divergence still fails.
    ok = worst_view < 5e-5 and worst_screen < 0.1
    print("C# vs offline reference: %s" % ("PASS" if ok else "FAIL"))
    if args.json:
        args.json.write_text(json.dumps(
            {"cases": rows, "worst_view_error_m": worst_view,
             "worst_screen_error_px": worst_screen}, indent=2), "utf-8")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
