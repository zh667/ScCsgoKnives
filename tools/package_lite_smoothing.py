"""Carry the verified crowd fixes into the existing 512 edition without Full meshes."""
import hashlib
import json
import shutil
import zipfile
import zlib
import xml.etree.ElementTree as ET
from pathlib import Path
from pack_single_scmods import raw_member, write_archive

ROOT = Path(__file__).resolve().parents[1]
STAGE = ROOT / '.tmp/lite-smooth-20260926'
NAME = '[API1.9]CS武器1.3.0-轻量包.scmod'
OLD = 'bfa85bcf02cd40246b883099367ee7a3a8bd7bac8c1e8b1602f5f8a8d596c9d7'
FULL = 'a2922a8115fe010b564691115351667dba1c3327329fd1a36870184ffef10f02'

def sha(data): return hashlib.sha256(data).hexdigest()
def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))
def write(path, data): path.write_text(json.dumps(data, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')

def stage():
    source = ROOT/'output'/NAME
    full = ROOT/'output/[API1.9]CS武器1.3.0-全量包.scmod'
    assert sha(source.read_bytes()) == OLD and sha(full.read_bytes()) == FULL
    replacements = {}
    with zipfile.ZipFile(full) as z:
        replacements['ScCsgoTactical.dll'] = z.read('ScCsgoTactical.dll')
        for role in ('ct', 't'):
            cache = (ROOT/f'.tmp/lite-actor/{role}.scanim').read_bytes()
            assert cache == z.read(f'Assets/Animations/ScCsgoTactical/{role}.scanim')
            assert sha(cache) == read(ROOT/f'.tmp/lite-actor/before-{role}.json')['cacheSha256']
            replacements[f'Assets/Animations/ScCsgoTactical/{role}.scanim'] = cache
            replacements[f'Assets/Models/ScCsgoTactical/{role}.glb'] = (ROOT/f'.tmp/lite-actor/{role}-stripped.glb').read_bytes()
    meshes = sorted((STAGE/'baked/Assets').rglob('*.scmesh'))
    assert len(meshes) == 63 and read(STAGE/'bake-weapons/checks.json')['failed'] == 0
    for path in meshes: replacements['Assets/'+path.relative_to(STAGE/'baked/Assets').as_posix()] = path.read_bytes()
    target = STAGE/'candidate'/NAME
    target.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(source) as old:
        marker = ET.fromstring(old.read('Assets/ScCsgoResources.xml'))
        hashes = {n: sha(old.read(n)) for n in old.namelist()}
        hashes.update({n: sha(d) for n,d in replacements.items()})
        marker.clear(); marker.attrib.update(Version='1.10.4', Format='1', Edition='Optimized512')
        for name,digest in sorted(hashes.items()):
            if name.startswith(('Assets/Textures/','Assets/Models/','Assets/Audio/','Assets/Animations/')):
                ET.SubElement(marker,'File',Path=name,Sha256=digest)
        replacements['Assets/ScCsgoResources.xml'] = ET.tostring(marker,encoding='utf-8',xml_declaration=True)
        entries = {i.filename:(i.compress_type,i.CRC,i.file_size,raw_member(old,i)) for i in old.infolist()}
        for name,data in replacements.items():
            compressor = zlib.compressobj(9,zlib.DEFLATED,-15)
            entries[name] = (8,zlib.crc32(data),len(data),compressor.compress(data)+compressor.flush())
        stronger=STAGE/'recompressed.zip'
        saved=0
        if stronger.exists():
            with zipfile.ZipFile(stronger) as compressed:
                assert compressed.testzip() is None and set(compressed.namelist())==set(replacements)
                for name,data in replacements.items():
                    assert compressed.read(name)==data,name
                    info=compressed.getinfo(name); assert info.compress_type in (0,8)
                    if info.compress_size<len(entries[name][3]):
                        saved+=len(entries[name][3])-info.compress_size
                        entries[name]=(info.compress_type,info.CRC,info.file_size,raw_member(compressed,info))
        write_archive(target,entries)
        with zipfile.ZipFile(target) as new:
            assert new.testzip() is None and set(new.namelist()) == set(old.namelist()) | set(replacements)
            for name in old.namelist():
                assert new.read(name) == replacements.get(name,old.read(name)), name
                if name not in replacements: assert raw_member(new,new.getinfo(name)) == raw_member(old,old.getinfo(name))
            assert json.loads(new.read('modinfo.json'))['Version'] == '1.3.0'
            assert json.loads(new.read('Integrations/ScCsgoBundle.json'))['version'] == '1.3.0'
            report = dict(path=NAME,revision='crowd-smooth-20260926',edition='Lite',sourceSha256=OLD,
                fullUnchangedSha256=FULL,sha256=sha(target.read_bytes()),bytes=target.stat().st_size,
                unchangedCompressedEntries=len(set(old.namelist())-set(replacements)),losslessAdditionalBytesSaved=saved,
                replacementHashes={n:sha(d) for n,d in replacements.items()},
                dllHashes={n:sha(new.read(n)) for n in new.namelist() if n.endswith(('.dll','.bin'))})
    write(STAGE/'package.json',report)
    print(json.dumps({k:v for k,v in report.items() if k not in ('replacementHashes','dllHashes')}))

def publish():
    report=read(STAGE/'package.json'); candidate=STAGE/'candidate'/NAME
    assert sha(candidate.read_bytes())==report['sha256']
    validations=[]
    for label in ['gate','native','ai','compatibility']+[f'official-{m}' for m in ['both','reversed','none','nmm-only','neo-only','disabled','outdated']]+['sampling/checks','weapons/checks','appearance/checks']:
        path=STAGE/(label+'.json'); result=read(path)
        assert path.stat().st_mtime_ns>=candidate.stat().st_mtime_ns and result['failed']==0,label
        assert all(not isinstance(c,dict) or c.get('ok',c.get('Ok',c.get('passed',False))) for c in result['checks']),label
        validations.append(dict(name=label,checks=len(result['checks']),sha256=sha(path.read_bytes())))
    assert read(STAGE/'ai.json')['dlcSha256']==report['sha256']
    assert read(STAGE/'compatibility.json')['modules'][2]['sha256']==report['dllHashes']['ScCsgoKnives.dll']
    for tool in ['ActorSamplingCheck','NpcWeaponCheck','AppearanceCheck','TacticalRenderCheck','ActorLoadCheck']:
        for name in ['ScCsgoKnives.dll','ScCsgoTactical.dll','ScCsgoResources.dll']:
            assert sha((STAGE/'runtime'/tool/name).read_bytes())==report['dllHashes'][name],(tool,name)
    assert sha((STAGE/'runtime/AppearanceCheck/ScCsgoAppearance.dll').read_bytes())==report['dllHashes']['Integrations/ScCsgoAppearance.bin']
    assert sha((STAGE/'runtime/AppearanceCheck/sc-nekomekomodel.dll').read_bytes())=='1ba873e781f3c082325ee516691811dd709a0108b82d29ef48ac3df79aa2916c'
    actors=read(STAGE/'actors/gpu.json'); assert len(actors)==12 and all(r['pixels']>1000 for r in actors)
    assert (STAGE/'actors/gpu.json').stat().st_mtime_ns>=candidate.stat().st_mtime_ns
    for role in ('ct','t'):
        before=read(ROOT/f'.tmp/lite-actor/before-{role}.json'); after=read(STAGE/f'after-{role}.json')
        assert before['failed']==after['failed']==0 and before['cacheSha256']==after['cacheSha256']
        assert after['gltfSha256']==report['replacementHashes'][f'Assets/Models/ScCsgoTactical/{role}.glb']
    source=ROOT/'output'/NAME
    assert sha(source.read_bytes())==OLD and sha((ROOT/'output/[API1.9]CS武器1.3.0-全量包.scmod').read_bytes())==FULL
    previous=STAGE/'previous-lite.scmod'
    if not previous.exists(): shutil.copy2(source,previous)
    assert sha(previous.read_bytes())==OLD
    pending=source.with_suffix('.pending'); shutil.copy2(candidate,pending); pending.replace(source)
    report.update(validation=validations,nativeActorFrames=12,
        scope='Actual Lite assets/resources in isolated Windows native fixtures; no Android/player-world acceptance')
    write(ROOT/'docs/release-lite-smoothing-1.3.0-2026-09-26-evidence.json',report)
    print(json.dumps(dict(published=True,bytes=report['bytes'],sha256=report['sha256'])))

if __name__=='__main__':
    import argparse
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--publish',action='store_true')
    publish() if p.parse_args().publish else stage()
