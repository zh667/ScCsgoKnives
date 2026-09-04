#!/usr/bin/env python3
"""The single acceptance entry point against real CS2 reference stills.

    python3 tools/cs2_reference_check.py --manifest reference/cs2/<date>/capture.json

Replaces cs2_videocheck.py, whose hand-filled LANDMARKS table this manifest
supersedes. cs2_render_check.py stays, but it compares our pipeline against our
pipeline and is a development tool, not an acceptance one.

Design rules, each of which exists because the obvious alternative produces a
false PASS:

  offsets are never fitted.  Refitting viewmodel_offset absorbs a +-2 degree
      error in viewmodel_fov down to 2.2-4.7 px worst residual over eight
      landmarks, so a 10 px pass with free offsets proves nothing about the FOV.
      They are pinned to the values the manifest records reading back from the
      game console. --fit-fov prints a diagnostic and never touches a verdict.

  masks are explicit.  No background is ever guessed from the image.

  photometry is relative.  Fitting one ambient multiplier makes the mean match by
      construction, so the mean is not a test. One multiplier is fitted on one
      gun and validated on the other two: this measures whether the three guns'
      materials were installed in the right relation to each other, which is what
      the material port actually decides. The item is named accordingly.

  the left hand carries the hand test.  At this machine's viewmodel_offset_x of
      2.5 the right hand is 13.3 % on screen (327 of 2466 weighted vertices, in a
      275x100 px corner); it is reported, never gated.

  a missing input is a FAIL, not a skip, and there is no overall PASS field.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import subprocess
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))

import cs2_arms_selftest as arms
import cs2_placement as place
import cs2_viewmodel as vm
from cs2_rig_selftest import GUNS

ROOT = Path(__file__).resolve().parent.parent
FORMAT = "ScCsgoKnives.Cs2Reference/1"


class Item:
    """One acceptance line. Missing input is a FAIL with a reason, never a silent skip."""

    def __init__(self, name):
        self.name = name
        self.status = "FAIL"
        self.reason = "not evaluated"
        self.data = {}

    def fail(self, reason, **data):
        self.status, self.reason = "FAIL", reason
        self.data.update(data)
        return self

    def ok(self, reason="", **data):
        self.status, self.reason = "PASS", reason
        self.data.update(data)
        return self

    def as_dict(self):
        return {"item": self.name, "status": self.status, "reason": self.reason, **self.data}


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as fh:
        for block in iter(lambda: fh.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def load_mask(path: Path, size):
    a = np.asarray(Image.open(path).convert("L"))
    if (a.shape[1], a.shape[0]) != tuple(size):
        raise ValueError("mask is %dx%d, image is %dx%d" % (a.shape[1], a.shape[0], size[0], size[1]))
    return a > 0


def srgb_to_linear(a):
    a = a / 255.0
    return np.where(a <= 0.04045, a / 12.92, ((a + 0.055) / 1.055) ** 2.4)


def luminance(rgb_linear):
    return rgb_linear @ np.array([0.2126, 0.7152, 0.0722])


# --- INPUT -------------------------------------------------------------------
def check_input(manifest, base: Path):
    item = Item("INPUT")
    if manifest.get("format") != FORMAT:
        return item.fail("manifest format is %r, expected %r" % (manifest.get("format"), FORMAT))
    space = manifest.get("comparison_space") or {}
    if not space.get("width") or not space.get("height"):
        return item.fail("comparison_space.width/height missing")
    cvars = (manifest.get("cs2") or {}).get("cvars") or {}
    needed = ["viewmodel_fov", "viewmodel_offset_x", "viewmodel_offset_y", "viewmodel_offset_z"]
    missing = [c for c in needed if c not in cvars]
    if missing:
        return item.fail("cvars not recorded: %s" % ", ".join(missing))
    shots = manifest.get("shots") or []
    if not shots:
        return item.fail("no shots in the manifest")

    problems = []
    checked = []
    for shot in shots:
        path = base / shot["image"]
        if not path.exists():
            problems.append("%s missing" % shot["image"])
            continue
        im = Image.open(path)
        if (im.width, im.height) != (space["width"], space["height"]):
            problems.append("%s is %dx%d, comparison space is %dx%d"
                            % (shot["image"], im.width, im.height, space["width"], space["height"]))
        if im.mode not in ("RGB", "RGBA"):
            problems.append("%s mode %s, expected RGB" % (shot["image"], im.mode))
        if shot.get("sha256"):
            got = sha256(path)
            if got.lower() != shot["sha256"].lower():
                problems.append("%s sha256 mismatch" % shot["image"])
        for key in ("weapon_mask", "hand_mask"):
            if not shot.get(key):
                problems.append("%s has no %s" % (shot["image"], key))
                continue
            mask_path = base / shot[key]
            if not mask_path.exists():
                problems.append("%s missing" % shot[key])
                continue
            try:
                load_mask(mask_path, (im.width, im.height))
            except ValueError as exc:
                problems.append("%s: %s" % (shot[key], exc))
        checked.append(shot["image"])
    if problems:
        return item.fail("; ".join(problems[:8]), shots_checked=checked)
    return item.ok("%d shots, sizes, colour mode, hashes and masks all agree" % len(checked),
                   shots_checked=checked, comparison_space=space)


# --- PLACE -------------------------------------------------------------------
def predict(gun, clip, t, cvars, width, height):
    over = ["Cs2ViewmodelFov=%g" % cvars["viewmodel_fov"],
            "Cs2ViewmodelOffsetX=%g" % cvars["viewmodel_offset_x"],
            "Cs2ViewmodelOffsetY=%g" % cvars["viewmodel_offset_y"],
            "Cs2ViewmodelOffsetZ=%g" % cvars["viewmodel_offset_z"]]
    out = subprocess.run(
        ["dotnet", "run", "--project", str(ROOT / "tools/ArmPreview/ArmPreview.csproj"),
         "-c", "Release", "--", "cs2", gun, clip, "%r" % t, str(width), str(height)] + over,
        capture_output=True, text=True, cwd=ROOT,
        env={**os.environ, "DOTNET_ROLL_FORWARD": "Major"})
    if out.returncode:
        raise RuntimeError(out.stderr.strip()[-600:])
    return json.loads(out.stdout.strip().splitlines()[-1])


def check_place(manifest, base: Path, threshold=10.0):
    item = Item("PLACE")
    space = manifest["comparison_space"]
    cvars = manifest["cs2"]["cvars"]
    rows = []
    worst = 0.0
    for shot in manifest.get("shots") or []:
        if not shot.get("landmarks"):
            return item.fail("%s has no landmarks file" % shot["image"])
        lm_path = base / shot["landmarks"]
        if not lm_path.exists():
            return item.fail("%s missing" % shot["landmarks"])
        want = (json.loads(lm_path.read_text("utf-8")) or {}).get("weapon") or {}
        if not want:
            return item.fail("%s has no weapon landmarks" % shot["landmarks"])
        try:
            got = predict(shot["gun"], shot.get("clip", "idle"), float(shot.get("t", 0.0)),
                          cvars, space["width"], space["height"])
        except RuntimeError as exc:
            return item.fail("prediction failed for %s: %s" % (shot["image"], exc))
        for name, (vx, vy) in want.items():
            hit = got["lm"].get(name)
            if hit is None:
                rows.append({"shot": shot["image"], "landmark": name, "status": "not in the rig"})
                continue
            sx, sy = hit["screen"]
            d = float(np.hypot(sx - vx, sy - vy))
            worst = max(worst, d)
            rows.append({"shot": shot["image"], "landmark": name,
                         "reference": [vx, vy], "chain": [round(sx, 1), round(sy, 1)],
                         "error_px": round(d, 2)})
    unknown = [r for r in rows if r.get("status")]
    if unknown:
        return item.fail("landmarks not in the rig: %s"
                         % ", ".join(r["landmark"] for r in unknown), landmarks=rows)
    if not rows:
        return item.fail("no landmarks compared", landmarks=rows)
    if worst >= threshold:
        return item.fail("worst landmark %.1f px, threshold %.0f" % (worst, threshold),
                         worst_px=round(worst, 2), landmarks=rows)
    return item.ok("worst landmark %.1f px over %d points" % (worst, len(rows)),
                   worst_px=round(worst, 2), landmarks=rows)


def diagnose_fov(manifest, base: Path):
    """Diagnostic only. Never touches a verdict - see the module docstring."""
    space = manifest["comparison_space"]
    cvars = dict(manifest["cs2"]["cvars"])
    out = []
    for fov in np.arange(58.0, 80.5, 1.0):
        trial = dict(cvars, viewmodel_fov=float(fov))
        worst = 0.0
        for shot in manifest.get("shots") or []:
            lm = (json.loads((base / shot["landmarks"]).read_text("utf-8")) or {}).get("weapon") or {}
            got = predict(shot["gun"], shot.get("clip", "idle"), float(shot.get("t", 0.0)),
                          trial, space["width"], space["height"])
            for name, (vx, vy) in lm.items():
                hit = got["lm"].get(name)
                if hit:
                    worst = max(worst, float(np.hypot(hit["screen"][0] - vx, hit["screen"][1] - vy)))
        out.append({"viewmodel_fov": float(fov), "worst_px": round(worst, 2)})
    return out


# --- PHOTO -------------------------------------------------------------------
def check_photo(manifest, base: Path, anchor=None, mean_tol=0.05, shape_tol=0.10):
    item = Item("PHOTO: fixed-scene three-gun relative photometric consistency")
    shots = [s for s in (manifest.get("shots") or []) if s.get("state", "idle") == "idle"]
    if len(shots) < 2:
        return item.fail("need at least two idle shots to compare guns against each other")

    ours = {}
    theirs = {}
    for shot in shots:
        image = base / shot["image"]
        mask_path = base / shot["weapon_mask"]
        if not image.exists() or not mask_path.exists():
            return item.fail("%s or its weapon mask is missing" % shot["image"])
        a = np.asarray(Image.open(image).convert("RGB"), float)
        mask = load_mask(mask_path, (a.shape[1], a.shape[0]))
        if mask.sum() < 500:
            return item.fail("%s weapon mask covers only %d px" % (shot["image"], mask.sum()))
        lum = luminance(srgb_to_linear(a[mask]))
        theirs[shot["gun"]] = lum
        render = base / (shot.get("our_render") or "")
        render_mask = base / (shot.get("our_render_mask") or "")
        if not shot.get("our_render") or not render.exists() or not render_mask.exists():
            return item.fail("%s has no matching offline render + mask (our_render / our_render_mask)"
                             % shot["gun"])
        r = np.asarray(Image.open(render).convert("RGB"), float)
        rm = load_mask(render_mask, (r.shape[1], r.shape[0]))
        ours[shot["gun"]] = luminance(srgb_to_linear(r[rm]))

    guns = sorted(theirs)
    anchor = anchor or guns[0]
    if anchor not in ours:
        return item.fail("anchor gun %s has no render" % anchor)
    k = float(np.mean(theirs[anchor]) / max(np.mean(ours[anchor]), 1e-9))

    rows = []
    worst_mean = 0.0
    worst_shape = 0.0
    for gun in guns:
        scaled = ours[gun] * k
        mean_err = abs(float(np.mean(scaled) / max(np.mean(theirs[gun]), 1e-9)) - 1.0)
        def shape(x):
            m = np.median(x)
            return float(np.percentile(x, 10) / max(m, 1e-9)), float(np.percentile(x, 90) / max(m, 1e-9))
        p10_o, p90_o = shape(scaled)
        p10_t, p90_t = shape(theirs[gun])
        shape_err = max(abs(p10_o / max(p10_t, 1e-9) - 1.0), abs(p90_o / max(p90_t, 1e-9) - 1.0))
        rows.append({"gun": gun, "anchor": gun == anchor,
                     "mean_error": round(mean_err, 4), "shape_error": round(shape_err, 4),
                     "p10_over_median": [round(p10_o, 4), round(p10_t, 4)],
                     "p90_over_median": [round(p90_o, 4), round(p90_t, 4)]})
        if gun != anchor:
            worst_mean = max(worst_mean, mean_err)
        worst_shape = max(worst_shape, shape_err)

    if len(guns) < 3:
        return item.fail("only %d guns present; the test needs three to be meaningful"
                         % len(guns), multiplier=round(k, 5), guns=rows)
    if worst_mean >= mean_tol or worst_shape >= shape_tol:
        return item.fail("worst validated mean error %.1f%% (limit %.0f%%), worst shape error "
                         "%.1f%% (limit %.0f%%)" % (100 * worst_mean, 100 * mean_tol,
                                                    100 * worst_shape, 100 * shape_tol),
                         multiplier=round(k, 5), guns=rows)
    return item.ok("one multiplier %.4f fitted on %s; the other guns match to %.1f%% mean, "
                   "%.1f%% shape" % (k, anchor, 100 * worst_mean, 100 * worst_shape),
                   multiplier=round(k, 5), guns=rows)


# --- HAND --------------------------------------------------------------------
def left_hand_mask(gun, clip, t, cvars, width, height):
    """Rasterise the left-hand triangles of the CS2 arms under the same placement.

    The maths is the reference implementation in cs2_placement/cs2_arms_selftest;
    cs2_placement_selftest.py is what establishes that the shipped C# agrees with
    it (5.4e-06 m, 0.05 px), so a mask built here stands for what the mod draws.
    """
    joints, ibm, pos, nor, w, j, mesh = arms.load_arms()
    cfg = GUNS[gun]
    stem = {v: k for k, v in cfg["clips"].items()}.get(clip, clip)
    c = vm.load_clip(vm.CLIPS / cfg["folder"] / (stem + ".dmx"))
    skinned = arms.skin(joints, ibm, pos, w, j, c.absolute(t), place.placement(cvars))
    fx, fy = place.projection_scales(cvars["viewmodel_fov"], width / height)
    s = place.to_screen(skinned, fx, fy, width, height)

    left = np.zeros(len(pos), bool)
    for k in range(4):
        left |= np.array([joints[x].endswith("_L") for x in j[:, k]]) & (w[:, k] > 0.5)

    mask = np.zeros((height, width), bool)
    yy, xx = np.mgrid[0:height, 0:width]
    for prim in mesh.primitives:
        tris = prim.indices.reshape(-1, 3)
        keep = left[tris].all(axis=1) & (s[tris][:, :, 2] > 0.02).all(axis=1)
        for tri in tris[keep]:
            p = s[tri][:, :2]
            x0, y0 = np.floor(p.min(0)).astype(int)
            x1, y1 = np.ceil(p.max(0)).astype(int) + 1
            x0, y0 = max(x0, 0), max(y0, 0)
            x1, y1 = min(x1, width), min(y1, height)
            if x1 <= x0 or y1 <= y0:
                continue
            gx, gy = xx[y0:y1, x0:x1], yy[y0:y1, x0:x1]
            d = ((p[1, 1] - p[2, 1]) * (p[0, 0] - p[2, 0]) + (p[2, 0] - p[1, 0]) * (p[0, 1] - p[2, 1]))
            if abs(d) < 1e-9:
                continue
            a = ((p[1, 1] - p[2, 1]) * (gx - p[2, 0]) + (p[2, 0] - p[1, 0]) * (gy - p[2, 1])) / d
            b = ((p[2, 1] - p[0, 1]) * (gx - p[2, 0]) + (p[0, 0] - p[2, 0]) * (gy - p[2, 1])) / d
            inside = (a >= 0) & (b >= 0) & (a + b <= 1)
            mask[y0:y1, x0:x1] |= inside
    return mask


def dilate(mask, radius):
    out = mask.copy()
    for _ in range(radius):
        shifted = out.copy()
        shifted[1:, :] |= out[:-1, :]
        shifted[:-1, :] |= out[1:, :]
        shifted[:, 1:] |= out[:, :-1]
        shifted[:, :-1] |= out[:, 1:]
        out = shifted
    return out


def check_hand(manifest, base: Path, iou_min=0.85, centroid_max=10.0, landmark_max=10.0):
    item = Item("HAND: left hand in the grip ROI")
    agent = (manifest.get("cs2") or {})
    if not agent.get("agent") or not agent.get("team") or agent.get("gloves") is None:
        return item.fail("agent_unverified: team/agent/gloves not recorded, so the reference "
                         "arms cannot be shown to be bare_arm_133 + glove_fingerless")
    if not agent.get("arms_verified"):
        return item.fail("agent_unverified: cs2.arms_verified is not set; run "
                         "cs2_capture_probe.py --arm-tone across candidate loadouts first")

    space = manifest["comparison_space"]
    cvars = manifest["cs2"]["cvars"]
    rows = []
    worst_iou = 1.0
    worst_centroid = 0.0
    worst_landmark = 0.0
    for shot in manifest.get("shots") or []:
        if shot.get("state", "idle") != "idle":
            continue
        hand_path = base / shot["hand_mask"]
        weapon_path = base / shot["weapon_mask"]
        if not hand_path.exists() or not weapon_path.exists():
            return item.fail("%s: hand or weapon mask missing" % shot["image"])
        ref_hand = load_mask(hand_path, (space["width"], space["height"]))
        ref_weapon = load_mask(weapon_path, (space["width"], space["height"]))
        ours = left_hand_mask(shot["gun"], shot.get("clip", "idle"), float(shot.get("t", 0.0)),
                              cvars, space["width"], space["height"])
        # Grip / contact ROI: where the hand and the weapon meet, not the whole arm.
        roi = (dilate(ref_hand, 12) & dilate(ref_weapon, 12)) | (ref_hand & dilate(ref_weapon, 24))
        if roi.sum() < 200:
            return item.fail("%s: grip ROI is only %d px; check the masks" % (shot["image"], roi.sum()))
        a, b = ref_hand & roi, ours & roi
        union = (a | b).sum()
        iou = float((a & b).sum() / union) if union else 0.0

        def centroid(m):
            ys, xs = np.nonzero(m)
            return np.array([xs.mean(), ys.mean()]) if len(xs) else np.array([np.nan, np.nan])

        dc = float(np.linalg.norm(centroid(a) - centroid(b)))
        row = {"shot": shot["image"], "gun": shot["gun"], "roi_px": int(roi.sum()),
               "iou": round(iou, 4), "centroid_px": round(dc, 2)}

        lm_path = base / shot.get("landmarks", "")
        if lm_path.exists():
            want = (json.loads(lm_path.read_text("utf-8")) or {}).get("left_hand") or {}
            if want:
                got = predict(shot["gun"], shot.get("clip", "idle"), float(shot.get("t", 0.0)),
                              cvars, space["width"], space["height"])
                marks = []
                for name, (vx, vy) in want.items():
                    hit = got["lm"].get(name)
                    if hit is None:
                        marks.append({"landmark": name, "status": "not emitted by ArmPreview cs2"})
                        continue
                    d = float(np.hypot(hit["screen"][0] - vx, hit["screen"][1] - vy))
                    worst_landmark = max(worst_landmark, d)
                    marks.append({"landmark": name, "error_px": round(d, 2)})
                row["landmarks"] = marks
        rows.append(row)
        worst_iou = min(worst_iou, iou)
        worst_centroid = max(worst_centroid, dc)

    if not rows:
        return item.fail("no idle shots to compare")
    if worst_iou < iou_min or worst_centroid >= centroid_max or worst_landmark >= landmark_max:
        return item.fail("worst IoU %.3f (min %.2f), worst centroid %.1f px, worst landmark %.1f px"
                         % (worst_iou, iou_min, worst_centroid, worst_landmark), shots=rows)
    return item.ok("worst IoU %.3f, centroid %.1f px, landmark %.1f px"
                   % (worst_iou, worst_centroid, worst_landmark), shots=rows)


def check_hand_right(manifest, base: Path):
    """Reported, never gated: at offset_x 2.5 the right hand is 13.3 % on screen."""
    item = Item("HAND-R: right hand (report only)")
    return item.ok("not gated; at this machine's viewmodel_offset_x the right hand is "
                   "13.3% on screen (327 of 2466 weighted vertices)", gated=False)


# --- SOUND -------------------------------------------------------------------
def check_sound():
    item = Item("SOUND: shipped cue coverage")
    path = ROOT / "src/ScCsgoKnives/AnimationData/cs2_sounds.json"
    if not path.exists():
        return item.fail("cs2_sounds.json missing")
    doc = json.loads(path.read_text("utf-8"))
    total = sum(len(c["Cues"]) for c in doc["Clips"].values())
    missing = [(k, q["Event"]) for k, c in doc["Clips"].items() for q in c["Cues"] if not q["Asset"]]
    if missing:
        return item.fail("%d of %d cues have no shipped OGG and are dropped at load"
                         % (len(missing), total), total=total,
                         missing=[{"clip": k, "event": e} for k, e in missing])
    return item.ok("%d of %d cues resolve to a shipped OGG; trigger times are source-verified "
                   "from the .vnmclip event tracks, not from recorded audio" % (total, total),
                   total=total)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--manifest", type=Path, required=True)
    ap.add_argument("--anchor", help="gun the photometric multiplier is fitted on")
    ap.add_argument("--fit-fov", action="store_true", help="diagnostic sweep; never affects a verdict")
    ap.add_argument("--out", type=Path, help="directory for the report (default: next to the manifest)")
    args = ap.parse_args()

    base = args.manifest.parent
    out_dir = args.out or base
    try:
        manifest = json.loads(args.manifest.read_text("utf-8"))
    except Exception as exc:
        print("cannot read manifest: %s" % exc)
        return 2

    items = [check_input(manifest, base)]
    if items[0].status == "PASS":
        for fn in (check_place, check_photo, check_hand):
            try:
                items.append(fn(manifest, base) if fn is not check_photo
                             else fn(manifest, base, args.anchor))
            except Exception as exc:
                items.append(Item(fn.__name__).fail("raised %s: %s" % (type(exc).__name__, exc)))
    else:
        for name in ("PLACE", "PHOTO: fixed-scene three-gun relative photometric consistency",
                     "HAND: left hand in the grip ROI"):
            items.append(Item(name).fail("INPUT failed; nothing downstream can be trusted"))
    items.append(check_hand_right(manifest, base))
    items.append(check_sound())

    diagnostics = {}
    if args.fit_fov and items[0].status == "PASS":
        try:
            diagnostics["fit_fov"] = diagnose_fov(manifest, base)
        except Exception as exc:
            diagnostics["fit_fov_error"] = str(exc)

    print("CS2 reference check - %s\n" % args.manifest)
    for it in items:
        print("  %-6s %s" % (it.status, it.name))
        print("         %s" % it.reason)
    if diagnostics.get("fit_fov"):
        best = min(diagnostics["fit_fov"], key=lambda r: r["worst_px"])
        print("\n  diagnostic only: worst-landmark error is lowest at viewmodel_fov %.0f "
              "(%.1f px); the manifest records %g. This does NOT affect any verdict - "
              "offsets are pinned, and refitting them absorbs a 2 degree FOV error."
              % (best["viewmodel_fov"], best["worst_px"], manifest["cs2"]["cvars"]["viewmodel_fov"]))

    gating = [it for it in items if it.name != "HAND-R: right hand (report only)"]
    ready = all(it.status == "PASS" for it in gating)
    print("\n  %d of %d gating items pass. %s"
          % (sum(1 for it in gating if it.status == "PASS"), len(gating),
             "All gating items pass; GunProfile may be defaulted to 1."
             if ready else "Not ready to default GunProfile to 1."))

    out_dir.mkdir(parents=True, exist_ok=True)
    report = {"manifest": str(args.manifest), "items": [it.as_dict() for it in items],
              "diagnostics": diagnostics}
    (out_dir / "cs2-reference-report.json").write_text(json.dumps(report, indent=2), "utf-8")
    lines = ["# CS2 reference check", "", "Manifest: `%s`" % args.manifest, ""]
    for it in items:
        lines += ["## %s: %s" % (it.status, it.name), "", it.reason, ""]
    (out_dir / "cs2-reference-report.md").write_text("\n".join(lines), "utf-8")
    print("  wrote %s and .md" % (out_dir / "cs2-reference-report.json"))
    return 0 if ready else 1


if __name__ == "__main__":
    raise SystemExit(main())
