"""Import six append-only CS2 grenade rigs, skins and per-material textures.

VRF export must include animations to preserve skins. Source files are read only.
The attached pin/handle are already weighted in each body; detached pin/spoon
models are debris and are deliberately not duplicated onto the held body.
"""
import argparse
import hashlib
import json
import re
from pathlib import Path
from PIL import Image
import cs2_viewmodel as vm
from cs2_dmx_to_rig import curve, r6, read_events
from cs2_glb_to_skinned import convert
from cs2_glb import Glb

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / 'src/ScCsgoKnives/AnimationData'
TEX = ROOT / 'src/ScCsgoKnives/Assets/Textures/ScCsgoKnives'
KINDS = ['hegrenade', 'flashbang', 'smokegrenade', 'molotov', 'incendiary', 'decoy']
ALIASES = {'deploy':'draw', 'idle':'idle', 'inspect':'lookat01', 'inspect2':'lookat02',
           'pullpin':'pullpin', 'holdHigh':'throwcharge_high', 'holdLow':'throwcharge_low',
           'throwHigh':'throw_overhand', 'throwLow':'throw_underhand'}

def main():
    ap = argparse.ArgumentParser(); ap.add_argument('--source', type=Path, required=True); args = ap.parse_args()
    source = args.source.resolve(); decomp = source / 'decompiled'
    vm.ROOTS = [source]
    audit, manifest = [], []
    def record(p, **extra):
        audit.append(dict(path=p.relative_to(source).as_posix(), sha256=hashlib.sha256(p.read_bytes()).hexdigest(), **extra))
    for kind in KINDS:
        asset = 'grenade_' + kind
        stem = {'decoy':'grenade','smokegrenade':'smoke'}.get(kind,kind)
        folder = 'grenade/_default_grenade' if kind == 'decoy' else 'grenade/grenade_' + kind
        ref = vm.load_clip(vm.clip_path(folder, 'idle_' + stem))
        skeleton = [dict(Index=i, Name=b.name, Parent=b.parent, Children=[j for j,c in enumerate(ref.bones) if c.parent==i],
                         Translation=list(map(r6,b.rest_position)), Rotation=list(map(r6,b.rest_orientation)), Scale=[1,1,1]) for i,b in enumerate(ref.bones)]
        clips = {}
        for alias, prefix in ALIASES.items():
            name = prefix + '_' + stem; p = vm.clip_path(folder,name); c = vm.load_clip(p)
            assert c.names == ref.names, name
            bones = {}
            for b in c.bones:
                entry = {}
                if b.orientation: entry['Rotation'] = curve(*b.orientation,'q')
                if b.position: entry['Translation'] = curve(*b.position,'v')
                if entry: bones[b.name] = entry
            events = read_events(p.with_suffix('.vnmclip'),c.frame_rate)
            clips[name] = dict(SourceName=name, Alias=alias, SourceFile=p.relative_to(source).as_posix(), FrameRate=r6(c.frame_rate),
                               FrameCount=c.frame_count, Duration=r6(c.duration), Events=events, Bones=bones)
            record(p, alias=alias, events=events); record(p.with_suffix('.vnmclip'))
        model = 'weapon_incendiarygrenade' if kind=='incendiary' else 'weapon_' + kind
        p = source / ('glb/weapons/models/grenade/'+kind+'/'+model+'.glb')
        blob,joints,stats = convert(p); (DATA/(asset+'.cs2.skin')).write_bytes(blob); record(p, joints=joints, primitives=stats)
        # CS2's cloth/liquid helper joints are procedural, absent from DMX.
        # Preserve their exported local bind beneath the animated parent instead
        # of letting weighted cloth vertices collapse to zero at runtime.
        nodes = Glb(p).json['nodes']
        for node_index,node in enumerate(nodes):
            if node.get('name') not in ('molotov_rag_jiggle','liquid_surface'): continue
            parent = next(n['name'] for n in nodes if node_index in n.get('children',[]))
            parent_index = next(b['Index'] for b in skeleton if b['Name']==parent)
            index=len(skeleton); skeleton[parent_index]['Children'].append(index)
            skeleton.append(dict(Index=index,Name=node['name'],Parent=parent_index,Children=[],
                Translation=[r6(v*39.370079) for v in node.get('translation',[0,0,0])],
                Rotation=node.get('rotation',[0,0,0,1]),Scale=[1,1,1]))
        doc = dict(Format='ScCsgoKnives.Cs2Animation/1', Units='inch', MeshParts=[], Bindings=[], Skinned=asset+'.cs2.skin',
                   Skeleton=skeleton, Clips=clips, Source=dict(folder=folder, releaseTiming='CS2 .Throw sound event; no gameplay release ID is exported',
                   sharedDebris='Body includes pin/ring/handle; separate detached pin/spoon are not attached a second time.',
                   proceduralJoints='Molotov rag/liquid follow their exported local bind under animated molotov; cloth simulation is not ported.'))
        (DATA/(asset+'.cs2.animation.json')).write_bytes(json.dumps(doc,separators=(',',':')).encode())
        for mat,_,_ in stats:
            p = decomp/('weapons/models/grenade/'+kind+'/materials/'+mat+'.vmat')
            bindings = dict(re.findall(r'"([^"\n]+)"\s+"([^"\n]+)"',p.read_text('utf-8'))); record(p)
            def texture(keys,mode,default):
                path = next((decomp/bindings[k] for k in keys if k in bindings and (decomp/bindings[k]).is_file()),None)
                if path:
                    record(path); return Image.open(path).convert(mode).resize((512,512),Image.Resampling.LANCZOS)
                return Image.new(mode,(512,512),default)
            color = texture(['TextureColor1','TextureColorA','TextureColor'],'RGB',(180,180,180))
            normal = texture(['TextureNormal','TextureNormalA'],'RGB',(128,128,255))
            ao = texture(['TextureAmbientOcclusion'],'L',255)
            rough = texture(['TextureRoughness1','TextureRoughnessA','TextureRoughness'],'L',150)
            metal = texture(['TextureMetalness1','TextureMetalnessA','TextureMetalness'],'L',0)
            if mat.endswith('_flame'):
                color = color.convert('RGBA'); color.putalpha(texture(['TextureTranslucency'],'L',180))
            key = asset+'_cs2' if mat == model else mat
            color.save(TEX/(key+'.png')); normal.save(TEX/(key+'_normal.png')); Image.merge('RGB',(ao,rough,metal)).save(TEX/(key+'_orm.png'))
        manifest.append(dict(Name=asset,MeshParts=[],SourceReferenceScale=1,Cs2Only=True,IsGrenade=True))
        print(asset,len(clips),'clips',len(joints),'joints',stats,flush=True)
    (DATA/'grenades.json').write_bytes(json.dumps(manifest,indent=2).encode())
    (ROOT/'docs/survival-grenade-sources.json').write_bytes(json.dumps(dict(tool='ValveResourceFormat 20.0',source=str(source),files=audit),ensure_ascii=False,indent=2).encode('utf-8'))

if __name__=='__main__': main()
