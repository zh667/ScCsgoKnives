"""Exercise the production simple shader on an independent desktop GPU context.
Verify sRGB illumination and complete darkness, not Android/gameplay performance.
"""
from pathlib import Path
import sys,json
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'.tmp/optimization-deps'))
import moderngl,numpy as np
ctx=moderngl.create_standalone_context(require=330)
source=ROOT/'src/ScCsgoKnives/Shaders'
vs=(source/'KnifePbr.vsh').read_text().replace('#version 300 es','#version 330').replace('OPENGL_POSITION_FIX;','')
fs=(source/'WeaponSimple.psh').read_text().replace('#version 300 es','#version 330')
p=ctx.program(vertex_shader=vs,fragment_shader=fs)
vb=ctx.buffer(np.array([[-1,-1,0,0,0,1,0,0],[1,-1,0,0,0,1,1,0],[1,1,0,0,0,1,1,1],[-1,1,0,0,0,1,0,1]],dtype='f4').tobytes())
ib=ctx.buffer(np.array([0,1,2,0,2,3],dtype='u4').tobytes())
vao=ctx.vertex_array(p,[(vb,'3f 3f 2f','a_position','a_normal','a_texcoord')],ib,index_element_size=4)
fbo=ctx.simple_framebuffer((8,8),components=4);fbo.use()
view=np.eye(4,dtype='f4');view[2,3]=-1
p['u_worldViewMatrix'].write(view.T.copy().tobytes());p['u_worldViewProjectionMatrix'].write(np.eye(4,dtype='f4').tobytes())
p['u_scopeCutout'].value=(0,1);p['u_lightDir1'].value=(1,0,0);p['u_lightDir2'].value=(-1,0,0)
tex=ctx.texture((1,1),4,bytes([220,220,220,255]));tex.use(0);p['u_baseColor'].value=0
checks=[]
for scene in [0,.05,.1,.2,.5,1]:
    p['u_light'].value=scene;fbo.clear();vao.render()
    actual=fbo.read(components=4)[0];expected=round(220*(.35*scene)**(1/2.2));old=round(220*.35*scene)
    checks.append(dict(sceneLight=scene,actual=actual,expected=expected,previousIncorrect=old,ok=abs(actual-expected)<=1))
assert all(r['ok'] for r in checks) and checks[0]['actual']==0
report=dict(renderer=ctx.info['GL_RENDERER'],scope='Production shader in standalone desktop GL context, not the game or Android',checks=checks)
(ROOT/'output/optimization-106/simple-lighting-gpu.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
