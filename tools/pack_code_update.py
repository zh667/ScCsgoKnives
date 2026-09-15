"""Bundle a code-only release using the existing immutable Full/512 resources.
Run pack_scmod.py --edition full --split-resources first, then this via dev.ps1.
"""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'output'
RESOURCES = {
    'Full': ('ScCsgoResources-1.0.0.scmod', '66fe182a3f83d01ff10ceb1dc6d00d0ebf5626f3ac30837eb9426751cbcb73eb'),
    'Optimized512': ('ScCsgoResources-1.1.0-Optimized512.scmod', '9b7dfbe33eda51fdc428db63f9247a49d91a399675194b8d3da1f5bda04d1049'),
}

def sha(data):
    return hashlib.sha256(data).hexdigest()

def write(path, entries, compression):
    pending = path.with_suffix('.pending')
    with zipfile.ZipFile(pending, 'w', compression) as archive:
        for name, data in sorted(entries.items()):
            info = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
            info.compress_type = compression
            archive.writestr(info, data)
    pending.replace(path)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', required=True)
    parser.add_argument('--notes', type=Path, required=True)
    args = parser.parse_args()
    full = OUT / f'ScCsgoKnives-{args.version}-preview.scmod'
    with zipfile.ZipFile(full) as archive:
        core = {name: archive.read(name) for name in archive.namelist()}
    metadata = json.loads(core['modinfo.json'])
    assert metadata['Version'] == f'{args.version}-preview'
    assert metadata['Dependencies']['zh667.ScCsgoResources'] == '[1.0.0]'
    resources = {}
    for edition, (filename, digest) in RESOURCES.items():
        data = (OUT / filename).read_bytes()
        assert sha(data) == digest, f'Immutable resource pack changed: {filename}'
        resources[edition] = data
    opt = dict(core)
    metadata.update(Version=f'{args.version}-optimized-preview', Name='CS武器 · 512 优化版')
    metadata['Dependencies']['zh667.ScCsgoResources'] = '[1.1.0]'
    metadata['Description'] = '全部武器和玩法；配套 512 WebP、模型优化资源包 1.1.0。简化材质默认开启，可在模组设置关闭。与全量版二选一安装。'
    opt['modinfo.json'] = (json.dumps(metadata, ensure_ascii=False, indent=2) + '\n').encode('utf-8')
    opt['Assets/ScCsgoKnivesEdition.xml'] = b'<Edition Name="Optimized512" />\n'
    optpath = OUT / f'ScCsgoKnives-{args.version}-Optimized512-preview.scmod'
    write(optpath, opt, zipfile.ZIP_DEFLATED)
    assert opt['ScCsgoKnives.dll'] == core['ScCsgoKnives.dll']
    manifest = {'version': args.version, 'sameGameplayDll': True, 'dllSha256': sha(core['ScCsgoKnives.dll']), 'files': []}
    files = [full, optpath]
    notes = args.notes.read_bytes()
    for edition, corepath, label in [('Full', full, '全量版'), ('Optimized512', optpath, '512优化版')]:
        resourceName = RESOURCES[edition][0]
        bundle = OUT / f'CS武器-{args.version}-{label}.zip'
        write(bundle, {corepath.name: corepath.read_bytes(), resourceName: resources[edition], '安装与更新说明.md': notes}, zipfile.ZIP_STORED)
        files.extend([OUT / resourceName, bundle])
    for path in files:
        manifest['files'].append({'path': path.name, 'bytes': path.stat().st_size, 'sha256': sha(path.read_bytes())})
    report = OUT / f'fixes-{args.version}'
    report.mkdir(exist_ok=True)
    (report / 'delivery.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(manifest, ensure_ascii=False, indent=2))

if __name__ == '__main__':
    main()
