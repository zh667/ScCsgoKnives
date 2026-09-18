"""Orthographic alpha coverage from PackageCheck exports; not an in-game render."""
import argparse, io, json, zipfile
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

ap=argparse.ArgumentParser();ap.add_argument('--scmod',type=Path,required=True);ap.add_argument('--frames',type=Path,required=True);ap.add_argument('--out',type=Path,required=True);args=ap.parse_args()
with zipfile.ZipFile(args.scmod) as z:
    atlas=np.asarray(Image.open(io.BytesIO(z.read('Assets/Textures/ScCsgoKnives/grenade_smoke_atlas.png'))).convert('RGBA'))[:,:,3]/255
sheet=Image.new('RGB',(1200,650),(220,225,230));draw=ImageDraw.Draw(sheet)
for i,name in enumerate(['side','top']):
    frames=json.loads((args.frames/f'smoke-{name}.json').read_text())
    lo,hi=(0,10) if name=='side' else (-5,5)
    yy,xx=np.mgrid[0:600,0:600];x=xx/60-5;y=hi-yy/60
    clear=np.ones_like(x)
    for s in frames:
        dx=x-s['x'];dy=y-s['y'];det=s['rx']*s['uy']-s['ry']*s['ux']
        u=(dx*s['uy']-dy*s['ux'])/det*.5+.5;v=.5-(dy*s['rx']-dx*s['ry'])/det*.5
        valid=(u>=0)&(u<=1)&(v>=0)&(v<=1)
        tx=np.clip(((s['frame']%4*.25+.004+u*.242)*atlas.shape[1]).astype(int),0,atlas.shape[1]-1)
        ty=np.clip(((s['frame']//4*.25+.004+v*.242)*atlas.shape[0]).astype(int),0,atlas.shape[0]-1)
        clear*=1-atlas[ty,tx]*s['alpha']*valid
    bg=np.full((600,600,3),225.0);bg[(xx%60<1)|(yy%60<1)]=185
    pixels=np.uint8(np.clip(bg*clear[:,:,None]+100*(1-clear[:,:,None]),0,255))
    sheet.paste(Image.fromarray(pixels),(i*600,40))
    draw.text((i*600+12,12),f'OFFLINE ATLAS ALPHA | {name} | grid = 1 m | NOT a game screenshot',fill='black')
args.out.parent.mkdir(parents=True,exist_ok=True);sheet.save(args.out)
