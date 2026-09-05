"""Offline contact sheets from packaged DLL mesh/sprite exports and packaged textures.

Uses simple orthographic lighting, no terrain occlusion or game UI. Not game captures.
"""
import argparse,io,json,sys,zipfile
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--frames',type=Path,required=True);ap.add_argument('--scmod',type=Path,required=True)
    ap.add_argument('--out',type=Path,required=True);ap.add_argument('--runtime',type=Path,required=True);args=ap.parse_args()
    sys.path.insert(0,str(args.runtime));import moderngl
    ctx=moderngl.create_standalone_context(require=330)
    prog=ctx.program(vertex_shader='''#version 330
        in vec3 pos; in vec2 uv; in vec4 color; uniform mat4 matrix;
        out vec2 texcoord;out vec4 tint;
        void main(){gl_Position=matrix*vec4(pos,1);texcoord=uv;tint=color;}
    ''',fragment_shader='''#version 330
        uniform sampler2D tex;in vec2 texcoord;in vec4 tint;out vec4 frag;
        void main(){frag=texture(tex,texcoord)*tint;}
    ''')
    archive=zipfile.ZipFile(args.scmod);textures={}
    def texture(name):
        if name not in textures:
            im=Image.open(io.BytesIO(archive.read('Assets/Textures/ScCsgoKnives/'+name+'.png'))).convert('RGBA')
            textures[name]=ctx.texture(im.size,4,im.tobytes());textures[name].filter=(moderngl.LINEAR,moderngl.LINEAR)
        textures[name].use(0);prog['tex']=0
    def axes(yaw=25,pitch=18):
        y,p=np.deg2rad([yaw,pitch]);right=np.array([np.cos(y),0,-np.sin(y)])
        forward=np.array([np.sin(y)*np.cos(p),np.sin(p),np.cos(y)*np.cos(p)])
        up=np.cross(forward,right);return right,up,forward
    def matrix(right,up,forward,span,center=(0,0,0),aspect=1):
        m=np.eye(4);m[:3,:3]=np.stack([right/span/aspect,up/span,-forward/20])
        m[:3,3]=-m[:3,:3]@np.array(center);prog['matrix'].write(m.astype('f4').T.tobytes())
    def draw(vertices,indices):
        vb=ctx.buffer(np.array(vertices,dtype='f4').tobytes());ib=ctx.buffer(np.array(indices,dtype='u4').tobytes())
        vao=ctx.vertex_array(prog,[(vb,'3f 2f 4f','pos','uv','color')],ib,index_element_size=4);vao.render();vao.release();ib.release();vb.release()
    def frame(w,h):
        fb=ctx.simple_framebuffer((w,h));fb.use();fb.clear(.14,.17,.20,1);return fb
    def grab(fb):
        im=Image.frombytes('RGB',fb.size,fb.read()).transpose(Image.Transpose.FLIP_TOP_BOTTOM);fb.release();return im
    labels=['Universal magazine','12-gauge shell','Machined billet','Precision mechanism','Grip assembly','Optics assembly','Weapon workbench']
    sheet=Image.new('RGB',(7*240,2*270),(25,29,35));d=ImageDraw.Draw(sheet)
    for kind,label in enumerate(labels):
        doc=json.loads((args.frames/f'item{kind}.json').read_text())
        for row,yaw in enumerate([30,-140]):
            fb=frame(240,240);ctx.enable(moderngl.DEPTH_TEST);ctx.disable(moderngl.BLEND)
            right,up,forward=axes(yaw,25);matrix(right,up,forward,.65)
            texture('survival_surface');draw(doc['vertices'],doc['indices']);im=grab(fb)
            sheet.paste(im,(kind*240,row*270+30));d.text((kind*240+8,row*270+8),f'OFFLINE | {label}',fill='white')
    args.out.mkdir(parents=True,exist_ok=True);sheet.save(args.out/'survival-items-offline.png')
    files=sorted(args.frames.glob('effect*.json'),key=lambda p:(int(p.stem[6]),float(p.stem.split('-')[1])));sheet=Image.new('RGB',(480*3,300*4),(25,29,35));d=ImageDraw.Draw(sheet)
    names=['grenade_smoke_atlas','grenade_fire_atlas','grenade_blast_atlas','grenade_glow']
    kinds={0:0,1:1,2:2,4:3};cols={}
    for path in files:
        doc=json.loads(path.read_text());kind=doc['kind'];row=kinds[kind];col=cols.get(kind,0);cols[kind]=col+1
        fb=frame(480,270);ctx.disable(moderngl.DEPTH_TEST);ctx.enable(moderngl.BLEND)
        right,up,forward=axes(20,12);matrix(right,up,forward,2.5,(0,1,0),480/270)
        for sprite in sorted(doc['sprites'],key=lambda s:(s['additive'],-np.dot(np.array(s['position']),forward))):
            ctx.blend_func=(moderngl.SRC_ALPHA,moderngl.ONE if sprite['additive'] else moderngl.ONE_MINUS_SRC_ALPHA)
            texture(names[sprite['texture']]);r=right.copy();u=up.copy()
            if sprite['upright']:r[1]=0;r/=np.linalg.norm(r);u=np.array([0,1,0])
            a=sprite['rotation'];rr=(r*np.cos(a)+u*np.sin(a))*sprite['width'];uu=(u*np.cos(a)-r*np.sin(a))*sprite['height']
            p=np.array(sprite['position']);key=sprite['texture'];f=sprite['frame']
            x=0 if key==3 else (f%4)*.25+.004;y=0 if key==3 else (f//4)*.25+.004;span=1 if key==3 else .242
            uv=[[x,y+span],[x+span,y+span],[x+span,y],[x,y]];points=[p-rr-uu,p+rr-uu,p+rr+uu,p-rr+uu]
            draw([list(pt)+v+sprite['color'] for pt,v in zip(points,uv)],[0,1,2,0,2,3])
        sheet.paste(grab(fb),(col*480,row*300+30));d.text((col*480+10,row*300+8),f"OFFLINE | {['HE','Flash','Smoke','Molotov','Fire'][kind]} @ {doc['time']:.2f}s",fill='white')
    sheet.save(args.out/'survival-effects-offline.png');print('Rendered packaged geometry/textures on',ctx.info['GL_RENDERER'])

if __name__=='__main__':main()
