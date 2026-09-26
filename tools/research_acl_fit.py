"""Audit actual timing/track layouts against ACL's documented uniform input.
This is a suitability audit, not an ACL compressor run or predicted ACL ratio.
"""
import json
from research_resource_codecs import STAGE,actor_spans,sha
import numpy as np
rows=json.loads((STAGE/'inventory.json').read_bytes());actors=[];weapon=dict(files=0,clips=0,curves=0,keys=0,nonUniformCurves=0,events=0,additiveClips=[])
for row in rows:
    data=(STAGE/row['path']).read_bytes();assert sha(data)==row['sha256']
    if row['group']=='actor-animation':
        spans,clips=actor_spans(data);details=[]
        for clip in clips:
            times=np.frombuffer(data,dtype='<f4',count=clip['keys'],offset=clip['timeOffset']).astype(float)
            grid=np.linspace(times[0],times[-1],len(times))
            details.append(dict(name=clip['name'],keys=clip['keys'],channels=len(clip['channels']),duration=clip['duration'],
                lastTime=float(times[-1]),maxUniformGridDeviation=float(np.abs(times-grid).max()),
                channelProperties=sorted({c['property'] for c in clip['channels']})))
        actors.append(dict(name=row['name'],clips=len(clips),details=details))
    elif row['group']=='weapon-animation':
        weapon['files']+=1
        for name,clip in json.loads(data).get('Clips',{}).items():
            weapon['clips']+=1;weapon['events']+=len(clip.get('Events') or [])
            if clip.get('Additive'):weapon['additiveClips'].append(dict(file=row['name'],clip=name,kind=clip.get('Additive'),base=clip.get('AdditiveBase'),over=clip.get('AdditiveOver')))
            for bone in clip.get('Bones',{}).values():
                for curve in bone.values():
                    if not curve or 'Times' not in curve:continue
                    times=np.array(curve['Times'],dtype=np.float32).astype(float);weapon['curves']+=1;weapon['keys']+=len(times)
                    if len(times)>2 and np.max(np.abs(times-np.linspace(times[0],times[-1],len(times))))>max(1e-6,abs(times[-1])*1e-6):weapon['nonUniformCurves']+=1
report=dict(actors=actors,weapon=weapon,aclExecuted=False,scope='Schema/timing audit using actual resources and pinned ACL documentation. No ACL ratio, pose accuracy or mobile performance claim.')
(STAGE/'acl-fit.json').write_text(json.dumps(report,indent=2),'utf8')
print(json.dumps(dict(weapon=weapon,actors=[dict(name=a['name'],clips=a['clips'],maxGridDeviation=max(c['maxUniformGridDeviation'] for c in a['details'])) for a in actors]),indent=2))
