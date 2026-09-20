"""Author an adapted shield and independent entity templates. Does not alter any CS2 source."""
import json,struct,uuid,xml.etree.ElementTree as E,shutil
from pathlib import Path
from PIL import Image,ImageDraw
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'src/ScCsgoTactical/Assets'

def shield():
    # .92 x 1.44 m. Original shield texture has front/back panels in each atlas half.
    # Author the geometry around a recessed reinforced viewport; this is not Valve geometry.
    positions=[]; normals=[];uv=[];indices=[]
    def quad(points,normal,tex):
        base=len(positions);positions.extend(points);normals.extend([normal]*4);uv.extend(tex);indices.extend([base,base+1,base+2,base,base+2,base+3])
    def box(x0,y0,z0,x1,y1,z1,u0=.52,v0=.22,u1=.98,v1=.98):
        quad([(x0,y0,z0),(x0,y1,z0),(x1,y1,z0),(x1,y0,z0)],(0,0,-1),[(u0,v1),(u0,v0),(u1,v0),(u1,v1)])
        quad([(x1,y0,z1),(x1,y1,z1),(x0,y1,z1),(x0,y0,z1)],(0,0,1),[(.02,v1),(.02,v0),(.48,v0),(.48,v1)])
        for pts,n in [([(x0,y0,z1),(x0,y1,z1),(x0,y1,z0),(x0,y0,z0)],(-1,0,0)), ([(x1,y0,z0),(x1,y1,z0),(x1,y1,z1),(x1,y0,z1)],(1,0,0)), ([(x0,y1,z0),(x0,y1,z1),(x1,y1,z1),(x1,y1,z0)],(0,1,0)), ([(x0,y0,z1),(x0,y0,z0),(x1,y0,z0),(x1,y0,z1)],(0,-1,0))]:quad(pts,n,[(.2,.04),(.2,.07),(.7,.07),(.7,.04)])
    box(-.46,-.72,-.035,.46,.4,.035,.52,.43,.98,.98)
    box(-.46,.56,-.035,.46,.72,.035,.52,.22,.98,.30)
    box(-.46,.4,-.035,-.32,.56,.035,.52,.30,.59,.38)
    box(.32,.4,-.035,.46,.56,.035,.91,.30,.98,.38)
    # Side handles on the rear, authored to sit where adapted two-hand pose grips.
    box(-.15,-.18,.035,-.10,.16,.12);box(.10,-.18,.035,.15,.16,.12)
    doc={'asset':{'version':'2.0','generator':'ZH667 adapted shield; CS2 material, authored geometry'},'scene':0,'scenes':[{'nodes':[0]}],'nodes':[{'name':'shield','mesh':0}],'meshes':[{'primitives':[]}],'bufferViews':[],'accessors':[]}
    blob=bytearray()
    def acc(values,fmt,typ,component):
        flat=[c for v in values for c in v] if isinstance(values[0],tuple) else values
        blob.extend(b'\0'*(-len(blob)%4));data=struct.pack('<'+fmt*len(flat),*flat);vi=len(doc['bufferViews']);doc['bufferViews'].append({'buffer':0,'byteOffset':len(blob),'byteLength':len(data)});blob.extend(data)
        a={'bufferView':vi,'componentType':component,'count':len(values),'type':typ}
        if typ=='VEC3':a.update(min=[min(v[i] for v in values) for i in range(3)],max=[max(v[i] for v in values) for i in range(3)])
        ai=len(doc['accessors']);doc['accessors'].append(a);return ai
    p={'attributes':{'POSITION':acc(positions,'f','VEC3',5126),'NORMAL':acc(normals,'f','VEC3',5126),'TEXCOORD_0':acc(uv,'f','VEC2',5126)},'indices':acc(indices,'H','SCALAR',5123)}
    doc['meshes'][0]['primitives'].append(p);blob.extend(b'\0'*(-len(blob)%4));doc['buffers']=[{'byteLength':len(blob)}]
    js=json.dumps(doc,separators=(',',':')).encode();js+=b' '*(-len(js)%4)
    (OUT/'Models/ScCsgoTactical/shield.glb').write_bytes(struct.pack('<4sII',b'glTF',2,28+len(js)+len(blob))+struct.pack('<II',len(js),0x4e4f534a)+js+struct.pack('<II',len(blob),0x004e4942)+blob)
    im=Image.new('RGBA',(128,128));d=ImageDraw.Draw(im);d.rounded_rectangle((30,28,98,116),12,fill='#343e49',outline='#82959f',width=4);d.rectangle((40,42,88,72),fill='#79d8dd');d.line((72,28,87,6),fill='#aab8bc',width=5);d.ellipse((52,82,76,106),fill='#dbb567');im.save(OUT/'Textures/ScCsgoTactical/beacon.png')

def templates():
    def node(parent,tag,name,inherit=None):
        n=E.SubElement(parent,tag,Name=name,Guid=str(uuid.uuid5(uuid.NAMESPACE_URL,'zh667.ScCsgoTactical/'+parent.get('Guid','root')+'/'+tag+'/'+name)))
        if inherit:n.set('InheritanceParent',inherit)
        return n
    def params(parent,values):
        for name,(v,t) in values.items():n=node(parent,'Parameter',name);n.set('Value',str(v));n.set('Type',t)
    r=E.Element('Mod');p=E.SubElement(r,'ProjectTemplate',Name='Project',Guid='85023bf8-1c90-4dd1-9442-e6c13691d078')
    params(node(p,'MemberSubsystemTemplate','ScTactical','fefb9590-4972-4893-b02a-76063611b745'),{'Class':('Game.SubsystemScTactical','string')})
    for name in ['TacticalEnemies','TacticalBombs']:
        params(node(p,'MemberSubsystemTemplate',name,'fefb9590-4972-4893-b02a-76063611b745'),{'Class':('Game.Subsystem'+name,'string')})
    for kind,label,asset in [('Hostage','救援同伴','hostage'),('CT','CT · SAS','ct'),('T','T · Phoenix','t'),('Enemy','T · 敌对小队','t')]:
        c=node(r,'EntityTemplate','ScTactical'+kind,'bc5be211-c1f8-4e50-9ffb-4fde625d2692')
        for name,values in {
            'Body':{'BoxSize':('0.65,1.8,0.65','Vector3'),'Mass':(75,'float')},
            'Creature':{'DisplayName':(label,'string'),'Description':('自然刷新的敌对小队成员。' if kind=='Enemy' else '旧版救援同伴，保留装备取回与解散。' if kind=='Hostage' else '主动攻击附近敌对生物，并协助主人攻击目标的战术同伴。','string'),'Category':('LandOther','Game.CreatureCategory'),'KillVerbs':('shot','string'),'ConstantSpawn':('False' if kind=='Enemy' else 'True','bool')},
            'Locomotion':{'WalkSpeed':(4.5,'float'),'FlySpeed':(0,'float'),'SwimSpeed':(1.5,'float'),'TurnSpeed':(7,'float'),'JumpSpeed':(5,'float')},
            'Health':{'AttackResilience':(60,'float'),'FireResilience':(15,'float'),'CorpseDuration':(3,'float')},
            'CreatureSounds':{'IdleSound':('','string'),'PainSound':('','string'),'MoanSound':('','string'),'AttackSound':('','string'),'RareIdleSound':('','string')},
        }.items():params(node(c,'MemberComponentTemplate',name),values)
        for name,parent in [('Pilot','53510bee-16d1-4245-9b55-05cf26064cfc'),('Pathfinding','125dc475-9340-4137-b694-e003f740ea2d'),('BehaviorSelector','45cd8302-bb36-4c84-abf0-96fba5c2b9df')]:node(c,'MemberComponentTemplate',name,parent)
        params(node(c,'MemberComponentTemplate','TacticalModel','681a5886-5bff-418a-bf5f-ac84f290a311'),{'Class':('Game.ComponentTacticalModel','string'),'ModelName':('Models/ScCsgoTactical/'+asset,'string'),'AnimationConfigPath':('Animations/ScTactical','string'),'ModelScale':(1,'float'),'BoundingSphereRadius':(2,'float'),'TextureOverride':('','string')})
        params(node(c,'MemberComponentTemplate','TacticalInventory','81a44c6a-c30a-4f53-8d64-0c30aabab8f9'),{'Class':('Game.ComponentTacticalInventory','string'),'SlotsCount':(5,'int')})
        behavior='TacticalEnemy' if kind=='Enemy' else 'TacticalCompanion'
        params(node(c,'MemberComponentTemplate',behavior,'b05700ed-7e4e-4679-98f5-b597f421496b'),{'Class':('Game.Component'+behavior,'string')})
        if kind=='Enemy':
            params(node(c,'MemberComponentTemplate','Spawn'),{'AutoDespawn':('True','bool')})
    E.indent(r,space='  ');(OUT/'ScTactical.xdb').write_text(E.tostring(r,encoding='unicode')+'\n',encoding='utf8')

def defuser():
    im=Image.new('RGBA',(128,128));d=ImageDraw.Draw(im)
    d.polygon([(39,9),(52,9),(62,41),(67,41),(77,9),(89,9),(82,48),(71,64),(100,110),(87,120),(64,78),(41,120),(28,110),(57,64),(46,48)],fill='#91a4ab',outline='#2a343b',width=3)
    d.line([(58,72),(35,113)],fill='#247785',width=13);d.line([(70,72),(94,113)],fill='#247785',width=13)
    d.ellipse((55,51,73,69),fill='#45535e',outline='#d0d8db',width=2)
    im.save(OUT/'Textures/ScCsgoTactical/defuser.png')

if __name__=='__main__':
    shield();templates();defuser()
    sound=OUT/'Audio/ScCsgoTactical/ShieldHit';sound.mkdir(parents=True,exist_ok=True)
    for i in range(1,8):
        p=ROOT/f'.tmp/cs2-companions-audit-20260920/sounds/sounds/physics/shield/bullet_hit_shield_{i:02}.wav'
        shutil.copyfile(p,sound/p.name)
