"""CS2's muzzle particle systems for every gun (deathmatch round 6, 2026-10-05; the user: "你还可以看所有枪的枪线，曳光弹，以及
枪烟，我感觉这些还是不够还原"; "先做几个，看看效果如何…然后之后把所有枪（有实际数据的），都做了") -> the core's
AnimationData/cs2_muzzle_fx.json and the textures its sprites and trails draw with. Read-only on CS2's files.

Run on Windows: ./tools/dev.ps1 python tools/cs2_muzzle_fx.py
Sources (the CS2 analysis tree the other imports use, E:/projects/CSMCReverse/local_cs2_analysis/all_weapons):
  02_models/events_full/<weapon>.analysis.txt   which muzzle system each weapon's model plays (AE_CL_CREATE_PARTICLE_EFFECT)
  06_particles/definitions/**/<system>.vpcf     every system under it: children and the random choice among them, emitters,
                                                initializers, operators, renderers (kv3, tools/cs2_kv3.py)
  06_particles/textures/**/<texture>.mks + _seqN_M.png   the sprite sheets the renderers name (frames in sheet order)
What is written is CS2's own program for each system, in order: the emitter (count, start, per-frame limit), the initializers and
operators with their parameters and float/vector inputs (literals, random ranges, particle number/age/attribute, control point
components, system age; direct, multiplied, remapped or through CS2's Hermite curves), the renderers (sprite or trail, texture,
blend, radius/alpha/colour scales, overbright, self-illumination, animation, screen-size fades and limits, trail lengths and
tapers), and the control point values of each system's configurations (fps_view, thirdperson, ...). Core/ScMuzzleParticles
runs that program. Anything else - the dynamic light, bloom-only passes, motion-vector frame blending, shadows, ground placement,
noise fields (drawn as uniform random) - is listed per system under "Unmodelled", never approximated silently.
Outputs:
  src/ScCsgoKnives/AnimationData/cs2_muzzle_fx.json
  src/ScCsgoKnives/Assets/Textures/ScCsgoKnives/cs2fx_<texture>.png (Full: frames up to 128 px)
  src/ScCsgoKnives/AssetsLite/Textures/ScCsgoKnives/cs2fx_<texture>.webp (Lite: frames up to 64 px)
Record: docs/tasks/muzzle-fx-cs2-20261005-assets.json (each texture's source frames hash and output hash; a core record)
"""
from __future__ import annotations
import hashlib, json, math, re, sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import cs2_kv3
from PIL import Image

ANALYSIS = Path(r"E:/projects/CSMCReverse/local_cs2_analysis/all_weapons")
EVENTS = ANALYSIS / "02_models/events_full"
DEFS = ANALYSIS / "06_particles/definitions"
TEX = ANALYSIS / "06_particles/textures"
OUT = ROOT / "src/ScCsgoKnives/AnimationData/cs2_muzzle_fx.json"
FULL = ROOT / "src/ScCsgoKnives/Assets/Textures/ScCsgoKnives"; LITE = ROOT / "src/ScCsgoKnives/AssetsLite/Textures/ScCsgoKnives"
RECORD = ROOT / "docs/tasks/muzzle-fx-cs2-20261005-assets.json"
# the mod's gun names and the CS2 weapon model each plays its effects from
MODELS = {"ak47": "weapon_ak47", "m4a4": "weapon_m4a1", "m4a1s": "weapon_m4a1_silencer", "awp": "weapon_awp", "deagle": "weapon_deagle", "glock18": "weapon_glock",
          "usp_silencer": "weapon_usp_silencer", "famas": "weapon_famas", "mp9": "weapon_mp9", "p90": "weapon_p90", "ssg08": "weapon_ssg08", "cz75a": "weapon_cz75a",
          "elite": "weapon_elite", "fiveseven": "weapon_fiveseven", "hkp2000": "weapon_hkp2000", "p250": "weapon_p250", "revolver": "weapon_revolver", "taser": "weapon_taser",
          "tec9": "weapon_tec9", "aug": "weapon_aug", "bizon": "weapon_bizon", "g3sg1": "weapon_g3sg1", "galilar": "weapon_galilar", "m249": "weapon_m249",
          "mac10": "weapon_mac10", "mag7": "weapon_mag7", "mp5sd": "weapon_mp5sd", "mp7": "weapon_mp7", "negev": "weapon_negev", "nova": "weapon_nova",
          "sawedoff": "weapon_sawedoff", "scar20": "weapon_scar20", "sg556": "weapon_sg556", "ump45": "weapon_ump45", "xm1014": "weapon_xm1014"}
SILENCED = {"uweapon_muzsilenced_rif", "uweapon_muzsilenced_subm"}
FULL_FRAME, LITE_FRAME, MAX_FRAMES = 128, 64, 64
def sha(p): return hashlib.sha256(Path(p).read_bytes()).hexdigest()

def find(name):
    for sub in ["particles/unified_weapon_fx", "particles/weapons/cs_weapon_fx", "particles/weapons"]:
        p = DEFS / sub / (name + ".vpcf")
        if p.exists(): return p
    hits = list(DEFS.rglob(name + ".vpcf")); return hits[0] if hits else None
def stem(ref): return ref.split("/")[-1].replace(".vpcf", "").replace(".vtex", "")

class Driven(Exception): """an input of a kind the runtime does not evaluate"""

FLOAT_SOURCES = {"PF_TYPE_PARTICLE_NUMBER": "number", "PF_TYPE_PARTICLE_NUMBER_NORMALIZED": "numberNormalized", "PF_TYPE_PARTICLE_AGE": "age",
                 "PF_TYPE_PARTICLE_AGE_NORMALIZED": "ageNormalized", "PF_TYPE_PARTICLE_FLOAT": "attribute", "PF_TYPE_CONTROL_POINT_COMPONENT": "cp",
                 "PF_TYPE_COLLECTION_AGE": "systemAge", "PF_TYPE_PARTICLE_DETAIL_LEVEL": "detail"}
MAPS = {None: "direct", "PF_MAP_TYPE_DIRECT": "direct", "PF_MAP_TYPE_MULT": "mult", "PF_MAP_TYPE_REMAP": "remap", "PF_MAP_TYPE_REMAP_BIASED": "remapBiased", "PF_MAP_TYPE_CURVE": "curve"}
def fin(v, default=None):
    """a float input as the runtime evaluates it: a number, a random range, or a source (particle number, age, attribute, control
    point, system age, detail level) mapped directly, multiplied, remapped or through the curve; Driven for anything else"""
    if v is None: return default
    if isinstance(v, (int, float)): return float(v)
    if not isinstance(v, dict): raise Driven(v)
    t = v.get("m_nType")
    if t == "PF_TYPE_LITERAL": return float(v.get("m_flLiteralValue", 0))
    if t in ("PF_TYPE_RANDOM_UNIFORM", "PF_TYPE_RANDOM_BIASED"):
        a, b = float(v.get("m_flRandomMin", 0)), float(v.get("m_flRandomMax", 1))
        out = {"T": "rand", "A": a, "B": b}
        if t == "PF_TYPE_RANDOM_BIASED": out["Bias"] = [v.get("m_nBiasType", "PF_BIAS_TYPE_STANDARD"), float(v.get("m_flBiasParameter", 0))]
        return out
    if t not in FLOAT_SOURCES or v.get("m_nMapType") not in MAPS: raise Driven(v)
    out = {"T": FLOAT_SOURCES[t], "Map": MAPS[v.get("m_nMapType")], "Lit": float(v.get("m_flLiteralValue", 0))}
    if t == "PF_TYPE_CONTROL_POINT_COMPONENT": out.update(Cp=int(v.get("m_nControlPoint", 0)), Comp=int(v.get("m_nVectorComponent", 0)))
    if t == "PF_TYPE_PARTICLE_FLOAT": out["Attr"] = int(v.get("m_nScalarAttribute", 3))
    m = out["Map"]
    if m == "mult": out["Mult"] = float(v.get("m_flMultFactor", 1))
    if m in ("remap", "remapBiased"):
        out.update(In=[float(v.get("m_flInput0", 0)), float(v.get("m_flInput1", 1))], Out=[float(v.get("m_flOutput0", 0)), float(v.get("m_flOutput1", 1))])
        if m == "remapBiased": out["Bias"] = [v.get("m_nBiasType", "PF_BIAS_TYPE_STANDARD"), float(v.get("m_flBiasParameter", 0))]
    if m == "curve":
        c = v.get("m_Curve") or {}; sp = c.get("m_spline") or []
        if not sp: raise Driven(v)
        out["Curve"] = [[float(k["x"]), float(k["y"]), float(k.get("m_flSlopeIncoming", 0)), float(k.get("m_flSlopeOutgoing", 0))] for k in sp]
    out["Clamp"] = v.get("m_nInputMode", "PF_INPUT_MODE_CLAMPED") != "PF_INPUT_MODE_LOOSE"
    return out
def vin(v, default=None):
    """a vector input: a literal (colours as 0..1), a particle vector, a float interpolated between two vectors, or three floats"""
    if v is None: return default
    if isinstance(v, list) and len(v) >= 3 and all(isinstance(x, (int, float)) for x in v[:3]): return [float(x) for x in v[:3]]
    if not isinstance(v, dict): raise Driven(v)
    t = v.get("m_nType")
    if t == "PVEC_TYPE_LITERAL": return [float(x) for x in v.get("m_vLiteralValue", [0, 0, 0])[:3]]
    if t == "PVEC_TYPE_LITERAL_COLOR": return [x / 255 for x in (v.get("m_LiteralColor") or [255, 255, 255])[:3]]
    if t == "PVEC_TYPE_PARTICLE_VECTOR": return {"T": "pvec", "Attr": int(v.get("m_nVectorAttribute", 0)), "Scale": [float(x) for x in (v.get("m_vVectorAttributeScale") or [1, 1, 1])[:3]]}
    if t == "PVEC_TYPE_FLOAT_INTERP_CLAMPED":
        return {"T": "interp", "F": fin(v.get("m_FloatInterp"), 0.0), "In": [float(v.get("m_flInterpInput0", 0)), float(v.get("m_flInterpInput1", 1))],
                "O0": [float(x) for x in v.get("m_vInterpOutput0", [0, 0, 0])[:3]], "O1": [float(x) for x in v.get("m_vInterpOutput1", [1, 1, 1])[:3]]}
    if t == "PVEC_TYPE_FLOAT_COMPONENTS": return {"T": "comps", "X": fin(v.get("m_FloatComponentX"), 0.0), "Y": fin(v.get("m_FloatComponentY"), 0.0), "Z": fin(v.get("m_FloatComponentZ"), 0.0)}
    raise Driven(v)
def color(v, default=(255, 255, 255)):
    v = v if isinstance(v, list) and len(v) >= 3 else default
    return [(x if isinstance(x, (int, float)) else 255) / 255 for x in v[:3]]
SET = {None: "replace", "PARTICLE_SET_REPLACE_VALUE": "replace", "PARTICLE_SET_SCALE_INITIAL_VALUE": "scaleInitial", "PARTICLE_SET_SCALE_CURRENT_VALUE": "scaleCurrent",
       "PARTICLE_SET_ADD_TO_INITIAL_VALUE": "addInitial", "PARTICLE_SET_ADD_TO_CURRENT_VALUE": "addCurrent"}
def setm(op, key="m_nSetMethod", default=None):
    m = op.get(key, default)
    if m not in SET: raise Driven(m)
    return SET[m]
def num(v, default):
    """a parameter that must be a constant: a number or a literal input; Driven for anything else"""
    if v is None: return default
    if isinstance(v, bool): return float(v)
    if isinstance(v, (int, float)): return float(v)
    if isinstance(v, dict) and v.get("m_nType") == "PF_TYPE_LITERAL": return float(v.get("m_flLiteralValue", 0))
    raise Driven(v)

def initializer(op, cls):
    """one initializer in the runtime's terms (None: nothing to do; Driven/KeyError: not modelled)"""
    g = op.get
    if cls in ("C_INIT_CreateWithinSphereTransform", "C_INIT_CreateWithinSphere"):
        return {"Op": "sphere", "RMin": fin(g("m_fRadiusMin"), 0.0), "RMax": fin(g("m_fRadiusMax"), 0.0), "SMin": fin(g("m_fSpeedMin"), 0.0), "SMax": fin(g("m_fSpeedMax"), 0.0),
                "SpeedExp": num(g("m_fSpeedRandExp"), 1.0), "LMin": vin(g("m_LocalCoordinateSystemSpeedMin"), [0, 0, 0]), "LMax": vin(g("m_LocalCoordinateSystemSpeedMax"), [0, 0, 0]),
                "Bias": vin(g("m_vecDistanceBias"), [1, 1, 1]), "BiasAbs": vin(g("m_vecDistanceBiasAbs"), [0, 0, 0])}
    if cls == "C_INIT_CreateWithinBox": return {"Op": "box", "Min": vin(g("m_vecMin"), [0, 0, 0]), "Max": vin(g("m_vecMax"), [0, 0, 0]), "Local": bool(g("m_bLocalSpace"))}
    if cls == "C_INIT_PositionOffset":
        if g("m_bProportional"): raise Driven("proportional offset")
        return {"Op": "offset", "Min": vin(g("m_OffsetMin"), [0, 0, 0]), "Max": vin(g("m_OffsetMax"), [0, 0, 0]), "Local": bool(g("m_bLocalCoords"))}
    if cls == "C_INIT_PositionWarp": return {"Op": "warp", "Min": vin(g("m_vecWarpMin"), [1, 1, 1]), "Max": vin(g("m_vecWarpMax"), [1, 1, 1])}
    if cls == "C_INIT_PositionWarpScalar": return {"Op": "warpScalar", "Min": vin(g("m_vecWarpMin"), [1, 1, 1]), "Max": vin(g("m_vecWarpMax"), [1, 1, 1]), "F": fin(g("m_InputValue"), 0.0)}
    if cls == "C_INIT_VelocityRandom":
        return {"Op": "velocity", "SMin": fin(g("m_fSpeedMin"), 0.0), "SMax": fin(g("m_fSpeedMax"), 0.0), "LMin": vin(g("m_LocalCoordinateSystemSpeedMin"), [0, 0, 0]), "LMax": vin(g("m_LocalCoordinateSystemSpeedMax"), [0, 0, 0])}
    if cls == "C_INIT_InitialVelocityNoise":
        # coherent noise in CS2; the runtime draws each component uniformly between the two vectors (listed as approximated)
        return {"Op": "velocityNoise", "Min": vin(g("m_vecOutputMin"), [0, 0, 0]), "Max": vin(g("m_vecOutputMax"), [1, 1, 1])}
    if cls == "C_INIT_RingWave":
        return {"Op": "ring", "PerOrbit": fin(g("m_flParticlesPerOrbit"), -1.0), "Radius": fin(g("m_flInitialRadius"), 0.0), "Thickness": fin(g("m_flThickness"), 0.0),
                "SMin": fin(g("m_flInitialSpeedMin"), 0.0), "SMax": fin(g("m_flInitialSpeedMax"), 0.0), "Roll": fin(g("m_flRoll"), 0.0), "Pitch": fin(g("m_flPitch"), 0.0), "Yaw": fin(g("m_flYaw"), 0.0),
                "Even": bool(g("m_bEvenDistribution")), "XYOnly": g("m_bXYVelocityOnly", True) is not False}
    if cls == "C_INIT_VelocityFromNormal": return {"Op": "velocityFromNormal", "SMin": num(g("m_fSpeedMin"), 0.0), "SMax": num(g("m_fSpeedMax"), 0.0)}
    if cls == "C_INIT_RemapInitialDirectionToTransformToVector":
        return {"Op": "directionToVector", "Field": int(g("m_nFieldOutput", 0)), "Normalize": bool(g("m_bNormalize")), "Scale": num(g("m_flScale"), 1.0)}
    if cls in ("C_INIT_InitFloat", "C_INIT_InitFloatCollection"):
        return {"Op": "float", "Field": int(g("m_nOutputField", 3)), "Set": setm(op), "V": fin(g("m_InputValue"), 0.0)}
    if cls == "C_INIT_CreationNoise":
        # coherent noise over position and time in CS2; uniform between the outputs here (listed as approximated)
        return {"Op": "float", "Field": int(g("m_nFieldOutput", 3)), "Set": "replace", "V": {"T": "rand", "A": num(g("m_flOutputMin"), 0.0), "B": num(g("m_flOutputMax"), 1.0)}}
    if cls == "C_INIT_InitVec": return {"Op": "vec", "Field": int(g("m_nOutputField", 0)), "Set": setm(op), "V": vin(g("m_InputValue"), [0, 0, 0])}
    if cls == "C_INIT_RemapScalarToVector":
        if g("m_flStartTime", -1) != -1 or g("m_flEndTime", -1) != -1: raise Driven("time window")
        return {"Op": "scalarToVector", "In": int(g("m_nFieldInput", 8)), "Field": int(g("m_nFieldOutput", 0)), "InMin": num(g("m_flInputMin"), 0.0), "InMax": num(g("m_flInputMax"), 1.0),
                "Min": vin(g("m_vecOutputMin"), [0, 0, 0]), "Max": vin(g("m_vecOutputMax"), [1, 1, 1]), "Local": g("m_bLocalCoords", True) is not False, "Set": setm(op)}
    if cls == "C_INIT_RandomSequence": return {"Op": "sequence", "Min": int(g("m_nSequenceMin", 0)), "Max": int(g("m_nSequenceMax", 0))}
    if cls == "C_INIT_RandomColor": return {"Op": "color", "Min": color(g("m_ColorMin")), "Max": color(g("m_ColorMax"))}
    if cls == "C_INIT_AgeNoise": return {"Op": "ageNoise", "Min": num(g("m_flAgeMin"), 0.0), "Max": num(g("m_flAgeMax"), 1.0)}
    if cls == "C_INIT_GlobalScale":
        return {"Op": "globalScale", "Cp": int(g("m_nScaleControlPointNumber", -1)), "Scale": num(g("m_flScale"), 1.0), "Radius": g("m_bScaleRadius", True) is not False,
                "Position": g("m_bScalePosition", True) is not False, "Velocity": bool(g("m_bScaleVelocity"))}
    if cls == "C_INIT_InheritVelocity": return {"Op": "inheritVelocity", "Scale": num(g("m_flVelocityScale"), 1.0)}
    if cls == "C_INIT_RandomSecondSequence": return {"Op": "sequence2", "Min": int(g("m_nSequenceMin", 0)), "Max": int(g("m_nSequenceMax", 0))}
    raise Driven(cls)

def operator(op, cls):
    g = op.get
    if cls == "C_OP_BasicMovement": return {"Op": "move", "Gravity": vin(g("m_Gravity"), [0, 0, 0]), "Drag": fin(g("m_fDrag"), 0.0)}
    if cls == "C_OP_Decay": return {"Op": "decay"}
    if cls in ("C_OP_FadeOut", "C_OP_FadeIn"):
        k = "Out" if cls == "C_OP_FadeOut" else "In"
        return {"Op": "fade" + k, "Min": num(g(f"m_flFade{k}TimeMin"), .25), "Max": num(g(f"m_flFade{k}TimeMax"), .25), "Exp": num(g(f"m_flFade{k}TimeExp"), 1.0),
                "Proportional": g("m_bProportional", True) is not False, "Ease": g("m_bEaseInAndOut", True) is not False}
    if cls == "C_OP_InterpolateRadius":
        return {"Op": "radius", "StartTime": num(g("m_flStartTime"), 0.0), "EndTime": num(g("m_flEndTime"), 1.0), "Start": fin(g("m_flStartScale"), 1.0), "End": fin(g("m_flEndScale"), 1.0),
                "Bias": num(g("m_flBias"), 0.0), "Ease": bool(g("m_bEaseInAndOut"))}
    if cls == "C_OP_ColorInterpolate":
        return {"Op": "colorFade", "Color": color(g("m_ColorFade")), "Start": num(g("m_flFadeStartTime"), 0.0), "End": num(g("m_flFadeEndTime"), 1.0), "Ease": g("m_bEaseInOut", True) is not False}
    if cls == "C_OP_MaxVelocity": return {"Op": "maxVelocity", "Max": num(g("m_flMaxVelocity"), 0.0), "Min": num(g("m_flMinVelocity"), 0.0)}
    if cls == "C_OP_PositionLock":
        return {"Op": "lock", "StartMin": num(g("m_flStartTime_min"), 1.0), "StartMax": num(g("m_flStartTime_max"), 1.0), "EndMin": num(g("m_flEndTime_min"), 1.0),
                "EndMax": num(g("m_flEndTime_max"), 1.0), "Rot": bool(g("m_bLockRot")), "Strength": fin(g("m_flOpStrength"), 1.0)}
    if cls == "C_OP_SetFloat": return {"Op": "setFloat", "Field": int(g("m_nOutputField", 3)), "Set": setm(op), "V": fin(g("m_InputValue"), 0.0)}
    if cls == "C_OP_SetVec": return {"Op": "setVec", "Field": int(g("m_nOutputField", 0)), "V": vin(g("m_InputValue"), [0, 0, 0])}
    if cls == "C_OP_RampScalarLinearSimple": return {"Op": "ramp", "Field": int(g("m_nField", 3)), "Rate": num(g("m_Rate"), 0.0), "Start": num(g("m_flStartTime"), 0.0), "End": num(g("m_flEndTime"), 1.0)}
    if cls == "C_OP_LerpScalar": return {"Op": "lerp", "Field": int(g("m_nFieldOutput", 3)), "Output": fin(g("m_flOutput"), 1.0), "Start": num(g("m_flStartTime"), 0.0), "End": num(g("m_flEndTime"), 1.0)}
    if cls == "C_OP_SpinUpdate": return {"Op": "spinUpdate"}
    if cls == "C_OP_Spin": return {"Op": "spin", "Rate": num(g("m_nSpinRateDegrees"), 0.0), "MinRate": num(g("m_nSpinRateMinDegrees"), 0.0), "StopTime": num(g("m_fSpinRateStopTime"), 0.0)}
    if cls == "C_OP_Cull": return {"Op": "cull", "Fraction": num(g("m_flCullPerc"), .5), "Start": num(g("m_flCullStart"), 0.0), "End": num(g("m_flCullEnd"), 1.0)}
    raise Driven(cls)

BLEND = {None: "blend", "PARTICLE_OUTPUT_BLEND_MODE_ALPHA": "blend", "PARTICLE_OUTPUT_BLEND_MODE_ADD": "add", "PARTICLE_OUTPUT_BLEND_MODE_LIGHTEN": "lighten"}
ANIMATION = {None: "fixed", "ANIMATION_TYPE_FIXED_RATE": "fixed", "ANIMATION_TYPE_FIT_LIFETIME": "fit", "ANIMATION_TYPE_MANUAL_FRAMES": "manual"}
def renderer(r, cls, un):
    g = r.get
    if g("m_bOnlyRenderInEffectsBloomPass"): un.append("bloom-only pass (no bloom in this game)"); return None
    if cls == "C_OP_RenderStandardLight":
        # the muzzle's short orange light: drawn as light on the particles CS2 lights diffusely (the smoke); the world's blocks,
        # the gun and the hands are not lit by it
        un.append("the light does not light blocks, the gun or the hands")
        return {"Kind": "light", "ColorScale": vin(g("m_vecColorScale"), [1, 1, 1]), "Intensity": num(g("m_flIntensity"), 1.0), "RadiusMultiplier": num(g("m_flRadiusMultiplier"), 1.0)}
    if cls not in ("C_OP_RenderSprites", "C_OP_RenderTrails"): raise Driven(cls)
    texs = [t for t in g("m_vecTexturesInput") or [] if t.get("m_hTexture") and t.get("m_nTextureType") in (None, "SPRITECARD_TEXTURE_DIFFUSE")]
    if not texs: raise Driven("no texture")
    # a further colour texture blended in by its own input (the muzzle smoke starts as thin steam and turns to smoke over the
    # system's first half second); motion-vector textures (frame blending) are not drawn
    second = next((t for t in texs[1:] if t.get("m_flTextureBlend") is not None), None)
    if any(t.get("m_nTextureType") == "SPRITECARD_TEXTURE_ANIMMOTIONVEC" for t in g("m_vecTexturesInput") or []): un.append("motion-vector frame blending")
    if len(texs) > (2 if second else 1): un.append("further sprite textures")
    if g("m_bParticleShadows"): un.append("particle shadows")
    if g("m_nOutputBlendMode") not in BLEND or g("m_nAnimationType") not in ANIMATION or g("m_nOrientationType") not in (None, "PARTICLE_ORIENTATION_SCREEN_ALIGNED"): raise Driven("blend/animation/orientation")
    out = {"Kind": "sprite" if cls == "C_OP_RenderSprites" else "trail", "Texture": stem(texs[0]["m_hTexture"]), "Blend": BLEND[g("m_nOutputBlendMode")],
           "RadiusScale": fin(g("m_flRadiusScale"), 1.0), "AlphaScale": fin(g("m_flAlphaScale"), 1.0), "ColorScale": vin(g("m_vecColorScale"), [1, 1, 1]),
           "Overbright": num(g("m_flOverbrightFactor"), 1.0), "SelfIllum": num(g("m_flSelfIllumAmount"), 0.0), "Diffuse": num(g("m_flDiffuseAmount"), 1.0),
           "AnimRate": num(g("m_flAnimationRate"), 0.1), "Anim": ANIMATION[g("m_nAnimationType")], "FitCycle": bool(g("m_bFitCycleToLifetime")),
           "StartFade": num(g("m_flStartFadeSize"), 100000.0), "EndFade": num(g("m_flEndFadeSize"), 200000.0), "MaxSize": num(g("m_flMaxSize"), 5000.0), "MinSize": num(g("m_flMinSize"), 0.0)}
    if second: out.update({"Texture2": stem(second["m_hTexture"]), "Blend2": fin(second["m_flTextureBlend"], 0.0)})
    if cls == "C_OP_RenderTrails":
        out.update({"LengthScale": num(g("m_flLengthScale"), 1.0), "LengthFadeIn": num(g("m_flLengthFadeInTime"), 0.0), "MaxLength": num(g("m_flMaxLength"), 2000.0),
                    "MinLength": num(g("m_flMinLength"), 0.0), "Taper": fin(g("m_flRadiusTaper"), 1.0), "HeadTaper": fin(g("m_flRadiusHeadTaper"), 1.0),
                    "HeadColor": vin(g("m_vecHeadColorScale"), [1, 1, 1]), "TailColor": vin(g("m_vecTailColorScale"), [1, 1, 1])})
    return out

def system(name):
    """one system: its children and the random choice among them, the control point values of its configurations, and - if it
    emits - its emitter, initializers, operators and renderers in order, each in the runtime's terms"""
    p = find(name)
    if p is None: return {"Missing": name}
    d = cs2_kv3.load(p); un = []
    out = {"Source": str(p.relative_to(ANALYSIS)).replace("\\", "/"), "Group": int(d.get("m_nGroupID") or 0)}
    kids = [{"System": stem(c.get("m_ChildRef", "")), "Group": 0, "Delay": num(c.get("m_flDelay"), 0.0)} for c in d.get("m_Children") or [] if not c.get("m_bDisableChild")]
    choose = []
    for op in d.get("m_PreEmissionOperators") or []:
        cls = op.get("_class")
        if cls == "C_OP_ChooseRandomChildrenInGroup":
            # A choice that names no group is taken as group 1. Inferred, not read: every unified root carries one such operator
            # next to "group 2, choose 2", and group 1 is where its flash variants are (one listed twice - a weighted pick -,
            # an empty one); taken as group 0 it would drop the smoke or the light and play every flash variant at once.
            n = fin(op.get("m_flNumberOfChildren"), 1.0); choose.append({"Group": int(op.get("m_nChildGroupID", 1)), "Count": int(round(n)) if isinstance(n, float) else 1})
        else: un.append(cls)
    # a child's group is the child system's own m_nGroupID (the parent's list only names it); filled in once every system is read
    if kids: out["Children"] = kids
    if choose: out["Choose"] = choose
    configs = {}
    for c in d.get("m_controlPointConfigurations") or []:
        cps = {str(dr.get("m_iControlPoint", 0)): [float(x) for x in (dr.get("m_vecOffset") or [0, 0, 0])[:3]] for dr in c.get("m_drivers") or [] if dr.get("m_iControlPoint", 0) > 0 and dr.get("m_iAttachType") == "PATTACH_WORLDORIGIN"}
        configs[c.get("m_name", "")] = cps
    if configs: out["Configs"] = configs
    emitters = d.get("m_Emitters") or []
    if emitters:
        e = emitters[0]
        if len(emitters) > 1 or e.get("_class") != "C_OP_InstantaneousEmitter": un.append("emitters other than one instantaneous burst")
        else:
            try: out["Emit"] = {"Count": fin(e.get("m_nParticlesToEmit"), 100.0), "Start": fin(e.get("m_flStartTime"), 0.0), "PerFrame": int(e.get("m_nMaxEmittedPerFrame", -1))}
            except Driven: un.append("emit count (driven input)")
    if "Emit" in out:
        out["Max"] = int(d.get("m_nMaxParticles") or 1000)
        out["Const"] = {"Radius": num(d.get("m_flConstantRadius"), 5.0), "Life": num(d.get("m_flConstantLifespan"), 1.0), "Color": color(d.get("m_ConstantColor")),
                        "Rotation": num(d.get("m_flConstantRotation"), 0.0), "RotationSpeed": num(d.get("m_flConstantRotationSpeed"), 0.0), "Sequence": int(d.get("m_nConstantSequenceNumber") or 0)}
        for key, fn, target in [("m_Initializers", initializer, "Init"), ("m_Operators", operator, "Ops")]:
            ops = []
            for op in d.get(key) or []:
                cls = op.get("_class")
                if cls in ("C_INIT_InitialVelocityNoise", "C_INIT_CreationNoise"): un.append(cls + " (noise drawn as uniform random)")
                try:
                    o = fn(op, cls)
                    if o is not None: ops.append(o)
                except Driven as x: un.append(f"{cls} ({str(x)[:60]})")
            out[target] = ops
        rends = []
        for r in d.get("m_Renderers") or []:
            try:
                o = renderer(r, r.get("_class"), un)
                if o is not None: rends.append(o)
            except Driven as x: un.append(f"{r.get('_class')} not drawn ({str(x)[:60]})")
        out["Renderers"] = rends
    out["Unmodelled"] = sorted(set(un))
    return out

def sheet(tex):
    """the texture's frames in sheet order, by sequence: [[png path, ...], ...]"""
    hits = list(TEX.rglob(tex + ".mks"))
    if hits:
        text = hits[0].read_text("utf-8", errors="replace"); seqs = []; cur = None
        for line in text.splitlines():
            line = line.strip()
            if line.startswith("sequence"): cur = []; seqs.append(cur)
            elif line.startswith("frame") and cur is not None:
                f = hits[0].parent / line.split()[1]
                if f.exists(): cur.append(f)
        seqs = [s for s in seqs if s]
        if seqs: return seqs
    single = list(TEX.rglob(tex + ".png"))
    return [[single[0]]] if single else []

# CS2 samples its sprite sheets in linear space and adds them there; the game here blends in gamma space, where an sRGB mid-tone
# adds about 2.4 times its linear value. A sheet only ever drawn additively (add, lighten) is stored with linear RGB (alpha
# as is), so what it adds is what CS2 adds; a sheet any alpha-blended renderer uses is kept as it is.
LINEAR = bytes(round(255 * (i / 255) ** 2.2) for i in range(256))
def bake(tex, frame_px, linear=False):
    seqs = sheet(tex)
    if not seqs: return None
    # at most MAX_FRAMES frames: each sequence keeps its share (rounded down), frames sampled evenly through it; the atlas grid
    # is a power of two each way (the engine's image checks and mipmapping)
    total = sum(len(s) for s in seqs); keep = []
    for s in seqs:
        n = max(1, MAX_FRAMES * len(s) // total) if total > MAX_FRAMES else len(s)
        keep.append([s[int(i * len(s) / n)] for i in range(n)])
    frames = [f for s in keep for f in s]
    assert len(frames) <= MAX_FRAMES, (tex, len(frames))
    cols = 1 << max(0, math.ceil(math.log2(math.ceil(math.sqrt(len(frames)))))); rows = 1 << max(0, math.ceil(math.log2(math.ceil(len(frames) / cols))))
    atlas = Image.new("RGBA", (cols * frame_px, rows * frame_px), (0, 0, 0, 0))
    for i, f in enumerate(frames):
        im = Image.open(f).convert("RGBA"); im.thumbnail((frame_px, frame_px), Image.Resampling.LANCZOS)
        cell = Image.new("RGBA", (frame_px, frame_px), (0, 0, 0, 0)); cell.paste(im, ((frame_px - im.width) // 2, (frame_px - im.height) // 2))
        atlas.paste(cell, ((i % cols) * frame_px, (i // cols) * frame_px))
    if linear:
        r, g, b, a = atlas.split(); atlas = Image.merge("RGBA", [c.point(list(LINEAR)) for c in (r, g, b)] + [a])
    starts, k = [], 0
    for s in keep: starts.append([k, len(s)]); k += len(s)
    return atlas, {"Columns": cols, "Rows": rows, "Frames": len(frames), "Sequences": starts}, hashlib.sha256(b"".join(Path(f).read_bytes() for f in frames)).hexdigest()

if __name__ == "__main__":
    guns, systems = {}, {}
    for gun, model in MODELS.items():
        ev = (EVENTS / f"{model}.analysis.txt").read_text("utf-8", errors="replace")
        muz = [stem(n) for n in dict.fromkeys(re.findall(r"particles/[\w/]+\.vpcf", ev)) if "muz" in n]
        entry = {}
        for m in muz: entry["silenced" if m in SILENCED and len(muz) > 1 else "default"] = m
        guns[gun] = entry
    todo = list(dict.fromkeys(m for g in guns.values() for m in g.values()))
    while todo:
        n = todo.pop(0)
        if n in systems: continue
        systems[n] = system(n)
        todo += [c["System"] for c in systems[n].get("Children", []) if c["System"] not in systems]
    for sp in systems.values():
        for c in sp.get("Children", []): c["Group"] = systems.get(c["System"], {}).get("Group", 0)
    textures, records = {}, []
    FULL.mkdir(parents=True, exist_ok=True); LITE.mkdir(parents=True, exist_ok=True)
    blends = {}
    for sp in systems.values():
        for r in sp.get("Renderers", []):
            for t in [r.get("Texture"), r.get("Texture2")]:
                if t: blends.setdefault(t, set()).add(r["Blend"])
    used = sorted(blends)
    for f in list(FULL.glob("cs2fx_*.png")) + list(LITE.glob("cs2fx_*.webp")): f.unlink()   # only this run's sheets stay
    for tex in used:
        entry = {}
        # an sRGB sheet for alpha-blended renderers, a linear one for additive renderers (both where both draw it)
        for linear, wanted in [(False, bool(blends[tex] - {"add", "lighten"})), (True, bool(blends[tex] & {"add", "lighten"}))]:
            if not wanted: continue
            full = bake(tex, FULL_FRAME, linear); lite = bake(tex, LITE_FRAME, linear)
            if full is None: break
            key = "cs2fx_" + tex + ("_lin" if linear else "")
            for (atlas, info, src_hash), folder, ext, edition in [(full, FULL, "png", "full"), (lite, LITE, "webp", "lite")]:
                target = folder / f"{key}.{ext}"
                if ext == "png": atlas.save(target, optimize=True)
                else: atlas.save(target, "WEBP", quality=88, method=6)
                records.append({"name": key, "sources": {f"06_particles/textures/**/{tex} ({info['Frames']} frames{', linear RGB' if linear else ''})": src_hash}, "target": target.relative_to(ROOT).as_posix(),
                                "member": f"Assets/Textures/ScCsgoKnives/{key}.{ext}", "sha256": sha(target), "bytes": target.stat().st_size, "edition": edition})
            entry["LinearAsset" if linear else "Asset"] = key; entry.update(full[1])
        textures[tex] = entry or {"Missing": True}
    # a second texture CS2's files do not carry (particle_ring_wave_8, half of the AWP flash): the first is drawn alone, in full
    for sp in systems.values():
        for r in sp.get("Renderers", []):
            if r.get("Texture2") and textures.get(r["Texture2"], {}).get("Missing"):
                sp["Unmodelled"] = sorted(set(sp["Unmodelled"]) | {f"second texture {r['Texture2']} not available (the first is drawn alone)"})
                del r["Texture2"], r["Blend2"]
    textures = {t: v for t, v in textures.items() if not v.get("Missing") or any(t == r.get("Texture") for sp in systems.values() for r in sp.get("Renderers", []))}
    doc = {"Format": "ScCsgoKnives.Cs2MuzzleFx/2", "Units": "CS2 units (inch); control point 0 is the muzzle (x forward, y left, z up); gravity in CS2 world (z up); colours 0..1; attributes by CS2 index (3 radius, 7 alpha, 18 scratch float, 38 manual animation frame, ...)",
           "Guns": guns, "Systems": systems, "Textures": textures}
    OUT.write_text(json.dumps(doc, ensure_ascii=False, indent=1), "utf-8")
    RECORD.write_text(json.dumps(records, ensure_ascii=False, indent=1), "utf-8")
    print(len(guns), "guns;", len(systems), "systems;", len(textures), "textures ->", OUT, sha(OUT)[:16])
    print({g: v for g, v in guns.items()})
    print("missing:", [k for k, v in systems.items() if "Missing" in v], [k for k, v in textures.items() if v.get("Missing")])
    from collections import Counter
    print(Counter(u for s in systems.values() for u in s.get("Unmodelled", [])).most_common(40))
