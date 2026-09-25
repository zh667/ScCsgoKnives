"""Derive 512/WebP light editions from the immutable family-1 compatibility archives."""
import argparse, hashlib, io, json, zipfile, zlib, xml.etree.ElementTree as ET
from pathlib import Path
import numpy as np
from PIL import Image

ROOT=Path(__file__).resolve().parents[1];OUT=ROOT/'output';REPORT=OUT/'release-compatibility-1'

def sha(b): return hashlib.sha256(b).hexdigest()

def light_texture(name,data):
    image=Image.open(io.BytesIO(data));image.load();old=image.size
    special=Path(name).name in ('weapon_c4_digits.png','env_specular_rgbm.png','muzzle_fire.png','muzzle_smoke.png')
    if max(old)>512 and not special:
        image=image.resize((round(old[0]*512/max(old)),round(old[1]*512/max(old))),Image.Resampling.LANCZOS)
        if name.lower().endswith('_normal.png'):
            pixels=np.array(image.convert('RGBA'));v=pixels[:,:,:3].astype(np.float32)/127.5-1
            n=np.linalg.norm(v,axis=2,keepdims=True);v/=np.maximum(n,1e-6)
            pixels[:,:,:3]=np.clip(np.rint((v+1)*127.5),0,255).astype(np.uint8);image=Image.fromarray(pixels)
    out=io.BytesIO();image.save(out,format='WEBP',quality=100 if special else 85,lossless=special,method=4,exact=True)
    return out.getvalue(),name[:-4]+'.webp'

def main():
    ap=argparse.ArgumentParser();ap.add_argument('profile',choices=['1.0.0','1.2.0']);a=ap.parse_args()
    source=OUT/f'[API1.9]CS武器{a.profile}-双向兼容修订1-作者ZH667.scmod'
    target=OUT/f'[API1.9]CS武器{a.profile}-双向兼容修订1-512轻量-作者ZH667.scmod'
    entries={};hashes={};renamed={};provenance=[]
    with zipfile.ZipFile(source) as z:
        for e in z.infolist():
            data=z.read(e.filename);name=e.filename
            if name.startswith('Assets/Textures/') and name.lower().endswith('.png'):
                original=name;data,name=light_texture(name,data);renamed[e.filename]=name;provenance.append(dict(path=original,kind='texture',sourceSha256=sha(z.read(e.filename)),sha256=sha(data)))
            entries[name]=data;hashes[name]=sha(data)
        meta=json.loads(z.read('modinfo.json'));meta['Name']=f'CS武器 · {a.profile}双向兼容修订 · 512轻量';meta['Description']=meta.get('Description','')+' 资源为512轻量版；语音附属需单独安装。'
        entries['modinfo.json']=(json.dumps(meta,ensure_ascii=False,indent=2)+'\n').encode('utf8');hashes['modinfo.json']=sha(entries['modinfo.json'])
        edition=ET.fromstring(z.read('Assets/ScCsgoKnivesEdition.xml'));edition.set('Name','Optimized512');entries['Assets/ScCsgoKnivesEdition.xml']=ET.tostring(edition,encoding='utf-8');hashes['Assets/ScCsgoKnivesEdition.xml']=sha(entries['Assets/ScCsgoKnivesEdition.xml'])
        marker=ET.fromstring(z.read('Assets/ScCsgoResources.xml'));marker.set('Edition','Optimized512')
        for f in marker.findall('File'):
            old=f.get('Path');new=renamed.get(old,old)
            f.set('Path',new);f.set('Sha256',hashes[new])
        entries['Assets/ScCsgoResources.xml']=ET.tostring(marker,encoding='utf-8',xml_declaration=True);hashes['Assets/ScCsgoResources.xml']=sha(entries['Assets/ScCsgoResources.xml'])
        family=json.loads(z.read('Integrations/CompatibilityFamily.json'));family['edition']='Optimized512';family['resource_policy']='512 textures, WebP Q85, original gameplay DLL';entries['Integrations/CompatibilityFamily.json']=json.dumps(family,ensure_ascii=False,indent=2).encode('utf8');hashes['Integrations/CompatibilityFamily.json']=sha(entries['Integrations/CompatibilityFamily.json'])
    entries['Assets/ScCsgoDerivedResources.json']=json.dumps(provenance,ensure_ascii=False,indent=2).encode('utf8');hashes['Assets/ScCsgoDerivedResources.json']=sha(entries['Assets/ScCsgoDerivedResources.json'])
    pending=target.with_suffix('.pending')
    with zipfile.ZipFile(pending,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=9) as out:
        for name,data in sorted(entries.items()):out.writestr(name,data)
    with zipfile.ZipFile(pending) as z:
        assert z.testzip() is None
        for name,h in hashes.items():assert sha(z.read(name))==h
    pending.replace(target);REPORT.mkdir(exist_ok=True)
    report=dict(path=target.name,version=a.profile+'-compat.1',profile=a.profile,edition='Lite',bytes=target.stat().st_size,sha256=sha(target.read_bytes()),sourceSha256=sha(source.read_bytes()),entries=len(entries))
    (REPORT/f'{a.profile}-Lite.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n','utf8');print(json.dumps(report,ensure_ascii=False))
if __name__=='__main__':main()
