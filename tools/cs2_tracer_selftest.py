#!/usr/bin/env python3
"""Acceptance for the CS2 tracer: geometry, envelope and textures.

The Windows smoke test on 0.16.5 found the tracer wrong in four ways at once - it
left the eye instead of the muzzle, it ignored the CS2 trail material, it was a
fixed-width rectangle, and its time envelope was a guess. Each of those is a
separate case here, and each is checked against the .vpcf or against the shipped
texture's own pixels rather than against a number typed in by hand.

  A  source      every number the ribbon uses is the one in the .vpcf
  B  origin      the tracer starts on the drawn muzzle, not at the eye
  C  width       the screen-space clamp holds at every distance, both directions
  D  envelope    alpha and length against the fraction of the shot line flown
  E  textures    the shipped PNGs are CS2's own, soft-edged and ramped at both ends

Usage:  python3 tools/cs2_tracer_selftest.py [--json out.json]
        (on Windows: python tools\\cs2_tracer_selftest.py)
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))

import cs2_kv3
import cs2_run

ROOT = Path(__file__).resolve().parent.parent
ARMPREVIEW = ROOT / "tools/ArmPreview/bin/Release/net10.0/ArmPreview.dll"
VPCF = (Path.home() / "workspaces/CSMCReverse/local_cs2_analysis/all_weapons/06_particles"
        / "definitions/particles/weapons/cs_weapon_fx")
TEXTURES = ROOT / "src/ScCsgoKnives/Assets/Textures/ScCsgoKnives"
GUNS = ["ak47", "m4a1s", "awp"]


def run(gun: str, fov: float = 60.0, width: int = 1400, height: int = 1050) -> dict:
    document, _ = cs2_run.run_json(
        ["dotnet", ARMPREVIEW, "tracer", gun, fov, width, height], dotnet=True)
    return document


def vpcf_facts(name: str) -> dict:
    """The numbers the ribbon claims, read straight out of the particle system."""
    doc = cs2_kv3.load(VPCF / name)

    def lit(node):
        return node.get("m_flLiteralValue") if isinstance(node, dict) else None

    facts = {"passes": []}
    for op in doc.get("m_Initializers") or []:
        if op["_class"] == "C_INIT_MoveBetweenPoints":
            facts["speed"] = lit(op.get("m_flSpeedMin")) or lit(op.get("m_flSpeedMax"))
    for op in doc.get("m_Operators") or []:
        if op["_class"] == "C_OP_FadeAndKillForTracers":
            facts["fade"] = [op.get("m_flStartFadeInTime", 0.0), op.get("m_flEndFadeInTime", 0.0),
                             op.get("m_flStartFadeOutTime", 1.0), op.get("m_flEndFadeOutTime", 1.0)]
    for op in doc.get("m_Renderers") or []:
        if op["_class"] == "C_OP_RenderTrails":
            texture = None
            for t in op.get("m_vecTexturesInput") or []:
                texture = t.get("m_hTexture") or texture
            facts["max_length"] = op.get("m_flMaxLength")
            facts["passes"].append({
                "texture": texture, "radius_scale": lit(op.get("m_flRadiusScale")),
                "length_fade_in": op.get("m_flLengthFadeInTime"),
                "min_size": float(op.get("m_flMinSize", 0.0)),
                "max_size": float(op.get("m_flMaxSize", 0.0)),
            })
    return facts


class Case:
    def __init__(self, name):
        self.name, self.rows, self.failures = name, [], []

    def check(self, ok, detail):
        self.rows.append(("ok" if ok else "FAIL", detail))
        if not ok:
            self.failures.append(detail)

    def report(self):
        print("  %-4s %-10s" % ("FAIL" if self.failures else "PASS", self.name))
        for state, detail in self.rows:
            print("        %-4s %s" % (state, detail))
        return not self.failures


def case_source(dumps) -> Case:
    """A: the C# numbers are the .vpcf numbers, to the digit."""
    c = Case("A source")
    for gun, d in dumps.items():
        facts = vpcf_facts(Path(d["Source"]).name)
        c.check(abs(d["Speed"] - facts["speed"]) < 1e-6,
                "%s speed %s in/s == %s in the vpcf" % (gun, d["Speed"], facts["speed"]))
        c.check(abs(d["MaxLength"] - facts["max_length"]) < 1e-6,
                "%s max length %s in == %s" % (gun, d["MaxLength"], facts["max_length"]))
        c.check(len(d["passes"]) == len(facts["passes"]) == 2,
                "%s draws %d passes, the vpcf has %d" % (gun, len(d["passes"]), len(facts["passes"])))
        for got, want in zip(d["passes"], facts["passes"]):
            c.check(got["SourceTexture"] == want["texture"],
                    "%s pass texture %s == %s" % (gun, got["SourceTexture"], want["texture"]))
            for key, wkey in (("RadiusScale", "radius_scale"), ("LengthFadeIn", "length_fade_in"),
                              ("MinSize", "min_size"), ("MaxSize", "max_size")):
                c.check(abs(float(got[key]) - float(want[wkey])) < 1e-9,
                        "%s %s %s == %s" % (gun, key, got[key], want[wkey]))
        env = d["envelope"]
        want = facts["fade"]
        peak = max(e["alpha"] for e in env)
        dark = 0.0
        for e in env:                      # the leading dark run only; it goes out at the end too
            if e["alpha"] >= peak * 0.01:
                break
            dark = e["u"]
        c.check(want[0] - 0.06 <= dark <= want[1] + 1e-6,
                "%s dark until u=%.2f, the vpcf fades in over %.2f..%.2f" % (gun, dark, want[0], want[1]))
    return c


def case_origin(dumps) -> Case:
    """B: the tracer starts where the barrel is drawn."""
    c = Case("B origin")
    for gun, d in dumps.items():
        for m in d["muzzles"]:
            tag = "%s%s" % (gun, " silenced" if m["silenced"] else "")
            c.check(m["errorPixels"] <= 3.0,
                    "%s starts %.3f px from the drawn muzzle (limit 3)" % (tag, m["errorPixels"]))
            # The correction has to be worth making: if it were not, or were applied
            # the wrong way round, the eye would be no further away than the fix.
            c.check(m["eyeOriginErrorPixels"] > 20.0 * max(m["errorPixels"], 1.0),
                    "%s the eye ray's origin is %.1f px off, the fixed start %.3f px"
                    % (tag, m["eyeOriginErrorPixels"], m["errorPixels"]))
            c.check(m["viewSpace"][2] < -0.1,
                    "%s muzzle is %.3f m in front of the eye" % (tag, -m["viewSpace"][2]))
    return c


def case_width(dumps) -> Case:
    """C: no plank near, no sub-pixel thread far, and the clamp is what does it."""
    c = Case("C width")
    for gun, d in dumps.items():
        for p in d["passes"]:
            near = [w for w in p["widths"] if w["depth"] <= 1.0]
            far = [w for w in p["widths"] if w["depth"] >= 40.0]
            worst_near = max(w["halfPixels"] for w in near)
            worst_far = min(w["halfPixels"] for w in far)
            c.check(worst_near <= 16.0,
                    "%s/%s <= %.2f px half-width inside 1 m (0.16.5 drew 21.8 px there)"
                    % (gun, p["Texture"], worst_near))
            c.check(worst_far >= 0.5,
                    "%s/%s >= %.2f px half-width past 40 m (0.16.5 drew 0.14 px at 80 m)"
                    % (gun, p["Texture"], worst_far))
            # The clamp must actually bind, or the numbers above are luck.
            binds_near = any(w["unclampedPixels"] > w["halfPixels"] + 1e-4 for w in near)
            binds_far = any(w["unclampedPixels"] < w["halfPixels"] - 1e-4 for w in far)
            c.check(binds_near and binds_far,
                    "%s/%s the clamp binds at both ends (near %s, far %s)"
                    % (gun, p["Texture"], binds_near, binds_far))
            # Monotone in depth: a trail may not get wider as it goes away.
            px = [w["halfPixels"] for w in sorted(p["widths"], key=lambda w: w["depth"])]
            c.check(all(a >= b - 1e-4 for a, b in zip(px, px[1:])),
                    "%s/%s half-width falls with distance: %s px"
                    % (gun, p["Texture"], " ".join("%.2f" % v for v in px)))
            if p["EndFadeSize"] > p["StartFadeSize"]:
                gone = [w for w in p["widths"] if w["sizeFade"] <= 0.0]
                c.check(bool(gone),
                        "%s/%s fades out entirely by %.1f m, as m_flStartFadeSize asks"
                        % (gun, p["Texture"], min(w["depth"] for w in gone) if gone else -1))
    return c


def case_envelope(dumps) -> Case:
    """D: the trail grows, holds, and goes out; it is not one flat bar."""
    c = Case("D envelope")
    for gun, d in dumps.items():
        env = d["envelope"]
        alphas = [e["alpha"] for e in env]
        c.check(alphas[0] <= 1e-6, "%s invisible at the muzzle (alpha %.4f at u=0)" % (gun, alphas[0]))
        c.check(max(alphas) > 0.5, "%s reaches alpha %.3f mid-flight" % (gun, max(alphas)))
        c.check(alphas[-1] < max(alphas), "%s fades before impact (%.3f vs %.3f)" % (gun, alphas[-1], max(alphas)))
        peak = max(alphas)
        rising = [e for e in env if 0.0 < e["alpha"] < peak - 1e-6 and e["u"] < 0.5]
        falling = [e for e in env if 0.0 < e["alpha"] < peak - 1e-6 and e["u"] > 0.5]
        c.check(len(rising) >= 3 and len(falling) >= 2,
                "%s ramps rather than switching: %d steps fading in, %d fading out"
                % (gun, len(rising), len(falling)))
        for p in d["passes"]:
            lengths = [l["trailMetres"] for l in sorted(p["lengths"], key=lambda l: l["age"])]
            c.check(all(a <= b + 1e-6 for a, b in zip(lengths, lengths[1:])),
                    "%s/%s trail grows with age: %s m"
                    % (gun, p["Texture"], " ".join("%.1f" % v for v in lengths)))
            c.check(lengths[0] < lengths[-1] * 0.5,
                    "%s/%s starts short (%.2f m) and reaches %.1f m"
                    % (gun, p["Texture"], lengths[0], lengths[-1]))
        c.check(d["lengthScale"]["at100m"] >= d["lengthScale"]["at1m"],
                "%s C_OP_DistanceToTransform lengthens with range: x%.3f at 1 m, x%.3f at 100 m"
                % (gun, d["lengthScale"]["at1m"], d["lengthScale"]["at100m"]))
    return c


def case_textures(dumps) -> Case:
    """E: the shipped strips are CS2's, soft across and ramped along."""
    c = Case("E textures")
    manifest = json.loads((ROOT / "docs/cs2-tracer-textures.json").read_text("utf-8"))
    baked = {m["texture"]: m for m in manifest}
    wanted = {p["Texture"] for d in dumps.values() for p in d["passes"]}
    for stem in sorted(wanted):
        path = TEXTURES / (stem + ".png")
        c.check(path.exists(), "%s.png ships" % stem)
        if not path.exists():
            continue
        c.check(stem in baked and Path(baked[stem]["source_png"]).exists(),
                "%s came from %s" % (stem, baked.get(stem, {}).get("vtex")))
        a = np.asarray(Image.open(path).convert("RGBA")).astype(float)
        alpha, luma = a[..., 3], a[..., :3].mean(-1)
        shape = alpha if np.ptp(alpha) > 1.0 else luma
        across = shape.mean(1)                      # V, across the width
        along = shape.mean(0)                       # U, along the trail
        peak = shape.max()
        c.check(across[0] < peak * 0.12 and across[-1] < peak * 0.12,
                "%s soft across the width: edges at %.1f%% / %.1f%% of peak"
                % (stem, 100 * across[0] / peak, 100 * across[-1] / peak))
        c.check(along[0] < along.max() * 0.35 and along[-1] < along.max() * 0.35,
                "%s ramped at head and tail: ends at %.1f%% / %.1f%% of its own peak"
                % (stem, 100 * along[0] / along.max(), 100 * along[-1] / along.max()))
        # A hard-edged white rectangle is exactly what 0.16.5 drew; this is the
        # measurement that says the shipped strip is not one.
        interior = across[1:-1]
        c.check(float(np.count_nonzero(interior > peak * 0.9)) / len(interior) < 0.5,
                "%s is not a flat bar: %.0f%% of the cross-section is within 10%% of peak"
                % (stem, 100.0 * np.count_nonzero(interior > peak * 0.9) / len(interior)))
    for gun, d in dumps.items():
        if d["ColorFromTexture"]:
            c.check(d["ColorMin"] == d["ColorMax"] == [255, 255, 255],
                    "%s takes its colour from the spark texture (vpcf tint is white)" % gun)
        else:
            c.check(d["ColorMin"] != d["ColorMax"],
                    "%s tints the texture %s..%s" % (gun, d["ColorMin"], d["ColorMax"]))
    return c


def report_flights(dumps):
    """Report only: how many 60 fps frames a tracer is on screen, and where it is.

    This is the number a real 10-second wall recording is held against, and it is not
    gated because there is nothing yet to gate it with. CS2 kills the particle at the
    impact point, so short shots really are over in a frame or two - that is the .vpcf,
    not a shortcut, and it is worth seeing before anyone calls it "vanishes too fast".
    """
    print("  ----  flight timing (report only, 60 fps)")
    for gun, d in dumps.items():
        for f in d["flights"]:
            heads = " ".join("%.0f" % fr["head"] for fr in f["frames"][:8])
            print("        %-6s %3.0f m: life %5.1f ms, %2d frame(s), head at %s m"
                  % (gun, f["metres"], 1000 * f["lifeSeconds"], f["framesAt60"], heads or "-"))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--json", type=Path, help="write the raw dumps here")
    args = ap.parse_args()

    if not ARMPREVIEW.exists():
        raise SystemExit("build tools/ArmPreview first: dotnet build tools/ArmPreview -c Release")
    dumps = {gun: run(gun) for gun in GUNS}
    if args.json:
        args.json.write_text(json.dumps(dumps, indent=1), "utf-8")

    cases = [case_source(dumps), case_origin(dumps), case_width(dumps),
             case_envelope(dumps), case_textures(dumps)]
    ok = all(c.report() for c in cases)
    report_flights(dumps)
    print("%s: %s" % ("PASS" if ok else "FAIL",
                      " ".join(c.name.split()[0] + ("" if not c.failures else "!") for c in cases)))
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
