"""Contact sheets use only frames listed by the current successful native run."""
from pathlib import Path
import json,os
from PIL import Image,ImageDraw
root=Path(__file__).resolve().parents[1]
p=root/os.environ.get('SC_MINIMAL_STAGE','.tmp/minimal-130-20260926')/'native-resources'
report=json.loads((p/'checks.json').read_text(encoding='utf-8'));assert report['failed']==0
groups={
    'idle':['ak47','m4a1s','awp','m249','nova','default_ct','deagle','revolver','c4'],
    'reload':['ak47','awp','m249','nova'],
}
if any('lookat' in f['alias'].lower() or 'inspect' in f['alias'].lower() for f in report['frames']):groups['inspect']=['ak47','awp','default_ct']
for group,names in groups.items():
    rows=(len(names)+2)//3
    canvas=Image.new('RGB',(960,260*rows),(12,15,20));draw=ImageDraw.Draw(canvas)
    for k,name in enumerate(names):
        frames=[f for f in report['frames'] if f['asset']==name and (f['alias'].startswith(group) if group!='inspect' else any(t in f['alias'].lower() for t in ['inspect','lookat'])) and f['phase']==(0 if group=='idle' else .5)]
        f=frames[0];phase='0' if f['phase']==0 else '0.5'
        q=p/f"{name}-{f['alias']}-{phase}.png"
        im=Image.open(q).convert('RGB');im.thumbnail((320,240));x=k%3*320;y=k//3*260
        canvas.paste(im,(x,y));draw.text((x+5,y+242),name+' '+f['alias'],fill='white')
    canvas.save(p/f'contact-{group}.jpg',quality=90)
