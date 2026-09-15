"""Official icon vs flat-lit UV check; not an in-game screenshot."""
import json, sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont
from cs2_glb import Glb
from gun_skin_preview import render
from build_knife_finishes import ROOT,TEX,EXPORT

def main():
    sys.stdout.reconfigure(encoding='utf-8')
    rows=json.loads((ROOT/'docs/knife-finishes-source-20260913.json').read_text('utf-8'))['rows']
    canvas=Image.new('RGB',(1200,5*330),(239,239,239));draw=ImageDraw.Draw(canvas)
    font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',15)
    for i,row in enumerate(rows):
        asset,key=row['asset'],row['finish'];x=(i%4)*300;y=(i//4)*330
        mesh=next(m for m in Glb(EXPORT/'glb/weapons/models/knife'/('knife_'+asset)/('weapon_knife_'+asset+'.glb')).meshes() if 'body' in m.name)
        p=mesh.primitives[0];a=p.attributes;pos=a['POSITION'][:,[2,0,1]];uv=a['TEXCOORD_0']
        triangles=[tuple((int(j),int(j)) for j in tri) for tri in p.indices.reshape(-1,3)]
        texture=np.asarray(Image.open(TEX/f'{asset}_finish__{key}.png').convert('RGB'),float)/255
        local=render(pos,uv,triangles,texture,280,yaw=.08,pitch=.04)
        # Crop only background, preserving knife silhouette aspect.
        pix=np.array(local);mask=(np.max(np.abs(pix.astype(int)-239),axis=2)>5)
        yy,xx=np.where(mask);local=local.crop((max(xx.min()-4,0),max(yy.min()-4,0),min(xx.max()+5,280),min(yy.max()+5,280)))
        local.thumbnail((280,125));canvas.paste(local,(x+(300-local.width)//2,y+185+(125-local.height)//2))
        icon=Image.open(TEX/f'{asset}_slot__{key}.png').convert('RGBA');icon.thumbnail((260,145))
        canvas.paste(icon,(x+(300-icon.width)//2,y+32),icon)
        label = 'Ruby' if key == 'am_ruby_marbleized' else 'Gamma '+key[-1] if 'gamma' in key else 'Fade'
        draw.text((x+8,y+3),asset+' / '+label,font=font,fill='black')
        draw.text((x+8,y+168),'上：CS2 原图   下：本地材质 UV 检查',font=font,fill='black')
        print(asset,flush=True)
    canvas.save(ROOT/'docs/knife-finishes-uv-check-20260913.jpg',quality=92)

if __name__=='__main__':main()
