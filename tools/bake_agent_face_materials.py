"""Bake CS2 eye/lens shader features into the existing derived base textures.

This is a fixed forward gaze / dark lens adaptation, not a Source 2 shader implementation.
Only embedded face/lens images change; geometry, UVs, skin weights and clips are preserved.
"""
from collections import deque
import hashlib
import io
import json
import struct
from pathlib import Path
import numpy as np
from PIL import Image
from cs2_glb import Glb

ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'.tmp/cs2-companions-audit-20260920/export/agents/models'

def baked_texture(agent, material, folder):
    if agent == 'ct' and material == 'ctm_sas_lenses':
        ao=np.asarray(Image.open(folder/'ctm_sas_lenses_ao_psd_394dcde8.png').convert('L'),dtype=float)/255
        y,x=np.mgrid[0:ao.shape[0],0:ao.shape[1]]/ao.shape[0]
        highlight=np.exp(-((x-.37)**2/.026+(y-.29)**2/.009))*ao
        color=np.array([129,124,123])[None,None,:]*(.10+.15*ao[:,:,None])
        color+=highlight[:,:,None]*np.array([29,37,43])
        return Image.fromarray(np.clip(color,0,255).astype('uint8'))
    if agent == 't' and material == 'tm_phoenix_v2_balaclava_varianta':
        base=Image.open(folder/'tm_phoenix_v2_balaclava_varianta_color_psd_3019cf96.png').convert('RGB')
        mask=np.asarray(Image.open(folder/'tm_phoenix_v2_balaclava_varianta_eyemask_psd_3567ecc.png').convert('RGB'))[:,:,0]
        assert mask.shape == (base.height,base.width)
        unvisited=mask>127; regions=[]
        while unvisited.any():
            y,x=np.argwhere(unvisited)[0]; q=deque([(int(y),int(x))]); unvisited[y,x]=False; pts=[]
            while q:
                y,x=q.popleft();pts.append((y,x))
                for yy,xx in ((y-1,x),(y+1,x),(y,x-1),(y,x+1)):
                    if 0<=yy<mask.shape[0] and 0<=xx<mask.shape[1] and unvisited[yy,xx]:unvisited[yy,xx]=False;q.append((yy,xx))
            if len(pts)>100:regions.append(np.array(pts))
        assert len(regions)==2, 'Expected two source eye mask islands'
        eye=Image.open(folder/'eyeball_brown_light_color_psd_2d666d0e.png').convert('RGBA')
        out=np.asarray(base,dtype=float).copy()
        for pts in regions:
            y0,x0=pts.min(axis=0);y1,x1=pts.max(axis=0);h=y1-y0+1
            cx=(x0+x1)/2;cy=y0+h*.56;scale=h*.92/200
            yy,xx=np.mgrid[y0:y1+1,x0:x1+1]
            # Source alpha marks iris coverage; the pupil itself is procedural in Source 2.
            sample=np.asarray(eye.transform((x1-x0+1,y1-y0+1),Image.Transform.AFFINE,(1/scale,0,(x0-cx)/scale+256,0,1/scale,(y0-cy)/scale+256),Image.Resampling.BILINEAR),dtype=float)
            alpha=sample[:,:,3]/255*(mask[y0:y1+1,x0:x1+1]/255)
            radius=np.sqrt((xx-cx)**2+(yy-cy)**2)
            pupil=np.clip((h*.15-radius)/1.5,0,1)*(mask[y0:y1+1,x0:x1+1]/255)
            color=sample[:,:,:3]*.68
            patch=out[y0:y1+1,x0:x1+1];patch[:]=patch*(1-alpha[:,:,None])+color*alpha[:,:,None]
            patch[:]=patch*(1-pupil[:,:,None])+np.array([6,7,8])*pupil[:,:,None]
        result=Image.fromarray(np.clip(out,0,255).astype('uint8'))
        assert np.array_equal(np.asarray(result)[mask==0],np.asarray(base)[mask==0]), 'Changed pixels outside eye mask'
        return result
    return None

def patch(agent, source_folder):
    path=ROOT/f'src/ScCsgoTactical/Assets/Models/ScCsgoTactical/{agent}.glb';g=Glb(path);j=g.json
    before=[g.accessor(i).copy() for i in range(len(j['accessors']))]
    replacements={};changed=[]
    for material in j['materials']:
        image=baked_texture(agent,material['name'],source_folder)
        if image is None:continue
        ti=material['pbrMetallicRoughness']['baseColorTexture']['index'];ii=j['textures'][ti]['source'];vi=j['images'][ii]['bufferView']
        b=io.BytesIO();image.save(b,format='PNG');replacements[vi]=b.getvalue();changed.append(material['name'])
    assert len(changed)==1
    blob=bytearray()
    for i,v in enumerate(j['bufferViews']):
        data=replacements.get(i,g.bin[v.get('byteOffset',0):v.get('byteOffset',0)+v['byteLength']]);blob.extend(b'\0'*(-len(blob)%4));v['byteOffset']=len(blob);v['byteLength']=len(data);blob.extend(data)
    blob.extend(b'\0'*(-len(blob)%4));j['buffers']=[{'byteLength':len(blob)}];doc=json.dumps(j,separators=(',',':')).encode();doc+=b' '*(-len(doc)%4)
    path.write_bytes(struct.pack('<4sII',b'glTF',2,28+len(doc)+len(blob))+struct.pack('<II',len(doc),0x4e4f534a)+doc+struct.pack('<II',len(blob),0x004e4942)+blob)
    result=Glb(path);assert all(np.array_equal(a,result.accessor(i)) for i,a in enumerate(before))
    return {'name':agent,'materials':changed,'bytes':path.stat().st_size,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'geometryAndAnimationsUnchanged':True}

if __name__=='__main__':
    report=[patch('ct',SOURCE/'ctm_sas'),patch('t',SOURCE/'tm_phoenix')]
    (ROOT/'docs/tactical-face-materials.json').write_text(json.dumps(report,indent=2)+'\n','utf8')
    target=ROOT/'docs/tactical-derived-assets.json';rows=json.loads(target.read_text('utf8'))
    for row in rows:
        for update in report:
            if row['name']==update['name']:row.update(bytes=update['bytes'],sha256=update['sha256'],faceMaterialAdaptation=update['materials'])
    target.write_text(json.dumps(rows,ensure_ascii=False,indent=2)+'\n','utf8')
    print(json.dumps(report))
