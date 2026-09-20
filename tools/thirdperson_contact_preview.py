"""Small annotated contact sheet from the native diagnostic; never an in-game image."""
from pathlib import Path
from PIL import Image,ImageDraw
ROOT=Path(__file__).resolve().parents[1]
source=ROOT/'output/tactical-1.2.2/appearance-installed'
sheet=Image.new('RGB',(1200,640),(25,30,38));draw=ImageDraw.Draw(sheet)
draw.text((16,8),'Native diagnostic - CT/T world props (not an in-game screenshot)',fill='white')
samples=[('ak47','Reload','.25'),('ak47','Reload','.55'),('ak47','Reload','.80'),('butterfly','Idle','.25'),('falchion','Idle','.25'),('push','Idle','.25')]
for i,(asset,kind,phase) in enumerate(samples):
    image=Image.open(source/f'action-ct-{asset}-{kind}-0{phase}.png')
    image=image.crop((95,135,350,350)).resize((400,270))
    x=i%3*400;y=35+i//3*300
    sheet.paste(image,(x,y+22));draw.text((x+12,y+4),f'{asset} / {kind} / {phase}',fill='white')
target=ROOT/'docs/thirdperson-1.4.6-preview.jpg';sheet.save(target,quality=90)
print(target)
