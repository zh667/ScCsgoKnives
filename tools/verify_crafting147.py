"""Check final release reports and preservation of every prior visual/audio asset."""
import hashlib
import json
from pathlib import Path
import zipfile

root = Path(__file__).resolve().parents[1]
report = root / 'output/release-crafting-1.4.7'
packages = [
    ('[API1.9]CS武器1.4.7-作者ZH667-全量版.scmod', '[API1.9]CS武器1.4.6-作者ZH667-全量版.scmod'),
    ('[API1.9]CS战术同伴拓展1.2.3-作者ZH667.scmod', '[API1.9]CS战术同伴拓展1.2.2-作者ZH667.scmod'),
]
sha = lambda data: hashlib.sha256(data).hexdigest()
evidence = dict(packages={}, checks={}, boundaries='Packaged DLL/native UI fixtures and static installed-mod audit, not full-game/Android/multiplayer acceptance.')
for name in ['core', 'tactical']:
    data = json.loads((report / f'{name}-check.json').read_text('utf-8-sig'))
    assert data['failed'] == 0 and all(c.get('ok', c.get('Ok', False)) for c in data['checks']), name
    evidence['checks'][name] = dict(count=len(data['checks']), failed=0)
    if name == 'core':
        assert data['packageSha256'] == sha((root / 'output' / packages[0][0]).read_bytes())
    else:
        assert data['coreSha256'] == sha((root / 'output' / packages[0][0]).read_bytes())
        assert data['dlcSha256'] == sha((root / 'output' / packages[1][0]).read_bytes())
for new, old in packages:
    path = root / 'output' / new
    baseline = root / 'output' / old
    if not baseline.exists():
        installation = json.loads((root / 'docs/crafting-1.4.7-installation.json').read_text('utf-8-sig'))
        baseline = next(Path(m['Archived']) for m in installation['Archived'] if Path(m['Original']) == baseline)
    with zipfile.ZipFile(path) as current, zipfile.ZipFile(baseline) as previous:
        assert current.testzip() is None
        assert len(current.namelist()) == len(set(current.namelist()))
        protected = [n for n in previous.namelist() if n.startswith('Assets/') or n == 'ScCsgoResources.dll']
        for n in protected:
            assert current.read(n) == previous.read(n), f'Unexpected resource change: {n}'
        evidence['packages'][new] = dict(sha256=sha(path.read_bytes()), bytes=path.stat().st_size,
            preservedResources=len(protected), dlls={n: sha(current.read(n)) for n in current.namelist() if n.endswith(('.dll', '.bin'))})
(root / 'docs/release-crafting-1.4.7-evidence.json').write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps(evidence, ensure_ascii=False, indent=2))
