"""Validate the exact split pair and replace delivered Lite; never touch installed mods."""
from pathlib import Path
import json,hashlib,zipfile,shutil,os
R=Path(__file__).resolve().parents[1];S=R/'.tmp/split-lite-130-20260926';D=R/'docs'
def sha(b):return hashlib.sha256(b).hexdigest()
def load(p):return json.loads(p.read_bytes())
packages=load(S/'packages.json');checks={}
paths=['native-core.json','native-agents.json','gate.json','native.json','local-nmm.json','curve-audit.json','actor-ct.json','actor-t.json','zip-core.json','zip-agents.json','resources-native/checks.json','npc/checks.json','appearance/checks.json']
paths += ['official-'+mode+'.json' for mode in ['both','reversed','none','nmm-only','neo-only','disabled','outdated']]
paths += ['compatibility-'+mode+'.json' for mode in ['historical','current','mini']]
for path in paths:
    value=load(S/path)
    assert value.get('failed',0)==0,path
    results=value.get('checks',[])
    if isinstance(results,list):assert not any(isinstance(c,dict) and (c.get('ok') is False or c.get('passed') is False) for c in results),path
    checks[path]=dict(sha256=sha((S/path).read_bytes()),failed=0,checks=len(results) if isinstance(results,list) else results,count=value.get('count'),frames=len(value.get('frames',[])),clips=value.get('clips'))
assert load(S/'resources-native/checks.json')['packageSha256']==packages['core']['sha256']
assert packages['core']['bytes']<40_000_000
expected={};archives={}
for key,row in packages.items():
    p=S/'candidate'/row['file'];assert len(p.read_bytes())==row['bytes'] and sha(p.read_bytes())==row['sha256']
    with zipfile.ZipFile(p) as z:
        assert z.testzip() is None and len(z.namelist())==len(set(z.namelist()))
        assert {n:sha(z.read(n)) for n in z.namelist()}==row['entries']
        meta=json.loads(z.read('modinfo.json'));assert meta['Version']=='1.3.0'
        for n in z.namelist():
            if n.startswith('Assets/') or n.endswith(('.dll','.bin')):
                if n in expected:assert expected[n]==sha(z.read(n)),n
                expected[n]=sha(z.read(n))
        archives[key]=dict(file=row['file'],bytes=row['bytes'],sha256=row['sha256'],compressedCategories={})
        for i in z.infolist():
            category=i.filename.split('/')[1] if i.filename.startswith('Assets/') else 'Code/metadata'
            counts=archives[key]['compressedCategories'];counts[category]=counts.get(category,0)+i.compress_size
with zipfile.ZipFile(S/'fixture-union.scmod') as z:
    for n,h in expected.items():assert sha(z.read(n))==h,('fixture mismatch',n)
for tool in ['NpcWeaponCheck','AppearanceCheck','ActorLoadCheck']:
    for n in ['ScCsgoKnives.dll','ScCsgoResources.dll','ScCsgoTactical.dll']:
        assert sha((S/'runtime'/tool/n).read_bytes())==expected[n],(tool,n)
for label,h in [('全量','a2922a8115fe010b564691115351667dba1c3327329fd1a36870184ffef10f02'),('极简','df63ff45a1e51e6a3f0f4765660cb5a6ec0643108aed3c57c149404a71239639')]:
    assert sha((R/f'output/[API1.9]CS武器1.3.0-{label}包.scmod').read_bytes())==h
original=S/'original-lite.scmod';assert sha(original.read_bytes())=='187598bfb07a358d88a60806c80dbb446808fe8663258761a4e38c776f8a3dce'
for key,row in packages.items():
    target=R/'output'/row['file'];candidate=S/'candidate'/row['file']
    if target.exists():assert sha(target.read_bytes()) in [row['sha256'],sha(original.read_bytes())],target
    staged=target.with_suffix('.scmod.partial');shutil.copyfile(candidate,staged);assert sha(staged.read_bytes())==row['sha256'];os.replace(staged,target)
record=dict(version='1.3.0',build='split-lite-130-20260926',packages=archives,checks=checks,sourceHashes=load(S/'source-hashes.json'),sourceLiteSha256=sha(original.read_bytes()),backups='manual',androidTested=False)
(D/'release-split-lite-1.3.0-2026-09-26-evidence.json').write_text(json.dumps(record,ensure_ascii=False,indent=2)+'\n',encoding='utf8')
shutil.copyfile(S/'asset-derivation.json',D/'release-split-lite-1.3.0-2026-09-26-resources.json')
shutil.copyfile(S/'resources-native/contact.png',D/'release-split-lite-1.3.0-2026-09-26.png')
print(json.dumps(archives,ensure_ascii=False,indent=2))
