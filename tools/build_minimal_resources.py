"""Derive standalone Mini assets in .tmp; published Full/Lite are immutable inputs.

Keep curve events/durations and persistent catalogues. This is not a publish tool.
Requires the verified prior Lite model reduction and resource DLL extraction.
"""
import collections
import copy
import hashlib
import io
import json
import math
import shutil
import struct
import zipfile
from pathlib import Path
import numpy as np
import soundfile as sf
from scipy.signal import resample_poly
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
STAGE=ROOT/'.tmp/minimal-130-20260926'
ASSETS=STAGE/'package'
RESOURCE=STAGE/'resources'
LITE=ROOT/'output/[API1.9]CS武器1.3.0-轻量包.scmod'
FULL=ROOT/'output/[API1.9]CS武器1.3.0-全量包.scmod'
def sha(b):return hashlib.sha256(b).hexdigest()
def write(path,data):path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(data)
def json_bytes(value):return json.dumps(value,ensure_ascii=False,separators=(',',':')).encode('utf-8')

def source_names(folder):
    base=ROOT/folder;names=set()
    for p in base.rglob('*'):
        if p.is_file():
            n='Assets/'+p.relative_to(base).as_posix();names.add(n)
            if n.endswith('.png'):names.add(n[:-4]+'.webp')
    return names

def curve_reduce(curve,rotation):
    times=np.asarray(curve['Times'],dtype=float);values=np.asarray(curve['Values'],dtype=float)
    assert len(times)==len(values) and np.isfinite(values).all() and np.all(np.diff(times)>0)
    if len(times)<3:return 0,0
    tol=math.radians(.15) if rotation else .01 # radians / source inches, not scene-space error
    if rotation:
        original=values.copy(); values=values/np.maximum(np.linalg.norm(values,axis=1,keepdims=True),1e-12)
    keep={0,len(times)-1};stack=[(0,len(times)-1)]
    def errors(a,b):
        f=(times[a+1:b]-times[a])/(times[b]-times[a])
        if not rotation:
            pred=values[a]+f[:,None]*(values[b]-values[a]);return np.linalg.norm(values[a+1:b]-pred,axis=1)
        x=values[a];y=values[b];dot=np.dot(x,y)
        if dot<0:y=-y;dot=-dot
        dot=np.clip(dot,-1,1)
        if dot>.9995:pred=x+f[:,None]*(y-x)
        else:
            angle=np.arccos(dot);pred=(np.sin((1-f)*angle)[:,None]*x+np.sin(f*angle)[:,None]*y)/np.sin(angle)
        pred/=np.linalg.norm(pred,axis=1,keepdims=True)
        return 2*np.arccos(np.clip(np.abs(np.sum(pred*values[a+1:b],axis=1)),0,1))
    while stack:
        a,b=stack.pop()
        if b-a<2:continue
        err=errors(a,b);i=int(np.argmax(err))
        if err[i]>tol:
            middle=a+i+1;keep.add(middle);stack.extend([(a,middle),(middle,b)])
    ids=sorted(keep);maximum=0
    for a,b in zip(ids,ids[1:]):
        if b-a>1:maximum=max(maximum,float(max(errors(a,b))))
    assert maximum<=tol+1e-10
    # Retain source values exactly at surviving keys; no additional float quantization.
    curve['Times']=[curve['Times'][i] for i in ids];curve['Values']=[curve['Values'][i] for i in ids]
    return len(times)-len(ids),maximum

def main():
    assert sha(LITE.read_bytes())=='187598bfb07a358d88a60806c80dbb446808fe8663258761a4e38c776f8a3dce'
    ASSETS.mkdir(parents=True,exist_ok=True);RESOURCE.mkdir(parents=True,exist_ok=True)
    reduction=ROOT/'.tmp/minimal-extra-reduction-20260926/derived'
    extracted=ROOT/'.tmp/lite-smooth-20260926/embedded'
    record=dict(sourceSha256=sha(LITE.read_bytes()),removed=[],changed=[],animations=[],audio=[],textures=[],files={})
    core=source_names('src/ScCsgoKnives/Assets')
    addon=source_names('src/ScCsgoTactical/Assets')|source_names('src/ScCsgoAppearance/Assets')
    budget=json.loads((ROOT/'docs/minimal-size-budget-2026-09-26.json').read_text(encoding='utf-8'))
    selected=budget['retainedMainWeaponFinishes'];retained=set(selected.values())
    with zipfile.ZipFile(LITE) as old,zipfile.ZipFile(FULL) as full:
        native=json.loads((ROOT/'src/ScCsgoKnives/AnimationData/gun_native_meshes.json').read_text(encoding='utf-8'))
        catalogue=json.loads((ROOT/'src/ScCsgoKnives/AnimationData/gun_additional_skins.json').read_text(encoding='utf-8'))
        legacy={s['gun'] for s in catalogue if s['key'] in retained and s['legacy']}|{'awp','m4a1s'}
        record['selectedSkins']=selected;record['retainedLegacyModels']=sorted(legacy)
        for item in old.infolist():
            n=item.filename;stem=Path(n).stem
            remove=n in addon and n not in core or n in ['ScCsgoTactical.dll','ScCsgoBundle.dll','Integrations/ScCsgoAppearance.bin'] or n.startswith('Integrations/ScCsgoTactical')
            if n.startswith('Assets/Textures/ScCsgoKnives/') and '__' in stem and any(x in stem for x in ('_hd__','_slot__','_finish__')):
                finish=stem.split('__',1)[1]
                remove|=not any(finish==key or finish.startswith(key+'_') for key in retained)
            if n.endswith('.obj') and '_legacy_cs2_' in n:remove|=Path(n).name.split('_legacy_cs2_')[0] not in legacy
            if n.startswith('Integrations/') and n.endswith('.md'):remove=True
            if n in ['ScCsgoKnives.dll','ScCsgoResources.dll','Assets/ScCsgoResources.xml']:continue
            if remove:
                record['removed'].append(dict(path=n,sha256=sha(old.read(n)),compressedBytes=item.compress_size));continue
            data=old.read(n);target=n
            if n.endswith('.obj') and (reduction/n).exists():data=(reduction/n).read_bytes()
            elif n.startswith('Assets/Textures/') and n.endswith('.webp'):
                # Derive from the original PNG, avoiding a second lossy pass over Lite textures.
                source=n[:-5]+'.png';raw=full.read(source) if source in full.namelist() else data
                image=Image.open(io.BytesIO(raw));image.load();before=image.size
                special=stem in ('weapon_c4_digits','env_specular_rgbm','muzzle_fire','muzzle_smoke')
                if not special:
                    cap=128 if '_slot' in stem or stem.startswith(('ui_','icon_')) else 256
                    if stem=='c4_cs2':image=image.convert('RGB')
                    if max(image.size)>cap:image=image.resize(tuple(max(1,round(x*cap/max(image.size))) for x in image.size),Image.Resampling.LANCZOS)
                    if stem.endswith('_normal'):
                        px=np.array(image.convert('RGBA'));v=px[:,:,:3].astype(float)/127.5-1;length=np.linalg.norm(v,axis=2,keepdims=True)
                        v/=np.maximum(length,1e-6);v[length[:,:,0]<1e-6]=(0,0,1);px[:,:,:3]=np.clip(np.rint((v+1)*127.5),0,255).astype(np.uint8);image=Image.fromarray(px)
                    buf=io.BytesIO();image.save(buf,format='WEBP',quality=80,method=6,exact=True);data=buf.getvalue()
                    record['textures'].append(dict(path=n,sourceSize=before,size=image.size,bytes=len(data)))
            elif n.startswith('Assets/Audio/') and n.endswith(('.wav','.ogg')):
                audio,rate=sf.read(io.BytesIO(data),dtype='float32',always_2d=True)
                mono=np.mean(audio,axis=1);out_rate=min(rate,22050);gcd=math.gcd(rate,out_rate)
                mono=resample_poly(mono,out_rate//gcd,rate//gcd) if rate!=out_rate else mono
                mono=np.clip(mono,-1,1);buf=io.BytesIO();sf.write(buf,mono,out_rate,format='OGG',subtype='VORBIS',compression_level=.65)
                candidate=buf.getvalue();decoded,sr=sf.read(io.BytesIO(candidate));assert sr==out_rate and abs(len(decoded)/sr-len(audio)/rate)<.001
                if len(candidate)<len(data):data=candidate;target=n.rsplit('.',1)[0]+'.ogg'
                record['audio'].append(dict(path=n,target=target,sourceRate=rate,rate=out_rate,channels=audio.shape[1],duration=len(audio)/rate,bytes=len(data)))
            write(ASSETS/target,data);record['files'][target]=sha(data)
            if target!=n or data!=old.read(n):record['changed'].append(dict(path=n,target=target,sourceSha256=sha(old.read(n)),sha256=sha(data)))
        print('External assets derived',len(record['files']),flush=True)
    for path in sorted(extracted.iterdir()):
        name=path.name.removeprefix('Game.AnimationData.')
        if path.suffix in ('.parts','.skin'):
            data=(reduction/'AnimationData'/path.name).read_bytes()
        elif name.endswith('.cs2.animation.json'):
            data=path.read_bytes();doc=json.loads(data);stats=dict(name=name,keysRemoved=0,inspectClips=[],translationError=0,rotationError=0)
            for key,clip in doc['Clips'].items():
                inspect=any(term in (key+' '+str(clip.get('Alias',''))).lower() for term in ('inspect','lookat'))
                if inspect:
                    clip['Bones']={};stats['inspectClips'].append(key);continue
                for bone in clip.get('Bones',{}).values():
                    for kind,curve in bone.items():
                        if curve and kind in ('Rotation','Translation'):
                            removed,error=curve_reduce(curve,kind=='Rotation');stats['keysRemoved']+=removed
                            field='rotationError' if kind=='Rotation' else 'translationError';stats[field]=max(stats[field],error)
            data=json_bytes(doc);stats['bytesBefore']=path.stat().st_size;stats['bytesAfter']=len(data);record['animations'].append(stats)
        else:continue
        write(RESOURCE/'AnimationData'/name,data)
    write(RESOURCE/'ResourceMarker.cs',(ROOT/'src/ScCsgoResources/ResourceMarker.cs').read_bytes())
    project='''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>ScCsgoResources</AssemblyName><Version>1.10.4</Version><AssemblyVersion>1.10.4.0</AssemblyVersion><IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion><GenerateDependencyFile>false</GenerateDependencyFile></PropertyGroup><ItemGroup><EmbeddedResource Include="AnimationData/*"><LogicalName>Game.AnimationData.%(Filename)%(Extension)</LogicalName></EmbeddedResource></ItemGroup></Project>'''
    write(RESOURCE/'ScCsgoResources.csproj',project.encode('utf-8'))
    write(STAGE/'resource-derivation.json',json.dumps(record,ensure_ascii=False,indent=2).encode('utf-8'))
    print('Resource derivation complete',sum(x['keysRemoved'] for x in record['animations']),'keys removed',flush=True)

if __name__=='__main__':main()
