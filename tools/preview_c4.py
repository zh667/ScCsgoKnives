"""Offline GPU render of vertices exported from the packaged DLL; not a game screenshot."""
from pathlib import Path
import sys,json
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'.tmp/optimization-deps'))
import moderngl
import numpy as np
from PIL import Image,ImageDraw
out=ROOT/'output/c4-hud-111/preview'
ctx=moderngl.create_standalone_context(require=330)
p=ctx.program(vertex_shader='''#version 330
in vec3 pos;in vec3 normal;in vec2 uv;uniform mat4 mvp;out vec2 tex;out vec3 n;
void main(){gl_Position=mvp*vec4(pos,1);tex=uv;n=normal;}''',fragment_shader='''#version 330
in vec2 tex;in vec3 n;uniform sampler2D base;uniform bool opaque;out vec4 color;
void main(){vec4 c=texture(base,tex);if(!opaque&&c.a<=0)discard;color=vec4(c.rgb*(.75+.25*abs(normalize(n).y)),opaque?1:c.a);}''')
fbo=ctx.simple_framebuffer((600,600));fbo.use();ctx.enable(moderngl.DEPTH_TEST)
canvas=Image.new('RGB',(1200,1240),(22,25,30));draw=ImageDraw.Draw(canvas)
for k,label in enumerate(['planted-old-alpha','planted','plant-1','plant-2']):
    name='planted' if k==0 else label
    p['opaque'].value=k!=0
    if k==0:ctx.enable(moderngl.BLEND);ctx.blend_func=moderngl.SRC_ALPHA,moderngl.ONE_MINUS_SRC_ALPHA
    else:ctx.disable(moderngl.BLEND)
    data=json.loads((out/(name+'.json')).read_text());v=np.array(data['vertices'],dtype='f4')
    rot=np.eye(3,dtype='f4')
    if name=='planted':
        right=np.array([1,0,0]);up=np.array([0,.55,-.835]);forward=np.cross(right,up);rot=np.array([right,up,forward])
    xyz=v[:,:3]@rot.T;lo=xyz.min(0);hi=xyz.max(0);scale=1.75/max((hi-lo)[:2])
    mat=np.eye(4,dtype='f4');mat[:3,:3]=rot*scale;mat[:3,3]=-(lo+hi)/2*scale
    mat[2,:]*=-.2
    p['mvp'].write(mat.T.copy().tobytes());p['base'].value=0
    vb=ctx.buffer(v.tobytes());fbo.clear(.08,.09,.11,1,depth=1)
    for g in data['groups']:
        material='weapon_c4_digits' if g['material']=='weapon_c4_digits' else 'c4_cs2'
        im=Image.open(ROOT/f'src/ScCsgoKnives/Assets/Textures/ScCsgoKnives/{material}.png').convert('RGBA')
        tex=ctx.texture(im.size,4,im.tobytes());tex.filter=(moderngl.LINEAR,moderngl.LINEAR);tex.use(0)
        ib=ctx.buffer(np.array(g['indices'],dtype='u4').tobytes());vao=ctx.vertex_array(p,[(vb,'3f 3f 2f','pos','normal','uv')],ib)
        vao.render();vao.release();ib.release();tex.release()
    im=Image.frombytes('RGB',(600,600),fbo.read(components=3)).transpose(Image.Transpose.FLIP_TOP_BOTTOM)
    canvas.paste(im,(k%2*600,k//2*620+20));draw.text((k%2*600+8,k//2*620+5),label+' | OFFLINE packaged-DLL geometry',fill='white');vb.release()
canvas.save(out/'c4-offline.png')
print(out/'c4-offline.png')
