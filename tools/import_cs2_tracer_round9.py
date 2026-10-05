"""CS2 tracer resources for round 9 (docs/tasks/round9-tp-aim-cs2-tracer-hud-20261001.md), read-only on the CS2 sources.
Run on Windows: ./tools/dev.ps1 python tools/import_cs2_tracer_round9.py

1. The tracer streak's colour lookup. Every C_OP_RenderTrails pass of weapon_tracers_assrifle / _rifle(_ssg/_scar) /
   _pistol / _shot carries a second texture input with m_bReplaceTextureWithGradient and
   m_nTextureType SPRITECARD_TEXTURE_1D_COLOR_LOOKUP: the first texture's value is looked up in that gradient. The
   gradients are read from the .vpcf (and checked equal across those systems), then baked into copies of the shipped
   streak textures (Assets/Textures/ScCsgoKnives/cs2_tracer_add.png, cs2_tracer_blend.png, made by
   tools/cs2_tracer_texture.py; U along the trail, V across):
     pass 1 (ADD, shape in RGB, opaque, channel mix RGBA_RGBALPHA): v = luminance; colour = gradient1(v) * v
     pass 2 (BLEND_ADD, shape in alpha):                              v = alpha;     colour = gradient2(v), alpha kept
   Which channel feeds the lookup and the RGB-as-alpha weighting are the reading of the shader inputs, not measured from
   a CS2 render (recorded as an approximation in the brief).
2. The lingering line (weapon_tracers_rifle_wisp, _wisp_ssg, _wisp_scar: AWP, SSG 08, G3SG1, SCAR-20): its two ropes'
   base textures materials/particle/beam_energy_01 and beam_smoke_01, copied pixel for pixel (U across the rope,
   V along it, as the rope's V repeat/scroll expects).
Outputs: Full PNG, Lite WebP (lossless) under Assets/ and AssetsLite/Textures/ScCsgoKnives/:
   cs2_tracer_add_lut, cs2_tracer_blend_lut, cs2_wisp_energy, cs2_wisp_smoke.
Record: docs/tasks/round9-tp-aim-cs2-tracer-hud-20261001-assets.json (source and output hashes), read by
tools/completion_140.py (CORE_RECORDS) to add the members to both core packages.
"""
import hashlib, json, subprocess, sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / ".tmp/skin-bake-deps")); sys.path.insert(0, str(ROOT / "tools"))
import numpy as np
from PIL import Image
import cs2_kv3

ANALYSIS = Path(r"E:/projects/CSMCReverse/local_cs2_analysis/all_weapons/06_particles")
FX = ANALYSIS / "definitions/particles/weapons/cs_weapon_fx"
MATERIALS = ANALYSIS / "textures/materials"
FULL = ROOT / "src/ScCsgoKnives/Assets"; LITE = ROOT / "src/ScCsgoKnives/AssetsLite"
TEX = "Textures/ScCsgoKnives"
def sha(p): return hashlib.sha256(Path(p).read_bytes()).hexdigest()

print("git status (textures, read-only):", subprocess.run(["git", "-C", str(ROOT), "status", "--short", "--", "src/ScCsgoKnives/Assets/Textures", "src/ScCsgoKnives/AssetsLite/Textures"],
                                                           capture_output=True, text=True).stdout.strip() or "clean")

def gradients(name):
    doc = cs2_kv3.load(FX / (name + ".vpcf")); out = []
    for r in doc.get("m_Renderers") or []:
        if r.get("_class") != "C_OP_RenderTrails": continue
        stops = None
        for t in r.get("m_vecTexturesInput") or []:
            if t.get("m_bReplaceTextureWithGradient") and t.get("m_nTextureType") == "SPRITECARD_TEXTURE_1D_COLOR_LOOKUP":
                stops = [(float(s["m_flPosition"]), [int(c) for c in s["m_Color"][:3]]) for s in t["m_Gradient"]["m_Stops"]]
        out.append(stops)
    return out
reference = gradients("weapon_tracers_assrifle")
assert len(reference) == 2 and all(reference), reference
for other in ["weapon_tracers_rifle", "weapon_tracers_rifle_ssg", "weapon_tracers_rifle_scar", "weapon_tracers_pistol", "weapon_tracers_shot"]:
    assert gradients(other) == reference, f"{other} has other colour lookups: {gradients(other)}"
print("colour lookups (assrifle = rifle* = pistol = shot):", json.dumps(reference))

def lookup(stops, v):
    pos = np.array([p for p, _ in stops], np.float32); col = np.array([c for _, c in stops], np.float32)
    return np.stack([np.interp(v, pos, col[:, k], left=col[0, k], right=col[-1, k]) for k in range(3)], axis=-1)

records = []
def record(name, sources, target, member, edition, **extra):
    records.append(dict(name=name, sources={Path(s).as_posix(): sha(s) for s in sources}, target=target.relative_to(ROOT).as_posix(),
                        member=member, sha256=sha(target), bytes=target.stat().st_size, edition=edition, **extra))
def save(image, rel, name, sources, **extra):
    full = FULL / TEX / (rel + ".png"); full.parent.mkdir(parents=True, exist_ok=True); image.save(full, optimize=True)
    record(name, sources, full, f"Assets/{TEX}/{rel}.png", "full", **extra)
    lite = LITE / TEX / (rel + ".webp"); lite.parent.mkdir(parents=True, exist_ok=True); image.save(lite, "WEBP", lossless=True, method=6)
    record(name, sources, lite, f"Assets/{TEX}/{rel}.webp", "lite", **extra)

# 1. Streak colour lookups, baked into copies of the shipped streak textures, on the texture's value alone: a white core
#    with the gradient's orange at its edges. (Tracers, 2026-10-05: a bake on the value times the particle's alpha turned the
#    core deep orange-red; CS2's own first-person frames show the AK's dash as a warm white, brightest at the head - the
#    one-pixel line mixes core and edge - so the bake stays on the value alone.)
add_src = FULL / TEX / "cs2_tracer_add.png"; blend_src = FULL / TEX / "cs2_tracer_blend.png"
add = np.asarray(Image.open(add_src).convert("RGBA")).astype(np.float32) / 255
v = add[..., 0] * .299 + add[..., 1] * .587 + add[..., 2] * .114
rgb = lookup(reference[0], v) * v[..., None]
out = np.concatenate([rgb, np.full(v.shape + (1,), 255, np.float32)], axis=-1)
save(Image.fromarray(out.clip(0, 255).round().astype(np.uint8), "RGBA"), "cs2_tracer_add_lut", "cs2_tracer_add_lut", [add_src, FX / "weapon_tracers_assrifle.vpcf"],
     lookup=reference[0], valueMax=round(float(v.max()), 4))
blend = np.asarray(Image.open(blend_src).convert("RGBA")).astype(np.float32) / 255
v = blend[..., 3]
rgb = lookup(reference[1], v)
out = np.concatenate([rgb, (v * 255)[..., None]], axis=-1)
save(Image.fromarray(out.clip(0, 255).round().astype(np.uint8), "RGBA"), "cs2_tracer_blend_lut", "cs2_tracer_blend_lut", [blend_src, FX / "weapon_tracers_assrifle.vpcf"],
     lookup=reference[1], valueMax=round(float(v.max()), 4))

# 2. The wisp ropes' base textures, unchanged pixels.
for name, src in [("cs2_wisp_energy", MATERIALS / "particle/beam_energy_01.png"), ("cs2_wisp_smoke", MATERIALS / "particle/beam_smoke_01.png")]:
    im = Image.open(src).convert("RGBA")
    save(im, name, name, [src, FX / "weapon_tracers_rifle_wisp.vpcf"], size=list(im.size))

out = ROOT / "docs/tasks/round9-tp-aim-cs2-tracer-hud-20261001-assets.json"
out.write_text(json.dumps(records, indent=1, ensure_ascii=False) + "\n", "utf8")
print(json.dumps(records, indent=1, ensure_ascii=False))
