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

    python3 tools/cs2_capture_probe.py a1.png --repeat a2.png \
            --other b1.png --other-repeat b2.png --arm-roi X0,Y0,X1,Y1

The decisive loadout test. Two loadouts, and *two captures of each*, all of the same
frame: same map, same position, same angles, same weapon, same clip, same tick, same
cvars. The same-loadout pairs measure how much the scene moves on its own - CS2 is
not deterministic to the bit, so a bare "max channel delta <= 2/255" threshold, which
is what 0.16.5 used, has nothing behind it and can fail on noise or pass on a real
difference that happens to be small. What is compared instead is the cross-loadout
difference against that measured baseline, over the same ROI, with the whole
distribution reported: mean, p95, p99, p99.9, max and the fraction of ROI pixels over
threshold.

The verdict is only "independent" when the cross-loadout distribution is inside the
baseline's, and only "different" when it is clearly outside it. When the baseline
itself is loose - a moving shadow, a smoke, an animated map prop in the ROI - the
answer is "inconclusive", not a pass.

    python3 tools/cs2_capture_probe.py a.png --compare b.png --arm-roi X0,Y0,X1,Y1

The same measurement for one pair only. It prints the distribution but refuses to
issue a verdict: without repeats there is no baseline to judge it against.

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


# How far outside the measured baseline a cross-loadout difference has to sit before
# it counts as a real difference, and how loose the baseline may be before the
# comparison is called off. One grey level of slack, and a floor of 2 so that a pair
# of bit-identical baselines does not make every 1-level difference "significant".
DELTA_SLACK = 1.0
DELTA_FLOOR = 2.0
BASELINE_LOOSE = 8.0
OVER_THRESHOLD = 2          # a pixel "differs" when a channel moves by more than this


def _pixels(path: Path) -> np.ndarray:
    return np.asarray(Image.open(path).convert("RGB"), np.int16)


def difference(a_path: Path, b_path: Path, box) -> dict:
    """The whole per-pixel difference distribution over the ROI, not one number."""
    a, b = _pixels(a_path), _pixels(b_path)
    if a.shape != b.shape:
        raise SystemExit("images differ in size: %s vs %s" % (a.shape[:2][::-1], b.shape[:2][::-1]))
    x0, y0, x1, y1 = box
    if not (0 <= x0 < x1 <= a.shape[1] and 0 <= y0 < y1 <= a.shape[0]):
        raise SystemExit("ROI %s does not fit inside %dx%d" % (list(box), a.shape[1], a.shape[0]))
    per_pixel = np.abs(a[y0:y1, x0:x1] - b[y0:y1, x0:x1]).max(axis=2).astype(float)
    if not per_pixel.size:
        raise SystemExit("empty ROI")
    return {"a": a_path.name, "b": b_path.name, "roi": list(box),
            "roi_pixels": int(per_pixel.size),
            "mean": round(float(per_pixel.mean()), 4),
            "p95": round(float(np.percentile(per_pixel, 95)), 4),
            "p99": round(float(np.percentile(per_pixel, 99)), 4),
            "p999": round(float(np.percentile(per_pixel, 99.9)), 4),
            "max": float(per_pixel.max()),
            "over_threshold_fraction": round(float((per_pixel > OVER_THRESHOLD).mean()), 6),
            "whole_frame_max": float(np.abs(a - b).max())}


def compare(a_path: Path, b_path: Path, box) -> dict:
    """One pair. Measured, but deliberately without a verdict: there is no baseline."""
    d = difference(a_path, b_path, box)
    d["verdict"] = "no_baseline"
    d["reason"] = ("a single pair cannot say whether this difference is the loadout or "
                   "the scene; capture a repeat of each loadout and use --repeat/--other-repeat")
    return d


def compare_set(a_images, b_images, box) -> dict:
    """Two loadouts, two captures each: baseline noise, then the cross-loadout test.

    The same-loadout pairs are the null hypothesis. A cross-loadout difference only
    means something if it sits outside them.
    """
    if len(a_images) < 2 or len(b_images) < 2:
        raise SystemExit("each loadout needs two captures of the same frame")
    baseline = [difference(a_images[0], a_images[1], box),
                difference(b_images[0], b_images[1], box)]
    cross = [difference(a_images[0], b_images[0], box),
             difference(a_images[1], b_images[1], box)]

    base_p999 = max(d["p999"] for d in baseline)
    base_mean = max(d["mean"] for d in baseline)
    cross_p999 = max(d["p999"] for d in cross)
    cross_mean = max(d["mean"] for d in cross)
    limit_p999 = max(base_p999 + DELTA_SLACK, DELTA_FLOOR)
    limit_mean = base_mean + 0.5

    if base_p999 > BASELINE_LOOSE:
        verdict, reason = "inconclusive", (
            "the same-loadout repeats already differ by %.2f at p99.9; the scene is not "
            "reproducible enough in this ROI to attribute anything to the loadout" % base_p999)
    elif cross_p999 <= limit_p999 and cross_mean <= limit_mean:
        verdict, reason = "independent", (
            "cross-loadout p99.9 %.2f and mean %.3f are inside the baseline's %.2f / %.3f "
            "(limits %.2f / %.3f)" % (cross_p999, cross_mean, base_p999, base_mean,
                                      limit_p999, limit_mean))
    else:
        verdict, reason = "different", (
            "cross-loadout p99.9 %.2f and mean %.3f exceed the baseline's limits %.2f / %.3f, "
            "so the arms depend on the loadout and the matching one has to be identified"
            % (cross_p999, cross_mean, limit_p999, limit_mean))

    return {"roi": list(box), "baseline": baseline, "cross": cross,
            "baseline_p999": base_p999, "baseline_mean": base_mean,
            "cross_p999": cross_p999, "cross_mean": cross_mean,
            "limit_p999": round(limit_p999, 4), "limit_mean": round(limit_mean, 4),
            "verdict": verdict, "reason": reason}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("image", type=Path)
    ap.add_argument("--render", help="declared render resolution, e.g. 1400x1050")
    ap.add_argument("--arm-tone", help="forearm sample rectangle X0,Y0,X1,Y1 (weak fallback)")
    ap.add_argument("--compare", type=Path, help="second capture of the same scene, other loadout")
    ap.add_argument("--repeat", type=Path, help="a second capture of the SAME loadout as `image`")
    ap.add_argument("--other", type=Path, help="first capture of the other loadout")
    ap.add_argument("--other-repeat", type=Path, help="a second capture of the other loadout")
    ap.add_argument("--arm-roi", help="forearm rectangle X0,Y0,X1,Y1")
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

    if args.repeat or args.other or args.other_repeat:
        missing = [n for n, v in (("--repeat", args.repeat), ("--other", args.other),
                                  ("--other-repeat", args.other_repeat)) if v is None]
        if missing:
            raise SystemExit("the baseline comparison needs all of %s" % ", ".join(missing))
        if not args.arm_roi:
            raise SystemExit("the comparison needs --arm-roi X0,Y0,X1,Y1")
        box = tuple(int(v) for v in args.arm_roi.split(","))
        cs = compare_set([args.image, args.repeat], [args.other, args.other_repeat], box)
        result["compare_set"] = cs
        print("\n   loadout comparison over ROI %s (%d px each)"
              % (list(box), cs["baseline"][0]["roi_pixels"]))
        for label, rows in (("baseline (same loadout)", cs["baseline"]), ("cross-loadout", cs["cross"])):
            for d in rows:
                print("      %-24s %-18s mean %6.3f  p95 %5.1f  p99 %5.1f  p99.9 %5.1f  max %5.1f  "
                      "over %d/255: %.4f%%"
                      % (label, "%s/%s" % (d["a"], d["b"]), d["mean"], d["p95"], d["p99"],
                         d["p999"], d["max"], OVER_THRESHOLD, 100 * d["over_threshold_fraction"]))
        print("      verdict: %s - %s" % (cs["verdict"].upper(), cs["reason"]))
        print("      put this whole block in the manifest as cs2.arms_evidence.statistics; "
              "the checker recomputes it from the hashed images and refuses a mismatch.")
        if args.json:
            args.json.write_text(json.dumps(result, indent=1), "utf-8")
        return 0

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
