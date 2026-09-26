"""Derive full-catalogue split assets from the immutable published Lite.

No model decimation: every Lite mesh, actor cache, skin and animation clip stays.
Use the previously validated bounded animation curves and compressed sounds.
"""
from pathlib import Path
import hashlib, json, zipfile, io, shutil, math
from build_minimal_resources import source_names
import soundfile as sf
import numpy as np
from scipy.signal import resample_poly
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
STAGE=ROOT/'.tmp/split-lite-130-20260926'
LITE=ROOT/'output/[API1.9]CS武器1.3.0-轻量包.scmod'
if (STAGE/'original-lite.scmod').exists():LITE=STAGE/'original-lite.scmod'
QUALITY=ROOT/'.tmp/minimal-inspect40-130-20260926'
def sha(b): return hashlib.sha256(b).hexdigest()
def write(p,b): p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b)

def main():
    assert sha(LITE.read_bytes())=='187598bfb07a358d88a60806c80dbb446808fe8663258761a4e38c776f8a3dce'
    write(STAGE/'original-lite.scmod',LITE.read_bytes())
    core=source_names('src/ScCsgoKnives/Assets')
    addon=source_names('src/ScCsgoTactical/Assets')|source_names('src/ScCsgoAppearance/Assets')|source_names('src/ScCsgoVoice/Assets')
    record=dict(sourceSha256=sha(LITE.read_bytes()),entries={},omitted=[],audio=[])
    old_audio={a['path']:a for a in json.loads((ROOT/'.tmp/minimal-130-20260926/resource-derivation.json').read_bytes())['audio']}
    with zipfile.ZipFile(LITE) as z,zipfile.ZipFile(ROOT/'output/[API1.9]CS武器1.3.0-全量包.scmod') as full:
        for i in z.infolist():
            n=i.filename
            if n.endswith('.dll') or n.startswith('Integrations/') or n in ['modinfo.json','INSTALL.txt','Assets/ScCsgoResources.xml','Assets/ScCsgoDerivedResources.json','Assets/ScCsgoTacticalDerivedResources.json']:
                record['omitted'].append(n);continue
            owner='agents' if (n in addon and n not in core) or 'chicken' in n.lower() else 'core'
            data=z.read(n);target=n;operation='unchanged'
            stem=Path(n).stem
            if owner=='core' and n.startswith('Assets/Textures/') and n.endswith('.webp') and (stem.endswith(('_normal','_orm')) or '_slot' in stem or stem.startswith(('ui_','icon_'))):
                original=n[:-5]+'.png';raw=full.read(original) if original in full.namelist() else data
                image=Image.open(io.BytesIO(raw));image.load();cap=256 if stem.endswith(('_normal','_orm')) else 128
                if max(image.size)>cap:image=image.resize(tuple(max(1,round(x*cap/max(image.size))) for x in image.size),Image.Resampling.LANCZOS)
                if stem.endswith('_normal'):
                    px=np.array(image.convert('RGBA'));v=px[:,:,:3].astype(float)/127.5-1;length=np.linalg.norm(v,axis=2,keepdims=True)
                    v/=np.maximum(length,1e-6);v[length[:,:,0]<1e-6]=(0,0,1);px[:,:,:3]=np.clip(np.rint((v+1)*127.5),0,255).astype(np.uint8);image=Image.fromarray(px)
                buf=io.BytesIO();image.save(buf,format='WEBP',quality=80,method=6,exact=True);data=buf.getvalue();operation='auxiliary256/icon128'
            if n.startswith('Assets/Audio/') and n.endswith(('.wav','.ogg')):
                if n in old_audio:
                    a=old_audio[n];target=a['target'];data=(ROOT/'.tmp/minimal-130-20260926/package'/target).read_bytes()
                else:
                    audio,rate=sf.read(io.BytesIO(data),dtype='float32',always_2d=True)
                    out=min(rate,22050);gcd=math.gcd(rate,out);mono=audio.mean(axis=1)
                    if out!=rate:mono=resample_poly(mono,out//gcd,rate//gcd)
                    buf=io.BytesIO();sf.write(buf,np.clip(mono,-1,1),out,format='OGG',subtype='VORBIS',compression_level=.65)
                    if len(buf.getvalue())<len(data):data=buf.getvalue();target=n.rsplit('.',1)[0]+'.ogg'
                operation='compressed sound' if data!=z.read(n) else 'unchanged'
                record['audio'].append(dict(source=n,target=target))
            write(STAGE/owner/'assets'/target,data)
            record['entries'][n]=dict(owner=owner,target=target,operation=operation,sha256=sha(data),sourceSha256=sha(z.read(n)))
        # Full Lite geometry, including all knives and legacy weapon meshes.
        for p in (ROOT/'.tmp/lite-smooth-20260926/embedded').iterdir():
            name=p.name.removeprefix('Game.AnimationData.')
            if p.suffix in ['.parts','.skin']:data=p.read_bytes()
            elif name.endswith('.animation.json'):
                doc=json.loads((QUALITY/'resources/AnimationData'/name).read_bytes())
                # Preserve timestamps/events exactly; sub-pixel value precision only.
                for clip in doc['Clips'].values():
                    for bone in clip.get('Bones',{}).values():
                        for kind,curve in bone.items():
                            if curve and kind in ['Rotation','Translation']:
                                digits=6 if kind=='Rotation' else 5
                                curve['Values']=[row if i in [0,len(curve['Values'])-1] else [round(v,digits) for v in row] for i,row in enumerate(curve['Values'])]
                data=json.dumps(doc,ensure_ascii=False,separators=(',',':')).encode()
            else:continue
            write(STAGE/'resources/AnimationData'/name,data)
        for n in ['ResourceMarker.cs','ScCsgoResources.csproj']:write(STAGE/'resources'/n,(QUALITY/'resources'/n).read_bytes())
        write(STAGE/'original-core.dll',z.read('ScCsgoKnives.dll'))
    write(STAGE/'asset-derivation.json',json.dumps(record,ensure_ascii=False,indent=2).encode())
    for owner in ['core','agents']:
        with zipfile.ZipFile(STAGE/(owner+'-assets.zip'),'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
            for p in sorted((STAGE/owner/'assets').rglob('*')):
                if p.is_file():z.write(p,p.relative_to(STAGE/owner/'assets').as_posix())
        print(owner,'external assets', (STAGE/(owner+'-assets.zip')).stat().st_size,flush=True)

if __name__=='__main__':main()
