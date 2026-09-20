"""Verify packaged adapter/dependencies, then exercise the exact shipped DLLs on the installed engine."""
import hashlib
import json
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
game = Path(sys.argv[1])
stage = ROOT/'.tmp/appearance-installed-check'
stage.mkdir(exist_ok=True)
build = ROOT/'tools/AppearanceCheck/bin/Release/net10.0'
def version(project):return json.loads((ROOT/'src'/project/'modinfo.json').read_text('utf8'))['Version']
core_version=version('ScCsgoKnives');tactical_version=version('ScCsgoTactical')
for p in build.iterdir():
    if p.is_file(): shutil.copy2(p, stage/p.name)
if (build/'runtimes').exists(): shutil.copytree(build/'runtimes', stage/'runtimes', dirs_exist_ok=True)
packages = [
    (f'[API1.9]CS武器{core_version}-作者ZH667-全量版.scmod', ROOT/'src/ScCsgoKnives', 'ScCsgoKnives.dll'),
    (f'[API1.9]CS战术同伴拓展{tactical_version}-作者ZH667.scmod', ROOT/'src/ScCsgoTactical', 'ScCsgoTactical.dll'),
    ('[API1.9]NekoMekoModel1.1-源码构建.scmod', ROOT/'.tmp/nmm-player-appearance-audit-20260920', 'sc-nekomekomodel.dll'),
]
def sha(data): return hashlib.sha256(data).hexdigest()
evidence = {'packages': {}, 'installedEngine': {}, 'boundaries': 'Native diagnostic, not full-game/Android/multiplayer acceptance.'}
for filename, source, dll in packages:
    path = ROOT/'output'/filename
    with zipfile.ZipFile(path) as archive:
        assert archive.testzip() is None
        assert len(archive.namelist()) == len(set(archive.namelist()))
        if dll=='sc-nekomekomodel.dll':
            # Keep the released, unmodified provider. A local reference rebuild may
            # have a different build identity; execute the exact existing package.
            assert sha(path.read_bytes())=='7345a25427dc15bed8dcd49d7d7ad8c241194f68af663d2ca82e1fd015c233f2'
            assert subprocess.check_output(['git','-C',str(source),'status','--porcelain'],text=True).strip()==''
            assert subprocess.check_output(['git','-C',str(source),'rev-parse','HEAD'],text=True).strip()=='48c8f4fd8269441eb59add85e9538d17d739f95c'
        else:
            assert archive.read(dll) == (build/dll).read_bytes(), f'Rebuild/repackage {dll}'
        assert archive.read('modinfo.json') == (source/'modinfo.json').read_bytes()
        for p in (source/'Assets').rglob('*'):
            if p.is_file(): assert archive.read(p.relative_to(source).as_posix()) == p.read_bytes(), str(p)
        (stage/dll).write_bytes(archive.read(dll))
        if dll == 'ScCsgoKnives.dll': (stage/'ScCsgoResources.dll').write_bytes(archive.read('ScCsgoResources.dll'))
        if dll == 'ScCsgoTactical.dll':
            bridge=archive.read('Integrations/ScCsgoAppearance.bin')
            assert bridge==(build/'ScCsgoAppearance.dll').read_bytes(),'Rebuild/repackage integrated appearance'
            (stage/'ScCsgoAppearance.dll').write_bytes(bridge)
            appearance_source=ROOT/'src/ScCsgoAppearance'
            for p in (appearance_source/'Assets').rglob('*'):
                if p.is_file():assert archive.read(p.relative_to(appearance_source).as_posix())==p.read_bytes(),str(p)
            evidence['integratedAppearanceSha256']=sha(bridge)
        evidence['packages'][filename] = {'sha256': sha(path.read_bytes()), 'bytes': path.stat().st_size, 'dllSha256': sha(archive.read(dll))}
for dependency in game.glob('*.dll'):
    shutil.copy2(dependency, stage/dependency.name)
for name in ('Engine.dll', 'Survivalcraft.dll', 'EntitySystem.dll'):
    shutil.copy2(game/name, stage/name)
    evidence['installedEngine'][name] = sha((stage/name).read_bytes())
for name in ('glfw3.dll', 'openal32.dll', 'wrap_oal.dll', 'nfd64.dll'):
    if (game/name).exists(): shutil.copy2(game/name, stage/name)
report = ROOT/f'output/tactical-{tactical_version}/appearance-installed'
subprocess.run(['dotnet', str(stage/'AppearanceCheck.dll'), str(ROOT), str(game/'Content.zip'), str(report)], cwd=ROOT, check=True)
result = json.loads((report/'checks.json').read_text('utf8'))
assert result['failed'] == 0
evidence['checks'] = result['checks']
evidence['failed'] = 0
for key,path in [('coreChecks',f'release-{core_version}/full-check.json'),('tacticalChecks',f'tactical-{tactical_version}/tactical-check.json'),('installedTacticalChecks',f'tactical-{tactical_version}/tactical-installed-check.json'),('deliveryChecks',f'release-{core_version}/delivery-verification.json')]:
    checked=json.loads((ROOT/'output'/path).read_text('utf-8-sig'));assert checked['failed']==0,path
    evidence[key]=len(checked['checks'])
    if 'coreSha256' in checked:
        assert checked['coreSha256']==evidence['packages'][packages[0][0]]['sha256']
        assert checked['dlcSha256']==evidence['packages'][packages[1][0]]['sha256']
    if 'packageSha256' in checked:assert checked['packageSha256']==evidence['packages'][packages[0][0]]['sha256']
loading=json.loads((ROOT/f'output/tactical-{tactical_version}/loading-checks.json').read_text('utf8'))
assert loading['failed']==0 and loading['tacticalSha256']==evidence['packages'][packages[1][0]]['sha256']
evidence['optionalLoading']=loading
preservation=ROOT/f'output/tactical-{tactical_version}/preservation-checks.json'
if preservation.exists():
    retained=json.loads(preservation.read_text('utf8'));assert retained['failed']==0
    evidence['preservationChecks']=len(retained['checks'])
(ROOT/f'docs/release-tactical-{tactical_version}-evidence.json').write_text(json.dumps(evidence, ensure_ascii=False, indent=2)+'\n','utf8')
print(f"Verified {len(result['checks'])} installed-engine appearance checks and {len(packages)} exact packages.")
