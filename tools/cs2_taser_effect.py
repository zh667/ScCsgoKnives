#!/usr/bin/env python3
"""The Zeus x27's shot effect, from CS2's own particle definitions ->
AnimationData/cs2_taser_effect.json, plus the sprite and beam textures it names.

CS2 plays two systems on a Zeus shot. weapon_taser.vdata names
weapon_tracers_taser.vpcf as the tracer (m_nTracerFrequency 1); the model has no
muzzle-flash event, and weapon_muzzle_flash_taser.vpcf exists on its own.

    weapon_tracers_taser        CP0 muzzle, CP1 trace end
      weapon_tracers_taser_wire1a/1b   15 particles laid along CP0->CP1, life 1.8 s,
                                       colour 52,52,52 - the wires; NO renderer
        weapon_tracers_taser_wire2     from the parent's particles 0.05 s later, life
                                       0.4 s, colour 149,218..232,255, radius by a
                                       curve over the particle index, two
                                       C_OP_RenderRopes passes (beam_crack_06_bw_center,
                                       beam_flame), overbright 4 - the arc
      weapon_taser_glow_impact         three glow_05 sprites at CP1
    weapon_muzzle_flash_taser
      weapon_taser_glow                blue glow_04 sprites at the muzzle
      weapon_taser_sparks              blue spark trails from the muzzle
      weapon_taser_flash               flare_004b sprites, MOD2X, overbright 10

Every number in the JSON is read from those files here; what the renderer has to
assume on top (the scroll rate is noise-driven, MOD2X is not available) is marked
in Cs2TaserEffect.cs and the response document.

    python3 tools/cs2_taser_effect.py [--check]
"""
import argparse, json, os, re, sys
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
EXPORT = os.path.expanduser("~/workspaces/CSMCReverse/local_cs2_analysis/all_weapons")
P = os.path.join(EXPORT, "06_particles/definitions/particles/weapons/cs_weapon_fx")
T = os.path.join(EXPORT, "06_particles/textures/materials")
DATA = os.path.join(ROOT, "src/ScCsgoKnives/AnimationData")
DEST = os.path.join(ROOT, "src/ScCsgoKnives/Assets/Textures/ScCsgoKnives")

# Source 2 particle attribute indices (m_nOutputField of C_INIT_InitFloat).
FIELD_LIFETIME, FIELD_RADIUS, FIELD_ROTATION = 1, 3, 4


def read(name):
    with open(os.path.join(P, name + ".vpcf"), encoding="utf-8", errors="replace") as f:
        return f.read()


def blocks(text):
    """Every brace block as (start, end, class) for those that declare _class."""
    stack, out = [], []
    for i, ch in enumerate(text):
        if ch == "{":
            stack.append(i)
        elif ch == "}" and stack:
            s = stack.pop()
            body = text[s:i + 1]
            m = re.match(r"\{\s*_class\s*=\s*\"([^\"]+)\"", body)
            if m:
                out.append((s, i + 1, m.group(1), body))
    return out


def ops(text, cls):
    return [b for b in blocks(text) if b[2] == cls]


def literal(body, key):
    """A PF_TYPE_LITERAL parameter, or a bare number."""
    m = re.search(r"%s\s*=\s*\{[^{}]*?m_flLiteralValue\s*=\s*([\d.\-]+)" % re.escape(key), body, re.S)
    if m:
        return float(m.group(1))
    m = re.search(r"%s\s*=\s*([\d.\-]+)" % re.escape(key), body)
    return float(m.group(1)) if m else None


def vec(body, key):
    m = re.search(r"%s\s*=\s*\[\s*([^\]]*)\]" % re.escape(key), body)
    if not m:
        return None
    return [float(x) for x in re.findall(r"[\d.\-]+", m.group(1))]


def color(body, key):
    v = vec(body, key)
    return [int(x) for x in v[:3]] if v else None


def init_floats(text):
    """C_INIT_InitFloat blocks -> {field: value or [min, max] or {'curveMax': ...}}"""
    out = {}
    for _, _, _, body in ops(text, "C_INIT_InitFloat"):
        m = re.search(r"m_nOutputField\s*=\s*(\d+)", body)
        field = int(m.group(1)) if m else FIELD_RADIUS
        if "PF_TYPE_RANDOM_UNIFORM" in body:
            lo = float(re.search(r"m_flRandomMin\s*=\s*([\d.\-]+)", body).group(1))
            hi = float(re.search(r"m_flRandomMax\s*=\s*([\d.\-]+)", body).group(1))
            out[field] = [lo, hi]
        elif "PF_MAP_TYPE_CURVE" in body:
            dom = vec(body, "m_vDomainMaxs")
            out[field] = {"curveDomainMax": dom}
        else:
            out[field] = float(re.search(r"m_flLiteralValue\s*=\s*([\d.\-]+)", body).group(1))
    return out


def fade_out(text):
    b = ops(text, "C_OP_FadeOut")
    if not b:
        return None
    body = b[0][3]
    lo = literal(body, "m_flFadeOutTimeMin")
    hi = literal(body, "m_flFadeOutTimeMax")
    prop = "m_bProportional = false" not in body
    return {"Min": lo if lo is not None else 0.25, "Max": hi if hi is not None else 0.25, "Proportional": prop}


def fade_in(text):
    b = ops(text, "C_OP_FadeIn")
    if not b:
        return None
    body = b[0][3]
    return {"Min": literal(body, "m_flFadeInTimeMin"), "Max": literal(body, "m_flFadeInTimeMax")}


def interpolate_radius(text):
    b = ops(text, "C_OP_InterpolateRadius")
    if not b:
        return None
    body = b[0][3]
    return {"StartScale": literal(body, "m_flStartScale") if literal(body, "m_flStartScale") is not None else 1.0,
            "EndScale": literal(body, "m_flEndScale"),
            "Bias": literal(body, "m_flBias") if literal(body, "m_flBias") is not None else 0.5}


def gravity(text):
    b = ops(text, "C_OP_BasicMovement")
    if not b:
        return None
    body = b[0][3]
    g = vec(body, "m_vLiteralValue") or vec(body, "m_Gravity")
    drag = literal(body, "m_fDrag")
    return {"Gravity": g, "Drag": drag if drag is not None else 0.0}


def textures(text):
    return re.findall(r'm_hTexture\s*=\s*resource:"materials/([^"]+)\.vtex"', text)


def sprite_system(text, name):
    inits = init_floats(text)
    sphere = ops(text, "C_INIT_CreateWithinSphereTransform")
    radius_max = literal(sphere[0][3], "m_fRadiusMax") if sphere else 0.0
    inst = ops(text, "C_OP_InstantaneousEmitter")
    cont = ops(text, "C_OP_ContinuousEmitter")
    if inst:
        count = literal(inst[0][3], "m_nParticlesToEmit")
    else:
        dur = literal(cont[0][3], "m_flEmissionDuration")
        rate = literal(cont[0][3], "m_flEmitRate")
        count = dur * rate if dur and rate else None
    rend = ops(text, "C_OP_RenderSprites")
    body = rend[0][3] if rend else ""
    blend = re.search(r'm_nOutputBlendMode\s*=\s*"([^"]+)"', body)
    return {
        "Source": name + ".vpcf",
        "Count": count,
        "LifeSeconds": inits.get(FIELD_LIFETIME),
        "RadiusInches": inits.get(FIELD_RADIUS),
        "SphereInches": radius_max or 0.0,
        "Radius": interpolate_radius(text),
        "FadeOut": fade_out(text),
        "ColorMin": color(text, "m_ColorMin"),
        "ColorMax": color(text, "m_ColorMax"),
        "Overbright": literal(body, "m_flOverbrightFactor"),
        "Blend": blend.group(1) if blend else "PARTICLE_OUTPUT_BLEND_MODE_ALPHA",
        "StartFadeSize": literal(body, "m_flStartFadeSize"),
        "EndFadeSize": literal(body, "m_flEndFadeSize"),
        "Texture": (textures(body) or [None])[0],
    }


def spark_system(text, name):
    inits = init_floats(text)
    sphere = ops(text, "C_INIT_CreateWithinSphereTransform")[0][3]
    cont = ops(text, "C_OP_ContinuousEmitter")[0][3]
    rend = ops(text, "C_OP_RenderTrails")[0][3]
    m = re.search(r"m_nMaxParticles\s*=\s*(\d+)", text)
    return {
        "Source": name + ".vpcf",
        "MaxParticles": int(m.group(1)) if m else None,
        "EmissionSeconds": literal(cont, "m_flEmissionDuration"),
        "EmitRate": literal(cont, "m_flEmitRate"),
        "LifeSeconds": inits.get(FIELD_LIFETIME),
        "RadiusInches": literal(text, "m_flConstantRadius"),
        "RadiusScale": literal(rend, "m_flRadiusScale"),
        "MaxLengthInches": literal(rend, "m_flMaxLength"),
        "SpeedMin": vec(sphere, "m_LocalCoordinateSystemSpeedMin"),
        "SpeedMax": vec(sphere, "m_LocalCoordinateSystemSpeedMax"),
        "DistanceBias": vec(sphere, "m_vecDistanceBiasAbs"),
        "Movement": gravity(text),
        "FadeIn": fade_in(text),
        "FadeOut": fade_out(text),
        "Color": color(text, "m_ColorMin"),
        "Texture": (textures(rend) or [None])[0],
    }


def arc_system():
    text = read("weapon_tracers_taser_wire2")
    inits = init_floats(text)
    inst = ops(text, "C_OP_InstantaneousEmitter")[0][3]
    ropes = ops(text, "C_OP_RenderRopes")
    passes = []
    for _, _, _, body in ropes:
        passes.append({
            "Textures": textures(body),
            "RadiusScale": literal(body, "m_flRadiusScale"),
            "Overbright": literal(body, "m_flOverbrightFactor"),
            "ScrollRateNoise": "m_flTextureVScrollRate" in body and "PF_TYPE_RANDOM" in body or "m_flNoiseScale" in body,
        })
    curve = inits.get(FIELD_RADIUS)
    return {
        "Source": "weapon_tracers_taser_wire2.vpcf",
        "StartSeconds": literal(inst, "m_flStartTime"),
        "Points": literal(inst, "m_nParticlesToEmit"),
        "LifeSeconds": inits.get(FIELD_LIFETIME),
        "RadiusCurveDomainMax": curve.get("curveDomainMax") if isinstance(curve, dict) else None,
        "Radius": interpolate_radius(text),
        "FadeOut": fade_out(text),
        "Movement": gravity(text),
        "DampenRangeInches": literal(ops(text, "C_OP_DampenToCP")[0][3], "m_flRange"),
        "ColorMin": color(text, "m_ColorMin"),
        "ColorMax": color(text, "m_ColorMax"),
        "Passes": passes,
    }


def wire_system():
    text = read("weapon_tracers_taser_wire1a")
    inits = init_floats(text)
    inst = ops(text, "C_OP_InstantaneousEmitter")[0][3]
    path = ops(text, "C_INIT_CreateSequentialPathV2")[0][3]
    return {
        "Source": "weapon_tracers_taser_wire1a.vpcf (wire1b identical)",
        "Points": literal(inst, "m_nParticlesToEmit"),
        "LifeSeconds": inits.get(FIELD_LIFETIME),
        "RadiusInches": inits.get(FIELD_RADIUS),
        "Bulge": literal(path, "m_flBulge"),
        "Color": color(text, "m_ColorMin"),
        "FadeOut": fade_out(text),
        "Movement": gravity(text),
        "Rendered": bool(ops(text, "C_OP_RenderRopes") or ops(text, "C_OP_RenderSprites")),
    }


# (baked stem, source png under 06_particles/textures/materials, output size, transpose)
BAKES = [
    ("cs2_zeus_arc", "particle/beam_crack_06_bw_center.png", (512, 128), True),
    ("cs2_zeus_flame", "particle/flames/beam_flame.png", (512, 128), True),
    ("cs2_zeus_glow", "particle/particle_glow_04.png", (128, 128), False),
    ("cs2_zeus_flare", "particle/particle_flares/particle_flare_004b_mod.png", (128, 128), False),
    ("cs2_zeus_glow_impact", "particle/particle_glow_05.png", (128, 128), False),
]


def bake(check):
    out = {}
    for stem, src, size, transpose in BAKES:
        img = Image.open(os.path.join(T, src)).convert("RGBA")
        w, h = img.size
        if transpose:
            # The beam textures run their length down V; the rope's U runs along
            # the arc, so they are turned like the tracer bake (cs2_tracer_texture.py).
            if h <= w:
                sys.exit("%s is %dx%d; expected the long axis down V" % (src, w, h))
            img = img.transpose(Image.Transpose.TRANSPOSE)
        img = img.resize(size, Image.Resampling.LANCZOS)
        out[stem] = {"source": src, "source_size": [w, h], "output_size": list(size), "transposed": transpose}
        if not check:
            img.save(os.path.join(DEST, stem + ".png"))
    return out


def ranged(doc):
    """LifeSeconds / RadiusInches always as [min, max], so one C# shape reads both
    the literal (sparks: 0.3) and the PF_TYPE_RANDOM_UNIFORM (glow: 0.1..0.2) forms."""
    if isinstance(doc, dict):
        for k, v in list(doc.items()):
            if k in ("LifeSeconds", "RadiusInches") and isinstance(v, (int, float)):
                doc[k] = [float(v), float(v)]
            else:
                ranged(v)
    elif isinstance(doc, list):
        for v in doc:
            ranged(v)
    return doc


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()
    vdata = open(os.path.join(EXPORT, "01_weapon_data/firearm_blocks/weapon_taser.vdata"), encoding="utf-8").read()
    tracer = re.search(r'm_szTracerParticle\s*=\s*resource_name:"([^"]+)"', vdata).group(1)
    freq = int(re.search(r"m_nTracerFrequency\s*=\s*(\d+)", vdata).group(1))
    top = read("weapon_tracers_taser")
    children = re.findall(r'm_ChildRef\s*=\s*resource:"particles/weapons/cs_weapon_fx/([^"]+)\.vpcf"', top)
    flash_children = re.findall(r'm_ChildRef\s*=\s*resource:"particles/weapons/cs_weapon_fx/([^"]+)\.vpcf"',
                                read("weapon_muzzle_flash_taser"))
    doc = {
        "Format": "ScCsgoKnives.Cs2TaserEffect/1",
        "Gun": "taser",
        "Source": {
            "vdata": "01_weapon_data/firearm_blocks/weapon_taser.vdata",
            "TracerParticle": tracer, "TracerFrequency": freq,
            "TracerChildren": children, "MuzzleFlashChildren": flash_children,
            "RangeInches": float(re.search(r"m_flRange\s*=\s*([\d.]+)", vdata).group(1)),
        },
        "Wire": wire_system(),
        "Arc": arc_system(),
        "MuzzleGlow": sprite_system(read("weapon_taser_glow"), "weapon_taser_glow"),
        "MuzzleFlash": sprite_system(read("weapon_taser_flash"), "weapon_taser_flash"),
        "MuzzleSparks": spark_system(read("weapon_taser_sparks"), "weapon_taser_sparks"),
        "ImpactGlow": sprite_system(read("weapon_taser_glow_impact"), "weapon_taser_glow_impact"),
        "ImpactSparks": spark_system(read("weapon_taser_sparks_impact"), "weapon_taser_sparks_impact"),
        "Baked": bake(args.check),
    }
    text = json.dumps(ranged(doc), indent=1)
    print(text)
    if not args.check:
        with open(os.path.join(DATA, "cs2_taser_effect.json"), "w", encoding="utf-8") as f:
            f.write(text + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
