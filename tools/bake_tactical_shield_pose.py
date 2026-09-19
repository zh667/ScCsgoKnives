"""Bake authored two-bone shield grips onto preserved CS2 lower-body motion."""
import copy,json,struct
import numpy as np
from scipy.spatial.transform import Rotation
from cs2_glb import Glb
from cs2_knife_render import key

def bake(path):
    g=Glb(path);doc=g.json;blob=bytearray(g.bin);original=copy.deepcopy(g.nodes);parents=g.parents();names={n.get('name'):i for i,n in enumerate(g.nodes)}
    def accessor(a,typ):
        a=np.asarray(a,dtype='<f4');blob.extend(b'\0'*(-len(blob)%4));view=len(doc['bufferViews']);doc['bufferViews'].append({'buffer':0,'byteOffset':len(blob),'byteLength':a.nbytes});blob.extend(a.tobytes());index=len(doc['accessors']);value={'bufferView':view,'componentType':5126,'count':len(a),'type':typ}
        if typ=='SCALAR':value.update(min=[float(a.min())],max=[float(a.max())])
        doc['accessors'].append(value);return index
    def rotate_to(a,b):
        a=a/np.linalg.norm(a);b=b/np.linalg.norm(b);cross=np.cross(a,b);dot=np.clip(a@b,-1,1)
        if np.linalg.norm(cross)<1e-8:return np.eye(3)
        return Rotation.from_rotvec(cross/np.linalg.norm(cross)*np.arccos(dot)).as_matrix().T
    for clip in doc['animations']:
        if clip['name'] not in ['shield','shieldwalk']:continue
        channels=copy.deepcopy(clip['channels']);duration=max(float(g.accessor(s['input']).max()) for s in clip['samplers']);times=np.linspace(0,duration,max(2,int(duration*30)+1));tracks={names[n]:[] for n in ['arm_upper_L','arm_lower_L','arm_upper_R','arm_lower_R']}
        for t in times:
            doc['nodes']=copy.deepcopy(original)
            for c in channels:
                s=clip['samplers'][c['sampler']];n=doc['nodes'][c['target']['node']];n.pop('matrix',None)
                n[c['target']['path']]=key({'Times':g.accessor(s['input']).ravel().tolist(),'Values':g.accessor(s['output']).tolist()},t).tolist()
            for side,sign in [('L',1),('R',-1)]:
                upper,lower,hand=[names[n+'_'+side] for n in ['arm_upper','arm_lower','hand']]
                a,b,h=[g.world_matrix(i)[3,:3] for i in [upper,lower,hand]];goal=np.array([sign*.125,1.026,.36]);l1=np.linalg.norm(b-a);l2=np.linalg.norm(h-b);direction=goal-a;distance=np.linalg.norm(direction);unit=direction/distance
                distance=np.clip(distance,abs(l1-l2)+.001,l1+l2-.001);goal=a+unit*distance
                bend=np.array([sign,-1.,-.2]);bend-=unit*(bend@unit);bend/=np.linalg.norm(bend)
                along=(l1*l1-l2*l2+distance*distance)/(2*distance);elbow=a+unit*along+bend*np.sqrt(max(0,l1*l1-along*along))
                for bone,child,target in [(upper,lower,elbow),(lower,hand,goal)]:
                    world=g.world_matrix(bone);origin=world[3,:3];end=g.world_matrix(child)[3,:3]
                    new=world[:3,:3]@rotate_to(end-origin,target-origin);parent=g.world_matrix(parents[bone]);local=new@np.linalg.inv(parent[:3,:3]);q=Rotation.from_matrix(local.T).as_quat();doc['nodes'][bone]['rotation']=q.tolist();doc['nodes'][bone].pop('matrix',None);tracks[bone].append(q)
        clip['channels']=[c for c in channels if not(c['target']['node'] in tracks and c['target']['path']=='rotation')]
        inp=accessor(times,'SCALAR')
        for bone,values in tracks.items():
            out=accessor(values,'VEC4');clip['channels'].append({'sampler':len(clip['samplers']),'target':{'node':bone,'path':'rotation'}});clip['samplers'].append({'input':inp,'output':out,'interpolation':'LINEAR'})
    doc['nodes']=original;blob.extend(b'\0'*(-len(blob)%4));doc['buffers']=[{'byteLength':len(blob)}];js=json.dumps(doc,separators=(',',':')).encode();js+=b' '*(-len(js)%4)
    path.write_bytes(struct.pack('<4sII',b'glTF',2,28+len(js)+len(blob))+struct.pack('<II',len(js),0x4e4f534a)+js+struct.pack('<II',len(blob),0x004e4942)+blob)
