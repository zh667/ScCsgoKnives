"""Stage/publish the full-only 1.3.0 crowd GPU optimization. Preserve existing resources."""
import argparse
import hashlib
import json
import shutil
import zipfile
import zlib
from pathlib import Path
from pack_single_scmods import raw_member, write_archive

ROOT = Path(__file__).resolve().parents[1]
STAGE = ROOT / '.tmp/crowd-opt-20260926'
NAME = '[API1.9]CS武器1.3.0-全量包.scmod'
SOURCE_SHA = '3760e94fde9e3b9dff4e96a9a47240b39c677d13ffc791d9d213b72cce8721a9'
LITE_SHA = 'bfa85bcf02cd40246b883099367ee7a3a8bd7bac8c1e8b1602f5f8a8d596c9d7'
def sha(data): return hashlib.sha256(data).hexdigest()
def read(path): return json.loads(path.read_text('utf-8-sig'))

def stage():
    source = ROOT / 'output' / NAME
    assert sha(source.read_bytes()) == SOURCE_SHA
    payload = (ROOT / 'src/ScCsgoTactical/bin/Release/net10.0/ScCsgoTactical.dll').read_bytes()
    entries = {}
    target = STAGE / 'candidate' / NAME
    target.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(source) as old:
        for info in old.infolist():
            entries[info.filename] = (info.compress_type, info.CRC, info.file_size, raw_member(old, info))
        compressor = zlib.compressobj(9, zlib.DEFLATED, -15)
        entries['ScCsgoTactical.dll'] = (8, zlib.crc32(payload), len(payload), compressor.compress(payload)+compressor.flush())
        caches = {}
        for path in sorted((ROOT / 'src/ScCsgoTactical/Assets/Models/ScCsgoTactical/Weapons').glob('*.scmesh')):
            name='Assets/Models/ScCsgoTactical/Weapons/'+path.name
            data=path.read_bytes(); compressor=zlib.compressobj(9,zlib.DEFLATED,-15)
            entries[name]=(8,zlib.crc32(data),len(data),compressor.compress(data)+compressor.flush())
            caches[name]=sha(data)
        assert len(caches)==63
        write_archive(target, entries)
        with zipfile.ZipFile(target) as new:
            assert new.testzip() is None and set(new.namelist()) == set(old.namelist()) | set(caches)
            for name in old.namelist():
                if name == 'ScCsgoTactical.dll': assert new.read(name) == payload
                else:
                    assert new.read(name) == old.read(name), name
                    assert raw_member(new, new.getinfo(name)) == raw_member(old, old.getinfo(name)), name
            assert json.loads(new.read('modinfo.json'))['Version'] == '1.3.0'
            report = dict(path=NAME, revision='crowd-gpu-20260926', sourceSha256=SOURCE_SHA,
                sha256=sha(target.read_bytes()), bytes=target.stat().st_size, changedEntries=['ScCsgoTactical.dll'],
                unchangedCompressedEntries=len(old.namelist())-1, meshCaches=caches,
                dllHashes={n: sha(new.read(n)) for n in new.namelist() if n.endswith(('.dll','.bin'))})
    (STAGE / 'package.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', 'utf-8')
    print(json.dumps(report, ensure_ascii=True))

def publish():
    report=read(STAGE/'package.json');candidate=STAGE/'candidate'/NAME
    assert sha(candidate.read_bytes()) == report['sha256']
    validation=[]
    for name in ['gate','native','ai','compatibility']+[f'official-{mode}' for mode in ['both','reversed','none','nmm-only','neo-only','disabled','outdated']]:
        path=STAGE/(name+'.json');r=read(path)
        assert r['failed']==0 and path.stat().st_mtime_ns>=candidate.stat().st_mtime_ns, name
        validation.append(dict(name=name,checks=len(r['checks']),sha256=sha(path.read_bytes())))
    assert read(STAGE/'ai.json')['dlcSha256']==report['sha256']
    assert read(STAGE/'compatibility.json')['modules'][2]['sha256']==report['dllHashes']['ScCsgoKnives.dll']
    for tool in ['NpcWeaponCheck','TacticalPerformanceCheck','AppearanceCheck','TacticalRenderCheck']:
        assert sha((ROOT/f'tools/{tool}/bin/Release/net10.0/ScCsgoTactical.dll').read_bytes())==report['dllHashes']['ScCsgoTactical.dll']
        assert sha((ROOT/f'tools/{tool}/bin/Release/net10.0/ScCsgoKnives.dll').read_bytes())==report['dllHashes']['ScCsgoKnives.dll']
    probe=read(STAGE/'probe/checks.json');appearance=read(STAGE/'appearance/checks.json');actors=read(STAGE/'actors/gpu.json')
    assert probe['failed']==appearance['failed']==0 and len(actors)==12 and all(c['pixels']>1000 for c in actors)
    assert all(c['passed'] for c in appearance['checks'])
    assert sha((ROOT/'tools/AppearanceCheck/bin/Release/net10.0/sc-nekomekomodel.dll').read_bytes())=='1ba873e781f3c082325ee516691811dd709a0108b82d29ef48ac3df79aa2916c'
    for name in ['probe/checks.json','appearance/checks.json','actors/gpu.json']:
        assert (STAGE/name).stat().st_mtime_ns>=candidate.stat().st_mtime_ns,name
    weapons=read(STAGE/'weapons/checks.json')
    assert weapons['failed']==0 and (STAGE/'weapons/checks.json').stat().st_mtime_ns>=candidate.stat().st_mtime_ns
    with zipfile.ZipFile(candidate) as archive:
        for name, expected in report['meshCaches'].items():
            assert sha(archive.read(name))==expected
            assert sha((ROOT/'src/ScCsgoTactical'/name).read_bytes())==expected
    source=ROOT/'output'/NAME
    assert sha(source.read_bytes()) == SOURCE_SHA
    assert sha((ROOT/'output/[API1.9]CS武器1.3.0-轻量包.scmod').read_bytes())==LITE_SHA
    previous=STAGE/'previous-full.scmod'
    if not previous.exists():shutil.copy2(source,previous)
    assert sha(previous.read_bytes())==SOURCE_SHA
    pending=source.with_suffix('.pending');shutil.copy2(candidate,pending);pending.replace(source)
    assert sha(source.read_bytes())==report['sha256']
    report.update(validation=validation,probe=probe,weaponChecks=len(weapons["checks"]),weaponMeasurements=weapons["measurements"],appearanceChecks=len(appearance['checks']),nativeActorFrames=len(actors),
        scope='Windows isolated native fixtures; no Android, actual player world, or all-installed-mod combination acceptance')
    (ROOT/'docs/release-crowd-optimization-1.3.0-2026-09-26-evidence.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n','utf-8')
    print(json.dumps(dict(published=True,sha256=report['sha256'],bytes=report['bytes'])))

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--publish',action='store_true')
    if parser.parse_args().publish:publish()
    else:stage()
