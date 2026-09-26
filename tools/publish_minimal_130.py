"""Verify final candidate/evidence, then publish ONLY the independent Mini file."""
from pathlib import Path
import argparse,hashlib,json,zipfile,shutil,xml.etree.ElementTree as ET,os

root=Path(__file__).resolve().parents[1];stage=root/os.environ.get('SC_MINIMAL_STAGE','.tmp/minimal-130-20260926')
def sha(b):return hashlib.sha256(b).hexdigest()
def read(p):return json.loads(p.read_text(encoding='utf-8'))
def dump(p,v):p.write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
parser=argparse.ArgumentParser();parser.add_argument('--publish',action='store_true');args=parser.parse_args()
report=read(stage/'package.json');candidate=stage/'candidate'/report['path']
derivation=read(stage/'resource-derivation.json');quality=derivation.get('profile')=='quality-512-no-chicken'
limit=40_000_000 if quality else 30_000_000
prefix='release-minimal-inspect-1.3.0-2026-09-26' if quality and derivation.get('inspect') else 'release-minimal-quality-1.3.0-2026-09-26' if quality else 'release-minimal-1.3.0-2026-09-26'
assert sha(candidate.read_bytes())==report['sha256'] and candidate.stat().st_size==report['bytes']<limit
baselines={'全量':'a2922a8115fe010b564691115351667dba1c3327329fd1a36870184ffef10f02',
           '轻量':'187598bfb07a358d88a60806c80dbb446808fe8663258761a4e38c776f8a3dce'}
for edition,digest in baselines.items():assert sha((root/'output'/f'[API1.9]CS武器1.3.0-{edition}包.scmod').read_bytes())==digest
with zipfile.ZipFile(candidate) as z:
    assert z.testzip() is None and len(z.namelist())==len(set(z.namelist()))
    assert all(i.compress_type in (0,8) for i in z.infolist())
    assert {n:sha(z.read(n)) for n in z.namelist()}==report['entries']
    assert 'ScCsgoTactical.dll' not in z.namelist() and 'ScCsgoBundle.dll' not in z.namelist()
    for n in ['modinfo.json','Integrations/ScCsgoKnives.modinfo.json']:assert json.loads(z.read(n))['Version']=='1.3.0'
    assert ET.fromstring(z.read('Assets/ScCsgoKnivesEdition.xml')).get('Name')=='Mini'
    assert ET.fromstring(z.read('Assets/ScCompatibilityManifest.xml')).get('Legacy')=='true'
    for e in ET.fromstring(z.read('Assets/ScCsgoResources.xml')):assert sha(z.read(e.get('Path')))==e.get('Sha256')
    for assembly,folder in [('ScCsgoKnives','core'),('ScCsgoResources','resources')]:
        assert sha((stage/f'{folder}/bin/Release/net10.0/{assembly}.dll').read_bytes())==report['entries'][assembly+'.dll']
    report['largest']=sorted([(i.compress_size,i.filename) for i in z.infolist()],reverse=True)[:25]
checks={}
for name in ['gate','native','compatibility-current','compatibility-historical']:
    result=read(stage/(name+'.json'));assert result['failed']==0
    if name.startswith('compatibility'):
        assert result['count']==result['passed']==243
        assert result['modules'][2]['sha256']==report['entries']['ScCsgoKnives.dll']
    checks[name]={'passed':len(result['checks']),'reportSha256':sha((stage/(name+'.json')).read_bytes())}
if quality:
    previous=read(stage/'compatibility-previous-mini.json');assert previous['failed']==0 and previous['passed']==243
    assert previous['modules'][2]['sha256']==report['entries']['ScCsgoKnives.dll']
    checks['previousMini']={'passed':243,'reportSha256':sha((stage/'compatibility-previous-mini.json').read_bytes())}
native=read(stage/'native-resources/checks.json');assert native['failed']==0 and native['packageSha256']==report['sha256']
checks['nativeResources']={'checks':len(native['checks']),'frames':len(native['frames']),'clips':native['clips'],'reportSha256':sha((stage/'native-resources/checks.json').read_bytes())}
provenance=read(stage/'compat-runner-provenance.json')
assert provenance['packages']['mini']==report['sha256']
assert provenance['packages']['full']==baselines['全量'] and provenance['packages']['lite']==baselines['轻量']
derivation=read(stage/'resource-derivation.json')
evidence={'package':report,'unchangedBaselines':baselines,'validation':checks,'compatibilityRunner':provenance,
          'coreSources':read(stage/'core-source-hashes.json'),
          'derivationSha256':sha((stage/'resource-derivation.json').read_bytes()),
          'removedFiles':len(derivation['removed']),'changedFiles':len(derivation['changed']),
          'textures':len(derivation['textures']),'audio':len(derivation['audio']),
          'animationKeysRemoved':sum(a['keysRemoved'] for a in derivation['animations']),
          'retainedMainWeaponFinishes':derivation['selectedSkins'],
          'limits':['Windows native fixtures; Android device acceptance pending','Geometry, textures, audio and animation curves are lossy','Scope overlay and full interactive gameplay are not covered by the isolated render fixture']}
evidence['limitBytes']=limit
if quality:
    evidence['qualityChanges']=derivation['qualityChanges'];evidence['inspectCurves']=derivation['inspectCurves']
    evidence['limits'][1]='Gun geometry/colour are byte-identical to published Lite; inherited Lite loss and reduced auxiliary resources remain.'
dump(stage/'package.json',report)
if args.publish:
    destination=root/'output'/report['path']
    prior='324de95ef77b43a7d236d6fccc619a0864f378713abc2ff9e335306d2e53c995'
    if destination.exists() and sha(destination.read_bytes())!=report['sha256']:
        assert quality and sha(destination.read_bytes())==prior,'Unrecognized prior Mini release'
        backup=stage/'previous-published.scmod';assert not backup.exists() or sha(backup.read_bytes())==prior
        if not backup.exists():shutil.copyfile(destination,backup)
    if not destination.exists() or sha(destination.read_bytes())!=report['sha256']:
        pending=destination.with_suffix('.scmod.pending');assert not pending.exists()
        shutil.copyfile(candidate,pending);assert sha(pending.read_bytes())==report['sha256'];pending.replace(destination)
    dump(root/'docs'/f'{prefix}-evidence.json',evidence)
    shutil.copyfile(stage/'resource-derivation.json',root/'docs'/f'{prefix}-resources.json')
    for name in (['idle','reload','inspect'] if derivation.get('inspect') else ['idle','reload']):
        shutil.copyfile(stage/f'native-resources/contact-{name}.jpg',root/'docs'/f'{prefix}-{name}.jpg')
print(json.dumps({'published':args.publish,'path':str(root/'output'/report['path']),'bytes':report['bytes'],'sha256':report['sha256'],'validation':checks},ensure_ascii=False,indent=2))
