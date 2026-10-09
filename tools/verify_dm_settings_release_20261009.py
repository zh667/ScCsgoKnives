"""Read-back release identity/resources and every retained official 1.4.0+ reader."""
import concurrent.futures,hashlib,json,sys,zipfile
from pathlib import Path
SOURCE=Path(__file__).resolve().parents[1];ROOT=Path('E:/projects/ScCsgoKnives');S=ROOT/'.tmp/dev-temp/dm-settings-release-20261009'
import deathmatch_140 as dm
base=dm.base
def sha(b):return hashlib.sha256(b).hexdigest()
def read(p):return json.loads(p.read_text('utf-8-sig'))

def inspect():
    packages=read(S/'packages.json');record={};sources={}
    for label,p in packages.items():
        candidate=S/'candidate'/p['file'];previous=ROOT/'output'/p['file']
        assert sha(candidate.read_bytes())==p['sha256']
        with zipfile.ZipFile(previous) as old,zipfile.ZipFile(candidate) as new:
            assert set(old.namelist())==set(new.namelist()) and new.testzip() is None
            changed=[];unchanged=0
            for n in new.namelist():
                if new.read(n)!=old.read(n):
                    assert n.endswith(('.dll','.bin')) or n=='Integrations/ScCsgoBundle.json',n
                    changed.append(n)
                else:
                    unchanged+=1
                    if label!='死亡竞赛':assert base.raw_member(old,old.getinfo(n))==base.raw_member(new,new.getinfo(n))
            if 'ScCsgoKnives.dll' in new.namelist():
                edition='full' if label=='全量' else 'lite'
                assert new.read('ScCsgoKnives.dll')==(S/edition/'core/source/bin/Release/net10.0/ScCsgoKnives.dll').read_bytes()
            if label=='全量':assert json.loads(new.read('Integrations/ScCsgoBundle.json'))['coreSha256']==sha(new.read('ScCsgoKnives.dll'))
            if label=='死亡竞赛':assert new.read('ScCsgoDeathmatch.dll')==(S/'dm/source/bin/Release/net10.0/ScCsgoDeathmatch.dll').read_bytes()
        record[label]=dict(file=p['file'],sha256=p['sha256'],previousSha256=sha(previous.read_bytes()),bytes=p['bytes'],changed=changed,unchanged=unchanged,resourcesUnchanged=True)
    for project in ['ScCsgoKnives','ScCsgoTactical','ScCsgoVoice','ScCsgoDeathmatch']:
        root=SOURCE/'src'/project
        for p in root.rglob('*.cs'):
            if {'bin','obj'}&set(p.parts):continue
            rel=p.relative_to(root)
            if project=='ScCsgoDeathmatch':targets=[S/'dm/source'/rel]
            else:
                targets=[]
                for edition in ['full','lite']:
                    part={'ScCsgoKnives':'core','ScCsgoTactical':'agents','ScCsgoVoice':'voice'}[project]
                    if edition=='lite' and project=='ScCsgoTactical' and rel.name=='TacticalBlocks.cs':part='core'
                    targets.append(S/edition/part/'source'/rel)
            for dest in targets:assert dest.read_bytes()==p.read_bytes(),str(dest)
            sources[p.relative_to(SOURCE).as_posix()]=sha(p.read_bytes())
    for edition,label in [('full','全量'),('lite','轻量')]:
        ui=read(S/f'settings-{edition}/settings.json')
        with zipfile.ZipFile(S/'candidate'/packages[label]['file']) as z:assert ui['core']==sha(z.read('ScCsgoKnives.dll'))
        with zipfile.ZipFile(S/'candidate'/packages['死亡竞赛']['file']) as z:assert ui['deathmatch']==sha(z.read('ScCsgoDeathmatch.dll'))
        assert ui['failed']==0 and len(ui['checks'])>=41
    base.dump(S/'archive-verification.json',record);base.dump(S/'compiled-source-verification.json',sources)
    print(json.dumps({'packages':record,'sourceFiles':len(sources)},ensure_ascii=False),flush=True)

def switching():
    packages=[]
    for version in ['1.4.0','1.5.0']:
        root=ROOT/'output'/f'history-{version}'
        if root.exists():packages.extend(p for p in root.rglob('*.scmod') if p.name.endswith(('全量包.scmod','轻量包.scmod')))
    packages.extend(ROOT/'output'/p['file'] for label,p in read(S/'packages.json').items() if label in ['全量','轻量'])
    identities={};jobs=[]
    for p in packages:
        with zipfile.ZipFile(p) as z:
            core=sha(z.read('ScCsgoKnives.dll'));package=sha(p.read_bytes())
            if core in identities:identities[core]['packages'].append(dict(path=str(p),sha256=package));continue
            folder=S/'historical-readers'/core[:12];folder.mkdir(parents=True,exist_ok=True)
            for name in z.namelist():
                if name.endswith('.dll'):(folder/Path(name).name).write_bytes(z.read(name))
        identities[core]=dict(packages=[dict(path=str(p),sha256=package)])
        for edition in ['full','lite']:
            key='historical-'+core[:12]+'-'+edition
            jobs.append((key,['dotnet',S/'tree/tools/CompatibilityCheck/bin/Release/net10.0/CompatibilityCheck.dll',folder/'ScCsgoKnives.dll',S/'old140/full/ScCsgoKnives.dll',S/edition/'core/source/bin/Release/net10.0/ScCsgoKnives.dll',S/(key+'.json')]))
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:results=dict(zip([j[0] for j in jobs],pool.map(lambda j:base.run(S,*j),jobs)))
    base.dump(S/'historical-inputs.json',identities);base.dump(S/'historical-checks.json',results)
    assert results and all(results.values()),results
    print('Historical readers',len(identities),'package revisions',len(packages),'passed',len(results),flush=True)
if __name__=='__main__':
    for step in sys.argv[1].split(','):{'inspect':inspect,'switching':switching}[step]()
