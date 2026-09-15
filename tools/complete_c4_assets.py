"""Complete C4 body + screen and preserve CS2 world poses/material events."""
import copy,json,re,struct,sys
from pathlib import Path
import numpy as np
from PIL import Image
from cs2_glb import Glb
from cs2_glb_to_skinned import convert
ROOT=Path(__file__).resolve().parents[1]
SRC=ROOT.parent/'CSMCReverse/local_cs2_analysis/all_weapons/13_c4'
DATA=ROOT/'src/ScCsgoKnives/AnimationData'
TEX=ROOT/'src/ScCsgoKnives/Assets/Textures/ScCsgoKnives'
g=Glb(SRC/'glb/weapon_c4.glb')
assert g.mesh_skin(0)['joints']==g.mesh_skin(1)['joints']
assert np.array_equal(g.mesh_skin(0)['inverse_bind'],g.mesh_skin(1)['inverse_bind'])
original=copy.deepcopy(g.json)
g.json['meshes'][0]['primitives']+=g.json['meshes'][1]['primitives']
j=json.dumps(g.json,separators=(',',':')).encode();j+=b' '*((-len(j))%4)
b=g.bin;b+=b'\0'*((-len(b))%4)
target=SRC/'glb/weapon_c4_complete.glb'
target.write_bytes(struct.pack('<4sII',b'glTF',2,28+len(j)+len(b))+struct.pack('<II',len(j),0x4e4f534a)+j+struct.pack('<II',len(b),0x004e4942)+b)
blob,joints,stats=convert(target);(DATA/'c4.cs2.skin').write_bytes(blob)
poses={}
for a in original['animations']:
    if a['name'] not in ('planted','dropped'):continue
    g.json=copy.deepcopy(original)
    for c in a['channels']:
        sample=a['samplers'][c['sampler']]
        assert len(g.accessor(sample['input']))==1
        g.nodes[c['target']['node']][c['target']['path']]=g.accessor(sample['output'])[0].tolist()
    poses[a['name']]={}
    for node in g.mesh_skin(0)['joint_nodes']:
        m=g.world_matrix(node);m[3,:3]*=39.370079
        poses[a['name']][g.node_name(node)]=m.reshape(-1).tolist()
events=[]
text=(SRC/'decompiled/animation/anims/viewmodel/equipment/c4/plant_c4.vnmclip').read_text()
for part in re.split(r'_class\s*=\s*"CNmClipDocEvent_MaterialAttribute"',text)[1:]:
    part=part.split('m_eventClassName')[0]
    start=float(re.search(r'm_flStartTime\s*=\s*([\d.]+)',part)[1])/30
    duration=float(re.search(r'm_flDuration\s*=\s*([\d.]+)',part)[1])/30
    if re.search(r'm_attributeName\s*=\s*"([^"]+)"',part)[1]!='c4_digits':continue
    values=[float(v) for v in re.findall(r'^\s*y\s*=\s*([-\d.]+)',part,re.M)]
    assert values and max(values)==min(values), 'Unexpected animated curve; do not flatten'
    events.append(dict(At=start,Duration=duration,Row=values[0]))
(DATA/'c4_visuals.json').write_text(json.dumps(dict(WorldPoses=poses,Digits=events),indent=2),encoding='utf-8')
Image.open(SRC/'decompiled/weapons/models/c4/materials/c4_digits.png').convert('RGBA').save(TEX/'weapon_c4_digits.png')
Image.new('RGB',(4,4),(128,128,255)).save(TEX/'weapon_c4_digits_normal.png')
Image.new('RGB',(4,4),(255,32,0)).save(TEX/'weapon_c4_digits_orm.png')
print(stats);print(events)
