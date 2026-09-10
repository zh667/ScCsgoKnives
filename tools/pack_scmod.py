"""Pack the verified Release build as Full and/or Lite without changing source assets."""
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'src/ScCsgoKnives'
BUILD = SOURCE / 'bin/Release/net10.0'
OUT = ROOT / 'output'
RESOURCE_SOURCE = ROOT / 'src/ScCsgoResources'
RESOURCE_DLL = RESOURCE_SOURCE / 'bin/Release/net10.0/ScCsgoResources.dll'


def is_resource_asset(name):
    return name.startswith(('Assets/Textures/', 'Assets/Models/', 'Assets/Audio/'))


def write_split(files, info):
    meta = json.loads((RESOURCE_SOURCE / 'modinfo.json').read_text(encoding='utf-8'))
    dependency = info.get('Dependencies', {}).get(meta['PackageName'])
    if dependency != f"[{meta['Version']}]":
        raise SystemExit('Core dependency does not match resource pack version.')
    if not RESOURCE_DLL.is_file():
        raise SystemExit('Missing resource assembly: build the core and its project reference first.')
    resource_files = [(n, p) for n, p in files if is_resource_asset(n)]
    core_files = [(n, p) for n, p in files if not is_resource_asset(n)]
    resources = {n: p.read_bytes() for n, p in resource_files}
    resources['ScCsgoResources.dll'] = RESOURCE_DLL.read_bytes()
    resources['modinfo.json'] = (json.dumps(meta, ensure_ascii=False, indent=2)+'\n').encode('utf-8')
    for n in ('LICENSE', 'THIRD_PARTY_NOTICES.md', 'ASSET_SOURCES.md'):
        resources[n] = (BUILD / n).read_bytes()
    marker = ET.Element('Resources', Version=meta['Version'], Format='1', Edition='Full')
    for n, data in sorted(resources.items()):
        if is_resource_asset(n):
            ET.SubElement(marker, 'File', Path=n, Sha256=hashlib.sha256(data).hexdigest())
    resources['Assets/ScCsgoResources.xml'] = ET.tostring(marker, encoding='utf-8')
    resource_target = OUT / f"ScCsgoResources-{meta['Version']}.scmod"
    core_target = OUT / f"ScCsgoKnives-{info['Version']}.scmod"

    def write_zip(path, items):
        # Fixed timestamps keep unchanged resources byte-identical across code-only rebuilds.
        with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED) as z:
            for name, data in sorted(items.items()):
                e = zipfile.ZipInfo(name, (2026, 1, 1, 0, 0, 0))
                e.compress_type = zipfile.ZIP_DEFLATED
                z.writestr(e, data)

    candidate = OUT / (resource_target.name + '.pending')
    write_zip(candidate, resources)
    if resource_target.exists():
        if hashlib.sha256(resource_target.read_bytes()).digest() != hashlib.sha256(candidate.read_bytes()).digest():
            candidate.unlink()
            raise SystemExit('Existing resource pack differs: bump its version and core dependency; never replace the same resource version.')
        candidate.unlink()
    else:
        candidate.replace(resource_target)
    write_zip(core_target, {n: p.read_bytes() for n, p in core_files})
    if any(is_resource_asset(n) for n, _ in core_files):
        raise AssertionError('Resources leaked into core')
    report = {'version': info['Version'], 'edition': 'Full', 'split': True,
              'core': {'file': core_target.name, 'bytes': core_target.stat().st_size, 'sha256': hashlib.sha256(core_target.read_bytes()).hexdigest()},
              'resources': {'file': resource_target.name, 'bytes': resource_target.stat().st_size, 'sha256': hashlib.sha256(resource_target.read_bytes()).hexdigest()},
              'movedAssets': [n for n, _ in resource_files],
              'resourceAssemblySha256': hashlib.sha256(resources['ScCsgoResources.dll']).hexdigest()}
    reports = OUT / 'reports'; reports.mkdir(exist_ok=True)
    (reports / f"ScCsgoKnives-{info['Version']}.split.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(f'{core_target}: {core_target.stat().st_size/1e6:.2f} MB; requires {resource_target.name}', flush=True)
    print(f'{resource_target}: {resource_target.stat().st_size/1e6:.2f} MB; {len(resource_files)} full-resolution asset files', flush=True)


def newest_source_mtime():
    return max(p.stat().st_mtime for p in SOURCE.rglob('*')
               if p.is_file() and not any(v in ('bin', 'obj') for v in p.relative_to(SOURCE).parts)
               and p.suffix in ('.cs', '.csproj', '.json'))


def check_catalog():
    root = SOURCE / 'AnimationData'
    expected = {}
    for path in sorted(root.glob('*.cs2.animation.json')):
        data = json.loads(path.read_text(encoding='utf-8'))
        expected[path.name.removesuffix('.cs2.animation.json')] = {
            key: data.get(key) for key in ('Skinned', 'Parts', 'MeshParts')}
        expected[path.name.removesuffix('.cs2.animation.json')]['Clips'] = {
            name: {k: clip[k] for k in ('SourceName', 'Alias', 'Duration', 'Events', 'Additive', 'AdditiveBase', 'AdditiveOver') if k in clip}
            for name, clip in data['Clips'].items()}
    path = root / 'cs2_catalog.json'
    if json.loads(path.read_text(encoding='utf-8')) != expected:
        raise SystemExit('cs2_catalog.json is stale; run tools/generate_cs2_catalog.py and rebuild.')


# Edition -> the size 1024 x 1024 textures are derived at. Lite is the phone
# edition; Mini goes one step further for the smallest download (weapon textures
# visibly softer up close). Everything else - DLL, audio, gameplay - is shared.
EDITION_TEXTURE_SIZE = {'Lite': 512, 'Mini': 256}
EDITION_META = {
    'Lite': ('ScCsgoKnives 轻量版 Lite', 'CS2 real hands and all weapons. 512px textures and reduced decorative particles. 与完整版二选一安装，玩法与存档兼容。'),
    'Mini': ('ScCsgoKnives 迷你版 Mini', 'CS2 real hands and all weapons. 256px textures and reduced decorative particles; the smallest download. 与其它版本二选一安装，玩法与存档兼容。'),
}


def lite_texture(data, name, size=512):
    from PIL import Image
    import numpy as np
    with Image.open(io.BytesIO(data)) as image:
        if image.size != (1024, 1024):
            return data, None
        image = image.resize((size, size), Image.Resampling.LANCZOS)
        normal = name.endswith('_normal.png')
        if normal:
            # Normals are vectors, not colors. Normalize after filtering; preserve alpha.
            pixels = np.array(image.convert('RGBA'))
            vectors = pixels[:, :, :3].astype(np.float32) / 127.5 - 1
            length = np.linalg.norm(vectors, axis=2, keepdims=True)
            bad = length[:, :, 0] < 1e-6
            vectors /= np.maximum(length, 1e-6)
            vectors[bad] = (0, 0, 1)
            pixels[:, :, :3] = np.clip(np.rint((vectors + 1) * 127.5), 0, 255).astype(np.uint8)
            image = Image.fromarray(pixels)
        stream = io.BytesIO()
        image.save(stream, format='PNG', optimize=True)
        return stream.getvalue(), {'path': name, 'from': [1024, 1024], 'to': [size, size],
            'normalRenormalized': normal, 'sourceSha256': hashlib.sha256(data).hexdigest(),
            'resultSha256': hashlib.sha256(stream.getvalue()).hexdigest()}


def pack(edition, files, info):
    suffix = '' if edition == 'Full' else f'-{edition}'
    target = OUT / f"ScCsgoKnives-{info['Version']}{suffix}.scmod"
    transformed = []
    with zipfile.ZipFile(target, 'w', zipfile.ZIP_DEFLATED) as archive:
        for name, path in files:
            data = path.read_bytes()
            if edition in EDITION_TEXTURE_SIZE:
                if name == 'modinfo.json':
                    meta = dict(info)
                    meta['Name'], meta['Description'] = EDITION_META[edition]
                    data = (json.dumps(meta, ensure_ascii=False, indent=2) + '\n').encode('utf-8')
                elif name == 'Assets/ScCsgoKnivesEdition.xml':
                    data = f'<Edition Name="{edition}" />\n'.encode('utf-8')
                elif name.lower().endswith('.png'):
                    data, record = lite_texture(data, name, EDITION_TEXTURE_SIZE[edition])
                    if record:
                        transformed.append(record)
            entry = zipfile.ZipInfo.from_file(path, name)
            entry.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(entry, data)
    manifest = {'edition': edition, 'version': info['Version'], 'package': target.name,
        'bytes': target.stat().st_size, 'sha256': hashlib.sha256(target.read_bytes()).hexdigest(),
        'dllSha256': hashlib.sha256((BUILD / 'ScCsgoKnives.dll').read_bytes()).hexdigest(),
        'transformed': transformed}
    target.with_suffix('.resources.json').write_bytes(json.dumps(manifest, ensure_ascii=False, indent=2).encode('utf-8'))
    print(f'{target}: {len(files)} entries, {target.stat().st_size / 1e6:.1f} MB; {len(transformed)} textures resized', flush=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--edition', choices=('full', 'lite', 'mini', 'both', 'all'), default='full',
                        help='both = Full + Lite; all = Full + Lite + Mini')
    parser.add_argument('--split-resources', action='store_true', help='Core + required complete resource pack (Full only)')
    args = parser.parse_args()
    dll = BUILD / 'ScCsgoKnives.dll'
    if not dll.exists() or dll.stat().st_mtime < newest_source_mtime():
        raise SystemExit('ScCsgoKnives.dll is older than sources; build successfully first.')
    check_catalog()
    info = json.loads((BUILD / 'modinfo.json').read_text(encoding='utf-8'))
    files = [(name, BUILD / name) for name in ('THIRD_PARTY_NOTICES.md', 'LICENSE', 'ASSET_SOURCES.md', 'ScCsgoKnives.dll', 'modinfo.json')]
    for source in sorted((SOURCE / 'Assets').rglob('*')):
        if not source.is_file():
            continue
        relative = source.relative_to(SOURCE)
        built = BUILD / relative
        if not built.is_file() or source.read_bytes() != built.read_bytes():
            raise SystemExit(f'missing or stale build asset: {relative}; rebuild first')
        files.append((relative.as_posix(), built))
    for name, path in files:
        if not path.is_file():
            raise SystemExit(f'missing build file: {name}')
    OUT.mkdir(exist_ok=True)
    if args.split_resources:
        if args.edition != 'full':
            raise SystemExit('Split resource delivery currently supports full resolution only.')
        write_split(files, info)
        return
    if info.get('Dependencies', {}).get('zh667.ScCsgoResources'):
        raise SystemExit('This core requires --split-resources; do not emit an incomplete standalone package.')
    editions = {'full': ('Full',), 'lite': ('Lite',), 'mini': ('Mini',),
                'both': ('Full', 'Lite'), 'all': ('Full', 'Lite', 'Mini')}[args.edition]
    for edition in editions:
        pack(edition, files, info)


if __name__ == '__main__':
    main()
