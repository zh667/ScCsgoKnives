"""Derive tactical Lite resources; preserve code, mesh, skin and animation bytes.

External textures follow the core 512/WebP policy. Embedded GLB images remain PNG
for native glTF compatibility. Every non-image bufferView is checked byte-for-byte.
Run with dev.ps1 after pack_tactical.py; original assets are never edited.
"""
import argparse
import copy
import io
import json
from pathlib import Path
import struct
import zipfile
import numpy as np
from PIL import Image
import optimize_weapon_resources as opt


def glb(data, name):
    magic, version, size = struct.unpack_from('<III', data)
    assert magic == 0x46546C67 and version == 2 and size == len(data)
    length, kind = struct.unpack_from('<II', data, 12)
    assert kind == 0x4E4F534A
    original = json.loads(data[20:20+length])
    start = 20+length
    size, kind = struct.unpack_from('<II', data, start)
    assert kind == 0x004E4942 and start+8+size == len(data)
    binary = data[start+8:]
    doc = copy.deepcopy(original)
    images = {image['bufferView']: i for i, image in enumerate(doc.get('images', [])) if 'bufferView' in image}
    normals = {doc['textures'][m['normalTexture']['index']]['source'] for m in doc.get('materials', []) if 'normalTexture' in m}
    result = bytearray()
    image_rows = []
    for index, view in enumerate(doc['bufferViews']):
        assert view.get('buffer', 0) == 0
        offset = view.get('byteOffset', 0)
        raw = binary[offset:offset+view['byteLength']]
        converted = raw
        if index in images:
            image = Image.open(io.BytesIO(raw)); image.load()
            old = image.size
            if max(old) > 512:
                image = image.resize(tuple(max(1, round(x*512/max(old))) for x in old), Image.Resampling.LANCZOS)
                if images[index] in normals:
                    pixels = np.array(image.convert('RGBA'))
                    v = pixels[:,:,:3].astype(np.float32)/127.5-1
                    norms = np.linalg.norm(v, axis=2, keepdims=True)
                    v /= np.maximum(norms, 1e-6); v[norms[:,:,0]<1e-6] = (0,0,1)
                    pixels[:,:,:3] = np.clip(np.rint((v+1)*127.5),0,255).astype(np.uint8)
                    image = Image.fromarray(pixels)
                output = io.BytesIO(); image.save(output, format='PNG', optimize=True)
                converted = output.getvalue()
                doc['images'][images[index]]['mimeType'] = 'image/png'
            image_rows.append(dict(index=images[index], fromSize=old, toSize=image.size,
                sourceSha256=opt.sha(raw), sha256=opt.sha(converted)))
        result.extend(b'\0'*(-len(result)%4))
        view['byteOffset'] = len(result); view['byteLength'] = len(converted)
        result.extend(converted)
        if index not in images:
            assert bytes(result[view['byteOffset']:view['byteOffset']+view['byteLength']]) == raw
    doc['buffers'][0]['byteLength'] = len(result)
    for key in ('accessors','animations','skins','nodes','meshes','scenes'):
        assert doc.get(key) == original.get(key), key
    header = json.dumps(doc, separators=(',',':'), ensure_ascii=False).encode('utf8')
    header += b' '*(-len(header)%4); result.extend(b'\0'*(-len(result)%4))
    converted = struct.pack('<III',magic,2,28+len(header)+len(result)) + struct.pack('<II',len(header),0x4E4F534A)+header+struct.pack('<II',len(result),0x004E4942)+result
    return converted, dict(path=name, kind='glb-images', sourceSha256=opt.sha(data),sha256=opt.sha(converted),
        bytesBefore=len(data),bytesAfter=len(converted),images=image_rows,
        unchangedNonImageViews=len(doc['bufferViews'])-len(images),animations=len(doc.get('animations',[])))


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--report',type=Path,required=True)
    args=parser.parse_args()
    entries={}
    opt.rows.clear()
    with zipfile.ZipFile(args.source) as archive:
        assert archive.testzip() is None
        for item in archive.infolist():
            name=item.filename; data=archive.read(name)
            if name.endswith('.png'):
                data=opt.texture(name,data); name=name[:-4]+'.webp'
            elif name.endswith('.glb'):
                data,row=glb(data,name);opt.rows.append(row)
            entries[name]=data
        for name in ('ScCsgoTactical.dll','Integrations/ScCsgoAppearance.bin'):
            assert entries[name] == archive.read(name)
    meta=json.loads(entries['modinfo.json'])
    meta['Name'] += ' · 512轻量版'
    meta['Description'] += ' 轻量资源：独立贴图512上限WebP，人物内嵌贴图512上限PNG；保留全部骨骼、网格及动画。与全量战术拓展二选一。'
    entries['modinfo.json']=(json.dumps(meta,ensure_ascii=False,indent=2)+'\n').encode('utf8')
    entries['Assets/ScCsgoTacticalDerivedResources.json']=json.dumps(opt.rows,ensure_ascii=False,indent=2).encode('utf8')
    pending=args.output.with_suffix('.pending')
    with zipfile.ZipFile(pending,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as archive:
        for name,data in sorted(entries.items()):
            item=zipfile.ZipInfo(name,(2026,1,1,0,0,0));item.compress_type=zipfile.ZIP_DEFLATED
            archive.writestr(item,data,compresslevel=9)
    pending.replace(args.output)
    report=dict(path=args.output.name,bytes=args.output.stat().st_size,sha256=opt.sha(args.output.read_bytes()),assets=opt.rows)
    args.report.parent.mkdir(parents=True,exist_ok=True)
    args.report.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n','utf8')
    print(json.dumps({k:v for k,v in report.items() if k!='assets'},ensure_ascii=False),flush=True)


if __name__=='__main__':main()
