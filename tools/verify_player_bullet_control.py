"""Verify the exact 1.4.3 / 1.1.3 packages and record the bullet-control checks."""
import hashlib
import json
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()
def read(path):
    return json.loads((ROOT / path).read_text('utf-8-sig'))

core = ROOT / 'output/[API1.9]CS武器1.4.3-作者ZH667-全量版.scmod'
dlc = ROOT / 'output/[API1.9]CS战术同伴拓展1.1.3-作者ZH667.scmod'
old_core = ROOT / 'output/[API1.9]CS武器1.4.2-作者ZH667-全量版.scmod'
old_dlc = ROOT / 'output/[API1.9]CS战术同伴拓展1.1.2-作者ZH667.scmod'
assert sha(old_core) == 'ffe007a09c48abd7023c6c33f31d1d005bfac90aaabd3258c10380cad3f9e64f'
assert sha(old_dlc) == '98eb776f9dda3846fd33859e647b1066eb6a2d657df5ffae1bdd58c568abd949'
for current, prior, dll in ((core, old_core, 'ScCsgoKnives.dll'), (dlc, old_dlc, 'ScCsgoTactical.dll')):
    with zipfile.ZipFile(current) as z, zipfile.ZipFile(prior) as old:
        assert z.testzip() is None and len(z.namelist()) == len(set(z.namelist()))
        assert set(z.namelist()) == set(old.namelist())
        for name in z.namelist():
            if name not in (dll, 'modinfo.json'):
                assert z.read(name) == old.read(name), name
        source = ROOT / 'src' / dll[:-4]
        assert z.read(dll) == (source / 'bin/Release/net10.0' / dll).read_bytes()
        assert z.read('modinfo.json') == (source / 'modinfo.json').read_bytes()

regular = read('output/release-1.4.3/full-check.json')
delivery = read('output/release-1.4.3/delivery-verification.json')
tactical = read('output/tactical-1.1.3/check.json')
installed = read('output/tactical-1.1.3/installed-check.json')
assert regular['failed'] == delivery['failed'] == tactical['failed'] == installed['failed'] == 0
assert regular['packageSha256'] == delivery['fullSha256'] == sha(core)
for report in (tactical, installed):
    assert report['coreSha256'] == sha(core) and report['dlcSha256'] == sha(dlc)
engine = {}
for name in ('Engine.dll', 'Survivalcraft.dll'):
    path = Path('D:/下载/[Windows]SurvivalcraftAPI_1.9.3.1') / name
    assert sha(path) == sha(ROOT / '.tmp/tactical-installed-check' / name)
    engine[name] = sha(path)
evidence = {
    'core': {'file': core.name, 'bytes': core.stat().st_size, 'sha256': sha(core), 'checks': len(regular['checks'])},
    'dlc': {'file': dlc.name, 'bytes': dlc.stat().st_size, 'sha256': sha(dlc), 'checks': len(tactical['checks']), 'installedEngineChecks': len(installed['checks'])},
    'deliveryChecks': len(delivery['checks']), 'failed': 0,
    'bulletChecks': [c for c in regular['checks'] if 'bullet-player-control/' in c['name']],
    'installedEngine': engine,
    'assetsByteIdenticalToPrevious': True,
    'formats': {'gunLayout': 5, 'gunSchema': 6, 'tacticalSchema': 1},
    'boundaries': ['Packaged DLL/native engine headless checks; no full-game, Android or all-mods acceptance.', 'Intentional Zeus electric control is preserved.']
}
(ROOT / 'docs/release-1.4.3-evidence.json').write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + '\n', 'utf8')
print(json.dumps({k: v for k, v in evidence.items() if k in ('core', 'dlc', 'deliveryChecks', 'failed')}, ensure_ascii=False))
