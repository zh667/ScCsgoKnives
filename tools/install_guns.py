"""Install the converted CS guns (AK-47, M4A1-S, AWP) into the mod assets.

Inputs: .tmp-guns/out/<gun>.animation.json and <gun>-parts/*.obj from CsmcAssetConverter,
~/csmc-guns/tex/<gun>_native/*.webp (CS:MC's native material set: basecolor, roughness,
metalness, normal, ambient_occlusion), and the client zip for the inventory icon.

    python3 tools/install_guns.py
"""
import glob, io, json, os, sys, zipfile
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from optimize_obj import optimize
from install_knives import trim, ICONS, CLIENT
import validate_obj
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, '.tmp-guns/out')
TEXSRC = os.path.expanduser('~/csmc-guns/tex')
ASSETS = os.path.join(ROOT, 'src/ScCsgoKnives/Assets')
MODELS = os.path.join(ASSETS, 'Models/ScCsgoKnives')
TEX = os.path.join(ASSETS, 'Textures/ScCsgoKnives')
ANIM = os.path.join(ROOT, 'src/ScCsgoKnives/AnimationData')
TEXTURE_SIZE = 1024          # rifles fill far more of the frame than a knife
# (converter record, mod asset name, csmcmod item id for the icon, weapon_table key)
GUNS = [
    ('ak47', 'ak47', 'weapon_ak47', 'ak47'),
    ('m4a1_silencer', 'm4a1s', 'weapon_m4a1_silencer', 'm4a1_silencer'),
    ('awp', 'awp', 'weapon_awp', 'awp'),
]
USED_CLIPS = {'deploy', 'idle', 'inspect', 'inspectStart', 'inspectLoop', 'inspectEnd',
              'shoot1', 'shoot2', 'shoot3', 'shootSilenced', 'shootUnsilenced', 'reload', 'attach', 'detach'}

def grey(path, size):
    return np.asarray(Image.open(path).convert('L').resize((size, size), Image.LANCZOS))

def main():
    client = zipfile.ZipFile(CLIENT)
    summary = []
    for record, name, item, key in GUNS:
        doc = json.load(open(os.path.join(SRC, f'{record}.animation.json')))
        bound = {b['Name'] for b in doc['Bindings']}
        # Part names carry "__2" (second record with the same name) and "__c1"/"__c2"
        # (a record split at Survivalcraft's 21845-face limit); the bone is the prefix.
        parts = [part for part in doc['MeshParts'] if part.split('__')[0] in bound]
        doc['MeshParts'] = parts
        for part in parts:
            optimize(os.path.join(SRC, f'{record}-parts', f'{part}.obj'), os.path.join(MODELS, f'{name}_{part}.obj'))
        doc['Clips'] = {k: v for k, v in doc['Clips'].items() if k in USED_CLIPS}
        json.dump(trim(doc), open(os.path.join(ANIM, f'{name}.csmc.animation.json'), 'w'), separators=(',', ':'))
        tex = os.path.join(TEXSRC, f'{record}_native')
        base = Image.open(glob.glob(os.path.join(tex, '*_basecolor.webp'))[0]).convert('RGBA')
        base.resize((TEXTURE_SIZE, TEXTURE_SIZE), Image.LANCZOS).save(os.path.join(TEX, f'{name}.png'), optimize=True)
        ao = grey(glob.glob(os.path.join(tex, '*_ambient_occlusion.webp'))[0], TEXTURE_SIZE)
        rough = grey(glob.glob(os.path.join(tex, '*_roughness.webp'))[0], TEXTURE_SIZE)
        metal = grey(glob.glob(os.path.join(tex, '*_metalness.webp'))[0], TEXTURE_SIZE)
        Image.fromarray(np.stack([ao, rough, metal], axis=-1), 'RGB').save(os.path.join(TEX, f'{name}_orm.png'), optimize=True)
        normal = Image.open(glob.glob(os.path.join(tex, '*_normal.webp'))[0]).convert('RGB')
        normal.resize((TEXTURE_SIZE, TEXTURE_SIZE), Image.LANCZOS).save(os.path.join(TEX, f'{name}_normal.png'), optimize=True)
        slot = Image.open(io.BytesIO(client.read(ICONS.format(item)))).convert('RGBA')
        if slot.size != (128, 128): slot = slot.resize((128, 128), Image.LANCZOS)
        slot.save(os.path.join(TEX, f'{name}_slot.png'), optimize=True)
        summary.append({'Name': name, 'MeshParts': parts, 'SourceReferenceScale': round(doc['SourceReferenceScale'], 5), 'Table': key})
        print(f"{name}: parts={parts} clips={sorted(doc['Clips'])} ref={doc['SourceReferenceScale']:.4f} tex {base.size} metal>127 {float((metal > 127).mean()):.2f} rough mean {int(rough.mean())}")
    bad = [f for f in sorted(glob.glob(os.path.join(MODELS, '*.obj'))) if validate_obj.validate(f)[0]]
    if bad: raise SystemExit('OBJ rejected by the Survivalcraft parser rules:\n  ' + '\n  '.join(bad))
    json.dump(summary, open(os.path.join(ANIM, 'guns.json'), 'w'), separators=(',', ':'))
    print('wrote', os.path.join(ANIM, 'guns.json'))

if __name__ == '__main__':
    main()
