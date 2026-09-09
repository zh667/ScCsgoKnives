"""Append official follow-up CZ reloads without rewriting existing shipped clips or gameplay data."""
import json
from pathlib import Path
import cs2_viewmodel as vm
from cs2_gun_rig import curve,r6,read_events
root=Path(__file__).resolve().parents[1]
vm.ROOTS=[root.parent/'CSMCReverse/local_cs2_analysis/all_weapons/08_first_person']
vm.ANALYSIS=vm.ROOTS[0];vm.ANIM=vm.ANALYSIS/'decompiled/animation'
path=root/'src/ScCsgoKnives/AnimationData/cz75a.cs2.animation.json'
doc=json.loads(path.read_text(encoding='utf8'))
for stem,alias in [('reload2_cz75a','reloadFollowup'),('reload2_empty_cz75a','reloadFollowupEmpty')]:
 source=vm.clip_path('pistol/pistol_cz75a',stem);c=vm.load_clip(source)
 if c.names != [b['Name'] for b in doc['Skeleton']]:raise ValueError('Skeleton mismatch')
 bones={}
 for b in c.bones:
  fields={}
  if b.orientation:fields['Rotation']=curve(*b.orientation,'q')
  if b.position:fields['Translation']=curve(*b.position,'v')
  if fields:bones[b.name]=fields
 doc['Clips'][stem]=dict(SourceName=stem,Alias=alias,SourceFile=vm.relative_to_root(source),FrameRate=r6(c.frame_rate),FrameCount=c.frame_count,Duration=r6(c.duration),Events=read_events(source.with_suffix('.vnmclip'),c.frame_rate),Bones=bones)
path.write_text(json.dumps(doc,ensure_ascii=False,separators=(',',':')),encoding='utf8')
sounds_path=root/'src/ScCsgoKnives/AnimationData/cs2_sounds.json'
sounds=json.loads(sounds_path.read_text(encoding='utf8'))
events={c['Event']:c['Asset'] for k,v in sounds['Clips'].items() if k.startswith('cz75a:') for c in v['Cues'] if c.get('Asset')}
for stem,alias in [('reload2_cz75a','reloadFollowup'),('reload2_empty_cz75a','reloadFollowupEmpty')]:
 c=doc['Clips'][stem];cues=[]
 for e in c['Events']:
  if e['Class']=='CNmClipDocEvent_Sound':
   if e['Name'] not in events:raise ValueError('No shipped sound for '+e['Name'])
   cues.append(dict(At=e['At'],Frame=e['StartFrame'],Event=e['Name'],Asset=events[e['Name']]))
 sounds['Clips']['cz75a:'+alias]=dict(SourceClip=stem,FrameRate=c['FrameRate'],Cues=cues)
sounds_path.write_text(json.dumps(sounds,ensure_ascii=False,separators=(',',':')),encoding='utf8')
