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

The acceptance form names the package, so the DLL under test is the one that
ships rather than whatever the working tree happens to build:

    python3 tools/cs2_runtime_selftest.py --scmod output/ScCsgoKnives-0.16.6.scmod \
                                          --sha256 <hex>

Without --scmod it rebuilds and runs the working tree, which is for iterating.
0.16.5 was signed off that way and the DLL it tested (72f814bf...) was not the DLL
in the package that shipped (02b70383...).

Usage:  python3 tools/cs2_runtime_selftest.py [--scmod P --sha256 H] [--json out.json]
        (on Windows: python tools\\cs2_runtime_selftest.py ...)
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import cs2_run

ROOT = Path(__file__).resolve().parent.parent
PACKAGECHECK = ROOT / "tools/PackageCheck/bin/Release/net10.0/PackageCheck.dll"


def run_working_tree():
    """The working-tree build, for iterating. Never an acceptance result on its own."""
    result, _ = cs2_run.run_json(
        ["dotnet", "run", "--project", ROOT / "tools/ArmPreview/ArmPreview.csproj",
         "-c", "Release", "--", "loadcheck"], dotnet=True, allow_exit=(0, 1))
    result["source"] = "working tree (tools/ArmPreview)"
    return result


def run_package(scmod: Path, sha256: str | None):
    """The DLL inside the package under acceptance, which is what ships.

    tools/PackageCheck verifies the package hash, extracts ScCsgoKnives.dll from it
    and calls the same Game.Cs2SelfTest.RunJson the working-tree path calls, so a
    difference between the two can only be the bytes.
    """
    if not PACKAGECHECK.exists():
        raise SystemExit("build it first: dotnet build tools/PackageCheck -c Release")
    cmd = ["dotnet", PACKAGECHECK, "--scmod", scmod]
    if sha256:
        cmd += ["--sha256", sha256]
    result, _ = cs2_run.run_json(cmd, dotnet=True, allow_exit=(0, 1))
    result["source"] = "package %s" % scmod.name
    return result


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--json", type=Path)
    ap.add_argument("--verbose", action="store_true")
    ap.add_argument("--scmod", type=Path,
                    help="accept the DLL inside this .scmod instead of a working-tree rebuild")
    ap.add_argument("--sha256", help="the package's expected SHA-256; refuses to run on a mismatch")
    args = ap.parse_args()

    if args.sha256 and not args.scmod:
        raise SystemExit("--sha256 only means something with --scmod")
    result = run_package(args.scmod, args.sha256) if args.scmod else run_working_tree()
    checks = result["checks"]
    groups = {}
    for c in checks:
        groups.setdefault(c["name"].split("/")[0], []).append(c)

    print("Runtime asset load, through the shipped C# loaders")
    print("   source: %s" % result.get("source"))
    if result.get("packageSha256"):
        print("   package sha256 %s, %d entries, %d bytes" %
              (result["packageSha256"], result["entries"], result["packageBytes"]))
        print("   ScCsgoKnives.dll inside it: sha256 %s" % result["dllSha256"])
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
