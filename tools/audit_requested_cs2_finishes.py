"""Read-only audit of the requested finishes against the installed CS2 VPK, not an old export list."""
import hashlib
import importlib.util
import json
import re
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EXPORT = ROOT.parent / 'CSMCReverse/local_cs2_analysis/all_weapons'
spec = importlib.util.spec_from_file_location('firearm_index', EXPORT / 'build_firearm_catalog.py')
vpk = importlib.util.module_from_spec(spec)
spec.loader.exec_module(vpk)

REQUESTS = [
 ('m4a1','反冲精英'),('m4a1','咆哮'),('usp_silencer','印花集'),('deagle','印花集'),
 ('p250','银装素裹'),('glock','伽玛多普勒'),('famas','机械工业'),('mp9','赤红新星'),
 ('p90','二西莫夫'),('ssg08','炎龙之焰'),('fiveseven','暴怒野兽'),('hkp2000','草皮'),
 ('tec9','燃料喷射器'),('cz75a','先驱'),('mac10','错觉'),('mp7','吸烟有害健康'),
 ('ump45','渐变之色'),('bizon','阿努比斯之审判'),('galilar','~甜甜的~'),('mp5sd','幻化绿洲'),
 ('scar20','心脏打击'),('g3sg1','绿苹果'),('aug','秋叶原之选'),('sg556','玩命职场'),
 ('nova','玩具士兵'),('xm1014','宁静'),('sawedoff','么么'),('mag7','反梗精英'),
 ('m249','星云十字军'),('negev','雷神之锤'),('revolver','疯狂老八'),('elite','浮木'),('taser','奥林匹斯')]

def kv(text):
    tokens = re.findall(r'"(?:\\.|[^"\\])*"|//[^\n]*|[{}]|[^\s{}"]+', text)
    tokens = iter(t for t in tokens if not t.startswith('//'))
    def unquote(t):
        return t[1:-1] if t.startswith('"') else t
    def obj():
        pairs=[]
        for key in tokens:
            if key == '}': return pairs
            value=next(tokens)
            pairs.append((unquote(key), obj() if value=='{' else unquote(value)))
        return pairs
    return obj()

def main():
    sys.stdout.reconfigure(encoding='utf-8')
    index=vpk.read_vpk_index()
    lower={p.lower():p for p in index}
    def read(path):
        archive,offset,length,preload=index[path]
        if archive==0x7fff:
            offset+=28+struct.unpack_from('<I',vpk.VPK.read_bytes(),8)[0]
            path=vpk.VPK
        else: path=vpk.VPK.with_name(f'pak01_{archive:03d}.vpk')
        with path.open('rb') as f:
            f.seek(offset); data=f.read(length)
        if len(data)!=length: raise ValueError('Truncated VPK entry')
        return preload+data
    def decode(data):
        return data.decode('utf-16' if data[:2] in (b'\xff\xfe',b'\xfe\xff') else 'utf-8-sig')
    items_data=read('scripts/items/items_game.txt')
    item_text=decode(items_data)
    items=kv(item_text)[0][1]
    language=decode(read('resource/csgo_schinese.txt'))
    def flat(pairs):
        for k,v in pairs:
            if isinstance(v,list): yield from flat(v)
            else: yield k.lower(),v
    labels=dict(flat(kv(language)))
    paints=[]
    for section,contents in items:
        if section=='paint_kits':
            for paintid,fields in contents:
                p=dict(fields);p['id']=paintid;paints.append(p)
    default=next(p for p in paints if p['id']=='0')
    local_files={p.relative_to(EXPORT/'10_paints/decompiled').as_posix().lower():p
                 for p in (EXPORT/'10_paints/decompiled').rglob('*') if p.is_file()}
    def refs(path):
        # Full resource paths only; layer_name metadata such as default_composite_inputs.vmat is not a dependency.
        return [r for r in re.findall(r'"([^"\n]+\.(?:vmat|vcompmat|vtex))"',path.read_text('utf-8')) if '/' in r]
    def dependency_audit(paths):
        todo=list(paths);seen=set();found={}
        while todo:
            path=todo.pop()
            if path in seen: continue
            seen.add(path)
            for ref in refs(path):
                compiled=lower.get((ref+'_c').lower())
                found[ref]={'path':ref,'inVpk':compiled is not None}
                if ref.lower() in local_files:todo.append(local_files[ref.lower()])
        return list(found.values())
    guns=json.loads((EXPORT/'01_weapon_data/firearms-catalog.json').read_text('utf-8'))
    models={g['name']:g['world_model'] for g in guns}
    associations=set((p.lower(),g) for p,g in re.findall(r'"\[([^\]]+)\]weapon_([a-z0-9_]+)"',item_text))
    rows=[]
    for gun,name in REQUESTS:
        matches=[]
        for p in paints:
            key=p.get('name',''); tag=p.get('description_tag','').lstrip('#').lower()
            if labels.get(tag)!=name: continue
            if gun=='glock' and p['id']!='1119': continue # user: Emerald only, no phases
            if (key.lower(),gun) not in associations: continue
            icons=[s for s in index if '/econ/default_generated/' in s and any(s.lower().endswith(f'/weapon_{gun}_{key}_{wear}_png.vtex_c'.lower()) for wear in ('light','medium','heavy'))]
            if not icons: continue
            recipes=[s for s in index if s.lower().endswith('/'+key.lower()+'.vcompmat_c')]
            materials=[s for s in index if s.lower().endswith('/'+key.lower()+'.vmat_c')]
            local=[local_files[s[:-2].lower()] for s in recipes if s[:-2].lower() in local_files]
            dependencies=dependency_audit(local)
            matches.append({'id':p['id'],'key':key,'fields':p,'recipes':recipes,'materials':materials,
                'wearMin':float(p.get('wear_remap_min',default['wear_remap_min'])),
                'wearMax':float(p.get('wear_remap_max',default['wear_remap_max'])),
                'icons':icons,'dependencies':dependencies,'exportedRecipes':[str(s.relative_to(EXPORT)) for s in local]})
        model=models.get('weapon_'+gun)
        glb=EXPORT/'02_models/glb_with_animations'/str(Path(model).with_suffix('.glb')) if model else None
        mesh_names=[]
        if glb and glb.exists():
            with glb.open('rb') as f:
                header=f.read(20);length,kind=struct.unpack_from('<II',header,12)
                assert kind==0x4e4f534a
                doc=json.loads(f.read(length));mesh_names=[m.get('name') for m in doc.get('meshes',[])]
        rows.append({'weapon':gun,'requested':name,'matches':matches,'model':model,'modelInVpk':model+'_c' in index if model else False,
                     'localGlb':str(glb) if glb and glb.exists() else None,'meshes':mesh_names})
    # The existing creative counter encoding adds variant + paint ID; it must remain collision-free.
    gun_source=(ROOT/'src/ScCsgoKnives/Rendering/GunSpec.cs').read_text('utf-8')
    order=re.findall(r'"([^"]+)"',re.search(r'FrozenOrder = \[(.*?)\]',gun_source).group(1))
    aliases={'m4a1':'m4a4','glock':'glock18'}
    old=json.loads((ROOT/'tools/gun_skins_catalog.json').read_text('utf-8'))['skins']
    candidates=[(s['paintId'],s['gun']) for s in old]+[(int(m['id']),aliases.get(r['weapon'],r['weapon'])) for r in rows for m in r['matches']]
    used={};collisions=[]
    for paint,gun in candidates:
        data=1000+paint+order.index(gun)
        if data in used:collisions.append([data,used[data],[paint,gun]])
        used[data]=[paint,gun]
    assert len(rows)==33 and all(r['matches'] and r['modelInVpk'] and r['localGlb'] for r in rows)
    assert not collisions
    assert all(m['recipes'] and len(m['icons'])==3 and not any(not d['inVpk'] for d in m['dependencies']) for r in rows for m in r['matches'])
    print(json.dumps({'vpk':str(vpk.VPK),'vpkIndexSha256':hashlib.sha256(vpk.VPK.read_bytes()).hexdigest(),
        'itemsSha256':hashlib.sha256(items_data).hexdigest(),'requested':len(rows),'rows':rows,'counterCollisions':collisions},ensure_ascii=False,indent=2))

if __name__=='__main__': main()
