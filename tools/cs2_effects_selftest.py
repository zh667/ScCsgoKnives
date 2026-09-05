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
import cs2_gun_rig as gun_rig
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
    # Every gun in the JSON. The vdata and the bone sit on the same bore line on all
    # of them; along the barrel the vdata is 0.95 in behind the bone on the AWP and
    # 7.73 in on the SSG08 (the Galil's is 0.71 in across, the Nova's 1.03), so it is the across-the-bore distance that is judged and
    # the renderer draws from the bone, which is where the model attaches the flash.
    worst = 0.0
    shipped_guns = {e["Name"] for e in json.loads((ROOT / "src/ScCsgoKnives/AnimationData/guns.json").read_text("utf-8"))}
    for gun in doc["Guns"]:
        if gun in GUNS:
            cfg = GUNS[gun]
            folder = cfg["folder"]
            stem = [k for k, v in cfg["clips"].items() if v == "idle"][0]
        else:
            folder = gun_rig.ALL_FOLDERS[gun]
            clips = gun_rig.config(gun, folder)["clips"]
            stem = [k for k, v in clips.items() if v == "idle"][0]
        clip = vm.load_clip(vm.clip_path(folder, stem))
        bones = clip.absolute(0.0)
        # The Dual Berettas carry muzzle_l / muzzle_r (the vdata's pos0 is the right
        # gun's); the Taser has no muzzle at all and no flash to place at one.
        name = next((n for n in ("muzzle", "muzzle_r") if n in bones), None)
        if name is None:
            print("   %-13s no muzzle bone in the rig (and no muzzle flash in the model)" % gun)
            rows.append({"gun": gun, "vdata": doc["Guns"][gun].get("MuzzlePos0"), "bone": None})
            continue
        bone = bones[name][3, :3]
        declared = np.array(doc["Guns"][gun]["MuzzlePos0"], float)
        delta = declared - bone
        across = float(np.linalg.norm(delta[1:]))
        # Judged for the guns the mod ships; the rest are reported so a gun's
        # numbers are already known when its batch comes (the Nova's is 1.03 in).
        if gun in shipped_guns:
            worst = max(worst, across)
        rows.append({"gun": gun, "vdata": declared.tolist(),
                     "bone": [round(float(x), 4) for x in bone],
                     "across_in": across, "along_in": float(delta[0])})
        print("   %-13s vdata %s  bone %s  ->  %.4f in across the bore, %+.3f along"
              % (gun, np.round(declared, 3), np.round(bone, 3), across, delta[0]))

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
                # Property names only: the keys under Guns are asset names, and
                # usp_silencer is one of them.
                if path != "Guns." and _re.search(r"[a-z]_[a-z]", k):
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
    # here too: two RenderTrails passes, each with a baked texture and a size clamp -
    # or, for the SMG rope, one C_OP_RenderRopes pass with the open 0..1 clamp.
    # Judged for the guns the mod ships (guns.json); the table also carries the
    # ones still to come, whose tracers (the AUG's tintable rope, the Taser's wire)
    # are read and modelled when they are.
    shipped = {e["Name"] for e in json.loads((ROOT / "src/ScCsgoKnives/AnimationData/guns.json").read_text("utf-8"))}
    tracer_faults = []
    for gun, g in doc["Guns"].items():
        if gun not in shipped:
            continue
        t = g.get("Tracer") or {}
        passes = t.get("Passes") or []
        if not g.get("Flash") and not passes:
            # The Zeus: no muzzle particle in its model and a wire tracer the ribbon
            # does not draw. GunSpec turns its effects off (MuzzleEffects = false) and
            # the packaged self-test asserts the table is empty for it; here that is
            # the expected shape, not a fault.
            continue
        rope = bool(passes) and all(ps.get("Renderer") == "C_OP_RenderRopes" for ps in passes)
        if len(passes) != (1 if rope else 2):
            tracer_faults.append("%s has %d tracer passes, not %d" % (gun, len(passes), 1 if rope else 2))
        for ps in passes:
            if not ps.get("Texture") or not ps.get("SourceTexture"):
                tracer_faults.append("%s pass has no texture (%r <- %r)"
                                     % (gun, ps.get("Texture"), ps.get("SourceTexture")))
            if rope:
                if not (ps.get("MinSize") == 0.0 and ps.get("MaxSize") == 1.0):
                    tracer_faults.append("%s rope pass clamp is %r..%r, expected the open 0..1"
                                         % (gun, ps.get("MinSize"), ps.get("MaxSize")))
            elif not (0.0 < ps.get("MinSize", 0.0) < ps.get("MaxSize", 0.0)):
                tracer_faults.append("%s pass screen clamp is %r..%r"
                                     % (gun, ps.get("MinSize"), ps.get("MaxSize")))
        for key in ("Speed", "MaxLength", "Radius", "TrailSeconds", "Alpha", "ColorMin", "ColorMax"):
            if t.get(key) in (None, [], ""):
                tracer_faults.append("%s tracer has no %s" % (gun, key))
    print("   tracer schema: %s" % ("trail passes with textures and size clamps for every gun, the SMG rope with one"
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
