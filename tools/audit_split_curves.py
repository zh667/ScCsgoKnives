"""Verify every source key against the shipping reduced interpolation curve."""
from pathlib import Path
import json,math
import numpy as np
R=Path(__file__).resolve().parents[1];S=R/'.tmp/split-lite-130-20260926';rows=[]
for p in sorted((S/'resources/AnimationData').glob('*.animation.json')):
    old=json.loads((R/'.tmp/lite-smooth-20260926/embedded'/('Game.AnimationData.'+p.name)).read_bytes());new=json.loads(p.read_bytes())
    assert old.keys()==new.keys() and old['Clips'].keys()==new['Clips'].keys()
    row=dict(name=p.name,rotationRadians=0.,translationInches=0.,curves=0,keysBefore=0,keysAfter=0)
    for name,c in old['Clips'].items():
        d=new['Clips'][name]
        assert {k:v for k,v in c.items() if k!='Bones'}=={k:v for k,v in d.items() if k!='Bones'}
        assert c['Bones'].keys()==d['Bones'].keys()
        for bone,b in c['Bones'].items():
            for kind,curve in b.items():
                after=d['Bones'][bone][kind]
                if not curve or kind not in ['Rotation','Translation']:assert curve==after;continue
                times=np.array(curve['Times']);vs=np.array(curve['Values']);ts=np.array(after['Times']);vv=np.array(after['Values'])
                assert set(ts).issubset(set(times)) and ts[0]==times[0] and ts[-1]==times[-1]
                if len(ts)==1:pred=np.repeat(vv,len(times),axis=0)
                else:
                    ids=np.clip(np.searchsorted(ts,times,side='right')-1,0,len(ts)-2);f=(times-ts[ids])/(ts[ids+1]-ts[ids]);a=vv[ids];b=vv[ids+1]
                    if kind=='Translation':pred=a+(b-a)*f[:,None]
                    else:
                        a=a/np.linalg.norm(a,axis=1,keepdims=True);b=b/np.linalg.norm(b,axis=1,keepdims=True);dot=(a*b).sum(axis=1);b=np.where((dot<0)[:,None],-b,b);dot=np.clip(abs(dot),0,1)
                        angle=np.arccos(dot);s=np.sin(angle);safe=np.where(s<1e-9,1,s)
                        pred=np.where((dot>.9995)[:,None],a+(b-a)*f[:,None],(np.sin((1-f)*angle)/safe)[:,None]*a+(np.sin(f*angle)/safe)[:,None]*b)
                if kind=='Translation':error=float(np.linalg.norm(pred-vs,axis=1).max());row['translationInches']=max(row['translationInches'],error);assert error<=.01002,(p,name,bone,error)
                else:
                    pred=pred/np.linalg.norm(pred,axis=1,keepdims=True);vs=vs/np.linalg.norm(vs,axis=1,keepdims=True)
                    error=float((2*np.arccos(np.clip(abs((pred*vs).sum(axis=1)),0,1))).max());row['rotationRadians']=max(row['rotationRadians'],error);assert error<=math.radians(.151),(p,name,bone,error)
                row['curves']+=1;row['keysBefore']+=len(times);row['keysAfter']+=len(ts)
    rows.append(row)
report=dict(failed=0,rows=rows,keysBefore=sum(r['keysBefore'] for r in rows),keysAfter=sum(r['keysAfter'] for r in rows),maxDegrees=math.degrees(max(r['rotationRadians'] for r in rows)),maxInches=max(r['translationInches'] for r in rows))
(S/'curve-audit.json').write_text(json.dumps(report,indent=2),encoding='utf8');print({k:v for k,v in report.items() if k!='rows'})
