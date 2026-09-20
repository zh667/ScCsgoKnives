"""Package only the optional pinned NMM dependency. CS appearance ships inside tactical."""
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
    unchanged=False
    if path.exists():
        with zipfile.ZipFile(path) as archive:unchanged=set(archive.namelist())==set(entries) and all(archive.read(n)==d for n,d in entries.items())
    if not unchanged:
        with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
            for name, data in entries.items(): archive.writestr(name, data)
    with zipfile.ZipFile(path) as archive: assert archive.testzip() is None
    report[filename] = {'sha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'bytes': path.stat().st_size, 'entries': len(entries)}
folder = ROOT/'output/nekomeko-1.1'
folder.mkdir(exist_ok=True)
(folder/'packages.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', 'utf8')
print(json.dumps(report, ensure_ascii=False))
