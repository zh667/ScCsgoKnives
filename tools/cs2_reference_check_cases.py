#!/usr/bin/env python3
"""Negative tests for tools/cs2_reference_check.py, with the fixtures it needs.

An acceptance checker that has only ever been run on data that passes is not known
to check anything. This builds a fixture tree from the mod's own rasteriser - a
synthetic "reference capture" that is by construction a perfect match - and then a
mutation of it per fault, and asserts that each mutation fails, on the expected item,
for the expected reason.

The arms_evidence cases exist because 0.16.5's gate accepted {"method": "anything"}:
it only inspected the measured number when the method happened to be the one it knew,
so any other string walked straight past. Every field of the evidence now has a case.

Usage:  python3 tools/cs2_reference_check_cases.py [--keep]
        (on Windows: python tools\\cs2_reference_check_cases.py)
"""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import sys
import tempfile
import zipfile
from pathlib import Path

import numpy as np
from PIL import Image, PngImagePlugin

sys.path.insert(0, str(Path(__file__).resolve().parent))

import cs2_capture_probe as probe
import cs2_placement as place
import cs2_raster as raster
import cs2_render_check as rck
import cs2_run

ROOT = Path(__file__).resolve().parent.parent
W, H = 1400, 1050
ARM_ROI = [560, 700, 760, 900]


def sha(path) -> str:
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def save_png(path, arr, mode="RGB"):
    meta = PngImagePlugin.PngInfo()
    meta.add(b"gAMA", (45455).to_bytes(4, "big"))          # what Game Bar writes
    Image.fromarray(arr, mode).save(path, pnginfo=meta)


def build(base: Path, package: Path) -> dict:
    """The passing fixture, plus every mutation of it."""
    for d in ("masks", "landmarks", "render", "loadout"):
        (base / d).mkdir(parents=True, exist_ok=True)
    cvars = place.CVARS
    shots = []
    first_image = None
    for gun in ("ak47", "m4a1s", "awp"):
        m = raster.masks(gun, "idle", 0.0, cvars, W, H)
        sweep = rck.sweep(gun, "idle", base)
        retargeted = rck.retarget(sweep, gun, base)
        cs2_run.run([sys.executable, ROOT / "tools/pbr_emulate.py", "%s_cs2" % gun, retargeted,
                     base / "render" / ("%s_idle.png" % gun), W, H, "flipv=1",
                     "tex=%s_hd" % gun,
                     "mask=%s" % (base / "render" / ("%s_idle_mask.png" % gun))])
        img = np.asarray(Image.open(base / "render" / ("%s_idle.png" % gun)).convert("RGB"))
        save_png(base / ("%s_idle.png" % gun), img)
        save_png(base / "masks" / ("%s_weapon.png" % gun), (m["weapon"] * 255).astype(np.uint8), "L")
        save_png(base / "render" / ("%s_idle_mask.png" % gun), (m["weapon"] * 255).astype(np.uint8), "L")
        save_png(base / "masks" / ("%s_hand.png" % gun), (m["left_hand"] * 255).astype(np.uint8), "L")
        (base / "landmarks" / ("%s.json" % gun)).write_text(json.dumps({"weapon": {}}), "utf-8")
        shots.append({"gun": gun, "state": "idle", "clip": "idle", "t": 0.0,
                      "image": "%s_idle.png" % gun, "sha256": sha(base / ("%s_idle.png" % gun)),
                      "weapon_mask": "masks/%s_weapon.png" % gun,
                      "hand_mask": "masks/%s_hand.png" % gun,
                      "landmarks": "landmarks/%s.json" % gun,
                      "our_render": "render/%s_idle.png" % gun,
                      "our_render_mask": "render/%s_idle_mask.png" % gun})
        if first_image is None:
            first_image = img

    # Four loadout captures. CS2 is not deterministic to the bit, so the repeats carry
    # the same +-1 grey level of noise the real thing does; the two loadouts are
    # otherwise identical, which is what "the arms do not depend on the loadout" means.
    rng = np.random.default_rng(20260905)
    names = {}
    for side in ("a", "b"):
        for k in (0, 1):
            noisy = np.clip(first_image.astype(np.int16)
                            + rng.integers(-1, 2, first_image.shape, dtype=np.int16), 0, 255)
            path = base / "loadout" / ("%s%d.png" % (side, k + 1))
            save_png(path, noisy.astype(np.uint8))
            names[(side, k)] = path
    stats = probe.compare_set([names[("a", 0)], names[("a", 1)]],
                              [names[("b", 0)], names[("b", 1)]], tuple(ARM_ROI))
    evidence = {
        "method": "loadout_difference",
        "arm_roi": ARM_ROI,
        "scene": {"map": "fixture", "weapon": "ak47", "clip": "idle", "tick": "0"},
        "loadouts": {
            side: {"team": "CT" if side == "a" else "T",
                   "agent": "fixture_agent_%s" % side, "gloves": "fixture_gloves_%s" % side,
                   "images": [{"path": str(names[(side, k)].relative_to(base)),
                               "sha256": sha(names[(side, k)])} for k in (0, 1)]}
            for side in ("a", "b")},
        "statistics": {k: stats[k] for k in
                       ("baseline_p999", "baseline_mean", "cross_p999", "cross_mean", "verdict")},
    }

    manifest = {
        "format": "ScCsgoKnives.Cs2Reference/1", "comparison_size": [W, H],
        "capture_transform": [[1, 0, 0], [0, 1, 0], [0, 0, 1]], "capture_tool": "xbox_game_bar",
        "package": {"path": str(package), "sha256": sha(package)},
        "cs2": {"cvars": cvars, "team": "FIXTURE", "agent": "FIXTURE", "gloves": "none",
                "color": {"encoding": "srgb", "gamma": 0.45455},
                "arms_evidence": evidence},
        "shots": shots,
    }
    (base / "capture.json").write_text(json.dumps(manifest, indent=1), "utf-8")

    # a loadout whose arms really do change: repaint the ROI in the b captures
    for k in (0, 1):
        a = np.asarray(Image.open(names[("b", k)]).convert("RGB")).copy()
        x0, y0, x1, y1 = ARM_ROI
        a[y0:y1, x0:x1, 0] = np.clip(a[y0:y1, x0:x1, 0].astype(int) + 40, 0, 255)
        save_png(base / "loadout" / ("b%d_different.png" % (k + 1)), a)

    def variant(name, mutate):
        d = json.loads(json.dumps(manifest))
        mutate(d)
        (base / ("capture_%s.json" % name)).write_text(json.dumps(d, indent=1), "utf-8")
        return name

    def arms(d):
        return d["cs2"]["arms_evidence"]

    variant("no_mask", lambda d: [s.pop("weapon_mask") for s in d["shots"]])
    variant("no_sha", lambda d: [s.pop("sha256") for s in d["shots"]])
    variant("no_gamma", lambda d: d["cs2"]["color"].pop("gamma"))
    (base / "landmarks" / "ak47_bad.json").write_text(json.dumps(
        {"weapon": {"muzzle": [100, 100]},
         "left_hand": {"index_knuckle_over_top": [10, 10]}}), "utf-8")
    variant("bad_landmark", lambda d: d["shots"][0].update({"landmarks": "landmarks/ak47_bad.json"}))
    variant("two_m4", lambda d: d["shots"].append(dict(d["shots"][1], image="m4a1s_idle.png")))

    # --- the arms_evidence cases ---------------------------------------------
    variant("arms_missing", lambda d: d["cs2"].pop("arms_evidence"))
    variant("arms_unknown_method", lambda d: arms(d).update({"method": "anything"}))
    variant("arms_no_loadouts", lambda d: arms(d).pop("loadouts"))
    variant("arms_no_agent", lambda d: arms(d)["loadouts"]["b"].pop("agent"))
    variant("arms_one_capture",
            lambda d: arms(d)["loadouts"]["a"].update({"images": arms(d)["loadouts"]["a"]["images"][:1]}))
    variant("arms_no_roi", lambda d: arms(d).pop("arm_roi"))
    variant("arms_bad_roi", lambda d: arms(d).update({"arm_roi": [900, 900, 100, 100]}))
    variant("arms_hash_mismatch",
            lambda d: arms(d)["loadouts"]["a"]["images"][0].update({"sha256": "0" * 64}))
    variant("arms_hand_filled_delta",
            lambda d: arms(d)["statistics"].update({"cross_p999": 0.0, "cross_mean": 0.0}))
    variant("arms_negative_delta",
            lambda d: arms(d)["statistics"].update({"cross_p999": -5.0}))
    variant("arms_claimed_verdict",
            lambda d: arms(d)["statistics"].update({"verdict": "independent",
                                                    "cross_p999": 40.0, "cross_mean": 9.0}))
    variant("arms_same_file_twice",
            lambda d: arms(d)["loadouts"]["b"]["images"].__setitem__(
                0, dict(arms(d)["loadouts"]["a"]["images"][0])))

    def really_different(d):
        for k in (0, 1):
            path = base / "loadout" / ("b%d_different.png" % (k + 1))
            arms(d)["loadouts"]["b"]["images"][k] = {
                "path": str(path.relative_to(base)), "sha256": sha(path)}
        stats2 = probe.compare_set([names[("a", 0)], names[("a", 1)]],
                                   [base / "loadout" / "b1_different.png",
                                    base / "loadout" / "b2_different.png"], tuple(ARM_ROI))
        arms(d)["statistics"] = {k: stats2[k] for k in
                                 ("baseline_p999", "baseline_mean", "cross_p999", "cross_mean", "verdict")}
    variant("arms_really_different", really_different)

    # un-occluded hand mask, and a package missing an OGG
    save_png(base / "masks" / "ak47_hand_unoccluded.png",
             (unoccluded("ak47", cvars) * 255).astype(np.uint8), "L")
    variant("unoccluded_hand",
            lambda d: d["shots"][0].update({"hand_mask": "masks/ak47_hand_unoccluded.png"}))
    bad_pkg = base / "ScCsgoKnives-missing-ogg.scmod"
    with zipfile.ZipFile(package) as zin, zipfile.ZipFile(bad_pkg, "w", zipfile.ZIP_DEFLATED) as zout:
        for info in zin.infolist():
            if info.filename.endswith("/movement3.ogg"):
                continue
            zout.writestr(info, zin.read(info.filename))
    variant("missing_ogg",
            lambda d: d["package"].update({"path": str(bad_pkg), "sha256": sha(bad_pkg)}))
    return manifest


def unoccluded(gun: str, cvars) -> np.ndarray:
    """The left hand with no depth test: what a mask traced without occlusion looks like."""
    points, tris, labels = raster.arm_triangles(gun, "idle", 0.0)
    fx, fy = place.projection_scales(cvars["viewmodel_fov"], W / H)
    view = (np.c_[points, np.ones(len(points))] @ place.placement(cvars))[:, :3]
    screen = place.to_screen(view, fx, fy, W, H)
    mask = np.zeros((H, W), bool)
    yy, xx = np.mgrid[0:H, 0:W]
    for tri, label in zip(tris, labels):
        if label != raster.LEFT:
            continue
        p = screen[tri]
        if (p[:, 2] <= 0.02).any():
            continue
        x0, y0 = np.maximum(np.floor(p[:, :2].min(0)).astype(int), 0)
        x1, y1 = np.minimum(np.ceil(p[:, :2].max(0)).astype(int) + 1, [W, H])
        if x1 <= x0 or y1 <= y0:
            continue
        gx, gy = xx[y0:y1, x0:x1], yy[y0:y1, x0:x1]
        det = ((p[1, 1] - p[2, 1]) * (p[0, 0] - p[2, 0]) + (p[2, 0] - p[1, 0]) * (p[0, 1] - p[2, 1]))
        if abs(det) < 1e-9:
            continue
        a = ((p[1, 1] - p[2, 1]) * (gx - p[2, 0]) + (p[2, 0] - p[1, 0]) * (gy - p[2, 1])) / det
        b = ((p[2, 1] - p[0, 1]) * (gx - p[2, 0]) + (p[0, 0] - p[2, 0]) * (gy - p[2, 1])) / det
        mask[y0:y1, x0:x1] |= (a >= 0) & (b >= 0) & (a + b <= 1)
    return mask


# case -> (item the fault must land on, a phrase the reason must contain).
# None means the case must pass every item.
EXPECTED = {
    "": None,
    "no_mask": ("INPUT", "no weapon_mask"),
    "no_sha": ("INPUT", "must be hashed"),
    "no_gamma": ("INPUT", "gamma is required"),
    "bad_landmark": ("PLACE", "landmark"),
    "two_m4": ("HAND", "pick one state per gun"),
    "unoccluded_hand": ("HAND", "IoU"),
    "missing_ogg": ("SOUND", "the package lacks"),
    "arms_missing": ("HAND", "arms_evidence is missing"),
    "arms_unknown_method": ("HAND", "is not a method"),
    "arms_no_loadouts": ("HAND", "exactly two"),
    "arms_no_agent": ("HAND", "has no agent"),
    "arms_one_capture": ("HAND", "exactly two captures"),
    "arms_no_roi": ("HAND", "arm_roi must be"),
    "arms_bad_roi": ("HAND", "empty or inverted"),
    "arms_hash_mismatch": ("HAND", "hashes"),
    "arms_hand_filled_delta": ("HAND", "recomputing"),
    "arms_negative_delta": ("HAND", "recomputing"),
    "arms_claimed_verdict": ("HAND", "recomputing"),
    "arms_same_file_twice": ("HAND", "four different"),
    "arms_really_different": ("HAND", "depend on the loadout"),
}


def run_case(base: Path, name: str):
    manifest = base / ("capture_%s.json" % name if name else "capture.json")
    out = cs2_run.run([sys.executable, ROOT / "tools/cs2_reference_check.py",
                       "--manifest", manifest, "--out", base / "reports"], check=False)
    report = base / "reports" / "cs2-reference-report.json"
    items = json.loads(report.read_text("utf-8"))["items"] if report.exists() else []
    return items, out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--keep", action="store_true", help="leave the fixture tree in place")
    ap.add_argument("--dir", type=Path, help="build the fixtures here")
    args = ap.parse_args()

    package = sorted((ROOT / "output").glob("ScCsgoKnives-*.scmod"))[-1]
    base = args.dir or Path(tempfile.mkdtemp(prefix="cs2-reference-cases-"))
    base.mkdir(parents=True, exist_ok=True)
    print("fixtures in %s, package %s" % (base, package.name))
    build(base, package)

    failures = []
    for name, expect in EXPECTED.items():
        items, _ = run_case(base, name)
        label = name or "(the passing fixture)"
        if not items:
            failures.append("%s: the checker produced no report" % label)
            print("  FAIL %-24s no report" % label)
            continue
        bad = [i for i in items if i["status"] != "PASS"]
        if expect is None:
            if bad:
                failures.append("%s: %s" % (label, "; ".join(i["item"] for i in bad)))
            print("  %-4s %-24s %d items, %d failing"
                  % ("FAIL" if bad else "ok", label, len(items), len(bad)))
            for i in bad:
                print("       %s -> %s" % (i["item"], i["reason"][:150]))
            continue
        item_name, phrase = expect
        hit = [i for i in bad if i["item"].startswith(item_name) and phrase in i["reason"]]
        if hit:
            print("  ok   %-24s %s fails: %s" % (label, item_name, hit[0]["reason"][:110]))
        else:
            failures.append("%s: no %s failure mentioning %r" % (label, item_name, phrase))
            print("  FAIL %-24s expected %s to fail with %r; got %s"
                  % (label, item_name, phrase,
                     "; ".join("%s: %s" % (i["item"], i["reason"][:80]) for i in bad) or "nothing"))

    if not args.keep and not args.dir:
        shutil.rmtree(base, ignore_errors=True)
    print("\n%d of %d cases behave as specified. %s"
          % (len(EXPECTED) - len(failures), len(EXPECTED), "PASS" if not failures else "FAIL"))
    return 0 if not failures else 1


if __name__ == "__main__":
    raise SystemExit(main())
