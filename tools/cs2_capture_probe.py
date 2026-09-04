#!/usr/bin/env python3
"""Work out what a CS2 screenshot actually is, before anything is measured on it.

CS2 on this machine renders 1400x1050 in "Normal 4:3" fullscreen. Xbox Game Bar
captures the composited desktop, so the PNG that comes back may be the render
size, the desktop size with pillarbox bars, or the desktop size with the 4:3
image stretched to 16:9. Those three cases need different handling and only the
last one needs resampling, so the case is determined from the image rather than
assumed.

    python3 tools/cs2_capture_probe.py shot.png --render 1400x1050

Reports the pixel size, whether there are pillarbox bars, the content rectangle,
and whether the content's aspect matches the declared render aspect. The
"comparison space" it prints is what belongs in capture.json.

    python3 tools/cs2_capture_probe.py a.png --compare b.png --arm-roi X0,Y0,X1,Y1

The decisive loadout test, and the one with a defensible threshold: two captures of
the *same* frame - same position, same angles, same cvars, same map - differing only
in the loadout. The scene is identical, so any pixel difference over the forearm is
the loadout. Max channel delta <= 2/255 over the ROI means the arms do not depend on
it; anything larger means they do and the matching one has to be identified.

    python3 tools/cs2_capture_probe.py shot.png --arm-tone X0,Y0,X1,Y1

The weaker fallback, for use only when --compare shows the loadouts differ. It prints
the forearm's colour ratios against CS2's bare_arm_133 texture. Treat it as a
tie-breaker, not proof: a rendered frame has been through coloured light, shadow and
tone mapping, so its ratios are not the texture's, and there is no threshold at which
a single shot proves a match.
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
from PIL import Image

# bare_arm_133_color, measured over its opaque texels (tools/cs2_capture_probe.py
# --arm-tone compares against these). Absolute levels move with the map's light;
# the two ratios do not, much.
ARM_133 = {"mean_rgb": [178.1, 135.3, 108.1], "median_rgb": [181.0, 138.0, 110.0],
           "r_over_g": 1.316, "g_over_b": 1.252}

BAR_LEVEL = 8          # a "black" bar column never exceeds this on any channel
BAR_FRACTION = 0.995   # ... for at least this share of its pixels


def content_rect(a: np.ndarray):
    """Trim pillarbox / letterbox bars. Returns (x0, y0, x1, y1) exclusive."""
    dark = a.max(axis=2) <= BAR_LEVEL
    h, w = dark.shape
    x0 = 0
    while x0 < w and dark[:, x0].mean() >= BAR_FRACTION:
        x0 += 1
    x1 = w
    while x1 > x0 and dark[:, x1 - 1].mean() >= BAR_FRACTION:
        x1 -= 1
    y0 = 0
    while y0 < h and dark[y0, :].mean() >= BAR_FRACTION:
        y0 += 1
    y1 = h
    while y1 > y0 and dark[y1 - 1, :].mean() >= BAR_FRACTION:
        y1 -= 1
    return x0, y0, x1, y1


def probe(path: Path, render):
    im = Image.open(path)
    a = np.asarray(im.convert("RGB"))
    h, w = a.shape[:2]
    x0, y0, x1, y1 = content_rect(a)
    cw, ch = x1 - x0, y1 - y0
    out = {
        "file": path.name, "sha256_size_bytes": path.stat().st_size,
        "pixels": [w, h], "mode": im.mode,
        "bits": 8 if im.mode in ("RGB", "RGBA", "L") else None,
        "pillarbox_left": x0, "pillarbox_right": w - x1,
        "letterbox_top": y0, "letterbox_bottom": h - y1,
        "content_rect": [x0, y0, x1, y1], "content_size": [cw, ch],
        "content_aspect": round(cw / ch, 6) if ch else None,
    }
    if render:
        rw, rh = render
        out["render_size"] = [rw, rh]
        out["render_aspect"] = round(rw / rh, 6)
        ratio = (cw / ch) / (rw / rh) if ch and rh else float("nan")
        out["aspect_ratio_of_ratios"] = round(ratio, 6)
        if abs(ratio - 1.0) <= 0.005:
            out["geometry"] = "unstretched"
            out["horizontal_stretch"] = 1.0
            out["comparison_space"] = {"width": cw, "height": ch, "unstretched": False,
                                       "resample": "none"}
        else:
            out["geometry"] = "horizontally stretched" if ratio > 1 else "horizontally squeezed"
            out["horizontal_stretch"] = round(ratio, 6)
            out["comparison_space"] = {"width": int(round(ch * rw / rh)), "height": ch,
                                       "unstretched": True, "resample": "lanczos, horizontal only"}
    return out


def arm_tone(path: Path, box):
    a = np.asarray(Image.open(path).convert("RGB"), float)
    x0, y0, x1, y1 = box
    patch = a[y0:y1, x0:x1].reshape(-1, 3)
    if not len(patch):
        raise SystemExit("empty sample rectangle")
    mean = patch.mean(0)
    med = np.median(patch, 0)
    rg = float(mean[0] / max(mean[1], 1e-6))
    gb = float(mean[1] / max(mean[2], 1e-6))
    return {"box": list(box), "pixels": int(len(patch)),
            "mean_rgb": [round(float(x), 1) for x in mean],
            "median_rgb": [round(float(x), 1) for x in med],
            "r_over_g": round(rg, 4), "g_over_b": round(gb, 4),
            "target": ARM_133,
            "delta_r_over_g": round(rg - ARM_133["r_over_g"], 4),
            "delta_g_over_b": round(gb - ARM_133["g_over_b"], 4)}


def compare(a_path: Path, b_path: Path, box):
    """Two captures of one scene, differing only in loadout. Difference is the loadout."""
    a = np.asarray(Image.open(a_path).convert("RGB"), np.int16)
    b = np.asarray(Image.open(b_path).convert("RGB"), np.int16)
    if a.shape != b.shape:
        raise SystemExit("images differ in size: %s vs %s" % (a.shape[:2][::-1], b.shape[:2][::-1]))
    x0, y0, x1, y1 = box
    da = np.abs(a[y0:y1, x0:x1] - b[y0:y1, x0:x1])
    if not da.size:
        raise SystemExit("empty ROI")
    frame = np.abs(a - b)
    return {"a": a_path.name, "b": b_path.name, "roi": list(box),
            "roi_pixels": int(da.shape[0] * da.shape[1]),
            "max_channel_delta": int(da.max()),
            "mean_channel_delta": round(float(da.mean()), 4),
            "roi_pixels_differing": int((da.max(axis=2) > 2).sum()),
            "whole_frame_max_delta": int(frame.max()),
            "identical_within_2": bool(da.max() <= 2)}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("image", type=Path)
    ap.add_argument("--render", help="declared render resolution, e.g. 1400x1050")
    ap.add_argument("--arm-tone", help="forearm sample rectangle X0,Y0,X1,Y1 (weak fallback)")
    ap.add_argument("--compare", type=Path, help="second capture of the same scene, other loadout")
    ap.add_argument("--arm-roi", help="forearm rectangle X0,Y0,X1,Y1 for --compare")
    ap.add_argument("--json", type=Path)
    args = ap.parse_args()

    render = None
    if args.render:
        rw, rh = args.render.lower().split("x")
        render = (int(rw), int(rh))

    result = probe(args.image, render)
    print("%s: %dx%d %s" % (result["file"], result["pixels"][0], result["pixels"][1], result["mode"]))
    print("   bars: left %d right %d top %d bottom %d"
          % (result["pillarbox_left"], result["pillarbox_right"],
             result["letterbox_top"], result["letterbox_bottom"]))
    print("   content %dx%d, aspect %.4f"
          % (result["content_size"][0], result["content_size"][1], result["content_aspect"]))
    if render:
        print("   render  %dx%d, aspect %.4f  ->  %s (x%.4f)"
              % (render[0], render[1], result["render_aspect"],
                 result["geometry"], result["horizontal_stretch"]))
        c = result["comparison_space"]
        print("   comparison space: %dx%d, resample %s" % (c["width"], c["height"], c["resample"]))
        if c["unstretched"]:
            print("   NOTE the capture is not at the render aspect; one horizontal resample is "
                  "unavoidable and must be recorded in capture.json.")
    else:
        print("   (pass --render WxH to classify the geometry)")

    if args.compare:
        if not args.arm_roi:
            raise SystemExit("--compare needs --arm-roi X0,Y0,X1,Y1")
        box = tuple(int(v) for v in args.arm_roi.split(","))
        cmp = compare(args.image, args.compare, box)
        result["compare"] = cmp
        print("\n   loadout comparison %s vs %s over ROI %s (%d px)"
              % (cmp["a"], cmp["b"], cmp["roi"], cmp["roi_pixels"]))
        print("   max channel delta %d, mean %.4f, pixels differing by more than 2: %d"
              % (cmp["max_channel_delta"], cmp["mean_channel_delta"], cmp["roi_pixels_differing"]))
        print("   whole-frame max delta %d" % cmp["whole_frame_max_delta"])
        if cmp["identical_within_2"]:
            print("   PASS: the arms do not depend on this loadout. Record method "
                  "loadout_difference with max_channel_delta %d in cs2.arms_evidence."
                  % cmp["max_channel_delta"])
        else:
            print("   DIFFER: the loadout changes the arms. Identify which one matches "
                  "before any hand comparison; --arm-tone is only a tie-breaker.")

    if args.arm_tone:
        box = tuple(int(v) for v in args.arm_tone.split(","))
        tone = arm_tone(args.image, box)
        result["arm_tone"] = tone
        print("\n   forearm sample %s, %d px" % (tone["box"], tone["pixels"]))
        print("   mean RGB %s  R/G %.4f  G/B %.4f" % (tone["mean_rgb"], tone["r_over_g"], tone["g_over_b"]))
        print("   bare_arm_133  R/G %.4f  G/B %.4f  ->  delta %+.4f / %+.4f"
              % (ARM_133["r_over_g"], ARM_133["g_over_b"],
                 tone["delta_r_over_g"], tone["delta_g_over_b"]))
        print("   WEAK EVIDENCE: a rendered frame has been through coloured light, shadow "
              "and tone mapping, so these ratios are not the texture's. Use --compare for "
              "the decisive test; this is only a tie-breaker when loadouts differ.")

    if args.json:
        args.json.write_text(json.dumps(result, indent=2), "utf-8")


if __name__ == "__main__":
    main()
