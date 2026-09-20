"""Independent contact references from preserved CS2 world exports, for native QA.

Record both hand frames relative to the weapon and magazine/knife parts. This
detects mixing viewmodel props with world actors, even when a model stays bounded.
"""
import json
import numpy as np
from build_companion_assets import ROOT, SOURCE, Source
from derive_world_props import matrix
from cs2_knife_render import key


def build(name):
    s=Source(SOURCE/f'agents/models/{"ctm_sas" if name=="ct" else "tm_phoenix"}/{"ctm_sas" if name=="ct" else "tm_phoenix"}.glb')
    nodes=s.j['nodes'];parents={c:i for i,n in enumerate(nodes) for c in n.get('children',[])}
    hands={n:next(i for i,x in enumerate(nodes) if x.get('name')==n) for n in ('hand_R','hand_L','wpn')}
    skeletons={n['name'].split('/')[-1][:-8]:i for i,n in enumerate(nodes) if n.get('name','').endswith('.vnmskel')}
    aliases={'ak':'ak47','glock':'glock18','usp':'usp_silencer','hkp':'hkp2000'}
    def array(i):
        a=s.j['accessors'][i];w={'SCALAR':1,'VEC3':3,'VEC4':4}[a['type']];v=s.j['bufferViews'][a['bufferView']]
        return np.ndarray((a['count'],w),dtype='<f4',buffer=s.view(a['bufferView']),offset=a.get('byteOffset',0),strides=(v.get('byteStride',w*4),4)).copy()
    records=[]
    for a in s.j['animations']:
        leaf=a['name'].split('/')[-1]
        if '/world/' not in a['name'] or 'crouch' in leaf or not leaf.startswith(('draw_','reload_')):continue
        kind,asset=leaf.split('_',1)
        if asset.startswith('empty_'):kind='reloadEmpty';asset=asset[6:]
        asset=aliases.get(asset,asset)
        skel={'m4a1s':'m4a1_silencer','galilar':'galil','grenade':'hegrenade'}.get(asset,asset)
        if skel not in skeletons:skel='knife_'+skel
        wrapper=skeletons[skel];roots=nodes[wrapper]['children']
        def descendants(i):
            yield i
            for c in nodes[i].get('children',[]):yield from descendants(c)
        selected={nodes[i]['name']:i for i in descendants(wrapper) if nodes[i].get('name') in ('weapon','clip','clip_l','clip_r','weapon_l','weapon_r','blade')}
        curves={}
        for c in a['channels']:
            sampler=a['samplers'][c['sampler']];curves[c['target']['node'],c['target']['path']]={'Times':array(sampler['input']).ravel().tolist(),'Values':array(sampler['output']).tolist()}
        duration=max(x['Times'][-1] for x in curves.values())
        phases=[.25,.55,.8]+([1] if kind=='draw' else [])
        for phase in phases:
            local={};world={}
            def pose(i):
                if i not in local:
                    n=dict(nodes[i])
                    for prop in ('translation','rotation','scale'):
                        if (i,prop) in curves:n[prop]=key(curves[i,prop],duration*phase)
                    local[i]=matrix(n)
                return local[i]
            def absolute(i):
                if i not in world:world[i]=pose(i)@(absolute(parents[i]) if i in parents else np.eye(4))
                return world[i]
            # Compose the prop's Source-axis chain before applying the actor socket.
            def prop_local(i):return np.eye(4) if i in roots else pose(i)@prop_local(parents[i])
            expected=[]
            for bone,i in selected.items():
                frame=prop_local(i)@absolute(hands['wpn'])
                # Source hides spent magazines with a .001 scale. Compare distances
                # in metres, not the hidden part's magnified inverse-scale units.
                frame[:3,:3]/=np.linalg.norm(frame[:3,:3],axis=1)[:,None]
                for hand in ('hand_R','hand_L'):
                    relative=absolute(hands[hand])@np.linalg.inv(frame)
                    expected.append({'bone':bone,'hand':hand,'matrix':np.round(relative,7).ravel().tolist()})
            records.append({'model':name,'asset':asset,'clip':('hold' if phase==1 else kind)+'_'+asset,'phase':0 if phase==1 else phase,'expected':expected})
    return records


if __name__=='__main__':
    records=build('ct')+build('t')
    path=ROOT/'tools/fixtures/world-prop-contacts.json'
    path.write_text(json.dumps(records,separators=(',',':'))+'\n','utf8')
    print(f'{len(records)} world-pose references; {sum(len(x["expected"]) for x in records)} hand/prop frames')
