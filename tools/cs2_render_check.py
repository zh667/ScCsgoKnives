#!/usr/bin/env python3
"""Stage 2 render check: draw the CS2 body_hd mesh with CS2's current materials.

The mod's renderer cannot place the CS2 parts yet - that is stage 3 - but it does
not have to. cs2_glb_to_obj deliberately puts the CS2 mesh in the *same*
normalized space as the mesh the mod already ships (same MeshCenter, same
MeshNormalizationScale), so a sweep captured from the shipped rig places the CS2
parts correctly by simply renaming its per-bone matrices.

What this does NOT do is shade the legacy mesh with the CS2 textures. The two
bodies do not share a UV layout - measured: 0 % of coincident vertices agree on
UV to within 0.005 - so that comparison would sample the new maps at the wrong
places. Both sides here use their own geometry with their own materials.

    python3 tools/cs2_render_check.py [--gun ak47] [--clip idle] [--out DIR]

With --reference <frame.png> it also reports masked brightness against a CS2
capture; without one it just records the numbers so that comparison is a
one-liner once a recording exists.
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "src/ScCsgoKnives/AnimationData"

# CS2 part name -> the shipped rig's bone whose sweep matrix places it.
PART_TO_LEGACY = {
    "ak47": {"weapon_offset": "weapon_hand_r", "weapon_offset__p2": "weapon_hand_r__2",
             "bolt": "v_weapon_ak47_bolt", "clip": "v_weapon_ak47_clip",
             "trigger": "v_weapon_ak47_trigger"},
    "m4a1s": {"weapon_offset__c1": "weapon_hand_r__c1", "weapon_offset__c2": "weapon_hand_r__c1",
              "bolt": "v_weapon_m4a1_bolt", "clip": "v_weapon_m4a1_clip",
              "silencer": "v_weapon_silencer", "trigger": "v_weapon_m4a1_trigger"},
    "awp": {"weapon_offset__c1": "weapon_hand_r", "weapon_offset__c2": "weapon_hand_r",
            "clip": "v_weapon_awp_clip", "rail": "v_weapon_awp_bolt_rail",
            "bolt_action": "v_weapon_awp_bolt_action", "trigger": "v_weapon_awp_trigger"},
}


def sweep(gun: str, clip: str, out: Path) -> Path:
    path = out / ("%s_%s.sweep.json" % (gun, clip))
    subprocess.run(
        ["dotnet", "run", "--project", str(ROOT / "tools/ArmPreview/ArmPreview.csproj"),
         "-c", "Release", "--", gun, clip, "30", str(path)],
        check=True, capture_output=True,
        env={**__import__("os").environ, "DOTNET_ROLL_FORWARD": "Major"})
    return path


def retarget(path: Path, gun: str, out: Path) -> Path:
    doc = json.loads(path.read_text("utf-8"))
    mapping = PART_TO_LEGACY[gun]
    parts = json.loads((DATA / ("%s.cs2.animation.json" % gun)).read_text("utf-8"))["MeshParts"]
    for frame in doc["frames"]:
        src = frame["parts"]
        missing = [p for p in parts if mapping.get(p) not in src]
        if missing:
            raise SystemExit("%s: no sweep matrix for %s" % (gun, ", ".join(missing)))
        frame["parts"] = {p: src[mapping[p]] for p in parts}
    target = out / ("%s_cs2.sweep.json" % gun)
    target.write_text(json.dumps(doc), "utf-8")
    return target


def render(asset: str, tex: str, sweep_path: Path, png: Path, size=(960, 540)):
    """Render, and take the rasteriser's own coverage mask alongside the image."""
    result = subprocess.run(
        [sys.executable, str(ROOT / "tools/pbr_emulate.py"), asset, str(sweep_path),
         str(png), str(size[0]), str(size[1]), "flipv=1", "tex=" + tex,
         "mask=" + str(png.with_name(png.stem + "_mask.png"))],
        capture_output=True, text=True, cwd=ROOT)
    if result.returncode:
        raise SystemExit(result.stderr.strip()[-800:])
    return result.stdout.strip().splitlines()[-1]


def statistics(png: Path, mask_path: Path = None):
    """Statistics inside the rasteriser's coverage mask.

    The mask is read, never inferred. Guessing the background from the modal colour
    - which this did until 2026-09-05 - is only valid over a flat backdrop, and would
    quietly mis-measure any real screenshot.
    """
    a = np.asarray(Image.open(png).convert("RGB"), float) / 255.0
    mask_path = mask_path or png.with_name(png.stem + "_mask.png")
    if not mask_path.exists():
        raise SystemExit("no coverage mask beside %s; pass mask= to pbr_emulate" % png.name)
    mask = np.asarray(Image.open(mask_path).convert("L")) > 0
    if mask.shape != a.shape[:2]:
        raise SystemExit("mask %s is %s, image is %s" % (mask_path.name, mask.shape, a.shape[:2]))
    if not mask.any():
        return {"pixels": 0}
    lit = a[mask]
    lum = lit @ np.array([0.2126, 0.7152, 0.0722])
    return {"pixels": int(mask.sum()),
            "mean_srgb": [round(float(x), 4) for x in lit.mean(0)],
            "luminance_mean": round(float(lum.mean()), 4),
            "luminance_median": round(float(np.median(lum)), 4),
            "luminance_p10": round(float(np.percentile(lum, 10)), 4),
            "luminance_p90": round(float(np.percentile(lum, 90)), 4)}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--gun", action="append", choices=sorted(PART_TO_LEGACY))
    ap.add_argument("--clip", default="idle")
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--reference", type=Path,
                    help="a CS2 capture frame; needs <stem>_mask.png beside it. Prefer "
                         "tools/cs2_reference_check.py, which is the acceptance path")
    ap.add_argument("--json", type=Path)
    args = ap.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)

    rows = []
    for gun in args.gun or sorted(PART_TO_LEGACY):
        base = sweep(gun, args.clip, args.out)
        cs2 = retarget(base, gun, args.out)
        old_png = args.out / ("%s_shipped.png" % gun)
        new_png = args.out / ("%s_cs2.png" % gun)
        old_line = render(gun, gun, base, old_png)
        new_line = render("%s_cs2" % gun, "%s_hd" % gun, cs2, new_png)
        row = {"gun": gun, "clip": args.clip,
               "shipped": {"line": old_line, **statistics(old_png)},
               "cs2": {"line": new_line, **statistics(new_png)}}
        if args.reference and args.reference.exists():
            row["reference"] = statistics(args.reference, args.reference.with_name(
                args.reference.stem + "_mask.png"))
        rows.append(row)
        s, c = row["shipped"], row["cs2"]
        print("%-6s %s" % (gun, args.clip))
        print("   shipped mesh+materials: %6d px, luminance mean %.4f median %.4f, sRGB %s"
              % (s["pixels"], s["luminance_mean"], s["luminance_median"], s["mean_srgb"]))
        print("   CS2 body_hd + current : %6d px, luminance mean %.4f median %.4f, sRGB %s"
              % (c["pixels"], c["luminance_mean"], c["luminance_median"], c["mean_srgb"]))
        print("   change: %+.1f%% pixels, %+.1f%% luminance mean"
              % (100 * (c["pixels"] / max(s["pixels"], 1) - 1),
                 100 * (c["luminance_mean"] / max(s["luminance_mean"], 1e-6) - 1)))
        if "reference" in row:
            r = row["reference"]
            print("   CS2 capture           : %6d px, luminance mean %.4f median %.4f"
                  % (r["pixels"], r["luminance_mean"], r["luminance_median"]))
            print("   CS2 render vs capture: %+.2f%% mean, %+.2f%% median"
                  % (100 * (c["luminance_mean"] / r["luminance_mean"] - 1),
                     100 * (c["luminance_median"] / r["luminance_median"] - 1)))
    if args.json:
        args.json.write_text(json.dumps(rows, indent=1), "utf-8")
        print("wrote %s" % args.json)


if __name__ == "__main__":
    main()
