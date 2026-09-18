"""Import verified CS2 casing meshes, textures and clip/attachment definitions.

First export weapons/models/shared/shells/*.vmdl_c using Source2Viewer CLI with
--gltf_export_format glb --gltf_export_materials. No generated substitute geometry.
Runtime uses the same metres as VRF glTF; attachment offsets remain Source inches.
"""
import argparse, hashlib, json, re, shutil
from pathlib import Path
import numpy as np
from PIL import Image
from cs2_glb import Glb
import cs2_kv3
from cs2_effects import GUNS

ROOT=Path(__file__).resolve().parent.parent
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    ap=argparse.ArgumentParser();ap.add_argument('--analysis',type=Path,required=True);ap.add_argument('--models',type=Path,required=True);a=ap.parse_args()
    source=ROOT/'src/ScCsgoKnives';assets=source/'Assets';models={};cues={};evidence=[]
    def model(particle):
        definition=a.analysis/'06_particles/definitions'/particle
        d=cs2_kv3.load(definition)
        paths=[x['m_model'] for r in d['m_Renderers'] for x in r.get('m_modelList',[]) if 'm_model' in x]
        if not paths: paths=re.findall(r'm_model = resource:"([^"]+)"',definition.read_text())
        assert len(set(paths))==1,particle
        path=paths[0];key=Path(path).stem
        if key not in models:
            glb=a.models/Path(path).with_suffix('.glb');g=Glb(glb);out=[];base=1;faces=0
            for ni,node in enumerate(g.nodes):
                if 'mesh' not in node:continue
                transform=g.world_matrix(ni)
                for p in g.meshes()[node['mesh']].primitives:
                    vertices=p.attributes['POSITION'];pos=np.c_[vertices,np.ones(len(vertices))]@transform
                    normal=p.attributes['NORMAL']@transform[:3,:3];uv=p.attributes['TEXCOORD_0']
                    out.append('o casing')
                    out += ['v '+' '.join(map(str,v[:3])) for v in pos]
                    # SCAPI consumes glTF UVs directly (no OBJ-style V flip).
                    out += ['vt '+str(v[0])+' '+str(v[1]) for v in uv]
                    out += ['vn '+' '.join(map(str,v)) for v in normal]
                    for face in p.indices.reshape(-1,3):out.append('f '+' '.join(f'{int(i)+base}/{int(i)+base}/{int(i)+base}' for i in face));faces+=1
                    base+=len(vertices)
            target=assets/'Models/ScCsgoKnives'/('casing_'+key+'.obj');target.write_text('\n'.join(out)+'\n')
            mats=g.json['materials'];assert len(mats)==1
            texture=g.json['textures'][mats[0]['pbrMetallicRoughness']['baseColorTexture']['index']]['source']
            image=glb.parent/g.json['images'][texture]['uri'];dest=assets/'Textures/ScCsgoKnives'/('casing_'+key+'.png')
            # Like C4, Source material alpha is not physical holes in an opaque casing.
            Image.open(image).convert('RGB').save(dest)
            models[key]={'Model':'casing_'+key,'Texture':'casing_'+key}
            evidence.append(dict(model=path,exportSha256=sha(glb),textureSource=image.name,textureSha256=sha(image),triangles=faces,objSha256=sha(target)))
        emitter=d['m_Emitters'][0];delay=emitter.get('m_flStartTime',{}).get('m_flLiteralValue',0)
        init=next(x for x in d['m_Initializers'] if 'm_LocalCoordinateSystemSpeedMin' in x)
        lo=init['m_LocalCoordinateSystemSpeedMin']['m_vLiteralValue'];hi=init['m_LocalCoordinateSystemSpeedMax']['m_vLiteralValue']
        return key,delay,lo,hi
    for gun,(stem,_) in GUNS.items():
        animation=json.loads((source/'AnimationData'/f'{gun}.cs2.animation.json').read_text())
        text=(a.analysis/'02_models/events_full'/f'{stem}.analysis.txt').read_text()
        for name,clip in animation['Clips'].items():
            relative=Path(clip['SourceFile']).with_suffix('.vnmclip')
            path=a.analysis/relative if relative.parts[0]=='08_first_person' else a.analysis/'08_first_person'/relative
            if not path.exists():continue
            d=cs2_kv3.load(path)
            for track in d.get('m_eventTracks',[]):
                for event in track.get('m_events',[]):
                    particle=event.get('m_particleSystem','')
                    if 'weapon_shell_casing_' not in particle:continue
                    attachment=event.get('m_attachmentPoint0') or event.get('m_config')
                    match=re.search(r'key = "'+re.escape(attachment)+r'"(.*?m_nInfluences = 1)',text,re.S)
                    if not match:raise ValueError((gun,attachment))
                    block=match[1]
                    bone=re.search(r'm_influenceNames\s*=\s*\[\s*"([^"]+)"',block)[1]
                    def vector(key):return [float(x) for x in re.search(key+r'\s*=\s*\[\s*\[([^]]+)',block)[1].split(',')]
                    mesh,delay,lo,hi=model(particle)
                    cues.setdefault(gun+':'+name,[]).append(dict(Model=mesh,At=round(event['m_flStartTime']/clip['FrameRate']+delay,6),Bone=bone,
                        Offset=vector('m_vInfluenceOffsets'),Rotation=vector('m_vInfluenceRotations'),SpeedMin=lo,SpeedMax=hi,Attachment=attachment))
    data=dict(Models=models,Cues=cues)
    (source/'AnimationData/cs2_casings.json').write_text(json.dumps(data,indent=2)+'\n')
    for kind,file in [('metal','bullet_casing_01.wav'),('shotgun','shotgun_shell1.wav')]:
        path=a.analysis/'05_audio/decoded/sounds/weapons/fx/tink'/file
        shutil.copyfile(path,assets/'Audio/ScCsgoKnives'/f'casing_{kind}.wav')
    (ROOT/'docs/cs2-casing-sources-2026-09-18.json').write_text(json.dumps(dict(meshes=evidence,clips=len(cues),cues=sum(map(len,cues.values())),notes='VRF metres; original source attachments and event frames. Runtime bounded debris approximates Source 2 physics.'),indent=2)+'\n')
    print('Models',len(models),'clips',len(cues),'guns',len(set(k.split(':')[0] for k in cues)))
if __name__=='__main__':main()
