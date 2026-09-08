#!/usr/bin/env python3
"""Import Valve's actual StatTrak module (VRF GLB + decompiled material textures).

No replacement mesh/font. glTF metres -> Source rig inches (Z,X,Y), no gun
normalization. The display retains its encoded UVs: integer U=1..6 identifies
left-to-right digits, fractional U identifies the original atlas column.
Atlas row 0: column 0=unlit, columns 1..10=digits 0..9.
"""
import argparse
import hashlib
import json
from pathlib import Path
import numpy as np
from PIL import Image
from cs2_glb import Glb
from cs2_glb_to_obj import write_obj, to_normalized_dir

ROOT = Path(__file__).resolve().parents[1]

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--glb', type=Path, required=True)
    ap.add_argument('--materials', type=Path, required=True)
    args = ap.parse_args()
    target = ROOT / 'src/ScCsgoKnives'
    models = target / 'Assets/Models/ScCsgoKnives'
    textures = target / 'Assets/Textures/ScCsgoKnives'
    parts = []
    for primitive in Glb(args.glb).meshes()[0].primitives:
        indices = primitive.indices.reshape(-1, 3)
        used, remapped = np.unique(indices, return_inverse=True)
        pos = primitive.attributes['POSITION'][used][:, [2, 0, 1]] * 39.370079
        uv = primitive.attributes['TEXCOORD_0'][used]
        normals = to_normalized_dir(primitive.attributes['NORMAL'][used])
        indices = remapped.reshape(-1, 3)
        name = primitive.material
        if name == 'stattrak_module':
            write_obj(models / 'stattrak_module.obj', name, pos, uv, normals, indices)
        elif name != 'stattrak_module_display':
            raise ValueError('Unexpected module material: ' + name)
        parts.append(dict(Name=name, Positions=pos.round(7).reshape(-1).tolist(),
                          Uvs=uv.reshape(-1).tolist(), Indices=indices.reshape(-1).tolist()))
    assert sorted(p['Name'] for p in parts) == ['stattrak_module', 'stattrak_module_display']
    (target / 'AnimationData/stattrak_mesh.json').write_text(json.dumps(parts, separators=(',', ':')), encoding='utf-8')
    inputs = [args.glb]
    for src, dst in [('stattrak_module_color.png', 'stattrak_module.png'),
                     ('stattrak_digit_atlas.png', 'stattrak_digit_atlas.png')]:
        path = args.materials / src
        inputs.append(path)
        # Source alpha is packed material data, not gun transparency.
        Image.open(path).convert('RGB').save(textures / dst)
    channels = []
    for name in ['stattrak_module_ao.png', 'stattrak_module_rough.png', 'stattrak_module_9f02384d_metal.png']:
        path = args.materials / name
        inputs.append(path)
        channels.append(Image.open(path).convert('RGB').getchannel('R'))
    assert len(set(c.size for c in channels)) == 1
    Image.merge('RGB', channels).save(textures / 'stattrak_module_orm.png')
    Image.new('RGB', channels[0].size, (128, 128, 255)).save(textures / 'stattrak_module_normal.png')
    report = dict(Source='weapons/models/shared/stattrak/stattrak_module.vmdl_c',
                  Units='Source inches, glTF ZXY axes',
                  Inputs={p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs},
                  Parts=[dict(Name=p['Name'], Vertices=len(p['Positions'])//3, Triangles=len(p['Indices'])//3) for p in parts],
                  Display='6 encoded UV slots; atlas 16 columns; row 0 lit digit n is column n+1',
                  Limitation='Original maps retained; Source 2 material brightness/contrast and HDR self-illumination are not its exact shader.')
    (ROOT / 'docs/stattrak-module-source-0401.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps(report, indent=2))

if __name__ == '__main__':
    main()
