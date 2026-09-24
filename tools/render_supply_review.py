"""Offline review of actual C# mesh exports against their source atlas, no scmod required.

No game UI, world lighting, hands or engine camera are simulated. Tiny icons are
rendered at their actual 32/48/64 px resolution, not resized from a large render.
"""
import argparse,json,math
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw,ImageFont
import moderngl
from cs2_glb import Glb

LABELS=['通用弹匣','霰弹壳','金属坯件','精密机构','握持组件','光学组件','武器装配台','涂装材料',
        '停用信标（旧值）','CT 招募信标','T 招募信标','三人敌队信标','五人敌队信标']

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--before',type=Path,required=True);ap.add_argument('--after',type=Path,required=True)
    ap.add_argument('--atlas',type=Path,required=True);ap.add_argument('--out',type=Path,required=True);args=ap.parse_args()
    args.out.mkdir(parents=True,exist_ok=True)
    ctx=moderngl.create_standalone_context(require=330)
    prog=ctx.program(vertex_shader='''#version 330
        in vec3 pos; in vec2 uv; in vec4 color; uniform mat4 matrix;
        out vec2 texcoord;out vec4 tint;
        void main(){gl_Position=matrix*vec4(pos,1);texcoord=uv;tint=color;}
    ''',fragment_shader='''#version 330
        uniform sampler2D tex;in vec2 texcoord;in vec4 tint;out vec4 frag;
        void main(){frag=texture(tex,texcoord)*tint;}
    ''')
    def tex(path):
        im=Image.open(path).convert('RGBA');t=ctx.texture(im.size,4,im.tobytes());t.filter=(moderngl.LINEAR,moderngl.LINEAR);return t
    oldtex=tex(args.before/'survival_surface.png');newtex=tex(args.atlas);radiotex=tex(args.before/'radio.png')
    meshes={}
    for tag,folder in [('before',args.before),('after',args.after)]:
        for file in folder.glob('item*.json'):
            doc=json.loads(file.read_text());meshes[tag,int(file.stem[4:])]=(np.asarray(doc['vertices'],dtype='f4'),np.asarray(doc['indices'],dtype='u4'))
    g=Glb(args.before/'radio.glb');parts=g.json['meshes'][0]['primitives'];points=[];indices=[]
    for part in parts:
        a={k:g.accessor(v) for k,v in part['attributes'].items()};p=a['POSITION'];normal=a['NORMAL']
        light=np.maximum(0,normal@np.array([-1,2,1])/math.sqrt(6))*.38+.62
        vertices=np.c_[p,a['TEXCOORD_0'],np.repeat(light[:,None],3,axis=1),np.ones(len(p))]
        indices.extend(g.accessor(part['indices']).ravel()+len(points));points.extend(vertices)
    for kind,tint in [(8,[1,1,1]),(9,[135/255,190/255,1]),(10,[1,150/255,125/255]),(11,[1,105/255,75/255]),(12,[1,105/255,75/255])]:
        v=np.array(points,dtype='f4');v[:,5:8]*=tint;meshes['before',kind]=(v,np.asarray(indices,dtype='u4'))
    def render(tag,kind,size=240,yaw=None,pitch=None):
        vertices,indices=meshes[tag,kind]
        y,p=np.deg2rad([yaw if yaw is not None else (19.3 if kind>=8 else 28.8),pitch if pitch is not None else (10.7 if kind>=8 else 19.3)])
        right=np.array([np.cos(y),0,-np.sin(y)]);forward=np.array([np.sin(y)*np.cos(p),np.sin(p),np.cos(y)*np.cos(p)]);up=np.cross(forward,right)
        span=.25 if kind>=8 else .72 if kind==6 else .59;center=np.array([0,.03 if kind>=8 else 0,0])
        matrix=np.eye(4);matrix[:3,:3]=np.stack([right/span,up/span,-forward/20]);matrix[:3,3]=-matrix[:3,:3]@center
        prog['matrix'].write(matrix.astype('f4').T.tobytes());prog['tex']=0
        (newtex if tag=='after' else radiotex if kind>=8 else oldtex).use(0)
        vb=ctx.buffer(vertices.tobytes());ib=ctx.buffer(indices.tobytes());vao=ctx.vertex_array(prog,[(vb,'3f 2f 4f','pos','uv','color')],ib,index_element_size=4)
        fb=ctx.simple_framebuffer((size,size));fb.use();fb.clear(.10,.125,.15,1);ctx.enable(moderngl.DEPTH_TEST);ctx.disable(moderngl.BLEND|moderngl.CULL_FACE);vao.render()
        im=Image.frombytes('RGB',fb.size,fb.read()).transpose(Image.Transpose.FLIP_TOP_BOTTOM)
        vao.release();ib.release();vb.release();fb.release();return im
    fontpath=Path('C:/Windows/Fonts/msyh.ttc')
    font=ImageFont.truetype(str(fontpath),18) if fontpath.exists() else ImageFont.load_default()
    small=ImageFont.truetype(str(fontpath),13) if fontpath.exists() else font
    kinds=[9,10,11,12,2,3,4,5,7,6,0,1]
    sheet=Image.new('RGB',(1536,4*320+56),(19,24,30));d=ImageDraw.Draw(sheet)
    d.text((18,15),'自制模型细化 · 左：修改前 / 右：修改后 · 离线运行时网格预览，非游戏截图',font=font,fill='#e1e5e7')
    for n,kind in enumerate(kinds):
        x=(n%3)*512;y=56+(n//3)*320
        d.text((x+16,y+5),LABELS[kind],font=font,fill='#e1e5e7')
        for j,tag in enumerate(['before','after']):
            sheet.paste(render(tag,kind), (x+8+j*252,y+32))
            d.text((x+16+j*252,y+281),'修改前' if tag=='before' else '修改后',font=small,fill='#aeb8bf')
    sheet.save(args.out/'supply-models-comparison.png')
    views=Image.new('RGB',(4*320,13*150+46),(19,24,30));d=ImageDraw.Draw(views)
    d.text((12,10),'离线多角度检查：正面 / 背面 / 侧面 / 上方',font=font,fill='white')
    for kind in range(13):
        y=46+kind*150
        for col,(yaw,pitch) in enumerate([(0,0),(180,0),(90,0),(30,65)]):
            views.paste(render('after',kind,136,yaw,pitch),(col*320+170,y))
            d.text((col*320+8,y+16),LABELS[kind],font=small,fill='white')
    views.save(args.out/'supply-models-views.png')
    icons=Image.new('RGB',(12*112,210),(19,24,30));d=ImageDraw.Draw(icons)
    d.text((10,5),'离线图标：32 / 48 / 64 px（原生尺寸）',font=small,fill='white')
    for n,kind in enumerate(kinds):
        d.text((n*112+4,28),LABELS[kind],font=small,fill='white')
        for y,size in [(50,32),(89,48),(143,64)]:icons.paste(render('after',kind,size),(n*112+24,y))
    icons.save(args.out/'supply-models-icons.png')
    (args.out/'renderer.json').write_text(json.dumps({'renderer':ctx.info['GL_RENDERER'],'source':'actual runtime C# mesh export; authored atlas','caveat':'offline orthographic rendering; not game, device or hand-contact acceptance','kinds':kinds},indent=2)+'\n')
    print('Rendered comparison, four views, and native-size icons on',ctx.info['GL_RENDERER'])

if __name__=='__main__':main()
