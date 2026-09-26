"""Remove runtime glTF animation conversion after native samples have been baked.

Preserve every node, mesh, skin, material and original-resolution image. Only
animation accessors and their now-unreferenced binary data leave the GLB.
The one tiny marker instructs CS's NPC/player/preview code to attach the cache.
"""
import argparse
import copy
import hashlib
import json
import struct
from pathlib import Path
import numpy as np
from cs2_glb import Glb


def strip(source: Path, target: Path, role: str):
    original = Glb(source)
    doc = copy.deepcopy(original.json)
    old = original.json
    assert role in ('ct', 't') and len(doc['buffers']) == 1
    assert all('sparse' not in a for a in doc['accessors'])
    assert not doc.get('extensions'), 'Audit root extensions before stripping accessors'
    accessors, views, blob = [], [], bytearray()
    accessor_map, view_map = {}, {}

    def view(index):
        if index not in view_map:
            item = copy.deepcopy(old['bufferViews'][index])
            assert item['buffer'] == 0 and not item.get('extensions')
            offset = item.get('byteOffset', 0)
            raw = original.bin[offset:offset+item['byteLength']]
            assert len(raw) == item['byteLength']
            blob.extend(b'\0' * (-len(blob) % 4))
            item['byteOffset'] = len(blob)
            blob.extend(raw)
            view_map[index] = len(views)
            views.append(item)
        return view_map[index]

    def accessor(index):
        if index not in accessor_map:
            item = copy.deepcopy(old['accessors'][index])
            assert not item.get('extensions')
            item['bufferView'] = view(item['bufferView'])
            accessor_map[index] = len(accessors)
            accessors.append(item)
        return accessor_map[index]

    for mesh in doc['meshes']:
        for primitive in mesh['primitives']:
            assert not primitive.get('extensions')
            primitive['attributes'] = {key: accessor(value) for key, value in primitive['attributes'].items()}
            if 'indices' in primitive:
                primitive['indices'] = accessor(primitive['indices'])
            for morph in primitive.get('targets', []):
                for key, value in list(morph.items()):
                    morph[key] = accessor(value)
    for skin in doc.get('skins', []):
        if 'inverseBindMatrices' in skin:
            skin['inverseBindMatrices'] = accessor(skin['inverseBindMatrices'])
    for image in doc.get('images', []):
        assert 'bufferView' in image, 'Expected embedded original-resolution images'
        image['bufferView'] = view(image['bufferView'])

    def marker_accessor(values, kind):
        blob.extend(b'\0' * (-len(blob) % 4))
        raw = struct.pack('<' + 'f' * len(values), *values)
        vi = len(views)
        views.append(dict(buffer=0, byteOffset=len(blob), byteLength=len(raw)))
        blob.extend(raw)
        ai = len(accessors)
        item = dict(bufferView=vi, componentType=5126, count=1, type=kind)
        if kind == 'SCALAR':
            item.update(min=[0], max=[0])
        accessors.append(item)
        return ai

    marker_time = marker_accessor([0], 'SCALAR')
    marker_value = marker_accessor([0, 0, 0], 'VEC3')
    doc['animations'] = [dict(name=f'__sc_prebaked_{role}_v1',
        samplers=[dict(input=marker_time, output=marker_value, interpolation='LINEAR')],
        channels=[dict(sampler=0, target=dict(node=0, path='translation'))])]
    doc['accessors'], doc['bufferViews'] = accessors, views
    blob.extend(b'\0' * (-len(blob) % 4))
    doc['buffers'] = [dict(byteLength=len(blob))]
    header = json.dumps(doc, ensure_ascii=False, separators=(',', ':')).encode('utf-8')
    header += b' ' * (-len(header) % 4)
    data = struct.pack('<4sII', b'glTF', 2, 28+len(header)+len(blob))
    data += struct.pack('<II', len(header), 0x4e4f534a) + header
    data += struct.pack('<II', len(blob), 0x004e4942) + blob
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(data)
    derived = Glb(target)
    for index, new_index in accessor_map.items():
        assert np.array_equal(original.accessor(index), derived.accessor(new_index)), index
    for index, new_index in view_map.items():
        a, b = old['bufferViews'][index], doc['bufferViews'][new_index]
        assert original.bin[a.get('byteOffset', 0):a.get('byteOffset', 0)+a['byteLength']] == \
            derived.bin[b['byteOffset']:b['byteOffset']+b['byteLength']]
    for key in ('nodes', 'materials', 'textures', 'samplers', 'scenes', 'scene'):
        assert old.get(key) == doc.get(key), key
    return dict(role=role, sourceSha256=hashlib.sha256(source.read_bytes()).hexdigest(),
        sha256=hashlib.sha256(data).hexdigest(), bytesBefore=source.stat().st_size, bytesAfter=len(data),
        clipsBaked=len(old['animations']), animationChannels=sum(len(a['channels']) for a in old['animations']),
        preservedAccessors=len(accessor_map), preservedBufferViews=len(view_map),
        geometrySkinMaterialsAndFullResolutionImagesExact=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('target', type=Path)
    parser.add_argument('role', choices=['ct', 't'])
    parser.add_argument('report', type=Path)
    args = parser.parse_args()
    result = strip(args.source, args.target, args.role)
    args.report.write_text(json.dumps(result, ensure_ascii=False, indent=2)+'\n', 'utf-8')
    print(json.dumps(result))
