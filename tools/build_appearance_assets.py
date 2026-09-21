"""Bake the installed CS2 glove SPIR-V compositor at .06 wear via OpenGL.
Source exports stay read-only. See docs/firstperson-gloves-assets.json for hashes.
"""
from pathlib import Path
import argparse, hashlib, json, re, math, subprocess
import numpy as np
from PIL import Image
import moderngl
import audit_requested_cs2_finishes as audit
import cs2_kv3
from cs2_glb import Glb
from cs2_glb_to_skinned import convert

ROOT=Path(__file__).resolve().parents[1]
STAGE=ROOT/'.tmp/gloves-20260921'
PAINT=STAGE/'decoded/gloves/paints'
DLC=ROOT/'src/ScCsgoTactical'
TEX=DLC/'Assets/Textures/ScCsgoKnives'
MESH=DLC/'ArmData'
SHADER=STAGE/'shader/shaders/vfx/csgo_customglove_vulkan_50.vfx'
CATALOG=[('sporty_green','树篱迷宫','sporty',10038),('sporty_purple','潘多拉之盒','sporty',10037),
 ('specialist_kimono_diamonds_red','深红和服','specialist',10033),
 ('sporty_blue_pink','迈阿密风云','sporty',10048),('slick_red','深红织物','slick',10016)]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def vmat(p):return dict(audit.kv(p.read_text('utf8'))[0][1])
def numbers(s):return [float(x) for x in re.findall(r'-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?',str(s))]

def stage_raw():
    index=audit.vpk.read_vpk_index(); paths=set()
    audit.vpk.extract_entry('scripts/items/items_game.txt',index['scripts/items/items_game.txt'],STAGE/'items_game.txt')
    for key,_,_,_ in CATALOG:
        for k,p in vmat(PAINT/(key+'.vmat'))['Compiled Textures']: paths.add(p+'_c')
    for p in paths: audit.vpk.extract_entry(p,index[p],STAGE/'raw'/p)
    subprocess.run([str(ROOT/'.tmp/vrf-cli/Source2Viewer-CLI.exe'),'-i',str(STAGE/'raw'),'-o',str(STAGE/'raw-decoded'),'-d','--recursive','--texture_decode_flags','none'],check=True)

def prepare_shader(ctx):
    source=SHADER.read_text('utf8')
    glsl=source[source.index('#version',source.index('GLSL reflection')):]
    glsl=glsl[:glsl.index('// ---------')]
    glsl=re.sub(r'#version[^\n]+','#version 330',glsl)
    glsl=re.sub(r'^\s*#extension[^\n]+','',glsl,flags=re.M)
    glsl=re.sub(r'layout\([^)]*\) uniform _2422 _Globals_;','uniform _2422 _Globals_;',glsl)
    glsl=re.sub(r'layout\([^)]*\) uniform texture2D (\w+);',r'uniform sampler2D \1;',glsl)
    glsl=re.sub(r'layout\([^)]*\) uniform sampler \w+;','',glsl)
    glsl=re.sub(r'sampler2D\((\w+), \w+\)',r'\1',glsl)
    glsl=glsl.replace('layout(location = 0) in vec4 input_0;','in vec4 input_0;')
    # Preserve native albedo; take final normal/ORM before Source2 oct/sRGB packing.
    glsl=glsl.replace('output_0 = _6805;', '''
        if (_Globals_.g_nOutputMode == 1) output_0=vec4(_23712*0.5+0.5,1);
        else if (_Globals_.g_nOutputMode == 2) output_0=vec4(_22205,_21931,_19526,1);
        else output_0=vec4(_6805.rgb,1);
    ''')
    glsl=glsl.replace('#version 330','#version 330\n#define saturate(x) clamp(x,0.0,1.0)')
    # Native packed normals have a few lower-hemisphere texels in UV padding.
    # GLSL pow(negative, fractional) is undefined; bound the AO cosine physically.
    glsl=glsl.replace('pow(_24364.z,','pow(max(_24364.z,0.0),')
    vert='''#version 330
    in vec2 pos;out vec4 input_0;uniform float side;
    void main(){gl_Position=vec4(pos,0,1);input_0=vec4((pos+1)*0.5,0,0);if(side<0)input_0.x-=1;}
    '''
    prog=ctx.program(vertex_shader=vert,fragment_shader=glsl)
    defaults={}
    for name,annotation in re.findall(r'\b(?:float[234]?|int[234]?|bool) (g_\w+)\s*<([\s\S]*?)>;',source[:source.index('GLSL reflection')]):
        match=re.search(r'Default\d?\(([^)]+)\)',annotation)
        if name not in defaults or match: defaults[name]=numbers(match[1]) if match else [0]
    fields=re.findall(r'(float|int|vec[234]|ivec[234]) (g_\w+);',glsl[glsl.index('struct _2422'):glsl.index('uniform _2422')])
    return prog,defaults,fields

def bake(ctx,prog,defaults,fields,key):
    p=vmat(PAINT/(key+'.vmat')); values=dict(defaults)
    values.update({k:numbers(v) for k,v in p.items() if not isinstance(v,list) and k.startswith('g_')})
    for node in cs2_kv3.walk(cs2_kv3.load(PAINT/(key+'.vcompmat'))):
        if node.get('m_strAlias')=='instance_params':
            for v in node.get('m_vecLooseVariables',[]): values[v['m_strName']]=[v.get('m_flValueFloatX',0),v.get('m_flValueFloatY',0)]
    def get(k,d=0):return values.get(k,[d])[0]
    values['g_bPattern']=[int(p.get('F_PATTERN','0'))]
    values['g_fWearProgress']=[0.06**get('g_fWearExponent',1)]
    values['g_nPatternReplaceIndex']=[get('g_nPatternReplaceIndex',1)-1]
    values['g_fFlipFixup']=[-1 if get('g_bFlipFixup') else 1]
    values['g_fPaintShadowPower']=[max(.01,get('g_fPaintThickness',1)*.15)]
    values['g_vTextileAlbedoLevels']=[.045,-1.4427*math.log(.4),1]
    values['g_vMetallicTextileAlbedoLevels']=[.08,-1.4427*math.log(.4),.92]
    for i in range(1,5): values[f'g_fDetailBlackPointCompensation{i}']=[1-get(f'g_fDetailBlackPoint{i}')]
    for kind in ['Pattern','Grunge']:
        flip=-1 if kind=='Pattern' and get('g_bFlipFixup') else 1
        angle=get(f'g_f{kind}TexCoordRotation')*flip*3.14159/180
        for rotation in [False,True]:
            a=-angle if rotation else angle;s=1 if rotation else get(f'g_f{kind}TexCoordScale',2.5)
            c=math.cos(a);sn=math.sin(a);half=.5/(s or 1)
            v7=half*math.cos(-a)-half*math.sin(-a);v8=v7*math.sin(-a)+half*math.cos(-a)
            off=[0,0] if rotation else values.get(f'g_f{kind}TexCoordOffset',[0,0])
            if len(off)==1:off=off*2
            stem=f'g_v{kind}Tex'+('Rotation' if rotation else 'Coord')+'Xform'
            values[stem+'0']=[c*s,-sn*s,0,s*c*v7-s*sn*v8+off[0]-.5]
            values[stem+'1']=[sn*s,c*s,0,s*sn*v7+s*c*v8+off[1]-.5]
    for typ,name in fields:
        uniform='_Globals_.'+name
        if uniform not in prog:continue
        count=int(typ[-1]) if typ[-1].isdigit() else 1
        vals=values.get(name,[0]);vals=(vals*count)[:count] if len(vals)==1 else vals[:count]
        assert len(vals)==count,(name,vals)
        if typ.startswith('i'):vals=[int(x) for x in vals]
        prog[uniform].value=vals[0] if count==1 else tuple(vals)
    textures=[]
    for slot,(name,resource) in enumerate(p['Compiled Textures']):
        if name not in prog:continue
        path=STAGE/'raw-decoded'/Path(resource).with_suffix('.png')
        im=Image.open(path).convert('RGBA')
        tex=ctx.texture(im.size,4,im.tobytes());tex.build_mipmaps();tex.filter=(moderngl.LINEAR_MIPMAP_LINEAR,moderngl.LINEAR)
        tex.use(slot);prog[name].value=slot;textures.append(tex)
    buf=ctx.buffer(np.float32([-1,-1,1,-1,-1,1,1,1]).tobytes());vao=ctx.simple_vertex_array(prog,buf,'pos')
    target=ctx.texture((1024,1024),4,dtype='f4');fb=ctx.framebuffer([target]);fb.use()
    for side in [1,-1]:
        prog['side'].value=side
        for mode,suffix in [(0,''),(1,'_normal'),(2,'_orm')]:
            prog['_Globals_.g_nOutputMode'].value=mode;vao.render(moderngl.TRIANGLE_STRIP)
            data=np.frombuffer(fb.read(components=4,dtype='f4'),np.float32).reshape(1024,1024,4)
            assert np.isfinite(data).all(),(key,mode,'non-finite pixels',(~np.isfinite(data)).sum(axis=(0,1)).tolist())
            if mode==0:data=np.where(data<=.0031308,12.92*data,1.055*np.maximum(data,0)**(1/2.4)-.055)
            path=TEX/f'tactical_arm_{key}_{"left" if side==1 else "right"}{suffix}.png'
            Image.fromarray(np.uint8(np.clip(data[:,:,:3]*255+.5,0,255))).save(path,optimize=True)
    vao.release();buf.release();fb.release();target.release()
    for t in textures:t.release()

def import_models():
    mats={};meshes={}
    def install(path,want,key):
        blob,joints,stats=convert(path,want);(MESH/(key+'.skin')).write_bytes(blob)
        g=Glb(path);mi=next(i for i,m in enumerate(g.meshes()) if want in m.name)
        meshes[key]={'sha256':sha(path),'mesh':g.meshes()[mi].name,'joints':joints,'stats':stats}
        for prim in g.meshes()[mi].primitives:
            name=prim.material
            if name in mats or name=='bare_arm_133' or name.startswith(('glove_sporty','glove_specialist','glove_slick')):continue
            mat=next(m for m in g.json['materials'] if m['name']==name)
            def image(prop,default):
                if prop is None:return Image.new('RGB',(1024,1024),default)
                img=g.json['textures'][prop['index']]['source'];im=Image.open(path.parent/g.json['images'][img]['uri']).convert('RGB')
                im.thumbnail((2048,2048));return im
            pbr=mat.get('pbrMetallicRoughness',{});nameout='tactical_arm_'+name
            image(pbr.get('baseColorTexture'),(255,255,255)).save(TEX/(nameout+'.png'))
            image(mat.get('normalTexture'),(128,128,255)).save(TEX/(nameout+'_normal.png'))
            ao=image(mat.get('occlusionTexture'),(255,255,255)).getchannel('R')
            Image.merge('RGB',(ao,Image.new('L',ao.size,178),Image.new('L',ao.size,0))).save(TEX/(nameout+'_orm.png'))
            mats[name]=nameout
    for role,agent in [('ct','ctm_sas'),('t','tm_phoenix')]:
        path=STAGE/f'rigged/agents/models/{agent}/{agent}.glb'
        install(path,'firstperson_default_gloves_arms',role+'_default')
        install(path,'firstperson_sleeves',role+'_sleeves')
    for kind in ['sporty','specialist','slick']:
        install(audit.EXPORT/f'08_first_person/glb/agents/models/shared/arms/glove_{kind}/glove_{kind}.glb','viewmodel','glove_'+kind)
    (MESH/'materials.json').write_text(json.dumps(mats,indent=2),encoding='utf8')
    return meshes

if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--stage',action='store_true');ap.add_argument('--audit-only',action='store_true');args=ap.parse_args()
    if args.stage:stage_raw()
    TEX.mkdir(parents=True,exist_ok=True);MESH.mkdir(parents=True,exist_ok=True)
    ctx=moderngl.create_standalone_context()
    if args.audit_only:meshes=json.loads((ROOT/'docs/firstperson-gloves-assets.json').read_text('utf8'))['meshes']
    else:
        prog,defaults,fields=prepare_shader(ctx)
        for key,*_ in CATALOG:bake(ctx,prog,defaults,fields,key);print('Baked',key,flush=True)
        meshes=import_models()
    items=audit.kv((STAGE/'items_game.txt').read_text('utf-8-sig'))[0][1]
    paints=next(dict(section) for name,section in items if name=='paint_kits')
    default=dict(paints['0']);minimum={}
    for key,label,kind,paintid in CATALOG:
        entry=dict(paints[str(paintid)]);assert entry['name']==key
        minimum[key]=float(entry.get('wear_remap_min',default['wear_remap_min']));assert minimum[key]==.06
    report={'wear':.06,'minimumWear':minimum,'itemsSha256':sha(STAGE/'items_game.txt'),'shaderSha256':sha(SHADER),'renderer':ctx.info['GL_RENDERER'],'catalog':CATALOG,'meshes':meshes,
      'inputs':{str(p.relative_to(STAGE)):sha(p) for p in list((STAGE/'raw').rglob('*'))+list(PAINT.glob('*.vmat'))+list(PAINT.glob('*.vcompmat')) if p.is_file()},
      'limitations':['Fixed pattern offset (0,0), recipe rotation; not a claimed CS2 economy paint seed.',
       'Source2 GLSL compositor adapted to OpenGL; raw packed textures and native wear expression.',
       'Agent sleeve/default-glove roughness estimated 0.7; glove paint ORM computed by original shader.'],
      'textures':{p.name:sha(p) for p in TEX.glob('tactical_arm_*.png')}}
    (ROOT/'docs/firstperson-gloves-assets.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
