#!/usr/bin/env python3
"""CS2's tracer trail textures -> Assets/Textures/ScCsgoKnives/cs2_tracer_*.png.

weapon_tracers_assrifle.vpcf and weapon_tracers_rifle.vpcf each draw the trail
twice, with different textures and blend modes:

    m_hTexture = "materials/effects/spark.vtex"          ADD
    m_hTexture = "materials/particle/sparks/sparks.vtex" BLEND_ADD

and pick sheet frame 4 of the sparks sheet (C_INIT_RandomSequence, min = max = 4).
Both are in the local CS2 export as PNG already, so nothing here is drawn by hand.

Orientation. Measured from the pixels rather than assumed: in both images the long
axis (64 rows) carries a plateau with a soft ramp at each end, and the short axis
(32 / 60 columns) carries a narrow soft-edged core. That is a streak whose length
runs down the rows, so the rows are the along-trail axis and the columns are the
across-width axis. The bake transposes them, so in the shipped texture U runs along
the trail and V across its width, which is what the ribbon's UVs want.

    spark.png        luma per row  0.00 .. 0.183 plateau .. 0.00   (ramped ends)
                     luma per col  0.00 .. 0.588 core .. 0.00      (soft edges)
    sparks_seq4.png  alpha per row 0.008 .. 0.206 plateau .. 0.016
                     alpha per col 0.001 .. 0.651 core .. 0.000

spark.png is fully opaque with the shape in its RGB: it is the additive pass, where
RGB is the emission and alpha is ignored. sparks.vtex frame 4 carries its shape in
alpha. The bake keeps each as it is and only transposes and resamples, so the AK and
M4A1-S trail colour comes from CS2's own spark pixels - those two guns have a
C_INIT_RandomColor with no m_ColorMin/m_ColorMax, i.e. a white tint, so the texture
is the only thing that colours them.

Usage:  python3 tools/cs2_tracer_texture.py [--check]
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
TEXTURES = Path.home() / "workspaces/CSMCReverse/local_cs2_analysis/all_weapons/06_particles/textures/materials"
OUT = ROOT / "src/ScCsgoKnives/Assets/Textures/ScCsgoKnives"

# (output stem, source png, the vtex the vpcf names, blend mode, output size U x V)
PASSES = [
    ("cs2_tracer_add", "effects/spark.png", "materials/effects/spark.vtex",
     "PARTICLE_OUTPUT_BLEND_MODE_ADD", (128, 64)),
    ("cs2_tracer_blend", "particle/sparks/sparks_seq4.png", "materials/particle/sparks/sparks.vtex",
     "PARTICLE_OUTPUT_BLEND_MODE_BLEND_ADD", (128, 64)),
    # weapon_tracers_smg.vpcf scrolls this one down a rope (C_OP_RenderRopes) rather
    # than moving a particle; one repeat of it is the streak the ribbon draws. Its
    # shape is in RGB, opaque alpha, the bright head at the high row index - so after
    # the transpose the head lands at U = 1, which is the ribbon's head end.
    ("cs2_tracer_smg", "particle/effects/bullet_tracer_seq.png",
     "materials/particle/effects/bullet_tracer_seq.vtex",
     "PARTICLE_OUTPUT_BLEND_MODE_ADD", (128, 64)),
    # weapon_tracers_assrifle_aug.vpcf (AUG, SG 553): the same kind of rope with a
    # streak whose head is a plateau rather than a bright tip; white, so the
    # ColorInterpolate tint is what colours it in CS2 (unmodelled here).
    ("cs2_tracer_tintable", "particle/effects/bullet_tracer_tintable.png",
     "materials/particle/effects/bullet_tracer_tintable.vtex",
     "PARTICLE_OUTPUT_BLEND_MODE_ADD", (128, 64)),
]


def profile(a: np.ndarray) -> dict:
    """Where the shape lives, so the transpose is justified by numbers, not by eye."""
    alpha, luma = a[..., 3].astype(float), a[..., :3].astype(float).mean(-1)
    shape = alpha if np.ptp(alpha) > 1.0 else luma
    rows, cols = shape.mean(1), shape.mean(0)

    def ends(v):
        return float(v[0] / max(v.max(), 1e-6)), float(v[-1] / max(v.max(), 1e-6))

    return {"rows": len(rows), "cols": len(cols),
            "row_ends": ends(rows), "col_ends": ends(cols),
            "row_peak": float(rows.max() / 255), "col_peak": float(cols.max() / 255),
            "carries_shape": "alpha" if np.ptp(alpha) > 1.0 else "rgb"}


def bake(check: bool) -> int:
    report = []
    for stem, rel, vtex, blend, (uw, vh) in PASSES:
        src = TEXTURES / rel
        if not src.exists():
            print("missing CS2 export: %s" % src, file=sys.stderr)
            return 2
        img = Image.open(src).convert("RGBA")
        a = np.asarray(img)
        p = profile(a)
        # The long axis must be the ramped one, or the transpose below is wrong.
        if p["rows"] < p["cols"]:
            print("%s: rows are not the long axis; check the export" % rel, file=sys.stderr)
            return 2
        # rows -> U (along the trail), cols -> V (across the width)
        turned = img.transpose(Image.Transpose.TRANSPOSE)
        out = turned.resize((uw, vh), Image.Resampling.LANCZOS)
        dest = OUT / (stem + ".png")
        data = out.tobytes()
        digest = hashlib.sha256(data).hexdigest()
        if not check:
            OUT.mkdir(parents=True, exist_ok=True)
            out.save(dest)
        report.append({"texture": stem, "source_png": str(src), "vtex": vtex, "blend": blend,
                       "source_size": list(img.size), "output_size": [uw, vh],
                       "source_sha256": hashlib.sha256(src.read_bytes()).hexdigest(),
                       "pixels_sha256": digest, "profile": p})
        print("%-18s <- %-34s %s -> %dx%d  %s"
              % (stem, rel, "x".join(str(x) for x in img.size), uw, vh, p["carries_shape"]))
    (ROOT / "docs/cs2-tracer-textures.json").write_text(json.dumps(report, indent=1), "utf-8")
    return 0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="report without writing the PNGs")
    return bake(ap.parse_args().check)


if __name__ == "__main__":
    raise SystemExit(main())
