"""Reduce derived NPC GPU palettes to 48 joints; keep the animation skeleton intact.

Collapse only to actual ancestors, choosing the least measured surface error across
all shipped clips. Original exports are never changed. Source bind matrices, vertex
positions and texture coordinates are preserved.
"""
import copy, json, struct
import numpy as np
from cs2_glb import Glb
from cs2_knife_render import key


def compact(path, budget=48):
    g=Glb(path);doc=g.json;original=copy.deepcopy(doc['nodes']);parents=g.parents()
    joints=doc['skins'][0]['joints'];lookup={n:i for i,n in enumerate(joints)}
    assert all(s['joints']==joints for s in doc['skins']), 'body/glove palettes differ'
    bind=g.accessor(doc['skins'][0]['inverseBindMatrices']).reshape(-1,4,4)
    parts=[]
    for mesh in doc['meshes']:
        for p in mesh['primitives']:
            a=p['attributes'];parts.append((p,g.accessor(a['POSITION']),g.accessor(a['JOINTS_0']).astype(int),g.accessor(a['WEIGHTS_0'])))
    pos=np.concatenate([p[1] for p in parts]);indices=np.concatenate([p[2] for p in parts]);weights=np.concatenate([p[3] for p in parts]);hp=np.c_[pos,np.ones(len(pos))]
    samples=[]
    for animation in doc['animations']:
        duration=max(g.accessor(s['input']).max() for s in animation['samplers'])
        for t in np.linspace(0,duration,5):
            doc['nodes']=copy.deepcopy(original)
            for c in animation['channels']:
                s=animation['samplers'][c['sampler']];value=key({'Times':g.accessor(s['input']).ravel().tolist(),'Values':g.accessor(s['output']).tolist()},float(t))
                n=doc['nodes'][c['target']['node']];n.pop('matrix',None);n[c['target']['path']]=value.tolist()
            samples.append(np.array([ib@g.world_matrix(j) for ib,j in zip(bind,joints)]))
    doc['nodes']=original;samples=np.array(samples)
    ancestor={}
    for i,n in enumerate(joints):
        parent=parents.get(n)
        while parent is not None and parent not in lookup:parent=parents.get(parent)
        ancestor[i]=lookup.get(parent)
    # Core locomotion/holding joints cannot be collapsed, even in motionless clips.
    core={'pelvis','spine_0','spine_1','spine_2','spine_3','neck_0','head_0'}
    core.update(x+s for x in ['clavicle_','arm_upper_','arm_lower_','hand_','leg_upper_','leg_lower_','ankle_'] for s in ['L','R'])
    protected={i for i,n in enumerate(joints) if original[n].get('name') in core or ancestor[i] is None}
    active=set(range(len(joints)));mapping=np.arange(len(joints));costs={}
    def cost(i,p):
        if (i,p) not in costs:
            v,k=np.where((indices==i)&(weights>0))
            if not len(v):costs[i,p]=(0.,0.)
            else:
                delta=np.einsum('vi,sij->svj',hp[v],samples[:,i]-samples[:,p])[...,:3]*weights[v,k][None,:,None]
                d=np.linalg.norm(delta,axis=2);costs[i,p]=(float(d.max()),float(np.mean(d*d)))
        return costs[i,p]
    while len(active)>budget:
        choices=[]
        for i in active-protected:
            parent=ancestor[i]
            while parent not in active:parent=ancestor[parent]
            group=np.flatnonzero(mapping==i);errors=[cost(int(k),parent) for k in group]
            choices.append((max(e[0] for e in errors),sum(e[1] for e in errors),i,parent))
        _,_,drop,parent=min(choices);mapping[mapping==drop]=parent;active.remove(drop)
    retained=sorted(active);new_index={old:i for i,old in enumerate(retained)}
    # Exact final surface displacement, including overlapping weighted influences.
    worst=0.;all_errors=[]
    for sample in samples:
        before=np.zeros((len(pos),3));after=before.copy()
        for k in range(4):
            before+=np.einsum('vi,vij->vj',hp,sample[indices[:,k]])[:,:3]*weights[:,k,None]
            after+=np.einsum('vi,vij->vj',hp,sample[mapping[indices[:,k]]])[:,:3]*weights[:,k,None]
        error=np.linalg.norm(after-before,axis=1);worst=max(worst,float(error.max()));all_errors.append(error)
    errors=np.concatenate(all_errors)
    assert worst<.04, f'{path.name}: excessive palette error {worst:.3f} m'
    blob=bytearray(g.bin)
    def add(array,kind,ctype):
        blob.extend(b'\0'*(-len(blob)%4));vi=len(doc['bufferViews']);data=array.tobytes()
        doc['bufferViews'].append({'buffer':0,'byteOffset':len(blob),'byteLength':len(data)});blob.extend(data)
        ai=len(doc['accessors']);doc['accessors'].append({'bufferView':vi,'componentType':ctype,'count':len(array),'type':kind});return ai
    for p,_,idx,w in parts:
        # Merge duplicate weights after collapse, avoiding engine-dependent normalization.
        remapped=np.vectorize(new_index.__getitem__)(mapping[idx]);outj=np.zeros_like(idx,dtype='<u2');outw=np.zeros_like(w,dtype='<f4')
        for v in range(len(idx)):
            merged={}
            for joint,weight in zip(remapped[v],w[v]):merged[int(joint)]=merged.get(int(joint),0)+float(weight)
            pairs=sorted(merged.items(),key=lambda x:-x[1]);total=sum(x[1] for x in pairs)
            for k,(joint,weight) in enumerate(pairs):outj[v,k]=joint;outw[v,k]=weight/total
        p['attributes']['JOINTS_0']=add(outj,'VEC4',5123);p['attributes']['WEIGHTS_0']=add(outw,'VEC4',5126)
    ib=add(bind[retained].reshape(-1,16).astype('<f4'),'MAT4',5126)
    for skin in doc['skins']:skin['joints']=[joints[i] for i in retained];skin['inverseBindMatrices']=ib
    blob.extend(b'\0'*(-len(blob)%4));doc['buffers']=[{'byteLength':len(blob)}];js=json.dumps(doc,separators=(',',':')).encode();js+=b' '*(-len(js)%4)
    path.write_bytes(struct.pack('<4sII',b'glTF',2,28+len(js)+len(blob))+struct.pack('<II',len(js),0x4e4f534a)+js+struct.pack('<II',len(blob),0x004e4942)+blob)
    report={'originalJoints':len(joints),'gpuJoints':len(retained),'samples':len(samples),'maxDisplacementMeters':worst,'p99DisplacementMeters':float(np.percentile(errors,99)),'rmsDisplacementMeters':float(np.sqrt(np.mean(errors**2))),'retained':[original[joints[i]].get('name') for i in retained]}
    print(path.name,json.dumps(report));return report
