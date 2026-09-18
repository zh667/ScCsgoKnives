"""Bake CS2-authored patterns/masks for shipped knife UVs; copy official icons.

Local material port, not Valve's compositor: fixed pattern placement, unworn
coat, no Source 2 pearlescence. Factory artwork and icons are not tinted.
"""
import argparse, json, shutil, sys
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
# Fixed per-knife pattern windows, not new item IDs or random-on-draw textures.
# These are local composition choices, not claimed Valve seed/Fade percentages.
FADE_WINDOWS = {
    'canis':(.30,.88), 'cord':(.06,.92), 'css':(.36,.90), 'kukri':(.18,.84),
    'navaja':(.02,.94), 'outdoor':(.32,.89), 'skeleton':(.12,.93),
    'stiletto':(.35,.91), 'talon':(.0,.88), 'ursus':(.27,.90),
}

def gem_pattern(pattern, u, v, p):
    """Retain source marble intensity instead of using it solely as a colour selector.

    This is a local material approximation, not Valve's proprietary compositor.
    The same source texels drive colour and veins; no generated noise or painted scratches.
    """
    angle=np.deg2rad(float(p.get('g_flPatternTexCoordRotation',0)))
    scale=float(p.get('g_flPatternTexCoordScale',1));offset=vec(p.get('g_vPatternTexCoordOffset'))
    uv=np.stack([(u*np.cos(angle)-v*np.sin(angle))*scale+offset[0],
                 (u*np.sin(angle)+v*np.cos(angle))*scale+offset[1]],-1)
    sampled=lookup(pattern,uv,wrap=True)
    # Max RGB follows vein brightness without darkening a stripe merely for its hue.
    intensity=sampled.max(axis=-1)
    lo,hi=np.percentile(pattern.max(axis=-1),[2,98])
    strength=np.clip((intensity-lo)/max(hi-lo,1e-6),0,1)
    # User's in-game feedback rejected the first .32 + .78 curve as black/harsh.
    # Keep moderate veins: darkest source areas now retain 65% of recipe colour.
    modulation=.65+.40*strength
    return pattern_colors(pattern,u,v,p)*modulation[...,None]

def main():
    sys.stdout.reconfigure(encoding='utf-8')
    ap=argparse.ArgumentParser();ap.add_argument('--icons',action='store_true');ap.add_argument('--report',type=Path,default=ROOT/'docs/knife-finishes-source-20260918.json');args=ap.parse_args()
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
            # Gamma's source colour masks already carry the phase's veins. The additional
            # intensity modulation read as grime in the user's game; reserve it for Ruby.
            flat=(gem_pattern if key == 'am_ruby_marbleized' else pattern_colors)(load_rgb(pattern),u,v,p)
            flat*=float(p.get('g_flColorBrightness',1))**(1/2.2)
            placement=('CS2 pattern RGB masks plus moderate source marble intensity' if key == 'am_ruby_marbleized' else 'CS2 pattern RGB masks without added vein darkening')+'; recipe colors/rotation/scale; fixed UV placement; local composition'
        elif key == 'aa_fade':
            mesh=next(m for m in Glb(glb).meshes() if 'body' in m.name)
            prim=mesh.primitives[0];a=prim.attributes
            xyz,ids=raster_surface(dict(positions=a['POSITION'],uv=a['TEXCOORD_0'],faces=prim.indices.reshape(-1,3)),size)
            near=distance_transform_edt(ids<0,return_distances=False,return_indices=True);xyz=xyz[tuple(near)]
            # Continuous blade-space projection using CS2's actual fade mask.
            # Stable varied layouts, including pink tips, not a claim about Valve pattern seeds.
            angle=np.deg2rad(float(p['g_flPatternTexCoordRotation']))
            longitudinal=xyz[...,2]*np.cos(angle)+xyz[...,1]*np.sin(angle)
            valid=(coverage>.5)&(ids>=0);lo,hi=np.percentile(longitudinal[valid],[1,99])
            t=np.clip((longitudinal-lo)/max(hi-lo,1e-6),0,1)
            start,end=FADE_WINDOWS[asset];t=start+(end-start)*t
            w=lookup(load_rgb(pattern),np.stack([1-t,np.full_like(t,.5)],-1))
            flat=np.broadcast_to(vec(p['g_vColor0']),w.shape).copy()
            for channel in range(3):flat=flat*(1-w[...,channel:channel+1])+vec(p[f'g_vColor{channel+1}'])*w[...,channel:channel+1]
            placement=f'CS2 fade texture and recipe colors, stable blade-space pattern window {start:.2f}..{end:.2f}; not Valve seed/fade percent'
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
        if args.icons:shutil.copyfile(icon,TEX/f'{asset}_slot__{key}.png')
        paths=[recipe,mat,pattern,composite,maskpath,glb,basepath,TEX/f'{asset}_cs2_orm.png',TEX/f'{asset}_cs2_normal.png',icon]
        if no_paint:paths.append(no_paint)
        outputs=[TEX/f'{stem}{s}.png' for s in ('','_orm','_normal')]+[TEX/f'{asset}_slot__{key}.png']
        report.append(dict(asset=asset,variant=row['variant'],finish=key,paintId=row['paintId'],placement=placement,finishTone=FINISH_TONE,
            sourceHashes={str(p):sha(p) for p in paths},outputHashes={p.name:sha(p) for p in outputs},
            officialIconUnchanged=sha(icon)==sha(outputs[-1]),coverage=float(coverage.mean())))
        print(asset,key,'coverage',round(float(coverage.mean()),3),flush=True)
    args.report.write_text(json.dumps(dict(sourceManifest=str(STAGE/'source-manifest.json'),
        sourceManifestSha256=sha(STAGE/'source-manifest.json'),note=__doc__,rows=report),ensure_ascii=False,indent=2)+'\n','utf-8')

if __name__=='__main__':main()
