#!/usr/bin/env python3
"""Overlay the cs2 profile on a CS2 capture and report landmark error in pixels.

Same shape as tools/videocheck.py, which does this for the knives against the
CS:MC video: landmarks are measured once from the capture and recorded in
LANDMARKS below, and this compares them against where the shipped chain puts the
same points. The prediction comes from ArmPreview's cs2 mode, i.e. from Cs2Rig
and Cs2Placement in the mod assembly, not from a reimplementation.

Workflow once a recording exists:

    # 1. pull the frames you want to measure (60 fps capture, HUD off)
    python3 tools/cs2_videocheck.py --extract CAPTURE/ak47.mp4 --at 3.0 4.2 --out .tmp-cs2
    # 2. read the pixel coordinates of muzzle / trigger / magazine off those frames
    #    and add them to LANDMARKS
    # 3. compare
    python3 tools/cs2_videocheck.py

Target, the same one the knives are held to: every landmark within 10 px at
1920x1080. A systematic miss on all landmarks at once points at the one assumed
value in the chain - Source's Hor+ reading of viewmodel_fov (see Cs2Placement) -
and --fit-fov reports the viewmodel_fov that would minimise it.
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

ROOT = Path(__file__).resolve().parent.parent

# gun -> clip -> [(clip time, {landmark: (x, y)}, note)]. Empty until the CS2
# recording is measured; every entry must say which frame it came from.
LANDMARKS: dict = {
    # "ak47": {"idle": [(0.0, {"muzzle": (1172, 680), "trigger": (1523, 996)}, "ak47.mp4 @ 3.00 s")]},
}


def extract(video: Path, times, out: Path):
    out.mkdir(parents=True, exist_ok=True)
    made = []
    for t in times:
        png = out / ("%s_%06.3f.png" % (video.stem, t))
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-ss", "%.3f" % t,
                        "-i", str(video), "-frames:v", "1", str(png)], check=True)
        made.append(png)
        print("wrote %s" % png)
    return made


def predict(gun: str, clip: str, t: float, cvars=None, width=1920, height=1080):
    over = ["%s=%g" % kv for kv in (cvars or {}).items()]
    out = subprocess.run(
        ["dotnet", "run", "--project", str(ROOT / "tools/ArmPreview/ArmPreview.csproj"),
         "-c", "Release", "--", "cs2", gun, clip, "%r" % t, str(width), str(height)] + over,
        capture_output=True, text=True, cwd=ROOT,
        env={**os.environ, "DOTNET_ROLL_FORWARD": "Major"})
    if out.returncode:
        raise SystemExit(out.stderr.strip()[-800:])
    return json.loads(out.stdout.strip().splitlines()[-1])


def compare(cvars=None, width=1920, height=1080):
    rows = []
    for gun, clips in LANDMARKS.items():
        for clip, cases in clips.items():
            for t, points, note in cases:
                got = predict(gun, clip, t, cvars, width, height)
                for name, (vx, vy) in points.items():
                    lm = got["lm"].get(name)
                    if lm is None:
                        rows.append({"gun": gun, "clip": clip, "t": t, "landmark": name,
                                     "status": "not in the rig"})
                        continue
                    sx, sy = lm["screen"]
                    rows.append({"gun": gun, "clip": clip, "t": t, "landmark": name,
                                 "video": [vx, vy], "chain": [round(sx, 1), round(sy, 1)],
                                 "error_px": float(np.hypot(sx - vx, sy - vy)), "note": note})
    return rows


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--extract", type=Path)
    ap.add_argument("--at", type=float, nargs="+", default=[])
    ap.add_argument("--out", type=Path, default=ROOT / ".tmp-cs2")
    ap.add_argument("--fit-fov", action="store_true")
    ap.add_argument("--json", type=Path)
    args = ap.parse_args()

    if args.extract:
        extract(args.extract, args.at, args.out)
        return 0

    if not LANDMARKS:
        print("No CS2 landmarks recorded yet: LANDMARKS in this file is empty.")
        print("The chain it would compare against is live - "
              "`python3 tools/cs2_placement.py` prints its predictions, and "
              "`python3 tools/cs2_placement_selftest.py` proves the shipped C# agrees with them.")
        print("Record a CS2 capture, pull frames with --extract, measure the landmarks, "
              "and fill LANDMARKS in.")
        return 0

    rows = compare()
    worst = 0.0
    for r in rows:
        if r.get("status"):
            print("   %-6s %-10s t=%.2f %-10s %s" % (r["gun"], r["clip"], r["t"], r["landmark"], r["status"]))
            continue
        worst = max(worst, r["error_px"])
        print("   %-6s %-10s t=%.2f %-16s video %s vs chain %s -> %6.1f px"
              % (r["gun"], r["clip"], r["t"], r["landmark"], r["video"], r["chain"], r["error_px"]))
    print("\nworst landmark error %.1f px (target < 10)" % worst)

    if args.fit_fov:
        best = None
        for fov in np.arange(50.0, 90.5, 0.5):
            e = max(r["error_px"] for r in compare({"Cs2ViewmodelFov": fov}) if not r.get("status"))
            if best is None or e < best[1]:
                best = (fov, e)
        print("best viewmodel_fov by worst-landmark error: %.1f (%.1f px); "
              "config says %g" % (best[0], best[1], 68))

    if args.json:
        args.json.write_text(json.dumps(rows, indent=2), "utf-8")
    return 0 if worst < 10 else 1


if __name__ == "__main__":
    raise SystemExit(main())
