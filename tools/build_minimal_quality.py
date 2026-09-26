"""Restore Lite gun geometry/512 colour; remove chicken."""
from pathlib import Path
import json,zipfile,hashlib,copy,io,os
from build_minimal_resources import curve_reduce
from PIL import Image
root=Path(__file__).resolve().parents[1]
old=root/'.tmp/minimal-130-20260926';inspect=os.environ.get('SC_MINIMAL_INSPECT')=='1'
stage=root/('.tmp/minimal-inspect40-130-20260926' if inspect else '.tmp/minimal-quality40-130-20260926')
def sha(b):return hashlib.sha256(b).hexdigest()
def write(p,b):p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b)
def jbytes(o):return json.dumps(o,ensure_ascii=False,separators=(',',':')).encode()
# All gun assets, not just the thirteen extra finishes.
gun_names={p.name.removeprefix('Game.AnimationData.').split('.cs2.')[0] for p in (root/'.tmp/lite-smooth-20260926/embedded').glob('*.parts')}
gun_names|={'ak47','m4a1s','awp'}
record=copy.deepcopy(json.loads((old/'resource-derivation.json').read_bytes()))
record.update(profile='quality-512-no-chicken',inspect=inspect,files={},qualityChanges=[],inspectCurves=[])
base_files=json.loads((old/'resource-derivation.json').read_bytes())['files']
with zipfile.ZipFile(root/'output/[API1.9]CS武器1.3.0-轻量包.scmod') as lite:
    assert sha((root/'output/[API1.9]CS武器1.3.0-轻量包.scmod').read_bytes())==record['sourceSha256']
    for n in base_files:
        data=(old/'package'/n).read_bytes();assert sha(data)==base_files[n]
        if 'chicken' in n.lower():
            record['qualityChanges'].append(dict(path=n,operation='remove chicken',before=len(data)));continue
        stem=Path(n).stem
        if n.endswith('.obj') and (stem.split('_legacy_cs2_')[0].split('_cs2_')[0] in gun_names):
            data=lite.read(n);record['qualityChanges'].append(dict(path=n,operation='restore Lite gun geometry',sha256=sha(data)))
        if n.startswith('Assets/Textures/ScCsgoKnives/') and n.endswith('.webp'):
            # Restore colour maps used by guns (factory and available finishes), and default hands.
            colour=stem.endswith('_hd') or '_hd__' in stem or stem in ['cs2_arm','cs2_glove']
            colour=colour and not stem.endswith(('_normal','_orm'))
            if colour:
                original=lite.read(n)
                with Image.open(io.BytesIO(original)) as image:assert max(image.size)<=512
                data=original;record['qualityChanges'].append(dict(path=n,operation='restore Lite colour',sha256=sha(data)))
        write(stage/'package'/n,data);record['files'][n]=sha(data)

for p in (old/'resources/AnimationData').iterdir():
    data=p.read_bytes();asset=p.name.split('.cs2.')[0]
    if p.suffix in ['.skin','.parts'] and asset in gun_names:
        data=(root/'.tmp/lite-smooth-20260926/embedded'/('Game.AnimationData.'+p.name)).read_bytes()
        record['qualityChanges'].append(dict(path='embedded/'+p.name,operation='restore Lite gun geometry',sha256=sha(data)))
    if inspect and p.name.endswith('.animation.json'):
        doc=json.loads(data);original=json.loads((root/'.tmp/lite-smooth-20260926/embedded'/('Game.AnimationData.'+p.name)).read_bytes())
        for name,clip in original['Clips'].items():
            if not any(term in (name+' '+str(clip.get('Alias',''))).lower() for term in ['inspect','lookat']):continue
            reduced=copy.deepcopy(clip);row=dict(asset=asset,clip=name,keysRemoved=0,translationError=0,rotationError=0)
            for bone in reduced.get('Bones',{}).values():
                for kind,curve in bone.items():
                    if curve and kind in ['Rotation','Translation']:
                        removed,error=curve_reduce(curve,kind=='Rotation');row['keysRemoved']+=removed
                        key='rotationError' if kind=='Rotation' else 'translationError';row[key]=max(row[key],error)
            doc['Clips'][name]=reduced;record['inspectCurves'].append(row)
        data=jbytes(doc)
    write(stage/'resources/AnimationData'/p.name,data)
for n in ['ResourceMarker.cs','ScCsgoResources.csproj']:write(stage/'resources'/n,(old/'resources'/n).read_bytes())
write(stage/'resource-derivation.json',json.dumps(record,ensure_ascii=False,indent=2).encode())
print(len(record["qualityChanges"]),"quality changes")
