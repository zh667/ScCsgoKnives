"""Verify the shipped glove gallery/third-person update and record release evidence."""
import hashlib
import io
import json
import zipfile
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
PAIRS = [
    ('[API1.9]CS武器1.4.9-作者ZH667-全量版.scmod', '[API1.9]CS武器1.4.8-作者ZH667-全量版.scmod'),
    ('[API1.9]CS战术同伴拓展1.3.2-作者ZH667.scmod', '[API1.9]CS战术同伴拓展1.3.1-作者ZH667.scmod'),
]

def read(path):
    return json.loads(path.read_text('utf-8-sig'))

def sha(data):
    return hashlib.sha256(data).hexdigest()

evidence = {'packages': {}, 'checks': {}, 'icons': {}, 'worldMeshes': {},
            'boundaries': 'Packaged DLL checks and native GPU diagnostics; not full-game, Android or network acceptance.'}
hashes = [sha((ROOT / 'output' / pair[0]).read_bytes()) for pair in PAIRS]
for kind in ('core', 'tactical'):
    report = read(ROOT / f'output/release-gloves-1.4.9/{kind}-check.json')
    assert report['failed'] == 0
    assert all(c.get('ok', c.get('Ok', False)) for c in report['checks'])
    assert report['packageSha256' if kind == 'core' else 'coreSha256'] == hashes[0]
    if kind == 'tactical':
        assert report['dlcSha256'] == hashes[1]
    evidence['checks'][kind] = {'count': len(report['checks']), 'failed': 0}

for current, old in PAIRS:
    path = ROOT / 'output' / current
    baseline = ROOT / 'output' / old
    if not baseline.exists():
        manifest = read(ROOT / 'docs/gloves-1.4.9-installation.json')
        baseline = next(Path(m['Archived']) for m in manifest['Archived'] if Path(m['Original']) == baseline)
    with zipfile.ZipFile(path) as package, zipfile.ZipFile(baseline) as previous:
        assert package.testzip() is None
        assert len(package.namelist()) == len(set(package.namelist()))
        resources = [n for n in previous.namelist() if n.startswith('Assets/') or n == 'ScCsgoResources.dll']
        for name in resources:
            assert package.read(name) == previous.read(name), 'Changed old resource: ' + name
        evidence['packages'][current] = {
            'sha256': sha(path.read_bytes()), 'bytes': path.stat().st_size,
            'preservedResources': len(resources),
            'dlls': {n: sha(package.read(n)) for n in package.namelist() if n.endswith(('.dll', '.bin'))}}

for name in ('ScCsgoKnives', 'ScCsgoTactical', 'ScCsgoAppearance'):
    package = PAIRS[0 if name == 'ScCsgoKnives' else 1][0]
    entry = 'Integrations/ScCsgoAppearance.bin' if name == 'ScCsgoAppearance' else name + '.dll'
    with zipfile.ZipFile(ROOT / 'output' / package) as archive:
        dll = archive.read(entry)
        assert dll == (ROOT / f'src/{name}/bin/Release/net10.0/{name}.dll').read_bytes()
        assert dll == (ROOT / f'tools/AppearanceCheck/bin/Release/net10.0/{name}.dll').read_bytes()

with zipfile.ZipFile(ROOT / 'output' / PAIRS[1][0]) as package:
    icons = sorted((ROOT / 'src/ScCsgoTactical/Assets/Textures/ScCsgoTactical/Gloves').glob('*.png'))
    assert len(icons) == 8
    for icon in icons:
        data = package.read('Assets/Textures/ScCsgoTactical/Gloves/' + icon.name)
        assert data == icon.read_bytes()
        image = Image.open(io.BytesIO(data))
        assert image.size == (512, 384) and image.mode == 'RGBA'
        assert image.getchannel('A').getextrema() == (0, 255)
        evidence['icons'][icon.name] = sha(data)
    textures = read(ROOT / 'docs/firstperson-gloves-assets.json')['textures']
    for name, digest in textures.items():
        assert sha(package.read('Assets/Textures/ScCsgoKnives/' + name)) == digest

for kind, audit in read(ROOT / 'docs/world-gloves-assets.json').items():
    path = ROOT / f'src/ScCsgoTactical/ArmData/world_{kind}.skin'
    assert sha(path.read_bytes()) == audit['sha256']
    assert sha(Path(audit['source']).read_bytes()) == audit['sourceSha256']
    evidence['worldMeshes'][kind] = {'sha256': audit['sha256'], 'sourceSha256': audit['sourceSha256']}

for label, folder in [('appearanceNative', 'appearance-native-149-fixed'), ('gloveUi', 'glove-ui-149')]:
    report = read(ROOT / f'output/{folder}/checks.json')
    assert report['failed'] == 0
    assert all(c['passed'] for c in report['checks'])
    evidence['checks'][label] = {'count': len(report['checks']), 'failed': 0}
report = read(ROOT / 'output/tactical-1.3.2/loading-checks.json')
assert report['failed'] == 0 and report['tacticalSha256'] == hashes[1]
assert len(report['runs']) == 18
evidence['checks']['optionalLoading'] = {'runs': len(report['runs']), 'failed': 0}
repro = read(ROOT / 'output/glove-light-repro-149/checks.json')
assert repro['failed'] == 1 and any('world glove uses body lighting across dark ground' in c.get('error', '') for c in repro['checks'])
evidence['checks']['lightingRegression'] = {'oldVersionFails': True, 'fixedVersionPasses': True,
    'coverage': 'Actual Draw across alternating foot-cell light, shared body light, real darkness and first-person suppression; CT/T and all five gloves.'}
(ROOT / 'docs/release-gloves-1.4.9-evidence.json').write_text(
    json.dumps(evidence, ensure_ascii=False, indent=2), encoding='utf8')
print(json.dumps(evidence, ensure_ascii=False, indent=2))
