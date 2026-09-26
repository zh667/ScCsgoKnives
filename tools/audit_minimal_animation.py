"""Separate compressed file contribution from animation numeric payload (not process RAM)."""
import json,zlib,struct,copy
from pathlib import Path
root=Path(__file__).resolve().parents[1]
source=root/'.tmp/lite-smooth-20260926/embedded'
mini=root/'.tmp/minimal-130-20260926/resources/AnimationData'
rows=[]
def encode(d):return json.dumps(d,ensure_ascii=False,separators=(',',':')).encode()
for p in source.glob('*.cs2.animation.json'):
    doc=json.loads(p.read_bytes());stripped=copy.deepcopy(doc)
    row=dict(name=p.name,inspectClips=0,inspectKeys=0,inspectFloatBytes=0,allFloatBytes=0)
    for name,c in doc['Clips'].items():
        inspect=any(x in (name+' '+str(c.get('Alias',''))).lower() for x in ['inspect','lookat'])
        if inspect:row['inspectClips']+=1;stripped['Clips'][name]['Bones']={}
        for b in c.get('Bones',{}).values():
            for curve in b.values():
                if isinstance(curve,dict) and 'Times' in curve:
                    size=4*(len(curve['Times'])+sum(len(v) for v in curve['Values']))
                    row['allFloatBytes']+=size
                    if inspect:row['inspectFloatBytes']+=size;row['inspectKeys']+=len(curve['Times'])
    row['compressedAll']=len(zlib.compress(encode(doc),9))
    row['compressedWithoutInspect']=len(zlib.compress(encode(stripped),9))
    row['inspectMarginalCompressed']=row['compressedAll']-row['compressedWithoutInspect']
    current=json.loads((mini/p.name.removeprefix('Game.AnimationData.')).read_bytes())
    row['currentCompressed']=len(zlib.compress(encode(current),9))
    row['currentInspectKeys']=sum(len(cv.get('Times',[])) for n,c in current['Clips'].items() if any(x in (n+' '+str(c.get('Alias',''))).lower() for x in ['inspect','lookat']) for b in c.get('Bones',{}).values() for cv in b.values() if isinstance(cv,dict))
    rows.append(row)
total={k:sum(r[k] for r in rows) for k in rows[0] if k!='name'}
report=dict(totals=total,rows=rows,method='Per-file zlib9 marginal estimate; float bytes exclude objects/arrays/metadata. Not measured runtime RAM.')
(root/'.tmp/minimal-animation-audit.json').write_text(json.dumps(report,indent=2),encoding='utf8')
print(json.dumps(total,indent=2))
