#!/usr/bin/env python3
"""Stage 5 acceptance: muzzle flash, tracers and the scope mask.

  A  muzzle   each gun's declared m_vecMuzzlePos0 against the `muzzle` bone of its
              idle clip. Two independent files describing the same point.
  B  sources  every particle system the JSON quotes exists in the export, and
              regenerating the JSON reproduces the shipped one byte for byte.
  C  scope    the radial alpha profile of the shipped cs2_scope_circle.png, which
              is what replaces the procedural mask.
  D  schema   no snake_case key survives anywhere in the file, and every Lifetime and
              Alpha is an array. Those two shapes are what made the C# loader throw in
              0.16.4; tools/cs2_runtime_selftest.py is what proves the loader is happy,
              this only stops the file regressing.

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

import cs2_run
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
            if "Missing" in flash:
                missing.append("%s/%s %s" % (gun, mode, flash["Missing"]))
            else:
                path = cs2_effects.ANALYSIS / flash["Source"]
                if not path.exists():
                    missing.append(str(path))
        tracer = g.get("Tracer") or {}
        if tracer.get("Source") and not (cs2_effects.PARTICLES / tracer["Source"]).exists():
            missing.append(tracer["Source"])
    before = EFFECTS.read_bytes()
    cs2_run.run([sys.executable, ROOT / "tools/cs2_effects.py"])
    stable = EFFECTS.read_bytes() == before
    print("   %d particle systems referenced, %d missing%s"
          % (sum(len(g.get("Flash") or {}) + (1 if (g.get("Tracer") or {}).get("Source") else 0)
                 for g in doc["Guns"].values()),
             len(missing), "" if not missing else ": " + ", ".join(missing)))
    print("   regenerating cs2_effects.json reproduces the shipped file: %s" % stable)
    for gun, g in doc["Guns"].items():
        for mode, flash in (g.get("Flash") or {}).items():
            if flash.get("Unmodelled"):
                print("   %-6s flash[%s] not modelled: %s" % (gun, mode, ", ".join(flash["Unmodelled"])))

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

    print("\nD. Schema shape")
    import re as _re
    snake = []
    shapes = []

    def walk(node, path=""):
        if isinstance(node, dict):
            for k, v in node.items():
                if _re.search(r"[a-z]_[a-z]", k):
                    snake.append(path + k)
                if k in ("Lifetime", "Alpha") and v is not None and not isinstance(v, list):
                    shapes.append("%s%s is %r, expected an array" % (path, k, v))
                walk(v, path + k + ".")
        elif isinstance(node, list):
            for v in node:
                walk(v, path)

    walk(doc)
    print("   snake_case keys: %d%s" % (len(snake), "" if not snake else " -> " + ", ".join(snake[:5])))
    print("   Lifetime/Alpha not an array: %d%s" % (len(shapes), "" if not shapes else " -> " + shapes[0]))
    print("   format: %s" % doc.get("Format"))

    # The tracer is the part the runtime draws geometry from, so its shape is pinned
    # here too: two RenderTrails passes, each with a baked texture and a size clamp.
    tracer_faults = []
    for gun, g in doc["Guns"].items():
        t = g.get("Tracer") or {}
        passes = t.get("Passes") or []
        if len(passes) != 2:
            tracer_faults.append("%s has %d tracer passes, not 2" % (gun, len(passes)))
        for ps in passes:
            if not ps.get("Texture") or not ps.get("SourceTexture"):
                tracer_faults.append("%s pass has no texture (%r <- %r)"
                                     % (gun, ps.get("Texture"), ps.get("SourceTexture")))
            if not (0.0 < ps.get("MinSize", 0.0) < ps.get("MaxSize", 0.0)):
                tracer_faults.append("%s pass screen clamp is %r..%r"
                                     % (gun, ps.get("MinSize"), ps.get("MaxSize")))
        for key in ("Speed", "MaxLength", "Radius", "TrailSeconds", "Alpha", "ColorMin", "ColorMax"):
            if t.get(key) in (None, [], ""):
                tracer_faults.append("%s tracer has no %s" % (gun, key))
    print("   tracer schema: %s" % ("2 passes with textures and size clamps for all three guns"
                                    if not tracer_faults else "; ".join(tracer_faults)))

    ok = (worst < 1.5 and not missing and stable and inner < 0.02 and 0.9 < crossing < 1.0
          and not snake and not shapes and not tracer_faults
          and doc.get("Format") == "ScCsgoKnives.Cs2Effects/3")
    print("\nA/B/C/D %s" % ("PASS" if ok else "FAIL"))
    if args.json:
        args.json.write_text(json.dumps({"muzzle": rows, "missing": missing,
                                         "regenerates_identically": stable,
                                         "scope": scope, "snake_case_keys": snake,
                                         "bad_shapes": shapes,
                                         "format": doc.get("Format")}, indent=2), "utf-8")
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
