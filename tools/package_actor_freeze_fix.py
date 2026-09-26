"""Stage the full-only 1.3.0 actor loading fix; preserve unrelated release bytes."""
import argparse
import hashlib
import json
import shutil
import zipfile
import zlib
from pathlib import Path
from pack_single_scmods import raw_member, write_archive

ROOT = Path(__file__).resolve().parents[1]
STAGE = ROOT / '.tmp/actor-freeze-20260926'
NAME = '[API1.9]CS武器1.3.0-全量包.scmod'
SOURCE_SHA = '82627ceded4e6f5fb7edfc203373c093f8d954790d7e5b9cb3f825bc41ca8f08'
LITE_SHA = 'bfa85bcf02cd40246b883099367ee7a3a8bd7bac8c1e8b1602f5f8a8d596c9d7'

def sha(data):
    return hashlib.sha256(data).hexdigest()

def read(path):
    return json.loads(path.read_text('utf-8-sig'))

def stage():
    source = ROOT / 'output' / NAME
    assert sha(source.read_bytes()) == SOURCE_SHA
    replacements = {'ScCsgoTactical.dll': (ROOT / 'src/ScCsgoTactical/bin/Release/net10.0/ScCsgoTactical.dll').read_bytes()}
    for role in ('ct','t'):
        check = read(STAGE / f'before-{role}.json')
        geometry = read(STAGE / f'geometry-{role}.json')
        for kind, folder, key in (('glb','Models','sha256'),('scanim','Animations','cacheSha256')):
            payload = (ROOT / f'src/ScCsgoTactical/Assets/{folder}/ScCsgoTactical/{role}.{kind}').read_bytes()
            assert sha(payload) == (geometry if kind == 'glb' else check)[key]
            replacements[f'Assets/{folder}/ScCsgoTactical/{role}.{kind}'] = payload
    entries = {}
    target = STAGE / 'candidate' / NAME
    with zipfile.ZipFile(source) as old:
        assert json.loads(old.read('modinfo.json'))['Version'] == '1.3.0'
        for role in ('ct','t'):
            assert sha(old.read(f'Assets/Models/ScCsgoTactical/{role}.glb')) == read(STAGE / f'geometry-{role}.json')['sourceSha256']
        for info in old.infolist():
            entries[info.filename] = (info.compress_type,info.CRC,info.file_size,raw_member(old,info))
        for name,data in replacements.items():
            compressor = zlib.compressobj(9,zlib.DEFLATED,-15)
            entries[name] = (8,zlib.crc32(data),len(data),compressor.compress(data)+compressor.flush())
        write_archive(target,entries)
        with zipfile.ZipFile(target) as new:
            assert new.testzip() is None
            assert set(new.namelist()) == set(old.namelist()) | set(replacements)
            for name in old.namelist():
                assert new.read(name) == replacements.get(name,old.read(name)),name
                if name not in replacements:
                    assert raw_member(new,new.getinfo(name)) == raw_member(old,old.getinfo(name)),name
            report=dict(path=NAME,version='1.3.0',edition='Full',sourceSha256=SOURCE_SHA,
                sha256=sha(target.read_bytes()),bytes=target.stat().st_size,entries=len(entries),
                changedEntries=sorted(set(old.namelist())&set(replacements)),
                addedEntries=sorted(set(replacements)-set(old.namelist())),
                unchangedCompressedEntries=len(set(old.namelist())-set(replacements)),
                dllHashes={n:sha(new.read(n)) for n in new.namelist() if n.endswith('.dll')},
                replacementHashes={n:sha(data) for n,data in replacements.items()})
    (STAGE/'candidate-package.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n','utf-8')
    print(json.dumps(report,ensure_ascii=True))

def publish():
    report=read(STAGE/'candidate-package.json')
    validation=validate()
    assert validation['failed']==0 and validation['packageSha256']==report['sha256']
    source=ROOT/'output'/NAME
    candidate=STAGE/'candidate'/NAME
    assert sha(source.read_bytes())==SOURCE_SHA and sha(candidate.read_bytes())==report['sha256']
    assert sha((ROOT/'output/[API1.9]CS武器1.3.0-轻量包.scmod').read_bytes())==LITE_SHA
    saved=STAGE/'previous-full.scmod'
    if not saved.exists():shutil.copy2(source,saved)
    assert sha(saved.read_bytes())==SOURCE_SHA
    pending=source.with_suffix('.pending')
    shutil.copy2(candidate,pending);pending.replace(source)
    assert sha(source.read_bytes())==report['sha256']
    folder=ROOT/'output/release-actor-freeze-1.3.0';folder.mkdir(exist_ok=True)
    (folder/'Full.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n','utf-8')
    print(json.dumps(dict(published=True,edition='Full',version='1.3.0',sha256=report['sha256'])))

def validate():
    package=read(STAGE/'candidate-package.json')
    candidate=STAGE/'candidate'/NAME
    assert sha(candidate.read_bytes())==package['sha256']
    checks=[]
    for provider in ('official','legacy'):
        for mode in ('both','reversed','none','nmm-only','neo-only','disabled','outdated'):
            checks.append(f'{provider}-{mode}')
    checks += ['gate','native','compatibility']
    reports=[]
    for name in checks:
        path=STAGE/(name+'.json');item=read(path)
        assert path.stat().st_mtime_ns >= candidate.stat().st_mtime_ns, name
        assert item['failed']==0 and all(c.get('ok') is True for c in item['checks']),name
        reports.append(dict(name=name,count=len(item['checks']),sha256=sha(path.read_bytes()),failed=0))
    compatibility=read(STAGE/'compatibility.json')
    assert compatibility['modules'][2]['sha256']==package['dllHashes']['ScCsgoKnives.dll']
    performance={}
    for role in ('ct','t'):
        before=read(STAGE/f'before-{role}.json');after=read(STAGE/f'after-{role}.json')
        assert before['failed']==after['failed']==0 and after['checks']>=13
        assert before['clips']==after['clips']==170
        assert before['channels']==after['channels'] and before['samples']==after['samples']
        assert before['cacheSha256']==after['cacheSha256']==package['replacementHashes'][f'Assets/Animations/ScCsgoTactical/{role}.scanim']
        assert after['gltfSha256']==package['replacementHashes'][f'Assets/Models/ScCsgoTactical/{role}.glb']
        assert after['gltfMs']+after['cacheReadMs'] < before['gltfMs']/5
        performance[role]=dict(before=before,after=after,geometry=read(STAGE/f'geometry-{role}.json'))
    appearance_path=STAGE/'appearance-official/checks.json'
    appearance=read(appearance_path)
    assert appearance['failed']==0 and all(c.get('passed') is True for c in appearance['checks'])
    actor_path=STAGE/'actors/gpu.json';actors=read(actor_path)
    assert len(actors)==12 and all(c['pixels']>1000 for c in actors)
    for tool in ('AppearanceCheck','TacticalRenderCheck'):
        assert sha((ROOT/f'tools/{tool}/bin/Release/net10.0/ScCsgoTactical.dll').read_bytes())==package['dllHashes']['ScCsgoTactical.dll']
    assert sha((ROOT/'tools/AppearanceCheck/bin/Release/net10.0/sc-nekomekomodel.dll').read_bytes())=='1ba873e781f3c082325ee516691811dd709a0108b82d29ef48ac3df79aa2916c'
    with zipfile.ZipFile(candidate) as z:
        for name,expected in package['replacementHashes'].items():assert sha(z.read(name))==expected,name
        assert sha(z.read('Integrations/ScCsgoAppearance.bin'))==sha((ROOT/'tools/AppearanceCheck/bin/Release/net10.0/ScCsgoAppearance.dll').read_bytes())
        assert json.loads(z.read('modinfo.json'))['Version']=='1.3.0'
        assert json.loads(z.read('Integrations/ScCsgoBundle.json'))['version']=='1.3.0'
    result=dict(failed=0,packageSha256=package['sha256'],performance=performance,reports=reports,
        appearance=dict(provider='unmodified official NekoMeko 1.1 DLL',checks=len(appearance['checks']),failed=0,sha256=sha(appearance_path.read_bytes())),
        actorRenders=dict(checks=len(actors),failed=0,sha256=sha(actor_path.read_bytes())),
        providers=read(STAGE/'providers.json'),
        verificationScope='Windows native CPU/GPU and simulated component/save lifecycle; no Android or actual player-world acceptance')
    (STAGE/'validation.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n','utf-8')
    return result

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    mode=parser.add_mutually_exclusive_group();mode.add_argument('--publish',action='store_true');mode.add_argument('--validate',action='store_true')
    args=parser.parse_args()
    if args.publish:publish()
    elif args.validate:print(json.dumps(validate(),ensure_ascii=True))
    else:stage()
