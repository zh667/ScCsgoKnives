"""Install every converted CS knife into the mod assets.

Keeps variants 0/1/2 as karambit/m9/butterfly so existing saves keep their
items, then appends the rest in CS:GO's own order.
"""
import json, os, shutil, subprocess, sys, glob
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from optimize_obj import optimize

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, '.tmp-csmc/out')
ASSETS = os.path.join(ROOT, 'src/ScCsgoKnives/Assets')
MODELS = os.path.join(ASSETS, 'Models/ScCsgoKnives')
TEX = os.path.join(ASSETS, 'Textures/ScCsgoKnives')
ANIM = os.path.join(ROOT, 'src/ScCsgoKnives/AnimationData')
TEXTURE_SIZE = 512

# Clips KnifeAnimationController can actually start. The converter exports every
# animation in the record (18-22 of them); keeping the unused ones cost ~1.8 MB
# of embedded JSON per knife, which is parsed at start-up.
USED_CLIPS = {'deploy', 'deploy2', 'idle', 'idle2', 'inspect', 'inspect2', 'inspect3',
              'slash1', 'slash2'}
PRECISION = 6

def trim(node):
    """Round every float in the document; curve keys carry 7+ digits of noise."""
    if isinstance(node, float):
        return round(node, PRECISION)
    if isinstance(node, list):
        return [trim(x) for x in node]
    if isinstance(node, dict):
        return {k: trim(v) for k, v in node.items()}
    return node

ICONS = 'overrides/gec_texture_stream/icons_128/base_weapons/{}.webp'
CLIENT = '/home/dev/workspaces/reference/CSMCClient20260822.zip'

# variant order -> (csmc mesh record, short asset name, official icon name).
# Variants 0-2 must stay put so items already in a save keep their identity.
KNIVES = [
    ('knife_karambit', 'karambit', 'weapon_knife_karambit'),
    ('knife_m9', 'm9', 'weapon_knife_m9_bayonet'),
    ('knife_butterfly', 'butterfly', 'weapon_knife_butterfly'),
    ('knife_bayonet', 'bayonet', 'weapon_bayonet'),
    ('knife_bowie', 'bowie', 'weapon_knife_survival_bowie'),
    ('knife_canis', 'canis', 'weapon_knife_canis'),
    ('knife_cord', 'cord', 'weapon_knife_cord'),
    ('knife_css', 'css', 'weapon_knife_css'),
    ('knife_default_ct', 'default_ct', 'weapon_knife'),
    ('knife_default_t', 'default_t', 'weapon_knife_t'),
    ('knife_falchion', 'falchion', 'weapon_knife_falchion'),
    ('knife_flip', 'flip', 'weapon_knife_flip'),
    ('knife_gut', 'gut', 'weapon_knife_gut'),
    ('knife_kukri', 'kukri', 'weapon_knife_kukri'),
    ('knife_navaja', 'navaja', 'weapon_knife_gypsy_jackknife'),
    ('knife_outdoor', 'outdoor', 'weapon_knife_outdoor'),
    ('knife_push', 'push', 'weapon_knife_push'),
    ('knife_skeleton', 'skeleton', 'weapon_knife_skeleton'),
    ('knife_stiletto', 'stiletto', 'weapon_knife_stiletto'),
    ('knife_tactical', 'tactical', 'weapon_knife_tactical'),
    ('knife_talon', 'talon', 'weapon_knife_widowmaker'),
    ('knife_ursus', 'ursus', 'weapon_knife_ursus'),
]

def main():
    import io, zipfile
    from PIL import Image
    client = zipfile.ZipFile(CLIENT)
    os.makedirs(MODELS, exist_ok=True); os.makedirs(TEX, exist_ok=True)
    for f in glob.glob(os.path.join(MODELS, '*.obj')):
        if not f.endswith('player_arm.obj'):
            os.remove(f)
    summary = []
    for record, name, icon in KNIVES:
        doc = json.load(open(os.path.join(SRC, f'{record}.animation.json')))
        # The falchion ships two 4-triangle "__mixed" stubs with no binding of
        # their own; they are placeholders, not geometry.
        bound = {b['Name'] for b in doc['Bindings']}
        doc['MeshParts'] = [p for p in doc['MeshParts'] if p in bound]
        for part in doc['MeshParts']:
            src = os.path.join(SRC, 'parts', record, f'{part}.obj')
            dst = os.path.join(MODELS, f'{name}_{part}.obj')
            optimize(src, dst)
        doc['Clips'] = {k: v for k, v in doc['Clips'].items() if k in USED_CLIPS}
        json.dump(trim(doc), open(os.path.join(ANIM, f'{name}.csmc.animation.json'), 'w'),
                  separators=(',', ':'))
        im = Image.open(os.path.join(ROOT, '.tmp-csmc/assets', f'{record}.color.webp')).convert('RGBA')  # SC's known-good texture format
        im.resize((TEXTURE_SIZE, TEXTURE_SIZE), Image.LANCZOS).save(
            os.path.join(TEX, f'{name}.png'), optimize=True)
        slot = Image.open(io.BytesIO(client.read(ICONS.format(icon)))).convert('RGBA')
        if slot.size != (128, 128):
            slot = slot.resize((128, 128), Image.LANCZOS)
        slot.save(os.path.join(TEX, f'{name}_slot.png'), optimize=True)
        summary.append((name, doc['MeshParts'], doc['SourceReferenceScale']))
    import validate_obj
    bad = [f for f in sorted(glob.glob(os.path.join(MODELS, '*.obj')))
           if validate_obj.validate(f)[0]]
    if bad:
        raise SystemExit('OBJ files rejected by the Survivalcraft parser rules:\n  '
                         + '\n  '.join(bad))
    obj = sum(os.path.getsize(f) for f in glob.glob(os.path.join(MODELS, '*.obj')))
    tex = sum(os.path.getsize(f) for f in glob.glob(os.path.join(TEX, '*.png')))
    print(f"installed {len(summary)} knives   models {obj/1e6:.1f} MB   textures {tex/1e6:.1f} MB")
    json.dump([{'name': n, 'parts': p, 'sourceScale': s} for n, p, s in summary],
              open(os.path.join(ROOT, '.tmp-csmc/installed.json'), 'w'), indent=1)
    # Small manifest so the block and the renderer can lay out every variant
    # without deserialising 20 MB of animation curves at start-up.
    json.dump([{'Name': n, 'MeshParts': p, 'SourceReferenceScale': round(s, 5)}
               for n, p, s in summary],
              open(os.path.join(ANIM, 'knives.json'), 'w'), separators=(',', ':'))

if __name__ == '__main__':
    main()
