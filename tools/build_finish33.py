"""User-approved light icons + native-UV local bake. Not Valve wear/pearl composition.
Preserves existing assets. Generates new OBJ geometry, material maps and deterministic metadata.
"""
import argparse,json,sys,hashlib,re
from pathlib import Path
import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt
from cs2_glb import Glb
from cs2_glb_to_obj import write_obj,MAX_FACES
from build_gun_skins import load_rgb,save_rgb,vec,pattern_colors,parse_vmat,recipe_parameters,find_one,clean_coat
from gun_skin_reproject import body,raster_surface

ROOT=Path(__file__).resolve().parents[1]
EXPORT=ROOT.parent/'CSMCReverse/local_cs2_analysis/all_weapons'
SOURCE=ROOT/'.tmp/finish33-source-20260910'
TEX=ROOT/'src/ScCsgoKnives/Assets/Textures/ScCsgoKnives'
MODEL=ROOT/'src/ScCsgoKnives/Assets/Models/ScCsgoKnives'
DATA=ROOT/'src/ScCsgoKnives/AnimationData'
ALIASES={'m4a1':'m4a4','glock':'glock18'}

def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def texpath(root,ref):
    # Decoded VTEX names preserve the hash; authoring PSD/TGA names do not.
    stem=Path(ref).stem.lower().replace('.','_')
    files=[p for p in root.rglob('*.png') if p.stem.lower()==stem or p.stem.lower().startswith(stem+'_')]
    if not files:raise FileNotFoundError((root,ref))
    return min(files,key=lambda p:(len(p.name),str(p)))
def material_maps(glb,mat,size):
    m=next(x for x in glb.json['materials'] if x['name']==mat)
    p=m.get('pbrMetallicRoughness',{})
    def image(slot,default):
        if not slot:return np.broadcast_to(default,(size,size,len(default))).copy().astype(np.float32)
        im=glb.json['images'][glb.json['textures'][slot['index']]['source']]
        return load_rgb(glb.path.parent/im['uri'],size)
    color=image(p.get('baseColorTexture'),p.get('baseColorFactor',[.25,.25,.25])[:3])
    mr=image(p.get('metallicRoughnessTexture'),[1,p.get('roughnessFactor',.5),p.get('metallicFactor',0)])
    ao=image(m.get('occlusionTexture'),[1,1,1])[...,0]
    normal=image(m.get('normalTexture'),[.5,.5,1])
    return color,np.stack([ao,mr[...,1],mr[...,2]],-1),normal

def geometry(asset,glb,suffix):
    index,mesh=next((i,m) for i,m in enumerate(glb.meshes()) if m.name.endswith('body_'+suffix))
    skin=glb.mesh_skin(index);joints=skin['joints'];parts=[];seams=0
    inches=np.diag([39.370079]*3+[1]);inv=[np.linalg.inv(inches)@m@inches for m in skin['inverse_bind']]
    # Match the normalized bind-space used by existing rigid inventory guns, not the three historical OBJ guns.
    allpos=np.concatenate([p.attributes['POSITION'] for p in mesh.primitives])*39.370079
    lo,hi=allpos.min(0),allpos.max(0);center=(lo+hi)/2;extent=(hi-lo).max()
    n_to_rig=np.diag([extent]*3+[1.]);n_to_rig[3,:3]=center
    rig=json.loads((DATA/f'{asset}.cs2.animation.json').read_text('utf-8'));bones={b['Name'] for b in rig['Skeleton']}
    for pi,p in enumerate(mesh.primitives):
        if p.material=='sticker_gaps':continue
        a=p.attributes;pos=(a['POSITION']*39.370079-center)/extent;uv=a['TEXCOORD_0'];nor=a['NORMAL']
        weights=a['WEIGHTS_0'];owner=a['JOINTS_0'][np.arange(len(pos)),weights.argmax(1)]
        tris=p.indices.reshape(-1,3);owners=owner[tris]
        dominant=np.where(owners[:,1]==owners[:,2],owners[:,1],owners[:,0]);seams+=int(np.any(owners!=owners[:,:1],axis=1).sum())
        for joint in np.unique(dominant):
            selected=tris[dominant==joint]
            for offset in range(0,len(selected),MAX_FACES):
                chunk=selected[offset:offset+MAX_FACES];used=np.unique(chunk);remap=np.full(len(pos),-1);remap[used]=np.arange(len(used))
                name=f'{joints[joint]}__p{pi}_c{offset//MAX_FACES}'
                filename=f'{asset}_legacy_cs2_{name}.obj'
                write_obj(MODEL/filename,name,pos[used],uv[used],nor[used],remap[chunk])
                bindjoint=joint if joints[joint] in bones else joints.index('weapon_offset')
                parts.append({'Name':name,'Bone':joints[bindjoint],'SourceMaterial':p.material,
                              'Material':None,'RightMatrix':(n_to_rig@inv[bindjoint]).ravel().tolist()})
    return mesh,parts,seams

def main():
    sys.stdout.reconfigure(encoding='utf-8');ap=argparse.ArgumentParser();ap.add_argument('--size',type=int,default=1024);ap.add_argument('--only',nargs='*');args=ap.parse_args();size=args.size
    manifest=json.loads((SOURCE/'source-manifest.json').read_text('utf-8'))
    audit=json.loads((ROOT/'.tmp/requested-skins-emerald-only-20260910.json').read_text('utf-8-sig'))
    fields={int(m['id']):m['fields'] for r in audit['rows'] for m in r['matches']}
    native=json.loads((DATA/'gun_native_meshes.json').read_text('utf-8'));extra=[];report={};done={}
    if args.only:
        extra=json.loads((DATA/'gun_additional_skins.json').read_text('utf-8'))
        report=json.loads((ROOT/'docs/finish33-bake-manifest.json').read_text('utf-8'))
    for skin in manifest['finishes']:
        asset=ALIASES.get(skin['gun'],skin['gun']);key=skin['key'];stem=f'{asset}_hd__{key}';pid=skin['paintId']
        if args.only and key not in args.only:continue
        glb=Glb(skin['sourceGlb']);recipe=SOURCE/'decoded'/skin['recipes'][0][:-2]
        # Reuse old parser for authoring values; source-only stage holds exact compiled texture names too.
        p,matpath=recipe_parameters(recipe,SOURCE/'decoded')
        pat=texpath(SOURCE/'decoded',p['TexturePattern']) if 'TexturePattern' in p else None
        style=int(p.get('F_PAINT_STYLE',int(fields[pid].get('style',1))-1));native_body=skin['body']=='legacy'
        if native_body:
            if asset not in done:done[asset]=geometry(asset,glb,'legacy')
            mesh,parts,seams=done[asset]
        else:
            mesh=next(m for m in glb.meshes() if m.name.endswith('body_hd'));parts=[];seams=0
        main_material=next(x.material for x in mesh.primitives if not x.material.startswith('weapon_')) if native_body else mesh.primitives[0].material
        base,orm,normal=material_maps(glb,main_material,size)
        v,u=(np.mgrid[:size,:size]+.5)/size
        # Native artwork carries full painted/unpainted body. No factory luminance multiplication.
        if style in (6,8):
            color=load_rgb(pat,size)
            coverage=np.ones((size,size),np.float32)
        else:
            folder=EXPORT/'04_current_weapon_materials/weapons/models'/glb.path.parent.name/'materials/composite_inputs'
            maskfiles=list(folder.glob('*_masks.png'))
            if native_body:
                # Legacy finishes in this batch are artwork except Glock Emerald. Use the native legacy paint mask
                # (extracted separately below), never apply an HD mask to a legacy atlas.
                masks=list((ROOT/'.tmp/finish33-inputs/decoded').rglob(f'*{asset}*masks*.png'))
                if asset=='glock18':masks=list((ROOT/'.tmp/finish33-inputs/decoded').rglob('*glock*masks*.png'))
                if not masks:raise FileNotFoundError('legacy paint coverage for '+asset)
                maskfiles=masks
            if not maskfiles:raise FileNotFoundError('paint coverage for '+asset)
            mask_channels=load_rgb(maskfiles[0],size)
            coverage=mask_channels[...,0]
            if not native_body:
                input_files=list(folder.glob('*_composite_inputs.vmat'))
                if input_files:
                    inputs=parse_vmat(input_files[0])
                    if 'TextureNoPaint1' in inputs:
                        coverage*=1-load_rgb(texpath(EXPORT/'04_current_weapon_materials',inputs['TextureNoPaint1']),size)[...,0]
            if style==0:
                flat=np.broadcast_to(vec(p.get('g_vColor0')),(size,size,3)).copy()
                for channel in range(3):
                    weight=mask_channels[...,channel:channel+1]
                    flat=flat*(1-weight)+vec(p.get(f'g_vColor{channel+1}',p.get('g_vColor0')))*weight
            elif style==3:flat=np.broadcast_to(vec(p.get('g_vColor0'),(.65,.02,.02)),(size,size,3))
            elif style==2:
                # Spray paints use object-space projection across UV islands, and paint
                # all surfaces except the authored NoPaint mask (R is a colour-region mask).
                primitive=next(x for x in mesh.primitives if x.material==main_material)
                a=primitive.attributes
                surface={'positions':a['POSITION'],'uv':a['TEXCOORD_0'],'faces':primitive.indices.reshape(-1,3)}
                xyz,ids=raster_surface(surface,size)
                nearest=distance_transform_edt(ids<0,return_distances=False,return_indices=True)
                xyz=xyz[tuple(nearest)]*39.370079
                # GLB axes: Y up, Z barrel. Normalize with the weapon's own source length;
                # unlike an atlas sample this keeps the wood grain continuous along the gun.
                inputs=parse_vmat(next(folder.glob('*_composite_inputs.vmat')))
                length=float(inputs['g_flWeaponLength1'])
                flat=pattern_colors(load_rgb(pat),xyz[...,1]/length,xyz[...,2]/length,p)
                coverage=1-load_rgb(texpath(EXPORT/'04_current_weapon_materials',inputs['TextureNoPaint1']),size)[...,0]
            else:flat=pattern_colors(load_rgb(pat),u,v,p)
            color=clean_coat(flat,base,coverage)
        rough=float(p.get('g_flPaintRoughness',.4));metal=1.0 if style in (3,4,5) else .65 if style in (8,9) else 0.0
        orm[...,1]=orm[...,1]*(1-coverage)+rough*coverage;orm[...,2]=orm[...,2]*(1-coverage)+metal*coverage
        if 'TextureNormal' in p and int(p.get('F_OVERRIDE_NORMAL',0))==1:
            normal=load_rgb(texpath(SOURCE/'decoded',p['TextureNormal']),size)
        # New-style recipes have explicit authored AO/roughness/normal resources, not legacy packed alpha.
        if not native_body and style==8:
            from cs2_kv3 import load,walk
            vars={n['m_strName']:n for n in walk(load(recipe)) if 'm_strName' in n and 'm_strTextureContentAssetPath' in n}
            for name in ('g_tPaintRoughness','g_tFinalAmbientOcclusion','g_tNormal'):
                if name in vars:
                    a=load_rgb(texpath(SOURCE/'decoded',vars[name]['m_strTextureContentAssetPath']),size)
                    if name=='g_tNormal':normal=a
                    else:orm[...,1 if name=='g_tPaintRoughness' else 0]=a[...,0]
            # Some submitted normal maps remain only as dependencies; prefer the author's supplied normal.
            normfiles=[x for x in (SOURCE/'decoded').rglob('*.png') if key in x.name and 'normal' in x.name]
            if normfiles:normal=load_rgb(normfiles[0],size)
        # Preserve dedicated hardware, shell and HD add-on materials on multi-material legacy guns.
        if native_body:
            hardware_done=set()
            for part in parts:
                material=part['SourceMaterial']
                # AUG's shared_scope is an internal optical surface, not its painted scope housing.
                painted=material==main_material or material in ('rif_aug_scope','snip_ssg08_scope')
                if material=='shared_scope_lens':
                    part['Material']='cs2_scope_lens'
                    continue
                if material=='shared_scope':
                    part['Material']='cs2_legacy_scope'
                    continue
                if not painted:
                    hardware=f'finish33_{asset}_{material}'
                    if hardware not in hardware_done:
                        maps=material_maps(glb,material,size)
                        for suffix,a in zip(('', '_orm','_normal'),maps):save_rgb(a,TEX/(hardware+suffix+'.png'))
                        hardware_done.add(hardware)
                    part['Material']=hardware
            native[asset]=[{k:v for k,v in part.items() if k!='SourceMaterial'} for part in parts]
        n=normal*2-1;n/=np.maximum(np.linalg.norm(n,axis=-1,keepdims=True),1e-6);normal=(n+1)/2
        for suffix,a in zip(('', '_orm','_normal'),(color,orm,normal)):save_rgb(a,TEX/(stem+suffix+'.png'))
        icon=next(x for x in (SOURCE/'decoded').rglob('*.png') if x.name.lower()==f'weapon_{skin["gun"]}_{key}_light_png.png'.lower())
        image=Image.open(icon).convert('RGBA');side=max(image.size);square=Image.new('RGBA',(side,side));square.paste(image,((side-image.width)//2,(side-image.height)//2));square.resize((128,128),Image.Resampling.LANCZOS).save(TEX/f'{asset}_slot__{key}.png')
        name=skin['name'] if asset!='glock18' else '伽玛多普勒·翡翠'
        entry={'paintId':pid,'key':key,'name':name,'gun':asset,'legacy':native_body,'tier':'Premium' if style in (6,8) else 'Standard'}
        if args.only and any(x['paintId']==pid for x in extra):extra[next(i for i,x in enumerate(extra) if x['paintId']==pid)]=entry
        else:extra.append(entry)
        report[key]={'paintId':pid,'style':style,'body':skin['body'],'sourceMinimumWear':skin['wear'],
            'wearPolicy':'light icon + local clean bake; no exact wear simulation or pearlescence; not Factory New guarantee',
            'sourceRecipeSha256':sha(recipe),'sourceGlbSha256':sha(glb.path),'seamTriangles':seams,
            'files':{f'{stem}{suffix}.png':sha(TEX/f'{stem}{suffix}.png') for suffix in ('','_orm','_normal')}}
        print(asset,key,'parts',len(parts),'style',style,flush=True)
        # Checkpoint metadata only after a finish is fully baked; retry never advertises a partial material.
        (ROOT/'.tmp/finish33-partial.json').write_text(json.dumps({'native':native,'extra':extra,'report':report},ensure_ascii=False),'utf-8')
    (DATA/'gun_native_meshes.json').write_text(json.dumps(native,ensure_ascii=False,indent=1)+'\n','utf-8')
    (DATA/'gun_additional_skins.json').write_text(json.dumps(extra,ensure_ascii=False,indent=1)+'\n','utf-8')
    (ROOT/'docs/finish33-bake-manifest.json').write_text(json.dumps(report,ensure_ascii=False,indent=1)+'\n','utf-8')

if __name__=='__main__':main()
