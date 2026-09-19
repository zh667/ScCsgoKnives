"""Offline first-person shield + actual CS2 skinned arms; not an in-game capture."""
from pathlib import Path
import sys,json,struct
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'.tmp/optimization-deps'))
import numpy as np
import moderngl
from scipy.spatial.transform import Rotation
from PIL import Image,ImageDraw
from cs2_glb import Glb
from cs2_knife_render import read_skin,sample,resolve_clip
import cs2_placement as P

def main():
    data=ROOT/'src/ScCsgoKnives/AnimationData';path=data/'cs2_arms.skin'
    joints,inverse,pos,bones,weights,prims=read_skin(path)
    # Read normal/UV channels from the same binary; read_skin intentionally only returns positions.
    b=path.read_bytes();offset=14
    for _ in joints:n=struct.unpack_from('<H',b,offset)[0];offset+=2+n+64
    count=struct.unpack_from('<i',b,offset)[0];raw=np.frombuffer(b,np.uint8,count*52,offset+4).reshape(count,52)
    normals=raw[:,12:24].copy().view('<f4').reshape(-1,3);uv=raw[:,24:32].copy().view('<f4').reshape(-1,2)
    rig=json.loads((data/'c4.cs2.animation.json').read_text());absolute=sample(rig,resolve_clip(rig,'idle'),0)
    for side in ['L','R']:
        parent='arm_lower_'+side;hand='hand_'+side
        for suffix,weight in [('TWIST',.5),('TWIST1',1.)]:
            name=parent+'_'+suffix;rest=np.linalg.inv(inverse[joints.index(name)])@inverse[joints.index(parent)]
            local=absolute[hand]@np.linalg.inv(absolute[parent]);q=Rotation.from_matrix(local[:3,:3].T).as_quat();twist=np.array([q[0],0,0,q[3]]);twist/=np.linalg.norm(twist)
            if twist[3]<0:twist=-twist
            m=np.eye(4);m[:3,:3]=Rotation.from_rotvec(Rotation.from_quat(twist).as_rotvec()*weight).as_matrix().T;absolute[name]=rest@m@absolute[parent]
    matrices=np.array([ib@absolute[n]@P.placement() if n in absolute else np.zeros((4,4)) for n,ib in zip(joints,inverse)])
    skin=(matrices[bones]*weights[:,:,None,None]).sum(axis=1)
    xyz=np.einsum('vi,vij->vj',np.c_[pos,np.ones(len(pos))],skin)[:,:3]+[-.10,-.22,-.12]
    norm=np.einsum('vi,vij->vj',normals,skin[:,:3,:3]);parts=[]
    for mat,idx in prims:parts.append((xyz,norm,uv,idx,ROOT/f'src/ScCsgoKnives/Assets/Textures/ScCsgoKnives/{"cs2_glove" if mat.startswith("glove") else "cs2_arm"}.png'))
    g=Glb(ROOT/'src/ScCsgoTactical/Assets/Models/ScCsgoTactical/shield.glb')
    for primitive in g.json['meshes'][0]['primitives']:
        a={k:g.accessor(v) for k,v in primitive['attributes'].items()}
        parts.append((a['POSITION']+[0,-.48,-.8],a['NORMAL'],a['TEXCOORD_0'],g.accessor(primitive['indices']).ravel(),ROOT/'src/ScCsgoTactical/Assets/Textures/ScCsgoTactical/shield.png'))
    ctx=moderngl.create_standalone_context(require=330);ctx.enable(moderngl.DEPTH_TEST)
    prog=ctx.program(vertex_shader='''#version 330
    in vec3 p;in vec3 n;in vec2 uv;uniform mat4 mvp;out vec2 t;out vec3 normal;
    void main(){gl_Position=mvp*vec4(p,1);t=uv;normal=n;}''',fragment_shader='''#version 330
    in vec2 t;in vec3 normal;uniform sampler2D image;out vec4 c;
    void main(){c=vec4(texture(image,t).rgb*(.7+.3*abs(normalize(normal).z)),1);}''')
    canvas=Image.new('RGB',(960,960));draw=ImageDraw.Draw(canvas)
    for row,(w,h) in enumerate([(960,540),(960,360)]):
        fbo=ctx.simple_framebuffer((w,h));fbo.use();fbo.clear(.20,.27,.32,1,depth=1)
        fx,fy=P.projection_scales(68,w/h);near=.02;far=64
        mat=np.zeros((4,4),dtype='f4');mat[0,0]=fx;mat[1,1]=fy;mat[2,2]=(far+near)/(near-far);mat[2,3]=2*near*far/(near-far);mat[3,2]=-1
        prog['mvp'].write(mat.T.copy().tobytes());prog['image'].value=0
        for xyz,n,uv,idx,texture in parts:
            im=Image.open(texture).convert('RGB');tex=ctx.texture(im.size,3,im.tobytes());tex.filter=(moderngl.LINEAR,moderngl.LINEAR);tex.use(0)
            vb=ctx.buffer(np.c_[xyz,n,uv].astype('f4').tobytes());ib=ctx.buffer(idx.astype('u4').tobytes());vao=ctx.vertex_array(prog,[(vb,'3f 3f 2f','p','n','uv')],ib);vao.render();vao.release();vb.release();ib.release();tex.release()
        image=Image.frombytes('RGB',(w,h),fbo.read(components=3)).transpose(Image.Transpose.FLIP_TOP_BOTTOM);y=30 if row==0 else 600;canvas.paste(image,(0,y));draw.text((10,y-20),f'OFFLINE SHIELD / CS2 ARMS | {w}x{h}',fill='white');fbo.release()
    output=ROOT/'output/release-1.4.0/tactical-shield-preview.png';canvas.save(output);print(output)
if __name__=='__main__':main()
