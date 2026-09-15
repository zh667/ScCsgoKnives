"""Standalone desktop GPU comparison; NOT an in-game screenshot or Android benchmark.
Uses the production shader sources with desktop GLSL version and an orthographic preview camera.
"""
from pathlib import Path
import sys,io,json,zipfile,struct
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'.tmp/optimization-deps'))
import numpy as np
import moderngl
from PIL import Image,ImageDraw

def binary(b):
    o=12
    def num(f):
        nonlocal o
        n=struct.unpack_from('<'+f,b,o)[0];o+=struct.calcsize('<'+f);return n
    def text():
        nonlocal o
        n=num('H');s=b[o:o+n].decode();o+=n;return s
    for _ in range(num('H')):text();o+=64
    skin=b[:8]==b'SCK2SKIN';count=num('i');stride=52 if skin else 32
    v=np.ndarray((count,8),dtype='<f4',buffer=b,offset=o,strides=(stride,4)).copy();o+=count*stride
    groups=[]
    for _ in range(num('H')):
        if not skin:num('H')
        material=text();n=num('i');indices=np.frombuffer(b,dtype='<u4',offset=o,count=n).copy();o+=n*4
        groups.append((material,indices))
    return v,groups

def obj(b):
    vs=[];ns=[];uv=[];v=[];indices=[]
    for line in b.decode('utf-8-sig').splitlines():
        a=line.split()
        if not a:continue
        if a[0]=='v':vs.append([float(x) for x in a[1:4]])
        elif a[0]=='vn':ns.append([float(x) for x in a[1:4]])
        elif a[0]=='vt':uv.append([float(x) for x in a[1:3]])
        elif a[0]=='f':
            assert len(a)==4
            for t in a[1:]:
                i,j,k=map(int,t.split('/'));v.append(vs[i-1]+ns[k-1]+uv[j-1]);indices.append(len(v)-1)
    return np.array(v,dtype=np.float32),[('',np.array(indices,dtype=np.uint32))]

def main():
    out=ROOT/'output/optimization-106';ctx=moderngl.create_standalone_context(require=330)
    shader=ROOT/'src/ScCsgoKnives/Shaders'
    vs=(shader/'KnifePbr.vsh').read_text().replace('#version 300 es','#version 330').replace('OPENGL_POSITION_FIX;','')
    def program(name):return ctx.program(vertex_shader=vs,fragment_shader=(shader/name).read_text().replace('#version 300 es','#version 330'))
    simple=program('WeaponSimple.psh');full=program('KnifePbr.psh')
    fbo=ctx.simple_framebuffer((420,220),components=4);fbo.use();ctx.enable(moderngl.DEPTH_TEST)
    original=zipfile.ZipFile(ROOT/'output/ScCsgoResources-1.0.0.scmod');stage=ROOT/'.tmp/optimized-resources'
    labels=[('M4A1-S Printstream','m4a1s','m4a1s_hd__cu_m4a1s_printstream','obj'),
        ('USP-S Printstream','usp_silencer','usp_silencer_hd__cu_usp_printstream','obj'),
        ('Deagle Printstream','deagle','deagle_hd__cu_deag_printstream','obj'),
        ('M249','m249','m249_hd','parts'),('Negev','negev','negev_hd','parts'),
        ('Butterfly','butterfly','butterfly_cs2','skin'),('Hands','cs2_arms','cs2_arm','skin')]
    # One actual texture triplet per representative mesh. Hands use the arm map only in this geometry preview.
    def mesh(asset,kind,optimized):
        if kind=='obj':
            names=[n for n in original.namelist() if n.startswith('Assets/Models/ScCsgoKnives/'+asset+'_legacy_cs2_weapon_offset') and n.endswith('.obj')]
            vertices=[];groups=[];offset=0
            for name in names:
                v,g=obj((stage/name).read_bytes() if optimized else original.read(name));vertices.append(v)
                groups.extend((m,i+offset) for m,i in g);offset+=len(v)
            return np.concatenate(vertices),groups
        name=asset+('.skin' if asset=='cs2_arms' else '.cs2.'+kind)
        return binary(((stage/'AnimationData' if optimized else ROOT/'src/ScCsgoKnives/AnimationData')/name).read_bytes())
    def texture(name,optimized):
        root='Assets/Textures/ScCsgoKnives/'+name
        data=(stage/(root+'.webp')).read_bytes() if optimized else original.read(root+'.png')
        im=Image.open(io.BytesIO(data)).convert('RGBA')
        tex=ctx.texture(im.size,4,im.tobytes());tex.filter=(moderngl.LINEAR,moderngl.LINEAR)
        return tex
    def draw(v,groups,material,optimized,simplified,lo,hi):
        p=simple if simplified else full
        mat=np.eye(4,dtype=np.float32);span=hi-lo;axes=np.argsort(span)[::-1];scale=1.7/max(span[axes[0]],1e-5)
        rot=np.eye(3)[[axes[0],axes[1],axes[2]]];mat[:3,:3]=rot*scale;mat[:3,3]=-rot@((lo+hi)/2)*scale;mat[2,3]-=3
        proj=np.diag([1,420/220,-.2,1]).astype(np.float32)
        p['u_worldViewMatrix'].write(mat.T.copy().tobytes());p['u_worldViewProjectionMatrix'].write((proj@mat).T.copy().tobytes())
        p['u_scopeCutout'].value=(0,1)
        p['u_lightDir1'].value=(.3,.6,.74162);p['u_lightDir2'].value=(-.5,-.4,.76811)
        textures=[]
        for i,(key,name) in enumerate([('u_baseColor',material)]+([] if simplified else [('u_orm',material+'_orm'),('u_normalMap',material+'_normal'),('u_env','env_specular_rgbm'),('u_brdf','env_brdf')])):
            t=texture(name,optimized);t.use(i);p[key].value=i;textures.append(t)
        if simplified:p['u_light'].value=1
        else:
            p['u_viewToWorld'].write(np.eye(4,dtype=np.float32).tobytes());p['u_lightColor1'].value=(.5,)*3;p['u_lightColor2'].value=(.5,)*3
            p['u_params'].value=(6,.25,1,0);p['u_params2'].value=(0,0,.25,0)
        vb=ctx.buffer(v.astype('f4').tobytes());ib=ctx.buffer(np.concatenate([g[1] for g in groups]).astype('u4').tobytes())
        vao=ctx.vertex_array(p,[(vb,'3f 3f 2f','a_position','a_normal','a_texcoord')],ib,index_element_size=4)
        fbo.clear(.09,.1,.12,1,depth=1);vao.render()
        im=Image.frombytes('RGBA',(420,220),fbo.read(components=4)).transpose(Image.Transpose.FLIP_TOP_BOTTOM)
        vao.release();vb.release();ib.release()
        for t in textures:t.release()
        return im
    canvas=Image.new('RGB',(1260,250*len(labels)+38),(20,23,28));d=ImageDraw.Draw(canvas)
    d.text((10,10),'Original geometry + Full PBR       |       Optimized + Full PBR       |       Optimized + Simple material',fill='white')
    results=[]
    for row,(label,asset,material,kind) in enumerate(labels):
        v,g=mesh(asset,kind,False);w,h=mesh(asset,kind,True);lo=v[:,:3].min(0);hi=v[:,:3].max(0)
        for col,(verts,groups,opt,simp) in enumerate([(v,g,False,False),(w,h,True,False),(w,h,True,True)]):
            im=draw(verts,groups,material,opt,simp,lo,hi);canvas.paste(im,(col*420,38+row*250));d.text((col*420+10,260+row*250),label,fill='white')
        results.append(dict(asset=asset,trianglesBefore=sum(len(x[1]) for x in g)//3,trianglesAfter=sum(len(x[1]) for x in h)//3))
    canvas.save(out/'gpu-material-comparison.png')
    (out/'gpu-preview.json').write_text(json.dumps(dict(renderer=ctx.info['GL_RENDERER'],version=ctx.info['GL_VERSION'],scope='Standalone desktop GPU, orthographic bind-pose preview. Not in-game or Android performance.',meshes=results),indent=2))
    print(out/'gpu-material-comparison.png')
if __name__=='__main__':main()
