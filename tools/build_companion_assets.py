"""Derive compact NPC meshes/clips from preserved CS2 audit exports. Never edit sources."""
import copy, hashlib, io, json, struct
from pathlib import Path
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'.tmp/cs2-companions-audit-20260920/export'
OUT=ROOT/'src/ScCsgoTactical/Assets'

class Source:
    def __init__(self,path):
        self.path=path; self.f=path.open('rb'); h=self.f.read(20)
        n=struct.unpack_from('<I',h,12)[0]; self.j=json.loads(self.f.read(n));self.start=28+n
    def view(self,i):
        v=self.j['bufferViews'][i];self.f.seek(self.start+v.get('byteOffset',0));return self.f.read(v['byteLength'])

def build(name,src,animation_source):
    j=src.j;nodes=j['nodes']; parents={c:i for i,n in enumerate(nodes) for c in n.get('children',[])}
    meshes=[i for i,n in enumerate(nodes) if 'mesh' in n and (n.get('name','').endswith(('.thirdperson_body','.thirdperson_default_gloves')) if name!='hostage' else n.get('name','').endswith('.hostage_a'))]
    assert meshes
    keep=set(meshes)
    for i in meshes:keep.update(j['skins'][nodes[i]['skin']]['joints'])
    for i in list(keep):
        while i in parents:i=parents[i];keep.add(i)
    ordered=sorted(keep);remap={v:i for i,v in enumerate(ordered)}
    doc={'asset':{'version':'2.0','generator':'CS Tactical derived NPC importer'},'scene':0,'scenes':[{'nodes':[remap[i] for i in ordered if i not in parents]}],
         'nodes':[], 'meshes':[], 'skins':[], 'materials':[], 'textures':[], 'images':[], 'bufferViews':[], 'accessors':[], 'animations':[]}
    blob=bytearray();views={};accessors={};materials={}
    def raw(b,extra=None):
        blob.extend(b'\0'*(-len(blob)%4));idx=len(doc['bufferViews']);v=dict(buffer=0,byteOffset=len(blob),byteLength=len(b));v.update(extra or {});doc['bufferViews'].append(v);blob.extend(b);return idx
    def accessor(source,i):
        key=(id(source),i)
        if key in accessors:return accessors[key]
        a=copy.deepcopy(source.j['accessors'][i]);assert 'sparse' not in a
        vi=a['bufferView'];vk=(id(source),vi)
        if vk not in views:views[vk]=raw(source.view(vi),{k:v for k,v in source.j['bufferViews'][vi].items() if k in ['byteStride','target']})
        a['bufferView']=views[vk];accessors[key]=len(doc['accessors']);doc['accessors'].append(a);return accessors[key]
    def material(i):
        if i in materials:return materials[i]
        m=j['materials'][i];tex=m['pbrMetallicRoughness']['baseColorTexture']['index'];image=j['images'][j['textures'][tex]['source']]
        im=Image.open(src.path.parent/image['uri']).convert('RGB');im.thumbnail((1024,1024),Image.Resampling.LANCZOS)
        buf=io.BytesIO();im.save(buf,format='PNG');ii=len(doc['images']);doc['images'].append({'bufferView':raw(buf.getvalue()),'mimeType':'image/png'});ti=len(doc['textures']);doc['textures'].append({'source':ii})
        materials[i]=len(doc['materials']);doc['materials'].append({'name':m.get('name',str(i)),'pbrMetallicRoughness':{'baseColorTexture':{'index':ti},'metallicFactor':0,'roughnessFactor':.8},'doubleSided':True});return materials[i]
    for old in ordered:
        n=copy.deepcopy(nodes[old]);n['children']=[remap[c] for c in n.get('children',[]) if c in remap]
        if not n['children']:n.pop('children')
        if old in meshes:
            mesh=copy.deepcopy(j['meshes'][n['mesh']]);mesh.pop('weights',None)
            for p in mesh['primitives']:
                p.pop('targets',None);p['attributes']={k:accessor(src,v) for k,v in p['attributes'].items()};p['indices']=accessor(src,p['indices']);p['material']=material(p['material'])
            n['mesh']=len(doc['meshes']);doc['meshes'].append(mesh)
            skin=copy.deepcopy(j['skins'][n['skin']]);skin['joints']=[remap[c] for c in skin['joints']]
            if 'skeleton' in skin:skin['skeleton']=remap[skin['skeleton']]
            skin['inverseBindMatrices']=accessor(src,skin['inverseBindMatrices']);n['skin']=len(doc['skins']);doc['skins'].append(skin)
        else:n.pop('mesh',None);n.pop('skin',None)
        doc['nodes'].append(n)
    names={n.get('name'):i for i,n in enumerate(doc['nodes'])}
    wanted={'idle':'animation/anims/world/knife/_default_knife/idle_knife','walk':'animation/anims/world/knife/_default_knife/walk_n_knife',
            'run':'animation/anims/world/knife/_default_knife/run_n_knife','aim':'animation/anims/world/rifle/_default_rifle/idle_rifle',
            'aimwalk':'animation/anims/world/rifle/_default_rifle/walk_n_rifle',
            'shield':'animation/anims/world/rifle/_default_rifle/idle_rifle',
            'shieldwalk':'animation/anims/world/rifle/_default_rifle/walk_n_rifle'}
    for alias,path in wanted.items():
        a=next(a for a in animation_source.j['animations'] if a['name']==path);out={'name':alias,'channels':[],'samplers':[]}
        choices=[(a,c) for c in a['channels']]
        for a,c in choices:
            target=animation_source.j['nodes'][c['target']['node']].get('name');prop=c['target']['path']
            if target not in names or (target=='root_motion' and prop=='translation') or prop=='weights':continue
            s=copy.deepcopy(a['samplers'][c['sampler']]);s['input']=accessor(animation_source,s['input']);s['output']=accessor(animation_source,s['output'])
            out['channels'].append({'sampler':len(out['samplers']),'target':{'node':names[target],'path':prop}});out['samplers'].append(s)
        doc['animations'].append(out)
    if name=='hostage':
        # Hostage export lacks the agents' axis-conversion root_motion parent. A same-name
        # animation transfer alone rotates the entire hostage onto its side.
        old_roots=doc['scenes'][0]['nodes'];root_index=len(doc['nodes'])
        doc['nodes'].append({'name':'hostage_axis','rotation':[-.5,-.5,-.5,.5],'children':old_roots})
        doc['scenes'][0]['nodes']=[root_index]
    blob.extend(b'\0'*(-len(blob)%4));doc['buffers']=[{'byteLength':len(blob)}];js=json.dumps(doc,separators=(',',':')).encode();js+=b' '*(-len(js)%4)
    target=OUT/f'Models/ScCsgoTactical/{name}.glb';target.parent.mkdir(parents=True,exist_ok=True)
    target.write_bytes(struct.pack('<4sII',b'glTF',2,28+len(js)+len(blob))+struct.pack('<II',len(js),0x4e4f534a)+js+struct.pack('<II',len(blob),0x004e4942)+blob)
    from bake_tactical_shield_pose import bake
    bake(target)
    from compact_tactical_skin import compact
    palette=compact(target)
    return {'name':name,'bytes':target.stat().st_size,'nodes':len(doc['nodes']),'meshes':len(meshes),'clips':list(wanted),'palette':palette,'sha256':hashlib.sha256(target.read_bytes()).hexdigest(),'source':str(src.path.relative_to(ROOT))}

def main():
    t=Source(SOURCE/'agents/models/tm_phoenix/tm_phoenix.glb');ct=Source(SOURCE/'agents/models/ctm_sas/ctm_sas.glb');h=Source(SOURCE/'models/hostage/hostage.glb')
    records=[build('ct',ct,ct),build('t',t,t),build('hostage',h,t)]
    config={'template':'Simple','rootBoneRotation':180,'modelScale':1,'animations':{n:{'source':n,'speed':1,'loop':True,'blendDuration':.18} for n in ['idle','walk','run','aim','aimwalk','shield','shieldwalk']},
       'states':{'gait':{'layer':'Base','rules':[{'condition':'IsDead','animation':None},{'condition':'[Shield] && [SpeedAbs] > 0.15','animation':'shieldwalk'},{'condition':'Shield','animation':'shield'},{'condition':'[Armed] && [SpeedAbs] > 0.15','animation':'aimwalk'}, {'condition':'Armed','animation':'aim'},{'condition':'[SpeedAbs] > 2.8','animation':'run'},{'condition':'[SpeedAbs] > 0.15','animation':'walk'},{'condition':'true','animation':'idle'}]}}}
    p=OUT/'Animations';p.mkdir(parents=True,exist_ok=True);(p/'ScTactical.json').write_text(json.dumps(config,indent=2),encoding='utf8')
    tex=OUT/'Textures/ScCsgoTactical';tex.mkdir(parents=True,exist_ok=True)
    shield=ROOT.parent/'CSMCReverse/local_cs2_analysis/all_weapons/03_legacy_vmodels_materials/materials/models/weapons/v_models/shield/shield_color.png'
    Image.open(shield).convert('RGB').save(tex/'shield.png')
    (ROOT/'docs/tactical-derived-assets.json').write_text(json.dumps(records,ensure_ascii=False,indent=2)+'\n',encoding='utf8');print(json.dumps(records))

if __name__=='__main__':main()
