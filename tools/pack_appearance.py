"""Package the optional appearance adapter and the pinned, unmodified NMM dependency build."""
import hashlib
import json
import subprocess
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
UPSTREAM = ROOT / '.tmp/nmm-player-appearance-audit-20260920'
PIN = '48c8f4fd8269441eb59add85e9538d17d739f95c'
assert subprocess.check_output(['git', '-C', str(UPSTREAM), 'rev-parse', 'HEAD'], text=True).strip() == PIN
assert not subprocess.check_output(['git', '-C', str(UPSTREAM), 'status', '--porcelain'], text=True).strip()
report = {}
for source, dll, filename in (
    (ROOT/'src/ScCsgoAppearance', ROOT/'src/ScCsgoAppearance/bin/Release/net10.0/ScCsgoAppearance.dll', '[API1.9]CS玩家T-CT外观1.0.0-作者ZH667.scmod'),
    (UPSTREAM, ROOT/'tools/NekoMekoReference/bin/Release/net10.0/sc-nekomekomodel.dll', '[API1.9]NekoMekoModel1.1-源码构建.scmod'),
):
    sources = [p for p in source.rglob('*.cs') if not {'obj', 'bin'} & set(p.relative_to(source).parts)]
    assert dll.stat().st_mtime >= max(p.stat().st_mtime for p in sources), 'Rebuild first'
    entries = {dll.name: dll.read_bytes(), 'modinfo.json': (source/'modinfo.json').read_bytes()}
    for name in ('LICENSE', 'ASSET_SOURCES.md', 'icon.webp'):
        if (source/name).exists(): entries[name] = (source/name).read_bytes()
    for p in sorted((source/'Assets').rglob('*')):
        if p.is_file(): entries[p.relative_to(source).as_posix()] = p.read_bytes()
    if source == UPSTREAM:
        entries['BUILD_PROVENANCE.txt'] = f'Unmodified upstream source: https://gitee.com/yangsanfengsc/sc-nekomekomodel\nCommit: {PIN}\nBuilt against Survivalcraft API 1.9.3.1 and user-installed Neorxna 1.4.\n'.encode()
    else:
        entries['LICENSE'] = (ROOT/'LICENSE').read_bytes()
    path = ROOT/'output'/filename
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for name, data in entries.items(): archive.writestr(name, data)
    with zipfile.ZipFile(path) as archive: assert archive.testzip() is None
    report[filename] = {'sha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'bytes': path.stat().st_size, 'entries': len(entries)}
folder = ROOT/'output/appearance-1.0.0'
folder.mkdir(exist_ok=True)
(folder/'packages.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', 'utf8')
print(json.dumps(report, ensure_ascii=False))
