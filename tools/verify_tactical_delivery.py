"""Verify tactical fix delivery and record compact, reproducible evidence."""
import hashlib,json,zipfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
core=ROOT/'output/[API1.9]CS武器1.4.2-作者ZH667-全量版.scmod'
dlc=ROOT/'output/[API1.9]CS战术同伴拓展1.1.1-作者ZH667.scmod'
def read(p):return json.loads((ROOT/p).read_text('utf-8-sig'))
regular=read('output/release-1.4.2/full-check.json')
checks=read('output/tactical-1.1.1/check.json');installed=read('output/tactical-1.1.1/installed-check.json')
assert regular['failed']==checks['failed']==installed['failed']==0
assert regular['packageSha256']==sha(core)
with zipfile.ZipFile(core) as c,zipfile.ZipFile(dlc) as z:
    assert z.testzip() is None and len(z.namelist())==len(set(z.namelist()))
    for r in (checks,installed):
        assert r['coreSha256']==sha(core)
        assert r['dlcSha256']==sha(dlc)
    assert z.read('ScCsgoTactical.dll')==(ROOT/'src/ScCsgoTactical/bin/Release/net10.0/ScCsgoTactical.dll').read_bytes()
    assert z.read('modinfo.json')==(ROOT/'src/ScCsgoTactical/modinfo.json').read_bytes()
    for p in (ROOT/'src/ScCsgoTactical/Assets').rglob('*'):
        if p.is_file():assert z.read(p.relative_to(ROOT/'src/ScCsgoTactical').as_posix())==p.read_bytes(),str(p)
    dll=hashlib.sha256(z.read('ScCsgoTactical.dll')).hexdigest();entries=len(z.namelist())
engine={}
for name in ('Engine.dll','Survivalcraft.dll'):
    source=Path('D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1')/name
    assert sha(source)==sha(ROOT/'.tmp/tactical-installed-check'/name)==sha(ROOT/'.tmp/tactical-111-installed-gpu'/name)
    engine[name]=sha(source)
gpu=read('output/tactical-1.1.1/installed-gpu/gpu.json')
assert len(gpu)==12 and all(r['pixels']>1000 for r in gpu)
evidence={'core':{'file':core.name,'bytes':core.stat().st_size,'sha256':sha(core),'checks':len(regular['checks']),'failed':0},
    'dlc':{'file':dlc.name,'bytes':dlc.stat().st_size,'sha256':sha(dlc),'dllSha256':dll,'entries':entries,'checks':len(checks['checks']),'installedEngineChecks':len(installed['checks']),'failed':0},
    'installedEngine':engine,'nativeGpu':gpu,
    'boundaries':['Isolated native desktop GPU diagnostic, not a full game screenshot.','Manual squad test uses prepared entity factories with the real packaged director.','No Android or all-mods acceptance.','Death is adapted skeletal collapse, not CS2 ragdoll physics.'],
    'formats':{'gunLayout':5,'gunSchema':6,'migrationRequired':False}}
(ROOT/'docs/release-1.4.2-evidence.json').write_text(json.dumps(evidence,ensure_ascii=False,indent=2)+'\n','utf8')
print(json.dumps({'core':evidence['core'],'dlc':evidence['dlc']},ensure_ascii=False))
