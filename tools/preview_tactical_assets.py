"""Offline asset-only skinning/render check. Not a Survivalcraft screenshot."""
from pathlib import Path
import sys,copy,json,io
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'.tmp/optimization-deps'))
import numpy as np
import moderngl
from PIL import Image,ImageDraw
from cs2_glb import Glb
from cs2_knife_render import key

def posed(g,clip,time):
    original=copy.deepcopy(g.json['nodes'])
    for c in next(a for a in g.json['animations'] if a['name']==clip)['channels']:
        s=next(a for a in g.json['animations'] if a['name']==clip)['samplers'][c['sampler']]
        value=key({'Times':g.accessor(s['input']).ravel().tolist(),'Values':g.accessor(s['output']).tolist()},time)
        node=g.json['nodes'][c['target']['node']];node.pop('matrix',None);node[c['target']['path']]=value.tolist()
    absolute=[g.world_matrix(i) for i in range(len(g.nodes))]
    meshes=[]
    for ni,node in enumerate(g.nodes):
        if 'mesh' not in node:continue
        sk=g.skin(node['skin']);matrices=np.array([ib@absolute[j] for ib,j in zip(sk['inverse_bind'],sk['joint_nodes'])])
        for primitive in g.json['meshes'][node['mesh']]['primitives']:
            at={k:g.accessor(v) for k,v in primitive['attributes'].items()};pos=at['POSITION'];joint=at['JOINTS_0'].astype(int);weight=at['WEIGHTS_0']
            skin=np.sum(matrices[joint]*weight[:,:,None,None],axis=1);v=np.einsum('ni,nij->nj',np.c_[pos,np.ones(len(pos))],skin)[:,:3]
            n=np.einsum('ni,nij->nj',at['NORMAL'],skin[:,:3,:3]);p=primitive['material'];m=g.json['materials'][p]
            img=g.json['images'][g.json['textures'][m['pbrMetallicRoughness']['baseColorTexture']['index']]['source']];view=g.json['bufferViews'][img['bufferView']];start=view.get('byteOffset',0)
            texture=Image.open(io.BytesIO(g.bin[start:start+view['byteLength']])).convert('RGB')
            meshes.append((v,n,at['TEXCOORD_0'],g.accessor(primitive['indices']).ravel(),texture))
    g.json['nodes']=original
    return meshes,absolute

def main():
    out=ROOT/'output/release-1.4.0';out.mkdir(exist_ok=True)
    ctx=moderngl.create_standalone_context(require=330);fbo=ctx.simple_framebuffer((400,520));fbo.use();ctx.enable(moderngl.DEPTH_TEST)
    shader=ctx.program(vertex_shader='''#version 330
    in vec3 p;in vec3 n;in vec2 uv;uniform mat4 mvp;out vec2 t;out vec3 norm;
    void main(){gl_Position=mvp*vec4(p,1);t=uv;norm=n;}''',fragment_shader='''#version 330
    in vec2 t;in vec3 norm;uniform sampler2D image;out vec4 c;
    void main(){c=vec4(texture(image,t).rgb*(.6+.4*abs(dot(normalize(norm),normalize(vec3(.2,.5,1))))),1);}''')
    canvas=Image.new('RGB',(1200,1620),(20,24,29));draw=ImageDraw.Draw(canvas);report=[]
    for col,name in enumerate(['ct','t','hostage']):
        g=Glb(ROOT/f'src/ScCsgoTactical/Assets/Models/ScCsgoTactical/{name}.glb')
        for row,clip in enumerate(['idle','aimwalk','shield']):
            parts,bones=posed(g,clip,.35);allpos=np.concatenate([p[0] for p in parts]);low=allpos.min(0);high=allpos.max(0)
            report.append({'name':name,'clip':clip,'min':low.tolist(),'max':high.tolist(),'bones':{g.nodes[i].get('name'):np.round(m[3,:3],4).tolist() for i,m in enumerate(bones) if g.nodes[i].get('name') in ['hand_L','hand_R','weapon_hand_R','head','pelvis','root_motion']}})
            mat=np.eye(4,dtype='f4');mat[0,0]=.82;mat[1,1]=.82;mat[1,3]=-.83;mat[2,2]=-.15
            shader['mvp'].write(mat.T.copy().tobytes());shader['image'].value=0;fbo.clear(.08,.095,.11,1,depth=1)
            for v,n,uv,idx,im in parts:
                tex=ctx.texture(im.size,3,im.tobytes());tex.filter=(moderngl.LINEAR,moderngl.LINEAR);tex.use(0)
                vb=ctx.buffer(np.c_[v,n,uv].astype('f4').tobytes());ib=ctx.buffer(idx.astype('u4').tobytes());vao=ctx.vertex_array(shader,[(vb,'3f 3f 2f','p','n','uv')],ib);vao.render();vao.release();vb.release();ib.release();tex.release()
            image=Image.frombytes('RGB',(400,520),fbo.read(components=3)).transpose(Image.Transpose.FLIP_TOP_BOTTOM);canvas.paste(image,(col*400,row*540+20));draw.text((col*400+12,row*540+4),name+' / '+clip+' | OFFLINE ASSET CHECK',fill='white')
    canvas.save(out/'tactical-model-preview.png');(out/'tactical-model-bounds.json').write_text(json.dumps(report,indent=2),encoding='utf8');print(json.dumps(report))

if __name__=='__main__':main()
