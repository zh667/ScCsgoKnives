"""Extra native switching, packaged-byte and source checks for the 2026-10-09 family."""
import concurrent.futures
import hashlib
import json
import subprocess
import sys
import zipfile
from pathlib import Path

ROOT=Path('E:/projects/ScCsgoKnives')
S=ROOT/'.tmp/dev-temp/repack-150-20261009'
sys.path.insert(0,str(ROOT/'tools'))
import deathmatch_140 as dm
base=dm.base
def sha(data):return hashlib.sha256(data).hexdigest()
def read(path):return json.loads(path.read_text('utf-8-sig'))

def switching():
    tool=S/'tree/tools/CompatibilityCheck/bin/Release/net10.0/CompatibilityCheck.dll'
    assert tool.exists()
    records={};jobs=[]
    # Include every previous delivered Full revision plus the current Full/Lite pair.
    previous=sorted(folder/base.name('全量') for folder in (ROOT/'output/history-1.5.0').iterdir()
                    if folder.is_dir() and (folder/base.name('全量')).is_file())
    previous+=[ROOT/'output'/base.name('全量'),ROOT/'output'/base.name('轻量')]
    for old in previous:
        digest=sha(old.read_bytes());folder=S/'old150all'/digest[:12];folder.mkdir(parents=True,exist_ok=True)
        with zipfile.ZipFile(old) as z:
            for info in z.infolist():
                if info.filename.endswith('.dll'):(folder/Path(info.filename).name).write_bytes(z.read(info))
        key='switching-150-'+digest[:12]
        records[key]=dict(package=str(old),sha256=digest)
        for edition in ['full','lite']:
            label=key+'-'+edition
            jobs.append((label,['dotnet',tool,folder/'ScCsgoKnives.dll',S/'old140/full/ScCsgoKnives.dll',
                S/edition/'core/source/bin/Release/net10.0/ScCsgoKnives.dll',S/(label+'.json')]))
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        results=dict(zip([j[0] for j in jobs],pool.map(lambda j:base.run(S,*j),jobs)))
    base.dump(S/'extra-switching-inputs.json',records);base.dump(S/'extra-switching.json',results)
    assert all(results.values()),results

def inspect():
    pk=read(S/'packages.json');changes={};source={}
    for label,record in pk.items():
        candidate=S/'candidate'/record['file'];old=ROOT/'output'/record['file']
        assert sha(candidate.read_bytes())==record['sha256']
        same=[];changed=[]
        with zipfile.ZipFile(old) as a,zipfile.ZipFile(candidate) as b:
            assert set(a.namelist())==set(b.namelist())
            assert b.testzip() is None
            for name in a.namelist():
                if a.read(name)==b.read(name):same.append(name)
                else:changed.append(name)
            assert all(name.endswith(('.dll','.bin')) for name in changed),changed
            assert json.loads(b.read('modinfo.json'))['Version']=='1.5.0'
            for name in same:
                if label!='死亡竞赛':assert base.raw_member(a,a.getinfo(name))==base.raw_member(b,b.getinfo(name))
        changes[label]=dict(previousSha256=sha(old.read_bytes()),changed=changed,unchanged=len(same),
            allResourceAndMetadataBytesUnchanged=True,sha256=record['sha256'],bytes=candidate.stat().st_size,file=record['file'])
    for product in ['ScCsgoKnives','ScCsgoTactical','ScCsgoVoice']:
        paths=[p for p in (ROOT/'src'/product).rglob('*.cs') if not {'bin','obj'}&set(p.parts)]
        for p in paths:
            rel=p.relative_to(ROOT/'src'/product)
            for edition in ['full','lite']:
                role={'ScCsgoKnives':'core','ScCsgoTactical':'agents','ScCsgoVoice':'voice'}[product]
                if edition=='lite' and product=='ScCsgoTactical' and rel.as_posix()=='TacticalBlocks.cs':role='core'
                dest=S/edition/role/'source'/rel
                assert dest.read_bytes()==p.read_bytes(),str(dest)
            source[p.relative_to(ROOT).as_posix()]=sha(p.read_bytes())
    for p in (ROOT/'src/ScCsgoDeathmatch').rglob('*.cs'):
        if {'bin','obj'}&set(p.parts):continue
        assert p.read_bytes()==(S/'dm/source'/p.relative_to(ROOT/'src/ScCsgoDeathmatch')).read_bytes()
        source[p.relative_to(ROOT).as_posix()]=sha(p.read_bytes())
    base.dump(S/'archive-verification.json',changes)
    base.dump(S/'compiled-source-verification.json',dict(commit='d3a9568a90c6d9e856e5ca1b653c17a6766625e9',files=source,count=len(source)))
    for role in ['full','lite']:
        r=read(S/f'airdrop-{role}.json');assert all(c['ok'] for c in r['checks'])
        assert r['packageSha256']==pk['全量' if role=='full' else '轻量']['sha256']
    print(json.dumps(dict(packages=changes,currentSourceFiles=len(source)),ensure_ascii=False),flush=True)

if __name__=='__main__':
    for step in sys.argv[1].split(','):{'switching':switching,'inspect':inspect}[step]()
