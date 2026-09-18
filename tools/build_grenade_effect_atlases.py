"""Rebuild only grenade effect atlases from preserved local CS2 extractions.
Full uses 1024px padded sheets; Optimized512 derives 512px copies when packaging.
This imports sprite frames, not Source 2 volumetric simulation or particle code.
"""
import argparse,hashlib,json
from pathlib import Path
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT.parent/'CSMCReverse/local_cs2_analysis/all_weapons/06_particles/textures/materials/particle'
OUT=ROOT/'src/ScCsgoKnives/Assets/Textures/ScCsgoKnives'
rows=[]
parser=argparse.ArgumentParser();parser.add_argument('--smoke-only',action='store_true');args=parser.parse_args()
for pattern,name,mask in [
    ('vistasmokev1/vistasmokev1_emods_seq1_*.png','grenade_smoke_atlas',True),
    ('fire_small_sim/fire_small_sim_a_seq0_*.png','grenade_fire_atlas',False),
    ('explosion_fireballs/smoke/explosion_fireball_large_01_smoke_seq0_*.png','grenade_blast_atlas',False)]:
    if args.smoke_only and not mask:continue
    files=sorted(SOURCE.glob(pattern),key=lambda p:int(p.stem.rsplit('_',1)[1]))
    assert len(files)>=16,(pattern,len(files))
    selected=[files[round(i*(len(files)-1)/15)] for i in range(16)]
    crop=None
    if mask:
        # One union crop for the entire animation keeps the authored motion/shape stable.
        # Raw CS2 frames carry ~40% transparent padding, which hid much of the nominal volume.
        boxes=[Image.open(p).getchannel('A').getbbox() for p in selected]
        crop=(min(b[0] for b in boxes),min(b[1] for b in boxes),max(b[2] for b in boxes),max(b[3] for b in boxes))
    atlas=Image.new('RGBA',(1024,1024))
    for i in range(16):
        path=selected[i]
        image=Image.open(path).convert('RGBA')
        if crop:image=image.crop(crop)
        # CS smoke is an alpha-only simulation output: its exported RGB is black.
        if mask:
            alpha=image.getchannel('A');image=Image.new('RGBA',image.size,'white');image.putalpha(alpha)
        # thumbnail() refuses to enlarge the newly cropped small source frames, silently
        # putting the same padding back. Fit in both directions, with a 4px atlas gutter.
        scale=min(248/image.width,248/image.height)
        image=image.resize((round(image.width*scale),round(image.height*scale)),Image.Resampling.LANCZOS)
        atlas.paste(image,((i%4)*256+(256-image.width)//2,(i//4)*256+(256-image.height)//2))
        rows.append(dict(output=name+'.png',frame=i,source=str(path),sha256=hashlib.sha256(path.read_bytes()).hexdigest(),sharedAlphaCrop=crop))
    atlas.save(OUT/(name+'.png'))
    print(name,atlas.size)
(ROOT/'docs/grenade-effects-1.2.2-sources.json').write_text(json.dumps(dict(imports=rows,
    conversion='4x4 straight-RGBA sheets; preserve source aspect ratio; smoke alpha mask gets white RGB for runtime grey tint; no Source 2 volume simulation'),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
