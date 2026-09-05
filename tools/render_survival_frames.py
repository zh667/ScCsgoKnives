"""Offline PBR contact sheet from PackageCheck --frames-out runtime vertices.

Not an in-game screenshot. Uses the mod shader and textures; simplified lighting.
"""
import argparse,json,sys
from pathlib import Path
import numpy as np
from PIL import Image,ImageDraw
import validate_shaders

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--frames',type=Path,required=True);ap.add_argument('--out',type=Path,required=True);ap.add_argument('--runtime',type=Path);args=ap.parse_args()
    if args.runtime:sys.path.insert(0,str(args.runtime))
    import moderngl
    root=Path(__file__).resolve().parent.parent
    texture_dir=root/'src/ScCsgoKnives/Assets/Textures/ScCsgoKnives'
    ctx=moderngl.create_standalone_context(require=330)
    prog=ctx.program(vertex_shader=validate_shaders.prepend((root/'src/ScCsgoKnives/Shaders/KnifePbr.vsh').read_text('utf-8'),True),
        fragment_shader=validate_shaders.prepend((root/'src/ScCsgoKnives/Shaders/KnifePbr.psh').read_text('utf-8'),False))
    prog['u_glymul']=1;prog['u_viewToWorld'].write(np.eye(4,dtype='f4').tobytes())
    prog['u_lightDir1']=(.6,.8,0);prog['u_lightDir2']=(-.7,.3,.6)
    prog['u_lightColor1']=(.5,.5,.5);prog['u_lightColor2']=(.5,.5,.5)
    prog['u_params']=(6,.25,1,0);prog['u_params2']=(0,0,.25,0);prog['u_scopeCutout']=(0,1)
    textures={}
    def tex(name,unit,uniform):
        if name not in textures:
            im=Image.open(texture_dir/(name+'.png')).convert('RGBA').transpose(Image.Transpose.FLIP_TOP_BOTTOM)
            t=ctx.texture(im.size,4,im.tobytes());t.filter=(moderngl.LINEAR,moderngl.LINEAR);textures[name]=t
        textures[name].use(unit);prog[uniform]=unit
    tex('env_specular_rgbm',3,'u_env');tex('env_brdf',4,'u_brdf')
    width,height=640,360;fy=4/3/np.tan(np.deg2rad(68/2));aspect=width/height
    projection=np.zeros((4,4));projection[0,0]=fy/aspect;projection[1,1]=fy;projection[2,2]=64/(.005-64);projection[2,3]=-1;projection[3,2]=.005*64/(.005-64)
    prog['u_worldViewMatrix'].write(np.eye(4,dtype='f4').tobytes());prog['u_worldViewProjectionMatrix'].write(projection.astype('f4').tobytes())
    kinds=['hegrenade','flashbang','smokegrenade','molotov','incendiary','decoy']
    sheet=Image.new('RGB',(width*2,(height+30)*6),(24,28,34));draw=ImageDraw.Draw(sheet)
    for row,kind in enumerate(kinds):
        for col,alias in enumerate(['idle','pullpin']):
            doc=json.loads((args.frames/('grenade_'+kind+'_'+alias+'.json')).read_text('utf-8'))
            fb=ctx.simple_framebuffer((width,height));fb.use();fb.clear(.19,.25,.31,1);ctx.enable(moderngl.DEPTH_TEST)
            for mesh in doc['meshes']:
                v=np.array(mesh['vertices'],dtype='f4');vbo=ctx.buffer(v.tobytes())
                for part in mesh['parts']:
                    material=part['material']
                    if material=='weapon_molotov_flame':continue
                    key=('cs2_glove' if material.startswith('glove') else 'cs2_arm') if mesh['arms'] else ('weapon_molotov_liquid' if material=='weapon_molotov_liquid' else doc['asset']+'_cs2')
                    tex(key,0,'u_baseColor');tex(key+'_orm',1,'u_orm');tex(key+'_normal',2,'u_normalMap')
                    ibo=ctx.buffer(np.array(part['indices'],dtype='u4').tobytes());vao=ctx.vertex_array(prog,[(vbo,'3f 3f 2f','a_position','a_normal','a_texcoord')],ibo,index_element_size=4)
                    vao.render();vao.release();ibo.release()
                vbo.release()
            im=Image.frombytes('RGB',(width,height),fb.read()).transpose(Image.Transpose.FLIP_TOP_BOTTOM);fb.release()
            y=row*(height+30);sheet.paste(im,(col*width,y+30));draw.text((col*width+8,y+8),f'OFFLINE | {kind} {alias} @ {doc["time"]:.2f}s',fill='white')
    args.out.parent.mkdir(parents=True,exist_ok=True);sheet.save(args.out)
    print(args.out,'GPU:',ctx.info['GL_RENDERER'])

if __name__=='__main__':main()
