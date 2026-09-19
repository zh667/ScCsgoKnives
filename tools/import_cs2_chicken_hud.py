"""Import local ValveResourceFormat exports, preserving the source extraction.
Run with dev.ps1 and .tmp/skin-bake-deps (Pillow, numpy, soundfile, resvg-py).
"""
from pathlib import Path
import hashlib,json,struct,io
import resvg_py,soundfile as sf
from PIL import Image
from cs2_glb import Glb
from chicken_in_place import remove_root_travel

ROOT=Path(__file__).resolve().parent.parent
SOURCE=ROOT/'.tmp/cs2-chicken-hud-20260919'
ASSETS=ROOT/'src/ScCsgoKnives/Assets'
records=[]
def record(src,dst):
    records.append({'source':src.relative_to(SOURCE).as_posix(),'sourceSha256':hashlib.sha256(src.read_bytes()).hexdigest(),
                    'target':dst.relative_to(ASSETS).as_posix(),'sha256':hashlib.sha256(dst.read_bytes()).hexdigest()})
for name in ['magazine','banana_mag','bizon_tube','box','generic_bullet','p90','revolver_loader','shotgun_shell']:
    p=SOURCE/f'panorama/images/hud/ammo_reserve_{name}.svg';dst=ASSETS/f'Textures/ScCsgoKnives/hud_ammo_{name}.png'
    dst.write_bytes(resvg_py.svg_to_bytes(svg_path=str(p),width=128,height=128,skip_system_fonts=True));record(p,dst)
for kind in ['idle','death']:
    dest=ASSETS/f'Audio/ScCsgoKnives/Chicken/{kind}';dest.mkdir(parents=True,exist_ok=True)
    for p in sorted((SOURCE/'sounds/ambient/common/animal').glob(f'chicken_{kind}_*.mp3')):
        samples,rate=sf.read(p);dst=dest/(p.stem+'.wav');sf.write(dst,samples,rate,subtype='PCM_16');record(p,dst)
src=SOURCE/'models/chicken/chicken.glb';g=Glb(src);j=g.json
# Native engine uses the color texture; ORM/normal are retained in the source
# extraction. Embed the one opaque albedo to avoid external glTF image lookups.
image=src.parent/'chicken_catalan_tan_color_psd_37a4e69b.png'
png=io.BytesIO();Image.open(image).convert('RGB').save(png,format='PNG');pixels=png.getvalue()
blob=g.bin;blob+=b'\0'*(-len(blob)%4);view=len(j['bufferViews']);j['bufferViews'].append({'buffer':0,'byteOffset':len(blob),'byteLength':len(pixels)})
blob+=pixels;blob+=b'\0'*(-len(blob)%4)
j['buffers']=[{'byteLength':len(blob)}];j['images']=[{'bufferView':view,'mimeType':'image/png'}];j['textures']=[{'source':0}]
j['materials']=[{'name':'CS2 Chicken Catalan Tan','pbrMetallicRoughness':{'baseColorTexture':{'index':0},'metallicFactor':0,'roughnessFactor':.8},'doubleSided':True}]
for mesh in j['meshes']:
    # Source morphs all have zero default weight and none of our clips animate
    # weights. Omit these unused targets (the engine allocates a GPU texture for
    # each), while preserving the original source and all skeletal animation.
    assert not any(mesh.get('weights',[]))
    for p in mesh['primitives']:
        p['material']=0
        p.pop('targets',None)
# Export only the three clips used by this creature. The untouched GLB retains
# all original clips. Unreferenced accessor bytes are harmless and kept stable.
keep={'chick_idle01':'idle','chick_walk01':'walk','chick_run01':'run'}
j['animations']=[a for a in j['animations'] if a['name'].split('/')[-1] in keep]
for a in j['animations']:a['name']=keep[a['name'].split('/')[-1]]
remove_root_travel(j)
js=json.dumps(j,separators=(',',':')).encode();js+=b' '*(-len(js)%4)
dst=ASSETS/'Models/ScCsgoKnives/chicken.glb';dst.write_bytes(struct.pack('<4sII',b'glTF',2,28+len(js)+len(blob))+struct.pack('<II',len(js),0x4e4f534a)+js+struct.pack('<II',len(blob),0x004e4942)+blob);record(src,dst)
texture=ASSETS/'Textures/ScCsgoKnives/chicken.png';texture.write_bytes(pixels);record(image,texture)
config={'template':'Simple','rootBoneRotation':180,'modelScale':1,
        'animations':{n:{'source':n,'speed':1,'loop':True,'blendDuration':.15} for n in keep.values()},
        'states':{'gait':{'layer':'Base','rules':[{'condition':'IsDead','animation':None},{'condition':'[SpeedAbs] > 1.8','animation':{'source':'run'}},{'condition':'[SpeedAbs] > 0.15','animation':{'source':'walk'}},{'condition':'true','animation':{'source':'idle'}}]}}}
(ASSETS/'Animations').mkdir(exist_ok=True)
(ASSETS/'Animations/ScChicken.json').write_text(json.dumps(config,indent=2)+'\n')
(ROOT/'docs/chicken-hud-source-20260919.json').write_text(json.dumps({'vpk':'E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/game/csgo/pak01_dir.vpk','records':records,'notes':'CS2 model/skin/idle-walk-run clips and audio; behavior and explosion are authored for Survivalcraft.'},ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print('Imported',len(records),'assets; animated chicken GLB',dst.stat().st_size)
