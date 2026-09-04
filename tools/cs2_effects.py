#!/usr/bin/env python3
"""CS2's muzzle flash and tracer numbers -> AnimationData/cs2_effects.json.

Everything here is read out of CS2's own files: the muzzle positions and tracer
particle from each gun's `.vdata`, and the flash's lifetime, particle count,
sprite sequence, colour, alpha, rotation and fade from the `.vpcf` the gun's
flash system references.

Muzzle positions cross-check the animation rig: the AK's m_vecMuzzlePos0 is
[37.422, -4.938, -3.394], which is its `muzzle` bone at idle to three decimals.

The mod does not run a particle system - it draws a sprite from an atlas - so
what is extracted is the envelope a sprite can honour: how long, how big, what
colour, how many frames, how fast it fades. Anything the vpcf does that a single
sprite cannot (eight particles with per-particle curves, drag, bloom-only second
pass) is recorded in the JSON's `unmodelled` list rather than faked.

Usage:  python3 tools/cs2_effects.py [--out FILE]
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import cs2_kv3

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / "src/ScCsgoKnives/AnimationData"
ANALYSIS = Path.home() / "workspaces/CSMCReverse/local_cs2_analysis/all_weapons"
VDATA = ANALYSIS / "01_weapon_data/firearm_blocks"
PARTICLES = ANALYSIS / "06_particles/definitions"

# Gun -> (vdata stem, {mode: leaf flash system}). The systems CS2 names for a
# weapon are containers; the leaves below are their primary-flash children,
# picked by reading each container's m_Children:
#   uweapon_muzflsh_ak47      -> uweapon_muzflsh_ak47_primaryflash
#   uweapon_muzflsh_riffle    -> the same primaryflash (rifles share the AK's)
#   uweapon_muzsilenced_rif   -> uweapon_muzsilenced_subm_smoke (a suppressed
#                                muzzle has no flare, only the gas puff)
#   weapon_muzzleflash_snip   -> uweapon_muzflsh_awp_primaryflash
UNIFIED = "particles/unified_weapon_fx/"
GUNS = {
    "ak47": ("weapon_ak47", {"default": UNIFIED + "uweapon_muzflsh_ak47_primaryflash.vpcf"}),
    "m4a1s": ("weapon_m4a1_silencer",
              {"default": UNIFIED + "uweapon_muzflsh_ak47_primaryflash.vpcf",
               "silenced": UNIFIED + "uweapon_muzsilenced_subm_smoke.vpcf"}),
    "awp": ("weapon_awp", {"default": UNIFIED + "uweapon_muzflsh_awp_primaryflash.vpcf"}),
}

# Source 2 particle attribute ids used below.
LIFE_DURATION = 1
ROTATION = 4
ALPHA = 7


def read_vdata(stem: str) -> dict:
    text = (VDATA / (stem + ".vdata")).read_text("utf-8", "replace")

    def vec(name):
        m = re.search(r"%s\s*=\s*\[([^\]]*)\]" % re.escape(name), text)
        return [float(x) for x in m.group(1).split(",")] if m else None

    def scalar(name):
        m = re.search(r"%s\s*=\s*(?:\[\s*([\d.\-]+)|([\d.\-]+))" % re.escape(name), text)
        if not m:
            return None
        return float(m.group(1) or m.group(2))

    tracer = re.search(r'm_szTracerParticle\s*=\s*resource_name:"([^"]+)"', text)
    return {"muzzle0": vec("m_vecMuzzlePos0"), "muzzle1": vec("m_vecMuzzlePos1"),
            "tracer_particle": tracer.group(1) if tracer else None,
            "tracer_frequency": scalar("m_nTracerFrequency")}


def literal(node):
    if isinstance(node, dict):
        if node.get("m_nType") == "PF_TYPE_LITERAL":
            return node.get("m_flLiteralValue")
        if node.get("m_nType") in ("PF_TYPE_RANDOM_UNIFORM", "PF_TYPE_RANDOM_BIASED"):
            return [node.get("m_flRandomMin"), node.get("m_flRandomMax")]
    return None


def read_flash(path: Path) -> dict:
    """One flash system's envelope.

    Keys are PascalCase and match Cs2Effects.cs field for field. 0.16.4 emitted
    snake_case and a scalar `lifetime`, which made the C# loader throw - System.Text.Json's
    PropertyNameCaseInsensitive ignores case, not separators - so every tracer and the
    whole CS2 flash envelope were inactive at runtime while the Python check passed.
    Lifetime is always written as an array for the same reason.
    """
    if not path.exists():
        return {"Missing": str(path)}
    doc = cs2_kv3.load(path)
    out = {"Source": str(path.relative_to(ANALYSIS)), "Unmodelled": []}

    for op in doc.get("m_Emitters") or []:
        if op.get("_class") == "C_OP_InstantaneousEmitter":
            out["Particles"] = literal(op.get("m_nParticlesToEmit"))

    for op in doc.get("m_Initializers") or []:
        cls = op.get("_class")
        if cls == "C_INIT_RandomSequence":
            out["SequenceFrames"] = int(op.get("m_nSequenceMax", 0)) + 1
        elif cls == "C_INIT_RandomColor":
            # Absent bounds are Source's default white, not "no colour"; writing the
            # default keeps the C# side from inventing a fallback tint.
            out["ColorMin"] = [int(x) for x in (op.get("m_ColorMin") or [255, 255, 255])[:3]]
            out["ColorMax"] = [int(x) for x in (op.get("m_ColorMax") or [255, 255, 255])[:3]]
        elif cls == "C_INIT_InitFloat":
            field = op.get("m_nOutputField")
            value = literal(op.get("m_InputValue"))
            if value is None:
                continue
            if field == LIFE_DURATION:
                # Always an array, even for a literal: the C# side is float[].
                out["Lifetime"] = value if isinstance(value, list) else [value]
            elif field == ALPHA:
                out["Alpha"] = value
            elif field == ROTATION:
                out["RotationDegrees"] = value

    for op in doc.get("m_Operators") or []:
        cls = op.get("_class")
        if cls == "C_OP_FadeOut":
            out["FadeOut"] = op.get("m_flFadeOutTimeMax")
        elif cls == "C_OP_BasicMovement":
            out["Drag"] = op.get("m_fDrag")
        elif cls not in ("C_OP_Decay", "C_OP_PositionLock", "C_OP_RampScalarLinearSimple"):
            out["Unmodelled"].append(cls)

    textures = []
    for r in doc.get("m_Renderers") or []:
        for t in r.get("m_vecTexturesInput") or []:
            if t.get("m_hTexture"):
                textures.append(t["m_hTexture"])
        if r.get("m_bOnlyRenderInEffectsBloomPass"):
            out["Unmodelled"].append("bloom-only second sprite pass")
        scale = literal(r.get("m_flRadiusScale"))
        if scale is not None:
            out["RadiusScale"] = scale
    out["Textures"] = sorted(set(textures))
    # Per-particle radius comes from a curve, not a literal; record its range.
    for op in doc.get("m_Initializers") or []:
        curve = (op.get("m_InputValue") or {}).get("m_Curve") if isinstance(op.get("m_InputValue"), dict) else None
        if curve and curve.get("m_vDomainMaxs") and curve["m_vDomainMaxs"][1] > 1.5:
            out["RadiusCurveRange"] = [curve["m_vDomainMins"][1], curve["m_vDomainMaxs"][1]]
    out["Unmodelled"] = sorted(set(out["Unmodelled"]))
    return out


# Source 2 particle attribute ids used by the tracer systems.
RADIUS = 3
TRAIL_LENGTH = 10

# The bake in tools/cs2_tracer_texture.py, keyed by the vtex the vpcf names. The
# shipped texture is that file's own pixels, transposed so U runs along the trail.
TRACER_TEXTURES = {
    "materials/effects/spark.vtex": "cs2_tracer_add",
    "materials/particle/sparks/sparks.vtex": "cs2_tracer_blend",
}


def read_tracer(path: str) -> dict:
    """The whole tracer system, not just its speed.

    CS2 fires hitscan and draws the tracer as a trail travelling the shot line:
    the assault-rifle tracer runs at 20500 units/s with a 1200-unit trail, the
    rifle (AWP) one at 30000 units/s with 900. Units are inches, so those are
    521 m/s and 762 m/s - real muzzle velocities, not effect speeds.

    Everything the mod's ribbon needs is read here rather than chosen:

      C_INIT_MoveBetweenPoints    speed, and that the particle runs CP0 -> CP1
      C_INIT_DistanceToCPInit     lifetime scales with the shot distance, so
                                  life = distance / speed
      C_INIT_InitFloat field 3    the particle radius, in inches
      C_INIT_InitFloat field 7    the per-shot alpha range
      C_INIT_InitFloat field 10   the trail length, in seconds of travel
      C_INIT_RandomColor          the tint; absent bounds mean white, and then the
                                  texture is the only thing that colours the trail
      C_OP_FadeAndKillForTracers  alpha against normalised life
      C_OP_DistanceToTransform    trail length scaled by distance from the viewer
      C_OP_RenderTrails           one entry per draw pass: texture, blend mode,
                                  radius scale, length fade-in, and the screen-space
                                  size clamp that stops a near trail becoming a plank

    Two RenderTrails per system means two passes, so they are collected into a list
    instead of the last one overwriting the first, which is what 0.16.5 did.
    """
    if not path:
        return {}
    full = PARTICLES / path
    if not full.exists():
        return {"Missing": path}
    doc = cs2_kv3.load(full)
    out = {"Source": path, "Passes": [], "Unmodelled": []}

    def pair(v, default=None):
        if v is None:
            return default
        return [float(v), float(v)] if not isinstance(v, list) else [float(v[0]), float(v[1])]

    for op in doc.get("m_Initializers") or []:
        cls = op.get("_class")
        if cls == "C_INIT_MoveBetweenPoints":
            for key, name in (("m_flSpeedMin", "SpeedMin"), ("m_flSpeedMax", "SpeedMax")):
                v = literal(op.get(key))
                if v is not None:
                    out[name] = v
        elif cls == "C_INIT_RandomColor":
            # No bounds in the file means Source's default white, and CS2 relies on
            # that for the AK and M4A1-S: their colour is in the spark texture. Writing
            # the default explicitly keeps the C# side from inventing a fallback.
            out["ColorMin"] = [int(x) for x in (op.get("m_ColorMin") or [255, 255, 255])[:3]]
            out["ColorMax"] = [int(x) for x in (op.get("m_ColorMax") or [255, 255, 255])[:3]]
            out["ColorFromTexture"] = out["ColorMin"] == out["ColorMax"] == [255, 255, 255]
        elif cls == "C_INIT_InitFloat":
            field = op.get("m_nOutputField", RADIUS)   # Source's default output is RADIUS
            v = literal(op.get("m_InputValue"))
            if v is None:
                continue
            if field == ALPHA:
                out["Alpha"] = pair(v)
            elif field == RADIUS:
                out["Radius"] = pair(v)
            elif field == TRAIL_LENGTH:
                out["TrailSeconds"] = pair(v)
        elif cls == "C_INIT_DistanceToCPInit" and op.get("m_nFieldOutput") == LIFE_DURATION:
            # The lifetime is scaled by |CP1 - CP0|, the shot distance, so the
            # particle reaches the hit point exactly as it dies.
            out["LifeFromShotDistance"] = True

    for op in (doc.get("m_Operators") or []) + (doc.get("m_Renderers") or []):
        cls = op.get("_class")
        if cls == "C_OP_FadeAndKillForTracers":
            out["FadeInStart"] = op.get("m_flStartFadeInTime", 0.0)
            out["FadeInEnd"] = op.get("m_flEndFadeInTime", 0.0)
            out["FadeOutStart"] = op.get("m_flStartFadeOutTime", 1.0)
            out["FadeOutEnd"] = op.get("m_flEndFadeOutTime", 1.0)
            out["StartAlpha"] = op.get("m_flStartAlpha", 1.0)
            out["EndAlpha"] = op.get("m_flEndAlpha", 0.0)
        elif cls == "C_OP_DistanceToTransform" and op.get("m_nFieldOutput") == TRAIL_LENGTH:
            out["LengthScaleInput"] = [op.get("m_flInputMin", 0.0), op.get("m_flInputMax", 0.0)]
            out["LengthScaleOutput"] = [op.get("m_flOutputMin", 1.0), op.get("m_flOutputMax", 1.0)]
        elif cls == "C_OP_RenderTrails":
            texture = None
            for t in op.get("m_vecTexturesInput") or []:
                if t.get("m_hTexture"):
                    texture = t["m_hTexture"]
            entry = {
                "SourceTexture": texture,
                "Texture": TRACER_TEXTURES.get(texture),
                "Blend": op.get("m_nOutputBlendMode"),
                "RadiusScale": literal(op.get("m_flRadiusScale")),
                "LengthFadeIn": op.get("m_flLengthFadeInTime"),
                "MinSize": float(op.get("m_flMinSize", 0.0)),
                "MaxSize": float(op.get("m_flMaxSize", 0.0)),
                "StartFadeSize": float(op.get("m_flStartFadeSize", 0.0) or 0.0),
                "EndFadeSize": float(op.get("m_flEndFadeSize", 0.0) or 0.0),
            }
            if entry["Texture"] is None:
                out["Unmodelled"].append("no baked texture for %s" % texture)
            out["MaxLength"] = op.get("m_flMaxLength")
            out["Passes"].append(entry)
            # U tiles five times along the trail. The frames the bake ships already
            # carry one head-to-tail ramp along that axis, so tiling would repeat the
            # ramp five times down the trail; the ribbon draws the ramp once instead.
            u = literal(op.get("m_flFinalTextureScaleU"))
            if u not in (None, 1.0):
                out["Unmodelled"].append("m_flFinalTextureScaleU=%g (would repeat the baked head/tail ramp)" % u)
        elif cls not in ("C_OP_BasicMovement",):
            out["Unmodelled"].append(cls)

    speed = out.get("SpeedMin") or out.get("SpeedMax")
    if speed:
        out["Speed"] = speed
    # The two passes agree on everything the ribbon shares; a difference would mean
    # the model below is wrong, so it is asserted rather than assumed.
    if len({p["MinSize"] for p in out["Passes"]}) > 1 or len({p["MaxSize"] for p in out["Passes"]}) > 1:
        raise SystemExit("%s: the two RenderTrails passes disagree on the size clamp" % path)
    out["Unmodelled"] = sorted(set(out["Unmodelled"]))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", type=Path, default=DATA / "cs2_effects.json")
    args = ap.parse_args()

    guns = {}
    for gun, (stem, flashes) in GUNS.items():
        vdata = read_vdata(stem)
        guns[gun] = {
            "MuzzlePos0": vdata["muzzle0"], "MuzzlePos1": vdata["muzzle1"],
            "TracerParticle": vdata["tracer_particle"],
            "TracerFrequency": vdata["tracer_frequency"],
            "Flash": {mode: read_flash(PARTICLES / path) for mode, path in flashes.items()},
            "Tracer": read_tracer(vdata["tracer_particle"]),
        }
        t = guns[gun]["Tracer"]
        print("%-6s muzzle0 %s%s tracer %s every %s shots"
              % (gun, vdata["muzzle0"],
                 "" if vdata["muzzle1"] is None else " / muzzle1 %s" % vdata["muzzle1"],
                 Path(vdata["tracer_particle"] or "-").stem, vdata["tracer_frequency"]))
        print("       tracer: %s in/s, trail %s in (%s s of travel), radius %s in, "
              "alpha %s, colour %s..%s%s"
              % (t.get("Speed"), t.get("MaxLength"), t.get("TrailSeconds"), t.get("Radius"),
                 t.get("Alpha"), t.get("ColorMin"), t.get("ColorMax"),
                 " (from the texture)" if t.get("ColorFromTexture") else ""))
        print("       tracer life = shot distance / speed; alpha ramps %s->%s and %s->%s of life; "
              "length x%s..%s over %s in from the viewer"
              % (t.get("FadeInStart"), t.get("FadeInEnd"), t.get("FadeOutStart"), t.get("FadeOutEnd"),
                 (t.get("LengthScaleOutput") or [None, None])[0],
                 (t.get("LengthScaleOutput") or [None, None])[1], t.get("LengthScaleInput")))
        for ps in t.get("Passes") or []:
            print("       pass %-16s %-38s radius x%s, fade-in %s s, screen clamp %g..%g%s"
                  % (ps["Texture"], ps["SourceTexture"], ps["RadiusScale"], ps["LengthFadeIn"],
                     ps["MinSize"], ps["MaxSize"],
                     "" if not ps["EndFadeSize"] else ", fades out %g..%g" % (ps["StartFadeSize"], ps["EndFadeSize"])))
        if t.get("Unmodelled"):
            print("       not modelled: %s" % ", ".join(t["Unmodelled"]))
        for mode, f in guns[gun]["Flash"].items():
            if "Missing" in f:
                print("       flash[%s]: %s NOT IN THE EXPORT" % (mode, f["Missing"]))
                continue
            print("       flash[%-9s] lifetime %s s, %s particles, %s frames, colour %s..%s, alpha %s, fade %s s"
                  % (mode, f.get("Lifetime"), f.get("Particles"), f.get("SequenceFrames"),
                     f.get("ColorMin"), f.get("ColorMax"), f.get("Alpha"), f.get("FadeOut")))
            if f["Unmodelled"]:
                print("                    not modelled: %s" % ", ".join(f["Unmodelled"]))

    doc = {"Format": "ScCsgoKnives.Cs2Effects/3",
           "Notes": ("Muzzle positions and tracer references are from each gun's .vdata; "
                     "flash and tracer numbers from the .vpcf systems CS2 plays. The mod "
                     "draws a sprite, not a particle system, so 'unmodelled' lists what a "
                     "single sprite cannot reproduce rather than approximating it."),
           "Guns": guns}
    args.out.write_text(json.dumps(doc, indent=1), "utf-8")
    print("wrote %s (%.1f KB)" % (args.out.name, args.out.stat().st_size / 1024))


if __name__ == "__main__":
    main()
