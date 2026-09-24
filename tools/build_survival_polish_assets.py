"""Reproducible CS2 icon/particle import and custom supply surface atlas.

No source extraction is modified. Records hashes for every imported image.
"""
from pathlib import Path
import hashlib
import json
import numpy as np
from PIL import Image, ImageDraw, ImageEnhance

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT.parent / 'CSMCReverse/local_cs2_analysis/all_weapons'
OUT = ROOT / 'src/ScCsgoKnives/Assets/Textures/ScCsgoKnives'
records = []

def read(path):
    records.append({'source': str(path), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
    return Image.open(path).convert('RGBA')

def atlas(pattern, name, mask=False):
    files = sorted(SOURCE.glob('06_particles/textures/materials/particle/'+pattern), key=lambda p: int(p.stem.rsplit('_',1)[1]))
    assert len(files) >= 16, (pattern, len(files))
    sheet = Image.new('RGBA', (512,512))
    for i, index in enumerate(np.linspace(0,len(files)-1,16).astype(int)):
        im=read(files[index]).resize((124,124),Image.Resampling.LANCZOS)
        if mask:
            alpha=im.getchannel('A'); im=Image.new('RGBA',im.size,'white');im.putalpha(alpha)
        # Two transparent pixels around each cell prevent linear-filter atlas bleeding.
        sheet.paste(im,((i%4)*128+2,(i//4)*128+2))
    sheet.save(OUT/(name+'.png'))

def main():
    icons = SOURCE/'11_icons/panorama/images/econ/weapons/base_weapons'
    for name,source in [('hegrenade','hegrenade'),('flashbang','flashbang'),('smokegrenade','smokegrenade'),('molotov','molotov'),('incendiary','incgrenade'),('decoy','decoy')]:
        im=read(icons/('weapon_'+source+'_png.png'))
        im=im.crop(im.getchannel('A').getbbox())
        im.thumbnail((114,114),Image.Resampling.LANCZOS)
        canvas=Image.new('RGBA',(128,128));canvas.paste(im,((128-im.width)//2,(128-im.height)//2))
        canvas.save(OUT/('grenade_'+name+'_slot.png'))
    atlas('fire_small_sim/fire_small_sim_a_seq0_*.png','grenade_fire_atlas')
    atlas('explosion_fireballs/smoke/explosion_fireball_large_01_smoke_seq0_*.png','grenade_blast_atlas')
    atlas('vistasmokev1/vistasmokev1_emods_seq1_*.png','grenade_smoke_atlas',True)
    # Smooth luminous kernel for additive flash/sparks: alpha falls to zero before edges.
    y,x=np.mgrid[-1:1:128j,-1:1:128j];r=np.sqrt(x*x+y*y)
    a=(np.maximum(0,1-r)**2 * np.exp(-r*r*4)*255).astype('uint8')
    glow=Image.new('RGBA',(128,128),'white');glow.putalpha(Image.fromarray(a));glow.save(OUT/'grenade_glow.png')
    # The authored atlas also has an independent entry point; never regenerate CS2 effects just to edit supplies.
    import build_supply_surface
    build_supply_surface.main()
    (ROOT/'docs/survival-polish-sources.json').write_bytes((json.dumps({'imports':records,'generated':['survival_surface.png','grenade_glow.png'],'conversion':'128px transparent icons; padded 4x4 RGBA animation atlases, 16 frames each'},ensure_ascii=False,indent=2)+'\n').encode('utf-8'))

if __name__=='__main__':main()
