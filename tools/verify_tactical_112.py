"""Verify the 1.1.2 delivery against both native-engine check reports and the prior assets."""
import hashlib
import json
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()
def read(path):
    return json.loads((ROOT / path).read_text('utf-8-sig'))

core = ROOT / 'output/[API1.9]CS武器1.4.2-作者ZH667-全量版.scmod'
dlc = ROOT / 'output/[API1.9]CS战术同伴拓展1.1.2-作者ZH667.scmod'
old = ROOT / 'output/[API1.9]CS战术同伴拓展1.1.1-作者ZH667.scmod'
assert sha(core) == 'ffe007a09c48abd7023c6c33f31d1d005bfac90aaabd3258c10380cad3f9e64f'
assert sha(old) == '0755715872f30b0c277d1a24b9c1efd4840e6a0f11e04594e687aaa250fe64ad'
reports = {name: read(f'output/tactical-1.1.2/{name}.json') for name in ('check', 'installed-check')}
for report in reports.values():
    assert report['failed'] == 0 and all(c['Ok'] for c in report['checks'])
    assert report['coreSha256'] == sha(core) and report['dlcSha256'] == sha(dlc)
with zipfile.ZipFile(dlc) as z, zipfile.ZipFile(old) as prior:
    assert z.testzip() is None and len(z.namelist()) == len(set(z.namelist()))
    assert set(z.namelist()) == set(prior.namelist())
    assert json.loads(z.read('modinfo.json'))['Version'] == '1.1.2'
    source = ROOT / 'src/ScCsgoTactical'
    assert z.read('ScCsgoTactical.dll') == (source / 'bin/Release/net10.0/ScCsgoTactical.dll').read_bytes()
    assert z.read('modinfo.json') == (source / 'modinfo.json').read_bytes()
    for path in (source / 'Assets').rglob('*'):
        if path.is_file():
            name = path.relative_to(source).as_posix()
            assert z.read(name) == path.read_bytes()
            if name != 'Assets/ScTactical.xdb':
                assert z.read(name) == prior.read(name), name
    dll_sha = hashlib.sha256(z.read('ScCsgoTactical.dll')).hexdigest()
engine = {}
for name in ('Engine.dll', 'Survivalcraft.dll'):
    installed = Path('D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1') / name
    assert sha(installed) == sha(ROOT / '.tmp/tactical-installed-check' / name)
    engine[name] = sha(installed)
evidence = {
    'core': {'file': core.name, 'sha256': sha(core), 'unchanged': True},
    'dlc': {'file': dlc.name, 'sha256': sha(dlc), 'bytes': dlc.stat().st_size, 'dllSha256': dll_sha},
    'checks': {name: {'passed': len(r['checks']), 'failed': r['failed'], 'cases': [c['Name'] for c in r['checks']]} for name, r in reports.items()},
    'installedEngine': engine,
    'formats': {'gunLayout': 5, 'gunSchema': 6, 'tacticalSchema': 1, 'migrationRequired': False},
    'boundaries': [
        'Headless native DLL checks, not a full game session or Android/all-mods acceptance.',
        'Squad creation uses prepared entity factories; native merged templates are checked separately.',
        'Stationary turn convergence applies the native locomotion yaw equation, not full physical locomotion.',
        'Graphics assets are byte-identical to 1.1.1; no new GPU acceptance claimed.',
        'Unknown mod-specific hostility requires an owner/self attack event if neither category nor native chase behavior identifies it.'
    ]
}
(ROOT / 'docs/release-tactical-1.1.2-evidence.json').write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + '\n', 'utf8')
print(json.dumps({'dlc': evidence['dlc'], 'checks': {k: len(v['checks']) for k, v in reports.items()}}, ensure_ascii=False))
