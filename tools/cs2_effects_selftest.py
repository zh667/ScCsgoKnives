#!/usr/bin/env python3
"""Stage 5 acceptance: muzzle flash, tracers and the scope mask.

  A  muzzle   each gun's declared m_vecMuzzlePos0 against the `muzzle` bone of its
              idle clip. Two independent files describing the same point.
  B  sources  every particle system the JSON quotes exists in the export, and
              regenerating the JSON reproduces the shipped one byte for byte.
  C  scope    the radial alpha profile of the shipped cs2_scope_circle.png, which
              is what replaces the procedural mask.

Usage:  python3 tools/cs2_effects_selftest.py [--json out.json]
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))

import cs2_effects
import cs2_viewmodel as vm
from cs2_rig_selftest import GUNS

ROOT = Path(__file__).resolve().parent.parent
EFFECTS = ROOT / "src/ScCsgoKnives/AnimationData/cs2_effects.json"
SCOPE = ROOT / "src/ScCsgoKnives/Assets/Textures/ScCsgoKnives/cs2_scope_circle.png"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--json", type=Path)
    args = ap.parse_args()

    doc = json.loads(EFFECTS.read_text("utf-8"))
    rows = []

    print("A. Declared muzzle position vs the rig's muzzle bone at idle (inches)")
    worst = 0.0
    for gun, cfg in GUNS.items():
        stem = [k for k, v in cfg["clips"].items() if v == "idle"][0]
        clip = vm.load_clip(vm.CLIPS / cfg["folder"] / (stem + ".dmx"))
        bone = clip.absolute(0.0)["muzzle"][3, :3]
        declared = np.array(doc["Guns"][gun]["MuzzlePos0"], float)
        d = float(np.linalg.norm(declared - bone))
        worst = max(worst, d)
        rows.append({"gun": gun, "vdata": declared.tolist(),
                     "bone": [round(float(x), 4) for x in bone], "delta_in": d})
        print("   %-6s vdata %s  bone %s  ->  %.4f in"
              % (gun, np.round(declared, 3), np.round(bone, 3), d))

    print("\nB. Sources")
    missing = []
    for gun, g in doc["Guns"].items():
        for mode, flash in (g.get("Flash") or {}).items():
            if "missing" in flash:
                missing.append("%s/%s %s" % (gun, mode, flash["missing"]))
            else:
                path = cs2_effects.ANALYSIS / flash["source"]
                if not path.exists():
                    missing.append(str(path))
        tracer = g.get("Tracer") or {}
        if tracer.get("source") and not (cs2_effects.PARTICLES / tracer["source"]).exists():
            missing.append(tracer["source"])
    before = EFFECTS.read_bytes()
    subprocess.run([sys.executable, str(ROOT / "tools/cs2_effects.py")],
                   capture_output=True, check=True, cwd=ROOT)
    stable = EFFECTS.read_bytes() == before
    print("   %d particle systems referenced, %d missing%s"
          % (sum(len(g.get("Flash") or {}) + (1 if (g.get("Tracer") or {}).get("source") else 0)
                 for g in doc["Guns"].values()),
             len(missing), "" if not missing else ": " + ", ".join(missing)))
    print("   regenerating cs2_effects.json reproduces the shipped file: %s" % stable)
    for gun, g in doc["Guns"].items():
        for mode, flash in (g.get("Flash") or {}).items():
            if flash.get("unmodelled"):
                print("   %-6s flash[%s] not modelled: %s" % (gun, mode, ", ".join(flash["unmodelled"])))

    print("\nC. Shipped scope circle")
    im = Image.open(SCOPE).convert("RGBA")
    a = np.asarray(im, float)
    h, w = a.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    r = np.hypot(xx - (w - 1) / 2, yy - (h - 1) / 2) / ((w - 1) / 2)
    alpha = a[..., 3] / 255.0
    inner = float(alpha[r < 0.85].max())
    profile = [(x, float(alpha[(r >= x) & (r < x + 0.01)].mean()))
               for x in np.arange(0.0, 1.0, 0.01) if ((r >= x) & (r < x + 0.01)).any()]
    crossing = next((x for (x, v), (_, prev) in zip(profile[1:], profile) if prev < 0.5 <= v), None)
    scope = {"size": list(im.size), "max_alpha_inside_r085": inner,
             "half_alpha_radius": crossing}
    print("   %dx%d, alpha inside r<0.85 max %.3f, 50%% crossing at r = %.2f of the half-width"
          % (im.size[0], im.size[1], inner, crossing))
    print("   -> aperture %.4f h, feather about %.3f h (procedural mask: 0.4825 h / 0.0185 h)"
          % (crossing * 0.5, 0.10 * 0.5))

    ok = worst < 1.5 and not missing and stable and inner < 0.02 and 0.9 < crossing < 1.0
    print("\nA/B/C %s" % ("PASS" if ok else "FAIL"))
    if args.json:
        args.json.write_text(json.dumps({"muzzle": rows, "missing": missing,
                                         "regenerates_identically": stable,
                                         "scope": scope}, indent=2), "utf-8")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
