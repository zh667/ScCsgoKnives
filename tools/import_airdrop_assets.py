"""Bake the installed CS2 supply crate and smoke frames without texture downsampling.

Inputs are Source2Viewer exports in the task stage; source VPK and exports stay untouched.
The smoke atlas preserves every pixel of all 64 decoded animation frames.
"""
import hashlib, json, shutil, struct, subprocess
from pathlib import Path
import numpy as np
from PIL import Image
from cs2_glb import Glb

ROOT = Path(__file__).resolve().parents[1]
STAGE = ROOT / '.tmp/dev-temp/subworld-airdrops-20261006'
OUT = ROOT / 'src/ScCsgoTactical/Assets'

def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()

def main():
    STAGE.mkdir(parents=True,exist_ok=True)
    vpk=Path('E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/game/csgo/pak01_dir.vpk')
    cli=ROOT/'.tmp/vrf-cli/Source2Viewer-CLI.exe'
    for name,output,extra in [
        ('models/generic/crate_supply_02/crate_supply_02.vmdl_c','cs2.glb',['--gltf_export_format','glb','--gltf_export_materials']),
        ('particles/maps/generic/smoke_vertical_large.vpcf_c','smoke_vertical_large.vpcf',[]),
        ('materials/particle/smoke/smokeburst/smokeloop_i_0_sc.vtex_c','smoke.vtex',[])]:
        result=subprocess.run([str(cli),'-i',str(vpk),'-o',str(STAGE/output),'-d','--vpk_filepath',name,*extra],capture_output=True,text=True,encoding='utf8',errors='replace')
        (STAGE/(output+'.log')).write_text(result.stdout+result.stderr,'utf8')
        if result.returncode:raise RuntimeError('Source2 export failed: '+name)
    source = STAGE / 'cs2.glb'
    g = Glb(source)
    parts = []
    for i, node in enumerate(g.nodes):
        if 'mesh' not in node: continue
        matrix = g.world_matrix(i)
        for primitive in g.json['meshes'][node['mesh']]['primitives']:
            a = {k: g.accessor(v) for k, v in primitive['attributes'].items()}
            pos = (np.c_[a['POSITION'], np.ones(len(a['POSITION']))] @ matrix)[:, :3]
            normal = a['NORMAL'] @ np.linalg.inv(matrix[:3, :3]).T
            normal /= np.maximum(np.linalg.norm(normal, axis=1, keepdims=True), 1e-8)
            parts.append((pos, normal, a['TEXCOORD_0'], g.accessor(primitive['indices']).ravel()))
    vertices = np.concatenate([p[0] for p in parts]); low = vertices.min(0); high = vertices.max(0)
    scale = .98 / max(high - low)
    center = (low + high) / 2
    center[1] = low[1]
    doc = dict(asset={'version': '2.0'}, scene=0, scenes=[{'nodes': [0]}], nodes=[{'mesh': 0}],
               meshes=[{'primitives': []}], bufferViews=[], accessors=[])
    binary = bytearray()
    def acc(data, kind, dtype, component):
        a = np.asarray(data, dtype=dtype); binary.extend(b'\0' * (-len(binary) % 4))
        index = len(doc['bufferViews'])
        doc['bufferViews'].append(dict(buffer=0, byteOffset=len(binary), byteLength=a.nbytes)); binary.extend(a.tobytes())
        item = dict(bufferView=index, componentType=component, count=len(a), type=kind)
        if kind == 'VEC3': item.update(min=a.min(0).tolist(), max=a.max(0).tolist())
        doc['accessors'].append(item); return len(doc['accessors']) - 1
    for pos, normal, uv, indices in parts:
        assert len(pos) < 65536 and indices.max() < len(pos)
        doc['meshes'][0]['primitives'].append(dict(attributes={
            'POSITION': acc((pos-center)*scale, 'VEC3', '<f4', 5126),
            'NORMAL': acc(normal, 'VEC3', '<f4', 5126), 'TEXCOORD_0': acc(uv, 'VEC2', '<f4', 5126)},
            indices=acc(indices, 'SCALAR', '<u4', 5125)))
    doc['buffers'] = [{'byteLength': len(binary)}]
    data = json.dumps(doc, separators=(',', ':')).encode(); data += b' ' * (-len(data) % 4)
    model = OUT / 'Models/ScCsgoTactical/airdrop.glb'
    model.write_bytes(struct.pack('<4sII', b'glTF', 2, 28+len(data)+len(binary)) + struct.pack('<II', len(data), 0x4e4f534a) + data + struct.pack('<II', len(binary), 0x004e4942) + binary)
    color = STAGE / 'crate_supply_02_color_tga_34931339.png'
    target_color = OUT / 'Textures/ScCsgoTactical/airdrop.png'
    shutil.copyfile(color, target_color)
    frames = [Image.open(STAGE / f'smokeloop_i_0_sc_seq0_{i}.png').convert('RGBA') for i in range(64)]
    # Source2 trims transparent frame margins. Pad, never stretch or downsample.
    width = height = max(max(f.size) for f in frames)
    atlas = Image.new('RGBA', (width*8,height*8))
    for i, frame in enumerate(frames): atlas.paste(frame, ((i%8)*width+(width-frame.width)//2,(i//8)*height+(height-frame.height)//2))
    smoke = OUT / 'Textures/ScCsgoTactical/airdrop_smoke.png'; atlas.save(smoke)
    result = dict(sourceModel='models/generic/crate_supply_02/crate_supply_02.vmdl_c',
        sourceEffect='particles/maps/generic/smoke_vertical_large.vpcf_c',
        sourceTexture='materials/particle/smoke/smokeburst/smokeloop_i_0_sc.vtex_c',
        vpkSha256=sha(vpk),
        sourceExportSha256=sha(source), vertices=len(vertices), triangles=sum(len(p[3])//3 for p in parts),
        colorPixels=Image.open(color).size, smokePixels=atlas.size,
        note='CS2 supply prop, not a verified Danger Zone airdrop model. Source2 particle textures and emission/lifetime values ported to the SC renderer; not the Source2 runtime.',
        outputs={str(p.relative_to(ROOT)):sha(p) for p in (model,target_color,smoke)})
    (ROOT / 'docs/tasks/airdrop-assets-20261006.json').write_text(json.dumps(result, indent=2)+'\n', 'utf8')
    print(json.dumps(result))

if __name__ == '__main__': main()
