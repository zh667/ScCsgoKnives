"""Offline UV placement contact sheet, not an in-game lighting screenshot."""
import json,sys
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw,ImageFont
from cs2_glb import Glb
from gun_skin_preview import render
R=Path(__file__).resolve().parents[1];T=R/'src/ScCsgoKnives/Assets/Textures/ScCsgoKnives'
sources=json.loads((R/'.tmp/finish33-source-20260910/source-manifest.json').read_text('utf-8'))['finishes']
added=json.loads((R/'src/ScCsgoKnives/AnimationData/gun_additional_skins.json').read_text('utf-8'))
font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',15)
sheet=Image.new('RGB',(1000,11*210),'white');draw=ImageDraw.Draw(sheet)
for i,s in enumerate(added):
    source=next(x for x in sources if x['paintId']==s['paintId']);g=Glb(source['sourceGlb']);m=next(m for m in g.meshes() if m.name.endswith('body_'+source['body']))
    # First authored body primitive; auxiliary materials are checked separately by package tests.
    p=next((p for p in m.primitives if not p.material.startswith('weapon_')),m.primitives[0]);a=p.attributes;uv=a['TEXCOORD_0'];pos=a['POSITION'][:,[2,0,1]]
    tris=[tuple((int(j),int(j)) for j in t) for t in p.indices.reshape(-1,3)]
    texture=np.asarray(Image.open(T/f'{s["gun"]}_hd__{s["key"]}.png').convert('RGB'),float)/255
    im=render(pos,uv,tris,texture,300,yaw=.08,pitch=.04).crop((0,65,300,235))
    x=(i%3)*333;y=(i//3)*210;sheet.paste(im,(x,y+30));draw.text((x+8,y+5),f'{s["gun"]} {s["name"]}',font=font,fill='black')
    print(s['key'],flush=True)
sheet.save(R/'docs/finish33-uv-preview.jpg',quality=90)
