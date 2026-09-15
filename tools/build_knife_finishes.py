"""Bake CS2-authored patterns/masks for shipped knife UVs; copy official icons.

Local material port, not Valve's compositor: fixed pattern placement, unworn
coat, no Source 2 pearlescence. Factory artwork and icons are not tinted.
"""
import json, shutil, sys
from pathlib import Path
import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt
from build_gun_skins import parse_vmat, recipe_parameters, find_one, vec, load_rgb, save_rgb, clean_coat, pattern_colors
from gun_skin_reproject import raster_surface, lookup
from cs2_glb import Glb
from stage_knife_finishes import STAGE, ROOT, sha

TEX=ROOT/'src/ScCsgoKnives/Assets/Textures/ScCsgoKnives'
EXPORT=ROOT.parent/'CSMCReverse/local_cs2_analysis/all_weapons/09_knives'
# Visual adjustment for the reported pale finish; not a measured lighting match.
# Tone only the coated finish pixels; leave the factory substrate intact.
FINISH_TONE = 0.86

def main():
    sys.stdout.reconfigure(encoding='utf-8')
    source=json.loads((STAGE/'source-manifest.json').read_text('utf-8')); report=[]
    for row in source['rows']:
        if not row['finish']:continue
        asset,key=row['asset'],row['finish']; root=EXPORT/'decompiled/weapons/models/knife'/('knife_'+asset)
        glb=EXPORT/'glb/weapons/models/knife'/('knife_'+asset)/('weapon_knife_'+asset+'.glb')
        composite=next((root/'materials/composite_inputs').glob('*composite_inputs.vmat'))
        inputs=parse_vmat(composite)
        maskpath=find_one(root,inputs['TextureMasks1'])
        basepath=TEX/f'{asset}_cs2.png'; size=Image.open(basepath).width
        base=load_rgb(basepath,size); masks=load_rgb(maskpath,size); coverage=masks[...,0]
        no_paint=None
        if inputs.get('TextureNoPaint1'):
            no_paint=find_one(root,inputs['TextureNoPaint1']);coverage*=1-load_rgb(no_paint,size)[...,0]
        recipe=STAGE/'decoded'/row['recipes'][0][:-2]
        p,mat=recipe_parameters(recipe,STAGE/'decoded'); pattern=find_one(STAGE/'decoded',p['TexturePattern'])
        v,u=(np.mgrid[:size,:size]+.5)/size
        if key.startswith('am_gamma') or key == 'am_ruby_marbleized':
            flat=pattern_colors(load_rgb(pattern),u,v,p)
            flat*=float(p.get('g_flColorBrightness',1))**(1/2.2)
            placement='CS2 pattern RGB masks, recipe colors/rotation/scale; fixed UV placement'
        elif key == 'aa_fade':
            mesh=next(m for m in Glb(glb).meshes() if 'body' in m.name)
            prim=mesh.primitives[0];a=prim.attributes
            xyz,ids=raster_surface(dict(positions=a['POSITION'],uv=a['TEXCOORD_0'],faces=prim.indices.reshape(-1,3)),size)
            near=distance_transform_edt(ids<0,return_distances=False,return_indices=True);xyz=xyz[tuple(near)]
            # Continuous blade-space projection using CS2's actual fade mask.
            # One fixed full-fade layout; not a claim about a Valve pattern seed.
            angle=np.deg2rad(float(p['g_flPatternTexCoordRotation']))
            longitudinal=xyz[...,2]*np.cos(angle)+xyz[...,1]*np.sin(angle)
            valid=(coverage>.5)&(ids>=0);lo,hi=np.percentile(longitudinal[valid],[1,99])
            t=np.clip((longitudinal-lo)/max(hi-lo,1e-6),0,1)
            w=lookup(load_rgb(pattern),np.stack([1-t,np.full_like(t,.5)],-1))
            flat=np.broadcast_to(vec(p['g_vColor0']),w.shape).copy()
            for channel in range(3):flat=flat*(1-w[...,channel:channel+1])+vec(p[f'g_vColor{channel+1}'])*w[...,channel:channel+1]
            placement='CS2 fade texture and recipe colors, fixed continuous blade-space projection; not Valve seed/fade percent'
        else:
            raise ValueError('Unsupported paint composition: '+key)
        color=clean_coat(np.clip(flat * FINISH_TONE, 0, 1),base,coverage)
        orm=load_rgb(TEX/f'{asset}_cs2_orm.png',size)
        orm[...,1]=orm[...,1]*(1-coverage)+float(p['g_flPaintRoughness'])*coverage
        orm[...,2]=orm[...,2]*(1-coverage)+coverage
        stem=f'{asset}_finish__{key}'
        save_rgb(color,TEX/f'{stem}.png');save_rgb(orm,TEX/f'{stem}_orm.png')
        shutil.copyfile(TEX/f'{asset}_cs2_normal.png',TEX/f'{stem}_normal.png')
        icon=STAGE/'decoded'/row['icon'].replace('.vtex_c','.png')
        # Official pixels and alpha unchanged from the decoded VPK resource.
        shutil.copyfile(icon,TEX/f'{asset}_slot__{key}.png')
        paths=[recipe,mat,pattern,composite,maskpath,glb,basepath,TEX/f'{asset}_cs2_orm.png',TEX/f'{asset}_cs2_normal.png',icon]
        if no_paint:paths.append(no_paint)
        outputs=[TEX/f'{stem}{s}.png' for s in ('','_orm','_normal')]+[TEX/f'{asset}_slot__{key}.png']
        report.append(dict(asset=asset,variant=row['variant'],finish=key,paintId=row['paintId'],placement=placement,finishTone=FINISH_TONE,
            sourceHashes={str(p):sha(p) for p in paths},outputHashes={p.name:sha(p) for p in outputs},
            officialIconUnchanged=sha(icon)==sha(outputs[-1]),coverage=float(coverage.mean())))
        print(asset,key,'coverage',round(float(coverage.mean()),3),flush=True)
    (ROOT/'docs/knife-finishes-source-20260913.json').write_text(json.dumps(dict(sourceManifest=str(STAGE/'source-manifest.json'),
        sourceManifestSha256=sha(STAGE/'source-manifest.json'),note=__doc__,rows=report),ensure_ascii=False,indent=2)+'\n','utf-8')

if __name__=='__main__':main()
