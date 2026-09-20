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
for p in build.iterdir():
    if p.is_file(): shutil.copy2(p, stage/p.name)
if (build/'runtimes').exists(): shutil.copytree(build/'runtimes', stage/'runtimes', dirs_exist_ok=True)
packages = [
    ('[API1.9]CS武器1.4.4-作者ZH667-全量版.scmod', ROOT/'src/ScCsgoKnives', 'ScCsgoKnives.dll'),
    ('[API1.9]CS战术同伴拓展1.1.4-作者ZH667.scmod', ROOT/'src/ScCsgoTactical', 'ScCsgoTactical.dll'),
    ('[API1.9]CS玩家T-CT外观1.0.0-作者ZH667.scmod', ROOT/'src/ScCsgoAppearance', 'ScCsgoAppearance.dll'),
    ('[API1.9]NekoMekoModel1.1-源码构建.scmod', ROOT/'.tmp/nmm-player-appearance-audit-20260920', 'sc-nekomekomodel.dll'),
]
def sha(data): return hashlib.sha256(data).hexdigest()
evidence = {'packages': {}, 'installedEngine': {}, 'boundaries': 'Native diagnostic, not full-game/Android/multiplayer acceptance.'}
for filename, source, dll in packages:
    path = ROOT/'output'/filename
    with zipfile.ZipFile(path) as archive:
        assert archive.testzip() is None
        assert len(archive.namelist()) == len(set(archive.namelist()))
        assert archive.read(dll) == (build/dll).read_bytes(), f'Rebuild/repackage {dll}'
        assert archive.read('modinfo.json') == (source/'modinfo.json').read_bytes()
        for p in (source/'Assets').rglob('*'):
            if p.is_file(): assert archive.read(p.relative_to(source).as_posix()) == p.read_bytes(), str(p)
        (stage/dll).write_bytes(archive.read(dll))
        if dll == 'ScCsgoKnives.dll': (stage/'ScCsgoResources.dll').write_bytes(archive.read('ScCsgoResources.dll'))
        evidence['packages'][filename] = {'sha256': sha(path.read_bytes()), 'bytes': path.stat().st_size, 'dllSha256': sha(archive.read(dll))}
for dependency in game.glob('*.dll'):
    shutil.copy2(dependency, stage/dependency.name)
for name in ('Engine.dll', 'Survivalcraft.dll', 'EntitySystem.dll'):
    shutil.copy2(game/name, stage/name)
    evidence['installedEngine'][name] = sha((stage/name).read_bytes())
for name in ('glfw3.dll', 'openal32.dll', 'wrap_oal.dll', 'nfd64.dll'):
    if (game/name).exists(): shutil.copy2(game/name, stage/name)
report = ROOT/'output/appearance-1.0.0/installed'
subprocess.run(['dotnet', str(stage/'AppearanceCheck.dll'), str(ROOT), str(game/'Content.zip'), str(report)], cwd=ROOT, check=True)
result = json.loads((report/'checks.json').read_text('utf8'))
assert result['failed'] == 0
evidence['checks'] = result['checks']
evidence['failed'] = 0
evidence['coreChecks'] = len(json.loads((ROOT/'output/release-1.4.4/full-check.json').read_text('utf-8-sig'))['checks'])
evidence['tacticalChecks'] = len(json.loads((ROOT/'output/appearance-1.0.0/tactical-check.json').read_text('utf-8-sig'))['checks'])
evidence['deliveryChecks'] = len(json.loads((ROOT/'output/release-1.4.4/delivery-verification.json').read_text('utf-8-sig'))['checks'])
(ROOT/'docs/release-appearance-1.0.0-evidence.json').write_text(json.dumps(evidence, ensure_ascii=False, indent=2)+'\n','utf8')
print(f"Verified {len(result['checks'])} installed-engine appearance checks and {len(packages)} exact packages.")
