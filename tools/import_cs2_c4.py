"""Import the user's installed CS2 C4 export, preserving original image resolution.
The source export is produced by Source2Viewer-CLI from the installed pak01 VPK.
"""
import hashlib, json, re, shutil, subprocess, sys
import imageio_ffmpeg
from pathlib import Path
from PIL import Image
import cs2_viewmodel as vm
from cs2_dmx_to_rig import curve, r6, read_events
from cs2_glb_to_skinned import convert

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT.parent / 'CSMCReverse/local_cs2_analysis/all_weapons/13_c4'
DATA = ROOT / 'src/ScCsgoKnives/AnimationData'
TEX = ROOT / 'src/ScCsgoKnives/Assets/Textures/ScCsgoKnives'
AUDIO = ROOT / 'src/ScCsgoKnives/Assets/Audio/ScCsgoKnives'

def main():
    vm.ROOTS = [SOURCE]
    folder = 'equipment/c4'
    ref = vm.load_clip(vm.clip_path(folder, 'idle_c4'))
    skeleton = [dict(Index=i, Name=b.name, Parent=b.parent, Children=[j for j,c in enumerate(ref.bones) if c.parent == i],
        Translation=list(map(r6, b.rest_position)), Rotation=list(map(r6, b.rest_orientation)), Scale=[1,1,1]) for i,b in enumerate(ref.bones)]
    clips = {}
    for alias, name in {'deploy':'draw_c4','idle':'idle_c4','inspect':'lookat01_c4','inspect2':'lookat02_c4','inspect3':'lookat03_c4','plant':'plant_c4'}.items():
        path = vm.clip_path(folder, name); clip = vm.load_clip(path); assert clip.names == ref.names
        bones = {}
        for b in clip.bones:
            entry = {}
            if b.orientation: entry['Rotation'] = curve(*b.orientation, 'q')
            if b.position: entry['Translation'] = curve(*b.position, 'v')
            if entry: bones[b.name] = entry
        clips[name] = dict(SourceName=name, Alias=alias, FrameRate=r6(clip.frame_rate), FrameCount=clip.frame_count,
            Duration=r6(clip.duration), Events=read_events(path.with_suffix('.vnmclip'), clip.frame_rate), Bones=bones)
    blob, joints, stats = convert(SOURCE/'glb/weapon_c4.glb')
    (DATA/'c4.cs2.skin').write_bytes(blob)
    doc = dict(Format='ScCsgoKnives.Cs2Animation/1', Units='inch', MeshParts=[], Bindings=[], Skinned='c4.cs2.skin', Skeleton=skeleton, Clips=clips,
        Source=dict(folder=folder, plantingSeconds=3.2, animationSeconds=4, notes='The source animation includes the hand recovery after planting.'))
    (DATA/'c4.cs2.animation.json').write_text(json.dumps(doc,separators=(',',':')),encoding='utf-8')
    (DATA/'equipment.json').write_text(json.dumps([dict(Name='c4',MeshParts=[],SourceReferenceScale=1,Cs2Only=True,IsC4=True)],indent=2),encoding='utf-8')
    catalog = json.loads((DATA/'cs2_catalog.json').read_text())
    catalog['c4'] = dict(Skinned='c4.cs2.skin',Parts=None,MeshParts=[],Clips={name:{k:v for k,v in c.items() if k!='Bones'} for name,c in clips.items()})
    (DATA/'cs2_catalog.json').write_text(json.dumps(catalog,separators=(',',':')),encoding='utf-8')
    mats = SOURCE/'decompiled/weapons/models/c4/materials'
    for material,_,_ in stats:
        bindings = dict(re.findall(r'"([^"\n]+)"\s+"([^"\n]+)"',(mats/(material+'.vmat')).read_text()))
        colorPath=SOURCE/'decompiled'/bindings.get('TextureColor1',bindings.get('TextureColor',''))
        if not colorPath.is_file(): colorPath=mats/'c4_digits.png'
        color=Image.open(colorPath).convert('RGBA'); size=color.size
        def tex(keys,mode,default):
            path=next((SOURCE/'decompiled'/bindings[k] for k in keys if k in bindings and (SOURCE/'decompiled'/bindings[k]).is_file()),None)
            return Image.open(path).convert(mode).resize(size,Image.Resampling.LANCZOS) if path else Image.new(mode,size,default)
        key='c4_cs2' if material=='weapon_c4' else material
        color.save(TEX/(key+'.png'))
        tex(['TextureNormal'],'RGB',(128,128,255)).save(TEX/(key+'_normal.png'))
        Image.merge('RGB',(tex(['TextureAmbientOcclusion'],'L',255),tex(['TextureRoughness1'],'L',180),tex(['TextureMetalness1'],'L',0))).save(TEX/(key+'_orm.png'))
    shutil.copyfile(ROOT.parent/'CSMCReverse/local_cs2_analysis/all_weapons/11_icons/panorama/images/econ/weapons/base_weapons/weapon_c4_png.png',TEX/'c4_slot.png')
    soundRoot=SOURCE/'decompiled/sounds/weapons/c4'
    for p in soundRoot.iterdir():
        if p.suffix not in ('.wav','.mp3'): continue
        name=p.stem if p.stem.startswith('c4_') else 'c4_'+p.stem
        subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(),'-hide_banner','-loglevel','error','-y','-i',str(p),'-c:a','libvorbis','-q:a','5',str(AUDIO/(name+'.ogg'))],check=True)
    files=[dict(path=str(p.relative_to(SOURCE)),bytes=p.stat().st_size,sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in SOURCE.rglob('*') if p.is_file()]
    (ROOT/'docs/c4-source-109.json').write_text(json.dumps(dict(source=str(SOURCE),joints=joints,primitives=stats,plantSounds=[e for e in clips['plant_c4']['Events'] if e['Class'].endswith('_Sound')],files=files),ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps(dict(clips=len(clips),joints=len(joints),primitives=stats),ensure_ascii=False))

if __name__=='__main__':
    main()
    # The screen is a second skinned primitive; never leave a body-only import.
    subprocess.run([sys.executable,str(ROOT/'tools/complete_c4_assets.py')],check=True)
