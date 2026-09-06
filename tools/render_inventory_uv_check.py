"""Offline icon sampling using packaged Block UV settings, not just asset existence."""
import argparse,io,json,zipfile
from pathlib import Path
from PIL import Image,ImageDraw

ap=argparse.ArgumentParser()
ap.add_argument('--scmod',type=Path,required=True)
ap.add_argument('--presentation',type=Path,required=True)
ap.add_argument('--out',type=Path,required=True)
args=ap.parse_args()
settings=next(x for x in json.loads(args.presentation.read_text()) if x['name']=='ScGrenadeBlock')
slots,face=settings['slots'],settings['face']
assert settings['view']==[0,0,1], 'Inventory plane must face the icon camera'
sheet=Image.new('RGB',(672,152),(38,43,50));d=ImageDraw.Draw(sheet)
d.text((8,8),'OFFLINE | packaged icon UV + 96px slots | not an in-game screenshot',fill='white')
with zipfile.ZipFile(args.scmod) as z:
    for i,kind in enumerate(['hegrenade','flashbang','smokegrenade','molotov','incendiary','decoy']):
        im=Image.open(io.BytesIO(z.read('Assets/Textures/ScCsgoKnives/grenade_'+kind+'_slot.png'))).convert('RGBA')
        x=(face%slots)*im.width/slots;y=(face//slots)*im.height/slots
        sample=im.crop((x,y,x+im.width/slots,y+im.height/slots))
        assert sample.getchannel('A').getbbox(), f'{kind}: runtime UV reads only transparent pixels'
        d.rounded_rectangle((i*112+8,30,i*112+104,126),5,fill=(111,105,88),outline=(151,145,129),width=2)
        # BlocksManager.DrawFlatBlock: half-size .85*1.45; BlockIconWidget ortho width 3.6.
        size=round(96*2*.85*1.45/3.6);sample=sample.resize((size,size),Image.Resampling.NEAREST)
        sheet.paste(sample,(i*112+8+(96-size)//2,30+(96-size)//2),sample)
        d.text((i*112+8,134),kind,fill='white')
args.out.parent.mkdir(parents=True,exist_ok=True);sheet.save(args.out)
print('6/6 packaged runtime UV samples visible:',args.out)
