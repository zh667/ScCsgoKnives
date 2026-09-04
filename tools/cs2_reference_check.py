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

  masks are occluded.  Weapon and arms go through one z-buffer (cs2_raster), so the
      left-hand mask is what a screenshot can contain. Projecting the arm triangles
      without depth made it 54 % larger than the visible hand (36 339 px against
      23 604 at idle), and the grip ROI is exactly where the occlusion is strongest,
      so an un-occluded mask fails correct geometry.

  silhouettes, not bone origins.  A muzzle or trigger bone origin is inside the
      model and cannot be pointed at on a screenshot. Both PLACE and HAND are
      scored on the visible silhouette - IoU, contour distance, centroid - and on
      extremes derived from the mask by the same code on both sides. Named
      landmarks stay supported as a diagnostic, and if a manifest lists any then
      every one of them must resolve or the item fails.

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

import cs2_placement as place
import cs2_raster as raster
from cs2_rig_selftest import GUNS

ROOT = Path(__file__).resolve().parent.parent
FORMAT = "ScCsgoKnives.Cs2Reference/1"

# Settled by the Windows capture probe on 2026-09-05: Game Bar returns the render
# natively, 1400x1050, 4:3, lossless RGBA PNG with alpha fully opaque, sRGB encoded
# (gamma 0.45455). No pillarbox, no stretch, so the capture transform is the
# identity and no resampling ever touches a reference image.
COMPARISON_SIZE = [1400, 1050]
IDENTITY = [[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]]
SRGB_GAMMA = 0.45455


def comparison_size(manifest):
    """Canonical `comparison_size: [w, h]`; `comparison_space` is the older spelling."""
    size = manifest.get("comparison_size")
    if size:
        return list(size)
    space = manifest.get("comparison_space") or {}
    if space.get("width") and space.get("height"):
        return [space["width"], space["height"]]
    return None


def transform_is_identity(m):
    if m is None:
        return False
    a = np.asarray(m, float)
    if a.shape == (2, 3):
        a = np.vstack([a, [0.0, 0.0, 1.0]])
    return a.shape == (3, 3) and bool(np.allclose(a, np.eye(3), atol=1e-9))


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
    size = comparison_size(manifest)
    if not size:
        return item.fail("comparison_size missing")
    if size != COMPARISON_SIZE:
        return item.fail("comparison_size is %s; the probe settled this machine at %s"
                         % (size, COMPARISON_SIZE))
    transform = manifest.get("capture_transform")
    if not transform_is_identity(transform):
        return item.fail("capture_transform must be the identity - the probe showed the "
                         "capture is native 1400x1050 with no stretch, so any other "
                         "transform means the image was resampled; got %r" % (transform,))
    colour = (manifest.get("cs2") or {}).get("color") or {}
    if "gamma" not in colour:
        return item.fail("cs2.color.gamma is required; the probe recorded sRGB %s" % SRGB_GAMMA)
    if abs(float(colour["gamma"]) - SRGB_GAMMA) > 1e-3:
        return item.fail("colour gamma %s, expected sRGB %s" % (colour["gamma"], SRGB_GAMMA))
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
        if [im.width, im.height] != size:
            problems.append("%s is %dx%d, comparison size is %dx%d"
                            % (shot["image"], im.width, im.height, size[0], size[1]))
        if im.mode not in ("RGB", "RGBA"):
            problems.append("%s mode %s, expected RGB or RGBA" % (shot["image"], im.mode))
        elif im.mode == "RGBA":
            alpha = np.asarray(im)[..., 3]
            if alpha.min() != 255:
                problems.append("%s has non-opaque alpha (min %d); the probe found the "
                                "capture fully opaque, so this image is not a raw capture"
                                % (shot["image"], alpha.min()))
        if not shot.get("sha256"):
            problems.append("%s has no sha256; every reference file must be hashed" % shot["image"])
        elif sha256(path).lower() != shot["sha256"].lower():
            problems.append("%s sha256 mismatch" % shot["image"])
        # The file's own colour metadata, not the manifest's claim about it. A PNG
        # carrying an sRGB chunk, or a gAMA of 1/2.2, is consistent; anything else is
        # a re-encode and the manifest is describing a file it no longer has.
        info = im.info or {}
        if "srgb" not in info:
            file_gamma = info.get("gamma")
            if file_gamma is None and "icc_profile" not in info:
                problems.append("%s carries no sRGB chunk, gAMA or ICC profile" % shot["image"])
            elif file_gamma is not None and abs(float(file_gamma) - 1.0 / 2.2) > 5e-3:
                problems.append("%s gAMA is %.5f, not 1/2.2" % (shot["image"], file_gamma))
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
    return item.ok("%d shots at %dx%d, identity capture transform, opaque alpha, hashes "
                   "and masks all agree" % (len(checked), size[0], size[1]),
                   shots_checked=checked, comparison_size=size)


def silhouette_extremes(mask):
    """Well-defined points derivable from a mask by identical code on both sides.

    Deliberately not bone origins: those sit inside the model and cannot be pointed
    at on a screenshot. These are the four axis extremes and the two ends of the
    principal axis, which are on the visible outline.
    """
    ys, xs = np.nonzero(mask)
    if not len(xs):
        return {}
    pts = np.c_[xs, ys].astype(float)
    out = {"leftmost": pts[xs.argmin()], "rightmost": pts[xs.argmax()],
           "topmost": pts[ys.argmin()], "bottommost": pts[ys.argmax()]}
    centre = pts.mean(0)
    axis = np.linalg.svd(pts - centre, full_matrices=False)[2][0]
    proj = (pts - centre) @ axis
    out["axis_min"] = pts[proj.argmin()]
    out["axis_max"] = pts[proj.argmax()]
    return {k: [round(float(v[0]), 1), round(float(v[1]), 1)] for k, v in out.items()}


def compare_masks(reference, ours, label):
    """IoU, contour distance and centroid between two masks, plus derived extremes."""
    mean, p95, worst = raster.chamfer(reference, ours)
    ca, cb = raster.centroid(reference), raster.centroid(ours)
    dc = float(np.linalg.norm(ca - cb)) if ca is not None and cb is not None else float("nan")
    ea, eb = silhouette_extremes(reference), silhouette_extremes(ours)
    extremes = {k: round(float(np.hypot(ea[k][0] - eb[k][0], ea[k][1] - eb[k][1])), 2)
                for k in ea if k in eb}
    return {"label": label,
            "reference_px": int(reference.sum()), "ours_px": int(ours.sum()),
            "iou": round(raster.iou(reference, ours), 4),
            "contour_mean_px": None if mean != mean else round(mean, 2),
            "contour_p95_px": None if p95 != p95 else round(p95, 2),
            "contour_max_px": None if worst != worst else round(worst, 2),
            "centroid_px": None if dc != dc else round(dc, 2),
            "extremes_px": extremes}


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


def check_place(manifest, base: Path, threshold=10.0, iou_min=0.90,
                contour_mean_max=3.0):
    """Weapon placement, scored on the visible silhouette.

    The gate is the silhouette because that is what a screenshot contains; named
    bone landmarks are a diagnostic, and if a manifest supplies any then every one
    must resolve or this fails - a landmark the rig cannot produce must never be
    quietly skipped, which is how a hand test with three unresolved landmarks used
    to pass with a worst error of zero.
    """
    item = Item("PLACE: weapon silhouette and placement")
    width, height = comparison_size(manifest)
    cvars = manifest["cs2"]["cvars"]
    rows = []
    unresolved = []
    worst_landmark = 0.0
    worst_extreme = 0.0
    worst_centroid = 0.0
    worst_p95 = 0.0
    lowest_iou = 1.0
    for shot in manifest.get("shots") or []:
        mask_path = base / shot["weapon_mask"]
        if not mask_path.exists():
            return item.fail("%s missing" % shot["weapon_mask"])
        reference = load_mask(mask_path, (width, height))
        try:
            ours = raster.masks(shot["gun"], shot.get("clip", "idle"),
                                float(shot.get("t", 0.0)), cvars, width, height)["weapon"]
        except Exception as exc:
            return item.fail("rasterising %s failed: %s: %s"
                             % (shot["image"], type(exc).__name__, exc))
        row = compare_masks(reference, ours, shot["image"])
        row["gun"] = shot["gun"]
        lowest_iou = min(lowest_iou, row["iou"])
        for key, target in (("contour_p95_px", "p95"), ("centroid_px", "centroid")):
            if row[key] is None:
                return item.fail("%s: %s could not be measured (empty mask?)" % (shot["image"], target))
        worst_p95 = max(worst_p95, row["contour_p95_px"])
        worst_centroid = max(worst_centroid, row["centroid_px"])
        if row["extremes_px"]:
            worst_extreme = max(worst_extreme, max(row["extremes_px"].values()))

        lm_file = shot.get("landmarks")
        if lm_file and (base / lm_file).exists():
            want = (json.loads((base / lm_file).read_text("utf-8")) or {}).get("weapon") or {}
            if want:
                try:
                    got = predict(shot["gun"], shot.get("clip", "idle"),
                                  float(shot.get("t", 0.0)), cvars, width, height)
                except RuntimeError as exc:
                    return item.fail("prediction failed for %s: %s" % (shot["image"], exc))
                marks = []
                for name, (vx, vy) in want.items():
                    hit = got["lm"].get(name)
                    if hit is None:
                        unresolved.append("%s/%s" % (shot["image"], name))
                        continue
                    d = float(np.hypot(hit["screen"][0] - vx, hit["screen"][1] - vy))
                    worst_landmark = max(worst_landmark, d)
                    marks.append({"landmark": name, "error_px": round(d, 2)})
                row["landmarks"] = marks
        rows.append(row)

    if not rows:
        return item.fail("no shots to compare", shots=rows)
    if unresolved:
        return item.fail("the rig cannot produce these landmarks, so they were never "
                         "compared: %s" % ", ".join(unresolved), shots=rows)
    problems = []
    if lowest_iou < iou_min:
        problems.append("worst IoU %.3f (min %.2f)" % (lowest_iou, iou_min))
    if worst_p95 >= threshold:
        problems.append("worst contour p95 %.1f px (limit %.0f)" % (worst_p95, threshold))
    if worst_centroid >= threshold:
        problems.append("worst centroid %.1f px" % worst_centroid)
    if worst_extreme >= threshold:
        problems.append("worst silhouette extreme %.1f px" % worst_extreme)
    if worst_landmark >= threshold:
        problems.append("worst named landmark %.1f px" % worst_landmark)
    if problems:
        return item.fail("; ".join(problems), shots=rows)
    return item.ok("worst IoU %.3f, contour p95 %.1f px, centroid %.1f px, extreme %.1f px, "
                   "landmark %.1f px over %d shots"
                   % (lowest_iou, worst_p95, worst_centroid, worst_extreme, worst_landmark, len(rows)),
                   shots=rows)


def diagnose_fov(manifest, base: Path):
    """Diagnostic only. Never touches a verdict - see the module docstring."""
    width, height = comparison_size(manifest)
    cvars = dict(manifest["cs2"]["cvars"])
    out = []
    for fov in np.arange(58.0, 80.5, 1.0):
        trial = dict(cvars, viewmodel_fov=float(fov))
        worst = 0.0
        for shot in manifest.get("shots") or []:
            lm = (json.loads((base / shot["landmarks"]).read_text("utf-8")) or {}).get("weapon") or {}
            got = predict(shot["gun"], shot.get("clip", "idle"), float(shot.get("t", 0.0)),
                          trial, width, height)
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
    labels = {}
    for shot in shots:
        image = base / shot["image"]
        mask_path = base / shot["weapon_mask"]
        if not image.exists() or not mask_path.exists():
            return item.fail("%s or its weapon mask is missing" % shot["image"])
        a = np.asarray(Image.open(image).convert("RGB"), float)
        mask = load_mask(mask_path, (a.shape[1], a.shape[0]))
        if mask.sum() < 500:
            return item.fail("%s weapon mask covers only %d px" % (shot["image"], mask.sum()))
        # Keyed by the shot's own image name, not by gun: two shots of the same gun in
        # different states would otherwise overwrite each other and be measured once.
        key = shot["image"]
        labels[key] = shot["gun"]
        lum = luminance(srgb_to_linear(a[mask]))
        theirs[key] = lum
        render = base / (shot.get("our_render") or "")
        render_mask = base / (shot.get("our_render_mask") or "")
        if not shot.get("our_render") or not render.exists() or not render_mask.exists():
            return item.fail("%s has no matching offline render + mask (our_render / our_render_mask)"
                             % shot["image"])
        r = np.asarray(Image.open(render).convert("RGB"), float)
        rm = load_mask(render_mask, (r.shape[1], r.shape[0]))
        ours[key] = luminance(srgb_to_linear(r[rm]))

    keys = sorted(theirs)
    if len(set(labels.values())) != len(keys):
        return item.fail("two shots of the same gun are both in the photometric set (%s); "
                         "pick one state per gun" % ", ".join("%s=%s" % (k, labels[k]) for k in keys))
    anchor = next((k for k in keys if labels[k] == anchor), None) if anchor else keys[0]
    if anchor is None or anchor not in ours:
        return item.fail("anchor has no render; candidates are %s" % ", ".join(keys))
    k = float(np.mean(theirs[anchor]) / max(np.mean(ours[anchor]), 1e-9))

    rows = []
    worst_mean = 0.0
    worst_shape = 0.0
    for gun in keys:
        scaled = ours[gun] * k
        mean_err = abs(float(np.mean(scaled) / max(np.mean(theirs[gun]), 1e-9)) - 1.0)
        def shape(x):
            m = np.median(x)
            return float(np.percentile(x, 10) / max(m, 1e-9)), float(np.percentile(x, 90) / max(m, 1e-9))
        p10_o, p90_o = shape(scaled)
        p10_t, p90_t = shape(theirs[gun])
        shape_err = max(abs(p10_o / max(p10_t, 1e-9) - 1.0), abs(p90_o / max(p90_t, 1e-9) - 1.0))
        rows.append({"shot": gun, "gun": labels[gun], "anchor": gun == anchor,
                     "mean_error": round(mean_err, 4), "shape_error": round(shape_err, 4),
                     "p10_over_median": [round(p10_o, 4), round(p10_t, 4)],
                     "p90_over_median": [round(p90_o, 4), round(p90_t, 4)]})
        if gun != anchor:
            worst_mean = max(worst_mean, mean_err)
        worst_shape = max(worst_shape, shape_err)

    if len(keys) < 3:
        return item.fail("only %d guns present; the test needs three to be meaningful"
                         % len(keys), multiplier=round(k, 5), guns=rows)
    if worst_mean >= mean_tol or worst_shape >= shape_tol:
        return item.fail("worst validated mean error %.1f%% (limit %.0f%%), worst shape error "
                         "%.1f%% (limit %.0f%%)" % (100 * worst_mean, 100 * mean_tol,
                                                    100 * worst_shape, 100 * shape_tol),
                         multiplier=round(k, 5), guns=rows)
    return item.ok("one multiplier %.4f fitted on %s; the other guns match to %.1f%% mean, "
                   "%.1f%% shape" % (k, labels[anchor], 100 * worst_mean, 100 * worst_shape),
                   multiplier=round(k, 5), guns=rows)


# --- HAND --------------------------------------------------------------------
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


def check_hand(manifest, base: Path, iou_min=0.85, threshold=10.0):
    """The left hand, scored on the *visible* mask inside the grip ROI.

    Visible means occluded: cs2_raster puts the weapon and both arms through one
    z-buffer, so the mask is what a screenshot can contain. Without that the arm
    silhouette is 54 % larger than the visible hand at idle (36 339 px against
    23 604), and since the ROI is chosen where hand and weapon meet, the extra area
    lands exactly where it does the most damage.
    """
    item = Item("HAND: visible left hand in the grip ROI")
    cs2 = manifest.get("cs2") or {}
    for field in ("team", "agent", "gloves"):
        if cs2.get(field) in (None, ""):
            return item.fail("agent_unverified: cs2.%s not recorded, so the reference arms "
                             "cannot be shown to be bare_arm_133 + glove_fingerless" % field)
    evidence = cs2.get("arms_evidence")
    if not isinstance(evidence, dict) or not evidence.get("method"):
        return item.fail("agent_unverified: cs2.arms_evidence is missing. A bare "
                         "arms_verified flag is not evidence; record the method, the "
                         "loadouts compared and the measured difference "
                         "(tools/cs2_capture_probe.py --compare)")
    if evidence.get("method") == "loadout_difference":
        delta = evidence.get("max_channel_delta")
        if delta is None or float(delta) > 2.0:
            return item.fail("agent_unverified: loadout comparison reports max channel "
                             "delta %r; needs <= 2/255 over the arm ROI to call the arms "
                             "loadout-independent" % (delta,))

    width, height = comparison_size(manifest)
    cvars = manifest["cs2"]["cvars"]
    rows = []
    unresolved = []
    lowest_iou = 1.0
    worst_p95 = 0.0
    worst_centroid = 0.0
    worst_landmark = 0.0
    seen = set()
    for shot in manifest.get("shots") or []:
        if shot.get("state", "idle") != "idle":
            continue
        if shot["gun"] in seen:
            return item.fail("two idle shots of %s; pick one state per gun" % shot["gun"])
        seen.add(shot["gun"])
        hand_path = base / shot["hand_mask"]
        weapon_path = base / shot["weapon_mask"]
        if not hand_path.exists() or not weapon_path.exists():
            return item.fail("%s: hand or weapon mask missing" % shot["image"])
        ref_hand = load_mask(hand_path, (width, height))
        ref_weapon = load_mask(weapon_path, (width, height))
        try:
            ours = raster.masks(shot["gun"], shot.get("clip", "idle"),
                                float(shot.get("t", 0.0)), cvars, width, height)["left_hand"]
        except Exception as exc:
            return item.fail("rasterising %s failed: %s: %s"
                             % (shot["image"], type(exc).__name__, exc))
        roi = (dilate(ref_hand, 12) & dilate(ref_weapon, 12)) | (ref_hand & dilate(ref_weapon, 24))
        if roi.sum() < 200:
            return item.fail("%s: grip ROI is only %d px; check the masks" % (shot["image"], roi.sum()))
        row = compare_masks(ref_hand & roi, ours & roi, shot["image"])
        row["gun"] = shot["gun"]
        row["roi_px"] = int(roi.sum())
        if row["contour_p95_px"] is None or row["centroid_px"] is None:
            return item.fail("%s: contour or centroid could not be measured inside the ROI"
                             % shot["image"], shots=rows)
        lowest_iou = min(lowest_iou, row["iou"])
        worst_p95 = max(worst_p95, row["contour_p95_px"])
        worst_centroid = max(worst_centroid, row["centroid_px"])

        lm_file = shot.get("landmarks")
        if lm_file and (base / lm_file).exists():
            want = (json.loads((base / lm_file).read_text("utf-8")) or {}).get("left_hand") or {}
            if want:
                got = predict(shot["gun"], shot.get("clip", "idle"), float(shot.get("t", 0.0)),
                              cvars, width, height)
                marks = []
                for name, (vx, vy) in want.items():
                    hit = got["lm"].get(name)
                    if hit is None:
                        unresolved.append("%s/%s" % (shot["image"], name))
                        continue
                    d = float(np.hypot(hit["screen"][0] - vx, hit["screen"][1] - vy))
                    worst_landmark = max(worst_landmark, d)
                    marks.append({"landmark": name, "error_px": round(d, 2)})
                row["landmarks"] = marks
        rows.append(row)

    if not rows:
        return item.fail("no idle shots to compare")
    if unresolved:
        return item.fail("the rig cannot produce these left-hand landmarks, so they were "
                         "never compared: %s" % ", ".join(unresolved), shots=rows)
    problems = []
    if lowest_iou < iou_min:
        problems.append("worst IoU %.3f (min %.2f)" % (lowest_iou, iou_min))
    if worst_p95 >= threshold:
        problems.append("worst contour p95 %.1f px" % worst_p95)
    if worst_centroid >= threshold:
        problems.append("worst centroid %.1f px" % worst_centroid)
    if worst_landmark >= threshold:
        problems.append("worst landmark %.1f px" % worst_landmark)
    if problems:
        return item.fail("; ".join(problems), shots=rows)
    return item.ok("worst IoU %.3f, contour p95 %.1f px, centroid %.1f px, landmark %.1f px"
                   % (lowest_iou, worst_p95, worst_centroid, worst_landmark), shots=rows)


def check_hand_right(manifest, base: Path):
    """Reported, never gated: at offset_x 2.5 the right hand is 13.3 % on screen."""
    item = Item("HAND-R: right hand (report only)")
    return item.ok("not gated; at this machine's viewmodel_offset_x the right hand is "
                   "13.3% on screen (327 of 2466 weighted vertices)", gated=False)


# --- SOUND -------------------------------------------------------------------
def check_sound(manifest, base: Path):
    """Cue coverage against the package under acceptance, not against the repo.

    cs2_sounds.json saying a cue has an Asset only means the generator found a name;
    it does not mean that OGG is inside the .scmod being tested. This opens the
    package the manifest names, checks its SHA-256, and looks for every referenced
    file inside it.
    """
    item = Item("SOUND: cue coverage in the package under test")
    package = manifest.get("package") or {}
    path = package.get("path")
    if not path:
        return item.fail("manifest has no package.path; coverage must be checked against "
                         "the .scmod under acceptance, not the working tree")
    scmod = (base / path) if not Path(path).is_absolute() else Path(path)
    if not scmod.exists():
        scmod = ROOT / path
    if not scmod.exists():
        return item.fail("package %s not found" % path)
    if not package.get("sha256"):
        return item.fail("manifest has no package.sha256")
    digest = sha256(scmod)
    if digest.lower() != package["sha256"].lower():
        return item.fail("package sha256 mismatch: %s has %s, manifest says %s"
                         % (scmod.name, digest[:16], package["sha256"][:16]))

    import zipfile
    with zipfile.ZipFile(scmod) as z:
        names = set(z.namelist())
        try:
            with z.open("Assets/AnimationData/cs2_sounds.json") as fh:
                doc = json.loads(fh.read().decode("utf-8"))
        except KeyError:
            # The sound table is embedded in the DLL, not shipped loose; fall back to
            # the source of truth in the tree but keep checking the OGGs in the package.
            doc = json.loads((ROOT / "src/ScCsgoKnives/AnimationData/cs2_sounds.json")
                             .read_text("utf-8"))
        oggs = {n.rsplit("/", 1)[-1][:-4] for n in names
                if n.startswith("Assets/Audio/") and n.endswith(".ogg")}

    total = sum(len(c["Cues"]) for c in doc["Clips"].values())
    unnamed = [(k, q["Event"]) for k, c in doc["Clips"].items() for q in c["Cues"] if not q["Asset"]]
    absent = sorted({(k, q["Asset"]) for k, c in doc["Clips"].items() for q in c["Cues"]
                     if q["Asset"] and q["Asset"] not in oggs})
    if unnamed or absent:
        return item.fail("%d cues have no asset name and %d name a file the package does "
                         "not contain" % (len(unnamed), len(absent)),
                         package=scmod.name, sha256=digest, total=total,
                         unnamed=[{"clip": k, "event": e} for k, e in unnamed],
                         absent=[{"clip": k, "asset": a} for k, a in absent])
    return item.ok("%d of %d cues resolve to an OGG present in %s (%d OGGs in the package); "
                   "trigger times are source-verified from the .vnmclip event tracks"
                   % (total, total, scmod.name, len(oggs)),
                   package=scmod.name, sha256=digest, total=total, oggs=len(oggs))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--manifest", type=Path, required=True)
    ap.add_argument("--template", action="store_true",
                    help="write a starter manifest at --manifest and exit")
    ap.add_argument("--anchor", help="gun the photometric multiplier is fitted on")
    ap.add_argument("--fit-fov", action="store_true", help="diagnostic sweep; never affects a verdict")
    ap.add_argument("--out", type=Path, help="directory for the report (default: next to the manifest)")
    args = ap.parse_args()

    base = args.manifest.parent
    out_dir = args.out or base
    if args.template:
        base.mkdir(parents=True, exist_ok=True)
        args.manifest.write_text(json.dumps(TEMPLATE, indent=1), "utf-8")
        print("wrote %s\nFill every REPLACE, then capture the shots it lists." % args.manifest)
        return 0
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
        for name in ("PLACE: weapon silhouette and placement",
                     "PHOTO: fixed-scene three-gun relative photometric consistency",
                     "HAND: visible left hand in the grip ROI"):
            items.append(Item(name).fail("INPUT failed; nothing downstream can be trusted"))
    items.append(check_hand_right(manifest, base))
    items.append(check_sound(manifest, base))

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
