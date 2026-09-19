"""Inspect local CS2 resource inventories and exported GLBs without changing game assets."""
import hashlib
import json
import re
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
AUDIT = ROOT / '.tmp/cs2-companions-audit-20260920'
VPK = Path('E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/game/csgo/pak01_dir.vpk')


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def inspect(path):
    with path.open('rb') as stream:
        header = stream.read(20)
        assert header[:4] == b'glTF'
        doc = json.loads(stream.read(struct.unpack_from('<I', header, 12)[0]))
    clips = [a['name'] for a in doc.get('animations', [])]
    images = [i['uri'] for i in doc.get('images', []) if 'uri' in i]
    missing = [i for i in images if not i.startswith('data:') and not (path.parent / i).is_file()]
    assert not missing, missing
    return dict(path=str(path.relative_to(ROOT)), bytes=path.stat().st_size, sha256=sha(path),
                meshes=len(doc.get('meshes', [])), skins=len(doc.get('skins', [])),
                materials=len(doc.get('materials', [])), images=len(doc.get('images', [])),
                missingImages=missing, animationCount=len(clips),
                animationExamples=clips if len(clips)<100 else [n for n in clips if any(s in n for s in
                    ['/idle_rifle','/run_n_rifle','/walk_n_rifle','reload_ak47','shoot_ak47','hostage','shield'])][:40],
                meshExamples=[m.get('name','') for m in doc.get('meshes', [])][:10],
                jointCounts=sorted(set(len(s['joints']) for s in doc.get('skins', []))))


def main():
    listing=ROOT/'.tmp/cs2-resource-audit-20260920-vpk.txt'
    paths=[line.split(' CRC:')[0] for line in listing.read_text('utf-8-sig').splitlines() if ' CRC:' in line]
    selected=[p for p in paths if (
        p.startswith(('models/hostage/','sounds/vo/hostage/','materials/models/weapons/v_models/shield/',
                      'materials/models/weapons/w_models/w_eq_shield/','sounds/physics/shield/'))
        or p in ['agents/models/ctm_sas/ctm_sas.vmdl_c','agents/models/tm_phoenix/tm_phoenix.vmdl_c',
                 'models/weapons/w_eq_armor.vmdl_c','models/weapons/w_eq_helmet.vmdl_c',
                 'models/weapons/w_eq_armor_helmet.vmdl_c','models/weapons/w_eq_assault_suit.vmdl_c'])]
    inventories=[listing,*sorted(AUDIT.glob('*.txt'))]
    shield=[]
    for p in inventories:
        shield += [dict(inventory=p.name,entry=line) for line in p.read_text('utf-8-sig').splitlines()
                   if re.search(r'(shield|riot|ballistic)',line,re.I) and re.search(r'\.(vmdl_c|vnmclip_c) ',line)]
    models=[inspect(p) for p in sorted((AUDIT/'export').rglob('*.glb')) if '_physics' not in p.stem]
    assert len(models)==5, 'Expected both defaults, hostage, vest and helmet'
    result=dict(date='2026-09-20',scope='Resource audit only; no mod implementation or package changes',
                vpk=str(VPK),vpkDirectorySha256=sha(VPK),listedMainEntries=len(paths),
                inventories=[dict(path=str(p.relative_to(ROOT)),sha256=sha(p)) for p in inventories],
                shieldModelOrAnimationNameMatches=shield,relevantMainEntries=selected,models=models,
                notes=['No matching shield geometry/animation path in scanned installed VPK inventories; not a claim about all CS versions.',
                       'Agent all-animation exports include many mesh variants/LODs and are audit sources, not shipping assets.',
                       'Material references and GLB structure checked; no Survivalcraft GPU rendering or skeleton retargeting acceptance.'])
    target=ROOT/'docs/companion-resource-audit-20260920.json'
    target.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
    for m in models:print(json.dumps(m,ensure_ascii=False))


if __name__=='__main__':main()
