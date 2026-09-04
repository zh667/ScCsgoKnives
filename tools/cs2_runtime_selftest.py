#!/usr/bin/env python3
"""Assert the shipped C# actually loads every embedded CS2 asset, with real values.

This exists because 0.16.4 shipped a cs2_effects.json that Cs2Effects could not
deserialise - the AK's `lifetime` was a scalar where the property is float[], and
keys like `sequence_frames` never bound because System.Text.Json's
PropertyNameCaseInsensitive ignores case, not separators. The load threw, the
warning was swallowed, and every tracer and the whole CS2 flash envelope were
inactive in game while tools/cs2_effects_selftest.py went on passing: it checked
the JSON against the vdata and the vpcf, never that the runtime could read it.

So this drives ArmPreview's "loadcheck", which calls Cs2Effects, Cs2Weapons,
Cs2Rig, Cs2Sounds and Cs2SkinnedMesh out of the mod assembly and reports the
values that actually arrive - not that a file parses, but that the magazine moves
mid-reload and the tracer speed is 20500 rather than a defaulted zero.

Usage:  python3 tools/cs2_runtime_selftest.py [--json out.json]
"""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def run():
    out = subprocess.run(
        ["dotnet", "run", "--project", str(ROOT / "tools/ArmPreview/ArmPreview.csproj"),
         "-c", "Release", "--", "loadcheck"],
        capture_output=True, text=True, cwd=ROOT,
        env={**os.environ, "DOTNET_ROLL_FORWARD": "Major"})
    lines = [l for l in out.stdout.splitlines() if l.startswith("{")]
    if not lines:
        raise SystemExit("loadcheck produced no JSON:\n%s" % (out.stderr[-2000:] or out.stdout[-2000:]))
    return json.loads(lines[-1])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--json", type=Path)
    ap.add_argument("--verbose", action="store_true")
    args = ap.parse_args()

    result = run()
    checks = result["checks"]
    groups = {}
    for c in checks:
        groups.setdefault(c["name"].split("/")[0], []).append(c)

    print("Runtime asset load, through the shipped C# loaders")
    for group, items in sorted(groups.items()):
        bad = [c for c in items if not c["ok"]]
        print("   %-9s %2d checks, %d failed" % (group, len(items), len(bad)))
        for c in bad:
            print("      FAIL %s -> %s" % (c["name"], c["detail"]))
        if args.verbose:
            for c in items:
                print("      ok   %s = %s" % (c["name"], c["detail"]))

    # A few values worth printing every run: these are the ones that were silently
    # zero in 0.16.4, so seeing the numbers is the point, not just the verdict.
    watch = ["effects/ak47/flash.seconds", "effects/ak47/tracer.speed",
             "effects/ak47/tracer.length", "effects/awp/tracer.freq",
             "weapons/ak47/spread", "rig/ak47/animates"]
    print("\n   values that were defaulted away in 0.16.4:")
    for name in watch:
        hit = next((c for c in checks if c["name"] == name), None)
        if hit:
            print("      %-28s %s" % (name, hit["detail"]))

    ok = result["failed"] == 0
    print("\n%d of %d checks pass. %s" % (len(checks) - result["failed"], len(checks),
                                          "PASS" if ok else "FAIL"))
    if args.json:
        args.json.write_text(json.dumps(result, indent=2), "utf-8")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
