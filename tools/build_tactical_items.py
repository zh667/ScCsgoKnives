"""Bake CS2 defuser/toolbox into static inventory meshes; author a portable beacon radio.

Source exports remain untouched. Textures are atlased, opaque and resolved at Block.Initialize.
The radio GLB is retained for historical diagnostics. Active radios now use
ScSupplyGeometry and build_supply_surface.py; rebuilding this legacy GLB does not
change the active procedural radio.
"""
from pathlib import Path
import io,json,struct,hashlib
import numpy as np
from PIL import Image
from cs2_glb import Glb

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'src/ScCsgoTactical/Assets'

def write(name,parts):
    doc={'asset':{'version':'2.0','generator':'ZH667 tactical static item bake'},'scene':0,'scenes':[{'nodes':[0]}],
         'nodes':[{'mesh':0,'name':name}],'meshes':[{'primitives':[]}],'bufferViews':[],'accessors':[]}
    blob=bytearray()
    def acc(data,kind,dtype,component):
        a=np.asarray(data,dtype=dtype);blob.extend(b'\0'*(-len(blob)%4));v=len(doc['bufferViews'])
        doc['bufferViews'].append({'buffer':0,'byteOffset':len(blob),'byteLength':a.nbytes});blob.extend(a.tobytes())
        spec={'bufferView':v,'componentType':component,'count':len(a),'type':kind}
        if kind=='VEC3':spec.update(min=a.min(0).tolist(),max=a.max(0).tolist())
        i=len(doc['accessors']);doc['accessors'].append(spec);return i
    atlas=Image.new('RGB',(512*len(parts),512))
    for k,(p,n,uv,idx,image) in enumerate(parts):
        # CS props can use a repeated tile outside [0,1] (toolbox U is 1..2).
        # Clamp destroys that mapping; bake its full tile rectangle into this atlas cell.
        low=np.floor(uv.min(0));tiles=np.maximum(1,np.ceil(uv.max(0))-low).astype(int)
        assert max(tiles)<=16, 'unexpectedly large texture repetition'
        tile=image.convert('RGB');repeated=Image.new('RGB',(tile.width*tiles[0],tile.height*tiles[1]))
        for y in range(tiles[1]):
            for x in range(tiles[0]):repeated.paste(tile,(x*tile.width,y*tile.height))
        atlas.paste(repeated.resize((512,512),Image.Resampling.LANCZOS),(k*512,0))
        uv=(uv.copy()-low)/tiles;uv[:,0]=(k+uv[:,0])/len(parts)
        doc['meshes'][0]['primitives'].append({'attributes':{'POSITION':acc(p,'VEC3','<f4',5126),'NORMAL':acc(n,'VEC3','<f4',5126),'TEXCOORD_0':acc(uv,'VEC2','<f4',5126)},'indices':acc(idx,'SCALAR','<u4',5125)})
    doc['buffers']=[{'byteLength':len(blob)}];js=json.dumps(doc,separators=(',',':')).encode();js+=b' '*(-len(js)%4)
    (OUT/f'Models/ScCsgoTactical/{name}.glb').write_bytes(struct.pack('<4sII',b'glTF',2,28+len(js)+len(blob))+struct.pack('<II',len(js),0x4e4f534a)+js+struct.pack('<II',len(blob),0x004e4942)+blob)
    atlas.save(OUT/f'Textures/ScCsgoTactical/{name}.png')

def imported(name,path,length):
    g=Glb(path);parts=[]
    for i,node in enumerate(g.nodes):
        if 'mesh' not in node:continue
        m=g.world_matrix(i)
        for primitive in g.json['meshes'][node['mesh']]['primitives']:
            a={k:g.accessor(v) for k,v in primitive['attributes'].items()}
            p=(np.c_[a['POSITION'],np.ones(len(a['POSITION']))]@m)[:,:3]
            n=a['NORMAL']@np.linalg.inv(m[:3,:3]).T;n/=np.maximum(np.linalg.norm(n,axis=1,keepdims=True),1e-8)
            material=g.json['materials'][primitive['material']];ti=material['pbrMetallicRoughness']['baseColorTexture']['index']
            image=g.json['images'][g.json['textures'][ti]['source']];im=Image.open(path.parent/image['uri'])
            parts.append((p,n,a['TEXCOORD_0'],g.accessor(primitive['indices']).ravel(),im))
    allp=np.concatenate([p[0] for p in parts]);lo=allp.min(0);hi=allp.max(0);center=(lo+hi)/2;scale=length/max(hi-lo)
    parts=[((p-center)*scale,n,uv,idx,im) for p,n,uv,idx,im in parts];write(name,parts)
    return {'item':name,'source':str(path.relative_to(ROOT)),'sourceSha256':hashlib.sha256(path.read_bytes()).hexdigest(),'length':length}

def radio():
    p=[];n=[];uv=[];idx=[]
    def box(lo,hi,front=False):
        x,y,z=lo;X,Y,Z=hi
        faces=[([(x,y,z),(x,Y,z),(X,Y,z),(X,y,z)],(0,0,-1)), ([(X,y,Z),(X,Y,Z),(x,Y,Z),(x,y,Z)],(0,0,1)), ([(x,y,Z),(x,Y,Z),(x,Y,z),(x,y,z)],(-1,0,0)), ([(X,y,z),(X,Y,z),(X,Y,Z),(X,y,Z)],(1,0,0)), ([(x,Y,z),(x,Y,Z),(X,Y,Z),(X,Y,z)],(0,1,0)), ([(x,y,Z),(x,y,z),(X,y,z),(X,y,Z)],(0,-1,0))]
        for f,(vertices,normal) in enumerate(faces):
            start=len(p);p.extend(vertices);n.extend([normal]*4);idx.extend([start,start+1,start+2,start,start+2,start+3])
            uv.extend([(.24,.91),(.24,.22),(.76,.22),(.76,.91)] if front and f==1 else [(.31,.85)]*4)
    box((-.095,-.14,-.035),(.095,.14,.035),True);box((.04,.14,-.008),(.052,.25,.008));box((-.06,.14,-.018),(-.025,.17,.018))
    write('radio',[(np.array(p),np.array(n),np.array(uv),np.array(idx),Image.open(OUT/'Textures/ScCsgoTactical/beacon.png'))])

def main():
    folder=ROOT/'.tmp/tactical-items-cs2'
    records=[imported('defuser_item',folder/'weapons/models/defuser/defuser.glb',.30),imported('repair_item',folder/'models/generic/toolkit_01/toolbox_01_closed.glb',.40)]
    radio();records.append({'item':'radio','source':'authored portable radio geometry / existing beacon atlas; not Valve geometry'})
    (ROOT/'docs/tactical-item-assets.json').write_text(json.dumps(records,ensure_ascii=False,indent=2)+'\n','utf8')
    print(json.dumps(records,ensure_ascii=False))
if __name__=='__main__':main()
