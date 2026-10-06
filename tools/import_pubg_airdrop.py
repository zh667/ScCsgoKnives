"""Convert the user-supplied Rex PUBG Mobile crate, preserving source texture bytes.

Requires ufbx==0.0.5 (task-local install) and numpy/Pillow. Original FBX/normal
map stay under CSMCReverse; this static SC mesh renderer uses the color map.
No smoke asset is present in this download.
"""
import hashlib
import json
import shutil
import struct
import sys
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / '.tmp/dev-temp/pubg-airdrop-20261006/python'))
import ufbx

SOURCE = Path('E:/projects/CSMCReverse/references/pubg-airdrop-rex-ed80ad57')
ASSETS = ROOT / 'src/ScCsgoTactical/Assets'
def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    scene = ufbx.load_file(str(SOURCE/'original/source/DROP.fbx'),
                          target_axes=ufbx.axes_right_handed_y_up, target_unit_meters=1.0)
    meshes = scene.meshes
    assert len(meshes) == 1, 'Unexpected source mesh count'
    mesh = meshes[0]
    instances = mesh.instances
    assert len(instances) == 1
    node = instances[0]
    matrix = np.array(node.geometry_to_world, dtype=float)
    # Keep attribute wrappers alive while copying their indexed values.
    position = mesh.vertex_position
    normal = mesh.vertex_normal
    texcoord = mesh.vertex_uv
    assert position.exists and normal.exists and texcoord.exists
    positions = np.array(list(position.values))[list(position.indices)]
    normals = np.array(list(normal.values))[list(normal.indices)]
    uv = np.array(list(texcoord.values))[list(texcoord.indices)]
    positions = positions @ matrix[:3] + matrix[3]
    normals = normals @ np.linalg.inv(matrix[:3]).T
    normals /= np.maximum(np.linalg.norm(normals, axis=1, keepdims=True), 1e-8)
    uv[:, 1] = 1 - uv[:, 1]  # FBX bottom-origin UV to glTF top-origin UV.
    triangles = []
    # Preserve all source faces. Ear clipping also handles non-convex source quads.
    for face in mesh.faces:
        polygon = list(range(face.index_begin, face.index_begin + face.num_indices))
        assert len(polygon) in (3, 4), 'Source format changed'
        if len(polygon) == 4:
            p = positions[polygon]
            n = np.sum(np.cross(p, np.roll(p, -1, axis=0)), axis=0)
            axis = int(np.argmax(np.abs(n)))
            p2 = np.delete(p, axis, axis=1)
            turns = []
            for i in range(4):
                a, b = p2[(i+1)%4]-p2[i], p2[(i+2)%4]-p2[(i+1)%4]
                turns.append(a[0]*b[1]-a[1]*b[0])
            # Convex quads use source diagonal 0-2; concave quads use the reflex corner.
            sign = 1 if sum(turns) >= 0 else -1
            reflex = [((i+1)%4) for i,v in enumerate(turns) if v*sign < -1e-10]
            if reflex:
                assert len(reflex) == 1
                k = reflex[0]
                polygon = polygon[k:] + polygon[:k]
        for i in range(1, len(polygon)-1):
            triangles.extend([polygon[0], polygon[i], polygon[i+1]])
    assert len(triangles)//3 == mesh.num_triangles
    low, high = positions.min(0), positions.max(0)
    scale = .98 / max(high-low)
    center = (low+high)/2
    center[1] = low[1]
    positions = (positions-center)*scale
    assert len(positions) < 65536 and np.isfinite(positions).all()
    doc = dict(asset={'version':'2.0','generator':'ScCsgoKnives import_pubg_airdrop.py / ufbx 0.0.5'},
               scene=0,scenes=[{'nodes':[0]}],nodes=[{'mesh':0}],
               meshes=[{'primitives':[]}],bufferViews=[],accessors=[])
    binary = bytearray()
    def acc(data, kind, dtype, component):
        a = np.asarray(data,dtype=dtype)
        binary.extend(b'\0'*(-len(binary)%4))
        view = len(doc['bufferViews'])
        doc['bufferViews'].append(dict(buffer=0,byteOffset=len(binary),byteLength=a.nbytes))
        binary.extend(a.tobytes())
        item = dict(bufferView=view,componentType=component,count=len(a),type=kind)
        if kind == 'VEC3': item.update(min=a.min(0).tolist(),max=a.max(0).tolist())
        doc['accessors'].append(item)
        return len(doc['accessors'])-1
    doc['meshes'][0]['primitives'].append(dict(attributes={
        'POSITION':acc(positions,'VEC3','<f4',5126),
        'NORMAL':acc(normals,'VEC3','<f4',5126),
        'TEXCOORD_0':acc(uv,'VEC2','<f4',5126)},indices=acc(triangles,'SCALAR','<u4',5125)))
    doc['buffers']=[{'byteLength':len(binary)}]
    data=json.dumps(doc,separators=(',',':')).encode()
    data+=b' '*(-len(data)%4)
    model=ASSETS/'Models/ScCsgoTactical/airdrop.glb'
    model.write_bytes(struct.pack('<4sII',b'glTF',2,28+len(data)+len(binary))+
                      struct.pack('<II',len(data),0x4e4f534a)+data+
                      struct.pack('<II',len(binary),0x004e4942)+binary)
    texture=ASSETS/'Textures/ScCsgoTactical/airdrop.png'
    source_color=SOURCE/'original/textures/DROP_D.tga.png'
    shutil.copyfile(source_color,texture)
    assert sha(texture)==sha(source_color)
    result=dict(sourceUrl='https://sketchfab.com/3d-models/pubg-mobile-air-drop-ed80ad57edd94dce94fcde85c5a277fe',
                author='Rex (@rex_7)',license='CC BY 4.0',sourceArchive=str(SOURCE/'pubg-mobile-air-drop.zip'),
                archiveSha256=sha(SOURCE/'pubg-mobile-air-drop.zip'),
                sourceFbxSha256=sha(SOURCE/'original/source/DROP.fbx'),
                vertices=len(positions),triangles=len(triangles)//3,
                bounds=[positions.min(0).tolist(),positions.max(0).tolist()],
                colorPixels=Image.open(texture).size,
                normalMap='Original preserved; existing SC static-mesh renderer does not evaluate normal maps.',
                smoke='Not included in this source. User approved tinting the existing CS2 atlas red; source pixels remain unchanged.',
                outputs={str(p.relative_to(ROOT)):sha(p) for p in [model,texture]})
    (ROOT/'docs/tasks/pubg-airdrop-assets-20261006.json').write_text(json.dumps(result,indent=2)+'\n',encoding='utf8')
    print(json.dumps(result),flush=True)

if __name__=='__main__': main()
