"""P1a package audit: only DLL/metadata change, every existing asset survives."""
import argparse,hashlib,json,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def verify():
    parser=argparse.ArgumentParser();parser.add_argument('--version',choices=['0.39.0','0.39.1'],default='0.39.0');args=parser.parse_args()
    version=args.version;previous='0.39.0' if version=='0.39.1' else '0.38.6'
    base=ROOT/f'output/ScCsgoKnives-{previous}.scmod'
    editions={}
    for edition,suffix in [('Full',''),('Lite','-Lite')]:
        p=ROOT/f'output/ScCsgoKnives-{version}{suffix}.scmod'
        r=json.loads(p.with_name(p.stem+'-check.json').read_text('utf-8-sig'))
        assert r['failed']==0 and r['packageSha256']==sha(p)
        editions[edition]={k:r[k] for k in ['packageSha256','dllSha256','packageBytes','entries','failed']}
        editions[edition]['passed']=len(r['checks'])
        old=base if not suffix else ROOT/f'output/ScCsgoKnives-{previous}-Lite.scmod'
        with zipfile.ZipFile(old) as a,zipfile.ZipFile(p) as b:
            assert set(a.namelist())==set(b.namelist())
            changed=[n for n in a.namelist() if a.read(n)!=b.read(n)]
            assert set(changed)=={'ScCsgoKnives.dll','modinfo.json'},changed
            editions[edition]['changedEntries']=changed
    assert editions['Full']['dllSha256']==editions['Lite']['dllSha256']
    expected=json.loads((ROOT/'docs/gun-handling-35-source-audit-2026-09-08.json').read_text('utf-8'))
    actual=json.loads((ROOT/'src/ScCsgoKnives/AnimationData/gun_handling.json').read_text('utf-8'))
    assert len(actual['Guns'])==35
    for row in expected['guns']:
        a=actual['Guns'][row['name']];p=row['proposal']
        assert a['Range']==p['maxRange'] and a['Modes']==p['modes']
    report={'version':version,'previousVersion':previous,'stage':'diagnostic instrumentation only' if version=='0.39.1' else 'P1a implementation and offline verification; device acceptance pending',
            'editions':editions,'removedAssets':[], 'unchanged':'All packaged models, textures, audio, animations and other asset files',
            'saveLayout':5,'registrySchema':2,'newCounterGrowthSchema':False,
            'installedToGame':False,'playerWorldsModified':False,
            'notYetImplemented':['P1b attribute UI','P2 mobile layout editor','P3 StatTrak and schema migration','P4 growth'],
            'deviceAcceptance':'No new live-world or Android acceptance; test new preset against classic before advancing the planned gate'}
    filename='gun-diagnostics-0391-verification.json' if version=='0.39.1' else 'gun-handling-p1a-0390-verification.json'
    (ROOT/'docs'/filename).write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n','utf-8')
    print(json.dumps(report,ensure_ascii=False,indent=2))
if __name__=='__main__':verify()
