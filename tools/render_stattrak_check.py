#!/usr/bin/env python3
"""Textured CPU QA, using PackageCheck --stattrak-out (not a game screenshot).

Reuses the established gun-skin rasterizer. Only the preview atlas is composited;
source and package textures are not changed. Three guns are compared on both sides.
"""
import argparse
import io
import json
import zipfile
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
from gun_skin_preview import gun_mesh, render, TEX

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--frames', type=Path, required=True)
    ap.add_argument('--package', type=Path, required=True)
    ap.add_argument('--out', type=Path, required=True)
    args = ap.parse_args()
    data = json.loads(args.frames.read_text('utf-8-sig'))
    package = zipfile.ZipFile(args.package)
    def texture(name):
        return Image.open(io.BytesIO(package.read('Assets/Textures/ScCsgoKnives/'+name+'.png'))).convert('RGB').resize((1024,1024))
    rows = []
    def compose(gun=None, legacy=False, count='654321'):
        atlas = Image.new('RGB',(3072,1024))
        atlas.paste(texture('stattrak_module'),(1024,0))
        atlas.paste(texture('stattrak_digit_atlas'),(2048,0))
        verts, uvs, tris = [], [], []
        if gun:
            p,u,t = gun_mesh(gun,True,legacy)
            verts.extend(p.tolist()); uvs.extend(np.column_stack([(u[:,0]%1)/3,u[:,1]%1]).tolist()); tris.extend(t)
            material = gun+'_hd'
            if legacy:
                material += '__' + ('am_lightning_awp' if gun=='awp' else 'cu_m4a1s_printstream')
            atlas.paste(texture(material),(0,0))
        key = gun + ('_legacy' if legacy else '') if gun else None
        matrix = np.array(data['frames'][key]).reshape(4,4) if gun else np.eye(4)
        for part in data['parts']:
            p = np.array(part['Positions']).reshape(-1,3)
            p = (np.column_stack([p,np.ones(len(p))]) @ matrix)[:,:3]
            display = part['Name'].endswith('_display')
            u = np.array(data['examples'][count] if display else part['Uvs']).reshape(-1,2)
            u[:,0] = ((u[:,0]%1) + (2 if display else 1))/3
            offsetp,offsetu = len(verts),len(uvs)
            verts.extend(p.tolist());uvs.extend(u.tolist())
            for tri in np.array(part['Indices']).reshape(-1,3):
                tris.append(tuple((int(i)+offsetp,int(i)+offsetu) for i in tri))
        return np.array(verts),np.array(uvs),tris,np.asarray(atlas)/255.0
    # Large direct views prove which actual atlas glyphs the runtime-selected UVs show.
    for count in data['examples']:
        p,u,t,tex = compose(count=count)
        # The reused rasterizer's (X,Z,Y) screen basis reflects handedness. Correct
        # its horizontal image axis for a camera on +Y looking down -Y with +Z up.
        image = render(p,u,t,tex,800,yaw=0,pitch=0,shader=lambda col,uv,n:col).transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        row = Image.new('RGB',(1600,280),'white');row.paste(image.crop((0,270,800,530)),(0,20))
        ImageDraw.Draw(row).text((810,95),'Runtime UV count: '+count+' (6 digits, saturated)',fill='black')
        rows.append(row)
    for gun,legacy in [('ak47',False),('awp',False),('awp',True),('m4a1s',False),('m4a1s',True)]:
        p,u,t,tex = compose(gun,legacy)
        row = Image.new('RGB',(1600,380),'white')
        for side in range(2):
            view = p * ([-1,-1,1] if side else [1,1,1])
            image = render(view,u,t,tex,800,yaw=.2,pitch=.12).transpose(Image.Transpose.FLIP_LEFT_RIGHT)
            row.paste(image.crop((0,225,800,575)),(side*800,25))
        ImageDraw.Draw(row).text((10,8),f'{gun} / '+('legacy' if legacy else 'HD')+' / normalized item frame / both sides',fill='black')
        rows.append(row)
    sheet = Image.new('RGB',(1600,sum(r.height for r in rows)+30),'white')
    ImageDraw.Draw(sheet).text((10,8),'OFFLINE GEOMETRY + UV CHECK — NOT IN-GAME / NOT SOURCE 2 SHADING',fill='black')
    y=30
    for row in rows: sheet.paste(row,(0,y));y+=row.height
    args.out.parent.mkdir(parents=True,exist_ok=True);sheet.save(args.out)
    print(args.out)

if __name__=='__main__': main()
